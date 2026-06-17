using MirDB;
using System.Text.Json.Serialization;

namespace Library.SystemModels
{
    public enum CustomResourceScope
    {
        Both,
        Monster,
        Skill
    }

    public enum CustomMonsterAnimationType
    {
        Standing,
        Walking,
        Attack,
        RangeAttack,
        Magic,
        Struck,
        Die,
        Dead,
        Show,
        Hide,
        Idle1,
        Idle2,
        Idle3,
        Idle4,
        Idle5,
        Idle6,
        Idle7,
        Idle8
    }

    public enum CustomMonsterEffectTarget
    {
        Self,
        CurrentTarget,
        AttackTargets,
        CurrentLocation,
        MagicLocations
    }

    public enum CustomMonsterBehaviorTemplate
    {
        None,
        Melee,
        Ranged,
        Caster,
        Summoner,
        Boss,
        Guard,
        PickupCompanion,
        RuleSet
    }

    public enum CustomAiTriggerType
    {
        OnTargetTick,
        OnTick,
        OnAttack,
        OnSpawn,
        OnStruck,
        OnDeath,
        OnMove
    }

    public enum CustomAiConditionType
    {
        Always,
        TargetInAttackRange,
        TargetOutOfAttackRange,
        SameCellAsTarget,
        CanAttack,
        CanMove,
        HealthBelow,
        HealthAbove,
        TargetHealthBelow,
        TargetHealthAbove,
        TargetCountAtLeast,
        TargetExists,
        TargetMissing,
        TargetDistanceBelow,
        TargetDistanceAbove,
        TargetInLine,
        TargetOutOfLine,
        TargetIsPlayer,
        TargetIsHero,
        TargetIsPlayerOrHero,
        TargetIsMonster,
        TargetIsPetMonster,
        TargetIsWildMonster,
        TargetIsBossMonster,
        TargetClassWarrior,
        TargetClassWizard,
        TargetClassTaoist,
        TargetClassAssassin,
        MinionCountBelow,
        MinionCountAbove,
        SelfHasBuff,
        SelfMissingBuff,
        TargetHasBuff,
        TargetMissingBuff,
        CounterBelow,
        CounterAbove,
        CounterEquals,
        Visible,
        Hidden
    }

    public enum CustomAiConditionJoin
    {
        And,
        Or
    }

    public enum CustomAiTargetSelectorType
    {
        CurrentTarget,
        Self,
        CurrentTargetLocation,
        SelfRadius,
        AllyRadius,
        TargetRadius,
        Line,
        AllInView,
        RandomInView,
        NearestInView,
        FarthestInView,
        LowestHealthInView,
        HighestHealthInView,
        RandomAllyInView,
        NearestAllyInView,
        LowestHealthAllyInView,
        ExperienceOwner,
        ExperienceOwnerGroup,
        Summoner,
        SummonerGroup
    }

    public enum CustomAiActionType
    {
        None,
        FaceTarget,
        MoveToTarget,
        RandomStepWhenSameCell,
        MeleeAttack,
        RangeAttack,
        LineAttack,
        AttackMagic,
        AttackAoE,
        LineAoE,
        MassAreaDamage,
        SpawnMinions,
        TeleportSelf,
        WeightedChoice,
        Sequence,
        SearchTarget,
        ClearTarget,
        SetCounter,
        ChangeCounter,
        Show,
        Hide,
        ApplyPoison,
        AddBuff,
        RemoveBuff,
        TeleportTarget,
        PullTarget,
        PushTarget,
        CastSkill,
        ReviveSelf,
        WakeNearby,
        MoveAwayFromTarget,
        TeleportNearTarget,
        DelayActionTime,
        DelayMoveTime,
        DelayCellTime,
        ScheduleAction,
        KillSelf,
        MapEffect,
        ObjectEffect,
        StoreLocation,
        ReturnToStoredLocation,
        TeleportTargetsRandom,
        PurifyTargets,
        RecallMonstersByFlag,
        SwapRandomMinionLocations,
        KillMinions,
        HazardRoam,
        BroadcastMessage,
        TeleportTargetsToRegion,
        DespawnSelf
    }

    public enum CustomAiPowerSource
    {
        DC,
        MC,
        SC,
        Fixed
    }

    public enum CustomSkillPowerSource
    {
        Auto,
        DC,
        MC,
        SC,
        Fixed
    }

    public enum CustomSkillTemplate
    {
        DirectDamage,
        ProjectileDamage,
        ChainDamage,
        AreaDamage,
        PersistentAreaDamage,
        Heal,
        Buff,
        Debuff,
        Summon,
        DamageProxy,
        Move,
        Control
    }

    public enum CustomSkillShape
    {
        Circle,
        Line,
        Cone,
        Rectangle
    }

    public enum CustomSkillTargetRule
    {
        Enemy,
        Ally,
        Self,
        Ground,
        EnemyOrGround,
        AllyOrSelf
    }

    public enum CustomSkillEffectStage
    {
        Cast,
        Projectile,
        Hit,
        Area,
        Buff,
        Tick
    }

    public sealed class CustomResourceLibraryInfo : DBObject
    {
        [IsIdentity]
        public string Key
        {
            get { return _Key; }
            set
            {
                if (_Key == value) return;

                var oldValue = _Key;
                _Key = value;

                OnChanged(oldValue, value, "Key");
            }
        }
        private string _Key;

        public string DisplayName
        {
            get { return _DisplayName; }
            set
            {
                if (_DisplayName == value) return;

                var oldValue = _DisplayName;
                _DisplayName = value;

                OnChanged(oldValue, value, "DisplayName");
            }
        }
        private string _DisplayName;

        public string RelativePath
        {
            get { return _RelativePath; }
            set
            {
                if (_RelativePath == value) return;

                var oldValue = _RelativePath;
                _RelativePath = value;

                OnChanged(oldValue, value, "RelativePath");
            }
        }
        private string _RelativePath;

        public global::Library.LibraryFile LibraryFile
        {
            get { return _LibraryFile; }
            set
            {
                if (_LibraryFile == value) return;

                var oldValue = _LibraryFile;
                _LibraryFile = value;

                OnChanged(oldValue, value, "LibraryFile");
            }
        }
        private global::Library.LibraryFile _LibraryFile;

        public string Hash
        {
            get { return _Hash; }
            set
            {
                if (_Hash == value) return;

                var oldValue = _Hash;
                _Hash = value;

                OnChanged(oldValue, value, "Hash");
            }
        }
        private string _Hash;

        public string Version
        {
            get { return _Version; }
            set
            {
                if (_Version == value) return;

                var oldValue = _Version;
                _Version = value;

                OnChanged(oldValue, value, "Version");
            }
        }
        private string _Version;

        public bool Enabled
        {
            get { return _Enabled; }
            set
            {
                if (_Enabled == value) return;

                var oldValue = _Enabled;
                _Enabled = value;

                OnChanged(oldValue, value, "Enabled");
            }
        }
        private bool _Enabled;

        public CustomResourceScope Scope
        {
            get { return _Scope; }
            set
            {
                if (_Scope == value) return;

                var oldValue = _Scope;
                _Scope = value;

                OnChanged(oldValue, value, "Scope");
            }
        }
        private CustomResourceScope _Scope;

        protected internal override void OnCreated()
        {
            base.OnCreated();

            Enabled = true;
            Scope = CustomResourceScope.Both;
        }

        public override string ToString()
        {
            return string.IsNullOrWhiteSpace(DisplayName) ? Key : DisplayName;
        }
    }

    public sealed class CustomMonsterVisualInfo : DBObject
    {
        [IsIdentity]
        public MonsterInfo Monster
        {
            get { return _Monster; }
            set
            {
                if (_Monster == value) return;

                var oldValue = _Monster;
                _Monster = value;

                OnChanged(oldValue, value, "Monster");
            }
        }
        private MonsterInfo _Monster;

        public CustomResourceLibraryInfo DefaultResource
        {
            get { return _DefaultResource; }
            set
            {
                if (_DefaultResource == value) return;

                var oldValue = _DefaultResource;
                _DefaultResource = value;

                OnChanged(oldValue, value, "DefaultResource");
            }
        }
        private CustomResourceLibraryInfo _DefaultResource;

        public float Scale
        {
            get { return _Scale; }
            set
            {
                if (_Scale == value) return;

                var oldValue = _Scale;
                _Scale = value;

                OnChanged(oldValue, value, "Scale");
            }
        }
        private float _Scale;

        public bool DrawShadow
        {
            get { return _DrawShadow; }
            set
            {
                if (_DrawShadow == value) return;

                var oldValue = _DrawShadow;
                _DrawShadow = value;

                OnChanged(oldValue, value, "DrawShadow");
            }
        }
        private bool _DrawShadow;

        public bool Blend
        {
            get { return _Blend; }
            set
            {
                if (_Blend == value) return;

                var oldValue = _Blend;
                _Blend = value;

                OnChanged(oldValue, value, "Blend");
            }
        }
        private bool _Blend;

