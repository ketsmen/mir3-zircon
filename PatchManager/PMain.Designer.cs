namespace PatchManager
{
    partial class PMain
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
            this.labelControl1 = new System.Windows.Forms.Label();
            this.CleanClientButtonEdit = new System.Windows.Forms.TextBox();
            this.CleanClientBrowseButton = new System.Windows.Forms.Button();
            this.labelControl2 = new System.Windows.Forms.Label();
            this.UsernameTextEdit = new System.Windows.Forms.TextBox();
            this.UseLoginCheckEdit = new System.Windows.Forms.CheckBox();
            this.labelControl3 = new System.Windows.Forms.Label();
            this.labelControl4 = new System.Windows.Forms.Label();
            this.labelControl5 = new System.Windows.Forms.Label();
            this.HostTextEdit = new System.Windows.Forms.TextBox();
            this.PasswordTextEdit = new System.Windows.Forms.TextBox();
            this.UploadPatchButton = new System.Windows.Forms.Button();
            this.labelControl6 = new System.Windows.Forms.Label();
            this.StatusLabel = new System.Windows.Forms.Label();
            this.labelControl8 = new System.Windows.Forms.Label();
            this.UploadSizeLabel = new System.Windows.Forms.Label();
            this.TotalProgressBar = new System.Windows.Forms.ProgressBar();
            this.UploadSpeedLabel = new System.Windows.Forms.Label();
            this.labelControl10 = new System.Windows.Forms.Label();
            this.ProtocolDropDown = new System.Windows.Forms.ComboBox();
            this.labelControl7 = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // labelControl1
            // 
            this.labelControl1.AutoSize = true;
            this.labelControl1.Location = new System.Drawing.Point(12, 18);
            this.labelControl1.Name = "labelControl1";
            this.labelControl1.Size = new System.Drawing.Size(79, 13);
            this.labelControl1.TabIndex = 0;
            this.labelControl1.Text = "干净客户端:";
            // 
            // CleanClientButtonEdit
            // 
            this.CleanClientButtonEdit.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) | System.Windows.Forms.AnchorStyles.Right)));
            this.CleanClientButtonEdit.Location = new System.Drawing.Point(112, 15);
            this.CleanClientButtonEdit.Name = "CleanClientButtonEdit";
            this.CleanClientButtonEdit.Size = new System.Drawing.Size(350, 20);
            this.CleanClientButtonEdit.TabIndex = 1;
            this.CleanClientButtonEdit.TextChanged += new System.EventHandler(this.CleanClientButtonEdit_EditValueChanged);
            // 
            // CleanClientBrowseButton
            // 
            this.CleanClientBrowseButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.CleanClientBrowseButton.Location = new System.Drawing.Point(468, 13);
            this.CleanClientBrowseButton.Name = "CleanClientBrowseButton";
            this.CleanClientBrowseButton.Size = new System.Drawing.Size(36, 23);
            this.CleanClientBrowseButton.TabIndex = 2;
            this.CleanClientBrowseButton.Text = "...";
            this.CleanClientBrowseButton.UseVisualStyleBackColor = true;
            this.CleanClientBrowseButton.Click += new System.EventHandler(this.CleanClientBrowseButton_Click);
            // 
            // labelControl2
            // 
            this.labelControl2.AutoSize = true;
            this.labelControl2.Location = new System.Drawing.Point(12, 47);
            this.labelControl2.Name = "labelControl2";
            this.labelControl2.Size = new System.Drawing.Size(37, 13);
            this.labelControl2.TabIndex = 3;
            this.labelControl2.Text = "Host:";
            // 
            // UsernameTextEdit
            // 
            this.UsernameTextEdit.Location = new System.Drawing.Point(112, 101);
            this.UsernameTextEdit.Name = "UsernameTextEdit";
            this.UsernameTextEdit.Size = new System.Drawing.Size(186, 20);
            this.UsernameTextEdit.TabIndex = 9;
            this.UsernameTextEdit.TextChanged += new System.EventHandler(this.UsernameTextEdit_EditValueChanged);
            // 
            // UseLoginCheckEdit
            // 
            this.UseLoginCheckEdit.AutoSize = true;
            this.UseLoginCheckEdit.Location = new System.Drawing.Point(112, 75);
            this.UseLoginCheckEdit.Name = "UseLoginCheckEdit";
            this.UseLoginCheckEdit.Size = new System.Drawing.Size(74, 17);
            this.UseLoginCheckEdit.TabIndex = 7;
            this.UseLoginCheckEdit.Text = "使用登录";
            this.UseLoginCheckEdit.UseVisualStyleBackColor = true;
            this.UseLoginCheckEdit.CheckedChanged += new System.EventHandler(this.UseLoginCheckEdit_CheckedChanged);
            // 
            // labelControl3
            // 
            this.labelControl3.AutoSize = true;
            this.labelControl3.Location = new System.Drawing.Point(12, 76);
            this.labelControl3.Name = "labelControl3";
            this.labelControl3.Size = new System.Drawing.Size(55, 13);
            this.labelControl3.TabIndex = 6;
            this.labelControl3.Text = "登录验证:";
            // 
            // labelControl4
            // 
            this.labelControl4.AutoSize = true;
            this.labelControl4.Location = new System.Drawing.Point(12, 104);
            this.labelControl4.Name = "labelControl4";
            this.labelControl4.Size = new System.Drawing.Size(43, 13);
            this.labelControl4.TabIndex = 8;
            this.labelControl4.Text = "用户名:";
            // 
            // labelControl5
            // 
            this.labelControl5.AutoSize = true;
            this.labelControl5.Location = new System.Drawing.Point(12, 130);
            this.labelControl5.Name = "labelControl5";
            this.labelControl5.Size = new System.Drawing.Size(31, 13);
            this.labelControl5.TabIndex = 10;
            this.labelControl5.Text = "密码:";
            // 
            // HostTextEdit
            // 
            this.HostTextEdit.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) | System.Windows.Forms.AnchorStyles.Right)));
            this.HostTextEdit.Location = new System.Drawing.Point(112, 44);
            this.HostTextEdit.Name = "HostTextEdit";
            this.HostTextEdit.Size = new System.Drawing.Size(392, 20);
            this.HostTextEdit.TabIndex = 4;
            this.HostTextEdit.TextChanged += new System.EventHandler(this.HostTextEdit_EditValueChanged);
            // 
            // PasswordTextEdit
            // 
            this.PasswordTextEdit.Location = new System.Drawing.Point(112, 127);
            this.PasswordTextEdit.Name = "PasswordTextEdit";
            this.PasswordTextEdit.Size = new System.Drawing.Size(186, 20);
            this.PasswordTextEdit.TabIndex = 11;
            this.PasswordTextEdit.UseSystemPasswordChar = true;
            this.PasswordTextEdit.TextChanged += new System.EventHandler(this.PasswordTextEdit_EditValueChanged);
            // 
            // UploadPatchButton
            // 
            this.UploadPatchButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.UploadPatchButton.Location = new System.Drawing.Point(404, 210);
            this.UploadPatchButton.Name = "UploadPatchButton";
            this.UploadPatchButton.Size = new System.Drawing.Size(100, 28);
            this.UploadPatchButton.TabIndex = 20;
            this.UploadPatchButton.Text = "上传补丁";
            this.UploadPatchButton.UseVisualStyleBackColor = true;
            this.UploadPatchButton.Click += new System.EventHandler(this.UploadPatchButton_Click);
            // 
            // labelControl6
            // 
            this.labelControl6.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.labelControl6.AutoSize = true;
            this.labelControl6.Location = new System.Drawing.Point(12, 164);
            this.labelControl6.Name = "labelControl6";
            this.labelControl6.Size = new System.Drawing.Size(31, 13);
            this.labelControl6.TabIndex = 14;
            this.labelControl6.Text = "状态:";
            // 
            // StatusLabel
            // 
            this.StatusLabel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.StatusLabel.AutoSize = true;
            this.StatusLabel.Location = new System.Drawing.Point(112, 164);
            this.StatusLabel.Name = "StatusLabel";
            this.StatusLabel.Size = new System.Drawing.Size(43, 13);
            this.StatusLabel.TabIndex = 15;
            this.StatusLabel.Text = "<None>";
            // 
            // labelControl8
            // 
            this.labelControl8.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.labelControl8.AutoSize = true;
            this.labelControl8.Location = new System.Drawing.Point(12, 187);
            this.labelControl8.Name = "labelControl8";
            this.labelControl8.Size = new System.Drawing.Size(31, 13);
            this.labelControl8.TabIndex = 16;
            this.labelControl8.Text = "上传:";
            // 
            // UploadSizeLabel
            // 
            this.UploadSizeLabel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.UploadSizeLabel.AutoSize = true;
            this.UploadSizeLabel.Location = new System.Drawing.Point(112, 187);
            this.UploadSizeLabel.Name = "UploadSizeLabel";
            this.UploadSizeLabel.Size = new System.Drawing.Size(43, 13);
            this.UploadSizeLabel.TabIndex = 17;
            this.UploadSizeLabel.Text = "<None>";
            // 
            // TotalProgressBar
            // 
            this.TotalProgressBar.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) | System.Windows.Forms.AnchorStyles.Right)));
            this.TotalProgressBar.Location = new System.Drawing.Point(15, 215);
            this.TotalProgressBar.Name = "TotalProgressBar";
            this.TotalProgressBar.Size = new System.Drawing.Size(383, 18);
            this.TotalProgressBar.TabIndex = 19;
            // 
            // UploadSpeedLabel
            // 
            this.UploadSpeedLabel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.UploadSpeedLabel.AutoSize = true;
            this.UploadSpeedLabel.Location = new System.Drawing.Point(421, 187);
            this.UploadSpeedLabel.Name = "UploadSpeedLabel";
            this.UploadSpeedLabel.Size = new System.Drawing.Size(43, 13);
            this.UploadSpeedLabel.TabIndex = 19;
            this.UploadSpeedLabel.Text = "<None>";
            // 
            // labelControl10
            // 
            this.labelControl10.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.labelControl10.AutoSize = true;
            this.labelControl10.Location = new System.Drawing.Point(348, 187);
            this.labelControl10.Name = "labelControl10";
            this.labelControl10.Size = new System.Drawing.Size(55, 13);
            this.labelControl10.TabIndex = 18;
            this.labelControl10.Text = "上传速度:";
            // 
            // ProtocolDropDown
            // 
            this.ProtocolDropDown.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.ProtocolDropDown.FormattingEnabled = true;
            this.ProtocolDropDown.Items.AddRange(new object[] { "Ftp", "Sftp" });
            this.ProtocolDropDown.Location = new System.Drawing.Point(112, 153);
            this.ProtocolDropDown.Name = "ProtocolDropDown";
            this.ProtocolDropDown.Size = new System.Drawing.Size(121, 21);
            this.ProtocolDropDown.TabIndex = 13;
            this.ProtocolDropDown.SelectedIndexChanged += new System.EventHandler(this.ProtocolDropDown_SelectedIndexChanged);
            // 
            // labelControl7
            // 
            this.labelControl7.AutoSize = true;
            this.labelControl7.Location = new System.Drawing.Point(12, 156);
            this.labelControl7.Name = "labelControl7";
            this.labelControl7.Size = new System.Drawing.Size(31, 13);
            this.labelControl7.TabIndex = 12;
            this.labelControl7.Text = "协议:";
            // 
            // PMain
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(516, 250);
            this.Controls.Add(this.labelControl7);
            this.Controls.Add(this.ProtocolDropDown);
            this.Controls.Add(this.UploadSpeedLabel);
            this.Controls.Add(this.labelControl10);
            this.Controls.Add(this.TotalProgressBar);
            this.Controls.Add(this.UploadSizeLabel);
            this.Controls.Add(this.labelControl8);
            this.Controls.Add(this.StatusLabel);
            this.Controls.Add(this.labelControl6);
            this.Controls.Add(this.UploadPatchButton);
            this.Controls.Add(this.PasswordTextEdit);
            this.Controls.Add(this.HostTextEdit);
            this.Controls.Add(this.labelControl5);
            this.Controls.Add(this.labelControl4);
            this.Controls.Add(this.labelControl3);
            this.Controls.Add(this.UseLoginCheckEdit);
            this.Controls.Add(this.UsernameTextEdit);
            this.Controls.Add(this.labelControl2);
            this.Controls.Add(this.CleanClientBrowseButton);
            this.Controls.Add(this.CleanClientButtonEdit);
            this.Controls.Add(this.labelControl1);
            this.Name = "PMain";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "补丁管理器";
            this.Load += new System.EventHandler(this.PMain_Load);
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private System.Windows.Forms.Label labelControl1;
        private System.Windows.Forms.TextBox CleanClientButtonEdit;
        private System.Windows.Forms.Button CleanClientBrowseButton;
        private System.Windows.Forms.Label labelControl2;
        private System.Windows.Forms.TextBox UsernameTextEdit;
        private System.Windows.Forms.CheckBox UseLoginCheckEdit;
        private System.Windows.Forms.Label labelControl3;
        private System.Windows.Forms.Label labelControl4;
        private System.Windows.Forms.Label labelControl5;
        private System.Windows.Forms.TextBox HostTextEdit;
        private System.Windows.Forms.TextBox PasswordTextEdit;
        private System.Windows.Forms.Button UploadPatchButton;
        private System.Windows.Forms.Label labelControl6;
        private System.Windows.Forms.Label StatusLabel;
        private System.Windows.Forms.Label labelControl8;
        private System.Windows.Forms.Label UploadSizeLabel;
        private System.Windows.Forms.ProgressBar TotalProgressBar;
        private System.Windows.Forms.Label UploadSpeedLabel;
        private System.Windows.Forms.Label labelControl10;
        private System.Windows.Forms.ComboBox ProtocolDropDown;
        private System.Windows.Forms.Label labelControl7;
    }
}
