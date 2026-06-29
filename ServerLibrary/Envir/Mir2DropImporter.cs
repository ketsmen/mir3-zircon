using Library.SystemModels;
using Library;
using MirDB;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Server.Envir
{
    public sealed class Mir2DropImporter
    {
        private static readonly Regex InsertRegex = new(@"^#INSERT\s+\[(.*?)\]\s*(?:(?:;|//).*)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private readonly Session _session;
        private readonly Dictionary<string, MonsterInfo> _monsters;
        private readonly Dictionary<int, MonsterInfo> _monstersByIndex;
        private readonly Dictionary<string, ItemInfo> _items;
        private readonly ItemInfo _goldInfo;

        public Mir2DropImporter(Session session)
        {
            _session = session;
            List<MonsterInfo> monsters = session.GetCollection<MonsterInfo>().Binding
                .Where(x => x != null)
                .ToList();

            _monsters = monsters
                .GroupBy(x => NormalizeName(x.MonsterName), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
            _monstersByIndex = monsters
                .GroupBy(x => x.Index)
                .ToDictionary(x => x.Key, x => x.First());
            _items = session.GetCollection<ItemInfo>().Binding
                .Where(x => x != null)
                .GroupBy(x => NormalizeName(x.ItemName), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
            _goldInfo = session.GetCollection<CurrencyInfo>().Binding.FirstOrDefault(x => x.Type == CurrencyType.Gold)?.DropItem;
        }

        public Mir2DropImportReport Import(string dropDirectory, bool replaceExisting)
        {
            if (string.IsNullOrWhiteSpace(dropDirectory))
                throw new ArgumentException("掉落目录不能为空。", nameof(dropDirectory));

            if (!Directory.Exists(dropDirectory))
                throw new DirectoryNotFoundException(dropDirectory);

            Mir2DropImportReport report = new();
            HashSet<string> mappedFiles = new(StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<string, string> pair in LoadDropPathMap(dropDirectory, report))
            {
                if (!TryResolveDropFile(dropDirectory, pair.Value, report, out string path))
                    continue;

                mappedFiles.Add(Path.GetFullPath(path));

                ImportFile(dropDirectory, path, pair.Key, replaceExisting, report);
            }

            foreach (string file in EnumerateDropTextFiles(dropDirectory, report))
            {
                string fileName = Path.GetFileName(file);
                if (fileName.Equals("DropPathMap.txt", StringComparison.OrdinalIgnoreCase)) continue;
                if (fileName.Equals("掉落格式示例.txt", StringComparison.OrdinalIgnoreCase)) continue;
                if (mappedFiles.Contains(Path.GetFullPath(file))) continue;

                ImportFile(dropDirectory, file, Path.GetFileNameWithoutExtension(file), replaceExisting, report);
            }

            return report;
        }

        public Mir2DropOverrideLoadResult BuildCompiledOverrides(string dropDirectory)
        {
            if (string.IsNullOrWhiteSpace(dropDirectory))
                throw new ArgumentException("掉落目录不能为空。", nameof(dropDirectory));

            if (!Directory.Exists(dropDirectory))
                throw new DirectoryNotFoundException(dropDirectory);

            Mir2DropOverrideLoadResult result = new();
            HashSet<string> mappedFiles = new(StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<string, string> pair in LoadDropPathMap(dropDirectory, result.Report))
            {
                if (!TryResolveDropFile(dropDirectory, pair.Value, result.Report, out string path))
                    continue;

                mappedFiles.Add(Path.GetFullPath(path));

                BuildCompiledOverrideFile(dropDirectory, path, pair.Key, result);
            }

            foreach (string file in EnumerateDropTextFiles(dropDirectory, result.Report))
            {
                string fileName = Path.GetFileName(file);
                if (fileName.Equals("DropPathMap.txt", StringComparison.OrdinalIgnoreCase)) continue;
                if (fileName.Equals("掉落格式示例.txt", StringComparison.OrdinalIgnoreCase)) continue;
                if (mappedFiles.Contains(Path.GetFullPath(file))) continue;

                BuildCompiledOverrideFile(dropDirectory, file, Path.GetFileNameWithoutExtension(file), result);
            }

            return result;
        }

        public Mir2DropImportReport ImportInline(MonsterInfo monster, IEnumerable<string> lines, bool replaceExisting)
        {
            if (monster == null) throw new ArgumentNullException(nameof(monster));

            Mir2DropImportReport report = new();
            report.MonstersVisited++;

            int beforeFailures = report.Failures.Count;
            int beforeMissingItems = report.MissingItems.Count;
            List<Mir2DropNode> nodes = ParseNodes(lines?.ToList() ?? [], monster.MonsterName, report);

            if (replaceExisting && HasImportBlockingParseIssues(report, beforeFailures, beforeMissingItems))
            {
                report.Failures.Add($"{monster.MonsterName}: 覆盖导入已跳过，解析存在错误或缺失物品，已保留原掉落。");
                return report;
            }

            if (replaceExisting)
            {
                ClearExistingDrops(monster);
                report.MonstersCleared++;
            }

            foreach (Mir2DropNode node in nodes)
                ImportNode(monster, null, node, report);

            report.MonstersImported++;
            return report;
        }

        public Mir2DropOverrideLoadResult BuildCompiledInline(MonsterInfo monster, IEnumerable<string> lines)
        {
            if (monster == null) throw new ArgumentNullException(nameof(monster));

            Mir2DropOverrideLoadResult result = new();
            Mir2DropImportReport report = result.Report;
            report.MonstersVisited++;

            List<CompiledDropEntry> entries = [];
            List<CompiledDropGroup> groups = [];

            int beforeFailures = report.Failures.Count;
            int beforeMissingItems = report.MissingItems.Count;
            List<Mir2DropNode> nodes = ParseNodes(lines?.ToList() ?? [], monster.MonsterName, report);
            if (HasImportBlockingParseIssues(report, beforeFailures, beforeMissingItems))
            {
                report.Failures.Add($"{monster.MonsterName}: 内联掉落覆盖已跳过，解析存在错误或缺失物品，已保留数据库掉落。");
                return result;
            }

            foreach (Mir2DropNode node in nodes)
            {
                if (node.Children != null)
                {
                    CompiledDropGroup group = CompileGroup(node, report);
                    if (group.HasDrops)
                    {
                        groups.Add(group);
                        report.GroupsImported++;
                    }

                    continue;
                }

                CompiledDropEntry? entry = CompileEntry(node);
                if (entry == null) continue;

                entries.Add(entry.Value);
                report.DropsImported++;
            }

            result.Overrides[monster] = new CompiledMonsterDrop(entries.ToArray(), groups.ToArray());
            report.MonstersImported++;
            return result;
        }

        private void ImportFile(string dropDirectory, string file, string monsterName, bool replaceExisting, Mir2DropImportReport report)
        {
            if (!File.Exists(file))
            {
                report.Failures.Add($"找不到掉落文件: {file}");
                return;
            }

            if (!TryGetMonster(monsterName, out MonsterInfo monster))
            {
                report.MissingMonsters.Add(monsterName);
                return;
            }

            report.MonstersVisited++;

            int beforeFailures = report.Failures.Count;
            int beforeMissingItems = report.MissingItems.Count;
            List<string> lines = ExpandInserts(file, dropDirectory, report, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            List<Mir2DropNode> nodes = ParseNodes(lines, monsterName, report);

            if (replaceExisting && HasImportBlockingParseIssues(report, beforeFailures, beforeMissingItems))
            {
                report.Failures.Add($"{monsterName}: 覆盖导入已跳过，解析存在错误或缺失物品，已保留原掉落。来源 {Path.GetFileName(file)}");
                return;
            }

            if (replaceExisting)
            {
                ClearExistingDrops(monster);
                report.MonstersCleared++;
            }

            foreach (Mir2DropNode node in nodes)
                ImportNode(monster, null, node, report);

            report.MonstersImported++;
        }

        private void BuildCompiledOverrideFile(string dropDirectory, string file, string monsterName, Mir2DropOverrideLoadResult result)
        {
            Mir2DropImportReport report = result.Report;

            if (!File.Exists(file))
            {
                report.Failures.Add($"找不到掉落文件: {file}");
                return;
            }

            if (!TryGetMonster(monsterName, out MonsterInfo monster))
            {
                report.MissingMonsters.Add(monsterName);
                return;
            }

            report.MonstersVisited++;

            int beforeFailures = report.Failures.Count;
            int beforeMissingItems = report.MissingItems.Count;
            List<string> lines = ExpandInserts(file, dropDirectory, report, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            List<Mir2DropNode> nodes = ParseNodes(lines, monsterName, report);
            if (HasImportBlockingParseIssues(report, beforeFailures, beforeMissingItems))
            {
                report.Failures.Add($"{monsterName}: 文本掉落覆盖已跳过，解析存在错误或缺失物品，已保留数据库掉落。来源 {Path.GetFileName(file)}");
                return;
            }

            List<CompiledDropEntry> entries = [];
            List<CompiledDropGroup> groups = [];

            foreach (Mir2DropNode node in nodes)
            {
                if (node.Children != null)
                {
                    CompiledDropGroup group = CompileGroup(node, report);
                    if (group.HasDrops)
                    {
                        groups.Add(group);
                        report.GroupsImported++;
                    }

                    continue;
                }

                CompiledDropEntry? entry = CompileEntry(node);
                if (entry == null) continue;

                entries.Add(entry.Value);
                report.DropsImported++;
            }

            result.Overrides[monster] = new CompiledMonsterDrop(entries.ToArray(), groups.ToArray());
            report.MonstersImported++;
        }

        private static Dictionary<string, string> LoadDropPathMap(string dropDirectory, Mir2DropImportReport report)
        {
            Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);
            string path = Path.Combine(dropDirectory, "DropPathMap.txt");
            if (!File.Exists(path)) return result;

            if (!TryReadAllLines(path, report, out string[] lines))
                return result;

            for (int i = 0; i < lines.Length; i++)
            {
                string rawLine = lines[i];
                string line = CleanLine(rawLine);
                if (string.IsNullOrWhiteSpace(line)) continue;

                string[] parts = line.Contains('=') ? line.Split('=', 2) : line.Split(',', 2);
                if (parts.Length != 2)
                {
                    report.Failures.Add($"DropPathMap.txt:{i + 1} 行格式无效: {line}");
                    continue;
                }

                string monsterName = parts[0].Trim();
                string dropPath = parts[1].Trim();

                if (string.IsNullOrWhiteSpace(monsterName))
                {
                    report.Failures.Add($"DropPathMap.txt:{i + 1} 怪物名不能为空。");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(dropPath))
                {
                    report.Failures.Add($"DropPathMap.txt:{i + 1} `{monsterName}` 的掉落文件路径不能为空。");
                    continue;
                }

                if (result.ContainsKey(monsterName))
                {
                    report.Failures.Add($"DropPathMap.txt:{i + 1} 怪物 `{monsterName}` 存在重复映射，已保留首次映射。");
                    continue;
                }

                result[monsterName] = dropPath;
            }

            report.DropPathMappings = result.Count;
            return result;
        }

        private static List<string> EnumerateDropTextFiles(string dropDirectory, Mir2DropImportReport report)
        {
            try
            {
                return Directory.EnumerateFiles(dropDirectory, "*.txt").ToList();
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
            {
                report.Failures.Add($"枚举掉落目录失败 `{dropDirectory}`: {ex.Message}");
                return [];
            }
        }

        private static bool TryResolveDropFile(string dropDirectory, string dropPath, Mir2DropImportReport report, out string path)
        {
            path = null;

            if (string.IsNullOrWhiteSpace(dropPath))
            {
                report.Failures.Add("掉落文件路径不能为空。");
                return false;
            }

            try
            {
                string root = Path.GetFullPath(dropDirectory);
                string candidate = Path.Combine(root, dropPath);
                if (!Path.HasExtension(candidate))
                    candidate += ".txt";

                string fullPath = Path.GetFullPath(candidate);
                if (!IsPathInsideDirectory(root, fullPath))
                {
                    report.Failures.Add($"掉落文件路径不能指向目录外: {dropPath}");
                    return false;
                }

                path = fullPath;
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
            {
                report.Failures.Add($"掉落文件路径无效 `{dropPath}`: {ex.Message}");
                return false;
            }
        }

        private static bool IsPathInsideDirectory(string root, string path)
        {
            string normalizedRoot = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedPath = Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            return string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
                   normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                   normalizedPath.StartsWith(normalizedRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private void ClearExistingDrops(MonsterInfo monster)
        {
            for (int i = monster.Drops.Count - 1; i >= 0; i--)
                monster.Drops[i].Delete();

            for (int i = monster.DropGroups.Count - 1; i >= 0; i--)
                monster.DropGroups[i].Delete();
        }

        private static bool HasImportBlockingParseIssues(Mir2DropImportReport report, int beforeFailures, int beforeMissingItems)
        {
            return report.Failures.Count > beforeFailures ||
                   report.MissingItems.Count > beforeMissingItems;
        }

        private List<string> ExpandInserts(string path, string dropDirectory, Mir2DropImportReport report, HashSet<string> stack)
        {
            if (!TryGetFullPath(path, "掉落文件路径", report, out string fullPath))
                return [];

            if (!stack.Add(fullPath))
            {
                report.Failures.Add($"循环 #INSERT: {fullPath}");
                return [];
            }

            try
            {
                List<string> result = [];
                if (!TryReadAllLines(fullPath, report, out string[] lines))
                    return result;

                foreach (string rawLine in lines)
                {
                    string line = CleanLine(rawLine);
                    Match match = InsertRegex.Match(line);
                    if (!match.Success)
                    {
                        if (line.StartsWith("#INSERT", StringComparison.OrdinalIgnoreCase))
                        {
                            report.Failures.Add($"{fullPath}: #INSERT 格式必须是整行 #INSERT [file]。");
                            continue;
                        }

                        result.Add(rawLine);
                        continue;
                    }

                    string includeName = match.Groups[1].Value;
                    if (!TryResolveDropFile(dropDirectory, includeName, report, out string includePath))
                        continue;

                    if (!File.Exists(includePath))
                    {
                        report.Failures.Add($"找不到 #INSERT 文件: {includeName} 来源 {fullPath}");
                        continue;
                    }

                    report.InsertFiles++;
                    result.AddRange(ExpandInserts(includePath, dropDirectory, report, stack));
                }

                return result;
            }
            finally
            {
                stack.Remove(fullPath);
            }
        }

        private List<Mir2DropNode> ParseNodes(List<string> lines, string monsterName, Mir2DropImportReport report)
        {
            List<Mir2DropNode> nodes = [];
            int index = 0;

            while (index < lines.Count)
            {
                string line = CleanLine(lines[index]);
                index++;

                if (string.IsNullOrWhiteSpace(line)) continue;

                Mir2DropNode node = ParseNode(line, lines, ref index, monsterName, report);
                if (node != null) nodes.Add(node);
            }

            return nodes;
        }

        private Mir2DropNode ParseNode(string line, List<string> lines, ref int index, string monsterName, Mir2DropImportReport report)
        {
            List<string> parts = TokenizeLine(line);
            if (parts.Count < 2)
            {
                report.Failures.Add($"{monsterName}: 无法解析掉落行 `{line}`");
                return null;
            }

            if (!TryParseChance(parts[0], out int chance))
            {
                report.Failures.Add($"{monsterName}: 概率格式无效 `{line}`");
                return null;
            }

            if (IsGroupToken(parts[1]))
                return ParseGroup(parts[1], chance, parts.Skip(2), lines, ref index, monsterName, report);

            Mir2DropNode node = new()
            {
                Chance = chance
            };

            if (IsGoldToken(parts[1]))
            {
                int gold = 1;
                IEnumerable<string> options = parts.Skip(2);

                if (parts.Count >= 3 && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedGold))
                {
                    gold = parsedGold;
                    options = parts.Skip(3);
                }

                node.Item = _goldInfo;
                if (node.Item == null)
                {
                    report.Failures.Add($"{monsterName}: 数据库没有配置金币 DropItem `{line}`");
                    return null;
                }

                node.Amount = gold;
                node.IsMir2Gold = true;
                if (!ApplyOptions(node, options, monsterName, line, report))
                    return null;

                if (node.Amount <= 0)
                {
                    report.Failures.Add($"{monsterName}: 金币数量无效 `{line}`");
                    return null;
                }

                report.GoldLines++;
                if (node.IsQuestOnly)
                    report.QuestOnlyLines.Add($"{monsterName}: {line}");
                return node;
            }

            if (!ApplyOptions(node, parts.Skip(2), monsterName, line, report))
                return null;

            string itemName = NormalizeName(parts[1]);
            if (!_items.TryGetValue(itemName, out ItemInfo item))
            {
                report.MissingItems.Add(parts[1]);
                return null;
            }

            node.Item = item;
            if (node.Amount <= 0)
                node.Amount = 1;

            if (node.IsQuestOnly)
                report.QuestOnlyLines.Add($"{monsterName}: {line}");

            return node;
        }

        private Mir2DropNode ParseGroup(string token, int chance, IEnumerable<string> options, List<string> lines, ref int index, string monsterName, Mir2DropImportReport report)
        {
            DropGroupMode mode = DropGroupMode.AllMatched;
            if (token.EndsWith("*", StringComparison.Ordinal))
                mode = DropGroupMode.RandomOneMatched;
            else if (token.EndsWith("^", StringComparison.Ordinal))
                mode = DropGroupMode.FirstMatched;

            while (index < lines.Count && string.IsNullOrWhiteSpace(CleanLine(lines[index])))
                index++;

            if (index >= lines.Count || CleanLine(lines[index]) != "{")
            {
                report.Failures.Add($"{monsterName}: GROUP 缺少 `{{`");
                return null;
            }

            index++;

            Mir2DropNode group = new()
            {
                Chance = chance,
                Mode = mode,
                Children = []
            };

            if (!ApplyOptions(group, options, monsterName, token, report))
            {
                SkipGroupBody(lines, ref index);
                return null;
            }

            bool closed = false;

            while (index < lines.Count)
            {
                string line = CleanLine(lines[index]);
                index++;

                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line == "}")
                {
                    closed = true;
                    break;
                }

                Mir2DropNode child = ParseNode(line, lines, ref index, monsterName, report);
                if (child != null)
                {
                    child.Sequence = group.Children.Count + 1;
                    group.Children.Add(child);
                }
            }

            if (!closed)
            {
                report.Failures.Add($"{monsterName}: GROUP 缺少 `}}`");
                return null;
            }

            if (group.Children.Count == 0)
            {
                report.Failures.Add($"{monsterName}: 空 GROUP");
                return null;
            }

            report.GroupLines++;
            return group;
        }

        private static void SkipGroupBody(List<string> lines, ref int index)
        {
            int depth = 1;

            while (index < lines.Count && depth > 0)
            {
                string line = CleanLine(lines[index]);
                index++;

                if (line == "{")
                    depth++;
                else if (line == "}")
                    depth--;
            }
        }

        private static bool IsGroupToken(string token)
        {
            return token.Equals("GROUP", StringComparison.OrdinalIgnoreCase) ||
                   token.Equals("GROUP*", StringComparison.OrdinalIgnoreCase) ||
                   token.Equals("GROUP^", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsGoldToken(string token)
        {
            if (token.Equals("Gold", StringComparison.OrdinalIgnoreCase))
                return true;

            return _goldInfo != null &&
                   NormalizeName(token).Equals(NormalizeName(_goldInfo.ItemName), StringComparison.OrdinalIgnoreCase);
        }

        private bool ImportNode(MonsterInfo monster, DropGroupInfo parent, Mir2DropNode node, Mir2DropImportReport report)
        {
            if (node.Children != null)
            {
                DropGroupInfo group = _session.GetCollection<DropGroupInfo>().CreateNewObject();
                group.Monster = monster;
                group.Parent = parent;
                group.Mode = node.Mode;
                group.Chance = node.Chance;
                group.Sequence = node.Sequence;
                group.DropSet = node.DropSet;
                group.EasterEvent = node.EasterEvent;

                bool imported = false;
                foreach (Mir2DropNode child in node.Children)
                    imported |= ImportNode(monster, group, child, report);

                if (!imported)
                {
                    group.Delete();
                    return false;
                }

                report.GroupsImported++;
                return true;
            }

            if (node.Item == null) return false;

            if (node.IsQuestOnly)
                return false;

            if (parent == null)
            {
                DropInfo drop = _session.GetCollection<DropInfo>().CreateNewObject();
                drop.Monster = monster;
                drop.Item = node.Item;
                drop.Chance = node.Chance;
                drop.Amount = node.Amount;
                drop.Mir2Gold = node.IsMir2Gold;
                drop.DropSet = node.DropSet;
                drop.PartOnly = node.PartOnly;
                drop.EasterEvent = node.EasterEvent;

                report.DropsImported++;
                return true;
            }

            DropGroupItemInfo item = _session.GetCollection<DropGroupItemInfo>().CreateNewObject();
            item.Group = parent;
            item.Item = node.Item;
            item.Chance = node.Chance;
            item.Amount = node.Amount;
            item.Mir2Gold = node.IsMir2Gold;
            item.PartOnly = node.PartOnly;
            item.DropSet = node.DropSet;
            item.EasterEvent = node.EasterEvent;
            item.Sequence = node.Sequence;
            report.GroupItemsImported++;
            return true;
        }

        private static CompiledDropGroup CompileGroup(Mir2DropNode node, Mir2DropImportReport report)
        {
            List<CompiledDropGroupChild> children = [];
            int fallbackSequence = 0;

            foreach (Mir2DropNode child in node.Children)
            {
                fallbackSequence++;

                if (child.Children != null)
                {
                    CompiledDropGroup childGroup = CompileGroup(child, report);
                    if (!childGroup.HasDrops) continue;

                    children.Add(CompiledDropGroupChild.FromGroup(childGroup, GetSortSequence(child.Sequence, fallbackSequence), fallbackSequence));
                    report.GroupsImported++;
                    continue;
                }

                CompiledDropEntry? entry = CompileEntry(child);
                if (entry == null) continue;

                children.Add(CompiledDropGroupChild.FromEntry(entry.Value, GetSortSequence(child.Sequence, fallbackSequence), fallbackSequence));
                report.GroupItemsImported++;
            }

            return new CompiledDropGroup(
                node.Mode,
                node.Chance,
                node.DropSet,
                node.Sequence,
                node.EasterEvent,
                children.OrderBy(x => x.SortSequence).ThenBy(x => x.Index).ToArray());
        }

        private static CompiledDropEntry? CompileEntry(Mir2DropNode node)
        {
            if (node.Item == null) return null;
            if (node.IsQuestOnly) return null;

            return new CompiledDropEntry(
                node.Item,
                node.Chance,
                node.Amount,
                node.DropSet,
                node.PartOnly,
                node.EasterEvent,
                node.IsMir2Gold,
                node.Sequence);
        }

        private static int GetSortSequence(int sequence, int fallback)
        {
            return sequence > 0 ? sequence : fallback;
        }

        private static bool ApplyOptions(Mir2DropNode node, IEnumerable<string> options, string monsterName, string line, Mir2DropImportReport report)
        {
            foreach (string option in options)
            {
                if (string.IsNullOrWhiteSpace(option)) continue;
                if (option.Equals("Q", StringComparison.OrdinalIgnoreCase))
                {
                    node.IsQuestOnly = true;
                    continue;
                }

                if (option.Equals("PartOnly", StringComparison.OrdinalIgnoreCase))
                {
                    node.PartOnly = true;
                    continue;
                }

                if (option.Equals("EasterEvent", StringComparison.OrdinalIgnoreCase))
                {
                    node.EasterEvent = true;
                    continue;
                }

                if (option.Equals("Mir2Gold", StringComparison.OrdinalIgnoreCase))
                {
                    node.IsMir2Gold = true;
                    continue;
                }

                string[] pair = option.Split('=', 2);
                if (pair.Length != 2)
                {
                    report.Failures.Add($"{monsterName}: 未知掉落选项 `{option}` `{line}`");
                    return false;
                }

                string key = pair[0];
                string value = pair[1];

                if (key.Equals("Amount", StringComparison.OrdinalIgnoreCase))
                {
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int amount) || amount <= 0)
                    {
                        report.Failures.Add($"{monsterName}: Amount 无效 `{line}`");
                        return false;
                    }

                    node.Amount = amount;
                    continue;
                }

                if (key.Equals("DropSet", StringComparison.OrdinalIgnoreCase))
                {
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int dropSet) || dropSet < 0)
                    {
                        report.Failures.Add($"{monsterName}: DropSet 无效 `{line}`");
                        return false;
                    }

                    node.DropSet = dropSet;
                    continue;
                }

                if (key.Equals("PartOnly", StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryParseBool(value, out bool partOnly))
                    {
                        report.Failures.Add($"{monsterName}: PartOnly 无效 `{line}`");
                        return false;
                    }

                    node.PartOnly = partOnly;
                    continue;
                }

                if (key.Equals("EasterEvent", StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryParseBool(value, out bool easterEvent))
                    {
                        report.Failures.Add($"{monsterName}: EasterEvent 无效 `{line}`");
                        return false;
                    }

                    node.EasterEvent = easterEvent;
                    continue;
                }

                if (key.Equals("Mir2Gold", StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryParseBool(value, out bool mir2Gold))
                    {
                        report.Failures.Add($"{monsterName}: Mir2Gold 无效 `{line}`");
                        return false;
                    }

                    node.IsMir2Gold = mir2Gold;
                    continue;
                }

                report.Failures.Add($"{monsterName}: 未知掉落选项 `{key}` `{line}`");
                return false;
            }

            return true;
        }

        private static bool TryParseBool(string value, out bool result)
        {
            if (bool.TryParse(value, out result)) return true;

            if (value == "1")
            {
                result = true;
                return true;
            }

            if (value == "0")
            {
                result = false;
                return true;
            }

            return false;
        }

        private static bool TryParseChance(string text, out int chance)
        {
            chance = 0;

            string[] parts = text.Split('/');
            if (parts.Length != 2) return false;
            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int numerator) || numerator != 1) return false;
            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out chance)) return false;

            return chance > 0;
        }

        private static string CleanLine(string line)
        {
            if (line == null) return string.Empty;

            string trimmed = StripComment(line).Trim();
            if (trimmed.StartsWith(";", StringComparison.Ordinal)) return string.Empty;

            return trimmed;
        }

        private static bool TryReadAllLines(string path, Mir2DropImportReport report, out string[] lines)
        {
            try
            {
                lines = File.ReadAllLines(path);
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
            {
                lines = Array.Empty<string>();
                report.Failures.Add($"读取掉落文件失败 `{path}`: {ex.Message}");
                return false;
            }
        }

        private static string StripComment(string line)
        {
            bool quoted = false;
            bool escaped = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (quoted && c == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (c == '"')
                {
                    quoted = !quoted;
                    continue;
                }

                if (quoted) continue;

                if (c == ';' && IsSemicolonCommentStart(line, i))
                    return line[..i];

                if (c == '/' && i + 1 < line.Length && line[i + 1] == '/' && IsSlashCommentStart(line, i))
                    return line[..i];
            }

            return line;
        }

        private static bool TryGetFullPath(string path, string description, Mir2DropImportReport report, out string fullPath)
        {
            try
            {
                fullPath = Path.GetFullPath(path);
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
            {
                fullPath = null;
                report.Failures.Add($"{description}无效 `{path}`: {ex.Message}");
                return false;
            }
        }

        private static bool IsSlashCommentStart(string line, int index)
        {
            return index == 0 || char.IsWhiteSpace(line[index - 1]);
        }

        private static bool IsSemicolonCommentStart(string line, int index)
        {
            if (index == 0)
                return true;

            if (char.IsWhiteSpace(line[index - 1]))
                return true;

            for (int i = 0; i < index; i++)
                if (!char.IsWhiteSpace(line[i]))
                    return false;

            return true;
        }

        private static string NormalizeName(string value)
        {
            return Regex.Replace(value ?? string.Empty, @"\s+", string.Empty);
        }

        private bool TryGetMonster(string monsterName, out MonsterInfo monster)
        {
            if (_monsters.TryGetValue(NormalizeName(monsterName), out monster))
                return true;

            return TryGetExportedIndexedMonster(monsterName, out monster);
        }

        private bool TryGetExportedIndexedMonster(string monsterName, out MonsterInfo monster)
        {
            monster = null;

            if (string.IsNullOrWhiteSpace(monsterName)) return false;

            int hashIndex = monsterName.LastIndexOf('#');
            if (hashIndex <= 0 || hashIndex >= monsterName.Length - 1) return false;

            string suffix = monsterName[(hashIndex + 1)..];
            int duplicateSuffixIndex = suffix.IndexOf('-');
            string indexText = duplicateSuffixIndex < 0 ? suffix : suffix[..duplicateSuffixIndex];

            if (!int.TryParse(indexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int monsterIndex)) return false;
            if (!_monstersByIndex.TryGetValue(monsterIndex, out monster)) return false;

            string baseName = monsterName[..hashIndex];
            string monsterFileName = SafeFileName(monster.MonsterName);

            if (NormalizeName(baseName).Equals(NormalizeName(monsterFileName), StringComparison.OrdinalIgnoreCase))
                return true;

            monster = null;
            return false;
        }

        private static string SafeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            foreach (char c in Path.GetInvalidFileNameChars())
                value = value.Replace(c.ToString(), string.Empty);

            return value.Trim();
        }

        private static List<string> TokenizeLine(string line)
        {
            List<string> tokens = [];
            if (string.IsNullOrWhiteSpace(line)) return tokens;

            StringBuilder current = new();
            bool quoted = false, escaped = false;

            foreach (char c in line)
            {
                if (escaped)
                {
                    current.Append(c);
                    escaped = false;
                    continue;
                }

                if (quoted && c == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (c == '"')
                {
                    quoted = !quoted;
                    continue;
                }

                if (!quoted && char.IsWhiteSpace(c))
                {
                    if (current.Length > 0)
                    {
                        tokens.Add(current.ToString());
                        current.Clear();
                    }

                    continue;
                }

                current.Append(c);
            }

            if (current.Length > 0)
                tokens.Add(current.ToString());

            return tokens;
        }

        private sealed class Mir2DropNode
        {
            public int Chance;
            public ItemInfo Item;
            public int Amount = 1;
            public bool IsQuestOnly;
            public bool IsMir2Gold;
            public bool PartOnly;
            public bool EasterEvent;
            public int DropSet;
            public int Sequence;
            public DropGroupMode Mode;
            public List<Mir2DropNode> Children;
        }
    }

    public sealed class Mir2DropImportReport
    {
        public int MonstersVisited { get; set; }
        public int MonstersCleared { get; set; }
        public int MonstersImported { get; set; }
        public int DropsImported { get; set; }
        public int GroupsImported { get; set; }
        public int GroupItemsImported { get; set; }
        public int GoldLines { get; set; }
        public int GroupLines { get; set; }
        public int InsertFiles { get; set; }
        public int DropPathMappings { get; set; }
        public List<string> MissingMonsters { get; } = [];
        public List<string> MissingItems { get; } = [];
        public List<string> QuestOnlyLines { get; } = [];
        public List<string> Failures { get; } = [];

        public string ToDisplayText()
        {
            return
                $"怪物文件: {MonstersVisited}\r\n" +
                $"已清空怪物: {MonstersCleared}\r\n" +
                $"已导入怪物: {MonstersImported}\r\n" +
                $"普通掉落: {DropsImported}\r\n" +
                $"组掉落: {GroupsImported}\r\n" +
                $"组内物品: {GroupItemsImported}\r\n" +
                $"金币行: {GoldLines}\r\n" +
                $"GROUP 行: {GroupLines}\r\n" +
                $"#INSERT: {InsertFiles}\r\n" +
                $"DropPath 映射: {DropPathMappings}\r\n" +
                $"任务专属行: {QuestOnlyLines.Count}\r\n" +
                $"缺失怪物: {MissingMonsters.Distinct(StringComparer.OrdinalIgnoreCase).Count()}\r\n" +
                $"缺失物品: {MissingItems.Distinct(StringComparer.OrdinalIgnoreCase).Count()}\r\n" +
                $"失败行: {Failures.Count}";
        }
    }

    public sealed class Mir2DropOverrideLoadResult
    {
        public Mir2DropImportReport Report { get; } = new();
        public Dictionary<MonsterInfo, CompiledMonsterDrop> Overrides { get; } = [];
    }
}