        public bool KeepCorpse
        {
            get { return _KeepCorpse; }
            set
            {
                if (_KeepCorpse == value) return;

                var oldValue = _KeepCorpse;
                _KeepCorpse = value;

                OnChanged(oldValue, value, "KeepCorpse");
            }
        }
        private bool _KeepCorpse;

        public int DefaultFrameDelay
        {
            get { return _DefaultFrameDelay; }
            set
            {
                if (_DefaultFrameDelay == value) return;

                var oldValue = _DefaultFrameDelay;
                _DefaultFrameDelay = value;

                OnChanged(oldValue, value, "DefaultFrameDelay");
            }
        }
        private int _DefaultFrameDelay;

        public int FrameBase
        {
            get { return _FrameBase; }
            set
            {
                if (_FrameBase == value) return;

                var oldValue = _FrameBase;
                _FrameBase = value;

                OnChanged(oldValue, value, "FrameBase");
            }
        }
        private int _FrameBase;

        public SoundIndex AttackSound
        {
            get { return _AttackSound; }
            set
            {
                if (_AttackSound == value) return;

                var oldValue = _AttackSound;
                _AttackSound = value;

                OnChanged(oldValue, value, "AttackSound");
            }
        }
        private SoundIndex _AttackSound;

        public SoundIndex RangeAttackSound
        {
            get { return _RangeAttackSound; }
            set
            {
                if (_RangeAttackSound == value) return;

                var oldValue = _RangeAttackSound;
                _RangeAttackSound = value;

                OnChanged(oldValue, value, "RangeAttackSound");
            }
        }
        private SoundIndex _RangeAttackSound;

        public SoundIndex MagicSound
        {
            get { return _MagicSound; }
            set
            {
                if (_MagicSound == value) return;

                var oldValue = _MagicSound;
                _MagicSound = value;

                OnChanged(oldValue, value, "MagicSound");
            }
        }
        private SoundIndex _MagicSound;

        public SoundIndex StruckSound
        {
            get { return _StruckSound; }
            set
            {
                if (_StruckSound == value) return;

                var oldValue = _StruckSound;
                _StruckSound = value;

                OnChanged(oldValue, value, "StruckSound");
            }
        }
        private SoundIndex _StruckSound;

        public SoundIndex DieSound
        {
            get { return _DieSound; }
            set
            {
                if (_DieSound == value) return;

                var oldValue = _DieSound;
                _DieSound = value;

                OnChanged(oldValue, value, "DieSound");
            }
        }
        private SoundIndex _DieSound;

        public SoundIndex ShowSound
        {
            get { return _ShowSound; }
            set
            {
                if (_ShowSound == value) return;

                var oldValue = _ShowSound;
                _ShowSound = value;

                OnChanged(oldValue, value, "ShowSound");
            }
        }
        private SoundIndex _ShowSound;

        public SoundIndex HideSound
        {
            get { return _HideSound; }
            set
            {
                if (_HideSound == value) return;

                var oldValue = _HideSound;
                _HideSound = value;

                OnChanged(oldValue, value, "HideSound");
            }
        }
        private SoundIndex _HideSound;

        [Association("Animations", true)]
        public DBBindingList<CustomMonsterAnimationInfo> Animations { get; set; }

        protected internal override void OnCreated()
        {
            base.OnCreated();

            Scale = 1F;
            DrawShadow = true;
            KeepCorpse = true;
            DefaultFrameDelay = 100;
        }

        public override string ToString()
        {
            return Monster?.MonsterName ?? $"CustomMonsterVisual #{Index}";
        }
    }

    public sealed class CustomMonsterAnimationInfo : DBObject
    {
        [Association("Animations")]
        public CustomMonsterVisualInfo Visual
        {
            get { return _Visual; }
            set
            {
                if (_Visual == value) return;

                var oldValue = _Visual;
                _Visual = value;

                OnChanged(oldValue, value, "Visual");
            }
        }
        private CustomMonsterVisualInfo _Visual;

        public CustomResourceLibraryInfo Resource
        {
            get { return _Resource; }
            set
            {
                if (_Resource == value) return;

                var oldValue = _Resource;
                _Resource = value;

                OnChanged(oldValue, value, "Resource");
            }
        }
        private CustomResourceLibraryInfo _Resource;

        public CustomMonsterAnimationType Action
        {
            get { return _Action; }
            set
            {
                if (_Action == value) return;

                var oldValue = _Action;
                _Action = value;

                OnChanged(oldValue, value, "Action");
            }
        }
        private CustomMonsterAnimationType _Action;

        public int Variant
        {
            get { return _Variant; }
            set
            {
                if (_Variant == value) return;

                var oldValue = _Variant;
                _Variant = value;

                OnChanged(oldValue, value, "Variant");
            }
        }
        private int _Variant;

        public int StartIndex
        {
            get { return _StartIndex; }
            set
            {
                if (_StartIndex == value) return;

                var oldValue = _StartIndex;
                _StartIndex = value;

                OnChanged(oldValue, value, "StartIndex");
            }
        }
        private int _StartIndex;

        public int EndIndex
        {
            get { return _EndIndex; }
            set
            {
                if (_EndIndex == value) return;

                var oldValue = _EndIndex;
                _EndIndex = value;

                OnChanged(oldValue, value, "EndIndex");
            }
        }
        private int _EndIndex;

        public int FrameCount
        {
            get { return _FrameCount; }
            set
            {
                if (_FrameCount == value) return;

                var oldValue = _FrameCount;
                _FrameCount = value;

                OnChanged(oldValue, value, "FrameCount");
            }
        }
        private int _FrameCount;

        public int DirectionCount
        {
            get { return _DirectionCount; }
            set
            {
                if (_DirectionCount == value) return;

                var oldValue = _DirectionCount;
                _DirectionCount = value;

                OnChanged(oldValue, value, "DirectionCount");
            }
        }
        private int _DirectionCount;

        public int DirectionOffset
        {
            get { return _DirectionOffset; }
            set
            {
                if (_DirectionOffset == value) return;

                var oldValue = _DirectionOffset;
                _DirectionOffset = value;

                OnChanged(oldValue, value, "DirectionOffset");
            }
        }
        private int _DirectionOffset;

        public int FrameDelay
        {
            get { return _FrameDelay; }
            set
            {
                if (_FrameDelay == value) return;

                var oldValue = _FrameDelay;
                _FrameDelay = value;

                OnChanged(oldValue, value, "FrameDelay");
            }
        }
        private int _FrameDelay;

        public bool Loop
        {
            get { return _Loop; }
            set
            {
                if (_Loop == value) return;

                var oldValue = _Loop;
                _Loop = value;

                OnChanged(oldValue, value, "Loop");
            }
        }
        private bool _Loop;

        public bool Reversed
        {
            get { return _Reversed; }
            set
            {
                if (_Reversed == value) return;

                var oldValue = _Reversed;
                _Reversed = value;

                OnChanged(oldValue, value, "Reversed");
            }
        }
        private bool _Reversed;

        public int OffsetX
        {
            get { return _OffsetX; }
            set
            {
                if (_OffsetX == value) return;

                var oldValue = _OffsetX;
                _OffsetX = value;

                OnChanged(oldValue, value, "OffsetX");
            }
        }
        private int _OffsetX;

        public int OffsetY
        {
            get { return _OffsetY; }
            set
            {
                if (_OffsetY == value) return;

                var oldValue = _OffsetY;
                _OffsetY = value;

                OnChanged(oldValue, value, "OffsetY");
            }
        }
        private int _OffsetY;

        public int Priority
        {
            get { return _Priority; }
            set
            {
                if (_Priority == value) return;

                var oldValue = _Priority;
                _Priority = value;

                OnChanged(oldValue, value, "Priority");
            }
        }
        private int _Priority;

        public SoundIndex Sound
        {
            get { return _Sound; }
            set
            {
                if (_Sound == value) return;

                var oldValue = _Sound;
                _Sound = value;

                OnChanged(oldValue, value, "Sound");
            }
        }
        private SoundIndex _Sound;

        public CustomResourceLibraryInfo EffectResource
        {
            get { return _EffectResource; }
            set
            {
                if (_EffectResource == value) return;

                var oldValue = _EffectResource;
                _EffectResource = value;

                OnChanged(oldValue, value, "EffectResource");
            }
        }
        private CustomResourceLibraryInfo _EffectResource;

        public CustomMonsterEffectTarget EffectTarget
        {
            get { return _EffectTarget; }
            set
            {
                if (_EffectTarget == value) return;

                var oldValue = _EffectTarget;
                _EffectTarget = value;

                OnChanged(oldValue, value, "EffectTarget");
            }
        }
        private CustomMonsterEffectTarget _EffectTarget;

        public int EffectStartIndex
        {
            get { return _EffectStartIndex; }
            set
            {
                if (_EffectStartIndex == value) return;

                var oldValue = _EffectStartIndex;
                _EffectStartIndex = value;

                OnChanged(oldValue, value, "EffectStartIndex");
            }
        }
        private int _EffectStartIndex;

