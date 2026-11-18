using System.Text;
using Vs2026ChatProxy.Config;
using Vs2026ChatProxy.Proxy;

namespace Vs2026ChatProxy.UI
{
    /// <summary>
    /// 主配置控件（UserControl 版本）。
    /// Provider 列表管理 + 运行日志 + 代理控制。
    /// 可被 WinForms Form 或 VSIX ToolWindow 承载。
    /// </summary>
    public partial class MainControl : UserControl
    {
        private readonly ProxyServer _server = new();
        private readonly string _configPath;
        private readonly string _logDir;
        private const long MaxLogFileSize = 10 * 1024 * 1024; // 10 MB
        private StreamWriter? _logWriter;
        private string _currentLogPath = "";
        private bool _disposed;

        // 保存日志回调引用，以便清理时取消订阅
        private readonly Action<string> _logHandler;

        // 日志节流：累积后批量刷新，减少 WinForms 重绘
        private readonly List<(string text, Color color)> _pendingLogs = new();
        private System.Windows.Forms.Timer? _logFlushTimer;
        private const int LogFlushIntervalMs = 100; // 100ms 批量刷新一次

        public ProxyServer Server => _server;

        public MainControl(string configPath)
        {
            _configPath = configPath;
            InitializeComponent();
            _logHandler = msg => SafeInvoke(() => Log(msg));
            _server.OnLog += _logHandler;

            // 初始化日志目录（与配置文件同目录）
            _logDir = Path.GetDirectoryName(_configPath) ?? AppContext.BaseDirectory;
            OpenNewLogFile();
            CleanOldLogFiles();

            LoadConfig();

            // 日志批量刷新定时器
            _logFlushTimer = new System.Windows.Forms.Timer { Interval = LogFlushIntervalMs };
            _logFlushTimer.Tick += LogFlushTimer_Tick;
            _logFlushTimer.Start();
        }

