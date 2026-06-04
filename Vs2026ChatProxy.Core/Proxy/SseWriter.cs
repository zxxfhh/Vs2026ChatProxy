using System.Text;
using Newtonsoft.Json;

namespace Vs2026ChatProxy.Proxy
{
    /// <summary>
    /// OpenAI 兼容 SSE 输出工具。
    /// </summary>
    public static class SseWriter
    {
        private static readonly JsonSerializerSettings _jsonSettings = new()
        {
            StringEscapeHandling = StringEscapeHandling.Default // 不转义 <> 等 HTML 字符
        };

        public static async Task WriteEventAsync(Stream body, object payload, CancellationToken ct = default)
        {
            var json = JsonConvert.SerializeObject(payload, _jsonSettings);
            var bytes = Encoding.UTF8.GetBytes($"data: {json}\n\n");
            await body.WriteAsync(bytes, ct);
        }

        public static async Task FlushAsync(Stream body, CancellationToken ct = default)
        {
            await body.FlushAsync(ct);
        }

        public static async Task WriteDoneAsync(Stream body, CancellationToken ct = default)
        {
            var bytes = Encoding.UTF8.GetBytes("data: [DONE]\n\n");
            await body.WriteAsync(bytes, ct);
            await body.FlushAsync(ct);
        }

        public static long UnixNow() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        private static int _counter;
        public static string NewChunkId() => $"chatcmpl-{Interlocked.Increment(ref _counter)}";

        public static object CreateRoleChunk(string id, string model, string role = "assistant") => new
        {
            id,
            @object = "chat.completion.chunk",
            created = UnixNow(),
            model,
            choices = new[]
            {
                new { index = 0, delta = new { role }, finish_reason = (string?)null }
            }
        };

        public static object CreateContentChunk(string id, string model, string content) => new
        {
            id,
            @object = "chat.completion.chunk",
            created = UnixNow(),
            model,
            choices = new[]
            {
                new { index = 0, delta = new { content }, finish_reason = (string?)null }
            }
        };

        public static object CreateFinishChunk(string id, string model, string reason = "stop") => new
        {
            id,
            @object = "chat.completion.chunk",
            created = UnixNow(),
            model,
            choices = new[]
            {
                new { index = 0, delta = new { }, finish_reason = reason }
            }
        };

        public static object CreateToolCallChunk(string id, string model, int index,
            string toolCallId, string functionName, string argumentsDelta) => new
        {
            id,
            @object = "chat.completion.chunk",
            created = UnixNow(),
            model,
            choices = new[]
            {
                new
                {
                    index = 0,
                    delta = new
                    {
                        tool_calls = new[]
                        {
                            new
                            {
                                index,
                                id = toolCallId,
                                type = "function",
                                function = new
                                {
                                    name = functionName,
                                    arguments = argumentsDelta
                                }
                            }
                        }
                    },
                    finish_reason = (string?)null
                }
            }
        };
    }
}