        public int EffectEndIndex
        {
            get { return _EffectEndIndex; }
            set
            {
                if (_EffectEndIndex == value) return;

                var oldValue = _EffectEndIndex;
                _EffectEndIndex = value;

                OnChanged(oldValue, value, "EffectEndIndex");
            }
        }
        private int _EffectEndIndex;

        public int EffectFrameCount
        {
            get { return _EffectFrameCount; }
            set
            {
                if (_EffectFrameCount == value) return;

                var oldValue = _EffectFrameCount;
                _EffectFrameCount = value;

                OnChanged(oldValue, value, "EffectFrameCount");
            }
        }
        private int _EffectFrameCount;

        public int EffectFrameDelay
        {
            get { return _EffectFrameDelay; }
            set
            {
                if (_EffectFrameDelay == value) return;

                var oldValue = _EffectFrameDelay;
                _EffectFrameDelay = value;

                OnChanged(oldValue, value, "EffectFrameDelay");
            }
        }
        private int _EffectFrameDelay;

        public int EffectOffsetX
        {
            get { return _EffectOffsetX; }
            set
            {
                if (_EffectOffsetX == value) return;

                var oldValue = _EffectOffsetX;
                _EffectOffsetX = value;

                OnChanged(oldValue, value, "EffectOffsetX");
            }
        }
        private int _EffectOffsetX;

        public int EffectOffsetY
        {
            get { return _EffectOffsetY; }
            set
            {
                if (_EffectOffsetY == value) return;

                var oldValue = _EffectOffsetY;
                _EffectOffsetY = value;

                OnChanged(oldValue, value, "EffectOffsetY");
            }
        }
        private int _EffectOffsetY;

        public int EffectStartLight
        {
            get { return _EffectStartLight; }
            set
            {
                if (_EffectStartLight == value) return;

                var oldValue = _EffectStartLight;
                _EffectStartLight = value;

                OnChanged(oldValue, value, "EffectStartLight");
            }
        }
        private int _EffectStartLight;

        public int EffectEndLight
        {
            get { return _EffectEndLight; }
            set
            {
                if (_EffectEndLight == value) return;

                var oldValue = _EffectEndLight;
                _EffectEndLight = value;

                OnChanged(oldValue, value, "EffectEndLight");
            }
        }
        private int _EffectEndLight;

        public int EffectStartDelay
        {
            get { return _EffectStartDelay; }
            set
            {
                if (_EffectStartDelay == value) return;

                var oldValue = _EffectStartDelay;
                _EffectStartDelay = value;

                OnChanged(oldValue, value, "EffectStartDelay");
            }
        }
        private int _EffectStartDelay;

        public int EffectDuration
        {
            get { return _EffectDuration; }
            set
            {
                if (_EffectDuration == value) return;

                var oldValue = _EffectDuration;
                _EffectDuration = value;

                OnChanged(oldValue, value, "EffectDuration");
            }
        }
        private int _EffectDuration;

        public int EffectTriggerFrame
        {
            get { return _EffectTriggerFrame; }
            set
            {
                if (_EffectTriggerFrame == value) return;

                var oldValue = _EffectTriggerFrame;
                _EffectTriggerFrame = value;

                OnChanged(oldValue, value, "EffectTriggerFrame");
            }
        }
        private int _EffectTriggerFrame;

        public bool EffectBlend
        {
            get { return _EffectBlend; }
            set
            {
                if (_EffectBlend == value) return;

                var oldValue = _EffectBlend;
                _EffectBlend = value;

                OnChanged(oldValue, value, "EffectBlend");
            }
        }
        private bool _EffectBlend;

        public bool EffectLoop
        {
            get { return _EffectLoop; }
            set
            {
                if (_EffectLoop == value) return;

                var oldValue = _EffectLoop;
                _EffectLoop = value;

                OnChanged(oldValue, value, "EffectLoop");
            }
        }
        private bool _EffectLoop;

        protected internal override void OnCreated()
        {
            base.OnCreated();

            DirectionCount = 8;
            DirectionOffset = 10;
            FrameDelay = 100;
            EffectFrameDelay = 100;
            EffectTriggerFrame = -1;
        }
    }

    public sealed class CustomMonsterBehaviorInfo : DBObject
    {
        [IsIdentity]
        public MonsterInfo Monster
        {
            get { return _Monster; }
            set
            {
                if (_Monster == value) return;

                var oldValue = _Monster;
                _Monster = value;

                OnChanged(oldValue, value, "Monster");
            }
        }
        private MonsterInfo _Monster;

        public CustomMonsterBehaviorTemplate Template
        {
            get { return _Template; }
            set
            {
                if (_Template == value) return;

                var oldValue = _Template;
                _Template = value;

                OnChanged(oldValue, value, "Template");
            }
        }
        private CustomMonsterBehaviorTemplate _Template;

        public bool CanBeKilled
        {
            get { return _CanBeKilled; }
            set
            {
                if (_CanBeKilled == value) return;

                var oldValue = _CanBeKilled;
                _CanBeKilled = value;

                OnChanged(oldValue, value, "CanBeKilled");
            }
        }
        private bool _CanBeKilled;

        public bool CanAttackPlayers
        {
            get { return _CanAttackPlayers; }
            set
            {
                if (_CanAttackPlayers == value) return;

                var oldValue = _CanAttackPlayers;
                _CanAttackPlayers = value;

                OnChanged(oldValue, value, "CanAttackPlayers");
            }
        }
        private bool _CanAttackPlayers;

        public bool CanBeTamed
        {
            get { return _CanBeTamed; }
            set
            {
                if (_CanBeTamed == value) return;

                var oldValue = _CanBeTamed;
                _CanBeTamed = value;

                OnChanged(oldValue, value, "CanBeTamed");
            }
        }
        private bool _CanBeTamed;

        public bool IsPickupCompanion
        {
            get { return _IsPickupCompanion; }
            set
            {
                if (_IsPickupCompanion == value) return;

                var oldValue = _IsPickupCompanion;
                _IsPickupCompanion = value;

                OnChanged(oldValue, value, "IsPickupCompanion");
            }
        }
        private bool _IsPickupCompanion;

        public bool Passive
        {
            get { return _Passive; }
            set
            {
                if (_Passive == value) return;

                var oldValue = _Passive;
                _Passive = value;

                OnChanged(oldValue, value, "Passive");
            }
        }
        private bool _Passive;

        public bool AlwaysActive
        {
            get { return _AlwaysActive; }
            set
            {
                if (_AlwaysActive == value) return;

                var oldValue = _AlwaysActive;
                _AlwaysActive = value;

                OnChanged(oldValue, value, "AlwaysActive");
            }
        }
        private bool _AlwaysActive;

        public bool DamageProxy
        {
            get { return _DamageProxy; }
            set
            {
                if (_DamageProxy == value) return;

                var oldValue = _DamageProxy;
                _DamageProxy = value;

                OnChanged(oldValue, value, "DamageProxy");
            }
        }
        private bool _DamageProxy;

        public int DamageProxyRevealDelay
        {
            get { return _DamageProxyRevealDelay; }
            set
            {
                if (_DamageProxyRevealDelay == value) return;

                var oldValue = _DamageProxyRevealDelay;
                _DamageProxyRevealDelay = value;

                OnChanged(oldValue, value, "DamageProxyRevealDelay");
            }
        }
        private int _DamageProxyRevealDelay;

        public int DamageProxyRange
        {
            get { return _DamageProxyRange; }
            set
            {
                if (_DamageProxyRange == value) return;

                var oldValue = _DamageProxyRange;
                _DamageProxyRange = value;

                OnChanged(oldValue, value, "DamageProxyRange");
            }
        }
        private int _DamageProxyRange;

        public Effect DamageProxySpawnEffect
        {
            get { return _DamageProxySpawnEffect; }
            set
            {
                if (_DamageProxySpawnEffect == value) return;

                var oldValue = _DamageProxySpawnEffect;
                _DamageProxySpawnEffect = value;

                OnChanged(oldValue, value, "DamageProxySpawnEffect");
            }
        }
        private Effect _DamageProxySpawnEffect;

        public string DamageProxyNameFormat
        {
            get { return _DamageProxyNameFormat; }
            set
            {
                if (_DamageProxyNameFormat == value) return;

                var oldValue = _DamageProxyNameFormat;
                _DamageProxyNameFormat = value;

                OnChanged(oldValue, value, "DamageProxyNameFormat");
            }
        }
        private string _DamageProxyNameFormat;

        public bool NeedHarvest
        {
            get { return _NeedHarvest; }
            set
            {
                if (_NeedHarvest == value) return;

                var oldValue = _NeedHarvest;
                _NeedHarvest = value;

                OnChanged(oldValue, value, "NeedHarvest");
            }
        }
        private bool _NeedHarvest;

        public int HarvestCount
        {
            get { return _HarvestCount; }
            set
            {
                if (_HarvestCount == value) return;

                var oldValue = _HarvestCount;
                _HarvestCount = value;

                OnChanged(oldValue, value, "HarvestCount");
            }
        }
        private int _HarvestCount;