        /// <summary>创建新的日志文件</summary>
        private void OpenNewLogFile()
        {
            try { _logWriter?.Close(); } catch { }
            _currentLogPath = Path.Combine(_logDir, $"proxy_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            _logWriter = new StreamWriter(_currentLogPath, append: true, Encoding.UTF8) { AutoFlush = true };
        }

        /// <summary>清理超过 3 天的旧日志文件</summary>
        private void CleanOldLogFiles()
        {
            try
            {
                var cutoff = DateTime.Now.AddDays(-3);
                foreach (var file in Directory.GetFiles(_logDir, "proxy_*.log"))
                {
                    if (File.GetLastWriteTime(file) < cutoff)
                    {
                        try { File.Delete(file); } catch { }
                    }
                }
            }
            catch { }
        }

        /// <summary>检查当前日志文件大小，超限则切换新文件</summary>
        private void CheckLogFileSize()
        {
            try
            {
                var fi = new FileInfo(_currentLogPath);
                if (fi.Exists && fi.Length > MaxLogFileSize)
                    OpenNewLogFile();
            }
            catch { }
        }

        private void LoadConfig()
        {
            try
            {
                ConfigManager.Instance.Load(_configPath);
                var cfg = ConfigManager.Instance.Config;
                numPort.Value = MathEx.Clamp(cfg.Port, 1024, 65535);
                numCompactThreshold.Value = MathEx.Clamp(cfg.CompactThresholdPercent, 10, 99);
                RefreshDefaultModelCombo();
                RefreshProviderGrid();
                Log("✅ 配置加载成功");
            }
            catch (Exception ex)
            {
                Log($"❌ 配置加载失败: {ex.Message}");
            }
        }

        private void SaveConfig()
        {
            try
            {
                ConfigManager.Instance.Save();
                Log("✅ 配置已保存");
            }
            catch (Exception ex)
            {
                Log($"❌ 保存失败: {ex.Message}");
            }
        }

        private void RefreshProviderGrid()
        {
            dgvProviders.Rows.Clear();
            foreach (var p in ConfigManager.Instance.Config.Providers)
            {
                dgvProviders.Rows.Add(
                    p.Name,
                    p.Type,
                    p.BaseUrl,
                    p.ApiKey != null ? "●●●●●●●●" : "(无)",
                    p.Models.Count
                );
            }
        }

        /// <summary>刷新默认模型按钮文本</summary>
        private void RefreshDefaultModelCombo()
        {
            var cfg = ConfigManager.Instance.Config;
            if (string.IsNullOrEmpty(cfg.DefaultModel) || cfg.DefaultModel == "(无)")
                btnDefaultModel.Text = "(未选择)";
            else
                btnDefaultModel.Text = cfg.DefaultModel;
        }

        /// <summary>打开默认模型选择弹框</summary>
        private void btnDefaultModel_Click(object? sender, EventArgs e)
        {
            var cfg = ConfigManager.Instance.Config;
            using var dlg = new Form
            {
                Text = "选择默认模型",
                Size = new Size(480, 440),
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                Font = new Font("Microsoft YaHei UI", 9F),
            };

            var tv = new TreeView
            {
                Dock = DockStyle.Fill,
                Font = new Font("Microsoft YaHei UI", 9.5F),
                ItemHeight = 28,
                ShowLines = true,
                ShowPlusMinus = true,
                FullRowSelect = true,
                HideSelection = false,
            };

            // 按供应商分组添加节点
            foreach (var p in cfg.Providers)
            {
                var pNode = new TreeNode($"{p.Name} ({p.Type})") { Tag = null };
                foreach (var m in p.Models.Where(m => m.Enabled))
                {
                    var key = $"{p.Name}:{m.Id}";
                    var label = m.ContextTag != "" && m.AppendContextTag
                        ? $"{m.Id} {m.ContextTag}"
                        : m.Id;
                    var mNode = new TreeNode(label) { Tag = key };
                    pNode.Nodes.Add(mNode);

                    // 回显选中
                    if (key == cfg.DefaultModel)
                    {
                        mNode.Checked = true;
                        tv.SelectedNode = mNode;
                    }
                }
                if (pNode.Nodes.Count > 0)
                {
                    tv.Nodes.Add(pNode);
                    pNode.Expand();
                }
            }

            var pnlBtn = new Panel { Dock = DockStyle.Bottom, Height = 50 };
            var btnOk = new Button
            {
                Text = "确定",
                Size = new Size(90, 34),
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                DialogResult = DialogResult.OK,
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Enabled = false,
            };
            var btnClear = new Button
            {
                Text = "清除",
                Size = new Size(90, 34),
                Font = new Font("Microsoft YaHei UI", 9F),
                FlatStyle = FlatStyle.Flat,
            };
            var btnCancel = new Button
            {
                Text = "取消",
                Size = new Size(90, 34),
                Font = new Font("Microsoft YaHei UI", 9F),
                DialogResult = DialogResult.Cancel,
                FlatStyle = FlatStyle.Flat,
            };

            // 选中叶子节点才启用确定按钮
            tv.AfterSelect += (_, args) =>
            {
                btnOk.Enabled = args.Node?.Tag is string;
            };

            var selected = cfg.DefaultModel;
            btnOk.Click += (_, _) =>
            {
                if (tv.SelectedNode?.Tag is string key)
                {
                    selected = key;
                    dlg.Close();
                }
            };
            btnClear.Click += (_, _) =>
            {
                selected = "";
                dlg.Close();
            };

            pnlBtn.Controls.AddRange(new Control[] { btnCancel, btnClear, btnOk });
            var pnlBtnTlp = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
            var btnFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(8, 8, 8, 4),
            };
            btnFlow.Controls.AddRange(new Control[] { btnCancel, btnClear, btnOk });
            pnlBtn.Controls.Add(btnFlow);

            dlg.Controls.Add(tv);
            dlg.Controls.Add(pnlBtn);
            dlg.AcceptButton = btnOk;
            dlg.CancelButton = btnCancel;

            // 自动展开并选中
            if (tv.SelectedNode != null)
                tv.SelectedNode.EnsureVisible();

            // 使用 FindForm 获取父窗体（兼容 UserControl 场景）
            var parentForm = this.FindForm();
            dlg.ShowDialog(parentForm);

            // 保存选择
            cfg.DefaultModel = selected;
            RefreshDefaultModelCombo();
            SaveConfig();
        }

        // ── 代理控制 ───────────────────────────────────────

