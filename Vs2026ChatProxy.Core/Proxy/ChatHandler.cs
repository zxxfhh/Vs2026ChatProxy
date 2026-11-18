using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vs2026ChatProxy.Config;
using Vs2026ChatProxy.Providers;

namespace Vs2026ChatProxy.Proxy
{
    /// <summary>
    /// POST /v1/chat/completions 处理器。
    /// 接收 OpenAI 格式请求 → 调用 IChatClient → 返回 OpenAI SSE 流。
    /// </summary>
    public static class ChatHandler
    {
        /// <summary>请求体最大大小限制：10 MB</summary>
        private const int MaxRequestBodySize = 10 * 1024 * 1024;

        public static async Task HandleAsync(Stream requestBody, Stream responseBody, ProxyConfig config, Action<string>? onLog, CancellationToken ct, int compactThreshold = 85, SessionManager? sessions = null)
        {
            // 读取请求体（读完立即关闭，避免占用连接）
            string reqJson;
            {
                using var ms = new MemoryStream();
                await requestBody.CopyToAsync(ms, 81920, ct);
                if (ms.Length > MaxRequestBodySize)
                {
                    onLog?.Invoke($"   ❌ 请求体过大: {ms.Length} bytes");
                    await WriteErrorAsync(responseBody, 413, "Request body too large", "request_too_large");
                    return;
                }
                reqJson = Encoding.UTF8.GetString(ms.ToArray());
            }

            var req = JObject.Parse(reqJson);
            var reqModel = req["model"]?.ToString() ?? "";
            var useStream = req["stream"]?.Value<bool>() ?? true;
            var msgCount = req["messages"] is JArray arr ? arr.Count : 0;
            var toolsArray = req["tools"] as JArray;
            var toolCount = toolsArray?.Count ?? 0;

            // 强制使用默认模型（忽略客户端传的模型名）
            var modelStr = !string.IsNullOrEmpty(config.DefaultModel) ? config.DefaultModel : reqModel;
            onLog?.Invoke($"   请求模型: {reqModel} → 实际使用: {modelStr} | 消息数: {msgCount} | 流式: {useStream} | tools: {toolCount}");

            // 请求 JSON 摘要日志（超长截断避免 LOH 压力）
            var reqJsonPreview = reqJson.Length > 1000
                ? reqJson.Substring(0, 1000) + $"...(共 {reqJson.Length} 字符)"
                : reqJson;
            onLog?.Invoke($"   🔎 请求摘要: {reqJsonPreview}");

            // 解码 model → provider + modelId
            // 同时解析推理模式（如 "provider:model-id/5_(maximum)"）
            string providerName, modelId;
            ThinkingEffort thinkingEffort = ThinkingEffort.None;
            try
            {
                // 先尝试解析推理模式后缀
                var (decoded, effort) = ThinkingModeParser.Parse(modelStr);
                thinkingEffort = effort;

                // 如果解析出了推理级别，用清理后的模型名继续解码
                (providerName, modelId) = ModelEncoding.Decode(effort != ThinkingEffort.None ? decoded : modelStr);
            }
            catch (ArgumentException ex)
            {
                onLog?.Invoke($"   ❌ 模型解码失败: {ex.Message}");
                await WriteErrorAsync(responseBody, 400, ex.Message, "invalid_request_error");
                return;
            }

            // 查找配置
            var provider = config.Providers.FirstOrDefault(p => p.Name == providerName);
            if (provider == null)
            {
                onLog?.Invoke($"   ❌ Provider \"{providerName}\" 未找到");
                await WriteErrorAsync(responseBody, 400, $"Provider \"{providerName}\" not found", "provider_not_found");
                return;
            }
            var model = provider.FindModel(modelId);
            if (model == null)
            {
                onLog?.Invoke($"   ❌ 模型 \"{modelId}\" 在 Provider \"{providerName}\" 中未找到");
                await WriteErrorAsync(responseBody, 400,
                    $"Model \"{modelId}\" not found in provider \"{providerName}\"", "model_not_found");
                return;
            }

            onLog?.Invoke($"   ✅ {provider.Type} → {provider.BaseUrl} | 模型: {modelId}");

            // 会话跟踪（用于 KV Cache 保持和并发管理）
            var sessionId = SessionManager.DeriveSessionId(ConvertMessages(req["messages"] as JArray));
            sessions?.TrackRequest(sessionId, providerName, modelId);

            // 转换 OpenAI messages → ChatMessage 列表
            var messages = ConvertMessages(req["messages"] as JArray);

            // 提取工作区根路径（用于将相对路径补全为绝对路径）
            // 直接从请求消息文本中提取，兼容多种格式
            string? workspaceRoot = null;
            if (req["messages"] is JArray msgArr)
            {
                foreach (var m in msgArr)
                {
                    var c = m["content"]?.ToString() ?? "";
                    if (string.IsNullOrEmpty(c)) continue;
                    // 格式1: "workspace root path is:\nI:\xxx\"
                    var m1 = Regex.Match(c, @"workspace\s+root\s+path\s+is:\s*([\r\n]+)?([A-Za-z]:[\\/][^\s\r\n""']+)", RegexOptions.IgnoreCase);
                    if (m1.Success) { workspaceRoot = m1.Groups[2].Value.TrimEnd('\\', '/'); break; }
                    // 格式2: "workspaceRoot:I:\xxx" 或 "workspace_root: I:\xxx"
                    var m2 = Regex.Match(c, @"(?:workspace[_-]?root|workspaceRoot|project[_-]?path)[:\s]+([A-Za-z]:[\\/][^\s\r\n""']+)", RegexOptions.IgnoreCase);
                    if (m2.Success) { workspaceRoot = m2.Groups[1].Value.TrimEnd('\\', '/'); break; }
                }
            }
            if (!string.IsNullOrEmpty(workspaceRoot))
                onLog?.Invoke($"   📂 workspaceRoot: {workspaceRoot}");

            // 上下文压缩（超限时自动触发）
            try
            {
                var compactResult = await ContextCompressor.TrimIfNeededAsync(messages, model, provider, compactThreshold, ct);
                if (compactResult.WasCompressed)
                {
                    messages = compactResult.Messages;
                }
            }
            catch
            {
                // 压缩失败不阻塞主流程，用原始消息继续
            }

            // MaxTokens 策略：
            // 1. 客户端明确传了 max_tokens → 使用客户端值
            // 2. 未传 → 默认 128K
            var clientMaxTokens = req["max_tokens"]?.Value<int>();
            int effectiveMaxTokens;
            if (clientMaxTokens.HasValue && clientMaxTokens.Value > 0)
            {
                effectiveMaxTokens = clientMaxTokens.Value;
            }
            else
            {
                effectiveMaxTokens = 131072; // 默认 128K
            }

            var options = new ChatOptions
            {
                Model = model.Id,
                Temperature = req["temperature"]?.Value<double>() ?? 0.7,
                MaxTokens = effectiveMaxTokens,
                Tools = (toolCount > 0) ? toolsArray : null,
                ToolChoice = req["tool_choice"],
            };

            onLog?.Invoke($"   📏 MaxTokens={effectiveMaxTokens} (模型上下文={model.MaxContextTokens}, 客户端传值={clientMaxTokens?.ToString() ?? "null"})");

            // 应用推理模式（参考 CopilotProxy 的 applyThinkingMode）
            if (thinkingEffort != ThinkingEffort.None)
            {
                options.ThinkingEffort = thinkingEffort;
                onLog?.Invoke($"   🧠 推理模式: {thinkingEffort} (budget={ThinkingModeParser.ToBudgetTokens(thinkingEffort)?.ToString() ?? "无限"})");
            }

            var client = AiClientFactory.Create(provider);

            try
            {
                // 日志：请求消息摘要（避免长内容导致 LOH 压力）
                foreach (var msg in messages)
                {
                    var preview = msg.Content?.Length > 200
                        ? msg.Content.Substring(0, 200) + $"...(共 {msg.Content.Length} 字符)"
                        : msg.Content;
                    onLog?.Invoke($"   📥 [{msg.Role}] {preview}");
                }

                if (useStream)
                    await HandleStreamAsync(responseBody, client, messages, options, modelStr, onLog, ct, workspaceRoot);
                else
                    await HandleNonStreamAsync(responseBody, client, messages, options, modelStr, onLog, ct, workspaceRoot);
            }
            catch (HttpListenerException ex)
            {
                // 客户端主动断开连接（超时/用户取消/连接丢失），不是服务端错误
                onLog?.Invoke($"   ⚠️ 客户端已断开连接（{ex.ErrorCode}: {ex.Message}）");
            }
            catch (Exception ex)
            {
                onLog?.Invoke($"   ❌ 流式异常: {ex.GetType().Name}: {ex.Message}");
                if (ex.InnerException != null)
                    onLog?.Invoke($"      内部异常: {ex.InnerException.Message}");
                try { await SseWriter.WriteEventAsync(responseBody, new { error = new { message = ex.Message, type = "api_error" } }); }
                catch { /* 连接已断开 */ }
            }
        }

