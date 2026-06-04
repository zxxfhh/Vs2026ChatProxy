using System;
using System.ComponentModel.Design;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Vs2026ChatProxy.VSIX.ToolWindows;
using Task = System.Threading.Tasks.Task;

namespace Vs2026ChatProxy.VSIX
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExists_string, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideToolWindow(typeof(MainToolWindow))]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [Guid("c3d4e5f6-a7b8-9012-cdef-123456789012")]
    public sealed class ProxyExtensionPackage : AsyncPackage
    {
        public static readonly Guid CommandSet = new Guid("d4e5f6a7-b8c9-0123-def4-567890123456");
        public const int CmdidShowMainWindow = 0x2000;

        private static readonly string ConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VsCopilotProxy");
        private static readonly string ConfigPath = Path.Combine(ConfigDir, "proxy-config.json");

        protected override async Task InitializeAsync(CancellationToken ct, IProgress<ServiceProgressData> progress)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(ct);

            // 确保配置目录存在
            Directory.CreateDirectory(ConfigDir);
            if (!File.Exists(ConfigPath))
            {
                var defaultConfig = new Vs2026ChatProxy.Config.ProxyConfig();
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(defaultConfig, Newtonsoft.Json.Formatting.Indented);
                File.WriteAllText(ConfigPath, json);
            }

            // 注册命令
            var cmdService = await GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (cmdService != null)
            {
                var menuCommandID = new CommandID(CommandSet, CmdidShowMainWindow);
                var menuItem = new MenuCommand(this.ShowToolWindow, menuCommandID);
                cmdService.AddCommand(menuItem);
            }
        }

        private void ShowToolWindow(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            ToolWindowPane window = FindToolWindow(typeof(MainToolWindow), 0, true);
            if ((null == window) || (null == window.Frame))
            {
                throw new NotSupportedException("Cannot create tool window");
            }

            IVsWindowFrame windowFrame = (IVsWindowFrame)window.Frame;
            ErrorHandler.ThrowOnFailure(windowFrame.Show());
        }

        public static string GetConfigPath() => ConfigPath;
    }
}
