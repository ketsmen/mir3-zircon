namespace ImageManager
{
    partial class IMain
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
            this.SelectedFolderButtonEdit = new System.Windows.Forms.TextBox();
            this.SelectedFolderButton = new System.Windows.Forms.Button();
            this.labelControl1 = new System.Windows.Forms.Label();
            this.SubFoldersCheckEdit = new System.Windows.Forms.CheckBox();
            this.labelControl2 = new System.Windows.Forms.Label();
            this.ConvertButton = new System.Windows.Forms.Button();
            this.progressBarControl1 = new System.Windows.Forms.ProgressBar();
            this.labelControl3 = new System.Windows.Forms.Label();
            this.ProgressLabel = new System.Windows.Forms.Label();
            this.CreaetLibrariesButton = new System.Windows.Forms.Button();
            this.FolderDialog = new System.Windows.Forms.FolderBrowserDialog();
            this.simpleButton1 = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // SelectedFolderButtonEdit
            // 
            this.SelectedFolderButtonEdit.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) | System.Windows.Forms.AnchorStyles.Right)));
            this.SelectedFolderButtonEdit.Location = new System.Drawing.Point(96, 18);
            this.SelectedFolderButtonEdit.Name = "SelectedFolderButtonEdit";
            this.SelectedFolderButtonEdit.Size = new System.Drawing.Size(394, 20);
            this.SelectedFolderButtonEdit.TabIndex = 1;
            // 
            // SelectedFolderButton
            // 
            this.SelectedFolderButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.SelectedFolderButton.Location = new System.Drawing.Point(496, 16);
            this.SelectedFolderButton.Name = "SelectedFolderButton";
            this.SelectedFolderButton.Size = new System.Drawing.Size(36, 23);
            this.SelectedFolderButton.TabIndex = 2;
            this.SelectedFolderButton.Text = "...";
            this.SelectedFolderButton.UseVisualStyleBackColor = true;
            this.SelectedFolderButton.Click += new System.EventHandler(this.SelectedFolderButtonEdit_ButtonClick);
            // 
            // labelControl1
            // 
            this.labelControl1.AutoSize = true;
            this.labelControl1.Location = new System.Drawing.Point(12, 21);
            this.labelControl1.Name = "labelControl1";
            this.labelControl1.Size = new System.Drawing.Size(31, 13);
            this.labelControl1.TabIndex = 0;
            this.labelControl1.Text = "目录:";
            // 
            // SubFoldersCheckEdit
            // 
            this.SubFoldersCheckEdit.AutoSize = true;
            this.SubFoldersCheckEdit.Location = new System.Drawing.Point(96, 50);
            this.SubFoldersCheckEdit.Name = "SubFoldersCheckEdit";
            this.SubFoldersCheckEdit.Size = new System.Drawing.Size(74, 17);
            this.SubFoldersCheckEdit.TabIndex = 4;
            this.SubFoldersCheckEdit.Text = "包含子目录";
            this.SubFoldersCheckEdit.UseVisualStyleBackColor = true;
            // 
            // labelControl2
            // 
            this.labelControl2.AutoSize = true;
            this.labelControl2.Location = new System.Drawing.Point(12, 51);
            this.labelControl2.Name = "labelControl2";
            this.labelControl2.Size = new System.Drawing.Size(31, 13);
            this.labelControl2.TabIndex = 3;
            this.labelControl2.Text = "选项:";
            // 
            // ConvertButton
            // 
            this.ConvertButton.Location = new System.Drawing.Point(96, 84);
            this.ConvertButton.Name = "ConvertButton";
            this.ConvertButton.Size = new System.Drawing.Size(130, 28);
            this.ConvertButton.TabIndex = 5;
            this.ConvertButton.Text = "转换 WTL";
            this.ConvertButton.UseVisualStyleBackColor = true;
            this.ConvertButton.Click += new System.EventHandler(this.ConvertLibrariesButton_Click);
            // 
            // progressBarControl1
            // 
            this.progressBarControl1.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) | System.Windows.Forms.AnchorStyles.Right)));
            this.progressBarControl1.Location = new System.Drawing.Point(96, 158);
            this.progressBarControl1.Name = "progressBarControl1";
            this.progressBarControl1.Size = new System.Drawing.Size(436, 18);
            this.progressBarControl1.TabIndex = 9;
            // 
            // labelControl3
            // 
            this.labelControl3.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.labelControl3.AutoSize = true;
            this.labelControl3.Location = new System.Drawing.Point(12, 161);
            this.labelControl3.Name = "labelControl3";
            this.labelControl3.Size = new System.Drawing.Size(31, 13);
            this.labelControl3.TabIndex = 8;
            this.labelControl3.Text = "进度:";
            // 
            // ProgressLabel
            // 
            this.ProgressLabel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.ProgressLabel.AutoSize = true;
            this.ProgressLabel.Location = new System.Drawing.Point(96, 137);
            this.ProgressLabel.Name = "ProgressLabel";
            this.ProgressLabel.Size = new System.Drawing.Size(43, 13);
            this.ProgressLabel.TabIndex = 7;
            this.ProgressLabel.Text = "<None>";
            // 
            // CreaetLibrariesButton
            // 
            this.CreaetLibrariesButton.Location = new System.Drawing.Point(232, 84);
            this.CreaetLibrariesButton.Name = "CreaetLibrariesButton";
            this.CreaetLibrariesButton.Size = new System.Drawing.Size(130, 28);
            this.CreaetLibrariesButton.TabIndex = 6;
            this.CreaetLibrariesButton.Text = "创建图库";
            this.CreaetLibrariesButton.UseVisualStyleBackColor = true;
            this.CreaetLibrariesButton.Click += new System.EventHandler(this.CreaetLibrariesButton_Click);
            // 
            // simpleButton1
            // 
            this.simpleButton1.Location = new System.Drawing.Point(368, 84);
            this.simpleButton1.Name = "simpleButton1";
            this.simpleButton1.Size = new System.Drawing.Size(130, 28);
            this.simpleButton1.TabIndex = 10;
            this.simpleButton1.Text = "缩放小地图";
            this.simpleButton1.UseVisualStyleBackColor = true;
            this.simpleButton1.Click += new System.EventHandler(this.simpleButton1_Click);
            // 
            // IMain
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(544, 190);
            this.Controls.Add(this.simpleButton1);
            this.Controls.Add(this.CreaetLibrariesButton);
            this.Controls.Add(this.ProgressLabel);
            this.Controls.Add(this.labelControl3);
            this.Controls.Add(this.progressBarControl1);
            this.Controls.Add(this.ConvertButton);
            this.Controls.Add(this.labelControl2);
            this.Controls.Add(this.SubFoldersCheckEdit);
            this.Controls.Add(this.labelControl1);
            this.Controls.Add(this.SelectedFolderButton);
            this.Controls.Add(this.SelectedFolderButtonEdit);
            this.Name = "IMain";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "图片管理器";
            this.Load += new System.EventHandler(this.IMain_Load);
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private System.Windows.Forms.TextBox SelectedFolderButtonEdit;
        private System.Windows.Forms.Button SelectedFolderButton;
        private System.Windows.Forms.Label labelControl1;
        private System.Windows.Forms.CheckBox SubFoldersCheckEdit;
        private System.Windows.Forms.Label labelControl2;
        private System.Windows.Forms.Button ConvertButton;
        private System.Windows.Forms.ProgressBar progressBarControl1;
        private System.Windows.Forms.Label labelControl3;
        private System.Windows.Forms.Label ProgressLabel;
        private System.Windows.Forms.Button CreaetLibrariesButton;
        private System.Windows.Forms.FolderBrowserDialog FolderDialog;
        private System.Windows.Forms.Button simpleButton1;
    }
}
