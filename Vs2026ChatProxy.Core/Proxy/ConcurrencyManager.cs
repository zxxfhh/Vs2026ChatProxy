using System.Text.RegularExpressions;
using Vs2026ChatProxy.Providers;

namespace Vs2026ChatProxy.Proxy
{
    /// <summary>
    /// 并发请求队列管理器。
    /// 参考 CopilotProxy 的 ConcurrencyQueue，区分推理模型和标准模型的并发限制。
    /// 推理模型（如 Claude extended thinking、DeepSeek R1）消耗更多资源，限制更严格。
    /// </summary>
    public class ConcurrencyManager
    {
        private readonly SemaphoreSlim _thinkingSemaphore;
        private readonly SemaphoreSlim _standardSemaphore;

        /// <summary>推理模型最大并发数</summary>
        public int ThinkingConcurrency { get; }

        /// <summary>标准模型最大并发数</summary>
        public int StandardConcurrency { get; }

        public ConcurrencyManager(int thinkingConcurrency = 3, int standardConcurrency = 8)
        {
            ThinkingConcurrency = Math.Max(1, thinkingConcurrency);
            StandardConcurrency = Math.Max(1, standardConcurrency);
            _thinkingSemaphore = new SemaphoreSlim(ThinkingConcurrency, ThinkingConcurrency);
            _standardSemaphore = new SemaphoreSlim(StandardConcurrency, StandardConcurrency);
        }

        /// <summary>判断模型是否为推理/思考模型</summary>
        public static bool IsThinkingModel(string? modelId)
        {
            if (string.IsNullOrEmpty(modelId)) return false;
            return Regex.IsMatch(modelId,
                @"(?:reasoner|deep.?seek.*r1|o1[-_]?mini|o1[-_]?preview|o3[-_]?mini)",
                RegexOptions.IgnoreCase);
        }

        /// <summary>获取模型对应的信号量</summary>
        private SemaphoreSlim GetSemaphore(string? modelId) =>
            IsThinkingModel(modelId) ? _thinkingSemaphore : _standardSemaphore;

        /// <summary>在并发控制下执行请求</summary>
        public async Task<T> RunAsync<T>(string? modelId, Func<Task<T>> fn, CancellationToken ct = default)
        {
            var sem = GetSemaphore(modelId);
            await sem.WaitAsync(ct);
            try
            {
                return await fn();
            }
            finally
            {
                sem.Release();
            }
        }

        /// <summary>等待所有并发请求完成（用于优雅关闭）</summary>
        public async Task WaitForDrainAsync(TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            try
            {
                await _thinkingSemaphore.WaitAsync(cts.Token);
                _thinkingSemaphore.Release();
                await _standardSemaphore.WaitAsync(cts.Token);
                _standardSemaphore.Release();
            }
            catch (OperationCanceledException) { /* 超时 */ }
        }

        /// <summary>当前队列状态（用于日志）</summary>
        public (int thinkingCurrent, int thinkingMax, int standardCurrent, int standardMax) GetStatus()
        {
            return (
                ThinkingConcurrency - _thinkingSemaphore.CurrentCount, ThinkingConcurrency,
                StandardConcurrency - _standardSemaphore.CurrentCount, StandardConcurrency
            );
        }
    }

    /// <summary>
    /// 推理模式解析器。
    /// 参考 CopilotProxy 的 parseThinkingMode，支持在模型名称中指定推理级别。
    /// 格式: "provider:model-id/level_(effort)"
    /// 例如: "my-claude:claude-sonnet-4/5_(maximum)"
    ///       "my-deepseek:deepseek-r1/3_(low)"
    /// </summary>
    public static class ThinkingModeParser
    {
        // 格式: model-id/level_(effort)
        private static readonly Regex Pattern = new(
            @"^(.+?)/(?:\d)_\(?((?:low|medium|high|maximum))\)?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>解析模型名称中的推理级别</summary>
        public static (string cleanModelId, ThinkingEffort effort) Parse(string? modelId)
        {
            if (string.IsNullOrEmpty(modelId)) return (modelId ?? "", ThinkingEffort.None);

            var m = Pattern.Match(modelId);
            if (!m.Success) return (modelId, ThinkingEffort.None);

            var clean = m.Groups[1].Value.Trim();
            var effortStr = m.Groups[2].Value.ToLowerInvariant();
            var effort = effortStr switch
            {
                "low" => ThinkingEffort.Low,
                "medium" => ThinkingEffort.Medium,
                "high" => ThinkingEffort.High,
                "maximum" => ThinkingEffort.Maximum,
                _ => ThinkingEffort.None
            };
            return (clean, effort);
        }

        /// <summary>将推理级别转换为 Claude 的 budget_tokens</summary>
        public static int? ToBudgetTokens(ThinkingEffort effort) => effort switch
        {
            ThinkingEffort.Low => 4096,
            ThinkingEffort.Medium => 10240,
            ThinkingEffort.High => 32768,
            ThinkingEffort.Maximum => null, // 不限制，使用 max_tokens
            _ => null
        };

        /// <summary>将推理级别转换为 DeepSeek 的 reasoning_effort</summary>
        public static string? ToReasoningEffort(ThinkingEffort effort) => effort switch
        {
            ThinkingEffort.Low => "low",
            ThinkingEffort.Medium => "medium",
            ThinkingEffort.High => "high",
            ThinkingEffort.Maximum => "maximum",
            _ => null
        };
    }
}
