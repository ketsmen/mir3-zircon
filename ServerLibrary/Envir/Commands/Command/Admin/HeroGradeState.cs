using Library;
using Server.DBModels;
using Server.Envir.Commands.Exceptions;
using Server.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Server.Envir.Commands.Command.Admin
{
    class HeroGradeState : AbstractParameterizedCommand<IAdminCommand>
    {
        public override string VALUE => "HEROGRADESTATE";
        public override int PARAMS_LENGTH => 1;

        public override void Action(PlayerObject player, string[] vals)
        {
            PlayerObject target = player;
            int slotNumber = 0;

            if (vals.Length >= 2)
            {
                if (int.TryParse(vals[1], out int ownSlot))
                {
                    slotNumber = ownSlot;
                }
                else
                {
                    target = SEnvir.GetPlayerByCharacter(vals[1]);

                    if (target == null)
                        throw new UserCommandException(string.Format("Could not find player: {0}", vals[1]));

                    if (vals.Length >= 3 && (!int.TryParse(vals[2], out slotNumber) || slotNumber <= 0))
                        ThrowNewInvalidParametersException();
                }
            }

            HeroInfo hero = ResolveHero(target, slotNumber);
            if (hero == null || hero.Deleted)
            {
                if (slotNumber > 0)
                    throw new UserCommandException(string.Format("{0} does not have a hero at slot {1}.", target.Name, slotNumber));

                throw new UserCommandException(string.Format("{0} does not have a current hero.", target.Name));
            }

            int gradeRate = Math.Max(0, Math.Min(20, Config.HeroGradeStatBonusRate));
            int maxGrade = Math.Max(0, Math.Min(4, Config.HeroMaxGrade));
            int currentBonus = hero.Grade * gradeRate;

            player.Connection.ReceiveChat(
                $"[HeroGradeState] 目标:{target.Name}，槽位:{hero.Slot + 1}，英雄:{hero.Name}(#{hero.Index})，当前品质:{HeroGradeName(hero.Grade)}({hero.Grade})，基础属性加成:+{currentBonus}%，最高品质:{HeroGradeName((byte)maxGrade)}。",
                MessageType.System);

            ClientHeroGradePromotePreview preview = target.GetHeroGradePromotePreview(hero);
            if (!string.IsNullOrEmpty(preview.ErrorText))
            {
                player.Connection.ReceiveChat($"[HeroGradeState] {preview.ErrorText}", MessageType.System);
                return;
            }

            player.Connection.ReceiveChat(
                $"[HeroGradeState] 下一档:{HeroGradeName(preview.CurrentGrade)}->{HeroGradeName(preview.NextGrade)}，基础属性加成:+{preview.CurrentGradeBonusRate}% -> +{preview.NextGradeBonusRate}%，{FormatSuccessRate(preview)}，{preview.GrowthText}。",
                MessageType.System);

            player.Connection.ReceiveChat(
                $"[HeroGradeState] 金币:{FormatCost(preview.GoldCost, preview.GoldAvailable)}；材料:{FormatNamedCost(preview.MaterialName, preview.MaterialCount, preview.MaterialAvailable)}；货币:{FormatNamedCost(preview.CurrencyName, preview.CurrencyAmount, preview.CurrencyAvailable)}。",
                MessageType.System);

            player.Connection.ReceiveChat(
                $"[HeroGradeState] 保护:{FormatProtection(preview)}；确认状态:{(preview.CanPromote ? "可培养" : "不可培养")}。",
                MessageType.System);
        }

        private static HeroInfo ResolveHero(PlayerObject target, int slotNumber)
        {
            if (target.Character == null) return null;

            if (slotNumber <= 0)
                return target.Character.CurrentHero;

            List<HeroInfo> heroes = target.Character.Heroes
                .Where(x => !x.Deleted)
                .OrderBy(x => x.Slot)
                .ThenBy(x => x.Index)
                .ToList();

            if (slotNumber > heroes.Count) return null;

            return heroes[slotNumber - 1];
        }

        private static string FormatCost(long required, long available)
        {
            return required <= 0 ? "无" : $"需要 {required:#,##0}，可用 {available:#,##0}，{FormatAvailability(required, available)}";
        }

        private static string FormatNamedCost(string name, long required, long available)
        {
            return required <= 0 ? "无" : $"{name} x{required:#,##0}，可用 {available:#,##0}，{FormatAvailability(required, available)}";
        }

        private static string FormatAvailability(long required, long available)
        {
            return available >= required ? "足够" : $"不足 {required - available:#,##0}";
        }

        private static string FormatSuccessRate(ClientHeroGradePromotePreview preview)
        {
            string text = $"成功率:{preview.SuccessRate}%(基础 {preview.BaseSuccessRate}%";

            if (preview.EventSuccessBonus > 0)
                text += $"，活动 +{preview.EventSuccessBonus}%";

            if (preview.FailureBonus > 0)
                text += $"，失败补偿 +{preview.FailureBonus}%/{preview.FailureBonusMax}%({preview.FailureCount}次)";

            return text + ")";
        }

        private static string FormatProtection(ClientHeroGradePromotePreview preview)
        {
            if (preview.ProtectionCount <= 0 || string.IsNullOrEmpty(preview.ProtectionName))
                return "未配置";

            string state;
            if (!preview.UseProtection)
                state = "不足，本次不启用保护";
            else if (preview.ProtectionConsumeOnFailureOnly && preview.SuccessRate >= 100)
                state = "成功率已满，本次不会消耗保护";
            else if (preview.ProtectionConsumeOnFailureOnly)
                state = "失败时才消耗并保底成功";
            else
                state = "确认后立即消耗并保底成功";

            return $"{preview.ProtectionName} x{preview.ProtectionCount:#,##0}，可用 {preview.ProtectionAvailable:#,##0}，{state}";
        }

        private static string HeroGradeName(byte value)
        {
            return value switch
            {
                0 => "普通",
                1 => "优秀",
                2 => "精良",
                3 => "卓越",
                4 => "传说",
                _ => $"品质{value}"
            };
        }
    }
}
