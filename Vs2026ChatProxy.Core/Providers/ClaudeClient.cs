using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vs2026ChatProxy.Config;

namespace Vs2026ChatProxy.Providers
{
    /// <summary>
    /// Anthropic Claude Messages API 客户端。
    /// 端点: POST /v1/messages
    /// 认证: x-api-key + anthropic-version headers
    /// SSE: event: content_block_delta / message_delta 等
    /// </summary>
    public class ClaudeClient : IChatClient
    {
        private readonly string _apiKey;
        private readonly string _baseUrl;
        private readonly ProviderConfig _provider;

        // 复用 HttpClient：按 Provider 身份缓存，避免 Socket 耗尽
        private static readonly ConcurrentDictionary<string, HttpClient> _httpCache = new();

        public ClaudeClient(ProviderConfig provider)
        {
            _provider = provider;
            _apiKey = provider.ApiKey ?? "";
            _baseUrl = (provider.BaseUrl ?? "https://api.anthropic.com").TrimEnd('/');
        }

        private HttpClient GetOrCreateHttpClient()
        {
            var key = $"{_apiKey}:{_baseUrl}";
            return _httpCache.GetOrAdd(key, _ =>
            {
                var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
                client.DefaultRequestHeaders.Add("x-api-key", _apiKey);
                client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
                client.DefaultRequestHeaders.Add("anthropic-dangerous-direct-browser-access", "true");
                client.DefaultRequestHeaders.Accept
                    .Add(new MediaTypeWithQualityHeaderValue("application/json"));
                return client;
            });
        }

        private object BuildBody(List<ChatMessage> messages, ChatOptions options, bool stream)
        {
            // Anthropic: system 消息单独放，其他角色直接放入 messages 数组
            var systemParts = new List<string>();
            var apiMessages = new List<object>();

            foreach (var m in messages)
            {
                if (m.Role == ChatRole.System)
                {
                    systemParts.Add(m.Content ?? "");
                }
                else if (m.Role == ChatRole.Tool)
                {
                    // tool result → user role with tool_result content block
                    apiMessages.Add(new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new { type = "tool_result", tool_use_id = m.ToolCallId ?? "", content = m.Content ?? "" }
                        }
                    });
                }
                else if (m.Role == ChatRole.Assistant && m.ToolCalls?.Count > 0)
                {
                    var contentParts = new List<object>();
                    if (!string.IsNullOrEmpty(m.Content))
                        contentParts.Add(new { type = "text", text = m.Content });
                    foreach (var tc in m.ToolCalls)
                    {
                        contentParts.Add(new
                        {
                            type = "tool_use",
                            id = tc.Id,
                            name = tc.Name,
                            input = SafeParseJson(tc.Arguments)
                        });
                    }
                    apiMessages.Add(new { role = "assistant", content = contentParts });
                }
                else
                {
                    apiMessages.Add(new { role = m.Role, content = m.Content ?? "" });
                }
            }

            var body = new Dictionary<string, object>
            {
                ["model"] = options.Model,
                // Claude API 的 max_tokens 是必填的，0 表示不限制需要给兜底值
                ["max_tokens"] = options.MaxTokens > 0 ? options.MaxTokens : 131072,
                ["messages"] = apiMessages,
                ["stream"] = stream,
            };

            if (systemParts.Count > 0)
                body["system"] = string.Join("\n\n", systemParts);

            if (options.Temperature != 0.7)
                body["temperature"] = options.Temperature;

            // 推理模式（extended thinking）：参考 CopilotProxy 的 applyThinkingMode
            if (options.ThinkingEffort != ThinkingEffort.None)
            {
                var budget = Vs2026ChatProxy.Proxy.ThinkingModeParser.ToBudgetTokens(options.ThinkingEffort);
                var thinkingObj = new Dictionary<string, object>
                {
                    ["type"] = "enabled",
                };
                if (budget.HasValue)
                    thinkingObj["budget_tokens"] = budget.Value;

                body["thinking"] = thinkingObj;

                // 推理模式下 Claude 不支持 temperature
                body.Remove("temperature");
            }

            // 工具定义转换（OpenAI → Anthropic 格式）
            var anthropicTools = ConvertToolsToAnthropicFormat(options.Tools);
            if (anthropicTools != null)
            {
                body["tools"] = anthropicTools;
                var anthropicChoice = ConvertToolChoiceToAnthropicFormat(options.ToolChoice);
                if (anthropicChoice != null)
                    body["tool_choice"] = anthropicChoice;
            }