        private static async Task HandleStreamAsync(Stream body, IChatClient client,
            List<ChatMessage> messages, ChatOptions options, string displayModel, Action<string>? onLog, CancellationToken ct, string? workspaceRoot = null)
        {
            // SSE headers
            // 注意: headers 由 ProxyServer 在 HttpContext 层设置，这里直接写 body

            var chunkId = SseWriter.NewChunkId();
            var totalChars = 0; // 仅统计字符数用于日志，不累积完整内容避免 LOH 压力
            var hasToolCalls = false; // 跟踪是否有 tool_calls

            // 发送 role chunk
            await SseWriter.WriteEventAsync(body, SseWriter.CreateRoleChunk(chunkId, displayModel), ct);
            await SseWriter.FlushAsync(body, ct);

            // 保活心跳：等待上游第一个 chunk 期间，每 15 秒发 SSE 注释防止客户端超时断开
            var keepaliveBytes = System.Text.Encoding.UTF8.GetBytes(": keep-alive\n\n");
            var firstChunkReceived = false;

            // ── Tool Call 累积器 ──
            // 参考 CopilotProxy stream-handler.js 的 reconstructToolCalls：
            // 先累积所有 delta 片段，流结束后一次性 normalize 再输出。
            // VS 2026 对不完整/参数错误的 tool call 极度敏感，必须输出规范化后的完整 tool call。
            var tcBuilders = new Dictionary<int, (string id, string type, string name, string arguments)>();

            // 用定时器在等待上游响应时发保活心跳
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
                catch { /* 连接已断开 */ }
            }, keepaliveCts.Token);

