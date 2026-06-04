using Vs2026ChatProxy.Config;

namespace Vs2026ChatProxy.Providers
{
    /// <summary>
    /// AI 客户端工厂。根据 ProviderConfig.Type 创建对应的 IChatClient 实现。
    /// 参考 AiNet 的 AiClientFactory 设计。
    /// </summary>
    public static class AiClientFactory
    {
        public static IChatClient Create(ProviderConfig provider)
        {
            if (provider == null)
                throw new ArgumentNullException(nameof(provider));

            return (provider.Type ?? "").ToLowerInvariant() switch
            {
                // Anthropic 原生协议
                "anthropic" or "claude" or "claude-code"
                    => new ClaudeClient(provider),

                // Google 原生协议
                "gemini" or "google" or "google-ai-studio"
                    => new GeminiClient(provider),

                // OpenAI 兼容协议（OpenAI / DeepSeek / Moonshot / Qwen / Ollama / ...）
                "openai" or "openai-chat-completion" or "ollama" or "ollama-chat"
                or "deepseek" or "moonshot" or "kimi" or "qwen" or "zhipuai" or "glm"
                or "siliconflow" or "groq" or "mistral" or "openrouter"
                    => new OpenAiClient(provider, DefaultBaseUrl(provider.Type)),

                // 默认走 OpenAI 兼容
                _ => new OpenAiClient(provider, "https://api.openai.com"),
            };
        }

        private static string DefaultBaseUrl(string? type) => (type ?? "").ToLowerInvariant() switch
        {
            "deepseek" => "https://api.deepseek.com",
            "ollama" or "ollama-chat" => "http://localhost:11434",
            _ => "https://api.openai.com",
        };
    }
}
