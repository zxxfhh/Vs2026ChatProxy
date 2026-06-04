using System.Drawing;
using System.Windows.Forms;

namespace Vs2026ChatProxy.UI
{
    partial class MainControl
    {
        private System.ComponentModel.IContainer components = null;

        private TableLayoutPanel tlpRoot;

        // ── 上半部分：Provider 管理 ───────────────────────
        private GroupBox grpProviders;
        private DataGridView dgvProviders;
        private FlowLayoutPanel pnlProviderButtons;
        private Button btnAddProvider;
        private Button btnEditProvider;
        private Button btnDelProvider;

        // ── 下半部分：日志 ────────────────────────────────
        private GroupBox grpLog;
        private RichTextBox txtLog;

        // ── 底部：状态栏 + 控制按钮 ──────────────────────
        private Panel pnlBottom;
        private Label lblStatus;
        private NumericUpDown numPort;
        private Button btnDefaultModel;
        private NumericUpDown numCompactThreshold;
        private Button btnToggleProxy;
        private Button btnGuide;

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            tlpRoot = new TableLayoutPanel();
            topTlp = new TableLayoutPanel();
            grpProviders = new GroupBox();
            provTlp = new TableLayoutPanel();
            dgvProviders = new DataGridView();
            dataGridViewTextBoxColumn1 = new DataGridViewTextBoxColumn();
            dataGridViewTextBoxColumn2 = new DataGridViewTextBoxColumn();
            dataGridViewTextBoxColumn3 = new DataGridViewTextBoxColumn();
            dataGridViewTextBoxColumn4 = new DataGridViewTextBoxColumn();
            dataGridViewTextBoxColumn5 = new DataGridViewTextBoxColumn();
            pnlProviderButtons = new FlowLayoutPanel();
            btnAddProvider = new Button();
            btnEditProvider = new Button();
            btnDelProvider = new Button();
            grpLog = new GroupBox();
            txtLog = new RichTextBox();
            pnlBottom = new Panel();
            bottomTlp = new TableLayoutPanel();
            lblStatus = new Label();
            lblPort = new Label();
            numPort = new NumericUpDown();
            lblModel = new Label();
            btnDefaultModel = new Button();
            lblThreshold = new Label();
            numCompactThreshold = new NumericUpDown();
            lblPercent = new Label();
            btnGuide = new Button();
            btnToggleProxy = new Button();
            tlpRoot.SuspendLayout();
            topTlp.SuspendLayout();
            grpProviders.SuspendLayout();
            provTlp.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)dgvProviders).BeginInit();
            pnlProviderButtons.SuspendLayout();
            grpLog.SuspendLayout();
            pnlBottom.SuspendLayout();
            bottomTlp.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)numPort).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numCompactThreshold).BeginInit();
            SuspendLayout();
            //
            // tlpRoot
            //
            tlpRoot.ColumnCount = 1;
            tlpRoot.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 20F));
            tlpRoot.Controls.Add(topTlp, 0, 0);
            tlpRoot.Controls.Add(pnlBottom, 0, 1);
            tlpRoot.Dock = DockStyle.Fill;
            tlpRoot.Location = new Point(0, 0);
            tlpRoot.Name = "tlpRoot";
            tlpRoot.Padding = new Padding(8);
            tlpRoot.RowCount = 2;
            tlpRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            tlpRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
            tlpRoot.Size = new Size(933, 643);
            tlpRoot.TabIndex = 0;
            //
            // topTlp
            //
            topTlp.ColumnCount = 1;
            topTlp.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 20F));
            topTlp.Controls.Add(grpProviders, 0, 0);
            topTlp.Controls.Add(grpLog, 0, 1);
            topTlp.Dock = DockStyle.Fill;
            topTlp.Location = new Point(11, 11);
            topTlp.Name = "topTlp";
            topTlp.RowCount = 2;
            topTlp.RowStyles.Add(new RowStyle(SizeType.Percent, 46.3971863F));
            topTlp.RowStyles.Add(new RowStyle(SizeType.Percent, 53.6028137F));
            topTlp.Size = new Size(911, 569);
            topTlp.TabIndex = 0;
            //
            // grpProviders
            //
            grpProviders.Controls.Add(provTlp);
            grpProviders.Dock = DockStyle.Fill;
            grpProviders.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
            grpProviders.Location = new Point(3, 3);
            grpProviders.Name = "grpProviders";
            grpProviders.Padding = new Padding(4);
            grpProviders.Size = new Size(905, 257);
            grpProviders.TabIndex = 0;
            grpProviders.TabStop = false;
            grpProviders.Text = "供应商列表";
            //
            // provTlp
            //
            provTlp.ColumnCount = 1;
            provTlp.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 20F));
            provTlp.Controls.Add(dgvProviders, 0, 0);
            provTlp.Controls.Add(pnlProviderButtons, 0, 1);
            provTlp.Dock = DockStyle.Fill;
            provTlp.Location = new Point(4, 21);
            provTlp.Name = "provTlp";
            provTlp.RowCount = 2;
            provTlp.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            provTlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 49F));
            provTlp.Size = new Size(897, 232);
            provTlp.TabIndex = 0;
            //
            // dgvProviders
            //
            dgvProviders.AllowUserToAddRows = false;
            dgvProviders.AllowUserToDeleteRows = false;
            dgvProviders.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dgvProviders.BackgroundColor = Color.White;
            dgvProviders.BorderStyle = BorderStyle.None;
            dgvProviders.ColumnHeadersHeight = 26;
            dgvProviders.Columns.AddRange(new DataGridViewColumn[] { dataGridViewTextBoxColumn1, dataGridViewTextBoxColumn2, dataGridViewTextBoxColumn3, dataGridViewTextBoxColumn4, dataGridViewTextBoxColumn5 });
            dgvProviders.Dock = DockStyle.Fill;
            dgvProviders.Location = new Point(3, 3);
            dgvProviders.MultiSelect = false;
            dgvProviders.Name = "dgvProviders";
            dgvProviders.ReadOnly = true;
            dgvProviders.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgvProviders.Size = new Size(891, 177);
            dgvProviders.TabIndex = 0;
            dgvProviders.CellDoubleClick += dgvProviders_CellDoubleClick;
            //
            // dataGridViewTextBoxColumn1
            //
            dataGridViewTextBoxColumn1.FillWeight = 16F;
            dataGridViewTextBoxColumn1.HeaderText = "名称";
            dataGridViewTextBoxColumn1.Name = "dataGridViewTextBoxColumn1";
            dataGridViewTextBoxColumn1.ReadOnly = true;
            //
            // dataGridViewTextBoxColumn2
            //
            dataGridViewTextBoxColumn2.FillWeight = 12F;
            dataGridViewTextBoxColumn2.HeaderText = "类型";
            dataGridViewTextBoxColumn2.Name = "dataGridViewTextBoxColumn2";
            dataGridViewTextBoxColumn2.ReadOnly = true;
            //
            // dataGridViewTextBoxColumn3
            //
            dataGridViewTextBoxColumn3.FillWeight = 40F;
            dataGridViewTextBoxColumn3.HeaderText = "Base URL";
            dataGridViewTextBoxColumn3.Name = "dataGridViewTextBoxColumn3";
            dataGridViewTextBoxColumn3.ReadOnly = true;
            //
            // dataGridViewTextBoxColumn4
            //
            dataGridViewTextBoxColumn4.FillWeight = 18F;
            dataGridViewTextBoxColumn4.HeaderText = "API Key";
            dataGridViewTextBoxColumn4.Name = "dataGridViewTextBoxColumn4";
            dataGridViewTextBoxColumn4.ReadOnly = true;
            //
            // dataGridViewTextBoxColumn5
            //
            dataGridViewTextBoxColumn5.FillWeight = 10F;
            dataGridViewTextBoxColumn5.HeaderText = "模型数";
            dataGridViewTextBoxColumn5.Name = "dataGridViewTextBoxColumn5";
            dataGridViewTextBoxColumn5.ReadOnly = true;
            //
            // pnlProviderButtons
            //
            pnlProviderButtons.Controls.Add(btnAddProvider);
            pnlProviderButtons.Controls.Add(btnEditProvider);
            pnlProviderButtons.Controls.Add(btnDelProvider);
            pnlProviderButtons.Dock = DockStyle.Fill;
            pnlProviderButtons.Location = new Point(3, 186);
            pnlProviderButtons.Name = "pnlProviderButtons";
            pnlProviderButtons.Padding = new Padding(2);
            pnlProviderButtons.Size = new Size(891, 43);
            pnlProviderButtons.TabIndex = 1;
            //
            // btnAddProvider
            //
            btnAddProvider.Font = new Font("Microsoft YaHei UI", 9F);
            btnAddProvider.Location = new Point(5, 5);
            btnAddProvider.Name = "btnAddProvider";
            btnAddProvider.Size = new Size(90, 32);
            btnAddProvider.TabIndex = 0;
            btnAddProvider.Text = "➕ 添加";
            btnAddProvider.Click += btnAddProvider_Click;
            //
            // btnEditProvider
            //
            btnEditProvider.Font = new Font("Microsoft YaHei UI", 9F);
            btnEditProvider.Location = new Point(101, 5);
            btnEditProvider.Name = "btnEditProvider";
            btnEditProvider.Size = new Size(90, 32);
            btnEditProvider.TabIndex = 1;
            btnEditProvider.Text = "✏️ 编辑";
            btnEditProvider.Click += btnEditProvider_Click;
            //
            // btnDelProvider
            //
            btnDelProvider.Font = new Font("Microsoft YaHei UI", 9F);
            btnDelProvider.Location = new Point(197, 5);
            btnDelProvider.Name = "btnDelProvider";
            btnDelProvider.Size = new Size(90, 32);
            btnDelProvider.TabIndex = 2;
            btnDelProvider.Text = "🗑️ 删除";
            btnDelProvider.Click += btnDelProvider_Click;
            //
            // grpLog
            //
            grpLog.Controls.Add(txtLog);
            grpLog.Dock = DockStyle.Fill;
            grpLog.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
            grpLog.Location = new Point(3, 266);
            grpLog.Name = "grpLog";
            grpLog.Padding = new Padding(4);
            grpLog.Size = new Size(905, 300);
            grpLog.TabIndex = 1;
            grpLog.TabStop = false;
            grpLog.Text = "运行日志";
            //
            // txtLog
            //
            txtLog.BackColor = Color.FromArgb(30, 30, 30);
            txtLog.BorderStyle = BorderStyle.None;
            txtLog.DetectUrls = false;
            txtLog.Dock = DockStyle.Fill;
            txtLog.Font = new Font("Cascadia Code", 8.5F);
            txtLog.ForeColor = Color.FromArgb(200, 200, 200);
            txtLog.Location = new Point(4, 21);
            txtLog.Name = "txtLog";
            txtLog.ReadOnly = true;
            txtLog.Size = new Size(897, 275);
            txtLog.TabIndex = 0;
            txtLog.Text = "";
            //
            // pnlBottom
            //
            pnlBottom.Controls.Add(bottomTlp);
            pnlBottom.Dock = DockStyle.Fill;
            pnlBottom.Location = new Point(11, 586);
            pnlBottom.Name = "pnlBottom";
            pnlBottom.Padding = new Padding(4, 6, 4, 4);
            pnlBottom.Size = new Size(911, 46);
            pnlBottom.TabIndex = 1;
            //
            // bottomTlp
            //
            bottomTlp.ColumnCount = 10;
            bottomTlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            bottomTlp.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42F));
            bottomTlp.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 65F));
            bottomTlp.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42F));
            bottomTlp.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180F));
            bottomTlp.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 75F));
            bottomTlp.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 55F));
            bottomTlp.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 20F));
            bottomTlp.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130F));
            bottomTlp.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130F));
            bottomTlp.Controls.Add(lblStatus, 0, 0);
            bottomTlp.Controls.Add(lblPort, 1, 0);
            bottomTlp.Controls.Add(numPort, 2, 0);
            bottomTlp.Controls.Add(lblModel, 3, 0);
            bottomTlp.Controls.Add(btnDefaultModel, 4, 0);
            bottomTlp.Controls.Add(lblThreshold, 5, 0);
            bottomTlp.Controls.Add(numCompactThreshold, 6, 0);
            bottomTlp.Controls.Add(lblPercent, 7, 0);
            bottomTlp.Controls.Add(btnGuide, 8, 0);
            bottomTlp.Controls.Add(btnToggleProxy, 9, 0);
            bottomTlp.Dock = DockStyle.Fill;
            bottomTlp.Location = new Point(4, 6);
            bottomTlp.Name = "bottomTlp";
            bottomTlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
            bottomTlp.Size = new Size(903, 36);
            bottomTlp.TabIndex = 0;
            //
            // lblStatus
            //
            lblStatus.Font = new Font("Microsoft YaHei UI", 10F);
            lblStatus.ForeColor = Color.Gray;
            lblStatus.Location = new Point(3, 0);
            lblStatus.Name = "lblStatus";
            lblStatus.Size = new Size(100, 23);
            lblStatus.TabIndex = 0;
            lblStatus.Text = "⏹ 未启动";
            lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            //
            // lblPort
            //
            lblPort.Dock = DockStyle.Fill;
            lblPort.Font = new Font("Microsoft YaHei UI", 9F);
            lblPort.Location = new Point(167, 0);
            lblPort.Name = "lblPort";
            lblPort.Size = new Size(36, 36);
            lblPort.TabIndex = 1;
            lblPort.Text = "端口:";
            lblPort.TextAlign = ContentAlignment.MiddleRight;
            //
            // numPort
            //
            numPort.Dock = DockStyle.Bottom;
            numPort.Font = new Font("Microsoft YaHei UI", 9F);
            numPort.Location = new Point(209, 10);
            numPort.Maximum = new decimal(new int[] { 65535, 0, 0, 0 });
            numPort.Minimum = new decimal(new int[] { 1024, 0, 0, 0 });
            numPort.Name = "numPort";
            numPort.Size = new Size(59, 23);
            numPort.TabIndex = 2;
            numPort.TextAlign = HorizontalAlignment.Center;
            numPort.Value = new decimal(new int[] { 58080, 0, 0, 0 });
            numPort.ValueChanged += numPort_ValueChanged;
            //
            // lblModel
            //
            lblModel.Dock = DockStyle.Fill;
            lblModel.Font = new Font("Microsoft YaHei UI", 9F);
            lblModel.Location = new Point(274, 0);
            lblModel.Name = "lblModel";
            lblModel.Size = new Size(36, 36);
            lblModel.TabIndex = 3;
            lblModel.Text = "模型:";
            lblModel.TextAlign = ContentAlignment.MiddleRight;
            //
            // btnDefaultModel
            //
            btnDefaultModel.FlatAppearance.BorderColor = Color.FromArgb(200, 200, 200);
            btnDefaultModel.FlatStyle = FlatStyle.Flat;
            btnDefaultModel.Font = new Font("Microsoft YaHei UI", 9F);
            btnDefaultModel.Location = new Point(316, 3);
            btnDefaultModel.Name = "btnDefaultModel";
            btnDefaultModel.Size = new Size(174, 30);
            btnDefaultModel.TabIndex = 4;
            btnDefaultModel.Text = "(未选择)";
            btnDefaultModel.Click += btnDefaultModel_Click;
            //
            // lblThreshold
            //
            lblThreshold.Dock = DockStyle.Fill;
            lblThreshold.Font = new Font("Microsoft YaHei UI", 9F);
            lblThreshold.Location = new Point(496, 0);
            lblThreshold.Name = "lblThreshold";
            lblThreshold.Size = new Size(69, 36);
            lblThreshold.TabIndex = 5;
            lblThreshold.Text = "压缩阈值:";
            lblThreshold.TextAlign = ContentAlignment.MiddleRight;
            //
            // numCompactThreshold
            //
            numCompactThreshold.Dock = DockStyle.Bottom;
            numCompactThreshold.Font = new Font("Microsoft YaHei UI", 9F);
            numCompactThreshold.Location = new Point(571, 10);
            numCompactThreshold.Maximum = new decimal(new int[] { 99, 0, 0, 0 });
            numCompactThreshold.Minimum = new decimal(new int[] { 10, 0, 0, 0 });
            numCompactThreshold.Name = "numCompactThreshold";
            numCompactThreshold.Size = new Size(49, 23);
            numCompactThreshold.TabIndex = 6;
            numCompactThreshold.TextAlign = HorizontalAlignment.Center;
            numCompactThreshold.Value = new decimal(new int[] { 85, 0, 0, 0 });
            numCompactThreshold.ValueChanged += numCompactThreshold_ValueChanged;
            //
            // lblPercent
            //
            lblPercent.Dock = DockStyle.Fill;
            lblPercent.Font = new Font("Microsoft YaHei UI", 9F);
            lblPercent.Location = new Point(626, 0);
            lblPercent.Name = "lblPercent";
            lblPercent.Size = new Size(14, 36);
            lblPercent.TabIndex = 7;
            lblPercent.Text = "%";
            lblPercent.TextAlign = ContentAlignment.MiddleLeft;
            //
            // btnGuide
            //
            btnGuide.FlatAppearance.BorderColor = Color.FromArgb(200, 200, 200);
            btnGuide.FlatStyle = FlatStyle.Flat;
            btnGuide.Font = new Font("Microsoft YaHei UI", 9F);
            btnGuide.Location = new Point(646, 3);
            btnGuide.Name = "btnGuide";
            btnGuide.Size = new Size(120, 30);
            btnGuide.TabIndex = 8;
            btnGuide.Text = "📋 配置指南";
            btnGuide.Click += btnGuide_Click;
            //
            // btnToggleProxy
            //
            btnToggleProxy.BackColor = Color.FromArgb(0, 120, 215);
            btnToggleProxy.FlatAppearance.BorderSize = 0;
            btnToggleProxy.FlatStyle = FlatStyle.Flat;
            btnToggleProxy.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            btnToggleProxy.ForeColor = Color.White;
            btnToggleProxy.Location = new Point(776, 3);
            btnToggleProxy.Name = "btnToggleProxy";
            btnToggleProxy.Size = new Size(120, 30);
            btnToggleProxy.TabIndex = 9;
            btnToggleProxy.Text = "启动代理";
            btnToggleProxy.UseVisualStyleBackColor = false;
            btnToggleProxy.Click += btnToggleProxy_Click;
            //
            // MainControl
            //
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(tlpRoot);
            Font = new Font("Microsoft YaHei UI", 9F);
            MinimumSize = new Size(640, 420);
            Name = "MainControl";
            Size = new Size(933, 643);
            tlpRoot.ResumeLayout(false);
            topTlp.ResumeLayout(false);
            grpProviders.ResumeLayout(false);
            provTlp.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)dgvProviders).EndInit();
            pnlProviderButtons.ResumeLayout(false);
            grpLog.ResumeLayout(false);
            pnlBottom.ResumeLayout(false);
            bottomTlp.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)numPort).EndInit();
            ((System.ComponentModel.ISupportInitialize)numCompactThreshold).EndInit();
            ResumeLayout(false);
        }

        private TableLayoutPanel topTlp;
        private TableLayoutPanel provTlp;
        private DataGridViewTextBoxColumn dataGridViewTextBoxColumn1;
        private DataGridViewTextBoxColumn dataGridViewTextBoxColumn2;
        private DataGridViewTextBoxColumn dataGridViewTextBoxColumn3;
        private DataGridViewTextBoxColumn dataGridViewTextBoxColumn4;
        private DataGridViewTextBoxColumn dataGridViewTextBoxColumn5;
        private TableLayoutPanel bottomTlp;
        private Label lblPort;
        private Label lblModel;
        private Label lblThreshold;
        private Label lblPercent;
    }
}
