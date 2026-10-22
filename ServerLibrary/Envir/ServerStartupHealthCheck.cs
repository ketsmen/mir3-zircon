using Library;
using System;
using System.Collections.Generic;
using System.Net;

namespace Server.Envir
{
    public static class ServerStartupHealthCheck
    {
        public static ServerStartupHealthReport Evaluate()
        {
            ServerStartupHealthReport report = new();

            if (!IPAddress.TryParse(Config.IPAddress, out _))
                report.Errors.Add($"IPAddress `{Config.IPAddress}` 不是有效 IP 地址。");

            ValidatePort(report, nameof(Config.Port), Config.Port);
            ValidatePort(report, nameof(Config.UserCountPort), Config.UserCountPort);

            if (Config.Port == Config.UserCountPort)
                report.Errors.Add("Port 和 UserCountPort 不能相同。");

            if (Config.DBSaveDelay <= TimeSpan.Zero)
                report.Errors.Add($"DBSaveDelay 必须大于 0，当前值: {Config.DBSaveDelay}。");

            if (Config.EnableWebServer)
            {
                ValidateHttpPrefix(report, nameof(Config.WebPrefix), Config.WebPrefix);
                ValidateHttpPrefix(report, nameof(Config.BuyPrefix), Config.BuyPrefix);
                ValidateHttpPrefix(report, nameof(Config.IPNPrefix), Config.IPNPrefix);

                if (Config.AllowSystemDBSync && string.IsNullOrWhiteSpace(Config.SyncKey))
                    report.Errors.Add("AllowSystemDBSync 已开启，但 SyncKey 为空。");

                if (Config.ProcessGameGold && string.IsNullOrWhiteSpace(Config.ReceiverEMail))
                    report.Warnings.Add("ProcessGameGold 已开启，但 ReceiverEMail 为空。");
            }

            if (Config.HeroMaxGrade < 0 || Config.HeroMaxGrade > 4)
                report.Warnings.Add($"HeroMaxGrade={Config.HeroMaxGrade} 超出建议范围 0-4。");

            return report;
        }

        private static void ValidatePort(ServerStartupHealthReport report, string name, int port)
        {
            if (port <= 0 || port > 65535)
                report.Errors.Add($"{name}={port} 不是有效端口。");
        }

        private static void ValidateHttpPrefix(ServerStartupHealthReport report, string name, string prefix)
        {
            if (string.IsNullOrWhiteSpace(prefix))
            {
                report.Errors.Add($"{name} 不能为空。");
                return;
            }

            string uriPrefix = NormalizeHttpListenerPrefix(prefix);

            if (!Uri.TryCreate(uriPrefix, UriKind.Absolute, out Uri uri) ||
                uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            {
                report.Errors.Add($"{name}=`{prefix}` 不是有效 HTTP/HTTPS 前缀。");
                return;
            }

            if (!prefix.EndsWith("/", StringComparison.Ordinal))
                report.Errors.Add($"{name}=`{prefix}` 必须以 `/` 结尾。");
        }

        private static string NormalizeHttpListenerPrefix(string prefix)
        {
            if (prefix.StartsWith("http://*:", StringComparison.OrdinalIgnoreCase))
                return "http://localhost:" + prefix["http://*:".Length..];

            if (prefix.StartsWith("http://+:", StringComparison.OrdinalIgnoreCase))
                return "http://localhost:" + prefix["http://+:".Length..];

            if (prefix.StartsWith("https://*:", StringComparison.OrdinalIgnoreCase))
                return "https://localhost:" + prefix["https://*:".Length..];

            if (prefix.StartsWith("https://+:", StringComparison.OrdinalIgnoreCase))
                return "https://localhost:" + prefix["https://+:".Length..];

            return prefix;
        }
    }

    public sealed class ServerStartupHealthReport
    {
        public List<string> Errors { get; } = [];
        public List<string> Warnings { get; } = [];
        public bool Success => Errors.Count == 0;
    }
}
