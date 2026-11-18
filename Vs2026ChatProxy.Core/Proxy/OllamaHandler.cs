using System.Diagnostics;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vs2026ChatProxy.Config;
using Vs2026ChatProxy.Providers;

namespace Vs2026ChatProxy.Proxy
{
    /// <summary>
    /// POST /api/chat 处理器。
    /// 接收 Ollama 格式请求 → 调用 IChatClient → 返回 Ollama NDJSON 流。
    ///
    /// Ollama NDJSON 协议关键点：
    /// - Content-Type: application/x-ndjson
    /// - 每行一个 JSON + \n（不是 SSE 的 data: ... 格式）
    /// - 流式：done:false 中间块, done:true 结束块
    /// - 结束字段：done_reason（不是 finish_reason）
    /// - tool_calls.arguments 是 object（不是 JSON 字符串）
    /// </summary>
    public static class OllamaHandler
    {
        private const int MaxRequestBodySize = 10 * 1024 * 1024;

        public static async Task HandleAsync(Stream requestBody, Stream responseBody, ProxyConfig config,
            Action<string>? onLog, CancellationToken ct, int compactThreshold = 85, SessionManager? sessions = null)
        {
            var stopwatch = Stopwatch.StartNew();

            // ── 读取请求体 ──────────────────────────────────────
            string reqJson;
            {
                using var ms = new MemoryStream();
                await requestBody.CopyToAsync(ms, 81920, ct);
                if (ms.Length > MaxRequestBodySize)
                {
                    onLog?.Invoke($"   ❌ [Ollama] 请求体过大: {ms.Length} bytes");
                    await WriteOllamaErrorAsync(responseBody, "unknown", "Request body too large", ct);
                    return;
                }
                reqJson = Encoding.UTF8.GetString(ms.ToArray());
            }

            var req = JObject.Parse(reqJson);
            var reqModel = req["model"]?.ToString() ?? "";
            var useStream = req["stream"]?.Value<bool>() ?? true;
            var messages = req["messages"] as JArray;
            var msgCount = messages?.Count ?? 0;
            var toolsArray = req["tools"] as JArray;
            var toolCount = toolsArray?.Count ?? 0;

            // 提取工作区根路径（与 OpenAI 通道共用），用于工具调用参数的相对路径补全
            var workspaceRoot = ToolCallNormalizer.ExtractWorkspaceRoot(messages);
            if (!string.IsNullOrEmpty(workspaceRoot))
                onLog?.Invoke($"   📂 [Ollama] workspaceRoot: {workspaceRoot}");

            // Ollama 特有: options 字段（temperature 等参数）
            var options = req["options"] as JObject;

            var modelStr = !string.IsNullOrEmpty(config.DefaultModel) ? config.DefaultModel : reqModel;
            onLog?.Invoke($"   [Ollama] 请求模型: {reqModel} → 实际使用: {modelStr} | 消息数: {msgCount} | 流式: {useStream} | tools: {toolCount}");

            var reqJsonPreview = reqJson.Length > 1000
                ? reqJson.Substring(0, 1000) + $"...(共 {reqJson.Length} 字符)"
                : reqJson;
            onLog?.Invoke($"   🔎 [Ollama] 请求摘要: {reqJsonPreview}");

            // ── 解码模型 ────────────────────────────────────────
            string providerName, modelId;
            ThinkingEffort thinkingEffort = ThinkingEffort.None;
            try
            {
                var (decoded, effort) = ThinkingModeParser.Parse(modelStr);
                thinkingEffort = effort;
                (providerName, modelId) = ModelEncoding.Decode(effort != ThinkingEffort.None ? decoded : modelStr);
            }
            catch (ArgumentException ex)
            {
                onLog?.Invoke($"   ❌ [Ollama] 模型解码失败: {ex.Message}");
                await WriteOllamaErrorAsync(responseBody, reqModel, ex.Message, ct);
                return;
            }

            var provider = config.Providers.FirstOrDefault(p => p.Name == providerName);
            if (provider == null)
            {
                onLog?.Invoke($"   ❌ [Ollama] Provider \"{providerName}\" 未找到");
                await WriteOllamaErrorAsync(responseBody, reqModel, $"Provider \"{providerName}\" not found", ct);
                return;
            }
            var model = provider.FindModel(modelId);
            if (model == null)
            {
                onLog?.Invoke($"   ❌ [Ollama] 模型 \"{modelId}\" 在 Provider \"{providerName}\" 中未找到");
                await WriteOllamaErrorAsync(responseBody, reqModel,
                    $"Model \"{modelId}\" not found in provider \"{providerName}\"", ct);
                return;
            }

            onLog?.Invoke($"   ✅ [Ollama] {provider.Type} → {provider.BaseUrl} | 模型: {modelId}");

            // ── 消息转换（Ollama → OpenAI）──────────────────────
            var chatMessages = ConvertOllamaMessages(messages);

            var sessionId = SessionManager.DeriveSessionId(chatMessages);
            sessions?.TrackRequest(sessionId, providerName, modelId);

            // 上下文压缩
            try
            {
                var compactResult = await ContextCompressor.TrimIfNeededAsync(chatMessages, model, provider, compactThreshold, ct);
                if (compactResult.WasCompressed)
                    chatMessages = compactResult.Messages;
            }
            catch { /* 压缩失败不阻塞 */ }

            // ── 构建 ChatOptions ─────────────────────────────────
            var clientMaxTokens = options?.GetValue("num_predict")?.Value<int>()
                ?? req["max_tokens"]?.Value<int>();
            int effectiveMaxTokens;
            if (clientMaxTokens.HasValue && clientMaxTokens.Value > 0)
            {
                effectiveMaxTokens = clientMaxTokens.Value;
            }
            else
            {
                effectiveMaxTokens = 131072; // 默认 128K
            }

            var chatOptions = new ChatOptions
            {
                Model = model.Id,
                Temperature = options?.GetValue("temperature")?.Value<double>()
                    ?? req["temperature"]?.Value<double>()
                    ?? 0.7,
                MaxTokens = effectiveMaxTokens,
                Tools = (toolCount > 0) ? toolsArray : null,
                ToolChoice = req["tool_choice"],
            };

            if (thinkingEffort != ThinkingEffort.None)
            {
                chatOptions.ThinkingEffort = thinkingEffort;
                onLog?.Invoke($"   🧠 [Ollama] 推理模式: {thinkingEffort}");
            }

            var client = AiClientFactory.Create(provider);

            try
            {
                foreach (var msg in chatMessages)
                {
                    var preview = msg.Content?.Length > 200
                        ? msg.Content.Substring(0, 200) + $"...(共 {msg.Content.Length} 字符)"
                        : msg.Content;
                    onLog?.Invoke($"   📥 [{msg.Role}] {preview}");
                }

                if (useStream)
                    await HandleOllamaStreamAsync(responseBody, client, chatMessages, chatOptions, reqModel, stopwatch, onLog, ct, workspaceRoot);
                else
                    await HandleOllamaNonStreamAsync(responseBody, client, chatMessages, chatOptions, reqModel, stopwatch, onLog, ct, workspaceRoot);
            }
            catch (HttpListenerException ex)
            {
                onLog?.Invoke($"   ⚠️ [Ollama] 客户端已断开连接（{ex.ErrorCode}: {ex.Message}）");
            }
            catch (Exception ex)
            {
                onLog?.Invoke($"   ❌ [Ollama] 异常: {ex.GetType().Name}: {ex.Message}");
                try { await WriteOllamaErrorAsync(responseBody, reqModel, ex.Message, ct); }
                catch { /* 连接已断开 */ }
            }
        }