        private async void btnToggleProxy_Click(object sender, EventArgs e)
        {
            if (_server.IsRunning)
            {
                await _server.StopAsync();
                btnToggleProxy.Text = "启动代理";
                btnToggleProxy.BackColor = Color.FromArgb(0, 120, 215);
                lblStatus.Text = "⏹ 已停止";
                lblStatus.ForeColor = Color.Gray;
            }
            else
            {
                try
                {
                    ConfigManager.Instance.Reload();
                    await _server.StartAsync(ConfigManager.Instance.Config);
                    btnToggleProxy.Text = "停止代理";
                    btnToggleProxy.BackColor = Color.FromArgb(220, 53, 69);
                    lblStatus.Text = $"● 运行中  http://{ConfigManager.Instance.Config.Host}:{ConfigManager.Instance.Config.Port}";
                    lblStatus.ForeColor = Color.FromArgb(40, 167, 69);
                }
                catch (Exception ex)
                {
                    Log($"❌ 启动失败: {ex.Message}");
                }
            }
        }

        // ── Provider 增删改 ───────────────────────────────

        private void btnAddProvider_Click(object sender, EventArgs e)
        {
            var existing = ConfigManager.Instance.Config.Providers.Select(p => p.Name).ToList();
            using var dlg = new ProviderEditDialog(null, existing);
            var parentForm = this.FindForm();
            if (dlg.ShowDialog(parentForm) == DialogResult.OK)
            {
                ConfigManager.Instance.Config.Providers.Add(dlg.Provider);
                RefreshProviderGrid();
                RefreshDefaultModelCombo();
                SaveConfig();
            }
        }

