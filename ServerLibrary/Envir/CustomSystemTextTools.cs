using Library;
using Library.SystemModels;
using MirDB;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

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
            "BuffStatsFromTarget",
            "CellTime",
            "ClassPowerRates",
            "Clear",
            "CollisionAction",
            "CollisionRadius",
            "CullMissingMinions",
            "DrainMP",
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
            "Scope",
            "SpecialRegion",
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
            IndexMonsterFilePrimaries(directory, createDetached: true, allowMissingPrimaryObject: false, includeTemplates: false);
            Dictionary<string, bool> damageProxyMonsters = BuildDamageProxyMonsterLookup(directory, includeTemplates: false);
            LoadSkillFiles(directory, result, resources, damageProxyMonsters, createDetached: true, allowMissingPrimaryObject: false, strictMode: strictMode, replaceExisting: false, includeTemplates: false);
            LoadMonsterFiles(directory, result, resources, createDetached: true, allowMissingPrimaryObject: false, strictMode, replaceExisting: false, includeTemplates: false);

            return result;
        }

        internal CustomSystemTextLoadResult BuildForDatabaseImport(string directory, CustomSystemImportMode mode)
        {
            CustomSystemTextLoadResult result = new();
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                result.Report.Failures.Add($"自定义系统目录不存在: {directory}");
                return result;
            }

            Dictionary<string, CustomResourceLibraryInfo> resources = new(_resources, StringComparer.OrdinalIgnoreCase);

            bool validateOnly = mode == CustomSystemImportMode.ValidateOnly;

            LoadResources(directory, result, resources, createDetached: validateOnly);
            IndexMonsterFilePrimaries(directory, createDetached: validateOnly, allowMissingPrimaryObject: true, includeTemplates: validateOnly);
            Dictionary<string, bool> damageProxyMonsters = BuildDamageProxyMonsterLookup(directory, includeTemplates: validateOnly);
            LoadSkillFiles(directory, result, resources, damageProxyMonsters, createDetached: validateOnly, allowMissingPrimaryObject: true, strictMode: true, replaceExisting: mode == CustomSystemImportMode.ReplaceExisting, includeTemplates: validateOnly);
            LoadMonsterFiles(directory, result, resources, createDetached: validateOnly, allowMissingPrimaryObject: true, strictMode: true, replaceExisting: mode == CustomSystemImportMode.ReplaceExisting, includeTemplates: validateOnly);

            return result;
        }

        private void LoadResources(string directory, CustomSystemTextLoadResult result, Dictionary<string, CustomResourceLibraryInfo> resources, bool createDetached)
        {
            string path = Path.Combine(directory, "Resources.txt");
            if (!File.Exists(path)) return;

            foreach (CustomTextLine line in ReadCleanLines(path, directory, result.Report, []))
            {
                List<string> tokens = Tokenize(line.Text);
                if (tokens.Count < 1)
                {
                    result.Report.Failures.Add($"{line.Source}: Resources.txt 行至少需要 Key。");
                    continue;
                }

                string key = tokens[0].Trim();
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

                CustomResourceLibraryInfo resource = createDetached ? new CustomResourceLibraryInfo() : resources.GetValueOrDefault(Normalize(key));
                if (!createDetached)
                    resource ??= _session.GetCollection<CustomResourceLibraryInfo>().CreateNewObject();
                else if (resource == null)
                    continue;

                resource.Key = key;
                resource.RelativePath = relativePath;
                resource.LibraryFile = libraryFile;
                resource.DisplayName = GetOption(options, "DisplayName", resource.DisplayName);
                resource.Hash = GetOption(options, "Hash", resource.Hash);
                resource.Version = GetOption(options, "Version", resource.Version);
                resource.Enabled = GetBool(options, "Enabled", true);
                resource.Scope = GetEnum(options, "Scope", CustomResourceScope.Both, result.Report, line.Source);

                resources[Normalize(key)] = resource;
                result.Resources.Add(resource);
                result.Report.ResourcesLoaded++;
            }
        }

        private void IndexMonsterFilePrimaries(string directory, bool createDetached, bool allowMissingPrimaryObject, bool includeTemplates)
        {
            string monstersDirectory = Path.Combine(directory, "Monsters");
            if (!Directory.Exists(monstersDirectory)) return;

            foreach (string file in EnumerateCustomTextFiles(monstersDirectory, includeTemplates))
            {
                CustomTextDocument document = ReadDocument(file, directory, new CustomSystemTextReport());
                CustomTextSection monsterSection = document.First("Monster");
                if (monsterSection == null) continue;

                MonsterInfo monster = ResolveMonster(monsterSection.Values, file, new CustomSystemTextReport(), allowMissingPrimaryObject, createDetached);
                if (monster == null) continue;

                if (createDetached)
                    _detachedTextMonsters.Add(monster);

                IndexMonster(monster);
            }
        }

        private Dictionary<string, bool> BuildDamageProxyMonsterLookup(string directory, bool includeTemplates)
        {
            Dictionary<string, bool> result = new(StringComparer.OrdinalIgnoreCase);

            foreach (CustomMonsterBehaviorInfo behavior in _session.GetCollection<CustomMonsterBehaviorInfo>().Binding.Where(x => x?.Monster != null))
                SetDamageProxyLookup(result, behavior.Monster, behavior.DamageProxy);

            string monstersDirectory = Path.Combine(directory, "Monsters");
            if (!Directory.Exists(monstersDirectory)) return result;

            foreach (string file in EnumerateCustomTextFiles(monstersDirectory, includeTemplates))
            {
                CustomTextDocument document = ReadDocument(file, directory, new CustomSystemTextReport());
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

            foreach (string file in EnumerateCustomTextFiles(monstersDirectory, includeTemplates))
            {
                int beforeFailures = result.Report.Failures.Count;
                CustomTextDocument document = ReadDocument(file, directory, result.Report);
                if (document.Sections.Count == 0) continue;

                ValidateKnownBooleanFields(document, result.Report);

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
                    CustomMonsterVisualInfo visual = createDetached ? new CustomMonsterVisualInfo() : null;
                    if (!createDetached)
                    {
                        visual ??= _session.GetCollection<CustomMonsterVisualInfo>().Binding.FirstOrDefault(x => x.Monster == monster);
                        visual ??= _session.GetCollection<CustomMonsterVisualInfo>().CreateNewObject();
                    }
                    if (visual == null) continue;
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
                    CustomMonsterBehaviorInfo behavior = createDetached ? new CustomMonsterBehaviorInfo() : null;
                    if (!createDetached)
                    {
                        behavior ??= _session.GetCollection<CustomMonsterBehaviorInfo>().Binding.FirstOrDefault(x => x.Monster == monster);
                        behavior ??= _session.GetCollection<CustomMonsterBehaviorInfo>().CreateNewObject();
                    }
                    if (behavior == null) continue;
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

        private static void ValidateKnownBooleanOptions(Dictionary<string, string> values, CustomSystemTextReport report, string source)
        {
            foreach (KeyValuePair<string, string> pair in values)
            {
                if (!BooleanTextKeys.Contains(pair.Key)) continue;
                if (TryParseTextBool(pair.Value, out _)) continue;

                report.Failures.Add($"{source}: `{pair.Key}` 布尔值无效: {pair.Value}，应为 True/False/1/0/yes/no。");
            }
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

            foreach (string file in EnumerateCustomTextFiles(skillsDirectory, includeTemplates))
            {
                int beforeFailures = result.Report.Failures.Count;
                CustomTextDocument document = ReadDocument(file, directory, result.Report);
                if (document.Sections.Count == 0) continue;

                ValidateKnownBooleanFields(document, result.Report);

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

                CustomSkillInfo skill = createDetached ? new CustomSkillInfo() : null;
                if (!createDetached)
                {
                    skill ??= _session.GetCollection<CustomSkillInfo>().Binding.FirstOrDefault(x => x.Magic == magic);
                    skill ??= _session.GetCollection<CustomSkillInfo>().CreateNewObject();
                }
                if (skill == null) continue;
                if (replaceExisting)
                    ClearSkillChildren(skill);
                else if (!createDetached)
                    DeduplicateSkillChildren(skill);

                ApplySkill(skill, magic, skillSection.Values, result.Report, skillSection.Source);
                ValidateDamageProxySkill(skill, damageProxyMonsters, result.Report, skillSection.Source);

                foreach (CustomTextSection section in document.Sections.Where(x => x.Name.Equals("Effect", StringComparison.OrdinalIgnoreCase)))
                {
                    CustomSkillEffectInfo effect = ResolveSkillEffect(skill, section.Values, resources, createDetached, replaceExisting);
                    effect.Skill = skill;
                    ApplySkillEffect(effect, section.Values, resources, result.Report, section.Source);
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

            if (values.TryGetValue("Index", out string indexValue) && int.TryParse(indexValue, out int index))
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

            if (values.TryGetValue("Index", out string indexValue) && int.TryParse(indexValue, out int index))
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

            ValidateBuffStatsFromTargetOption(action, report, source);
            ValidateBuffTypeActionOption(action, "RequireHasBuff", report, source);
            ValidateBuffTypeActionOption(action, "RequireMissingBuff", report, source);
            ValidatePowerSourceActionOption(action, "TargetHealthPower", report, source);
            ValidateDirectionActionOption(action, "OriginDirection", report, source);
            ValidateDirectionActionOption(action, "OffsetDirection", report, source);
        }

        private static void ValidateKnownAiActionOptionNames(CustomMonsterAiActionInfo action, CustomSystemTextReport report, string source)
        {
            bool allowsActionReferences = action.Action is CustomAiActionType.WeightedChoice
                or CustomAiActionType.Sequence
                or CustomAiActionType.ScheduleAction;

            foreach (string token in SplitActionOptionTokens(action.Options))
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

            if (bool.TryParse(value, out _)) return;
            if (value == "1" || value == "0") return;

            report.Failures.Add($"{source}: AIAction `{action.Name}` 的 {optionName} `{value}` 必须是 True/False/1/0。");
        }

        private static void ValidateBuffStatsFromTargetOption(CustomMonsterAiActionInfo action, CustomSystemTextReport report, string source)
        {
            string value = GetActionOptionValue(action.Options, "BuffStatsFromTarget");
            if (string.IsNullOrWhiteSpace(value)) return;

            if (action.Action != CustomAiActionType.AddBuff)
                report.Failures.Add($"{source}: AIAction `{action.Name}` 配置 BuffStatsFromTarget 时 Type 应为 AddBuff。");

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
                        report.Failures.Add($"{source}: AIAction `{action.Name}` 的 BuffStatsFromTarget 项 `{token}` 必须写成 来源Stat>目标Stat:倍率 或 来源Stat:目标Stat:倍率。");
                        continue;
                    }

                    sourceStat = statParts[0];
                    destinationStat = statParts[1];
                    rateValue = rateParts[1];
                }
                else
                {
                    report.Failures.Add($"{source}: AIAction `{action.Name}` 的 BuffStatsFromTarget 项 `{token}` 必须写成 来源Stat>目标Stat:倍率 或 来源Stat:目标Stat:倍率。");
                    continue;
                }

                if (!Enum.TryParse(sourceStat, true, out Stat parsedSource) || parsedSource == Stat.None)
                    report.Failures.Add($"{source}: AIAction `{action.Name}` 的 BuffStatsFromTarget 来源 Stat `{sourceStat}` 无效。");

                if (!Enum.TryParse(destinationStat, true, out Stat parsedDestination) || parsedDestination == Stat.None)
                    report.Failures.Add($"{source}: AIAction `{action.Name}` 的 BuffStatsFromTarget 目标 Stat `{destinationStat}` 无效。");

                if (!int.TryParse(rateValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int rate) || rate < 0)
                    report.Failures.Add($"{source}: AIAction `{action.Name}` 的 BuffStatsFromTarget 倍率 `{rateValue}` 必须是非负整数。");
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

            if (!string.IsNullOrWhiteSpace(flagName) && !Enum.TryParse(flagName, true, out MonsterFlag _))
                report.Failures.Add($"{source}: AIAction `{action.Name}` 的 MonsterFlag `{flagName}` 不是有效 MonsterFlag 枚举。");

            string directionName = GetActionOptionValue(action.Options, "OriginDirection");
            if (string.IsNullOrWhiteSpace(directionName))
                directionName = GetActionOptionValue(action.Options, "OffsetDirection");

            if (!string.IsNullOrWhiteSpace(directionName) && !Enum.TryParse(directionName, true, out MirDirection _))
                report.Failures.Add($"{source}: AIAction `{action.Name}` 的 OriginDirection `{directionName}` 不是有效 MirDirection 枚举。");
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
                string name = token.TrimStart('!').Split(':', 2, StringSplitOptions.TrimEntries)[0];
                if (!Enum.TryParse(name, true, out CustomAiConditionType condition))
                {
                    report.Failures.Add($"{source}: AIRule `{rule.Name}` 的 Conditions 包含未知条件 `{name}`。");
                    continue;
                }

                if (RuleConditionRequiresBuff(condition) && rule.BuffType == BuffType.None && !ConditionTokenHasValue(token, 1))
                    report.Failures.Add($"{source}: AIRule `{rule.Name}` 的 Conditions 条件 `{condition}` 必须在条件参数或 BuffType 中指定 Buff。");

                if (RuleConditionRequiresCounter(condition) && string.IsNullOrWhiteSpace(rule.CounterName) && !ConditionTokenHasValue(token, 2))
                    report.Failures.Add($"{source}: AIRule `{rule.Name}` 的 Conditions 条件 `{condition}` 必须在条件参数或 CounterName 中指定计数器名。");
            }
        }

        private static IEnumerable<string> SplitConditionTokens(string conditions)
        {
            return conditions
                .Replace("&&", ";", StringComparison.Ordinal)
                .Replace("||", ";", StringComparison.Ordinal)
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        private static bool ConditionTokenHasValue(string token, int partIndex)
        {
            string[] parts = token.TrimStart('!').Split(':', StringSplitOptions.TrimEntries);
            return parts.Length > partIndex && !string.IsNullOrWhiteSpace(parts[partIndex]);
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
            skill.Magic = magic;
            skill.Enabled = GetBool(values, "Enabled", true);
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

        private static void ApplySkillEffect(CustomSkillEffectInfo effect, Dictionary<string, string> values, Dictionary<string, CustomResourceLibraryInfo> resources, CustomSystemTextReport report, string source)
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
            effect.FollowTarget = GetBool(values, "FollowTarget", true);

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

            if (int.TryParse(value, out int index) && _magicsByIndex.TryGetValue(index, out MagicInfo byIndex))
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

            if (int.TryParse(value, out int index) && _monstersByIndex.TryGetValue(index, out MonsterInfo byIndex))
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

        private CustomMonsterAnimationInfo ResolveAnimation(CustomMonsterVisualInfo visual, Dictionary<string, string> values, bool createDetached, bool replaceExisting)
        {
            if (createDetached || replaceExisting || visual?.Animations == null)
                return createDetached ? new CustomMonsterAnimationInfo() : _session.GetCollection<CustomMonsterAnimationInfo>().CreateNewObject();

            CustomMonsterAnimationType action = GetEnum(values, "Action", CustomMonsterAnimationType.Standing);
            int variant = GetInt(values, "Variant", 0);

            return visual.Animations.FirstOrDefault(x => x != null && x.Action == action && x.Variant == variant)
                   ?? _session.GetCollection<CustomMonsterAnimationInfo>().CreateNewObject();
        }

        private CustomMonsterSkillInfo ResolveMonsterSkill(CustomMonsterBehaviorInfo behavior, Dictionary<string, string> values, bool createDetached, bool replaceExisting)
        {
            if (createDetached || replaceExisting || behavior?.Skills == null)
                return createDetached ? new CustomMonsterSkillInfo() : _session.GetCollection<CustomMonsterSkillInfo>().CreateNewObject();

            MagicInfo magic = TryResolveMagicReference(values, "Magic");
            CustomAiTargetSelectorType target = GetEnum(values, "Target", CustomAiTargetSelectorType.CurrentTarget);
            int animationVariant = GetInt(values, "AnimationVariant", GetInt(values, "Variant", 0));

            return behavior.Skills.FirstOrDefault(x => x != null && x.Magic == magic && x.Target == target && x.AnimationVariant == animationVariant)
                   ?? _session.GetCollection<CustomMonsterSkillInfo>().CreateNewObject();
        }

        private CustomMonsterAiRuleInfo ResolveAiRule(CustomMonsterBehaviorInfo behavior, Dictionary<string, string> values, bool createDetached, bool replaceExisting)
        {
            if (createDetached || replaceExisting || behavior?.Rules == null)
                return createDetached ? new CustomMonsterAiRuleInfo() : _session.GetCollection<CustomMonsterAiRuleInfo>().CreateNewObject();

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
                return createDetached ? new CustomMonsterAiActionInfo() : _session.GetCollection<CustomMonsterAiActionInfo>().CreateNewObject();

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
                return createDetached ? new CustomSkillEffectInfo() : _session.GetCollection<CustomSkillEffectInfo>().CreateNewObject();

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

            if (int.TryParse(value, out int index) && _magicsByIndex.TryGetValue(index, out MagicInfo byIndex))
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
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                report.Failures.Add($"{source}: 资源路径不能为空。");
                return false;
            }

            if (Path.IsPathRooted(relativePath) || relativePath.Contains(".."))
            {
                report.Failures.Add($"{source}: 资源路径必须是客户端目录内的相对路径，不能包含绝对路径或 `..`: {relativePath}");
                return false;
            }

            return true;
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

            foreach (CustomTextLine line in ReadCleanLines(path, rootDirectory, report, []))
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
            string fullPath = Path.GetFullPath(path);

            if (!stack.Add(fullPath))
            {
                report.Failures.Add($"检测到循环 #INCLUDE/#INSERT: {fullPath}");
                return result;
            }

            string[] lines = File.ReadAllLines(fullPath);
            for (int i = 0; i < lines.Length; i++)
            {
                string text = StripComment(lines[i]).Trim();
                if (string.IsNullOrWhiteSpace(text)) continue;

                if (text.StartsWith("#INSERT", StringComparison.OrdinalIgnoreCase) || text.StartsWith("#INCLUDE", StringComparison.OrdinalIgnoreCase))
                {
                    int start = text.IndexOf('[');
                    int end = text.IndexOf(']');
                    if (start < 0 || end <= start)
                    {
                        report.Failures.Add($"{Path.GetFileName(fullPath)}:{i + 1}: #INSERT/#INCLUDE 格式必须是 #INCLUDE [file]。");
                        continue;
                    }

                    string include = text.Substring(start + 1, end - start - 1);
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

            stack.Remove(fullPath);
            return result;
        }

        private static string ResolveIncludePath(string rootDirectory, string include, CustomSystemTextReport report, string source)
        {
            if (string.IsNullOrWhiteSpace(include) || Path.IsPathRooted(include))
            {
                report.Failures.Add($"{source}: include 路径必须是自定义目录内的相对路径: {include}");
                return null;
            }

            string root = Path.GetFullPath(rootDirectory);
            string includePath = Path.GetFullPath(Path.Combine(root, include));

            bool insideRoot = includePath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(includePath, root, StringComparison.OrdinalIgnoreCase);
            if (insideRoot)
                return includePath;

            report.Failures.Add($"{source}: include 路径不能跳出自定义目录: {include}");
            return null;
        }

        private static string StripComment(string line)
        {
            if (line == null) return string.Empty;
            int index = line.IndexOf(';');
            if (index >= 0) line = line[..index];

            index = line.IndexOf("//", StringComparison.Ordinal);
            if (index >= 0) line = line[..index];

            return line;
        }

        private static List<string> Tokenize(string line)
        {
            List<string> result = [];
            if (string.IsNullOrWhiteSpace(line)) return result;

            StringBuilder builder = new();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    quoted = !quoted;
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

                if (c == '\\' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    builder.Append('"');
                    i++;
                    continue;
                }

                builder.Append(c);
            }

            if (builder.Length > 0)
                result.Add(builder.ToString());

            return result;
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

            foreach (string token in SplitActionOptionTokens(value))
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

        private static IEnumerable<string> SplitActionOptionTokens(string options)
        {
            if (string.IsNullOrWhiteSpace(options)) yield break;

            foreach (string token in options.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (!string.IsNullOrWhiteSpace(token))
                    yield return token;
        }

        private static string GetActionOptionValue(string options, string key)
        {
            if (string.IsNullOrWhiteSpace(options)) return null;

            foreach (string token in SplitActionOptionTokens(options))
            {
                string[] parts = token.Split('=', 2, StringSplitOptions.TrimEntries);
                if (parts.Length == 2 && parts[0].Equals(key, StringComparison.OrdinalIgnoreCase))
                    return parts[1];
            }

            return null;
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
            return values.TryGetValue(key, out string value) && Enum.TryParse(value, true, out T result) ? result : defaultValue;
        }

        private static T GetEnum<T>(Dictionary<string, string> values, string key, T defaultValue, CustomSystemTextReport report, string source) where T : struct
        {
            if (!values.TryGetValue(key, out string value)) return defaultValue;
            if (Enum.TryParse(value, true, out T result)) return result;

            report.Failures.Add($"{source}: `{key}` 枚举值无效: {value}，类型 {typeof(T).Name}。");
            return defaultValue;
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

        private static IEnumerable<string> EnumerateCustomTextFiles(string directory, bool includeTemplates)
        {
            foreach (string file in Directory.EnumerateFiles(directory, "*.txt", SearchOption.TopDirectoryOnly))
                yield return file;

            if (!includeTemplates) yield break;

            string templatesDirectory = Path.Combine(directory, "Templates");
            if (!Directory.Exists(templatesDirectory)) yield break;

            foreach (string file in Directory.EnumerateFiles(templatesDirectory, "*.txt", SearchOption.AllDirectories))
                yield return file;
        }

        private static bool IsTemplateTextFile(string directory, string file)
        {
            string templatesDirectory = Path.GetFullPath(Path.Combine(directory, "Templates"));
            string fullPath = Path.GetFullPath(file);

            return fullPath.StartsWith(templatesDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
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

            CustomSystemTextLoadResult validation = loader.BuildForDatabaseImport(directory, CustomSystemImportMode.ValidateOnly);
            CustomSystemTextReport report = validation.Report;
            report.Mode = mode;

            if (mode == CustomSystemImportMode.ValidateOnly || report.Failures.Count > 0)
                return report;

            loader = new CustomSystemTextLoader(_session);
            CustomSystemTextLoadResult load = loader.BuildForDatabaseImport(directory, mode);
            report = load.Report;
            report.Mode = mode;

            if (report.Failures.Count > 0)
                return report;

            report.ResourcesImported = load.Resources.Count;
            report.MonsterVisualsImported = load.Visuals.Count;
            report.MonsterBehaviorsImported = load.Behaviors.Count;
            report.MonsterStatsImported = load.Report.MonsterStatsLoaded;
            report.MonsterDropEntriesImported = load.Report.MonsterDropEntriesImported;
            report.MonsterDropGroupsImported = load.Report.MonsterDropGroupsImported;
            report.SkillsImported = load.Skills.Count;
            return report;
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

            Directory.CreateDirectory(directory);
            Directory.CreateDirectory(Path.Combine(directory, "Monsters"));
            Directory.CreateDirectory(Path.Combine(directory, "Skills"));

            CustomSystemTextReport report = new();
            report.Mode = CustomSystemImportMode.Export;
            ExportResources(Path.Combine(directory, "Resources.txt"), overwrite, report);
            ExportMonsters(Path.Combine(directory, "Monsters"), overwrite, report);
            ExportSkills(Path.Combine(directory, "Skills"), overwrite, report);
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
                report.ResourcesExported++;
            }

            File.WriteAllLines(path, lines, new UTF8Encoding(false));
        }

        private void ExportMonsters(string directory, bool overwrite, CustomSystemTextReport report)
        {
            Dictionary<int, CustomMonsterVisualInfo> visuals = _session.GetCollection<CustomMonsterVisualInfo>().Binding
                .Where(x => x?.Monster != null)
                .GroupBy(x => x.Monster.Index)
                .ToDictionary(x => x.Key, x => x.First());

            Dictionary<int, CustomMonsterBehaviorInfo> behaviors = _session.GetCollection<CustomMonsterBehaviorInfo>().Binding
                .Where(x => x?.Monster != null)
                .GroupBy(x => x.Monster.Index)
                .ToDictionary(x => x.Key, x => x.First());

            foreach (MonsterInfo monster in visuals.Values.Select(x => x.Monster)
                         .Concat(behaviors.Values.Select(x => x.Monster))
                         .Where(x => x != null)
                         .GroupBy(x => x.Index)
                         .Select(x => x.First())
                         .OrderBy(x => x.MonsterName))
            {
                string path = Path.Combine(directory, SafeFileName(monster.MonsterName) + ".txt");
                if (File.Exists(path) && !overwrite)
                {
                    report.Failures.Add($"已存在怪物文本，未覆盖: {path}");
                    continue;
                }

                visuals.TryGetValue(monster.Index, out CustomMonsterVisualInfo visual);
                behaviors.TryGetValue(monster.Index, out CustomMonsterBehaviorInfo behavior);

                Mir2DropExportReport dropReport = new();
                List<string> dropLines = new Mir2DropExporter(_session).BuildMonsterLines(monster, dropReport);

                File.WriteAllText(path, BuildMonsterText(monster, visual, behavior, dropLines), new UTF8Encoding(false));
                report.MonstersExported++;
                if (visual != null)
                    report.MonsterVisualsExported++;
                if (behavior != null)
                    report.MonsterBehaviorsExported++;
                report.MonsterStatsExported += monster.MonsterInfoStats?.Count(x => x != null && x.Stat != Stat.None && x.Amount != 0) ?? 0;
                report.MonsterDropEntriesExported += dropReport.DropLines + dropReport.GroupItemLines;
                report.MonsterDropGroupsExported += dropReport.GroupLines;
            }
        }

        private static string BuildMonsterText(MonsterInfo monster, CustomMonsterVisualInfo visual, CustomMonsterBehaviorInfo behavior, List<string> dropLines)
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

                foreach (CustomMonsterAnimationInfo animation in visual.Animations.OrderBy(x => x.Priority).ThenBy(x => x.Action).ThenBy(x => x.Variant))
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

                foreach (CustomMonsterSkillInfo skill in behavior.Skills.OrderBy(x => x.Index))
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

            return sb.ToString();
        }

        private void ExportSkills(string directory, bool overwrite, CustomSystemTextReport report)
        {
            foreach (CustomSkillInfo skill in _session.GetCollection<CustomSkillInfo>().Binding.Where(x => x?.Magic != null).OrderBy(x => x.Magic.Name))
            {
                string path = Path.Combine(directory, SafeFileName(skill.Magic.Name) + ".txt");
                if (File.Exists(path) && !overwrite)
                {
                    report.Failures.Add($"已存在技能文本，未覆盖: {path}");
                    continue;
                }

                File.WriteAllText(path, BuildSkillText(skill), new UTF8Encoding(false));
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

            foreach (CustomSkillEffectInfo effect in skill.Effects.OrderBy(x => x.Stage).ThenBy(x => x.Index))
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

        private static string SafeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "Unnamed";
            foreach (char c in Path.GetInvalidFileNameChars())
                value = value.Replace(c.ToString(), string.Empty);
            return string.IsNullOrWhiteSpace(value) ? "Unnamed" : value.Trim();
        }

        private static string Quote(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "\"\"";
            return value.Any(char.IsWhiteSpace) || value.Contains('"') ? $"\"{value.Replace("\"", "\\\"")}\"" : value;
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

        public string ToDisplayText()
        {
            StringBuilder sb = new();
            sb.AppendLine($"模式: {Mode}");
            sb.AppendLine($"资源: 加载 {ResourcesLoaded}, 导入 {ResourcesImported}, 导出 {ResourcesExported}");
            sb.AppendLine($"怪物文件: 加载 {MonsterFilesLoaded}, 导出 {MonstersExported}");
            sb.AppendLine($"怪物属性: 加载 {MonsterStatsLoaded}, 导入 {MonsterStatsImported}, 导出 {MonsterStatsExported}");
            sb.AppendLine($"怪物掉落: 加载物品 {MonsterDropEntriesLoaded}, 加载组 {MonsterDropGroupsLoaded}, 导入物品 {MonsterDropEntriesImported}, 导入组 {MonsterDropGroupsImported}, 导出物品 {MonsterDropEntriesExported}, 导出组 {MonsterDropGroupsExported}");
            sb.AppendLine($"掉落特殊行: 金币 {MonsterDropGoldLines}, 任务专属跳过 {MonsterDropQuestOnlyLines}, 缺失物品 {MissingItems.Distinct(StringComparer.OrdinalIgnoreCase).Count()}");
            sb.AppendLine($"怪物视觉: 加载 {MonsterVisualsLoaded}, 导入 {MonsterVisualsImported}, 导出 {MonsterVisualsExported}");
            sb.AppendLine($"怪物行为: 加载 {MonsterBehaviorsLoaded}, 导入 {MonsterBehaviorsImported}, 导出 {MonsterBehaviorsExported}");
            sb.AppendLine($"技能: 加载 {SkillFilesLoaded}, 导入 {SkillsImported}, 导出 {SkillsExported}");
            if (TemplateMonsterFilesLoaded > 0 || TemplateSkillFilesLoaded > 0)
                sb.AppendLine($"模板校验: 怪物 {TemplateMonsterFilesLoaded}, 技能 {TemplateSkillFilesLoaded}");
            sb.AppendLine($"缺失引用: 资源 {MissingResources.Distinct(StringComparer.OrdinalIgnoreCase).Count()}, 怪物 {MissingMonsters.Distinct(StringComparer.OrdinalIgnoreCase).Count()}, 技能 {MissingMagics.Distinct(StringComparer.OrdinalIgnoreCase).Count()}, 物品 {MissingItems.Distinct(StringComparer.OrdinalIgnoreCase).Count()}");
            sb.AppendLine($"跳过: 怪物 {MonstersSkipped}, 技能 {SkillsSkipped}");
            sb.AppendLine($"错误: {Failures.Count}");

            AppendSamples(sb, "缺失资源", MissingResources);
            AppendSamples(sb, "缺失怪物", MissingMonsters);
            AppendSamples(sb, "缺失技能", MissingMagics);
            AppendSamples(sb, "缺失物品", MissingItems);

            if (Failures.Count > 0)
                AppendSamples(sb, "错误示例", Failures);

            return sb.ToString();
        }

        private static void AppendSamples(StringBuilder sb, string title, IEnumerable<string> values)
        {
            List<string> samples = values?
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(30)
                .ToList() ?? [];

            if (samples.Count == 0) return;

            sb.AppendLine();
            sb.AppendLine($"{title}:");
            sb.AppendLine(string.Join(Environment.NewLine, samples));
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
