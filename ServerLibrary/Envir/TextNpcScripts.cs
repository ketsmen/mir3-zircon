using Library;
using Library.Network.ServerPackets;
using Library.SystemModels;
using MirDB;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Server.Envir
{
    public sealed class TextNpcImporter
    {
        private static readonly Regex IncludeRegex = new(@"^#(?:INSERT|INCLUDE)\s+\[(.*?)\]\s*(?:(?:;|//).*)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex PageRegex = new(@"^\[\s*(?<Reference>@?[\w\-.]+(?:\([^]]*\))?)\s*\]$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
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
            try
            {
                npcDirectory = Path.GetFullPath(npcDirectory);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
            {
                result.Report.Failures.Add($"NPC 文本目录无效 `{npcDirectory}`: {ex.Message}");
                return result;
            }

            string infoPath = Path.Combine(npcDirectory, "NPCInfo.txt");
            if (!File.Exists(infoPath))
            {
                result.Report.Failures.Add($"找不到 NPCInfo.txt: {infoPath}");
                return result;
            }

            HashSet<string> duplicateKeys = new(StringComparer.OrdinalIgnoreCase);
            int textIndex = 1;

            foreach (TextNpcScriptLine infoLine in ReadCleanLines(infoPath, npcDirectory, result.Report, new HashSet<string>(StringComparer.OrdinalIgnoreCase)))
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

            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x) ||
                !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y))
            {
                report.Failures.Add($"{line.Source}: NPC 坐标无效 `{parts[1]} {parts[2]}`。");
                return null;
            }

            if (!int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int image) ||
                !int.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int faceImage))
            {
                report.Failures.Add($"{line.Source}: NPC Image/FaceImage 必须是数字。");
                return null;
            }

            if (!TryResolveScriptPath(npcDirectory, parts[6], out string scriptPath))
            {
                report.Failures.Add($"{line.Source}: 脚本路径 `{parts[6]}` 必须位于 NPC 文本目录内。");
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
                ScriptPath = scriptPath,
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
                        if (!TryParseDefinedEnum(value, out MapIcon mapIcon))
                            report.Failures.Add($"{line.Source}: MapIcon 无效 `{value}`。");
                        else
                            info.MapIcon = mapIcon;
                        break;
                    case "DIRECTION":
                        if (!TryParseDefinedEnum(value, out MirDirection direction))
                            report.Failures.Add($"{line.Source}: Direction 无效 `{value}`。");
                        else
                            info.Direction = direction;
                        break;
                    case "MINLEVEL":
                        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out info.MinLevel))
                            report.Failures.Add($"{line.Source}: MinLevel 无效 `{value}`。");
                        break;
                    case "MAXLEVEL":
                        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out info.MaxLevel))
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
                        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out info.OverrideIndex))
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
            List<TextNpcScriptLine> lines = ReadCleanLines(scriptPath, npcDirectory, report, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
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
                    string pageReference = pageMatch.Groups["Reference"].Value;
                    if (CompiledNpcScript.ReferenceHasArguments(pageReference))
                        report.Failures.Add($"{line.Source}: 页面定义暂不支持默认参数，请使用 [@PAGE()]，并在按钮、GOTO 或 #SUCCESS 目标里传参。");

                    string key = CompiledNpcScript.GetReferencePageKey(pageReference);
                    if (!script.Pages.TryGetValue(key, out currentPage))
                    {
                        currentPage = new CompiledNpcPage { Key = key, Source = line.Source };
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

            if (IsDirectiveCommand(directive, "#SUCCESS"))
            {
                List<string> parts = TokenizeLine(directive);
                if (parts.Count < 2)
                {
                    report.Failures.Add($"{line.Source}: #SUCCESS 需要目标页面。");
                    return true;
                }

                page.SuccessKey = CompiledNpcScript.NormalizePageReference(parts[1]);
                page.SuccessSource = line.Source;
                return true;
            }

            if (IsDirectiveCommand(directive, "#DIALOGTYPE"))
            {
                List<string> parts = TokenizeLine(directive);
                if (parts.Count < 2)
                {
                    report.Failures.Add($"{line.Source}: #DIALOGTYPE 需要 NPCDialogType 名称。");
                    return true;
                }

                if (!TryParseDefinedEnum(parts[1], out NPCDialogType dialogType))
                    report.Failures.Add($"{line.Source}: #DIALOGTYPE 无效 `{parts[1]}`。");
                else
                    page.DialogType = dialogType;

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
                        checks.Add(WithSource(new CompiledNpcCheck { CheckType = CompiledNpcCheckType.Level, Operator = levelOp, IntParameter1 = levelValue }, line));
                    else
                        report.Failures.Add($"{line.Source}: LEVEL 条件格式无效。");
                    break;
                case "CHECKGOLD":
                    if (TryParseComparison(parts.Skip(1).ToList(), Operator.GreaterThanOrEqual, out Operator goldOp, out int goldValue))
                        checks.Add(WithSource(new CompiledNpcCheck { CheckType = CompiledNpcCheckType.Gold, Operator = goldOp, IntParameter1 = goldValue }, line));
                    else
                        report.Failures.Add($"{line.Source}: CHECKGOLD 条件格式无效。");
                    break;
                case "CHECKCURRENCY":
                    ParseCurrencyCheck(line, checks, report);
                    break;
                case "CHECKFAME":
                    checks.Add(WithSource(new CompiledNpcCheck { CheckType = CompiledNpcCheckType.Fame }, line));
                    break;
                case "CHECKITEM":
                    ParseItemCheck(line, checks, report);
                    break;
                case "HASBAGSPACE":
                case "CHECKBAGSPACE":
                case "CANGAINITEM":
                    ParseItemCheck(line, checks, report, CompiledNpcCheckType.CanGainItem);
                    break;
                case "HASWEAPON":
                case "CHECKWEAPON":
                    checks.Add(WithSource(new CompiledNpcCheck { CheckType = CompiledNpcCheckType.Weapon, Operator = Operator.Equal }, line));
                    break;
                case "NOWEAPON":
                    checks.Add(WithSource(new CompiledNpcCheck { CheckType = CompiledNpcCheckType.Weapon, Operator = Operator.NotEqual }, line));
                    break;
                case "WEAPONCANREFINE":
                case "CHECKWEAPONCANREFINE":
                    checks.Add(WithSource(new CompiledNpcCheck { CheckType = CompiledNpcCheckType.WeaponCanRefine, Operator = Operator.Equal }, line));
                    break;
                case "WEAPONCANNOTREFINE":
                    checks.Add(WithSource(new CompiledNpcCheck { CheckType = CompiledNpcCheckType.WeaponCanRefine, Operator = Operator.NotEqual }, line));
                    break;
                case "CANRESETWEAPON":
                case "WEAPONCANRESET":
                    checks.Add(WithSource(new CompiledNpcCheck { CheckType = CompiledNpcCheckType.WeaponCanReset, Operator = Operator.Equal }, line));
                    break;
                case "WEAPONCANNOTRESET":
                    checks.Add(WithSource(new CompiledNpcCheck { CheckType = CompiledNpcCheckType.WeaponCanReset, Operator = Operator.NotEqual }, line));
                    break;
                case "CHECKWEAPONLEVEL":
                case "WEAPONLEVEL":
                    if (TryParseComparison(parts.Skip(1).ToList(), Operator.GreaterThanOrEqual, out Operator weaponLevelOp, out int weaponLevel))
                        checks.Add(WithSource(new CompiledNpcCheck { CheckType = CompiledNpcCheckType.WeaponLevel, Operator = weaponLevelOp, IntParameter1 = weaponLevel }, line));
                    else
                        report.Failures.Add($"{line.Source}: {parts[0]} 条件格式无效。");
                    break;
                case "CHECKWEAPONELEMENT":
                case "WEAPONELEMENT":
                    ParseWeaponElementCheck(line, parts, checks, report);
                    break;
                case "CHECKWEAPONSTAT":
                case "CHECKWEAPONADDEDSTATS":
                case "WEAPONADDEDSTATS":
                    ParseWeaponStatCheck(line, parts, checks, report);
                    break;
                case "CHECKMARRIAGE":
                case "CHECKRELATIONSHIP":
                    checks.Add(WithSource(new CompiledNpcCheck { CheckType = CompiledNpcCheckType.Marriage, Operator = Operator.Equal }, line));
                    break;
                case "NOMARRIAGE":
                case "NORELATIONSHIP":
                    checks.Add(WithSource(new CompiledNpcCheck { CheckType = CompiledNpcCheckType.Marriage, Operator = Operator.NotEqual }, line));
                    break;
                case "CHECKWEDDINGRING":
                    checks.Add(WithSource(new CompiledNpcCheck { CheckType = CompiledNpcCheckType.WeddingRing, Operator = Operator.Equal }, line));
                    break;
                case "NOWEDDINGRING":
                    checks.Add(WithSource(new CompiledNpcCheck { CheckType = CompiledNpcCheckType.WeddingRing, Operator = Operator.NotEqual }, line));
                    break;
                case "CHECKHORSE":
                    ParseHorseCheck(line, parts, checks, report);
                    break;
                case "CHECKCLASS":
                    if (parts.Count >= 2 && TryParseRequiredClass(parts[1], out RequiredClass requiredClass))
                        checks.Add(WithSource(new CompiledNpcCheck { CheckType = CompiledNpcCheckType.Class, Class = requiredClass }, line));
                    else
                        report.Failures.Add($"{line.Source}: CHECKCLASS 条件格式无效。");
                    break;
                case "CHECKGENDER":
                    if (parts.Count >= 2 && TryParseRequiredGender(parts[1], out RequiredGender requiredGender))
                        checks.Add(WithSource(new CompiledNpcCheck { CheckType = CompiledNpcCheckType.Gender, Gender = requiredGender }, line));
                    else
                        report.Failures.Add($"{line.Source}: CHECKGENDER 条件格式无效。");
                    break;
                case "DAYOFWEEK":
                case "CHECKDAY":
                    if (parts.Count >= 2 && TryParseDays(parts[1], out DaysOfWeek days))
                        checks.Add(WithSource(new CompiledNpcCheck { CheckType = CompiledNpcCheckType.DayOfWeek, Days = days }, line));
                    else
                        report.Failures.Add($"{line.Source}: {parts[0]} 条件格式无效。");
                    break;
                case "HOUR":
                    ParseTimeCheck(line, parts, checks, CompiledNpcCheckType.Hour, 0, 23, report);
                    break;
                case "MIN":
                case "MINUTE":
                    ParseTimeCheck(line, parts, checks, CompiledNpcCheckType.Minute, 0, 59, report);
                    break;
                case "CHECKPKPOINT":
                    if (TryParsePKPointComparison(parts.Skip(1).ToList(), Operator.LessThanOrEqual, out Operator pkOp, out int pkValue, out bool useRedPointThreshold))
                        checks.Add(WithSource(new CompiledNpcCheck { CheckType = CompiledNpcCheckType.PKPoint, Operator = pkOp, IntParameter1 = pkValue, UseRedPointThreshold = useRedPointThreshold }, line));
                    else
                        report.Failures.Add($"{line.Source}: CHECKPKPOINT 条件格式无效，应为 CHECKPKPOINT [运算符] 数值|REDPOINT。");
                    break;
                case "RANDOM":
                    if (parts.Count >= 2 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int randomMax) && randomMax > 0)
                        checks.Add(WithSource(new CompiledNpcCheck { CheckType = CompiledNpcCheckType.Random, Operator = Operator.Equal, IntParameter1 = randomMax, IntParameter2 = parts.Count >= 3 && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int randomValue) ? randomValue : 0 }, line));
                    else
                        report.Failures.Add($"{line.Source}: RANDOM 条件格式无效。");
                    break;
                case "CHECKNAMELIST":
                    if (parts.Count >= 2)
                    {
                        if (TryParseDataType(line, parts, 2, report, out NPCDataType dataType))
                            checks.Add(WithSource(new CompiledNpcCheck { CheckType = CompiledNpcCheckType.NameList, StringParameter1 = parts[1], DataType = dataType }, line));
                    }
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
                        actions.Add(WithSource(new CompiledNpcAction { ActionType = CompiledNpcActionType.Goto, StringParameter1 = CompiledNpcScript.NormalizePageReference(parts[1]) }, line));
                    else
                        report.Failures.Add($"{line.Source}: GOTO 需要目标页面。");
                    break;
                case "GIVEGOLD":
                    ParseIntAction(line, parts, actions, CompiledNpcActionType.GiveGold, report);
                    break;
                case "TAKEGOLD":
                    ParseIntAction(line, parts, actions, CompiledNpcActionType.TakeGold, report);
                    break;
                case "GIVECURRENCY":
                    ParseCurrencyAction(line, parts, actions, CompiledNpcActionType.GiveCurrency, report);
                    break;
                case "TAKECURRENCY":
                    ParseCurrencyAction(line, parts, actions, CompiledNpcActionType.TakeCurrency, report);
                    break;
                case "GIVEITEM":
                    ParseItemAction(line, parts, actions, CompiledNpcActionType.GiveItem, report);
                    break;
                case "TAKEITEM":
                    ParseItemAction(line, parts, actions, CompiledNpcActionType.TakeItem, report);
                    break;
                case "GIVEITEMEXP":
                case "GIVEITEMEXPERIENCE":
                    ParseItemExperienceAction(line, parts, actions, report);
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
                case "CLEARNAMELIST":
                    ParseDataAction(line, parts, actions, CompiledNpcActionType.ClearNameList, report);
                    break;
                case "SETVALUE":
                    ParseValueAction(line, parts, actions, CompiledNpcActionType.SetValue, report);
                    break;
                case "CHANGEVALUE":
                    ParseValueAction(line, parts, actions, CompiledNpcActionType.ChangeValue, report);
                    break;
                case "LOCALMESSAGE":
                    string message = GetCommandRemainder(line.Text);
                    if (!string.IsNullOrWhiteSpace(message))
                        actions.Add(WithSource(new CompiledNpcAction { ActionType = CompiledNpcActionType.LocalMessage, StringParameter1 = message }, line));
                    else
                        report.Failures.Add($"{line.Source}: LOCALMESSAGE 需要消息内容。");
                    break;
                case "PROMOTEFAME":
                    actions.Add(WithSource(new CompiledNpcAction { ActionType = CompiledNpcActionType.PromoteFame }, line));
                    break;
                case "MARRIAGE":
                    actions.Add(WithSource(new CompiledNpcAction { ActionType = CompiledNpcActionType.Marriage }, line));
                    break;
                case "DIVORCE":
                case "FORCEDIVORCE":
                    actions.Add(WithSource(new CompiledNpcAction { ActionType = CompiledNpcActionType.Divorce }, line));
                    break;
                case "REMOVEWEDDINGRING":
                    actions.Add(WithSource(new CompiledNpcAction { ActionType = CompiledNpcActionType.RemoveWeddingRing }, line));
                    break;
                case "RESETWEAPON":
                    actions.Add(WithSource(new CompiledNpcAction { ActionType = CompiledNpcActionType.ResetWeapon }, line));
                    break;
                case "REBIRTH":
                    actions.Add(WithSource(new CompiledNpcAction { ActionType = CompiledNpcActionType.Rebirth }, line));
                    break;
                case "CHANGEHORSE":
                    ParseHorseAction(line, parts, actions, report);
                    break;
                case "CHANGEELEMENT":
                case "CHANGEWEAPONELEMENT":
                    ParseElementAction(line, parts, actions, report);
                    break;
                case "SPECIALREFINE":
                    ParseSpecialRefineAction(line, parts, actions, report);
                    break;
                default:
                    report.Failures.Add($"{line.Source}: 未知 NPC 动作 `{parts[0]}`。");
                    report.UnknownCommands.Add($"{line.Source}: {line.Text}");
                    break;
            }
        }

        private static CompiledNpcCheck WithSource(CompiledNpcCheck check, TextNpcScriptLine line)
        {
            check.Source = line.Source;
            return check;
        }

        private static CompiledNpcAction WithSource(CompiledNpcAction action, TextNpcScriptLine line)
        {
            action.Source = line.Source;
            return action;
        }

        private void ParseItemCheck(TextNpcScriptLine line, List<CompiledNpcCheck> checks, TextNpcLoadReport report, CompiledNpcCheckType checkType = CompiledNpcCheckType.Item)
        {
            List<string> parts = TokenizeLine(line.Text);
            if (parts.Count < 2)
            {
                report.Failures.Add($"{line.Source}: CHECKITEM 需要物品名。");
                return;
            }

            int count = 1;
            int nameEnd = parts.Count;
            if (parts.Count >= 3 && int.TryParse(parts[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedCount))
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

            checks.Add(WithSource(new CompiledNpcCheck { CheckType = checkType, Operator = Operator.GreaterThanOrEqual, ItemParameter1 = item, IntParameter1 = count }, line));
        }

        private void ParseCurrencyCheck(TextNpcScriptLine line, List<CompiledNpcCheck> checks, TextNpcLoadReport report)
        {
            List<string> parts = TokenizeLine(line.Text);
            if (parts.Count < 3)
            {
                report.Failures.Add($"{line.Source}: CHECKCURRENCY 格式应为 CHECKCURRENCY 货币名 [运算符] 数值。");
                return;
            }

            Operator op = Operator.GreaterThanOrEqual;
            int amount;
            int nameEnd;

            if (parts.Count >= 4 && TryParseOperator(parts[^2], out op) &&
                int.TryParse(parts[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out amount))
            {
                nameEnd = parts.Count - 2;
            }
            else if (int.TryParse(parts[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out amount))
            {
                nameEnd = parts.Count - 1;
            }
            else
            {
                report.Failures.Add($"{line.Source}: CHECKCURRENCY 数值无效。");
                return;
            }

            string currencyName = string.Join(" ", parts.Skip(1).Take(nameEnd - 1));
            if (!_currencies.TryGetValue(NormalizeName(currencyName), out CurrencyInfo currency))
            {
                report.MissingCurrencies.Add(currencyName);
                report.Failures.Add($"{line.Source}: 找不到货币 `{currencyName}`。");
                return;
            }

            checks.Add(WithSource(new CompiledNpcCheck { CheckType = CompiledNpcCheckType.Currency, Operator = op, IntParameter1 = amount, StringParameter1 = currency.Name }, line));
        }

        private void ParseTimeCheck(TextNpcScriptLine line, List<string> parts, List<CompiledNpcCheck> checks, CompiledNpcCheckType checkType, int minValue, int maxValue, TextNpcLoadReport report)
        {
            if (TryParseComparison(parts.Skip(1).ToList(), Operator.Equal, out Operator op, out int value) && value >= minValue && value <= maxValue)
                checks.Add(WithSource(new CompiledNpcCheck { CheckType = checkType, Operator = op, IntParameter1 = value }, line));
            else
                report.Failures.Add($"{line.Source}: {parts[0]} 需要 {minValue}-{maxValue} 的数字参数。");
        }

        private void ParseValueCheck(TextNpcScriptLine line, List<CompiledNpcCheck> checks, TextNpcLoadReport report)
        {
            List<string> parts = TokenizeLine(line.Text);
            if (parts.Count < 4 || !TryParseOperator(parts[2], out Operator op) ||
                !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                report.Failures.Add($"{line.Source}: CHECKVALUE 格式应为 CHECKVALUE 名称 运算符 数值 [DataType]。");
                return;
            }

            if (!TryParseDataType(line, parts, 4, report, out NPCDataType dataType)) return;

            checks.Add(WithSource(new CompiledNpcCheck
            {
                CheckType = CompiledNpcCheckType.Value,
                StringParameter1 = parts[1],
                Operator = op,
                IntParameter1 = value,
                DataType = dataType
            }, line));
        }

        private void ParseIntAction(TextNpcScriptLine line, List<string> parts, List<CompiledNpcAction> actions, CompiledNpcActionType type, TextNpcLoadReport report)
        {
            if (parts.Count >= 2 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                actions.Add(WithSource(new CompiledNpcAction { ActionType = type, IntParameter1 = value }, line));
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
            if (parts.Count >= 3 && int.TryParse(parts[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedCount))
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

            actions.Add(WithSource(new CompiledNpcAction { ActionType = type, ItemParameter1 = item, IntParameter1 = count }, line));
        }

        private void ParseItemExperienceAction(TextNpcScriptLine line, List<string> parts, List<CompiledNpcAction> actions, TextNpcLoadReport report)
        {
            if (parts.Count < 4 ||
                !int.TryParse(parts[^2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) ||
                !int.TryParse(parts[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int experience))
            {
                report.Failures.Add($"{line.Source}: {parts[0]} 格式应为 {parts[0]} 物品名 数量 经验值。");
                return;
            }

            string itemName = string.Join(" ", parts.Skip(1).Take(parts.Count - 3));
            if (!_items.TryGetValue(NormalizeName(itemName), out ItemInfo item))
            {
                report.MissingItems.Add(itemName);
                report.Failures.Add($"{line.Source}: 找不到物品 `{itemName}`。");
                return;
            }

            actions.Add(WithSource(new CompiledNpcAction { ActionType = CompiledNpcActionType.GiveItemExperience, ItemParameter1 = item, IntParameter1 = count, IntParameter2 = experience }, line));
        }

        private void ParseCurrencyAction(TextNpcScriptLine line, List<string> parts, List<CompiledNpcAction> actions, CompiledNpcActionType type, TextNpcLoadReport report)
        {
            if (parts.Count < 3 || !int.TryParse(parts[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int amount))
            {
                report.Failures.Add($"{line.Source}: {parts[0]} 格式应为 {parts[0]} 货币名 数值。");
                return;
            }

            string currencyName = string.Join(" ", parts.Skip(1).Take(parts.Count - 2));
            if (!_currencies.TryGetValue(NormalizeName(currencyName), out CurrencyInfo currency))
            {
                report.MissingCurrencies.Add(currencyName);
                report.Failures.Add($"{line.Source}: 找不到货币 `{currencyName}`。");
                return;
            }

            actions.Add(WithSource(new CompiledNpcAction { ActionType = type, StringParameter1 = currency.Name, IntParameter1 = amount }, line));
        }

        private void ParseMoveAction(TextNpcScriptLine line, List<string> parts, List<CompiledNpcAction> actions, TextNpcLoadReport report)
        {
            if (parts.Count < 4 ||
                !int.TryParse(parts[^2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x) ||
                !int.TryParse(parts[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y))
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

            actions.Add(WithSource(new CompiledNpcAction { ActionType = CompiledNpcActionType.Move, MapParameter1 = map, IntParameter1 = x, IntParameter2 = y }, line));
        }

        private void ParseDataAction(TextNpcScriptLine line, List<string> parts, List<CompiledNpcAction> actions, CompiledNpcActionType type, TextNpcLoadReport report)
        {
            if (parts.Count < 2)
            {
                report.Failures.Add($"{line.Source}: {parts[0]} 需要名称。");
                return;
            }

            if (TryParseDataType(line, parts, 2, report, out NPCDataType dataType))
                actions.Add(WithSource(new CompiledNpcAction { ActionType = type, StringParameter1 = parts[1], DataType = dataType }, line));
        }

        private void ParseValueAction(TextNpcScriptLine line, List<string> parts, List<CompiledNpcAction> actions, CompiledNpcActionType type, TextNpcLoadReport report)
        {
            if (parts.Count < 3 || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                report.Failures.Add($"{line.Source}: {parts[0]} 格式应为 {parts[0]} 名称 数值 [DataType]。");
                return;
            }

            if (TryParseDataType(line, parts, 3, report, out NPCDataType dataType))
                actions.Add(WithSource(new CompiledNpcAction { ActionType = type, StringParameter1 = parts[1], IntParameter1 = value, DataType = dataType }, line));
        }

        private void ParseHorseCheck(TextNpcScriptLine line, List<string> parts, List<CompiledNpcCheck> checks, TextNpcLoadReport report)
        {
            if (parts.Count < 2)
            {
                report.Failures.Add($"{line.Source}: CHECKHORSE 需要 HorseType。");
                return;
            }

            Operator op = Operator.Equal;
            string horseText = parts[1];
            if (parts.Count >= 3 && TryParseOperator(parts[1], out op))
                horseText = parts[2];

            if (!TryParseHorseType(horseText, out HorseType horseType))
            {
                report.Failures.Add($"{line.Source}: CHECKHORSE 未知 HorseType `{horseText}`。");
                return;
            }

            checks.Add(WithSource(new CompiledNpcCheck { CheckType = CompiledNpcCheckType.Horse, Operator = op, IntParameter1 = (int)horseType }, line));
        }

        private void ParseHorseAction(TextNpcScriptLine line, List<string> parts, List<CompiledNpcAction> actions, TextNpcLoadReport report)
        {
            if (parts.Count < 2 || !TryParseHorseType(parts[1], out HorseType horseType))
            {
                report.Failures.Add($"{line.Source}: CHANGEHORSE 需要有效 HorseType。");
                return;
            }

            actions.Add(WithSource(new CompiledNpcAction { ActionType = CompiledNpcActionType.ChangeHorse, IntParameter1 = (int)horseType }, line));
        }

        private void ParseWeaponElementCheck(TextNpcScriptLine line, List<string> parts, List<CompiledNpcCheck> checks, TextNpcLoadReport report)
        {
            if (parts.Count < 3 || !TryParseElement(parts[1], out Element element) ||
                !TryParseComparison(parts.Skip(2).ToList(), Operator.GreaterThanOrEqual, out Operator op, out int value))
            {
                report.Failures.Add($"{line.Source}: {parts[0]} 格式应为 {parts[0]} Element [运算符] 数值。");
                return;
            }

            checks.Add(WithSource(new CompiledNpcCheck { CheckType = CompiledNpcCheckType.WeaponElement, Operator = op, IntParameter1 = (int)element, IntParameter2 = value }, line));
        }

        private void ParseWeaponStatCheck(TextNpcScriptLine line, List<string> parts, List<CompiledNpcCheck> checks, TextNpcLoadReport report)
        {
            if (parts.Count < 3 || !TryParseStat(parts[1], out Stat stat) ||
                !TryParseComparison(parts.Skip(2).ToList(), Operator.GreaterThanOrEqual, out Operator op, out int value))
            {
                report.Failures.Add($"{line.Source}: {parts[0]} 格式应为 {parts[0]} Stat [运算符] 数值。");
                return;
            }

            checks.Add(WithSource(new CompiledNpcCheck { CheckType = CompiledNpcCheckType.WeaponAddedStats, Operator = op, StatParameter1 = stat, IntParameter1 = value }, line));
        }

        private void ParseElementAction(TextNpcScriptLine line, List<string> parts, List<CompiledNpcAction> actions, TextNpcLoadReport report)
        {
            if (parts.Count < 2 || !TryParseElement(parts[1], out Element element))
            {
                report.Failures.Add($"{line.Source}: {parts[0]} 需要有效 Element。");
                return;
            }

            actions.Add(WithSource(new CompiledNpcAction { ActionType = CompiledNpcActionType.ChangeElement, IntParameter1 = (int)element }, line));
        }

        private void ParseSpecialRefineAction(TextNpcScriptLine line, List<string> parts, List<CompiledNpcAction> actions, TextNpcLoadReport report)
        {
            if (parts.Count < 3 || !TryParseStat(parts[1], out Stat stat) ||
                !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int amount))
            {
                report.Failures.Add($"{line.Source}: SPECIALREFINE 格式应为 SPECIALREFINE Stat 数值。");
                return;
            }

            actions.Add(WithSource(new CompiledNpcAction { ActionType = CompiledNpcActionType.SpecialRefine, StatParameter1 = stat, IntParameter1 = amount }, line));
        }

        private void ParseGood(TextNpcScriptLine line, CompiledNpcPage page, TextNpcLoadReport report)
        {
            List<string> parts = TokenizeLine(line.Text);
            if (parts.Count == 0) return;

            decimal rate = 1M;
            int nameEnd = parts.Count;
            if (parts.Count >= 2 && decimal.TryParse(parts[^1], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsedRate))
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

                if (!TryParseDefinedEnum(part, out ItemType type))
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
            string currencyName = string.Join(" ", TokenizeLine(line.Text));
            if (string.IsNullOrWhiteSpace(currencyName))
                currencyName = line.Text?.Trim() ?? string.Empty;

            if (!_currencies.TryGetValue(NormalizeName(currencyName), out CurrencyInfo currency))
            {
                report.MissingCurrencies.Add(currencyName);
                report.Failures.Add($"{line.Source}: 找不到货币 `{currencyName}`。");
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
                string target = CompiledNpcScript.NormalizePageReference(match.Groups["Target"].Value.Trim());
                string targetKey = CompiledNpcScript.GetReferencePageKey(target);
                int buttonId = targetKey is "@EXIT" or "@CLOSE" ? 0 : nextButtonId++;

                if (buttonId > 0)
                    block.Buttons.Add(new CompiledNpcButton { ButtonID = buttonId, Text = text, DestinationKey = target });

                return $"[{text}:{buttonId}]";
            });
        }

        private static void ValidateDestinations(CompiledNpcScript script, CompiledNpcPage page, CompiledNpcBlock block, TextNpcLoadReport report)
        {
            foreach (CompiledNpcButton button in block.Buttons)
            {
                if (CompiledNpcScript.ContainsArgumentToken(button.DestinationKey)) continue;

                string destinationKey = CompiledNpcScript.GetReferencePageKey(button.DestinationKey);
                if (script.Pages.ContainsKey(destinationKey)) continue;

                report.Failures.Add($"{script.ScriptFile} {page.Key}: 按钮 `{button.Text}` 目标页 `{button.DestinationKey}` 不存在。");
            }

            foreach (CompiledNpcAction action in block.Actions)
            {
                if (action.ActionType != CompiledNpcActionType.Goto) continue;
                if (CompiledNpcScript.ContainsArgumentToken(action.StringParameter1)) continue;

                string destinationKey = CompiledNpcScript.GetReferencePageKey(action.StringParameter1);
                if (script.Pages.ContainsKey(destinationKey)) continue;

                report.Failures.Add($"{script.ScriptFile} {page.Key}: GOTO 目标页 `{action.StringParameter1}` 不存在。");
            }

            if (!string.IsNullOrWhiteSpace(page.SuccessKey))
            {
                if (CompiledNpcScript.ContainsArgumentToken(page.SuccessKey)) return;

                string successKey = CompiledNpcScript.GetReferencePageKey(page.SuccessKey);
                if (!script.Pages.ContainsKey(successKey))
                    report.Failures.Add($"{script.ScriptFile} {page.Key}: #SUCCESS 目标页 `{page.SuccessKey}` 不存在。");
            }
        }

        private List<TextNpcScriptLine> ReadCleanLines(string path, string npcDirectory, TextNpcLoadReport report, HashSet<string> stack)
        {
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
                if (!IsPathInsideDirectory(npcDirectory, fullPath))
                {
                    report.Failures.Add($"脚本路径必须位于 NPC 文本目录内: {fullPath}");
                    return [];
                }
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
            {
                report.Failures.Add($"脚本路径无效 `{path}`: {ex.Message}");
                return [];
            }

            if (!stack.Add(fullPath))
            {
                report.Failures.Add($"循环 #INSERT/#INCLUDE: {fullPath}");
                return [];
            }

            try
            {
                List<TextNpcScriptLine> result = [];
                string[] lines;

                try
                {
                    lines = File.ReadAllLines(fullPath);
                }
                catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
                {
                    report.Failures.Add($"{fullPath}: 读取脚本失败: {ex.Message}");
                    return result;
                }

                for (int i = 0; i < lines.Length; i++)
                {
                    string text = CleanLine(lines[i]);
                    if (string.IsNullOrWhiteSpace(text)) continue;

                    bool includeDirective = text.StartsWith("#INSERT", StringComparison.OrdinalIgnoreCase) ||
                                            text.StartsWith("#INCLUDE", StringComparison.OrdinalIgnoreCase);
                    Match include = IncludeRegex.Match(text);
                    if (!include.Success)
                    {
                        if (includeDirective)
                        {
                            report.Failures.Add($"{fullPath}:{i + 1}: #INSERT/#INCLUDE 格式必须是整行 #INCLUDE [file]。");
                            continue;
                        }

                        result.Add(new TextNpcScriptLine(fullPath, i + 1, text));
                        continue;
                    }

                    string includeFile = include.Groups[1].Value;
                    if (!TryResolveScriptPath(npcDirectory, includeFile, out string includePath))
                    {
                        report.Failures.Add($"{fullPath}:{i + 1}: #INSERT/#INCLUDE 文件 `{includeFile}` 必须位于 NPC 文本目录内。");
                        continue;
                    }

                    if (!File.Exists(includePath))
                    {
                        report.Failures.Add($"{fullPath}:{i + 1}: 找不到 #INSERT/#INCLUDE 文件 `{includeFile}`。");
                        continue;
                    }

                    report.IncludeFiles++;
                    result.AddRange(ReadCleanLines(includePath, npcDirectory, report, stack));
                }

                return result;
            }
            finally
            {
                stack.Remove(fullPath);
            }
        }

        private static bool TryParseComparison(List<string> parts, Operator defaultOperator, out Operator op, out int value)
        {
            op = defaultOperator;
            value = 0;

            if (parts.Count == 1)
                return int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

            if (parts.Count >= 2 && TryParseOperator(parts[0], out op))
                return int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

            return false;
        }

        private static bool TryParsePKPointComparison(List<string> parts, Operator defaultOperator, out Operator op, out int value, out bool useRedPointThreshold)
        {
            op = defaultOperator;
            value = 0;
            useRedPointThreshold = false;

            if (parts.Count == 1)
                return TryParsePKPointValue(parts[0], out value, out useRedPointThreshold);

            if (parts.Count >= 2 && TryParseOperator(parts[0], out op))
                return TryParsePKPointValue(parts[1], out value, out useRedPointThreshold);

            return false;
        }

        private static bool TryParsePKPointValue(string text, out int value, out bool useRedPointThreshold)
        {
            useRedPointThreshold = false;

            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                return true;

            switch (text.Trim().ToUpperInvariant())
            {
                case "REDPOINT":
                case "CONFIG.REDPOINT":
                case "DEFAULT":
                    value = 0;
                    useRedPointThreshold = true;
                    return true;
                default:
                    value = 0;
                    return false;
            }
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

        private static bool TryParseHorseType(string value, out HorseType horseType)
        {
            if (Enum.TryParse(value, true, out horseType) && Enum.IsDefined(typeof(HorseType), horseType))
                return true;

            if (byte.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte raw) && Enum.IsDefined(typeof(HorseType), raw))
            {
                horseType = (HorseType)raw;
                return true;
            }

            horseType = HorseType.None;
            return false;
        }

        private static bool TryParseElement(string value, out Element element)
        {
            if (Enum.TryParse(value, true, out element) && Enum.IsDefined(typeof(Element), element))
                return true;

            if (byte.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte raw) && Enum.IsDefined(typeof(Element), raw))
            {
                element = (Element)raw;
                return true;
            }

            element = Element.None;
            return false;
        }

        private static bool TryParseStat(string value, out Stat stat)
        {
            if (Enum.TryParse(value, true, out stat) && Enum.IsDefined(typeof(Stat), stat))
                return true;

            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int raw) && Enum.IsDefined(typeof(Stat), raw))
            {
                stat = (Stat)raw;
                return true;
            }

            stat = Stat.None;
            return false;
        }

        private static bool TryParseDataType(TextNpcScriptLine line, List<string> parts, int index, TextNpcLoadReport report, out NPCDataType type)
        {
            type = NPCDataType.User;
            if (parts.Count <= index) return true;

            if (Enum.TryParse(parts[index], true, out type) && Enum.IsDefined(typeof(NPCDataType), type))
                return true;

            report.Failures.Add($"{line.Source}: 未知 DataType `{parts[index]}`，应为 None/User/Guild/Account。");
            type = NPCDataType.User;
            return false;
        }

        private static NPCDialogType GetDialogType(string key)
        {
            return key.ToUpperInvariant() switch
            {
                "@BUY" or "@SELL" or "@BUYSELL" => NPCDialogType.BuySell,
                "@REPAIR" => NPCDialogType.Repair,
                "@REFINE" => NPCDialogType.Refine,
                "@REFINERETRIEVE" => NPCDialogType.RefineRetrieve,
                "@COMPANIONMANAGE" => NPCDialogType.CompanionManage,
                "@WEDDINGRING" => NPCDialogType.WeddingRing,
                "@REFINEMENTSTONE" => NPCDialogType.RefinementStone,
                "@MASTERREFINE" => NPCDialogType.MasterRefine,
                "@WEAPONRESET" => NPCDialogType.WeaponReset,
                "@ITEMFRAGMENT" => NPCDialogType.ItemFragment,
                "@ACCESSORYREFINEUPGRADE" => NPCDialogType.AccessoryRefineUpgrade,
                "@ACCESSORYREFINELEVEL" => NPCDialogType.AccessoryRefineLevel,
                "@ACCESSORYRESET" => NPCDialogType.AccessoryReset,
                "@WEAPONCRAFT" => NPCDialogType.WeaponCraft,
                "@ACCESSORYREFINE" => NPCDialogType.AccessoryRefine,
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
                if (!TryParseDefinedFlag(part, out RequiredClass parsed)) return false;
                result |= parsed;
            }

            return result != RequiredClass.None;
        }

        private static bool TryParseRequiredGender(string text, out RequiredGender result)
        {
            result = RequiredGender.None;
            if (text.Equals("All", StringComparison.OrdinalIgnoreCase) || text.Equals("None", StringComparison.OrdinalIgnoreCase))
                return true;

            result = 0;
            foreach (string part in text.Split([',', '|', '+'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!TryParseDefinedFlag(part, out RequiredGender parsed)) return false;
                result |= parsed;
            }

            return result != 0;
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
                if (!TryParseDefinedFlag(part, out DaysOfWeek parsed)) return false;
                result |= parsed;
            }

            return result != DaysOfWeek.None;
        }

        private static bool IsDirectiveCommand(string text, string command)
        {
            return text.StartsWith(command, StringComparison.OrdinalIgnoreCase) &&
                   (text.Length == command.Length || char.IsWhiteSpace(text[command.Length]));
        }

        private static bool TryParseDefinedEnum<TEnum>(string value, out TEnum result) where TEnum : struct, Enum
        {
            return Enum.TryParse(value, true, out result) &&
                   Enum.IsDefined(typeof(TEnum), result);
        }

        private static bool TryParseDefinedFlag<TEnum>(string value, out TEnum result) where TEnum : struct, Enum
        {
            if (!Enum.TryParse(value, true, out result))
                return false;

            if (!TryGetEnumBits(result, out ulong parsed))
                return false;

            ulong allowed = 0;

            foreach (TEnum defined in Enum.GetValues<TEnum>())
            {
                if (!TryGetEnumBits(defined, out ulong definedBits))
                    return false;

                allowed |= definedBits;
            }

            return (parsed & ~allowed) == 0;
        }

        private static bool TryGetEnumBits<TEnum>(TEnum value, out ulong bits) where TEnum : struct, Enum
        {
            bits = 0;

            try
            {
                switch (Type.GetTypeCode(Enum.GetUnderlyingType(typeof(TEnum))))
                {
                    case TypeCode.SByte:
                        bits = unchecked((ulong)(byte)Convert.ToSByte(value, CultureInfo.InvariantCulture));
                        return true;
                    case TypeCode.Byte:
                        bits = Convert.ToByte(value, CultureInfo.InvariantCulture);
                        return true;
                    case TypeCode.Int16:
                        bits = unchecked((ulong)(ushort)Convert.ToInt16(value, CultureInfo.InvariantCulture));
                        return true;
                    case TypeCode.UInt16:
                        bits = Convert.ToUInt16(value, CultureInfo.InvariantCulture);
                        return true;
                    case TypeCode.Int32:
                        bits = unchecked((ulong)(uint)Convert.ToInt32(value, CultureInfo.InvariantCulture));
                        return true;
                    case TypeCode.UInt32:
                        bits = Convert.ToUInt32(value, CultureInfo.InvariantCulture);
                        return true;
                    case TypeCode.Int64:
                        bits = unchecked((ulong)Convert.ToInt64(value, CultureInfo.InvariantCulture));
                        return true;
                    case TypeCode.UInt64:
                        bits = Convert.ToUInt64(value, CultureInfo.InvariantCulture);
                        return true;
                    default:
                        return false;
                }
            }
            catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
            {
                return false;
            }
        }

        private static bool TryResolveScriptPath(string npcDirectory, string scriptFile, out string path)
        {
            path = null;

            try
            {
                path = Path.Combine(npcDirectory, scriptFile);
                if (!Path.HasExtension(path))
                    path += ".txt";

                path = Path.GetFullPath(path);
                return IsPathInsideDirectory(npcDirectory, path);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
            {
                SEnvir.Log($"NPC 脚本路径无效 `{scriptFile}`: {ex.Message}");
                return false;
            }
        }

        private static bool IsPathInsideDirectory(string directory, string path)
        {
            string normalizedDirectory = Path.GetFullPath(directory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedPath = Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            return string.Equals(normalizedPath, normalizedDirectory, StringComparison.OrdinalIgnoreCase) ||
                   normalizedPath.StartsWith(normalizedDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                   normalizedPath.StartsWith(normalizedDirectory + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
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

            string trimmed = StripComment(line).Trim();
            if (trimmed.Length == 0) return string.Empty;

            return trimmed;
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

        public static List<string> TokenizeLine(string line)
        {
            List<string> tokens = [];
            if (string.IsNullOrWhiteSpace(line)) return tokens;

            StringBuilder current = new();
            bool quoted = false, escaped = false, tokenStarted = false;

            foreach (char c in line)
            {
                if (escaped)
                {
                    current.Append(c);
                    tokenStarted = true;
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
                    tokenStarted = true;
                    continue;
                }

                if (!quoted && char.IsWhiteSpace(c))
                {
                    if (tokenStarted)
                    {
                        tokens.Add(current.ToString());
                        current.Clear();
                        tokenStarted = false;
                    }

                    continue;
                }

                current.Append(c);
                tokenStarted = true;
            }

            if (tokenStarted)
                tokens.Add(current.ToString());

            return tokens;
        }

        private static string GetCommandRemainder(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return string.Empty;

            int index = 0;
            while (index < line.Length && !char.IsWhiteSpace(line[index]))
                index++;

            while (index < line.Length && char.IsWhiteSpace(line[index]))
                index++;

            return index >= line.Length ? string.Empty : line[index..].Trim();
        }
    }

    public sealed class TextNpcExporter
    {
        public TextNpcExportReport Export(string npcDirectory, IEnumerable<NPCInfo> npcs, bool overwrite)
        {
            return new TextNpcDatabaseExporter().Export(npcDirectory, npcs, overwrite);
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
        public List<string> MissingCurrencies { get; } = [];

        public string ToDisplayText()
        {
            return $"NPCInfo行={InfoLines}, 脚本={ScriptFiles}, Include={IncludeFiles}, 页面={PagesLoaded}, NPC={NpcsLoaded}, 覆盖={OverrideNpcs}, 跳过={NpcsSkipped}, 缺地图={MissingMaps.Count}, 缺物品={MissingItems.Count}, 缺货币={MissingCurrencies.Count}, 错误={Failures.Count}";
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
        private static readonly Regex PageReferenceRegex = new(@"^(?<Key>@?[\w\-.]+)(?:\((?<Args>.*)\))?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ArgumentTokenRegex = new(@"\%ARG\((?<Index>\d+)\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public string ScriptFile;
        public Dictionary<string, CompiledNpcPage> Pages { get; } = new(StringComparer.OrdinalIgnoreCase);
        public CompiledNpcPage EntryPage => Pages.TryGetValue("@MAIN", out CompiledNpcPage page) ? page : Pages.Values.FirstOrDefault();
        public CompiledNpcPageCall EntryPageCall => EntryPage == null ? null : new CompiledNpcPageCall(EntryPage, []);

        public CompiledNpcPage GetPage(string key)
        {
            return GetPageCall(key)?.Page;
        }

        public CompiledNpcPageCall GetPageCall(string reference, IReadOnlyList<string> inheritedArguments = null)
        {
            CompiledNpcPageReference pageReference = ParsePageReference(ReplaceArguments(reference, inheritedArguments));
            return Pages.TryGetValue(pageReference.Key, out CompiledNpcPage page) ? new CompiledNpcPageCall(page, pageReference.Arguments) : null;
        }

        public static string NormalizePageKey(string key)
        {
            key = (key ?? string.Empty).Trim();
            int argumentStart = key.IndexOf('(');
            if (argumentStart >= 0)
                key = key[..argumentStart].Trim();

            if (key.Length == 0) return "@MAIN";
            if (!key.StartsWith("@", StringComparison.Ordinal)) key = "@" + key;

            return key.ToUpperInvariant();
        }

        public static string NormalizePageReference(string reference)
        {
            reference = (reference ?? string.Empty).Trim();
            if (ContainsArgumentToken(reference)) return reference;

            return ParsePageReference(reference).ToReference();
        }

        public static string GetReferencePageKey(string reference)
        {
            return ParsePageReference(reference).Key;
        }

        public static bool ReferenceHasArguments(string reference)
        {
            return ParsePageReference(reference).Arguments.Count > 0;
        }

        public static bool ContainsArgumentToken(string value)
        {
            return !string.IsNullOrWhiteSpace(value) && ArgumentTokenRegex.IsMatch(value);
        }

        public static string ReplaceArguments(string value, IReadOnlyList<string> arguments)
        {
            if (value == null) return null;

            return ArgumentTokenRegex.Replace(value, match =>
            {
                return arguments != null && int.TryParse(match.Groups["Index"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) && index >= 0 && index < arguments.Count
                    ? arguments[index] ?? string.Empty
                    : string.Empty;
            });
        }

        public static string BuildPageReference(string key, IReadOnlyList<string> arguments)
        {
            key = NormalizePageKey(key);
            if (arguments == null || arguments.Count == 0) return key;

            return $"{key}({string.Join(",", arguments.Select(FormatPageArgument))})";
        }

        public static string FormatPageArgument(string argument)
        {
            if (string.IsNullOrEmpty(argument)) return "\"\"";

            bool quote = argument.Any(c => c == ',' || c == '"' || c == '\\' || char.IsWhiteSpace(c));
            if (!quote) return argument;

            return $"\"{argument.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
        }

        private static CompiledNpcPageReference ParsePageReference(string reference)
        {
            reference = (reference ?? string.Empty).Trim();
            if (reference.Length == 0)
                return new CompiledNpcPageReference("@MAIN", []);

            Match match = PageReferenceRegex.Match(reference);
            if (!match.Success)
                return new CompiledNpcPageReference(NormalizePageKey(reference), []);

            List<string> arguments = match.Groups["Args"].Success ? SplitPageArguments(match.Groups["Args"].Value) : [];
            return new CompiledNpcPageReference(NormalizePageKey(match.Groups["Key"].Value), arguments);
        }

        private static List<string> SplitPageArguments(string text)
        {
            List<string> result = [];
            if (text == null) return result;
            if (text.Length == 0) return result;

            StringBuilder current = new();
            bool quoted = false, escaped = false;

            foreach (char c in text)
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

                if (!quoted && c == ',')
                {
                    result.Add(current.ToString().Trim());
                    current.Clear();
                    continue;
                }

                current.Append(c);
            }

            result.Add(current.ToString().Trim());
            return result;
        }
    }

    public sealed class CompiledNpcPageCall
    {
        public CompiledNpcPage Page { get; }
        public List<string> Arguments { get; }

        public CompiledNpcPageCall(CompiledNpcPage page, IEnumerable<string> arguments)
        {
            Page = page;
            Arguments = arguments?.ToList() ?? [];
        }
    }

    public readonly struct CompiledNpcPageReference
    {
        public readonly string Key;
        public readonly List<string> Arguments;

        public CompiledNpcPageReference(string key, List<string> arguments)
        {
            Key = key;
            Arguments = arguments ?? [];
        }

        public string ToReference()
        {
            return Arguments.Count == 0 ? Key : $"{Key}({string.Join(",", Arguments.Select(CompiledNpcScript.FormatPageArgument))})";
        }
    }

    public sealed class CompiledNpcPage
    {
        public string Key;
        public string Source;
        public NPCDialogType DialogType;
        public CurrencyInfo Currency;
        public List<CompiledNpcCheck> Checks { get; } = [];
        public CompiledNpcBlock MainBlock { get; } = new();
        public CompiledNpcBlock ElseBlock { get; } = new();
        public List<CompiledTextNpcGood> Goods { get; } = [];
        public List<ItemType> Types { get; } = [];
        public string SuccessKey;
        public string SuccessSource;

        public ClientTextNPCPage ToClientPage(CompiledNpcBlock block, IReadOnlyList<string> arguments = null)
        {
            return new ClientTextNPCPage
            {
                Key = CompiledNpcScript.BuildPageReference(Key, arguments),
                Say = CompiledNpcScript.ReplaceArguments(block.Say ?? string.Empty, arguments),
                DialogType = DialogType,
                CurrencyIndex = Currency?.Index ?? 0,
                Buttons = block.Buttons.Select(x => new ClientTextNPCButton
                {
                    ButtonID = x.ButtonID,
                    Text = CompiledNpcScript.ReplaceArguments(x.Text, arguments),
                    DestinationKey = CompiledNpcScript.ReplaceArguments(x.DestinationKey, arguments)
                }).ToList(),
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
        public string Source;
        public CompiledNpcCheckType CheckType;
        public Operator Operator;
        public int IntParameter1;
        public int IntParameter2;
        public string StringParameter1;
        public ItemInfo ItemParameter1;
        public Stat StatParameter1;
        public RequiredClass Class;
        public RequiredGender Gender = RequiredGender.None;
        public DaysOfWeek Days = DaysOfWeek.None;
        public NPCDataType DataType = NPCDataType.User;
        public bool UseRedPointThreshold;
    }

    public sealed class CompiledNpcAction
    {
        public string Source;
        public CompiledNpcActionType ActionType;
        public int IntParameter1;
        public int IntParameter2;
        public string StringParameter1;
        public ItemInfo ItemParameter1;
        public MapInfo MapParameter1;
        public Stat StatParameter1;
        public NPCDataType DataType = NPCDataType.User;
    }

    public enum CompiledNpcCheckType
    {
        Level,
        Gold,
        Item,
        CanGainItem,
        Weapon,
        WeaponCanRefine,
        WeaponCanReset,
        WeaponLevel,
        WeaponElement,
        WeaponAddedStats,
        Marriage,
        WeddingRing,
        Horse,
        Class,
        Gender,
        Fame,
        DayOfWeek,
        Hour,
        Minute,
        PKPoint,
        Random,
        NameList,
        Value,
        Currency
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
        ClearNameList,
        SetValue,
        ChangeValue,
        LocalMessage,
        GiveCurrency,
        TakeCurrency,
        PromoteFame,
        Marriage,
        Divorce,
        RemoveWeddingRing,
        ResetWeapon,
        Rebirth,
        ChangeHorse,
        GiveItemExperience,
        ChangeElement,
        SpecialRefine
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
