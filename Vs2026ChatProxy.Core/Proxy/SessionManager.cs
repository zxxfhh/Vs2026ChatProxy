using System.Collections.Concurrent;

namespace Vs2026ChatProxy.Proxy
{
    /// <summary>
    /// 会话跟踪与 Keepalive 管理器。
    /// 参考 CopilotProxy 的 session-keepalive，跟踪活跃会话并支持定期 ping 保持 KV Cache。
    /// </summary>
    public class SessionManager
    {
        /// <summary>会话信息</summary>
        private class SessionInfo
        {
            public string SessionId { get; set; } = "";
            public string ProviderName { get; set; } = "";
            public string ModelId { get; set; } = "";
            public DateTime LastActivity { get; set; } = DateTime.UtcNow;
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
            public int RequestCount { get; set; }
        }

        private readonly ConcurrentDictionary<string, SessionInfo> _sessions = new();

        /// <summary>会话最大空闲时间（默认 30 分钟）</summary>
        public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(30);

        /// <summary>会话最大生命周期（默认 24 小时）</summary>
        public TimeSpan MaxLifetime { get; set; } = TimeSpan.FromHours(24);

        /// <summary>最大会话数</summary>
        public int MaxSessions { get; set; } = 100;

        /// <summary>记录请求并更新会话活跃时间</summary>
        public void TrackRequest(string sessionId, string providerName, string modelId)
        {
            if (string.IsNullOrEmpty(sessionId)) return;

            _sessions.AddOrUpdate(sessionId,
                _ => new SessionInfo
                {
                    SessionId = sessionId,
                    ProviderName = providerName,
                    ModelId = modelId,
                    LastActivity = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow,
                    RequestCount = 1
                },
                (_, existing) =>
                {
                    existing.LastActivity = DateTime.UtcNow;
                    existing.RequestCount++;
                    return existing;
                });

            // 超过上限时清理过期会话
            if (_sessions.Count > MaxSessions)
                CleanupExpired();
        }

        /// <summary>获取活跃会话数</summary>
        public int ActiveSessionCount => _sessions.Count;

        /// <summary>清理过期和超生命周期的会话</summary>
        public int CleanupExpired()
        {
            var now = DateTime.UtcNow;
            var toRemove = new List<string>();

            foreach (var kvp in _sessions)
            {
                var idle = now - kvp.Value.LastActivity;
                var age = now - kvp.Value.CreatedAt;
                if (idle > IdleTimeout || age > MaxLifetime)
                    toRemove.Add(kvp.Key);
            }

            foreach (var key in toRemove)
                _sessions.TryRemove(key, out _);

            return toRemove.Count;
        }

        /// <summary>从请求消息列表中生成会话 ID（基于 system 消息内容的哈希）</summary>
        public static string DeriveSessionId(List<Providers.ChatMessage> messages)
        {
            // 使用第一条 system 消息的内容作为会话标识基础
            var systemMsg = messages.FirstOrDefault(m => m.Role == "system");
            var key = systemMsg?.Content ?? "";
            if (string.IsNullOrEmpty(key))
                return "_default";

            // 简单的 DJB2 哈希
            uint hash = 5381;
            foreach (var c in key)
                hash = ((hash << 5) + hash) ^ (uint)c;
            return hash.ToString("x8");
        }
    }
}
