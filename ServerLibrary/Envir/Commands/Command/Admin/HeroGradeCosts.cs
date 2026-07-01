using Library;
using Library.SystemModels;
using Server.Models;
using System;
using System.Linq;

namespace Server.Envir.Commands.Command.Admin
{
    class HeroGradeCosts : AbstractCommand<IAdminCommand>
    {
        public override string VALUE => "HEROGRADECOSTS";

        public override void Action(PlayerObject player)
        {
            int maxGrade = Math.Max(0, Math.Min(4, Config.HeroMaxGrade));
            int growthRate = HeroGradePromoteCostCalculator.ClampCostGrowthRate(Config.HeroGradePromoteCostGrowthRate);
            int costRate = HeroGradePromoteCostCalculator.ClampCostRate(Config.HeroGradePromoteEventCostRate);
            int eventSuccessBonus = HeroGradePromoteCostCalculator.ClampSuccessBonus(Config.HeroGradePromoteEventSuccessBonus);
            int failureBonusRate = HeroGradePromoteCostCalculator.ClampSuccessBonus(Config.HeroGradePromoteFailureBonusRate);
            int failureBonusMax = HeroGradePromoteCostCalculator.ClampSuccessBonus(Config.HeroGradePromoteFailureBonusMax);
            string eventEndText = FormatEventEnd(Config.HeroGradePromoteEventEnd, Config.HeroGradePromoteEventSaveOnExpire);
            string materialName = GetItemName(Config.HeroGradePromoteItemIndex);
            string currencyName = GetCurrencyName(Config.HeroGradePromoteCurrencyIndex);
            string protectionName = GetItemName(Config.HeroGradePromoteProtectionItemIndex);
            string protectionMode = Config.HeroGradePromoteProtectionConsumeOnFailureOnly ? "失败时才消耗" : "启用保护即消耗";

            player.Connection.ReceiveChat(
                $"[HeroGradeCosts] 开关:{(Config.HeroGradePromoteEnabled ? "开启" : "关闭")}，最高品质:{HeroGradeName((byte)maxGrade)}，基础成功率:{Math.Max(0, Math.Min(100, Config.HeroGradePromoteSuccessRate))}%，活动成功:+{eventSuccessBonus}%，活动结束:{eventEndText}，失败补偿每次:+{failureBonusRate}%(上限:+{failureBonusMax}%)，金币倍率:{Config.HeroGradePromoteGoldCostMultiplier}%，消耗成长率:{growthRate}%，活动消耗倍率:{costRate}%。",
                MessageType.System);

            player.Connection.ReceiveChat(
                $"[HeroGradeCosts] 材料:{FormatBaseCost(materialName, Config.HeroGradePromoteBaseItemCount)}；货币:{FormatBaseCost(currencyName, Config.HeroGradePromoteBaseCurrencyAmount)}；保护:{FormatProtection(protectionName, Config.HeroGradePromoteProtectionItemCount, protectionMode)}。",
                MessageType.System);

            if (maxGrade <= 0)
            {
                player.Connection.ReceiveChat("[HeroGradeCosts] 当前没有可培养档位。", MessageType.System);
                return;
            }

            long totalGoldCost = 0;
            long totalMaterialCount = 0;
            long totalCurrencyAmount = 0;

            for (int current = 0; current < maxGrade; current++)
            {
                byte currentGrade = (byte)current;
                byte nextGrade = (byte)(current + 1);
                decimal growthMultiplier = HeroGradePromoteCostCalculator.GetCostGrowthMultiplier(currentGrade, growthRate);
                long goldCost = HeroGradePromoteCostCalculator.GetGoldCost(currentGrade, Config.HeroGradePromoteBaseGoldCost, Config.HeroGradePromoteGoldCostMultiplier, growthRate, costRate);
                long materialCount = Config.HeroGradePromoteItemIndex <= 0 ? 0 : HeroGradePromoteCostCalculator.GetScaledCost(currentGrade, Config.HeroGradePromoteBaseItemCount, growthRate, costRate);
                long currencyAmount = Config.HeroGradePromoteCurrencyIndex <= 0 ? 0 : HeroGradePromoteCostCalculator.GetScaledCost(currentGrade, Config.HeroGradePromoteBaseCurrencyAmount, growthRate, costRate);
                totalGoldCost = AddCapped(totalGoldCost, goldCost);
                totalMaterialCount = AddCapped(totalMaterialCount, materialCount);
                totalCurrencyAmount = AddCapped(totalCurrencyAmount, currencyAmount);

                player.Connection.ReceiveChat(
                    $"[HeroGradeCosts] {HeroGradeName(currentGrade)}->{HeroGradeName(nextGrade)}：成长倍率 x{growthMultiplier:0.##}，金币 {FormatAmount(goldCost)}(累计 {FormatAmount(totalGoldCost)})，材料 {FormatNamedAmount(materialName, materialCount)}(累计 {FormatNamedAmount(materialName, totalMaterialCount)})，货币 {FormatNamedAmount(currencyName, currencyAmount)}(累计 {FormatNamedAmount(currencyName, totalCurrencyAmount)})。",
                    MessageType.System);
            }
        }

        private static string GetItemName(int index)
        {
            if (index <= 0) return "未配置";

            ItemInfo item = SEnvir.ItemInfoList?.Binding?.FirstOrDefault(x => x.Index == index);
            return item == null ? $"物品索引 {index}" : $"{item.ItemName}(#{index})";
        }

        private static string GetCurrencyName(int index)
        {
            if (index <= 0) return "未配置";

            CurrencyInfo currency = SEnvir.CurrencyInfoList?.Binding?.FirstOrDefault(x => x.Index == index);
            return currency == null ? $"货币索引 {index}" : $"{currency.Name}(#{index})";
        }

        private static string FormatBaseCost(string name, long amount)
        {
            return amount <= 0 ? "无" : $"{name} 基础 {amount:#,##0}";
        }

        private static string FormatProtection(string name, long amount, string mode)
        {
            return amount <= 0 || name == "未配置" ? "未配置" : $"{name} x{amount:#,##0}，{mode}";
        }

        private static string FormatAmount(long amount)
        {
            return amount <= 0 ? "无" : amount.ToString("#,##0");
        }

        private static long AddCapped(long current, long amount)
        {
            if (amount <= 0) return current;
            if (current >= long.MaxValue - amount) return long.MaxValue;

            return current + amount;
        }

        private static string FormatNamedAmount(string name, long amount)
        {
            return amount <= 0 ? "无" : $"{name} x{amount:#,##0}";
        }

        private static string FormatEventEnd(DateTime eventEnd, bool saveOnExpire)
        {
            if (eventEnd == DateTime.MinValue)
                return "不自动到期";

            TimeSpan remaining = eventEnd - SEnvir.Now;
            string state = remaining <= TimeSpan.Zero ? "已到期，等待自动恢复" : $"剩余 {FormatDuration(remaining)}";
            string saveText = saveOnExpire ? "，到期写回配置" : "，到期只恢复内存";

            return $"{eventEnd:yyyy-MM-dd HH:mm:ss} UTC({state}{saveText})";
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