        public bool AvoidFireWall
        {
            get { return _AvoidFireWall; }
            set
            {
                if (_AvoidFireWall == value) return;

                var oldValue = _AvoidFireWall;
                _AvoidFireWall = value;

                OnChanged(oldValue, value, "AvoidFireWall");
            }
        }
        private bool _AvoidFireWall;

        public bool IgnoreShield
        {
            get { return _IgnoreShield; }
            set
            {
                if (_IgnoreShield == value) return;

                var oldValue = _IgnoreShield;
                _IgnoreShield = value;

                OnChanged(oldValue, value, "IgnoreShield");
            }
        }
        private bool _IgnoreShield;

        public bool IgnoreArmour
        {
            get { return _IgnoreArmour; }
            set
            {
                if (_IgnoreArmour == value) return;

                var oldValue = _IgnoreArmour;
                _IgnoreArmour = value;

                OnChanged(oldValue, value, "IgnoreArmour");
            }
        }
        private bool _IgnoreArmour;

        public PoisonType PoisonType
        {
            get { return _PoisonType; }
            set
            {
                if (_PoisonType == value) return;

                var oldValue = _PoisonType;
                _PoisonType = value;

                OnChanged(oldValue, value, "PoisonType");
            }
        }
        private PoisonType _PoisonType;

        public int PoisonValue
        {
            get { return _PoisonValue; }
            set
            {
                if (_PoisonValue == value) return;

                var oldValue = _PoisonValue;
                _PoisonValue = value;

                OnChanged(oldValue, value, "PoisonValue");
            }
        }
        private int _PoisonValue;

        public int PoisonRate
        {
            get { return _PoisonRate; }
            set
            {
                if (_PoisonRate == value) return;

                var oldValue = _PoisonRate;
                _PoisonRate = value;

                OnChanged(oldValue, value, "PoisonRate");
            }
        }
        private int _PoisonRate;

        public int PoisonTicks
        {
            get { return _PoisonTicks; }
            set
            {
                if (_PoisonTicks == value) return;

                var oldValue = _PoisonTicks;
                _PoisonTicks = value;

                OnChanged(oldValue, value, "PoisonTicks");
            }
        }
        private int _PoisonTicks;

        public int PoisonFrequency
        {
            get { return _PoisonFrequency; }
            set
            {
                if (_PoisonFrequency == value) return;

                var oldValue = _PoisonFrequency;
                _PoisonFrequency = value;

                OnChanged(oldValue, value, "PoisonFrequency");
            }
        }
        private int _PoisonFrequency;

        public bool PoisonCanKill
        {
            get { return _PoisonCanKill; }
            set
            {
                if (_PoisonCanKill == value) return;

                var oldValue = _PoisonCanKill;
                _PoisonCanKill = value;

                OnChanged(oldValue, value, "PoisonCanKill");
            }
        }
        private bool _PoisonCanKill;

        public bool PoisonStackable
        {
            get { return _PoisonStackable; }
            set
            {
                if (_PoisonStackable == value) return;

                var oldValue = _PoisonStackable;
                _PoisonStackable = value;

                OnChanged(oldValue, value, "PoisonStackable");
            }
        }
        private bool _PoisonStackable;

        public bool DisableMovement
        {
            get { return _DisableMovement; }
            set
            {
                if (_DisableMovement == value) return;

                var oldValue = _DisableMovement;
                _DisableMovement = value;

                OnChanged(oldValue, value, "DisableMovement");
            }
        }
        private bool _DisableMovement;

        public bool DisableAttack
        {
            get { return _DisableAttack; }
            set
            {
                if (_DisableAttack == value) return;

                var oldValue = _DisableAttack;
                _DisableAttack = value;

                OnChanged(oldValue, value, "DisableAttack");
            }
        }
        private bool _DisableAttack;

        public bool DisableRegen
        {
            get { return _DisableRegen; }
            set
            {
                if (_DisableRegen == value) return;

                var oldValue = _DisableRegen;
                _DisableRegen = value;

                OnChanged(oldValue, value, "DisableRegen");
            }
        }
        private bool _DisableRegen;

        public bool DisableBlocking
        {
            get { return _DisableBlocking; }
            set
            {
                if (_DisableBlocking == value) return;

                var oldValue = _DisableBlocking;
                _DisableBlocking = value;

                OnChanged(oldValue, value, "DisableBlocking");
            }
        }
        private bool _DisableBlocking;

        public bool HiddenBlocks
        {
            get { return _HiddenBlocks; }
            set
            {
                if (_HiddenBlocks == value) return;

                var oldValue = _HiddenBlocks;
                _HiddenBlocks = value;

                OnChanged(oldValue, value, "HiddenBlocks");
            }
        }
        private bool _HiddenBlocks;

        public bool HiddenCanAttack
        {
            get { return _HiddenCanAttack; }
            set
            {
                if (_HiddenCanAttack == value) return;

                var oldValue = _HiddenCanAttack;
                _HiddenCanAttack = value;

                OnChanged(oldValue, value, "HiddenCanAttack");
            }
        }
        private bool _HiddenCanAttack;

        public bool HiddenCanBeAttacked
        {
            get { return _HiddenCanBeAttacked; }
            set
            {
                if (_HiddenCanBeAttacked == value) return;

                var oldValue = _HiddenCanBeAttacked;
                _HiddenCanBeAttacked = value;

                OnChanged(oldValue, value, "HiddenCanBeAttacked");
            }
        }
        private bool _HiddenCanBeAttacked;

        public MonsterFlag ProtectedByMonsterFlag
        {
            get { return _ProtectedByMonsterFlag; }
            set
            {
                if (_ProtectedByMonsterFlag == value) return;

                var oldValue = _ProtectedByMonsterFlag;
                _ProtectedByMonsterFlag = value;

                OnChanged(oldValue, value, "ProtectedByMonsterFlag");
            }
        }
        private MonsterFlag _ProtectedByMonsterFlag;

        public string ProtectedByMonsterFlags
        {
            get { return _ProtectedByMonsterFlags; }
            set
            {
                if (_ProtectedByMonsterFlags == value) return;

                var oldValue = _ProtectedByMonsterFlags;
                _ProtectedByMonsterFlags = value;

                OnChanged(oldValue, value, "ProtectedByMonsterFlags");
            }
        }
        private string _ProtectedByMonsterFlags;

        public int ProtectionRadius
        {
            get { return _ProtectionRadius; }
            set
            {
                if (_ProtectionRadius == value) return;

                var oldValue = _ProtectionRadius;
                _ProtectionRadius = value;

                OnChanged(oldValue, value, "ProtectionRadius");
            }
        }
        private int _ProtectionRadius;

        public int ProtectionCheckInterval
        {
            get { return _ProtectionCheckInterval; }
            set
            {
                if (_ProtectionCheckInterval == value) return;

                var oldValue = _ProtectionCheckInterval;
                _ProtectionCheckInterval = value;

                OnChanged(oldValue, value, "ProtectionCheckInterval");
            }
        }
        private int _ProtectionCheckInterval;

        public int AttackRange
        {
            get { return _AttackRange; }
            set
            {
                if (_AttackRange == value) return;

                var oldValue = _AttackRange;
                _AttackRange = value;

                OnChanged(oldValue, value, "AttackRange");
            }
        }
        private int _AttackRange;

        public int ChaseRange
        {
            get { return _ChaseRange; }
            set
            {
                if (_ChaseRange == value) return;

                var oldValue = _ChaseRange;
                _ChaseRange = value;

                OnChanged(oldValue, value, "ChaseRange");
            }
        }
        private int _ChaseRange;

        public int ReturnRange
        {
            get { return _ReturnRange; }
            set
            {
                if (_ReturnRange == value) return;

                var oldValue = _ReturnRange;
                _ReturnRange = value;

                OnChanged(oldValue, value, "ReturnRange");
            }
        }
        private int _ReturnRange;

        public int MaxMinions
        {
            get { return _MaxMinions; }
            set
            {
                if (_MaxMinions == value) return;

                var oldValue = _MaxMinions;
                _MaxMinions = value;

                OnChanged(oldValue, value, "MaxMinions");
            }
        }
        private int _MaxMinions;

        public int SkillChance
        {
            get { return _SkillChance; }
            set
            {
                if (_SkillChance == value) return;

                var oldValue = _SkillChance;
                _SkillChance = value;

                OnChanged(oldValue, value, "SkillChance");
            }
        }
        private int _SkillChance;

        public int RoamDelay
        {
            get { return _RoamDelay; }
            set
            {
                if (_RoamDelay == value) return;

                var oldValue = _RoamDelay;
                _RoamDelay = value;

                OnChanged(oldValue, value, "RoamDelay");
            }
        }
        private int _RoamDelay;

        public bool UseBaseFallback
        {
            get { return _UseBaseFallback; }
            set
            {
                if (_UseBaseFallback == value) return;

                var oldValue = _UseBaseFallback;
                _UseBaseFallback = value;

                OnChanged(oldValue, value, "UseBaseFallback");
            }
        }
        private bool _UseBaseFallback;

        [Association("Skills", true)]
        public DBBindingList<CustomMonsterSkillInfo> Skills { get; set; }

