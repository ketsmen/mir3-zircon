using Library;
using Server.Envir.Commands.Exceptions;
using Server.Models;

namespace Server.Envir.Commands.Command.Admin
{
    class Capabilities : AbstractParameterizedCommand<IAdminCommand>
    {
        public override string VALUE => "CAPABILITIES";
        public override int PARAMS_LENGTH => 1;

        public override void Action(PlayerObject player, string[] vals)
        {
            PlayerObject target = player;

            if (vals.Length >= 2)
            {
                target = SEnvir.GetPlayerByCharacter(vals[1]);

                if (target == null)
                    throw new UserCommandException(string.Format("Could not find player: {0}", vals[1]));
            }

            string summary = target.Connection?.GetCapabilitiesSummary() ?? "No connection.";

            player.Connection.ReceiveChat($"[Capabilities] {target.Name}: {summary}", MessageType.System);
        }
    }
}