            // 累积纯文本内容（用于文本中的工具调用提取）
            var fullContent = new StringBuilder();

            try
            {
                await foreach (var chunk in client.ChatStreamAsync(messages, options, ct))
                {
                    firstChunkReceived = true; // 收到第一个 chunk，停止心跳

                    if (!string.IsNullOrEmpty(chunk.Content))
                    {
                        totalChars += chunk.Content.Length;
                        fullContent.Append(chunk.Content);

                        // 如果还没检测到结构化 tool call，先暂存文本
                        // （稍后可能需要从文本中提取工具调用）
                        if (!hasToolCalls)
                        {
                            await SseWriter.WriteEventAsync(body, SseWriter.CreateContentChunk(chunkId, displayModel, chunk.Content), ct);
                            await SseWriter.FlushAsync(body, ct);
                        }
                    }

                    if (chunk.ToolCalls?.Count > 0)
                    {
                        hasToolCalls = true;

                        // 累积 tool call delta（参考 CopilotProxy 的 tcBuilders 机制）
                        foreach (var tc in chunk.ToolCalls)
                        {
                            var idx = 0; // 简化：单 tool call
                            if (!tcBuilders.TryGetValue(idx, out var existing))
                            {
                                existing = ("", "function", "", "");
                            }
                            // 增量拼接
                            var newId = !string.IsNullOrEmpty(tc.Id) ? tc.Id : existing.id;
                            var newName = !string.IsNullOrEmpty(tc.Name) ? tc.Name : existing.name;
                            var newArgs = existing.arguments + (tc.Arguments ?? "");
                            tcBuilders[idx] = (newId, "function", newName, newArgs);
                        }
                    }

                    // 上游 finish_reason 到达时，不立即输出给客户端
                    // 等后续 normalize 完成后再输出
                    if (!string.IsNullOrEmpty(chunk.FinishReason))
                    {
                        // 标记但不输出 —— 在 finally 块中统一输出
                    }
                }
            }
            finally
            {
                keepaliveCts.Cancel();
                try { await keepaliveTask; } catch { }
            }

