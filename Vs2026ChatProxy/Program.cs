using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Vs2026ChatProxy.Config;
using Vs2026ChatProxy.Proxy;
using Vs2026ChatProxy.UI;

namespace Vs2026ChatProxy
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 解析配置文件路径
            var configPath = "proxy-config.json";
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--config")
                {
                    configPath = args[i + 1];
                    break;
                }
            }
            if (Environment.GetEnvironmentVariable("UCP_PROXY_CONFIG") is string envPath)
                configPath = envPath;

            // 如果配置文件不存在，从示例复制
            if (!File.Exists(configPath) && File.Exists("proxy-config.example.json"))
                File.Copy("proxy-config.example.json", configPath);

            // 没指定 --config、当前目录也没有配置时，用 exe 旁边的而不是在当前目录新建：
            // 从别的目录启动（比如在源码目录里的终端）否则会在那里悄悄留下一份带 Key 的配置。
            if (!File.Exists(configPath) && configPath == "proxy-config.json")
            {
                var besideExe = Path.Combine(AppContext.BaseDirectory, "proxy-config.json");
                if (File.Exists(besideExe))
                    configPath = besideExe;
            }

            // --headless：无窗口后台模式（供 VS Code 插件等外部宿主拉起）
            if (HasArg(args, "--headless"))
            {
                RunHeadless(configPath, ParseIntArg(args, "--parent-pid"));
                return;
            }

            Application.Run(new MainForm(configPath));
        }

        private static bool HasArg(string[] args, string name)
        {
            foreach (var a in args)
                if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        /// <summary>取带值的开关参数（如 --parent-pid 1234）；缺失或非法返回 0</summary>
        private static int ParseIntArg(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(args[i + 1], out var value) && value > 0)
                    return value;
            }
            return 0;
        }

        /// <summary>
        /// 后台模式：直接启动代理服务，不显示窗口。
        /// 日志写入配置文件同目录（与 UI 模式相同的 proxy_*.log 约定）。
        /// 进程由外部宿主管理生命周期（直接结束进程即可停止）；
        /// 给了 parentPid 时，宿主进程没了（崩溃/被强杀）自己也跟着退出，不留残余进程占着端口。
        /// </summary>
        private static void RunHeadless(string configPath, int parentPid = 0)
        {
            var configDir = Path.GetDirectoryName(Path.GetFullPath(configPath));
            if (string.IsNullOrEmpty(configDir)) configDir = AppContext.BaseDirectory;
            var logPath = Path.Combine(configDir, $"proxy_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            using var logWriter = new StreamWriter(logPath, append: true, Encoding.UTF8) { AutoFlush = true };

            void Log(string msg)
            {
                try { logWriter.WriteLine($"{DateTime.Now:HH:mm:ss} {msg}"); } catch { }
            }

            try
            {
                ConfigManager.Instance.Load(configPath);
                var config = ConfigManager.Instance.Config;
                Log($"📂 [headless] 配置: {Path.GetFullPath(configPath)}");

                var server = new ProxyServer();
                server.OnLog += Log;
                server.StartAsync(config).GetAwaiter().GetResult();

                Log("✅ [headless] 代理已启动，等待请求（结束本进程即停止）");

                // 阻塞直到进程被终止
                using var mre = new ManualResetEvent(false);

                // 宿主进程退出后自己退出（先于 mre 释放，避免定时器回调访问已释放对象）
                using var parentWatch = parentPid > 0
                    ? new System.Threading.Timer(_ =>
                    {
                        bool gone;
                        try
                        {
                            using var parent = System.Diagnostics.Process.GetProcessById(parentPid);
                            gone = parent.HasExited;
                        }
                        catch { gone = true; } // 宿主进程已不存在
                        if (gone) { try { mre.Set(); } catch { } }
                    }, null, 2000, 2000)
                    : null;

                Console.CancelKeyPress += (_, e) => { e.Cancel = true; mre.Set(); };
                AppDomain.CurrentDomain.ProcessExit += (_, _) => mre.Set();
                mre.WaitOne();

                server.StopAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Log($"❌ [headless] 启动失败: {ex.Message}");
                Environment.ExitCode = 1;
            }
        }
    }
}
