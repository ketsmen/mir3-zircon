using Library;
using Server.Envir.Commands.Command;
using Server.Envir.Commands.Command.Admin;
using Server.Models;
using System;
using System.IO;
using System.Linq;

namespace Server.Envir.Commands.Admin
{
    class ExportNPCs : AbstractCommand<IAdminCommand>
    {
        public override string VALUE => "EXPORTNPCS";

        public override void Action(PlayerObject player)
        {
            if (string.IsNullOrWhiteSpace(Config.NpcTextPath))
            {
                player.Connection.ReceiveChat("NPC 导出失败: Config.NpcTextPath 未配置。", MessageType.System);
                return;
            }

            string path;
            TextNpcExportReport report;
            try
            {
                path = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Config.NpcTextPath));

                TextNpcDatabaseExporter exporter = new();
                report = exporter.Export(path, SEnvir.NPCInfoList.Binding, true);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
            {
                player.Connection.ReceiveChat($"NPC 导出失败: {ex.Message}", MessageType.System);
                return;
            }

            player.Connection.ReceiveChat($"NPC 已导出到 {path}，NPC={report.NpcsExported}，页面={report.PagesExported}，TODO={report.TodoLines}，失败={report.Failures.Count}", MessageType.System);

            foreach (string failure in report.Failures.Take(5))
                player.Connection.ReceiveChat($"[NPC导出] {failure}", MessageType.System);

            if (report.Failures.Count > 5)
                player.Connection.ReceiveChat($"[NPC导出] 还有 {report.Failures.Count - 5} 条失败，请查看导出 TODO 报告。", MessageType.System);
        }
    }
}
