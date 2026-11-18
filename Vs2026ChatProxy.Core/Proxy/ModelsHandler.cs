using System.Text;
using Newtonsoft.Json;
using Vs2026ChatProxy.Config;

namespace Vs2026ChatProxy.Proxy
{
    /// <summary>
    /// GET /v1/models 处理器。
    /// 返回所有已配置的模型列表（OpenAI 格式），
    /// 并附带 capabilities / model_info 元数据：
    /// VS Code Copilot 只读取 model_info[general.architecture + ".context_length"] 获取上下文长度。
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
                        owned_by = provider.Type,
                        capabilities = new[] { "completion", "tools" },
                        model_info = BuildModelInfo(provider, model)
                    });
                }
            }

            var response = new { @object = "list", data = models };
            var json = JsonConvert.SerializeObject(response);
            var bytes = Encoding.UTF8.GetBytes(json);
            await body.WriteAsync(bytes);
            await body.FlushAsync();
        }

        /// <summary>
        /// 构建 Ollama model_info 风格的元数据（/v1/models 与 /api/show 共用）。
        /// MaxContextTokens 为 0（未知）时省略 context_length 字段，不伪造数值。
        /// </summary>
        public static Dictionary<string, object> BuildModelInfo(ProviderConfig provider, ModelConfig model)
        {
            var info = new Dictionary<string, object>
            {
                ["general.architecture"] = "vschatproxy",
                ["general.basename"] = model.Id,
                ["vschatproxy.namespace_kind"] = "gateway_compatibility",
                ["vschatproxy.provider"] = provider.Name,
            };
            if (model.MaxContextTokens > 0)
            {
                info["general.context_length"] = model.MaxContextTokens;
                info["vschatproxy.context_length"] = model.MaxContextTokens;
            }
            return info;
        }
    }
}
