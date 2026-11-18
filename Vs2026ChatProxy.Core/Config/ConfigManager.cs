using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Vs2026ChatProxy.Config
{
    /// <summary>
    /// 配置管理器。读写 JSON 配置文件，提供查找方法。
    /// </summary>
    public class ConfigManager
    {
        private static readonly Lazy<ConfigManager> _instance = new(() => new());
        public static ConfigManager Instance => _instance.Value;

        private ProxyConfig _config = new();
        private string _configPath = "";

        public ProxyConfig Config => _config;

        private ConfigManager() { }

        /// <summary>从文件加载配置</summary>
        public void Load(string configPath)
        {
            _configPath = configPath;
            if (!File.Exists(configPath))
            {
                _config = new ProxyConfig();
                return;
            }

            var json = File.ReadAllText(configPath);
            _config = JsonConvert.DeserializeObject<ProxyConfig>(json) ?? new ProxyConfig();
            MigrateLegacyFields(json);
            Validate();
        }

        /// <summary>
        /// 迁移旧版配置字段：
        /// maxInputTokens（旧版键，当前模型已不读取）→ MaxContextTokens。
        /// MaxContextTokens 影响 /v1/models 与 /api/show 广播的 context_length
        /// （VS Code Copilot 依赖该字段），旧配置缺迁移会导致上下文长度缺失。
        /// </summary>
        private void MigrateLegacyFields(string json)
        {
            try
            {
                var raw = JObject.Parse(json);
                var providers = raw["providers"] as JArray;
                if (providers == null) return;
                foreach (var p in providers)
                {
                    var providerName = p?["name"]?.ToString();
                    if (string.IsNullOrEmpty(providerName)) continue;
                    var provider = _config.Providers.FirstOrDefault(x => x.Name == providerName);
                    if (provider == null) continue;
                    var models = p["models"] as JArray;
                    if (models == null) continue;
                    foreach (var m in models)
                    {
                        var modelId = m?["id"]?.ToString();
                        if (string.IsNullOrEmpty(modelId)) continue;
                        var model = provider.FindModel(modelId);
                        if (model == null || model.MaxContextTokens > 0) continue;
                        var legacyMax = m?["maxInputTokens"]?.Value<int>() ?? 0;
                        if (legacyMax > 0)
                            model.MaxContextTokens = legacyMax;
                    }
                }
            }
            catch
            {
                // 迁移失败不影响主流程（按新配置语义继续）
            }
        }

        /// <summary>保存配置到文件</summary>
        public void Save()
        {
            if (string.IsNullOrEmpty(_configPath)) return;
            var json = JsonConvert.SerializeObject(_config, Formatting.Indented);
            File.WriteAllText(_configPath, json);
        }

        /// <summary>重载配置</summary>
        public void Reload()
        {
            if (!string.IsNullOrEmpty(_configPath) && File.Exists(_configPath))
                Load(_configPath);
        }

        public ProviderConfig? FindProvider(string name)
            => _config.Providers.FirstOrDefault(p => p.Name == name);

        private void Validate()
        {
            foreach (var p in _config.Providers)
            {
                if (string.IsNullOrEmpty(p.Name))
                    throw new InvalidOperationException("每个 Provider 必须有 Name");
                if (string.IsNullOrEmpty(p.Type))
                    throw new InvalidOperationException($"Provider \"{p.Name}\" 必须有 Type");
                if (p.Models.Count == 0)
                    throw new InvalidOperationException($"Provider \"{p.Name}\" 必须至少有一个模型");
            }
        }
    }
}
