using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vs2026ChatProxy.Config;

namespace Vs2026ChatProxy.Providers
{
    /// <summary>
    /// Google Gemini Generative Language API 客户端。
    /// 端点: POST /v1beta/models/{model}:streamGenerateContent?alt=sse&amp;key={apiKey}
    /// 认证: API Key 作为 URL query parameter
    /// SSE: candidates[0].content.parts[0].text
    /// </summary>
    public class GeminiClient : IChatClient
    {
        private readonly string _apiKey;
        private readonly string _baseUrl;
        private readonly ProviderConfig _provider;

        public GeminiClient(ProviderConfig provider)
        {
            _provider = provider;
            _apiKey = provider.ApiKey ?? "";
            _baseUrl = (provider.BaseUrl ?? "https://generativelanguage.googleapis.com").TrimEnd('/');
        }

        private HttpClient CreateHttpClient()
        {
            var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            return client;
        }

        private object BuildBody(List<ChatMessage> messages, ChatOptions options)
        {
            var contents = new List<object>();
            string? systemInstruction = null;

            foreach (var m in messages)
            {
                if (m.Role == ChatRole.System)
                {
                    systemInstruction = m.Content ?? "";
                }
                else
                {
                    contents.Add(new
                    {
                        role = m.Role == ChatRole.Assistant ? "model" : "user",
                        parts = new object[] { new { text = m.Content ?? "" } }
                    });
                }
            }

            var body = new Dictionary<string, object>
            {
                ["contents"] = contents,
            };

            if (systemInstruction != null)
                body["systemInstruction"] = new { parts = new[] { new { text = systemInstruction } } };

            // generationConfig
            var genConfig = new Dictionary<string, object>();
            if (options.Temperature != 0.7)
                genConfig["temperature"] = options.Temperature;
            if (options.MaxTokens != 4096)
                genConfig["maxOutputTokens"] = options.MaxTokens;
            if (genConfig.Count > 0)
                body["generationConfig"] = genConfig;

            // 工具定义转换（OpenAI → Gemini 格式）
            var geminiTools = ConvertToolsToGeminiFormat(options.Tools);
            if (geminiTools != null)
            {
                body["tools"] = geminiTools;
                var geminiChoice = ConvertToolChoiceToGeminiFormat(options.ToolChoice);
                if (geminiChoice != null)
                    body["tool_config"] = geminiChoice;
            }

            return body;
        }

        private string BuildStreamUrl(string model)
        {
            var base_ = _baseUrl.TrimEnd('/');
            return $"{base_}/v1beta/models/{model}:streamGenerateContent?alt=sse&key={_apiKey}";
        }

        private string BuildNonStreamUrl(string model)
        {
            var base_ = _baseUrl.TrimEnd('/');
            return $"{base_}/v1beta/models/{model}:generateContent?key={_apiKey}";
        }

        // ── 非流式 ───────────────────────────────────────────

        public async Task<ChatResult> ChatAsync(
            List<ChatMessage> messages, ChatOptions options, CancellationToken ct = default)
        {
            using var http = CreateHttpClient();
            var url = BuildNonStreamUrl(options.Model);
            var body = BuildBody(messages, options);
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
            var candidate = jobj["candidates"]?.FirstOrDefault();
            var parts = candidate?["content"]?["parts"] as JArray;
            var textParts = new List<string>();

            if (parts != null)
            {
                foreach (var part in parts)
                {
                    var text = part["text"]?.ToString();
                    if (text != null) textParts.Add(text);
                }
            }

            var usage = jobj["usageMetadata"];
            return new ChatResult
            {
                Content = string.Join("", textParts),
                FinishReason = candidate?["finishReason"]?.ToString()?.ToLower() ?? "",
                PromptTokens = usage?["promptTokenCount"]?.Value<int>() ?? 0,
                CompletionTokens = usage?["candidatesTokenCount"]?.Value<int>() ?? 0,
            };
        }

        // ── 流式 ─────────────────────────────────────────────

