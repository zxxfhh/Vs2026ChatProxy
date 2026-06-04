using System.Net;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vs2026ChatProxy.Config;

namespace Vs2026ChatProxy.Proxy
{
    /// <summary>
    /// POST /v1/compact 处理器。
    /// 手动触发上下文压缩（等同于 /compact 命令）。
    /// 请求体: { "model": "provider:model-id", "messages": [...] }
    /// 响应: { "compressed": true, "report": "...", "messages": [...] }
    /// </summary>
    public static class CompactHandler
    {
        public static async Task HandleAsync(Stream requestBody, HttpListenerResponse res, ProxyConfig config, int compactThreshold, CancellationToken ct)
        {
            string reqJson;
            using (var ms = new MemoryStream())
            {
                await requestBody.CopyToAsync(ms, 81920);
                reqJson = Encoding.UTF8.GetString(ms.ToArray());
            }

            var req = JObject.Parse(reqJson);
            var modelStr = req["model"]?.ToString() ?? "";

            // 解码 model → provider + modelId
            string providerName, modelId;
            try
            {
                (providerName, modelId) = ModelEncoding.Decode(modelStr);
            }
            catch (ArgumentException ex)
            {
                await WriteJsonAsync(res, 400, new { error = ex.Message });
                return;
            }

            var provider = config.Providers.FirstOrDefault(p => p.Name == providerName);
            if (provider == null)
            {
                await WriteJsonAsync(res, 400, new { error = $"Provider \"{providerName}\" not found" });
                return;
            }

            var model = provider.FindModel(modelId);
            if (model == null)
            {
                await WriteJsonAsync(res, 400, new { error = $"Model \"{modelId}\" not found" });
                return;
            }

            // 转换消息
            var messages = ChatHandler.ConvertMessages(req["messages"] as JArray);

            // 强制压缩
            var result = await ContextCompressor.ForceCompactAsync(messages, model, provider, compactThreshold, ct);

            // 返回压缩后的消息
            var responseMessages = result.Messages.Select(m => new
            {
                role = m.Role,
                content = m.Content
            }).ToList();

            await WriteJsonAsync(res, 200, new
            {
                compressed = result.WasCompressed,
                report = result.Report,
                messages = responseMessages
            });
        }

        private static async Task WriteJsonAsync(HttpListenerResponse res, int status, object data)
        {
            res.StatusCode = status;
            res.ContentType = "application/json";
            var json = JsonConvert.SerializeObject(data);
            var bytes = Encoding.UTF8.GetBytes(json);
            await res.OutputStream.WriteAsync(bytes);
            await res.OutputStream.FlushAsync();
        }
    }
}
