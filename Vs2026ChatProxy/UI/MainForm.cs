using Vs2026ChatProxy.UI;

namespace Vs2026ChatProxy.UI
{
    /// <summary>
    /// WinForms 主窗口（薄壳）。
    /// 内部承载 Core 中的 MainControl，保持独立 exe 的完整窗口体验。
    /// </summary>
    public partial class MainForm : Form
    {
        private readonly MainControl _mainControl;

        public MainForm(string configPath)
        {
            _mainControl = new MainControl(configPath);
            InitializeComponent();

            // 将 MainControl 填充到 Form 中
            _mainControl.Dock = DockStyle.Fill;
            Controls.Add(_mainControl);

            // 窗口关闭时清理资源
            this.FormClosing += async (_, _) => await _mainControl.CleanupAsync();
        }
    }
}