            // ── Tool Call 后处理：规范化 + 输出 ──
            if (hasToolCalls && tcBuilders.Count > 0)
            {
                // 1. 拼装完整的 ToolCall 列表
                var rawToolCalls = tcBuilders.OrderBy(kv => kv.Key)
                    .Select(kv => new ToolCall
                    {
                        Id = !string.IsNullOrEmpty(kv.Value.id) ? kv.Value.id : $"call_{Guid.NewGuid():N}",
                        Name = kv.Value.name,
                        Arguments = kv.Value.arguments
                    }).ToList();

                // 2. 对每个 tool call 做规范化（修复 JSON、填充默认值、路径修正等）
                var normalizedCalls = ToolCallNormalizer.NormalizeAll(rawToolCalls, workspaceRoot);

                // 3. 如果 normalize 后全部被丢弃（返回空），尝试从累积文本中提取
                if (normalizedCalls.Count == 0 && fullContent.Length > 0)
                {
                    var (cleanText, extractedCalls) = ToolCallNormalizer.ExtractFromText(fullContent.ToString());
                    if (extractedCalls.Count > 0)
                    {
                        normalizedCalls = extractedCalls;
                    }
                }

                // 4. 关键修复：如果规范化后 tool calls 为空，不应发送 tool_calls finish_reason
                //    否则 VS 收到 finish_reason=tool_calls 但没有 tool_call 数据，会无限重试
                if (normalizedCalls.Count > 0)
                {
                    // 输出规范化后的完整 tool calls（一次性输出，VS 不会收到残缺数据）
                    for (int i = 0; i < normalizedCalls.Count; i++)
                    {
                        var tc = normalizedCalls[i];
                        onLog?.Invoke($"   🔧 tool_call[{i}]: {tc.Name}({(tc.Arguments?.Length > 200 ? tc.Arguments.Substring(0, 200) + "..." : tc.Arguments)})");
                        // 诊断：检查路径值是否包含反斜杠
                        var diagFilename = System.Text.RegularExpressions.Regex.Match(tc.Arguments ?? "", @"""filename""\s*:\s*""([^""]*)""");
                        if (diagFilename.Success) onLog?.Invoke($"   📋 diag filename raw: [{diagFilename.Groups[1].Value}]");
                        await SseWriter.WriteEventAsync(body,
                            SseWriter.CreateToolCallChunk(chunkId, displayModel, i,
                                tc.Id, tc.Name, tc.Arguments ?? "{}"), ct);
                        await SseWriter.FlushAsync(body, ct);
                    }

                    // finish_reason = "tool_calls"
                    onLog?.Invoke($"   🏁 finish_reason: stop → tool_calls ({normalizedCalls.Count} tool calls)");
                    await SseWriter.WriteEventAsync(body, SseWriter.CreateFinishChunk(chunkId, displayModel, "tool_calls"), ct);
                    await SseWriter.FlushAsync(body, ct);
                }
                else
                {
                    // 规范化后为空 → 当作普通 stop 处理
                    // hasToolCalls=true 时文本没输出过，补发
                    if (fullContent.Length > 0)
                    {
                        await SseWriter.WriteEventAsync(body, SseWriter.CreateContentChunk(chunkId, displayModel, fullContent.ToString()), ct);
                        await SseWriter.FlushAsync(body, ct);
                    }
                    onLog?.Invoke($"   ⚠️ tool_calls 规范化后为空，回退为 stop");
                    await SseWriter.WriteEventAsync(body, SseWriter.CreateFinishChunk(chunkId, displayModel, "stop"), ct);
                    await SseWriter.FlushAsync(body, ct);
                }
            }
            else
            {
                // 没有结构化 tool call → 先尝试从文本提取
                // 如果有累积文本但没检测到结构化 tool call，尝试从文本提取
                if (fullContent.Length > 0)
                {
                    var (cleanText, extractedCalls) = ToolCallNormalizer.ExtractFromText(fullContent.ToString());
                    if (extractedCalls.Count > 0)
                    {
                        // 从文本中提取到了工具调用！
                        for (int i = 0; i < extractedCalls.Count; i++)
                        {
                            var tc = extractedCalls[i];
                            onLog?.Invoke($"   🔧 extracted tool_call[{i}]: {tc.Name}");
                            await SseWriter.WriteEventAsync(body,
                                SseWriter.CreateToolCallChunk(chunkId, displayModel, i,
                                    tc.Id, tc.Name, tc.Arguments ?? "{}"), ct);
                            await SseWriter.FlushAsync(body, ct);
                        }
                        hasToolCalls = true;
                        onLog?.Invoke($"   🏁 finish_reason: stop → tool_calls ({extractedCalls.Count} extracted from text)");
                        await SseWriter.WriteEventAsync(body, SseWriter.CreateFinishChunk(chunkId, displayModel, "tool_calls"), ct);
                        await SseWriter.FlushAsync(body, ct);
                    }
                    else
                    {
                        await SseWriter.WriteEventAsync(body, SseWriter.CreateFinishChunk(chunkId, displayModel, "stop"), ct);
                        await SseWriter.FlushAsync(body, ct);
                    }
                }
                else
                {
                    // 完全没有内容也没有 tool calls → 模型返回空响应
                    // VS 对空 assistant 回应会重试，我们加个空白内容避免触发重试
                    onLog?.Invoke($"   ⚠️ 模型返回空响应（无内容无工具调用），输出空白占位");
                    await SseWriter.WriteEventAsync(body, SseWriter.CreateContentChunk(chunkId, displayModel, " "), ct);
                    await SseWriter.FlushAsync(body, ct);
                    await SseWriter.WriteEventAsync(body, SseWriter.CreateFinishChunk(chunkId, displayModel, "stop"), ct);
                    await SseWriter.FlushAsync(body, ct);
                }
            }