        // ── Ollama 流式响应（NDJSON）─────────────────────────────
        private static async Task HandleOllamaStreamAsync(Stream body, IChatClient client,
            List<ChatMessage> messages, ChatOptions options, string displayModel,
            Stopwatch stopwatch, Action<string>? onLog, CancellationToken ct, string? workspaceRoot = null)
        {
            var totalChars = 0;
            var allToolCalls = new List<ToolCall>();
            var createdAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

            // 逐块累积 tool calls（上游返回的可能是分段）
            var activeToolCalls = new Dictionary<int, (string id, string name, StringBuilder args)>();
            string? finishReason = null;
            var firstChunkReceived = false;

            // 保活心跳：等待上游第一个 chunk 期间，每 15 秒发一个空行（NDJSON 保活）
            var keepaliveBytes = System.Text.Encoding.UTF8.GetBytes("\n");
            using var keepaliveCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var keepaliveTask = Task.Run(async () =>
            {
                try
                {
                    while (!keepaliveCts.Token.IsCancellationRequested)
                    {
                        await Task.Delay(15000, keepaliveCts.Token);
                        if (firstChunkReceived) break;
                        await body.WriteAsync(keepaliveBytes, keepaliveCts.Token);
                        await body.FlushAsync(keepaliveCts.Token);
                    }
                }
                catch (OperationCanceledException) { }
                catch { }
            }, keepaliveCts.Token);

            try
            {
                await foreach (var chunk in client.ChatStreamAsync(messages, options, ct))
                {
                    firstChunkReceived = true;
                // 普通文本内容 → NDJSON 中间块（done:false）
                if (!string.IsNullOrEmpty(chunk.Content))
                {
                    totalChars += chunk.Content.Length;

                    // 模拟 CopilotProxy：将文本按约 20 字符切分后逐块发送
                    // 但如果内容本身就短，直接一发
                    if (chunk.Content.Length <= 20)
                    {
                        await WriteNdjsonAsync(body, new
                        {
                            model = displayModel,
                            created_at = createdAt,
                            message = new { role = "assistant", content = chunk.Content },
                            done = false
                        }, ct);
                    }
                    else
                    {
                        // 按固定宽度切分，模拟流式输出效果
                        for (int i = 0; i < chunk.Content.Length; i += 20)
                        {
                            var len = Math.Min(20, chunk.Content.Length - i);
                            await WriteNdjsonAsync(body, new
                            {
                                model = displayModel,
                                created_at = createdAt,
                                message = new { role = "assistant", content = chunk.Content.Substring(i, len) },
                                done = false
                            }, ct);
                        }
                    }
                }

                // 累积 tool call 片段
                if (chunk.ToolCalls?.Count > 0)
                {
                    foreach (var tc in chunk.ToolCalls)
                    {
                        var idx = 0;
                        var id = tc.Id ?? "";
                        var name = tc.Name ?? "";

                        if (activeToolCalls.TryGetValue(idx, out var existing))
                        {
                            if (!string.IsNullOrEmpty(id)) existing.id = id;
                            if (!string.IsNullOrEmpty(name)) existing.name = name;
                            existing.args.Append(tc.Arguments ?? "");
                            activeToolCalls[idx] = existing;
                        }
                        else
                        {
                            activeToolCalls[idx] = (id, name, new StringBuilder(tc.Arguments ?? ""));
                        }
                    }
                }

                if (!string.IsNullOrEmpty(chunk.FinishReason))
                    finishReason = chunk.FinishReason;
            }

            // 合并累积的 tool calls，并走与 OpenAI 通道相同的规范化管道
            // （路径清理/别名映射/相对路径补全/默认值填充）
            foreach (var kvp in activeToolCalls.OrderBy(k => k.Key))
            {
                allToolCalls.Add(new ToolCall
                {
                    Id = kvp.Value.id,
                    Name = kvp.Value.name,
                    Arguments = kvp.Value.args.ToString()
                });
            }
            if (allToolCalls.Count > 0)
            {
                allToolCalls = ToolCallNormalizer.NormalizeAll(allToolCalls, workspaceRoot);
                onLog?.Invoke($"   🔧 [Ollama] 工具调用规范化: {activeToolCalls.Count} → {allToolCalls.Count}");
            }

            var hasToolCalls = allToolCalls.Count > 0;
            var doneReason = hasToolCalls ? "tool_calls" : (finishReason ?? "stop");

            // 空响应保护：无文本也无工具调用时补一个空格块，
            // 避免 VS 客户端把空 assistant 响应当异常而无限重试（与 ChatHandler 一致）
            if (totalChars == 0 && !hasToolCalls)
            {
                await WriteNdjsonAsync(body, new
                {
                    model = displayModel,
                    created_at = createdAt,
                    message = new { role = "assistant", content = " " },
                    done = false
                }, ct);
            }

            onLog?.Invoke($"   🏁 [Ollama] done_reason: {doneReason}");

            // 如果有 tool_calls，先发一个带 tool_calls 的块（done:false）
            // CopilotProxy 的做法：所有 tool_calls 在一个块中发出
            if (hasToolCalls)
            {
                var tcArray = allToolCalls.Select(tc =>
                {
                    object argsObj;
                    try { argsObj = JsonConvert.DeserializeObject(tc.Arguments) ?? new { }; }
                    catch { argsObj = new { }; }

                    return new
                    {
                        id = string.IsNullOrEmpty(tc.Id) ? GenerateToolCallId() : tc.Id,
                        type = "function",
                        function = new { name = tc.Name, arguments = argsObj }
                    };
                }).ToArray();

                await WriteNdjsonAsync(body, new
                {
                    model = displayModel,
                    created_at = createdAt,
                    message = new { role = "assistant", content = "", tool_calls = tcArray },
                    done = false
                }, ct);
            }

            // 最终 done:true 块
            var duration = stopwatch.ElapsedMilliseconds;
            await WriteNdjsonAsync(body, new
            {
                model = displayModel,
                created_at = createdAt,
                message = new { role = "assistant", content = "" },
                done = true,
                done_reason = doneReason,
                total_duration = duration * 1_000_000L, // 微秒
                load_duration = 0L,
                prompt_eval_count = 0,
                prompt_eval_duration = 0L,
                eval_count = 0,
                eval_duration = 0L
            }, ct);
            }
            finally
            {
                keepaliveCts.Cancel();
                try { await keepaliveTask; } catch { }
            }

            onLog?.Invoke($"   ✅ [Ollama] 流式结束，共 {totalChars} 字符, {stopwatch.ElapsedMilliseconds}ms");
        }

