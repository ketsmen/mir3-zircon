using System;

namespace Server.Envir
{
    public static class HeroGradePromoteCostCalculator
    {
        public static long GetGoldCost(byte currentGrade, long baseGoldCost, int goldCostMultiplier, int costGrowthRate, int costRate = 100)
        {
            decimal cost = Math.Max(0, baseGoldCost);
            cost *= currentGrade + 1;
            cost *= Math.Max(0, goldCostMultiplier) / 100M;
            cost *= GetCostGrowthMultiplier(currentGrade, costGrowthRate);
            cost *= ClampCostRate(costRate) / 100M;

            return CeilingToLong(cost);
        }

        public static long GetScaledCost(byte currentGrade, long baseCost, int costGrowthRate, int costRate = 100)
        {
            if (baseCost <= 0) return 0;

            decimal cost = baseCost;
            cost *= currentGrade + 1;
            cost *= GetCostGrowthMultiplier(currentGrade, costGrowthRate);
            cost *= ClampCostRate(costRate) / 100M;

            return CeilingToLong(cost);
        }

        public static decimal GetCostGrowthMultiplier(byte currentGrade, int costGrowthRate)
        {
            decimal rate = 1M + ClampCostGrowthRate(costGrowthRate) / 100M;
            decimal multiplier = 1M;

            for (int i = 0; i < currentGrade; i++)
                multiplier *= rate;

            return multiplier;
        }

        public static string GetCostGrowthText(byte currentGrade, int costGrowthRate, int costRate = 100)
        {
            int growthRate = ClampCostGrowthRate(costGrowthRate);
            int rate = ClampCostRate(costRate);
            decimal multiplier = GetCostGrowthMultiplier(currentGrade, growthRate);
            string costRateText = rate == 100 ? string.Empty : $"，活动消耗倍率 {rate}%";

            if (growthRate <= 0)
                return $"消耗成长率 0%，本档成长倍率 x1{costRateText}";

            return $"消耗成长率 {growthRate}%，本档成长倍率 x{multiplier:0.##}{costRateText}";
        }

        public static int ClampCostGrowthRate(int costGrowthRate)
        {
            return Math.Max(0, Math.Min(1000, costGrowthRate));
        }

        public static int ClampCostRate(int costRate)
        {
            return Math.Max(0, Math.Min(1000, costRate));
        }

        public static int ClampSuccessBonus(int successBonus)
        {
            return Math.Max(0, Math.Min(100, successBonus));
        }

        public static int GetFailureBonus(int failureCount, int failureBonusRate, int failureBonusMax = 100)
        {
            if (failureCount <= 0 || failureBonusRate <= 0) return 0;

            long bonus = (long)failureCount * ClampSuccessBonus(failureBonusRate);
            int max = ClampSuccessBonus(failureBonusMax);
            return bonus >= max ? max : (int)bonus;
        }

        public static int GetSuccessRate(int baseSuccessRate, int eventSuccessBonus, int failureCount, int failureBonusRate, int failureBonusMax = 100)
        {
            int rate = Math.Max(0, Math.Min(100, baseSuccessRate));
            rate += ClampSuccessBonus(eventSuccessBonus);
            rate += GetFailureBonus(failureCount, failureBonusRate, failureBonusMax);

            return Math.Max(0, Math.Min(100, rate));
        }

        private static long CeilingToLong(decimal cost)
        {
            if (cost >= long.MaxValue) return long.MaxValue;

            return Math.Max(0, (long)Math.Ceiling(cost));
        }
    }
}