            onLog?.Invoke($"   ✅ 流式结束，共 {totalChars} 字符");

            try
            {
                await SseWriter.WriteDoneAsync(body, ct);
                await SseWriter.FlushAsync(body, ct);
            }
            catch
            {
                // 客户端收到 tool_calls 后可能已主动断开，[DONE] 发不出是正常的
                if (hasToolCalls)
                    onLog?.Invoke($"   ℹ️ tool_calls 已发送，客户端已关闭（正常行为）");
                else
                    onLog?.Invoke($"   ⚠️ 客户端已断开连接");
            }
        }

        private static async Task HandleNonStreamAsync(Stream body, IChatClient client,
            List<ChatMessage> messages, ChatOptions options, string displayModel, Action<string>? onLog, CancellationToken ct, string? workspaceRoot = null)
        {
            var result = await client.ChatAsync(messages, options, ct);

            // ── Tool Call 规范化 ──
            // 参考 CopilotProxy server.js 的 normalizeToolCall 逻辑：
            // 修复 JSON、填充默认值、路径修正、字段别名映射。
            var toolCalls = result.ToolCalls;
            bool hasExtractedFromText = false;

            if (toolCalls.Count > 0)
            {
                toolCalls = ToolCallNormalizer.NormalizeAll(toolCalls, workspaceRoot);
            }
            else if (!string.IsNullOrEmpty(result.Content))
            {
                // 没有结构化 tool call → 尝试从文本中提取（XML/Markdown/JSON 格式）
                var (cleanText, extractedCalls) = ToolCallNormalizer.ExtractFromText(result.Content ?? "");
                if (extractedCalls.Count > 0)
                {
                    toolCalls = extractedCalls;
                    hasExtractedFromText = true;
                    result.Content = cleanText; // 用清理后的文本替换
                }
            }

            // 关键修复：当有 tool_calls 时，finish_reason 必须是 "tool_calls"
            var effectiveFinishReason = toolCalls.Count > 0 ? "tool_calls" : (result.FinishReason ?? "stop");

            var completion = new
            {
                id = SseWriter.NewChunkId(),
                @object = "chat.completion",
                created = SseWriter.UnixNow(),
                model = displayModel,
                choices = new[]
                {
                    new
                    {
                        index = 0,
                        message = new
                        {
                            role = "assistant",
                            // 有工具调用时 content 设为 null（参考 CopilotProxy）
                            content = toolCalls.Count > 0 ? (string?)null : result.Content,
                            tool_calls = toolCalls.Count > 0
                                ? toolCalls.Select(tc => new
                                {
                                    id = tc.Id,
                                    type = "function",
                                    function = new { name = tc.Name, arguments = tc.Arguments }
                                }).ToArray()
                                : null
                        },
                        finish_reason = effectiveFinishReason
                    }
                },
                usage = new
                {
                    prompt_tokens = result.PromptTokens,
                    completion_tokens = result.CompletionTokens,
                    total_tokens = result.PromptTokens + result.CompletionTokens
                }
            };

            var json = JsonConvert.SerializeObject(completion);
            var bytes = Encoding.UTF8.GetBytes(json);
            await body.WriteAsync(bytes, ct);
            await body.FlushAsync(ct);

            // 日志：返回内容（超长截断避免 LOH 压力）
            if (toolCalls.Count > 0)
            {
                foreach (var tc in toolCalls)
                    onLog?.Invoke($"   🔧 tool_call: {tc.Name}({(tc.Arguments?.Length > 200 ? tc.Arguments.Substring(0, 200) + "..." : tc.Arguments)})");
                if (hasExtractedFromText)
                    onLog?.Invoke($"   📤 [assistant] (工具调用从文本中提取，共 {toolCalls.Count} 个)");
            }
            else
            {
                var contentPreview = result.Content?.Length > 500
                    ? result.Content.Substring(0, 500) + $"...(共 {result.Content.Length} 字符)"
                    : result.Content;
                onLog?.Invoke($"   📤 [assistant] {contentPreview}");
            }
            if (result.PromptTokens > 0 || result.CompletionTokens > 0)
                onLog?.Invoke($"   📊 tokens: prompt={result.PromptTokens} completion={result.CompletionTokens}");
        }

        // ── 消息转换 ─────────────────────────────────────────

        internal static List<ChatMessage> ConvertMessages(JArray? msgs)
        {
            var result = new List<ChatMessage>();
            if (msgs == null) return result;

            foreach (var msg in msgs)
            {
                var role = msg["role"]?.ToString() ?? "user";
                var content = msg["content"]?.ToString() ?? "";

                var cm = new ChatMessage { Role = role, Content = content };

                // tool_calls (assistant role)
                var toolCalls = msg["tool_calls"] as JArray;
                if (toolCalls != null)
                {
                    foreach (var tc in toolCalls)
                    {
                        cm.ToolCalls.Add(new ToolCall
                        {
                            Id = tc["id"]?.ToString() ?? "",
                            Name = tc["function"]?["name"]?.ToString() ?? "",
                            Arguments = tc["function"]?["arguments"]?.ToString() ?? ""
                        });
                    }
                }

                // tool role
                cm.ToolCallId = msg["tool_call_id"]?.ToString() ?? "";
                cm.Name = msg["name"]?.ToString() ?? "";

                result.Add(cm);
            }
            return result;
        }

        // ── 错误响应 ─────────────────────────────────────────

        private static async Task WriteErrorAsync(Stream body, int status, string message, string code)
        {
            var error = new { error = new { message, type = "invalid_request_error", code } };
            var json = JsonConvert.SerializeObject(error);
            var bytes = Encoding.UTF8.GetBytes(json);
            await body.WriteAsync(bytes);
            await body.FlushAsync();
        }
    }
}
