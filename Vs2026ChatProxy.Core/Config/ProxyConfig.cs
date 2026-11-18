namespace Vs2026ChatProxy.Config
{
    /// <summary>顶层代理配置</summary>
    public class ProxyConfig
    {
        /// <summary>监听端口。默认 11434（Ollama 默认端口，IDE 添加 Ollama 端点时预填的就是它）</summary>
        public int Port { get; set; } = 11434;
        public string Host { get; set; } = "127.0.0.1";
        public List<ProviderConfig> Providers { get; set; } = [];
        /// <summary>
        /// 上下文压缩触发阈值（百分比，0~100）。
        /// 当上下文使用量超过 MaxContextTokens × 此值时自动触发压缩。
        /// 默认 85（即使用到 85% 时开始压缩）。
        /// </summary>
        public int CompactThresholdPercent { get; set; } = 85;
        /// <summary>
        /// 默认模型（格式: "providerName:modelId"）。
        /// 当请求未指定 model 或 model 不匹配任何已配置模型时使用。
        /// </summary>
        public string DefaultModel { get; set; } = "";
    }

    /// <summary>Provider 配置（对应一个 AI 供应商）</summary>
    public class ProviderConfig
    {
        public string Type { get; set; } = "openai";
        public string Name { get; set; } = "";
        public string BaseUrl { get; set; } = "";
        public string? ApiKey { get; set; }
        public List<ModelConfig> Models { get; set; } = [];

        public ModelConfig? FindModel(string modelId)
            => Models.FirstOrDefault(m => m.Id == modelId);
    }

    /// <summary>模型配置</summary>
    public class ModelConfig
    {
        public string Id { get; set; } = "";
        /// <summary>是否启用。禁用的模型不会暴露给代理的 /v1/models 接口</summary>
        public bool Enabled { get; set; } = true;
        /// <summary>
        /// 上下文标签，用户选择。如 "[1M]"、"[128K]" 等。
        /// 用于 /v1/models 展示和 compact 压缩的阈值判断。
        /// </summary>
        public string ContextTag { get; set; } = "";
        /// <summary>
        /// 是否将上下文标签组合到模型 ID 中（如 "deepseek-chat [1M]"）。
        /// true → /v1/models 显示 "provider:model-id [1M]"，代理转发时自动剥离标签。
        /// false → /v1/models 只显示 "provider:model-id"。
        /// </summary>
        public bool AppendContextTag { get; set; } = false;
        /// <summary>
        /// 最大上下文 Token 数（用于 compact 压缩的阈值判断）。
        /// 用户选择 ContextTag 时自动填充，也可手动修改。
        /// 0 表示不限制（不做压缩）。
        /// </summary>
        public int MaxContextTokens { get; set; } = 0;

        /// <summary>上下文标签 → Token 数映射</summary>
        public static readonly Dictionary<string, int> ContextTagTokenMap = new()
        {
            ["[2M]"] = 2_000_000,
            ["[1M]"] = 1_000_000,
            ["[512K]"] = 524_288,
            ["[256K]"] = 262_144,
            ["[200K]"] = 204_800,
            ["[128K]"] = 131_072,
        };

        /// <summary>根据 ContextTag 自动填充 MaxContextTokens</summary>
        public void SyncMaxContextFromTag()
        {
            if (ContextTagTokenMap.TryGetValue(ContextTag, out var tokens))
                MaxContextTokens = tokens;
        }
    }
}
