using Newtonsoft.Json;

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
            Validate();
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
