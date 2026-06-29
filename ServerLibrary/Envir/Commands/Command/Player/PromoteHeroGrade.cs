using Server.Envir.Commands.Command;
using Server.Envir.Commands.Command.Player;
using Server.Models;
using System;

namespace Server.Envir.Commands.Player
{
    class PromoteHeroGrade : AbstractParameterizedCommand<IPlayerCommand>
    {
        public override string VALUE => "PROMOTEHEROGRADE";
        public override int PARAMS_LENGTH => 1;

        public override void Action(PlayerObject player, string[] vals)
        {
            if (vals.Length >= 2 &&
                (vals[1].Equals("preview", StringComparison.OrdinalIgnoreCase) ||
                 vals[1].Equals("预览", StringComparison.OrdinalIgnoreCase)))
            {
                player.PreviewHeroGradePromote();
                return;
            }

            player.PromoteHeroGrade();
        }
    }
}
