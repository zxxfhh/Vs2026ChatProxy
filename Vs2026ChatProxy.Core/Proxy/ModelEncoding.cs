using Vs2026ChatProxy.Config;

namespace Vs2026ChatProxy.Proxy
{
    /// <summary>
    /// Model 字段编解码。
    /// 显示格式（组合模式下）: {providerName}:{modelId} {contextTag}
    /// 例: deepseek:deepseek-chat [128K]
    /// 代理内部始终用纯 modelId，上下文标签仅用于 /v1/models 展示。
    /// </summary>
    public static class ModelEncoding
    {
        /// <summary>解码 model 字段 → (providerName, 纯modelId)</summary>
        public static (string providerName, string modelId) Decode(string model)
        {
            var stripped = StripContextTag(model);
            var idx = stripped.IndexOf(':');
            if (idx < 0)
                throw new ArgumentException($"Invalid model encoding: \"{model}\". Expected: providerName:modelId");
            return (stripped.Substring(0, idx), stripped.Substring(idx + 1));
        }

        /// <summary>编码 model 字段（用于 /v1/models 展示）</summary>
        public static string EncodeWithDisplay(string providerName, ModelConfig model)
        {
            var id = model.Id;
            // 只有开启了"组合"且有标签时，才拼接到 id 中
            if (model.AppendContextTag && !string.IsNullOrEmpty(model.ContextTag))
                id += " " + model.ContextTag;
            return $"{providerName}:{id}";
        }

        /// <summary>从 model 字符串中剥离上下文标签</summary>
        public static string StripContextTag(string model)
        {
            var bracket = model.LastIndexOf('[');
            if (bracket > 0 && model.EndsWith("]") )
            {
                var inside = model.Substring(bracket + 1, model.Length - bracket - 2);
                if (inside.Contains("K") || inside.Contains("M") || inside.Contains("/"))
                    return model.Substring(0, bracket).TrimEnd();
            }
            return model;
        }

        /// <summary>可选的上下文标签列表（128K 起步，低于此的没意义）</summary>
        public static readonly string[] ContextTagOptions =
        [
            "",       // 不标注
            "[2M]",
            "[1M]",
            "[512K]",
            "[256K]",
            "[200K]",
            "[128K]",
        ];
    }
}