        [Association("Rules", true)]
        public DBBindingList<CustomMonsterAiRuleInfo> Rules { get; set; }

        [Association("Actions", true)]
        public DBBindingList<CustomMonsterAiActionInfo> Actions { get; set; }

        protected internal override void OnCreated()
        {
            base.OnCreated();

            Template = CustomMonsterBehaviorTemplate.Melee;
            CanBeKilled = true;
            CanAttackPlayers = true;
            HiddenBlocks = true;
            HiddenCanAttack = true;
            HiddenCanBeAttacked = true;
            ProtectionCheckInterval = 1000;
            AttackRange = 1;
            ChaseRange = 12;
            ReturnRange = 16;
            MaxMinions = 20;
            SkillChance = 25;
            RoamDelay = 2000;
            DamageProxyRevealDelay = 500;
            DamageProxySpawnEffect = Effect.CursedDoll;
            DamageProxyNameFormat = "{Target} Doll";
            PoisonRate = 10;
            PoisonTicks = 5;
            PoisonFrequency = 2;
            UseBaseFallback = true;
        }

        public override string ToString()
        {
            return Monster?.MonsterName ?? $"CustomMonsterBehavior #{Index}";
        }
    }

    public sealed class CustomMonsterSkillInfo : DBObject
    {
        [Association("Skills")]
        public CustomMonsterBehaviorInfo Behavior
        {
            get { return _Behavior; }
            set
            {
                if (_Behavior == value) return;

                var oldValue = _Behavior;
                _Behavior = value;

                OnChanged(oldValue, value, "Behavior");
            }
        }
        private CustomMonsterBehaviorInfo _Behavior;

        public MagicInfo Magic
        {
            get { return _Magic; }
            set
            {
                if (_Magic == value) return;

                var oldValue = _Magic;
                _Magic = value;

                OnChanged(oldValue, value, "Magic");
            }
        }
        private MagicInfo _Magic;

        public CustomAiTargetSelectorType Target
        {
            get { return _Target; }
            set
            {
                if (_Target == value) return;

                var oldValue = _Target;
                _Target = value;

                OnChanged(oldValue, value, "Target");
            }
        }
        private CustomAiTargetSelectorType _Target;

        public int AnimationVariant
        {
            get { return _AnimationVariant; }
            set
            {
                if (_AnimationVariant == value) return;

                var oldValue = _AnimationVariant;
                _AnimationVariant = value;

                OnChanged(oldValue, value, "AnimationVariant");
            }
        }
        private int _AnimationVariant;

        public int Chance
        {
            get { return _Chance; }
            set
            {
                if (_Chance == value) return;

                var oldValue = _Chance;
                _Chance = value;

                OnChanged(oldValue, value, "Chance");
            }
        }
        private int _Chance;

        public int Cooldown
        {
            get { return _Cooldown; }
            set
            {
                if (_Cooldown == value) return;

                var oldValue = _Cooldown;
                _Cooldown = value;

                OnChanged(oldValue, value, "Cooldown");
            }
        }
        private int _Cooldown;

        public int Range
        {
            get { return _Range; }
            set
            {
                if (_Range == value) return;

                var oldValue = _Range;
                _Range = value;

                OnChanged(oldValue, value, "Range");
            }
        }
        private int _Range;

        public int MinHealthPercent
        {
            get { return _MinHealthPercent; }
            set
            {
                if (_MinHealthPercent == value) return;

                var oldValue = _MinHealthPercent;
                _MinHealthPercent = value;

                OnChanged(oldValue, value, "MinHealthPercent");
            }
        }
        private int _MinHealthPercent;

        public int MaxHealthPercent
        {
            get { return _MaxHealthPercent; }
            set
            {
                if (_MaxHealthPercent == value) return;

                var oldValue = _MaxHealthPercent;
                _MaxHealthPercent = value;

                OnChanged(oldValue, value, "MaxHealthPercent");
            }
        }
        private int _MaxHealthPercent;

        protected internal override void OnCreated()
        {
            base.OnCreated();

            Chance = 25;
            Cooldown = 3000;
            Range = 8;
            Target = CustomAiTargetSelectorType.CurrentTarget;
            MaxHealthPercent = 100;
        }
    }

    public sealed class CustomMonsterAiRuleInfo : DBObject
    {
        [Association("Rules")]
        public CustomMonsterBehaviorInfo Behavior
        {
            get { return _Behavior; }
            set
            {
                if (_Behavior == value) return;

                var oldValue = _Behavior;
                _Behavior = value;

                OnChanged(oldValue, value, "Behavior");
            }
        }
        private CustomMonsterBehaviorInfo _Behavior;

        public string Name
        {
            get { return _Name; }
            set
            {
                if (_Name == value) return;

                var oldValue = _Name;
                _Name = value;

                OnChanged(oldValue, value, "Name");
            }
        }
        private string _Name;

        public CustomAiTriggerType Trigger
        {
            get { return _Trigger; }
            set
            {
                if (_Trigger == value) return;

                var oldValue = _Trigger;
                _Trigger = value;

                OnChanged(oldValue, value, "Trigger");
            }
        }
        private CustomAiTriggerType _Trigger;

        public CustomAiConditionType Condition
        {
            get { return _Condition; }
            set
            {
                if (_Condition == value) return;

                var oldValue = _Condition;
                _Condition = value;

                OnChanged(oldValue, value, "Condition");
            }
        }
        private CustomAiConditionType _Condition;

        public string ActionName
        {
            get { return _ActionName; }
            set
            {
                if (_ActionName == value) return;

                var oldValue = _ActionName;
                _ActionName = value;

                OnChanged(oldValue, value, "ActionName");
            }
        }
        private string _ActionName;

        public int Priority
        {
            get { return _Priority; }
            set
            {
                if (_Priority == value) return;

                var oldValue = _Priority;
                _Priority = value;

                OnChanged(oldValue, value, "Priority");
            }
        }
        private int _Priority;

        public int Chance
        {
            get { return _Chance; }
            set
            {
                if (_Chance == value) return;

                var oldValue = _Chance;
                _Chance = value;

                OnChanged(oldValue, value, "Chance");
            }
        }
        private int _Chance;

        public int Cooldown
        {
            get { return _Cooldown; }
            set
            {
                if (_Cooldown == value) return;

                var oldValue = _Cooldown;
                _Cooldown = value;

                OnChanged(oldValue, value, "Cooldown");
            }
        }
        private int _Cooldown;

        public int MaxExecutions
        {
            get { return _MaxExecutions; }
            set
            {
                if (_MaxExecutions == value) return;

                var oldValue = _MaxExecutions;
                _MaxExecutions = value;

                OnChanged(oldValue, value, "MaxExecutions");
            }
        }
        private int _MaxExecutions;

        public int Value
        {
            get { return _Value; }
            set
            {
                if (_Value == value) return;

                var oldValue = _Value;
                _Value = value;

                OnChanged(oldValue, value, "Value");
            }
        }
        private int _Value;

        public int Radius
        {
            get { return _Radius; }
            set
            {
                if (_Radius == value) return;

                var oldValue = _Radius;
                _Radius = value;

                OnChanged(oldValue, value, "Radius");
            }
        }
        private int _Radius;

        public BuffType BuffType
        {
            get { return _BuffType; }
            set
            {
                if (_BuffType == value) return;

                var oldValue = _BuffType;
                _BuffType = value;

                OnChanged(oldValue, value, "BuffType");
            }
        }
        private BuffType _BuffType;

        public string Conditions
        {
            get { return _Conditions; }
            set
            {
                if (_Conditions == value) return;

                var oldValue = _Conditions;
                _Conditions = value;

                OnChanged(oldValue, value, "Conditions");
            }
        }
        private string _Conditions;

        public CustomAiConditionJoin ConditionJoin
        {
            get { return _ConditionJoin; }
            set
            {
                if (_ConditionJoin == value) return;

                var oldValue = _ConditionJoin;
                _ConditionJoin = value;

                OnChanged(oldValue, value, "ConditionJoin");
            }
        }
        private CustomAiConditionJoin _ConditionJoin;

        public bool InvertCondition
        {
            get { return _InvertCondition; }
            set
            {
                if (_InvertCondition == value) return;

                var oldValue = _InvertCondition;
                _InvertCondition = value;

                OnChanged(oldValue, value, "InvertCondition");
            }
        }
        private bool _InvertCondition;

        public string CounterName
        {
            get { return _CounterName; }
            set
            {
                if (_CounterName == value) return;

                var oldValue = _CounterName;
                _CounterName = value;

                OnChanged(oldValue, value, "CounterName");
            }
        }
        private string _CounterName;

        public int MinHealthPercent
        {
            get { return _MinHealthPercent; }
            set
            {
                if (_MinHealthPercent == value) return;

                var oldValue = _MinHealthPercent;
                _MinHealthPercent = value;

                OnChanged(oldValue, value, "MinHealthPercent");
            }
        }
        private int _MinHealthPercent;

