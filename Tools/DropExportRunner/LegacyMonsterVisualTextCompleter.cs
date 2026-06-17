using Library;
using Library.SystemModels;
using Server.Envir;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

internal static class LegacyMonsterVisualTextCompleter
{
    private static readonly Regex CaseRegex = new(@"case\s+MonsterImage\.(?<name>[A-Za-z0-9_]+)\s*:", RegexOptions.Compiled);
    private static readonly Regex LibraryRegex = new(@"LibraryFile\.(?<name>[A-Za-z0-9_]+)", RegexOptions.Compiled);
    private static readonly Regex BodyShapeRegex = new(@"BodyShape\s*=\s*(?<value>-?\d+)", RegexOptions.Compiled);
    private static readonly Regex BodyOffsetRegex = new(@"BodyOffSet\s*=\s*(?<value>-?\d+)", RegexOptions.Compiled);
    private static readonly Regex SoundRegex = new(@"(?<field>AttackSound|RangeAttackSound|MagicSound|StruckSound|DieSound|ShowSound|HideSound)\s*=\s*SoundIndex\.(?<value>[A-Za-z0-9_]+)", RegexOptions.Compiled);
    private static readonly Regex FrameSetRegex = new(@"FrameSet\.(?<name>[A-Za-z0-9_]+)", RegexOptions.Compiled);

    public static void Complete(string outputRoot, string repositoryRoot, CustomSystemTextReport report)
    {
        if (string.IsNullOrWhiteSpace(outputRoot) || string.IsNullOrWhiteSpace(repositoryRoot))
            return;

        string monsterDirectory = Path.Combine(outputRoot, "Monsters");
        if (!Directory.Exists(monsterDirectory))
            return;

        Dictionary<MonsterImage, LegacyVisualDefinition> visualMap = ParseVisualMap(Path.Combine(repositoryRoot, "Client", "Models", "MonsterObject.cs"));
        if (visualMap.Count == 0)
        {
            report.TodoDetails.Add("视觉补全失败: 未能从 Client\\Models\\MonsterObject.cs 解析旧客户端 MonsterImage 映射。");
            RewriteTodoReport(outputRoot, report);
            return;
        }

        HashSet<LibraryFile> usedLibraries = new();
        int completed = 0;

        foreach (string file in Directory.EnumerateFiles(monsterDirectory, "*.txt"))
        {
            string text = File.ReadAllText(file, Encoding.UTF8);
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
                report.TodoDetails.Add($"怪物 `{monsterName}` -> Monsters\\{Path.GetFileName(file)}: 无法从旧客户端源码解析 MonsterImage={imageName} 的图库映射，仍需手工补 [Visual]/[Animation]。");
                continue;
            }

            usedLibraries.Add(visual.LibraryFile);
            string visualText = BuildVisualText(visual);
            text = RemoveVisualTodoLine(text);
            text = InsertBeforeSection(text, "Behavior", visualText);
            File.WriteAllText(file, text, new UTF8Encoding(false));
            completed++;
        }

        MergeResources(Path.Combine(outputRoot, "Resources.txt"), usedLibraries);
        report.TodoDetails.RemoveAll(x => x.Contains("没有 CustomMonsterVisualInfo", StringComparison.OrdinalIgnoreCase));
        report.TodoDetails.Add($"视觉补全完成: 已从旧客户端 MonsterImage 映射生成 {completed} 个 [Visual]/[Animation] 段和 {usedLibraries.Count} 个旧图库资源引用。");
        RewriteTodoReport(outputRoot, report);
    }

    private static Dictionary<MonsterImage, LegacyVisualDefinition> ParseVisualMap(string sourcePath)
    {
        Dictionary<MonsterImage, LegacyVisualDefinition> result = new();
        if (!File.Exists(sourcePath))
            return result;

        List<MonsterImage> currentCases = [];
        LegacyVisualDefinition current = new();
        bool insideImageSwitch = false;

        foreach (string rawLine in File.ReadLines(sourcePath))
        {
            string line = StripLineComment(rawLine).Trim();
            if (line.Length == 0) continue;

            if (!insideImageSwitch)
            {
                if (line.Equals("switch (Image)", StringComparison.Ordinal))
                    insideImageSwitch = true;
                continue;
            }

            if (line.StartsWith("if (EasterEvent)", StringComparison.Ordinal))
                break;

            Match caseMatch = CaseRegex.Match(line);
            if (caseMatch.Success)
            {
                if (Enum.TryParse(caseMatch.Groups["name"].Value, out MonsterImage image))
                    currentCases.Add(image);
                continue;
            }

            if (currentCases.Count == 0)
                continue;

            Match libraryMatch = LibraryRegex.Match(line);
            if (libraryMatch.Success && Enum.TryParse(libraryMatch.Groups["name"].Value, out LibraryFile libraryFile))
                current.LibraryFile = libraryFile;

            Match shapeMatch = BodyShapeRegex.Match(line);
            if (shapeMatch.Success && int.TryParse(shapeMatch.Groups["value"].Value, out int bodyShape))
                current.BodyShape = bodyShape;

            Match offsetMatch = BodyOffsetRegex.Match(line);
            if (offsetMatch.Success && int.TryParse(offsetMatch.Groups["value"].Value, out int bodyOffset))
                current.BodyOffset = bodyOffset;

            Match soundMatch = SoundRegex.Match(line);
            if (soundMatch.Success)
                current.SetSound(soundMatch.Groups["field"].Value, soundMatch.Groups["value"].Value);

            Match frameSetMatch = FrameSetRegex.Match(line);
            if (frameSetMatch.Success)
                current.FrameSets.Add(frameSetMatch.Groups["name"].Value);

            if (!line.Equals("break;", StringComparison.Ordinal))
                continue;

            foreach (MonsterImage image in currentCases)
            {
                LegacyVisualDefinition copy = current.Clone();
                if (copy.BodyOffset <= 0)
                    copy.BodyOffset = 1000;
                result[image] = copy;
            }

            currentCases.Clear();
            current = new LegacyVisualDefinition();
        }

        return result;
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

    private static void MergeResources(string path, HashSet<LibraryFile> usedLibraries)
    {
        SortedDictionary<string, string> lines = new(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(path))
            foreach (string line in File.ReadAllLines(path, Encoding.UTF8).Where(x => !string.IsNullOrWhiteSpace(x)))
                lines[line.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? line] = line;

        foreach (LibraryFile library in usedLibraries.OrderBy(x => x.ToString()))
        {
            string key = GetResourceKey(library);
            lines[key] = $"{key} LibraryFile={library} DisplayName=\"旧客户端 {library} 图库\" Scope=Monster Enabled=True";
        }

        File.WriteAllLines(path, lines.Values, new UTF8Encoding(false));
    }

    private static void RewriteTodoReport(string outputRoot, CustomSystemTextReport report)
    {
        string path = Path.Combine(outputRoot, "TODO报告.txt");
        StringBuilder sb = new();
        sb.AppendLine("自定义怪物与技能全量导出 TODO 报告");
        sb.AppendLine();
        sb.AppendLine($"待处理项: {report.PendingTodoCount}");
        sb.AppendLine();

        foreach (string todo in report.TodoDetails)
            sb.AppendLine("- " + todo);

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
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
}
