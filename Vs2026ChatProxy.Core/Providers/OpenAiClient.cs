using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vs2026ChatProxy.Config;

namespace Vs2026ChatProxy.Providers
{
    /// <summary>
    /// OpenAI 协议客户端。
    /// 完全自研 HttpClient，零外部 AI SDK 依赖。
    /// 兼容：OpenAI / DeepSeek / Moonshot / Qwen / Ollama / 等所有 OpenAI 兼容厂商。
    /// 参考 AiNet 的 OpenAiClient 实现。
    /// </summary>
    public class OpenAiClient : IChatClient
    {
        protected readonly string ApiKey;
        protected readonly string BaseUrl;
        protected readonly int TimeoutSeconds;
        protected readonly ProviderConfig Provider;

        public OpenAiClient(ProviderConfig provider, string defaultBaseUrl = "https://api.openai.com")
        {
            Provider = provider;
            ApiKey = provider.ApiKey ?? "";
            var configuredBase = string.IsNullOrWhiteSpace(provider.BaseUrl) ? defaultBaseUrl : provider.BaseUrl;
            BaseUrl = configuredBase.TrimEnd('/');
            TimeoutSeconds = 120;
        }

        /// <summary>
        /// 构建完整 API 端点 URL。
        /// 自动适配 BaseUrl 是否已包含版本路径，避免双重 /v1 问题。
        /// </summary>
        protected string BuildUrl(string relativePath)
        {
            var b = BaseUrl;
            // relativePath 自身已带版本前缀 → 直接追加
            if (System.Text.RegularExpressions.Regex.IsMatch(relativePath, @"^/v\d+"))
                return b + relativePath;
            // 若 BaseUrl 末尾已是版本路径 → 直接追加
            if (System.Text.RegularExpressions.Regex.IsMatch(b, @"/v\d+(/.*)?$"))
                return b + relativePath;
            return b + "/v1" + relativePath;
        }

        protected virtual HttpClient CreateHttpClient()
        {
            var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            if (!string.IsNullOrEmpty(ApiKey))
            {
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", ApiKey);
            }
            client.DefaultRequestHeaders.Accept
                .Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return client;
        }

        protected virtual object BuildRequestBody(List<ChatMessage> messages, ChatOptions options, bool stream)
        {
            var msgs = messages.Select(m =>
            {
                if (m.Role == ChatRole.Tool)
                {
                    return (object)new
                    {
                        role = m.Role,
                        tool_call_id = m.ToolCallId ?? "",
                        name = m.Name ?? "",
                        content = m.Content ?? "",
                    };
                }

                if (m.Role == ChatRole.Assistant && m.ToolCalls?.Count > 0)
                {
                    return (object)new
                    {
                        role = m.Role,
                        content = (string?)m.Content,
                        tool_calls = m.ToolCalls.Select(tc => new
                        {
                            id = tc.Id,
                            type = "function",
                            function = new { name = tc.Name, arguments = tc.Arguments }
                        }).ToList()
                    };
                }

                return (object)new { role = m.Role, content = m.Content ?? "" };
            }).ToList();

            var body = new Dictionary<string, object>
            {
                ["model"] = options.Model,
                ["messages"] = msgs,
                ["temperature"] = options.Temperature,
                ["top_p"] = options.TopP,
                ["max_tokens"] = options.MaxTokens,
                ["stream"] = stream,
            };

            if (stream)
                body["stream_options"] = new { include_usage = true };

            // 透传工具定义（OpenAI 兼容格式直接转发）
            if (options.Tools != null)
                body["tools"] = options.Tools;
            if (options.ToolChoice != null)
                body["tool_choice"] = options.ToolChoice;

            return body;
        }

        // ── 非流式 ───────────────────────────────────────────

