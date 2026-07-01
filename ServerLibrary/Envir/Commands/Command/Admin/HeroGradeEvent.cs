using Library;
using Server.Envir.Commands.Exceptions;
using Server.Models;
using System;
using System.Collections.Generic;
using S = Library.Network.ServerPackets;

namespace Server.Envir.Commands.Command.Admin
{
    class HeroGradeEvent : AbstractParameterizedCommand<IAdminCommand>
    {
        public override string VALUE => "HEROGRADEEVENT";
        public override int PARAMS_LENGTH => 2;
        private const int MaxDurationMinutes = HeroGradePromoteEventPresetParser.MaxDurationMinutes;

        public override void Action(PlayerObject player, string[] vals)
        {
            if (vals.Length == 1)
            {
                SendStatus(player);
                return;
            }

            if (string.Equals(vals[1], "PRESET", StringComparison.OrdinalIgnoreCase))
            {
                ApplyPreset(player, vals);
                return;
            }

            if (vals.Length == 2 && string.Equals(vals[1], "RESET", StringComparison.OrdinalIgnoreCase))
            {
                ApplyEvent(player, 100, 0, DateTime.MinValue, false, true, false);
                return;
            }

            if (vals.Length == 3 && string.Equals(vals[1], "RESET", StringComparison.OrdinalIgnoreCase) && IsSaveToken(vals[2]))
            {
                ApplyEvent(player, 100, 0, DateTime.MinValue, false, true, true);
                return;
            }

            if (vals.Length < 3 || vals.Length > 5)
                ThrowNewInvalidParametersException();

            bool costRateParsed = int.TryParse(vals[1], out int costRate);
            bool successBonusParsed = int.TryParse(vals[2], out int successBonus);

            if (!costRateParsed || !successBonusParsed)
                ThrowNewInvalidParametersException();

            if (costRate < 0 || costRate > 1000)
                throw new UserCommandException("HeroGradePromoteEventCostRate 必须在 0 到 1000 之间。");

            if (successBonus < 0 || successBonus > 100)
                throw new UserCommandException("HeroGradePromoteEventSuccessBonus 必须在 0 到 100 之间。");

            int durationMinutes = 0;
            bool durationSet = false;
            bool save = false;

            for (int i = 3; i < vals.Length; i++)
            {
                if (IsSaveToken(vals[i]))
                {
                    if (save)
                        ThrowNewInvalidParametersException();

                    save = true;
                    continue;
                }

                if (durationSet || !int.TryParse(vals[i], out durationMinutes))
                    ThrowNewInvalidParametersException();

                durationSet = true;

                if (durationMinutes < 0 || durationMinutes > MaxDurationMinutes)
                    throw new UserCommandException($"活动持续分钟数必须在 0 到 {MaxDurationMinutes} 之间。");
            }

            DateTime eventEnd = durationMinutes > 0 ? SEnvir.Now.AddMinutes(durationMinutes) : DateTime.MinValue;
            ApplyEvent(player, costRate, successBonus, eventEnd, save && eventEnd != DateTime.MinValue, false, save);
        }

        private static void SendStatus(PlayerObject player)
        {
            int costRate = HeroGradePromoteCostCalculator.ClampCostRate(Config.HeroGradePromoteEventCostRate);
            int successBonus = HeroGradePromoteCostCalculator.ClampSuccessBonus(Config.HeroGradePromoteEventSuccessBonus);
            string endText = FormatEventEnd(Config.HeroGradePromoteEventEnd, Config.HeroGradePromoteEventSaveOnExpire);

            player.Connection.ReceiveChat(
                $"[HeroGradeEvent] 当前活动消耗倍率 {costRate}%，活动成功率加成 +{successBonus}%，结束:{endText}。用法: @HEROGRADEEVENT <消耗倍率0-1000> <成功率加成0-100> [持续分钟0-{MaxDurationMinutes}] [save]，@HEROGRADEEVENT preset [名称] [持续分钟] [save]，或 @HEROGRADEEVENT reset [save]。",
                MessageType.System);
        }

        private void ApplyPreset(PlayerObject player, string[] vals)
        {
            if (!TryLoadPresets(player, out List<HeroGradePromoteEventPreset> presets))
                return;

            if (vals.Length == 2)
            {
                SendPresetList(player, presets);
                return;
            }

            if (vals.Length < 3 || vals.Length > 5)
                ThrowNewInvalidParametersException();

            HeroGradePromoteEventPreset preset = HeroGradePromoteEventPresetParser.Find(presets, vals[2]);
            if (preset == null)
                throw new UserCommandException($"找不到英雄品质培养活动模板 `{vals[2]}`。");

            int durationMinutes = preset.DurationMinutes;
            bool durationSet = false;
            bool save = false;

            for (int i = 3; i < vals.Length; i++)
            {
                if (IsSaveToken(vals[i]))
                {
                    if (save)
                        ThrowNewInvalidParametersException();

                    save = true;
                    continue;
                }

                if (durationSet || !int.TryParse(vals[i], out durationMinutes))
                    ThrowNewInvalidParametersException();

                durationSet = true;

                if (durationMinutes < 0 || durationMinutes > MaxDurationMinutes)
                    throw new UserCommandException($"活动持续分钟数必须在 0 到 {MaxDurationMinutes} 之间。");
            }

            DateTime eventEnd = durationMinutes > 0 ? SEnvir.Now.AddMinutes(durationMinutes) : DateTime.MinValue;
            ApplyEvent(player, preset.CostRate, preset.SuccessBonus, eventEnd, save && eventEnd != DateTime.MinValue, false, save, $"模板:{preset.Name}");
        }