        public int MaxHealthPercent
        {
            get { return _MaxHealthPercent; }
            set
            {
                if (_MaxHealthPercent == value) return;

                var oldValue = _MaxHealthPercent;
                _MaxHealthPercent = value;

                OnChanged(oldValue, value, "MaxHealthPercent");
            }
        }
        private int _MaxHealthPercent;

        protected internal override void OnCreated()
        {
            base.OnCreated();

            Trigger = CustomAiTriggerType.OnTargetTick;
            Condition = CustomAiConditionType.Always;
            ConditionJoin = CustomAiConditionJoin.And;
            Chance = 100;
            MaxHealthPercent = 100;
        }
    }

    public sealed class CustomMonsterAiActionInfo : DBObject
    {
        [Association("Actions")]
        public CustomMonsterBehaviorInfo Behavior
        {
            get { return _Behavior; }
            set
            {
                if (_Behavior == value) return;

                var oldValue = _Behavior;
                _Behavior = value;

                OnChanged(oldValue, value, "Behavior");
            }
        }
        private CustomMonsterBehaviorInfo _Behavior;

        public string Name
        {
            get { return _Name; }
            set
            {
                if (_Name == value) return;

                var oldValue = _Name;
                _Name = value;

                OnChanged(oldValue, value, "Name");
            }
        }
        private string _Name;

        public CustomAiActionType Action
        {
            get { return _Action; }
            set
            {
                if (_Action == value) return;

                var oldValue = _Action;
                _Action = value;

                OnChanged(oldValue, value, "Action");
            }
        }
        private CustomAiActionType _Action;

        public CustomAiTargetSelectorType Target
        {
            get { return _Target; }
            set
            {
                if (_Target == value) return;

                var oldValue = _Target;
                _Target = value;

                OnChanged(oldValue, value, "Target");
            }
        }
        private CustomAiTargetSelectorType _Target;

        public int AnimationVariant
        {
            get { return _AnimationVariant; }
            set
            {
                if (_AnimationVariant == value) return;

                var oldValue = _AnimationVariant;
                _AnimationVariant = value;

                OnChanged(oldValue, value, "AnimationVariant");
            }
        }
        private int _AnimationVariant;

        public string Options
        {
            get { return _Options; }
            set
            {
                if (_Options == value) return;

                var oldValue = _Options;
                _Options = value;

                OnChanged(oldValue, value, "Options");
            }
        }
        private string _Options;

        public MagicType Magic
        {
            get { return _Magic; }
            set
            {
                if (_Magic == value) return;

                var oldValue = _Magic;
                _Magic = value;

                OnChanged(oldValue, value, "Magic");
            }
        }
        private MagicType _Magic;

        public MagicInfo MagicInfo
        {
            get { return _MagicInfo; }
            set
            {
                if (_MagicInfo == value) return;

                var oldValue = _MagicInfo;
                _MagicInfo = value;

                OnChanged(oldValue, value, "MagicInfo");
            }
        }
        private MagicInfo _MagicInfo;

        public Element Element
        {
            get { return _Element; }
            set
            {
                if (_Element == value) return;

                var oldValue = _Element;
                _Element = value;

                OnChanged(oldValue, value, "Element");
            }
        }
        private Element _Element;

        public CustomAiPowerSource PowerSource
        {
            get { return _PowerSource; }
            set
            {
                if (_PowerSource == value) return;

                var oldValue = _PowerSource;
                _PowerSource = value;

                OnChanged(oldValue, value, "PowerSource");
            }
        }
        private CustomAiPowerSource _PowerSource;

        public MonsterInfo SpawnMonster
        {
            get { return _SpawnMonster; }
            set
            {
                if (_SpawnMonster == value) return;

                var oldValue = _SpawnMonster;
                _SpawnMonster = value;

                OnChanged(oldValue, value, "SpawnMonster");
            }
        }
        private MonsterInfo _SpawnMonster;

        public BuffType BuffType
        {
            get { return _BuffType; }
            set
            {
                if (_BuffType == value) return;

                var oldValue = _BuffType;
                _BuffType = value;

                OnChanged(oldValue, value, "BuffType");
            }
        }
        private BuffType _BuffType;

        public PoisonType PoisonType
        {
            get { return _PoisonType; }
            set
            {
                if (_PoisonType == value) return;

                var oldValue = _PoisonType;
                _PoisonType = value;

                OnChanged(oldValue, value, "PoisonType");
            }
        }
        private PoisonType _PoisonType;

        public bool PoisonCanKill
        {
            get { return _PoisonCanKill; }
            set
            {
                if (_PoisonCanKill == value) return;

                var oldValue = _PoisonCanKill;
                _PoisonCanKill = value;

                OnChanged(oldValue, value, "PoisonCanKill");
            }
        }
        private bool _PoisonCanKill;

        public Stats BuffStats
        {
            get { return _BuffStats; }
            set
            {
                if (_BuffStats == value) return;

                var oldValue = _BuffStats;
                _BuffStats = value;

                OnChanged(oldValue, value, "BuffStats");
            }
        }
        private Stats _BuffStats;

        public bool BuffVisible
        {
            get { return _BuffVisible; }
            set
            {
                if (_BuffVisible == value) return;

                var oldValue = _BuffVisible;
                _BuffVisible = value;

                OnChanged(oldValue, value, "BuffVisible");
            }
        }
        private bool _BuffVisible;

        public bool BuffPause
        {
            get { return _BuffPause; }
            set
            {
                if (_BuffPause == value) return;

                var oldValue = _BuffPause;
                _BuffPause = value;

                OnChanged(oldValue, value, "BuffPause");
            }
        }
        private bool _BuffPause;

        public bool BuffHidden
        {
            get { return _BuffHidden; }
            set
            {
                if (_BuffHidden == value) return;

                var oldValue = _BuffHidden;
                _BuffHidden = value;

                OnChanged(oldValue, value, "BuffHidden");
            }
        }
        private bool _BuffHidden;

        public int BuffExtra
        {
            get { return _BuffExtra; }
            set
            {
                if (_BuffExtra == value) return;

                var oldValue = _BuffExtra;
                _BuffExtra = value;

                OnChanged(oldValue, value, "BuffExtra");
            }
        }
        private int _BuffExtra;

        public int Duration
        {
            get { return _Duration; }
            set
            {
                if (_Duration == value) return;

                var oldValue = _Duration;
                _Duration = value;

                OnChanged(oldValue, value, "Duration");
            }
        }
        private int _Duration;

        public int TickRate
        {
            get { return _TickRate; }
            set
            {
                if (_TickRate == value) return;

                var oldValue = _TickRate;
                _TickRate = value;

                OnChanged(oldValue, value, "TickRate");
            }
        }
        private int _TickRate;

        public int FixedPower
        {
            get { return _FixedPower; }
            set
            {
                if (_FixedPower == value) return;

                var oldValue = _FixedPower;
                _FixedPower = value;

                OnChanged(oldValue, value, "FixedPower");
            }
        }
        private int _FixedPower;

        public int PowerRate
        {
            get { return _PowerRate; }
            set
            {
                if (_PowerRate == value) return;

                var oldValue = _PowerRate;
                _PowerRate = value;

                OnChanged(oldValue, value, "PowerRate");
            }
        }
        private int _PowerRate;

        public int Radius
        {
            get { return _Radius; }
            set
            {
                if (_Radius == value) return;

                var oldValue = _Radius;
                _Radius = value;

                OnChanged(oldValue, value, "Radius");
            }
        }
        private int _Radius;

        public int Distance
        {
            get { return _Distance; }
            set
            {
                if (_Distance == value) return;

                var oldValue = _Distance;
                _Distance = value;

                OnChanged(oldValue, value, "Distance");
            }
        }
        private int _Distance;

        public int MinOffset
        {
            get { return _MinOffset; }
            set
            {
                if (_MinOffset == value) return;

                var oldValue = _MinOffset;
                _MinOffset = value;

                OnChanged(oldValue, value, "MinOffset");
            }
        }
        private int _MinOffset;

        public int MaxOffset
        {
            get { return _MaxOffset; }
            set
            {
                if (_MaxOffset == value) return;

                var oldValue = _MaxOffset;
                _MaxOffset = value;

                OnChanged(oldValue, value, "MaxOffset");
            }
        }
        private int _MaxOffset;

        public int Delay
        {
            get { return _Delay; }
            set
            {
                if (_Delay == value) return;

                var oldValue = _Delay;
                _Delay = value;

                OnChanged(oldValue, value, "Delay");
            }
        }
        private int _Delay;

        public int TargetChance
        {
            get { return _TargetChance; }
            set
            {
                if (_TargetChance == value) return;

                var oldValue = _TargetChance;
                _TargetChance = value;

                OnChanged(oldValue, value, "TargetChance");
            }
        }
        private int _TargetChance;

        public int EmptyLocationChance
        {
            get { return _EmptyLocationChance; }
            set
            {
                if (_EmptyLocationChance == value) return;

                var oldValue = _EmptyLocationChance;
                _EmptyLocationChance = value;

                OnChanged(oldValue, value, "EmptyLocationChance");
            }
        }
        private int _EmptyLocationChance;

