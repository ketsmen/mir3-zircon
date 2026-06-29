using Library;
using Server.Envir.Commands.Exceptions;
using Server.Models;
using System;

namespace Server.Envir.Commands.Command.Admin
{
    class SetHeroGrade : AbstractParameterizedCommand<IAdminCommand>
    {
        public override string VALUE => "SETHEROGRADE";
        public override int PARAMS_LENGTH => 2;

        public override void Action(PlayerObject player, string[] vals)
        {
            PlayerObject issuer = player;
            PlayerObject target = player;
            int grade;

            if (vals.Length >= PARAMS_LENGTH + 1)
            {
                target = SEnvir.GetPlayerByCharacter(vals[1]);
                if (target == null)
                    throw new UserCommandException(string.Format("Could not find player: {0}", vals[1]));

                if (!int.TryParse(vals[2], out grade))
                    ThrowNewInvalidParametersException();
            }
            else
            {
                if (vals.Length < PARAMS_LENGTH)
                    ThrowNewInvalidParametersException();

                if (!int.TryParse(vals[1], out grade))
                    ThrowNewInvalidParametersException();
            }

            int maxGrade = Math.Max(0, Math.Min(4, Config.HeroMaxGrade));

            if (grade < 0 || grade > maxGrade)
                ThrowNewInvalidParametersException();

            if (target.Character?.CurrentHero == null || target.Character.CurrentHero.Deleted)
                throw new UserCommandException(string.Format("{0} does not have a current hero.", target.Name));

            target.Character.CurrentHero.Grade = (byte)grade;
            target.HeroSetup();

            if (target.Hero != null)
            {
                target.Hero.RefreshStats();
                target.Hero.SendInformation();
            }

            target.SendHeroManage();

            string message = $"英雄{target.Character.CurrentHero.Name}的品质已设置为{grade}。";
            target.Connection?.ReceiveChat(message, MessageType.System);

            if (issuer != target)
                issuer.Connection?.ReceiveChat($"[{target.Name}] {message}", MessageType.System);
        }
    }
}
