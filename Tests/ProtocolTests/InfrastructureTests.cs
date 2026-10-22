using System;
using System.IO;
using System.Linq;
using Server.Envir;
using Xunit;

namespace ProtocolTests
{
    public sealed class InfrastructureTests
    {
        [Fact]
        public void StartupHealthAcceptsDefaultConfiguration()
        {
            ServerStartupHealthReport report = ServerStartupHealthCheck.Evaluate();

            Assert.True(report.Success, string.Join(Environment.NewLine, report.Errors));
        }

        [Fact]
        public void StartupHealthAcceptsHttpListenerWildcardPrefixes()
        {
            bool oldEnabled = Config.EnableWebServer;
            string oldWeb = Config.WebPrefix;
            string oldBuy = Config.BuyPrefix;
            string oldIpn = Config.IPNPrefix;

            try
            {
                Config.EnableWebServer = true;
                Config.WebPrefix = "http://*:80/Command/";
                Config.BuyPrefix = "http://+:80/BuyGameGold/";
                Config.IPNPrefix = "https://*:443/IPN/";

                ServerStartupHealthReport report = ServerStartupHealthCheck.Evaluate();

                Assert.True(report.Success, string.Join(Environment.NewLine, report.Errors));
            }
            finally
            {
                Config.EnableWebServer = oldEnabled;
                Config.WebPrefix = oldWeb;
                Config.BuyPrefix = oldBuy;
                Config.IPNPrefix = oldIpn;
            }
        }

        [Fact]
        public void LogFileMaintenanceRotatesAndCleansOldFiles()
        {
            string directory = Path.Combine(Path.GetTempPath(), "zircon-log-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);

            try
            {
                string path = Path.Combine(directory, "Logs.txt");
                File.WriteAllText(path, "12345");

                LogFileMaintenance.RotateIfNeeded(path, 1);

                Assert.False(File.Exists(path));
                Assert.Single(Directory.GetFiles(directory, "Logs.*.txt"));

                string oldArchive = Path.Combine(directory, "Logs.20000101-000000.txt");
                File.WriteAllText(oldArchive, "old");
                File.SetLastWriteTime(oldArchive, DateTime.Now.AddDays(-30));

                LogFileMaintenance.CleanupOldFiles(path, 14);

                Assert.False(File.Exists(oldArchive));
                Assert.Equal(1, Directory.GetFiles(directory, "Logs.*.txt").Count(File.Exists));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