        public int MaxTargets
        {
            get { return _MaxTargets; }
            set
            {
                if (_MaxTargets == value) return;

                var oldValue = _MaxTargets;
                _MaxTargets = value;

                OnChanged(oldValue, value, "MaxTargets");
            }
        }
        private int _MaxTargets;

        public int RandomTargetCount
        {
            get { return _RandomTargetCount; }
            set
            {
                if (_RandomTargetCount == value) return;

                var oldValue = _RandomTargetCount;
                _RandomTargetCount = value;

                OnChanged(oldValue, value, "RandomTargetCount");
            }
        }
        private int _RandomTargetCount;

        public string CounterName
        {
            get { return _CounterName; }
            set
            {
                if (_CounterName == value) return;

                var oldValue = _CounterName;
                _CounterName = value;

                OnChanged(oldValue, value, "CounterName");
            }
        }
        private string _CounterName;

        public int FixedCount
        {
            get { return _FixedCount; }
            set
            {
                if (_FixedCount == value) return;

                var oldValue = _FixedCount;
                _FixedCount = value;

                OnChanged(oldValue, value, "FixedCount");
            }
        }
        private int _FixedCount;

        public int RandomCount
        {
            get { return _RandomCount; }
            set
            {
                if (_RandomCount == value) return;

                var oldValue = _RandomCount;
                _RandomCount = value;

                OnChanged(oldValue, value, "RandomCount");
            }
        }
        private int _RandomCount;

        public int MinionDuration
        {
            get { return _MinionDuration; }
            set
            {
                if (_MinionDuration == value) return;

                var oldValue = _MinionDuration;
                _MinionDuration = value;

                OnChanged(oldValue, value, "MinionDuration");
            }
        }
        private int _MinionDuration;

        public bool Travel
        {
            get { return _Travel; }
            set
            {
                if (_Travel == value) return;

                var oldValue = _Travel;
                _Travel = value;

                OnChanged(oldValue, value, "Travel");
            }
        }
        private bool _Travel;

        protected internal override void OnCreated()
        {
            base.OnCreated();

            Target = CustomAiTargetSelectorType.CurrentTarget;
            PowerSource = CustomAiPowerSource.DC;
            PowerRate = 100;
            Delay = 500;
            TargetChance = 100;
            Duration = 3000;
            TickRate = 1000;
            PoisonCanKill = true;
            BuffVisible = true;
        }
    }

    public sealed class CustomSkillInfo : DBObject
    {
        [IsIdentity]
        public MagicInfo Magic
        {
            get { return _Magic; }
            set
            {
                if (_Magic == value) return;

                var oldValue = _Magic;
                _Magic = value;

                OnChanged(oldValue, value, "Magic");
            }
        }
        private MagicInfo _Magic;

        public CustomSkillTemplate Template
        {
            get { return _Template; }
            set
            {
                if (_Template == value) return;

                var oldValue = _Template;
                _Template = value;

                OnChanged(oldValue, value, "Template");
            }
        }
        private CustomSkillTemplate _Template;

        public CustomSkillTargetRule TargetRule
        {
            get { return _TargetRule; }
            set
            {
                if (_TargetRule == value) return;

                var oldValue = _TargetRule;
                _TargetRule = value;

                OnChanged(oldValue, value, "TargetRule");
            }
        }
        private CustomSkillTargetRule _TargetRule;

        public bool Enabled
        {
            get { return _Enabled; }
            set
            {
                if (_Enabled == value) return;

                var oldValue = _Enabled;
                _Enabled = value;

                OnChanged(oldValue, value, "Enabled");
            }
        }
        private bool _Enabled;

        public int Range
        {
            get { return _Range; }
            set
            {
                if (_Range == value) return;

                var oldValue = _Range;
                _Range = value;

                OnChanged(oldValue, value, "Range");
            }
        }
        private int _Range;

        public int Radius
        {
            get { return _Radius; }
            set
            {
                if (_Radius == value) return;

                var oldValue = _Radius;
                _Radius = value;

                OnChanged(oldValue, value, "Radius");
            }
        }
        private int _Radius;

        public int MaxTargets
        {
            get { return _MaxTargets; }
            set
            {
                if (_MaxTargets == value) return;

                var oldValue = _MaxTargets;
                _MaxTargets = value;

                OnChanged(oldValue, value, "MaxTargets");
            }
        }
        private int _MaxTargets;

        public CustomSkillShape Shape
        {
            get { return _Shape; }
            set
            {
                if (_Shape == value) return;

                var oldValue = _Shape;
                _Shape = value;

                OnChanged(oldValue, value, "Shape");
            }
        }
        private CustomSkillShape _Shape;

        public int Length
        {
            get { return _Length; }
            set
            {
                if (_Length == value) return;

                var oldValue = _Length;
                _Length = value;

                OnChanged(oldValue, value, "Length");
            }
        }
        private int _Length;

        public int Width
        {
            get { return _Width; }
            set
            {
                if (_Width == value) return;

                var oldValue = _Width;
                _Width = value;

                OnChanged(oldValue, value, "Width");
            }
        }
        private int _Width;

        public int Angle
        {
            get { return _Angle; }
            set
            {
                if (_Angle == value) return;

                var oldValue = _Angle;
                _Angle = value;

                OnChanged(oldValue, value, "Angle");
            }
        }
        private int _Angle;

        public int WaveCount
        {
            get { return _WaveCount; }
            set
            {
                if (_WaveCount == value) return;

                var oldValue = _WaveCount;
                _WaveCount = value;

                OnChanged(oldValue, value, "WaveCount");
            }
        }
        private int _WaveCount;

        public int WaveInterval
        {
            get { return _WaveInterval; }
            set
            {
                if (_WaveInterval == value) return;

                var oldValue = _WaveInterval;
                _WaveInterval = value;

                OnChanged(oldValue, value, "WaveInterval");
            }
        }
        private int _WaveInterval;

        public int KnockbackDistance
        {
            get { return _KnockbackDistance; }
            set
            {
                if (_KnockbackDistance == value) return;

                var oldValue = _KnockbackDistance;
                _KnockbackDistance = value;

                OnChanged(oldValue, value, "KnockbackDistance");
            }
        }
        private int _KnockbackDistance;

        public int PullDistance
        {
            get { return _PullDistance; }
            set
            {
                if (_PullDistance == value) return;

                var oldValue = _PullDistance;
                _PullDistance = value;

                OnChanged(oldValue, value, "PullDistance");
            }
        }
        private int _PullDistance;

        public Element Element
        {
            get { return _Element; }
            set
            {
                if (_Element == value) return;

                var oldValue = _Element;
                _Element = value;

                OnChanged(oldValue, value, "Element");
            }
        }
        private Element _Element;

        public CustomSkillPowerSource PowerSource
        {
            get { return _PowerSource; }
            set
            {
                if (_PowerSource == value) return;

                var oldValue = _PowerSource;
                _PowerSource = value;

                OnChanged(oldValue, value, "PowerSource");
            }
        }
        private CustomSkillPowerSource _PowerSource;

        public bool IgnoreShield
        {
            get { return _IgnoreShield; }
            set
            {
                if (_IgnoreShield == value) return;

                var oldValue = _IgnoreShield;
                _IgnoreShield = value;

                OnChanged(oldValue, value, "IgnoreShield");
            }
        }
        private bool _IgnoreShield;

        public bool IgnoreArmour
        {
            get { return _IgnoreArmour; }
            set
            {
                if (_IgnoreArmour == value) return;

                var oldValue = _IgnoreArmour;
                _IgnoreArmour = value;

                OnChanged(oldValue, value, "IgnoreArmour");
            }
        }
        private bool _IgnoreArmour;

        public int FixedPower
        {
            get { return _FixedPower; }
            set
            {
                if (_FixedPower == value) return;

                var oldValue = _FixedPower;
                _FixedPower = value;

                OnChanged(oldValue, value, "FixedPower");
            }
        }
        private int _FixedPower;

        public int PowerRate
        {
            get { return _PowerRate; }
            set
            {
                if (_PowerRate == value) return;

                var oldValue = _PowerRate;
                _PowerRate = value;

                OnChanged(oldValue, value, "PowerRate");
            }
        }
        private int _PowerRate;

        public int Duration
        {
            get { return _Duration; }
            set
            {
                if (_Duration == value) return;

                var oldValue = _Duration;
                _Duration = value;

                OnChanged(oldValue, value, "Duration");
            }
        }
        private int _Duration;

        public int TickRate
        {
            get { return _TickRate; }
            set
            {
                if (_TickRate == value) return;

                var oldValue = _TickRate;
                _TickRate = value;

                OnChanged(oldValue, value, "TickRate");
            }
        }
        private int _TickRate;

        public BuffType BuffType
        {
            get { return _BuffType; }
            set
            {
                if (_BuffType == value) return;

                var oldValue = _BuffType;
                _BuffType = value;

                OnChanged(oldValue, value, "BuffType");
            }
        }
        private BuffType _BuffType;

        public PoisonType PoisonType
        {
            get { return _PoisonType; }
            set
            {
                if (_PoisonType == value) return;

                var oldValue = _PoisonType;
                _PoisonType = value;

                OnChanged(oldValue, value, "PoisonType");
            }
        }
        private PoisonType _PoisonType;

