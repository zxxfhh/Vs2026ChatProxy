using System.Text;
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
        public static async Task HandleAsync(Stream requestBody, Stream responseBody, ProxyConfig config, Action<string>? onLog, CancellationToken ct, int compactThreshold = 85)
        {
            // 读取请求体（读完立即关闭，避免占用连接）
            string reqJson;
            {
                using var ms = new MemoryStream();
                await requestBody.CopyToAsync(ms, 81920);
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

            // 完整请求 JSON 写日志（不截断）
            onLog?.Invoke($"   🔎 完整请求: {reqJson}");

            // 解码 model → provider + modelId
            string providerName, modelId;
            try
            {
                (providerName, modelId) = ModelEncoding.Decode(modelStr);
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

            // 转换 OpenAI messages → ChatMessage 列表
            var messages = ConvertMessages(req["messages"] as JArray);

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

            var options = new ChatOptions
            {
                Model = model.Id,
                Temperature = req["temperature"]?.Value<double>() ?? 0.7,
                MaxTokens = req["max_tokens"]?.Value<int>() ?? 4096,
                Tools = (toolCount > 0) ? toolsArray : null,
                ToolChoice = req["tool_choice"],
            };

            var client = AiClientFactory.Create(provider);

            try
            {
                // 日志：请求消息完整内容
                foreach (var msg in messages)
                {
                    onLog?.Invoke($"   📥 [{msg.Role}] {msg.Content}");
                }

                if (useStream)
                    await HandleStreamAsync(responseBody, client, messages, options, modelStr, onLog, ct);
                else
                    await HandleNonStreamAsync(responseBody, client, messages, options, modelStr, onLog, ct);
            }
            catch (Exception ex)
            {
                // 流式中途出错
                onLog?.Invoke($"   ❌ 流式异常: {ex.GetType().Name}: {ex.Message}");
                if (ex.InnerException != null)
                    onLog?.Invoke($"      内部异常: {ex.InnerException.Message}");
                try { await SseWriter.WriteEventAsync(responseBody, new { error = new { message = ex.Message, type = "api_error" } }); }
                catch { /* 连接已断开 */ }
            }
        }

        private static async Task HandleStreamAsync(Stream body, IChatClient client,
            List<ChatMessage> messages, ChatOptions options, string displayModel, Action<string>? onLog, CancellationToken ct)
        {
            // SSE headers
            // 注意: headers 由 ProxyServer 在 HttpContext 层设置，这里直接写 body

            var chunkId = SseWriter.NewChunkId();
            var sb = new StringBuilder();

            // 发送 role chunk
            await SseWriter.WriteEventAsync(body, SseWriter.CreateRoleChunk(chunkId, displayModel), ct);
            await SseWriter.FlushAsync(body, ct);

            // 活跃 tool call 追踪
            var activeToolCalls = new Dictionary<int, (string id, string name)>();

            await foreach (var chunk in client.ChatStreamAsync(messages, options, ct))
            {
                if (!string.IsNullOrEmpty(chunk.Content))
                {
                    sb.Append(chunk.Content);
                    await SseWriter.WriteEventAsync(body, SseWriter.CreateContentChunk(chunkId, displayModel, chunk.Content), ct);
                    await SseWriter.FlushAsync(body, ct);
                }

                if (chunk.ToolCalls?.Count > 0)
                {
                    foreach (var tc in chunk.ToolCalls)
                    {
                        var idx = 0; // 简化：单 tool call
                        var id = tc.Id;
                        var name = tc.Name;

                        if (!string.IsNullOrEmpty(id))
                            activeToolCalls[idx] = (id, name);
                        else if (activeToolCalls.TryGetValue(idx, out var existing))
                            name = string.IsNullOrEmpty(name) ? existing.name : name;

                        await SseWriter.WriteEventAsync(body,
                            SseWriter.CreateToolCallChunk(chunkId, displayModel, idx,
                                id ?? (activeToolCalls.TryGetValue(idx, out var tcEntry) ? tcEntry.id : null) ?? "",
                                name ?? "",
                                tc.Arguments), ct);
                        await SseWriter.FlushAsync(body, ct);
                    }
                }

                if (!string.IsNullOrEmpty(chunk.FinishReason))
                {
                    onLog?.Invoke($"   🏁 finish_reason: {chunk.FinishReason}");
                    await SseWriter.WriteEventAsync(body, SseWriter.CreateFinishChunk(chunkId, displayModel, chunk.FinishReason), ct);
                    await SseWriter.FlushAsync(body, ct);
                }
            }

            onLog?.Invoke($"   ✅ 流式结束，共 {sb.Length} 字符");

            await SseWriter.WriteDoneAsync(body, ct);
            await SseWriter.FlushAsync(body, ct);

            // 日志：返回完整内容
            onLog?.Invoke($"   📤 [assistant] {sb}");
        }

        private static async Task HandleNonStreamAsync(Stream body, IChatClient client,
            List<ChatMessage> messages, ChatOptions options, string displayModel, Action<string>? onLog, CancellationToken ct)
        {
            var result = await client.ChatAsync(messages, options, ct);

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
                            content = result.Content,
                            tool_calls = result.ToolCalls.Count > 0
                                ? result.ToolCalls.Select(tc => new
                                {
                                    id = tc.Id,
                                    type = "function",
                                    function = new { name = tc.Name, arguments = tc.Arguments }
                                }).ToArray()
                                : null
                        },
                        finish_reason = result.FinishReason
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

            // 日志：返回完整内容
            onLog?.Invoke($"   📤 [assistant] {result.Content}");
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