        private static bool TryLoadPresets(PlayerObject player, out List<HeroGradePromoteEventPreset> presets)
        {
            if (HeroGradePromoteEventPresetParser.TryParse(Config.HeroGradePromoteEventPresets, out presets, out List<string> errors))
                return true;

            player.Connection.ReceiveChat("[HeroGradeEvent] 活动模板配置无效。", MessageType.System);
            foreach (string error in errors)
                player.Connection.ReceiveChat($"[HeroGradeEvent] {error}", MessageType.System);

            return false;
        }

        private static void SendPresetList(PlayerObject player, List<HeroGradePromoteEventPreset> presets)
        {
            if (presets.Count == 0)
            {
                player.Connection.ReceiveChat("[HeroGradeEvent] 当前没有配置活动模板。请设置 HeroGradePromoteEventPresets，例如 weekend=80,10,2880。", MessageType.System);
                return;
            }

            player.Connection.ReceiveChat("[HeroGradeEvent] 可用活动模板:", MessageType.System);
            foreach (HeroGradePromoteEventPreset preset in presets)
                player.Connection.ReceiveChat($"[HeroGradeEvent] {HeroGradePromoteEventPresetParser.Format(preset)}；用法: @HEROGRADEEVENT preset {preset.Name} [持续分钟] [save]", MessageType.System);
        }

        private static void ApplyEvent(PlayerObject player, int costRate, int successBonus, DateTime eventEnd, bool saveOnExpire, bool reset, bool saveNow, string sourceText = null)
        {
            Config.HeroGradePromoteEventCostRate = HeroGradePromoteCostCalculator.ClampCostRate(costRate);
            Config.HeroGradePromoteEventSuccessBonus = HeroGradePromoteCostCalculator.ClampSuccessBonus(successBonus);
            Config.HeroGradePromoteEventEnd = eventEnd;
            Config.HeroGradePromoteEventSaveOnExpire = saveOnExpire;

            string actionText = reset ? "已关闭" : "已更新";
            string source = string.IsNullOrWhiteSpace(sourceText) ? string.Empty : $"，{sourceText}";
            string endText = reset ? string.Empty : $"，结束:{FormatEventEnd(Config.HeroGradePromoteEventEnd, Config.HeroGradePromoteEventSaveOnExpire)}";
            string message = $"英雄品质培养活动{actionText}{source}：消耗倍率 {Config.HeroGradePromoteEventCostRate}%，成功率加成 +{Config.HeroGradePromoteEventSuccessBonus}%{endText}。";

            player.Connection.ReceiveChat($"[HeroGradeEvent] {message}", MessageType.System);
            SEnvir.Broadcast(new S.Chat { Text = message, Type = MessageType.Announcement });
            SEnvir.BroadcastHeroGradePromoteEventState();
            SEnvir.Log($"[HeroGradeEvent] {player.Name} set costRate:{Config.HeroGradePromoteEventCostRate}, successBonus:{Config.HeroGradePromoteEventSuccessBonus}, end:{Config.HeroGradePromoteEventEnd:o}, saveOnExpire:{Config.HeroGradePromoteEventSaveOnExpire}, reset:{reset}, saveNow:{saveNow}.", true);

            if (saveNow)
                SaveConfig(player);
        }

        private static bool IsSaveToken(string value)
        {
            return string.Equals(value, "SAVE", StringComparison.OrdinalIgnoreCase);
        }

        private static string FormatEventEnd(DateTime eventEnd, bool saveOnExpire)
        {
            if (eventEnd == DateTime.MinValue)
                return "不自动到期";

            TimeSpan remaining = eventEnd - SEnvir.Now;
            string state = remaining <= TimeSpan.Zero ? "已到期，等待自动恢复" : $"剩余 {FormatDuration(remaining)}";
            string saveText = saveOnExpire ? "，到期写回配置" : "，到期只恢复内存";

            return $"{eventEnd:yyyy-MM-dd HH:mm:ss} UTC（{state}{saveText}）";
        }

        private static string FormatDuration(TimeSpan duration)
        {
            if (duration.TotalDays >= 1)
                return $"{Math.Ceiling(duration.TotalDays)}天";

            if (duration.TotalHours >= 1)
                return $"{Math.Ceiling(duration.TotalHours)}小时";

            if (duration.TotalMinutes >= 1)
                return $"{Math.Ceiling(duration.TotalMinutes)}分钟";

            return $"{Math.Ceiling(Math.Max(0, duration.TotalSeconds))}秒";
        }

        private static void SaveConfig(PlayerObject player)
        {
            try
            {
                ConfigReader.Save(typeof(Config).Assembly);
                player.Connection.ReceiveChat("[HeroGradeEvent] 已写入 Server.ini。", MessageType.System);
            }
            catch (Exception ex)
            {
                SEnvir.Log("[HeroGradeEvent] failed to save Server.ini: " + ex, true);
                player.Connection.ReceiveChat($"[HeroGradeEvent] 写入 Server.ini 失败: {ex.Message}", MessageType.System);
            }
        }
    }
}