        private void btnEditProvider_Click(object sender, EventArgs e)
        {
            if (dgvProviders.SelectedRows.Count == 0)
            {
                MessageBox.Show("请先选择一个 Provider", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var idx = dgvProviders.SelectedRows[0].Index;
            var provider = ConfigManager.Instance.Config.Providers[idx];
            var existingNames = ConfigManager.Instance.Config.Providers.Select(p => p.Name).ToList();
            using var dlg = new ProviderEditDialog(provider, existingNames);
            var parentForm = this.FindForm();
            if (dlg.ShowDialog(parentForm) == DialogResult.OK)
            {
                ConfigManager.Instance.Config.Providers[idx] = dlg.Provider;
                RefreshProviderGrid();
                RefreshDefaultModelCombo();
                SaveConfig();
            }
        }

        private void btnDelProvider_Click(object sender, EventArgs e)
        {
            if (dgvProviders.SelectedRows.Count == 0) return;
            var idx = dgvProviders.SelectedRows[0].Index;
            var name = ConfigManager.Instance.Config.Providers[idx].Name;
            if (MessageBox.Show($"确定删除 Provider \"{name}\"？", "确认",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                ConfigManager.Instance.Config.Providers.RemoveAt(idx);
                RefreshProviderGrid();
                SaveConfig();
            }
        }

        private void dgvProviders_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0) btnEditProvider_Click(sender, e);
        }

        private void numCompactThreshold_ValueChanged(object sender, EventArgs e)
        {
            ConfigManager.Instance.Config.CompactThresholdPercent = (int)numCompactThreshold.Value;
            SaveConfig();
        }

        private void numPort_ValueChanged(object sender, EventArgs e)
        {
            ConfigManager.Instance.Config.Port = (int)numPort.Value;
            SaveConfig();
        }

        // ── 配置指南弹框 ──────────────────────────────────

        private void btnGuide_Click(object sender, EventArgs e)
        {
            var port = ConfigManager.Instance.Config.Port;
            var host = ConfigManager.Instance.Config.Host;
            var guideText =
                "VS 2026 / VS Code 接入步骤：\r\n" +
                "\r\n" +
                "━━━ 第一步：配置供应商 ━━━\r\n" +
                "\r\n" +
                "1. 点击「➕ 添加」按钮\r\n" +
                "2. 填写供应商信息（类型、名称、Base URL、API Key）\r\n" +
                "3. 添加至少一个模型（可选填上下文标签，如 [128K]）\r\n" +
                "4. 点击保存\r\n" +
                "\r\n" +
                "━━━ 第二步：启动代理 ━━━\r\n" +
                "\r\n" +
                "点击底部「启动代理」按钮\r\n" +
                $"代理将在 http://{host}:{port} 监听\r\n" +
                "\r\n" +
                "━━━ 第三步 A：VS 2026（18.10+ 新版）━━━\r\n" +
                "\r\n" +
                "18.10 起 BYOK 替换了旧 Ask/Agent 体验，入口在 Chat 内：\r\n" +
                "\r\n" +
                "1. 打开 Chat 窗口 → 顶部模型选择器\r\n" +
                "2. 点击「Add a model / Manage Models」\r\n" +
                "3. 方式一（推荐）：连接「Ollama 自定义端点」\r\n" +
                $"   服务地址填：http://{host}:{port}\r\n" +
                "   模型列表自动出现，选择完整 ID（供应商:模型 格式）\r\n" +
                "4. 方式二：连接「OpenAI 自定义端点」（Chat Completions）\r\n" +
                $"   基地址填：http://{host}:{port}/v1\r\n" +
                "   从模型列表选择（或手动粘贴完整 ID）\r\n" +
                "5. 添加后进入 Agent (Preview) 即可使用\r\n" +
                "\r\n" +
                "提示：字段名称以当前 VS 界面为准；\r\n" +
                "不要把 VS Code 的 JSON 配置粘贴进 Visual Studio。\r\n" +
                "\r\n" +
                "━━━ 第三步 B：旧版 VS（17.x / 18.10 前）━━━\r\n" +
                "\r\n" +
                "Tools → Options → GitHub → Copilot Chat →\r\n" +
                "「Add Custom Model」→ 类型 Ollama\r\n" +
                $"URL 填：http://{host}:{port}，API Key 留空\r\n" +
                "模型名称格式：{供应商名称}:{模型ID}\r\n" +
                "\r\n" +
                "━━━ 第三步 C：VS Code ━━━\r\n" +
                "\r\n" +
                "命令面板执行 Chat: Manage Language Models →\r\n" +
                "Add Models → Custom Endpoint → Chat Completions\r\n" +
                $"基地址填：http://{host}:{port}/v1\r\n" +
                "或安装本项目的 VS Code 插件一键配置（见 README）\r\n" +
                "\r\n" +
                "━━━ 示例 ━━━\r\n" +
                "\r\n" +
                "供应商名称: my-anthropic\r\n" +
                "模型 ID: claude-sonnet-4-20250514\r\n" +
                "→ 模型完整 ID：my-anthropic:claude-sonnet-4-20250514\r\n" +
                "\r\n" +
                "━━━ 支持的供应商 ━━━\r\n" +
                "\r\n" +
                "• OpenAI (GPT-4o 等)  • Anthropic (Claude 系列)\r\n" +
                "• Google (Gemini 系列)  • DeepSeek / Moonshot / Qwen\r\n" +
                "• Ollama / SiliconFlow / Groq  • 其他 OpenAI 兼容服务";

            using var dlg = new Form
            {
                Text = "📋 VS 2026 / VS Code 配置指南",
                Size = new Size(560, 700),
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                Font = new Font("Microsoft YaHei UI", 9F),
            };

            var txt = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                Text = guideText,
                Font = new Font("Microsoft YaHei UI", 9.5F),
                BackColor = Color.FromArgb(248, 249, 250),
                BorderStyle = BorderStyle.None,
                ScrollBars = ScrollBars.Vertical,
                Padding = new Padding(12),
            };

            var btnClose = new Button
            {
                Text = "知道了",
                Size = new Size(100, 36),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold),
                DialogResult = DialogResult.OK,
            };

            var pnlBottom = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 52,
                Padding = new Padding(0, 6, 16, 8),
            };
            pnlBottom.Controls.Add(btnClose);

            dlg.Controls.Add(txt);
            dlg.Controls.Add(pnlBottom);
            dlg.AcceptButton = btnClose;