        public virtual async Task<ChatResult> ChatAsync(
            List<ChatMessage> messages, ChatOptions options, CancellationToken ct = default)
        {
            using var http = CreateHttpClient();
            var url = BuildUrl("/chat/completions");
            var body = BuildRequestBody(messages, options, false);
            var json = JsonConvert.SerializeObject(body);

            using var resp = await http.PostAsync(url,
                new StringContent(json, System.Text.Encoding.UTF8, "application/json"), ct);
            var respText = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
                return Fail($"HTTP {(int)resp.StatusCode}: {respText}");

            try
            {
                return ParseNonStreamResponse(JObject.Parse(respText));
            }
            catch (Exception ex)
            {
                return Fail($"解析响应失败: {ex.Message}");
            }
        }

        protected virtual ChatResult ParseNonStreamResponse(JObject jobj)
        {
            var choice = jobj["choices"]?.FirstOrDefault();
            var msg = choice?["message"];
            var usage = jobj["usage"];

            var result = new ChatResult
            {
                FinishReason = choice?["finish_reason"]?.ToString() ?? "",
                Content = msg?["content"]?.ToString() ?? "",
                PromptTokens = usage?["prompt_tokens"]?.Value<int>() ?? 0,
                CompletionTokens = usage?["completion_tokens"]?.Value<int>() ?? 0,
            };

            var toolCalls = msg?["tool_calls"];
            if (toolCalls != null)
            {
                result.ToolCalls = toolCalls.Select(tc => new ToolCall
                {
                    Id = tc["id"]?.ToString() ?? "",
                    Name = tc["function"]?["name"]?.ToString() ?? "",
                    Arguments = tc["function"]?["arguments"]?.ToString() ?? ""
                }).ToList();
            }

            return result;
        }

        // ── 流式 ─────────────────────────────────────────────

        public virtual async IAsyncEnumerable<ChatChunk> ChatStreamAsync(
            List<ChatMessage> messages, ChatOptions options,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            using var http = CreateHttpClient();
            var url = BuildUrl("/chat/completions");
            var body = BuildRequestBody(messages, options, true);
            var json = JsonConvert.SerializeObject(body);

            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };

            HttpResponseMessage? resp = null;
            Exception? sendEx = null;
            try
            {
                resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            }
            catch (Exception ex) { sendEx = ex; }

            if (sendEx != null)
            {
                yield return new ChatChunk { FinishReason = "error", Content = sendEx.Message };
                yield break;
            }

            if (resp == null)
            {
                yield return new ChatChunk { FinishReason = "error", Content = "请求未返回响应。" };
                yield break;
            }

            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync(ct);
                yield return new ChatChunk { FinishReason = "error", Content = $"HTTP {(int)resp.StatusCode}: {err}" };
                resp.Dispose();
                yield break;
            }

            using var _ = resp;
            using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);

            while (!reader.EndOfStream && !ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (string.IsNullOrEmpty(line)) continue;
                if (!line.StartsWith("data: ")) continue;

                var data = line.Substring(6);
                if (data == "[DONE]") break;

                JObject jobj;
                try { jobj = JObject.Parse(data); }
                catch { continue; }

                var choice = jobj["choices"]?.FirstOrDefault();
                if (choice == null) continue;

                var delta = choice["delta"];
                var finishReason = choice["finish_reason"]?.ToString();

                var content = delta?["content"]?.ToString();
                var toolCalls = delta?["tool_calls"];

                List<ToolCall>? parsedTools = null;
                if (toolCalls != null)
                {
                    parsedTools = toolCalls.Select(tc => new ToolCall
                    {
                        Id = tc["id"]?.ToString() ?? "",
                        Name = tc["function"]?["name"]?.ToString() ?? "",
                        Arguments = tc["function"]?["arguments"]?.ToString() ?? ""
                    }).ToList();
                }

                if (!string.IsNullOrEmpty(content) || parsedTools?.Count > 0 || !string.IsNullOrEmpty(finishReason))
                {
                    yield return new ChatChunk
                    {
                        Content = content ?? "",
                        FinishReason = finishReason ?? "",
                        ToolCalls = parsedTools ?? []
                    };
                }
            }
        }

        protected static ChatResult Fail(string msg) =>
            new() { Success = false, ErrorMessage = msg };
    }
}
