using Library;
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
    public enum TextNpcDatabaseImportMode
    {
        ValidateOnly,
        ReplaceExisting,
        Merge
    }

    public sealed class TextNpcDatabaseImporter
    {
        private readonly Session _session;
        private readonly DBCollection<NPCInfo> _npcs;
        private readonly DBCollection<MapRegion> _regions;
        private readonly DBCollection<NPCPage> _pages;
        private readonly DBCollection<NPCCheck> _checks;
        private readonly DBCollection<NPCAction> _actions;
        private readonly DBCollection<NPCButton> _buttons;
        private readonly DBCollection<NPCGood> _goods;
        private readonly DBCollection<NPCType> _types;

        public TextNpcDatabaseImporter(Session session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _npcs = session.GetCollection<NPCInfo>();
            _regions = session.GetCollection<MapRegion>();
            _pages = session.GetCollection<NPCPage>();
            _checks = session.GetCollection<NPCCheck>();
            _actions = session.GetCollection<NPCAction>();
            _buttons = session.GetCollection<NPCButton>();
            _goods = session.GetCollection<NPCGood>();
            _types = session.GetCollection<NPCType>();
        }

        public TextNpcDatabaseImportReport Import(string npcDirectory, TextNpcDatabaseImportMode mode, bool strictMode = true)
        {
            if (string.IsNullOrWhiteSpace(npcDirectory))
                throw new ArgumentException("NPC 文本目录不能为空。", nameof(npcDirectory));

            TextNpcDatabaseImportReport report = new() { Mode = mode };
            TextNpcImporter loader = new(_session);
            TextNpcLoadResult loadResult = loader.Build(npcDirectory, strictMode);
            report.LoadReport = loadResult.Report;

            foreach (CompiledTextNpcInfo info in loadResult.NPCs)
            {
                if (!ValidateForDatabase(info, report))
                {
                    report.NpcsSkipped++;
                    continue;
                }

                if (mode == TextNpcDatabaseImportMode.ValidateOnly)
                {
                    report.NpcsValidated++;
                    continue;
                }

                ImportNpc(info, mode, report);
            }

            return report;
        }

        private bool ValidateForDatabase(CompiledTextNpcInfo info, TextNpcDatabaseImportReport report)
        {
            bool result = true;

            foreach (CompiledNpcPage page in info.Script.Pages.Values)
            {
                foreach (CompiledNpcCheck check in page.Checks)
                {
                    if (check.CheckType == CompiledNpcCheckType.Class && !CanMapClassCheck(check.Class))
                    {
                        report.Failures.Add(FormatIssue(info, page, check.Source, $"CHECKCLASS `{check.Class}` 不能无损导入旧数据库条件，只支持 All/None 或单职业。"));
                        result = false;
                    }
                }

                if (!ValidateBlockForDatabase(info, page, page.MainBlock, "主分支", report))
                    result = false;

                if (!ValidateBlockForDatabase(info, page, page.ElseBlock, "ELSE 分支", report))
                    result = false;
            }

            return result;
        }

        private static bool ValidateBlockForDatabase(CompiledTextNpcInfo info, CompiledNpcPage page, CompiledNpcBlock block, string branch, TextNpcDatabaseImportReport report)
        {
            bool result = true;
            int gotoCount = block.Actions.Count(x => x.ActionType == CompiledNpcActionType.Goto);

            if (gotoCount > 1)
            {
                string source = block.Actions.FirstOrDefault(x => x.ActionType == CompiledNpcActionType.Goto)?.Source;
                report.Failures.Add(FormatIssue(info, page, source, $"{branch}: 旧数据库 SuccessPage 只能表达一个 GOTO。"));
                result = false;
            }

            if (gotoCount > 0 && !string.IsNullOrWhiteSpace(block.Say))
            {
                string source = block.Actions.FirstOrDefault(x => x.ActionType == CompiledNpcActionType.Goto)?.Source;
                report.Failures.Add(FormatIssue(info, page, source, $"{branch}: 带对话文本的 GOTO 不能无损导入旧数据库模式。"));
                result = false;
            }

            CompiledNpcAction gotoAction = block.Actions.FirstOrDefault(x => x.ActionType == CompiledNpcActionType.Goto);
            string gotoTarget = gotoAction?.StringParameter1;
            if (!string.IsNullOrWhiteSpace(gotoTarget) &&
                !string.IsNullOrWhiteSpace(page.SuccessKey) &&
                !string.Equals(gotoTarget, page.SuccessKey, StringComparison.OrdinalIgnoreCase))
            {
                report.Failures.Add(FormatIssue(info, page, gotoAction.Source, $"{branch}: GOTO `{gotoTarget}` 与 #SUCCESS `{page.SuccessKey}` 不能同时无损导入旧数据库。"));
                result = false;
            }

            foreach (CompiledNpcAction action in block.Actions)
            {
                if (action.ActionType == CompiledNpcActionType.LocalMessage)
                {
                    report.Failures.Add(FormatIssue(info, page, action.Source, $"{branch}: LOCALMESSAGE 当前只能在文本运行时执行，旧数据库动作没有等价运行分支。"));
                    result = false;
                }
            }

            return result;
        }

        private void ImportNpc(CompiledTextNpcInfo info, TextNpcDatabaseImportMode mode, TextNpcDatabaseImportReport report)
        {
            NPCInfo target = FindTargetNpc(info);
            bool created = false;

            if (target == null)
            {
                target = _npcs.CreateNewObject();
                created = true;
                report.NpcsCreated++;
            }
            else if (mode == TextNpcDatabaseImportMode.ReplaceExisting)
            {
                ClearOwnedPageTree(target);
                report.NpcsReplaced++;
            }
            else
            {
                report.NpcsMerged++;
            }

            ApplyNpcInfo(target, info);

            Dictionary<string, NpcPageBuild> pageBuilds = CreatePages(info);

            foreach (NpcPageBuild build in pageBuilds.Values)
            {
                PopulatePage(info, build.SourcePage, build.SourcePage.MainBlock, build.MainPage, pageBuilds, build.ElsePage, true, report);

                if (build.ElsePage != null)
                    PopulatePage(info, build.SourcePage, build.SourcePage.ElseBlock, build.ElsePage, pageBuilds, null, false, report);
            }

            target.EntryPage = pageBuilds.TryGetValue("@MAIN", out NpcPageBuild entry) ? entry.MainPage : pageBuilds.Values.First().MainPage;

            if (!created && mode != TextNpcDatabaseImportMode.ReplaceExisting)
                report.NpcsUpdated++;

            report.NpcsImported++;
        }

        private NPCInfo FindTargetNpc(CompiledTextNpcInfo info)
        {
            if (info.OverrideNPC != null) return info.OverrideNPC;

            if (info.OverrideIndex > 0)
            {
                NPCInfo byIndex = _npcs.Binding.FirstOrDefault(x => x.Index == info.OverrideIndex);
                if (byIndex != null) return byIndex;
            }

            return _npcs.Binding.FirstOrDefault(x =>
                string.Equals(x.NPCName, info.NPCName, StringComparison.OrdinalIgnoreCase) &&
                x.Region?.Map == info.Map &&
                RegionContains(x.Region, info.Location));
        }

        private void ApplyNpcInfo(NPCInfo target, CompiledTextNpcInfo info)
        {
            target.NPCName = info.NPCName;
            target.Image = info.Image;
            target.FaceImage = info.FaceImage;
            target.MapIcon = info.MapIcon;

            if (target.Region == null || target.Region.Map != info.Map || !RegionContains(target.Region, info.Location))
                target.Region = CreateSinglePointRegion(info);
        }

        private MapRegion CreateSinglePointRegion(CompiledTextNpcInfo info)
        {
            MapRegion region = _regions.CreateNewObject();
            region.Map = info.Map;
            region.Description = $"NPC {info.NPCName} {info.Location.X},{info.Location.Y}";
            region.RegionType = RegionType.Npc;
            region.PointRegion = [info.Location];
            region.PointList = [info.Location];
            return region;
        }

        private Dictionary<string, NpcPageBuild> CreatePages(CompiledTextNpcInfo info)
        {
            Dictionary<string, NpcPageBuild> result = new(StringComparer.OrdinalIgnoreCase);

            foreach (CompiledNpcPage sourcePage in info.Script.Pages.Values)
            {
                NPCPage main = CreatePage(info, sourcePage, sourcePage.Key);
                NPCPage elsePage = sourcePage.ElseBlock.IsEmpty ? null : CreatePage(info, sourcePage, $"{sourcePage.Key}_ELSE");

                result[sourcePage.Key] = new NpcPageBuild
                {
                    SourcePage = sourcePage,
                    MainPage = main,
                    ElsePage = elsePage
                };
            }

            return result;
        }

        private NPCPage CreatePage(CompiledTextNpcInfo info, CompiledNpcPage sourcePage, string key)
        {
            NPCPage page = _pages.CreateNewObject();
            page.Description = $"{info.NPCName}:{key}";
            page.DialogType = sourcePage.DialogType;
            page.Currency = sourcePage.Currency;
            return page;
        }

        private void PopulatePage(
            CompiledTextNpcInfo info,
            CompiledNpcPage sourcePage,
            CompiledNpcBlock block,
            NPCPage targetPage,
            Dictionary<string, NpcPageBuild> pageBuilds,
            NPCPage failPage,
            bool includePageData,
            TextNpcDatabaseImportReport report)
        {
            targetPage.Say = block.Say ?? string.Empty;
            targetPage.SuccessPage = ResolveSuccessPage(sourcePage.SuccessKey, block, pageBuilds);

            if (includePageData)
            {
                foreach (CompiledNpcCheck compiledCheck in sourcePage.Checks)
                {
                    NPCCheck check = CreateCheck(compiledCheck);
                    if (check == null) continue;

                    check.Page = targetPage;
                    check.FailPage = failPage;
                    report.ChecksCreated++;
                }
            }

            foreach (CompiledNpcAction compiledAction in block.Actions)
            {
                if (compiledAction.ActionType == CompiledNpcActionType.Goto) continue;

                NPCAction action = CreateAction(compiledAction);
                if (action == null)
                {
                    report.Failures.Add(FormatIssue(info, sourcePage, compiledAction.Source, $"动作 `{compiledAction.ActionType}` 没有旧数据库映射。"));
                    continue;
                }

                action.Page = targetPage;
                report.ActionsCreated++;
            }

            foreach (CompiledNpcButton compiledButton in block.Buttons)
            {
                if (!pageBuilds.TryGetValue(compiledButton.DestinationKey, out NpcPageBuild destination))
                {
                    report.Failures.Add($"{info.NPCName} {sourcePage.Key}: 按钮 `{compiledButton.Text}` 目标页 `{compiledButton.DestinationKey}` 不存在。");
                    continue;
                }

                NPCButton button = _buttons.CreateNewObject();
                button.Page = targetPage;
                button.ButtonID = compiledButton.ButtonID;
                button.DestinationPage = destination.MainPage;
                report.ButtonsCreated++;
            }

            if (!includePageData)
            {
                report.PagesCreated++;
                return;
            }

            foreach (CompiledTextNpcGood compiledGood in sourcePage.Goods)
            {
                NPCGood good = _goods.CreateNewObject();
                good.Page = targetPage;
                good.Item = compiledGood.Item;
                good.Rate = compiledGood.Rate;
                report.GoodsCreated++;
            }

            foreach (ItemType itemType in sourcePage.Types)
            {
                NPCType type = _types.CreateNewObject();
                type.Page = targetPage;
                type.ItemType = itemType;
                report.TypesCreated++;
            }

            report.PagesCreated++;
        }

        private NPCPage ResolveSuccessPage(string successKey, CompiledNpcBlock block, Dictionary<string, NpcPageBuild> pageBuilds)
        {
            string targetKey = block.Actions.FirstOrDefault(x => x.ActionType == CompiledNpcActionType.Goto)?.StringParameter1;
            if (string.IsNullOrWhiteSpace(targetKey))
                targetKey = successKey;

            if (string.IsNullOrWhiteSpace(targetKey)) return null;

            return pageBuilds.TryGetValue(targetKey, out NpcPageBuild build) ? build.MainPage : null;
        }

        private static string FormatIssue(CompiledTextNpcInfo info, CompiledNpcPage page, string source, string message)
        {
            string location = string.IsNullOrWhiteSpace(source) ? $"{info.NPCName} {page.Key}" : source;
            return $"{location}: {info.NPCName} {page.Key}: {message}";
        }

        private NPCCheck CreateCheck(CompiledNpcCheck source)
        {
            if (source.CheckType == CompiledNpcCheckType.Class && (source.Class == RequiredClass.None || source.Class == RequiredClass.All))
                return null;

            NPCCheck check = _checks.CreateNewObject();
            check.Operator = source.Operator;
            check.IntParameter1 = source.IntParameter1;
            check.IntParameter2 = source.IntParameter2;
            check.StringParameter1 = source.StringParameter1;
            check.ItemParameter1 = source.ItemParameter1;

            switch (source.CheckType)
            {
                case CompiledNpcCheckType.Level:
                    check.CheckType = NPCCheckType.Level;
                    break;
                case CompiledNpcCheckType.Gold:
                    check.CheckType = NPCCheckType.Gold;
                    break;
                case CompiledNpcCheckType.Currency:
                    check.CheckType = NPCCheckType.Currency;
                    break;
                case CompiledNpcCheckType.Item:
                    check.CheckType = NPCCheckType.HasItem;
                    break;
                case CompiledNpcCheckType.Class:
                    check.CheckType = NPCCheckType.Class;
                    check.Operator = Operator.Equal;
                    check.IntParameter1 = (int)ToMirClass(source.Class).Value;
                    break;
                case CompiledNpcCheckType.PKPoint:
                    check.CheckType = NPCCheckType.PKPoints;
                    break;
                case CompiledNpcCheckType.Random:
                    check.CheckType = NPCCheckType.Random;
                    break;
                case CompiledNpcCheckType.NameList:
                    check.CheckType = NPCCheckType.CheckDataList;
                    check.IntParameter1 = (int)source.DataType;
                    break;
                case CompiledNpcCheckType.Value:
                    check.CheckType = NPCCheckType.CheckDataValue;
                    check.IntParameter1 = (int)source.DataType;
                    check.IntParameter2 = source.IntParameter1;
                    break;
            }

            return check;
        }

        private NPCAction CreateAction(CompiledNpcAction source)
        {
            NPCAction action = _actions.CreateNewObject();
            action.IntParameter1 = source.IntParameter1;
            action.IntParameter2 = source.IntParameter2;
            action.StringParameter1 = source.StringParameter1;
            action.ItemParameter1 = source.ItemParameter1;
            action.MapParameter1 = source.MapParameter1;

            switch (source.ActionType)
            {
                case CompiledNpcActionType.GiveGold:
                    action.ActionType = NPCActionType.GiveGold;
                    break;
                case CompiledNpcActionType.TakeGold:
                    action.ActionType = NPCActionType.TakeGold;
                    break;
                case CompiledNpcActionType.GiveCurrency:
                    action.ActionType = NPCActionType.GiveCurrency;
                    break;
                case CompiledNpcActionType.TakeCurrency:
                    action.ActionType = NPCActionType.TakeCurrency;
                    break;
                case CompiledNpcActionType.GiveItem:
                    action.ActionType = NPCActionType.GiveItem;
                    break;
                case CompiledNpcActionType.TakeItem:
                    action.ActionType = NPCActionType.TakeItem;
                    break;
                case CompiledNpcActionType.Move:
                    action.ActionType = NPCActionType.Teleport;
                    break;
                case CompiledNpcActionType.AddNameList:
                    action.ActionType = NPCActionType.AddDataList;
                    action.IntParameter1 = (int)source.DataType;
                    break;
                case CompiledNpcActionType.DeleteNameList:
                    action.ActionType = NPCActionType.RemoveDataList;
                    action.IntParameter1 = (int)source.DataType;
                    break;
                case CompiledNpcActionType.SetValue:
                    action.ActionType = NPCActionType.SetDataValue;
                    action.IntParameter1 = (int)source.DataType;
                    action.IntParameter2 = source.IntParameter1;
                    break;
                case CompiledNpcActionType.ChangeValue:
                    action.ActionType = NPCActionType.ChangeDataValue;
                    action.IntParameter1 = (int)source.DataType;
                    action.IntParameter2 = source.IntParameter1;
                    break;
                default:
                    action.Delete();
                    return null;
            }

            return action;
        }

        private void ClearOwnedPageTree(NPCInfo npc)
        {
            if (npc.EntryPage == null) return;

            HashSet<NPCPage> ownPages = CollectPages(npc.EntryPage);
            HashSet<NPCPage> otherPages = [];

            foreach (NPCInfo other in _npcs.Binding)
            {
                if (other == npc || other.EntryPage == null) continue;

                foreach (NPCPage page in CollectPages(other.EntryPage))
                    otherPages.Add(page);
            }

            npc.EntryPage = null;

            foreach (NPCPage page in ownPages.Where(x => !otherPages.Contains(x)).ToArray())
                DeletePage(page);
        }

        private static HashSet<NPCPage> CollectPages(NPCPage entryPage)
        {
            HashSet<NPCPage> result = [];
            Queue<NPCPage> queue = new();
            queue.Enqueue(entryPage);

            while (queue.Count > 0)
            {
                NPCPage page = queue.Dequeue();
                if (page == null || !result.Add(page)) continue;

                if (page.SuccessPage != null)
                    queue.Enqueue(page.SuccessPage);

                foreach (NPCButton button in page.Buttons)
                    if (button.DestinationPage != null)
                        queue.Enqueue(button.DestinationPage);

                foreach (NPCCheck check in page.Checks)
                    if (check.FailPage != null)
                        queue.Enqueue(check.FailPage);
            }

            return result;
        }

        private static void DeletePage(NPCPage page)
        {
            for (int i = page.Buttons.Count - 1; i >= 0; i--)
                page.Buttons[i].Delete();

            for (int i = page.Actions.Count - 1; i >= 0; i--)
                page.Actions[i].Delete();

            for (int i = page.Checks.Count - 1; i >= 0; i--)
                page.Checks[i].Delete();

            for (int i = page.Goods.Count - 1; i >= 0; i--)
                page.Goods[i].Delete();

            for (int i = page.Types.Count - 1; i >= 0; i--)
                page.Types[i].Delete();

            for (int i = page.Values.Count - 1; i >= 0; i--)
                page.Values[i].Delete();

            page.Delete();
        }

        private static bool RegionContains(MapRegion region, Point point)
        {
            if (region == null) return false;

            if (region.PointList != null && region.PointList.Contains(point)) return true;
            if (region.PointRegion != null && region.PointRegion.Contains(point)) return true;

            return false;
        }

        private static bool CanMapClassCheck(RequiredClass requiredClass)
        {
            return requiredClass == RequiredClass.None ||
                   requiredClass == RequiredClass.All ||
                   ToMirClass(requiredClass) != null;
        }

        private static MirClass? ToMirClass(RequiredClass requiredClass)
        {
            return requiredClass switch
            {
                RequiredClass.Warrior => MirClass.Warrior,
                RequiredClass.Wizard => MirClass.Wizard,
                RequiredClass.Taoist => MirClass.Taoist,
                RequiredClass.Assassin => MirClass.Assassin,
                _ => null
            };
        }

        private sealed class NpcPageBuild
        {
            public CompiledNpcPage SourcePage;
            public NPCPage MainPage;
            public NPCPage ElsePage;
        }
    }

    public sealed class TextNpcDatabaseExporter
    {
        private static readonly Regex DbButtonRegex = new(@"\[(?<Text>[^\[\]:]+):(?<ID>\d+)\]", RegexOptions.Compiled);

        public TextNpcExportReport Export(string npcDirectory, IEnumerable<NPCInfo> npcs, bool overwrite)
        {
            if (string.IsNullOrWhiteSpace(npcDirectory))
                throw new ArgumentException("NPC 导出目录不能为空。", nameof(npcDirectory));

            Directory.CreateDirectory(npcDirectory);

            TextNpcExportReport report = new();
            string infoPath = Path.Combine(npcDirectory, "NPCInfo.txt");
            if (File.Exists(infoPath) && !overwrite)
                throw new IOException($"导出文件已存在: {infoPath}");

            StringBuilder info = new();

            foreach (NPCInfo npc in npcs.Where(x => x?.Region?.Map != null && x.EntryPage != null).OrderBy(x => x.Index))
            {
                report.NpcsVisited++;

                Point point = npc.Region.PointList?.FirstOrDefault() ?? npc.Region.PointRegion?.FirstOrDefault() ?? Point.Empty;
                string scriptFile = $"{SanitizeFileName(npc.NPCName)}_{npc.Index}.txt";

                info.AppendLine($"{Quote(npc.Region.Map.FileName)} {point.X} {point.Y} {Quote(npc.NPCName)} {npc.Image} {npc.FaceImage} {Quote(scriptFile)} MapIcon={npc.MapIcon} OverrideIndex={npc.Index}");
                ExportScript(Path.Combine(npcDirectory, scriptFile), npc, overwrite, report);
                report.NpcsExported++;
            }

            File.WriteAllText(infoPath, info.ToString(), Encoding.UTF8);
            report.InfoPath = infoPath;
            return report;
        }

        private static void ExportScript(string path, NPCInfo npc, bool overwrite, TextNpcExportReport report)
        {
            if (File.Exists(path) && !overwrite)
                throw new IOException($"导出文件已存在: {path}");

            Dictionary<NPCPage, string> keys = BuildPageKeys(npc.EntryPage);
            HashSet<NPCPage> embeddedElsePages = FindEmbeddableElsePages(keys.Keys);

            StringBuilder script = new();

            foreach (KeyValuePair<NPCPage, string> pair in keys)
            {
                NPCPage page = pair.Key;
                if (embeddedElsePages.Contains(page)) continue;

                string key = pair.Value;
                script.AppendLine($"[{key}]");

                if (page.DialogType != NPCDialogType.None && !DialogTypeMatchesKey(page.DialogType, key))
                    script.AppendLine($"#DIALOGTYPE {page.DialogType}");

                if (page.Checks.Count > 0)
                {
                    script.AppendLine("#IF");
                    foreach (NPCCheck check in page.Checks)
                        ExportCheck(script, npc, page, check, report);
                }

                script.AppendLine("#SAY");
                script.AppendLine(ConvertButtonsForExport(page.Say, page, keys, report));

                if (page.Actions.Count > 0)
                {
                    script.AppendLine("#ACT");
                    foreach (NPCAction action in page.Actions)
                        ExportAction(script, npc, page, action, report);
                }

                NPCPage elsePage = GetCommonFailPage(page);
                if (elsePage != null && embeddedElsePages.Contains(elsePage))
                {
                    if (!string.IsNullOrWhiteSpace(elsePage.Say))
                    {
                        script.AppendLine("#ELSESAY");
                        script.AppendLine(ConvertButtonsForExport(elsePage.Say, elsePage, keys, report));
                    }

                    if (elsePage.Actions.Count > 0)
                    {
                        script.AppendLine("#ELSEACT");
                        foreach (NPCAction action in elsePage.Actions)
                            ExportAction(script, npc, elsePage, action, report);
                    }
                }

                if (page.SuccessPage != null && keys.TryGetValue(page.SuccessPage, out string successKey))
                    script.AppendLine($"#SUCCESS {successKey}");

                ExportTrade(script, page, report);
                ExportTypes(script, page, report);
                ExportCurrency(script, page);
                ExportUnsupportedValues(script, npc, page, report);

                script.AppendLine();
                report.PagesExported++;
            }

            File.WriteAllText(path, script.ToString(), Encoding.UTF8);
        }

        private static Dictionary<NPCPage, string> BuildPageKeys(NPCPage entryPage)
        {
            Dictionary<NPCPage, string> keys = [];
            Queue<NPCPage> queue = new();

            keys[entryPage] = "@MAIN";
            queue.Enqueue(entryPage);

            while (queue.Count > 0)
            {
                NPCPage page = queue.Dequeue();

                foreach (NPCButton button in page.Buttons)
                    AddPage(button.DestinationPage);

                foreach (NPCCheck check in page.Checks)
                    AddPage(check.FailPage);

                AddPage(page.SuccessPage);
            }

            return keys;

            void AddPage(NPCPage page)
            {
                if (page == null || keys.ContainsKey(page)) return;

                string preferred = GetPreferredPageKey(page.DialogType);
                if (preferred != null && !keys.ContainsValue(preferred))
                    keys[page] = preferred;
                else
                    keys[page] = $"@PAGE{keys.Count + 1}";

                queue.Enqueue(page);
            }
        }

        private static HashSet<NPCPage> FindEmbeddableElsePages(IEnumerable<NPCPage> pages)
        {
            List<NPCPage> pageList = pages.ToList();
            Dictionary<NPCPage, int> incoming = [];

            foreach (NPCPage page in pageList)
            {
                foreach (NPCButton button in page.Buttons)
                    Count(button.DestinationPage);

                if (page.SuccessPage != null)
                    Count(page.SuccessPage);

                foreach (NPCCheck check in page.Checks)
                    Count(check.FailPage);
            }

            HashSet<NPCPage> result = [];
            foreach (NPCPage page in pageList)
            {
                NPCPage failPage = GetCommonFailPage(page);
                if (failPage == null) continue;
                if (!CanEmbedElsePage(failPage)) continue;
                if (incoming.TryGetValue(failPage, out int count) && count == page.Checks.Count)
                    result.Add(failPage);
            }

            return result;

            void Count(NPCPage page)
            {
                if (page == null) return;
                incoming.TryGetValue(page, out int count);
                incoming[page] = count + 1;
            }
        }

        private static bool CanEmbedElsePage(NPCPage page)
        {
            return page.Checks.Count == 0 &&
                   page.Goods.Count == 0 &&
                   page.Types.Count == 0 &&
                   page.Values.Count == 0 &&
                   page.Currency == null &&
                   page.DialogType == NPCDialogType.None &&
                   page.SuccessPage == null;
        }

        private static NPCPage GetCommonFailPage(NPCPage page)
        {
            if (page.Checks.Count == 0) return null;

            NPCPage failPage = page.Checks[0].FailPage;
            if (failPage == null) return null;

            foreach (NPCCheck check in page.Checks)
                if (check.FailPage != failPage)
                    return null;

            return failPage;
        }

        private static void ExportCheck(StringBuilder script, NPCInfo npc, NPCPage page, NPCCheck check, TextNpcExportReport report)
        {
            switch (check.CheckType)
            {
                case NPCCheckType.Level:
                    script.AppendLine($"LEVEL {OperatorText(check.Operator)} {check.IntParameter1}");
                    report.ChecksExported++;
                    break;
                case NPCCheckType.Gold:
                    script.AppendLine($"CHECKGOLD {OperatorText(check.Operator)} {check.IntParameter1}");
                    report.ChecksExported++;
                    break;
                case NPCCheckType.Currency:
                    script.AppendLine($"CHECKCURRENCY {Quote(check.StringParameter1)} {OperatorText(check.Operator)} {check.IntParameter1}");
                    report.ChecksExported++;
                    break;
                case NPCCheckType.HasItem:
                    if (check.ItemParameter1 != null && check.Operator == Operator.GreaterThanOrEqual)
                    {
                        script.AppendLine($"CHECKITEM {Quote(check.ItemParameter1.ItemName)} {check.IntParameter1}");
                        report.ChecksExported++;
                    }
                    else
                    {
                        AddTodo(script, report, npc, page, $"手动迁移条件: {check.CheckType} {check.Operator}");
                    }
                    break;
                case NPCCheckType.Class:
                    if (check.Operator == Operator.Equal && Enum.IsDefined(typeof(MirClass), check.IntParameter1))
                    {
                        script.AppendLine($"CHECKCLASS {(MirClass)check.IntParameter1}");
                        report.ChecksExported++;
                    }
                    else
                    {
                        AddTodo(script, report, npc, page, $"手动迁移职业条件: Operator={check.Operator}, Value={check.IntParameter1}");
                    }
                    break;
                case NPCCheckType.PKPoints:
                    script.AppendLine($"CHECKPKPOINT {OperatorText(check.Operator)} {check.IntParameter1}");
                    report.ChecksExported++;
                    break;
                case NPCCheckType.Random:
                    if (check.Operator == Operator.Equal)
                    {
                        script.AppendLine($"RANDOM {check.IntParameter1} {check.IntParameter2}");
                        report.ChecksExported++;
                    }
                    else
                    {
                        AddTodo(script, report, npc, page, $"手动迁移随机条件: Operator={check.Operator}, Max={check.IntParameter1}, Value={check.IntParameter2}");
                    }
                    break;
                case NPCCheckType.CheckDataList:
                    script.AppendLine($"CHECKNAMELIST {Quote(check.StringParameter1)} {(NPCDataType)check.IntParameter1}");
                    report.ChecksExported++;
                    break;
                case NPCCheckType.CheckDataValue:
                    script.AppendLine($"CHECKVALUE {Quote(check.StringParameter1)} {OperatorText(check.Operator)} {check.IntParameter2} {(NPCDataType)check.IntParameter1}");
                    report.ChecksExported++;
                    break;
                default:
                    AddTodo(script, report, npc, page, $"手动迁移条件: {check.CheckType}");
                    break;
            }

            if (check.FailPage != null && GetCommonFailPage(page) != check.FailPage)
                AddTodo(script, report, npc, page, $"条件失败页不能自动合并为 #ELSESAY: {check.FailPage.Description}");
        }

        private static void ExportAction(StringBuilder script, NPCInfo npc, NPCPage page, NPCAction action, TextNpcExportReport report)
        {
            switch (action.ActionType)
            {
                case NPCActionType.Teleport:
                    if (action.MapParameter1 != null && action.InstanceParameter1 == null)
                    {
                        script.AppendLine($"MOVE {Quote(action.MapParameter1.FileName)} {action.IntParameter1} {action.IntParameter2}");
                        report.ActionsExported++;
                    }
                    else
                    {
                        AddTodo(script, report, npc, page, $"手动迁移传送动作: {action.InstanceParameter1}");
                    }
                    break;
                case NPCActionType.GiveGold:
                    script.AppendLine($"GIVEGOLD {action.IntParameter1}");
                    report.ActionsExported++;
                    break;
                case NPCActionType.TakeGold:
                    script.AppendLine($"TAKEGOLD {action.IntParameter1}");
                    report.ActionsExported++;
                    break;
                case NPCActionType.GiveCurrency:
                    script.AppendLine($"GIVECURRENCY {Quote(action.StringParameter1)} {action.IntParameter1}");
                    report.ActionsExported++;
                    break;
                case NPCActionType.TakeCurrency:
                    script.AppendLine($"TAKECURRENCY {Quote(action.StringParameter1)} {action.IntParameter1}");
                    report.ActionsExported++;
                    break;
                case NPCActionType.GiveItem:
                    if (action.ItemParameter1 != null)
                    {
                        script.AppendLine($"GIVEITEM {Quote(action.ItemParameter1.ItemName)} {action.IntParameter1}");
                        report.ActionsExported++;
                    }
                    else
                    {
                        AddTodo(script, report, npc, page, "手动迁移给物品动作: 缺少 ItemParameter1");
                    }
                    break;
                case NPCActionType.TakeItem:
                    if (action.ItemParameter1 != null)
                    {
                        script.AppendLine($"TAKEITEM {Quote(action.ItemParameter1.ItemName)} {action.IntParameter1}");
                        report.ActionsExported++;
                    }
                    else
                    {
                        AddTodo(script, report, npc, page, "手动迁移收物品动作: 缺少 ItemParameter1");
                    }
                    break;
                case NPCActionType.AddDataList:
                    script.AppendLine($"ADDNAMELIST {Quote(action.StringParameter1)} {(NPCDataType)action.IntParameter1}");
                    report.ActionsExported++;
                    break;
                case NPCActionType.RemoveDataList:
                    script.AppendLine($"DELNAMELIST {Quote(action.StringParameter1)} {(NPCDataType)action.IntParameter1}");
                    report.ActionsExported++;
                    break;
                case NPCActionType.SetDataValue:
                    script.AppendLine($"SETVALUE {Quote(action.StringParameter1)} {action.IntParameter2} {(NPCDataType)action.IntParameter1}");
                    report.ActionsExported++;
                    break;
                case NPCActionType.ChangeDataValue:
                    script.AppendLine($"CHANGEVALUE {Quote(action.StringParameter1)} {action.IntParameter2} {(NPCDataType)action.IntParameter1}");
                    report.ActionsExported++;
                    break;
                default:
                    AddTodo(script, report, npc, page, $"手动迁移动作: {action.ActionType}");
                    break;
            }
        }

        private static void ExportTrade(StringBuilder script, NPCPage page, TextNpcExportReport report)
        {
            if (page.Goods.Count == 0) return;

            script.AppendLine("[TRADE]");
            foreach (NPCGood good in page.Goods)
            {
                if (good.Item == null) continue;

                script.AppendLine($"{Quote(good.Item.ItemName)} {good.Rate}");
                report.GoodsExported++;
            }
        }

        private static void ExportTypes(StringBuilder script, NPCPage page, TextNpcExportReport report)
        {
            if (page.Types.Count == 0) return;

            script.AppendLine("[TYPES]");
            script.AppendLine(string.Join(" ", page.Types.Select(x => x.ItemType)));
            report.TypesExported += page.Types.Count;
        }

        private static void ExportCurrency(StringBuilder script, NPCPage page)
        {
            if (page.Currency == null) return;

            script.AppendLine("[CURRENCY]");
            script.AppendLine(Quote(page.Currency.Name));
        }

        private static void ExportUnsupportedValues(StringBuilder script, NPCInfo npc, NPCPage page, TextNpcExportReport report)
        {
            foreach (NPCValue value in page.Values)
                AddTodo(script, report, npc, page, $"手动迁移动态值: {value.ValueType} ID={value.ValueID}");
        }

        private static string ConvertButtonsForExport(string say, NPCPage page, Dictionary<NPCPage, string> keys, TextNpcExportReport report)
        {
            if (string.IsNullOrWhiteSpace(say)) return string.Empty;

            Dictionary<int, NPCButton> buttons = page.Buttons
                .GroupBy(x => x.ButtonID)
                .ToDictionary(x => x.Key, x => x.First());

            return DbButtonRegex.Replace(say, match =>
            {
                if (!int.TryParse(match.Groups["ID"].Value, out int id)) return match.Value;
                if (id == 0) return $"<{match.Groups["Text"].Value}/@EXIT>";
                if (!buttons.TryGetValue(id, out NPCButton button)) return match.Value;
                if (button.DestinationPage == null || !keys.TryGetValue(button.DestinationPage, out string destination)) return match.Value;

                report.ButtonsExported++;
                return $"<{match.Groups["Text"].Value}/{destination}>";
            });
        }

        private static void AddTodo(StringBuilder script, TextNpcExportReport report, NPCInfo npc, NPCPage page, string message)
        {
            string text = $"{npc.NPCName}#{npc.Index} {page.Description}: {message}";
            report.Todos.Add(text);
            report.TodoLines++;
            script.AppendLine($"; TODO {message}");
        }

        private static string GetPreferredPageKey(NPCDialogType dialogType)
        {
            return dialogType switch
            {
                NPCDialogType.BuySell => "@BUYSELL",
                NPCDialogType.Repair => "@REPAIR",
                NPCDialogType.Refine => "@REFINE",
                NPCDialogType.RefineRetrieve => "@REFINERETRIEVE",
                NPCDialogType.CompanionManage => "@COMPANIONMANAGE",
                NPCDialogType.WeddingRing => "@WEDDINGRING",
                NPCDialogType.RefinementStone => "@REFINEMENTSTONE",
                NPCDialogType.MasterRefine => "@MASTERREFINE",
                NPCDialogType.WeaponReset => "@WEAPONRESET",
                NPCDialogType.ItemFragment => "@ITEMFRAGMENT",
                NPCDialogType.AccessoryRefineUpgrade => "@ACCESSORYREFINEUPGRADE",
                NPCDialogType.AccessoryRefineLevel => "@ACCESSORYREFINELEVEL",
                NPCDialogType.AccessoryReset => "@ACCESSORYRESET",
                NPCDialogType.WeaponCraft => "@WEAPONCRAFT",
                NPCDialogType.AccessoryRefine => "@ACCESSORYREFINE",
                NPCDialogType.RollDie => "@ROLLDIE",
                NPCDialogType.RollYut => "@ROLLYUT",
                _ => null
            };
        }

        private static bool DialogTypeMatchesKey(NPCDialogType dialogType, string key)
        {
            string preferred = GetPreferredPageKey(dialogType);
            return preferred != null && preferred.Equals(key, StringComparison.OrdinalIgnoreCase);
        }

        private static string OperatorText(Operator op)
        {
            return op switch
            {
                Operator.Equal => "==",
                Operator.NotEqual => "!=",
                Operator.LessThan => "<",
                Operator.LessThanOrEqual => "<=",
                Operator.GreaterThan => ">",
                Operator.GreaterThanOrEqual => ">=",
                _ => "=="
            };
        }

        private static string Quote(string value)
        {
            value ??= string.Empty;
            return $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
        }

        private static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');

            return string.IsNullOrWhiteSpace(name) ? "NPC" : name;
        }
    }

    public sealed class TextNpcDatabaseImportReport
    {
        public TextNpcDatabaseImportMode Mode;
        public TextNpcLoadReport LoadReport;
        public int NpcsValidated;
        public int NpcsImported;
        public int NpcsCreated;
        public int NpcsUpdated;
        public int NpcsReplaced;
        public int NpcsMerged;
        public int NpcsSkipped;
        public int PagesCreated;
        public int ButtonsCreated;
        public int ChecksCreated;
        public int ActionsCreated;
        public int GoodsCreated;
        public int TypesCreated;
        public List<string> Failures { get; } = [];

        public string ToDisplayText()
        {
            string load = LoadReport == null ? "未解析" : LoadReport.ToDisplayText();
            return
                $"模式={Mode}, 解析=({load})\r\n" +
                $"验证={NpcsValidated}, 导入={NpcsImported}, 新建={NpcsCreated}, 更新={NpcsUpdated}, 覆盖={NpcsReplaced}, 合并={NpcsMerged}, 跳过={NpcsSkipped}\r\n" +
                $"页面={PagesCreated}, 按钮={ButtonsCreated}, 条件={ChecksCreated}, 动作={ActionsCreated}, 商品={GoodsCreated}, 类型={TypesCreated}, 映射错误={Failures.Count}";
        }
    }

    public sealed class TextNpcExportReport
    {
        public string InfoPath;
        public int NpcsVisited;
        public int NpcsExported;
        public int PagesExported;
        public int ButtonsExported;
        public int ChecksExported;
        public int ActionsExported;
        public int GoodsExported;
        public int TypesExported;
        public int TodoLines;
        public List<string> Todos { get; } = [];

        public string ToDisplayText()
        {
            return
                $"输出 NPCInfo: {InfoPath}\r\n" +
                $"访问NPC={NpcsVisited}, 导出NPC={NpcsExported}, 页面={PagesExported}, 按钮={ButtonsExported}\r\n" +
                $"条件={ChecksExported}, 动作={ActionsExported}, 商品={GoodsExported}, 类型={TypesExported}, TODO={TodoLines}";
        }
    }
}