        public int PoisonValue
        {
            get { return _PoisonValue; }
            set
            {
                if (_PoisonValue == value) return;

                var oldValue = _PoisonValue;
                _PoisonValue = value;

                OnChanged(oldValue, value, "PoisonValue");
            }
        }
        private int _PoisonValue;

        public bool PoisonCanKill
        {
            get { return _PoisonCanKill; }
            set
            {
                if (_PoisonCanKill == value) return;

                var oldValue = _PoisonCanKill;
                _PoisonCanKill = value;

                OnChanged(oldValue, value, "PoisonCanKill");
            }
        }
        private bool _PoisonCanKill;

        public Stats BuffStats
        {
            get { return _BuffStats; }
            set
            {
                if (_BuffStats == value) return;

                var oldValue = _BuffStats;
                _BuffStats = value;

                OnChanged(oldValue, value, "BuffStats");
            }
        }
        private Stats _BuffStats;

        public bool BuffVisible
        {
            get { return _BuffVisible; }
            set
            {
                if (_BuffVisible == value) return;

                var oldValue = _BuffVisible;
                _BuffVisible = value;

                OnChanged(oldValue, value, "BuffVisible");
            }
        }
        private bool _BuffVisible;

        public bool BuffPause
        {
            get { return _BuffPause; }
            set
            {
                if (_BuffPause == value) return;

                var oldValue = _BuffPause;
                _BuffPause = value;

                OnChanged(oldValue, value, "BuffPause");
            }
        }
        private bool _BuffPause;

        public bool BuffHidden
        {
            get { return _BuffHidden; }
            set
            {
                if (_BuffHidden == value) return;

                var oldValue = _BuffHidden;
                _BuffHidden = value;

                OnChanged(oldValue, value, "BuffHidden");
            }
        }
        private bool _BuffHidden;

        public int BuffExtra
        {
            get { return _BuffExtra; }
            set
            {
                if (_BuffExtra == value) return;

                var oldValue = _BuffExtra;
                _BuffExtra = value;

                OnChanged(oldValue, value, "BuffExtra");
            }
        }
        private int _BuffExtra;

        public int CastDelay
        {
            get { return _CastDelay; }
            set
            {
                if (_CastDelay == value) return;

                var oldValue = _CastDelay;
                _CastDelay = value;

                OnChanged(oldValue, value, "CastDelay");
            }
        }
        private int _CastDelay;

        public int ProjectileSpeed
        {
            get { return _ProjectileSpeed; }
            set
            {
                if (_ProjectileSpeed == value) return;

                var oldValue = _ProjectileSpeed;
                _ProjectileSpeed = value;

                OnChanged(oldValue, value, "ProjectileSpeed");
            }
        }
        private int _ProjectileSpeed;

        public MonsterInfo SummonMonster
        {
            get { return _SummonMonster; }
            set
            {
                if (_SummonMonster == value) return;

                var oldValue = _SummonMonster;
                _SummonMonster = value;

                OnChanged(oldValue, value, "SummonMonster");
            }
        }
        private MonsterInfo _SummonMonster;

        public int MaxSummons
        {
            get { return _MaxSummons; }
            set
            {
                if (_MaxSummons == value) return;

                var oldValue = _MaxSummons;
                _MaxSummons = value;

                OnChanged(oldValue, value, "MaxSummons");
            }
        }
        private int _MaxSummons;

        public int SummonDuration
        {
            get { return _SummonDuration; }
            set
            {
                if (_SummonDuration == value) return;

                var oldValue = _SummonDuration;
                _SummonDuration = value;

                OnChanged(oldValue, value, "SummonDuration");
            }
        }
        private int _SummonDuration;

        [Association("Effects", true)]
        public DBBindingList<CustomSkillEffectInfo> Effects { get; set; }

        protected internal override void OnCreated()
        {
            base.OnCreated();

            Enabled = true;
            Template = CustomSkillTemplate.DirectDamage;
            TargetRule = CustomSkillTargetRule.Enemy;
            Range = Globals.MagicRange;
            Radius = 0;
            Shape = CustomSkillShape.Circle;
            WaveCount = 1;
            PowerRate = 100;
            TickRate = 1000;
            MaxSummons = 1;
            BuffVisible = true;
            CastDelay = 500;
            ProjectileSpeed = Globals.ProjectileSpeed;
        }

        public override string ToString()
        {
            return Magic?.Name ?? $"CustomSkill #{Index}";
        }
    }

    public sealed class CustomSkillEffectInfo : DBObject
    {
        [Association("Effects")]
        public CustomSkillInfo Skill
        {
            get { return _Skill; }
            set
            {
                if (_Skill == value) return;

                var oldValue = _Skill;
                _Skill = value;

                OnChanged(oldValue, value, "Skill");
            }
        }
        private CustomSkillInfo _Skill;

        public CustomResourceLibraryInfo Resource
        {
            get { return _Resource; }
            set
            {
                if (_Resource == value) return;

                var oldValue = _Resource;
                _Resource = value;

                OnChanged(oldValue, value, "Resource");
            }
        }
        private CustomResourceLibraryInfo _Resource;

        public CustomSkillEffectStage Stage
        {
            get { return _Stage; }
            set
            {
                if (_Stage == value) return;

                var oldValue = _Stage;
                _Stage = value;

                OnChanged(oldValue, value, "Stage");
            }
        }
        private CustomSkillEffectStage _Stage;

        public int StartIndex
        {
            get { return _StartIndex; }
            set
            {
                if (_StartIndex == value) return;

                var oldValue = _StartIndex;
                _StartIndex = value;

                OnChanged(oldValue, value, "StartIndex");
            }
        }
        private int _StartIndex;

        public int EndIndex
        {
            get { return _EndIndex; }
            set
            {
                if (_EndIndex == value) return;

                var oldValue = _EndIndex;
                _EndIndex = value;

                OnChanged(oldValue, value, "EndIndex");
            }
        }
        private int _EndIndex;

        public int FrameCount
        {
            get { return _FrameCount; }
            set
            {
                if (_FrameCount == value) return;

                var oldValue = _FrameCount;
                _FrameCount = value;

                OnChanged(oldValue, value, "FrameCount");
            }
        }
        private int _FrameCount;

        public int FrameDelay
        {
            get { return _FrameDelay; }
            set
            {
                if (_FrameDelay == value) return;

                var oldValue = _FrameDelay;
                _FrameDelay = value;

                OnChanged(oldValue, value, "FrameDelay");
            }
        }
        private int _FrameDelay;

        public int OffsetX
        {
            get { return _OffsetX; }
            set
            {
                if (_OffsetX == value) return;

                var oldValue = _OffsetX;
                _OffsetX = value;

                OnChanged(oldValue, value, "OffsetX");
            }
        }
        private int _OffsetX;

        public int OffsetY
        {
            get { return _OffsetY; }
            set
            {
                if (_OffsetY == value) return;

                var oldValue = _OffsetY;
                _OffsetY = value;

                OnChanged(oldValue, value, "OffsetY");
            }
        }
        private int _OffsetY;

        public int StartLight
        {
            get { return _StartLight; }
            set
            {
                if (_StartLight == value) return;

                var oldValue = _StartLight;
                _StartLight = value;

                OnChanged(oldValue, value, "StartLight");
            }
        }
        private int _StartLight;

        public int EndLight
        {
            get { return _EndLight; }
            set
            {
                if (_EndLight == value) return;

                var oldValue = _EndLight;
                _EndLight = value;

                OnChanged(oldValue, value, "EndLight");
            }
        }
        private int _EndLight;

        public SoundIndex Sound
        {
            get { return _Sound; }
            set
            {
                if (_Sound == value) return;

                var oldValue = _Sound;
                _Sound = value;

                OnChanged(oldValue, value, "Sound");
            }
        }
        private SoundIndex _Sound;

        public bool Blend
        {
            get { return _Blend; }
            set
            {
                if (_Blend == value) return;

                var oldValue = _Blend;
                _Blend = value;

                OnChanged(oldValue, value, "Blend");
            }
        }
        private bool _Blend;

        public bool Loop
        {
            get { return _Loop; }
            set
            {
                if (_Loop == value) return;

                var oldValue = _Loop;
                _Loop = value;

                OnChanged(oldValue, value, "Loop");
            }
        }
        private bool _Loop;

        public int Duration
        {
            get { return _Duration; }
            set
            {
                if (_Duration == value) return;

                var oldValue = _Duration;
                _Duration = value;

                OnChanged(oldValue, value, "Duration");
            }
        }
        private int _Duration;

        public bool FollowTarget
        {
            get { return _FollowTarget; }
            set
            {
                if (_FollowTarget == value) return;

                var oldValue = _FollowTarget;
                _FollowTarget = value;

                OnChanged(oldValue, value, "FollowTarget");
            }
        }
        private bool _FollowTarget;

        protected internal override void OnCreated()
        {
            base.OnCreated();

            FrameDelay = 100;
            FollowTarget = true;
        }
    }
}