        // ── Ollama 非流式响应（单行 NDJSON）──────────────────────
        private static async Task HandleOllamaNonStreamAsync(Stream body, IChatClient client,
            List<ChatMessage> messages, ChatOptions options, string displayModel,
            Stopwatch stopwatch, Action<string>? onLog, CancellationToken ct, string? workspaceRoot = null)
        {
            var result = await client.ChatAsync(messages, options, ct);
            var duration = stopwatch.ElapsedMilliseconds;
            var createdAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

            // 工具调用规范化（与流式/OpenAI 通道一致）
            var normalizedToolCalls = result.ToolCalls;
            if (normalizedToolCalls.Count > 0)
            {
                normalizedToolCalls = ToolCallNormalizer.NormalizeAll(normalizedToolCalls, workspaceRoot);
                onLog?.Invoke($"   🔧 [Ollama] 工具调用规范化: {result.ToolCalls.Count} → {normalizedToolCalls.Count}");
            }

            var hasToolCalls = normalizedToolCalls.Count > 0;
            var doneReason = hasToolCalls ? "tool_calls" : (result.FinishReason ?? "stop");

            // 构建 Ollama 响应的 message 对象
            var msgObj = new Dictionary<string, object>
            {
                ["role"] = "assistant",
                ["content"] = result.Content ?? ""
            };

            if (hasToolCalls)
            {
                msgObj["tool_calls"] = normalizedToolCalls.Select(tc =>
                {
                    object argsObj;
                    try { argsObj = JsonConvert.DeserializeObject(tc.Arguments) ?? new { }; }
                    catch { argsObj = new { }; }

                    return new
                    {
                        id = string.IsNullOrEmpty(tc.Id) ? GenerateToolCallId() : tc.Id,
                        type = "function",
                        function = new { name = tc.Name, arguments = argsObj }
                    };
                }).ToArray();
            }

            var response = new Dictionary<string, object>
            {
                ["model"] = displayModel,
                ["created_at"] = createdAt,
                ["message"] = msgObj,
                ["done"] = true,
                ["done_reason"] = doneReason,
                ["total_duration"] = duration * 1_000_000L,
                ["load_duration"] = 0L,
                ["prompt_eval_count"] = result.PromptTokens,
                ["prompt_eval_duration"] = 0L,
                ["eval_count"] = result.CompletionTokens,
                ["eval_duration"] = 0L
            };

            await WriteNdjsonAsync(body, response, ct);

            var contentPreview = result.Content?.Length > 500
                ? result.Content.Substring(0, 500) + $"...(共 {result.Content.Length} 字符)"
                : result.Content;
            onLog?.Invoke($"   📤 [Ollama] [assistant] {contentPreview}");
            if (result.PromptTokens > 0 || result.CompletionTokens > 0)
                onLog?.Invoke($"   📊 [Ollama] tokens: prompt={result.PromptTokens} completion={result.CompletionTokens}");
        }

