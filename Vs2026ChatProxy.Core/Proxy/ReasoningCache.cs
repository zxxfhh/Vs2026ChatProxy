using System.Collections.Concurrent;

namespace Vs2026ChatProxy.Proxy
{
    /// <summary>
    /// 推理内容跨请求缓存。
    /// 参考 CopilotProxy 的 reasoning-cache，基于消息内容哈希缓存推理结果，避免重复推理。
    /// </summary>
    public class ReasoningCache
    {
        private class CacheEntry
        {
            public string Reasoning { get; set; } = "";
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
            public DateTime LastAccessed { get; set; } = DateTime.UtcNow;
            public int HitCount { get; set; }
        }

        private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();

        /// <summary>最大缓存条目数</summary>
        public int MaxEntries { get; set; } = 500;

        /// <summary>缓存过期时间（默认 1 小时）</summary>
        public TimeSpan TTL { get; set; } = TimeSpan.FromHours(1);

        /// <summary>缓存推理内容。key 为消息内容哈希，value 为推理结果。</summary>
        public void Store(string messageHash, string reasoning)
        {
            if (string.IsNullOrEmpty(messageHash) || string.IsNullOrEmpty(reasoning)) return;

            // 超过上限时先清理过期条目
            if (_cache.Count >= MaxEntries)
                Cleanup();

            _cache[messageHash] = new CacheEntry
            {
                Reasoning = reasoning,
                CreatedAt = DateTime.UtcNow,
                LastAccessed = DateTime.UtcNow,
                HitCount = 0
            };
        }

        /// <summary>尝试获取缓存的推理内容</summary>
        public bool TryGet(string messageHash, out string? reasoning)
        {
            reasoning = null;
            if (!_cache.TryGetValue(messageHash, out var entry)) return false;

            // 检查是否过期
            if (DateTime.UtcNow - entry.CreatedAt > TTL)
            {
                _cache.TryRemove(messageHash, out _);
                return false;
            }

            entry.LastAccessed = DateTime.UtcNow;
            entry.HitCount++;
            reasoning = entry.Reasoning;
            return true;
        }

        /// <summary>根据消息内容生成缓存键（DJB2 哈希）</summary>
        public static string HashMessages(IEnumerable<Providers.ChatMessage> messages)
        {
            uint hash = 5381;
            foreach (var msg in messages)
            {
                if (string.IsNullOrEmpty(msg.Content)) continue;
                foreach (var c in msg.Content)
                    hash = ((hash << 5) + hash) ^ (uint)c;
            }
            return hash.ToString("x8");
        }

        /// <summary>清理过期条目</summary>
        public int Cleanup()
        {
            var now = DateTime.UtcNow;
            var toRemove = new List<string>();

            foreach (var kvp in _cache)
            {
                if (now - kvp.Value.CreatedAt > TTL)
                    toRemove.Add(kvp.Key);
            }

            // 如果清理过期后仍然超限，按 LRU（LastAccessed）淘汰
            if (_cache.Count - toRemove.Count > MaxEntries)
            {
                var lru = _cache
                    .Where(kvp => !toRemove.Contains(kvp.Key))
                    .OrderBy(kvp => kvp.Value.LastAccessed)
                    .Take(_cache.Count - toRemove.Count - MaxEntries + MaxEntries / 4)
                    .Select(kvp => kvp.Key);
                toRemove.AddRange(lru);
            }

            foreach (var key in toRemove)
                _cache.TryRemove(key, out _);

            return toRemove.Count;
        }

        /// <summary>当前缓存条目数</summary>
        public int Count => _cache.Count;
    }
}
