using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace Vs2026ChatProxy.VSIX.ToolWindows
{
    [Guid("b5c6d7e8-f9a0-1234-bcde-f01234567891")]
    public class MainToolWindow : ToolWindowPane
    {
        private Vs2026ChatProxy.UI.MainControl _mainControl; // default null

        public MainToolWindow() : base(null)
        {
            this.Caption = "VsCopilotProxy";
            this.BitmapImageMoniker = KnownMonikers.Console;

            try
            {
                var configPath = ProxyExtensionPackage.GetConfigPath();
                Debug.WriteLine($"[VsCopilotProxy] ConfigPath={configPath}");

                // 创建 WinForms MainControl
                _mainControl = new Vs2026ChatProxy.UI.MainControl(configPath);

                // 强制浅色背景，防止 VS 深色主题导致黑底黑字
                ForceLightTheme(_mainControl);

                Debug.WriteLine("[VsCopilotProxy] MainControl created OK");

                // 关键：ToolWindowPane.Content 需要 WPF 控件，
                // 用 WindowsFormsHost 包装 WinForms 控件
                var host = new WindowsFormsHost();
                host.Child = _mainControl;
                this.Content = host;
                Debug.WriteLine("[VsCopilotProxy] WindowsFormsHost set as Content OK");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VsCopilotProxy] ERROR: {ex.GetType().Name}: {ex.Message}");
                Debug.WriteLine($"[VsCopilotProxy] STACK: {ex.StackTrace}");
                if (ex.InnerException != null)
                {
                    Debug.WriteLine($"[VsCopilotProxy] INNER: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                }

                // 错误面板也用 WindowsFormsHost 包装
                var errorPanel = new Panel { Dock = DockStyle.Fill, BackColor = System.Drawing.Color.FromArgb(30, 30, 30) };
                var errorLabel = new Label
                {
                    Text = $"VsCopilotProxy Error:\n\n{ex.Message}\n\n{ex.GetType().Name}",
                    Dock = DockStyle.Fill,
                    ForeColor = System.Drawing.Color.Red,
                    Font = new System.Drawing.Font("Consolas", 10)
                };
                errorPanel.Controls.Add(errorLabel);
                var errorHost = new WindowsFormsHost { Child = errorPanel };
                this.Content = errorHost;
            }
        }

        protected override void OnClose()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // 清理 MainControl：停止代理服务器、释放资源、取消事件订阅
            if (_mainControl != null)
            {
                _ = _mainControl.CleanupAsync();
                _mainControl.Dispose();
                _mainControl = null;
            }

            base.OnClose();
        }

        /// <summary>
        /// 递归强制浅色主题：确保所有 WinForms 控件在 VS 深色主题下可读。
        /// 跳过已自行设置深色配色的控件（如日志框 txtLog）。
        /// </summary>
        private static void ForceLightTheme(System.Windows.Forms.Control control)
        {
            foreach (System.Windows.Forms.Control c in control.Controls)
            {
                // 跳过日志框（它有自己的深色配色方案）
                if (c is System.Windows.Forms.RichTextBox)
                    continue;

                // 跳过已设置浅色背景的 DataGridView
                if (c is System.Windows.Forms.DataGridView dgv)
                {
                    dgv.BackgroundColor = System.Drawing.Color.White;
                    continue;
                }

                // GroupBox / Panel / Label / Button 等强制浅色
                if (c is System.Windows.Forms.GroupBox)
                {
                    c.BackColor = System.Drawing.SystemColors.Control;
                    c.ForeColor = System.Drawing.SystemColors.ControlText;
                }
                else if (c is System.Windows.Forms.Label)
                {
                    c.BackColor = System.Drawing.Color.Transparent;
                    c.ForeColor = System.Drawing.SystemColors.ControlText;
                }
                else if (c is System.Windows.Forms.Panel || c is System.Windows.Forms.TableLayoutPanel || c is System.Windows.Forms.FlowLayoutPanel)
                {
                    c.BackColor = System.Drawing.SystemColors.Control;
                }
                else if (c is System.Windows.Forms.Button btn && btn.FlatStyle == System.Windows.Forms.FlatStyle.Standard)
                {
                    btn.BackColor = System.Drawing.SystemColors.Control;
                    btn.ForeColor = System.Drawing.SystemColors.ControlText;
                }
                else if (c is System.Windows.Forms.ComboBox || c is System.Windows.Forms.NumericUpDown)
                {
                    c.BackColor = System.Drawing.Color.White;
                    c.ForeColor = System.Drawing.SystemColors.ControlText;
                }

                // 递归子控件
                if (c.HasChildren)
                    ForceLightTheme(c);
            }

            // 根控件自身
            control.BackColor = System.Drawing.SystemColors.Control;
            control.ForeColor = System.Drawing.SystemColors.ControlText;
        }
    }
}