            return body;
        }

        private static object SafeParseJson(string json)
        {
            try { return JObject.Parse(json); }
            catch { return json; }
        }

        // ── 非流式 ───────────────────────────────────────────

        public async Task<ChatResult> ChatAsync(
            List<ChatMessage> messages, ChatOptions options, CancellationToken ct = default)
        {
            var http = GetOrCreateHttpClient();
            var url = $"{_baseUrl}/v1/messages";
            var body = BuildBody(messages, options, false);
            var json = JsonConvert.SerializeObject(body);

            using var resp = await http.PostAsync(url,
                new StringContent(json, System.Text.Encoding.UTF8, "application/json"), ct);
            var respText = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
                return new ChatResult { Success = false, ErrorMessage = $"HTTP {(int)resp.StatusCode}: {respText}" };

            return ParseResponse(JObject.Parse(respText));
        }

        private static ChatResult ParseResponse(JObject jobj)
        {
            var contentBlocks = jobj["content"] as JArray;
            var textParts = new List<string>();
            var toolCalls = new List<ToolCall>();

            if (contentBlocks != null)
            {
                foreach (var block in contentBlocks)
                {
                    var type = block["type"]?.ToString();
                    if (type == "text")
                        textParts.Add(block["text"]?.ToString() ?? "");
                    else if (type == "tool_use")
                        toolCalls.Add(new ToolCall
                        {
                            Id = block["id"]?.ToString() ?? "",
                            Name = block["name"]?.ToString() ?? "",
                            Arguments = block["input"]?.ToString(Formatting.None) ?? "{}"
                        });
                }
            }

            var usage = jobj["usage"];
            return new ChatResult
            {
                Content = string.Join("", textParts),
                FinishReason = jobj["stop_reason"]?.ToString() ?? "",
                PromptTokens = usage?["input_tokens"]?.Value<int>() ?? 0,
                CompletionTokens = usage?["output_tokens"]?.Value<int>() ?? 0,
                ToolCalls = toolCalls,
            };
        }

        // ── 流式 ─────────────────────────────────────────────

        public async IAsyncEnumerable<ChatChunk> ChatStreamAsync(
            List<ChatMessage> messages, ChatOptions options,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            var http = GetOrCreateHttpClient();
            var url = $"{_baseUrl}/v1/messages";
            var body = BuildBody(messages, options, true);
            var json = JsonConvert.SerializeObject(body);

            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };

            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync(ct);
                yield return new ChatChunk { FinishReason = "error", Content = $"HTTP {(int)resp.StatusCode}: {err}" };
                yield break;
            }

            using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);

            // Anthropic SSE: 事件行 "event: xxx" + 数据行 "data: {json}"
            var currentEvent = "";

            while (!reader.EndOfStream && !ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (string.IsNullOrEmpty(line)) continue;

                if (line.StartsWith("event: "))
                {
                    currentEvent = line.Substring(7).Trim();
                    continue;
                }

                if (!line.StartsWith("data: ")) continue;
                var data = line.Substring(6);

                JObject jobj;
                try { jobj = JObject.Parse(data); }
                catch { continue; }

                switch (currentEvent)
                {
                    case "content_block_delta":
                        var deltaType = jobj["delta"]?["type"]?.ToString();
                        if (deltaType == "text_delta")
                        {
                            var text = jobj["delta"]?["text"]?.ToString() ?? "";
                            if (!string.IsNullOrEmpty(text))
                                yield return new ChatChunk { Content = text };
                        }
                        else if (deltaType == "input_json_delta")
                        {
                            var partialJson = jobj["delta"]?["partial_json"]?.ToString() ?? "";
                            yield return new ChatChunk
                            {
                                ToolCalls = [new() { Arguments = partialJson }]
                            };
                        }
                        break;

                    case "content_block_start":
                        var blockType = jobj["content_block"]?["type"]?.ToString();
                        if (blockType == "tool_use")
                        {
                            yield return new ChatChunk
                            {
                                ToolCalls = [new()
                                {
                                    Id = jobj["content_block"]?["id"]?.ToString() ?? "",
                                    Name = jobj["content_block"]?["name"]?.ToString() ?? ""
                                }]
                            };
                        }
                        break;

                    case "message_delta":
                        var stopReason = jobj["delta"]?["stop_reason"]?.ToString();
                        if (!string.IsNullOrEmpty(stopReason))
                        {
                            yield return new ChatChunk { FinishReason = stopReason };
                        }
                        break;
                }

                currentEvent = "";
            }
        }

        // ── 工具格式转换（OpenAI → Anthropic）───────────────

        /// <summary>
        /// 将 OpenAI 格式的 tools 转换为 Anthropic 格式。
        /// OpenAI: { type:"function", function: { name, description, parameters } }
        /// Anthropic: { name, description, input_schema }
        /// </summary>
        private static JArray? ConvertToolsToAnthropicFormat(JArray? openaiTools)
        {
            if (openaiTools == null || openaiTools.Count == 0) return null;

            var anthropicTools = new JArray();
            foreach (var tool in openaiTools)
            {
                var func = tool["function"];
                if (func == null) continue;

                var anthropicTool = new JObject
                {
                    ["name"] = func["name"]?.ToString() ?? "",
                    ["description"] = func["description"]?.ToString() ?? "",
                };
                // parameters → input_schema
                if (func["parameters"] != null)
                    anthropicTool["input_schema"] = func["parameters"];

                anthropicTools.Add(anthropicTool);
            }
            return anthropicTools.Count > 0 ? anthropicTools : null;
        }

        /// <summary>
        /// 将 OpenAI 格式的 tool_choice 转换为 Anthropic 格式。
        /// OpenAI: "auto" | "none" | { type:"function", function:{name:"..."} }
        /// Anthropic: { type:"auto" } | { type:"any" } | { type:"tool", name:"..." }
        /// </summary>
        private static JObject? ConvertToolChoiceToAnthropicFormat(JToken? openaiChoice)
        {
            if (openaiChoice == null) return null;

            if (openaiChoice.Type == JTokenType.String)
            {
                var val = openaiChoice.ToString();
                return val switch
                {
                    "auto" => new JObject { ["type"] = "auto" },
                    "none" => null, // Anthropic 不支持 "none"，不传即可
                    "required" => new JObject { ["type"] = "any" },
                    _ => new JObject { ["type"] = "auto" }
                };
            }

            if (openaiChoice.Type == JTokenType.Object)
            {
                var obj = (JObject)openaiChoice;
                var funcName = obj["function"]?["name"]?.ToString();
                if (!string.IsNullOrEmpty(funcName))
                    return new JObject { ["type"] = "tool", ["name"] = funcName };
            }

            return null;
        }
    }
}
