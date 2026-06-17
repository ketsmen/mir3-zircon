using Library;
using Server.Models;
using System.Linq;
using S = Library.Network.ServerPackets;

namespace Server.Envir.Commands.Command.Admin
{
    class ReloadCustomSystem : AbstractCommand<IAdminCommand>
    {
        public override string VALUE => "RELOADCUSTOMSYSTEM";

        public override void Action(PlayerObject player)
        {
            CustomSystemTextReport report = SEnvir.ReloadCustomSystem();

            player.Connection.ReceiveChat(
                $"自定义系统已重载：资源 {SEnvir.CustomSystem.ResourcesByKey.Count}，怪物视觉 {SEnvir.CustomSystem.MonsterVisualsByMonsterIndex.Count}，怪物行为 {SEnvir.CustomSystem.MonsterBehaviorsByMonsterIndex.Count}，技能 {SEnvir.CustomSystem.SkillsByMagicIndex.Count}，错误 {report.Failures.Count}。",
                MessageType.System);

            foreach (string failure in report.Failures.Take(5))
                player.Connection.ReceiveChat($"[CustomSystem] {failure}", MessageType.System);

            if (report.Failures.Count > 5)
                player.Connection.ReceiveChat($"[CustomSystem] 还有 {report.Failures.Count - 5} 条错误，请查看服务端日志。", MessageType.System);

            foreach (Server.DBModels.UserMagic magic in player.Character.Magics)
                player.Enqueue(new S.NewMagic { Magic = magic.ToClientInfo() });

            player.RemoveAllObjects();
            player.AddAllObjects();
            player.Connection.ReceiveChat("[CustomSystem] 已刷新你的技能缓存和当前视野对象，文本快照会重新下发。", MessageType.System);
        }
    }
}
