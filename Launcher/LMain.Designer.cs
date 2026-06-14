namespace Launcher
{
    partial class LMain
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
                components.Dispose();

            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(LMain));
            this.StartGameButton = new System.Windows.Forms.Button();
            this.TotalProgressBar = new System.Windows.Forms.ProgressBar();
            this.labelControl1 = new System.Windows.Forms.Label();
            this.StatusLabel = new System.Windows.Forms.Label();
            this.labelControl3 = new System.Windows.Forms.Label();
            this.DownloadSizeLabel = new System.Windows.Forms.Label();
            this.DownloadSpeedLabel = new System.Windows.Forms.Label();
            this.labelControl6 = new System.Windows.Forms.Label();
            this.pictureBox1 = new System.Windows.Forms.PictureBox();
            this.PatchNotesHyperlinkControl = new System.Windows.Forms.LinkLabel();
            this.RepairButton = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).BeginInit();
            this.SuspendLayout();
            // 
            // StartGameButton
            // 
            this.StartGameButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.StartGameButton.Location = new System.Drawing.Point(612, 354);
            this.StartGameButton.Name = "StartGameButton";
            this.StartGameButton.Size = new System.Drawing.Size(100, 46);
            this.StartGameButton.TabIndex = 0;
            this.StartGameButton.Text = "启动游戏";
            this.StartGameButton.UseVisualStyleBackColor = true;
            this.StartGameButton.Click += new System.EventHandler(this.StartGameButton_Click);
            // 
            // TotalProgressBar
            // 
            this.TotalProgressBar.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) | System.Windows.Forms.AnchorStyles.Right)));
            this.TotalProgressBar.Location = new System.Drawing.Point(12, 382);
            this.TotalProgressBar.Name = "TotalProgressBar";
            this.TotalProgressBar.Size = new System.Drawing.Size(594, 18);
            this.TotalProgressBar.TabIndex = 1;
            // 
            // labelControl1
            // 
            this.labelControl1.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.labelControl1.AutoSize = true;
            this.labelControl1.Location = new System.Drawing.Point(28, 344);
            this.labelControl1.Name = "labelControl1";
            this.labelControl1.Size = new System.Drawing.Size(31, 13);
            this.labelControl1.TabIndex = 2;
            this.labelControl1.Text = "状态:";
            // 
            // StatusLabel
            // 
            this.StatusLabel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.StatusLabel.AutoSize = true;
            this.StatusLabel.Location = new System.Drawing.Point(69, 344);
            this.StatusLabel.Name = "StatusLabel";
            this.StatusLabel.Size = new System.Drawing.Size(43, 13);
            this.StatusLabel.TabIndex = 3;
            this.StatusLabel.Text = "<None>";
            // 
            // labelControl3
            // 
            this.labelControl3.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.labelControl3.AutoSize = true;
            this.labelControl3.Location = new System.Drawing.Point(12, 363);
            this.labelControl3.Name = "labelControl3";
            this.labelControl3.Size = new System.Drawing.Size(31, 13);
            this.labelControl3.TabIndex = 4;
            this.labelControl3.Text = "下载:";
            // 
            // DownloadSizeLabel
            // 
            this.DownloadSizeLabel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.DownloadSizeLabel.AutoSize = true;
            this.DownloadSizeLabel.Location = new System.Drawing.Point(69, 363);
            this.DownloadSizeLabel.Name = "DownloadSizeLabel";
            this.DownloadSizeLabel.Size = new System.Drawing.Size(43, 13);
            this.DownloadSizeLabel.TabIndex = 5;
            this.DownloadSizeLabel.Text = "<None>";
            // 
            // DownloadSpeedLabel
            // 
            this.DownloadSpeedLabel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.DownloadSpeedLabel.AutoSize = true;
            this.DownloadSpeedLabel.Location = new System.Drawing.Point(550, 363);
            this.DownloadSpeedLabel.Name = "DownloadSpeedLabel";
            this.DownloadSpeedLabel.Size = new System.Drawing.Size(43, 13);
            this.DownloadSpeedLabel.TabIndex = 7;
            this.DownloadSpeedLabel.Text = "<None>";
            // 
            // labelControl6
            // 
            this.labelControl6.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.labelControl6.AutoSize = true;
            this.labelControl6.Location = new System.Drawing.Point(478, 363);
            this.labelControl6.Name = "labelControl6";
            this.labelControl6.Size = new System.Drawing.Size(55, 13);
            this.labelControl6.TabIndex = 6;
            this.labelControl6.Text = "下载速度:";
            // 
            // pictureBox1
            // 
            this.pictureBox1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) | System.Windows.Forms.AnchorStyles.Left) | System.Windows.Forms.AnchorStyles.Right)));
            this.pictureBox1.Image = global::Launcher.Properties.Resources.PatchHeader;
            this.pictureBox1.Location = new System.Drawing.Point(12, 12);
            this.pictureBox1.Name = "pictureBox1";
            this.pictureBox1.Size = new System.Drawing.Size(700, 307);
            this.pictureBox1.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.pictureBox1.TabIndex = 8;
            this.pictureBox1.TabStop = false;
            // 
            // PatchNotesHyperlinkControl
            // 
            this.PatchNotesHyperlinkControl.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.PatchNotesHyperlinkControl.AutoSize = true;
            this.PatchNotesHyperlinkControl.Location = new System.Drawing.Point(512, 328);
            this.PatchNotesHyperlinkControl.Name = "PatchNotesHyperlinkControl";
            this.PatchNotesHyperlinkControl.Size = new System.Drawing.Size(94, 13);
            this.PatchNotesHyperlinkControl.TabIndex = 9;
            this.PatchNotesHyperlinkControl.TabStop = true;
            this.PatchNotesHyperlinkControl.Text = "最新补丁说明";
            this.PatchNotesHyperlinkControl.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.PatchNotesHyperlinkControl_LinkClicked);
            // 
            // RepairButton
            // 
            this.RepairButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.RepairButton.Location = new System.Drawing.Point(612, 325);
            this.RepairButton.Name = "RepairButton";
            this.RepairButton.Size = new System.Drawing.Size(100, 23);
            this.RepairButton.TabIndex = 15;
            this.RepairButton.Text = "修复";
            this.RepairButton.UseVisualStyleBackColor = true;
            this.RepairButton.Click += new System.EventHandler(this.RepairButton_Click);
            // 
            // LMain
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(724, 412);
            this.Controls.Add(this.RepairButton);
            this.Controls.Add(this.PatchNotesHyperlinkControl);
            this.Controls.Add(this.pictureBox1);
            this.Controls.Add(this.DownloadSpeedLabel);
            this.Controls.Add(this.labelControl6);
            this.Controls.Add(this.DownloadSizeLabel);
            this.Controls.Add(this.labelControl3);
            this.Controls.Add(this.StatusLabel);
            this.Controls.Add(this.labelControl1);
            this.Controls.Add(this.TotalProgressBar);
            this.Controls.Add(this.StartGameButton);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Name = "LMain";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Zircon 启动器";
            this.Load += new System.EventHandler(this.LMain_Load);
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private System.Windows.Forms.ProgressBar TotalProgressBar;
        private System.Windows.Forms.Button StartGameButton;
        private System.Windows.Forms.Label labelControl1;
        private System.Windows.Forms.Label StatusLabel;
        private System.Windows.Forms.Label labelControl3;
        private System.Windows.Forms.Label DownloadSizeLabel;
        private System.Windows.Forms.Label DownloadSpeedLabel;
        private System.Windows.Forms.Label labelControl6;
        private System.Windows.Forms.PictureBox pictureBox1;
        private System.Windows.Forms.LinkLabel PatchNotesHyperlinkControl;
        private System.Windows.Forms.Button RepairButton;
    }
}
