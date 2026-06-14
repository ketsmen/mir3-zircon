using Library;
using System;
using System.Reflection;
using System.Windows.Forms;

namespace Launcher
{
    static class Program
    {
        public const string PatcherFileName = @".\Patcher.exe";

        [STAThread]
        static void Main()
        {
            ConfigReader.Load(Assembly.GetAssembly(typeof(Config)));

            Application.EnableVisualStyles();
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.SetCompatibleTextRenderingDefault(false);

            Application.Run(new LMain());

            ConfigReader.Save(typeof(Config).Assembly);
        }
    }
}
