using Library;
using Library.SystemModels;
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
    public enum CustomSystemImportMode
    {
        ValidateOnly,
        ReplaceExisting,
        Merge,
        Export
    }

    public sealed class CustomSystemRegistry
    {
        public static readonly CustomSystemRegistry Empty = new();

        public Dictionary<string, CustomResourceLibraryInfo> ResourcesByKey { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<int, CustomMonsterVisualInfo> MonsterVisualsByMonsterIndex { get; } = [];
        public Dictionary<int, CustomMonsterBehaviorInfo> MonsterBehaviorsByMonsterIndex { get; } = [];
        public Dictionary<int, CustomSkillInfo> SkillsByMagicIndex { get; } = [];

        public bool TryGetVisual(MonsterInfo monster, out CustomMonsterVisualInfo visual)
        {
            visual = null;
            return monster != null && MonsterVisualsByMonsterIndex.TryGetValue(monster.Index, out visual);
        }

        public bool TryGetBehavior(MonsterInfo monster, out CustomMonsterBehaviorInfo behavior)
        {
            behavior = null;
            return monster != null && MonsterBehaviorsByMonsterIndex.TryGetValue(monster.Index, out behavior);
        }

        public bool TryGetSkill(MagicInfo magic, out CustomSkillInfo skill)
        {
            skill = null;
            return magic != null && SkillsByMagicIndex.TryGetValue(magic.Index, out skill);
        }

        public static CustomSystemRegistry Build(Session session, CustomSystemTextLoadResult textResult = null)
        {
            CustomSystemRegistry registry = new();

            foreach (CustomResourceLibraryInfo resource in session.GetCollection<CustomResourceLibraryInfo>().Binding.Where(x => x?.Enabled == true))
                if (!string.IsNullOrWhiteSpace(resource.Key) && !registry.ResourcesByKey.ContainsKey(resource.Key))
                    registry.ResourcesByKey[resource.Key] = resource;

            foreach (CustomMonsterVisualInfo visual in session.GetCollection<CustomMonsterVisualInfo>().Binding.Where(x => x?.Monster != null))
                registry.MonsterVisualsByMonsterIndex[visual.Monster.Index] = visual;

            foreach (CustomMonsterBehaviorInfo behavior in session.GetCollection<CustomMonsterBehaviorInfo>().Binding.Where(x => x?.Monster != null))
                registry.MonsterBehaviorsByMonsterIndex[behavior.Monster.Index] = behavior;

            foreach (CustomSkillInfo skill in session.GetCollection<CustomSkillInfo>().Binding.Where(x => x?.Magic != null && x.Enabled))
                registry.SkillsByMagicIndex[skill.Magic.Index] = skill;

            if (textResult != null)
            {
                foreach (CustomResourceLibraryInfo resource in textResult.Resources)
                    if (!string.IsNullOrWhiteSpace(resource.Key) && resource.Enabled)
                        registry.ResourcesByKey[resource.Key] = resource;

                foreach (CustomMonsterVisualInfo visual in textResult.Visuals)
                    if (visual.Monster != null)
                        registry.MonsterVisualsByMonsterIndex[visual.Monster.Index] = visual;

                foreach (CustomMonsterBehaviorInfo behavior in textResult.Behaviors)
                    if (behavior.Monster != null)
                        registry.MonsterBehaviorsByMonsterIndex[behavior.Monster.Index] = behavior;

                foreach (CustomSkillInfo skill in textResult.Skills)
                    if (skill.Magic != null && skill.Enabled)
                        registry.SkillsByMagicIndex[skill.Magic.Index] = skill;
            }

            return registry;
        }
    }

    public sealed class CustomSystemTextLoader
    {
        private static readonly Regex IncludeRegex = new(@"^#(?:INSERT|INCLUDE)\s+\[(.*?)\]\s*(?:(?:;|//).*)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly HashSet<string> BoolActionOptions = new(StringComparer.OrdinalIgnoreCase)
        {
            "SuppressDrops",
            "AttackWhenBlocked",
            "AttackCurrentCell",
            "AttackBlockedCell",
            "SearchAlternate",
            "TurnWhenBlocked",
            "RequireBlocking",
            "IgnoreAttackCooldown",
            "IgnoreArmour",
            "IgnoreShield",
            "NoDamage",
            "DrainMP",
            "AllDirections",
            "RangePacket",
            "AllowDead",
            "PerTarget",
            "CullMissingMinions",
            "RequireAll",
            "PacketOnly",
            "StateOnly",
            "ForcePacket",
            "ActionTime",
            "MoveTime",
            "CellTime",
            "ExtendOnly",
            "RestoreDirection",
            "LeaveEffect",
            "EnterEffect",
            "Clear",
            "HealthDecay",
            "ReviveHealthDecay",
            "Stackable",
            "AllMap",
            "RequireInjured",
            "Players",
            "IncludePlayers",
            "Monsters",
            "IncludeMonsters",
            "Pets",
            "IncludePets",
            "WildMonsters",
            "IncludeWildMonsters",
            "IgnoreSafeZone",
            "IncludeSelf",
            "Despawn",
            "DecreaseAliveCount",
            "VisibleOriginOnly",
            "BroadcastMagic",
            "SearchAllies",
            "SearchCanHelp",
            "IgnoreMaxMinions",
            "PreserveBoss",
            "PreserveBossMinions",
        };

        private static readonly HashSet<string> KnownActionOptions = new(StringComparer.OrdinalIgnoreCase)
        {
            "ActionChance",
            "ActionTime",
            "AllDirections",
            "AllMap",
            "AllowDead",
            "AttackBlockedCell",
            "AttackCurrentCell",
            "AttackWhenBlocked",
            "Attempts",
            "BuffStatsFromSelf",
            "BuffStatsFromTarget",
            "CellTime",
            "ClassPowerRates",
            "Clear",
            "CollisionAction",
            "CollisionRadius",
            "CullMissingMinions",
            "DecreaseAliveCount",
            "DrainMP",
            "DurationRandom",
            "Effect",
            "EnterEffect",
            "ExtendOnly",
            "Flag",
            "ForcePacket",
            "ForwardChance",
            "HealthDecay",
            "IgnoreAttackCooldown",
            "IgnoreArmour",
            "IgnoreShield",
            "IncludeSelf",
            "Key",
            "LeashRadius",
            "LeaveEffect",
            "LocationKey",
            "MaxTotal",
            "Message",
            "MessageType",
            "MinionLeashRadius",
            "MonsterFlag",
            "MonsterFlags",
            "Monsters",
            "MoveTime",
            "NoDamage",
            "OffsetDirection",
            "OffsetDistance",
            "OnHitAction",
            "OnHitChance",
            "OriginDirection",
            "OriginDistance",
            "PacketOnly",
            "Players",
            "Pets",
            "PerTarget",
            "PreserveBoss",
            "PreserveBossMinions",
            "PushDistance",
            "PushDirection",
            "RangePacket",
            "RecallOutsideRadius",
            "Region",
            "RegionDescription",
            "RegionIndex",
            "RegionName",
            "RequireAll",
            "RequireBlocking",
            "RequireHasBuff",
            "RequireInjured",
            "RequireMissingBuff",
            "RestoreDirection",
            "ReviveHealthDecay",
            "Repeat",
            "SearchAlternate",
            "SearchAllies",
            "SearchCanHelp",
            "Scope",
            "SpecialRegion",
            "SpellEffect",
            "SpawnPool",
            "Stackable",
            "StateOnly",
            "SuppressDrops",
            "TargetHealthPower",
            "TargetHealthRate",
            "TargetHealthSourceRate",
            "Text",
            "TurnWhenBlocked",
            "Format",
            "IncludePlayers",
            "IncludeMonsters",
            "IncludePets",
            "WildMonsters",
            "IncludeWildMonsters",
            "IgnoreSafeZone",
            "Despawn",
            "DecreaseAliveCount",
            "VisibleOriginOnly",
            "BroadcastMagic",
            "IgnoreMaxMinions",
        };

        private static readonly HashSet<string> DynamicDirectionOptions = new(StringComparer.OrdinalIgnoreCase)
        {
            "Target",
            "CurrentTarget",
            "Away",
            "AwayFromTarget",
            "FromTarget",
        };

        private static readonly HashSet<string> BooleanTextKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "Enabled",
            "Undead",
            "CanPush",
            "CanTame",
            "IsBoss",
            "DrawShadow",
            "Blend",
            "KeepCorpse",
            "Loop",
            "Reversed",
            "EffectBlend",
            "EffectLoop",
            "CanBeKilled",
            "CanAttackPlayers",
            "CanBeTamed",
            "IsPickupCompanion",
            "Passive",
            "AlwaysActive",
            "DamageProxy",
            "NeedHarvest",
            "AvoidFireWall",
            "IgnoreShield",
            "IgnoreArmour",
            "PoisonCanKill",
            "PoisonStackable",
            "DisableMovement",
            "DisableAttack",
            "DisableRegen",
            "DisableBlocking",
            "HiddenBlocks",
            "HiddenCanAttack",
            "HiddenCanBeAttacked",
            "UseBaseFallback",
            "InvertCondition",
            "Invert",
            "Travel",
            "BuffVisible",
            "BuffPause",
            "BuffHidden",
            "FollowTarget",
        };

        private static readonly HashSet<string> IntegerTextKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "AI",
            "Index",
            "Level",
            "ViewRange",
            "CoolEye",
            "AttackDelay",
            "MoveDelay",
            "FaceImage",
            "Icon",
            "MinBasePower",
            "MaxBasePower",
            "MinLevelPower",
            "MaxLevelPower",
            "BaseCost",
            "LevelCost",
            "NeedLevel1",
            "NeedLevel2",
            "NeedLevel3",
            "Experience1",
            "Experience2",
            "Experience3",
            "DefaultFrameDelay",
            "FrameBase",
            "BaseIndex",
            "ShapeIndex",
            "BodyShape",
            "ShapeFrameOffset",
            "BodyOffset",
            "Variant",
            "AnimationVariant",
            "Start",
            "StartIndex",
            "End",
            "EndIndex",
            "FrameCount",
            "DirectionCount",
            "DirectionOffset",
            "FrameDelay",
            "OffsetX",
            "OffsetY",
            "Priority",
            "EffectStart",
            "EffectStartIndex",
            "EffectEnd",
            "EffectEndIndex",
            "EffectFrameCount",
            "EffectFrameDelay",
            "EffectOffsetX",
            "EffectOffsetY",
            "EffectStartLight",
            "EffectEndLight",
            "EffectStartDelay",
            "EffectDuration",
            "EffectTriggerFrame",
            "StartLight",
            "EndLight",
            "DamageProxyRevealDelay",
            "DamageProxyRange",
            "HarvestCount",
            "PoisonValue",
            "PoisonRate",
            "PoisonTicks",
            "PoisonFrequency",
            "ProtectionRadius",
            "ProtectionCheckInterval",
            "AttackRange",
            "ChaseRange",
            "ReturnRange",
            "MaxMinions",
            "SkillChance",
            "RoamDelay",
            "Chance",
            "Cooldown",
            "MaxExecutions",
            "Radius",
            "MinHealthPercent",
            "MaxHealthPercent",
            "FixedPower",
            "PowerRate",
            "Distance",
            "MinOffset",
            "MaxOffset",
            "Delay",
            "TargetChance",
            "EmptyLocationChance",
            "MaxTargets",
            "RandomTargetCount",
            "RandomTargets",
            "FixedCount",
            "Count",
            "RandomCount",
            "MinionDuration",
            "SummonDuration",
            "BuffExtra",
            "Duration",
            "TickRate",
            "Range",
            "Length",
            "Width",
            "Angle",
            "WaveCount",
            "Waves",
            "WaveInterval",
            "KnockbackDistance",
            "PushDistance",
            "PullDistance",
            "DurationPowerRate",
            "CastDelay",
            "ProjectileSpeed",
            "MaxSummons",
        };

        private static readonly HashSet<string> DecimalTextKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "Experience",
            "Scale",
        };

        private readonly Session _session;
        private readonly Dictionary<string, MonsterInfo> _monsters;
        private readonly Dictionary<int, MonsterInfo> _monstersByIndex;
        private readonly Dictionary<string, MagicInfo> _magics;
        private readonly Dictionary<int, MagicInfo> _magicsByIndex;
        private readonly Dictionary<string, CustomResourceLibraryInfo> _resources;
        private readonly HashSet<MonsterInfo> _detachedTextMonsters = [];

        public CustomSystemTextLoader(Session session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));

            _monsters = session.GetCollection<MonsterInfo>().Binding
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.MonsterName))
                .GroupBy(x => Normalize(x.MonsterName), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
            _monstersByIndex = session.GetCollection<MonsterInfo>().Binding.Where(x => x != null).ToDictionary(x => x.Index);

            _magics = session.GetCollection<MagicInfo>().Binding
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Name))
                .GroupBy(x => Normalize(x.Name), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
            _magicsByIndex = session.GetCollection<MagicInfo>().Binding.Where(x => x != null).ToDictionary(x => x.Index);

            _resources = session.GetCollection<CustomResourceLibraryInfo>().Binding
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Key))
                .GroupBy(x => Normalize(x.Key), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        }

        public CustomSystemTextLoadResult BuildRuntimeOverrides(string directory, bool strictMode)
        {
            CustomSystemTextLoadResult result = new();
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                result.Report.Failures.Add($"自定义系统目录不存在: {directory}");
                return result;
            }

            Dictionary<string, CustomResourceLibraryInfo> resources = new(_resources, StringComparer.OrdinalIgnoreCase);

            LoadResources(directory, result, resources, createDetached: true);
            IndexMonsterFilePrimaries(directory, result.Report, createDetached: true, allowMissingPrimaryObject: false, includeTemplates: false);
            Dictionary<string, bool> damageProxyMonsters = BuildDamageProxyMonsterLookup(directory, result.Report, includeTemplates: false);
            LoadSkillFiles(directory, result, resources, damageProxyMonsters, createDetached: true, allowMissingPrimaryObject: false, strictMode: strictMode, replaceExisting: false, includeTemplates: false);
            LoadMonsterFiles(directory, result, resources, createDetached: true, allowMissingPrimaryObject: false, strictMode, replaceExisting: false, includeTemplates: false);

            return result;
        }

        internal CustomSystemTextLoadResult BuildForDatabaseImport(string directory, CustomSystemImportMode mode, bool includeTemplatesInValidation = false)
        {
            CustomSystemTextLoadResult result = new();
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                result.Report.Failures.Add($"自定义系统目录不存在: {directory}");
                return result;
            }

            Dictionary<string, CustomResourceLibraryInfo> resources = new(_resources, StringComparer.OrdinalIgnoreCase);

            bool validateOnly = mode == CustomSystemImportMode.ValidateOnly;
            bool includeTemplates = validateOnly && includeTemplatesInValidation;

            LoadResources(directory, result, resources, createDetached: validateOnly);
            IndexMonsterFilePrimaries(directory, result.Report, createDetached: validateOnly, allowMissingPrimaryObject: true, includeTemplates: includeTemplates);
            Dictionary<string, bool> damageProxyMonsters = BuildDamageProxyMonsterLookup(directory, result.Report, includeTemplates: includeTemplates);
            LoadSkillFiles(directory, result, resources, damageProxyMonsters, createDetached: validateOnly, allowMissingPrimaryObject: true, strictMode: true, replaceExisting: mode == CustomSystemImportMode.ReplaceExisting, includeTemplates: includeTemplates);
            LoadMonsterFiles(directory, result, resources, createDetached: validateOnly, allowMissingPrimaryObject: true, strictMode: true, replaceExisting: mode == CustomSystemImportMode.ReplaceExisting, includeTemplates: includeTemplates);

            return result;
        }

        private void LoadResources(string directory, CustomSystemTextLoadResult result, Dictionary<string, CustomResourceLibraryInfo> resources, bool createDetached)
        {
            string path = Path.Combine(directory, "Resources.txt");
            if (!File.Exists(path)) return;

            HashSet<string> fileResourceKeys = new(StringComparer.OrdinalIgnoreCase);

            foreach (CustomTextLine line in ReadCleanLines(path, directory, result.Report, new HashSet<string>(StringComparer.OrdinalIgnoreCase)))
            {
                List<string> tokens = Tokenize(line.Text);
                if (tokens.Count < 1)
                {
                    result.Report.Failures.Add($"{line.Source}: Resources.txt 行至少需要 Key。");
                    continue;
                }

                string key = tokens[0].Trim();
                if (string.IsNullOrWhiteSpace(key))
                {
                    result.Report.Failures.Add($"{line.Source}: Resources.txt Key 不能为空。");
                    continue;
                }

                string normalizedKey = Normalize(key);
                if (!fileResourceKeys.Add(normalizedKey))
                {
                    result.Report.Failures.Add($"{line.Source}: Resources.txt 存在重复资源 Key `{key}`。");
                    continue;
                }

                int optionStart = 1;
                string relativePath = null;

                if (tokens.Count > 1 && !tokens[1].Contains('='))
                {
                    relativePath = tokens[1].Trim();
                    optionStart = 2;
                }

                Dictionary<string, string> options = ParseOptions(tokens.Skip(optionStart));
                relativePath = GetOption(options, "RelativePath", GetOption(options, "Path", relativePath));
                LibraryFile libraryFile = GetLibraryFile(options, line.Source, result.Report);
                ValidateKnownBooleanOptions(options, result.Report, line.Source);

                if (string.IsNullOrWhiteSpace(relativePath) && libraryFile == LibraryFile.None)
                {
                    result.Report.Failures.Add($"{line.Source}: 资源 `{key}` 必须配置 RelativePath 或 LibraryFile。");
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(relativePath) && !ValidateRelativePath(relativePath, line.Source, result.Report))
                    continue;

                if (libraryFile != LibraryFile.None && !Libraries.LibraryList.ContainsKey(libraryFile))
                {
                    result.Report.Failures.Add($"{line.Source}: LibraryFile `{libraryFile}` 不在客户端图库清单中。");
                    continue;
                }

                CustomResourceLibraryInfo resource = createDetached ? new CustomResourceLibraryInfo() : resources.GetValueOrDefault(normalizedKey);
                if (!createDetached)
                    resource ??= _session.GetCollection<CustomResourceLibraryInfo>().CreateNewObject();
                else if (resource == null)
                    continue;

                bool isNewResource = string.IsNullOrWhiteSpace(resource.Key);

                resource.Key = key;
                resource.RelativePath = relativePath;
                resource.LibraryFile = libraryFile;
                resource.DisplayName = GetOption(options, "DisplayName", resource.DisplayName);
                resource.Hash = GetOption(options, "Hash", resource.Hash);
                resource.Version = GetOption(options, "Version", resource.Version);
                resource.Enabled = GetBool(options, "Enabled", isNewResource ? true : resource.Enabled);
                resource.Scope = GetEnum(options, "Scope", isNewResource ? CustomResourceScope.Both : resource.Scope, result.Report, line.Source);

                resources[normalizedKey] = resource;
                result.Resources.Add(resource);
                result.Report.ResourcesLoaded++;
            }
        }

        private void IndexMonsterFilePrimaries(string directory, CustomSystemTextReport report, bool createDetached, bool allowMissingPrimaryObject, bool includeTemplates)
        {
            string monstersDirectory = Path.Combine(directory, "Monsters");
            if (!Directory.Exists(monstersDirectory)) return;

            foreach (string file in EnumerateCustomTextFiles(monstersDirectory, includeTemplates, report))
            {
                CustomTextDocument document = ReadDocument(file, directory, report);
                CustomTextSection monsterSection = document.First("Monster");
                if (monsterSection == null) continue;

                MonsterInfo monster = ResolveMonster(monsterSection.Values, file, report, allowMissingPrimaryObject, createDetached);
                if (monster == null) continue;

                if (createDetached)
                    _detachedTextMonsters.Add(monster);

                IndexMonster(monster);
            }
        }

        private Dictionary<string, bool> BuildDamageProxyMonsterLookup(string directory, CustomSystemTextReport report, bool includeTemplates)
        {
            Dictionary<string, bool> result = new(StringComparer.OrdinalIgnoreCase);

            foreach (CustomMonsterBehaviorInfo behavior in _session.GetCollection<CustomMonsterBehaviorInfo>().Binding.Where(x => x?.Monster != null))
                SetDamageProxyLookup(result, behavior.Monster, behavior.DamageProxy);

            string monstersDirectory = Path.Combine(directory, "Monsters");
            if (!Directory.Exists(monstersDirectory)) return result;

            foreach (string file in EnumerateCustomTextFiles(monstersDirectory, includeTemplates, report))
            {
                int beforeFailures = report.Failures.Count;
                CustomTextDocument document = ReadDocument(file, directory, report);
                if (report.Failures.Count > beforeFailures) continue;

                CustomTextSection monsterSection = document.First("Monster");
                CustomTextSection behaviorSection = document.First("Behavior");
                if (monsterSection == null || behaviorSection == null) continue;

                MonsterInfo monster = ResolveIndexedMonster(monsterSection.Values, file);
                if (monster == null) continue;

                SetDamageProxyLookup(result, monster, GetBool(behaviorSection.Values, "DamageProxy", false));
            }

            return result;
        }

        private MonsterInfo ResolveIndexedMonster(Dictionary<string, string> values, string file)
        {
            if (values.TryGetValue("Index", out string indexValue) &&
                int.TryParse(indexValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) &&
                _monstersByIndex.TryGetValue(index, out MonsterInfo indexed))
                return indexed;

            string name = GetValue(values, "Name", Path.GetFileNameWithoutExtension(file));
            return !string.IsNullOrWhiteSpace(name) && _monsters.TryGetValue(Normalize(name), out MonsterInfo named)
                ? named
                : null;
        }

        private static void SetDamageProxyLookup(Dictionary<string, bool> lookup, MonsterInfo monster, bool enabled)
        {
            if (lookup == null || monster == null) return;

            if (monster.Index > 0)
                lookup[$"index:{monster.Index}"] = enabled;

            if (!string.IsNullOrWhiteSpace(monster.MonsterName))
                lookup[$"name:{Normalize(monster.MonsterName)}"] = enabled;
        }

        private void LoadMonsterFiles(
            string directory,
            CustomSystemTextLoadResult result,
            Dictionary<string, CustomResourceLibraryInfo> resources,
            bool createDetached,
            bool allowMissingPrimaryObject,
            bool strictMode,
            bool replaceExisting,
            bool includeTemplates)
        {
            string monstersDirectory = Path.Combine(directory, "Monsters");
            if (!Directory.Exists(monstersDirectory)) return;

            Dictionary<string, CustomSkillInfo> customSkillsByMagic = BuildCustomSkillLookup(result);

            foreach (string file in EnumerateCustomTextFiles(monstersDirectory, includeTemplates, result.Report))
            {
                int beforeFailures = result.Report.Failures.Count;
                CustomTextDocument document = ReadDocument(file, directory, result.Report);
                if (document.Sections.Count == 0) continue;

                ValidateKnownBooleanFields(document, result.Report);
                ValidateKnownNumericFields(document, result.Report);

                CustomTextSection monsterSection = document.First("Monster");
                if (monsterSection == null)
                {
                    result.Report.Failures.Add($"{Path.GetFileName(file)}: 缺少 [Monster] 段。");
                    continue;
                }

                ValidateUniqueMonsterSections(document, result.Report, Path.GetFileName(file));

                MonsterInfo monster = ResolveMonster(monsterSection.Values, file, result.Report, allowMissingPrimaryObject, createDetached);
                if (monster == null) continue;
                IndexMonster(monster);

                List<CustomTextSection> statSections = document.Sections
                    .Where(x => x.Name.Equals("Stats", StringComparison.OrdinalIgnoreCase) || x.Name.Equals("Stat", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (statSections.Count > 0 && allowMissingPrimaryObject)
                {
                    if (replaceExisting && !createDetached)
                        ClearMonsterStats(monster);

                    result.Report.MonsterStatsLoaded += ApplyMonsterStats(monster, statSections, createDetached, result.Report);
                    monster.StatsChanged();
                }

                List<CustomTextSection> dropSections = document.Sections
                    .Where(IsDropSection)
                    .ToList();

                if (dropSections.Count > 0)
                    ApplyMonsterDrops(monster, dropSections, createDetached, replaceExisting, result);

                CustomTextSection visualSection = document.First("Visual");
                if (visualSection != null)
                {
                    CustomMonsterVisualInfo visual = createDetached ? CreateDetachedCustomMonsterVisual() : null;
                    if (!createDetached)
                    {
                        visual ??= _session.GetCollection<CustomMonsterVisualInfo>().Binding.FirstOrDefault(x => x.Monster == monster);
                        visual ??= _session.GetCollection<CustomMonsterVisualInfo>().CreateNewObject();
                    }
                    if (visual == null) continue;
                    EnsureVisualChildren(visual);
                    if (replaceExisting)
                        ClearVisualChildren(visual);
                    else if (!createDetached)
                        DeduplicateVisualChildren(visual);

                    ApplyVisual(visual, monster, visualSection.Values, resources, result.Report, visualSection.Source);

                    foreach (CustomTextSection section in document.Sections.Where(x => x.Name.Equals("Animation", StringComparison.OrdinalIgnoreCase)))
                    {
                        CustomMonsterAnimationInfo animation = ResolveAnimation(visual, section.Values, createDetached, replaceExisting);
                        animation.Visual = visual;
                        ApplyAnimation(animation, section.Values, resources, result.Report, section.Source);
                        visual.Animations?.Add(animation);
                    }

                    result.Visuals.Add(visual);
                    result.Report.MonsterVisualsLoaded++;
                }

                CustomTextSection behaviorSection = document.First("Behavior");
                if (behaviorSection != null)
                {
                    CustomMonsterBehaviorInfo behavior = createDetached ? CreateDetachedCustomMonsterBehavior() : null;
                    if (!createDetached)
                    {
                        behavior ??= _session.GetCollection<CustomMonsterBehaviorInfo>().Binding.FirstOrDefault(x => x.Monster == monster);
                        behavior ??= _session.GetCollection<CustomMonsterBehaviorInfo>().CreateNewObject();
                    }
                    if (behavior == null) continue;
                    EnsureBehaviorChildren(behavior);
                    if (replaceExisting)
                        ClearBehaviorChildren(behavior);
                    else if (!createDetached)
                        DeduplicateBehaviorChildren(behavior);

                    ApplyBehavior(behavior, monster, behaviorSection.Values, result.Report, behaviorSection.Source);

                    foreach (CustomTextSection section in document.Sections.Where(x => x.Name.Equals("Skill", StringComparison.OrdinalIgnoreCase)))
                    {
                        CustomMonsterSkillInfo skill = ResolveMonsterSkill(behavior, section.Values, createDetached, replaceExisting);
                        skill.Behavior = behavior;
                        ApplyMonsterSkill(skill, section.Values, result.Report, section.Source);
                        behavior.Skills?.Add(skill);
                    }

                    foreach (CustomTextSection section in document.Sections.Where(x => x.Name.Equals("AIRule", StringComparison.OrdinalIgnoreCase) || x.Name.Equals("Rule", StringComparison.OrdinalIgnoreCase)))
                    {
                        CustomMonsterAiRuleInfo rule = ResolveAiRule(behavior, section.Values, createDetached, replaceExisting);
                        rule.Behavior = behavior;
                        ApplyAiRule(rule, section.Values, result.Report, section.Source);
                        behavior.Rules?.Add(rule);
                    }

                    foreach (CustomTextSection section in document.Sections.Where(x => x.Name.Equals("AIAction", StringComparison.OrdinalIgnoreCase) || x.Name.Equals("Action", StringComparison.OrdinalIgnoreCase)))
                    {
                        CustomMonsterAiActionInfo aiAction = ResolveAiAction(behavior, section.Values, createDetached, replaceExisting);
                        aiAction.Behavior = behavior;
                        ApplyAiAction(aiAction, section.Values, result.Report, section.Source);
                        behavior.Actions?.Add(aiAction);
                    }

                    ValidateAiReferences(behavior, result.Report, Path.GetFileName(file), customSkillsByMagic);

                    result.Behaviors.Add(behavior);
                    result.Report.MonsterBehaviorsLoaded++;
                }

                if (strictMode && result.Report.Failures.Count > beforeFailures)
                {
                    result.Visuals.RemoveAll(x => x.Monster == monster);
                    result.Behaviors.RemoveAll(x => x.Monster == monster);
                    result.DropOverrides.Remove(monster);
                    result.Report.MonstersSkipped++;
                }
                else
                {
                    result.Report.MonsterFilesLoaded++;
                    if (IsTemplateTextFile(monstersDirectory, file))
                        result.Report.TemplateMonsterFilesLoaded++;
                }
            }
        }

        private Dictionary<string, CustomSkillInfo> BuildCustomSkillLookup(CustomSystemTextLoadResult result)
        {
            Dictionary<string, CustomSkillInfo> skills = new(StringComparer.OrdinalIgnoreCase);

            foreach (CustomSkillInfo skill in _session.GetCollection<CustomSkillInfo>().Binding.Where(x => x?.Magic != null && x.Enabled))
                AddCustomSkillLookup(skills, skill);

            foreach (CustomSkillInfo skill in result.Skills.Where(x => x?.Magic != null && x.Enabled))
                AddCustomSkillLookup(skills, skill);

            return skills;
        }

        private static void AddCustomSkillLookup(Dictionary<string, CustomSkillInfo> skills, CustomSkillInfo skill)
        {
            if (skill?.Magic == null) return;

            if (skill.Magic.Index > 0)
                skills[$"index:{skill.Magic.Index}"] = skill;

            if (!string.IsNullOrWhiteSpace(skill.Magic.Name))
                skills[$"name:{Normalize(skill.Magic.Name)}"] = skill;
        }

        private static void ValidateUniqueMonsterSections(CustomTextDocument document, CustomSystemTextReport report, string fileName)
        {
            ValidateUniqueSections(
                document.Sections.Where(x => x.Name.Equals("Animation", StringComparison.OrdinalIgnoreCase)),
                section =>
                {
                    CustomMonsterAnimationType action = GetEnum(section.Values, "Action", CustomMonsterAnimationType.Standing);
                    int variant = GetInt(section.Values, "Variant", 0);
                    return $"{action}|{variant}";
                },
                "Animation",
                "Action+Variant",
                report,
                fileName);

            ValidateUniqueSections(
                document.Sections.Where(x => x.Name.Equals("Skill", StringComparison.OrdinalIgnoreCase)),
                section =>
                {
                    string magic = Normalize(GetValue(section.Values, "Magic", null));
                    if (string.IsNullOrWhiteSpace(magic)) return null;

                    CustomAiTargetSelectorType target = GetEnum(section.Values, "Target", CustomAiTargetSelectorType.CurrentTarget);
                    int animationVariant = GetInt(section.Values, "AnimationVariant", GetInt(section.Values, "Variant", 0));
                    return $"{magic}|{target}|{animationVariant}";
                },
                "Skill",
                "Magic+Target+AnimationVariant",
                report,
                fileName);

            ValidateUniqueNamedSections(
                document.Sections.Where(x => x.Name.Equals("AIAction", StringComparison.OrdinalIgnoreCase) || x.Name.Equals("Action", StringComparison.OrdinalIgnoreCase)),
                "AIAction",
                report,
                fileName);

            ValidateUniqueNamedSections(
                document.Sections.Where(x => x.Name.Equals("AIRule", StringComparison.OrdinalIgnoreCase) || x.Name.Equals("Rule", StringComparison.OrdinalIgnoreCase)),
                "AIRule",
                report,
                fileName);
        }

        private static void ValidateUniqueSkillSections(CustomTextDocument document, CustomSystemTextReport report, string fileName)
        {
            ValidateUniqueSections(
                document.Sections.Where(x => x.Name.Equals("Effect", StringComparison.OrdinalIgnoreCase)),
                section =>
                {
                    CustomSkillEffectStage stage = GetEnum(section.Values, "Stage", CustomSkillEffectStage.Cast);
                    string resource = Normalize(GetValue(section.Values, "Resource", null));
                    int startIndex = GetInt(section.Values, "Start", GetInt(section.Values, "StartIndex", 0));
                    int endIndex = GetInt(section.Values, "End", GetInt(section.Values, "EndIndex", 0));
                    return $"{stage}|{resource}|{startIndex}|{endIndex}";
                },
                "Effect",
                "Stage+Resource+Start+End",
                report,
                fileName);
        }

        private static void ValidateKnownBooleanFields(CustomTextDocument document, CustomSystemTextReport report)
        {
            foreach (CustomTextSection section in document.Sections)
                ValidateKnownBooleanOptions(section.Values, report, section.Source);
        }

        private static void ValidateKnownNumericFields(CustomTextDocument document, CustomSystemTextReport report)
        {
            foreach (CustomTextSection section in document.Sections)
                ValidateKnownNumericOptions(section.Values, report, section.Source, section.Name);
        }

        private static void ValidateKnownBooleanOptions(Dictionary<string, string> values, CustomSystemTextReport report, string source)
        {
            foreach (KeyValuePair<string, string> pair in values)
            {
                if (!BooleanTextKeys.Contains(pair.Key)) continue;
                if (TryParseTextBool(pair.Value, out _)) continue;

                report.Failures.Add($"{source}: `{pair.Key}` 布尔值无效: {pair.Value}，应为 True/False/1/0/yes/no。");
            }
        }

        private static void ValidateKnownNumericOptions(Dictionary<string, string> values, CustomSystemTextReport report, string source, string sectionName = null)
        {
            foreach (KeyValuePair<string, string> pair in values)
            {
                if (IntegerTextKeys.Contains(pair.Key) || IsSectionScopedIntegerKey(sectionName, pair.Key))
                {
                    if (!int.TryParse(pair.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                        report.Failures.Add($"{source}: `{pair.Key}` 数值无效: {pair.Value}，应为整数。");

                    continue;
                }

                if (DecimalTextKeys.Contains(pair.Key) &&
                    !decimal.TryParse(pair.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
                    report.Failures.Add($"{source}: `{pair.Key}` 数值无效: {pair.Value}，应为数字。");
            }
        }

        private static bool IsSectionScopedIntegerKey(string sectionName, string key)
        {
            return key.Equals("Value", StringComparison.OrdinalIgnoreCase) &&
                   !string.IsNullOrWhiteSpace(sectionName) &&
                   (sectionName.Equals("AIRule", StringComparison.OrdinalIgnoreCase) ||
                    sectionName.Equals("Rule", StringComparison.OrdinalIgnoreCase));
        }

        private static void ValidateUniqueNamedSections(IEnumerable<CustomTextSection> sections, string sectionName, CustomSystemTextReport report, string fileName)
        {
            ValidateUniqueSections(
                sections,
                section =>
                {
                    string name = Normalize(GetValue(section.Values, "Name", null));
                    return string.IsNullOrWhiteSpace(name) ? null : name;
                },
                sectionName,
                "Name",
                report,
                fileName);
        }

        private static void ValidateUniqueSections(IEnumerable<CustomTextSection> sections, Func<CustomTextSection, string> keySelector, string sectionName, string keyDescription, CustomSystemTextReport report, string fileName)
        {
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

            foreach (CustomTextSection section in sections)
            {
                string key = keySelector(section);
                if (string.IsNullOrWhiteSpace(key)) continue;
                if (seen.Add(key)) continue;

                report.Failures.Add($"{section.Source ?? fileName}: [{sectionName}] 存在重复 {keyDescription}: {key}");
            }
        }

        private void LoadSkillFiles(
            string directory,
            CustomSystemTextLoadResult result,
            Dictionary<string, CustomResourceLibraryInfo> resources,
            Dictionary<string, bool> damageProxyMonsters,
            bool createDetached,
            bool allowMissingPrimaryObject,
            bool strictMode,
            bool replaceExisting,
            bool includeTemplates)
        {
            string skillsDirectory = Path.Combine(directory, "Skills");
            if (!Directory.Exists(skillsDirectory)) return;

            foreach (string file in EnumerateCustomTextFiles(skillsDirectory, includeTemplates, result.Report))
            {
                int beforeFailures = result.Report.Failures.Count;
                CustomTextDocument document = ReadDocument(file, directory, result.Report);
                if (document.Sections.Count == 0) continue;

                ValidateKnownBooleanFields(document, result.Report);
                ValidateKnownNumericFields(document, result.Report);

                CustomTextSection magicSection = document.First("Magic");
                CustomTextSection skillSection = document.First("Skill");

                if (magicSection == null || skillSection == null)
                {
                    result.Report.Failures.Add($"{Path.GetFileName(file)}: 技能文件必须包含 [Magic] 和 [Skill] 段。");
                    continue;
                }

                ValidateUniqueSkillSections(document, result.Report, Path.GetFileName(file));

                MagicInfo magic = ResolveMagic(magicSection.Values, file, result.Report, allowMissingPrimaryObject, createDetached);
                if (magic == null) continue;
                IndexMagic(magic);

                CustomSkillInfo skill = createDetached ? CreateDetachedCustomSkill() : null;
                if (!createDetached)
                {
                    skill ??= _session.GetCollection<CustomSkillInfo>().Binding.FirstOrDefault(x => x.Magic == magic);
                    skill ??= _session.GetCollection<CustomSkillInfo>().CreateNewObject();
                }
                if (skill == null) continue;
                EnsureSkillChildren(skill);
                if (replaceExisting)
                    ClearSkillChildren(skill);
                else if (!createDetached)
                    DeduplicateSkillChildren(skill);

                ApplySkill(skill, magic, skillSection.Values, result.Report, skillSection.Source);
                ValidateDamageProxySkill(skill, damageProxyMonsters, result.Report, skillSection.Source);

                foreach (CustomTextSection section in document.Sections.Where(x => x.Name.Equals("Effect", StringComparison.OrdinalIgnoreCase)))
                {
                    CustomSkillEffectInfo effect = ResolveSkillEffect(skill, section.Values, resources, createDetached, replaceExisting);
                    bool isNewEffect = effect.Skill == null;
                    ApplySkillEffect(effect, section.Values, resources, result.Report, section.Source, isNewEffect);
                    effect.Skill = skill;
                    skill.Effects?.Add(effect);
                }

                result.Skills.Add(skill);

                if (strictMode && result.Report.Failures.Count > beforeFailures)
                {
                    result.Skills.Remove(skill);
                    result.Report.SkillsSkipped++;
                }
                else
                {
                    result.Report.SkillFilesLoaded++;
                    if (IsTemplateTextFile(skillsDirectory, file))
                        result.Report.TemplateSkillFilesLoaded++;
                }
            }
        }

        private MonsterInfo ResolveMonster(Dictionary<string, string> values, string file, CustomSystemTextReport report, bool allowCreate, bool createDetached)
        {
            MonsterInfo monster = null;

            if (values.TryGetValue("Index", out string indexValue) && int.TryParse(indexValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
                _monstersByIndex.TryGetValue(index, out monster);

            string name = GetValue(values, "Name", Path.GetFileNameWithoutExtension(file));
            if (monster == null && !string.IsNullOrWhiteSpace(name))
                _monsters.TryGetValue(Normalize(name), out monster);

            if (monster == null && !allowCreate)
            {
                report.MissingMonsters.Add(name);
                report.Failures.Add($"{Path.GetFileName(file)}: 找不到怪物 `{name}`，请先导入或创建 MonsterInfo。");
                return null;
            }

            if (createDetached)
            {
                if (!allowCreate)
                    return monster;

                if (monster == null || !_detachedTextMonsters.Contains(monster))
                    monster = new MonsterInfo();
            }
            else
            {
                monster ??= _session.GetCollection<MonsterInfo>().CreateNewObject();
            }

            monster.MonsterName = name;
            monster.Image = GetEnum(values, "Image", monster.Image, report, Path.GetFileName(file));
            monster.AI = GetInt(values, "AI", monster.AI);
            monster.Level = GetInt(values, "Level", monster.Level);
            monster.ViewRange = GetInt(values, "ViewRange", monster.ViewRange);
            monster.CoolEye = GetInt(values, "CoolEye", monster.CoolEye);
            monster.Experience = GetDecimal(values, "Experience", monster.Experience);
            monster.Undead = GetBool(values, "Undead", monster.Undead);
            monster.CanPush = GetBool(values, "CanPush", monster.CanPush);
            monster.CanTame = GetBool(values, "CanTame", monster.CanTame);
            monster.AttackDelay = GetInt(values, "AttackDelay", monster.AttackDelay);
            monster.MoveDelay = GetInt(values, "MoveDelay", monster.MoveDelay);
            monster.IsBoss = GetBool(values, "IsBoss", monster.IsBoss);
            monster.Flag = GetEnum(values, "Flag", monster.Flag, report, Path.GetFileName(file));
            monster.FaceImage = GetInt(values, "FaceImage", monster.FaceImage);

            return monster;
        }

        private int ApplyMonsterStats(MonsterInfo monster, IEnumerable<CustomTextSection> sections, bool createDetached, CustomSystemTextReport report)
        {
            int count = 0;

            foreach (CustomTextSection section in sections)
            {
                if (section.Name.Equals("Stats", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (KeyValuePair<string, string> pair in section.Values)
                    {
                        if (!TryParseStat(pair.Key, out Stat stat))
                        {
                            report.Failures.Add($"{section.Source}: 未知怪物属性 `{pair.Key}`。");
                            continue;
                        }

                        if (!int.TryParse(pair.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int amount))
                        {
                            report.Failures.Add($"{section.Source}: 怪物属性 `{pair.Key}` 的数值无效: {pair.Value}");
                            continue;
                        }

                        SetMonsterStat(monster, stat, amount, createDetached);
                        count++;
                    }

                    continue;
                }

                string statName = GetValue(section.Values, "Stat", GetValue(section.Values, "Name", null));
                if (string.IsNullOrWhiteSpace(statName))
                {
                    report.Failures.Add($"{section.Source}: [Stat] 必须配置 Stat 或 Name。");
                    continue;
                }

                if (!TryParseStat(statName, out Stat singleStat))
                {
                    report.Failures.Add($"{section.Source}: 未知怪物属性 `{statName}`。");
                    continue;
                }

                if (!section.Values.TryGetValue("Amount", out string amountValue) || !int.TryParse(amountValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int singleAmount))
                {
                    report.Failures.Add($"{section.Source}: [Stat] `{statName}` 必须配置有效 Amount。");
                    continue;
                }

                SetMonsterStat(monster, singleStat, singleAmount, createDetached);
                count++;
            }

            return count;
        }

        private void SetMonsterStat(MonsterInfo monster, Stat stat, int amount, bool createDetached)
        {
            MonsterInfoStat info = monster.MonsterInfoStats?.FirstOrDefault(x => x.Stat == stat);

            if (info == null)
                info = createDetached ? new MonsterInfoStat() : _session.GetCollection<MonsterInfoStat>().CreateNewObject();

            info.Monster = monster;
            info.Stat = stat;
            info.Amount = amount;

            if (monster.MonsterInfoStats != null && !monster.MonsterInfoStats.Contains(info))
                monster.MonsterInfoStats.Add(info);
        }

        private void ApplyMonsterDrops(MonsterInfo monster, IEnumerable<CustomTextSection> sections, bool createDetached, bool replaceExisting, CustomSystemTextLoadResult result)
        {
            List<string> lines = sections
                .SelectMany(x => x.Lines)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            if (lines.Count == 0) return;

            Mir2DropImporter importer = new(_session);

            if (createDetached)
            {
                Mir2DropOverrideLoadResult drops = importer.BuildCompiledInline(monster, lines);
                MergeDropReport(result.Report, drops.Report, imported: false);

                foreach (KeyValuePair<MonsterInfo, CompiledMonsterDrop> pair in drops.Overrides)
                    result.DropOverrides[pair.Key] = pair.Value;

                return;
            }

            Mir2DropImportReport report = importer.ImportInline(monster, lines, replaceExisting);
            MergeDropReport(result.Report, report, imported: true);
        }

        private static void MergeDropReport(CustomSystemTextReport target, Mir2DropImportReport source, bool imported)
        {
            int entries = source.DropsImported + source.GroupItemsImported;

            target.MonsterDropEntriesLoaded += entries;
            target.MonsterDropGroupsLoaded += source.GroupsImported;
            target.MonsterDropGoldLines += source.GoldLines;
            target.MonsterDropQuestOnlyLines += source.QuestOnlyLines.Count;

            if (imported)
            {
                target.MonsterDropEntriesImported += entries;
                target.MonsterDropGroupsImported += source.GroupsImported;
            }

            target.Failures.AddRange(source.Failures);
            target.MissingItems.AddRange(source.MissingItems);
            target.MissingMonsters.AddRange(source.MissingMonsters);
        }

        private MagicInfo ResolveMagic(Dictionary<string, string> values, string file, CustomSystemTextReport report, bool allowCreate, bool createDetached)
        {
            MagicInfo magic = null;

            if (values.TryGetValue("Index", out string indexValue) && int.TryParse(indexValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
                _magicsByIndex.TryGetValue(index, out magic);

            string name = GetValue(values, "Name", Path.GetFileNameWithoutExtension(file));
            if (magic == null && !string.IsNullOrWhiteSpace(name))
                _magics.TryGetValue(Normalize(name), out magic);

            if (magic == null && !allowCreate)
            {
                report.MissingMagics.Add(name);
                report.Failures.Add($"{Path.GetFileName(file)}: 找不到技能 `{name}`，请先导入或创建 MagicInfo。");
                return null;
            }

            if (createDetached)
            {
                if (!allowCreate)
                    return magic;

                magic = new MagicInfo();
            }
            else
            {
                magic ??= _session.GetCollection<MagicInfo>().CreateNewObject();
            }

            magic.Name = name;
            magic.Magic = GetEnum(values, "Magic", magic.Magic == MagicType.None ? MagicType.Custom : magic.Magic, report, Path.GetFileName(file));
            magic.Class = GetEnum(values, "Class", magic.Class, report, Path.GetFileName(file));
            magic.School = GetEnum(values, "School", magic.School == MagicSchool.None ? MagicSchool.Active : magic.School, report, Path.GetFileName(file));
            magic.Property = GetEnum(values, "Property", magic.Property == MagicProperty.None ? MagicProperty.Active : magic.Property, report, Path.GetFileName(file));
            magic.Icon = GetInt(values, "Icon", magic.Icon);
            magic.MinBasePower = GetInt(values, "MinBasePower", magic.MinBasePower);
            magic.MaxBasePower = GetInt(values, "MaxBasePower", magic.MaxBasePower);
            magic.MinLevelPower = GetInt(values, "MinLevelPower", magic.MinLevelPower);
            magic.MaxLevelPower = GetInt(values, "MaxLevelPower", magic.MaxLevelPower);
            magic.BaseCost = GetInt(values, "BaseCost", magic.BaseCost);
            magic.LevelCost = GetInt(values, "LevelCost", magic.LevelCost);
            magic.NeedLevel1 = GetInt(values, "NeedLevel1", magic.NeedLevel1);
            magic.NeedLevel2 = GetInt(values, "NeedLevel2", magic.NeedLevel2);
            magic.NeedLevel3 = GetInt(values, "NeedLevel3", magic.NeedLevel3);
            magic.Experience1 = GetInt(values, "Experience1", magic.Experience1);
            magic.Experience2 = GetInt(values, "Experience2", magic.Experience2);
            magic.Experience3 = GetInt(values, "Experience3", magic.Experience3);
            magic.Delay = GetInt(values, "Delay", magic.Delay);
            magic.Description = GetValue(values, "Description", magic.Description);

            return magic;
        }

        private static void ApplyVisual(CustomMonsterVisualInfo visual, MonsterInfo monster, Dictionary<string, string> values, Dictionary<string, CustomResourceLibraryInfo> resources, CustomSystemTextReport report, string source)
        {
            visual.Monster = monster;
            visual.DefaultResource = ResolveResource(values, "Resource", resources, report, source);
            visual.Scale = GetFloat(values, "Scale", visual.Scale == 0 ? 1F : visual.Scale);
            visual.DrawShadow = GetBool(values, "DrawShadow", visual.DrawShadow);
            visual.Blend = GetBool(values, "Blend", visual.Blend);
            visual.KeepCorpse = GetBool(values, "KeepCorpse", visual.KeepCorpse);
            visual.DefaultFrameDelay = GetInt(values, "DefaultFrameDelay", visual.DefaultFrameDelay == 0 ? 100 : visual.DefaultFrameDelay);
            visual.AttackSound = GetEnum(values, "AttackSound", visual.AttackSound, report, source);
            visual.RangeAttackSound = GetEnum(values, "RangeAttackSound", visual.RangeAttackSound, report, source);
            visual.MagicSound = GetEnum(values, "MagicSound", visual.MagicSound, report, source);
            visual.StruckSound = GetEnum(values, "StruckSound", visual.StruckSound, report, source);
            visual.DieSound = GetEnum(values, "DieSound", visual.DieSound, report, source);
            visual.ShowSound = GetEnum(values, "ShowSound", visual.ShowSound, report, source);
            visual.HideSound = GetEnum(values, "HideSound", visual.HideSound, report, source);

            int frameBase = GetInt(values, "FrameBase", GetInt(values, "BaseIndex", visual.FrameBase));
            int shapeIndex = GetInt(values, "ShapeIndex", GetInt(values, "BodyShape", 0));
            int shapeFrameOffset = GetInt(values, "ShapeFrameOffset", GetInt(values, "BodyOffset", 0));
            visual.FrameBase = frameBase + shapeIndex * shapeFrameOffset;
        }

        private static void ApplyAnimation(CustomMonsterAnimationInfo animation, Dictionary<string, string> values, Dictionary<string, CustomResourceLibraryInfo> resources, CustomSystemTextReport report, string source)
        {
            animation.Resource = ResolveResource(values, "Resource", resources, report, source) ?? animation.Visual?.DefaultResource;
            animation.Action = GetEnum(values, "Action", CustomMonsterAnimationType.Standing, report, source);
            animation.Variant = GetInt(values, "Variant", 0);
            ValidateAnimationVariant(animation.Variant, "Variant", report, source);
            animation.StartIndex = GetInt(values, "Start", GetInt(values, "StartIndex", animation.StartIndex));
            animation.EndIndex = GetInt(values, "End", GetInt(values, "EndIndex", animation.EndIndex));
            animation.FrameCount = GetInt(values, "FrameCount", animation.FrameCount);
            animation.DirectionCount = GetInt(values, "DirectionCount", animation.DirectionCount == 0 ? 8 : animation.DirectionCount);
            animation.DirectionOffset = GetInt(values, "DirectionOffset", animation.DirectionOffset == 0 ? 10 : animation.DirectionOffset);
            animation.FrameDelay = GetInt(values, "FrameDelay", animation.FrameDelay == 0 ? 100 : animation.FrameDelay);
            animation.Loop = GetBool(values, "Loop", animation.Loop);
            animation.Reversed = GetBool(values, "Reversed", animation.Reversed);
            animation.OffsetX = GetInt(values, "OffsetX", animation.OffsetX);
            animation.OffsetY = GetInt(values, "OffsetY", animation.OffsetY);
            animation.Priority = GetInt(values, "Priority", animation.Priority);
            animation.Sound = GetEnum(values, "Sound", animation.Sound, report, source);
            animation.EffectResource = ResolveResource(values, "EffectResource", resources, report, source);
            animation.EffectTarget = GetEnum(values, "EffectTarget", animation.EffectTarget, report, source);
            animation.EffectStartIndex = GetInt(values, "EffectStart", GetInt(values, "EffectStartIndex", animation.EffectStartIndex));
            animation.EffectEndIndex = GetInt(values, "EffectEnd", GetInt(values, "EffectEndIndex", animation.EffectEndIndex));
            animation.EffectFrameCount = GetInt(values, "EffectFrameCount", animation.EffectFrameCount);
            animation.EffectFrameDelay = GetInt(values, "EffectFrameDelay", animation.EffectFrameDelay == 0 ? 100 : animation.EffectFrameDelay);
            animation.EffectOffsetX = GetInt(values, "EffectOffsetX", animation.EffectOffsetX);
            animation.EffectOffsetY = GetInt(values, "EffectOffsetY", animation.EffectOffsetY);
            animation.EffectStartLight = GetInt(values, "EffectStartLight", animation.EffectStartLight);
            animation.EffectEndLight = GetInt(values, "EffectEndLight", animation.EffectEndLight);
            animation.EffectStartDelay = GetInt(values, "EffectStartDelay", animation.EffectStartDelay);
            animation.EffectDuration = GetInt(values, "EffectDuration", animation.EffectDuration);
            animation.EffectTriggerFrame = GetInt(values, "EffectTriggerFrame", animation.EffectTriggerFrame);
            animation.EffectBlend = GetBool(values, "EffectBlend", animation.EffectBlend);
            animation.EffectLoop = GetBool(values, "EffectLoop", animation.EffectLoop);

            if (animation.Resource == null)
                report.Failures.Add($"{source}: [Animation] `{animation.Action}` 必须配置 Resource，或在 [Visual] 配置默认 Resource。");

            ValidateFrameSegment($"[Animation] `{animation.Action}`", animation.StartIndex, animation.EndIndex, animation.FrameCount, report, source);
            ValidatePositive(animation.DirectionCount, "DirectionCount", $"[Animation] `{animation.Action}`", report, source);
            if (animation.DirectionCount > 8)
                report.Failures.Add($"{source}: [Animation] `{animation.Action}` 的 DirectionCount 不能大于 8。");
            if (animation.DirectionOffset < 0)
                report.Failures.Add($"{source}: [Animation] `{animation.Action}` 的 DirectionOffset 不能小于 0。");
            if (animation.DirectionCount > 1 && animation.DirectionOffset == 0)
                report.Failures.Add($"{source}: [Animation] `{animation.Action}` 的 DirectionCount 大于 1 时 DirectionOffset 必须大于 0。");
            ValidatePositive(animation.FrameDelay, "FrameDelay", $"[Animation] `{animation.Action}`", report, source);

            if (animation.EffectResource != null)
            {
                ValidateFrameSegment($"[Animation] `{animation.Action}` 的动作特效", animation.EffectStartIndex, animation.EffectEndIndex, animation.EffectFrameCount, report, source);
                ValidatePositive(animation.EffectFrameDelay, "EffectFrameDelay", $"[Animation] `{animation.Action}` 的动作特效", report, source);

                if (animation.EffectStartDelay < 0)
                    report.Failures.Add($"{source}: [Animation] `{animation.Action}` 的 EffectStartDelay 不能小于 0。");
                if (animation.EffectDuration < 0)
                    report.Failures.Add($"{source}: [Animation] `{animation.Action}` 的 EffectDuration 不能小于 0。");
                if (animation.EffectTriggerFrame < -1)
                    report.Failures.Add($"{source}: [Animation] `{animation.Action}` 的 EffectTriggerFrame 不能小于 -1。");

                int animationFrameCount = GetEffectiveFrameCount(animation.StartIndex, animation.EndIndex, animation.FrameCount);
                if (animation.EffectTriggerFrame >= animationFrameCount)
                    report.Failures.Add($"{source}: [Animation] `{animation.Action}` 的 EffectTriggerFrame 超出动作帧数 {animationFrameCount}。");
            }
        }

        private static void ApplyBehavior(CustomMonsterBehaviorInfo behavior, MonsterInfo monster, Dictionary<string, string> values, CustomSystemTextReport report, string source)
        {
            bool isNew = behavior.Monster == null;

            behavior.Monster = monster;
            behavior.Template = GetEnum(values, "Template", behavior.Template == CustomMonsterBehaviorTemplate.None ? CustomMonsterBehaviorTemplate.Melee : behavior.Template, report, source);
            behavior.CanBeKilled = GetBool(values, "CanBeKilled", isNew ? true : behavior.CanBeKilled);
            behavior.CanAttackPlayers = GetBool(values, "CanAttackPlayers", isNew ? true : behavior.CanAttackPlayers);
            behavior.CanBeTamed = GetBool(values, "CanBeTamed", behavior.CanBeTamed);
            behavior.IsPickupCompanion = GetBool(values, "IsPickupCompanion", behavior.IsPickupCompanion);
            behavior.Passive = GetBool(values, "Passive", behavior.Passive);
            behavior.AlwaysActive = GetBool(values, "AlwaysActive", behavior.AlwaysActive);
            behavior.DamageProxy = GetBool(values, "DamageProxy", behavior.DamageProxy);
            behavior.DamageProxyRevealDelay = GetInt(values, "DamageProxyRevealDelay", behavior.DamageProxyRevealDelay == 0 ? 500 : behavior.DamageProxyRevealDelay);
            behavior.DamageProxyRange = GetInt(values, "DamageProxyRange", behavior.DamageProxyRange);
            behavior.DamageProxySpawnEffect = GetEnum(values, "DamageProxySpawnEffect", behavior.DamageProxySpawnEffect, report, source);
            behavior.DamageProxyNameFormat = GetValue(values, "DamageProxyNameFormat", behavior.DamageProxyNameFormat);
            behavior.NeedHarvest = GetBool(values, "NeedHarvest", behavior.NeedHarvest);
            behavior.HarvestCount = GetInt(values, "HarvestCount", behavior.HarvestCount);
            behavior.AvoidFireWall = GetBool(values, "AvoidFireWall", behavior.AvoidFireWall);
            behavior.IgnoreShield = GetBool(values, "IgnoreShield", behavior.IgnoreShield);
            behavior.IgnoreArmour = GetBool(values, "IgnoreArmour", behavior.IgnoreArmour);
            behavior.PoisonType = GetEnum(values, "PoisonType", behavior.PoisonType, report, source);
            behavior.PoisonValue = GetInt(values, "PoisonValue", behavior.PoisonValue);
            behavior.PoisonRate = GetInt(values, "PoisonRate", behavior.PoisonRate == 0 ? 10 : behavior.PoisonRate);
            behavior.PoisonTicks = GetInt(values, "PoisonTicks", behavior.PoisonTicks == 0 ? 5 : behavior.PoisonTicks);
            behavior.PoisonFrequency = GetInt(values, "PoisonFrequency", behavior.PoisonFrequency == 0 ? 2 : behavior.PoisonFrequency);
            behavior.PoisonCanKill = GetBool(values, "PoisonCanKill", behavior.PoisonCanKill);
            behavior.PoisonStackable = GetBool(values, "PoisonStackable", behavior.PoisonStackable);
            behavior.DisableMovement = GetBool(values, "DisableMovement", behavior.DisableMovement);
            behavior.DisableAttack = GetBool(values, "DisableAttack", behavior.DisableAttack);
            behavior.DisableRegen = GetBool(values, "DisableRegen", behavior.DisableRegen);
            behavior.DisableBlocking = GetBool(values, "DisableBlocking", behavior.DisableBlocking);
            behavior.HiddenBlocks = GetBool(values, "HiddenBlocks", isNew ? true : behavior.HiddenBlocks);
            behavior.HiddenCanAttack = GetBool(values, "HiddenCanAttack", isNew ? true : behavior.HiddenCanAttack);
            behavior.HiddenCanBeAttacked = GetBool(values, "HiddenCanBeAttacked", isNew ? true : behavior.HiddenCanBeAttacked);
            behavior.ProtectedByMonsterFlag = GetEnum(values, "ProtectedByMonsterFlag", behavior.ProtectedByMonsterFlag, report, source);
            behavior.ProtectedByMonsterFlags = GetValue(values, "ProtectedByMonsterFlags", GetValue(values, "ProtectionFlags", behavior.ProtectedByMonsterFlags));
            behavior.ProtectionRadius = GetInt(values, "ProtectionRadius", behavior.ProtectionRadius);
            behavior.ProtectionCheckInterval = GetInt(values, "ProtectionCheckInterval", behavior.ProtectionCheckInterval == 0 ? 1000 : behavior.ProtectionCheckInterval);
            behavior.AttackRange = GetInt(values, "AttackRange", behavior.AttackRange == 0 ? 1 : behavior.AttackRange);
            behavior.ChaseRange = GetInt(values, "ChaseRange", behavior.ChaseRange == 0 ? 12 : behavior.ChaseRange);
            behavior.ReturnRange = GetInt(values, "ReturnRange", behavior.ReturnRange == 0 ? 16 : behavior.ReturnRange);
            behavior.MaxMinions = GetInt(values, "MaxMinions", behavior.MaxMinions == 0 ? 20 : behavior.MaxMinions);
            behavior.SkillChance = GetInt(values, "SkillChance", behavior.SkillChance == 0 ? 25 : behavior.SkillChance);
            behavior.RoamDelay = GetInt(values, "RoamDelay", behavior.RoamDelay == 0 ? 2000 : behavior.RoamDelay);
            behavior.UseBaseFallback = GetBool(values, "UseBaseFallback", isNew ? true : behavior.UseBaseFallback);

            if (behavior.PoisonType != PoisonType.None)
            {
                if (behavior.PoisonRate < 0)
                    report.Failures.Add($"{source}: Behavior.PoisonRate 不能小于 0；0 表示每次命中必定附毒，大于 0 表示 1/N 概率。");
                if (behavior.PoisonTicks <= 0)
                    report.Failures.Add($"{source}: Behavior.PoisonTicks 必须大于 0。");
                if (behavior.PoisonFrequency <= 0)
                    report.Failures.Add($"{source}: Behavior.PoisonFrequency 必须大于 0，单位为秒。");
                if (behavior.PoisonFrequency >= 100)
                    report.Failures.Add($"{source}: Behavior.PoisonFrequency 单位为秒，当前值 {behavior.PoisonFrequency} 过大；如果需要毫秒级毒跳间隔，请使用 AIAction.TickRate。");
            }

            if (behavior.HarvestCount < 0)
                report.Failures.Add($"{source}: Behavior.HarvestCount 不能小于 0。");
            if (behavior.DamageProxyRevealDelay < 0)
                report.Failures.Add($"{source}: Behavior.DamageProxyRevealDelay 不能小于 0。");
            if (behavior.DamageProxyRange < 0)
                report.Failures.Add($"{source}: Behavior.DamageProxyRange 不能小于 0。");
            if (behavior.ProtectionRadius < 0)
                report.Failures.Add($"{source}: Behavior.ProtectionRadius 不能小于 0。");
            if (behavior.ProtectionCheckInterval <= 0)
                report.Failures.Add($"{source}: Behavior.ProtectionCheckInterval 必须大于 0。");
            if (behavior.AttackRange < 0)
                report.Failures.Add($"{source}: Behavior.AttackRange 不能小于 0。");
            if (!behavior.DisableAttack && behavior.AttackRange == 0)
                report.Failures.Add($"{source}: Behavior.AttackRange 必须大于 0；DisableAttack=True 的纯功能怪物才允许 0。");
            if (behavior.ChaseRange < 0)
                report.Failures.Add($"{source}: Behavior.ChaseRange 不能小于 0。");
            if (behavior.ReturnRange < 0)
                report.Failures.Add($"{source}: Behavior.ReturnRange 不能小于 0。");
            if (behavior.MaxMinions < 0)
                report.Failures.Add($"{source}: Behavior.MaxMinions 不能小于 0。");
            if (behavior.SkillChance < 0 || behavior.SkillChance > 100)
                report.Failures.Add($"{source}: Behavior.SkillChance 必须在 0..100 范围内。");
            if (behavior.RoamDelay < 0)
                report.Failures.Add($"{source}: Behavior.RoamDelay 不能小于 0。");

            ValidateMonsterFlagList(behavior.ProtectedByMonsterFlags, "Behavior.ProtectedByMonsterFlags", report, source);
        }

        private static void ValidateMonsterFlagList(string value, string context, CustomSystemTextReport report, string source)
        {
            if (string.IsNullOrWhiteSpace(value)) return;

            foreach (string token in SplitFlagList(value))
                if (!Enum.TryParse(token, true, out MonsterFlag _))
                    report.Failures.Add($"{source}: {context} 包含未知 MonsterFlag `{token}`。");
        }

        private static IEnumerable<string> SplitFlagList(string value)
        {
            return (value ?? string.Empty)
                .Split(['|', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(x => !string.IsNullOrWhiteSpace(x));
        }

        private static void ApplyAiRule(CustomMonsterAiRuleInfo rule, Dictionary<string, string> values, CustomSystemTextReport report, string source)
        {
            rule.Name = GetValue(values, "Name", rule.Name);
            rule.Trigger = GetEnum(values, "Trigger", rule.Trigger, report, source);
            rule.Condition = GetEnum(values, "Condition", rule.Condition, report, source);
            rule.ActionName = GetValue(values, "Action", GetValue(values, "ActionName", rule.ActionName));
            rule.Priority = GetInt(values, "Priority", rule.Priority);
            rule.Chance = GetInt(values, "Chance", rule.Chance == 0 ? 100 : rule.Chance);
            rule.Cooldown = GetInt(values, "Cooldown", rule.Cooldown);
            rule.MaxExecutions = GetInt(values, "MaxExecutions", rule.MaxExecutions);
            rule.Value = GetInt(values, "Value", rule.Value);
            rule.Radius = GetInt(values, "Radius", rule.Radius);
            rule.BuffType = GetEnum(values, "BuffType", rule.BuffType, report, source);
            rule.Conditions = GetValue(values, "Conditions", GetValue(values, "ConditionExpression", rule.Conditions));
            rule.ConditionJoin = GetEnum(values, "ConditionJoin", GetEnum(values, "ConditionMode", rule.ConditionJoin, report, source), report, source);
            rule.InvertCondition = GetBool(values, "InvertCondition", GetBool(values, "Invert", rule.InvertCondition));
            rule.CounterName = GetValue(values, "CounterName", GetValue(values, "Counter", rule.CounterName));
            rule.MinHealthPercent = GetInt(values, "MinHealthPercent", rule.MinHealthPercent);
            rule.MaxHealthPercent = GetInt(values, "MaxHealthPercent", rule.MaxHealthPercent == 0 ? 100 : rule.MaxHealthPercent);

            if (string.IsNullOrWhiteSpace(rule.ActionName))
                report.Failures.Add($"{source}: AIRule 必须配置 Action。");
            if (RuleConditionRequiresBuff(rule.Condition) && rule.BuffType == BuffType.None)
                report.Failures.Add($"{source}: AIRule `{rule.Name}` 使用 {rule.Condition} 时必须配置 BuffType。");
            if (RuleConditionRequiresCounter(rule.Condition) && string.IsNullOrWhiteSpace(rule.CounterName))
                report.Failures.Add($"{source}: AIRule `{rule.Name}` 使用 {rule.Condition} 时必须配置 CounterName。");

            ValidateAdditionalRuleConditions(rule, report, source);
        }

        private void ApplyAiAction(CustomMonsterAiActionInfo action, Dictionary<string, string> values, CustomSystemTextReport report, string source)
        {
            action.Name = GetValue(values, "Name", action.Name);
            action.Action = GetEnum(values, "Type", GetEnum(values, "Action", action.Action, report, source), report, source);
            action.Target = GetEnum(values, "Target", action.Target, report, source);
            action.AnimationVariant = GetInt(values, "AnimationVariant", GetInt(values, "Variant", action.AnimationVariant));
            ValidateAnimationVariant(action.AnimationVariant, "AnimationVariant", report, source);
            action.Options = GetValue(values, "Options", action.Options);
            action.Magic = GetEnum(values, "Magic", action.Magic, report, source);
            action.MagicInfo = ResolveMagicReference(values, "MagicInfo", report, source);
            action.Element = GetEnum(values, "Element", action.Element, report, source);
            action.PowerSource = GetEnum(values, "Power", GetEnum(values, "PowerSource", action.PowerSource, report, source), report, source);
            action.FixedPower = GetInt(values, "FixedPower", action.FixedPower);
            action.PowerRate = GetInt(values, "PowerRate", action.PowerRate == 0 ? 100 : action.PowerRate);
            action.Radius = GetInt(values, "Radius", action.Radius);
            action.Distance = GetInt(values, "Distance", action.Distance);
            action.MinOffset = GetInt(values, "MinOffset", action.MinOffset);
            action.MaxOffset = GetInt(values, "MaxOffset", action.MaxOffset);
            action.Delay = GetInt(values, "Delay", action.Delay == 0 ? 500 : action.Delay);
            action.TargetChance = GetInt(values, "TargetChance", action.TargetChance == 0 ? 100 : action.TargetChance);
            action.EmptyLocationChance = GetInt(values, "EmptyLocationChance", action.EmptyLocationChance);
            action.MaxTargets = GetInt(values, "MaxTargets", action.MaxTargets);
            action.RandomTargetCount = GetInt(values, "RandomTargetCount", GetInt(values, "RandomTargets", action.RandomTargetCount));
            action.CounterName = GetValue(values, "CounterName", GetValue(values, "Counter", action.CounterName));
            action.FixedCount = GetInt(values, "FixedCount", GetInt(values, "Count", action.FixedCount));
            action.RandomCount = GetInt(values, "RandomCount", action.RandomCount);
            action.MinionDuration = GetInt(values, "MinionDuration", GetInt(values, "SummonDuration", action.MinionDuration));
            action.Travel = GetBool(values, "Travel", action.Travel);
            action.SpawnMonster = ResolveMonsterReference(values, "SpawnMonster", report, source);
            action.BuffType = GetEnum(values, "BuffType", action.BuffType, report, source);
            action.PoisonType = GetEnum(values, "PoisonType", action.PoisonType, report, source);
            action.PoisonCanKill = GetBool(values, "PoisonCanKill", action.PoisonCanKill);
            action.BuffStats = GetStats(values, "BuffStats", action.BuffStats, report, source);
            action.BuffVisible = GetBool(values, "BuffVisible", action.BuffVisible);
            action.BuffPause = GetBool(values, "BuffPause", action.BuffPause);
            action.BuffHidden = GetBool(values, "BuffHidden", action.BuffHidden);
            action.BuffExtra = GetInt(values, "BuffExtra", action.BuffExtra);
            action.Duration = GetInt(values, "Duration", action.Duration == 0 ? 3000 : action.Duration);
            action.TickRate = GetInt(values, "TickRate", action.TickRate == 0 ? 1000 : action.TickRate);

            if (string.IsNullOrWhiteSpace(action.Name))
                report.Failures.Add($"{source}: AIAction 必须配置 Name。");
            if (action.Action == CustomAiActionType.None)
                report.Failures.Add($"{source}: AIAction `{action.Name}` 必须配置有效 Type。");
            if (action.Action is CustomAiActionType.SetCounter or CustomAiActionType.ChangeCounter && string.IsNullOrWhiteSpace(action.CounterName))
                report.Failures.Add($"{source}: AIAction `{action.Name}` 使用 {action.Action} 时必须配置 CounterName。");

            ValidateAiActionFields(action, report, source);
        }

        private void ApplyMonsterSkill(CustomMonsterSkillInfo skill, Dictionary<string, string> values, CustomSystemTextReport report, string source)
        {
            skill.Magic = ResolveMagicReference(values, "Magic", report, source);
            skill.Target = GetEnum(values, "Target", skill.Target, report, source);
            skill.AnimationVariant = GetInt(values, "AnimationVariant", GetInt(values, "Variant", skill.AnimationVariant));
            ValidateAnimationVariant(skill.AnimationVariant, "AnimationVariant", report, source);
            skill.Chance = GetInt(values, "Chance", skill.Chance == 0 ? 25 : skill.Chance);
            skill.Cooldown = GetInt(values, "Cooldown", skill.Cooldown == 0 ? 3000 : skill.Cooldown);
            skill.Range = GetInt(values, "Range", skill.Range == 0 ? 8 : skill.Range);
            skill.MinHealthPercent = GetInt(values, "MinHealthPercent", skill.MinHealthPercent);
            skill.MaxHealthPercent = GetInt(values, "MaxHealthPercent", skill.MaxHealthPercent == 0 ? 100 : skill.MaxHealthPercent);
        }

        private static void ValidateAiReferences(CustomMonsterBehaviorInfo behavior, CustomSystemTextReport report, string source, Dictionary<string, CustomSkillInfo> customSkillsByMagic)
        {
            if (behavior == null) return;

            HashSet<string> actionNames = behavior.Actions
                .Where(x => !string.IsNullOrWhiteSpace(x?.Name))
                .Select(x => Normalize(x.Name))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (CustomMonsterAiRuleInfo rule in behavior.Rules)
            {
                if (rule == null || string.IsNullOrWhiteSpace(rule.ActionName)) continue;

                if (!actionNames.Contains(Normalize(rule.ActionName)))
                    report.Failures.Add($"{source}: AIRule `{rule.Name}` 引用的 AIAction `{rule.ActionName}` 不存在。");
            }

            foreach (CustomMonsterSkillInfo skillEntry in behavior.Skills)
                ValidateCustomSkillReference(
                    skillEntry?.Magic,
                    skillEntry?.Target ?? CustomAiTargetSelectorType.CurrentTarget,
                    $"[Skill] `{skillEntry?.Magic?.Name}`",
                    customSkillsByMagic,
                    report,
                    source);

            foreach (CustomMonsterAiActionInfo action in behavior.Actions)
            {
                if (action?.Action == CustomAiActionType.CastSkill && action.MagicInfo == null)
                    report.Failures.Add($"{source}: AIAction `{action.Name}` 使用 CastSkill 时必须配置 MagicInfo。");
                else if (action?.Action == CustomAiActionType.CastSkill)
                    ValidateCustomSkillReference(
                        action.MagicInfo,
                        action.Target,
                        $"AIAction `{action.Name}`",
                        customSkillsByMagic,
                        report,
                        source);

                ValidateAiActionOptions(action, report, source);

                if (action?.Action is not (CustomAiActionType.WeightedChoice or CustomAiActionType.Sequence or CustomAiActionType.ScheduleAction) || string.IsNullOrWhiteSpace(action.Options)) continue;

                foreach ((string option, _) in ParseWeightedOptions(action.Options))
                    if (!actionNames.Contains(Normalize(option)))
                        report.Failures.Add($"{source}: AIAction `{action.Name}` 的 Options 引用的 AIAction `{option}` 不存在。");
            }

            foreach (CustomMonsterAiActionInfo action in behavior.Actions)
            {
                if (action == null) continue;

                string onHitAction = GetActionOptionValue(action.Options, "OnHitAction");
                if (!string.IsNullOrWhiteSpace(onHitAction) && !actionNames.Contains(Normalize(onHitAction)))
                    report.Failures.Add($"{source}: AIAction `{action.Name}` 的 OnHitAction `{onHitAction}` 不存在。");

                if (action.Action != CustomAiActionType.HazardRoam) continue;

                string collisionAction = GetActionOptionValue(action.Options, "CollisionAction");
                if (!string.IsNullOrWhiteSpace(collisionAction) && !actionNames.Contains(Normalize(collisionAction)))
                    report.Failures.Add($"{source}: AIAction `{action.Name}` 的 CollisionAction `{collisionAction}` 不存在。");
            }
        }

        private static void ValidateAiActionOptions(CustomMonsterAiActionInfo action, CustomSystemTextReport report, string source)
        {
            if (action == null) return;

            if (action.Action is CustomAiActionType.MapEffect or CustomAiActionType.ObjectEffect)
            {
                string effectName = GetActionOptionValue(action.Options, "Effect");
                if (string.IsNullOrWhiteSpace(effectName))
                    report.Failures.Add($"{source}: AIAction `{action.Name}` 使用 {action.Action} 时必须在 Options 中配置 Effect。");
                else if (!Enum.TryParse(effectName, true, out Effect _))
                    report.Failures.Add($"{source}: AIAction `{action.Name}` 的 {action.Action} Effect `{effectName}` 不是有效 Effect 枚举。");
            }

            if (action.Action == CustomAiActionType.SpawnSpellObject)
            {
                string effectName = GetActionOptionValue(action.Options, "SpellEffect");
                if (string.IsNullOrWhiteSpace(effectName))
                    effectName = GetActionOptionValue(action.Options, "Effect");

                if (string.IsNullOrWhiteSpace(effectName))
                    report.Failures.Add($"{source}: AIAction `{action.Name}` 使用 SpawnSpellObject 时必须在 Options 中配置 SpellEffect 或 Effect。");
                else if (!Enum.TryParse(effectName, true, out SpellEffect parsed) || parsed == SpellEffect.None)
                    report.Failures.Add($"{source}: AIAction `{action.Name}` 的 SpellEffect `{effectName}` 不是有效 SpellEffect 枚举。");
            }

            if (action.Action == CustomAiActionType.BroadcastMessage)
                ValidateBroadcastMessageAction(action, report, source);

            if (action.Action == CustomAiActionType.TeleportTargetsToRegion)
                ValidateTeleportTargetsToRegionAction(action, report, source);

            if (action.Action == CustomAiActionType.RecallMonstersByFlag)
                ValidateRecallMonstersByFlagAction(action, report, source);

            if (string.IsNullOrWhiteSpace(action.Options)) return;

            ValidateKnownAiActionOptionNames(action, report, source);
            ValidateNonNegativeActionOption(action, "TargetHealthRate", report, source);
            ValidateNonNegativeActionOption(action, "TargetHealthSourceRate", report, source);
            ValidateNonNegativeActionOption(action, "MinionLeashRadius", report, source);
            ValidateNonNegativeActionOption(action, "LeashRadius", report, source);
            ValidateNonNegativeActionOption(action, "Attempts", report, source);
            ValidateNonNegativeActionOption(action, "RecallOutsideRadius", report, source);
            ValidateNonNegativeActionOption(action, "OriginDistance", report, source);
            ValidateNonNegativeActionOption(action, "OffsetDistance", report, source);
            ValidateNonNegativeActionOption(action, "CollisionRadius", report, source);
            ValidateNonNegativeActionOption(action, "MaxTotal", report, source);
            ValidateNonNegativeActionOption(action, "DurationRandom", report, source);
            ValidateNonNegativeActionOption(action, "PushDistance", report, source);
            ValidateNonNegativeActionOption(action, "RegionIndex", report, source);
            ValidateNonNegativeActionOption(action, "Repeat", report, source);
            ValidatePercentActionOption(action, "ForwardChance", report, source);
            ValidatePercentActionOption(action, "OnHitChance", report, source);
            ValidatePercentActionOption(action, "ActionChance", report, source);

            foreach (string optionName in BoolActionOptions)
                ValidateBoolActionOption(action, optionName, report, source);

            string classPowerRates = GetActionOptionValue(action.Options, "ClassPowerRates");
            if (!string.IsNullOrWhiteSpace(classPowerRates))
                foreach (string token in classPowerRates.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    string[] parts = token.Split(':', 2, StringSplitOptions.TrimEntries);
                    if (parts.Length != 2)
                    {
                        report.Failures.Add($"{source}: AIAction `{action.Name}` 的 ClassPowerRates 项 `{token}` 必须写成 职业:倍率。");
                        continue;
                    }

                    if (!Enum.TryParse(parts[0], true, out MirClass _))
                        report.Failures.Add($"{source}: AIAction `{action.Name}` 的 ClassPowerRates 包含未知职业 `{parts[0]}`。");

                    if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int rate) || rate < 0)
                        report.Failures.Add($"{source}: AIAction `{action.Name}` 的 ClassPowerRates 倍率 `{parts[1]}` 必须是非负整数。");
                }

            ValidateBuffStatsScaleOption(action, "BuffStatsFromTarget", report, source);
            ValidateBuffStatsScaleOption(action, "BuffStatsFromSelf", report, source);
            ValidateBuffTypeActionOption(action, "RequireHasBuff", report, source);
            ValidateBuffTypeActionOption(action, "RequireMissingBuff", report, source);
            ValidatePowerSourceActionOption(action, "TargetHealthPower", report, source);
            ValidateDirectionActionOption(action, "OriginDirection", report, source);
            ValidateDirectionActionOption(action, "OffsetDirection", report, source);
            ValidateDirectionActionOption(action, "PushDirection", report, source);
            ValidateMonsterFlagActionOption(action, report, source);
            ValidateMonsterFlagPoolActionOption(action, "MonsterFlags", report, source);
            ValidateMonsterFlagPoolActionOption(action, "SpawnPool", report, source);
        }

        private static void ValidateKnownAiActionOptionNames(CustomMonsterAiActionInfo action, CustomSystemTextReport report, string source)
        {
            bool allowsActionReferences = action.Action is CustomAiActionType.WeightedChoice
                or CustomAiActionType.Sequence
                or CustomAiActionType.ScheduleAction;

            foreach (string token in SplitActionOptionTokens(action.Options, splitBareItems: allowsActionReferences))
            {
                string[] parts = token.Split('=', 2, StringSplitOptions.TrimEntries);
                if (parts.Length != 2)
                {
                    if (!allowsActionReferences)
                        report.Failures.Add($"{source}: AIAction `{action.Name}` 的 Options 项 `{token}` 必须写成 key=value。");

                    continue;
                }

                if (!KnownActionOptions.Contains(parts[0]))
                    report.Failures.Add($"{source}: AIAction `{action.Name}` 的 Options 包含未知选项 `{parts[0]}`。");
            }
        }

        private static void ValidateAiActionFields(CustomMonsterAiActionInfo action, CustomSystemTextReport report, string source)
        {
            if (action == null) return;

            ValidateNonNegative(action.Radius, "Radius", action, report, source);
            ValidateNonNegative(action.Distance, "Distance", action, report, source);
            ValidateNonNegative(action.Delay, "Delay", action, report, source);
            ValidatePercent(action.TargetChance, "TargetChance", action, report, source);
            ValidatePercent(action.EmptyLocationChance, "EmptyLocationChance", action, report, source);
            ValidateNonNegative(action.MaxTargets, "MaxTargets", action, report, source);
            ValidateNonNegative(action.RandomTargetCount, "RandomTargetCount", action, report, source);
            ValidateNonNegative(action.FixedCount, "FixedCount", action, report, source);
            ValidateNonNegative(action.RandomCount, "RandomCount", action, report, source);
            ValidateNonNegative(action.MinionDuration, "MinionDuration", action, report, source);
            ValidateNonNegative(action.PowerRate, "PowerRate", action, report, source);
            ValidateNonNegative(action.Duration, "Duration", action, report, source);
            ValidateNonNegative(action.TickRate, "TickRate", action, report, source);

            if (action.Action == CustomAiActionType.ApplyPoison && action.PoisonType == PoisonType.None)
                report.Failures.Add($"{source}: AIAction `{action.Name}` 使用 ApplyPoison 时必须配置 PoisonType。");

            if (action.Action is CustomAiActionType.AddBuff or CustomAiActionType.RemoveBuff && action.BuffType == BuffType.None)
                report.Failures.Add($"{source}: AIAction `{action.Name}` 使用 {action.Action} 时必须配置 BuffType。");
        }

        private static void ValidateNonNegative(int value, string key, CustomMonsterAiActionInfo action, CustomSystemTextReport report, string source)
        {
            if (value < 0)
                report.Failures.Add($"{source}: AIAction `{action.Name}` 的 {key} 不能小于 0。");
        }

        private static void ValidatePercent(int value, string key, CustomMonsterAiActionInfo action, CustomSystemTextReport report, string source)
        {
            if (value < 0 || value > 100)
                report.Failures.Add($"{source}: AIAction `{action.Name}` 的 {key} 必须在 0..100 范围内。");
        }

        private static void ValidateNonNegativeActionOption(CustomMonsterAiActionInfo action, string optionName, CustomSystemTextReport report, string source)
        {
            string value = GetActionOptionValue(action.Options, optionName);
            if (string.IsNullOrWhiteSpace(value)) return;

            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) || parsed < 0)
                report.Failures.Add($"{source}: AIAction `{action.Name}` 的 {optionName} `{value}` 必须是非负整数。");
        }

        private static void ValidatePercentActionOption(CustomMonsterAiActionInfo action, string optionName, CustomSystemTextReport report, string source)
        {
            string value = GetActionOptionValue(action.Options, optionName);
            if (string.IsNullOrWhiteSpace(value)) return;

            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) || parsed < 0 || parsed > 100)
                report.Failures.Add($"{source}: AIAction `{action.Name}` 的 {optionName} `{value}` 必须是 0..100 的整数。");
        }

        private static void ValidateBoolActionOption(CustomMonsterAiActionInfo action, string optionName, CustomSystemTextReport report, string source)
        {
            string value = GetActionOptionValue(action.Options, optionName);
            if (string.IsNullOrWhiteSpace(value)) return;

            if (TryParseTextBool(value, out _)) return;

            report.Failures.Add($"{source}: AIAction `{action.Name}` 的 {optionName} `{value}` 必须是 True/False/1/0/yes/no。");
        }

        private static void ValidateBuffStatsScaleOption(CustomMonsterAiActionInfo action, string optionName, CustomSystemTextReport report, string source)
        {
            string value = GetActionOptionValue(action.Options, optionName);
            if (string.IsNullOrWhiteSpace(value)) return;

            if (action.Action != CustomAiActionType.AddBuff)
                report.Failures.Add($"{source}: AIAction `{action.Name}` 配置 {optionName} 时 Type 应为 AddBuff。");

            foreach (string token in value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string[] rateParts = token.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                string sourceStat;
                string destinationStat;
                string rateValue;

                if (rateParts.Length == 3)
                {
                    sourceStat = rateParts[0];
                    destinationStat = rateParts[1];
                    rateValue = rateParts[2];
                }
                else if (rateParts.Length == 2)
                {
                    string[] statParts = rateParts[0].Split('>', 2, StringSplitOptions.TrimEntries);
                    if (statParts.Length != 2)
                    {
                        report.Failures.Add($"{source}: AIAction `{action.Name}` 的 {optionName} 项 `{token}` 必须写成 来源Stat>目标Stat:倍率 或 来源Stat:目标Stat:倍率。");
                        continue;
                    }

                    sourceStat = statParts[0];
                    destinationStat = statParts[1];
                    rateValue = rateParts[1];
                }
                else
                {
                    report.Failures.Add($"{source}: AIAction `{action.Name}` 的 {optionName} 项 `{token}` 必须写成 来源Stat>目标Stat:倍率 或 来源Stat:目标Stat:倍率。");
                    continue;
                }

                if (!Enum.TryParse(sourceStat, true, out Stat parsedSource) || parsedSource == Stat.None)
                    report.Failures.Add($"{source}: AIAction `{action.Name}` 的 {optionName} 来源 Stat `{sourceStat}` 无效。");

                if (!Enum.TryParse(destinationStat, true, out Stat parsedDestination) || parsedDestination == Stat.None)
                    report.Failures.Add($"{source}: AIAction `{action.Name}` 的 {optionName} 目标 Stat `{destinationStat}` 无效。");

                if (!int.TryParse(rateValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int rate) || rate < 0)
                    report.Failures.Add($"{source}: AIAction `{action.Name}` 的 {optionName} 倍率 `{rateValue}` 必须是非负整数。");
            }
        }

        private static void ValidateBuffTypeActionOption(CustomMonsterAiActionInfo action, string optionName, CustomSystemTextReport report, string source)
        {
            string value = GetActionOptionValue(action.Options, optionName);
            if (string.IsNullOrWhiteSpace(value)) return;

            if (!Enum.TryParse(value, true, out BuffType parsed) || parsed == BuffType.None)
                report.Failures.Add($"{source}: AIAction `{action.Name}` 的 {optionName} `{value}` 不是有效 BuffType。");
        }

        private static void ValidatePowerSourceActionOption(CustomMonsterAiActionInfo action, string optionName, CustomSystemTextReport report, string source)
        {
            string value = GetActionOptionValue(action.Options, optionName);
            if (string.IsNullOrWhiteSpace(value)) return;

            if (!Enum.TryParse(value, true, out CustomAiPowerSource _))
                report.Failures.Add($"{source}: AIAction `{action.Name}` 的 {optionName} `{value}` 不是有效 PowerSource。");
        }

        private static void ValidateDirectionActionOption(CustomMonsterAiActionInfo action, string optionName, CustomSystemTextReport report, string source)
        {
            string value = GetActionOptionValue(action.Options, optionName);
            if (string.IsNullOrWhiteSpace(value)) return;

            if (DynamicDirectionOptions.Contains(value)) return;
            if (Enum.TryParse(value, true, out MirDirection _)) return;

            report.Failures.Add($"{source}: AIAction `{action.Name}` 的 {optionName} `{value}` 不是有效方向。可用 MirDirection、Target、CurrentTarget、AwayFromTarget。");
        }

        private static void ValidateMonsterFlagActionOption(CustomMonsterAiActionInfo action, CustomSystemTextReport report, string source)
        {
            string flagName = GetActionOptionValue(action.Options, "MonsterFlag");
            if (string.IsNullOrWhiteSpace(flagName))
                flagName = GetActionOptionValue(action.Options, "Flag");

            if (!string.IsNullOrWhiteSpace(flagName) && !Enum.TryParse(flagName, true, out MonsterFlag _))
                report.Failures.Add($"{source}: AIAction `{action.Name}` 的 MonsterFlag `{flagName}` 不是有效 MonsterFlag 枚举。");
        }

        private static void ValidateMonsterFlagPoolActionOption(CustomMonsterAiActionInfo action, string optionName, CustomSystemTextReport report, string source)
        {
            string value = GetActionOptionValue(action.Options, optionName);
            if (string.IsNullOrWhiteSpace(value)) return;

            foreach (string token in value.Split(['|', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string[] parts = token.Split(':', 2, StringSplitOptions.TrimEntries);
                if (!Enum.TryParse(parts[0], true, out MonsterFlag flag) || flag == MonsterFlag.None)
                {
                    report.Failures.Add($"{source}: AIAction `{action.Name}` 的 {optionName} 项 `{token}` 不是有效 MonsterFlag 枚举。");
                    continue;
                }

                if (parts.Length == 2 && (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int weight) || weight <= 0))
                    report.Failures.Add($"{source}: AIAction `{action.Name}` 的 {optionName} 项 `{token}` 权重必须大于 0。");
            }
        }

        private static void ValidateBroadcastMessageAction(CustomMonsterAiActionInfo action, CustomSystemTextReport report, string source)
        {
            string message = GetActionOptionValue(action.Options, "Message");
            if (string.IsNullOrWhiteSpace(message))
                message = GetActionOptionValue(action.Options, "Text");
            if (string.IsNullOrWhiteSpace(message))
                message = GetActionOptionValue(action.Options, "Format");

            if (string.IsNullOrWhiteSpace(message))
                report.Failures.Add($"{source}: AIAction `{action.Name}` 使用 BroadcastMessage 时必须配置 Options=Message=...。");

            string messageType = GetActionOptionValue(action.Options, "MessageType");
            if (!string.IsNullOrWhiteSpace(messageType) && !Enum.TryParse(messageType, true, out MessageType _))
                report.Failures.Add($"{source}: AIAction `{action.Name}` 的 MessageType `{messageType}` 不是有效 MessageType 枚举。");

            string scope = GetActionOptionValue(action.Options, "Scope");
            if (string.IsNullOrWhiteSpace(scope)) return;

            switch (scope.Trim().ToUpperInvariant())
            {
                case "ALL":
                case "MAP":
                case "RADIUS":
                case "NEARBY":
                    return;
                default:
                    report.Failures.Add($"{source}: AIAction `{action.Name}` 的 Scope `{scope}` 无效，可用 All、Map、Radius、Nearby。");
                    return;
            }
        }

        private static void ValidateTeleportTargetsToRegionAction(CustomMonsterAiActionInfo action, CustomSystemTextReport report, string source)
        {
            bool hasRegion =
                !string.IsNullOrWhiteSpace(GetActionOptionValue(action.Options, "SpecialRegion")) ||
                !string.IsNullOrWhiteSpace(GetActionOptionValue(action.Options, "RegionIndex")) ||
                !string.IsNullOrWhiteSpace(GetActionOptionValue(action.Options, "Region")) ||
                !string.IsNullOrWhiteSpace(GetActionOptionValue(action.Options, "RegionDescription")) ||
                !string.IsNullOrWhiteSpace(GetActionOptionValue(action.Options, "RegionName"));

            if (!hasRegion)
                report.Failures.Add($"{source}: AIAction `{action.Name}` 使用 TeleportTargetsToRegion 时必须配置 SpecialRegion、RegionIndex、RegionDescription 或 RegionName。");
        }

        private static void ValidateRecallMonstersByFlagAction(CustomMonsterAiActionInfo action, CustomSystemTextReport report, string source)
        {
            string flagName = GetActionOptionValue(action.Options, "MonsterFlag");
            if (string.IsNullOrWhiteSpace(flagName))
                flagName = GetActionOptionValue(action.Options, "Flag");

            if (action.SpawnMonster == null && string.IsNullOrWhiteSpace(flagName))
                report.Failures.Add($"{source}: AIAction `{action.Name}` 使用 RecallMonstersByFlag 时必须配置 SpawnMonster 或 Options=MonsterFlag=...。");

            string directionName = GetActionOptionValue(action.Options, "OriginDirection");
            if (string.IsNullOrWhiteSpace(directionName))
                directionName = GetActionOptionValue(action.Options, "OffsetDirection");

            if (!string.IsNullOrWhiteSpace(directionName) &&
                !DynamicDirectionOptions.Contains(directionName) &&
                !Enum.TryParse(directionName, true, out MirDirection _))
                report.Failures.Add($"{source}: AIAction `{action.Name}` 的 OriginDirection `{directionName}` 不是有效方向。可用 MirDirection、Target、CurrentTarget、AwayFromTarget。");
        }

        private static void ValidateCustomSkillReference(
            MagicInfo magic,
            CustomAiTargetSelectorType target,
            string context,
            Dictionary<string, CustomSkillInfo> customSkillsByMagic,
            CustomSystemTextReport report,
            string source)
        {
            if (magic == null) return;

            if (!TryGetCustomSkill(customSkillsByMagic, magic, out CustomSkillInfo skill) || skill == null || !skill.Enabled)
            {
                report.Failures.Add($"{source}: {context} 引用的技能 `{magic.Name}` 没有启用的 CustomSkillInfo，运行时无法由自定义技能执行器释放。");
                return;
            }

            ValidateCustomSkillTarget(skill, target, context, report, source);
        }

        private static bool TryGetCustomSkill(Dictionary<string, CustomSkillInfo> customSkillsByMagic, MagicInfo magic, out CustomSkillInfo skill)
        {
            skill = null;
            if (customSkillsByMagic == null || magic == null) return false;

            if (magic.Index > 0 && customSkillsByMagic.TryGetValue($"index:{magic.Index}", out skill))
                return true;

            if (!string.IsNullOrWhiteSpace(magic.Name) && customSkillsByMagic.TryGetValue($"name:{Normalize(magic.Name)}", out skill))
                return true;

            return false;
        }

        private static void ValidateCustomSkillTarget(CustomSkillInfo skill, CustomAiTargetSelectorType target, string context, CustomSystemTextReport report, string source)
        {
            if (skill == null) return;

            bool allySelector = IsAllyTargetSelector(target);
            bool enemySelector = IsEnemyTargetSelector(target);
            bool selfSelector = target == CustomAiTargetSelectorType.Self;
            bool locationPayload = SkillUsesLocationPayload(skill);

            if (skill.Template is CustomSkillTemplate.Heal or CustomSkillTemplate.Buff)
            {
                if (enemySelector)
                    report.Failures.Add($"{source}: {context} 是治疗或辅助技能，但 Target={target} 会选择敌对目标。请改用 Self、AllyRadius、RandomAllyInView、NearestAllyInView、LowestHealthAllyInView、Summoner 或 SummonerGroup。");

                if (skill.TargetRule == CustomSkillTargetRule.Ally && !allySelector && !selfSelector)
                    report.Failures.Add($"{source}: {context} 的 TargetRule=Ally，但 Target={target} 不能稳定选中友方目标。");

                return;
            }

            if (locationPayload)
                return;

            if (skill.TargetRule is CustomSkillTargetRule.Enemy or CustomSkillTargetRule.EnemyOrGround && (allySelector || selfSelector))
                report.Failures.Add($"{source}: {context} 是敌对技能，但 Target={target} 会选择友方或自己。");

            if (skill.TargetRule == CustomSkillTargetRule.Ally && enemySelector)
                report.Failures.Add($"{source}: {context} 是友方技能，但 Target={target} 会选择敌对目标。");
        }

        private static void ValidateAnimationVariant(int variant, string key, CustomSystemTextReport report, string source)
        {
            if (variant < 0 || variant > 8)
                report.Failures.Add($"{source}: `{key}` 必须在 0..8 范围内，0 表示默认动作，1..8 对应 Combat1..Combat8。");
        }

        private static void ValidateSkillFields(CustomSkillInfo skill, CustomSystemTextReport report, string source)
        {
            if (skill == null) return;

            ValidateNonNegative(skill.Range, "Range", "[Skill]", report, source);
            ValidateNonNegative(skill.Radius, "Radius", "[Skill]", report, source);
            ValidateNonNegative(skill.MaxTargets, "MaxTargets", "[Skill]", report, source);
            ValidateNonNegative(skill.Length, "Length", "[Skill]", report, source);
            ValidateNonNegative(skill.Width, "Width", "[Skill]", report, source);
            ValidateNonNegative(skill.Angle, "Angle", "[Skill]", report, source);
            ValidatePositive(skill.WaveCount, "WaveCount", "[Skill]", report, source);
            ValidateNonNegative(skill.WaveInterval, "WaveInterval", "[Skill]", report, source);
            ValidateNonNegative(skill.KnockbackDistance, "KnockbackDistance", "[Skill]", report, source);
            ValidateNonNegative(skill.PullDistance, "PullDistance", "[Skill]", report, source);
            ValidateNonNegative(skill.PowerRate, "PowerRate", "[Skill]", report, source);
            ValidateNonNegative(skill.Duration, "Duration", "[Skill]", report, source);
            ValidateNonNegative(skill.TickRate, "TickRate", "[Skill]", report, source);
            ValidateNonNegative(skill.CastDelay, "CastDelay", "[Skill]", report, source);
            ValidateNonNegative(skill.ProjectileSpeed, "ProjectileSpeed", "[Skill]", report, source);
            ValidatePositive(skill.MaxSummons, "MaxSummons", "[Skill]", report, source);
            ValidateNonNegative(skill.SummonDuration, "SummonDuration", "[Skill]", report, source);

            if (skill.Template is CustomSkillTemplate.Summon or CustomSkillTemplate.DamageProxy && skill.SummonMonster == null)
                report.Failures.Add($"{source}: [Skill] Template={skill.Template} 时必须配置 SummonMonster。");

            if (skill.Template == CustomSkillTemplate.DamageProxy &&
                skill.TargetRule is CustomSkillTargetRule.Ally or CustomSkillTargetRule.AllyOrSelf or CustomSkillTargetRule.Self)
                report.Failures.Add($"{source}: [Skill] Template=DamageProxy 时 TargetRule 必须是 Enemy 或 EnemyOrGround。");

            if (skill.Template == CustomSkillTemplate.Buff && skill.BuffType == BuffType.None)
                report.Failures.Add($"{source}: [Skill] Template=Buff 时必须配置 BuffType。");
        }

        private static void ValidateDamageProxySkill(CustomSkillInfo skill, Dictionary<string, bool> damageProxyMonsters, CustomSystemTextReport report, string source)
        {
            if (skill?.Template != CustomSkillTemplate.DamageProxy || skill.SummonMonster == null) return;

            if (IsDamageProxyMonster(skill.SummonMonster, damageProxyMonsters)) return;

            report.Failures.Add($"{source}: [Skill] Template=DamageProxy 的 SummonMonster `{skill.SummonMonster.MonsterName}` 必须配置 [Behavior] DamageProxy=True。");
        }

        private static bool IsDamageProxyMonster(MonsterInfo monster, Dictionary<string, bool> damageProxyMonsters)
        {
            if (monster == null || damageProxyMonsters == null) return false;

            if (monster.Index > 0 && damageProxyMonsters.TryGetValue($"index:{monster.Index}", out bool byIndex))
                return byIndex;

            return !string.IsNullOrWhiteSpace(monster.MonsterName) &&
                   damageProxyMonsters.TryGetValue($"name:{Normalize(monster.MonsterName)}", out bool byName) &&
                   byName;
        }

        private static void ValidateFrameSegment(string context, int startIndex, int endIndex, int frameCount, CustomSystemTextReport report, string source)
        {
            if (startIndex < 0)
                report.Failures.Add($"{source}: {context} 的 Start 不能小于 0。");

            if (endIndex < 0)
                report.Failures.Add($"{source}: {context} 的 End 不能小于 0。");

            if (frameCount < 0)
                report.Failures.Add($"{source}: {context} 的 FrameCount 不能小于 0。");

            if (frameCount == 0 && endIndex < startIndex)
                report.Failures.Add($"{source}: {context} 未配置 FrameCount 时 End 必须大于等于 Start。");

            if (GetEffectiveFrameCount(startIndex, endIndex, frameCount) <= 0)
                report.Failures.Add($"{source}: {context} 的有效帧数必须大于 0。");
        }

        private static int GetEffectiveFrameCount(int startIndex, int endIndex, int frameCount)
        {
            return frameCount > 0 ? frameCount : endIndex - startIndex + 1;
        }

        private static void ValidatePositive(int value, string key, string context, CustomSystemTextReport report, string source)
        {
            if (value <= 0)
                report.Failures.Add($"{source}: {context} 的 {key} 必须大于 0。");
        }

        private static void ValidateNonNegative(int value, string key, string context, CustomSystemTextReport report, string source)
        {
            if (value < 0)
                report.Failures.Add($"{source}: {context} 的 {key} 不能小于 0。");
        }

        private static bool SkillUsesLocationPayload(CustomSkillInfo skill)
        {
            return skill?.Template is CustomSkillTemplate.AreaDamage
                or CustomSkillTemplate.PersistentAreaDamage
                or CustomSkillTemplate.Move
                or CustomSkillTemplate.Summon;
        }

        private static bool IsAllyTargetSelector(CustomAiTargetSelectorType target)
        {
            return target is CustomAiTargetSelectorType.AllyRadius
                or CustomAiTargetSelectorType.RandomAllyInView
                or CustomAiTargetSelectorType.NearestAllyInView
                or CustomAiTargetSelectorType.LowestHealthAllyInView
                or CustomAiTargetSelectorType.Summoner
                or CustomAiTargetSelectorType.SummonerGroup;
        }

        private static bool RuleConditionRequiresBuff(CustomAiConditionType condition)
        {
            return condition is CustomAiConditionType.SelfHasBuff
                or CustomAiConditionType.SelfMissingBuff
                or CustomAiConditionType.TargetHasBuff
                or CustomAiConditionType.TargetMissingBuff;
        }

        private static bool RuleConditionRequiresCounter(CustomAiConditionType condition)
        {
            return condition is CustomAiConditionType.CounterBelow
                or CustomAiConditionType.CounterAbove
                or CustomAiConditionType.CounterEquals;
        }

        private static void ValidateAdditionalRuleConditions(CustomMonsterAiRuleInfo rule, CustomSystemTextReport report, string source)
        {
            if (string.IsNullOrWhiteSpace(rule?.Conditions)) return;

            foreach (string token in SplitConditionTokens(rule.Conditions))
            {
                string[] parts = token.TrimStart('!').Split(':', StringSplitOptions.TrimEntries);
                string name = parts[0];
                if (!Enum.TryParse(name, true, out CustomAiConditionType condition))
                {
                    report.Failures.Add($"{source}: AIRule `{rule.Name}` 的 Conditions 包含未知条件 `{name}`。");
                    continue;
                }

                ValidateRuleConditionArguments(rule, condition, parts, report, source);
            }
        }

        private static void ValidateRuleConditionArguments(CustomMonsterAiRuleInfo rule, CustomAiConditionType condition, string[] parts, CustomSystemTextReport report, string source)
        {
            if (RuleConditionRequiresBuff(condition))
            {
                if (ConditionPartHasValue(parts, 1))
                {
                    string buffName = parts[1];
                    if (!Enum.TryParse(buffName, true, out BuffType parsed) || parsed == BuffType.None)
                        report.Failures.Add($"{source}: AIRule `{rule.Name}` 的 Conditions 条件 `{condition}` Buff 参数 `{buffName}` 无效。");
                }
                else if (rule.BuffType == BuffType.None)
                {
                    report.Failures.Add($"{source}: AIRule `{rule.Name}` 的 Conditions 条件 `{condition}` 必须在条件参数或 BuffType 中指定 Buff。");
                }

                return;
            }

            if (RuleConditionRequiresCounter(condition))
            {
                bool hasCounterName = !string.IsNullOrWhiteSpace(rule.CounterName);

                if (ConditionPartHasValue(parts, 1) &&
                    !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                    hasCounterName = true;

                if (ConditionPartHasValue(parts, 2) &&
                    !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                    hasCounterName = true;

                if (ConditionPartHasValue(parts, 3))
                    hasCounterName = true;

                if (ConditionPartHasValue(parts, 1) &&
                    ConditionPartHasValue(parts, 2) &&
                    int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out _) &&
                    int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                {
                    report.Failures.Add($"{source}: AIRule `{rule.Name}` 的 Conditions 条件 `{condition}` 不能同时把 `{parts[1]}` 和 `{parts[2]}` 都作为数值参数，必须至少提供一个计数器名。");
                }

                if (!hasCounterName)
                    report.Failures.Add($"{source}: AIRule `{rule.Name}` 的 Conditions 条件 `{condition}` 必须在条件参数或 CounterName 中指定计数器名。");

                return;
            }

            if (RuleConditionUsesNumericValue(condition) && ConditionPartHasValue(parts, 1) &&
                !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                report.Failures.Add($"{source}: AIRule `{rule.Name}` 的 Conditions 条件 `{condition}` 数值参数 `{parts[1]}` 必须是整数。");

            if (RuleConditionUsesRadius(condition) && ConditionPartHasValue(parts, 2) &&
                !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                report.Failures.Add($"{source}: AIRule `{rule.Name}` 的 Conditions 条件 `{condition}` 半径参数 `{parts[2]}` 必须是整数。");
        }

        private static IEnumerable<string> SplitConditionTokens(string conditions)
        {
            return conditions
                .Replace("&&", ";", StringComparison.Ordinal)
                .Replace("||", ";", StringComparison.Ordinal)
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        private static bool ConditionPartHasValue(string[] parts, int partIndex)
        {
            return parts.Length > partIndex && !string.IsNullOrWhiteSpace(parts[partIndex]);
        }

        private static bool RuleConditionUsesNumericValue(CustomAiConditionType condition)
        {
            return condition is CustomAiConditionType.HealthBelow
                or CustomAiConditionType.HealthAbove
                or CustomAiConditionType.TargetHealthBelow
                or CustomAiConditionType.TargetHealthAbove
                or CustomAiConditionType.TargetCountAtLeast
                or CustomAiConditionType.TargetDistanceBelow
                or CustomAiConditionType.TargetDistanceAbove
                or CustomAiConditionType.TargetInLine
                or CustomAiConditionType.TargetOutOfLine
                or CustomAiConditionType.MinionCountBelow
                or CustomAiConditionType.MinionCountAbove;
        }

        private static bool RuleConditionUsesRadius(CustomAiConditionType condition)
        {
            return condition is CustomAiConditionType.TargetCountAtLeast;
        }

        private static bool IsEnemyTargetSelector(CustomAiTargetSelectorType target)
        {
            return target is CustomAiTargetSelectorType.RandomInView
                or CustomAiTargetSelectorType.NearestInView
                or CustomAiTargetSelectorType.FarthestInView
                or CustomAiTargetSelectorType.LowestHealthInView
                or CustomAiTargetSelectorType.HighestHealthInView
                or CustomAiTargetSelectorType.Line;
        }

        private void ApplySkill(CustomSkillInfo skill, MagicInfo magic, Dictionary<string, string> values, CustomSystemTextReport report, string source)
        {
            bool isNew = skill.Magic == null;

            skill.Magic = magic;
            skill.Enabled = GetBool(values, "Enabled", isNew ? true : skill.Enabled);
            skill.Template = GetEnum(values, "Template", CustomSkillTemplate.DirectDamage, report, source);
            skill.TargetRule = GetEnum(values, "TargetRule", CustomSkillTargetRule.Enemy, report, source);
            skill.Range = GetInt(values, "Range", skill.Range == 0 ? Globals.MagicRange : skill.Range);
            skill.Radius = GetInt(values, "Radius", skill.Radius);
            skill.MaxTargets = GetInt(values, "MaxTargets", skill.MaxTargets);
            skill.Shape = GetEnum(values, "Shape", skill.Shape, report, source);
            skill.Length = GetInt(values, "Length", skill.Length);
            skill.Width = GetInt(values, "Width", skill.Width);
            skill.Angle = GetInt(values, "Angle", skill.Angle);
            skill.WaveCount = GetInt(values, "WaveCount", GetInt(values, "Waves", skill.WaveCount == 0 ? 1 : skill.WaveCount));
            skill.WaveInterval = GetInt(values, "WaveInterval", skill.WaveInterval);
            skill.KnockbackDistance = GetInt(values, "KnockbackDistance", GetInt(values, "PushDistance", skill.KnockbackDistance));
            skill.PullDistance = GetInt(values, "PullDistance", skill.PullDistance);
            skill.Element = GetEnum(values, "Element", skill.Element, report, source);
            skill.PowerSource = GetEnum(values, "PowerSource", GetEnum(values, "Power", skill.PowerSource, report, source), report, source);
            skill.IgnoreShield = GetBool(values, "IgnoreShield", skill.IgnoreShield);
            skill.IgnoreArmour = GetBool(values, "IgnoreArmour", skill.IgnoreArmour);
            skill.FixedPower = GetInt(values, "FixedPower", skill.FixedPower);
            skill.PowerRate = GetInt(values, "PowerRate", skill.PowerRate == 0 ? 100 : skill.PowerRate);
            skill.Duration = GetInt(values, "Duration", skill.Duration);
            skill.DurationPowerRate = GetInt(values, "DurationPowerRate", skill.DurationPowerRate);
            skill.TickRate = GetInt(values, "TickRate", skill.TickRate == 0 ? 1000 : skill.TickRate);
            skill.BuffType = GetEnum(values, "BuffType", skill.BuffType, report, source);
            skill.PoisonType = GetEnum(values, "PoisonType", skill.PoisonType, report, source);
            skill.PoisonValue = GetInt(values, "PoisonValue", skill.PoisonValue);
            skill.PoisonCanKill = GetBool(values, "PoisonCanKill", skill.PoisonCanKill);
            skill.BuffStats = GetStats(values, "BuffStats", skill.BuffStats, report, source);
            skill.BuffVisible = GetBool(values, "BuffVisible", skill.BuffVisible);
            skill.BuffPause = GetBool(values, "BuffPause", skill.BuffPause);
            skill.BuffHidden = GetBool(values, "BuffHidden", skill.BuffHidden);
            skill.BuffExtra = GetInt(values, "BuffExtra", skill.BuffExtra);
            skill.CastDelay = GetInt(values, "CastDelay", skill.CastDelay == 0 ? 500 : skill.CastDelay);
            skill.ProjectileSpeed = GetInt(values, "ProjectileSpeed", skill.ProjectileSpeed == 0 ? Globals.ProjectileSpeed : skill.ProjectileSpeed);
            skill.SummonMonster = ResolveMonsterReference(values, "SummonMonster", report, source);
            skill.MaxSummons = GetInt(values, "MaxSummons", skill.MaxSummons == 0 ? 1 : skill.MaxSummons);
            skill.SummonDuration = GetInt(values, "SummonDuration", skill.SummonDuration);

            ValidateSkillFields(skill, report, source);
        }

        private static void ApplySkillEffect(CustomSkillEffectInfo effect, Dictionary<string, string> values, Dictionary<string, CustomResourceLibraryInfo> resources, CustomSystemTextReport report, string source, bool isNew)
        {
            effect.Resource = ResolveResource(values, "Resource", resources, report, source);
            effect.Stage = GetEnum(values, "Stage", CustomSkillEffectStage.Cast, report, source);
            effect.StartIndex = GetInt(values, "Start", GetInt(values, "StartIndex", effect.StartIndex));
            effect.EndIndex = GetInt(values, "End", GetInt(values, "EndIndex", effect.EndIndex));
            effect.FrameCount = GetInt(values, "FrameCount", effect.FrameCount);
            effect.FrameDelay = GetInt(values, "FrameDelay", effect.FrameDelay == 0 ? 100 : effect.FrameDelay);
            effect.OffsetX = GetInt(values, "OffsetX", effect.OffsetX);
            effect.OffsetY = GetInt(values, "OffsetY", effect.OffsetY);
            effect.StartLight = GetInt(values, "StartLight", effect.StartLight);
            effect.EndLight = GetInt(values, "EndLight", effect.EndLight);
            effect.Sound = GetEnum(values, "Sound", effect.Sound, report, source);
            effect.Blend = GetBool(values, "Blend", effect.Blend);
            effect.Loop = GetBool(values, "Loop", effect.Loop);
            effect.Duration = GetInt(values, "Duration", effect.Duration);
            effect.FollowTarget = GetBool(values, "FollowTarget", isNew ? true : effect.FollowTarget);

            if (effect.Resource != null)
            {
                ValidateFrameSegment($"[Effect] `{effect.Stage}`", effect.StartIndex, effect.EndIndex, effect.FrameCount, report, source);
                ValidatePositive(effect.FrameDelay, "FrameDelay", $"[Effect] `{effect.Stage}`", report, source);
            }

            if (effect.Duration < 0)
                report.Failures.Add($"{source}: [Effect] `{effect.Stage}` 的 Duration 不能小于 0。");
        }

        private MagicInfo ResolveMagicReference(Dictionary<string, string> values, string key, CustomSystemTextReport report, string source)
        {
            string value = GetValue(values, key, null);
            if (string.IsNullOrWhiteSpace(value)) return null;

            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) && _magicsByIndex.TryGetValue(index, out MagicInfo byIndex))
                return byIndex;

            if (_magics.TryGetValue(Normalize(value), out MagicInfo byName))
                return byName;

            report.MissingMagics.Add(value);
            report.Failures.Add($"{source}: 找不到技能 `{value}`。");
            return null;
        }

        private MonsterInfo ResolveMonsterReference(Dictionary<string, string> values, string key, CustomSystemTextReport report, string source)
        {
            string value = GetValue(values, key, null);
            if (string.IsNullOrWhiteSpace(value)) return null;

            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) && _monstersByIndex.TryGetValue(index, out MonsterInfo byIndex))
                return byIndex;

            if (_monsters.TryGetValue(Normalize(value), out MonsterInfo byName))
                return byName;

            report.MissingMonsters.Add(value);
            report.Failures.Add($"{source}: 找不到怪物 `{value}`。");
            return null;
        }

        private static CustomResourceLibraryInfo ResolveResource(Dictionary<string, string> values, string key, Dictionary<string, CustomResourceLibraryInfo> resources, CustomSystemTextReport report, string source)
        {
            string value = GetValue(values, key, null);
            if (string.IsNullOrWhiteSpace(value)) return null;

            if (resources.TryGetValue(Normalize(value), out CustomResourceLibraryInfo resource))
                return resource;

            report.MissingResources.Add(value);
            report.Failures.Add($"{source}: 找不到自定义资源 `{value}`。");
            return null;
        }

        private static CustomMonsterVisualInfo CreateDetachedCustomMonsterVisual()
        {
            CustomMonsterVisualInfo visual = new()
            {
                Scale = 1F,
                DrawShadow = true,
                KeepCorpse = true,
                DefaultFrameDelay = 100,
            };

            EnsureVisualChildren(visual);
            return visual;
        }

        private static void EnsureVisualChildren(CustomMonsterVisualInfo visual)
        {
            if (visual == null || visual.Animations != null) return;

            visual.Animations = new DBBindingList<CustomMonsterAnimationInfo>(
                visual,
                typeof(CustomMonsterVisualInfo).GetProperty(nameof(CustomMonsterVisualInfo.Animations)));
        }

        private static CustomMonsterAnimationInfo CreateDetachedCustomMonsterAnimation()
        {
            return new CustomMonsterAnimationInfo
            {
                DirectionCount = 8,
                DirectionOffset = 10,
                FrameDelay = 100,
                EffectFrameDelay = 100,
                EffectTriggerFrame = -1,
            };
        }

        private static CustomMonsterBehaviorInfo CreateDetachedCustomMonsterBehavior()
        {
            CustomMonsterBehaviorInfo behavior = new()
            {
                Template = CustomMonsterBehaviorTemplate.Melee,
                CanBeKilled = true,
                CanAttackPlayers = true,
                HiddenBlocks = true,
                HiddenCanAttack = true,
                HiddenCanBeAttacked = true,
                ProtectionCheckInterval = 1000,
                AttackRange = 1,
                ChaseRange = 12,
                ReturnRange = 16,
                MaxMinions = 20,
                SkillChance = 25,
                RoamDelay = 2000,
                DamageProxyRevealDelay = 500,
                DamageProxySpawnEffect = Effect.CursedDoll,
                DamageProxyNameFormat = "{Target} Doll",
                PoisonRate = 10,
                PoisonTicks = 5,
                PoisonFrequency = 2,
                UseBaseFallback = true,
            };

            EnsureBehaviorChildren(behavior);
            return behavior;
        }

        private static void EnsureBehaviorChildren(CustomMonsterBehaviorInfo behavior)
        {
            if (behavior == null) return;

            behavior.Skills ??= new DBBindingList<CustomMonsterSkillInfo>(
                behavior,
                typeof(CustomMonsterBehaviorInfo).GetProperty(nameof(CustomMonsterBehaviorInfo.Skills)));

            behavior.Rules ??= new DBBindingList<CustomMonsterAiRuleInfo>(
                behavior,
                typeof(CustomMonsterBehaviorInfo).GetProperty(nameof(CustomMonsterBehaviorInfo.Rules)));

            behavior.Actions ??= new DBBindingList<CustomMonsterAiActionInfo>(
                behavior,
                typeof(CustomMonsterBehaviorInfo).GetProperty(nameof(CustomMonsterBehaviorInfo.Actions)));
        }

        private static CustomMonsterSkillInfo CreateDetachedCustomMonsterSkill()
        {
            return new CustomMonsterSkillInfo
            {
                Chance = 25,
                Cooldown = 3000,
                Range = 8,
                Target = CustomAiTargetSelectorType.CurrentTarget,
                MaxHealthPercent = 100,
            };
        }

        private static CustomMonsterAiRuleInfo CreateDetachedCustomMonsterAiRule()
        {
            return new CustomMonsterAiRuleInfo
            {
                Trigger = CustomAiTriggerType.OnTargetTick,
                Condition = CustomAiConditionType.Always,
                ConditionJoin = CustomAiConditionJoin.And,
                Chance = 100,
                MaxHealthPercent = 100,
            };
        }

        private static CustomMonsterAiActionInfo CreateDetachedCustomMonsterAiAction()
        {
            return new CustomMonsterAiActionInfo
            {
                Target = CustomAiTargetSelectorType.CurrentTarget,
                PowerSource = CustomAiPowerSource.DC,
                PowerRate = 100,
                Delay = 500,
                TargetChance = 100,
                Duration = 3000,
                TickRate = 1000,
                PoisonCanKill = true,
                BuffVisible = true,
            };
        }

        private static CustomSkillInfo CreateDetachedCustomSkill()
        {
            CustomSkillInfo skill = new()
            {
                Enabled = true,
                Template = CustomSkillTemplate.DirectDamage,
                TargetRule = CustomSkillTargetRule.Enemy,
                Range = Globals.MagicRange,
                Shape = CustomSkillShape.Circle,
                WaveCount = 1,
                PowerRate = 100,
                TickRate = 1000,
                MaxSummons = 1,
                BuffVisible = true,
                CastDelay = 500,
                ProjectileSpeed = Globals.ProjectileSpeed,
            };

            EnsureSkillChildren(skill);
            return skill;
        }

        private static void EnsureSkillChildren(CustomSkillInfo skill)
        {
            if (skill == null || skill.Effects != null) return;

            skill.Effects = new DBBindingList<CustomSkillEffectInfo>(
                skill,
                typeof(CustomSkillInfo).GetProperty(nameof(CustomSkillInfo.Effects)));
        }

        private static CustomSkillEffectInfo CreateDetachedCustomSkillEffect()
        {
            return new CustomSkillEffectInfo
            {
                FrameDelay = 100,
                FollowTarget = true,
            };
        }

        private CustomMonsterAnimationInfo ResolveAnimation(CustomMonsterVisualInfo visual, Dictionary<string, string> values, bool createDetached, bool replaceExisting)
        {
            if (createDetached || replaceExisting || visual?.Animations == null)
                return createDetached ? CreateDetachedCustomMonsterAnimation() : _session.GetCollection<CustomMonsterAnimationInfo>().CreateNewObject();

            CustomMonsterAnimationType action = GetEnum(values, "Action", CustomMonsterAnimationType.Standing);
            int variant = GetInt(values, "Variant", 0);

            return visual.Animations.FirstOrDefault(x => x != null && x.Action == action && x.Variant == variant)
                   ?? _session.GetCollection<CustomMonsterAnimationInfo>().CreateNewObject();
        }

        private CustomMonsterSkillInfo ResolveMonsterSkill(CustomMonsterBehaviorInfo behavior, Dictionary<string, string> values, bool createDetached, bool replaceExisting)
        {
            if (createDetached || replaceExisting || behavior?.Skills == null)
                return createDetached ? CreateDetachedCustomMonsterSkill() : _session.GetCollection<CustomMonsterSkillInfo>().CreateNewObject();

            MagicInfo magic = TryResolveMagicReference(values, "Magic");
            CustomAiTargetSelectorType target = GetEnum(values, "Target", CustomAiTargetSelectorType.CurrentTarget);
            int animationVariant = GetInt(values, "AnimationVariant", GetInt(values, "Variant", 0));

            return behavior.Skills.FirstOrDefault(x => x != null && x.Magic == magic && x.Target == target && x.AnimationVariant == animationVariant)
                   ?? _session.GetCollection<CustomMonsterSkillInfo>().CreateNewObject();
        }

        private CustomMonsterAiRuleInfo ResolveAiRule(CustomMonsterBehaviorInfo behavior, Dictionary<string, string> values, bool createDetached, bool replaceExisting)
        {
            if (createDetached || replaceExisting || behavior?.Rules == null)
                return createDetached ? CreateDetachedCustomMonsterAiRule() : _session.GetCollection<CustomMonsterAiRuleInfo>().CreateNewObject();

            string name = GetValue(values, "Name", null);
            if (!string.IsNullOrWhiteSpace(name))
            {
                CustomMonsterAiRuleInfo named = behavior.Rules.FirstOrDefault(x => x != null && Normalize(x.Name).Equals(Normalize(name), StringComparison.OrdinalIgnoreCase));
                if (named != null) return named;
            }

            CustomAiTriggerType trigger = GetEnum(values, "Trigger", CustomAiTriggerType.OnTargetTick);
            CustomAiConditionType condition = GetEnum(values, "Condition", CustomAiConditionType.Always);
            string actionName = GetValue(values, "Action", GetValue(values, "ActionName", null));
            int priority = GetInt(values, "Priority", 0);

            return behavior.Rules.FirstOrDefault(x =>
                       x != null &&
                       string.IsNullOrWhiteSpace(x.Name) &&
                       x.Trigger == trigger &&
                       x.Condition == condition &&
                       Normalize(x.ActionName).Equals(Normalize(actionName), StringComparison.OrdinalIgnoreCase) &&
                       x.Priority == priority)
                   ?? _session.GetCollection<CustomMonsterAiRuleInfo>().CreateNewObject();
        }

        private CustomMonsterAiActionInfo ResolveAiAction(CustomMonsterBehaviorInfo behavior, Dictionary<string, string> values, bool createDetached, bool replaceExisting)
        {
            if (createDetached || replaceExisting || behavior?.Actions == null)
                return createDetached ? CreateDetachedCustomMonsterAiAction() : _session.GetCollection<CustomMonsterAiActionInfo>().CreateNewObject();

            string name = GetValue(values, "Name", null);
            if (!string.IsNullOrWhiteSpace(name))
            {
                CustomMonsterAiActionInfo named = behavior.Actions.FirstOrDefault(x => x != null && Normalize(x.Name).Equals(Normalize(name), StringComparison.OrdinalIgnoreCase));
                if (named != null) return named;
            }

            CustomAiActionType action = GetEnum(values, "Type", GetEnum(values, "Action", CustomAiActionType.None));
            CustomAiTargetSelectorType target = GetEnum(values, "Target", CustomAiTargetSelectorType.CurrentTarget);
            int animationVariant = GetInt(values, "AnimationVariant", GetInt(values, "Variant", 0));
            MagicInfo magicInfo = TryResolveMagicReference(values, "MagicInfo");

            return behavior.Actions.FirstOrDefault(x =>
                       x != null &&
                       string.IsNullOrWhiteSpace(x.Name) &&
                       x.Action == action &&
                       x.Target == target &&
                       x.AnimationVariant == animationVariant &&
                       x.MagicInfo == magicInfo)
                   ?? _session.GetCollection<CustomMonsterAiActionInfo>().CreateNewObject();
        }

        private CustomSkillEffectInfo ResolveSkillEffect(CustomSkillInfo skill, Dictionary<string, string> values, Dictionary<string, CustomResourceLibraryInfo> resources, bool createDetached, bool replaceExisting)
        {
            if (createDetached || replaceExisting || skill?.Effects == null)
                return createDetached ? CreateDetachedCustomSkillEffect() : _session.GetCollection<CustomSkillEffectInfo>().CreateNewObject();

            CustomSkillEffectStage stage = GetEnum(values, "Stage", CustomSkillEffectStage.Cast);
            CustomResourceLibraryInfo resource = TryResolveResource(values, "Resource", resources);
            int startIndex = GetInt(values, "Start", GetInt(values, "StartIndex", 0));
            int endIndex = GetInt(values, "End", GetInt(values, "EndIndex", 0));

            return skill.Effects.FirstOrDefault(x =>
                       x != null &&
                       x.Stage == stage &&
                       x.Resource == resource &&
                       x.StartIndex == startIndex &&
                       x.EndIndex == endIndex)
                   ?? _session.GetCollection<CustomSkillEffectInfo>().CreateNewObject();
        }

        private MagicInfo TryResolveMagicReference(Dictionary<string, string> values, string key)
        {
            string value = GetValue(values, key, null);
            if (string.IsNullOrWhiteSpace(value)) return null;

            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) && _magicsByIndex.TryGetValue(index, out MagicInfo byIndex))
                return byIndex;

            return _magics.TryGetValue(Normalize(value), out MagicInfo byName) ? byName : null;
        }

        private static CustomResourceLibraryInfo TryResolveResource(Dictionary<string, string> values, string key, Dictionary<string, CustomResourceLibraryInfo> resources)
        {
            string value = GetValue(values, key, null);
            if (string.IsNullOrWhiteSpace(value)) return null;

            return resources.TryGetValue(Normalize(value), out CustomResourceLibraryInfo resource) ? resource : null;
        }

        private static bool ValidateRelativePath(string relativePath, string source, CustomSystemTextReport report)
        {
            relativePath = relativePath?.Trim();

            if (string.IsNullOrWhiteSpace(relativePath))
            {
                report.Failures.Add($"{source}: 资源路径不能为空。");
                return false;
            }

            if (Path.IsPathRooted(relativePath) || ContainsParentDirectorySegment(relativePath))
            {
                report.Failures.Add($"{source}: 资源路径必须是客户端目录内的相对路径，不能包含绝对路径或 `..`: {relativePath}");
                return false;
            }

            try
            {
                string clientRoot = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "__ClientRoot"));
                string customRoot = Path.GetFullPath(Path.Combine(clientRoot, "Data", "Custom"));
                string fullPath = Path.GetFullPath(Path.Combine(clientRoot, relativePath));

                if (!IsPathInsideDirectory(customRoot, fullPath) ||
                    string.Equals(
                        fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                        customRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                        StringComparison.OrdinalIgnoreCase))
                {
                    report.Failures.Add($"{source}: 资源路径必须位于客户端 Data\\Custom 目录下: {relativePath}");
                    return false;
                }
            }
            catch (Exception ex) when (IsTextPathException(ex))
            {
                report.Failures.Add($"{source}: 资源路径无效 `{relativePath}`: {ex.Message}");
                return false;
            }

            return true;
        }

        private static bool ContainsParentDirectorySegment(string path)
        {
            return path.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
                .Any(segment => segment == "..");
        }

        private static bool TryParseStat(string value, out Stat stat)
        {
            return Enum.TryParse(value, true, out stat) && stat != Stat.None;
        }

        private static void ClearMonsterStats(MonsterInfo monster)
        {
            if (monster?.MonsterInfoStats == null) return;

            for (int i = monster.MonsterInfoStats.Count - 1; i >= 0; i--)
                monster.MonsterInfoStats[i].Delete();
        }

        private static void ClearVisualChildren(CustomMonsterVisualInfo visual)
        {
            if (visual?.Animations == null) return;

            for (int i = visual.Animations.Count - 1; i >= 0; i--)
                visual.Animations[i].Delete();
        }

        private static void ClearBehaviorChildren(CustomMonsterBehaviorInfo behavior)
        {
            if (behavior == null) return;

            if (behavior.Skills != null)
                for (int i = behavior.Skills.Count - 1; i >= 0; i--)
                    behavior.Skills[i].Delete();

            if (behavior.Rules != null)
                for (int i = behavior.Rules.Count - 1; i >= 0; i--)
                    behavior.Rules[i].Delete();

            if (behavior.Actions != null)
                for (int i = behavior.Actions.Count - 1; i >= 0; i--)
                    behavior.Actions[i].Delete();
        }

        private static void ClearSkillChildren(CustomSkillInfo skill)
        {
            if (skill?.Effects == null) return;

            for (int i = skill.Effects.Count - 1; i >= 0; i--)
                skill.Effects[i].Delete();
        }

        private static void DeduplicateVisualChildren(CustomMonsterVisualInfo visual)
        {
            Deduplicate(visual?.Animations, x => $"{x.Action}|{x.Variant}");
        }

        private static void DeduplicateBehaviorChildren(CustomMonsterBehaviorInfo behavior)
        {
            Deduplicate(behavior?.Skills, x => x.Magic == null ? null : $"{x.Magic.Index}|{x.Target}|{x.AnimationVariant}");
            Deduplicate(behavior?.Actions, x => string.IsNullOrWhiteSpace(x.Name) ? null : Normalize(x.Name));
            Deduplicate(behavior?.Rules, x => string.IsNullOrWhiteSpace(x.Name) ? null : Normalize(x.Name));
        }

        private static void DeduplicateSkillChildren(CustomSkillInfo skill)
        {
            Deduplicate(skill?.Effects, x => $"{x.Stage}|{x.Resource?.Index ?? 0}|{x.StartIndex}|{x.EndIndex}");
        }

        private static void Deduplicate<T>(IEnumerable<T> values, Func<T, string> keySelector) where T : DBObject
        {
            if (values == null) return;

            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            foreach (T value in values.ToList())
            {
                if (value == null) continue;

                string key = keySelector(value);
                if (string.IsNullOrWhiteSpace(key)) continue;
                if (seen.Add(key)) continue;

                value.Delete();
            }
        }

        private static CustomTextDocument ReadDocument(string path, string rootDirectory, CustomSystemTextReport report)
        {
            CustomTextDocument document = new();
            CustomTextSection current = null;

            foreach (CustomTextLine line in ReadCleanLines(path, rootDirectory, report, new HashSet<string>(StringComparer.OrdinalIgnoreCase)))
            {
                if (line.Text.StartsWith("[", StringComparison.Ordinal) && line.Text.EndsWith("]", StringComparison.Ordinal))
                {
                    current = new CustomTextSection(line.Text.Trim('[', ']').Trim(), line.Source);
                    document.Sections.Add(current);
                    continue;
                }

                if (current == null)
                {
                    report.Failures.Add($"{line.Source}: 缺少段落标题。");
                    continue;
                }

                current.Lines.Add(line.Text);

                if (IsRawLineSection(current.Name))
                    continue;

                foreach (string token in Tokenize(line.Text))
                {
                    string[] kv = token.Split('=', 2);
                    if (kv.Length != 2)
                    {
                        report.Failures.Add($"{line.Source}: 参数必须是 Key=Value: {token}");
                        continue;
                    }

                    current.Values[kv[0].Trim()] = kv[1].Trim();
                }
            }

            return document;
        }

        private static bool IsRawLineSection(string sectionName)
        {
            return sectionName.Equals("Drops", StringComparison.OrdinalIgnoreCase) ||
                   sectionName.Equals("Drop", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDropSection(CustomTextSection section)
        {
            return section != null && IsRawLineSection(section.Name);
        }

        private static List<CustomTextLine> ReadCleanLines(string path, string rootDirectory, CustomSystemTextReport report, HashSet<string> stack)
        {
            List<CustomTextLine> result = [];
            string fullPath;

            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch (Exception ex) when (IsTextPathException(ex))
            {
                report.Failures.Add($"文本路径无效 `{path}`: {ex.Message}");
                return result;
            }

            if (!stack.Add(fullPath))
            {
                report.Failures.Add($"检测到循环 #INCLUDE/#INSERT: {fullPath}");
                return result;
            }

            try
            {
                string[] lines;

                try
                {
                    lines = File.ReadAllLines(fullPath);
                }
                catch (Exception ex) when (IsTextPathException(ex) || ex is UnauthorizedAccessException)
                {
                    report.Failures.Add($"{Path.GetFileName(fullPath)}: 读取文本失败: {ex.Message}");
                    return result;
                }

                for (int i = 0; i < lines.Length; i++)
                {
                    string text = StripComment(lines[i]).Trim();
                    if (string.IsNullOrWhiteSpace(text)) continue;

                    if (text.StartsWith("#INSERT", StringComparison.OrdinalIgnoreCase) || text.StartsWith("#INCLUDE", StringComparison.OrdinalIgnoreCase))
                    {
                        Match includeMatch = IncludeRegex.Match(text);
                        if (!includeMatch.Success)
                        {
                            report.Failures.Add($"{Path.GetFileName(fullPath)}:{i + 1}: #INSERT/#INCLUDE 格式必须是整行 #INCLUDE [file]。");
                            continue;
                        }

                        string include = includeMatch.Groups[1].Value.Trim();
                        string includePath = ResolveIncludePath(rootDirectory, include, report, $"{Path.GetFileName(fullPath)}:{i + 1}");
                        if (includePath == null)
                            continue;

                        if (!File.Exists(includePath))
                        {
                            report.Failures.Add($"{Path.GetFileName(fullPath)}:{i + 1}: 找不到 include 文件 `{include}`。");
                            continue;
                        }

                        result.AddRange(ReadCleanLines(includePath, rootDirectory, report, stack));
                        continue;
                    }

                    result.Add(new CustomTextLine(fullPath, i + 1, text));
                }
            }
            finally
            {
                stack.Remove(fullPath);
            }

            return result;
        }

        private static string ResolveIncludePath(string rootDirectory, string include, CustomSystemTextReport report, string source)
        {
            if (string.IsNullOrWhiteSpace(include))
            {
                report.Failures.Add($"{source}: include 路径必须是自定义目录内的相对路径: {include}");
                return null;
            }

            try
            {
                if (Path.IsPathRooted(include))
                {
                    report.Failures.Add($"{source}: include 路径必须是自定义目录内的相对路径: {include}");
                    return null;
                }

                string root = Path.GetFullPath(rootDirectory);
                string includePath = Path.Combine(root, include);
                if (!Path.HasExtension(includePath))
                    includePath += ".txt";

                includePath = Path.GetFullPath(includePath);

                if (IsPathInsideDirectory(root, includePath))
                    return includePath;
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
            {
                report.Failures.Add($"{source}: include 路径无效 `{include}`: {ex.Message}");
                return null;
            }

            report.Failures.Add($"{source}: include 路径不能跳出自定义目录: {include}");
            return null;
        }

        private static bool IsTextPathException(Exception ex)
        {
            return ex is ArgumentException or IOException or NotSupportedException;
        }

        private static bool IsPathInsideDirectory(string rootDirectory, string path)
        {
            string normalizedRoot = Path.GetFullPath(rootDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedPath = Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            return string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
                   normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                   normalizedPath.StartsWith(normalizedRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static string StripComment(string line)
        {
            if (line == null) return string.Empty;

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

                if (quoted)
                    continue;

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

        private static List<string> Tokenize(string line)
        {
            List<string> result = [];
            if (string.IsNullOrWhiteSpace(line)) return result;

            StringBuilder builder = new();
            bool quoted = false;
            bool preserveQuote = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted && c == '\\' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    if (preserveQuote)
                        builder.Append('\\');
                    builder.Append('"');
                    i++;
                    continue;
                }

                if (c == '"')
                {
                    if (!quoted)
                    {
                        preserveQuote = IsOptionsToken(builder) && !IsWholeOptionsValueQuote(builder);
                        if (preserveQuote)
                            builder.Append(c);
                        quoted = true;
                    }
                    else
                    {
                        if (preserveQuote)
                            builder.Append(c);
                        quoted = false;
                        preserveQuote = false;
                    }
                    continue;
                }

                if (char.IsWhiteSpace(c) && !quoted)
                {
                    if (builder.Length > 0)
                    {
                        result.Add(builder.ToString());
                        builder.Clear();
                    }
                    continue;
                }

                builder.Append(c);
            }

            if (builder.Length > 0)
                result.Add(builder.ToString());

            return result;
        }

        private static bool IsOptionsToken(StringBuilder builder)
        {
            const string prefix = "Options=";
            if (builder.Length < prefix.Length) return false;

            for (int i = 0; i < prefix.Length; i++)
                if (char.ToUpperInvariant(builder[i]) != char.ToUpperInvariant(prefix[i]))
                    return false;

            return true;
        }

        private static bool IsWholeOptionsValueQuote(StringBuilder builder)
        {
            const string prefix = "Options=";
            return builder.Length == prefix.Length && IsOptionsToken(builder);
        }

        private static Dictionary<string, string> ParseOptions(IEnumerable<string> tokens)
        {
            Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);
            foreach (string token in tokens)
            {
                string[] kv = token.Split('=', 2);
                if (kv.Length == 2)
                    result[kv[0].Trim()] = kv[1].Trim();
            }
            return result;
        }

        private static List<(string Name, int Weight)> ParseWeightedOptions(string value)
        {
            List<(string Name, int Weight)> result = [];
            if (string.IsNullOrWhiteSpace(value)) return result;

            foreach (string token in SplitActionOptionTokens(value, splitBareItems: true))
            {
                if (token.Contains('=', StringComparison.Ordinal))
                    continue;

                string[] parts = token.Split(':', 2, StringSplitOptions.TrimEntries);
                string name = parts[0];
                int weight = parts.Length == 2 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                    ? parsed
                    : 1;

                if (!string.IsNullOrWhiteSpace(name) && weight > 0)
                    result.Add((name, weight));
            }

            return result;
        }

        private static IEnumerable<string> SplitActionOptionTokens(string options, bool splitBareItems = false)
        {
            if (string.IsNullOrWhiteSpace(options)) yield break;

            int start = 0;
            bool quoted = false;
            bool escaped = false;

            for (int i = 0; i < options.Length; i++)
            {
                char c = options[i];

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

                if (c != ',' || quoted || !ShouldSplitActionOptionComma(options, i, splitBareItems)) continue;

                string token = options[start..i].Trim();
                if (!string.IsNullOrWhiteSpace(token))
                    yield return token;

                start = i + 1;
            }

            string last = options[start..].Trim();
            if (!string.IsNullOrWhiteSpace(last))
                yield return last;
        }

        private static string GetActionOptionValue(string options, string key)
        {
            if (string.IsNullOrWhiteSpace(options)) return null;

            foreach (string token in SplitActionOptionTokens(options, splitBareItems: false))
            {
                string[] parts = token.Split('=', 2, StringSplitOptions.TrimEntries);
                if (parts.Length == 2 && parts[0].Equals(key, StringComparison.OrdinalIgnoreCase))
                    return TrimOptionQuotes(parts[1]);
            }

            return null;
        }

        private static bool ShouldSplitActionOptionComma(string options, int commaIndex, bool splitBareItems)
        {
            if (splitBareItems)
                return true;

            int index = commaIndex + 1;
            while (index < options.Length && char.IsWhiteSpace(options[index]))
                index++;

            if (index >= options.Length || options[index] == '"')
                return false;

            bool hasName = false;
            for (; index < options.Length; index++)
            {
                char c = options[index];

                if (c == '=')
                    return hasName;

                if (c == ',')
                    return false;

                if (char.IsWhiteSpace(c))
                    continue;

                if (char.IsLetterOrDigit(c) || c == '_')
                {
                    hasName = true;
                    continue;
                }

                return false;
            }

            return false;
        }

        private static string TrimOptionQuotes(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;

            value = value.Trim();
            return value.Length >= 2 && value[0] == '"' && value[^1] == '"'
                ? value[1..^1].Replace("\\\"", "\"")
                : value;
        }

        private static string GetOption(Dictionary<string, string> values, string key, string defaultValue)
        {
            return values.TryGetValue(key, out string value) ? value : defaultValue;
        }

        private static string GetValue(Dictionary<string, string> values, string key, string defaultValue)
        {
            return values.TryGetValue(key, out string value) ? value : defaultValue;
        }

        private static int GetInt(Dictionary<string, string> values, string key, int defaultValue)
        {
            return values.TryGetValue(key, out string value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) ? result : defaultValue;
        }

        private static decimal GetDecimal(Dictionary<string, string> values, string key, decimal defaultValue)
        {
            return values.TryGetValue(key, out string value) && decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal result) ? result : defaultValue;
        }

        private static float GetFloat(Dictionary<string, string> values, string key, float defaultValue)
        {
            return values.TryGetValue(key, out string value) && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result) ? result : defaultValue;
        }

        private static bool GetBool(Dictionary<string, string> values, string key, bool defaultValue)
        {
            if (!values.TryGetValue(key, out string value)) return defaultValue;
            return TryParseTextBool(value, out bool result) ? result : defaultValue;
        }

        private static bool TryParseTextBool(string value, out bool result)
        {
            if (bool.TryParse(value, out result)) return true;

            if (value.Equals("1", StringComparison.OrdinalIgnoreCase) || value.Equals("yes", StringComparison.OrdinalIgnoreCase))
            {
                result = true;
                return true;
            }

            if (value.Equals("0", StringComparison.OrdinalIgnoreCase) || value.Equals("no", StringComparison.OrdinalIgnoreCase))
            {
                result = false;
                return true;
            }

            result = false;
            return false;
        }

        private static T GetEnum<T>(Dictionary<string, string> values, string key, T defaultValue) where T : struct
        {
            return values.TryGetValue(key, out string value) &&
                   Enum.TryParse(value, true, out T result) &&
                   IsDefinedEnumValue(result)
                ? result
                : defaultValue;
        }

        private static T GetEnum<T>(Dictionary<string, string> values, string key, T defaultValue, CustomSystemTextReport report, string source) where T : struct
        {
            if (!values.TryGetValue(key, out string value)) return defaultValue;
            if (Enum.TryParse(value, true, out T result) && IsDefinedEnumValue(result)) return result;

            report.Failures.Add($"{source}: `{key}` 枚举值无效: {value}，类型 {typeof(T).Name}。");
            return defaultValue;
        }

        private static bool IsDefinedEnumValue<T>(T value) where T : struct
        {
            Type enumType = typeof(T);
            if (!enumType.IsEnum) return false;

            if (!Attribute.IsDefined(enumType, typeof(FlagsAttribute)))
                return Enum.IsDefined(enumType, value);

            if (!TryGetEnumBits(value, enumType, out ulong raw))
                return false;

            ulong allowed = 0;

            foreach (object defined in Enum.GetValues(enumType))
            {
                if (!TryGetEnumBits(defined, enumType, out ulong definedBits))
                    return false;

                allowed |= definedBits;
            }

            return (raw & ~allowed) == 0;
        }

        private static bool TryGetEnumBits(object value, Type enumType, out ulong bits)
        {
            bits = 0;

            try
            {
                switch (Type.GetTypeCode(Enum.GetUnderlyingType(enumType)))
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

        private static Stats GetStats(Dictionary<string, string> values, string key, Stats defaultValue, CustomSystemTextReport report, string source)
        {
            if (!values.TryGetValue(key, out string value)) return defaultValue;

            if (string.IsNullOrWhiteSpace(value) || value.Equals("None", StringComparison.OrdinalIgnoreCase))
                return null;

            Stats stats = new();

            foreach (string token in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string[] parts = token.Split([':', '='], 2, StringSplitOptions.TrimEntries);
                if (parts.Length != 2)
                {
                    report.Failures.Add($"{source}: `{key}` 条目格式无效: {token}，应为 Stat:Value。");
                    continue;
                }

                if (!TryParseStat(parts[0], out Stat stat))
                {
                    report.Failures.Add($"{source}: `{key}` 包含未知属性 `{parts[0]}`。");
                    continue;
                }

                if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int amount))
                {
                    report.Failures.Add($"{source}: `{key}` 属性 `{parts[0]}` 的数值无效: {parts[1]}");
                    continue;
                }

                stats[stat] = amount;
            }

            return stats.Count == 0 ? null : stats;
        }

        private static List<string> EnumerateCustomTextFiles(string directory, bool includeTemplates, CustomSystemTextReport report)
        {
            List<string> files = [];

            try
            {
                files.AddRange(Directory.EnumerateFiles(directory, "*.txt", SearchOption.TopDirectoryOnly)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
            }
            catch (Exception ex) when (IsTextPathException(ex) || ex is UnauthorizedAccessException)
            {
                report.Failures.Add($"枚举自定义文本目录失败 `{directory}`: {ex.Message}");
                return files;
            }

            if (!includeTemplates)
                return files;

            string templatesDirectory = Path.Combine(directory, "Templates");
            if (!Directory.Exists(templatesDirectory))
                return files;

            try
            {
                files.AddRange(Directory.EnumerateFiles(templatesDirectory, "*.txt", SearchOption.AllDirectories)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
            }
            catch (Exception ex) when (IsTextPathException(ex) || ex is UnauthorizedAccessException)
            {
                report.Failures.Add($"枚举自定义模板目录失败 `{templatesDirectory}`: {ex.Message}");
            }

            return files;
        }

        private static bool IsTemplateTextFile(string directory, string file)
        {
            string templatesDirectory = Path.GetFullPath(Path.Combine(directory, "Templates"));
            string fullPath = Path.GetFullPath(file);

            return IsPathInsideDirectory(templatesDirectory, fullPath);
        }

        private static LibraryFile GetLibraryFile(Dictionary<string, string> values, string source, CustomSystemTextReport report)
        {
            string value = GetOption(values, "LibraryFile", GetOption(values, "UseLibraryFile", null));
            if (string.IsNullOrWhiteSpace(value)) return LibraryFile.None;

            if (Enum.TryParse(value, true, out LibraryFile result))
                return result;

            report.Failures.Add($"{source}: LibraryFile `{value}` 无效。");
            return LibraryFile.None;
        }

        private static string Normalize(string value)
        {
            return (value ?? string.Empty).Trim();
        }

        private void IndexMonster(MonsterInfo monster)
        {
            if (monster == null) return;
            if (!string.IsNullOrWhiteSpace(monster.MonsterName))
                _monsters[Normalize(monster.MonsterName)] = monster;
            if (monster.Index > 0)
                _monstersByIndex[monster.Index] = monster;
        }

        private void IndexMagic(MagicInfo magic)
        {
            if (magic == null) return;
            if (!string.IsNullOrWhiteSpace(magic.Name))
                _magics[Normalize(magic.Name)] = magic;
            if (magic.Index > 0)
                _magicsByIndex[magic.Index] = magic;
        }
    }

    public sealed class CustomSystemDatabaseImporter
    {
        private readonly Session _session;

        public CustomSystemDatabaseImporter(Session session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        public CustomSystemTextReport Import(string directory, CustomSystemImportMode mode)
        {
            CustomSystemTextLoader loader = new(_session);

            bool includeTemplatesInValidation = mode == CustomSystemImportMode.ValidateOnly;
            CustomSystemTextLoadResult validation = loader.BuildForDatabaseImport(directory, CustomSystemImportMode.ValidateOnly, includeTemplatesInValidation);
            CustomSystemTextReport report = validation.Report;
            report.Mode = mode;

            AddMissingDropItemFailures(report);

            if (mode == CustomSystemImportMode.ValidateOnly || report.Failures.Count > 0)
                return report;

            loader = new CustomSystemTextLoader(_session);
            CustomSystemTextLoadResult load = loader.BuildForDatabaseImport(directory, mode);
            report = load.Report;
            report.Mode = mode;

            AddMissingDropItemFailures(report);
            ApplyImportCounts(report, load);

            if (report.Failures.Count > 0)
                return report;

            return report;
        }

        private static void ApplyImportCounts(CustomSystemTextReport report, CustomSystemTextLoadResult load)
        {
            report.ResourcesImported = load.Resources.Count;
            report.MonsterVisualsImported = load.Visuals.Count;
            report.MonsterBehaviorsImported = load.Behaviors.Count;
            report.MonsterStatsImported = load.Report.MonsterStatsLoaded;
            report.MonsterDropEntriesImported = load.Report.MonsterDropEntriesImported;
            report.MonsterDropGroupsImported = load.Report.MonsterDropGroupsImported;
            report.SkillsImported = load.Skills.Count;
        }

        private static void AddMissingDropItemFailures(CustomSystemTextReport report)
        {
            HashSet<string> existingFailures = new(report.Failures, StringComparer.OrdinalIgnoreCase);
            foreach (string item in report.MissingItems.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string failure = $"[Drops] 引用的物品 `{item}` 不存在，数据库导入已阻止。";
                if (existingFailures.Add(failure))
                    report.Failures.Add(failure);
            }
        }
    }

    public sealed class CustomSystemDatabaseExporter
    {
        private readonly Session _session;

        public CustomSystemDatabaseExporter(Session session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        public CustomSystemTextReport Export(string directory, bool overwrite)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("输出目录不能为空。", nameof(directory));

            CustomSystemTextReport report = new();
            report.Mode = CustomSystemImportMode.Export;
            if (!TryCreateExportDirectories(directory, report))
                return report;

            ExportResources(Path.Combine(directory, "Resources.txt"), overwrite, report);
            ExportMonsters(Path.Combine(directory, "Monsters"), overwrite, report);
            ExportSkills(Path.Combine(directory, "Skills"), overwrite, report);
            return report;
        }

        public CustomSystemTextReport ExportAllDatabaseMonstersAndSkills(string directory, bool overwrite)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("输出目录不能为空。", nameof(directory));

            CustomSystemTextReport report = new();
            report.Mode = CustomSystemImportMode.Export;
            if (!TryCreateExportDirectories(directory, report))
                return report;

            report.TodoDetails.Add("本报告由全量数据库怪物/技能导出生成。没有 CustomMonsterBehaviorInfo 或 CustomSkillInfo 的旧数据只导出可编辑骨架；旧 C# AI 和旧 MagicObject 逻辑需要继续按 TODO 迁移。");

            ExportResources(Path.Combine(directory, "Resources.txt"), overwrite, report);
            ExportMonsters(Path.Combine(directory, "Monsters"), overwrite, report, includeAllDatabaseMonsters: true);
            ExportSkills(Path.Combine(directory, "Skills"), overwrite, report, includeAllDatabaseSkills: true);
            ExportTodoReport(Path.Combine(directory, "TODO报告.txt"), overwrite, report);
            return report;
        }

        private void ExportResources(string path, bool overwrite, CustomSystemTextReport report)
        {
            if (File.Exists(path) && !overwrite)
            {
                report.Failures.Add($"已存在 Resources.txt，未覆盖: {path}");
                return;
            }

            List<string> lines = [];
            int exported = 0;
            foreach (CustomResourceLibraryInfo resource in _session.GetCollection<CustomResourceLibraryInfo>().Binding.Where(x => x != null).OrderBy(x => x.Key))
            {
                List<string> parts = [Quote(resource.Key)];

                if (!string.IsNullOrWhiteSpace(resource.RelativePath))
                    parts.Add(Quote(resource.RelativePath));

                if (resource.LibraryFile != LibraryFile.None)
                    parts.Add($"LibraryFile={resource.LibraryFile}");

                parts.Add($"DisplayName={Quote(resource.DisplayName)}");
                parts.Add($"Hash={Quote(resource.Hash)}");
                parts.Add($"Version={Quote(resource.Version)}");
                parts.Add($"Scope={resource.Scope}");
                parts.Add($"Enabled={resource.Enabled}");

                lines.Add(string.Join(' ', parts));
                exported++;
            }

            if (TryWriteAllLines(path, lines, report, "Resources.txt"))
                report.ResourcesExported += exported;
        }

        private void ExportMonsters(string directory, bool overwrite, CustomSystemTextReport report, bool includeAllDatabaseMonsters = false)
        {
            Dictionary<int, CustomMonsterVisualInfo> visuals = _session.GetCollection<CustomMonsterVisualInfo>().Binding
                .Where(x => x?.Monster != null)
                .GroupBy(x => x.Monster.Index)
                .ToDictionary(x => x.Key, x => x.First());

            Dictionary<int, CustomMonsterBehaviorInfo> behaviors = _session.GetCollection<CustomMonsterBehaviorInfo>().Binding
                .Where(x => x?.Monster != null)
                .GroupBy(x => x.Monster.Index)
                .ToDictionary(x => x.Key, x => x.First());

            Dictionary<int, CustomMonsterAiMigrationRow> aiSuggestions = includeAllDatabaseMonsters
                ? CustomMonsterAiMigrationReportBuilder.Build(_session).Rows.ToDictionary(x => x.AI, x => x)
                : [];

            IEnumerable<MonsterInfo> monsters = includeAllDatabaseMonsters
                ? _session.GetCollection<MonsterInfo>().Binding.Where(x => x != null)
                : visuals.Values.Select(x => x.Monster)
                    .Concat(behaviors.Values.Select(x => x.Monster))
                    .Where(x => x != null)
                    .GroupBy(x => x.Index)
                    .Select(x => x.First());

            HashSet<string> usedFileNames = new(StringComparer.OrdinalIgnoreCase);

            foreach (MonsterInfo monster in monsters.OrderBy(x => x.MonsterName).ThenBy(x => x.Index))
            {
                string fileName = GetUniqueFileName(SafeFileName(monster.MonsterName), monster.Index, usedFileNames);
                string path = Path.Combine(directory, fileName + ".txt");
                if (File.Exists(path) && !overwrite)
                {
                    report.Failures.Add($"已存在怪物文本，未覆盖: {path}");
                    continue;
                }

                visuals.TryGetValue(monster.Index, out CustomMonsterVisualInfo visual);
                behaviors.TryGetValue(monster.Index, out CustomMonsterBehaviorInfo behavior);
                aiSuggestions.TryGetValue(monster.AI, out CustomMonsterAiMigrationRow migration);

                Mir2DropExportReport dropReport = new();
                List<string> dropLines = new Mir2DropExporter(_session).BuildMonsterLines(monster, dropReport);

                if (!TryWriteAllText(path, BuildMonsterText(monster, visual, behavior, dropLines, migration, includeAllDatabaseMonsters), report, $"怪物 `{monster.MonsterName}`"))
                    continue;

                report.MonstersExported++;
                if (visual != null)
                    report.MonsterVisualsExported++;
                if (behavior != null)
                    report.MonsterBehaviorsExported++;
                if (includeAllDatabaseMonsters)
                    AddMonsterExportTodo(report, monster, visual, behavior, migration, fileName);
                report.MonsterStatsExported += monster.MonsterInfoStats?.Count(x => x != null && x.Stat != Stat.None && x.Amount != 0) ?? 0;
                report.MonsterDropEntriesExported += dropReport.DropLines + dropReport.GroupItemLines;
                report.MonsterDropGroupsExported += dropReport.GroupLines;
            }
        }

        private static string BuildMonsterText(MonsterInfo monster, CustomMonsterVisualInfo visual, CustomMonsterBehaviorInfo behavior, List<string> dropLines, CustomMonsterAiMigrationRow migration = null, bool includeFallbackBehavior = false)
        {
            StringBuilder sb = new();
            sb.AppendLine("[Monster]");
            sb.AppendLine($"Index={monster.Index}");
            sb.AppendLine($"Name={Quote(monster.MonsterName)}");
            sb.AppendLine($"Image={monster.Image}");
            sb.AppendLine($"AI={monster.AI}");
            sb.AppendLine($"Level={monster.Level}");
            sb.AppendLine($"ViewRange={monster.ViewRange}");
            sb.AppendLine($"CoolEye={monster.CoolEye}");
            sb.AppendLine($"Experience={monster.Experience.ToString(CultureInfo.InvariantCulture)}");
            sb.AppendLine($"Undead={monster.Undead}");
            sb.AppendLine($"CanPush={monster.CanPush}");
            sb.AppendLine($"CanTame={monster.CanTame}");
            sb.AppendLine($"AttackDelay={monster.AttackDelay}");
            sb.AppendLine($"MoveDelay={monster.MoveDelay}");
            sb.AppendLine($"IsBoss={monster.IsBoss}");
            sb.AppendLine($"Flag={monster.Flag}");
            sb.AppendLine($"FaceImage={monster.FaceImage}");
            bool autoMigratedBehavior = includeFallbackBehavior && IsAutoMigratedBasicBehavior(monster.AI);
            if (autoMigratedBehavior)
            {
                sb.AppendLine($"; AutoMigrated [AI] 原 AI {monster.AI} 已转换为基础 RuleSet 行为。");
            }
            else if (includeFallbackBehavior && migration != null)
            {
                sb.AppendLine($"; TODO [AI] 原 AI {monster.AI}: {migration.CurrentImplementation}");
                sb.AppendLine($"; TODO [AI] 建议迁移: {migration.SuggestedTemplate}");
                sb.AppendLine($"; TODO [AI] 优先级: {migration.Priority}; {migration.Notes}");
            }

            List<MonsterInfoStat> stats = monster.MonsterInfoStats?
                .Where(x => x != null && x.Stat != Stat.None && x.Amount != 0)
                .OrderBy(x => x.Stat)
                .ToList() ?? [];

            if (stats.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("[Stats]");
                foreach (MonsterInfoStat stat in stats)
                    sb.AppendLine($"{stat.Stat}={stat.Amount}");
            }

            if (dropLines?.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("[Drops]");
                foreach (string line in dropLines)
                    sb.AppendLine(line);
            }

            if (visual != null)
            {
                sb.AppendLine();
                sb.AppendLine("[Visual]");
                sb.AppendLine($"Resource={Quote(visual.DefaultResource?.Key)}");
                sb.AppendLine($"Scale={visual.Scale.ToString(CultureInfo.InvariantCulture)}");
                sb.AppendLine($"DrawShadow={visual.DrawShadow}");
                sb.AppendLine($"Blend={visual.Blend}");
                sb.AppendLine($"KeepCorpse={visual.KeepCorpse}");
                sb.AppendLine($"DefaultFrameDelay={visual.DefaultFrameDelay}");
                sb.AppendLine($"FrameBase={visual.FrameBase}");
                sb.AppendLine($"AttackSound={visual.AttackSound}");
                sb.AppendLine($"RangeAttackSound={visual.RangeAttackSound}");
                sb.AppendLine($"MagicSound={visual.MagicSound}");
                sb.AppendLine($"StruckSound={visual.StruckSound}");
                sb.AppendLine($"DieSound={visual.DieSound}");
                sb.AppendLine($"ShowSound={visual.ShowSound}");
                sb.AppendLine($"HideSound={visual.HideSound}");

                foreach (CustomMonsterAnimationInfo animation in (visual.Animations?.Where(x => x != null) ?? Enumerable.Empty<CustomMonsterAnimationInfo>()).OrderBy(x => x.Priority).ThenBy(x => x.Action).ThenBy(x => x.Variant))
                {
                    sb.AppendLine();
                    sb.AppendLine("[Animation]");
                    sb.AppendLine($"Action={animation.Action}");
                    sb.AppendLine($"Resource={Quote(animation.Resource?.Key)}");
                    sb.AppendLine($"Variant={animation.Variant}");
                    sb.AppendLine($"Start={animation.StartIndex}");
                    sb.AppendLine($"End={animation.EndIndex}");
                    sb.AppendLine($"FrameCount={animation.FrameCount}");
                    sb.AppendLine($"DirectionCount={animation.DirectionCount}");
                    sb.AppendLine($"DirectionOffset={animation.DirectionOffset}");
                    sb.AppendLine($"FrameDelay={animation.FrameDelay}");
                    sb.AppendLine($"Loop={animation.Loop}");
                    sb.AppendLine($"Reversed={animation.Reversed}");
                    sb.AppendLine($"OffsetX={animation.OffsetX}");
                    sb.AppendLine($"OffsetY={animation.OffsetY}");
                    sb.AppendLine($"Priority={animation.Priority}");
                    sb.AppendLine($"Sound={animation.Sound}");
                    sb.AppendLine($"EffectResource={Quote(animation.EffectResource?.Key)}");
                    sb.AppendLine($"EffectTarget={animation.EffectTarget}");
                    sb.AppendLine($"EffectStart={animation.EffectStartIndex}");
                    sb.AppendLine($"EffectEnd={animation.EffectEndIndex}");
                    sb.AppendLine($"EffectFrameCount={animation.EffectFrameCount}");
                    sb.AppendLine($"EffectFrameDelay={animation.EffectFrameDelay}");
                    sb.AppendLine($"EffectOffsetX={animation.EffectOffsetX}");
                    sb.AppendLine($"EffectOffsetY={animation.EffectOffsetY}");
                    sb.AppendLine($"EffectStartLight={animation.EffectStartLight}");
                    sb.AppendLine($"EffectEndLight={animation.EffectEndLight}");
                    sb.AppendLine($"EffectStartDelay={animation.EffectStartDelay}");
                    sb.AppendLine($"EffectDuration={animation.EffectDuration}");
                    sb.AppendLine($"EffectTriggerFrame={animation.EffectTriggerFrame}");
                    sb.AppendLine($"EffectBlend={animation.EffectBlend}");
                    sb.AppendLine($"EffectLoop={animation.EffectLoop}");
                }
            }
            else if (includeFallbackBehavior)
            {
                sb.AppendLine();
                sb.AppendLine("; TODO [Visual] 数据库没有 CustomMonsterVisualInfo。当前仍依赖客户端硬编码 MonsterImage；如需完全自定义，请补 [Visual] 和 [Animation] 资源帧段。");
            }

            if (behavior != null)
            {
                sb.AppendLine();
                sb.AppendLine("[Behavior]");
                sb.AppendLine($"Template={behavior.Template}");
                sb.AppendLine($"CanBeKilled={behavior.CanBeKilled}");
                sb.AppendLine($"CanAttackPlayers={behavior.CanAttackPlayers}");
                sb.AppendLine($"CanBeTamed={behavior.CanBeTamed}");
                sb.AppendLine($"IsPickupCompanion={behavior.IsPickupCompanion}");
                sb.AppendLine($"Passive={behavior.Passive}");
                sb.AppendLine($"AlwaysActive={behavior.AlwaysActive}");
                sb.AppendLine($"DamageProxy={behavior.DamageProxy}");
                sb.AppendLine($"DamageProxyRevealDelay={behavior.DamageProxyRevealDelay}");
                sb.AppendLine($"DamageProxyRange={behavior.DamageProxyRange}");
                sb.AppendLine($"DamageProxySpawnEffect={behavior.DamageProxySpawnEffect}");
                sb.AppendLine($"DamageProxyNameFormat={Quote(behavior.DamageProxyNameFormat)}");
                sb.AppendLine($"NeedHarvest={behavior.NeedHarvest}");
                sb.AppendLine($"HarvestCount={behavior.HarvestCount}");
                sb.AppendLine($"AvoidFireWall={behavior.AvoidFireWall}");
                sb.AppendLine($"IgnoreShield={behavior.IgnoreShield}");
                sb.AppendLine($"IgnoreArmour={behavior.IgnoreArmour}");
                sb.AppendLine($"PoisonType={behavior.PoisonType}");
                sb.AppendLine($"PoisonValue={behavior.PoisonValue}");
                sb.AppendLine($"PoisonRate={behavior.PoisonRate}");
                sb.AppendLine($"PoisonTicks={behavior.PoisonTicks}");
                sb.AppendLine($"PoisonFrequency={behavior.PoisonFrequency}");
                sb.AppendLine($"PoisonCanKill={behavior.PoisonCanKill}");
                sb.AppendLine($"PoisonStackable={behavior.PoisonStackable}");
                sb.AppendLine($"DisableMovement={behavior.DisableMovement}");
                sb.AppendLine($"DisableAttack={behavior.DisableAttack}");
                sb.AppendLine($"DisableRegen={behavior.DisableRegen}");
                sb.AppendLine($"DisableBlocking={behavior.DisableBlocking}");
                sb.AppendLine($"HiddenBlocks={behavior.HiddenBlocks}");
                sb.AppendLine($"HiddenCanAttack={behavior.HiddenCanAttack}");
                sb.AppendLine($"HiddenCanBeAttacked={behavior.HiddenCanBeAttacked}");
                sb.AppendLine($"ProtectedByMonsterFlag={behavior.ProtectedByMonsterFlag}");
                sb.AppendLine($"ProtectedByMonsterFlags={Quote(behavior.ProtectedByMonsterFlags)}");
                sb.AppendLine($"ProtectionRadius={behavior.ProtectionRadius}");
                sb.AppendLine($"ProtectionCheckInterval={behavior.ProtectionCheckInterval}");
                sb.AppendLine($"AttackRange={behavior.AttackRange}");
                sb.AppendLine($"ChaseRange={behavior.ChaseRange}");
                sb.AppendLine($"ReturnRange={behavior.ReturnRange}");
                sb.AppendLine($"MaxMinions={behavior.MaxMinions}");
                sb.AppendLine($"SkillChance={behavior.SkillChance}");
                sb.AppendLine($"RoamDelay={behavior.RoamDelay}");
                sb.AppendLine($"UseBaseFallback={behavior.UseBaseFallback}");

                foreach (CustomMonsterSkillInfo skill in (behavior.Skills?.Where(x => x != null) ?? Enumerable.Empty<CustomMonsterSkillInfo>()).OrderBy(x => x.Index))
                {
                    sb.AppendLine();
                    sb.AppendLine("[Skill]");
                    sb.AppendLine($"Magic={Quote(skill.Magic?.Name)}");
                    sb.AppendLine($"Target={skill.Target}");
                    sb.AppendLine($"AnimationVariant={skill.AnimationVariant}");
                    sb.AppendLine($"Chance={skill.Chance}");
                    sb.AppendLine($"Cooldown={skill.Cooldown}");
                    sb.AppendLine($"Range={skill.Range}");
                    sb.AppendLine($"MinHealthPercent={skill.MinHealthPercent}");
                    sb.AppendLine($"MaxHealthPercent={skill.MaxHealthPercent}");
                }

                if (behavior.Actions != null)
                {
                    foreach (CustomMonsterAiActionInfo action in behavior.Actions.OrderBy(x => x.Index))
                    {
                        sb.AppendLine();
                        sb.AppendLine("[AIAction]");
                        sb.AppendLine($"Name={Quote(action.Name)}");
                        sb.AppendLine($"Type={action.Action}");
                        sb.AppendLine($"Target={action.Target}");
                        sb.AppendLine($"AnimationVariant={action.AnimationVariant}");
                        sb.AppendLine($"Options={Quote(action.Options)}");
                        sb.AppendLine($"Magic={action.Magic}");
                        sb.AppendLine($"MagicInfo={Quote(action.MagicInfo?.Name)}");
                        sb.AppendLine($"Element={action.Element}");
                        sb.AppendLine($"Power={action.PowerSource}");
                        sb.AppendLine($"FixedPower={action.FixedPower}");
                        sb.AppendLine($"PowerRate={action.PowerRate}");
                        sb.AppendLine($"Radius={action.Radius}");
                        sb.AppendLine($"Distance={action.Distance}");
                        sb.AppendLine($"MinOffset={action.MinOffset}");
                        sb.AppendLine($"MaxOffset={action.MaxOffset}");
                        sb.AppendLine($"Delay={action.Delay}");
                        sb.AppendLine($"TargetChance={action.TargetChance}");
                        sb.AppendLine($"EmptyLocationChance={action.EmptyLocationChance}");
                        sb.AppendLine($"MaxTargets={action.MaxTargets}");
                        sb.AppendLine($"RandomTargetCount={action.RandomTargetCount}");
                        sb.AppendLine($"CounterName={Quote(action.CounterName)}");
                        sb.AppendLine($"FixedCount={action.FixedCount}");
                        sb.AppendLine($"RandomCount={action.RandomCount}");
                        sb.AppendLine($"MinionDuration={action.MinionDuration}");
                        sb.AppendLine($"Travel={action.Travel}");
                        sb.AppendLine($"SpawnMonster={Quote(action.SpawnMonster?.MonsterName)}");
                        sb.AppendLine($"BuffType={action.BuffType}");
                        sb.AppendLine($"PoisonType={action.PoisonType}");
                        sb.AppendLine($"PoisonCanKill={action.PoisonCanKill}");
                        sb.AppendLine($"BuffStats={Quote(FormatStats(action.BuffStats))}");
                        sb.AppendLine($"BuffVisible={action.BuffVisible}");
                        sb.AppendLine($"BuffPause={action.BuffPause}");
                        sb.AppendLine($"BuffHidden={action.BuffHidden}");
                        sb.AppendLine($"BuffExtra={action.BuffExtra}");
                        sb.AppendLine($"Duration={action.Duration}");
                        sb.AppendLine($"TickRate={action.TickRate}");
                    }
                }

                if (behavior.Rules != null)
                {
                    foreach (CustomMonsterAiRuleInfo rule in behavior.Rules.OrderBy(x => x.Priority).ThenBy(x => x.Index))
                    {
                        sb.AppendLine();
                        sb.AppendLine("[AIRule]");
                        sb.AppendLine($"Name={Quote(rule.Name)}");
                        sb.AppendLine($"Trigger={rule.Trigger}");
                        sb.AppendLine($"Condition={rule.Condition}");
                        sb.AppendLine($"Action={Quote(rule.ActionName)}");
                        sb.AppendLine($"Priority={rule.Priority}");
                        sb.AppendLine($"Chance={rule.Chance}");
                        sb.AppendLine($"Cooldown={rule.Cooldown}");
                        sb.AppendLine($"MaxExecutions={rule.MaxExecutions}");
                        sb.AppendLine($"Value={rule.Value}");
                        sb.AppendLine($"Radius={rule.Radius}");
                        sb.AppendLine($"BuffType={rule.BuffType}");
                        sb.AppendLine($"Conditions={Quote(rule.Conditions)}");
                        sb.AppendLine($"ConditionJoin={rule.ConditionJoin}");
                        sb.AppendLine($"InvertCondition={rule.InvertCondition}");
                        sb.AppendLine($"CounterName={Quote(rule.CounterName)}");
                        sb.AppendLine($"MinHealthPercent={rule.MinHealthPercent}");
                        sb.AppendLine($"MaxHealthPercent={rule.MaxHealthPercent}");
                    }
                }
            }
            else if (includeFallbackBehavior)
            {
                AppendFallbackBehavior(sb, monster, migration);
            }

            return sb.ToString();
        }

        private static void AppendFallbackBehavior(StringBuilder sb, MonsterInfo monster, CustomMonsterAiMigrationRow migration)
        {
            bool passive = IsPassiveAi(monster.AI);
            bool passiveDamage = IsAutoPassiveDamageAi(monster.AI);
            bool noAttack = IsAutoNoAttackAi(monster.AI);
            bool fixedObject = IsFixedAi(monster.AI);
            bool damageProxy = IsAutoDamageProxyAi(monster.AI);
            bool harvest = TryGetHarvestCount(monster.AI, out int harvestCount);
            CustomMonsterBehaviorTemplate template = InferFallbackTemplate(monster, migration);
            ApplyAutoPoison(monster.AI, out PoisonType poisonType, out int poisonRate, out int poisonTicks, out int poisonFrequency);
            bool ignoreShield = IsAutoIgnoreShield(monster.AI);

            sb.AppendLine();
            sb.AppendLine("[Behavior]");
            bool autoMigrated = IsAutoMigratedBasicBehavior(monster.AI);
            if (autoMigrated)
            {
                sb.AppendLine($"; AutoMigrated [Behavior] 原 AI {monster.AI} 已按旧 C# 行为分组生成自定义 RuleSet。");
                template = monster.AI == -1 ? CustomMonsterBehaviorTemplate.Guard : CustomMonsterBehaviorTemplate.RuleSet;
            }
            else
            {
                sb.AppendLine($"; TODO [Behavior] 这是根据 MonsterInfo 自动生成的迁移骨架，不是旧 C# AI 的完整等价实现。");
                if (migration != null)
                    sb.AppendLine($"; TODO [Behavior] 推荐参考: {migration.SuggestedTemplate}");
                sb.AppendLine("; TODO [Behavior] Template=None 表示导入数据库后继续走旧 C# AI；完成迁移后再改成 RuleSet/其他模板。");
                template = CustomMonsterBehaviorTemplate.None;
            }

            sb.AppendLine($"Template={template}");
            sb.AppendLine($"CanBeKilled={!IsAutoInvulnerableAi(monster.AI)}");
            sb.AppendLine($"CanAttackPlayers={!noAttack && (!passive || passiveDamage)}");
            sb.AppendLine($"CanBeTamed={monster.CanTame}");
            sb.AppendLine("IsPickupCompanion=False");
            sb.AppendLine($"Passive={passive}");
            sb.AppendLine($"AlwaysActive={IsAlwaysActiveAi(monster.AI)}");
            sb.AppendLine($"DamageProxy={damageProxy}");
            sb.AppendLine($"DamageProxyRevealDelay={(damageProxy ? 500 : 0)}");
            sb.AppendLine($"DamageProxyRange=0");
            sb.AppendLine($"DamageProxySpawnEffect={(damageProxy ? Effect.CursedDoll.ToString() : "0")}");
            sb.AppendLine($"DamageProxyNameFormat={Quote(damageProxy ? "{Target} Doll" : string.Empty)}");
            sb.AppendLine($"NeedHarvest={harvest}");
            sb.AppendLine($"HarvestCount={harvestCount}");
            sb.AppendLine($"AvoidFireWall={IsAutoAvoidFireWall(monster.AI)}");
            sb.AppendLine($"IgnoreShield={ignoreShield}");
            sb.AppendLine($"IgnoreArmour={IsAutoIgnoreArmour(monster.AI)}");
            sb.AppendLine($"PoisonType={poisonType}");
            sb.AppendLine("PoisonValue=0");
            sb.AppendLine($"PoisonRate={poisonRate}");
            sb.AppendLine($"PoisonTicks={poisonTicks}");
            sb.AppendLine($"PoisonFrequency={poisonFrequency}");
            sb.AppendLine("PoisonCanKill=False");
            sb.AppendLine($"PoisonStackable={IsAutoPoisonStackable(monster.AI)}");
            sb.AppendLine($"DisableMovement={fixedObject}");
            sb.AppendLine($"DisableAttack={noAttack || passive && !passiveDamage}");
            sb.AppendLine($"DisableRegen={IsAutoDisableRegenAi(monster.AI)}");
            sb.AppendLine($"DisableBlocking={IsAutoNoBlockingAi(monster.AI)}");
            bool hiddenAi = IsAutoHiddenAi(monster.AI);
            sb.AppendLine($"HiddenBlocks={!hiddenAi}");
            sb.AppendLine($"HiddenCanAttack={!hiddenAi}");
            sb.AppendLine($"HiddenCanBeAttacked={!hiddenAi}");
            sb.AppendLine($"ProtectedByMonsterFlag={GetAutoProtectedByMonsterFlag(monster.AI)}");
            sb.AppendLine($"ProtectedByMonsterFlags={Quote(GetAutoProtectedByMonsterFlags(monster.AI))}");
            sb.AppendLine($"ProtectionRadius={GetAutoProtectionRadius(monster.AI)}");
            sb.AppendLine("ProtectionCheckInterval=1000");
            int attackRange = GetAutoAttackRange(monster, template);
            sb.AppendLine($"AttackRange={attackRange}");
            sb.AppendLine($"ChaseRange={Math.Max(12, monster.ViewRange + 4)}");
            sb.AppendLine($"ReturnRange={Math.Max(16, monster.ViewRange + 8)}");
            sb.AppendLine($"MaxMinions={GetAutoMaxMinions(monster.AI)}");
            sb.AppendLine("SkillChance=25");
            sb.AppendLine("RoamDelay=2000");
            sb.AppendLine($"UseBaseFallback={!autoMigrated}");

            if (autoMigrated && template == CustomMonsterBehaviorTemplate.RuleSet)
            {
                if (TryGetAutoSelfDestructRange(monster.AI, out int selfDestructRange))
                    AppendSelfDestructRuleSet(sb, selfDestructRange);
                else if (IsAutoHiddenSpawnAi(monster.AI))
                    AppendSpawnAppearRuleSet(sb, monster.AttackDelay);
                else if (IsAutoHiddenWakeAi(monster.AI))
                    AppendHiddenWakeRuleSet(sb, monster.AI == 132, monster.AttackDelay);
                else if (IsAutoPlantAmbushAi(monster.AI))
                    AppendPlantAmbushRuleSet(sb, monster.AttackDelay);
                else if (IsAutoReviveAi(monster.AI))
                    AppendReviveMeleeRuleSet(sb, monster.AttackDelay);
                else if (TryGetAutoBossRange(monster.AI, out int bossRange))
                    AppendBossRangeRuleSet(sb, bossRange, monster.AttackDelay);
                else if (TryGetAutoSamaProfile(monster.AI, out Element samaElement, out MagicType samaProjectile, out MagicType samaLine, out MagicType samaArea, out MagicType samaBurst, out MagicType samaEmpowered))
                    AppendSamaGuardianRuleSet(sb, samaElement, samaProjectile, samaLine, samaArea, samaBurst, samaEmpowered, monster.AttackDelay);
                else if (TryGetAutoIcySpiritProfile(monster.AI, out bool drainMp))
                    AppendIcySpiritRuleSet(sb, drainMp, monster.AttackDelay);
                else if (IsAutoGhostMageAi(monster.AI))
                    AppendGhostMageRuleSet(sb, monster.AttackDelay);
                else if (IsAutoWedgeMothLarvaAi(monster.AI))
                    AppendWedgeMothLarvaRuleSet(sb, monster.AttackDelay);
                else if (IsAutoArachnidGrazerAi(monster.AI))
                    AppendArachnidGrazerRuleSet(sb, monster.AttackDelay, monster.AI == 144 ? MonsterFlag.SuperLarva : MonsterFlag.Larva, monster.AI == 144 ? 3 : 1);
                else if (IsAutoTornadoAi(monster.AI))
                    AppendTornadoRuleSet(sb);
                else if (TryGetAutoPortalProfile(monster.AI, out string specialRegion, out bool includeMonsters, out string openMessage, out string closeMessage))
                    AppendPortalRuleSet(sb, specialRegion, includeMonsters, openMessage, closeMessage, monster.ViewRange);
                else if (IsAutoBanyoLordGuzakAi(monster.AI))
                    AppendBanyoLordGuzakRuleSet(sb, monster.AttackDelay);
                else if (IsAutoBanyoCaptainAi(monster.AI))
                    AppendBanyoCaptainRuleSet(sb, monster.AttackDelay);
                else if (IsAutoEscortCommanderAi(monster.AI))
                    AppendEscortCommanderRuleSet(sb, monster.AttackDelay);
                else if (IsAutoWingedHorrorAi(monster.AI))
                    AppendWingedHorrorRuleSet(sb, monster.AttackDelay);
                else if (IsAutoChaosKnightAi(monster.AI))
                    AppendChaosKnightRuleSet(sb, monster.AttackDelay);
                else if (TryGetAutoJinchonDevilProfile(monster.AI, out int deathCloudCooldown, out int deathCloudDuration, out int deathCloudDurationRandom))
                    AppendJinchonDevilRuleSet(sb, monster.AttackDelay, deathCloudCooldown, deathCloudDuration, deathCloudDurationRandom);
                else if (IsAutoSamaScorcerAi(monster.AI))
                    AppendSamaScorcerRuleSet(sb, monster.AttackDelay);
                else if (IsAutoSamaProphetAi(monster.AI))
                    AppendSamaProphetRuleSet(sb, monster.AttackDelay, monster.ViewRange);
                else if (IsAutoQueenOfDawnAi(monster.AI))
                    AppendQueenOfDawnRuleSet(sb, monster.AttackDelay);
                else if (IsAutoQuartzTurtleSubAi(monster.AI))
                    AppendQuartzTurtleSubRuleSet(sb, monster.AttackDelay);
                else if (IsAutoMonasteryBossAi(monster.AI))
                    AppendMonasteryBossRuleSet(sb, monster.AttackDelay);
                else if (IsAutoHealerAntAi(monster.AI))
                    AppendHealerAntRuleSet(sb, monster.AttackDelay);
                else if (IsAutoLordNiJaeAi(monster.AI))
                    AppendLordNiJaeRuleSet(sb, monster.AttackDelay);
                else if (IsAutoFrostLordHwaAi(monster.AI))
                    AppendFrostLordHwaRuleSet(sb, monster.AttackDelay);
                else if (IsAutoSunFeralWarriorAi(monster.AI))
                    AppendSunFeralWarriorRuleSet(sb, monster.AttackDelay);
                else if (IsAutoFerociousIceTigerAi(monster.AI))
                    AppendFerociousIceTigerRuleSet(sb, monster.AttackDelay);
                else if (IsAutoDragonQueenAi(monster.AI))
                    AppendDragonQueenRuleSet(sb, monster.AttackDelay);
                else if (IsAutoDragonLordAi(monster.AI))
                    AppendDragonLordRuleSet(sb, monster.AttackDelay);
                else if (IsAutoArchLichTaeduAi(monster.AI))
                    AppendArchLichTaeduRuleSet(sb, monster.AttackDelay);
                else if (IsAutoEnragedArchLichTaeduAi(monster.AI))
                    AppendEnragedArchLichTaeduRuleSet(sb, monster.AttackDelay);
                else if (IsAutoZumaKingAi(monster.AI))
                    AppendZumaKingRuleSet(sb, monster.AttackDelay);
                else if (IsAutoEnragedLordNiJaeAi(monster.AI))
                    AppendEnragedLordNiJaeRuleSet(sb, monster.AttackDelay);
                else if (IsAutoQuartzTreeAi(monster.AI))
                    AppendQuartzTreeRuleSet(sb);
                else if (IsAutoDoomClawAi(monster.AI))
                    AppendDoomClawRuleSet(sb, monster.AttackDelay);
                else if (IsAutoDamageProxyAi(monster.AI))
                    AppendDamageProxyRuleSet(sb);
                else if (TryGetAutoSimpleCaster(monster.AI, out MagicType casterMagic, out Element casterElement, out bool casterTravel))
                    AppendSimpleCasterRuleSet(sb, casterMagic, casterElement, casterTravel, monster.AttackDelay);
                else if (TryGetAutoLightningChoiceCaster(monster.AI))
                    AppendLightningChoiceCasterRuleSet(sb, monster.AttackDelay);
                else if (TryGetAutoFixedMassRange(monster.AI, out int fixedRange, out MagicType fixedMagic, out Element fixedElement))
                    AppendFixedMassRangeRuleSet(sb, fixedRange, fixedMagic, fixedElement, monster.AttackDelay);
                else if (TryGetAutoLineAoEProfile(monster.AI, out int aoeDistance, out int minOffset, out int maxOffset, out MagicType aoeMagic, out Element aoeElement))
                    AppendLineAoERuleSet(sb, aoeDistance, minOffset, maxOffset, aoeMagic, aoeElement, monster.AttackDelay);
                else if (IsAutoDeathSelfSpawnAi(monster.AI))
                    AppendDeathSelfSpawnRuleSet(sb, monster.MonsterName, monster.AttackDelay);
                else if (monster.AI == 53)
                    AppendShinsuRuleSet(sb, monster.AttackDelay);
                else if (monster.AI == 133)
                    AppendTerracottaSubRuleSet(sb, monster.AttackDelay);
                else if (monster.AI == 134)
                    AppendTerracottaBossRuleSet(sb, monster.AttackDelay);
                else if (TryGetAutoTeleportMelee(monster.AI, out bool bonusDamage, out int teleportCooldown))
                    AppendTeleportMeleeRuleSet(sb, bonusDamage, teleportCooldown, monster.AttackDelay);
                else if (TryGetAutoRangedDistance(monster.AI, out int rangedDistance))
                    AppendRangedKiteRuleSet(sb, rangedDistance, monster.AttackDelay, monster.AI == 20);
                else if (TryGetAutoLineDistance(monster.AI, out int lineAttackDistance))
                    AppendLineAttackRuleSet(sb, lineAttackDistance);
                else if (TryGetAutoRangeSwitchDistance(monster.AI, out int rangeSwitchDistance))
                    AppendRangeSwitchRuleSet(sb, rangeSwitchDistance, GetAutoRangeCooldown(monster.AI));
                else if (TryGetAutoAreaMelee(monster.AI, out int areaRadius, out bool areaPoison))
                    AppendAreaMeleeRuleSet(sb, areaRadius, areaPoison, monster.AttackDelay);
                else if (IsAutoMagicWeaknessAi(monster.AI))
                    AppendMagicWeaknessRuleSet(sb, monster.AttackDelay);
                else
                    AppendBasicMeleeRuleSet(sb);
            }
        }

        private static void AddMonsterExportTodo(CustomSystemTextReport report, MonsterInfo monster, CustomMonsterVisualInfo visual, CustomMonsterBehaviorInfo behavior, CustomMonsterAiMigrationRow migration, string fileName)
        {
            string path = $"Monsters\\{fileName}.txt";

            if (visual == null)
                report.TodoDetails.Add($"怪物 `{monster.MonsterName}` -> {path}: 没有 CustomMonsterVisualInfo，仍依赖 MonsterImage={monster.Image} 的旧客户端硬编码模型；如需完全自定义 ZL 帧段，请补 [Visual]/[Animation]。");

            if (behavior == null && !IsAutoMigratedBasicBehavior(monster.AI))
            {
                string suggestion = migration == null
                    ? "当前没有 AI 迁移建议。"
                    : $"建议 `{migration.SuggestedTemplate}`，优先级 {migration.Priority}，当前实现 `{migration.CurrentImplementation}`。";
                report.TodoDetails.Add($"怪物 `{monster.MonsterName}` -> {path}: 没有 CustomMonsterBehaviorInfo，已生成 Template=None 的惰性迁移骨架；导入后仍走旧 C# AI，原 AI={monster.AI} 尚未无损迁移。{suggestion}");
            }
        }

        private static void AppendBasicMeleeRuleSet(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=MoveToTarget");
            sb.AppendLine("Type=MoveToTarget");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=StepAway");
            sb.AppendLine("Type=RandomStepWhenSameCell");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=MeleeHit");
            sb.AppendLine("Type=MeleeAttack");
            sb.AppendLine("Distance=1");
            sb.AppendLine("Delay=400");
            sb.AppendLine("Power=DC");
            sb.AppendLine("PowerRate=100");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=StepWhenStacked");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=SameCellAsTarget");
            sb.AppendLine("Action=StepAway");
            sb.AppendLine("Priority=10");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=MoveWhenFar");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=TargetOutOfAttackRange");
            sb.AppendLine("Action=MoveToTarget");
            sb.AppendLine("Priority=20");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=AttackWhenNear");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=TargetInAttackRange");
            sb.AppendLine("Action=MeleeHit");
            sb.AppendLine("Priority=30");
        }

        private static void AppendRangedKiteRuleSet(StringBuilder sb, int range, int attackDelay, bool fearPause)
        {
            int keepDistance = Math.Max(2, range - 1);

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=MoveToTarget");
            sb.AppendLine("Type=MoveToTarget");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=KeepDistance");
            sb.AppendLine("Type=MoveAwayFromTarget");
            sb.AppendLine("Target=CurrentTarget");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=Shoot");
            sb.AppendLine("Type=RangeAttack");
            sb.AppendLine("Target=CurrentTarget");
            sb.AppendLine($"Distance={range}");
            sb.AppendLine("Delay=400");
            sb.AppendLine("Power=DC");
            sb.AppendLine("PowerRate=100");
            sb.AppendLine("Travel=True");
            sb.AppendLine("Options=RangePacket=True");

            if (fearPause)
            {
                sb.AppendLine();
                sb.AppendLine("[AIAction]");
                sb.AppendLine("Name=FearPause");
                sb.AppendLine("Type=DelayActionTime");
                sb.AppendLine("Delay=3000");
                sb.AppendLine("Options=ActionChance=17");

                sb.AppendLine();
                sb.AppendLine("[AIAction]");
                sb.AppendLine("Name=ShootAndMaybePause");
                sb.AppendLine("Type=Sequence");
                sb.AppendLine("Options=Shoot:1,FearPause:1");
            }

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=KeepDistanceWhenClose");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=TargetDistanceBelow");
            sb.AppendLine($"Value={keepDistance}");
            sb.AppendLine("Action=KeepDistance");
            sb.AppendLine("Priority=10");
            sb.AppendLine("Cooldown=500");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=ShootWhenInRange");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=TargetDistanceBelow");
            sb.AppendLine($"Value={range}");
            sb.AppendLine($"Action={(fearPause ? "ShootAndMaybePause" : "Shoot")}");
            sb.AppendLine("Priority=20");
            sb.AppendLine($"Cooldown={Math.Max(500, attackDelay)}");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=MoveWhenFar");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=TargetDistanceAbove");
            sb.AppendLine($"Value={range}");
            sb.AppendLine("Action=MoveToTarget");
            sb.AppendLine("Priority=30");
        }

        private static void AppendSelfDestructRuleSet(StringBuilder sb, int range)
        {
            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=MoveToTarget");
            sb.AppendLine("Type=MoveToTarget");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=KillSelf");
            sb.AppendLine("Type=KillSelf");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=DeathBurst");
            sb.AppendLine("Type=MassAreaDamage");
            sb.AppendLine("Target=SelfRadius");
            sb.AppendLine($"Radius={range}");
            sb.AppendLine("Delay=800");
            sb.AppendLine("Power=DC");
            sb.AppendLine("PowerRate=100");
            sb.AppendLine("Options=AllowDead=True,IgnoreAttackCooldown=True");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=DieWithoutTarget");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=TargetMissing");
            sb.AppendLine("Action=KillSelf");
            sb.AppendLine("Priority=5");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=ExplodeWhenClose");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=TargetDistanceBelow");
            sb.AppendLine($"Value={range}");
            sb.AppendLine("Action=KillSelf");
            sb.AppendLine("Priority=10");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=MoveWhenFar");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=TargetDistanceAbove");
            sb.AppendLine($"Value={range}");
            sb.AppendLine("Action=MoveToTarget");
            sb.AppendLine("Priority=20");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=BurstOnDeath");
            sb.AppendLine("Trigger=OnDeath");
            sb.AppendLine("Condition=Always");
            sb.AppendLine("Action=DeathBurst");
            sb.AppendLine("Priority=1");
            sb.AppendLine("MaxExecutions=1");
        }

        private static void AppendSpawnAppearRuleSet(StringBuilder sb, int attackDelay)
        {
            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=StartHidden");
            sb.AppendLine("Type=Hide");
            sb.AppendLine("Options=StateOnly=True");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=SpawnEffect");
            sb.AppendLine("Type=MapEffect");
            sb.AppendLine("Target=Self");
            sb.AppendLine("Options=Effect=SummonSkeleton");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=Appear");
            sb.AppendLine("Type=Show");
            sb.AppendLine("Options=ForcePacket=True");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=ScheduleAppear");
            sb.AppendLine("Type=ScheduleAction");
            sb.AppendLine("Delay=1000");
            sb.AppendLine("Options=Appear:1");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=SpawnSequence");
            sb.AppendLine("Type=Sequence");
            sb.AppendLine("Options=StartHidden:1,SpawnEffect:1,ScheduleAppear:1");

            AppendMeleeMovementActions(sb, includeStepAway: false);

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=SpawnVisual");
            sb.AppendLine("Trigger=OnSpawn");
            sb.AppendLine("Condition=Always");
            sb.AppendLine("Action=SpawnSequence");
            sb.AppendLine("Priority=1");
            sb.AppendLine("MaxExecutions=1");

            AppendVisibleMeleeRules(sb, attackDelay);
        }

        private static void AppendHiddenWakeRuleSet(StringBuilder sb, bool canPhase, int attackDelay)
        {
            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=StartHidden");
            sb.AppendLine("Type=Hide");
            sb.AppendLine("Options=StateOnly=True");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=WakeGroup");
            sb.AppendLine("Type=WakeNearby");
            sb.AppendLine("Radius=7");

            if (canPhase)
            {
                sb.AppendLine();
                sb.AppendLine("[AIAction]");
                sb.AppendLine("Name=PhaseHidePacket");
                sb.AppendLine("Type=Hide");
                sb.AppendLine("Options=PacketOnly=True");

                sb.AppendLine();
                sb.AppendLine("[AIAction]");
                sb.AppendLine("Name=PhaseHideState");
                sb.AppendLine("Type=Hide");
                sb.AppendLine("Options=StateOnly=True");

                sb.AppendLine();
                sb.AppendLine("[AIAction]");
                sb.AppendLine("Name=SchedulePhaseHideState");
                sb.AppendLine("Type=ScheduleAction");
                sb.AppendLine("Delay=900");
                sb.AppendLine("Options=PhaseHideState:1");

                sb.AppendLine();
                sb.AppendLine("[AIAction]");
                sb.AppendLine("Name=PhaseOut");
                sb.AppendLine("Type=Sequence");
                sb.AppendLine("Options=PhaseHidePacket:1,SchedulePhaseHideState:1");
            }

            AppendMeleeMovementActions(sb, includeStepAway: true);

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=HideOnSpawn");
            sb.AppendLine("Trigger=OnSpawn");
            sb.AppendLine("Condition=Always");
            sb.AppendLine("Action=StartHidden");
            sb.AppendLine("Priority=1");
            sb.AppendLine("MaxExecutions=1");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=WakeWhenClose");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=Hidden");
            sb.AppendLine("Conditions=TargetDistanceBelow:3");
            sb.AppendLine("Action=WakeGroup");
            sb.AppendLine("Priority=10");

            if (canPhase)
            {
                sb.AppendLine();
                sb.AppendLine("[AIRule]");
                sb.AppendLine("Name=PhaseOutWhenFar");
                sb.AppendLine("Trigger=OnTargetTick");
                sb.AppendLine("Condition=Visible");
                sb.AppendLine("Conditions=TargetDistanceAbove:4");
                sb.AppendLine("Action=PhaseOut");
                sb.AppendLine("Priority=15");
                sb.AppendLine("Chance=3");
                sb.AppendLine("Cooldown=5000");
            }

            AppendVisibleMeleeRules(sb, attackDelay);
        }

        private static void AppendPlantAmbushRuleSet(StringBuilder sb, int attackDelay)
        {
            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=StartHidden");
            sb.AppendLine("Type=Hide");
            sb.AppendLine("Options=StateOnly=True");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=ShowSelf");
            sb.AppendLine("Type=Show");
            sb.AppendLine("Options=ForcePacket=True");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=HideSelf");
            sb.AppendLine("Type=Hide");
            sb.AppendLine("Options=ForcePacket=True");

            AppendMeleeMovementActions(sb, includeStepAway: false);

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=HideOnSpawn");
            sb.AppendLine("Trigger=OnSpawn");
            sb.AppendLine("Condition=Always");
            sb.AppendLine("Action=StartHidden");
            sb.AppendLine("Priority=1");
            sb.AppendLine("MaxExecutions=1");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=ShowWhenClose");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=Hidden");
            sb.AppendLine("Conditions=TargetDistanceBelow:1");
            sb.AppendLine("Action=ShowSelf");
            sb.AppendLine("Priority=10");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=HideWhenFar");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=Visible");
            sb.AppendLine("Conditions=TargetDistanceAbove:5");
            sb.AppendLine("Action=HideSelf");
            sb.AppendLine("Priority=15");
            sb.AppendLine("Cooldown=4000");

            AppendVisibleMeleeRules(sb, attackDelay);
        }

        private static void AppendReviveMeleeRuleSet(StringBuilder sb, int attackDelay)
        {
            AppendBasicMeleeRuleSet(sb);

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=Revive");
            sb.AppendLine("Type=ReviveSelf");
            sb.AppendLine("RandomCount=3");
            sb.AppendLine("Delay=3000");
            sb.AppendLine("PowerRate=100");
            sb.AppendLine("Options=HealthDecay=True,SuppressDrops=True");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=ReviveAfterDeath");
            sb.AppendLine("Trigger=OnDeath");
            sb.AppendLine("Condition=Always");
            sb.AppendLine("Action=Revive");
            sb.AppendLine("Priority=1");
        }

        private static void AppendBossRangeRuleSet(StringBuilder sb, int range, int attackDelay)
        {
            AppendMeleeMovementActions(sb, includeStepAway: true);

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=TargetAreaHit");
            sb.AppendLine("Type=RangeAttack");
            sb.AppendLine("Target=TargetRadius");
            sb.AppendLine("Radius=2");
            sb.AppendLine("MaxTargets=8");
            sb.AppendLine("Delay=400");
            sb.AppendLine("Power=DC");
            sb.AppendLine("PowerRate=100");
            sb.AppendLine("Options=RangePacket=True");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=MassCyclone");
            sb.AppendLine("Type=MassAreaDamage");
            sb.AppendLine("Magic=Cyclone");
            sb.AppendLine($"Radius={range}");
            sb.AppendLine("MaxTargets=10");
            sb.AppendLine("TargetChance=75");
            sb.AppendLine("EmptyLocationChance=3");
            sb.AppendLine("Delay=500");
            sb.AppendLine("Power=DC");
            sb.AppendLine("PowerRate=100");
            sb.AppendLine("Element=Wind");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=RangeChoice");
            sb.AppendLine("Type=WeightedChoice");
            sb.AppendLine("Options=MassCyclone:1,TargetAreaHit:2");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=OutOfRangeTryRange");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=TargetOutOfAttackRange");
            sb.AppendLine("Action=RangeChoice");
            sb.AppendLine("Priority=10");
            sb.AppendLine("Chance=33");
            sb.AppendLine($"Cooldown={Math.Max(500, attackDelay)}");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=SameCellStepAway");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=SameCellAsTarget");
            sb.AppendLine("Action=StepAway");
            sb.AppendLine("Priority=20");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=OutOfRangeMove");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=TargetOutOfAttackRange");
            sb.AppendLine("Action=MoveToTarget");
            sb.AppendLine("Priority=30");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=InRangeMelee");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=TargetInAttackRange");
            sb.AppendLine("Action=MeleeHit");
            sb.AppendLine("Priority=40");
            sb.AppendLine("Chance=80");
            sb.AppendLine($"Cooldown={Math.Max(500, attackDelay)}");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=CanAttackTryRange");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=CanAttack");
            sb.AppendLine("Action=RangeChoice");
            sb.AppendLine("Priority=50");
            sb.AppendLine("Chance=20");
            sb.AppendLine($"Cooldown={Math.Max(500, attackDelay)}");
        }

        private static void AppendTeleportMeleeRuleSet(StringBuilder sb, bool bonusDamage, int teleportCooldown, int attackDelay)
        {
            AppendMeleeMovementActions(sb, includeStepAway: true);

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=TeleportNearTarget");
            sb.AppendLine("Type=TeleportNearTarget");
            sb.AppendLine("Target=CurrentTarget");
            sb.AppendLine("MinOffset=1");
            sb.AppendLine("Radius=1");

            if (bonusDamage)
            {
                sb.AppendLine();
                sb.AppendLine("[AIAction]");
                sb.AppendLine("Name=SetBonus");
                sb.AppendLine("Type=SetCounter");
                sb.AppendLine("CounterName=Bonus");
                sb.AppendLine("FixedCount=1");

                sb.AppendLine();
                sb.AppendLine("[AIAction]");
                sb.AppendLine("Name=ClearBonus");
                sb.AppendLine("Type=SetCounter");
                sb.AppendLine("CounterName=Bonus");
                sb.AppendLine("FixedCount=0");

                sb.AppendLine();
                sb.AppendLine("[AIAction]");
                sb.AppendLine("Name=BonusMelee");
                sb.AppendLine("Type=MeleeAttack");
                sb.AppendLine("Distance=1");
                sb.AppendLine("Delay=400");
                sb.AppendLine("Power=DC");
                sb.AppendLine("PowerRate=200");

                sb.AppendLine();
                sb.AppendLine("[AIAction]");
                sb.AppendLine("Name=BonusHitAndClear");
                sb.AppendLine("Type=Sequence");
                sb.AppendLine("Options=BonusMelee:1,ClearBonus:1");

                sb.AppendLine();
                sb.AppendLine("[AIAction]");
                sb.AppendLine("Name=TeleportAndPrime");
                sb.AppendLine("Type=Sequence");
                sb.AppendLine("Options=TeleportNearTarget:1,SetBonus:1");
            }

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=TryTeleportWhenFar");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=TargetDistanceAbove");
            sb.AppendLine("Value=1");
            sb.AppendLine($"Action={(bonusDamage ? "TeleportAndPrime" : "TeleportNearTarget")}");
            sb.AppendLine("Priority=10");
            sb.AppendLine("Chance=14");
            sb.AppendLine($"Cooldown={Math.Max(1000, teleportCooldown)}");

            if (bonusDamage)
            {
                sb.AppendLine();
                sb.AppendLine("[AIRule]");
                sb.AppendLine("Name=UseBonusMelee");
                sb.AppendLine("Trigger=OnTargetTick");
                sb.AppendLine("Condition=CounterEquals");
                sb.AppendLine("CounterName=Bonus");
                sb.AppendLine("Value=1");
                sb.AppendLine("Conditions=TargetInAttackRange");
                sb.AppendLine("Action=BonusHitAndClear");
                sb.AppendLine("Priority=15");
                sb.AppendLine($"Cooldown={Math.Max(500, attackDelay)}");
            }

            AppendStandardMeleeRules(sb, attackDelay, bonusDamage ? 20 : 15);
        }

        private static void AppendAreaMeleeRuleSet(StringBuilder sb, int radius, bool applyPoison, int attackDelay)
        {
            AppendMeleeMovementActions(sb, includeStepAway: true);

            if (applyPoison)
            {
                sb.AppendLine();
                sb.AppendLine("[AIAction]");
                sb.AppendLine("Name=ApplyGreenPoison");
                sb.AppendLine("Type=ApplyPoison");
                sb.AppendLine("Target=TargetRadius");
                sb.AppendLine($"Radius={radius}");
                sb.AppendLine("PoisonType=Green");
                sb.AppendLine("Power=SC");
                sb.AppendLine("TickRate=2000");
                sb.AppendLine("Duration=20000");
                sb.AppendLine("Options=OnHitChance=100");
            }

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=AreaHit");
            sb.AppendLine("Type=MassAreaDamage");
            sb.AppendLine("Target=SelfRadius");
            sb.AppendLine($"Radius={radius}");
            sb.AppendLine("Delay=400");
            sb.AppendLine("Power=DC");
            sb.AppendLine("PowerRate=100");
            sb.AppendLine(applyPoison ? "Options=OnHitAction=ApplyGreenPoison" : "Options=RangePacket=True");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=AreaWhenClose");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=TargetDistanceBelow");
            sb.AppendLine($"Value={radius}");
            sb.AppendLine("Action=AreaHit");
            sb.AppendLine("Priority=15");
            sb.AppendLine(applyPoison ? "Chance=17" : "Chance=100");
            sb.AppendLine($"Cooldown={Math.Max(500, attackDelay)}");

            AppendStandardMeleeRules(sb, attackDelay, 20);
        }

        private static void AppendMagicWeaknessRuleSet(StringBuilder sb, int attackDelay)
        {
            AppendBasicMeleeRuleSet(sb);

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=ApplyMagicWeakness");
            sb.AppendLine("Type=AddBuff");
            sb.AppendLine("Target=SelfRadius");
            sb.AppendLine("Radius=3");
            sb.AppendLine("BuffType=MagicWeakness");
            sb.AppendLine("Duration=10000");
            sb.AppendLine("TickRate=0");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=DebuffNearby");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=CanAttack");
            sb.AppendLine("Action=ApplyMagicWeakness");
            sb.AppendLine("Priority=5");
            sb.AppendLine($"Cooldown={Math.Max(10000, attackDelay)}");
        }

        private static void AppendSimpleCasterRuleSet(StringBuilder sb, MagicType magic, Element element, bool travel, int attackDelay)
        {
            AppendMeleeMovementActions(sb, includeStepAway: true);

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=CastAttack");
            sb.AppendLine("Type=AttackMagic");
            sb.AppendLine("Target=CurrentTarget");
            sb.AppendLine("Distance=9");
            sb.AppendLine("Delay=400");
            sb.AppendLine($"Magic={magic}");
            sb.AppendLine($"Element={element}");
            sb.AppendLine("Power=DC");
            sb.AppendLine("PowerRate=100");
            sb.AppendLine($"Travel={travel}");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=CastWhenFar");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=TargetDistanceAbove");
            sb.AppendLine("Value=1");
            sb.AppendLine("Conditions=TargetDistanceBelow:9;CanAttack");
            sb.AppendLine("ConditionJoin=And");
            sb.AppendLine("Action=CastAttack");
            sb.AppendLine("Priority=10");
            sb.AppendLine("Chance=50");
            sb.AppendLine($"Cooldown={Math.Max(500, attackDelay)}");

            AppendStandardMeleeRules(sb, attackDelay, 20);
        }

        private static void AppendLightningChoiceCasterRuleSet(StringBuilder sb, int attackDelay)
        {
            AppendMeleeMovementActions(sb, includeStepAway: true);

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=LightningBall");
            sb.AppendLine("Type=AttackMagic");
            sb.AppendLine("Target=CurrentTarget");
            sb.AppendLine("Distance=9");
            sb.AppendLine("Delay=400");
            sb.AppendLine("Magic=LightningBall");
            sb.AppendLine("Element=Lightning");
            sb.AppendLine("Power=DC");
            sb.AppendLine("PowerRate=67");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=ThunderBolt");
            sb.AppendLine("Type=AttackMagic");
            sb.AppendLine("Target=CurrentTarget");
            sb.AppendLine("Distance=9");
            sb.AppendLine("Delay=400");
            sb.AppendLine("Magic=ThunderBolt");
            sb.AppendLine("Element=Lightning");
            sb.AppendLine("Power=DC");
            sb.AppendLine("PowerRate=100");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=LightningWave");
            sb.AppendLine("Type=AttackAoE");
            sb.AppendLine("Target=TargetRadius");
            sb.AppendLine("Radius=1");
            sb.AppendLine("Distance=9");
            sb.AppendLine("Delay=400");
            sb.AppendLine("Magic=LightningWave");
            sb.AppendLine("Element=Lightning");
            sb.AppendLine("Power=DC");
            sb.AppendLine("PowerRate=67");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=LightningChoice");
            sb.AppendLine("Type=WeightedChoice");
            sb.AppendLine("Options=LightningBall:1,ThunderBolt:1,LightningWave:1");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=CastLightningChoice");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=TargetDistanceAbove");
            sb.AppendLine("Value=1");
            sb.AppendLine("Conditions=TargetDistanceBelow:9;CanAttack");
            sb.AppendLine("ConditionJoin=And");
            sb.AppendLine("Action=LightningChoice");
            sb.AppendLine("Priority=10");
            sb.AppendLine("Chance=50");
            sb.AppendLine($"Cooldown={Math.Max(500, attackDelay)}");

            AppendStandardMeleeRules(sb, attackDelay, 20);
        }

        private static void AppendSamaGuardianRuleSet(StringBuilder sb, Element element, MagicType projectile, MagicType line, MagicType area, MagicType burst, MagicType empowered, int attackDelay)
        {
            AppendMeleeMovementActions(sb, includeStepAway: true);

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=Projectile");
            sb.AppendLine("Type=AttackMagic");
            sb.AppendLine("Target=CurrentTarget");
            sb.AppendLine("Distance=10");
            sb.AppendLine("Delay=400");
            sb.AppendLine($"Magic={projectile}");
            sb.AppendLine($"Element={element}");
            sb.AppendLine("Power=DC");
            sb.AppendLine("PowerRate=100");
            sb.AppendLine($"Travel={projectile is MagicType.AdamantineFireBall or MagicType.IceBlades}");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=LineSkill");
            sb.AppendLine("Type=LineAoE");
            sb.AppendLine("Distance=10");
            sb.AppendLine("MinOffset=-2");
            sb.AppendLine("MaxOffset=2");
            sb.AppendLine("Delay=400");
            sb.AppendLine($"Magic={line}");
            sb.AppendLine($"Element={element}");
            sb.AppendLine("Power=DC");
            sb.AppendLine("PowerRate=100");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=SmallArea");
            sb.AppendLine("Type=AttackAoE");
            sb.AppendLine("Target=TargetRadius");
            sb.AppendLine("Radius=2");
            sb.AppendLine("Distance=10");
            sb.AppendLine("Delay=400");
            sb.AppendLine($"Magic={area}");
            sb.AppendLine($"Element={element}");
            sb.AppendLine("Power=DC");
            sb.AppendLine("PowerRate=100");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=BurstArea");
            sb.AppendLine("Type=AttackAoE");
            sb.AppendLine("Target=TargetRadius");
            sb.AppendLine("Radius=3");
            sb.AppendLine("Distance=10");
            sb.AppendLine("Delay=400");
            sb.AppendLine($"Magic={burst}");
            sb.AppendLine($"Element={element}");
            sb.AppendLine("Power=DC");
            sb.AppendLine("PowerRate=100");

            string options = "Projectile:1,LineSkill:1,SmallArea:1,BurstArea:1";
            if (empowered != MagicType.None)
            {
                sb.AppendLine();
                sb.AppendLine("[AIAction]");
                sb.AppendLine("Name=EmpoweredArea");
                sb.AppendLine("Type=AttackAoE");
                sb.AppendLine("Target=TargetRadius");
                sb.AppendLine("Radius=3");
                sb.AppendLine("Distance=10");
                sb.AppendLine("Delay=400");
                sb.AppendLine($"Magic={empowered}");
                sb.AppendLine($"Element={element}");
                sb.AppendLine("Power=DC");
                sb.AppendLine("PowerRate=200");
                options += ",EmpoweredArea:1";
            }

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=ElementChoice");
            sb.AppendLine("Type=WeightedChoice");
            sb.AppendLine($"Options={options}");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=UseElementSkill");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=CanAttack");
            sb.AppendLine("Conditions=TargetDistanceBelow:10");
            sb.AppendLine("ConditionJoin=And");
            sb.AppendLine("Action=ElementChoice");
            sb.AppendLine("Priority=10");
            sb.AppendLine("Chance=20");
            sb.AppendLine($"Cooldown={Math.Max(500, attackDelay)}");

            AppendStandardMeleeRules(sb, attackDelay, 20);
        }

        private static void AppendIcySpiritRuleSet(StringBuilder sb, bool drainMp, int attackDelay)
        {
            AppendMeleeMovementActions(sb, includeStepAway: true);

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=IceRange");
            sb.AppendLine("Type=RangeAttack");
            sb.AppendLine("Target=CurrentTarget");
            sb.AppendLine("Distance=8");
            sb.AppendLine("Delay=400");
            sb.AppendLine("Power=DC");
            sb.AppendLine("PowerRate=100");
            sb.AppendLine($"Element={(drainMp ? Element.Dark : Element.Ice)}");
            sb.AppendLine($"Options=RangePacket=True{(drainMp ? ",DrainMP=True,IgnoreShield=True" : string.Empty)}");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=IceBurst");
            sb.AppendLine("Type=AttackAoE");
            sb.AppendLine("Target=TargetRadius");
            sb.AppendLine("Radius=1");
            sb.AppendLine("Distance=8");
            sb.AppendLine("Delay=400");
            sb.AppendLine("Magic=MonsterIceStorm");
            sb.AppendLine("Element=Ice");
            sb.AppendLine("Power=DC");
            sb.AppendLine("PowerRate=100");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=IceChoice");
            sb.AppendLine("Type=WeightedChoice");
            sb.AppendLine("Options=IceRange:1,IceBurst:1");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=UseIceWhenInRange");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=TargetDistanceBelow");
            sb.AppendLine("Value=8");
            sb.AppendLine("Action=IceChoice");
            sb.AppendLine("Priority=10");
            sb.AppendLine($"Cooldown={Math.Max(500, attackDelay)}");

            AppendStandardMeleeRules(sb, attackDelay, 20);
        }

        private static void AppendFixedMassRangeRuleSet(StringBuilder sb, int range, MagicType magic, Element element, int attackDelay)
        {
            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=MassHit");
            sb.AppendLine("Type=MassAreaDamage");
            sb.AppendLine("Target=AllInView");
            sb.AppendLine($"Radius={range}");
            sb.AppendLine("Delay=500");
            sb.AppendLine($"Magic={magic}");
            sb.AppendLine($"Element={element}");
            sb.AppendLine("Power=DC");
            sb.AppendLine("PowerRate=100");
            sb.AppendLine("MaxTargets=12");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=MassHitWhenTargetExists");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=TargetExists");
            sb.AppendLine("Action=MassHit");
            sb.AppendLine("Priority=10");
            sb.AppendLine($"Cooldown={Math.Max(500, attackDelay)}");
        }

        private static void AppendDeathSelfSpawnRuleSet(StringBuilder sb, string monsterName, int attackDelay)
        {
            AppendBasicMeleeRuleSet(sb);

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=SpawnSelfOnDeath");
            sb.AppendLine("Type=SpawnMinions");
            sb.AppendLine($"SpawnMonster={Quote(monsterName)}");
            sb.AppendLine("FixedCount=20");
            sb.AppendLine("Radius=10");
            sb.AppendLine("MaxTargets=20");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=SpawnSelfOnDeath");
            sb.AppendLine("Trigger=OnDeath");
            sb.AppendLine("Condition=Always");
            sb.AppendLine("Action=SpawnSelfOnDeath");
            sb.AppendLine("Priority=1");
            sb.AppendLine("MaxExecutions=1");
        }

        private static void AppendGhostMageRuleSet(StringBuilder sb, int attackDelay)
        {
            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=StartHidden");
            sb.AppendLine("Type=Hide");
            sb.AppendLine("Options=StateOnly=True");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=WakeEffect");
            sb.AppendLine("Type=MapEffect");
            sb.AppendLine("Target=Self");
            sb.AppendLine("Options=Effect=SummonSkeleton");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=ShowSelf");
            sb.AppendLine("Type=Show");
            sb.AppendLine("Options=ForcePacket=True");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=WakeSequence");
            sb.AppendLine("Type=Sequence");
            sb.AppendLine("Options=WakeEffect:1,ShowSelf:1");

            AppendMeleeMovementActions(sb, includeStepAway: true);

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=HideOnSpawn");
            sb.AppendLine("Trigger=OnSpawn");
            sb.AppendLine("Condition=Always");
            sb.AppendLine("Action=StartHidden");
            sb.AppendLine("Priority=1");
            sb.AppendLine("MaxExecutions=1");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=WakeWhenNear");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=Hidden");
            sb.AppendLine("Conditions=TargetDistanceBelow:5");
            sb.AppendLine("ConditionJoin=And");
            sb.AppendLine("Action=WakeSequence");
            sb.AppendLine("Priority=10");
            sb.AppendLine("Cooldown=3000");

            AppendVisibleMeleeRules(sb, attackDelay);
        }

        private static void AppendWedgeMothLarvaRuleSet(StringBuilder sb, int attackDelay)
        {
            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=FaceTarget");
            sb.AppendLine("Type=FaceTarget");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=SpawnLesserWedgeMoth");
            sb.AppendLine("Type=SpawnMinions");
            sb.AppendLine("FixedCount=1");
            sb.AppendLine("Radius=1");
            sb.AppendLine("Options=MonsterFlag=LesserWedgeMoth");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=AttackSequence");
            sb.AppendLine("Type=Sequence");
            sb.AppendLine("Options=FaceTarget:1,SpawnLesserWedgeMoth:1");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=SpawnWhenTargetExists");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=TargetExists");
            sb.AppendLine("Action=AttackSequence");
            sb.AppendLine("Priority=10");
            sb.AppendLine($"Cooldown={Math.Max(500, attackDelay)}");
        }

        private static void AppendArachnidGrazerRuleSet(StringBuilder sb, int attackDelay, MonsterFlag spawnFlag, int spawnCount)
        {
            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=FaceTarget");
            sb.AppendLine("Type=FaceTarget");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=SpawnLarva");
            sb.AppendLine("Type=SpawnMinions");
            sb.AppendLine($"FixedCount={Math.Max(1, spawnCount)}");
            sb.AppendLine("Radius=3");
            sb.AppendLine($"Options=MonsterFlag={spawnFlag}");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=AttackSequence");
            sb.AppendLine("Type=Sequence");
            sb.AppendLine("Options=FaceTarget:1,SpawnLarva:1");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=SpawnWhenTargetExists");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=TargetExists");
            sb.AppendLine("Action=AttackSequence");
            sb.AppendLine("Priority=10");
            sb.AppendLine($"Cooldown={Math.Max(500, attackDelay)}");
        }

        private static void AppendTornadoRuleSet(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=CollisionHit");
            sb.AppendLine("Type=MassAreaDamage");
            sb.AppendLine("Target=SelfRadius");
            sb.AppendLine("Radius=1");
            sb.AppendLine("Delay=100");
            sb.AppendLine("Power=DC");
            sb.AppendLine("PowerRate=100");
            sb.AppendLine("Options=IgnoreAttackCooldown=True");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=RoamHazard");
            sb.AppendLine("Type=HazardRoam");
            sb.AppendLine("Options=ForwardChance=80,AttackWhenBlocked=True,AttackCurrentCell=True,AttackBlockedCell=False,CollisionAction=CollisionHit,TurnWhenBlocked=True");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=Expire");
            sb.AppendLine("Type=DespawnSelf");
            sb.AppendLine("Options=DecreaseAliveCount=False");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=ScheduleExpire");
            sb.AppendLine("Type=ScheduleAction");
            sb.AppendLine("Delay=10000");
            sb.AppendLine("Options=Expire:1");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=ScheduleExpireOnSpawn");
            sb.AppendLine("Trigger=OnSpawn");
            sb.AppendLine("Condition=Always");
            sb.AppendLine("Action=ScheduleExpire");
            sb.AppendLine("Priority=1");
            sb.AppendLine("MaxExecutions=1");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=RoamEveryTick");
            sb.AppendLine("Trigger=OnTick");
            sb.AppendLine("Condition=Always");
            sb.AppendLine("Action=RoamHazard");
            sb.AppendLine("Priority=10");
            sb.AppendLine("Cooldown=1000");
        }

        private static void AppendPortalRuleSet(StringBuilder sb, string specialRegion, bool includeMonsters, string openMessage, string closeMessage, int viewRange)
        {
            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=OpenMessage");
            sb.AppendLine("Type=BroadcastMessage");
            sb.AppendLine($"Options=Scope=Map,Message={Quote(openMessage)}");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=CloseMessage");
            sb.AppendLine("Type=BroadcastMessage");
            sb.AppendLine($"Options=Scope=Map,Message={Quote(closeMessage)}");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=TeleportNearby");
            sb.AppendLine("Type=TeleportTargetsToRegion");
            sb.AppendLine($"Radius={Math.Max(1, viewRange)}");
            sb.AppendLine($"Options=SpecialRegion={specialRegion},Players=True,Monsters={includeMonsters},IncludeMonsters={includeMonsters},WildMonsters={includeMonsters},IncludeWildMonsters={includeMonsters},Pets={includeMonsters}");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=DespawnPortal");
            sb.AppendLine("Type=DespawnSelf");
            sb.AppendLine("Options=DecreaseAliveCount=True");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=CloseAndDespawn");
            sb.AppendLine("Type=Sequence");
            sb.AppendLine("Options=CloseMessage:1,DespawnPortal:1");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=ScheduleClose");
            sb.AppendLine("Type=ScheduleAction");
            sb.AppendLine("Delay=1200000");
            sb.AppendLine("Options=CloseAndDespawn:1");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=OpenSequence");
            sb.AppendLine("Type=Sequence");
            sb.AppendLine("Options=OpenMessage:1,ScheduleClose:1");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=OpenOnSpawn");
            sb.AppendLine("Trigger=OnSpawn");
            sb.AppendLine("Condition=Always");
            sb.AppendLine("Action=OpenSequence");
            sb.AppendLine("Priority=1");
            sb.AppendLine("MaxExecutions=1");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=TeleportNearbyTargets");
            sb.AppendLine("Trigger=OnTick");
            sb.AppendLine("Condition=Always");
            sb.AppendLine("Action=TeleportNearby");
            sb.AppendLine("Priority=10");
            sb.AppendLine("Cooldown=3000");
        }

        private static void AppendBanyoLordGuzakRuleSet(StringBuilder sb, int attackDelay)
        {
            AppendMeleeMovementActions(sb, includeStepAway: true);

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=TeleportNearTarget");
            sb.AppendLine("Type=TeleportNearTarget");
            sb.AppendLine("Target=CurrentTarget");
            sb.AppendLine("Radius=1");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=PurifyTargets");
            sb.AppendLine("Type=PurifyTargets");
            sb.AppendLine("Target=AllInView");
            sb.AppendLine($"Radius={Config.MaxViewRange}");
            sb.AppendLine("Delay=500");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=SpawnBanyoCaptains");
            sb.AppendLine("Type=SpawnMinions");
            sb.AppendLine("FixedCount=5");
            sb.AppendLine("Radius=10");
            sb.AppendLine("Options=MonsterFlag=BanyoCaptain,LeashRadius=10");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=PurifyEveryTwentySeconds");
            sb.AppendLine("Trigger=OnTick");
            sb.AppendLine("Condition=Always");
            sb.AppendLine("Action=PurifyTargets");
            sb.AppendLine("Priority=10");
            sb.AppendLine("Cooldown=20000");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=SpawnCaptains");
            sb.AppendLine("Trigger=OnTick");
            sb.AppendLine("Condition=Always");
            sb.AppendLine("Action=SpawnBanyoCaptains");
            sb.AppendLine("Priority=20");
            sb.AppendLine("Cooldown=45000");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=TeleportWhenFar");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=TargetDistanceAbove");
            sb.AppendLine("Value=1");
            sb.AppendLine("Action=TeleportNearTarget");
            sb.AppendLine("Priority=30");
            sb.AppendLine("Chance=14");
            sb.AppendLine("Cooldown=5000");

            AppendStandardMeleeRules(sb, attackDelay, 40);
        }

        private static void AppendBanyoCaptainRuleSet(StringBuilder sb, int attackDelay)
        {
            AppendMeleeMovementActions(sb, includeStepAway: true);

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=HeavyMelee");
            sb.AppendLine("Type=MeleeAttack");
            sb.AppendLine("Distance=1");
            sb.AppendLine("Delay=400");
            sb.AppendLine("Power=DC");
            sb.AppendLine("PowerRate=200");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=MeleeChoice");
            sb.AppendLine("Type=WeightedChoice");
            sb.AppendLine("Options=MeleeHit:9,HeavyMelee:1");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=ThunderBolt");
            sb.AppendLine("Type=AttackMagic");
            sb.AppendLine("Target=CurrentTarget");
            sb.AppendLine("Distance=9");
            sb.AppendLine("Delay=400");
            sb.AppendLine("Magic=ThunderBolt");
            sb.AppendLine("Element=Lightning");
            sb.AppendLine("Power=Fixed");
            sb.AppendLine("FixedPower=150");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=PurifyTarget");
            sb.AppendLine("Type=PurifyTargets");
            sb.AppendLine("Target=CurrentTarget");
            sb.AppendLine("Distance=9");
            sb.AppendLine("Delay=500");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=CastChoice");
            sb.AppendLine("Type=WeightedChoice");
            sb.AppendLine("Options=ThunderBolt:4,PurifyTarget:1");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=CastPeriodic");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=TargetDistanceBelow");
            sb.AppendLine("Value=9");
            sb.AppendLine("Action=CastChoice");
            sb.AppendLine("Priority=10");
            sb.AppendLine("Cooldown=3000");

            AppendStandardMeleeRules(sb, attackDelay, 20);

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=UseMeleeChoiceWhenNear");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=TargetInAttackRange");
            sb.AppendLine("Action=MeleeChoice");
            sb.AppendLine("Priority=35");
            sb.AppendLine($"Cooldown={Math.Max(500, attackDelay)}");
        }

        private static void AppendEscortCommanderRuleSet(StringBuilder sb, int attackDelay)
        {
            AppendMeleeMovementActions(sb, includeStepAway: true);

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=ThunderBolt");
            sb.AppendLine("Type=AttackMagic");
            sb.AppendLine("Target=CurrentTarget");
            sb.AppendLine("Distance=9");
            sb.AppendLine("Delay=400");
            sb.AppendLine("Magic=ThunderBolt");
            sb.AppendLine("Element=Lightning");
            sb.AppendLine("Power=DC");
            sb.AppendLine("PowerRate=100");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=ThunderStorm");
            sb.AppendLine("Type=MassAreaDamage");
            sb.AppendLine("Target=SelfRadius");
            sb.AppendLine("Radius=2");
            sb.AppendLine("Delay=500");
            sb.AppendLine("Magic=MonsterThunderStorm");
            sb.AppendLine("Element=Lightning");
            sb.AppendLine("Power=DC");
            sb.AppendLine("PowerRate=100");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=CloseThunderChoice");
            sb.AppendLine("Type=WeightedChoice");
            sb.AppendLine("Options=ThunderStorm:1,ThunderBolt:1");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=CloseThunder");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=TargetDistanceBelow");
            sb.AppendLine("Value=2");
            sb.AppendLine("Action=CloseThunderChoice");
            sb.AppendLine("Priority=10");
            sb.AppendLine("Chance=50");
            sb.AppendLine($"Cooldown={Math.Max(500, attackDelay)}");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=FarThunder");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=TargetDistanceBelow");
            sb.AppendLine("Value=9");
            sb.AppendLine("Action=ThunderBolt");
            sb.AppendLine("Priority=20");
            sb.AppendLine("Chance=50");
            sb.AppendLine($"Cooldown={Math.Max(500, attackDelay)}");

            AppendStandardMeleeRules(sb, attackDelay, 30);
        }

        private static void AppendWingedHorrorRuleSet(StringBuilder sb, int attackDelay)
        {
            AppendMeleeMovementActions(sb, includeStepAway: true);

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=MassLightningBall");
            sb.AppendLine("Type=MassAreaDamage");
            sb.AppendLine("Target=AllInView");
            sb.AppendLine($"Radius={Config.MaxViewRange}");
            sb.AppendLine("Delay=500");
            sb.AppendLine("Magic=LightningBall");
            sb.AppendLine("Element=Lightning");
            sb.AppendLine("Power=DC");
            sb.AppendLine("PowerRate=100");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=LightningBeam");
            sb.AppendLine("Type=LineAoE");
            sb.AppendLine("Distance=10");
            sb.AppendLine("MinOffset=0");
            sb.AppendLine("MaxOffset=8");
            sb.AppendLine("Delay=400");
            sb.AppendLine("Magic=LightningBeam");
            sb.AppendLine("Element=Lightning");
            sb.AppendLine("Power=DC");
            sb.AppendLine("PowerRate=100");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=MassThunderBolt");
            sb.AppendLine("Type=MassAreaDamage");
            sb.AppendLine("Target=AllInView");
            sb.AppendLine($"Radius={Config.MaxViewRange}");
            sb.AppendLine("Delay=500");
            sb.AppendLine("Magic=ThunderBolt");
            sb.AppendLine("Element=Lightning");
            sb.AppendLine("Power=DC");
            sb.AppendLine("PowerRate=100");
            sb.AppendLine("TargetChance=50");
            sb.AppendLine("EmptyLocationChance=2");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=RangeChoice");
            sb.AppendLine("Type=WeightedChoice");
            sb.AppendLine("Options=MassLightningBall:1,LightningBeam:1,MassThunderBolt:1");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=UseRangeChoice");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=CanAttack");
            sb.AppendLine("Action=RangeChoice");
            sb.AppendLine("Priority=10");
            sb.AppendLine("Chance=50");
            sb.AppendLine($"Cooldown={Math.Max(500, attackDelay)}");

            AppendStandardMeleeRules(sb, attackDelay, 20);
        }

        private static void AppendChaosKnightRuleSet(StringBuilder sb, int attackDelay)
        {
            AppendMeleeMovementActions(sb, includeStepAway: true);

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=PoisonousCloud");
            sb.AppendLine("Type=SpawnSpellObject");
            sb.AppendLine("Target=SelfRadius");
            sb.AppendLine("Radius=2");
            sb.AppendLine("Magic=PoisonousCloud");
            sb.AppendLine("FixedCount=1");
            sb.AppendLine("FixedPower=20");
            sb.AppendLine("TickRate=20000");
            sb.AppendLine("Options=Effect=PoisonousCloud,VisibleOriginOnly=True");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=CastPoisonousCloud");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=TargetInAttackRange");
            sb.AppendLine("Action=PoisonousCloud");
            sb.AppendLine("Priority=10");
            sb.AppendLine("Cooldown=20000");

            AppendStandardMeleeRules(sb, attackDelay, 20);
        }

        private static void AppendJinchonDevilRuleSet(StringBuilder sb, int attackDelay, int deathCloudCooldown, int deathCloudDuration, int deathCloudDurationRandom)
        {
            AppendMeleeMovementActions(sb, includeStepAway: true);

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=DeathCloud");
            sb.AppendLine("Type=SpawnSpellObject");
            sb.AppendLine("Target=AllInView");
            sb.AppendLine("Radius=2");
            sb.AppendLine("Duration=" + deathCloudDuration);
            sb.AppendLine("Magic=None");
            sb.AppendLine("Options=Effect=MonsterDeathCloud,VisibleOriginOnly=True,BroadcastMagic=True,PerTarget=True" + (deathCloudDurationRandom > 0 ? $",DurationRandom={deathCloudDurationRandom}" : string.Empty));

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=LineHit");
            sb.AppendLine("Type=LineAttack");
            sb.AppendLine("Distance=3");
            sb.AppendLine("Delay=400");
            sb.AppendLine("Power=DC");
            sb.AppendLine("PowerRate=100");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=CloseLineChoice");
            sb.AppendLine("Type=WeightedChoice");
            sb.AppendLine("Options=LineHit:1,MeleeHit:2");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=CastDeathCloud");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=CanAttack");
            sb.AppendLine("Action=DeathCloud");
            sb.AppendLine("Priority=10");
            sb.AppendLine($"Cooldown={deathCloudCooldown}");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=LineWhenAligned");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=TargetInLine");
            sb.AppendLine("Value=3");
            sb.AppendLine("Action=CloseLineChoice");
            sb.AppendLine("Priority=20");
            sb.AppendLine($"Cooldown={Math.Max(500, attackDelay)}");

            AppendStandardMeleeRules(sb, attackDelay, 30);
        }

        private static void AppendSamaScorcerRuleSet(StringBuilder sb, int attackDelay)
        {
            AppendMeleeMovementActions(sb, includeStepAway: true);

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=TeleportTargetNearSelf");
            sb.AppendLine("Type=TeleportTarget");
            sb.AppendLine("Target=CurrentTarget");
            sb.AppendLine("Radius=2");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=PullDistantTarget");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=TargetDistanceAbove");
            sb.AppendLine("Value=5");
            sb.AppendLine("Action=TeleportTargetNearSelf");
            sb.AppendLine("Priority=10");
            sb.AppendLine("Chance=20");
            sb.AppendLine($"Cooldown={Math.Max(500, attackDelay)}");

            AppendStandardMeleeRules(sb, attackDelay, 20);
        }

        private static void AppendSamaProphetRuleSet(StringBuilder sb, int attackDelay, int viewRange)
        {
            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=SpawnSamaScorcer");
            sb.AppendLine("Type=SpawnMinions");
            sb.AppendLine("FixedCount=1");
            sb.AppendLine("Radius=4");
            sb.AppendLine("Options=MonsterFlag=SamaSorcerer,OriginDirection=DownLeft,OriginDistance=5");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=RecallSamaScorcer");
            sb.AppendLine("Type=RecallMonstersByFlag");
            sb.AppendLine("Radius=1");
            sb.AppendLine($"Distance={Math.Max(1, viewRange - 3)}");
            sb.AppendLine("Options=MonsterFlag=SamaSorcerer,OriginDirection=DownLeft,OriginDistance=2");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=TeleportTargetNearGuard");
            sb.AppendLine("Type=TeleportTarget");
            sb.AppendLine("Target=CurrentTarget");
            sb.AppendLine("Radius=1");
            sb.AppendLine("Options=OriginDirection=DownLeft,OriginDistance=2");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=ProphetFire");
            sb.AppendLine("Type=AttackAoE");
            sb.AppendLine("Target=CurrentTargetLocation");
            sb.AppendLine("Radius=1");
            sb.AppendLine("Distance=15");
            sb.AppendLine("Delay=400");
            sb.AppendLine("Magic=SamaProphetFire");
            sb.AppendLine("Element=Fire");
            sb.AppendLine("Power=DC");
            sb.AppendLine("PowerRate=100");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=ProphetWind");
            sb.AppendLine("Type=AttackAoE");
            sb.AppendLine("Target=CurrentTargetLocation");
            sb.AppendLine("Radius=1");
            sb.AppendLine("Distance=15");
            sb.AppendLine("Delay=400");
            sb.AppendLine("Magic=SamaProphetWind");
            sb.AppendLine("Element=Ice");
            sb.AppendLine("Power=DC");
            sb.AppendLine("PowerRate=100");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=ProphetLightning");
            sb.AppendLine("Type=AttackAoE");
            sb.AppendLine("Target=CurrentTargetLocation");
            sb.AppendLine("Radius=1");
            sb.AppendLine("Distance=15");
            sb.AppendLine("Delay=400");
            sb.AppendLine("Magic=SamaProphetLightning");
            sb.AppendLine("Element=Lightning");
            sb.AppendLine("Power=DC");
            sb.AppendLine("PowerRate=100");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=ProphetChoice");
            sb.AppendLine("Type=WeightedChoice");
            sb.AppendLine("Options=ProphetFire:1,ProphetWind:1,ProphetLightning:1");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=SpawnGuardOnSpawn");
            sb.AppendLine("Trigger=OnSpawn");
            sb.AppendLine("Condition=Always");
            sb.AppendLine("Action=SpawnSamaScorcer");
            sb.AppendLine("Priority=1");
            sb.AppendLine("MaxExecutions=1");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=RecallGuard");
            sb.AppendLine("Trigger=OnTick");
            sb.AppendLine("Condition=Always");
            sb.AppendLine("Action=RecallSamaScorcer");
            sb.AppendLine("Priority=10");
            sb.AppendLine("Cooldown=10000");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=PullDistantTarget");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=TargetDistanceAbove");
            sb.AppendLine("Value=5");
            sb.AppendLine("Action=TeleportTargetNearGuard");
            sb.AppendLine("Priority=20");
            sb.AppendLine("Chance=20");
            sb.AppendLine($"Cooldown={Math.Max(500, attackDelay)}");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=CastProphetSkill");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=TargetDistanceBelow");
            sb.AppendLine("Value=15");
            sb.AppendLine("Action=ProphetChoice");
            sb.AppendLine("Priority=30");
            sb.AppendLine($"Cooldown={Math.Max(500, attackDelay)}");
        }

        private static void AppendQueenOfDawnRuleSet(StringBuilder sb, int attackDelay)
        {
            AppendMeleeMovementActions(sb, includeStepAway: true);

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=TeleportAllTargetsRandom");
            sb.AppendLine("Type=TeleportTargetsRandom");
            sb.AppendLine("Options=AllMap=True,Attempts=25");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=TeleportNearTarget");
            sb.AppendLine("Type=TeleportNearTarget");
            sb.AppendLine("Target=CurrentTarget");
            sb.AppendLine("Radius=1");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=CloseAreaHit");
            sb.AppendLine("Type=MassAreaDamage");
            sb.AppendLine("Target=SelfRadius");
            sb.AppendLine("Radius=2");
            sb.AppendLine("Delay=400");
            sb.AppendLine("Power=DC");
            sb.AppendLine("PowerRate=100");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=AttackChoice");
            sb.AppendLine("Type=WeightedChoice");
            sb.AppendLine("Options=MeleeHit:4,CloseAreaHit:1");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=TeleportAllTargetsEveryMinute");
            sb.AppendLine("Trigger=OnTick");
            sb.AppendLine("Condition=Always");
            sb.AppendLine("Action=TeleportAllTargetsRandom");
            sb.AppendLine("Priority=10");
            sb.AppendLine("Cooldown=60000");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=TeleportNearTargetWhenFar");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=TargetDistanceAbove");
            sb.AppendLine("Value=1");
            sb.AppendLine("Action=TeleportNearTarget");
            sb.AppendLine("Priority=10");
            sb.AppendLine("Chance=10");
            sb.AppendLine("Cooldown=1000");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=UseAttackChoice");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=TargetInAttackRange");
            sb.AppendLine("Action=AttackChoice");
            sb.AppendLine("Priority=20");
            sb.AppendLine($"Cooldown={Math.Max(500, attackDelay)}");

            AppendStandardMeleeRules(sb, attackDelay, 30);
        }

        private static void AppendQuartzTurtleSubRuleSet(StringBuilder sb, int attackDelay)
        {
            AppendMeleeMovementActions(sb, includeStepAway: true);

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=SpawnQuartzMiniTurtles");
            sb.AppendLine("Type=SpawnMinions");
            sb.AppendLine("FixedCount=10");
            sb.AppendLine("Radius=10");
            sb.AppendLine("Options=MonsterFlag=QuartzMiniTurtle");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=SpawnMiniTurtles");
            sb.AppendLine("Trigger=OnTick");
            sb.AppendLine("Condition=Always");
            sb.AppendLine("Action=SpawnQuartzMiniTurtles");
            sb.AppendLine("Priority=10");
            sb.AppendLine("Cooldown=20000");

            AppendStandardMeleeRules(sb, attackDelay, 20);
        }

        private static void AppendMonasteryBossRuleSet(StringBuilder sb, int attackDelay)
        {
            AppendMeleeMovementActions(sb, includeStepAway: true);

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=GreenSludgeBall");
            sb.AppendLine("Type=AttackMagic");
            sb.AppendLine("Target=CurrentTarget");
            sb.AppendLine("Distance=9");
            sb.AppendLine("Delay=400");
            sb.AppendLine("Magic=GreenSludgeBall");
            sb.AppendLine("Element=Wind");
            sb.AppendLine("Power=DC");
            sb.AppendLine("PowerRate=100");
            sb.AppendLine("Travel=True");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=SpawnSacrificeOnDeath");
            sb.AppendLine("Type=SpawnMinions");
            sb.AppendLine("FixedCount=1");
            sb.AppendLine("Radius=6");
            sb.AppendLine("Options=MonsterFlag=Sacrifice");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=CastGreenSludgeBall");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=TargetDistanceBelow");
            sb.AppendLine("Value=9");
            sb.AppendLine("Action=GreenSludgeBall");
            sb.AppendLine("Priority=10");
            sb.AppendLine($"Cooldown={Math.Max(500, attackDelay)}");

            AppendStandardMeleeRules(sb, attackDelay, 20);

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=SpawnSacrificeOnDeath");
            sb.AppendLine("Trigger=OnDeath");
            sb.AppendLine("Condition=Always");
            sb.AppendLine("Action=SpawnSacrificeOnDeath");
            sb.AppendLine("Priority=1");
            sb.AppendLine("MaxExecutions=1");
        }

        private static void AppendShinsuRuleSet(StringBuilder sb, int attackDelay)
        {
            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=StartHidden");
            sb.AppendLine("Type=Hide");
            sb.AppendLine("Options=StateOnly=True");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=SpawnEffect");
            sb.AppendLine("Type=MapEffect");
            sb.AppendLine("Target=Self");
            sb.AppendLine("Options=Effect=SummonShinsu");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=Appear");
            sb.AppendLine("Type=Show");
            sb.AppendLine("Options=ForcePacket=True");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=ScheduleAppear");
            sb.AppendLine("Type=ScheduleAction");
            sb.AppendLine("Delay=1000");
            sb.AppendLine("Options=Appear:1");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=SpawnSequence");
            sb.AppendLine("Type=Sequence");
            sb.AppendLine("Options=StartHidden:1,SpawnEffect:1,ScheduleAppear:1");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=MoveToTarget");
            sb.AppendLine("Type=MoveToTarget");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=LineHit");
            sb.AppendLine("Type=LineAttack");
            sb.AppendLine("Distance=2");
            sb.AppendLine("Delay=400");
            sb.AppendLine("Power=DC");
            sb.AppendLine("PowerRate=100");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=SpawnVisual");
            sb.AppendLine("Trigger=OnSpawn");
            sb.AppendLine("Condition=Always");
            sb.AppendLine("Action=SpawnSequence");
            sb.AppendLine("Priority=1");
            sb.AppendLine("MaxExecutions=1");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=LineWhenAligned");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=Visible");
            sb.AppendLine("Conditions=TargetInLine:2");
            sb.AppendLine("ConditionJoin=And");
            sb.AppendLine("Action=LineHit");
            sb.AppendLine("Priority=20");
            sb.AppendLine($"Cooldown={Math.Max(500, attackDelay)}");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=MoveWhenNotAligned");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=Visible");
            sb.AppendLine("Conditions=TargetOutOfLine:2");
            sb.AppendLine("ConditionJoin=And");
            sb.AppendLine("Action=MoveToTarget");
            sb.AppendLine("Priority=30");
        }

        private static void AppendTerracottaSubRuleSet(StringBuilder sb, int attackDelay)
        {
            AppendHiddenWakeRuleSet(sb, canPhase: true, attackDelay);

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=ConeHit");
            sb.AppendLine("Type=LineAttack");
            sb.AppendLine("Distance=2");
            sb.AppendLine("MinOffset=-1");
            sb.AppendLine("MaxOffset=1");
            sb.AppendLine("Delay=400");
            sb.AppendLine("Power=DC");
            sb.AppendLine("PowerRate=100");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=ConeWhenVisibleClose");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=Visible");
            sb.AppendLine("Conditions=TargetDistanceBelow:2");
            sb.AppendLine("ConditionJoin=And");
            sb.AppendLine("Action=ConeHit");
            sb.AppendLine("Priority=35");
            sb.AppendLine("Chance=50");
            sb.AppendLine($"Cooldown={Math.Max(500, attackDelay)}");
        }

        private static void AppendTerracottaBossRuleSet(StringBuilder sb, int attackDelay)
        {
            AppendMeleeMovementActions(sb, includeStepAway: true);

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=DarkLine");
            sb.AppendLine("Type=LineAoE");
            sb.AppendLine("Distance=12");
            sb.AppendLine("MinOffset=1");
            sb.AppendLine("MaxOffset=1");
            sb.AppendLine("Delay=400");
            sb.AppendLine("Magic=None");
            sb.AppendLine("Element=Dark");
            sb.AppendLine("Power=DC");
            sb.AppendLine("PowerRate=100");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=MeleeArea");
            sb.AppendLine("Type=MassAreaDamage");
            sb.AppendLine("Target=TargetRadius");
            sb.AppendLine("Radius=2");
            sb.AppendLine("Distance=3");
            sb.AppendLine("Delay=400");
            sb.AppendLine("Power=MC");
            sb.AppendLine("PowerRate=100");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=AttackChoice");
            sb.AppendLine("Type=WeightedChoice");
            sb.AppendLine("Options=DarkLine:4,MeleeArea:1");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=AttackWhenInRange");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=TargetDistanceBelow");
            sb.AppendLine("Value=11");
            sb.AppendLine("Action=AttackChoice");
            sb.AppendLine("Priority=10");
            sb.AppendLine($"Cooldown={Math.Max(500, attackDelay)}");

            AppendStandardMeleeRules(sb, attackDelay, 20);
        }

        private static void AppendLineAoERuleSet(StringBuilder sb, int distance, int minOffset, int maxOffset, MagicType magic, Element element, int attackDelay)
        {
            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=LineAoE");
            sb.AppendLine("Type=LineAoE");
            sb.AppendLine($"Distance={distance}");
            sb.AppendLine($"MinOffset={minOffset}");
            sb.AppendLine($"MaxOffset={maxOffset}");
            sb.AppendLine("Delay=400");
            sb.AppendLine($"Magic={magic}");
            sb.AppendLine($"Element={element}");
            sb.AppendLine("Power=DC");
            sb.AppendLine("PowerRate=100");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=LineAoEWhenTargetExists");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=TargetExists");
            sb.AppendLine("Action=LineAoE");
            sb.AppendLine("Priority=10");
            sb.AppendLine($"Cooldown={Math.Max(500, attackDelay)}");
        }

        private static void AppendHealerAntRuleSet(StringBuilder sb, int attackDelay)
        {
            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=SearchInjuredAlly");
            sb.AppendLine("Type=SearchTarget");
            sb.AppendLine("Target=LowestHealthAllyInView");
            sb.AppendLine($"Radius={Globals.MagicRange}");
            sb.AppendLine("Options=SearchAllies=True,RequireInjured=True,RequireMissingBuff=Heal");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=MoveToTarget");
            sb.AppendLine("Type=MoveToTarget");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=HealAlly");
            sb.AppendLine("Type=AddBuff");
            sb.AppendLine("Target=CurrentTarget");
            sb.AppendLine("BuffType=Heal");
            sb.AppendLine("Duration=86400000");
            sb.AppendLine("TickRate=1000");
            sb.AppendLine("Options=RequireInjured=True,RequireMissingBuff=Heal,BuffStatsFromSelf=Healing:Healing:100|HealingCap:HealingCap:100");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=ClearTarget");
            sb.AppendLine("Type=ClearTarget");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=HealAndClear");
            sb.AppendLine("Type=Sequence");
            sb.AppendLine("Options=HealAlly:1,ClearTarget:1");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=FindAllyWhenIdle");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=TargetMissing");
            sb.AppendLine("Action=SearchInjuredAlly");
            sb.AppendLine("Priority=10");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=MoveToAlly");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=TargetOutOfAttackRange");
            sb.AppendLine("Action=MoveToTarget");
            sb.AppendLine("Priority=20");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=HealWhenInRange");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=TargetInAttackRange");
            sb.AppendLine("Action=HealAndClear");
            sb.AppendLine("Priority=30");
            sb.AppendLine($"Cooldown={Math.Max(500, attackDelay)}");
        }

        private static void AppendLordNiJaeRuleSet(StringBuilder sb, int attackDelay)
        {
            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=StartHidden");
            sb.AppendLine("Type=Hide");
            sb.AppendLine("Options=StateOnly=True");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=ShowSelf");
            sb.AppendLine("Type=Show");
            sb.AppendLine("Options=ForcePacket=True");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=HideSelf");
            sb.AppendLine("Type=Hide");
            sb.AppendLine("Options=ForcePacket=True");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=GreenPoison");
            sb.AppendLine("Type=ApplyPoison");
            sb.AppendLine("Target=CurrentTarget");
            sb.AppendLine("PoisonType=Green");
            sb.AppendLine("Power=SC");
            sb.AppendLine("PowerRate=100");
            sb.AppendLine("Duration=20000");
            sb.AppendLine("TickRate=2000");
            sb.AppendLine("Options=ActionChance=20");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=ParalysisPoison");
            sb.AppendLine("Type=ApplyPoison");
            sb.AppendLine("Target=CurrentTarget");
            sb.AppendLine("PoisonType=Paralysis");
            sb.AppendLine("Duration=5000");
            sb.AppendLine("TickRate=5000");
            sb.AppendLine("Options=ActionChance=10");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=HitPoisons");
            sb.AppendLine("Type=Sequence");
            sb.AppendLine("Options=GreenPoison:1,ParalysisPoison:1");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=MassStrike");
            sb.AppendLine("Type=MassAreaDamage");
            sb.AppendLine("Target=AllInView");
            sb.AppendLine($"Radius={Globals.MagicRange}");
            sb.AppendLine("Delay=500");
            sb.AppendLine("Power=DC");
            sb.AppendLine("PowerRate=100");
            sb.AppendLine("Options=OnHitAction=HitPoisons");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=HideOnSpawn");
            sb.AppendLine("Trigger=OnSpawn");
            sb.AppendLine("Condition=Always");
            sb.AppendLine("Action=StartHidden");
            sb.AppendLine("Priority=1");
            sb.AppendLine("MaxExecutions=1");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=ShowWhenTargetClose");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=Hidden");
            sb.AppendLine("Conditions=TargetDistanceBelow:5");
            sb.AppendLine("Action=ShowSelf");
            sb.AppendLine("Priority=10");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=HideWhenTargetFar");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=Visible");
            sb.AppendLine("Conditions=TargetDistanceAbove:5");
            sb.AppendLine("Action=HideSelf");
            sb.AppendLine("Priority=15");
            sb.AppendLine("Cooldown=4000");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=MassStrikeWhenVisible");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=Visible");
            sb.AppendLine("Conditions=TargetExists");
            sb.AppendLine("Action=MassStrike");
            sb.AppendLine("Priority=20");
            sb.AppendLine($"Cooldown={Math.Max(500, attackDelay)}");
        }

        private static void AppendFrostLordHwaRuleSet(StringBuilder sb, int attackDelay)
        {
            AppendMeleeMovementActions(sb, includeStepAway: true);

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=TeleportNearTarget");
            sb.AppendLine("Type=TeleportNearTarget");
            sb.AppendLine("Target=CurrentTarget");
            sb.AppendLine("Radius=1");
            sb.AppendLine($"Distance={Config.MaxViewRange}");

            foreach (PoisonType poison in new[] { PoisonType.Abyss, PoisonType.Silenced, PoisonType.Red, PoisonType.WraithGrip })
            {
                sb.AppendLine();
                sb.AppendLine("[AIAction]");
                sb.AppendLine($"Name=Apply{poison}");
                sb.AppendLine("Type=ApplyPoison");
                sb.AppendLine("Target=AllInView");
                sb.AppendLine($"Radius={Config.MaxViewRange}");
                sb.AppendLine($"PoisonType={poison}");
                sb.AppendLine("Duration=10000");
                sb.AppendLine(poison == PoisonType.WraithGrip ? "TickRate=5000" : "TickRate=10000");
            }

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=PullTargets");
            sb.AppendLine("Type=TeleportTargetsRandom");
            sb.AppendLine("Target=AllInView");
            sb.AppendLine($"Radius={Config.MaxViewRange}");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=FullControl");
            sb.AppendLine("Type=Sequence");
            sb.AppendLine("Options=PullTargets:1,ApplyAbyss:1,ApplySilenced:1,ApplyRed:1,ApplyWraithGrip:1");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=TeleportWhenFar");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=TargetDistanceAbove");
            sb.AppendLine("Value=3");
            sb.AppendLine("Action=TeleportNearTarget");
            sb.AppendLine("Priority=10");
            sb.AppendLine("Cooldown=5000");

            AppendStandardMeleeRules(sb, attackDelay, 20);

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=MinuteControl");
            sb.AppendLine("Trigger=OnTick");
            sb.AppendLine("Condition=TargetCountAtLeast");
            sb.AppendLine("Value=1");
            sb.AppendLine($"Radius={Config.MaxViewRange}");
            sb.AppendLine("Action=FullControl");
            sb.AppendLine("Priority=1");
            sb.AppendLine("Cooldown=60000");
        }

        private static void AppendSunFeralWarriorRuleSet(StringBuilder sb, int attackDelay)
        {
            AppendMeleeMovementActions(sb, includeStepAway: true);

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=SpawnFlames");
            sb.AppendLine("Type=SpawnMinions");
            sb.AppendLine("FixedCount=2");
            sb.AppendLine("RandomCount=6");
            sb.AppendLine("Radius=6");
            sb.AppendLine("Options=MonsterFlags=FerociousFlameDemon:5|FlameDemon:1");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=FireBall");
            sb.AppendLine("Type=AttackMagic");
            sb.AppendLine("Magic=FireBall");
            sb.AppendLine("Element=Fire");
            sb.AppendLine($"Distance={Globals.MagicRange}");
            sb.AppendLine("Delay=400");
            sb.AppendLine("Power=DC");
            sb.AppendLine("PowerRate=100");
            sb.AppendLine("Travel=True");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=FireStorm");
            sb.AppendLine("Type=AttackAoE");
            sb.AppendLine("Target=TargetRadius");
            sb.AppendLine("Radius=1");
            sb.AppendLine($"Distance={Globals.MagicRange}");
            sb.AppendLine("Magic=FireStorm");
            sb.AppendLine("Element=Fire");
            sb.AppendLine("Delay=400");
            sb.AppendLine("Power=DC");
            sb.AppendLine("PowerRate=100");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=RangeChoice");
            sb.AppendLine("Type=WeightedChoice");
            sb.AppendLine("Options=FireBall:1,FireStorm:1");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=SpawnOnFirstStruck");
            sb.AppendLine("Trigger=OnStruck");
            sb.AppendLine("Condition=Always");
            sb.AppendLine("Action=SpawnFlames");
            sb.AppendLine("Priority=1");
            sb.AppendLine("MaxExecutions=1");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=RangeWhenFar");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=TargetDistanceAbove");
            sb.AppendLine("Value=1");
            sb.AppendLine("Action=RangeChoice");
            sb.AppendLine("Priority=10");
            sb.AppendLine("Chance=50");
            sb.AppendLine($"Cooldown={Math.Max(500, attackDelay)}");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=RangeWhenClose");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=TargetInAttackRange");
            sb.AppendLine("Action=RangeChoice");
            sb.AppendLine("Priority=15");
            sb.AppendLine("Chance=20");
            sb.AppendLine($"Cooldown={Math.Max(500, attackDelay)}");

            AppendStandardMeleeRules(sb, attackDelay, 20);
        }

        private static void AppendFerociousIceTigerRuleSet(StringBuilder sb, int attackDelay)
        {
            AppendMeleeMovementActions(sb, includeStepAway: true);

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=ClearSpin");
            sb.AppendLine("Type=SetCounter");
            sb.AppendLine("CounterName=Spinning");
            sb.AppendLine("FixedCount=0");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=SetSpin");
            sb.AppendLine("Type=SetCounter");
            sb.AppendLine("CounterName=Spinning");
            sb.AppendLine("FixedCount=1");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=SpinHit");
            sb.AppendLine("Type=MassAreaDamage");
            sb.AppendLine("Target=SelfRadius");
            sb.AppendLine("Radius=3");
            sb.AppendLine("Delay=400");
            sb.AppendLine("Power=DC");
            sb.AppendLine("PowerRate=100");
            sb.AppendLine("Options=ClassPowerRates=Warrior:80");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=SpinSequence");
            sb.AppendLine("Type=Sequence");
            sb.AppendLine("Options=SpinHit:1,SetSpin:1");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=LungeHit");
            sb.AppendLine("Type=AttackAoE");
            sb.AppendLine("Target=TargetRadius");
            sb.AppendLine("Radius=3");
            sb.AppendLine("Distance=3");
            sb.AppendLine("Delay=600");
            sb.AppendLine("Power=DC");
            sb.AppendLine("PowerRate=50");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=ClearSpinOnMove");
            sb.AppendLine("Trigger=OnMove");
            sb.AppendLine("Condition=Always");
            sb.AppendLine("Action=ClearSpin");
            sb.AppendLine("Priority=1");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=ContinueSpin");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=CounterEquals");
            sb.AppendLine("CounterName=Spinning");
            sb.AppendLine("Value=1");
            sb.AppendLine("Action=SpinSequence");
            sb.AppendLine("Priority=10");
            sb.AppendLine($"Cooldown={Math.Max(500, attackDelay)}");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=RandomSpin");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=TargetInAttackRange");
            sb.AppendLine("Action=SpinSequence");
            sb.AppendLine("Priority=20");
            sb.AppendLine("Chance=20");
            sb.AppendLine($"Cooldown={Math.Max(500, attackDelay)}");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=LungeWhenInRange");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=TargetInAttackRange");
            sb.AppendLine("Action=LungeHit");
            sb.AppendLine("Priority=30");
            sb.AppendLine($"Cooldown={Math.Max(500, attackDelay)}");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=MoveWhenFar");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=TargetOutOfAttackRange");
            sb.AppendLine("Action=MoveToTarget");
            sb.AppendLine("Priority=40");
        }

        private static void AppendDragonQueenRuleSet(StringBuilder sb, int attackDelay)
        {
            AppendYumgonStyleRules(sb, attackDelay, Element.None);
            AppendPeriodicSpawnRule(sb, "SpawnDragonQueenGuards", "OYoungBeast:2|YumgonWitch:2|MaWarden:2|MaWarlord:2|JinhwanSpirit:2|JinhwanGuardian:2|OyoungGeneral:2|YumgonGeneral:2", 10, 0, 10, 15, 15000, priority: 1);

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=SpawnDragonLordOnDeath");
            sb.AppendLine("Type=SpawnMinions");
            sb.AppendLine("FixedCount=1");
            sb.AppendLine("Radius=3");
            sb.AppendLine("Options=MonsterFlag=DragonLord,IgnoreMaxMinions=True");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=SpawnDragonLordOnDeath");
            sb.AppendLine("Trigger=OnDeath");
            sb.AppendLine("Condition=Always");
            sb.AppendLine("Action=SpawnDragonLordOnDeath");
            sb.AppendLine("Priority=1");
            sb.AppendLine("MaxExecutions=1");
        }

        private static void AppendDragonLordRuleSet(StringBuilder sb, int attackDelay)
        {
            AppendMeleeMovementActions(sb, includeStepAway: true);

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=DragonBreath");
            sb.AppendLine("Type=RangeAttack");
            sb.AppendLine("Target=AllInView");
            sb.AppendLine("Radius=12");
            sb.AppendLine("Delay=1000");
            sb.AppendLine("TargetChance=40");
            sb.AppendLine("Power=DC");
            sb.AppendLine("PowerRate=100");
            sb.AppendLine("Options=RangePacket=True");

            AppendPeriodicSpawnRule(sb, "SpawnDragonLordGuards", "OYoungBeast:10000|YumgonWitch:10000|MaWarden:10000|MaWarlord:10000|JinhwanSpirit:10000|JinhwanGuardian:10000|OyoungGeneral:10000|YumgonGeneral:10000|DragonLord:1", 10, 0, 10, 15, 15000, priority: 1, preserveBoss: true);

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=BreathWhenInRange");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=TargetInAttackRange");
            sb.AppendLine("Action=DragonBreath");
            sb.AppendLine("Priority=10");
            sb.AppendLine($"Cooldown={Math.Max(500, attackDelay)}");

            AppendStandardMeleeRules(sb, attackDelay, 20);
        }

        private static void AppendArchLichTaeduRuleSet(StringBuilder sb, int attackDelay)
        {
            AppendMeleeMovementActions(sb, includeStepAway: true);
            AppendRangeShootAction(sb, "BoneBolt", range: 7);
            AppendStageSpawnRules(sb, "StageBoneSpawn", "BoneArcher:90|BoneSoldier:15|BoneBladesman:15|BoneCaptain:15|SkeletonEnforcer:1", 20, 5, 6);
            AppendMixedMeleeRangeRules(sb, "BoneBolt", attackDelay, rangeChance: 20);
        }

        private static void AppendEnragedArchLichTaeduRuleSet(StringBuilder sb, int attackDelay)
        {
            AppendMeleeMovementActions(sb, includeStepAway: true);
            AppendRangeShootAction(sb, "GoruBolt", range: 7);
            AppendStageSpawnRules(sb, "StageGoruSpawn", "GoruArcher:10|GoruGeneral:5|GoruSpearman:5", 5, 5, 6);

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=HalfHealthBlink");
            sb.AppendLine("Type=TeleportSelf");
            sb.AppendLine("MinOffset=7");
            sb.AppendLine("Radius=12");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=TeleportStrike");
            sb.AppendLine("Type=TeleportNearTarget");
            sb.AppendLine("Radius=1");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=BonusHit");
            sb.AppendLine("Type=MeleeAttack");
            sb.AppendLine("Distance=1");
            sb.AppendLine("Delay=400");
            sb.AppendLine("Power=DC");
            sb.AppendLine("PowerRate=200");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=TeleportStrikeSequence");
            sb.AppendLine("Type=Sequence");
            sb.AppendLine("Options=TeleportStrike:1,BonusHit:1");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=BlinkBelowHalf");
            sb.AppendLine("Trigger=OnStruck");
            sb.AppendLine("Condition=HealthBelow");
            sb.AppendLine("Value=50");
            sb.AppendLine("Action=HalfHealthBlink");
            sb.AppendLine("Priority=1");
            sb.AppendLine("MaxExecutions=1");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=RandomTeleportStrike");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=TargetDistanceAbove");
            sb.AppendLine("Value=1");
            sb.AppendLine("Action=TeleportStrikeSequence");
            sb.AppendLine("Priority=10");
            sb.AppendLine("Chance=14");
            sb.AppendLine("Cooldown=1000");

            AppendMixedMeleeRangeRules(sb, "GoruBolt", attackDelay, rangeChance: 20, priorityBase: 20);
        }

        private static void AppendZumaKingRuleSet(StringBuilder sb, int attackDelay)
        {
            AppendHiddenWakeRuleSet(sb, canPhase: false, attackDelay);
            AppendStageSpawnRules(sb, "StageZumaSpawn", "ZumaArcherMonster:50|ZumaFanaticMonster:25|ZumaGuardianMonster:25|ZumaKeeperMonster:1", 4, 8, 6);

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=ZumaFireLine");
            sb.AppendLine("Type=LineAoE");
            sb.AppendLine("Distance=12");
            sb.AppendLine("MinOffset=-2");
            sb.AppendLine("MaxOffset=2");
            sb.AppendLine("Delay=400");
            sb.AppendLine("Magic=MonsterScortchedEarth");
            sb.AppendLine("Element=Fire");
            sb.AppendLine("Power=DC");
            sb.AppendLine("PowerRate=100");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=FireLineWhenVisible");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=Visible");
            sb.AppendLine("Conditions=TargetExists");
            sb.AppendLine("Action=ZumaFireLine");
            sb.AppendLine("Priority=18");
            sb.AppendLine("Chance=20");
            sb.AppendLine($"Cooldown={Math.Max(500, attackDelay)}");
        }

        private static void AppendEnragedLordNiJaeRuleSet(StringBuilder sb, int attackDelay)
        {
            AppendLordNiJaeRuleSet(sb, attackDelay);
            AppendPeriodicSpawnRule(sb, "SpawnMillipedes", "Millipede:1", 5, 0, 10, 10, 30000, priority: 1);
        }

        private static void AppendQuartzTreeRuleSet(StringBuilder sb)
        {
            AppendPeriodicSpawnRule(sb, "SpawnQuartzAdds", "QuartzBlueBat:20|QuartzPinkBat:20|QuartzBlueCrystal:20|QuartzRedHood:2", 10, 0, 5, 30, 60000, priority: 10);

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=SpawnQuartzSubBoss");
            sb.AppendLine("Type=SpawnMinions");
            sb.AppendLine("FixedCount=1");
            sb.AppendLine("Radius=5");
            sb.AppendLine("Options=MonsterFlag=QuartzTurtleSub,IgnoreMaxMinions=True");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=SpawnSubBossBelowQuarter");
            sb.AppendLine("Trigger=OnTick");
            sb.AppendLine("Condition=HealthBelow");
            sb.AppendLine("Value=25");
            sb.AppendLine("Action=SpawnQuartzSubBoss");
            sb.AppendLine("Priority=1");
            sb.AppendLine("MaxExecutions=1");
        }

        private static void AppendDoomClawRuleSet(StringBuilder sb, int attackDelay)
        {
            const string rates = "Warrior:60|Wizard:70|Taoist:40|Assassin:80";

            AppendDoomClawAction(sb, "Wave", MagicType.DoomClawWave, pushDirection: "DownRight", pushDistance: 15, rates);
            AppendDoomClawAction(sb, "Spit", MagicType.DoomClawSpit, pushDirection: null, pushDistance: 0, rates);
            AppendDoomClawAction(sb, "RightPinch", MagicType.DoomClawRightPinch, pushDirection: null, pushDistance: 0, rates);
            AppendDoomClawAction(sb, "RightSwipe", MagicType.DoomClawRightSwipe, pushDirection: "DownLeft", pushDistance: 5, rates);
            AppendDoomClawAction(sb, "LeftPinch", MagicType.DoomClawLeftPinch, pushDirection: null, pushDistance: 0, rates);
            AppendDoomClawAction(sb, "LeftSwipe", MagicType.DoomClawLeftSwipe, pushDirection: "UpRight", pushDistance: 5, rates);

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=ClawChoice");
            sb.AppendLine("Type=WeightedChoice");
            sb.AppendLine("Options=Spit:6,RightPinch:9,RightSwipe:1,LeftPinch:9,LeftSwipe:1");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=WaveSometimes");
            sb.AppendLine("Trigger=OnTick");
            sb.AppendLine("Condition=TargetCountAtLeast");
            sb.AppendLine("Value=1");
            sb.AppendLine($"Radius={Config.MaxViewRange}");
            sb.AppendLine("Action=Wave");
            sb.AppendLine("Priority=10");
            sb.AppendLine("Chance=5");
            sb.AppendLine($"Cooldown={Math.Max(500, attackDelay)}");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=ClawAttack");
            sb.AppendLine("Trigger=OnTick");
            sb.AppendLine("Condition=TargetCountAtLeast");
            sb.AppendLine("Value=1");
            sb.AppendLine($"Radius={Config.MaxViewRange}");
            sb.AppendLine("Action=ClawChoice");
            sb.AppendLine("Priority=20");
            sb.AppendLine($"Cooldown={Math.Max(500, attackDelay)}");
        }

        private static void AppendDamageProxyRuleSet(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("; DamageProxy=True 的怪物由 DamageProxy 技能绑定施法者和目标后驱动，不需要额外 AI 规则。");
        }

        private static void AppendYumgonStyleRules(StringBuilder sb, int attackDelay, Element areaElement)
        {
            AppendMeleeMovementActions(sb, includeStepAway: true);

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=CloseArea");
            sb.AppendLine("Type=MassAreaDamage");
            sb.AppendLine("Target=SelfRadius");
            sb.AppendLine("Radius=3");
            sb.AppendLine("Delay=400");
            sb.AppendLine($"Element={areaElement}");
            sb.AppendLine("Power=DC");
            sb.AppendLine("PowerRate=100");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=CloseAreaChance");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=TargetDistanceBelow");
            sb.AppendLine("Value=3");
            sb.AppendLine("Action=CloseArea");
            sb.AppendLine("Priority=10");
            sb.AppendLine("Chance=20");
            sb.AppendLine($"Cooldown={Math.Max(500, attackDelay)}");

            AppendStandardMeleeRules(sb, attackDelay, 20);
        }

        private static void AppendRangeShootAction(StringBuilder sb, string name, int range)
        {
            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine($"Name={name}");
            sb.AppendLine("Type=RangeAttack");
            sb.AppendLine("Target=CurrentTarget");
            sb.AppendLine($"Distance={range}");
            sb.AppendLine("Delay=400");
            sb.AppendLine("Power=DC");
            sb.AppendLine("PowerRate=100");
            sb.AppendLine("Options=RangePacket=True");
        }

        private static void AppendMixedMeleeRangeRules(StringBuilder sb, string rangeAction, int attackDelay, int rangeChance, int priorityBase = 10)
        {
            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine($"Name={rangeAction}WhenFar");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=TargetOutOfAttackRange");
            sb.AppendLine($"Action={rangeAction}");
            sb.AppendLine($"Priority={priorityBase}");
            sb.AppendLine($"Chance={rangeChance}");
            sb.AppendLine($"Cooldown={Math.Max(500, attackDelay)}");

            AppendStandardMeleeRules(sb, attackDelay, priorityBase + 10);
        }

        private static void AppendStageSpawnRules(StringBuilder sb, string actionName, string spawnPool, int fixedCount, int randomCount, int radius)
        {
            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine($"Name={actionName}");
            sb.AppendLine("Type=SpawnMinions");
            sb.AppendLine($"FixedCount={fixedCount}");
            sb.AppendLine($"RandomCount={randomCount}");
            sb.AppendLine($"Radius={radius}");
            sb.AppendLine($"Options=MonsterFlags={spawnPool}");

            int[] thresholds = { 85, 71, 57, 42, 28, 14, 1 };
            for (int i = 0; i < thresholds.Length; i++)
            {
                sb.AppendLine();
                sb.AppendLine("[AIRule]");
                sb.AppendLine($"Name={actionName}{i + 1}");
                sb.AppendLine("Trigger=OnTick");
                sb.AppendLine("Condition=HealthBelow");
                sb.AppendLine($"Value={thresholds[i]}");
                sb.AppendLine($"Action={actionName}");
                sb.AppendLine($"Priority={10 + i}");
                sb.AppendLine("MaxExecutions=1");
            }
        }

        private static void AppendPeriodicSpawnRule(StringBuilder sb, string actionName, string spawnPool, int fixedCount, int randomCount, int radius, int leashRadius, int cooldown, int priority, bool preserveBoss = false)
        {
            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine($"Name={actionName}");
            sb.AppendLine("Type=SpawnMinions");
            sb.AppendLine($"FixedCount={fixedCount}");
            sb.AppendLine($"RandomCount={randomCount}");
            sb.AppendLine($"Radius={radius}");
            string options = $"MonsterFlags={spawnPool},LeashRadius={leashRadius},MaxTotal={fixedCount}";
            if (preserveBoss)
                options += ",PreserveBossMinions=True";
            sb.AppendLine($"Options={options}");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine($"Name={actionName}");
            sb.AppendLine("Trigger=OnTick");
            sb.AppendLine("Condition=Always");
            sb.AppendLine($"Action={actionName}");
            sb.AppendLine($"Priority={priority}");
            sb.AppendLine($"Cooldown={cooldown}");
        }

        private static void AppendDoomClawAction(StringBuilder sb, string name, MagicType magic, string pushDirection, int pushDistance, string classPowerRates)
        {
            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine($"Name={name}");
            sb.AppendLine("Type=MassAreaDamage");
            sb.AppendLine("Target=AllInView");
            sb.AppendLine($"Radius={Config.MaxViewRange}");
            sb.AppendLine("Delay=400");
            sb.AppendLine($"Magic={magic}");
            sb.AppendLine("Power=DC");
            sb.AppendLine("PowerRate=100");

            string options = $"ClassPowerRates={classPowerRates}";
            if (pushDistance > 0 && !string.IsNullOrWhiteSpace(pushDirection))
                options += $",PushDirection={pushDirection},PushDistance={pushDistance}";
            sb.AppendLine($"Options={options}");
        }

        private static void AppendMeleeMovementActions(StringBuilder sb, bool includeStepAway)
        {
            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=MoveToTarget");
            sb.AppendLine("Type=MoveToTarget");

            if (includeStepAway)
            {
                sb.AppendLine();
                sb.AppendLine("[AIAction]");
                sb.AppendLine("Name=StepAway");
                sb.AppendLine("Type=RandomStepWhenSameCell");
            }

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=MeleeHit");
            sb.AppendLine("Type=MeleeAttack");
            sb.AppendLine("Distance=1");
            sb.AppendLine("Delay=400");
            sb.AppendLine("Power=DC");
            sb.AppendLine("PowerRate=100");
        }

        private static void AppendVisibleMeleeRules(StringBuilder sb, int attackDelay)
        {
            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=MoveWhenVisibleFar");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=Visible");
            sb.AppendLine("Conditions=TargetOutOfAttackRange");
            sb.AppendLine("ConditionJoin=And");
            sb.AppendLine("Action=MoveToTarget");
            sb.AppendLine("Priority=30");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=AttackWhenVisibleNear");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=Visible");
            sb.AppendLine("Conditions=TargetInAttackRange");
            sb.AppendLine("ConditionJoin=And");
            sb.AppendLine("Action=MeleeHit");
            sb.AppendLine("Priority=40");
            sb.AppendLine($"Cooldown={Math.Max(500, attackDelay)}");
        }

        private static void AppendStandardMeleeRules(StringBuilder sb, int attackDelay, int priorityBase)
        {
            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=StepWhenStacked");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=SameCellAsTarget");
            sb.AppendLine("Action=StepAway");
            sb.AppendLine($"Priority={priorityBase}");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=MoveWhenFar");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=TargetOutOfAttackRange");
            sb.AppendLine("Action=MoveToTarget");
            sb.AppendLine($"Priority={priorityBase + 10}");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=AttackWhenNear");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=TargetInAttackRange");
            sb.AppendLine("Action=MeleeHit");
            sb.AppendLine($"Priority={priorityBase + 20}");
            sb.AppendLine($"Cooldown={Math.Max(500, attackDelay)}");
        }

        private static bool IsAutoMigratedBasicBehavior(int ai)
        {
            return ai is
                -2 or -1 or 0 or 1 or 2 or 3 or 4 or 5 or
                6 or 7 or 8 or 9 or 10 or 11 or 12 or 13 or 14 or 15 or 16 or 17 or 18 or 19 or 20 or 21 or 22 or 23 or 24 or 25 or 26 or 27 or 28 or
                29 or 30 or 31 or 33 or 34 or 35 or 36 or 37 or 38 or 39 or 40 or 41 or 42 or 43 or 44 or 45 or 46 or 47 or 48 or 49 or 50 or
                52 or 53 or 54 or 56 or 57 or 58 or 59 or 60 or 61 or 62 or 63 or 64 or 65 or 66 or 67 or 68 or 69 or 70 or 71 or 72 or 74 or 77 or 78 or 79 or 80 or 81 or 82 or 83 or 84 or 85 or 86 or 87 or 88 or 89 or 90 or 91 or
                92 or 93 or 94 or 95 or 96 or 97 or 98 or 99 or 100 or 101 or 102 or 103 or 104 or 105 or 106 or 107 or 108 or 109 or 110 or 111 or 112 or 113 or 114 or 115 or 116 or 117 or 118 or 119 or 120 or 121 or 122 or 123 or 124 or 125 or 126 or 128 or 130 or 131 or 132 or 133 or 134 or
                127 or 129 or 143 or 144 or 146 or 147 or 149 or 150 or 151 or 152 or 153 or 154 or 156 or 159 or 999 or 1000;
        }

        private static void AppendLineAttackRuleSet(StringBuilder sb, int distance)
        {
            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=MoveToTarget");
            sb.AppendLine("Type=MoveToTarget");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=StepAway");
            sb.AppendLine("Type=RandomStepWhenSameCell");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=LineHit");
            sb.AppendLine("Type=LineAttack");
            sb.AppendLine($"Distance={distance}");
            sb.AppendLine("Delay=400");
            sb.AppendLine("Power=DC");
            sb.AppendLine("PowerRate=100");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=StepWhenStacked");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=SameCellAsTarget");
            sb.AppendLine("Action=StepAway");
            sb.AppendLine("Priority=10");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=LineWhenAligned");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=TargetInLine");
            sb.AppendLine($"Value={distance}");
            sb.AppendLine("Action=LineHit");
            sb.AppendLine("Priority=20");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=MoveWhenNotAligned");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=TargetOutOfLine");
            sb.AppendLine($"Value={distance}");
            sb.AppendLine("Action=MoveToTarget");
            sb.AppendLine("Priority=30");
        }

        private static bool TryGetAutoLineDistance(int ai, out int distance)
        {
            distance = ai switch
            {
                6 or 14 or 42 or 46 or 47 => 2,
                9 or 62 => 6,
                31 => 3,
                48 => 3,
                88 => 2,
                97 => 5,
                156 => 5,
                _ => 0,
            };

            return distance > 0;
        }

        private static void AppendRangeSwitchRuleSet(StringBuilder sb, int range, int cooldown)
        {
            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=MoveToTarget");
            sb.AppendLine("Type=MoveToTarget");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=StepAway");
            sb.AppendLine("Type=RandomStepWhenSameCell");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=MeleeHit");
            sb.AppendLine("Type=MeleeAttack");
            sb.AppendLine("Distance=1");
            sb.AppendLine("Delay=400");
            sb.AppendLine("Power=DC");
            sb.AppendLine("PowerRate=100");

            sb.AppendLine();
            sb.AppendLine("[AIAction]");
            sb.AppendLine("Name=RangeHit");
            sb.AppendLine("Type=RangeAttack");
            sb.AppendLine("Target=CurrentTarget");
            sb.AppendLine($"Distance={range}");
            sb.AppendLine("Delay=400");
            sb.AppendLine("Power=DC");
            sb.AppendLine("PowerRate=100");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=StepWhenStacked");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=SameCellAsTarget");
            sb.AppendLine("Action=StepAway");
            sb.AppendLine("Priority=10");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=MeleeWhenClose");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=TargetDistanceBelow");
            sb.AppendLine("Value=1");
            sb.AppendLine("Action=MeleeHit");
            sb.AppendLine("Priority=20");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=RangeWhenInRange");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=TargetInAttackRange");
            sb.AppendLine("Action=RangeHit");
            sb.AppendLine("Priority=30");
            if (cooldown > 0)
                sb.AppendLine($"Cooldown={cooldown}");

            sb.AppendLine();
            sb.AppendLine("[AIRule]");
            sb.AppendLine("Name=MoveWhenFar");
            sb.AppendLine("Trigger=OnTargetTick");
            sb.AppendLine("Condition=TargetOutOfAttackRange");
            sb.AppendLine("Action=MoveToTarget");
            sb.AppendLine("Priority=40");
        }

        private static bool TryGetAutoRangeSwitchDistance(int ai, out int distance)
        {
            distance = ai switch
            {
                23 or 24 or 64 => 2,
                45 => 3,
                33 or 92 => 9,
                105 => 5,
                49 or 50 or 79 or 106 => ai == 79 ? 10 : 8,
                94 or 95 => 8,
                98 or 100 => 8,
                103 => 5,
                151 => 7,
                152 => 5,
                154 or 159 or 999 => 7,
                _ => 0,
            };

            return distance > 0;
        }

        private static int GetAutoRangeCooldown(int ai)
        {
            return ai is 54 or 79 ? 5000 : 0;
        }

        private static bool IsAutoIgnoreShield(int ai)
        {
            return ai is 33 or 48 or 56 or 67 or 68 or 97 or 105 or 143 or 146;
        }

        private static bool IsAutoIgnoreArmour(int ai)
        {
            return ai is 143 or 146;
        }

        private static bool IsAutoPoisonStackable(int ai)
        {
            return ai is 146;
        }

        private static bool IsAutoAvoidFireWall(int ai)
        {
            return ai is 21;
        }

        private static bool IsAlwaysActiveAi(int ai)
        {
            return ai is 30 or 69 or 129;
        }

        private static bool IsAutoHiddenAi(int ai)
        {
            return IsAutoHiddenSpawnAi(ai) || IsAutoHiddenWakeAi(ai) || IsAutoPlantAmbushAi(ai) || IsAutoGhostMageAi(ai);
        }

        private static bool IsAutoPassiveDamageAi(int ai)
        {
            return ai is 86 or 129;
        }

        private static bool IsAutoNoBlockingAi(int ai)
        {
            return ai is 129;
        }

        private static bool IsAutoNoAttackAi(int ai)
        {
            return ai is 12 or 30 or 69 or 124 or 128;
        }

        private static bool IsAutoInvulnerableAi(int ai)
        {
            return ai is 30 or 69;
        }

        private static bool IsAutoGhostMageAi(int ai)
        {
            return ai is 10 or 27;
        }

        private static bool IsAutoWedgeMothLarvaAi(int ai)
        {
            return ai is 44;
        }

        private static bool IsAutoArachnidGrazerAi(int ai)
        {
            return ai is 17 or 144;
        }

        private static bool IsAutoTornadoAi(int ai)
        {
            return ai is 129;
        }

        private static bool IsAutoBanyoCaptainAi(int ai)
        {
            return ai is 72;
        }

        private static bool IsAutoBanyoLordGuzakAi(int ai)
        {
            return ai is 74;
        }

        private static bool IsAutoEscortCommanderAi(int ai)
        {
            return ai is 93;
        }

        private static bool IsAutoWingedHorrorAi(int ai)
        {
            return ai is 84;
        }

        private static bool IsAutoChaosKnightAi(int ai)
        {
            return ai is 60;
        }

        private static bool TryGetAutoJinchonDevilProfile(int ai, out int cooldown, out int duration, out int durationRandom)
        {
            cooldown = 0;
            duration = 0;
            durationRandom = 0;

            switch (ai)
            {
                case 78:
                    cooldown = 15000;
                    duration = 4000;
                    return true;
                case 127:
                    cooldown = 8000;
                    duration = 2000;
                    durationRandom = 5000;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryGetAutoPortalProfile(int ai, out string specialRegion, out bool includeMonsters, out string openMessage, out string closeMessage)
        {
            specialRegion = null;
            includeMonsters = false;
            openMessage = null;
            closeMessage = null;

            switch (ai)
            {
                case 30:
                    specialRegion = "MysteryShip";
                    includeMonsters = true;
                    openMessage = "异界之门已开启。";
                    closeMessage = "异界之门已关闭。";
                    return true;
                case 69:
                    specialRegion = "Lair";
                    openMessage = "赤龙石门已开启。";
                    closeMessage = "赤龙石门已关闭。";
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsAutoSamaProphetAi(int ai)
        {
            return ai is 115;
        }

        private static bool IsAutoSamaScorcerAi(int ai)
        {
            return ai is 116;
        }

        private static bool IsAutoQueenOfDawnAi(int ai)
        {
            return ai is 96;
        }

        private static bool IsAutoQuartzTurtleSubAi(int ai)
        {
            return ai is 122;
        }

        private static bool IsAutoMonasteryBossAi(int ai)
        {
            return ai is 126;
        }

        private static bool IsAutoHealerAntAi(int ai) => ai is 12;

        private static bool IsAutoLordNiJaeAi(int ai) => ai is 13;

        private static bool IsAutoZumaKingAi(int ai) => ai is 22;

        private static bool IsAutoFrostLordHwaAi(int ai) => ai is 70;

        private static bool IsAutoEnragedLordNiJaeAi(int ai) => ai is 77;

        private static bool IsAutoSunFeralWarriorAi(int ai) => ai is 80;

        private static bool IsAutoArchLichTaeduAi(int ai) => ai is 43;

        private static bool IsAutoEnragedArchLichTaeduAi(int ai) => ai is 91;

        private static bool IsAutoDragonQueenAi(int ai) => ai is 101;

        private static bool IsAutoDragonLordAi(int ai) => ai is 102;

        private static bool IsAutoFerociousIceTigerAi(int ai) => ai is 104;

        private static bool IsAutoDoomClawAi(int ai) => ai is 120;

        private static bool IsAutoQuartzTreeAi(int ai) => ai is 124;

        private static bool IsAutoDamageProxyAi(int ai) => ai is 128;

        private static bool IsAutoDisableRegenAi(int ai) => ai is 124;

        private static MonsterFlag GetAutoProtectedByMonsterFlag(int ai)
        {
            return ai switch
            {
                116 => MonsterFlag.BloodStone,
                _ => MonsterFlag.None,
            };
        }

        private static string GetAutoProtectedByMonsterFlags(int ai)
        {
            return ai switch
            {
                115 => "BloodStone|SamaSorcerer",
                _ => string.Empty,
            };
        }

        private static int GetAutoProtectionRadius(int ai)
        {
            return ai is 115 or 116 ? 0 : 12;
        }

        private static bool IsAutoHiddenSpawnAi(int ai)
        {
            return ai is 52 or 130;
        }

        private static bool IsAutoHiddenWakeAi(int ai)
        {
            return ai is 21 or 131 or 132;
        }

        private static bool IsAutoPlantAmbushAi(int ai)
        {
            return ai is 5 or 65 or 125;
        }

        private static bool IsAutoReviveAi(int ai)
        {
            return ai is 11;
        }

        private static bool IsAutoMagicWeaknessAi(int ai)
        {
            return ai is 59;
        }

        private static bool TryGetAutoRangedDistance(int ai, out int distance)
        {
            distance = ai switch
            {
                7 or 20 or 29 or 89 => 7,
                28 => 7,
                34 => 9,
                63 or 94 or 95 => 9,
                121 => 7,
                _ => 0,
            };

            return distance > 0;
        }

        private static bool TryGetAutoSelfDestructRange(int ai, out int range)
        {
            range = ai switch
            {
                18 => 1,
                123 => 3,
                _ => 0,
            };

            return range > 0;
        }

        private static bool TryGetAutoBossRange(int ai, out int range)
        {
            range = ai switch
            {
                16 => 8,
                41 or 85 => 8,
                _ => 0,
            };

            return range > 0;
        }

        private static bool TryGetAutoTeleportMelee(int ai, out bool bonusDamage, out int cooldown)
        {
            bonusDamage = ai is 71 or 81 or 87 or 88 or 89 or 90 or 117;
            cooldown = ai switch
            {
                56 or 57 => 5000,
                61 => 5000,
                71 or 81 or 87 or 88 or 89 or 90 or 117 => 1000,
                147 => 1000,
                _ => 0,
            };

            return ai is 56 or 57 or 61 or 71 or 81 or 87 or 88 or 89 or 90 or 117 or 147;
        }

        private static bool TryGetAutoSimpleCaster(int ai, out MagicType magic, out Element element, out bool travel)
        {
            magic = ai switch
            {
                26 or 36 => MagicType.ThunderBolt,
                38 => MagicType.FireBall,
                _ => MagicType.None,
            };
            element = ai switch
            {
                38 => Element.Fire,
                _ => Element.Lightning,
            };
            travel = ai is 38;

            return magic != MagicType.None;
        }

        private static bool TryGetAutoLightningChoiceCaster(int ai)
        {
            return ai is 118;
        }

        private static bool TryGetAutoSamaProfile(
            int ai,
            out Element element,
            out MagicType projectile,
            out MagicType line,
            out MagicType area,
            out MagicType burst,
            out MagicType empowered)
        {
            element = Element.None;
            projectile = MagicType.None;
            line = MagicType.None;
            area = MagicType.None;
            burst = MagicType.None;
            empowered = MagicType.None;

            switch (ai)
            {
                case 107:
                    element = Element.Fire;
                    projectile = MagicType.AdamantineFireBall;
                    line = MagicType.ScortchedEarth;
                    area = MagicType.FireStorm;
                    burst = MagicType.SamaGuardianFire;
                    return true;
                case 108:
                    element = Element.Ice;
                    projectile = MagicType.IceBlades;
                    line = MagicType.GreaterFrozenEarth;
                    area = MagicType.IceStorm;
                    burst = MagicType.SamaGuardianIce;
                    return true;
                case 109:
                    element = Element.Lightning;
                    projectile = MagicType.ThunderBolt;
                    line = MagicType.LightningBeam;
                    area = MagicType.LightningWave;
                    burst = MagicType.SamaGuardianLightning;
                    return true;
                case 110:
                    element = Element.Wind;
                    projectile = MagicType.Cyclone;
                    line = MagicType.BlowEarth;
                    area = MagicType.DragonTornado;
                    burst = MagicType.SamaGuardianWind;
                    return true;
                case 111:
                    element = Element.Fire;
                    projectile = MagicType.AdamantineFireBall;
                    line = MagicType.ScortchedEarth;
                    area = MagicType.FireStorm;
                    burst = MagicType.SamaGuardianFire;
                    empowered = MagicType.SamaPhoenixFire;
                    return true;
                case 112:
                    element = Element.Ice;
                    projectile = MagicType.IceBlades;
                    line = MagicType.GreaterFrozenEarth;
                    area = MagicType.IceStorm;
                    burst = MagicType.SamaGuardianIce;
                    empowered = MagicType.SamaBlackIce;
                    return true;
                case 113:
                    element = Element.Lightning;
                    projectile = MagicType.ThunderBolt;
                    line = MagicType.LightningBeam;
                    area = MagicType.LightningWave;
                    burst = MagicType.SamaGuardianLightning;
                    empowered = MagicType.SamaBlueLightning;
                    return true;
                case 114:
                    element = Element.Wind;
                    projectile = MagicType.Cyclone;
                    line = MagicType.BlowEarth;
                    area = MagicType.DragonTornado;
                    burst = MagicType.SamaGuardianWind;
                    empowered = MagicType.SamaWhiteWind;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryGetAutoFixedMassRange(int ai, out int range, out MagicType magic, out Element element)
        {
            range = 0;
            magic = MagicType.None;
            element = Element.None;

            switch (ai)
            {
                case 19:
                    range = Config.MaxViewRange;
                    magic = MagicType.None;
                    element = Element.Dark;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryGetAutoIcySpiritProfile(int ai, out bool drainMp)
        {
            drainMp = ai is 67;
            return ai is 66 or 67;
        }

        private static bool IsAutoDeathSelfSpawnAi(int ai)
        {
            return ai is 99;
        }

        private static bool TryGetAutoLineAoEProfile(int ai, out int distance, out int minOffset, out int maxOffset, out MagicType magic, out Element element)
        {
            distance = 0;
            minOffset = 0;
            maxOffset = 0;
            magic = MagicType.None;
            element = Element.None;

            switch (ai)
            {
                case 82:
                    distance = 12;
                    minOffset = -2;
                    maxOffset = 2;
                    magic = MagicType.GreaterFrozenEarth;
                    element = Element.Ice;
                    return true;
                case 83:
                    distance = 8;
                    minOffset = -2;
                    maxOffset = 2;
                    magic = MagicType.MonsterScortchedEarth;
                    element = Element.Fire;
                    return true;
                case 86:
                    distance = 12;
                    minOffset = 0;
                    maxOffset = 8;
                    magic = MagicType.MonsterScortchedEarth;
                    element = Element.Fire;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryGetAutoAreaMelee(int ai, out int radius, out bool applyPoison)
        {
            radius = ai switch
            {
                25 => 1,
                58 => 2,
                149 or 150 => 1,
                _ => 0,
            };
            applyPoison = ai is 25 or 150;

            return radius > 0;
        }

        private static int GetAutoAttackRange(MonsterInfo monster, CustomMonsterBehaviorTemplate template)
        {
            if (TryGetAutoSelfDestructRange(monster.AI, out int selfDestructRange))
                return selfDestructRange;

            if (TryGetAutoBossRange(monster.AI, out _))
                return 1;

            if (TryGetAutoSamaProfile(monster.AI, out _, out _, out _, out _, out _, out _))
                return 2;

            if (TryGetAutoSimpleCaster(monster.AI, out _, out _, out _) || TryGetAutoLightningChoiceCaster(monster.AI))
                return 9;

            if (TryGetAutoFixedMassRange(monster.AI, out int fixedRange, out _, out _))
                return fixedRange;

            if (TryGetAutoLineAoEProfile(monster.AI, out int aoeDistance, out _, out _, out _, out _))
                return aoeDistance;

            if (IsAutoWedgeMothLarvaAi(monster.AI))
                return Config.MaxViewRange;

            if (IsAutoArachnidGrazerAi(monster.AI))
                return Math.Max(1, monster.ViewRange);

            if (TryGetAutoPortalProfile(monster.AI, out _, out _, out _, out _))
                return Math.Max(1, monster.ViewRange);

            if (IsAutoTornadoAi(monster.AI))
                return 1;

            if (IsAutoBanyoCaptainAi(monster.AI) || IsAutoEscortCommanderAi(monster.AI) || IsAutoWingedHorrorAi(monster.AI))
                return 1;

            if (IsAutoBanyoLordGuzakAi(monster.AI))
                return 1;

            if (IsAutoChaosKnightAi(monster.AI))
                return 1;

            if (TryGetAutoJinchonDevilProfile(monster.AI, out _, out _, out _))
                return 3;

            if (IsAutoSamaProphetAi(monster.AI))
                return 12;

            if (IsAutoSamaScorcerAi(monster.AI))
                return 3;

            if (IsAutoQueenOfDawnAi(monster.AI) || IsAutoQuartzTurtleSubAi(monster.AI))
                return 1;

            if (IsAutoMonasteryBossAi(monster.AI))
                return 9;

            if (IsAutoLordNiJaeAi(monster.AI) || IsAutoEnragedLordNiJaeAi(monster.AI))
                return Config.MaxViewRange;

            if (IsAutoDragonLordAi(monster.AI))
                return 12;

            if (IsAutoDragonQueenAi(monster.AI))
                return 10;

            if (IsAutoDoomClawAi(monster.AI))
                return Config.MaxViewRange;

            if (IsAutoFerociousIceTigerAi(monster.AI) || IsAutoArchLichTaeduAi(monster.AI) || IsAutoEnragedArchLichTaeduAi(monster.AI))
                return 3;

            if (IsAutoHealerAntAi(monster.AI))
                return Globals.MagicRange;

            if (TryGetAutoRangedDistance(monster.AI, out int rangedDistance))
                return rangedDistance;

            if (TryGetAutoLineDistance(monster.AI, out int lineDistance))
                return lineDistance;

            if (TryGetAutoRangeSwitchDistance(monster.AI, out int rangeSwitchDistance))
                return rangeSwitchDistance;

            if (TryGetAutoAreaMelee(monster.AI, out int areaRadius, out _))
                return areaRadius;

            if (IsAutoPlantAmbushAi(monster.AI))
                return 1;

            return InferFallbackAttackRange(monster, template);
        }

        private static int GetAutoMaxMinions(int ai)
        {
            return ai switch
            {
                77 => 200,
                43 or 91 => 50,
                101 or 102 or 124 => 10,
                74 => 5,
                122 => 10,
                144 => 30,
                _ => 20,
            };
        }

        private static void ApplyAutoPoison(int ai, out PoisonType poisonType, out int poisonRate, out int poisonTicks, out int poisonFrequency)
        {
            poisonType = PoisonType.None;
            poisonRate = 10;
            poisonTicks = 5;
            poisonFrequency = 2;

            switch (ai)
            {
                case 6:
                case 14:
                    poisonType = PoisonType.Green;
                    break;
                case 46:
                    poisonType = PoisonType.Red;
                    poisonTicks = 1;
                    poisonFrequency = 10;
                    poisonRate = 25;
                    break;
                case 47:
                    poisonType = PoisonType.Green;
                    poisonTicks = 7;
                    poisonRate = 15;
                    break;
                case 8:
                case 49:
                    poisonType = PoisonType.Paralysis;
                    poisonTicks = 1;
                    poisonFrequency = 5;
                    poisonRate = ai == 8 ? 12 : 10;
                    break;
                case 23:
                    poisonType = PoisonType.Green;
                    break;
                case 24:
                    poisonType = PoisonType.Red;
                    break;
                case 25:
                    poisonType = PoisonType.Green;
                    poisonRate = 6;
                    poisonTicks = 10;
                    poisonFrequency = 2;
                    break;
                case 85:
                    poisonType = PoisonType.Paralysis;
                    poisonTicks = 1;
                    poisonFrequency = 5;
                    poisonRate = 8;
                    break;
                case 87:
                    poisonType = PoisonType.Abyss;
                    poisonTicks = 1;
                    poisonFrequency = 7;
                    poisonRate = 15;
                    break;
                case 89:
                case 119:
                    poisonType = PoisonType.Silenced;
                    poisonTicks = 1;
                    poisonFrequency = 5;
                    poisonRate = 10;
                    break;
                case 90:
                case 95:
                case 133:
                case 134:
                    poisonType = PoisonType.Paralysis;
                    poisonTicks = 1;
                    poisonFrequency = 5;
                    poisonRate = ai == 90 ? 25 : 15;
                    break;
                case 143:
                case 146:
                    poisonType = PoisonType.StrongGreen;
                    poisonTicks = ai == 146 ? 20 : 10;
                    poisonFrequency = 1;
                    poisonRate = 0;
                    break;
                case 150:
                    poisonType = PoisonType.Green;
                    poisonTicks = 10;
                    poisonFrequency = 2;
                    poisonRate = 5;
                    break;
                case 153:
                    poisonType = PoisonType.Green;
                    poisonTicks = 20;
                    poisonRate = 5;
                    break;
            }
        }

        private static CustomMonsterBehaviorTemplate InferFallbackTemplate(MonsterInfo monster, CustomMonsterAiMigrationRow migration)
        {
            if (monster.AI == -1)
                return CustomMonsterBehaviorTemplate.Guard;

            string text = $"{migration?.SuggestedTemplate} {migration?.CurrentImplementation} {migration?.Notes}";

            if (monster.IsBoss || ContainsAny(text, "Boss", "牛魔王", "全屏", "阶段"))
                return CustomMonsterBehaviorTemplate.Boss;

            if (ContainsAny(text, "召唤", "Summon", "SpawnMinions"))
                return CustomMonsterBehaviorTemplate.Summoner;

            if (ContainsAny(text, "法师", "范围", "AoE", "Magic", "Caster", "施法"))
                return CustomMonsterBehaviorTemplate.Caster;

            if (ContainsAny(text, "远程", "Range", "投掷", "直线"))
                return CustomMonsterBehaviorTemplate.Ranged;

            return CustomMonsterBehaviorTemplate.Melee;
        }

        private static int InferFallbackAttackRange(MonsterInfo monster, CustomMonsterBehaviorTemplate template)
        {
            if (template is CustomMonsterBehaviorTemplate.Ranged or CustomMonsterBehaviorTemplate.Caster or CustomMonsterBehaviorTemplate.Boss)
                return Math.Max(2, Math.Min(Math.Max(monster.ViewRange, 2), 12));

            return 1;
        }

        private static bool IsPassiveAi(int ai)
        {
            return ai is 1 or 2 or 4 or 5 or 86 or 124 or 128 or 129;
        }

        private static bool IsFixedAi(int ai)
        {
            return ai is 1 or 2 or 4 or 5 or 12 or 19 or 30 or 44 or 65 or 69 or 86 or 120 or 124 or 128 or 144 or 147 or 159;
        }

        private static bool TryGetHarvestCount(int ai, out int harvestCount)
        {
            harvestCount = ai switch
            {
                1 or 5 or 6 or 8 => 2,
                2 or 3 or 4 => 3,
                _ => 0,
            };

            return harvestCount > 0;
        }

        private static bool ContainsAny(string value, params string[] tokens)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            return tokens.Any(x => value.Contains(x, StringComparison.OrdinalIgnoreCase));
        }

        private void ExportSkills(string directory, bool overwrite, CustomSystemTextReport report, bool includeAllDatabaseSkills = false)
        {
            Dictionary<int, CustomSkillInfo> customSkills = _session.GetCollection<CustomSkillInfo>().Binding
                .Where(x => x?.Magic != null)
                .GroupBy(x => x.Magic.Index)
                .ToDictionary(x => x.Key, x => x.First());

            IEnumerable<MagicInfo> magics = includeAllDatabaseSkills
                ? _session.GetCollection<MagicInfo>().Binding.Where(x => x != null)
                : customSkills.Values.Select(x => x.Magic);

            HashSet<string> usedFileNames = new(StringComparer.OrdinalIgnoreCase);

            foreach (MagicInfo magic in magics.OrderBy(x => x.Name).ThenBy(x => x.Index))
            {
                string fileName = GetUniqueFileName(SafeFileName(magic.Name), magic.Index, usedFileNames);
                string path = Path.Combine(directory, fileName + ".txt");
                if (File.Exists(path) && !overwrite)
                {
                    report.Failures.Add($"已存在技能文本，未覆盖: {path}");
                    continue;
                }

                if (customSkills.TryGetValue(magic.Index, out CustomSkillInfo skill))
                {
                    if (!TryWriteAllText(path, BuildSkillText(skill), report, $"技能 `{magic.Name}`"))
                        continue;
                }
                else
                {
                    if (!TryWriteAllText(path, BuildFallbackSkillText(magic, out bool autoMigrated), report, $"技能 `{magic.Name}`"))
                        continue;

                    if (includeAllDatabaseSkills && !autoMigrated)
                        report.TodoDetails.Add($"技能 `{magic.Name}` -> Skills\\{fileName}.txt: 数据库没有 CustomSkillInfo，已导出 Enabled=False 的自定义技能骨架；旧 MagicObject 逻辑未迁移。原因: {GetManualSkillMigrationReason(magic.Magic)}");
                }

                report.SkillsExported++;
            }
        }

        private static string FormatStats(Stats stats)
        {
            if (stats == null || stats.Values.Count == 0) return string.Empty;

            return string.Join(",", stats.Values
                .Where(x => x.Key != Stat.None && x.Value != 0)
                .Select(x => $"{x.Key}:{x.Value}"));
        }

        private static string BuildSkillText(CustomSkillInfo skill)
        {
            MagicInfo magic = skill.Magic;
            StringBuilder sb = new();
            sb.AppendLine("[Magic]");
            sb.AppendLine($"Index={magic.Index}");
            sb.AppendLine($"Name={Quote(magic.Name)}");
            sb.AppendLine($"Magic={magic.Magic}");
            sb.AppendLine($"Class={magic.Class}");
            sb.AppendLine($"School={magic.School}");
            sb.AppendLine($"Property={magic.Property}");
            sb.AppendLine($"Icon={magic.Icon}");
            sb.AppendLine($"MinBasePower={magic.MinBasePower}");
            sb.AppendLine($"MaxBasePower={magic.MaxBasePower}");
            sb.AppendLine($"MinLevelPower={magic.MinLevelPower}");
            sb.AppendLine($"MaxLevelPower={magic.MaxLevelPower}");
            sb.AppendLine($"BaseCost={magic.BaseCost}");
            sb.AppendLine($"LevelCost={magic.LevelCost}");
            sb.AppendLine($"NeedLevel1={magic.NeedLevel1}");
            sb.AppendLine($"NeedLevel2={magic.NeedLevel2}");
            sb.AppendLine($"NeedLevel3={magic.NeedLevel3}");
            sb.AppendLine($"Experience1={magic.Experience1}");
            sb.AppendLine($"Experience2={magic.Experience2}");
            sb.AppendLine($"Experience3={magic.Experience3}");
            sb.AppendLine($"Delay={magic.Delay}");
            sb.AppendLine($"Description={Quote(magic.Description)}");
            sb.AppendLine();
            sb.AppendLine("[Skill]");
            sb.AppendLine($"Enabled={skill.Enabled}");
            sb.AppendLine($"Template={skill.Template}");
            sb.AppendLine($"TargetRule={skill.TargetRule}");
            sb.AppendLine($"Range={skill.Range}");
            sb.AppendLine($"Radius={skill.Radius}");
            sb.AppendLine($"MaxTargets={skill.MaxTargets}");
            sb.AppendLine($"Shape={skill.Shape}");
            sb.AppendLine($"Length={skill.Length}");
            sb.AppendLine($"Width={skill.Width}");
            sb.AppendLine($"Angle={skill.Angle}");
            sb.AppendLine($"WaveCount={skill.WaveCount}");
            sb.AppendLine($"WaveInterval={skill.WaveInterval}");
            sb.AppendLine($"KnockbackDistance={skill.KnockbackDistance}");
            sb.AppendLine($"PullDistance={skill.PullDistance}");
            sb.AppendLine($"Element={skill.Element}");
            sb.AppendLine($"PowerSource={skill.PowerSource}");
            sb.AppendLine($"IgnoreShield={skill.IgnoreShield}");
            sb.AppendLine($"IgnoreArmour={skill.IgnoreArmour}");
            sb.AppendLine($"FixedPower={skill.FixedPower}");
            sb.AppendLine($"PowerRate={skill.PowerRate}");
            sb.AppendLine($"Duration={skill.Duration}");
            sb.AppendLine($"DurationPowerRate={skill.DurationPowerRate}");
            sb.AppendLine($"TickRate={skill.TickRate}");
            sb.AppendLine($"BuffType={skill.BuffType}");
            sb.AppendLine($"PoisonType={skill.PoisonType}");
            sb.AppendLine($"PoisonValue={skill.PoisonValue}");
            sb.AppendLine($"PoisonCanKill={skill.PoisonCanKill}");
            sb.AppendLine($"BuffStats={Quote(FormatStats(skill.BuffStats))}");
            sb.AppendLine($"BuffVisible={skill.BuffVisible}");
            sb.AppendLine($"BuffPause={skill.BuffPause}");
            sb.AppendLine($"BuffHidden={skill.BuffHidden}");
            sb.AppendLine($"BuffExtra={skill.BuffExtra}");
            sb.AppendLine($"CastDelay={skill.CastDelay}");
            sb.AppendLine($"ProjectileSpeed={skill.ProjectileSpeed}");
            sb.AppendLine($"SummonMonster={Quote(skill.SummonMonster?.MonsterName)}");
            sb.AppendLine($"MaxSummons={skill.MaxSummons}");
            sb.AppendLine($"SummonDuration={skill.SummonDuration}");

            foreach (CustomSkillEffectInfo effect in (skill.Effects?.Where(x => x != null) ?? Enumerable.Empty<CustomSkillEffectInfo>()).OrderBy(x => x.Stage).ThenBy(x => x.Index))
            {
                sb.AppendLine();
                sb.AppendLine("[Effect]");
                sb.AppendLine($"Stage={effect.Stage}");
                sb.AppendLine($"Resource={Quote(effect.Resource?.Key)}");
                sb.AppendLine($"Start={effect.StartIndex}");
                sb.AppendLine($"End={effect.EndIndex}");
                sb.AppendLine($"FrameCount={effect.FrameCount}");
                sb.AppendLine($"FrameDelay={effect.FrameDelay}");
                sb.AppendLine($"OffsetX={effect.OffsetX}");
                sb.AppendLine($"OffsetY={effect.OffsetY}");
                sb.AppendLine($"StartLight={effect.StartLight}");
                sb.AppendLine($"EndLight={effect.EndLight}");
                sb.AppendLine($"Sound={effect.Sound}");
                sb.AppendLine($"Blend={effect.Blend}");
                sb.AppendLine($"Loop={effect.Loop}");
                sb.AppendLine($"Duration={effect.Duration}");
                sb.AppendLine($"FollowTarget={effect.FollowTarget}");
            }

            return sb.ToString();
        }

        private static string BuildFallbackSkillText(MagicInfo magic, out bool autoMigrated)
        {
            StringBuilder sb = new();
            AppendMagicText(sb, magic);

            autoMigrated = TryGetAutoSkillProfile(magic, out AutoSkillExportProfile profile);
            if (autoMigrated)
                sb.AppendLine($"; AutoMigrated [Skill] 旧 MagicObject `{magic.Magic}` 已按模板 `{profile.Template}` 导出。复杂强化/被动联动仍以旧技能系统为准。");
            else
                sb.AppendLine($"; TODO [Skill] 数据库没有 CustomSkillInfo。旧技能逻辑仍在 C# MagicObject 中；迁移前保持 Enabled=False，避免覆盖正式旧技能。原因: {GetManualSkillMigrationReason(magic.Magic)}");

            sb.AppendLine("[Skill]");
            sb.AppendLine($"Enabled={autoMigrated}");
            sb.AppendLine($"Template={profile.Template}");
            sb.AppendLine($"TargetRule={profile.TargetRule}");
            sb.AppendLine($"Range={profile.Range}");
            sb.AppendLine($"Radius={profile.Radius}");
            sb.AppendLine($"MaxTargets={profile.MaxTargets}");
            sb.AppendLine($"Shape={profile.Shape}");
            sb.AppendLine($"Length={profile.Length}");
            sb.AppendLine($"Width={profile.Width}");
            sb.AppendLine($"Angle={profile.Angle}");
            sb.AppendLine($"WaveCount={profile.WaveCount}");
            sb.AppendLine($"WaveInterval={profile.WaveInterval}");
            sb.AppendLine($"KnockbackDistance={profile.KnockbackDistance}");
            sb.AppendLine($"PullDistance={profile.PullDistance}");
            sb.AppendLine($"Element={profile.Element}");
            sb.AppendLine($"PowerSource={profile.PowerSource}");
            sb.AppendLine($"IgnoreShield={profile.IgnoreShield}");
            sb.AppendLine($"IgnoreArmour={profile.IgnoreArmour}");
            sb.AppendLine($"FixedPower={profile.FixedPower}");
            sb.AppendLine($"PowerRate={profile.PowerRate}");
            sb.AppendLine($"Duration={profile.Duration}");
            sb.AppendLine($"DurationPowerRate={profile.DurationPowerRate}");
            sb.AppendLine($"TickRate={profile.TickRate}");
            sb.AppendLine($"BuffType={profile.BuffType}");
            sb.AppendLine($"PoisonType={profile.PoisonType}");
            sb.AppendLine($"PoisonValue={profile.PoisonValue}");
            sb.AppendLine($"PoisonCanKill={profile.PoisonCanKill}");
            sb.AppendLine($"BuffStats={Quote(profile.BuffStats)}");
            sb.AppendLine($"BuffVisible={profile.BuffVisible}");
            sb.AppendLine($"BuffPause={profile.BuffPause}");
            sb.AppendLine($"BuffHidden={profile.BuffHidden}");
            sb.AppendLine($"BuffExtra={profile.BuffExtra}");
            sb.AppendLine($"CastDelay={profile.CastDelay}");
            sb.AppendLine($"ProjectileSpeed={profile.ProjectileSpeed}");
            sb.AppendLine($"SummonMonster={Quote(profile.SummonMonster)}");
            sb.AppendLine($"MaxSummons={profile.MaxSummons}");
            sb.AppendLine($"SummonDuration={profile.SummonDuration}");

            return sb.ToString();
        }

        private static string GetManualSkillMigrationReason(MagicType magic)
        {
            return magic switch
            {
                MagicType.Unused =>
                    "占位或未使用技能，没有可迁移的旧运行逻辑。",

                MagicType.AugmentCelestialLight or
                MagicType.AugmentDefiance or
                MagicType.AugmentDestructiveSurge or
                MagicType.AugmentEvilSlayer or
                MagicType.AugmentExplosiveTalisman or
                MagicType.AugmentNeutralize or
                MagicType.AugmentPoisonDust or
                MagicType.AugmentPurification or
                MagicType.AugmentReflectDamage or
                MagicType.AugmentResurrection or
                MagicType.ArtOfShadows or
                MagicType.ElementalPuppet or
                MagicType.EmpoweredHealing or
                MagicType.PledgeOfBlood or
                MagicType.TheNewBeginning or
                MagicType.TouchOfTheDeparted =>
                    "强化/被动联动技能，旧逻辑通过 GetAugmentedSkill、额外冷却、等级经验和宿主技能回调触发；需要先新增强化技能挂载模板。",

                MagicType.Swordsmanship or
                MagicType.PotionMastery or
                MagicType.AdvancedPotionMastery or
                MagicType.Slaying or
                MagicType.Thrusting or
                MagicType.HalfMoon or
                MagicType.SwiftBlade or
                MagicType.BladeStorm or
                MagicType.FlamingSword or
                MagicType.DragonRise or
                MagicType.DestructiveSurge or
                MagicType.DefensiveBlow or
                MagicType.DefensiveMastery or
                MagicType.ElementalSwords or
                MagicType.DragonWave or
                MagicType.DualWeaponSkills or
                MagicType.FatalBlow or
                MagicType.Massacre or
                MagicType.FlameSplash or
                MagicType.WillowDance or
                MagicType.VineTreeDance or
                MagicType.WhiteLotus or
                MagicType.RedLotus or
                MagicType.SweetBrier or
                MagicType.WaningMoon or
                MagicType.FullBloom or
                MagicType.BloodyFlower or
                MagicType.DanceOfSwallow or
                MagicType.FlashOfLight or
                MagicType.CalamityOfFullMoon =>
                    "普攻被动、Toggle 或连招触发技能，旧逻辑依赖 AttackCast/AttackComplete/SecondaryAttackLocation；需要新增攻击触发模板。",

                MagicType.Burning or
                MagicType.Shocked or
                MagicType.Infection or
                MagicType.GhostWalk or
                MagicType.Stealth or
                MagicType.Release or
                MagicType.Resolution or
                MagicType.Rejuvenation or
                MagicType.Vitality or
                MagicType.Assault or
                MagicType.PhysicalImmunity or
                MagicType.MagicImmunity =>
                    "旧类本身没有完整主动施法流程，通常由其他技能、状态或被动 Process 间接调用；需要先确认目标自定义语义。",

                MagicType.MirrorImage =>
                    "玩家镜像技能，依赖玩家外观、装备、包体伪装和宠物生命周期；需要专用玩家镜像模板。",

                MagicType.SummonPuppet =>
                    "替身木偶技能，依赖玩家伪装、暗石属性继承、Cloak/GhostWalk 联动和多宠生命周期；需要专用替身模板。",

                MagicType.Resurrection or
                MagicType.SummonDead or
                MagicType.CorpseExploder =>
                    "尸体/死亡对象技能，依赖死亡玩家或怪物状态、复活/尸体清理和特殊成功率；需要新增尸体目标模板。",

                MagicType.DemonExplosion or
                MagicType.DemonicRecovery =>
                    "焰魔宠物专用技能，依赖指定宠物 Flag、宠物血量/位置和牺牲/治疗逻辑；需要新增宠物目标模板。",

                MagicType.ExpelUndead =>
                    "亡灵秒杀技能，依赖 Undead/Boss/等级/经验归属等特殊判定；需要新增条件击杀模板。",

                MagicType.Purification =>
                    "净化技能，旧逻辑是清除中毒和增益/减益状态，不是伤害或普通 Buff；需要新增状态清理模板。",

                MagicType.Teleportation =>
                    "随机传送技能，依赖地图 SkillDelay 和随机落点；当前 Move 模板只能传送到指定位置。",

                MagicType.Interchange =>
                    "换位技能，依赖目标合法性、双方位置交换、PVP 冷却和等级/可推动判定；需要新增换位模板。",

                MagicType.ShoulderDash or
                MagicType.DragonRepulse =>
                    "冲撞位移技能，依赖逐格移动、阻挡检查、推人和客户端 Dash 包；需要新增冲撞模板。",

                MagicType.MagicCombustion =>
                    "魔力燃烧技能，主效果是扣目标 MP；需要新增法力燃烧模板。",

                MagicType.SoulResonance =>
                    "队友灵魂绑定技能，依赖组队关系、双向 Buff 和死亡联动；需要新增组队绑定模板。",

                MagicType.Karma or
                MagicType.Renounce or
                MagicType.Discipline or
                MagicType.ElementalSuperiority or
                MagicType.ChangeOfSeasons or
                MagicType.AdventOfDemon or
                MagicType.AdventOfDevil or
                MagicType.DragonBlood =>
                    "职业专属状态或复杂资源转换技能，旧逻辑跨 Buff、装备、属性和被动回调；需要专用模板而不是基础模板硬迁。",

                _ =>
                    "旧技能包含当前基础模板尚不能表达的条件、状态或触发点，需新增专用模板后再迁移。",
            };
        }

        private static bool TryGetAutoSkillProfile(MagicInfo magic, out AutoSkillExportProfile profile)
        {
            profile = AutoSkillExportProfile.Default;
            if (magic == null || magic.Magic == MagicType.None) return false;

            profile.Element = GetAutoSkillElement(magic.Magic);

            switch (magic.Magic)
            {
                case MagicType.FireBall:
                case MagicType.AdamantineFireBall:
                case MagicType.IceBolt:
                case MagicType.IceBlades:
                case MagicType.ThunderBolt:
                case MagicType.LightningBall:
                case MagicType.Cyclone:
                case MagicType.GustBlast:
                case MagicType.EvilSlayer:
                case MagicType.GreaterEvilSlayer:
                case MagicType.ExplosiveTalisman:
                case MagicType.ImprovedExplosiveTalisman:
                case MagicType.SearingLight:
                case MagicType.Shuriken:
                    profile.Template = CustomSkillTemplate.ProjectileDamage;
                    profile.ProjectileSpeed = Globals.ProjectileSpeed;
                    return true;

                case MagicType.ThunderStrike:
                case MagicType.LightningStrike:
                case MagicType.ElectricShock:
                case MagicType.FrostBite:
                case MagicType.BurningFire:
                    profile.Template = CustomSkillTemplate.DirectDamage;
                    return true;

                case MagicType.ChainLightning:
                case MagicType.FireBounce:
                case MagicType.Chain:
                case MagicType.ChainOfFire:
                    profile.Template = CustomSkillTemplate.ChainDamage;
                    profile.Radius = 4;
                    profile.MaxTargets = 3;
                    return true;

                case MagicType.FireStorm:
                case MagicType.IceStorm:
                case MagicType.LightningWave:
                case MagicType.IceRain:
                case MagicType.MeteorShower:
                case MagicType.Tempest:
                case MagicType.Storm:
                case MagicType.FrozenEarth:
                case MagicType.GreaterFrozenEarth:
                case MagicType.ScortchedEarth:
                case MagicType.Asteroid:
                case MagicType.JudgementOfHeaven:
                    profile.Template = CustomSkillTemplate.AreaDamage;
                    profile.TargetRule = CustomSkillTargetRule.EnemyOrGround;
                    profile.Radius = magic.Magic is MagicType.MeteorShower or MagicType.Tempest or MagicType.Storm ? 4 : 2;
                    profile.MaxTargets = 0;
                    return true;

                case MagicType.FireWall:
                case MagicType.PoisonCloud:
                case MagicType.Tornado:
                    profile.Template = CustomSkillTemplate.PersistentAreaDamage;
                    profile.TargetRule = CustomSkillTargetRule.EnemyOrGround;
                    profile.Radius = magic.Magic == MagicType.Tornado ? 2 : 1;
                    profile.Duration = magic.Magic == MagicType.FireWall ? 10000 : 8000;
                    profile.TickRate = 1000;
                    return true;

                case MagicType.LightningBeam:
                case MagicType.BlowEarth:
                case MagicType.DragonTornado:
                case MagicType.ElementalHurricane:
                case MagicType.FrozenDragon:
                case MagicType.IceDragon:
                    profile.Template = CustomSkillTemplate.AreaDamage;
                    profile.TargetRule = CustomSkillTargetRule.EnemyOrGround;
                    profile.Shape = CustomSkillShape.Line;
                    profile.Length = Globals.MagicRange;
                    profile.Width = magic.Magic is MagicType.DragonTornado or MagicType.ElementalHurricane ? 3 : 1;
                    profile.Radius = profile.Length;
                    return true;

                case MagicType.Heal:
                    profile.Template = CustomSkillTemplate.Heal;
                    profile.TargetRule = CustomSkillTargetRule.AllyOrSelf;
                    profile.Element = Element.None;
                    profile.PowerSource = CustomSkillPowerSource.SC;
                    return true;

                case MagicType.MassHeal:
                    profile.Template = CustomSkillTemplate.Heal;
                    profile.TargetRule = CustomSkillTargetRule.AllyOrSelf;
                    profile.Element = Element.None;
                    profile.PowerSource = CustomSkillPowerSource.SC;
                    profile.Radius = 3;
                    profile.MaxTargets = 8;
                    return true;

                case MagicType.Invisibility:
                    ConfigureBuff(ref profile, BuffType.Invisibility, CustomSkillTargetRule.Self, "Invisibility=1", powerSource: CustomSkillPowerSource.SC, durationPowerRate: 1000);
                    return true;

                case MagicType.MassInvisibility:
                    ConfigureBuff(ref profile, BuffType.Invisibility, CustomSkillTargetRule.AllyOrSelf, "Invisibility=1", radius: 2, maxTargets: 0, powerSource: CustomSkillPowerSource.SC, durationPowerRate: 1000);
                    return true;

                case MagicType.Transparency:
                    ConfigureBuff(ref profile, BuffType.Transparency, CustomSkillTargetRule.Self, "Transparency=1", powerSource: CustomSkillPowerSource.SC, durationPowerRate: 1000);
                    return true;

                case MagicType.MagicShield:
                    ConfigureBuff(ref profile, BuffType.MagicShield, CustomSkillTargetRule.Self, "MagicShield=50", duration: 30000, powerSource: CustomSkillPowerSource.MC, powerRate: 50, durationPowerRate: 1000, castDelay: 1100);
                    return true;

                case MagicType.SuperiorMagicShield:
                    ConfigureBuff(ref profile, BuffType.SuperiorMagicShield, CustomSkillTargetRule.Self, "SuperiorMagicShield=25", duration: int.MaxValue, castDelay: 1100);
                    return true;

                case MagicType.MagicResistance:
                    ConfigureBuff(ref profile, BuffType.MagicResistance, CustomSkillTargetRule.AllyOrSelf, "MaxMR=5", radius: 3, maxTargets: 0, powerSource: CustomSkillPowerSource.SC, powerRate: 200, durationPowerRate: 1000);
                    return true;

                case MagicType.Resilience:
                    ConfigureBuff(ref profile, BuffType.Resilience, CustomSkillTargetRule.AllyOrSelf, "MaxAC=5,PhysicalResistance=1", radius: 3, maxTargets: 0, powerSource: CustomSkillPowerSource.SC, powerRate: 200, durationPowerRate: 1000);
                    return true;

                case MagicType.BloodLust:
                    ConfigureBuff(ref profile, BuffType.BloodLust, CustomSkillTargetRule.AllyOrSelf, "MaxDC=5", radius: 3, maxTargets: 0, powerSource: CustomSkillPowerSource.SC, powerRate: 200, durationPowerRate: 1000);
                    return true;

                case MagicType.LifeSteal:
                    ConfigureBuff(ref profile, BuffType.LifeSteal, CustomSkillTargetRule.AllyOrSelf, "LifeSteal=4", radius: 3, maxTargets: 0, powerSource: CustomSkillPowerSource.SC, durationPowerRate: 1000);
                    return true;

                case MagicType.Spiritualism:
                    ConfigureBuff(ref profile, BuffType.Spiritualism, CustomSkillTargetRule.Self, "MaxAC=5,MaxMR=5", powerSource: CustomSkillPowerSource.SC, powerRate: 200, durationPowerRate: 1000);
                    return true;

                case MagicType.CelestialLight:
                    ConfigureBuff(ref profile, BuffType.CelestialLight, CustomSkillTargetRule.Self, "CelestialLight=10", durationPowerRate: 1000, castDelay: 1500);
                    return true;

                case MagicType.StrengthOfFaith:
                    ConfigureBuff(ref profile, BuffType.StrengthOfFaith, CustomSkillTargetRule.Self, "DCPercent=-20,PetDCPercent=30", durationPowerRate: 1000);
                    return true;

                case MagicType.Endurance:
                    ConfigureBuff(ref profile, BuffType.Endurance, CustomSkillTargetRule.Self, string.Empty, duration: 10000, durationPowerRate: 5000);
                    return true;

                case MagicType.Defiance:
                    ConfigureBuff(ref profile, BuffType.Defiance, CustomSkillTargetRule.Self, "PhysicalDefencePercent=10,MagicDefencePercent=10,DCPercent=-20", duration: 60000, durationPowerRate: 1000);
                    return true;

                case MagicType.Might:
                    ConfigureBuff(ref profile, BuffType.Might, CustomSkillTargetRule.Self, "DCPercent=10,MagicDefencePercent=-10,PhysicalDefencePercent=-10", duration: 60000, durationPowerRate: 1000);
                    return true;

                case MagicType.ReflectDamage:
                    ConfigureBuff(ref profile, BuffType.ReflectDamage, CustomSkillTargetRule.Self, "ReflectDamage=5", duration: 15000, durationPowerRate: 1000);
                    return true;

                case MagicType.Invincibility:
                    ConfigureBuff(ref profile, BuffType.Invincibility, CustomSkillTargetRule.Self, "Invincibility=1", duration: 5000, durationPowerRate: 1000);
                    return true;

                case MagicType.RagingWind:
                    ConfigureBuff(ref profile, BuffType.RagingWind, CustomSkillTargetRule.Self, string.Empty, durationPowerRate: 1000);
                    return true;

                case MagicType.Evasion:
                    ConfigureBuff(ref profile, BuffType.Evasion, CustomSkillTargetRule.Self, "EvasionChance=4", durationPowerRate: 1000);
                    return true;

                case MagicType.Concentration:
                    ConfigureBuff(ref profile, BuffType.Concentration, CustomSkillTargetRule.Self, "CriticalChance=5", durationPowerRate: 1000);
                    return true;

                case MagicType.Cloak:
                    ConfigureBuff(ref profile, BuffType.Cloak, CustomSkillTargetRule.Self, "Cloak=1", duration: int.MaxValue, tickRate: 2000);
                    return true;

                case MagicType.PoisonDust:
                    ConfigureDebuff(ref profile, PoisonType.Green, powerSource: CustomSkillPowerSource.SC, durationPowerRate: 1000, tickRate: 2000, poisonValue: 1);
                    return true;

                case MagicType.Parasite:
                    ConfigureDebuff(ref profile, PoisonType.Parasite, powerSource: CustomSkillPowerSource.SC, duration: 20000, tickRate: 2000);
                    return true;

                case MagicType.Abyss:
                    ConfigureDebuff(ref profile, PoisonType.Abyss, powerSource: CustomSkillPowerSource.SC, duration: 8000, tickRate: 1000);
                    return true;

                case MagicType.WraithGrip:
                    ConfigureDebuff(ref profile, PoisonType.WraithGrip, powerSource: CustomSkillPowerSource.SC, durationPowerRate: 1000, tickRate: 1000);
                    return true;

                case MagicType.Fetter:
                    ConfigureDebuff(ref profile, PoisonType.Slow, targetRule: CustomSkillTargetRule.EnemyOrGround, radius: 2, duration: 8000, tickRate: 1000);
                    return true;

                case MagicType.Repulsion:
                    ConfigureControl(ref profile, radius: 1, knockbackDistance: 3);
                    return true;

                case MagicType.CombatKick:
                    ConfigureControl(ref profile, radius: 1, knockbackDistance: 3);
                    profile.Template = CustomSkillTemplate.AreaDamage;
                    profile.Shape = CustomSkillShape.Line;
                    profile.Length = 1;
                    profile.Width = 1;
                    profile.Radius = 1;
                    profile.PowerSource = CustomSkillPowerSource.DC;
                    return true;

                case MagicType.ThunderKick:
                    profile.Template = CustomSkillTemplate.AreaDamage;
                    profile.TargetRule = CustomSkillTargetRule.EnemyOrGround;
                    profile.Shape = CustomSkillShape.Circle;
                    profile.Radius = 1;
                    profile.MaxTargets = 0;
                    profile.PowerSource = CustomSkillPowerSource.DC;
                    profile.KnockbackDistance = 3;
                    return true;

                case MagicType.Beckon:
                    ConfigureControl(ref profile, radius: 1, pullDistance: 4);
                    return true;

                case MagicType.MassBeckon:
                    ConfigureControl(ref profile, targetRule: CustomSkillTargetRule.EnemyOrGround, radius: 4, pullDistance: 4);
                    return true;

                case MagicType.SummonSkeleton:
                    ConfigureSummon(ref profile, "变异骷髅");
                    return true;

                case MagicType.SummonShinsu:
                    ConfigureSummon(ref profile, "神兽");
                    return true;

                case MagicType.SummonJinSkeleton:
                    ConfigureSummon(ref profile, "超强骷髅");
                    return true;

                case MagicType.SummonDemonicCreature:
                    ConfigureSummon(ref profile, "焰魔");
                    return true;

                case MagicType.CursedDoll:
                    ConfigureSummon(ref profile, "诅咒娃娃");
                    return true;

                case MagicType.Shredding:
                    profile.Template = CustomSkillTemplate.DirectDamage;
                    profile.Element = Element.Fire;
                    profile.PowerSource = CustomSkillPowerSource.SC;
                    return true;

                case MagicType.HellFire:
                    profile.Template = CustomSkillTemplate.DirectDamage;
                    profile.Element = Element.Fire;
                    profile.PowerSource = CustomSkillPowerSource.DC;
                    profile.CastDelay = 1200;
                    return true;

                case MagicType.SeismicSlam:
                    profile.Template = CustomSkillTemplate.AreaDamage;
                    profile.TargetRule = CustomSkillTargetRule.EnemyOrGround;
                    profile.Shape = CustomSkillShape.Circle;
                    profile.Radius = 3;
                    profile.MaxTargets = 0;
                    profile.PowerSource = CustomSkillPowerSource.DC;
                    profile.CastDelay = 600;
                    return true;

                case MagicType.CrushingWave:
                    profile.Template = CustomSkillTemplate.AreaDamage;
                    profile.TargetRule = CustomSkillTargetRule.EnemyOrGround;
                    profile.Shape = CustomSkillShape.Line;
                    profile.Length = 12;
                    profile.Width = 3;
                    profile.Radius = 12;
                    profile.PowerSource = CustomSkillPowerSource.DC;
                    profile.CastDelay = 400;
                    return true;

                case MagicType.PoisonousCloud:
                    profile.Template = CustomSkillTemplate.PersistentAreaDamage;
                    profile.TargetRule = CustomSkillTargetRule.Self;
                    profile.Radius = 2;
                    profile.MaxTargets = 0;
                    profile.Element = Element.Dark;
                    profile.PowerSource = CustomSkillPowerSource.Fixed;
                    profile.FixedPower = 5;
                    profile.DurationPowerRate = 1000;
                    profile.TickRate = 1000;
                    return true;

                case MagicType.Neutralize:
                    ConfigureDebuff(ref profile, PoisonType.Neutralize, duration: 9000, tickRate: 1000);
                    profile.CastDelay = 1400;
                    return true;

                case MagicType.Containment:
                    ConfigureDebuff(ref profile, PoisonType.Containment, targetRule: CustomSkillTargetRule.EnemyOrGround, radius: 3, duration: 3000, tickRate: 1000, powerSource: CustomSkillPowerSource.SC);
                    profile.MaxTargets = 1;
                    return true;

                case MagicType.Hemorrhage:
                    ConfigureDebuff(ref profile, PoisonType.Hemorrhage, durationPowerRate: 1000, tickRate: 1000, powerSource: CustomSkillPowerSource.SC);
                    return true;

                case MagicType.BindingTalisman:
                    ConfigureDebuff(ref profile, PoisonType.Binding, durationPowerRate: 1000, tickRate: 1000, powerSource: CustomSkillPowerSource.SC);
                    profile.Template = CustomSkillTemplate.Control;
                    profile.Element = Element.Holy;
                    return true;

                case MagicType.TrapOctagon:
                    ConfigureControl(ref profile, targetRule: CustomSkillTargetRule.EnemyOrGround, radius: 1);
                    profile.PoisonType = PoisonType.Paralysis;
                    profile.DurationPowerRate = 1000;
                    profile.TickRate = 1000;
                    profile.PowerSource = CustomSkillPowerSource.SC;
                    return true;

                case MagicType.DarkSoulPrison:
                    profile.Template = CustomSkillTemplate.PersistentAreaDamage;
                    profile.TargetRule = CustomSkillTargetRule.EnemyOrGround;
                    profile.Radius = 3;
                    profile.MaxTargets = 0;
                    profile.Element = Element.Dark;
                    profile.PowerSource = CustomSkillPowerSource.SC;
                    profile.PowerRate = 40;
                    profile.Duration = 10000;
                    profile.TickRate = 2000;
                    return true;

                case MagicType.GeoManipulation:
                    profile.Template = CustomSkillTemplate.Move;
                    profile.TargetRule = CustomSkillTargetRule.Ground;
                    profile.Element = Element.None;
                    profile.CastDelay = 500;
                    return true;

                case MagicType.DarkConversion:
                    ConfigureBuff(ref profile, BuffType.DarkConversion, CustomSkillTargetRule.Self, "DarkConversion=1", duration: int.MaxValue, tickRate: 2000);
                    return true;

                case MagicType.Rake:
                    profile.Template = CustomSkillTemplate.DirectDamage;
                    profile.TargetRule = CustomSkillTargetRule.Enemy;
                    profile.Element = Element.None;
                    profile.PowerSource = CustomSkillPowerSource.DC;
                    profile.CastDelay = 600;
                    return true;

                default:
                    return false;
            }
        }

        private static void ConfigureBuff(
            ref AutoSkillExportProfile profile,
            BuffType buffType,
            CustomSkillTargetRule targetRule,
            string buffStats,
            int radius = 0,
            int maxTargets = 1,
            int duration = 0,
            int durationPowerRate = 0,
            int tickRate = 0,
            CustomSkillPowerSource powerSource = CustomSkillPowerSource.Auto,
            int powerRate = 100,
            int castDelay = 500)
        {
            profile.Template = CustomSkillTemplate.Buff;
            profile.TargetRule = targetRule;
            profile.Element = Element.None;
            profile.Radius = radius;
            profile.MaxTargets = maxTargets;
            profile.PowerSource = powerSource;
            profile.PowerRate = powerRate;
            profile.Duration = duration;
            profile.DurationPowerRate = durationPowerRate;
            profile.TickRate = tickRate <= 0 ? 1000 : tickRate;
            profile.BuffType = buffType;
            profile.BuffStats = buffStats;
            profile.CastDelay = castDelay;
        }

        private static void ConfigureDebuff(
            ref AutoSkillExportProfile profile,
            PoisonType poisonType,
            CustomSkillTargetRule targetRule = CustomSkillTargetRule.Enemy,
            int radius = 0,
            int duration = 0,
            int durationPowerRate = 0,
            int tickRate = 1000,
            int poisonValue = 0,
            CustomSkillPowerSource powerSource = CustomSkillPowerSource.Auto)
        {
            profile.Template = CustomSkillTemplate.Debuff;
            profile.TargetRule = targetRule;
            profile.Element = Element.None;
            profile.Radius = radius;
            profile.MaxTargets = radius > 0 ? 0 : 1;
            profile.PowerSource = powerSource;
            profile.Duration = duration;
            profile.DurationPowerRate = durationPowerRate;
            profile.TickRate = tickRate;
            profile.PoisonType = poisonType;
            profile.PoisonValue = poisonValue;
        }

        private static void ConfigureControl(
            ref AutoSkillExportProfile profile,
            CustomSkillTargetRule targetRule = CustomSkillTargetRule.EnemyOrGround,
            int radius = 0,
            int knockbackDistance = 0,
            int pullDistance = 0)
        {
            profile.Template = CustomSkillTemplate.Control;
            profile.TargetRule = targetRule;
            profile.Radius = radius;
            profile.MaxTargets = radius > 0 ? 0 : 1;
            profile.KnockbackDistance = knockbackDistance;
            profile.PullDistance = pullDistance;
            profile.Duration = 1000;
        }

        private static void ConfigureSummon(ref AutoSkillExportProfile profile, string monsterName)
        {
            profile.Template = CustomSkillTemplate.Summon;
            profile.TargetRule = CustomSkillTargetRule.Ground;
            profile.Element = Element.None;
            profile.SummonMonster = monsterName;
            profile.MaxSummons = 2;
            profile.SummonDuration = 0;
        }

        private static Element GetAutoSkillElement(MagicType magic)
        {
            return magic switch
            {
                MagicType.FireBall or MagicType.AdamantineFireBall or MagicType.FireStorm or MagicType.FireWall or MagicType.ScortchedEarth or MagicType.FireBounce or MagicType.ChainOfFire or MagicType.BurningFire => Element.Fire,
                MagicType.IceBolt or MagicType.IceBlades or MagicType.IceStorm or MagicType.FrozenEarth or MagicType.GreaterFrozenEarth or MagicType.IceRain or MagicType.FrostBite or MagicType.FrozenDragon or MagicType.IceDragon => Element.Ice,
                MagicType.ThunderBolt or MagicType.LightningBall or MagicType.LightningStrike or MagicType.LightningBeam or MagicType.LightningWave or MagicType.ElectricShock or MagicType.ChainLightning or MagicType.ThunderStrike => Element.Lightning,
                MagicType.Cyclone or MagicType.BlowEarth or MagicType.DragonTornado or MagicType.GustBlast or MagicType.Tornado or MagicType.Tempest or MagicType.Storm or MagicType.ElementalHurricane => Element.Wind,
                MagicType.EvilSlayer or MagicType.GreaterEvilSlayer or MagicType.SearingLight => Element.Holy,
                MagicType.PoisonCloud => Element.Dark,
                _ => Element.None,
            };
        }

        private struct AutoSkillExportProfile
        {
            public bool Enabled { get; set; }
            public CustomSkillTemplate Template { get; set; }
            public CustomSkillTargetRule TargetRule { get; set; }
            public int Range { get; set; }
            public int Radius { get; set; }
            public int MaxTargets { get; set; }
            public CustomSkillShape Shape { get; set; }
            public int Length { get; set; }
            public int Width { get; set; }
            public int Angle { get; set; }
            public int WaveCount { get; set; }
            public int WaveInterval { get; set; }
            public int KnockbackDistance { get; set; }
            public int PullDistance { get; set; }
            public Element Element { get; set; }
            public CustomSkillPowerSource PowerSource { get; set; }
            public bool IgnoreShield { get; set; }
            public bool IgnoreArmour { get; set; }
            public int FixedPower { get; set; }
            public int PowerRate { get; set; }
            public int Duration { get; set; }
            public int DurationPowerRate { get; set; }
            public int TickRate { get; set; }
            public BuffType BuffType { get; set; }
            public PoisonType PoisonType { get; set; }
            public int PoisonValue { get; set; }
            public bool PoisonCanKill { get; set; }
            public string BuffStats { get; set; }
            public bool BuffVisible { get; set; }
            public bool BuffPause { get; set; }
            public bool BuffHidden { get; set; }
            public int BuffExtra { get; set; }
            public int CastDelay { get; set; }
            public int ProjectileSpeed { get; set; }
            public string SummonMonster { get; set; }
            public int MaxSummons { get; set; }
            public int SummonDuration { get; set; }

            public static AutoSkillExportProfile Default => new()
            {
                Template = CustomSkillTemplate.DirectDamage,
                TargetRule = CustomSkillTargetRule.Enemy,
                Range = Globals.MagicRange,
                Shape = CustomSkillShape.Circle,
                WaveCount = 1,
                Element = Element.None,
                PowerSource = CustomSkillPowerSource.Auto,
                PowerRate = 100,
                TickRate = 1000,
                BuffType = BuffType.None,
                PoisonType = PoisonType.None,
                BuffStats = string.Empty,
                BuffVisible = true,
                CastDelay = 500,
                ProjectileSpeed = Globals.ProjectileSpeed,
                SummonMonster = string.Empty,
                MaxSummons = 1,
            };
        }

        private static void AppendMagicText(StringBuilder sb, MagicInfo magic)
        {
            sb.AppendLine("[Magic]");
            sb.AppendLine($"Index={magic.Index}");
            sb.AppendLine($"Name={Quote(magic.Name)}");
            sb.AppendLine($"Magic={magic.Magic}");
            sb.AppendLine($"Class={magic.Class}");
            sb.AppendLine($"School={magic.School}");
            sb.AppendLine($"Property={magic.Property}");
            sb.AppendLine($"Icon={magic.Icon}");
            sb.AppendLine($"MinBasePower={magic.MinBasePower}");
            sb.AppendLine($"MaxBasePower={magic.MaxBasePower}");
            sb.AppendLine($"MinLevelPower={magic.MinLevelPower}");
            sb.AppendLine($"MaxLevelPower={magic.MaxLevelPower}");
            sb.AppendLine($"BaseCost={magic.BaseCost}");
            sb.AppendLine($"LevelCost={magic.LevelCost}");
            sb.AppendLine($"NeedLevel1={magic.NeedLevel1}");
            sb.AppendLine($"NeedLevel2={magic.NeedLevel2}");
            sb.AppendLine($"NeedLevel3={magic.NeedLevel3}");
            sb.AppendLine($"Experience1={magic.Experience1}");
            sb.AppendLine($"Experience2={magic.Experience2}");
            sb.AppendLine($"Experience3={magic.Experience3}");
            sb.AppendLine($"Delay={magic.Delay}");
            sb.AppendLine($"Description={Quote(magic.Description)}");
            sb.AppendLine();
        }

        private static void ExportTodoReport(string path, bool overwrite, CustomSystemTextReport report)
        {
            if (File.Exists(path) && !overwrite)
            {
                report.Failures.Add($"已存在 TODO 报告，未覆盖: {path}");
                return;
            }

            TryWriteAllText(path, CustomSystemReportFormatter.BuildTodoReportText(report), report, "TODO 报告");
        }

        private static bool TryWriteAllLines(string path, IEnumerable<string> lines, CustomSystemTextReport report, string description)
        {
            string tempPath = null;

            try
            {
                tempPath = GetTempWritePath(path);
                File.WriteAllLines(tempPath, lines, new UTF8Encoding(false));
                ReplaceOrMoveTempFile(tempPath, path);
                return true;
            }
            catch (Exception ex) when (IsExportWriteException(ex))
            {
                TryDeleteTempFile(tempPath);
                report.Failures.Add($"{description} 写入失败: {path} ({ex.Message})");
                return false;
            }
        }

        private static bool TryWriteAllText(string path, string text, CustomSystemTextReport report, string description)
        {
            string tempPath = null;

            try
            {
                tempPath = GetTempWritePath(path);
                File.WriteAllText(tempPath, text, new UTF8Encoding(false));
                ReplaceOrMoveTempFile(tempPath, path);
                return true;
            }
            catch (Exception ex) when (IsExportWriteException(ex))
            {
                TryDeleteTempFile(tempPath);
                report.Failures.Add($"{description} 写入失败: {path} ({ex.Message})");
                return false;
            }
        }

        private static void ReplaceOrMoveTempFile(string tempPath, string destinationPath)
        {
            if (File.Exists(destinationPath))
                File.Replace(tempPath, destinationPath, null);
            else
                File.Move(tempPath, destinationPath);
        }

        private static string GetTempWritePath(string path)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            string fileName = Path.GetFileName(path);
            return Path.Combine(string.IsNullOrEmpty(directory) ? "." : directory, $".{fileName}.{Guid.NewGuid():N}.tmp");
        }

        private static void TryDeleteTempFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex) when (IsExportWriteException(ex))
            {
            }
        }

        private static bool TryCreateExportDirectories(string directory, CustomSystemTextReport report)
        {
            try
            {
                Directory.CreateDirectory(directory);
                Directory.CreateDirectory(Path.Combine(directory, "Monsters"));
                Directory.CreateDirectory(Path.Combine(directory, "Skills"));
                return true;
            }
            catch (Exception ex) when (IsExportWriteException(ex))
            {
                report.Failures.Add($"创建自定义系统导出目录失败: {directory} ({ex.Message})");
                return false;
            }
        }

        private static bool IsExportWriteException(Exception ex)
        {
            return ex is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException;
        }

        private static string SafeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "Unnamed";
            foreach (char c in Path.GetInvalidFileNameChars())
                value = value.Replace(c.ToString(), string.Empty);
            return string.IsNullOrWhiteSpace(value) ? "Unnamed" : value.Trim();
        }

        private static string GetUniqueFileName(string fileName, int index, HashSet<string> usedFileNames)
        {
            fileName = SafeFileName(fileName);
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

        private static string Quote(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "\"\"";
            return value.Any(char.IsWhiteSpace) || value.Contains('"') || value.Contains(',')
                ? $"\"{value.Replace("\"", "\\\"")}\""
                : value;
        }
    }

    public sealed class CustomSystemTextLoadResult
    {
        public CustomSystemTextReport Report { get; } = new();
        public List<CustomResourceLibraryInfo> Resources { get; } = [];
        public List<CustomMonsterVisualInfo> Visuals { get; } = [];
        public List<CustomMonsterBehaviorInfo> Behaviors { get; } = [];
        public List<CustomSkillInfo> Skills { get; } = [];
        public Dictionary<MonsterInfo, CompiledMonsterDrop> DropOverrides { get; } = [];
    }

    public sealed class CustomSystemTextReport
    {
        public CustomSystemImportMode Mode { get; set; }
        public int ResourcesLoaded { get; set; }
        public int MonsterFilesLoaded { get; set; }
        public int TemplateMonsterFilesLoaded { get; set; }
        public int MonsterStatsLoaded { get; set; }
        public int MonsterDropEntriesLoaded { get; set; }
        public int MonsterDropGroupsLoaded { get; set; }
        public int MonsterDropGoldLines { get; set; }
        public int MonsterDropQuestOnlyLines { get; set; }
        public int MonsterVisualsLoaded { get; set; }
        public int MonsterBehaviorsLoaded { get; set; }
        public int SkillFilesLoaded { get; set; }
        public int TemplateSkillFilesLoaded { get; set; }
        public int MonstersSkipped { get; set; }
        public int SkillsSkipped { get; set; }
        public int ResourcesImported { get; set; }
        public int MonsterStatsImported { get; set; }
        public int MonsterDropEntriesImported { get; set; }
        public int MonsterDropGroupsImported { get; set; }
        public int MonsterVisualsImported { get; set; }
        public int MonsterBehaviorsImported { get; set; }
        public int SkillsImported { get; set; }
        public int ResourcesExported { get; set; }
        public int MonstersExported { get; set; }
        public int MonsterStatsExported { get; set; }
        public int MonsterDropEntriesExported { get; set; }
        public int MonsterDropGroupsExported { get; set; }
        public int MonsterVisualsExported { get; set; }
        public int MonsterBehaviorsExported { get; set; }
        public int SkillsExported { get; set; }
        public List<string> Failures { get; } = [];
        public List<string> MissingResources { get; } = [];
        public List<string> MissingMonsters { get; } = [];
        public List<string> MissingMagics { get; } = [];
        public List<string> MissingItems { get; } = [];
        public List<string> TodoDetails { get; } = [];
        public IEnumerable<string> PendingTodoDetails => TodoDetails.Where(IsPendingTodoDetail);
        public int PendingTodoCount => PendingTodoDetails.Count();

        public string ToDisplayText()
        {
            return CustomSystemReportFormatter.BuildDisplayText(this);
        }

        private static bool IsPendingTodoDetail(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;

            return value.StartsWith("怪物 `", StringComparison.Ordinal) ||
                   value.StartsWith("技能 `", StringComparison.Ordinal);
        }
    }

    internal sealed class CustomTextDocument
    {
        public List<CustomTextSection> Sections { get; } = [];

        public CustomTextSection First(string name)
        {
            return Sections.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }
    }

    internal sealed class CustomTextSection
    {
        public CustomTextSection(string name, string source)
        {
            Name = name;
            Source = source;
        }

        public string Name { get; }
        public string Source { get; }
        public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Lines { get; } = [];
    }

    internal readonly struct CustomTextLine
    {
        public CustomTextLine(string file, int line, string text)
        {
            File = file;
            Line = line;
            Text = text;
        }

        public string File { get; }
        public int Line { get; }
        public string Text { get; }
        public string Source => $"{Path.GetFileName(File)}:{Line}";
    }
}
