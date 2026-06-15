using Library;
using Server.Envir.Commands.Command;
using Server.Envir.Commands.Command.Admin;
using Server.Models;
using System;
using System.IO;

namespace Server.Envir.Commands.Admin
{
    class ExportNPCs : AbstractCommand<IAdminCommand>
    {
        public override string VALUE => "EXPORTNPCS";

        public override void Action(PlayerObject player)
        {
            string path = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Config.NpcTextPath));

            TextNpcDatabaseExporter exporter = new();
            TextNpcExportReport report = exporter.Export(path, SEnvir.NPCInfoList.Binding, true);

            player.Connection.ReceiveChat($"NPC 已导出到 {path}，NPC={report.NpcsExported}，页面={report.PagesExported}，TODO={report.TodoLines}", MessageType.System);
        }
    }
}