        public async IAsyncEnumerable<ChatChunk> ChatStreamAsync(
            List<ChatMessage> messages, ChatOptions options,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            using var http = CreateHttpClient();
            var url = BuildStreamUrl(options.Model);
            var body = BuildBody(messages, options);
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

            while (!reader.EndOfStream && !ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (string.IsNullOrEmpty(line)) continue;
                if (!line.StartsWith("data: ")) continue;

                var data = line.Substring(6);
                // Google SSE 有时返回多行 JSON 片段（数组项），以逗号结尾
                data = data.TrimEnd(',');

                JObject jobj;
                try { jobj = JObject.Parse(data); }
                catch { continue; }

                var candidate = jobj["candidates"]?.FirstOrDefault();
                if (candidate == null) continue;

                var parts = candidate["content"]?["parts"] as JArray;
                if (parts != null)
                {
                    foreach (var part in parts)
                    {
                        var text = part["text"]?.ToString();
                        if (text != null)
                            yield return new ChatChunk { Content = text };

                        var functionCall = part["functionCall"];
                        if (functionCall != null)
                        {
                            yield return new ChatChunk
                            {
                                ToolCalls = [new()
                                {
                                    Id = $"fc_{Guid.NewGuid():N}",
                                    Name = functionCall["name"]?.ToString() ?? "",
                                    Arguments = functionCall["args"]?.ToString(Formatting.None) ?? "{}"
                                }]
                            };
                        }
                    }
                }

                var finishReason = candidate["finishReason"]?.ToString();
                if (!string.IsNullOrEmpty(finishReason))
                    yield return new ChatChunk { FinishReason = finishReason.ToLower() };
            }
        }

        // ── 工具格式转换（OpenAI → Gemini）───────────────────

        /// <summary>
        /// 将 OpenAI 格式的 tools 转换为 Gemini 格式。
        /// OpenAI: [{ type:"function", function:{ name, description, parameters } }]
        /// Gemini:  [{ function_declarations: [{ name, description, parameters }] }]
        /// </summary>
        private static JArray? ConvertToolsToGeminiFormat(JArray? openaiTools)
        {
            if (openaiTools == null || openaiTools.Count == 0) return null;

            var declarations = new JArray();
            foreach (var tool in openaiTools)
            {
                var func = tool["function"];
                if (func == null) continue;

                var decl = new JObject
                {
                    ["name"] = func["name"]?.ToString() ?? "",
                    ["description"] = func["description"]?.ToString() ?? "",
                };
                if (func["parameters"] != null)
                    decl["parameters"] = func["parameters"];

                declarations.Add(decl);
            }

            if (declarations.Count == 0) return null;

            // Gemini 把所有声明包在一个 tools[] 条目里
            return new JArray(new JObject { ["function_declarations"] = declarations });
        }

        /// <summary>
        /// 将 OpenAI 格式的 tool_choice 转换为 Gemini tool_config 格式。
        /// OpenAI: "auto" | "none" | { type:"function", function:{name} }
        /// Gemini: { function_calling_config: { mode:"AUTO"|"ANY"|"NONE" } }
        /// </summary>
        private static JObject? ConvertToolChoiceToGeminiFormat(JToken? openaiChoice)
        {
            if (openaiChoice == null) return null;

            if (openaiChoice.Type == JTokenType.String)
            {
                var val = openaiChoice.ToString().ToUpperInvariant();
                var mode = val switch
                {
                    "AUTO" => "AUTO",
                    "NONE" => "NONE",
                    "REQUIRED" => "ANY",
                    _ => "AUTO"
                };
                return new JObject
                {
                    ["function_calling_config"] = new JObject { ["mode"] = mode }
                };
            }

            if (openaiChoice.Type == JTokenType.Object)
            {
                var obj = (JObject)openaiChoice;
                var funcName = obj["function"]?["name"]?.ToString();
                if (!string.IsNullOrEmpty(funcName))
                {
                    return new JObject
                    {
                        ["function_calling_config"] = new JObject
                        {
                            ["mode"] = "ANY",
                            ["allowed_function_names"] = new JArray(funcName)
                        }
                    };
                }
            }

            return null;
        }
    }
}
