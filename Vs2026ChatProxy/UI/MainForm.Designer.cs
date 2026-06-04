using System.Drawing;
using System.Windows.Forms;

namespace Vs2026ChatProxy.UI
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
            SuspendLayout();
            //
            // MainForm
            //
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(933, 643);
            Font = new Font("Microsoft YaHei UI", 9F);
            Icon = (Icon)resources.GetObject("$this.Icon");
            MinimumSize = new Size(640, 420);
            Name = "MainForm";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "VS2026 Chat Proxy（AI 多模型代理）";
            ResumeLayout(false);
        }
    }
}
