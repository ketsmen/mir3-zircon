using System;
using System.Collections.Generic;

namespace Server.Envir
{
    public sealed class HeroGradePromoteEventPreset
    {
        public string Name { get; set; }
        public int CostRate { get; set; }
        public int SuccessBonus { get; set; }
        public int DurationMinutes { get; set; }
    }

    public static class HeroGradePromoteEventPresetParser
    {
        public const int MaxDurationMinutes = 525600;

        public static bool TryParse(string text, out List<HeroGradePromoteEventPreset> presets, out List<string> errors)
        {
            presets = new List<HeroGradePromoteEventPreset>();
            errors = new List<string>();

            if (string.IsNullOrWhiteSpace(text))
                return true;

            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] entries = text.Split(new[] { ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string rawEntry in entries)
            {
                string entry = rawEntry.Trim();
                if (entry.Length == 0) continue;

                int split = entry.IndexOf('=');
                if (split <= 0 || split >= entry.Length - 1)
                {
                    errors.Add($"模板 `{entry}` 格式错误，应为 name=costRate,successBonus,durationMinutes。");
                    continue;
                }

                string name = entry.Substring(0, split).Trim();
                string valuesText = entry.Substring(split + 1).Trim();
                int entryErrorCount = errors.Count;

                ValidateName(name, entry, names, errors);

                string[] values = valuesText.Split(',');
                if (values.Length != 3)
                {
                    errors.Add($"模板 `{name}` 格式错误，应提供 3 个值: costRate, successBonus, durationMinutes。");
                    continue;
                }

                if (!int.TryParse(values[0].Trim(), out int costRate) || costRate < 0 || costRate > 1000)
                    errors.Add($"模板 `{name}` 的消耗倍率必须在 0 到 1000 之间。");

                if (!int.TryParse(values[1].Trim(), out int successBonus) || successBonus < 0 || successBonus > 100)
                    errors.Add($"模板 `{name}` 的成功率加成必须在 0 到 100 之间。");

                if (!int.TryParse(values[2].Trim(), out int durationMinutes) || durationMinutes < 0 || durationMinutes > MaxDurationMinutes)
                    errors.Add($"模板 `{name}` 的持续分钟数必须在 0 到 {MaxDurationMinutes} 之间。");

                if (errors.Count > entryErrorCount)
                    continue;

                if (!names.Add(name))
                    continue;

                presets.Add(new HeroGradePromoteEventPreset
                {
                    Name = name,
                    CostRate = costRate,
                    SuccessBonus = successBonus,
                    DurationMinutes = durationMinutes
                });
            }

            return errors.Count == 0;
        }

        public static HeroGradePromoteEventPreset Find(List<HeroGradePromoteEventPreset> presets, string name)
        {
            if (presets == null || string.IsNullOrWhiteSpace(name)) return null;

            foreach (HeroGradePromoteEventPreset preset in presets)
            {
                if (string.Equals(preset.Name, name, StringComparison.OrdinalIgnoreCase))
                    return preset;
            }

            return null;
        }

        public static string Format(HeroGradePromoteEventPreset preset)
        {
            if (preset == null) return string.Empty;

            string duration = preset.DurationMinutes <= 0 ? "不自动到期" : $"{preset.DurationMinutes}分钟";
            return $"{preset.Name}=消耗{preset.CostRate}%，成功+{preset.SuccessBonus}%，持续{duration}";
        }

        private static void ValidateName(string name, string entry, HashSet<string> names, List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                errors.Add($"模板 `{entry}` 名称不能为空。");
                return;
            }

            foreach (char c in name)
            {
                if (!char.IsWhiteSpace(c)) continue;

                errors.Add($"模板 `{name}` 名称不能包含空白字符。");
                break;
            }

            if (string.Equals(name, "preset", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "reset", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "save", StringComparison.OrdinalIgnoreCase))
                errors.Add($"模板 `{name}` 使用了保留名称。");

            if (names.Contains(name))
                errors.Add($"模板 `{name}` 重复。");
        }
    }
}
