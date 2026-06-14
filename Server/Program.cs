using Library;
using Server.Envir;
using Server.WinForms;
using System;
using System.Reflection;
using System.Runtime;
using System.Windows.Forms;

namespace Server
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            var assembly = Assembly.GetAssembly(typeof(Config));
            ConfigReader.Load(assembly);
            Config.LoadVersion();

            GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;

            Application.EnableVisualStyles();
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                MirDbSessionFactory.ConfigureEncryption();
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "服务端启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            ConfigReader.Save(typeof(Config).Assembly);
        }
    }
}
