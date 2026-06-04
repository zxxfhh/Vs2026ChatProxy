using System.Net;
using System.Text;
using Newtonsoft.Json;
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
        private HttpListener? _listener;
        private CancellationTokenSource? _cts;
        private bool _running;

        public int Port { get; private set; } = 58080;
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
                Log($"   POST http://{Host}:{Port}/v1/chat/completions");
                Log($"   POST http://{Host}:{Port}/v1/compact");
                Log($"   GET  http://{Host}:{Port}/v1/models");
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
            Log("🛑 代理服务已停止");
            await Task.CompletedTask;
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

                    // 使用 Task.Run 避免阻塞 Accept 循环
                    _ = Task.Run(() => HandleRequestAsync(ctx, config, ct), ct);
                }
                catch (HttpListenerException) when (ct.IsCancellationRequested) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex)
                {
                    Log($"Accept error: {ex.Message}");
                }
            }
        }

        private async Task HandleRequestAsync(HttpListenerContext ctx, ProxyConfig config, CancellationToken ct)
        {
            var req = ctx.Request;
            var res = ctx.Response;
            var path = req.Url?.AbsolutePath ?? "/";
            var method = req.HttpMethod;

            try
            {
                var ua = req.Headers["User-Agent"] ?? "(未知)";
                Log($"→ {method} {path}  [{ua}]");

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
                        await ChatHandler.HandleAsync(req.InputStream, res.OutputStream, config, Log, ct, config.CompactThresholdPercent);
                        res.Close();
                        return; // 跳过 finally 的重复 Close

                    case ("/v1/compact", "POST"):
                    case ("/compact", "POST"):
                        await CompactHandler.HandleAsync(req.InputStream, res, config, config.CompactThresholdPercent, ct);
                        break;

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

        private void Log(string message) => OnLog?.Invoke(message);
    }
}