        // ── Ollama 消息格式转换 ──────────────────────────────────
        //
        // Ollama /api/chat 的消息格式与 OpenAI 基本相同，但 tool_calls 有差异：
        // - Ollama: tool_calls[].function.arguments 是 **object**（已解析的 JSON 对象）
        // - OpenAI: tool_calls[].function.arguments 是 **string**（JSON 字符串）
        //
        // 我们内部统一用 string，所以需要把 Ollama 的 object 序列化为 string。

        internal static List<ChatMessage> ConvertOllamaMessages(JArray? msgs)
        {
            var result = new List<ChatMessage>();
            if (msgs == null) return result;

            foreach (var msg in msgs)
            {
                var role = msg["role"]?.ToString() ?? "user";

                // Ollama 的 content 可能是 string 或 null
                var content = msg["content"]?.ToString() ?? "";

                var cm = new ChatMessage { Role = role, Content = content };

                // tool_calls (assistant role) — Ollama 格式中 arguments 是 object
                var toolCalls = msg["tool_calls"] as JArray;
                if (toolCalls != null)
                {
                    foreach (var tc in toolCalls)
                    {
                        var argsToken = tc["function"]?["arguments"];
                        // 关键：Ollama 的 arguments 是 object，需要序列化为 string
                        var argsStr = argsToken?.Type == JTokenType.Object
                            ? argsToken.ToString(Formatting.None)
                            : argsToken?.ToString() ?? "";

                        cm.ToolCalls.Add(new ToolCall
                        {
                            Id = tc["id"]?.ToString() ?? "",
                            Name = tc["function"]?["name"]?.ToString() ?? "",
                            Arguments = argsStr
                        });
                    }
                }

                // tool role
                cm.ToolCallId = msg["tool_call_id"]?.ToString() ?? "";
                cm.Name = msg["name"]?.ToString() ?? "";

                // Ollama 特有: images 字段（暂不支持，忽略）

                result.Add(cm);
            }
            return result;
        }

        // ── NDJSON 写入工具 ─────────────────────────────────────

        private static readonly JsonSerializerSettings _jsonSettings = new()
        {
            StringEscapeHandling = StringEscapeHandling.Default,
            NullValueHandling = NullValueHandling.Ignore
        };

        private static async Task WriteNdjsonAsync(Stream body, object payload, CancellationToken ct)
        {
            var json = JsonConvert.SerializeObject(payload, _jsonSettings);
            var bytes = Encoding.UTF8.GetBytes(json + "\n");
            await body.WriteAsync(bytes, ct);
            await body.FlushAsync(ct);
        }

        private static async Task WriteOllamaErrorAsync(Stream body, string model, string errorMsg, CancellationToken ct)
        {
            await WriteNdjsonAsync(body, new
            {
                model,
                created_at = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                message = new { role = "assistant", content = $"Error: {errorMsg}" },
                done = true,
                done_reason = "error"
            }, ct);
        }

        private static int _tcCounter;
        private static string GenerateToolCallId() =>
            $"call_{Interlocked.Increment(ref _tcCounter):x8}";
    }
}
