using Library;
using System;
using System.Reflection;
using System.Windows.Forms;

namespace PatchManager
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            ConfigReader.Load(Assembly.GetAssembly(typeof(Config)));

            Application.EnableVisualStyles();
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new PMain());

            ConfigReader.Save(typeof(Config).Assembly);
        }
    }
}
