using Library.SystemModels;
using MirDB;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Server.Envir
{
    public sealed class Mir2DropExporter
    {
        private readonly Session _session;

        public Mir2DropExporter(Session session)
        {
            _session = session;
        }

        public Mir2DropExportReport Export(string outputDirectory)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory))
                throw new ArgumentException("输出目录不能为空。", nameof(outputDirectory));

            Directory.CreateDirectory(outputDirectory);

            Mir2DropExportReport report = new();
            HashSet<string> usedFileNames = new(StringComparer.OrdinalIgnoreCase);

            foreach (MonsterInfo monster in _session.GetCollection<MonsterInfo>().Binding.Where(x => x != null))
            {
                string fileName = SafeFileName(monster.MonsterName);
                if (string.IsNullOrWhiteSpace(fileName))
                    continue;

                List<string> lines = BuildMonsterLines(monster, report);
                if (lines.Count == 0)
                    continue;

                fileName = GetUniqueFileName(fileName, monster.Index, usedFileNames);
                File.WriteAllLines(Path.Combine(outputDirectory, fileName + ".txt"), lines, new UTF8Encoding(false));
                report.MonstersExported++;
            }

            return report;
        }

        public List<string> BuildMonsterLines(MonsterInfo monster, Mir2DropExportReport report)
        {
            List<string> lines = [];

            foreach (DropInfo drop in monster.Drops.OrderBy(x => x.Index))
            {
                string line = FormatDropLine(drop);
                if (!string.IsNullOrWhiteSpace(line))
                {
                    lines.Add(line);
                    report.DropLines++;
                }
            }

            foreach (DropGroupInfo group in monster.DropGroups.Where(x => x.Parent == null).OrderBy(x => x.Sequence).ThenBy(x => x.Index))
            {
                List<string> groupLines = BuildGroupLines(group, report, 0);
                if (groupLines.Count == 0) continue;

                lines.AddRange(groupLines);
                report.GroupLines++;
            }

            return lines;
        }

        private List<string> BuildGroupLines(DropGroupInfo group, Mir2DropExportReport report, int depth)
        {
            List<string> lines = [];
            string indent = new string('\t', depth);

            lines.Add(FormatGroupHeader(group, indent));
            lines.Add(indent + "{");

            foreach (DropGroupItemInfo item in group.Items.OrderBy(x => x.Sequence).ThenBy(x => x.Index))
            {
                string line = FormatGroupItemLine(item, indent + "\t");
                if (string.IsNullOrWhiteSpace(line)) continue;
                lines.Add(line);
                report.GroupItemLines++;
            }

            foreach (DropGroupInfo child in group.Groups.OrderBy(x => x.Sequence).ThenBy(x => x.Index))
            {
                List<string> childLines = BuildGroupLines(child, report, depth + 1);
                if (childLines.Count == 0) continue;
                lines.AddRange(childLines);
            }

            lines.Add(indent + "}");
            return lines;
        }

        private static string FormatDropLine(DropInfo drop)
        {
            if (drop?.Item == null || drop.Chance <= 0)
                return string.Empty;

            List<string> parts = new()
            {
                $"1/{drop.Chance}",
                QuoteIfNeeded(drop.Item.ItemName)
            };

            if (drop.Amount > 1)
            {
                parts.Add($"Amount={drop.Amount}");
            }

            AddOptions(parts, drop.DropSet, drop.PartOnly, drop.EasterEvent, drop.Mir2Gold);
            return string.Join(" ", parts);
        }

        private static string FormatGroupHeader(DropGroupInfo group, string indent)
        {
            string token = group.Mode switch
            {
                DropGroupMode.RandomOneMatched => "GROUP*",
                DropGroupMode.FirstMatched => "GROUP^",
                _ => "GROUP"
            };

            List<string> parts = new()
            {
                $"1/{group.Chance}",
                token
            };

            AddOptions(parts, group.DropSet, false, group.EasterEvent, false);
            return indent + string.Join(" ", parts);
        }

        private static string FormatGroupItemLine(DropGroupItemInfo item, string indent)
        {
            if (item?.Item == null || item.Chance <= 0)
                return string.Empty;

            List<string> parts = new()
            {
                $"1/{item.Chance}",
                QuoteIfNeeded(item.Item.ItemName)
            };

            if (item.Amount > 1)
            {
                parts.Add($"Amount={item.Amount}");
            }

            AddOptions(parts, item.DropSet, item.PartOnly, item.EasterEvent, item.Mir2Gold);

            return indent + string.Join(" ", parts);
        }

        private static void AddOptions(List<string> parts, int dropSet, bool partOnly, bool easterEvent, bool mir2Gold)
        {
            if (dropSet > 0) parts.Add($"DropSet={dropSet}");
            if (partOnly) parts.Add("PartOnly");
            if (easterEvent) parts.Add("EasterEvent");
            if (mir2Gold) parts.Add("Mir2Gold");
        }

        private static string QuoteIfNeeded(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "\"\"";
            return value.Any(char.IsWhiteSpace) ? $"\"{value.Replace("\"", "\\\"")}\"" : value;
        }

        private static string SafeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            foreach (char c in Path.GetInvalidFileNameChars())
                value = value.Replace(c.ToString(), string.Empty);

            return value.Trim();
        }

        private static string GetUniqueFileName(string fileName, int index, HashSet<string> usedFileNames)
        {
            if (usedFileNames.Add(fileName))
                return fileName;

            string indexedName = $"{fileName}#{index}";
            if (usedFileNames.Add(indexedName))
                return indexedName;

            int suffix = 2;
            while (!usedFileNames.Add($"{indexedName}-{suffix}"))
                suffix++;

            return $"{indexedName}-{suffix}";
        }
    }

    public sealed class Mir2DropExportReport
    {
        public int MonstersExported { get; set; }
        public int DropLines { get; set; }
        public int GroupLines { get; set; }
        public int GroupItemLines { get; set; }
    }
}
