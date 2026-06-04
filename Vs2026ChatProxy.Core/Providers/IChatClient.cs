namespace Vs2026ChatProxy.Providers
{
    /// <summary>
    /// AI 对话客户端统一接口（参考 AiNet，零外部 SDK 依赖）。
    /// </summary>
    public interface IChatClient
    {
        /// <summary>普通对话（等待完整响应后返回）</summary>
        Task<ChatResult> ChatAsync(List<ChatMessage> messages, ChatOptions options, CancellationToken ct = default);

        /// <summary>流式对话（逐块 yield，调用方负责消费并输出 SSE）</summary>
        IAsyncEnumerable<ChatChunk> ChatStreamAsync(List<ChatMessage> messages, ChatOptions options, CancellationToken ct = default);
    }
}