            var parentForm = this.FindForm();
            dlg.ShowDialog(parentForm);
        }

        // ── 日志 ───────────────────────────────────────────

        /// <summary>
        /// 线程安全地调用 UI 委托。
        /// 兼容桌面应用和 VSIX（WindowsFormsHost）环境。
        /// </summary>
        private void SafeInvoke(Action action)
        {
            if (IsDisposed) return;
            try
            {
                if (InvokeRequired)
                {
                    if (IsHandleCreated)
                        BeginInvoke(action);
                    else
                        action();
                }
                else
                {
                    action();
                }
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        /// <summary>根据消息前缀/内容判断日志颜色</summary>
        private static Color GetLogColor(string message)
        {
            if (message.Contains("📥 [user]") || message.Contains("📥 [system]"))
                return Color.FromArgb(100, 180, 255);
            if (message.Contains("📤 [assistant]"))
                return Color.FromArgb(80, 220, 140);
            if (message.Contains("tool_calls") || message.Contains("tool_use") || message.Contains("tools:"))
                return Color.FromArgb(255, 180, 80);
            if (message.Contains("❌") || message.Contains("Error") || message.Contains("error"))
                return Color.FromArgb(255, 90, 90);
            if (message.Contains("✅") || message.Contains("🏁"))
                return Color.FromArgb(80, 200, 120);
            if (message.Contains("请求模型") || message.Contains("🔎"))
                return Color.FromArgb(200, 180, 255);
            if (message.Contains("→ POST") || message.Contains("→ GET"))
                return Color.FromArgb(255, 220, 100);
            if (message.Contains("📦") || message.Contains("压缩"))
                return Color.FromArgb(180, 140, 255);
            return Color.FromArgb(180, 180, 180);
        }

        /// <summary>清除日志</summary>
        private void btnClearLog_Click(object sender, EventArgs e)
        {
            txtLog.Clear();
            lock (_pendingLogs)
                _pendingLogs.Clear();
            Log("🧹 日志已清除");
        }

        private void Log(string message)
        {
            var time = DateTime.Now.ToString("HH:mm:ss");

            // 文件日志：同步写入（AutoFlush 已开启）
            try
            {
                var fileMsg = message.Length > 2000 ? message.Substring(0, 2000) + "...(截断)" : message;
                _logWriter?.WriteLine($"[{time}] {fileMsg}");
                CheckLogFileSize();
            }
            catch { }

            // UI 日志：累积到缓冲区，由定时器批量刷新
            var uiMsg = message.Length > 500 ? message.Substring(0, 500) + "..." : message;
            lock (_pendingLogs)
            {
                _pendingLogs.Add(($"[{time}] {uiMsg}", GetLogColor(message)));
            }
        }

        /// <summary>定时批量刷新日志到 TextBox，减少 UI 重绘次数</summary>
        private void LogFlushTimer_Tick(object? sender, EventArgs e)
        {
            if (_disposed || txtLog.IsDisposed || !txtLog.IsHandleCreated) return;

            List<(string text, Color color)> batch;
            lock (_pendingLogs)
            {
                if (_pendingLogs.Count == 0) return;
                batch = new List<(string, Color)>(_pendingLogs);
                _pendingLogs.Clear();
            }

            txtLog.SuspendLayout();
            try
            {
                foreach (var (text, color) in batch)
                {
                    txtLog.SelectionStart = txtLog.TextLength;
                    txtLog.SelectionLength = 0;
                    txtLog.SelectionColor = color;
                    txtLog.AppendText($"{text}\r\n");
                }
                txtLog.SelectionColor = txtLog.ForeColor;

                // 超出 2000 行时裁剪旧日志
                if (txtLog.Lines.Length > 2000)
                {
                    txtLog.SelectionStart = 0;
                    txtLog.SelectionLength = txtLog.GetFirstCharIndexFromLine(500);
                    txtLog.SelectedText = "";
                }

                txtLog.SelectionStart = txtLog.TextLength;
                txtLog.ScrollToCaret();
            }
            finally
            {
                txtLog.ResumeLayout();
            }
        }

        /// <summary>停止代理并清理资源（供宿主调用）</summary>
        public async Task CleanupAsync()
        {
            if (_disposed) return;
            _disposed = true;

            // 取消事件订阅，防止 GC Root 泄漏
            _server.OnLog -= _logHandler;

            if (_server.IsRunning) await _server.StopAsync();

            // 停止日志刷新定时器
            if (_logFlushTimer != null)
            {
                _logFlushTimer.Stop();
                _logFlushTimer.Dispose();
                _logFlushTimer = null;
            }

            try { _logWriter?.Dispose(); } catch { }
            _logWriter = null;
        }
    }
}
