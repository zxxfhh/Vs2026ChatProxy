using System.Drawing;
using System.Windows.Forms;

namespace Vs2026ChatProxy.UI
{
    partial class ProviderEditDialog
    {
        private System.ComponentModel.IContainer components = null;

        private Label lblType;
        private ComboBox cmbType;
        private Label lblName;
        private TextBox txtName;
        private Label lblBaseUrl;
        private TextBox txtBaseUrl;
        private Label lblApiKey;
        private TextBox txtApiKey;
        private CheckBox chkShowKey;
        private GroupBox grpModels;
        private DataGridView dgvModels;
        private Button btnAddModel;
        private Button btnDelModel;
        private Button btnFetchModels;
        private Button btnSave;
        private Button btnCancel;
        private TableLayoutPanel tlpMain;

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ProviderEditDialog));
            this.tlpMain = new System.Windows.Forms.TableLayoutPanel();
            this.lblType = new System.Windows.Forms.Label();
            this.cmbType = new System.Windows.Forms.ComboBox();
            this.lblName = new System.Windows.Forms.Label();
            this.txtName = new System.Windows.Forms.TextBox();
            this.lblBaseUrl = new System.Windows.Forms.Label();
            this.txtBaseUrl = new System.Windows.Forms.TextBox();
            this.lblApiKey = new System.Windows.Forms.Label();
            this.apiKeyRow = new System.Windows.Forms.TableLayoutPanel();
            this.txtApiKey = new System.Windows.Forms.TextBox();
            this.chkShowKey = new System.Windows.Forms.CheckBox();
            this.grpModels = new System.Windows.Forms.GroupBox();
            this.mdlTlp = new System.Windows.Forms.TableLayoutPanel();
            this.dgvModels = new System.Windows.Forms.DataGridView();
            this.dataGridViewCheckBoxColumn1 = new System.Windows.Forms.DataGridViewCheckBoxColumn();
            this.dataGridViewTextBoxColumn1 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.dataGridViewComboBoxColumn1 = new System.Windows.Forms.DataGridViewComboBoxColumn();
            this.dataGridViewCheckBoxColumn2 = new System.Windows.Forms.DataGridViewCheckBoxColumn();
            this.btnRow = new System.Windows.Forms.FlowLayoutPanel();
            this.btnAddModel = new System.Windows.Forms.Button();
            this.btnDelModel = new System.Windows.Forms.Button();
            this.btnFetchModels = new System.Windows.Forms.Button();
            this.chkToggleAll = new System.Windows.Forms.CheckBox();
            this.btnPanel = new System.Windows.Forms.FlowLayoutPanel();
            this.btnCancel = new System.Windows.Forms.Button();
            this.btnSave = new System.Windows.Forms.Button();
            this.tlpMain.SuspendLayout();
            this.apiKeyRow.SuspendLayout();
            this.grpModels.SuspendLayout();
            this.mdlTlp.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dgvModels)).BeginInit();
            this.btnRow.SuspendLayout();
            this.btnPanel.SuspendLayout();
            this.SuspendLayout();
            // 
            // tlpMain
            // 
            this.tlpMain.ColumnCount = 2;
            this.tlpMain.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 77F));
            this.tlpMain.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tlpMain.Controls.Add(this.lblType, 0, 0);
            this.tlpMain.Controls.Add(this.cmbType, 1, 0);
            this.tlpMain.Controls.Add(this.lblName, 0, 1);
            this.tlpMain.Controls.Add(this.txtName, 1, 1);
            this.tlpMain.Controls.Add(this.lblBaseUrl, 0, 2);
            this.tlpMain.Controls.Add(this.txtBaseUrl, 1, 2);
            this.tlpMain.Controls.Add(this.lblApiKey, 0, 3);
            this.tlpMain.Controls.Add(this.apiKeyRow, 1, 3);
            this.tlpMain.Controls.Add(this.grpModels, 0, 4);
            this.tlpMain.Controls.Add(this.btnPanel, 0, 5);
            this.tlpMain.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tlpMain.Location = new System.Drawing.Point(0, 0);
            this.tlpMain.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.tlpMain.Name = "tlpMain";
            this.tlpMain.Padding = new System.Windows.Forms.Padding(10, 8, 10, 8);
            this.tlpMain.RowCount = 6;
            this.tlpMain.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 25F));
            this.tlpMain.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 25F));
            this.tlpMain.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 25F));
            this.tlpMain.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 25F));
            this.tlpMain.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tlpMain.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 44F));
            this.tlpMain.Size = new System.Drawing.Size(721, 534);
            this.tlpMain.TabIndex = 0;
            // 
            // lblType
            // 
            this.lblType.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblType.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this.lblType.Location = new System.Drawing.Point(13, 8);
            this.lblType.Name = "lblType";
            this.lblType.Size = new System.Drawing.Size(71, 25);
            this.lblType.TabIndex = 0;
            this.lblType.Text = "类型:";
            this.lblType.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // cmbType
            // 
            this.cmbType.Dock = System.Windows.Forms.DockStyle.Fill;
            this.cmbType.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbType.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this.cmbType.Items.AddRange(new object[] {
            "anthropic",
            "gemini",
            "ollama",
            "deepseek",
            "openai"});
            this.cmbType.Location = new System.Drawing.Point(90, 10);
            this.cmbType.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.cmbType.Name = "cmbType";
            this.cmbType.Size = new System.Drawing.Size(618, 25);
            this.cmbType.TabIndex = 1;
            this.cmbType.SelectedIndexChanged += new System.EventHandler(this.cmbType_SelectedIndexChanged);
            // 
            // lblName
            // 
            this.lblName.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblName.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this.lblName.Location = new System.Drawing.Point(13, 33);
            this.lblName.Name = "lblName";
            this.lblName.Size = new System.Drawing.Size(71, 25);
            this.lblName.TabIndex = 2;
            this.lblName.Text = "名称:";
            this.lblName.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // txtName
            // 
            this.txtName.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtName.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this.txtName.Location = new System.Drawing.Point(90, 35);
            this.txtName.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.txtName.Name = "txtName";
            this.txtName.Size = new System.Drawing.Size(618, 23);
            this.txtName.TabIndex = 3;
            // 
            // lblBaseUrl
            // 
            this.lblBaseUrl.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblBaseUrl.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this.lblBaseUrl.Location = new System.Drawing.Point(13, 58);
            this.lblBaseUrl.Name = "lblBaseUrl";
            this.lblBaseUrl.Size = new System.Drawing.Size(71, 25);
            this.lblBaseUrl.TabIndex = 4;
            this.lblBaseUrl.Text = "Base URL:";
            this.lblBaseUrl.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // txtBaseUrl
            // 
            this.txtBaseUrl.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtBaseUrl.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this.txtBaseUrl.Location = new System.Drawing.Point(90, 60);
            this.txtBaseUrl.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.txtBaseUrl.Name = "txtBaseUrl";
            this.txtBaseUrl.Size = new System.Drawing.Size(618, 23);
            this.txtBaseUrl.TabIndex = 5;
            // 
            // lblApiKey
            // 
            this.lblApiKey.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblApiKey.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this.lblApiKey.Location = new System.Drawing.Point(13, 83);
            this.lblApiKey.Name = "lblApiKey";
            this.lblApiKey.Size = new System.Drawing.Size(71, 25);
            this.lblApiKey.TabIndex = 6;
            this.lblApiKey.Text = "API Key:";
            this.lblApiKey.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // apiKeyRow
            // 
            this.apiKeyRow.ColumnCount = 2;
            this.apiKeyRow.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.apiKeyRow.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 69F));
            this.apiKeyRow.Controls.Add(this.txtApiKey, 0, 0);
            this.apiKeyRow.Controls.Add(this.chkShowKey, 1, 0);
            this.apiKeyRow.Dock = System.Windows.Forms.DockStyle.Fill;
            this.apiKeyRow.Location = new System.Drawing.Point(90, 85);
            this.apiKeyRow.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.apiKeyRow.Name = "apiKeyRow";
            this.apiKeyRow.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.apiKeyRow.Size = new System.Drawing.Size(618, 21);
            this.apiKeyRow.TabIndex = 7;
            // 
            // txtApiKey
            // 
            this.txtApiKey.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtApiKey.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this.txtApiKey.Location = new System.Drawing.Point(3, 2);
            this.txtApiKey.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.txtApiKey.Name = "txtApiKey";
            this.txtApiKey.Size = new System.Drawing.Size(543, 23);
            this.txtApiKey.TabIndex = 0;
            this.txtApiKey.UseSystemPasswordChar = true;
            // 
            // chkShowKey
            // 
            this.chkShowKey.Dock = System.Windows.Forms.DockStyle.Fill;
            this.chkShowKey.Font = new System.Drawing.Font("Microsoft YaHei UI", 8.5F);
            this.chkShowKey.Location = new System.Drawing.Point(552, 2);
            this.chkShowKey.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.chkShowKey.Name = "chkShowKey";
            this.chkShowKey.Size = new System.Drawing.Size(63, 17);
            this.chkShowKey.TabIndex = 1;
            this.chkShowKey.Text = "显示";
            this.chkShowKey.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.chkShowKey.CheckedChanged += new System.EventHandler(this.chkShowKey_CheckedChanged);
            // 
            // grpModels
            // 
            this.tlpMain.SetColumnSpan(this.grpModels, 2);
            this.grpModels.Controls.Add(this.mdlTlp);
            this.grpModels.Dock = System.Windows.Forms.DockStyle.Fill;
            this.grpModels.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this.grpModels.Location = new System.Drawing.Point(13, 110);
            this.grpModels.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.grpModels.Name = "grpModels";
            this.grpModels.Padding = new System.Windows.Forms.Padding(5, 4, 5, 4);
            this.grpModels.Size = new System.Drawing.Size(695, 370);
            this.grpModels.TabIndex = 8;
            this.grpModels.TabStop = false;
            this.grpModels.Text = "模型列表";
            // 
            // mdlTlp
            // 
            this.mdlTlp.ColumnCount = 1;
            this.mdlTlp.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 541F));
            this.mdlTlp.Controls.Add(this.dgvModels, 0, 0);
            this.mdlTlp.Controls.Add(this.btnRow, 0, 1);
            this.mdlTlp.Dock = System.Windows.Forms.DockStyle.Fill;
            this.mdlTlp.Location = new System.Drawing.Point(5, 20);
            this.mdlTlp.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.mdlTlp.Name = "mdlTlp";
            this.mdlTlp.RowCount = 2;
            this.mdlTlp.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.mdlTlp.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 39F));
            this.mdlTlp.Size = new System.Drawing.Size(685, 346);
            this.mdlTlp.TabIndex = 0;
            // 
            // dgvModels
            // 
            this.dgvModels.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this.dgvModels.BackgroundColor = System.Drawing.Color.White;
            this.dgvModels.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.dataGridViewCheckBoxColumn1,
            this.dataGridViewTextBoxColumn1,
            this.dataGridViewComboBoxColumn1,
            this.dataGridViewCheckBoxColumn2});
            this.dgvModels.Dock = System.Windows.Forms.DockStyle.Fill;
            this.dgvModels.Location = new System.Drawing.Point(3, 2);
            this.dgvModels.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.dgvModels.Name = "dgvModels";
            this.dgvModels.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.dgvModels.Size = new System.Drawing.Size(679, 303);
            this.dgvModels.TabIndex = 0;
            // 
            // dataGridViewCheckBoxColumn1
            // 
            this.dataGridViewCheckBoxColumn1.FalseValue = false;
            this.dataGridViewCheckBoxColumn1.FillWeight = 40F;
            this.dataGridViewCheckBoxColumn1.HeaderText = "启用";
            this.dataGridViewCheckBoxColumn1.Name = "dataGridViewCheckBoxColumn1";
            this.dataGridViewCheckBoxColumn1.TrueValue = true;
            // 
            // dataGridViewTextBoxColumn1
            // 
            this.dataGridViewTextBoxColumn1.HeaderText = "模型 ID";
            this.dataGridViewTextBoxColumn1.Name = "dataGridViewTextBoxColumn1";
            // 
            // dataGridViewComboBoxColumn1
            // 
            this.dataGridViewComboBoxColumn1.FillWeight = 60F;
            this.dataGridViewComboBoxColumn1.HeaderText = "上下文标签";
            this.dataGridViewComboBoxColumn1.Items.AddRange(new object[] {
            "",
            "[2M]",
            "[1M]",
            "[512K]",
            "[256K]",
            "[200K]",
            "[128K]"});
            this.dataGridViewComboBoxColumn1.Name = "dataGridViewComboBoxColumn1";
            // 
            // dataGridViewCheckBoxColumn2
            // 
            this.dataGridViewCheckBoxColumn2.FalseValue = false;
            this.dataGridViewCheckBoxColumn2.FillWeight = 30F;
            this.dataGridViewCheckBoxColumn2.HeaderText = "追加";
            this.dataGridViewCheckBoxColumn2.Name = "dataGridViewCheckBoxColumn2";
            this.dataGridViewCheckBoxColumn2.TrueValue = true;
            // 
            // btnRow
            // 
            this.btnRow.Controls.Add(this.btnAddModel);
            this.btnRow.Controls.Add(this.btnDelModel);
            this.btnRow.Controls.Add(this.btnFetchModels);
            this.btnRow.Controls.Add(this.chkToggleAll);
            this.btnRow.Dock = System.Windows.Forms.DockStyle.Fill;
            this.btnRow.Location = new System.Drawing.Point(3, 309);
            this.btnRow.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.btnRow.Name = "btnRow";
            this.btnRow.Size = new System.Drawing.Size(679, 35);
            this.btnRow.TabIndex = 1;
            // 
            // btnAddModel
            // 
            this.btnAddModel.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this.btnAddModel.Location = new System.Drawing.Point(3, 2);
            this.btnAddModel.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.btnAddModel.Name = "btnAddModel";
            this.btnAddModel.Size = new System.Drawing.Size(86, 28);
            this.btnAddModel.TabIndex = 0;
            this.btnAddModel.Text = "添加模型";
            this.btnAddModel.Click += new System.EventHandler(this.btnAddModel_Click);
            // 
            // btnDelModel
            // 
            this.btnDelModel.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this.btnDelModel.Location = new System.Drawing.Point(95, 2);
            this.btnDelModel.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.btnDelModel.Name = "btnDelModel";
            this.btnDelModel.Size = new System.Drawing.Size(86, 28);
            this.btnDelModel.TabIndex = 1;
            this.btnDelModel.Text = "删除选中";
            this.btnDelModel.Click += new System.EventHandler(this.btnDelModel_Click);
            // 
            // btnFetchModels
            // 
            this.btnFetchModels.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(120)))), ((int)(((byte)(215)))));
            this.btnFetchModels.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnFetchModels.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnFetchModels.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this.btnFetchModels.ForeColor = System.Drawing.Color.White;
            this.btnFetchModels.Location = new System.Drawing.Point(187, 2);
            this.btnFetchModels.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.btnFetchModels.Name = "btnFetchModels";
            this.btnFetchModels.Size = new System.Drawing.Size(103, 28);
            this.btnFetchModels.TabIndex = 2;
            this.btnFetchModels.Text = "🔄 获取模型";
            this.btnFetchModels.UseVisualStyleBackColor = false;
            this.btnFetchModels.Click += new System.EventHandler(this.btnFetchModels_Click);
            // 
            // chkToggleAll
            // 
            this.chkToggleAll.CheckAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.chkToggleAll.Checked = true;
            this.chkToggleAll.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkToggleAll.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this.chkToggleAll.Location = new System.Drawing.Point(296, 2);
            this.chkToggleAll.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.chkToggleAll.Name = "chkToggleAll";
            this.chkToggleAll.Size = new System.Drawing.Size(75, 28);
            this.chkToggleAll.TabIndex = 3;
            this.chkToggleAll.Text = "全选";
            this.chkToggleAll.CheckedChanged += new System.EventHandler(this.chkToggleAll_CheckedChanged);
            // 
            // btnPanel
            // 
            this.tlpMain.SetColumnSpan(this.btnPanel, 2);
            this.btnPanel.Controls.Add(this.btnCancel);
            this.btnPanel.Controls.Add(this.btnSave);
            this.btnPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.btnPanel.FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft;
            this.btnPanel.Location = new System.Drawing.Point(13, 484);
            this.btnPanel.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.btnPanel.Name = "btnPanel";
            this.btnPanel.Size = new System.Drawing.Size(695, 40);
            this.btnPanel.TabIndex = 9;
            // 
            // btnCancel
            // 
            this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btnCancel.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this.btnCancel.Location = new System.Drawing.Point(606, 2);
            this.btnCancel.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(86, 33);
            this.btnCancel.TabIndex = 0;
            this.btnCancel.Text = "取消";
            // 
            // btnSave
            // 
            this.btnSave.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(120)))), ((int)(((byte)(215)))));
            this.btnSave.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnSave.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F, System.Drawing.FontStyle.Bold);
            this.btnSave.ForeColor = System.Drawing.Color.White;
            this.btnSave.Location = new System.Drawing.Point(514, 2);
            this.btnSave.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.btnSave.Name = "btnSave";
            this.btnSave.Size = new System.Drawing.Size(86, 33);
            this.btnSave.TabIndex = 1;
            this.btnSave.Text = "保存";
            this.btnSave.UseVisualStyleBackColor = false;
            this.btnSave.Click += new System.EventHandler(this.btnSave_Click);
            // 
            // ProviderEditDialog
            // 
            this.AcceptButton = this.btnSave;
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.btnCancel;
            this.ClientSize = new System.Drawing.Size(721, 534);
            this.Controls.Add(this.tlpMain);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "ProviderEditDialog";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "供应商";
            this.tlpMain.ResumeLayout(false);
            this.tlpMain.PerformLayout();
            this.apiKeyRow.ResumeLayout(false);
            this.apiKeyRow.PerformLayout();
            this.grpModels.ResumeLayout(false);
            this.mdlTlp.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.dgvModels)).EndInit();
            this.btnRow.ResumeLayout(false);
            this.btnPanel.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        private TableLayoutPanel apiKeyRow;
        private TableLayoutPanel mdlTlp;
        private DataGridViewCheckBoxColumn dataGridViewCheckBoxColumn1;
        private DataGridViewTextBoxColumn dataGridViewTextBoxColumn1;
        private DataGridViewComboBoxColumn dataGridViewComboBoxColumn1;
        private DataGridViewCheckBoxColumn dataGridViewCheckBoxColumn2;
        private FlowLayoutPanel btnRow;
        private CheckBox chkToggleAll;
        private FlowLayoutPanel btnPanel;
    }
}
