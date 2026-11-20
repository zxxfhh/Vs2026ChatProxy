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

        /// <summary>
        /// 把 Provider 名净化为 Ollama 标签合法字符并转小写。
        /// VS 2026 的 Ollama 客户端会校验标签格式（Ollama 惯例为全小写
        /// [a-z0-9._-]）：含中文/大写的模型 ID 在管理页正常显示，
        /// 但在模型选择器中被置灰不可选。
        /// 例: "智谱Coding" → "coding"；全非 ASCII 时回退 "provider"。
        /// </summary>
        public static string SanitizeProviderName(string name)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var ch in name)
            {
                if ((ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9')
                    || ch == '_' || ch == '-' || ch == '.')
                    sb.Append(ch);
            }
            var result = sb.ToString().Trim('.').ToLowerInvariant();
            return result.Length > 0 ? result : "provider";
        }

        /// <summary>
        /// 判断请求带回的 provider 名是否匹配配置项：兼容原始名与净化名
        /// （对外广播净化名，请求两种形式都可能回来）。
        /// </summary>
        public static bool ProviderMatches(string requestedName, string actualName)
            => requestedName == actualName
               || string.Equals(requestedName, SanitizeProviderName(actualName), StringComparison.OrdinalIgnoreCase);

        /// <summary>编码 model 字段（用于 /v1/models 展示）</summary>
        public static string EncodeWithDisplay(string providerName, ModelConfig model)
        {
            var id = model.Id;
            // 只有开启了"组合"且有标签时，才拼接到 id 中
            if (model.AppendContextTag && !string.IsNullOrEmpty(model.ContextTag))
                id += " " + model.ContextTag;
            return $"{SanitizeProviderName(providerName)}:{id}";
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
