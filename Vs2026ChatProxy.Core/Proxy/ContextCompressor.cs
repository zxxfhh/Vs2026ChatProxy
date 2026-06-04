using Vs2026ChatProxy.Config;
using Vs2026ChatProxy.Providers;

namespace Vs2026ChatProxy.Proxy
{
    /// <summary>
    /// 上下文压缩器（精简版，参考 AiNet ContextCompressor）。
    /// 两级策略：
    ///   Level 1：调用同一个 Provider 的 LLM 对旧消息做摘要
    ///   Level 2：摘要失败或仍超限 → 硬截断（保留 system + 最近 N 条）
    /// </summary>
    public static class ContextCompressor
    {
        /// <summary>始终保留最近 N 条非 system 消息（不参与压缩）</summary>
        private const int KeepRecentCount = 6;

        /// <summary>摘要用的 System Prompt</summary>
        private const string SummarizeSystemPrompt =
            "你是一个对话摘要助手。请用简洁的中文摘要以下历史对话的关键信息，" +
            "保留所有重要的上下文、决策、代码变更和用户偏好。" +
            "摘要应该足够详细，使得 AI 助手能无缝继续对话。";

        /// <summary>
        /// 按需压缩。上下文使用量超过阈值百分比时自动触发。
        /// 阈值 = MaxContextTokens × (CompactThresholdPercent / 100)。
        /// </summary>
        public static async Task<CompactResult> TrimIfNeededAsync(
            List<ChatMessage> messages,
            ModelConfig model,
            ProviderConfig provider,
            int compactThresholdPercent,
            CancellationToken ct)
        {
            var maxTokens = model.MaxContextTokens;
            if (maxTokens <= 0)
                return new CompactResult(messages, false, "无上下文限制");

            var estimated = EstimateTokens(messages);
            var pct = MathEx.Clamp(compactThresholdPercent, 10, 99);
            var threshold = (int)(maxTokens * pct / 100.0);
            if (estimated <= threshold)
                return new CompactResult(messages, false, $"未超限 ({estimated}/{maxTokens}, 阈值{pct}%={threshold})");

            return await CompactAsync(messages, threshold, provider, ct);
        }

        /// <summary>
        /// 强制压缩（供 POST /v1/compact 手动触发）。
        /// 压缩目标比自动触发更激进（压到阈值的 80%）。
        /// </summary>
        public static async Task<CompactResult> ForceCompactAsync(
            List<ChatMessage> messages,
            ModelConfig model,
            ProviderConfig provider,
            int compactThresholdPercent,
            CancellationToken ct)
        {
            var maxTokens = model.MaxContextTokens;
            if (maxTokens <= 0)
                maxTokens = 131072; // 默认 128K

            var pct = MathEx.Clamp(compactThresholdPercent, 10, 99);
            // 手动压缩更激进：压到阈值的 80%
            var target = (int)(maxTokens * pct / 100.0 * 0.80);
            return await CompactAsync(messages, target, provider, ct, force: true);
        }

        // ── 核心压缩逻辑 ──────────────────────────────────────────

