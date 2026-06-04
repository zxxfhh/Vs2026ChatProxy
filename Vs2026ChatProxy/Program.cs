using System;
using System.IO;
using System.Windows.Forms;
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

            Application.Run(new MainForm(configPath));
        }
    }
}
