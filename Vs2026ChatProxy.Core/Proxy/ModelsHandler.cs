using System.Text;
using Newtonsoft.Json;
using Vs2026ChatProxy.Config;

namespace Vs2026ChatProxy.Proxy
{
    /// <summary>
    /// GET /v1/models 处理器。
    /// 返回所有已配置的模型列表（OpenAI 格式）。
    /// </summary>
    public static class ModelsHandler
    {
        public static async Task HandleAsync(Stream body, ProxyConfig config)
        {
            var models = new List<object>();
            foreach (var provider in config.Providers)
            {
                foreach (var model in provider.Models)
                {
                    if (!model.Enabled) continue;
                    models.Add(new
                    {
                        id = ModelEncoding.EncodeWithDisplay(provider.Name, model),
                        @object = "model",
                        created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                        owned_by = provider.Type
                    });
                }
            }

            var response = new { @object = "list", data = models };
            var json = JsonConvert.SerializeObject(response);
            var bytes = Encoding.UTF8.GetBytes(json);
            await body.WriteAsync(bytes);
            await body.FlushAsync();
        }
    }
}
