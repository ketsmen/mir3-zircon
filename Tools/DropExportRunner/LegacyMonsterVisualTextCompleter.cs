using Library;
using Library.SystemModels;
using Server.Envir;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

internal static class LegacyMonsterVisualTextCompleter
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly Regex CaseRegex = new(@"case\s+MonsterImage\.(?<name>[A-Za-z0-9_]+)\s*:", RegexOptions.Compiled);
    private static readonly Regex DefaultRegex = new(@"^default\s*:", RegexOptions.Compiled);
    private static readonly Regex SwitchImageRegex = new(@"^switch\s*\(\s*Image\s*\)", RegexOptions.Compiled);
    private static readonly Regex LibraryRegex = new(@"LibraryFile\.(?<name>[A-Za-z0-9_]+)", RegexOptions.Compiled);
    private static readonly Regex BodyShapeAssignmentRegex = new(@"BodyShape\s*=\s*(?<value>[^;]+)", RegexOptions.Compiled);
    private static readonly Regex BodyShapeRegex = new(@"BodyShape\s*=\s*(?<value>-?\d+)", RegexOptions.Compiled);
    private static readonly Regex BodyOffsetAssignmentRegex = new(@"BodyOffSet\s*=\s*(?<value>[^;]+)", RegexOptions.Compiled);
    private static readonly Regex BodyOffsetRegex = new(@"BodyOffSet\s*=\s*(?<value>-?\d+)", RegexOptions.Compiled);
    private static readonly Regex SoundRegex = new(@"(?<field>AttackSound|RangeAttackSound|MagicSound|StruckSound|DieSound|ShowSound|HideSound)\s*=\s*SoundIndex\.(?<value>[A-Za-z0-9_]+)", RegexOptions.Compiled);
    private static readonly Regex FrameSetRegex = new(@"FrameSet\.(?<name>[A-Za-z0-9_]+)", RegexOptions.Compiled);
    private static readonly Dictionary<MonsterImage, string> KnownSkippedImages = new()
    {
        [MonsterImage.CastleFlag] = "依赖运行时 Overlay 绘制和 CastleFlagInfo 配色",
        [MonsterImage.Shinsu] = "旧客户端大/小神兽由 MonsterImage.Shinsu + Extra 运行时状态切换",
        [MonsterImage.Shinsu1] = "旧客户端大/小神兽由 MonsterImage.Shinsu + Extra 运行时状态切换",
        [MonsterImage.SDMob8] = "枚举存在但 Client\\Models\\MonsterObject.cs 主视觉 switch 没有 case；FrameSet.SDMob8 只由 OmaMage 分支使用，不能安全套用 default 图库",
        [MonsterImage.NumaKing] = "枚举存在但 Client\\Models\\MonsterObject.cs 主视觉 switch 没有 case，不能安全套用 default 图库",
    };

    public static void Complete(string outputRoot, string repositoryRoot, CustomSystemTextReport report)
    {
        if (string.IsNullOrWhiteSpace(outputRoot) || string.IsNullOrWhiteSpace(repositoryRoot))
            return;

        string monsterDirectory = Path.Combine(outputRoot, "Monsters");
        if (!Directory.Exists(monsterDirectory))
            return;

        string monsterObjectPath = Path.Combine(repositoryRoot, "Client", "Models", "MonsterObject.cs");
        LegacyVisualParseResult parseResult;
        try
        {
            parseResult = ParseVisualMap(monsterObjectPath);
        }
        catch (Exception ex) when (IsFileAccessException(ex))
        {
            report.TodoDetails.Add($"视觉补全失败: 读取 Client\\Models\\MonsterObject.cs 失败: {ex.Message}");
            RewriteTodoReport(outputRoot, report);
            return;
        }

        Dictionary<MonsterImage, LegacyVisualDefinition> visualMap = parseResult.Visuals;
        if (visualMap.Count == 0)
        {
            report.TodoDetails.Add("视觉补全失败: 未能从 Client\\Models\\MonsterObject.cs 解析旧客户端 MonsterImage 映射。");
            RewriteTodoReport(outputRoot, report);
            return;
        }

        HashSet<LibraryFile> usedLibraries = new();
        int completed = 0;
        List<string> monsterFiles;

        try
        {
            monsterFiles = Directory.EnumerateFiles(monsterDirectory, "*.txt")
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (IsFileAccessException(ex))
        {
            report.Failures.Add($"视觉补全失败: 枚举怪物文本目录 `{monsterDirectory}` 失败: {ex.Message}");
            RewriteTodoReport(outputRoot, report);
            return;
        }

        foreach (string file in monsterFiles)
        {
            string text;
            try
            {
                text = File.ReadAllText(file, Encoding.UTF8);
            }
            catch (Exception ex) when (IsFileAccessException(ex))
            {
                report.Failures.Add($"视觉补全跳过 `{Path.GetFileName(file)}`: 读取怪物文本失败: {ex.Message}");
                continue;
            }

            if (ContainsSection(text, "Visual"))
                continue;

            string imageName = ReadKeyValue(text, "Image");
            if (Enum.TryParse(imageName, true, out MonsterImage image) && image == MonsterImage.None)
                continue;

            if (!Enum.TryParse(imageName, true, out image) ||
                !visualMap.TryGetValue(image, out LegacyVisualDefinition? visual) ||
                visual.LibraryFile == LibraryFile.None)
            {
                string monsterName = ReadKeyValue(text, "Name").Trim('"');
                if (Enum.TryParse(imageName, true, out image) && parseResult.Skipped.TryGetValue(image, out string? reason))
                    report.TodoDetails.Add($"怪物 `{monsterName}` -> Monsters\\{Path.GetFileName(file)}: MonsterImage={imageName} {reason}，无法自动生成单一 [Visual]/[Animation]，需手工迁移。");
                else
                    report.TodoDetails.Add($"怪物 `{monsterName}` -> Monsters\\{Path.GetFileName(file)}: 无法从旧客户端源码解析 MonsterImage={imageName} 的图库映射，仍需手工补 [Visual]/[Animation]。");
                continue;
            }

            string visualText = BuildVisualText(visual);
            text = RemoveVisualTodoLine(text);
            text = InsertBeforeSection(text, "Behavior", visualText);
            try
            {
                WriteAllTextAtomically(file, text);
            }
            catch (Exception ex) when (IsFileAccessException(ex))
            {
                report.Failures.Add($"视觉补全跳过 `{Path.GetFileName(file)}`: 写入怪物文本失败: {ex.Message}");
                continue;
            }

            usedLibraries.Add(visual.LibraryFile);
            completed++;
        }

        int resourcesAdded = MergeResources(Path.Combine(outputRoot, "Resources.txt"), usedLibraries, report);
        report.ResourcesExported += resourcesAdded;
        report.TodoDetails.RemoveAll(x => x.Contains("没有 CustomMonsterVisualInfo", StringComparison.OrdinalIgnoreCase));
        report.TodoDetails.Add($"视觉补全完成: 已从旧客户端 MonsterImage 映射生成 {completed} 个 [Visual]/[Animation] 段和 {usedLibraries.Count} 个旧图库资源引用；按 default 分支补齐 {parseResult.DefaultMappedImages.Count} 个无显式 case 的 MonsterImage；跳过 {parseResult.Skipped.Count} 个动态或无法唯一确定的外观分支。");

        if (parseResult.DefaultMappedImages.Count > 0)
            report.TodoDetails.Add($"视觉补全影响范围: default 分支覆盖 {FormatImageList(parseResult.DefaultMappedImages)}。");

        if (parseResult.Skipped.Count > 0)
            report.TodoDetails.Add($"视觉补全剩余手工项: {FormatSkippedImages(parseResult.Skipped)}。");

        RewriteTodoReport(outputRoot, report);
    }

    private static LegacyVisualParseResult ParseVisualMap(string sourcePath)
    {
        LegacyVisualParseResult result = new();
        if (!File.Exists(sourcePath))
            return result;

        List<MonsterImage> currentCases = [];
        LegacyVisualDefinition current = new();
        LegacyVisualDefinition? defaultVisual = null;
        HashSet<LibraryFile> assignedLibraries = [];
        HashSet<string> bodyShapeAssignments = new(StringComparer.Ordinal);
        HashSet<string> bodyOffsetAssignments = new(StringComparer.Ordinal);
        bool insideImageSwitch = false;
        bool switchBlockStarted = false;
        int switchBraceDepth = 0;
        bool currentIsDefault = false;

        foreach (string rawLine in File.ReadLines(sourcePath))
        {
            string line = StripLineComment(rawLine).Trim();
            if (line.Length == 0) continue;

            if (!insideImageSwitch)
            {
                if (SwitchImageRegex.IsMatch(line))
                {
                    insideImageSwitch = true;
                    switchBraceDepth += CountBraces(line);
                    switchBlockStarted = switchBraceDepth > 0;
                }

                continue;
            }

            if (!switchBlockStarted)
            {
                switchBraceDepth += CountBraces(line);
                switchBlockStarted = switchBraceDepth > 0;
                continue;
            }

            switchBraceDepth += CountBraces(line);
            if (switchBraceDepth <= 0)
                break;

            Match caseMatch = CaseRegex.Match(line);
            if (caseMatch.Success)
            {
                if (Enum.TryParse(caseMatch.Groups["name"].Value, out MonsterImage image))
                    currentCases.Add(image);
                continue;
            }

            Match defaultMatch = DefaultRegex.Match(line);
            if (defaultMatch.Success)
            {
                currentIsDefault = true;
                continue;
            }

            if (currentCases.Count == 0)
            {
                if (!currentIsDefault)
                    continue;
            }

            Match bodyShapeAssignmentMatch = BodyShapeAssignmentRegex.Match(line);
            if (bodyShapeAssignmentMatch.Success)
                bodyShapeAssignments.Add(bodyShapeAssignmentMatch.Groups["value"].Value.Trim());

            Match bodyOffsetAssignmentMatch = BodyOffsetAssignmentRegex.Match(line);
            if (bodyOffsetAssignmentMatch.Success)
                bodyOffsetAssignments.Add(bodyOffsetAssignmentMatch.Groups["value"].Value.Trim());

            foreach (Match libraryMatch in LibraryRegex.Matches(line))
            {
                if (!Enum.TryParse(libraryMatch.Groups["name"].Value, out LibraryFile libraryFile))
                    continue;

                assignedLibraries.Add(libraryFile);
                current.LibraryFile = libraryFile;
            }

            Match shapeMatch = BodyShapeRegex.Match(line);
            if (shapeMatch.Success && int.TryParse(shapeMatch.Groups["value"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int bodyShape))
                current.BodyShape = bodyShape;

            Match offsetMatch = BodyOffsetRegex.Match(line);
            if (offsetMatch.Success && int.TryParse(offsetMatch.Groups["value"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int bodyOffset))
                current.BodyOffset = bodyOffset;

            Match soundMatch = SoundRegex.Match(line);
            if (soundMatch.Success)
                current.SetSound(soundMatch.Groups["field"].Value, soundMatch.Groups["value"].Value);

            foreach (Match frameSetMatch in FrameSetRegex.Matches(line))
                current.FrameSets.Add(frameSetMatch.Groups["name"].Value);

            if (!line.Equals("break;", StringComparison.Ordinal))
                continue;

            if (TryGetSkipReason(assignedLibraries, bodyShapeAssignments, bodyOffsetAssignments, out string? reason))
            {
                foreach (MonsterImage image in currentCases)
                    result.Skipped[image] = reason ?? "依赖运行时状态";
            }
            else if (currentIsDefault)
            {
                defaultVisual = NormalizeVisual(current);
            }
            else
            {
                foreach (MonsterImage image in currentCases)
                    result.Visuals[image] = NormalizeVisual(current);
            }

            currentCases.Clear();
            current = new LegacyVisualDefinition();
            assignedLibraries.Clear();
            bodyShapeAssignments.Clear();
            bodyOffsetAssignments.Clear();
            currentIsDefault = false;
        }

        if (defaultVisual != null)
        {
            foreach (KeyValuePair<MonsterImage, string> pair in KnownSkippedImages)
                result.Skipped[pair.Key] = pair.Value;

            foreach (MonsterImage image in Enum.GetValues<MonsterImage>().Where(x => x != MonsterImage.None).OrderBy(x => x.ToString()))
            {
                if (result.Visuals.ContainsKey(image) || result.Skipped.ContainsKey(image))
                    continue;

                result.Visuals[image] = defaultVisual.Clone();
                result.DefaultMappedImages.Add(image);
            }
        }

        return result;
    }

    private static int CountBraces(string line)
    {
        int depth = 0;

        foreach (char ch in line)
        {
            if (ch == '{')
                depth++;
            else if (ch == '}')
                depth--;
        }

        return depth;
    }

    private static LegacyVisualDefinition NormalizeVisual(LegacyVisualDefinition visual)
    {
        LegacyVisualDefinition copy = visual.Clone();
        if (copy.BodyOffset <= 0)
            copy.BodyOffset = 1000;

        return copy;
    }

    private static bool TryGetSkipReason(HashSet<LibraryFile> assignedLibraries, HashSet<string> bodyShapeAssignments, HashSet<string> bodyOffsetAssignments, out string? reason)
    {
        if (assignedLibraries.Count > 1)
        {
            reason = $"依赖运行时条件切换图库 ({string.Join("/", assignedLibraries.OrderBy(x => x.ToString()))})";
            return true;
        }

        List<string> nonNumericBodyShapes = bodyShapeAssignments
            .Where(x => !int.TryParse(x, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            .ToList();

        if (nonNumericBodyShapes.Count > 0)
        {
            reason = $"依赖运行时 BodyShape={string.Join("/", nonNumericBodyShapes)}";
            return true;
        }

        List<string> nonNumericBodyOffsets = bodyOffsetAssignments
            .Where(x => !int.TryParse(x, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            .ToList();

        if (nonNumericBodyOffsets.Count > 0)
        {
            reason = $"依赖运行时 BodyOffSet={string.Join("/", nonNumericBodyOffsets)}";
            return true;
        }

        reason = null;
        return false;
    }

    private static string BuildVisualText(LegacyVisualDefinition visual)
    {
        string resourceKey = GetResourceKey(visual.LibraryFile);
        Dictionary<MirAnimation, Frame> frames = BuildFrames(visual.FrameSets);

        StringBuilder sb = new();
        sb.AppendLine();
        sb.AppendLine("[Visual]");
        sb.AppendLine($"Resource={resourceKey}");
        sb.AppendLine("Scale=1");
        sb.AppendLine("DrawShadow=True");
        sb.AppendLine("KeepCorpse=True");
        sb.AppendLine($"DefaultFrameDelay={GetDefaultFrameDelay(frames)}");
        sb.AppendLine($"FrameBase={Math.Max(0, visual.BodyShape) * Math.Max(1, visual.BodyOffset)}");
        AppendSound(sb, "AttackSound", visual.AttackSound);
        AppendSound(sb, "RangeAttackSound", visual.RangeAttackSound);
        AppendSound(sb, "MagicSound", visual.MagicSound);
        AppendSound(sb, "StruckSound", visual.StruckSound);
        AppendSound(sb, "DieSound", visual.DieSound);
        AppendSound(sb, "ShowSound", visual.ShowSound);
        AppendSound(sb, "HideSound", visual.HideSound);

        foreach ((CustomMonsterAnimationType action, int variant, Frame frame) in BuildAnimationRows(frames))
        {
            int directionCount = frame.OffSet > 0 ? 8 : 1;
            int end = frame.StartIndex + Math.Max(0, directionCount - 1) * Math.Max(0, frame.OffSet) + Math.Max(1, frame.FrameCount) - 1;

            sb.AppendLine();
            sb.AppendLine("[Animation]");
            sb.AppendLine($"Action={action}");
            sb.AppendLine($"Resource={resourceKey}");
            sb.AppendLine($"Variant={variant}");
            sb.AppendLine($"Start={frame.StartIndex}");
            sb.AppendLine($"End={end}");
            sb.AppendLine($"FrameCount={Math.Max(1, frame.FrameCount)}");
            sb.AppendLine($"DirectionCount={directionCount}");
            sb.AppendLine($"DirectionOffset={Math.Max(0, frame.OffSet)}");
            sb.AppendLine($"FrameDelay={GetFrameDelay(frame)}");
            sb.AppendLine("Loop=False");
            sb.AppendLine($"Reversed={frame.Reversed}");
            sb.AppendLine("OffsetX=0");
            sb.AppendLine("OffsetY=0");
            sb.AppendLine("Priority=0");
        }

        return sb.ToString().TrimEnd();
    }

    private static Dictionary<MirAnimation, Frame> BuildFrames(List<string> frameSetNames)
    {
        Dictionary<MirAnimation, Frame> frames = FrameSet.DefaultMonster
            .Where(x => IsExportableAnimation(x.Key))
            .ToDictionary(x => x.Key, x => new Frame(x.Value));

        foreach (string frameSetName in frameSetNames)
        {
            FieldInfo? field = typeof(FrameSet).GetField(frameSetName, BindingFlags.Public | BindingFlags.Static);
            if (field?.GetValue(null) is not Dictionary<MirAnimation, Frame> overlay)
                continue;

            foreach (KeyValuePair<MirAnimation, Frame> pair in overlay)
            {
                if (!IsExportableAnimation(pair.Key))
                    continue;

                frames[pair.Key] = new Frame(pair.Value);
            }
        }

        return frames;
    }

    private static IEnumerable<(CustomMonsterAnimationType Action, int Variant, Frame Frame)> BuildAnimationRows(Dictionary<MirAnimation, Frame> frames)
    {
        foreach (KeyValuePair<MirAnimation, Frame> pair in frames.OrderBy(x => GetAnimationSort(x.Key)))
        {
            if (!TryMapAnimation(pair.Key, out CustomMonsterAnimationType action, out int variant))
                continue;

            yield return (action, variant, pair.Value);
        }
    }

    private static bool TryMapAnimation(MirAnimation animation, out CustomMonsterAnimationType action, out int variant)
    {
        variant = 0;
        action = animation switch
        {
            MirAnimation.Standing => CustomMonsterAnimationType.Standing,
            MirAnimation.Walking => CustomMonsterAnimationType.Walking,
            MirAnimation.Combat1 => CustomMonsterAnimationType.Attack,
            MirAnimation.Combat2 => CustomMonsterAnimationType.RangeAttack,
            MirAnimation.Combat3 => CustomMonsterAnimationType.Magic,
            MirAnimation.Combat4 => CustomMonsterAnimationType.Idle4,
            MirAnimation.Combat5 => CustomMonsterAnimationType.Idle5,
            MirAnimation.Combat6 => CustomMonsterAnimationType.Idle6,
            MirAnimation.Combat7 => CustomMonsterAnimationType.Idle7,
            MirAnimation.Combat8 => CustomMonsterAnimationType.Idle8,
            MirAnimation.Struck => CustomMonsterAnimationType.Struck,
            MirAnimation.Die => CustomMonsterAnimationType.Die,
            MirAnimation.Dead => CustomMonsterAnimationType.Dead,
            MirAnimation.Show => CustomMonsterAnimationType.Show,
            MirAnimation.Hide => CustomMonsterAnimationType.Hide,
            _ => CustomMonsterAnimationType.Standing,
        };

        return IsExportableAnimation(animation);
    }

    private static bool IsExportableAnimation(MirAnimation animation)
    {
        return animation is MirAnimation.Standing
            or MirAnimation.Walking
            or MirAnimation.Combat1
            or MirAnimation.Combat2
            or MirAnimation.Combat3
            or MirAnimation.Combat4
            or MirAnimation.Combat5
            or MirAnimation.Combat6
            or MirAnimation.Combat7
            or MirAnimation.Combat8
            or MirAnimation.Struck
            or MirAnimation.Die
            or MirAnimation.Dead
            or MirAnimation.Show
            or MirAnimation.Hide;
    }

    private static int GetAnimationSort(MirAnimation animation)
    {
        return animation switch
        {
            MirAnimation.Standing => 0,
            MirAnimation.Walking => 10,
            MirAnimation.Combat1 => 20,
            MirAnimation.Combat2 => 21,
            MirAnimation.Combat3 => 22,
            MirAnimation.Combat4 => 23,
            MirAnimation.Combat5 => 24,
            MirAnimation.Combat6 => 25,
            MirAnimation.Combat7 => 26,
            MirAnimation.Combat8 => 27,
            MirAnimation.Struck => 30,
            MirAnimation.Die => 40,
            MirAnimation.Dead => 41,
            MirAnimation.Show => 50,
            MirAnimation.Hide => 51,
            _ => 100,
        };
    }

    private static int MergeResources(string path, HashSet<LibraryFile> usedLibraries, CustomSystemTextReport report)
    {
        if (usedLibraries.Count == 0)
            return 0;

        SortedDictionary<string, string> lines = new(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(path))
        {
            string[] resourceLines;
            try
            {
                resourceLines = File.ReadAllLines(path, Encoding.UTF8);
            }
            catch (Exception ex) when (IsFileAccessException(ex))
            {
                report.Failures.Add($"视觉补全无法合并 Resources.txt: 读取失败: {ex.Message}");
                return 0;
            }

            foreach (string line in resourceLines.Where(x => !string.IsNullOrWhiteSpace(x)))
                lines[GetResourceLineKey(line)] = line;
        }

        int existingResources = lines.Count;
        foreach (LibraryFile library in usedLibraries.OrderBy(x => x.ToString()))
        {
            string key = GetResourceKey(library);
            lines[key] = $"{key} LibraryFile={library} DisplayName=\"旧客户端 {library} 图库\" Scope=Monster Enabled=True";
        }

        try
        {
            WriteAllLinesAtomically(path, lines.Values);
        }
        catch (Exception ex) when (IsFileAccessException(ex))
        {
            report.Failures.Add($"视觉补全无法合并 Resources.txt: 写入失败: {ex.Message}");
            return 0;
        }

        return Math.Max(0, lines.Count - existingResources);
    }

    private static void RewriteTodoReport(string outputRoot, CustomSystemTextReport report)
    {
        string path = Path.Combine(outputRoot, "TODO报告.txt");
        StringBuilder sb = new();
        sb.AppendLine("自定义怪物与技能全量导出 TODO 报告");
        sb.AppendLine();
        sb.AppendLine($"待处理项: {report.PendingTodoCount}");
        sb.AppendLine($"错误: {report.Failures.Count}");
        sb.AppendLine();

        if (report.Failures.Count > 0)
        {
            sb.AppendLine("错误明细:");
            foreach (string failure in report.Failures)
                sb.AppendLine("- " + failure);
            sb.AppendLine();
        }

        foreach (string todo in report.TodoDetails)
            sb.AppendLine("- " + todo);

        try
        {
            WriteAllTextAtomically(path, sb.ToString());
        }
        catch (Exception ex) when (IsFileAccessException(ex))
        {
            report.Failures.Add($"写入 TODO报告.txt 失败: {ex.Message}");
        }
    }

    private static void WriteAllTextAtomically(string path, string text)
    {
        string tempPath = GetTempSiblingPath(path, out string fullPath);

        try
        {
            File.WriteAllText(tempPath, text, Utf8NoBom);
            ReplaceOrMoveTempFile(tempPath, fullPath);
        }
        catch
        {
            TryDeleteTempFile(tempPath);
            throw;
        }
    }

    private static void WriteAllLinesAtomically(string path, IEnumerable<string> lines)
    {
        string tempPath = GetTempSiblingPath(path, out string fullPath);

        try
        {
            File.WriteAllLines(tempPath, lines, Utf8NoBom);
            ReplaceOrMoveTempFile(tempPath, fullPath);
        }
        catch
        {
            TryDeleteTempFile(tempPath);
            throw;
        }
    }

    private static void ReplaceOrMoveTempFile(string tempPath, string destinationPath)
    {
        if (File.Exists(destinationPath))
            File.Replace(tempPath, destinationPath, null);
        else
            File.Move(tempPath, destinationPath);
    }

    private static string GetTempSiblingPath(string path, out string fullPath)
    {
        fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        string tempDirectory = string.IsNullOrEmpty(directory) ? "." : directory;
        return Path.Combine(tempDirectory, Path.GetFileName(fullPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
    }

    private static void TryDeleteTempFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
        }
    }

    private static string InsertBeforeSection(string text, string section, string insert)
    {
        Match match = Regex.Match(text, $@"(?m)^\[{Regex.Escape(section)}\]\s*$");
        if (!match.Success)
            return text.TrimEnd() + Environment.NewLine + insert + Environment.NewLine;

        return text[..match.Index].TrimEnd() + Environment.NewLine + insert + Environment.NewLine + Environment.NewLine + text[match.Index..].TrimStart();
    }

    private static bool ContainsSection(string text, string section)
    {
        return Regex.IsMatch(text, $@"(?m)^\[{Regex.Escape(section)}\]\s*$");
    }

    private static bool IsFileAccessException(Exception ex)
    {
        return ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException;
    }

    private static string RemoveVisualTodoLine(string text)
    {
        return string.Join(Environment.NewLine, text
            .Split(["\r\n", "\n"], StringSplitOptions.None)
            .Where(x => !x.Contains("TODO [Visual]", StringComparison.OrdinalIgnoreCase)));
    }

    private static string ReadKeyValue(string text, string key)
    {
        Match match = Regex.Match(text, $@"(?m)^{Regex.Escape(key)}=(?<value>.+?)\s*$");
        return match.Success ? match.Groups["value"].Value.Trim() : string.Empty;
    }

    private static string FormatImageList(IEnumerable<MonsterImage> images)
    {
        return string.Join(", ", images.OrderBy(x => x.ToString()).Select(x => x.ToString()));
    }

    private static string FormatSkippedImages(Dictionary<MonsterImage, string> skipped)
    {
        return string.Join("; ", skipped
            .OrderBy(x => x.Key.ToString())
            .Select(x => $"{x.Key}: {x.Value}"));
    }

    private static string GetResourceLineKey(string line)
    {
        Match match = Regex.Match(line ?? string.Empty, @"^\s*(\S+)");
        return match.Success ? match.Groups[1].Value : line ?? string.Empty;
    }

    private static string StripLineComment(string line)
    {
        int index = line.IndexOf("//", StringComparison.Ordinal);
        return index >= 0 ? line[..index] : line;
    }

    private static string GetResourceKey(LibraryFile libraryFile)
    {
        return "Legacy_" + libraryFile;
    }

    private static int GetFrameDelay(Frame frame)
    {
        if (frame.Delays == null || frame.Delays.Length == 0)
            return 100;

        return Math.Max(1, (int)Math.Round(frame.Delays[0].TotalMilliseconds));
    }

    private static int GetDefaultFrameDelay(Dictionary<MirAnimation, Frame> frames)
    {
        return frames.TryGetValue(MirAnimation.Standing, out Frame? frame) ? GetFrameDelay(frame) : 100;
    }

    private static void AppendSound(StringBuilder sb, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            sb.AppendLine($"{key}={value}");
    }

    private sealed class LegacyVisualDefinition
    {
        public LibraryFile LibraryFile { get; set; }
        public int BodyShape { get; set; }
        public int BodyOffset { get; set; } = 1000;
        public List<string> FrameSets { get; } = [];
        public string? AttackSound { get; set; }
        public string? RangeAttackSound { get; set; }
        public string? MagicSound { get; set; }
        public string? StruckSound { get; set; }
        public string? DieSound { get; set; }
        public string? ShowSound { get; set; }
        public string? HideSound { get; set; }

        public void SetSound(string field, string value)
        {
            switch (field)
            {
                case "AttackSound":
                    AttackSound = value;
                    break;
                case "RangeAttackSound":
                    RangeAttackSound = value;
                    break;
                case "MagicSound":
                    MagicSound = value;
                    break;
                case "StruckSound":
                    StruckSound = value;
                    break;
                case "DieSound":
                    DieSound = value;
                    break;
                case "ShowSound":
                    ShowSound = value;
                    break;
                case "HideSound":
                    HideSound = value;
                    break;
            }
        }

        public LegacyVisualDefinition Clone()
        {
            LegacyVisualDefinition clone = new()
            {
                LibraryFile = LibraryFile,
                BodyShape = BodyShape,
                BodyOffset = BodyOffset,
                AttackSound = AttackSound,
                RangeAttackSound = RangeAttackSound,
                MagicSound = MagicSound,
                StruckSound = StruckSound,
                DieSound = DieSound,
                ShowSound = ShowSound,
                HideSound = HideSound,
            };

            clone.FrameSets.AddRange(FrameSets);
            return clone;
        }
    }

    private sealed class LegacyVisualParseResult
    {
        public Dictionary<MonsterImage, LegacyVisualDefinition> Visuals { get; } = new();
        public Dictionary<MonsterImage, string> Skipped { get; } = new();
        public List<MonsterImage> DefaultMappedImages { get; } = [];
    }
}
