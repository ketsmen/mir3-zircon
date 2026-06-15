using Library;
using Library.Network.ServerPackets;
using Library.SystemModels;
using MirDB;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Server.Envir
{
    public sealed class TextNpcImporter
    {
        private static readonly Regex IncludeRegex = new(@"#(?:INSERT|INCLUDE)\s+\[(.*?)\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex PageRegex = new(@"^\[\s*(?<Key>@?[\w\-.]+)\s*\]$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ButtonRegex = new(@"\<(?<Text>[^<>/]+)\/(?<Target>@?[^<>]+)\>", RegexOptions.Compiled);

        private readonly Dictionary<string, MapInfo> _maps = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ItemInfo> _items = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, CurrencyInfo> _currencies = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, NPCInfo> _npcsByIndex;

        public TextNpcImporter(Session session)
        {
            foreach (MapInfo map in session.GetCollection<MapInfo>().Binding)
            {
                AddLookup(_maps, map.FileName, map);
                AddLookup(_maps, Path.GetFileNameWithoutExtension(map.FileName), map);
                AddLookup(_maps, map.Description, map);
            }

            foreach (ItemInfo item in session.GetCollection<ItemInfo>().Binding)
                AddLookup(_items, item.ItemName, item);

            foreach (CurrencyInfo currency in session.GetCollection<CurrencyInfo>().Binding)
            {
                AddLookup(_currencies, currency.Name, currency);
                AddLookup(_currencies, currency.Type.ToString(), currency);
            }

            _npcsByIndex = session.GetCollection<NPCInfo>().Binding.ToDictionary(x => x.Index);
        }

        public TextNpcLoadResult Build(string npcDirectory, bool strictMode)
        {
            if (string.IsNullOrWhiteSpace(npcDirectory))
                throw new ArgumentException("NPC 文本目录不能为空。", nameof(npcDirectory));

            TextNpcLoadResult result = new();

            string infoPath = Path.Combine(npcDirectory, "NPCInfo.txt");
            if (!File.Exists(infoPath))
            {
                result.Report.Failures.Add($"找不到 NPCInfo.txt: {infoPath}");
                return result;
            }

            HashSet<string> duplicateKeys = new(StringComparer.OrdinalIgnoreCase);
            int textIndex = 1;

            foreach (TextNpcScriptLine infoLine in ReadCleanLines(infoPath, npcDirectory, result.Report, []))
            {
                result.Report.InfoLines++;

                int beforeFailures = result.Report.Failures.Count;
                CompiledTextNpcInfo info = ParseNpcInfoLine(infoLine, npcDirectory, textIndex, result.Report);

                if (info == null)
                    continue;

                string duplicateKey = $"{info.Map.FileName}:{info.Location.X}:{info.Location.Y}:{info.NPCName}";
                if (!duplicateKeys.Add(duplicateKey))
                {
                    result.Report.Failures.Add($"{infoLine.Source}: 重复 NPC 定义 `{duplicateKey}`");
                    continue;
                }

                if (info.OverrideIndex > 0 && !_npcsByIndex.TryGetValue(info.OverrideIndex, out info.OverrideNPC))
                {
                    result.Report.Failures.Add($"{infoLine.Source}: OverrideIndex={info.OverrideIndex} 找不到数据库 NPC。");
                    continue;
                }

                if (!File.Exists(info.ScriptPath))
                {
                    result.Report.Failures.Add($"{infoLine.Source}: 找不到脚本文件 `{info.ScriptFile}`。");
                    continue;
                }

                info.Script = CompileScript(info.ScriptPath, npcDirectory, result.Report);
                if (info.Script == null || info.Script.EntryPage == null)
                {
                    result.Report.Failures.Add($"{infoLine.Source}: 脚本 `{info.ScriptFile}` 没有可用的 [@MAIN] 页面。");
                    continue;
                }

                if (strictMode && result.Report.Failures.Count > beforeFailures)
                {
                    result.Report.NpcsSkipped++;
                    continue;
                }

                result.NPCs.Add(info);
                if (info.OverrideNPC != null)
                    result.Overrides[info.OverrideNPC] = info;

                textIndex++;
            }

            result.Report.NpcsLoaded = result.NPCs.Count;
            result.Report.OverrideNpcs = result.Overrides.Count;

            return result;
        }

        private CompiledTextNpcInfo ParseNpcInfoLine(TextNpcScriptLine line, string npcDirectory, int textIndex, TextNpcLoadReport report)
        {
            List<string> parts = TokenizeLine(line.Text);
            if (parts.Count < 7)
            {
                report.Failures.Add($"{line.Source}: NPCInfo 行至少需要 7 列。");
                return null;
            }

            if (!_maps.TryGetValue(NormalizeName(parts[0]), out MapInfo map))
            {
                report.MissingMaps.Add(parts[0]);
                report.Failures.Add($"{line.Source}: 找不到地图 `{parts[0]}`。");
                return null;
            }

            if (!int.TryParse(parts[1], out int x) || !int.TryParse(parts[2], out int y))
            {
                report.Failures.Add($"{line.Source}: NPC 坐标无效 `{parts[1]} {parts[2]}`。");
                return null;
            }

            if (!int.TryParse(parts[4], out int image) || !int.TryParse(parts[5], out int faceImage))
            {
                report.Failures.Add($"{line.Source}: NPC Image/FaceImage 必须是数字。");
                return null;
            }

            CompiledTextNpcInfo info = new()
            {
                TextIndex = textIndex,
                Map = map,
                Location = new Point(x, y),
                NPCName = parts[3],
                Image = image,
                FaceImage = faceImage,
                ScriptFile = parts[6],
                ScriptPath = ResolveScriptPath(npcDirectory, parts[6]),
                MapIcon = MapIcon.None,
                Direction = MirDirection.Down,
                Class = RequiredClass.All,
                Days = DaysOfWeek.Weekday | DaysOfWeek.Weekend
            };

            foreach (string option in parts.Skip(7))
            {
                string[] kv = option.Split('=', 2);
                if (kv.Length != 2)
                {
                    report.Failures.Add($"{line.Source}: NPCInfo 可选参数必须是 Key=Value 格式 `{option}`。");
                    continue;
                }

                string key = kv[0].Trim();
                string value = kv[1].Trim();

                switch (key.ToUpperInvariant())
                {
                    case "MAPICON":
                        if (!Enum.TryParse(value, true, out MapIcon mapIcon))
                            report.Failures.Add($"{line.Source}: MapIcon 无效 `{value}`。");
                        else
                            info.MapIcon = mapIcon;
                        break;
                    case "DIRECTION":
                        if (!Enum.TryParse(value, true, out MirDirection direction))
                            report.Failures.Add($"{line.Source}: Direction 无效 `{value}`。");
                        else
                            info.Direction = direction;
                        break;
                    case "MINLEVEL":
                        if (!int.TryParse(value, out info.MinLevel))
                            report.Failures.Add($"{line.Source}: MinLevel 无效 `{value}`。");
                        break;
                    case "MAXLEVEL":
                        if (!int.TryParse(value, out info.MaxLevel))
                            report.Failures.Add($"{line.Source}: MaxLevel 无效 `{value}`。");
                        break;
                    case "CLASS":
                        if (!TryParseRequiredClass(value, out info.Class))
                            report.Failures.Add($"{line.Source}: Class 无效 `{value}`。");
                        break;
                    case "DAYS":
                        if (!TryParseDays(value, out info.Days))
                            report.Failures.Add($"{line.Source}: Days 无效 `{value}`。");
                        break;
                    case "OVERRIDEINDEX":
                        if (!int.TryParse(value, out info.OverrideIndex))
                            report.Failures.Add($"{line.Source}: OverrideIndex 无效 `{value}`。");
                        break;
                    default:
                        report.Failures.Add($"{line.Source}: 未知 NPCInfo 参数 `{key}`。");
                        break;
                }
            }

            return info;
        }

        private CompiledNpcScript CompileScript(string scriptPath, string npcDirectory, TextNpcLoadReport report)
        {
            List<TextNpcScriptLine> lines = ReadCleanLines(scriptPath, npcDirectory, report, []);
            if (lines.Count == 0) return null;

            CompiledNpcScript script = new()
            {
                ScriptFile = Path.GetFileName(scriptPath)
            };

            CompiledNpcPage currentPage = null;
            TextNpcParseMode mode = TextNpcParseMode.Say;
            CompiledNpcBlock currentBlock = null;

            foreach (TextNpcScriptLine line in lines)
            {
                if (line.Text.Equals("[TRADE]", StringComparison.OrdinalIgnoreCase))
                {
                    mode = TextNpcParseMode.Trade;
                    continue;
                }

                if (line.Text.Equals("[TYPES]", StringComparison.OrdinalIgnoreCase))
                {
                    mode = TextNpcParseMode.Types;
                    continue;
                }

                if (line.Text.Equals("[CURRENCY]", StringComparison.OrdinalIgnoreCase))
                {
                    mode = TextNpcParseMode.Currency;
                    continue;
                }

                Match pageMatch = PageRegex.Match(line.Text);
                if (pageMatch.Success)
                {
                    string key = CompiledNpcScript.NormalizePageKey(pageMatch.Groups["Key"].Value);
                    if (!script.Pages.TryGetValue(key, out currentPage))
                    {
                        currentPage = new CompiledNpcPage { Key = key };
                        script.Pages[key] = currentPage;
                    }

                    currentPage.DialogType = GetDialogType(key);
                    currentBlock = currentPage.MainBlock;
                    mode = TextNpcParseMode.Say;
                    continue;
                }

                if (currentPage == null)
                {
                    report.Failures.Add($"{line.Source}: 页面内容必须写在 [@PAGE] 之后。");
                    continue;
                }

                if (line.Text.StartsWith("#", StringComparison.Ordinal))
                {
                    if (!ApplyDirective(line, currentPage, ref currentBlock, ref mode, report))
                    {
                        report.Failures.Add($"{line.Source}: 未知 NPC 指令 `{line.Text}`。");
                        report.UnknownCommands.Add($"{line.Source}: {line.Text}");
                    }
                    continue;
                }

                switch (mode)
                {
                    case TextNpcParseMode.Check:
                        ParseCheck(line, currentPage.Checks, report);
                        break;
                    case TextNpcParseMode.Say:
                        currentBlock ??= currentPage.MainBlock;
                        currentBlock.SayLines.Add(line.Text);
                        break;
                    case TextNpcParseMode.Act:
                        currentBlock ??= currentPage.MainBlock;
                        ParseAction(line, currentBlock.Actions, report);
                        break;
                    case TextNpcParseMode.Trade:
                        ParseGood(line, currentPage, report);
                        break;
                    case TextNpcParseMode.Types:
                        ParseTypes(line, currentPage, report);
                        break;
                    case TextNpcParseMode.Currency:
                        ParseCurrency(line, currentPage, report);
                        break;
                }
            }

            foreach (CompiledNpcPage page in script.Pages.Values)
            {
                FinaliseBlock(page.MainBlock, page, report);
                FinaliseBlock(page.ElseBlock, page, report);

                ValidateDestinations(script, page, page.MainBlock, report);
                ValidateDestinations(script, page, page.ElseBlock, report);
            }

            report.ScriptFiles++;
            report.PagesLoaded += script.Pages.Count;

            return script;
        }

        private bool ApplyDirective(TextNpcScriptLine line, CompiledNpcPage page, ref CompiledNpcBlock currentBlock, ref TextNpcParseMode mode, TextNpcLoadReport report)
        {
            string directive = line.Text.Trim();

            if (directive.Equals("#IF", StringComparison.OrdinalIgnoreCase))
            {
                mode = TextNpcParseMode.Check;
                currentBlock = page.MainBlock;
                return true;
            }

            if (directive.Equals("#SAY", StringComparison.OrdinalIgnoreCase))
            {
                mode = TextNpcParseMode.Say;
                currentBlock = page.MainBlock;
                return true;
            }

            if (directive.Equals("#ACT", StringComparison.OrdinalIgnoreCase))
            {
                mode = TextNpcParseMode.Act;
                currentBlock = page.MainBlock;
                return true;
            }

            if (directive.Equals("#ELSESAY", StringComparison.OrdinalIgnoreCase))
            {
                mode = TextNpcParseMode.Say;
                currentBlock = page.ElseBlock;
                return true;
            }

            if (directive.Equals("#ELSEACT", StringComparison.OrdinalIgnoreCase))
            {
                mode = TextNpcParseMode.Act;
                currentBlock = page.ElseBlock;
                return true;
            }

            if (directive.StartsWith("#SUCCESS", StringComparison.OrdinalIgnoreCase))
            {
                List<string> parts = TokenizeLine(directive);
                if (parts.Count < 2)
                {
                    report.Failures.Add($"{line.Source}: #SUCCESS 需要目标页面。");
                    return true;
                }

                page.SuccessKey = CompiledNpcScript.NormalizePageKey(parts[1]);
                return true;
            }

            return false;
        }

        private void ParseCheck(TextNpcScriptLine line, List<CompiledNpcCheck> checks, TextNpcLoadReport report)
        {
            List<string> parts = TokenizeLine(line.Text);
            if (parts.Count == 0) return;

            string command = parts[0].ToUpperInvariant();

            switch (command)
            {
                case "LEVEL":
                    if (TryParseComparison(parts.Skip(1).ToList(), Operator.GreaterThanOrEqual, out Operator levelOp, out int levelValue))
                        checks.Add(new CompiledNpcCheck { CheckType = CompiledNpcCheckType.Level, Operator = levelOp, IntParameter1 = levelValue });
                    else
                        report.Failures.Add($"{line.Source}: LEVEL 条件格式无效。");
                    break;
                case "CHECKGOLD":
                    if (TryParseComparison(parts.Skip(1).ToList(), Operator.GreaterThanOrEqual, out Operator goldOp, out int goldValue))
                        checks.Add(new CompiledNpcCheck { CheckType = CompiledNpcCheckType.Gold, Operator = goldOp, IntParameter1 = goldValue });
                    else
                        report.Failures.Add($"{line.Source}: CHECKGOLD 条件格式无效。");
                    break;
                case "CHECKITEM":
                    ParseItemCheck(line, checks, report);
                    break;
                case "CHECKCLASS":
                    if (parts.Count >= 2 && TryParseRequiredClass(parts[1], out RequiredClass requiredClass))
                        checks.Add(new CompiledNpcCheck { CheckType = CompiledNpcCheckType.Class, Class = requiredClass });
                    else
                        report.Failures.Add($"{line.Source}: CHECKCLASS 条件格式无效。");
                    break;
                case "CHECKPKPOINT":
                    if (TryParseComparison(parts.Skip(1).ToList(), Operator.LessThanOrEqual, out Operator pkOp, out int pkValue))
                        checks.Add(new CompiledNpcCheck { CheckType = CompiledNpcCheckType.PKPoint, Operator = pkOp, IntParameter1 = pkValue });
                    else
                        report.Failures.Add($"{line.Source}: CHECKPKPOINT 条件格式无效。");
                    break;
                case "RANDOM":
                    if (parts.Count >= 2 && int.TryParse(parts[1], out int randomMax) && randomMax > 0)
                        checks.Add(new CompiledNpcCheck { CheckType = CompiledNpcCheckType.Random, Operator = Operator.Equal, IntParameter1 = randomMax, IntParameter2 = parts.Count >= 3 && int.TryParse(parts[2], out int randomValue) ? randomValue : 0 });
                    else
                        report.Failures.Add($"{line.Source}: RANDOM 条件格式无效。");
                    break;
                case "CHECKNAMELIST":
                    if (parts.Count >= 2)
                        checks.Add(new CompiledNpcCheck { CheckType = CompiledNpcCheckType.NameList, StringParameter1 = parts[1], DataType = ParseDataType(parts, 2) });
                    else
                        report.Failures.Add($"{line.Source}: CHECKNAMELIST 条件格式无效。");
                    break;
                case "CHECKVALUE":
                    ParseValueCheck(line, checks, report);
                    break;
                default:
                    report.Failures.Add($"{line.Source}: 未知 NPC 条件 `{parts[0]}`。");
                    report.UnknownCommands.Add($"{line.Source}: {line.Text}");
                    break;
            }
        }

        private void ParseAction(TextNpcScriptLine line, List<CompiledNpcAction> actions, TextNpcLoadReport report)
        {
            List<string> parts = TokenizeLine(line.Text);
            if (parts.Count == 0) return;

            string command = parts[0].ToUpperInvariant();

            switch (command)
            {
                case "GOTO":
                    if (parts.Count >= 2)
                        actions.Add(new CompiledNpcAction { ActionType = CompiledNpcActionType.Goto, StringParameter1 = CompiledNpcScript.NormalizePageKey(parts[1]) });
                    else
                        report.Failures.Add($"{line.Source}: GOTO 需要目标页面。");
                    break;
                case "GIVEGOLD":
                    ParseIntAction(line, parts, actions, CompiledNpcActionType.GiveGold, report);
                    break;
                case "TAKEGOLD":
                    ParseIntAction(line, parts, actions, CompiledNpcActionType.TakeGold, report);
                    break;
                case "GIVEITEM":
                    ParseItemAction(line, parts, actions, CompiledNpcActionType.GiveItem, report);
                    break;
                case "TAKEITEM":
                    ParseItemAction(line, parts, actions, CompiledNpcActionType.TakeItem, report);
                    break;
                case "MOVE":
                    ParseMoveAction(line, parts, actions, report);
                    break;
                case "ADDNAMELIST":
                    ParseDataAction(line, parts, actions, CompiledNpcActionType.AddNameList, report);
                    break;
                case "DELNAMELIST":
                    ParseDataAction(line, parts, actions, CompiledNpcActionType.DeleteNameList, report);
                    break;
                case "SETVALUE":
                    ParseValueAction(line, parts, actions, CompiledNpcActionType.SetValue, report);
                    break;
                case "CHANGEVALUE":
                    ParseValueAction(line, parts, actions, CompiledNpcActionType.ChangeValue, report);
                    break;
                case "LOCALMESSAGE":
                    if (parts.Count >= 2)
                        actions.Add(new CompiledNpcAction { ActionType = CompiledNpcActionType.LocalMessage, StringParameter1 = line.Text.Substring(line.Text.IndexOf(' ') + 1).Trim() });
                    else
                        report.Failures.Add($"{line.Source}: LOCALMESSAGE 需要消息内容。");
                    break;
                default:
                    report.Failures.Add($"{line.Source}: 未知 NPC 动作 `{parts[0]}`。");
                    report.UnknownCommands.Add($"{line.Source}: {line.Text}");
                    break;
            }
        }

        private void ParseItemCheck(TextNpcScriptLine line, List<CompiledNpcCheck> checks, TextNpcLoadReport report)
        {
            List<string> parts = TokenizeLine(line.Text);
            if (parts.Count < 2)
            {
                report.Failures.Add($"{line.Source}: CHECKITEM 需要物品名。");
                return;
            }

            int count = 1;
            int nameEnd = parts.Count;
            if (parts.Count >= 3 && int.TryParse(parts[^1], out int parsedCount))
            {
                count = parsedCount;
                nameEnd--;
            }

            string itemName = string.Join(" ", parts.Skip(1).Take(nameEnd - 1));
            if (!_items.TryGetValue(NormalizeName(itemName), out ItemInfo item))
            {
                report.MissingItems.Add(itemName);
                report.Failures.Add($"{line.Source}: 找不到物品 `{itemName}`。");
                return;
            }

            checks.Add(new CompiledNpcCheck { CheckType = CompiledNpcCheckType.Item, Operator = Operator.GreaterThanOrEqual, ItemParameter1 = item, IntParameter1 = count });
        }

        private void ParseValueCheck(TextNpcScriptLine line, List<CompiledNpcCheck> checks, TextNpcLoadReport report)
        {
            List<string> parts = TokenizeLine(line.Text);
            if (parts.Count < 4 || !TryParseOperator(parts[2], out Operator op) || !int.TryParse(parts[3], out int value))
            {
                report.Failures.Add($"{line.Source}: CHECKVALUE 格式应为 CHECKVALUE 名称 运算符 数值 [DataType]。");
                return;
            }

            checks.Add(new CompiledNpcCheck
            {
                CheckType = CompiledNpcCheckType.Value,
                StringParameter1 = parts[1],
                Operator = op,
                IntParameter1 = value,
                DataType = ParseDataType(parts, 4)
            });
        }

        private void ParseIntAction(TextNpcScriptLine line, List<string> parts, List<CompiledNpcAction> actions, CompiledNpcActionType type, TextNpcLoadReport report)
        {
            if (parts.Count >= 2 && int.TryParse(parts[1], out int value))
                actions.Add(new CompiledNpcAction { ActionType = type, IntParameter1 = value });
            else
                report.Failures.Add($"{line.Source}: {parts[0]} 需要数字参数。");
        }

        private void ParseItemAction(TextNpcScriptLine line, List<string> parts, List<CompiledNpcAction> actions, CompiledNpcActionType type, TextNpcLoadReport report)
        {
            if (parts.Count < 2)
            {
                report.Failures.Add($"{line.Source}: {parts[0]} 需要物品名。");
                return;
            }

            int count = 1;
            int nameEnd = parts.Count;
            if (parts.Count >= 3 && int.TryParse(parts[^1], out int parsedCount))
            {
                count = parsedCount;
                nameEnd--;
            }

            string itemName = string.Join(" ", parts.Skip(1).Take(nameEnd - 1));
            if (!_items.TryGetValue(NormalizeName(itemName), out ItemInfo item))
            {
                report.MissingItems.Add(itemName);
                report.Failures.Add($"{line.Source}: 找不到物品 `{itemName}`。");
                return;
            }

            actions.Add(new CompiledNpcAction { ActionType = type, ItemParameter1 = item, IntParameter1 = count });
        }

        private void ParseMoveAction(TextNpcScriptLine line, List<string> parts, List<CompiledNpcAction> actions, TextNpcLoadReport report)
        {
            if (parts.Count < 4 || !int.TryParse(parts[^2], out int x) || !int.TryParse(parts[^1], out int y))
            {
                report.Failures.Add($"{line.Source}: MOVE 格式应为 MOVE 地图 X Y。");
                return;
            }

            string mapName = string.Join(" ", parts.Skip(1).Take(parts.Count - 3));
            if (!_maps.TryGetValue(NormalizeName(mapName), out MapInfo map))
            {
                report.MissingMaps.Add(mapName);
                report.Failures.Add($"{line.Source}: 找不到地图 `{mapName}`。");
                return;
            }

            actions.Add(new CompiledNpcAction { ActionType = CompiledNpcActionType.Move, MapParameter1 = map, IntParameter1 = x, IntParameter2 = y });
        }

        private void ParseDataAction(TextNpcScriptLine line, List<string> parts, List<CompiledNpcAction> actions, CompiledNpcActionType type, TextNpcLoadReport report)
        {
            if (parts.Count < 2)
            {
                report.Failures.Add($"{line.Source}: {parts[0]} 需要名称。");
                return;
            }

            actions.Add(new CompiledNpcAction { ActionType = type, StringParameter1 = parts[1], DataType = ParseDataType(parts, 2) });
        }

        private void ParseValueAction(TextNpcScriptLine line, List<string> parts, List<CompiledNpcAction> actions, CompiledNpcActionType type, TextNpcLoadReport report)
        {
            if (parts.Count < 3 || !int.TryParse(parts[2], out int value))
            {
                report.Failures.Add($"{line.Source}: {parts[0]} 格式应为 {parts[0]} 名称 数值 [DataType]。");
                return;
            }

            actions.Add(new CompiledNpcAction { ActionType = type, StringParameter1 = parts[1], IntParameter1 = value, DataType = ParseDataType(parts, 3) });
        }

        private void ParseGood(TextNpcScriptLine line, CompiledNpcPage page, TextNpcLoadReport report)
        {
            List<string> parts = TokenizeLine(line.Text);
            if (parts.Count == 0) return;

            decimal rate = 1M;
            int nameEnd = parts.Count;
            if (parts.Count >= 2 && decimal.TryParse(parts[^1], out decimal parsedRate))
            {
                rate = parsedRate;
                nameEnd--;
            }

            string itemName = string.Join(" ", parts.Take(nameEnd));
            if (!_items.TryGetValue(NormalizeName(itemName), out ItemInfo item))
            {
                report.MissingItems.Add(itemName);
                report.Failures.Add($"{line.Source}: 找不到商品物品 `{itemName}`。");
                return;
            }

            page.Goods.Add(new CompiledTextNpcGood { Index = page.Goods.Count + 1, Item = item, Rate = rate });
            if (page.DialogType == NPCDialogType.None)
                page.DialogType = NPCDialogType.BuySell;
        }

        private void ParseTypes(TextNpcScriptLine line, CompiledNpcPage page, TextNpcLoadReport report)
        {
            foreach (string part in TokenizeLine(line.Text))
            {
                if (part.Equals("All", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (ItemType itemType in Enum.GetValues(typeof(ItemType)))
                    {
                        if (itemType == ItemType.Nothing || page.Types.Contains(itemType)) continue;
                        page.Types.Add(itemType);
                    }

                    continue;
                }

                if (!Enum.TryParse(part, true, out ItemType type))
                {
                    report.Failures.Add($"{line.Source}: 未知 ItemType `{part}`。");
                    continue;
                }

                if (!page.Types.Contains(type))
                    page.Types.Add(type);
            }

            if (page.DialogType == NPCDialogType.None)
                page.DialogType = NPCDialogType.BuySell;
        }

        private void ParseCurrency(TextNpcScriptLine line, CompiledNpcPage page, TextNpcLoadReport report)
        {
            if (!_currencies.TryGetValue(NormalizeName(line.Text), out CurrencyInfo currency))
            {
                report.Failures.Add($"{line.Source}: 找不到货币 `{line.Text}`。");
                return;
            }

            page.Currency = currency;
        }

        private static void FinaliseBlock(CompiledNpcBlock block, CompiledNpcPage page, TextNpcLoadReport report)
        {
            if (block.SayLines.Count == 0) return;

            int nextButtonId = 1;
            string say = string.Join(Environment.NewLine, block.SayLines);
            block.Say = ButtonRegex.Replace(say, match =>
            {
                string text = match.Groups["Text"].Value.Trim();
                string target = CompiledNpcScript.NormalizePageKey(match.Groups["Target"].Value.Trim());
                int buttonId = target is "@EXIT" or "@CLOSE" ? 0 : nextButtonId++;

                if (buttonId > 0)
                    block.Buttons.Add(new CompiledNpcButton { ButtonID = buttonId, Text = text, DestinationKey = target });

                return $"[{text}:{buttonId}]";
            });
        }

        private static void ValidateDestinations(CompiledNpcScript script, CompiledNpcPage page, CompiledNpcBlock block, TextNpcLoadReport report)
        {
            foreach (CompiledNpcButton button in block.Buttons)
            {
                if (script.Pages.ContainsKey(button.DestinationKey)) continue;

                report.Failures.Add($"{script.ScriptFile} {page.Key}: 按钮 `{button.Text}` 目标页 `{button.DestinationKey}` 不存在。");
            }

            foreach (CompiledNpcAction action in block.Actions)
            {
                if (action.ActionType != CompiledNpcActionType.Goto) continue;
                if (script.Pages.ContainsKey(action.StringParameter1)) continue;

                report.Failures.Add($"{script.ScriptFile} {page.Key}: GOTO 目标页 `{action.StringParameter1}` 不存在。");
            }

            if (!string.IsNullOrWhiteSpace(page.SuccessKey) && !script.Pages.ContainsKey(page.SuccessKey))
                report.Failures.Add($"{script.ScriptFile} {page.Key}: #SUCCESS 目标页 `{page.SuccessKey}` 不存在。");
        }

        private List<TextNpcScriptLine> ReadCleanLines(string path, string npcDirectory, TextNpcLoadReport report, HashSet<string> stack)
        {
            string fullPath = Path.GetFullPath(path);
            if (!stack.Add(fullPath))
            {
                report.Failures.Add($"循环 #INSERT/#INCLUDE: {fullPath}");
                return [];
            }

            List<TextNpcScriptLine> result = [];
            string[] lines = File.ReadAllLines(fullPath);

            for (int i = 0; i < lines.Length; i++)
            {
                string text = CleanLine(lines[i]);
                if (string.IsNullOrWhiteSpace(text)) continue;

                Match include = IncludeRegex.Match(text);
                if (!include.Success)
                {
                    result.Add(new TextNpcScriptLine(fullPath, i + 1, text));
                    continue;
                }

                string includePath = ResolveScriptPath(npcDirectory, include.Groups[1].Value);
                if (!File.Exists(includePath))
                {
                    report.Failures.Add($"{fullPath}:{i + 1}: 找不到 #INSERT/#INCLUDE 文件 `{include.Groups[1].Value}`。");
                    continue;
                }

                report.IncludeFiles++;
                result.AddRange(ReadCleanLines(includePath, npcDirectory, report, stack));
            }

            stack.Remove(fullPath);
            return result;
        }

        private static bool TryParseComparison(List<string> parts, Operator defaultOperator, out Operator op, out int value)
        {
            op = defaultOperator;
            value = 0;

            if (parts.Count == 1)
                return int.TryParse(parts[0], out value);

            if (parts.Count >= 2 && TryParseOperator(parts[0], out op))
                return int.TryParse(parts[1], out value);

            return false;
        }

        private static bool TryParseOperator(string text, out Operator op)
        {
            op = Operator.Equal;

            switch (text.Trim().ToUpperInvariant())
            {
                case "=":
                case "==":
                case "EQUAL":
                    op = Operator.Equal;
                    return true;
                case "!=":
                case "<>":
                case "NOTEQUAL":
                    op = Operator.NotEqual;
                    return true;
                case "<":
                    op = Operator.LessThan;
                    return true;
                case "<=":
                    op = Operator.LessThanOrEqual;
                    return true;
                case ">":
                    op = Operator.GreaterThan;
                    return true;
                case ">=":
                    op = Operator.GreaterThanOrEqual;
                    return true;
                default:
                    return false;
            }
        }

        private static NPCDataType ParseDataType(List<string> parts, int index)
        {
            if (parts.Count <= index) return NPCDataType.User;
            return Enum.TryParse(parts[index], true, out NPCDataType type) ? type : NPCDataType.User;
        }

        private static NPCDialogType GetDialogType(string key)
        {
            return key.ToUpperInvariant() switch
            {
                "@BUY" or "@SELL" or "@BUYSELL" => NPCDialogType.BuySell,
                "@REPAIR" => NPCDialogType.Repair,
                "@REFINE" => NPCDialogType.Refine,
                "@REFINERETRIEVE" => NPCDialogType.RefineRetrieve,
                "@MASTERREFINE" => NPCDialogType.MasterRefine,
                "@ITEMFRAGMENT" => NPCDialogType.ItemFragment,
                "@ROLLDIE" => NPCDialogType.RollDie,
                "@ROLLYUT" => NPCDialogType.RollYut,
                _ => NPCDialogType.None
            };
        }

        private static bool TryParseRequiredClass(string text, out RequiredClass result)
        {
            result = RequiredClass.None;
            if (text.Equals("All", StringComparison.OrdinalIgnoreCase))
            {
                result = RequiredClass.All;
                return true;
            }

            foreach (string part in text.Split([',', '|', '+'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!Enum.TryParse(part, true, out RequiredClass parsed)) return false;
                result |= parsed;
            }

            return result != RequiredClass.None;
        }

        private static bool TryParseDays(string text, out DaysOfWeek result)
        {
            if (text.Equals("All", StringComparison.OrdinalIgnoreCase))
            {
                result = DaysOfWeek.Weekday | DaysOfWeek.Weekend;
                return true;
            }

            result = DaysOfWeek.None;
            foreach (string part in text.Split([',', '|', '+'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!Enum.TryParse(part, true, out DaysOfWeek parsed)) return false;
                result |= parsed;
            }

            return result != DaysOfWeek.None;
        }

        private static string ResolveScriptPath(string npcDirectory, string scriptFile)
        {
            string path = Path.Combine(npcDirectory, scriptFile);
            if (!Path.HasExtension(path))
                path += ".txt";

            return path;
        }

        private static void AddLookup<T>(Dictionary<string, T> lookup, string key, T value)
        {
            string normalized = NormalizeName(key);
            if (string.IsNullOrWhiteSpace(normalized) || lookup.ContainsKey(normalized)) return;

            lookup[normalized] = value;
        }

        public static string NormalizeName(string value)
        {
            return Regex.Replace(value ?? string.Empty, @"\s+", string.Empty);
        }

        private static string CleanLine(string line)
        {
            if (line == null) return string.Empty;

            string trimmed = line.Trim();
            if (trimmed.Length == 0) return string.Empty;
            if (trimmed.StartsWith(";", StringComparison.Ordinal) || trimmed.StartsWith("//", StringComparison.Ordinal)) return string.Empty;

            return trimmed;
        }

        public static List<string> TokenizeLine(string line)
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
    }

    public sealed class TextNpcExporter
    {
        public void Export(string npcDirectory, IEnumerable<NPCInfo> npcs, bool overwrite)
        {
            Directory.CreateDirectory(npcDirectory);

            string infoPath = Path.Combine(npcDirectory, "NPCInfo.export.txt");
            if (File.Exists(infoPath) && !overwrite)
                throw new IOException($"导出文件已存在: {infoPath}");

            StringBuilder info = new();
            foreach (NPCInfo npc in npcs.Where(x => x.Region?.Map != null && x.EntryPage != null).OrderBy(x => x.Index))
            {
                Point point = npc.Region.PointList?.FirstOrDefault() ?? npc.Region.PointRegion?.FirstOrDefault() ?? Point.Empty;
                string scriptFile = $"{SanitizeFileName(npc.NPCName)}_{npc.Index}.txt";

                info.AppendLine($"{npc.Region.Map.FileName} {point.X} {point.Y} \"{npc.NPCName}\" {npc.Image} {npc.FaceImage} \"{scriptFile}\" MapIcon={npc.MapIcon} OverrideIndex={npc.Index}");
                ExportScript(Path.Combine(npcDirectory, scriptFile), npc.EntryPage, overwrite);
            }

            File.WriteAllText(infoPath, info.ToString(), Encoding.UTF8);
        }

        private static void ExportScript(string path, NPCPage entryPage, bool overwrite)
        {
            if (File.Exists(path) && !overwrite)
                throw new IOException($"导出文件已存在: {path}");

            Dictionary<NPCPage, string> keys = [];
            Queue<NPCPage> queue = new();

            keys[entryPage] = "@MAIN";
            queue.Enqueue(entryPage);

            while (queue.Count > 0)
            {
                NPCPage page = queue.Dequeue();

                foreach (NPCButton button in page.Buttons)
                {
                    if (button.DestinationPage == null || keys.ContainsKey(button.DestinationPage)) continue;

                    keys[button.DestinationPage] = $"@PAGE{keys.Count + 1}";
                    queue.Enqueue(button.DestinationPage);
                }

                if (page.SuccessPage != null && !keys.ContainsKey(page.SuccessPage))
                {
                    keys[page.SuccessPage] = $"@PAGE{keys.Count + 1}";
                    queue.Enqueue(page.SuccessPage);
                }
            }

            StringBuilder script = new();
            foreach (KeyValuePair<NPCPage, string> pair in keys)
            {
                NPCPage page = pair.Key;
                script.AppendLine($"[{pair.Value}]");

                if (page.Checks.Count > 0)
                {
                    script.AppendLine("#IF");
                    foreach (NPCCheck check in page.Checks)
                        script.AppendLine($"; TODO 手动迁移条件: {check.CheckType}");
                }

                script.AppendLine("#SAY");
                script.AppendLine(ConvertButtonsForExport(page.Say, page, keys));

                if (page.Actions.Count > 0)
                {
                    script.AppendLine("#ACT");
                    foreach (NPCAction action in page.Actions)
                        script.AppendLine($"; TODO 手动迁移动作: {action.ActionType}");
                }

                if (page.SuccessPage != null && keys.TryGetValue(page.SuccessPage, out string successKey))
                    script.AppendLine($"#SUCCESS {successKey}");

                if (page.Goods.Count > 0)
                {
                    script.AppendLine("[TRADE]");
                    foreach (NPCGood good in page.Goods)
                        if (good.Item != null)
                            script.AppendLine($"\"{good.Item.ItemName}\" {good.Rate}");
                }

                if (page.Types.Count > 0)
                {
                    script.AppendLine("[TYPES]");
                    script.AppendLine(string.Join(" ", page.Types.Select(x => x.ItemType)));
                }

                script.AppendLine();
            }

            File.WriteAllText(path, script.ToString(), Encoding.UTF8);
        }

        private static string ConvertButtonsForExport(string say, NPCPage page, Dictionary<NPCPage, string> keys)
        {
            if (string.IsNullOrWhiteSpace(say)) return string.Empty;

            Dictionary<int, NPCButton> buttons = page.Buttons.ToDictionary(x => x.ButtonID);

            return Regex.Replace(say, @"\[(?<Text>.*?):(?<ID>\d+)\]", match =>
            {
                if (!int.TryParse(match.Groups["ID"].Value, out int id)) return match.Value;
                if (!buttons.TryGetValue(id, out NPCButton button)) return match.Value;
                if (button.DestinationPage == null || !keys.TryGetValue(button.DestinationPage, out string destination)) return match.Value;

                return $"<{match.Groups["Text"].Value}/{destination}>";
            });
        }

        private static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');

            return string.IsNullOrWhiteSpace(name) ? "NPC" : name;
        }
    }

    public sealed class TextNpcLoadResult
    {
        public List<CompiledTextNpcInfo> NPCs { get; } = [];
        public Dictionary<NPCInfo, CompiledTextNpcInfo> Overrides { get; } = [];
        public TextNpcLoadReport Report { get; } = new();
    }

    public sealed class TextNpcLoadReport
    {
        public int InfoLines;
        public int ScriptFiles;
        public int IncludeFiles;
        public int PagesLoaded;
        public int NpcsLoaded;
        public int NpcsSkipped;
        public int OverrideNpcs;
        public List<string> Failures { get; } = [];
        public List<string> UnknownCommands { get; } = [];
        public List<string> MissingMaps { get; } = [];
        public List<string> MissingItems { get; } = [];

        public string ToDisplayText()
        {
            return $"NPCInfo行={InfoLines}, 脚本={ScriptFiles}, Include={IncludeFiles}, 页面={PagesLoaded}, NPC={NpcsLoaded}, 覆盖={OverrideNpcs}, 跳过={NpcsSkipped}, 错误={Failures.Count}";
        }
    }

    public sealed class CompiledTextNpcInfo
    {
        public int TextIndex;
        public MapInfo Map;
        public Point Location;
        public string NPCName;
        public int Image;
        public int FaceImage;
        public MapIcon MapIcon;
        public MirDirection Direction;
        public int MinLevel;
        public int MaxLevel;
        public RequiredClass Class;
        public DaysOfWeek Days;
        public int OverrideIndex;
        public NPCInfo OverrideNPC;
        public string ScriptFile;
        public string ScriptPath;
        public CompiledNpcScript Script;

        public ClientTextNPCInfo ToClientInfo()
        {
            return new ClientTextNPCInfo
            {
                Index = TextIndex,
                NPCName = NPCName,
                Image = Image,
                FaceImage = FaceImage,
                MapIcon = MapIcon
            };
        }
    }

    public sealed class CompiledNpcScript
    {
        public string ScriptFile;
        public Dictionary<string, CompiledNpcPage> Pages { get; } = new(StringComparer.OrdinalIgnoreCase);
        public CompiledNpcPage EntryPage => Pages.TryGetValue("@MAIN", out CompiledNpcPage page) ? page : Pages.Values.FirstOrDefault();

        public CompiledNpcPage GetPage(string key)
        {
            return Pages.TryGetValue(NormalizePageKey(key), out CompiledNpcPage page) ? page : null;
        }

        public static string NormalizePageKey(string key)
        {
            key = (key ?? string.Empty).Trim();
            if (key.Length == 0) return "@MAIN";
            if (!key.StartsWith("@", StringComparison.Ordinal)) key = "@" + key;

            return key.ToUpperInvariant();
        }
    }

    public sealed class CompiledNpcPage
    {
        public string Key;
        public NPCDialogType DialogType;
        public CurrencyInfo Currency;
        public List<CompiledNpcCheck> Checks { get; } = [];
        public CompiledNpcBlock MainBlock { get; } = new();
        public CompiledNpcBlock ElseBlock { get; } = new();
        public List<CompiledTextNpcGood> Goods { get; } = [];
        public List<ItemType> Types { get; } = [];
        public string SuccessKey;

        public ClientTextNPCPage ToClientPage(CompiledNpcBlock block)
        {
            return new ClientTextNPCPage
            {
                Key = Key,
                Say = block.Say ?? string.Empty,
                DialogType = DialogType,
                CurrencyIndex = Currency?.Index ?? 0,
                Buttons = block.Buttons.Select(x => new ClientTextNPCButton { ButtonID = x.ButtonID, Text = x.Text, DestinationKey = x.DestinationKey }).ToList(),
                Goods = Goods.Select(x => x.ToClientGood()).ToList(),
                Types = Types.ToList()
            };
        }
    }

    public sealed class CompiledNpcBlock
    {
        public List<string> SayLines { get; } = [];
        public string Say;
        public List<CompiledNpcButton> Buttons { get; } = [];
        public List<CompiledNpcAction> Actions { get; } = [];

        public bool IsEmpty => string.IsNullOrWhiteSpace(Say) && Actions.Count == 0;
    }

    public sealed class CompiledNpcButton
    {
        public int ButtonID;
        public string Text;
        public string DestinationKey;
    }

    public sealed class CompiledTextNpcGood
    {
        public int Index;
        public ItemInfo Item;
        public decimal Rate = 1M;
        public int Cost => Item == null ? 0 : (int)Math.Round(Item.Price * Rate);

        public ClientTextNPCGood ToClientGood()
        {
            return new ClientTextNPCGood
            {
                Index = Index,
                ItemIndex = Item?.Index ?? 0,
                Rate = Rate
            };
        }
    }

    public sealed class CompiledNpcCheck
    {
        public CompiledNpcCheckType CheckType;
        public Operator Operator;
        public int IntParameter1;
        public int IntParameter2;
        public string StringParameter1;
        public ItemInfo ItemParameter1;
        public RequiredClass Class;
        public NPCDataType DataType = NPCDataType.User;
    }

    public sealed class CompiledNpcAction
    {
        public CompiledNpcActionType ActionType;
        public int IntParameter1;
        public int IntParameter2;
        public string StringParameter1;
        public ItemInfo ItemParameter1;
        public MapInfo MapParameter1;
        public NPCDataType DataType = NPCDataType.User;
    }

    public enum CompiledNpcCheckType
    {
        Level,
        Gold,
        Item,
        Class,
        PKPoint,
        Random,
        NameList,
        Value
    }

    public enum CompiledNpcActionType
    {
        Goto,
        GiveGold,
        TakeGold,
        GiveItem,
        TakeItem,
        Move,
        AddNameList,
        DeleteNameList,
        SetValue,
        ChangeValue,
        LocalMessage
    }

    internal enum TextNpcParseMode
    {
        Check,
        Say,
        Act,
        Trade,
        Types,
        Currency
    }

    internal readonly struct TextNpcScriptLine
    {
        public readonly string File;
        public readonly int Line;
        public readonly string Text;
        public string Source => $"{File}:{Line}";

        public TextNpcScriptLine(string file, int line, string text)
        {
            File = file;
            Line = line;
            Text = text;
        }
    }
}
