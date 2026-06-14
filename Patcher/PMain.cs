using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Patcher
{
    public partial class PMain : Form
    {
        public PMain()
        {
            InitializeComponent();
        }

        public void SetCaption(string caption)
        {
            CaptionLabel.Text = caption;
        }

        public void SetDescription(string description)
        {
            DescriptionLabel.Text = description;
        }

        private async void PMain_Load(object sender, EventArgs e)
        {
            try
            {
                string patchFrom = Program.Arguments[0];
                string patchTo = Program.Arguments[1];

                SetDescription("等待 5 秒以便启动器关闭...");

                await Task.Delay(TimeSpan.FromSeconds(5));

                if (File.Exists(patchTo))
                    File.Delete(patchTo);

                SetDescription("正在更新启动器...");

                await Task.Delay(TimeSpan.FromSeconds(3));

                File.Move(patchFrom, patchTo);
                Process.Start(new ProcessStartInfo(patchTo) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "更新失败");
            }

            Close();
        }
    }
}