        private static async Task<CompactResult> CompactAsync(
            List<ChatMessage> messages,
            int maxTokens,
            ProviderConfig provider,
            CancellationToken ct,
            bool force = false)
        {
            var originalTokens = EstimateTokens(messages);
            var systemMsgs = messages.Where(m => m.Role == "system").ToList();
            var nonSystem = messages.Where(m => m.Role != "system").ToList();

            // 消息太少则直接硬截断
            if (!force && nonSystem.Count <= KeepRecentCount)
            {
                var truncated = HardTruncate(messages, maxTokens);
                return new CompactResult(truncated, true,
                    $"消息数不足，硬截断 ({originalTokens}→{EstimateTokens(truncated)})");
            }

            var keepCount = force ? Math.Min(2, Math.Max(0, nonSystem.Count - 1)) : KeepRecentCount;
            var compressCount = Math.Max(0, nonSystem.Count - keepCount);
            var toCompress = nonSystem.Take(compressCount).ToList();
            var toKeep = nonSystem.Skip(compressCount).ToList();

            // Level 1：调用 LLM 做摘要
            string summary = "";
            try
            {
                summary = await SummarizeAsync(toCompress, provider, ct);
            }
            catch
            {
                // 摘要失败不阻塞，走 Level 2
            }

            // 组合结果
            var result = new List<ChatMessage>();
            result.AddRange(systemMsgs);

            if (!string.IsNullOrWhiteSpace(summary))
            {
                result.Add(new ChatMessage
                {
                    Role = "system",
                    Content = $"[历史对话摘要（早期 {compressCount} 条消息已压缩）]\n{summary}"
                });
            }

            result.AddRange(toKeep);

            // Level 2：仍超限则硬截断
            if (EstimateTokens(result) > maxTokens)
            {
                result = HardTruncate(result, maxTokens);
            }

            var finalTokens = EstimateTokens(result);
            return new CompactResult(result, true,
                $"已压缩 ({originalTokens}→{finalTokens}，摘要{(string.IsNullOrEmpty(summary) ? "失败" : "成功")})");
        }

        // ── LLM 摘要 ─────────────────────────────────────────────

        private static async Task<string> SummarizeAsync(
            List<ChatMessage> toCompress,
            ProviderConfig provider,
            CancellationToken ct)
        {
            var client = AiClientFactory.Create(provider);

            var messages = new List<ChatMessage>
            {
                new() { Role = "system", Content = SummarizeSystemPrompt }
            };

            // 把要压缩的消息拼成文本
            var sb = new System.Text.StringBuilder();
            foreach (var msg in toCompress)
            {
                var label = msg.Role switch
                {
                    "user" => "用户",
                    "assistant" => "AI",
                    "system" => "系统",
                    _ => msg.Role
                };
                sb.AppendLine($"[{label}] {msg.Content}");
            }

            messages.Add(new ChatMessage { Role = "user", Content = sb.ToString() });

            var options = new ChatOptions
            {
                Model = provider.Models.FirstOrDefault()?.Id ?? "",
                Temperature = 0.3,
                MaxTokens = 2048,
            };

            var result = await client.ChatAsync(messages, options, ct);

            if (!result.Success)
                throw new InvalidOperationException(result.ErrorMessage ?? "摘要调用失败");

            return result.Content ?? "";
        }

        // ── Token 估算（字符数 ÷ 4 + 每条消息固定开销 4 tokens）──

        public static int EstimateTokens(IEnumerable<ChatMessage> messages)
            => messages?.Sum(m => (m.Content?.Length ?? 0) / 4 + 4) ?? 0;

        // ── 硬截断：保留 system + 从后往前尽量多塞 ────────────────

        public static List<ChatMessage> HardTruncate(List<ChatMessage> messages, int maxTokens)
        {
            if (messages.Count == 0 || maxTokens <= 0) return messages;

            var result = new List<ChatMessage>();

            // 始终保留所有 system 消息
            var systemMsgs = messages.Where(m => m.Role == "system").ToList();
            var budget = maxTokens;
            foreach (var sm in systemMsgs)
            {
                result.Add(sm);
                budget -= (sm.Content?.Length ?? 0) / 4 + 4;
            }

            budget = Math.Max(16, budget);

            // 非system消息从后往前加
            var nonSystem = messages.Where(m => m.Role != "system").Reverse().ToList();
            var selected = new List<ChatMessage>();
            foreach (var msg in nonSystem)
            {
                var cost = (msg.Content?.Length ?? 0) / 4 + 4;
                if (cost > budget) break;
                selected.Add(msg);
                budget -= cost;
            }

            selected.Reverse();
            result.AddRange(selected);
            return result;
        }
    }

    /// <summary>压缩结果</summary>
    public class CompactResult
    {
        public List<ChatMessage> Messages { get; }
        public bool WasCompressed { get; }
        public string Report { get; }

        public CompactResult(List<ChatMessage> messages, bool wasCompressed, string report)
        {
            Messages = messages;
            WasCompressed = wasCompressed;
            Report = report;
        }
    }
}
