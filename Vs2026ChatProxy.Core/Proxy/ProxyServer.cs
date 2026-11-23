using System.Net;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vs2026ChatProxy.Config;

namespace Vs2026ChatProxy.Proxy
{
    /// <summary>
    /// HTTP 代理服务。
    /// 使用 HttpListener（轻量级，不需要 ASP.NET Core 依赖）。
    /// 监听 localhost，处理 /v1/chat/completions、/v1/models 等端点。
    /// </summary>
    public class ProxyServer
    {
        /// <summary>代理自身版本（/api/version 返回值），随发布版本一起改（package.json / vsixmanifest / git tag 同号）</summary>
        public const string ProxyVersion = "1.1.5";

        private HttpListener? _listener;
        private CancellationTokenSource? _cts;
        private bool _running;

        // 并发控制：区分推理模型和标准模型，防止线程池饥饿
        private ConcurrencyManager? _concurrency;

        // 会话管理：跟踪活跃会话
        public SessionManager Sessions { get; } = new();

        public int Port { get; private set; } = 11434;
        public string Host { get; private set; } = "127.0.0.1";
        public bool IsRunning => _running;

        public event Action<string>? OnLog;

        /// <summary>启动代理服务</summary>
        public async Task StartAsync(ProxyConfig? config = null)
        {
            config ??= ConfigManager.Instance.Config;
            Port = config.Port;
            Host = config.Host;

            _cts = new CancellationTokenSource();
            _concurrency = new ConcurrencyManager(
                thinkingConcurrency: 3,
                standardConcurrency: 8);
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://{Host}:{Port}/");

            try
            {
                _listener.Start();
                _running = true;
                Log($"🚀 代理服务已启动: http://{Host}:{Port}");
                Log($"   Providers: {config.Providers.Count}");
                foreach (var p in config.Providers)
                    Log($"   ├─ {p.Name} ({p.Type}) — {p.Models.Count} models");
                Log("");
                Log("端点:");
                Log($"   POST http://{Host}:{Port}/v1/chat/completions  (OpenAI SSE)");
                Log($"   POST http://{Host}:{Port}/api/chat            (Ollama NDJSON)");
                Log($"   POST http://{Host}:{Port}/v1/compact");
                Log($"   GET  http://{Host}:{Port}/v1/models           (含 model_info 元数据)");
                Log($"   GET/POST http://{Host}:{Port}/api/tags|ps|show|version  (Ollama 兼容)");
                Log($"   GET  http://{Host}:{Port}/health");

                // 开始处理请求
                _ = Task.Run(() => AcceptLoopAsync(config, _cts.Token));
            }
            catch (HttpListenerException ex)
            {
                Log($"❌ 启动失败: {ex.Message}");
                if (ex.ErrorCode == 32) // Address already in use
                    Log($"   端口 {Port} 已被占用，请更改配置中的 Port");
                throw;
            }
        }

        /// <summary>停止代理服务</summary>
        public async Task StopAsync()
        {
            _running = false;
            _cts?.Cancel();
            _listener?.Stop();
            // 等待并发请求释放（最多等 5 秒）
            if (_concurrency != null)
            {
                await _concurrency.WaitForDrainAsync(TimeSpan.FromSeconds(5));
                _concurrency = null;
            }
            _cts?.Dispose();
            _cts = null;
            Log("🛑 代理服务已停止");
        }

        /// <summary>重启代理服务</summary>
        public async Task RestartAsync(ProxyConfig config)
        {
            await StopAsync();
            await StartAsync(config);
        }

        private async Task AcceptLoopAsync(ProxyConfig config, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _listener != null && _listener.IsListening)
            {
                try
                {
                    var ctx = await _listener.GetContextAsync();

                    // 并发控制：区分推理/标准模型，防止线程池饥饿
                    _ = Task.Run(async () =>
                    {
                        // 预读请求路径判断是否为聊天请求
                        var path = ctx.Request.Url?.AbsolutePath ?? "/";
                        await HandleRequestWithConcurrencyAsync(ctx, config, ct, path);
                    }, ct);
                }
                catch (HttpListenerException) when (ct.IsCancellationRequested) { break; }
                catch (ObjectDisposedException) { break; }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Log($"Accept error: {ex.Message}");
                }
            }
        }

        /// <summary>带并发控制的请求处理：非聊天请求直接执行，聊天请求按模型类型排队</summary>
        private async Task HandleRequestWithConcurrencyAsync(HttpListenerContext ctx, ProxyConfig config, CancellationToken ct, string path)
        {
            // 非聊天请求（健康检查、模型列表等）直接执行，不占并发槽
            var isChat = path == "/v1/chat/completions" || path == "/chat/completions"
                || path == "/api/chat";
            if (!isChat || _concurrency == null)
            {
                await HandleRequestAsync(ctx, config, ct);
                return;
            }

            // 聊天请求：使用默认信号量（标准模型队列），实际并发控制在 ChatHandler 内按模型细分
            await _concurrency.RunAsync<object?>(null, async () =>
            {
                await HandleRequestAsync(ctx, config, ct);
                return null;
            }, ct);
        }

        private async Task HandleRequestAsync(HttpListenerContext ctx, ProxyConfig config, CancellationToken ct)
        {
            var req = ctx.Request;
            var res = ctx.Response;
            var path = req.Url?.AbsolutePath ?? "/";
            var method = req.HttpMethod;

            try
            {
                // 客户端 UA 分类显示（Navicat 等第三方 Ollama 客户端可能不带 UA）
                var ua = req.Headers["User-Agent"];
                string clientTag;
                if (string.IsNullOrWhiteSpace(ua))
                    clientTag = $"{req.RemoteEndPoint?.Address} (无UA)";
                else if (ua.IndexOf("vscode", StringComparison.OrdinalIgnoreCase) >= 0)
                    clientTag = "VSCode";
                else if (ua.IndexOf("visualstudio", StringComparison.OrdinalIgnoreCase) >= 0)
                    clientTag = "VisualStudio";
                else
                    clientTag = ua;
                Log($"→ {method} {path}  [{clientTag}]");

                // CORS
                res.Headers.Add("Access-Control-Allow-Origin", "*");
                res.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
                res.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization");

                if (method == "OPTIONS")
                {
                    res.StatusCode = 204;
                    res.Close();
                    return;
                }

                switch (path, method)
                {
                    case ("/", "GET"):
                        await WriteJsonAsync(res, new { service = "Vs2026ChatProxy", status = "running" });
                        break;

                    case ("/health", "GET"):
                        await WriteJsonAsync(res, new { status = "ok", providers = config.Providers.Count });
                        break;

                    case ("/v1/models", "GET"):
                    case ("/models", "GET"):
                        res.ContentType = "application/json";
                        await ModelsHandler.HandleAsync(res.OutputStream, config);
                        break;

                    case ("/v1/chat/completions", "POST"):
                    case ("/chat/completions", "POST"):
                        res.ContentType = "text/event-stream; charset=utf-8";
                        res.Headers.Add("Cache-Control", "no-cache");
                        res.Headers.Add("Connection", "keep-alive");
                        res.SendChunked = true;
                        await ChatHandler.HandleAsync(req.InputStream, res.OutputStream, config, Log, ct, config.CompactThresholdPercent, Sessions);
                        res.Close();
                        return; // 跳过 finally 的重复 Close

                    case ("/api/chat", "POST"):
                        res.ContentType = "application/x-ndjson; charset=utf-8";
                        res.Headers.Add("Cache-Control", "no-cache");
                        res.SendChunked = true;
                        await OllamaHandler.HandleAsync(req.InputStream, res.OutputStream, config, Log, ct, config.CompactThresholdPercent, Sessions);
                        res.Close();
                        return;

                    case ("/v1/compact", "POST"):
                    case ("/compact", "POST"):
                        await CompactHandler.HandleAsync(req.InputStream, res, config, config.CompactThresholdPercent, ct);
                        break;

                    // ── Ollama 兼容端点（VS 2026 BYOK / VS Code 会查询）──

                    case ("/api/tags", "GET"):
                    case ("/api/list", "GET"):
                    case ("/api/models", "GET"):
                        // Ollama 模型列表格式（VS 2026 会查询此端点）
                        await WriteOllamaTagsAsync(res, config);
                        break;

                    case ("/api/show", "POST"):
                        // Ollama 模型详情：VS Code Copilot 只读 model_info[general.architecture + ".context_length"]
                        await HandleOllamaShowAsync(req, res, config, Log);
                        break;

                    case ("/api/ps", "GET"):
                        // Ollama 已加载模型列表（客户端探测端点，本地网关无加载态，返回空表）
                        await WriteJsonAsync(res, new { models = new object[0] });
                        break;

                    case ("/api/version", "GET"):
                        await WriteJsonAsync(res, new { version = ProxyVersion });
                        break;

                    case ("/api/embeddings", "POST"):
                    case ("/api/embed", "POST"):
                        await WriteJsonAsync(res, new { model = "proxy", embeddings = new[] { new[] { 0.0 } }, total_duration = 0L, load_duration = 0L, prompt_eval_count = 0 });
                        break;

                    case ("/api/pull", "POST"):
                        // 模拟 pull 成功
                        res.ContentType = "application/x-ndjson";
                        res.SendChunked = true;
                        var pullJson = JsonConvert.SerializeObject(new { status = "success" }) + "\n";
                        await res.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(pullJson), ct);
                        await res.OutputStream.FlushAsync(ct);
                        res.Close();
                        return;

                    default:
                        res.StatusCode = 404;
                        await WriteJsonAsync(res, new { error = new { message = $"Not found: {method} {path}" } });
                        break;
                }
            }
            catch (Exception ex)
            {
                Log($"[{method} {path}] Error: {ex.Message}");
                try
                {
                    if (res.StatusCode == 200) res.StatusCode = 500;
                    await WriteJsonAsync(res, new { error = new { message = ex.Message, type = "internal_error" } });
                }
                catch { /* 连接已关闭 */ }
            }
            finally
            {
                try { res.Close(); } catch { }
            }
        }

        private static async Task WriteJsonAsync(HttpListenerResponse res, object data)
        {
            res.ContentType = "application/json";
            var json = JsonConvert.SerializeObject(data);
            var bytes = Encoding.UTF8.GetBytes(json);
            await res.OutputStream.WriteAsync(bytes);
            await res.OutputStream.FlushAsync();
        }

        /// <summary>
        /// 处理 POST /api/show：返回指定模型的 Ollama 详情。
        /// VS Code Copilot 通过 model_info[general.architecture + ".context_length"] 读取上下文长度；
        /// 未知模型返回 404，不做伪造。
        /// </summary>
        private static async Task HandleOllamaShowAsync(HttpListenerRequest req, HttpListenerResponse res, ProxyConfig config, Action<string>? onLog = null)
        {
            string? modelName = null;
            try
            {
                using var reader = new StreamReader(req.InputStream, Encoding.UTF8);
                var bodyJson = JsonConvert.DeserializeObject(await reader.ReadToEndAsync()) as JObject;
                modelName = (bodyJson?["model"] ?? bodyJson?["name"])?.ToString();
            }
            catch { /* body 解析失败按未指定处理 */ }

            if (!string.IsNullOrWhiteSpace(modelName))
            {
                try
                {
                    var (providerName, modelId) = ModelEncoding.Decode(modelName);
                    var provider = config.Providers.FirstOrDefault(p => ModelEncoding.ProviderMatches(providerName, p.Name));
                    var model = provider?.FindModel(modelId);
                    if (provider != null && model != null)
                    {
                        onLog?.Invoke($"   [api/show] model=\"{modelName}\" → 200 (provider: {provider.Name})");
                        await WriteJsonAsync(res, new
                        {
                            capabilities = new[] { "completion", "tools" },
                            model_info = ModelsHandler.BuildModelInfo(provider, model),
                            details = new { family = provider.Type },
                        });
                        return;
                    }
                }
                catch (ArgumentException) { /* 编码格式不合法，按未找到处理 */ }
            }

            onLog?.Invoke($"   [api/show] model=\"{modelName ?? ""}\" → 404 未匹配");
            res.StatusCode = 404;
            await WriteJsonAsync(res, new { error = new { code = 404, message = $"model not found: \"{modelName ?? ""}\"" } });
        }

        /// <summary>
        /// 返回 Ollama /api/tags 格式的模型列表。
        /// VS 2026 配置为 Ollama 时会用这个端点获取可用模型。
        /// </summary>
        private static async Task WriteOllamaTagsAsync(HttpListenerResponse res, ProxyConfig config)
        {
            var models = new List<object>();
            foreach (var provider in config.Providers)
            {
                foreach (var m in provider.Models)
                {
                    // Ollama 格式：模型名用 provider:model 编码（provider 段净化为 ASCII，
                    // VS 2026 的 Ollama 客户端会校验标签合法性，中文 ID 会被置灰）
                    var tagName = $"{ModelEncoding.SanitizeProviderName(provider.Name)}:{m.Id}";
                    models.Add(new
                    {
                        name = tagName,
                        model = tagName,
                        modified_at = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                        size = 0L,
                        digest = "",
                        details = new
                        {
                            parent_model = "",
                            format = "gguf",
                            family = provider.Type,
                            parameter_size = "",
                            quantization_level = ""
                        }
                    });
                }
            }
            await WriteJsonAsync(res, new { models });
        }

        private void Log(string message) => OnLog?.Invoke(message);
    }
}
