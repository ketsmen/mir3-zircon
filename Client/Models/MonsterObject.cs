using Client.Envir;
using Client.Rendering;
using Client.Scenes;
using Client.Scenes.Views;
using Library;
using Library.SystemModels;
using MirDB;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using S = Library.Network.ServerPackets;

namespace Client.Models
{
    public sealed class MonsterObject : MapObject
    {
        public override ObjectType Race => ObjectType.Monster;
        public override bool Blocking => base.Blocking && CompanionObject == null && !(MonsterInfo.Flag == MonsterFlag.CastleDefense && Direction == (MirDirection)7);

        public MonsterInfo MonsterInfo;

        public MirLibrary BodyLibrary;
        public int BodyOffSet = 1000;
        public int BodyShape;

        public float Scale = 1F;
        public CustomMonsterVisualInfo CustomVisual;
        public bool UsingCustomVisual;
        private Dictionary<MirAnimation, CustomMonsterAnimationInfo> CustomAnimationMap;
        private Dictionary<MirAnimation, MirLibrary> CustomAnimationLibraryMap;

        public int GrowthLevel;

        public int BodyFrame => DrawFrame + (BodyShape % 10) * BodyOffSet;

        public SoundIndex AttackSound, StruckSound, DieSound;

        public bool Extra, EasterEvent, ChristmasEvent, HalloweenEvent;

        public int Extra1;

        public Color Colour;

        public override int RenderY
        {
            get
            {
                int offset = 0;

                if (Image == MonsterImage.LobsterLord)
                    offset += 5;

                return base.RenderY + offset;
            }
        }

        public override string Name
        {
            get
            {
                if (string.IsNullOrEmpty(PetOwner))
                    return base.Name;

                return base.Name + $" ({PetOwner})";
            }
            set { base.Name = value; }
        }

        public ClientCompanionObject CompanionObject;

        public MonsterImage Image;

        public MonsterObject(CompanionInfo info)
        {
            MonsterInfo = info.MonsterInfo;
            CustomVisual = Globals.CustomMonsterVisualInfoList?.Binding.FirstOrDefault(x => x?.Monster == MonsterInfo);

            Stats = new Stats(MonsterInfo.Stats);

            Light = Stats[Stat.Light];

            Name = MonsterInfo.MonsterName;

            Direction = MirDirection.DownLeft;

            UpdateLibraries();

            SetAnimation(new ObjectAction(MirAction.Standing, Direction, Point.Empty));
        }
        public MonsterObject(S.ObjectMonster info)
        {
            ObjectID = info.ObjectID;

            MonsterInfo = ResolveMonsterInfo(info.MonsterIndex, info.RuntimeMonsterInfo);
            CustomVisual = CreateRuntimeCustomVisual(info.CustomMonsterVisual, MonsterInfo)
                           ?? ResolveCustomVisual(MonsterInfo, info.CustomMonsterVisualIndex);

            CompanionObject = info.CompanionObject;

            Stats = new Stats(MonsterInfo.Stats);

            Light = Stats[Stat.Light];

            Name = string.IsNullOrEmpty(info.CustomName) ? (CompanionObject?.Name ?? MonsterInfo.MonsterName) : info.CustomName;

            PetOwner = info.PetOwner;
            NameColour = info.NameColour;
            Extra = info.Extra;

            Extra1 = info.Extra1;
            Colour = info.Colour;

            CurrentLocation = info.Location;
            Direction = info.Direction;

            Dead = info.Dead;
            Skeleton = info.Skeleton;

            EasterEvent = info.EasterEvent;
            HalloweenEvent = info.HalloweenEvent;
            ChristmasEvent = info.ChristmasEvent;

            Poison = info.Poison;

            foreach (BuffType type in info.Buffs.Keys)
            {
                if (!VisibleBuffs.ContainsKey(type))
                    VisibleBuffs[type] = 0;

                VisibleBuffs[type] = info.Buffs[type];
            }

            UpdateLibraries();
            SetScale();

            int frameStartDelay = CEnvir.Random.Next(5) * 100;

            SetFrame(new ObjectAction(!Dead ? MirAction.Standing : MirAction.Dead, MirDirection.Up, CurrentLocation), frameStartDelay);

            GameScene.Game.MapControl.AddObject(this);

            UpdateQuests();
        }

        private static MonsterInfo ResolveMonsterInfo(int monsterIndex, S.ClientRuntimeMonsterInfo runtimeInfo)
        {
            return Globals.MonsterInfoList?.Binding.FirstOrDefault(x => x.Index == monsterIndex)
                   ?? runtimeInfo?.ToMonsterInfo()
                   ?? new S.ClientRuntimeMonsterInfo { Index = monsterIndex }.ToMonsterInfo();
        }

        private static CustomMonsterVisualInfo ResolveCustomVisual(MonsterInfo monsterInfo, int visualIndex)
        {
            if (visualIndex > 0)
                return Globals.CustomMonsterVisualInfoList?.Binding.FirstOrDefault(x => x?.Index == visualIndex);

            return Globals.CustomMonsterVisualInfoList?.Binding.FirstOrDefault(x => x?.Monster == monsterInfo);
        }

        private static CustomMonsterVisualInfo CreateRuntimeCustomVisual(S.ClientCustomMonsterVisualInfo info, MonsterInfo monsterInfo)
        {
            if (info == null) return null;

            CustomMonsterVisualInfo visual = new CustomMonsterVisualInfo
            {
                Monster = monsterInfo,
                DefaultResource = CreateRuntimeCustomResource(info.DefaultResource),
                Scale = info.Scale <= 0 ? 1F : info.Scale,
                DrawShadow = info.DrawShadow,
                Blend = info.Blend,
                KeepCorpse = info.KeepCorpse,
                DefaultFrameDelay = info.DefaultFrameDelay <= 0 ? 100 : info.DefaultFrameDelay,
                FrameBase = info.FrameBase,
                AttackSound = info.AttackSound,
                RangeAttackSound = info.RangeAttackSound,
                MagicSound = info.MagicSound,
                StruckSound = info.StruckSound,
                DieSound = info.DieSound,
                ShowSound = info.ShowSound,
                HideSound = info.HideSound,
            };

            visual.Animations = new DBBindingList<CustomMonsterAnimationInfo>(
                visual,
                typeof(CustomMonsterVisualInfo).GetProperty(nameof(CustomMonsterVisualInfo.Animations)));

            if (info.Animations == null) return visual;

            foreach (S.ClientCustomMonsterAnimationInfo source in info.Animations.Where(x => x != null))
            {
                CustomMonsterAnimationInfo animation = new CustomMonsterAnimationInfo
                {
                    Resource = CreateRuntimeCustomResource(source.Resource),
                    Action = source.Action,
                    Variant = source.Variant,
                    StartIndex = source.StartIndex,
                    EndIndex = source.EndIndex,
                    FrameCount = source.FrameCount,
                    DirectionCount = source.DirectionCount <= 0 ? 8 : source.DirectionCount,
                    DirectionOffset = source.DirectionOffset,
                    FrameDelay = source.FrameDelay <= 0 ? 100 : source.FrameDelay,
                    Loop = source.Loop,
                    Reversed = source.Reversed,
                    OffsetX = source.OffsetX,
                    OffsetY = source.OffsetY,
                    Priority = source.Priority,
                    Sound = source.Sound,
                    EffectResource = CreateRuntimeCustomResource(source.EffectResource),
                    EffectTarget = source.EffectTarget,
                    EffectStartIndex = source.EffectStartIndex,
                    EffectEndIndex = source.EffectEndIndex,
                    EffectFrameCount = source.EffectFrameCount,
                    EffectFrameDelay = source.EffectFrameDelay <= 0 ? 100 : source.EffectFrameDelay,
                    EffectOffsetX = source.EffectOffsetX,
                    EffectOffsetY = source.EffectOffsetY,
                    EffectStartLight = source.EffectStartLight,
                    EffectEndLight = source.EffectEndLight,
                    EffectStartDelay = source.EffectStartDelay,
                    EffectDuration = source.EffectDuration,
                    EffectTriggerFrame = source.EffectTriggerFrame,
                    EffectBlend = source.EffectBlend,
                    EffectLoop = source.EffectLoop,
                };

                visual.Animations.Add(animation);
            }

            return visual;
        }

        private static CustomResourceLibraryInfo CreateRuntimeCustomResource(S.ClientCustomResourceLibraryInfo info)
        {
            if (info == null) return null;

            return new CustomResourceLibraryInfo
            {
                Key = info.Key,
                DisplayName = info.DisplayName,
                RelativePath = info.RelativePath,
                LibraryFile = info.LibraryFile,
                Hash = info.Hash,
                Version = info.Version,
                Enabled = info.Enabled,
                Scope = info.Scope,
            };
        }

        public void UpdateLibraries()
        {
            BodyLibrary = null;
            UsingCustomVisual = false;
            CustomAnimationMap = null;
            CustomAnimationLibraryMap = null;

            Frames = new Dictionary<MirAnimation, Frame>(FrameSet.DefaultMonster);

            BodyOffSet = 1000;

            AttackSound = SoundIndex.None;
            StruckSound = SoundIndex.None;
            DieSound = SoundIndex.None;
            //OtherSounds

            Image = MonsterInfo.Image;

            if (TryApplyCustomVisual())
                return;

            switch (Image)
            {
                case MonsterImage.Chicken:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_3, out BodyLibrary);
                    BodyShape = 0;
                    AttackSound = SoundIndex.ChickenAttack;
                    StruckSound = SoundIndex.ChickenStruck;
                    DieSound = SoundIndex.ChickenDie;
                    break;
                case MonsterImage.Pig:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_12, out BodyLibrary);
                    BodyShape = 9;
                    AttackSound = SoundIndex.PigAttack;
                    StruckSound = SoundIndex.PigStruck;
                    DieSound = SoundIndex.PigDie;
                    break;
                case MonsterImage.Deer:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_3, out BodyLibrary);
                    BodyShape = 1;
                    AttackSound = SoundIndex.DeerAttack;
                    StruckSound = SoundIndex.DeerStruck;
                    DieSound = SoundIndex.DeerDie;
                    break;
                case MonsterImage.Cow:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_13, out BodyLibrary);
                    BodyShape = 1;
                    AttackSound = SoundIndex.CowAttack;
                    StruckSound = SoundIndex.CowStruck;
                    DieSound = SoundIndex.CowDie;
                    break;
                case MonsterImage.Sheep:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_6, out BodyLibrary);
                    BodyShape = 8;
                    AttackSound = SoundIndex.SheepAttack;
                    StruckSound = SoundIndex.SheepStruck;
                    DieSound = SoundIndex.SheepDie;
                    break;
                case MonsterImage.SkyStinger:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_6, out BodyLibrary);
                    BodyShape = 9;
                    AttackSound = SoundIndex.SkyStingerAttack;
                    StruckSound = SoundIndex.SkyStingerStruck;
                    DieSound = SoundIndex.SkyStingerDie;
                    break;
                case MonsterImage.ClawCat:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_4, out BodyLibrary);
                    BodyShape = 8;
                    AttackSound = SoundIndex.ClawCatAttack;
                    StruckSound = SoundIndex.ClawCatStruck;
                    DieSound = SoundIndex.ClawCatDie;
                    break;
                case MonsterImage.RakingCat:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_28, out BodyLibrary);
                    BodyShape = 6;
                    AttackSound = SoundIndex.ClawCatAttack;
                    StruckSound = SoundIndex.ClawCatStruck;
                    DieSound = SoundIndex.ClawCatDie;
                    break;
                case MonsterImage.Wolf:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_7, out BodyLibrary);
                    BodyShape = 5;
                    AttackSound = SoundIndex.WolfAttack;
                    StruckSound = SoundIndex.WolfStruck;
                    DieSound = SoundIndex.WolfDie;
                    break;
                case MonsterImage.ForestYeti:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_4, out BodyLibrary);
                    BodyShape = 0;
                    AttackSound = SoundIndex.ForestYetiAttack;
                    StruckSound = SoundIndex.ForestYetiStruck;
                    DieSound = SoundIndex.ForestYetiDie;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.ForestYeti)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.ChestnutTree:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_13, out BodyLibrary);
                    BodyShape = 7;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.ChestnutTree)
                        Frames[frame.Key] = frame.Value;

                    break;
                case MonsterImage.CarnivorousPlant:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_4, out BodyLibrary);
                    BodyShape = 1;
                    AttackSound = SoundIndex.CarnivorousPlantAttack;
                    StruckSound = SoundIndex.CarnivorousPlantStruck;
                    DieSound = SoundIndex.CarnivorousPlantDie;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.CarnivorousPlant)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.Oma:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_3, out BodyLibrary);
                    BodyShape = 3;
                    AttackSound = SoundIndex.OmaAttack;
                    StruckSound = SoundIndex.OmaStruck;
                    DieSound = SoundIndex.OmaDie;
                    break;
                case MonsterImage.OmaWar:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_28, out BodyLibrary);
                    BodyShape = 4;
                    AttackSound = SoundIndex.OmaAttack;
                    StruckSound = SoundIndex.OmaStruck;
                    DieSound = SoundIndex.OmaDie;
                    break;
                case MonsterImage.OmaInfant:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_28, out BodyLibrary);
                    BodyShape = 4;
                    AttackSound = SoundIndex.OmaAttack;
                    StruckSound = SoundIndex.OmaStruck;
                    DieSound = SoundIndex.OmaDie;
                    break;
                case MonsterImage.Yob:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_28, out BodyLibrary);
                    BodyShape = 5;
                    AttackSound = SoundIndex.YobAttack;
                    StruckSound = SoundIndex.YobStruck;
                    DieSound = SoundIndex.YobDie;
                    break;
                case MonsterImage.TigerSnake:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_6, out BodyLibrary);
                    BodyShape = 7;
                    AttackSound = SoundIndex.TigerSnakeAttack;
                    StruckSound = SoundIndex.TigerSnakeStruck;
                    DieSound = SoundIndex.TigerSnakeDie;
                    break;
                case MonsterImage.RedSnake:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_6, out BodyLibrary);
                    BodyShape = 4;
                    AttackSound = SoundIndex.TigerSnakeAttack;
                    StruckSound = SoundIndex.TigerSnakeStruck;
                    DieSound = SoundIndex.TigerSnakeDie;
                    break;
                case MonsterImage.SpittingSpider:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_3, out BodyLibrary);
                    BodyShape = 5;
                    AttackSound = SoundIndex.SpittingSpiderAttack;
                    StruckSound = SoundIndex.SpittingSpiderStruck;
                    DieSound = SoundIndex.SpittingSpiderDie;

                    break;
                case MonsterImage.Scarecrow:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_5, out BodyLibrary);
                    BodyShape = 0;
                    AttackSound = SoundIndex.ScarecrowAttack;
                    StruckSound = SoundIndex.ScarecrowStruck;
                    DieSound = SoundIndex.ScarecrowDie;
                    break;
                case MonsterImage.OmaHero:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_3, out BodyLibrary);
                    BodyShape = 4;
                    AttackSound = SoundIndex.OmaHeroAttack;
                    StruckSound = SoundIndex.OmaHeroStruck;
                    DieSound = SoundIndex.OmaHeroDie;
                    break;
                case MonsterImage.Guard:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_3, out BodyLibrary);
                    BodyShape = 6;
                    break;
                case MonsterImage.ForestGuard:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_12, out BodyLibrary);
                    BodyShape = 3;
                    break;
                case MonsterImage.TownGuard:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_12, out BodyLibrary);
                    BodyShape = 4;
                    break;
                case MonsterImage.SandGuard:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_12, out BodyLibrary);
                    BodyShape = 5;
                    break;
                case MonsterImage.CaveBat:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_3, out BodyLibrary);
                    BodyShape = 9;
                    AttackSound = SoundIndex.CaveBatAttack;
                    StruckSound = SoundIndex.CaveBatStruck;
                    DieSound = SoundIndex.CaveBatDie;
                    break;
                case MonsterImage.Scorpion:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_3, out BodyLibrary);
                    BodyShape = 8;
                    AttackSound = SoundIndex.ScorpionAttack;
                    StruckSound = SoundIndex.ScorpionStruck;
                    DieSound = SoundIndex.ScorpionDie;
                    break;
                case MonsterImage.Skeleton:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_4, out BodyLibrary);
                    BodyShape = 2;
                    AttackSound = SoundIndex.SkeletonAttack;
                    StruckSound = SoundIndex.SkeletonStruck;
                    DieSound = SoundIndex.SkeletonDie;
                    break;
                case MonsterImage.SkeletonAxeMan:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_4, out BodyLibrary);
                    BodyShape = 4;
                    AttackSound = SoundIndex.SkeletonAxeManAttack;
                    StruckSound = SoundIndex.SkeletonAxeManStruck;
                    DieSound = SoundIndex.SkeletonAxeManDie;
                    break;
                case MonsterImage.SkeletonAxeThrower:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_4, out BodyLibrary);
                    BodyShape = 3;
                    AttackSound = SoundIndex.SkeletonAxeThrowerAttack;
                    StruckSound = SoundIndex.SkeletonAxeThrowerStruck;
                    DieSound = SoundIndex.SkeletonAxeThrowerDie;
                    break;
                case MonsterImage.SkeletonWarrior:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_4, out BodyLibrary);
                    BodyShape = 5;
                    AttackSound = SoundIndex.SkeletonWarriorAttack;
                    StruckSound = SoundIndex.SkeletonWarriorStruck;
                    DieSound = SoundIndex.SkeletonWarriorDie;
                    break;
                case MonsterImage.SkeletonLord:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_4, out BodyLibrary);
                    BodyShape = 6;
                    AttackSound = SoundIndex.SkeletonLordAttack;
                    StruckSound = SoundIndex.SkeletonLordStruck;
                    DieSound = SoundIndex.SkeletonLordDie;
                    break;
                case MonsterImage.CaveMaggot:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_4, out BodyLibrary);
                    BodyShape = 7;
                    AttackSound = SoundIndex.CaveMaggotAttack;
                    StruckSound = SoundIndex.CaveMaggotStruck;
                    DieSound = SoundIndex.CaveMaggotDie;

                    break;
                case MonsterImage.GhostSorcerer:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_5, out BodyLibrary);
                    BodyShape = 8;
                    AttackSound = SoundIndex.GhostSorcererAttack;
                    StruckSound = SoundIndex.GhostSorcererStruck;
                    DieSound = SoundIndex.GhostSorcererDie;
                    break;
                case MonsterImage.GhostMage:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_5, out BodyLibrary);
                    BodyShape = 9;
                    AttackSound = SoundIndex.GhostMageAttack;
                    StruckSound = SoundIndex.GhostMageStruck;
                    DieSound = SoundIndex.GhostMageDie; //Appear SOUND ?
                    break;
                case MonsterImage.VoraciousGhost:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_6, out BodyLibrary);
                    BodyShape = 0;
                    AttackSound = SoundIndex.VoraciousGhostAttack;
                    StruckSound = SoundIndex.VoraciousGhostStruck;
                    DieSound = SoundIndex.VoraciousGhostDie;
                    break;
                case MonsterImage.DevouringGhost:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_6, out BodyLibrary);
                    BodyShape = 1;
                    AttackSound = SoundIndex.VoraciousGhostAttack;
                    StruckSound = SoundIndex.VoraciousGhostStruck;
                    DieSound = SoundIndex.VoraciousGhostDie;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.DevouringGhost)
                        Frames[frame.Key] = frame.Value;


                    break;
                case MonsterImage.CorpseRaisingGhost:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_6, out BodyLibrary);
                    BodyShape = 2;
                    AttackSound = SoundIndex.VoraciousGhostAttack;
                    StruckSound = SoundIndex.VoraciousGhostStruck;
                    DieSound = SoundIndex.VoraciousGhostDie;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.DevouringGhost)
                        Frames[frame.Key] = frame.Value;

                    break;
                case MonsterImage.GhoulChampion:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_6, out BodyLibrary);
                    BodyShape = 3;
                    AttackSound = SoundIndex.GhoulChampionAttack;
                    StruckSound = SoundIndex.GhoulChampionStruck;
                    DieSound = SoundIndex.GhoulChampionDie;
                    break;
                case MonsterImage.ArmoredAnt:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_1, out BodyLibrary);
                    BodyShape = 8;
                    AttackSound = SoundIndex.ArmoredAntAttack;
                    StruckSound = SoundIndex.ArmoredAntStruck;
                    DieSound = SoundIndex.ArmoredAntDie;
                    break;
                case MonsterImage.AntSoldier:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_2, out BodyLibrary);
                    BodyShape = 4;
                    AttackSound = SoundIndex.ArmoredAntAttack;
                    StruckSound = SoundIndex.ArmoredAntStruck;
                    DieSound = SoundIndex.ArmoredAntDie;
                    break;
                case MonsterImage.AntHealer:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_1, out BodyLibrary);
                    BodyShape = 7;
                    AttackSound = SoundIndex.ArmoredAntAttack;
                    StruckSound = SoundIndex.ArmoredAntStruck;
                    DieSound = SoundIndex.ArmoredAntDie;
                    break;
                case MonsterImage.AntNeedler:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_10, out BodyLibrary);
                    BodyShape = 6;
                    AttackSound = SoundIndex.AntNeedlerAttack;
                    StruckSound = SoundIndex.AntNeedlerStruck;
                    DieSound = SoundIndex.AntNeedlerDie;
                    break;
                case MonsterImage.Beetle:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_7, out BodyLibrary);
                    BodyShape = 3;
                    AttackSound = SoundIndex.KeratoidAttack;
                    StruckSound = SoundIndex.KeratoidStruck;
                    DieSound = SoundIndex.KeratoidDie;
                    break;
                case MonsterImage.ShellNipper:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_7, out BodyLibrary);
                    BodyShape = 0;
                    AttackSound = SoundIndex.ShellNipperAttack;
                    StruckSound = SoundIndex.ShellNipperStruck;
                    DieSound = SoundIndex.ShellNipperDie;
                    break;
                case MonsterImage.VisceralWorm:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_7, out BodyLibrary);
                    BodyShape = 1;
                    AttackSound = SoundIndex.VisceralWormAttack;
                    StruckSound = SoundIndex.VisceralWormStruck;
                    DieSound = SoundIndex.VisceralWormDie;
                    break;
                case MonsterImage.MutantFlea:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_15, out BodyLibrary);
                    BodyShape = 5;
                    AttackSound = SoundIndex.MutantFleaAttack;
                    StruckSound = SoundIndex.MutantFleaStruck;
                    DieSound = SoundIndex.MutantFleaDie;
                    break;
                case MonsterImage.PoisonousMutantFlea:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_15, out BodyLibrary);
                    BodyShape = 9;
                    AttackSound = SoundIndex.PoisonousMutantFleaAttack;
                    StruckSound = SoundIndex.PoisonousMutantFleaStruck;
                    DieSound = SoundIndex.PoisonousMutantFleaDie;
                    break;
                case MonsterImage.BlasterMutantFlea:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_15, out BodyLibrary);
                    BodyShape = 7;
                    AttackSound = SoundIndex.BlasterMutantFleaAttack;
                    StruckSound = SoundIndex.BlasterMutantFleaStruck;
                    DieSound = SoundIndex.BlasterMutantFleaDie;
                    break;

                case MonsterImage.WaspHatchling:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_8, out BodyLibrary);
                    BodyShape = 1;
                    AttackSound = SoundIndex.WasHatchlingAttack;
                    StruckSound = SoundIndex.WasHatchlingStruck;
                    DieSound = SoundIndex.WasHatchlingDie;
                    break;

                case MonsterImage.Centipede:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_7, out BodyLibrary);
                    BodyShape = 6;
                    AttackSound = SoundIndex.CentipedeAttack;
                    StruckSound = SoundIndex.CentipedeStruck;
                    DieSound = SoundIndex.CentipedeDie;
                    break;

                case MonsterImage.ButterflyWorm:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_8, out BodyLibrary);
                    BodyShape = 2;
                    AttackSound = SoundIndex.ButterflyWormAttack;
                    StruckSound = SoundIndex.ButterflyWormStruck;
                    DieSound = SoundIndex.ButterflyWormDie;
                    break;

                case MonsterImage.MutantMaggot:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_7, out BodyLibrary);
                    BodyShape = 8;
                    AttackSound = SoundIndex.MutantMaggotAttack;
                    StruckSound = SoundIndex.MutantMaggotStruck;
                    DieSound = SoundIndex.MutantMaggotDie;
                    break;

                case MonsterImage.Earwig:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_7, out BodyLibrary);
                    BodyShape = 9;
                    AttackSound = SoundIndex.EarwigAttack;
                    StruckSound = SoundIndex.EarwigStruck;
                    DieSound = SoundIndex.EarwigDie;
                    break;

                case MonsterImage.IronLance:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_8, out BodyLibrary);
                    BodyShape = 0;
                    AttackSound = SoundIndex.IronLanceAttack;
                    StruckSound = SoundIndex.IronLanceStruck;
                    DieSound = SoundIndex.IronLanceDie;
                    break;

                case MonsterImage.LordNiJae:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_7, out BodyLibrary);
                    BodyShape = 7;
                    AttackSound = SoundIndex.LordNiJaeAttack;
                    StruckSound = SoundIndex.LordNiJaeStruck;
                    DieSound = SoundIndex.LordNiJaeDie;
                    break;

                case MonsterImage.RottingGhoul:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_14, out BodyLibrary);
                    BodyShape = 8;
                    AttackSound = SoundIndex.RottingGhoulAttack;
                    StruckSound = SoundIndex.RottingGhoulStruck;
                    DieSound = SoundIndex.RottingGhoulDie;
                    break;

                case MonsterImage.DecayingGhoul:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_14, out BodyLibrary);
                    BodyShape = 2;
                    AttackSound = SoundIndex.DecayingGhoulAttack;
                    StruckSound = SoundIndex.DecayingGhoulStruck;
                    DieSound = SoundIndex.DecayingGhoulDie;
                    break;

                case MonsterImage.BloodThirstyGhoul:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_5, out BodyLibrary);
                    BodyShape = 2;
                    AttackSound = SoundIndex.BloodThirstyGhoulAttack;
                    StruckSound = SoundIndex.BloodThirstyGhoulStruck;
                    DieSound = SoundIndex.BloodThirstyGhoulDie;
                    break;

                case MonsterImage.SpinedDarkLizard:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_5, out BodyLibrary);
                    BodyShape = 6;
                    AttackSound = SoundIndex.SpinedDarkLizardAttack;
                    StruckSound = SoundIndex.SpinedDarkLizardStruck;
                    DieSound = SoundIndex.SpinedDarkLizardDie;
                    break;
                case MonsterImage.Dung:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_5, out BodyLibrary);
                    BodyShape = 7;
                    AttackSound = SoundIndex.DungAttack;
                    StruckSound = SoundIndex.DungStruck;
                    DieSound = SoundIndex.DungDie;
                    break;
                case MonsterImage.UmaInfidel:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_5, out BodyLibrary);
                    BodyShape = 1;
                    AttackSound = SoundIndex.UmaInfidelAttack;
                    StruckSound = SoundIndex.UmaInfidelStruck;
                    DieSound = SoundIndex.UmaInfidelDie;
                    break;
                case MonsterImage.UmaFlameThrower:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_5, out BodyLibrary);
                    BodyShape = 3;
                    AttackSound = SoundIndex.UmaFlameThrowerAttack;
                    StruckSound = SoundIndex.UmaFlameThrowerStruck;
                    DieSound = SoundIndex.UmaFlameThrowerDie;
                    break;
                case MonsterImage.UmaMaceInfidel:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_10, out BodyLibrary);
                    BodyShape = 1;
                    AttackSound = SoundIndex.UmaInfidelAttack; //TODO
                    StruckSound = SoundIndex.UmaInfidelStruck;
                    DieSound = SoundIndex.UmaInfidelDie;
                    break;
                case MonsterImage.UmaTridentInfidel:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_28, out BodyLibrary);
                    BodyShape = 7;
                    AttackSound = SoundIndex.UmaInfidelAttack; //TODO
                    StruckSound = SoundIndex.UmaInfidelStruck;
                    DieSound = SoundIndex.UmaInfidelDie;
                    break;
                case MonsterImage.UmaAnguisher:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_5, out BodyLibrary);
                    BodyShape = 4;
                    AttackSound = SoundIndex.UmaAnguisherAttack;
                    StruckSound = SoundIndex.UmaAnguisherStruck;
                    DieSound = SoundIndex.UmaAnguisherDie;
                    break;
                case MonsterImage.UmaKing:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_5, out BodyLibrary);
                    BodyShape = 5;
                    AttackSound = SoundIndex.UmaKingAttack;
                    StruckSound = SoundIndex.UmaKingStruck;
                    DieSound = SoundIndex.UmaKingDie;
                    break;

                case MonsterImage.SpiderBat:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_11, out BodyLibrary);
                    BodyShape = 1;
                    AttackSound = SoundIndex.SpiderBatAttack;
                    StruckSound = SoundIndex.SpiderBatStruck;
                    DieSound = SoundIndex.SpiderBatDie;
                    break;
                case MonsterImage.ArachnidGazer:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_11, out BodyLibrary);
                    BodyShape = 6;
                    StruckSound = SoundIndex.ArachnidGazerStruck;
                    DieSound = SoundIndex.ArachnidGazerDie;
                    break;

                case MonsterImage.Larva:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_11, out BodyLibrary);
                    BodyShape = 5;
                    AttackSound = SoundIndex.LarvaAttack;
                    StruckSound = SoundIndex.LarvaStruck;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.Larva)
                        Frames[frame.Key] = frame.Value;
                    break;

                case MonsterImage.RedMoonGuardian:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_11, out BodyLibrary);
                    BodyShape = 7;
                    AttackSound = SoundIndex.RedMoonGuardianAttack;
                    StruckSound = SoundIndex.RedMoonGuardianStruck;
                    DieSound = SoundIndex.RedMoonGuardianDie;
                    break;
                case MonsterImage.RedMoonProtector:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_11, out BodyLibrary);
                    BodyShape = 8;
                    AttackSound = SoundIndex.RedMoonProtectorAttack;
                    StruckSound = SoundIndex.RedMoonProtectorStruck;
                    DieSound = SoundIndex.RedMoonProtectorDie;
                    break;
                case MonsterImage.RedMoonRedProtector:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_11, out BodyLibrary);
                    BodyShape = 9;
                    //TODO - Needs sound
                    break;
                case MonsterImage.RedMoonGrayProtector:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_12, out BodyLibrary);
                    BodyShape = 0;
                    //TODO - Needs sound
                    break;
                case MonsterImage.VenomousArachnid:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_12, out BodyLibrary);
                    BodyShape = 1;
                    AttackSound = SoundIndex.VenomousArachnidAttack;
                    StruckSound = SoundIndex.VenomousArachnidStruck;
                    DieSound = SoundIndex.VenomousArachnidDie;
                    break;
                case MonsterImage.DarkArachnid:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_12, out BodyLibrary);
                    BodyShape = 2;
                    AttackSound = SoundIndex.DarkArachnidAttack;
                    StruckSound = SoundIndex.DarkArachnidStruck;
                    DieSound = SoundIndex.DarkArachnidDie;
                    break;
                case MonsterImage.RedMoonTheFallen:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_11, out BodyLibrary);
                    BodyShape = 4;
                    AttackSound = SoundIndex.RedMoonTheFallenAttack;
                    StruckSound = SoundIndex.RedMoonTheFallenStruck;
                    DieSound = SoundIndex.RedMoonTheFallenDie;
                    break;

                case MonsterImage.ViciousRat:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_9, out BodyLibrary);
                    BodyShape = 1;
                    AttackSound = SoundIndex.ViciousRatAttack;
                    StruckSound = SoundIndex.ViciousRatStruck;
                    DieSound = SoundIndex.ViciousRatDie;
                    break;

                case MonsterImage.ZumaSharpShooter:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_9, out BodyLibrary);
                    BodyShape = 2;
                    AttackSound = SoundIndex.ZumaSharpShooterAttack;
                    StruckSound = SoundIndex.ZumaSharpShooterStruck;
                    DieSound = SoundIndex.ZumaSharpShooterDie;
                    break;

                case MonsterImage.ZumaFanatic:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_9, out BodyLibrary);
                    BodyShape = 3;
                    AttackSound = SoundIndex.ZumaFanaticAttack;
                    StruckSound = SoundIndex.ZumaFanaticStruck;
                    DieSound = SoundIndex.ZumaFanaticDie;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.ZumaGuardian)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.ZumaGuardian:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_9, out BodyLibrary);
                    BodyShape = 4;
                    AttackSound = SoundIndex.ZumaGuardianAttack;
                    StruckSound = SoundIndex.ZumaGuardianStruck;
                    DieSound = SoundIndex.ZumaGuardianDie;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.ZumaGuardian)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.ZumaKing:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_9, out BodyLibrary);
                    BodyShape = 5;
                    AttackSound = SoundIndex.ZumaKingAttack;
                    StruckSound = SoundIndex.ZumaKingStruck;
                    DieSound = SoundIndex.ZumaKingDie;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.ZumaKing)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.ArcherGuard:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_9, out BodyLibrary);
                    BodyShape = 6;

                    break;
                case MonsterImage.EvilFanatic:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_16, out BodyLibrary);
                    BodyShape = 7;
                    AttackSound = SoundIndex.EvilFanaticAttack;
                    StruckSound = SoundIndex.EvilFanaticStruck;
                    DieSound = SoundIndex.EvilFanaticDie;
                    break;
                case MonsterImage.Monkey:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_16, out BodyLibrary);
                    BodyShape = 4;
                    AttackSound = SoundIndex.MonkeyAttack;
                    StruckSound = SoundIndex.MonkeyStruck;
                    DieSound = SoundIndex.MonkeyDie;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.Monkey)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.EvilElephant:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_16, out BodyLibrary);
                    BodyShape = 8;
                    AttackSound = SoundIndex.EvilElephantAttack;
                    StruckSound = SoundIndex.EvilElephantStruck;
                    DieSound = SoundIndex.EvilElephantDie;
                    break;
                case MonsterImage.CannibalFanatic:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_16, out BodyLibrary);
                    BodyShape = 6;
                    AttackSound = SoundIndex.CannibalFanaticAttack;
                    StruckSound = SoundIndex.CannibalFanaticStruck;
                    DieSound = SoundIndex.CannibalFanaticDie;
                    break;

                case MonsterImage.SpikedBeetle:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_7, out BodyLibrary);
                    BodyShape = 4;
                    AttackSound = SoundIndex.SpikedBeetleAttack;
                    StruckSound = SoundIndex.SpikedBeetleStruck;
                    DieSound = SoundIndex.SpikedBeetleDie;
                    break;
                case MonsterImage.NumaGrunt:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_13, out BodyLibrary);
                    BodyShape = 8;
                    AttackSound = SoundIndex.NumaGruntAttack;
                    StruckSound = SoundIndex.NumaGruntStruck;
                    DieSound = SoundIndex.NumaGruntDie;
                    break;
                case MonsterImage.NumaWarrior:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_13, out BodyLibrary);
                    BodyShape = 9;
                    AttackSound = SoundIndex.NumaGruntAttack;
                    StruckSound = SoundIndex.NumaGruntStruck;
                    DieSound = SoundIndex.NumaGruntDie;
                    break;
                case MonsterImage.NumaMage:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_2, out BodyLibrary);
                    BodyShape = 3;
                    AttackSound = SoundIndex.NumaMageAttack;
                    StruckSound = SoundIndex.NumaMageStruck;
                    DieSound = SoundIndex.NumaMageDie;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.NumaMage)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.NumaElite:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_2, out BodyLibrary);
                    BodyShape = 7;
                    AttackSound = SoundIndex.NumaEliteAttack;
                    StruckSound = SoundIndex.NumaEliteStruck;
                    DieSound = SoundIndex.NumaEliteDie;
                    break;
                case MonsterImage.Phantom:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_2, out BodyLibrary);
                    BodyShape = 8;
                    //TODO - Add sounds
                    break;
                case MonsterImage.SandShark:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_10, out BodyLibrary);
                    BodyShape = 4;
                    AttackSound = SoundIndex.SandSharkAttack;
                    StruckSound = SoundIndex.SandSharkStruck;
                    DieSound = SoundIndex.SandSharkDie;
                    break;
                case MonsterImage.StoneGolem:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_1, out BodyLibrary);
                    BodyShape = 4;
                    AttackSound = SoundIndex.StoneGolemAttack;
                    StruckSound = SoundIndex.StoneGolemStruck;
                    DieSound = SoundIndex.StoneGolemDie;
                    break;
                case MonsterImage.WindfurySorceress:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_10, out BodyLibrary);
                    BodyShape = 7;
                    AttackSound = SoundIndex.WindfurySorceressAttack;
                    StruckSound = SoundIndex.WindfurySorceressStruck;
                    DieSound = SoundIndex.WindfurySorceressDie;
                    break;
                case MonsterImage.CursedCactus:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_10, out BodyLibrary);
                    BodyShape = 5;
                    AttackSound = SoundIndex.CursedCactusAttack;
                    StruckSound = SoundIndex.CursedCactusStruck;
                    DieSound = SoundIndex.CursedCactusDie;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.CursedCactus)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.NetherWorldGate:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_1, out BodyLibrary);
                    BodyShape = 5;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.NetherWorldGate)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.RagingLizard:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_20, out BodyLibrary);
                    BodyShape = 1;
                    AttackSound = SoundIndex.RagingLizardAttack;
                    StruckSound = SoundIndex.RagingLizardStruck;
                    DieSound = SoundIndex.RagingLizardDie;
                    break;

                case MonsterImage.SawToothLizard:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_20, out BodyLibrary);
                    BodyShape = 2;
                    AttackSound = SoundIndex.SawToothLizardAttack;
                    StruckSound = SoundIndex.SawToothLizardStruck;
                    DieSound = SoundIndex.SawToothLizardDie;
                    break;
                case MonsterImage.MutantLizard:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_20, out BodyLibrary);
                    BodyShape = 3;
                    AttackSound = SoundIndex.MutantLizardAttack;
                    StruckSound = SoundIndex.MutantLizardStruck;
                    DieSound = SoundIndex.MutantLizardDie;
                    break;
                case MonsterImage.VenomSpitter:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_20, out BodyLibrary);
                    BodyShape = 4;
                    AttackSound = SoundIndex.VenomSpitterAttack;
                    StruckSound = SoundIndex.VenomSpitterStruck;
                    DieSound = SoundIndex.VenomSpitterDie;
                    break;
                case MonsterImage.SonicLizard:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_20, out BodyLibrary);
                    BodyShape = 5;
                    AttackSound = SoundIndex.SonicLizardAttack;
                    StruckSound = SoundIndex.SonicLizardStruck;
                    DieSound = SoundIndex.SonicLizardDie;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.WestDesertLizard)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.GiantLizard:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_20, out BodyLibrary);
                    BodyShape = 6;
                    AttackSound = SoundIndex.GiantLizardAttack;
                    StruckSound = SoundIndex.GiantLizardStruck;
                    DieSound = SoundIndex.GiantLizardDie;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.WestDesertLizard)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.CrazedLizard:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_20, out BodyLibrary);
                    BodyShape = 9;
                    AttackSound = SoundIndex.CrazedLizardAttack;
                    StruckSound = SoundIndex.CrazedLizardStruck;
                    DieSound = SoundIndex.CrazedLizardDie;
                    break;
                case MonsterImage.TaintedTerror:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_20, out BodyLibrary);
                    BodyShape = 7;
                    AttackSound = SoundIndex.TaintedTerrorAttack;
                    StruckSound = SoundIndex.TaintedTerrorStruck;
                    DieSound = SoundIndex.TaintedTerrorDie;


                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.WestDesertLizard)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.DeathLordJichon:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_20, out BodyLibrary);
                    BodyShape = 8;
                    AttackSound = SoundIndex.DeathLordJichonAttack;
                    StruckSound = SoundIndex.DeathLordJichonStruck;
                    DieSound = SoundIndex.DeathLordJichonDie;
                    break;

                case MonsterImage.Minotaur:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_14, out BodyLibrary);
                    BodyShape = 7;
                    AttackSound = SoundIndex.MinotaurAttack;
                    StruckSound = SoundIndex.MinotaurStruck;
                    DieSound = SoundIndex.MinotaurDie;
                    break;
                case MonsterImage.FrostMinotaur:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_14, out BodyLibrary);
                    BodyShape = 3;
                    AttackSound = SoundIndex.FrostMinotaurAttack;
                    StruckSound = SoundIndex.FrostMinotaurStruck;
                    DieSound = SoundIndex.FrostMinotaurDie;
                    break;
                case MonsterImage.ShockMinotaur:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_14, out BodyLibrary);
                    BodyShape = 4;
                    AttackSound = SoundIndex.FrostMinotaurAttack;
                    StruckSound = SoundIndex.FrostMinotaurStruck;
                    DieSound = SoundIndex.FrostMinotaurDie;
                    break;
                case MonsterImage.FlameMinotaur:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_14, out BodyLibrary);
                    BodyShape = 6;
                    AttackSound = SoundIndex.FrostMinotaurAttack;
                    StruckSound = SoundIndex.FrostMinotaurStruck;
                    DieSound = SoundIndex.FrostMinotaurDie;
                    break;
                case MonsterImage.FuryMinotaur:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_14, out BodyLibrary);
                    BodyShape = 5;
                    AttackSound = SoundIndex.FrostMinotaurAttack;
                    StruckSound = SoundIndex.FrostMinotaurStruck;
                    DieSound = SoundIndex.FrostMinotaurDie;
                    break;
                case MonsterImage.BanyaLeftGuard:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_14, out BodyLibrary);
                    BodyShape = 1;
                    AttackSound = SoundIndex.BanyaLeftGuardAttack;
                    StruckSound = SoundIndex.BanyaLeftGuardStruck;
                    DieSound = SoundIndex.BanyaLeftGuardDie;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.BanyaGuard)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.BanyaRightGuard:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_14, out BodyLibrary);
                    BodyShape = 0;
                    AttackSound = SoundIndex.BanyaLeftGuardAttack;
                    StruckSound = SoundIndex.BanyaLeftGuardStruck;
                    DieSound = SoundIndex.BanyaLeftGuardDie;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.BanyaGuard)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.EmperorSaWoo:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_14, out BodyLibrary);
                    BodyShape = 9;
                    AttackSound = SoundIndex.EmperorSaWooAttack;
                    StruckSound = SoundIndex.EmperorSaWooStruck;
                    DieSound = SoundIndex.EmperorSaWooDie;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.EmperorSaWoo)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.BoneArcher:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_15, out BodyLibrary);
                    BodyShape = 4;
                    AttackSound = SoundIndex.BoneArcherAttack;
                    StruckSound = SoundIndex.BoneArcherStruck;
                    DieSound = SoundIndex.BoneArcherDie;
                    break;
                case MonsterImage.BoneBladesman:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_15, out BodyLibrary);
                    BodyShape = 3;
                    AttackSound = SoundIndex.BoneArcherAttack;
                    StruckSound = SoundIndex.BoneArcherStruck;
                    DieSound = SoundIndex.BoneArcherDie;
                    break;
                case MonsterImage.BoneCaptain:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_15, out BodyLibrary);
                    BodyShape = 0;
                    AttackSound = SoundIndex.BoneCaptainAttack;
                    StruckSound = SoundIndex.BoneCaptainStruck;
                    DieSound = SoundIndex.BoneCaptainDie;
                    break;
                case MonsterImage.BoneSoldier:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_15, out BodyLibrary);
                    BodyShape = 2;
                    AttackSound = SoundIndex.BoneArcherAttack;
                    StruckSound = SoundIndex.BoneArcherStruck;
                    DieSound = SoundIndex.BoneArcherDie;
                    break;
                case MonsterImage.ArchLichTaedu:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_15, out BodyLibrary);
                    BodyShape = 1;
                    AttackSound = SoundIndex.ArchLichTaeduAttack;
                    StruckSound = SoundIndex.ArchLichTaeduStruck;
                    DieSound = SoundIndex.ArchLichTaeduDie;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.ArchLichTaeda)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.WedgeMothLarva:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_8, out BodyLibrary);
                    BodyShape = 3;
                    AttackSound = SoundIndex.WedgeMothLarvaAttack;
                    StruckSound = SoundIndex.WedgeMothLarvaStruck;
                    DieSound = SoundIndex.WedgeMothLarvaDie;
                    break;
                case MonsterImage.LesserWedgeMoth:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_8, out BodyLibrary);
                    BodyShape = 4;
                    AttackSound = SoundIndex.LesserWedgeMothAttack;
                    StruckSound = SoundIndex.LesserWedgeMothStruck;
                    DieSound = SoundIndex.LesserWedgeMothDie;
                    break;
                case MonsterImage.WedgeMoth:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_8, out BodyLibrary);
                    BodyShape = 5;
                    AttackSound = SoundIndex.WedgeMothAttack;
                    StruckSound = SoundIndex.WedgeMothStruck;
                    DieSound = SoundIndex.WedgeMothDie;
                    break;
                case MonsterImage.RedBoar:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_8, out BodyLibrary);
                    BodyShape = 6;
                    AttackSound = SoundIndex.RedBoarAttack;
                    StruckSound = SoundIndex.RedBoarStruck;
                    DieSound = SoundIndex.RedBoarDie;
                    break;
                case MonsterImage.ClawSerpent:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_8, out BodyLibrary);
                    BodyShape = 9;
                    AttackSound = SoundIndex.ClawSerpentAttack;
                    StruckSound = SoundIndex.ClawSerpentStruck;
                    DieSound = SoundIndex.ClawSerpentDie;
                    break;
                case MonsterImage.BlackBoar:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_8, out BodyLibrary);
                    BodyShape = 7;
                    AttackSound = SoundIndex.BlackBoarAttack;
                    StruckSound = SoundIndex.BlackBoarStruck;
                    DieSound = SoundIndex.BlackBoarDie;
                    break;
                case MonsterImage.TuskLord:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_8, out BodyLibrary);
                    BodyShape = 8;
                    AttackSound = SoundIndex.TuskLordAttack;
                    StruckSound = SoundIndex.TuskLordStruck;
                    DieSound = SoundIndex.TuskLordDie;
                    break;
                case MonsterImage.RazorTusk:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_16, out BodyLibrary);
                    BodyShape = 0;
                    AttackSound = SoundIndex.RazorTuskAttack;
                    StruckSound = SoundIndex.RazorTuskStruck;
                    DieSound = SoundIndex.RazorTuskDie;
                    break;


                case MonsterImage.PinkGoddess:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_17, out BodyLibrary);
                    BodyShape = 2;
                    AttackSound = SoundIndex.PinkGoddessAttack;
                    StruckSound = SoundIndex.PinkGoddessStruck;
                    DieSound = SoundIndex.PinkGoddessDie;
                    break;
                case MonsterImage.GreenGoddess:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_17, out BodyLibrary);
                    BodyShape = 3;
                    AttackSound = SoundIndex.GreenGoddessAttack;
                    StruckSound = SoundIndex.GreenGoddessStruck;
                    DieSound = SoundIndex.GreenGoddessDie;
                    break;
                case MonsterImage.MutantCaptain:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_17, out BodyLibrary);
                    BodyShape = 1;
                    AttackSound = SoundIndex.MutantCaptainAttack;
                    StruckSound = SoundIndex.MutantCaptainStruck;
                    DieSound = SoundIndex.MutantCaptainDie;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.WestDesertLizard)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.StoneGriffin:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_17, out BodyLibrary);
                    BodyShape = 0;
                    AttackSound = SoundIndex.StoneGriffinAttack;
                    StruckSound = SoundIndex.StoneGriffinStruck;
                    DieSound = SoundIndex.StoneGriffinDie;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.BanyaGuard)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.FlameGriffin:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_16, out BodyLibrary);
                    BodyShape = 9;
                    AttackSound = SoundIndex.FlameGriffinAttack;
                    StruckSound = SoundIndex.FlameGriffinStruck;
                    DieSound = SoundIndex.FlameGriffinDie;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.BanyaGuard)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.JinchonDevil:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_17, out BodyLibrary);
                    BodyShape = 4;
                    //AttackSound = SoundIndex.JinchonDevilAttack;
                    StruckSound = SoundIndex.JinchonDevilStruck;
                    DieSound = SoundIndex.JinchonDevilDie;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.JinchonDevil)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.WhiteBone:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_6, out BodyLibrary);
                    BodyShape = 6;
                    AttackSound = SoundIndex.WhiteBoneAttack;
                    StruckSound = SoundIndex.WhiteBoneStruck;
                    DieSound = SoundIndex.WhiteBoneDie;
                    break;
                case MonsterImage.Shinsu:
                    if (Extra)
                    {
                        CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_10, out BodyLibrary);
                        BodyShape = 0;
                        AttackSound = SoundIndex.ShinsuBigAttack;
                        StruckSound = SoundIndex.ShinsuBigStruck;
                        DieSound = SoundIndex.ShinsuBigDie;
                    }
                    else
                    {
                        CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_9, out BodyLibrary);
                        BodyShape = 9;

                        AttackSound = SoundIndex.None;
                        StruckSound = SoundIndex.ShinsuSmallStruck;
                        DieSound = SoundIndex.ShinsuSmallDie;
                    }
                    break;

                case MonsterImage.CorpseStalker:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_2, out BodyLibrary);
                    BodyShape = 2;
                    AttackSound = SoundIndex.CorpseStalkerAttack;
                    StruckSound = SoundIndex.CorpseStalkerStruck;
                    DieSound = SoundIndex.CorpseStalkerDie;
                    break;
                case MonsterImage.LightArmedSoldier:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_1, out BodyLibrary);
                    BodyShape = 6;
                    AttackSound = SoundIndex.LightArmedSoldierAttack;
                    StruckSound = SoundIndex.LightArmedSoldierStruck;
                    DieSound = SoundIndex.LightArmedSoldierDie;
                    break;
                case MonsterImage.CorrosivePoisonSpitter:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_10, out BodyLibrary);
                    BodyShape = 3;
                    AttackSound = SoundIndex.CorrosivePoisonSpitterAttack;
                    StruckSound = SoundIndex.CorrosivePoisonSpitterStruck;
                    DieSound = SoundIndex.CorrosivePoisonSpitterDie;
                    break;
                case MonsterImage.PhantomSoldier:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_10, out BodyLibrary);
                    BodyShape = 9;
                    AttackSound = SoundIndex.PhantomSoldierAttack;
                    StruckSound = SoundIndex.PhantomSoldierStruck;
                    DieSound = SoundIndex.PhantomSoldierDie;
                    break;
                case MonsterImage.MutatedOctopus:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_1, out BodyLibrary);
                    BodyShape = 2;
                    AttackSound = SoundIndex.MutatedOctopusAttack;
                    StruckSound = SoundIndex.MutatedOctopusStruck;
                    DieSound = SoundIndex.MutatedOctopusDie;
                    break;
                case MonsterImage.AquaLizard:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_10, out BodyLibrary);
                    BodyShape = 2;
                    AttackSound = SoundIndex.AquaLizardAttack;
                    StruckSound = SoundIndex.AquaLizardStruck;
                    DieSound = SoundIndex.AquaLizardDie;
                    break;
                case MonsterImage.Stomper:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_1, out BodyLibrary);
                    BodyShape = 9;
                    AttackSound = SoundIndex.AquaLizardAttack;
                    StruckSound = SoundIndex.AquaLizardStruck;
                    DieSound = SoundIndex.AquaLizardDie;
                    break;
                case MonsterImage.CrimsonNecromancer:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_2, out BodyLibrary);
                    BodyShape = 9;
                    AttackSound = SoundIndex.CrimsonNecromancerAttack;
                    StruckSound = SoundIndex.CrimsonNecromancerStruck;
                    DieSound = SoundIndex.CrimsonNecromancerDie;
                    break;
                case MonsterImage.ChaosKnight:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_2, out BodyLibrary);
                    BodyShape = 0;
                    AttackSound = SoundIndex.ChaosKnightAttack;
                    //  StruckSound = SoundIndex.ChaosKnightStruck;
                    DieSound = SoundIndex.ChaosKnightDie;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.BanyaGuard)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.PachonTheChaosBringer:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_13, out BodyLibrary);
                    BodyShape = 0;
                    AttackSound = SoundIndex.PachontheChaosbringerAttack;
                    StruckSound = SoundIndex.PachontheChaosbringerStruck;
                    DieSound = SoundIndex.PachontheChaosbringerDie;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.PachonTheChaosBringer)
                        Frames[frame.Key] = frame.Value;
                    break;

                case MonsterImage.NumaCavalry:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_19, out BodyLibrary);
                    BodyShape = 0;
                    AttackSound = SoundIndex.NumaCavalryAttack;
                    StruckSound = SoundIndex.NumaCavalryStruck;
                    DieSound = SoundIndex.NumaCavalryDie;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.BanyaGuard)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.NumaHighMage:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_19, out BodyLibrary);
                    BodyShape = 4;
                    AttackSound = SoundIndex.NumaHighMageAttack;
                    StruckSound = SoundIndex.NumaHighMageStruck;
                    DieSound = SoundIndex.NumaHighMageDie;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.BanyaGuard)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.NumaStoneThrower:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_19, out BodyLibrary);
                    BodyShape = 3;
                    AttackSound = SoundIndex.NumaStoneThrowerAttack;
                    StruckSound = SoundIndex.NumaStoneThrowerStruck;
                    DieSound = SoundIndex.NumaStoneThrowerDie;
                    break;
                case MonsterImage.NumaRoyalGuard:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_19, out BodyLibrary);
                    BodyShape = 5;
                    AttackSound = SoundIndex.NumaRoyalGuardAttack;
                    StruckSound = SoundIndex.NumaRoyalGuardStruck;
                    DieSound = SoundIndex.NumaRoyalGuardDie;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.EmperorSaWoo)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.NumaArmoredSoldier:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_19, out BodyLibrary);
                    BodyShape = 1;
                    AttackSound = SoundIndex.NumaArmoredSoldierAttack;
                    StruckSound = SoundIndex.NumaArmoredSoldierStruck;
                    DieSound = SoundIndex.NumaArmoredSoldierDie;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.BanyaGuard)
                        Frames[frame.Key] = frame.Value;
                    break;

                case MonsterImage.IcyRanger:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_21, out BodyLibrary);
                    BodyShape = 0;
                    AttackSound = SoundIndex.IcyRangerAttack;
                    StruckSound = SoundIndex.IcyRangerStruck;
                    DieSound = SoundIndex.IcyRangerDie;
                    break;
                case MonsterImage.IcyGoddess:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_18, out BodyLibrary);
                    BodyShape = 0;
                    AttackSound = SoundIndex.IcyGoddessAttack;
                    StruckSound = SoundIndex.IcyGoddessStruck;
                    DieSound = SoundIndex.IcyGoddessDie;
                    break;
                case MonsterImage.IcySpiritWarrior:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_21, out BodyLibrary);
                    BodyShape = 2;
                    AttackSound = SoundIndex.IcySpiritWarriorAttack;
                    StruckSound = SoundIndex.IcySpiritWarriorStruck;
                    DieSound = SoundIndex.IcySpiritWarriorDie;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.NumaMage)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.IcySpiritGeneral:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_21, out BodyLibrary);
                    BodyShape = 3;
                    AttackSound = SoundIndex.IcySpiritWarriorAttack;
                    StruckSound = SoundIndex.IcySpiritWarriorStruck;
                    DieSound = SoundIndex.IcySpiritWarriorDie;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.IcySpiritGeneral)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.GhostKnight:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_21, out BodyLibrary);
                    BodyShape = 4;
                    AttackSound = SoundIndex.GhostKnightAttack;
                    StruckSound = SoundIndex.GhostKnightStruck;
                    DieSound = SoundIndex.GhostKnightDie;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.EmperorSaWoo)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.IcySpiritSpearman:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_21, out BodyLibrary);
                    BodyShape = 6;
                    AttackSound = SoundIndex.IcySpiritSpearmanAttack;
                    StruckSound = SoundIndex.IcySpiritSpearmanStruck;
                    DieSound = SoundIndex.IcySpiritSpearmanDie;
                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.BanyaGuard)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.Werewolf:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_21, out BodyLibrary);
                    BodyShape = 7;
                    AttackSound = SoundIndex.WerewolfAttack;
                    StruckSound = SoundIndex.WerewolfStruck;
                    DieSound = SoundIndex.WerewolfDie;
                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.BanyaGuard)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.Whitefang:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_21, out BodyLibrary);
                    BodyShape = 8;
                    AttackSound = SoundIndex.WhitefangAttack;
                    StruckSound = SoundIndex.WhitefangStruck;
                    DieSound = SoundIndex.WhitefangDie;
                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.BanyaGuard)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.IcySpiritSolider:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_21, out BodyLibrary);
                    BodyShape = 9;
                    AttackSound = SoundIndex.IcySpiritSoliderAttack;
                    StruckSound = SoundIndex.IcySpiritSoliderStruck;
                    DieSound = SoundIndex.IcySpiritSoliderDie;
                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.BanyaGuard)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.WildBoar:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_18, out BodyLibrary);
                    BodyShape = 1;
                    AttackSound = SoundIndex.WildBoarAttack;
                    StruckSound = SoundIndex.WildBoarStruck;
                    DieSound = SoundIndex.WildBoarDie;
                    break;
                case MonsterImage.JinamStoneGate:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_23, out BodyLibrary);
                    BodyShape = 9;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.JinamStoneGate)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.FrostLordHwa:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_21, out BodyLibrary);
                    BodyShape = 5;
                    AttackSound = SoundIndex.FrostLordHwaAttack;
                    StruckSound = SoundIndex.FrostLordHwaStruck;
                    DieSound = SoundIndex.FrostLordHwaDie;
                    break;
                case MonsterImage.Companion_Pig:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_34, out BodyLibrary);
                    BodyShape = 0;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.Companion_Pig)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.Companion_TuskLord:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_34, out BodyLibrary);
                    BodyShape = 1;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.Companion_TuskLord)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.Companion_SkeletonLord:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_34, out BodyLibrary);
                    BodyShape = 2;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.Companion_SkeletonLord)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.Companion_Griffin:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_34, out BodyLibrary);
                    BodyShape = 3;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.Companion_Griffin)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.Companion_Dragon:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_34, out BodyLibrary);
                    BodyShape = 4;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.Companion_Dragon)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.Companion_Donkey:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_34, out BodyLibrary);
                    BodyShape = 5;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.Companion_Donkey)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.Companion_Sheep:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_34, out BodyLibrary);
                    BodyShape = 6;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.Companion_Sheep)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.Companion_BanyoLordGuzak:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_34, out BodyLibrary);
                    BodyShape = 7;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.Companion_BanyoLordGuzak)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.Companion_Panda:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_34, out BodyLibrary);
                    BodyShape = 8;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.Companion_Panda)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.Companion_Rabbit:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_34, out BodyLibrary);
                    BodyShape = 9;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.Companion_Rabbit)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.Companion_Dog:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_57, out BodyLibrary);
                    BodyShape = 0;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.Companion_Dog)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.Companion_Jinchon:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_57, out BodyLibrary);
                    BodyShape = 1;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.Companion_Jinchon)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.Companion_Dino:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_57, out BodyLibrary);
                    BodyShape = 2;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.Companion_Dino)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.InfernalSoldier:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_26, out BodyLibrary);

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.InfernalSoldier)
                        Frames[frame.Key] = frame.Value;

                    BodyShape = 0;
                    break;
                case MonsterImage.OmaWarlord:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_3, out BodyLibrary);
                    BodyShape = 7;

                    AttackSound = SoundIndex.OmaHeroAttack;
                    StruckSound = SoundIndex.OmaHeroStruck;
                    DieSound = SoundIndex.OmaHeroDie;
                    break;
                case MonsterImage.EscortCommander:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_22, out BodyLibrary);
                    BodyShape = 0;

                    AttackSound = SoundIndex.EscortCommanderAttack;
                    StruckSound = SoundIndex.EscortCommanderStruck;
                    DieSound = SoundIndex.EscortCommanderDie;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.BanyaGuard)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.FieryDancer:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_22, out BodyLibrary);
                    BodyShape = 2;

                    AttackSound = SoundIndex.FieryDancerAttack;
                    StruckSound = SoundIndex.FieryDancerStruck;
                    DieSound = SoundIndex.FieryDancerDie;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.FieryDancer)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.EmeraldDancer:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_22, out BodyLibrary);
                    BodyShape = 3;

                    AttackSound = SoundIndex.EmeraldDancerAttack;
                    StruckSound = SoundIndex.EmeraldDancerStruck;
                    DieSound = SoundIndex.EmeraldDancerDie;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.EmeraldDancer)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.QueenOfDawn:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_22, out BodyLibrary);
                    BodyShape = 1;

                    AttackSound = SoundIndex.QueenOfDawnAttack;
                    StruckSound = SoundIndex.QueenOfDawnStruck;
                    DieSound = SoundIndex.QueenOfDawnDie;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.QueenOfDawn)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.OYoungBeast:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_23, out BodyLibrary);
                    BodyShape = 3;

                    AttackSound = SoundIndex.OYoungBeastAttack;
                    StruckSound = SoundIndex.OYoungBeastStruck;
                    DieSound = SoundIndex.OYoungBeastDie;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.OYoungBeast)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.YumgonWitch:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_23, out BodyLibrary);
                    BodyShape = 6;

                    AttackSound = SoundIndex.YumgonWitchAttack;
                    StruckSound = SoundIndex.YumgonWitchStruck;
                    DieSound = SoundIndex.YumgonWitchDie;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.YumgonWitch)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.MaWarlord:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_23, out BodyLibrary);
                    BodyShape = 4;

                    AttackSound = SoundIndex.MaWarlordAttack;
                    StruckSound = SoundIndex.MaWarlordStruck;
                    DieSound = SoundIndex.MaWarlordDie;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.OYoungBeast)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.JinhwanSpirit:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_23, out BodyLibrary);
                    BodyShape = 7;

                    AttackSound = SoundIndex.JinhwanSpiritAttack;
                    StruckSound = SoundIndex.JinhwanSpiritStruck;
                    DieSound = SoundIndex.JinhwanSpiritDie;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.JinhwanSpirit)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.JinhwanGuardian:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_23, out BodyLibrary);
                    BodyShape = 8;

                    AttackSound = SoundIndex.JinhwanGuardianAttack;
                    StruckSound = SoundIndex.JinhwanGuardianStruck;
                    DieSound = SoundIndex.JinhwanGuardianDie;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.JinhwanSpirit)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.YumgonGeneral:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_23, out BodyLibrary);
                    BodyShape = 5;

                    AttackSound = SoundIndex.YumgonGeneralAttack;
                    StruckSound = SoundIndex.YumgonGeneralStruck;
                    DieSound = SoundIndex.YumgonGeneralDie;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.OYoungBeast)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.ChiwooGeneral:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_23, out BodyLibrary);
                    BodyShape = 0;

                    AttackSound = SoundIndex.ChiwooGeneralAttack;
                    StruckSound = SoundIndex.ChiwooGeneralStruck;
                    DieSound = SoundIndex.ChiwooGeneralDie;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.ChiwooGeneral)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.DragonQueen:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_23, out BodyLibrary);
                    BodyShape = 2;

                    AttackSound = SoundIndex.DragonQueenAttack;
                    StruckSound = SoundIndex.DragonQueenStruck;
                    DieSound = SoundIndex.DragonQueenDie;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.DragonQueen)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.DragonLord:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_23, out BodyLibrary);
                    BodyShape = 1;

                    AttackSound = SoundIndex.DragonLordAttack;
                    StruckSound = SoundIndex.DragonLordStruck;
                    DieSound = SoundIndex.DragonLordDie;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.DragonLord)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.FerociousIceTiger:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_21, out BodyLibrary);
                    BodyShape = 1;

                    AttackSound = SoundIndex.FerociousIceTigerAttack;
                    StruckSound = SoundIndex.FerociousIceTigerStruck;
                    DieSound = SoundIndex.FerociousIceTigerDie;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.FerociousIceTiger)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.SamaFireGuardian:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_25, out BodyLibrary);
                    BodyShape = 0;

                    AttackSound = SoundIndex.SamaFireGuardianAttack;
                    StruckSound = SoundIndex.SamaFireGuardianStruck;
                    DieSound = SoundIndex.SamaFireGuardianDie;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.SamaFireGuardian)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.SamaIceGuardian:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_25, out BodyLibrary);
                    BodyShape = 1;

                    AttackSound = SoundIndex.SamaIceGuardianAttack;
                    StruckSound = SoundIndex.SamaIceGuardianStruck;
                    DieSound = SoundIndex.SamaIceGuardianDie;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.SamaFireGuardian)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.SamaLightningGuardian:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_25, out BodyLibrary);
                    BodyShape = 2;

                    AttackSound = SoundIndex.SamaLightningGuardianAttack;
                    StruckSound = SoundIndex.SamaLightningGuardianStruck;
                    DieSound = SoundIndex.SamaLightningGuardianDie;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.SamaFireGuardian)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.SamaWindGuardian:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_25, out BodyLibrary);
                    BodyShape = 3;

                    AttackSound = SoundIndex.SamaWindGuardianAttack;
                    StruckSound = SoundIndex.SamaWindGuardianStruck;
                    DieSound = SoundIndex.SamaWindGuardianDie;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.SamaFireGuardian)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.Phoenix:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_25, out BodyLibrary);
                    BodyShape = 4;

                    AttackSound = SoundIndex.PhoenixAttack;
                    StruckSound = SoundIndex.PhoenixStruck;
                    DieSound = SoundIndex.PhoenixDie;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.Phoenix)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.BlackTortoise:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_25, out BodyLibrary);
                    BodyShape = 5;

                    AttackSound = SoundIndex.BlackTortoiseAttack;
                    StruckSound = SoundIndex.BlackTortoiseStruck;
                    DieSound = SoundIndex.BlackTortoiseDie;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.Phoenix)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.BlueDragon:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_25, out BodyLibrary);
                    BodyShape = 6;

                    AttackSound = SoundIndex.BlueDragonAttack;
                    StruckSound = SoundIndex.BlueDragonStruck;
                    DieSound = SoundIndex.BlueDragonDie;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.Phoenix)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.WhiteTiger:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_25, out BodyLibrary);
                    BodyShape = 7;

                    AttackSound = SoundIndex.WhiteTigerAttack;
                    StruckSound = SoundIndex.WhiteTigerStruck;
                    DieSound = SoundIndex.WhiteTigerDie;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.Phoenix)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.EnshrinementBox:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_27, out BodyLibrary);
                    BodyShape = 5;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.EnshrinementBox)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.BloodStone:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_19, out BodyLibrary);
                    BodyShape = 7;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.BloodStone)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.SamaCursedBladesman:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_27, out BodyLibrary);
                    BodyShape = 0;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.SamaCursedBladesman)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.SamaCursedSlave:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_27, out BodyLibrary);
                    BodyShape = 1;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.SamaCursedSlave)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.SamaCursedFlameMage:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_27, out BodyLibrary);
                    BodyShape = 2;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.SamaCursedSlave)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.SamaProphet:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_27, out BodyLibrary);
                    BodyShape = 3;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.SamaProphet)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.SamaSorcerer:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_27, out BodyLibrary);
                    BodyShape = 4;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.SamaSorcerer)
                        Frames[frame.Key] = frame.Value;
                    break;

                case MonsterImage.OrangeTiger:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_35, out BodyLibrary);
                    BodyShape = 0;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.OrangeTiger)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.RegularTiger:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_35, out BodyLibrary);
                    BodyShape = 1;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.OrangeTiger)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.RedTiger:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_35, out BodyLibrary);
                    BodyShape = 2;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.RedTiger)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.SnowTiger:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_35, out BodyLibrary);
                    BodyShape = 3;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.OrangeTiger)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.BlackTiger:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_35, out BodyLibrary);
                    BodyShape = 4;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.OrangeTiger)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.BigBlackTiger:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_35, out BodyLibrary);
                    BodyShape = 5;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.OrangeTiger)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.BigWhiteTiger:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_35, out BodyLibrary);
                    BodyShape = 6;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.OrangeTiger)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.OrangeBossTiger:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_35, out BodyLibrary);
                    BodyShape = 7;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.OrangeBossTiger)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.BigBossTiger:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_35, out BodyLibrary);
                    BodyShape = 8;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.OrangeBossTiger)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.WildMonkey:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_30, out BodyLibrary);
                    BodyShape = 0;
                    AttackSound = SoundIndex.MonkeyAttack;
                    StruckSound = SoundIndex.MonkeyStruck;
                    DieSound = SoundIndex.MonkeyDie;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.Monkey)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.FrostYeti:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_30, out BodyLibrary);
                    BodyShape = 1;
                    AttackSound = SoundIndex.ForestYetiAttack;
                    StruckSound = SoundIndex.ForestYetiStruck;
                    DieSound = SoundIndex.ForestYetiDie;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.ForestYeti)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.EvilSnake:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_9, out BodyLibrary);
                    BodyShape = 0;

                    AttackSound = SoundIndex.ClawSerpentAttack;
                    StruckSound = SoundIndex.ClawSerpentStruck;
                    DieSound = SoundIndex.ClawSerpentDie;
                    break;
                case MonsterImage.Salamander:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_28, out BodyLibrary);
                    BodyShape = 0;

                    break;
                case MonsterImage.SandGolem:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_28, out BodyLibrary);
                    BodyShape = 1;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.SDMob3)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.SDMob4:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_29, out BodyLibrary);
                    BodyShape = 0;

                    break;
                case MonsterImage.SDMob5:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_29, out BodyLibrary);
                    BodyShape = 1;

                    break;
                case MonsterImage.SDMob6:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_29, out BodyLibrary);
                    BodyShape = 2;

                    break;
                case MonsterImage.SDMob7:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_29, out BodyLibrary);
                    BodyShape = 8;

                    break;
                case MonsterImage.OmaMage:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_29, out BodyLibrary);
                    BodyShape = 9;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.SDMob8)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.SDMob9:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_32, out BodyLibrary);
                    BodyShape = 1;

                    break;
                case MonsterImage.SDMob10:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_32, out BodyLibrary);
                    BodyShape = 5;

                    break;
                case MonsterImage.SDMob11:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_32, out BodyLibrary);
                    BodyShape = 6;

                    break;
                case MonsterImage.SDMob12:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_32, out BodyLibrary);
                    BodyShape = 7;

                    break;
                case MonsterImage.SDMob13:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_32, out BodyLibrary);
                    BodyShape = 8;

                    break;
                case MonsterImage.SDMob14:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_32, out BodyLibrary);
                    BodyShape = 9;

                    break;
                case MonsterImage.CrystalGolem:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_40, out BodyLibrary);
                    BodyShape = 0;


                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.SDMob15)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.DustDevil:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_41, out BodyLibrary);
                    BodyShape = 1;


                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.SDMob16)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.TwinTailScorpion:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_41, out BodyLibrary);
                    BodyShape = 2;


                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.SDMob17)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.BloodyMole:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_41, out BodyLibrary);
                    BodyShape = 3;


                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.SDMob18)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.SDMob19:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_44, out BodyLibrary);
                    BodyShape = 3;


                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.SDMob19)
                        Frames[frame.Key] = frame.Value;

                    break;
                case MonsterImage.SDMob20:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_44, out BodyLibrary);
                    BodyShape = 4;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.SDMob19)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.SDMob21:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_44, out BodyLibrary);
                    BodyShape = 5;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.SDMob21)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.SDMob22:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_44, out BodyLibrary);
                    BodyShape = 6;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.SDMob22)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.SDMob23:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_44, out BodyLibrary);
                    BodyShape = 7;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.SDMob23)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.SDMob24:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_44, out BodyLibrary);
                    BodyShape = 8;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.SDMob24)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.SDMob25:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_44, out BodyLibrary);
                    BodyShape = 9;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.SDMob25)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.SDMob26:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_45, out BodyLibrary);
                    BodyShape = 0;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.SDMob26)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.GangSpider:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_28, out BodyLibrary);
                    BodyShape = 8;

                    break;
                case MonsterImage.VenomSpider:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_28, out BodyLibrary);
                    BodyShape = 9;

                    break;
                case MonsterImage.LobsterLord:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_45, out BodyLibrary);
                    BodyShape = 3;


                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.LobsterLord)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.NewMob1:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_47, out BodyLibrary);
                    BodyShape = 0;

                    break;
                case MonsterImage.NewMob2:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_47, out BodyLibrary);
                    BodyShape = 1;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.BobbitWorm)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.NewMob3:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_47, out BodyLibrary);
                    BodyShape = 2;



                    break;
                case MonsterImage.NewMob4:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_47, out BodyLibrary);
                    BodyShape = 3;



                    /*
                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.LobsterLord)
                        Frames[frame.Key] = frame.Value;*/
                    break;
                case MonsterImage.NewMob5:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_47, out BodyLibrary);
                    BodyShape = 4;



                    /*
                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.LobsterLord)
                        Frames[frame.Key] = frame.Value;*/
                    break;
                case MonsterImage.NewMob6:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_47, out BodyLibrary);
                    BodyShape = 5;



                    /*
                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.LobsterLord)
                        Frames[frame.Key] = frame.Value;*/
                    break;
                case MonsterImage.NewMob7:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_47, out BodyLibrary);
                    BodyShape = 6;



                    /*
                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.LobsterLord)
                        Frames[frame.Key] = frame.Value;*/
                    break;
                case MonsterImage.NewMob8:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_47, out BodyLibrary);
                    BodyShape = 7;



                    /*
                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.LobsterLord)
                        Frames[frame.Key] = frame.Value;*/
                    break;
                case MonsterImage.NewMob9:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_47, out BodyLibrary);
                    BodyShape = 8;



                    /*
                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.LobsterLord)
                        Frames[frame.Key] = frame.Value;*/
                    break;
                case MonsterImage.NewMob10:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_47, out BodyLibrary);
                    BodyShape = 9;


                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.DeadTree)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.MonasteryMon0:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_49, out BodyLibrary);
                    BodyShape = 0;

                    break;
                case MonsterImage.MonasteryMon1:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_49, out BodyLibrary);
                    BodyShape = 1;


                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.MonasteryMon1)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.MonasteryMon2:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_49, out BodyLibrary);
                    BodyShape = 2;

                    break;
                case MonsterImage.MonasteryMon3:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_49, out BodyLibrary);
                    BodyShape = 3;

                    //Fucked up mob

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.MonasteryMon3)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.MonasteryMon4:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_49, out BodyLibrary);
                    BodyShape = 4;

                    break;
                case MonsterImage.MonasteryMon5:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_49, out BodyLibrary);
                    BodyShape = 5;


                    break;
                case MonsterImage.MonasteryMon6:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_49, out BodyLibrary);
                    BodyShape = 6;

                    //Fuckjed up Mob

                    break;
                case MonsterImage.Terracotta1:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_42, out BodyLibrary);
                    BodyShape = 4;
                    AttackSound = SoundIndex.Terracotta1Attack;
                    StruckSound = SoundIndex.Terracotta1Struck;
                    DieSound = SoundIndex.Terracotta1Die;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.Terracotta1)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.Terracotta2:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_42, out BodyLibrary);
                    BodyShape = 5;
                    AttackSound = SoundIndex.Terracotta2Attack;
                    StruckSound = SoundIndex.Terracotta2Struck;
                    DieSound = SoundIndex.Terracotta2Die;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.Terracotta2)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.Terracotta3:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_42, out BodyLibrary);
                    BodyShape = 6;
                    AttackSound = SoundIndex.Terracotta3Attack;
                    StruckSound = SoundIndex.Terracotta3Struck;
                    DieSound = SoundIndex.Terracotta3Die;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.Terracotta3)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.Terracotta4:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_42, out BodyLibrary);
                    BodyShape = 7;
                    AttackSound = SoundIndex.Terracotta4Attack;
                    StruckSound = SoundIndex.Terracotta4Struck;
                    DieSound = SoundIndex.Terracotta4Die;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.Terracotta3)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.TerracottaSub:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_42, out BodyLibrary);
                    BodyShape = 8;
                    AttackSound = SoundIndex.TerracottaSubAttack;
                    StruckSound = SoundIndex.TerracottaSubStruck;
                    DieSound = SoundIndex.TerracottaSubDie;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.TerracottaSub)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.TerracottaBoss:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_42, out BodyLibrary);
                    BodyShape = 9;
                    AttackSound = SoundIndex.TerracottaBossAttack2;
                    StruckSound = SoundIndex.TerracottaBossStruck;
                    DieSound = SoundIndex.TerracottaBossDie;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.TerracottaBoss)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.WildBrownHorse:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_52, out BodyLibrary);
                    BodyShape = 0;
                    break;
                case MonsterImage.WildWhiteHorse:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_52, out BodyLibrary);
                    BodyShape = 1;
                    break;
                case MonsterImage.WildBlackHorse:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_52, out BodyLibrary);
                    BodyShape = 2;
                    break;
                case MonsterImage.WildRedHorse:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_52, out BodyLibrary);
                    BodyShape = 4;
                    break;
                case MonsterImage.SeaHorseCavalry:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_53, out BodyLibrary);
                    BodyShape = 0;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.SeaHorseCavalry)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.Seamancer:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_53, out BodyLibrary);
                    BodyShape = 1;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.Seamancer)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.CoralStoneDuin:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_53, out BodyLibrary);
                    BodyShape = 2;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.CoralStoneDuin)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.Brachiopod:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_53, out BodyLibrary);
                    BodyShape = 3;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.Brachiopod)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.GiantClam:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_53, out BodyLibrary);
                    BodyShape = 4;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.GiantClam)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.BlueMassif:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_53, out BodyLibrary);
                    BodyShape = 5;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.BlueMassif)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.Mollusk:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_53, out BodyLibrary);
                    BodyShape = 6;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.Mollusk)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.Kraken:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_53, out BodyLibrary);
                    BodyShape = 7;
                    //??
                    break;
                case MonsterImage.KrakenLeg:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_53, out BodyLibrary);
                    BodyShape = 8;
                    //??
                    break;
                case MonsterImage.GiantClam1:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_53, out BodyLibrary);
                    BodyShape = 9;

                    //Shares some frames with GiantClam?
                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.GiantClam1)
                        Frames[frame.Key] = frame.Value;
                    break;

                case MonsterImage.Tornado:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_56, out BodyLibrary);
                    BodyShape = 6;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.Tornado)
                        Frames[frame.Key] = frame.Value;

                    break;
                case MonsterImage.CastleFlag:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.CastleFlag, out BodyLibrary);

                    BodyOffSet = 100;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.CastleFlag)
                        Frames[frame.Key] = frame.Value;

                    BodyShape = Extra1;

                    break;
                case MonsterImage.SabukGateSouth:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_54, out BodyLibrary);
                    BodyShape = 0;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.SabukGate)
                        Frames[frame.Key] = frame.Value;

                    break;
                case MonsterImage.SabukGateNorth:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_54, out BodyLibrary);
                    BodyShape = 1;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.SabukGate)
                        Frames[frame.Key] = frame.Value;

                    break;
                case MonsterImage.SabukGateEast:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_54, out BodyLibrary);
                    BodyShape = 2;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.SabukGate)
                        Frames[frame.Key] = frame.Value;

                    break;
                case MonsterImage.SabukGateWest:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_54, out BodyLibrary);
                    BodyShape = 3;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.SabukGate)
                        Frames[frame.Key] = frame.Value;

                    break;
                case MonsterImage.DarkChampion:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_48, out BodyLibrary);
                    BodyShape = 0;
                    break;
                case MonsterImage.DarkGuard:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_48, out BodyLibrary);
                    BodyShape = 1;
                    break;
                case MonsterImage.DarkKing:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_48, out BodyLibrary);
                    BodyShape = 2;
                    break;
                case MonsterImage.TempleGuardian:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_57, out BodyLibrary);
                    BodyShape = 8;
                    break;
                case MonsterImage.DemonicBlack:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_29, out BodyLibrary);
                    BodyShape = 4;
                    break;
                case MonsterImage.ForestTree:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_31, out BodyLibrary);
                    BodyShape = 0;
                    break;
                case MonsterImage.DarkForestTree:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_24, out BodyLibrary);
                    BodyShape = 4;
                    break;
                case MonsterImage.TempleKnight:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_59, out BodyLibrary);
                    BodyShape = 4;
                    break;
                case MonsterImage.LavaGod:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_50, out BodyLibrary);
                    BodyShape = 1;
                    break;
                case MonsterImage.LavaScorp:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_50, out BodyLibrary);
                    BodyShape = 2;
                    break;
                case MonsterImage.TempleSnake:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_63, out BodyLibrary);
                    BodyShape = 1;
                    break;
                case MonsterImage.Tachinid:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_62, out BodyLibrary);
                    BodyShape = 3;
                    break;
                case MonsterImage.SnailKing:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_56, out BodyLibrary);
                    BodyShape = 3;
                    break;
                case MonsterImage.Turtle:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_56, out BodyLibrary);
                    BodyShape = 6;
                    break;
                case MonsterImage.WarBeast:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_56, out BodyLibrary);
                    BodyShape = 4;
                    break;
                case MonsterImage.Ent:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_59, out BodyLibrary);
                    BodyShape = 1;
                    break;
                case MonsterImage.VenusFlytrap1:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_38, out BodyLibrary);
                    BodyShape = 2;
                    break;
                case MonsterImage.VenusFlytrap2:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_38, out BodyLibrary);
                    BodyShape = 3;
                    break;
                case MonsterImage.PoisonusTreant:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_31, out BodyLibrary);
                    BodyShape = 1;
                    break;
                case MonsterImage.SeaFloor0:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_53, out BodyLibrary);
                    BodyShape = 0;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.SeaFloor0)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.SeaFloor1:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_53, out BodyLibrary);
                    BodyShape = 1;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.SeaFloor1)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.SeaFloor2:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_53, out BodyLibrary);
                    BodyShape = 2;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.SeaFloor2)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.SeaFloor3:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_53, out BodyLibrary);
                    BodyShape = 3;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.SeaFloor3)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.SeaFloor4:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_53, out BodyLibrary);
                    BodyShape = 4;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.SeaFloor4)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.SeaFloorSub:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_53, out BodyLibrary);
                    BodyShape = 5;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.SeaFloorSub)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.SeaFloor6:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_53, out BodyLibrary);
                    BodyShape = 6;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.SeaFloor6)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.SeaFloorBoss:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_53, out BodyLibrary);
                    BodyShape = 7;

                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.SeaFloorBoss)
                        Frames[frame.Key] = frame.Value;
                    break;
                case MonsterImage.CrazedPrimate:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_36, out BodyLibrary);
                    BodyShape = 2;
                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.CrazedPrimate)
                        Frames[frame.Key] = frame.Value;

                    AttackSound = SoundIndex.CrazedPrimateAttack;
                    StruckSound = SoundIndex.CrazedPrimateStruck;
                    DieSound = SoundIndex.CrazedPrimateDie;
                    break;
                case MonsterImage.HellBringer:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_36, out BodyLibrary);
                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.HellBringer)
                        Frames[frame.Key] = frame.Value;
                    BodyShape = 3;

                    StruckSound = SoundIndex.HellBringerStruck;
                    DieSound = SoundIndex.HellBringerDie;
                    break;
                case MonsterImage.YurinMon0:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_37, out BodyLibrary);
                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.YurinMon0)
                        Frames[frame.Key] = frame.Value;
                    BodyShape = 0;

                    AttackSound = SoundIndex.YurinHoundAttack;
                    StruckSound = SoundIndex.YurinHoundStruck;
                    DieSound = SoundIndex.YurinHoundDie;
                    break;
                case MonsterImage.YurinMon1:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_37, out BodyLibrary);
                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.YurinMon1)
                        Frames[frame.Key] = frame.Value;
                    BodyShape = 1;

                    AttackSound = SoundIndex.YurinHoundAttack;
                    StruckSound = SoundIndex.YurinHoundStruck;
                    DieSound = SoundIndex.YurinHoundDie;
                    break;
                case MonsterImage.WhiteBeardedTiger:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_37, out BodyLibrary);
                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.WhiteBeardedTiger)
                        Frames[frame.Key] = frame.Value;
                    BodyShape = 2;

                    AttackSound = SoundIndex.YurinTigerAttack;
                    StruckSound = SoundIndex.YurinTigerStruck;
                    DieSound = SoundIndex.YurinTigerDie;
                    break;
                case MonsterImage.BlackBeardedTiger:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_37, out BodyLibrary);
                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.WhiteBeardedTiger)
                        Frames[frame.Key] = frame.Value;
                    BodyShape = 3;

                    AttackSound = SoundIndex.YurinTigerAttack;
                    StruckSound = SoundIndex.YurinTigerStruck;
                    DieSound = SoundIndex.YurinTigerDie;
                    break;
                case MonsterImage.HardenedRhino:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_37, out BodyLibrary);
                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.HardenedRhino)
                        Frames[frame.Key] = frame.Value;
                    BodyShape = 4;

                    AttackSound = SoundIndex.HardenedRhinoAttack;
                    StruckSound = SoundIndex.HardenedRhinoStruck;
                    DieSound = SoundIndex.HardenedRhinoDie;
                    break;
                case MonsterImage.Mammoth:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_37, out BodyLibrary);
                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.Mammoth)
                        Frames[frame.Key] = frame.Value;
                    BodyShape = 5;

                    AttackSound = SoundIndex.MammothAttack;
                    StruckSound = SoundIndex.MammothStruck;
                    DieSound = SoundIndex.MammothDie;
                    break;
                case MonsterImage.CursedSlave1:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_37, out BodyLibrary);
                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.CursedSlave1)
                        Frames[frame.Key] = frame.Value;
                    BodyShape = 6;

                    StruckSound = SoundIndex.CursedSlave1Struck;
                    DieSound = SoundIndex.CursedSlave1Die;
                    break;
                case MonsterImage.CursedSlave2:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_37, out BodyLibrary);
                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.CursedSlave2)
                        Frames[frame.Key] = frame.Value;
                    BodyShape = 7;

                    AttackSound = SoundIndex.CursedSlave2Attack;
                    StruckSound = SoundIndex.CursedSlave2Struck;
                    DieSound = SoundIndex.CursedSlave2Die;
                    break;
                case MonsterImage.CursedSlave3:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_37, out BodyLibrary);
                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.CursedSlave3)
                        Frames[frame.Key] = frame.Value;
                    BodyShape = 8;

                    StruckSound = SoundIndex.CursedSlave3Struck;
                    DieSound = SoundIndex.CursedSlave3Die;
                    break;
                case MonsterImage.PoisonousGolem:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_37, out BodyLibrary);
                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.PoisonousGolem)
                        Frames[frame.Key] = frame.Value;
                    BodyShape = 9;

                    StruckSound = SoundIndex.PoisonousGolemStruck;
                    DieSound = SoundIndex.PoisonousGolemDie;
                    break;
                case MonsterImage.GardenSoldier:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_38, out BodyLibrary);
                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.GardenSoldier)
                        Frames[frame.Key] = frame.Value;
                    BodyShape = 0;

                    StruckSound = SoundIndex.GardenSoldierStruck;
                    DieSound = SoundIndex.GardenSoldierDie;
                    break;
                case MonsterImage.GardenDefender:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_38, out BodyLibrary);
                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.GardenDefender)
                        Frames[frame.Key] = frame.Value;
                    BodyShape = 1;

                    StruckSound = SoundIndex.GardenDefenderStruck;
                    DieSound = SoundIndex.GardenDefenderDie;
                    break;
                case MonsterImage.RedBlossom:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_38, out BodyLibrary);
                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.RedBlossom)
                        Frames[frame.Key] = frame.Value;
                    BodyShape = 2;

                    StruckSound = SoundIndex.RedBlossomStruck;
                    DieSound = SoundIndex.RedBlossomDie;
                    break;
                case MonsterImage.BlueBlossom:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_38, out BodyLibrary);
                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.BlueBlossom)
                        Frames[frame.Key] = frame.Value;
                    BodyShape = 3;

                    AttackSound = SoundIndex.BlueBlossomAttack;
                    StruckSound = SoundIndex.BlueBlossomStruck;
                    DieSound = SoundIndex.BlueBlossomDie;
                    break;
                case MonsterImage.FireBird:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_38, out BodyLibrary);
                    foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.FireBird)
                        Frames[frame.Key] = frame.Value;
                    BodyShape = 4;

                    StruckSound = SoundIndex.FireBirdStruck;
                    DieSound = SoundIndex.FireBirdDie;
                    break;
                default:
                    CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_1, out BodyLibrary);
                    BodyShape = 0;
                    break;
            }

            if (EasterEvent)
            {
                CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_30, out BodyLibrary);
                BodyShape = 4;

                Frames = new Dictionary<MirAnimation, Frame>(FrameSet.DefaultMonster);

                foreach (KeyValuePair<MirAnimation, Frame> frame in FrameSet.EasterEvent)
                    Frames[frame.Key] = frame.Value;
            }
            else if (HalloweenEvent)
            {
                CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_1, out BodyLibrary);
                BodyShape = 1;

                Frames = new Dictionary<MirAnimation, Frame>(FrameSet.DefaultMonster);
            }
            else if (ChristmasEvent)
            {
                CEnvir.LibraryList.TryGetValue(LibraryFile.Mon_20, out BodyLibrary);
                BodyShape = 0;

                Frames = new Dictionary<MirAnimation, Frame>(FrameSet.DefaultMonster);
            }
        }

        private bool TryApplyCustomVisual()
        {
            if (CustomVisual?.Animations == null || CustomVisual.Animations.Count == 0)
                return false;

            CustomResourceLibraryInfo resource = CustomVisual.DefaultResource ?? CustomVisual.Animations.FirstOrDefault(x => x?.Resource != null)?.Resource;
            if (!CEnvir.TryGetCustomLibrary(resource, out BodyLibrary))
                return false;

            BodyShape = 0;
            BodyOffSet = 0;
            Frames = new Dictionary<MirAnimation, Frame>();
            CustomAnimationMap = new Dictionary<MirAnimation, CustomMonsterAnimationInfo>();
            CustomAnimationLibraryMap = new Dictionary<MirAnimation, MirLibrary>();
            UsingCustomVisual = true;
            AttackSound = CustomVisual.AttackSound;
            StruckSound = CustomVisual.StruckSound;
            DieSound = CustomVisual.DieSound;

            foreach (CustomMonsterAnimationInfo animation in CustomVisual.Animations
                         .Where(x => x != null)
                         .OrderBy(x => x.Priority)
                         .ThenBy(x => x.Action)
                         .ThenBy(x => x.Variant))
            {
                MirAnimation mirAnimation = GetCustomAnimation(animation.Action, animation.Variant);
                MirLibrary animationLibrary = BodyLibrary;

                if (animation.Resource != null && !CEnvir.TryGetCustomLibrary(animation.Resource, out animationLibrary))
                    continue;

                int frameCount = animation.FrameCount > 0
                    ? animation.FrameCount
                    : Math.Max(1, animation.EndIndex - animation.StartIndex + 1);

                int directionOffset = animation.DirectionOffset > 0
                    ? animation.DirectionOffset
                    : frameCount;

                int startIndex = CustomVisual.FrameBase + animation.StartIndex;
                int endIndex = CustomVisual.FrameBase + animation.EndIndex;

                ReportMissingCustomAnimationFrames(animation, animationLibrary, startIndex, frameCount, directionOffset);

                Frame frame = new(startIndex, frameCount, directionOffset, TimeSpan.FromMilliseconds(Math.Max(1, animation.FrameDelay)))
                {
                    Reversed = animation.Reversed
                };

                Frames[mirAnimation] = frame;
                CustomAnimationMap[mirAnimation] = animation;
                CustomAnimationLibraryMap[mirAnimation] = animationLibrary;

                if (animation.Action == CustomMonsterAnimationType.Walking)
                {
                    Frames[MirAnimation.Pushed] = new Frame(startIndex, frameCount, directionOffset, TimeSpan.FromMilliseconds(50)) { Reversed = true, StaticSpeed = true };
                    CustomAnimationMap[MirAnimation.Pushed] = animation;
                    CustomAnimationLibraryMap[MirAnimation.Pushed] = animationLibrary;
                }
                else if (animation.Action == CustomMonsterAnimationType.Die && !Frames.ContainsKey(MirAnimation.Dead))
                {
                    Frames[MirAnimation.Dead] = new Frame(animation.EndIndex > 0 ? endIndex : startIndex + frameCount - 1, 1, directionOffset, TimeSpan.FromMilliseconds(1000));
                    CustomAnimationMap[MirAnimation.Dead] = animation;
                    CustomAnimationLibraryMap[MirAnimation.Dead] = animationLibrary;
                }
            }

            if (!Frames.ContainsKey(MirAnimation.Standing))
                Frames[MirAnimation.Standing] = new Frame(CustomVisual.FrameBase, 1, 0, TimeSpan.FromMilliseconds(Math.Max(1, CustomVisual.DefaultFrameDelay)));

            if (!Frames.ContainsKey(MirAnimation.Walking))
                CopyCustomAnimationFallback(MirAnimation.Walking, MirAnimation.Standing);

            if (!Frames.ContainsKey(MirAnimation.Combat1))
                CopyCustomAnimationFallback(MirAnimation.Combat1, MirAnimation.Standing);

            if (!Frames.ContainsKey(MirAnimation.Combat2))
                CopyCustomAnimationFallback(MirAnimation.Combat2, MirAnimation.Combat1);

            if (!Frames.ContainsKey(MirAnimation.Combat3))
                CopyCustomAnimationFallback(MirAnimation.Combat3, MirAnimation.Combat1);

            if (!Frames.ContainsKey(MirAnimation.Struck))
                CopyCustomAnimationFallback(MirAnimation.Struck, MirAnimation.Standing);

            if (!Frames.ContainsKey(MirAnimation.Die))
                CopyCustomAnimationFallback(MirAnimation.Die, MirAnimation.Standing);

            if (!Frames.ContainsKey(MirAnimation.Dead))
                CopyCustomAnimationFallback(MirAnimation.Dead, MirAnimation.Die);

            return true;
        }

        private void ReportMissingCustomAnimationFrames(CustomMonsterAnimationInfo animation, MirLibrary library, int startIndex, int frameCount, int directionOffset)
        {
            if (animation == null || library == null) return;

            int directionCount = Math.Max(1, animation.DirectionCount <= 0 ? 8 : animation.DirectionCount);
            int lastDirectionStart = startIndex + directionOffset * (directionCount - 1);
            int maxIndex = lastDirectionStart + Math.Max(1, frameCount) - 1;

            if (library.HasImage(startIndex) && library.HasImage(maxIndex)) return;

            string resourceName = GetCustomResourceName(animation.Resource ?? CustomVisual?.DefaultResource);
            CEnvir.AddDynamicLibraryDiagnostic(
                $"自定义怪物动画帧缺失: {Name ?? MonsterInfo?.MonsterName} Action={animation.Action} Variant={animation.Variant} Resource={resourceName} FrameBase={CustomVisual?.FrameBase ?? 0} Start={animation.StartIndex} End={animation.EndIndex} FrameCount={frameCount} DirectionCount={directionCount} DirectionOffset={directionOffset} MaxIndex={maxIndex}");
        }

        private static string GetCustomResourceName(CustomResourceLibraryInfo resource)
        {
            if (resource == null) return "未配置";
            if (!string.IsNullOrWhiteSpace(resource.Key)) return resource.Key;
            if (resource.LibraryFile != LibraryFile.None) return resource.LibraryFile.ToString();
            return string.IsNullOrWhiteSpace(resource.RelativePath) ? $"#{resource.Index}" : resource.RelativePath;
        }

        private void CopyCustomAnimationFallback(MirAnimation target, MirAnimation source)
        {
            Frames[target] = Frames.TryGetValue(source, out Frame frame) ? frame : Frame.EmptyFrame;

            if (CustomAnimationMap != null && CustomAnimationMap.TryGetValue(source, out CustomMonsterAnimationInfo animation))
                CustomAnimationMap[target] = animation;

            if (CustomAnimationLibraryMap != null && CustomAnimationLibraryMap.TryGetValue(source, out MirLibrary library))
                CustomAnimationLibraryMap[target] = library;
        }

        private static MirAnimation GetCustomAnimation(CustomMonsterAnimationType action, int variant)
        {
            return action switch
            {
                CustomMonsterAnimationType.Standing => MirAnimation.Standing,
                CustomMonsterAnimationType.Walking => MirAnimation.Walking,
                CustomMonsterAnimationType.Attack => GetCombatAnimation(variant, MirAnimation.Combat1),
                CustomMonsterAnimationType.RangeAttack => GetCombatAnimation(variant, MirAnimation.Combat2),
                CustomMonsterAnimationType.Magic => GetCombatAnimation(variant, MirAnimation.Combat3),
                CustomMonsterAnimationType.Struck => MirAnimation.Struck,
                CustomMonsterAnimationType.Die => MirAnimation.Die,
                CustomMonsterAnimationType.Dead => MirAnimation.Dead,
                CustomMonsterAnimationType.Show => MirAnimation.Show,
                CustomMonsterAnimationType.Hide => MirAnimation.Hide,
                CustomMonsterAnimationType.Idle1 => MirAnimation.Combat1,
                CustomMonsterAnimationType.Idle2 => MirAnimation.Combat2,
                CustomMonsterAnimationType.Idle3 => MirAnimation.Combat3,
                CustomMonsterAnimationType.Idle4 => MirAnimation.Combat4,
                CustomMonsterAnimationType.Idle5 => MirAnimation.Combat5,
                CustomMonsterAnimationType.Idle6 => MirAnimation.Combat6,
                CustomMonsterAnimationType.Idle7 => MirAnimation.Combat7,
                CustomMonsterAnimationType.Idle8 => MirAnimation.Combat8,
                _ => MirAnimation.Standing,
            };
        }

        private static MirAnimation GetCombatAnimation(int variant, MirAnimation defaultAnimation)
        {
            return variant switch
            {
                1 => MirAnimation.Combat1,
                2 => MirAnimation.Combat2,
                3 => MirAnimation.Combat3,
                4 => MirAnimation.Combat4,
                5 => MirAnimation.Combat5,
                6 => MirAnimation.Combat6,
                7 => MirAnimation.Combat7,
                8 => MirAnimation.Combat8,
                _ => defaultAnimation,
            };
        }

        public void SetScale()
        {
            int sizePercent = Stats[Stat.SizePercent];

            GrowthLevel = Math.Min(Globals.MaxGrowthLevel, Math.Max(0, Stats[Stat.GrowthLevel]));

            if (GrowthLevel > 0)
                sizePercent = GrowthLevel * 5;

            float statScale = (float)(100 + Math.Min(20, Math.Max(-20, sizePercent))) / 100;
            Scale = UsingCustomVisual && CustomVisual?.Scale > 0 ? CustomVisual.Scale * statScale : statScale;
        }

        public override void SetAnimation(ObjectAction action)
        {
            if (UsingCustomVisual)
            {
                CurrentAnimation = GetCustomActionAnimation(action);
                if (!Frames.TryGetValue(CurrentAnimation, out CurrentFrame))
                    CurrentFrame = Frame.EmptyFrame;

                return;
            }

            MirAnimation animation;
            MagicType type;

            switch (action.Action)
            {
                case MirAction.Standing:
                    switch (Image)
                    {
                        case MonsterImage.ZumaGuardian:
                        case MonsterImage.ZumaFanatic:
                        case MonsterImage.ZumaKing:
                            animation = !Extra ? MirAnimation.StoneStanding : MirAnimation.Standing;
                            break;
                        default:
                            animation = MirAnimation.Standing;

                            if (VisibleBuffs.ContainsKey(BuffType.DragonRepulse))
                                animation = MirAnimation.DragonRepulseMiddle;
                            else if (CurrentAnimation == MirAnimation.DragonRepulseMiddle)
                                animation = MirAnimation.DragonRepulseEnd;
                            break;
                    }
                    break;
                case MirAction.Moving:
                    animation = MirAnimation.Walking;
                    break;
                case MirAction.Pushed:
                    animation = MirAnimation.Pushed;
                    break;
                case MirAction.Attack:
                    animation = MirAnimation.Combat1;
                    break;
                case MirAction.RangeAttack:
                    animation = MirAnimation.Combat2;
                    break;
                case MirAction.Spell:
                    type = (MagicType)action.Extra[0];

                    animation = MirAnimation.Combat3;

                    if (type == MagicType.DragonRepulse)
                        animation = MirAnimation.DragonRepulseStart;

                    switch (type)
                    {
                        case MagicType.DoomClawRightPinch:
                            animation = MirAnimation.Combat1;
                            break;
                        case MagicType.DoomClawRightSwipe:
                            animation = MirAnimation.Combat2;
                            break;
                        case MagicType.DoomClawSpit:
                            animation = MirAnimation.Combat7;
                            break;
                        case MagicType.DoomClawWave:
                            animation = MirAnimation.Combat6;
                            break;
                        case MagicType.DoomClawLeftPinch:
                            animation = MirAnimation.Combat4;
                            break;
                        case MagicType.DoomClawLeftSwipe:
                            animation = MirAnimation.Combat5;
                            break;
                        case MagicType.HellBringerBats:
                            animation = MirAnimation.Combat4;
                            break;
                        case MagicType.IgyuCyclone:
                            animation = MirAnimation.Combat4;
                            break;
                    }
                    break;
                case MirAction.Struck:
                    animation = MirAnimation.Struck;
                    break;
                case MirAction.Die:
                    animation = MirAnimation.Die;
                    break;
                case MirAction.Dead:
                    animation = !Skeleton ? MirAnimation.Dead : MirAnimation.Skeleton;
                    break;
                case MirAction.Show:
                    animation = MirAnimation.Show;
                    break;
                case MirAction.Hide:
                    animation = MirAnimation.Hide;
                    break;
                case MirAction.Idle:
                    {
                        animation = (int)action.Extra[0] switch
                        {
                            1 => MirAnimation.Combat1,
                            2 => MirAnimation.Combat2,
                            3 => MirAnimation.Combat3,
                            4 => MirAnimation.Combat4,
                            5 => MirAnimation.Combat5,
                            6 => MirAnimation.Combat6,
                            7 => MirAnimation.Combat7,
                            8 => MirAnimation.Combat8,
                            _ => MirAnimation.Standing,
                        };
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            CurrentAnimation = animation;
            if (!Frames.TryGetValue(CurrentAnimation, out CurrentFrame))
                CurrentFrame = Frame.EmptyFrame;
        }

        private static MirAnimation GetCustomActionAnimation(ObjectAction action)
        {
            return action.Action switch
            {
                MirAction.Standing => MirAnimation.Standing,
                MirAction.Moving => MirAnimation.Walking,
                MirAction.Pushed => MirAnimation.Pushed,
                MirAction.Attack => GetCombatAnimation(GetCustomActionVariant(action), MirAnimation.Combat1),
                MirAction.RangeAttack => GetCombatAnimation(GetCustomActionVariant(action), MirAnimation.Combat2),
                MirAction.Spell => GetCombatAnimation(GetCustomActionVariant(action), MirAnimation.Combat3),
                MirAction.Struck => MirAnimation.Struck,
                MirAction.Die => MirAnimation.Die,
                MirAction.Dead => MirAnimation.Dead,
                MirAction.Show => MirAnimation.Show,
                MirAction.Hide => MirAnimation.Hide,
                MirAction.Idle => action.Extra?.Length > 0 && action.Extra[0] is int value
                    ? value switch
                    {
                        1 => MirAnimation.Combat1,
                        2 => MirAnimation.Combat2,
                        3 => MirAnimation.Combat3,
                        4 => MirAnimation.Combat4,
                        5 => MirAnimation.Combat5,
                        6 => MirAnimation.Combat6,
                        7 => MirAnimation.Combat7,
                        8 => MirAnimation.Combat8,
                        _ => MirAnimation.Standing,
                    }
                    : MirAnimation.Standing,
                _ => MirAnimation.Standing,
            };
        }

        private static int GetCustomActionVariant(ObjectAction action)
        {
            return action.Action switch
            {
                MirAction.Attack => action.Extra?.Length > 3 && action.Extra[3] is int attackVariant ? attackVariant : 0,
                MirAction.RangeAttack => action.Extra?.Length > 4 && action.Extra[4] is int rangeVariant ? rangeVariant : 0,
                MirAction.Spell => action.Extra?.Length > 6 && action.Extra[6] is int magicVariant ? magicVariant : 0,
                _ => 0,
            };
        }

        public override void Draw()
        {
            if (!Visible || GetCurrentBodyLibrary() == null) return;

            int y = DrawY;

            switch (Image)
            {
                case MonsterImage.ChestnutTree:
                    y -= MapControl.CellHeight;
                    break;
                case MonsterImage.NewMob10:
                    y -= MapControl.CellHeight * 4;
                    break;
            }

            DrawShadow(DrawX, y);

            DrawBody(DrawX, y, MouseObject == this);
        }

        public void DrawShadow(int x, int y)
        {
            if (UsingCustomVisual)
            {
                MirLibrary library = GetCurrentBodyLibrary();
                if (CustomVisual?.DrawShadow == true && library != null)
                {
                    Point offset = GetCurrentCustomAnimationOffset();
                    library.Draw(BodyFrame, x + offset.X, y + offset.Y, Color.White, true, 0.5f, ImageType.Shadow, Scale);
                }

                return;
            }

            switch (Image)
            {
                case MonsterImage.None:
                    break;
                case MonsterImage.DustDevil:
                case MonsterImage.Tornado:
                case MonsterImage.SabukGateSouth:
                case MonsterImage.SabukGateNorth:
                case MonsterImage.SabukGateEast:
                case MonsterImage.SabukGateWest:
                    break;
                case MonsterImage.LobsterLord:
                    BodyLibrary.Draw(BodyFrame, x, y, Color.White, true, 0.5f, ImageType.Shadow, Scale);
                    BodyLibrary.Draw(BodyFrame + 1000, x, y, Color.White, true, 0.5f, ImageType.Shadow, Scale);
                    BodyLibrary.Draw(BodyFrame + 2000, x, y, Color.White, true, 0.5f, ImageType.Shadow, Scale);
                    break;
                default:
                    BodyLibrary.Draw(BodyFrame, x, y, Color.White, true, 0.5f, ImageType.Shadow, Scale);
                    break;
            }
        }

        public void DrawBody(int x, int y, bool mouseOver = false)
        {
            bool outlineEnabled = false;

            if (mouseOver && Config.ShowTargetOutline)
            {
                const int levelDiff = 2;

                Color result;

                if (PetOwner == GameScene.Game.User.Name)
                {
                    result = Config.TargetMonsterFriendlyColour;
                }
                else
                {
                    result = (GameScene.Game.User.Level - MonsterInfo.Level) switch
                    {
                        > levelDiff => Config.TargetMonsterLowLevelColour,
                        <= levelDiff and >= 0 => Config.TargetMonsterSameLevelColour,
                        _ => Config.TargetMonsterHighLevelColour
                    };
                }

                if (result != Color.FromArgb(0, 0, 0, 0))
                {
                    RenderingPipelineManager.EnableOutlineEffect(result, 2f);
                    outlineEnabled = true;
                }
            }

            if (UsingCustomVisual)
            {
                MirLibrary currentLibrary = GetCurrentBodyLibrary();
                if (currentLibrary == null)
                {
                    if (outlineEnabled)
                        RenderingPipelineManager.DisableOutlineEffect();

                    return;
                }

                Point offset = GetCurrentCustomAnimationOffset();
                x += offset.X;
                y += offset.Y;

                if (CustomVisual?.Blend == true)
                    currentLibrary.DrawBlend(BodyFrame, x, y, DrawColour, true, Opacity, ImageType.Image);
                else
                    currentLibrary.Draw(BodyFrame, x, y, DrawColour, true, Opacity, ImageType.Image, Scale);

                if (outlineEnabled)
                    RenderingPipelineManager.DisableOutlineEffect();

                return;
            }

            switch (Image)
            {
                case MonsterImage.None:
                    break;
                case MonsterImage.DustDevil:
                case MonsterImage.Tornado:
                    BodyLibrary.DrawBlend(BodyFrame, x, y, DrawColour, true, Opacity, ImageType.Image);
                    break;
                case MonsterImage.LobsterLord:
                    BodyLibrary.Draw(BodyFrame, x, y, DrawColour, true, Opacity, ImageType.Image, Scale);
                    BodyLibrary.Draw(BodyFrame + 1000, x, y, DrawColour, true, Opacity, ImageType.Image, Scale);
                    BodyLibrary.Draw(BodyFrame + 2000, x, y, DrawColour, true, Opacity, ImageType.Image, Scale);
                    break;
                case MonsterImage.CastleFlag:
                    BodyLibrary.Draw(BodyFrame, x, y, DrawColour, true, Opacity, ImageType.Image, Scale);
                    BodyLibrary.Draw(BodyFrame, DrawX, DrawY, Colour, true, 1F, ImageType.Overlay, Scale);
                    break;
                default:
                    BodyLibrary.Draw(BodyFrame, x, y, DrawColour, true, Opacity, ImageType.Image, Scale);
                    break;
            }

            MirLibrary library;
            switch (Image)
            {
                case MonsterImage.NewMob1:
                    if (CurrentAction == MirAction.Dead) break;
                    if (!CEnvir.LibraryList.TryGetValue(LibraryFile.MonMagicEx20, out library)) break;
                    library.DrawBlend(DrawFrame + 2000, x, y, Color.White, true, 1f, ImageType.Image);
                    break;
                case MonsterImage.NumaHighMage:
                    if (CurrentAction == MirAction.Dead) break;
                    if (!CEnvir.LibraryList.TryGetValue(LibraryFile.MonMagicEx4, out library)) break;
                    library.DrawBlend(DrawFrame + 500, x, y, Color.White, true, 1f, ImageType.Image);
                    break;
                case MonsterImage.InfernalSoldier:
                    if (CurrentAction == MirAction.Dead) break;
                    if (!CEnvir.LibraryList.TryGetValue(LibraryFile.MonMagicEx8, out library)) break;
                    library.DrawBlend(DrawFrame, x, y, Color.White, true, 1f, ImageType.Image);
                    library.DrawBlend(DrawFrame + 1000, x, y, Color.White, true, 1f, ImageType.Image);
                    break;
                case MonsterImage.JinamStoneGate:
                    if (CurrentAction == MirAction.Dead) break;
                    if (!CEnvir.LibraryList.TryGetValue(LibraryFile.MonMagicEx6, out library)) break;
                    library.DrawBlend((GameScene.Game.MapControl.Animation % 30) + 1400, x, y, Color.White, true, 1f, ImageType.Image);
                    break;
                case MonsterImage.FireBird:
                    if (CurrentAction == MirAction.Dead) break;
                    if (!CEnvir.LibraryList.TryGetValue(LibraryFile.MonMagicEx14, out library)) break;
                    library.DrawBlend(DrawFrame + 2000, x, y, Color.White, true, 1f, ImageType.Image);
                    break;
            }

            if (CompanionObject != null && CompanionObject.HeadShape > 0)
            {
                switch (Image)
                {
                    case MonsterImage.Companion_Pig:
                        if (!CEnvir.LibraryList.TryGetValue(LibraryFile.PEquipH1, out library)) break;
                        library.Draw(DrawFrame + (CompanionObject.HeadShape * 1000), x, y, Color.White, true, 1f, ImageType.Image, Scale);
                        break;
                }
            }

            if (CompanionObject != null && CompanionObject.BackShape > 0)
            {
                switch (Image)
                {
                    case MonsterImage.Companion_Pig:
                        if (!CEnvir.LibraryList.TryGetValue(LibraryFile.PEquipB1, out library)) break;
                        library.Draw(DrawFrame + (CompanionObject.BackShape * 1000), x, y, Color.White, true, 1f, ImageType.Image, Scale);
                        break;
                }
            }

            if (outlineEnabled)
            {
                RenderingPipelineManager.DisableOutlineEffect();
            }
        }
        public override void DrawHealth()
        {
            if (!Config.ShowMonsterHealth || ((CEnvir.Now > DrawHealthTime || !Visible) && PetOwner != User.Name)) return;

            if (MonsterInfo.AI < 0) return;

            ClientObjectData data;
            if (!GameScene.Game.DataDictionary.TryGetValue(ObjectID, out data)) return;

            if (data.MaxHealth == 0) return;

            MirLibrary library;

            if (!CEnvir.LibraryList.TryGetValue(LibraryFile.Interface, out library)) return;

            float percent = Math.Min(1, Math.Max(0, data.Health / (float)data.MaxHealth));

            if (percent == 0) return;

            Size size = library.GetSize(79);

            Color color = !string.IsNullOrEmpty(PetOwner) ? Color.Yellow : Color.FromArgb(0, 200, 74);

            library.Draw(80, DrawX, DrawY - 55, Color.White, false, 1F, ImageType.Image);
            library.Draw(79, DrawX + 1, DrawY - 55 + 1, color, new Rectangle(0, 0, (int)(size.Width * percent), size.Height), 1F, ImageType.Image);
        }

        public override void DrawBlend()
        {
            if (!Visible || GetCurrentBodyLibrary() == null) return;

            int y = DrawY;

            switch (Image)
            {
                case MonsterImage.ChestnutTree:
                    y -= MapControl.CellHeight;
                    break;
                case MonsterImage.JinamStoneGate:
                    if (!UsingCustomVisual)
                        return;
                    break;
            }

            RenderingPipelineManager.SetBlend(true, 0.20F, BlendMode.HIGHLIGHT);//0.60F
            DrawBody(DrawX, y);
            RenderingPipelineManager.SetBlend(false);
        }

        public override void DrawName()
        {
            if (!Visible) return;

            base.DrawName();
        }

        public override void CreateProjectile()
        {
            if (UsingCustomVisual)
                return;

            base.CreateProjectile();

            switch (Image)
            {
                case MonsterImage.SkeletonAxeThrower:
                    foreach (MapObject attackTarget in AttackTargets)
                    {
                        Effects.Add(new MirProjectile(800, 6, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagic, 0, 0, Globals.NoneColour, CurrentLocation)
                        {
                            Target = attackTarget,
                            Has16Directions = false,
                        });
                    }
                    break;

                case MonsterImage.AntNeedler:
                    foreach (MapObject attackTarget in AttackTargets)
                    {
                        Effects.Add(new MirProjectile(80, 6, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagic, 0, 0, Globals.NoneColour, CurrentLocation)
                        {
                            Target = attackTarget,
                            Skip = 0,
                        });
                    }
                    break;

                case MonsterImage.AntHealer:
                    foreach (MapObject attackTarget in AttackTargets)
                    {
                        Effects.Add(new MirEffect(100, 10, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagic, 20, 40, Globals.HolyColour)
                        {
                            Target = attackTarget,
                            Skip = 0,
                            Blend = true,
                        });
                    }
                    break;

                case MonsterImage.SpinedDarkLizard:
                    foreach (MapObject attackTarget in AttackTargets)
                    {
                        Effects.Add(new MirProjectile(1240, 1, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagic, 0, 0, Globals.NoneColour, CurrentLocation)
                        {
                            Target = attackTarget,
                            Skip = 10,
                        });
                    }
                    break;

                case MonsterImage.RedMoonTheFallen:
                    foreach (MapObject attackTarget in AttackTargets)
                    {
                        Effects.Add(new MirEffect(2230, 6, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagic, 0, 0, Globals.NoneColour)
                        {
                            MapTarget = attackTarget.CurrentLocation,
                            Skip = 0,
                        });
                    }
                    break;

                case MonsterImage.ZumaSharpShooter:
                case MonsterImage.BoneArcher:
                case MonsterImage.ArcherGuard:
                    foreach (MapObject attackTarget in AttackTargets)
                    {
                        Effects.Add(new MirProjectile(1070, 1, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagic, 0, 0, Globals.NoneColour, CurrentLocation)
                        {
                            Target = attackTarget,
                            Skip = 10,
                        });
                    }
                    break;
                case MonsterImage.Monkey:
                    foreach (MapObject attackTarget in AttackTargets)
                    {
                        Effects.Add(new MirProjectile(900, 1, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx2, 0, 0, Globals.NoneColour, CurrentLocation)
                        {
                            Target = attackTarget,
                            Skip = 10,
                            Has16Directions = false,
                        });
                    }
                    break;
                case MonsterImage.CannibalFanatic:
                    foreach (MapObject attackTarget in AttackTargets)
                    {
                        Effects.Add(new MirProjectile(0, 1, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx2, 0, 0, Globals.NoneColour, CurrentLocation)
                        {
                            Target = attackTarget,
                            Has16Directions = false,
                        });
                    }
                    break;
                case MonsterImage.CursedCactus:
                    foreach (MapObject attackTarget in AttackTargets)
                    {
                        Effects.Add(new MirProjectile(960, 1, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagic, 0, 0, Globals.NoneColour, CurrentLocation)
                        {
                            Target = attackTarget,
                            Skip = 0,
                        });
                    }
                    break;
                case MonsterImage.WindfurySorceress:
                    foreach (MapObject attackTarget in AttackTargets)
                    {
                        Effects.Add(new MirEffect(1570, 10, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagic, 20, 60, Globals.WindColour)
                        {
                            Target = attackTarget,
                            Blend = true,
                        });
                    }
                    break;
                case MonsterImage.SonicLizard:
                    Effects.Add(new MirEffect(1444, 6, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx4, 20, 60, Globals.FireColour)
                    {
                        Target = this,
                        Blend = true,
                        Direction = Direction,

                    });
                    break;
                case MonsterImage.GiantLizard:
                    foreach (MapObject attackTarget in AttackTargets)
                    {
                        Effects.Add(new MirEffect(5930, 4, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx4, 0, 0, Globals.NoneColour)
                        {
                            MapTarget = attackTarget.CurrentLocation,
                        });
                    }
                    break;
                case MonsterImage.CrazedLizard:
                    foreach (MapObject attackTarget in AttackTargets)
                    {
                        Effects.Add(new MirProjectile(5830, 3, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx4, 0, 0, Globals.NoneColour, CurrentLocation)
                        {
                            Target = attackTarget,
                            Has16Directions = false,
                            Blend = true,
                        });
                    }
                    break;
                case MonsterImage.EmperorSaWoo:
                    foreach (MapObject attackTarget in AttackTargets)
                    {
                        Effects.Add(new MirEffect(600, 10, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx, 60, 60, Globals.WindColour)
                        {
                            MapTarget = attackTarget.CurrentLocation,
                            Blend = true,
                        });
                    }
                    break;
                case MonsterImage.ArchLichTaedu:
                    foreach (MapObject attackTarget in AttackTargets)
                    {
                        Effects.Add(new MirProjectile(420, 5, TimeSpan.FromMilliseconds(100), LibraryFile.Magic, 30, 50, Globals.FireColour, CurrentLocation)
                        {
                            Target = attackTarget,
                            Blend = true,
                        });
                    }
                    break;
                case MonsterImage.RazorTusk:
                    foreach (MapObject attackTarget in AttackTargets)
                    {
                        Effects.Add(new MirEffect(1890, 6, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx, 30, 50, Globals.WindColour)
                        {
                            MapTarget = attackTarget.CurrentLocation,
                            Blend = true,
                            Direction = attackTarget.Direction,
                            BlendRate = 1F,
                        });
                    }
                    break;
                case MonsterImage.MutantCaptain:
                    Effects.Add(new MirEffect(560, 9, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx2, 20, 60, Globals.FireColour)
                    {
                        Target = this,
                        Blend = true,
                        Direction = Direction,
                    });
                    break;
                case MonsterImage.StoneGriffin:
                    foreach (MapObject attackTarget in AttackTargets)
                    {
                        Effects.Add(new MirProjectile(1080, 6, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx2, 0, 0, Globals.DarkColour, CurrentLocation)
                        {
                            Target = attackTarget,
                            Blend = true,
                        });
                    }
                    break;
                case MonsterImage.FlameGriffin:
                    foreach (MapObject attackTarget in AttackTargets)
                    {
                        Effects.Add(new MirProjectile(1080, 6, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx2, 0, 0, Globals.FireColour, CurrentLocation)
                        {
                            Target = attackTarget,
                            Blend = true,
                            DrawColour = Color.Orange,
                        });
                    }
                    break;
                case MonsterImage.NumaCavalry:
                    foreach (MapObject attackTarget in AttackTargets)
                    {
                        Effects.Add(new MirProjectile(0, 4, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx4, 0, 0, Globals.NoneColour, CurrentLocation)
                        {
                            Target = attackTarget,
                            Has16Directions = false,
                            Skip = 10,
                        });
                    }
                    break;
                case MonsterImage.NumaStoneThrower:
                    foreach (MapObject attackTarget in AttackTargets)
                    {
                        MirEffect effect;
                        Effects.Add(effect = new MirProjectile(0, 4, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx3, 0, 0, Globals.NoneColour, CurrentLocation)
                        {
                            Target = attackTarget,
                            Has16Directions = false,
                            Skip = 10,
                        });

                        effect.CompleteAction = () =>
                        {
                            attackTarget.Effects.Add(effect = new MirEffect(80, 10, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx3, 10, 35, Globals.NoneColour)
                            {
                                Blend = true,
                                Target = attackTarget,
                            });
                            effect.Process();

                            DXSoundManager.Play(SoundIndex.FireStormEnd);
                        };
                        effect.Process();
                    }
                    break;
                case MonsterImage.NumaRoyalGuard:
                    Effects.Add(new MirEffect(1440, 10, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx4, 20, 60, Globals.FireColour)
                    {
                        Target = this,
                        Blend = true,
                        Direction = Direction,
                    });
                    break;
                case MonsterImage.IcyGoddess:
                    foreach (MapObject attackTarget in AttackTargets)
                    {
                        Effects.Add(new MirProjectile(6200, 5, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx3, 0, 0, Globals.NoneColour, CurrentLocation)
                        {
                            Target = attackTarget,
                            Has16Directions = false,
                            Skip = 0,
                            Blend = true,
                        });
                    }
                    break;
                case MonsterImage.IcySpiritGeneral:
                case MonsterImage.IcySpiritWarrior:
                    foreach (MapObject attackTarget in AttackTargets)
                    {
                        Effects.Add(new MirEffect(580, 7, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx3, 0, 0, Globals.NoneColour)
                        {
                            Target = attackTarget,
                            Blend = true,
                        });
                    }
                    break;
                case MonsterImage.EvilElephant:
                case MonsterImage.SandShark:
                    Effects.Add(new MirEffect(320, 10, TimeSpan.FromMilliseconds(80), LibraryFile.MonMagic, 10, 35, Globals.DarkColour)
                    {
                        Blend = true,
                        Target = this,
                    });
                    break;
                case MonsterImage.GhostKnight:
                    Effects.Add(new MirEffect(6350, 10, TimeSpan.FromMilliseconds(80), LibraryFile.MonMagicEx3, 10, 35, Globals.DarkColour)
                    {
                        Blend = true,
                        Target = this,
                    });
                    break;
                case MonsterImage.IcyRanger:
                    foreach (MapObject attackTarget in AttackTargets)
                    {
                        Effects.Add(new MirProjectile(190, 1, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx3, 0, 0, Globals.NoneColour, CurrentLocation)
                        {
                            Target = attackTarget,
                            Has16Directions = false,
                        });
                    }
                    break;
                case MonsterImage.YumgonWitch:
                    Effects.Add(new MirEffect(20, 18, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx6, 20, 60, Globals.LightningColour)
                    {
                        Target = this,
                        Blend = true,
                    });
                    break;
                case MonsterImage.ChiwooGeneral:
                    Effects.Add(new MirEffect(1000, 15, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx6, 0, 0, Globals.NoneColour)
                    {
                        Target = this,
                        Blend = true,
                    });
                    break;
                case MonsterImage.DragonLord:
                    foreach (MapObject target in AttackTargets)
                    {
                        MirProjectile eff;
                        Point p = new Point(target.CurrentLocation.X + 4, target.CurrentLocation.Y - 10);
                        Effects.Add(eff = new MirProjectile(130, 10, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx6, 0, 0, Globals.NoneColour, p)
                        {
                            MapTarget = target.CurrentLocation,
                            Skip = 0,
                            Explode = true,
                            Blend = true,
                        });

                        eff.CompleteAction = () =>
                        {
                            Effects.Add(new MirEffect(140, 8, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx6, 0, 0, Globals.NoneColour)
                            {
                                MapTarget = eff.MapTarget,
                                Blend = true,
                            });
                        };
                    }
                    break;
                case MonsterImage.SamaCursedFlameMage:
                    foreach (MapObject attackTarget in AttackTargets)
                    {
                        MirEffect effect;
                        Effects.Add(effect = new MirProjectile(5000, 9, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx9, 0, 0, Globals.FireColour, CurrentLocation)
                        {
                            Target = attackTarget,
                            Has16Directions = false,
                            Skip = 10,
                            Blend = true,
                        });

                        effect.CompleteAction = () =>
                        {
                            attackTarget.Effects.Add(effect = new MirEffect(5100, 7, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx9, 10, 35, Globals.FireColour)
                            {
                                Blend = true,
                                Target = attackTarget,
                            });
                            effect.Process();
                        };
                        effect.Process();
                    }
                    break;
                case MonsterImage.HellBringer:
                    foreach (MapObject attackTarget in AttackTargets)
                    {
                        MirEffect effect;
                        Effects.Add(effect = new MirProjectile(1050, 4, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx13, 0, 0, Globals.NoneColour, CurrentLocation)
                        {
                            Target = attackTarget,
                            Blend = true,
                            Has16Directions = false,
                        });

                        effect.CompleteAction = () =>
                        {
                            attackTarget.Effects.Add(effect = new MirEffect(1140, 10, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx13, 10, 35, Globals.FireColour)
                            {
                                Blend = true,
                                Target = attackTarget,
                            });
                            effect.Process();
                        };
                        effect.Process();
                    }
                    break;
            }

        }

        public override bool MouseOver(Point p)
        {
            if (!Visible) return false;

            MirLibrary library = GetCurrentBodyLibrary();
            if (library == null) return false;

            if (UsingCustomVisual)
            {
                Point offset = GetCurrentCustomAnimationOffset();
                return library.VisiblePixel(BodyFrame, new Point(p.X - DrawX - offset.X, p.Y - DrawY - offset.Y), false, true);
            }

            switch (Image)
            {
                case MonsterImage.LobsterLord:
                    return library.VisiblePixel(BodyFrame, new Point(p.X - DrawX, p.Y - DrawY), false, true) ||
                           library.VisiblePixel(BodyFrame + 1000, new Point(p.X - DrawX, p.Y - DrawY), false, true) ||
                           library.VisiblePixel(BodyFrame + 2000, new Point(p.X - DrawX, p.Y - DrawY), false, true);
                default:
                    return library.VisiblePixel(BodyFrame, new Point(p.X - DrawX, p.Y - DrawY), false, true);
            }

        }

        public override void FrameIndexChanged()
        {
            if (UsingCustomVisual)
            {
                ProcessCustomFrameEvent();
                return;
            }

            switch (CurrentAction)
            {
                case MirAction.Attack:
                    if (FrameIndex == 1)
                        PlayAttackSound();

                    switch (Image)
                    {
                        case MonsterImage.RedBlossom:
                            if (FrameIndex == 6)
                            {
                                DXSoundManager.Play(SoundIndex.RedBlossomAttack);
                                Effects.Add(new MirEffect(400, 10, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx14, 0, 0, Globals.FireColour)
                                {
                                    Blend = true,
                                    Target = this,
                                    Direction = Direction,
                                });
                            }
                            break;
                        case MonsterImage.FireBird:
                            if (FrameIndex == 5)
                            {
                                DXSoundManager.Play(SoundIndex.FireBirdAttack);
                                Effects.Add(new MirEffect(700, 5, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx14, 0, 0, Globals.FireColour)
                                {
                                    Blend = true,
                                    Target = this,
                                    Direction = Direction,
                                });
                            }
                            break;
                    }
                    break;
                case MirAction.RangeAttack:
                    if (FrameIndex != 4) return;
                    if (MagicInfoIndex > 0 && CEnvir.TryGetCustomSkill(MagicInfoIndex, out _))
                    {
                        PlayAttackSound();
                        return;
                    }

                    CreateProjectile();
                    PlayAttackSound();
                    break;
                case MirAction.Struck:
                    if (GameScene.Game.StruckEnabled)
                    {
                        if (FrameIndex == 0)
                            PlayStruckSound();
                    }
                    break;
                case MirAction.Die:
                    if (FrameIndex == 0)
                        PlayDieSound();
                    break;
            }
        }

        private void ProcessCustomFrameEvent()
        {
            CustomMonsterAnimationInfo animation = GetCurrentCustomAnimation();
            int triggerFrame = GetCustomActionTriggerFrame(animation);

            if (FrameIndex != triggerFrame) return;

            PlayCustomActionSound(animation);
            CreateCustomAnimationEffect(animation);

            if (CurrentAction != MirAction.RangeAttack) return;

            if (MagicInfoIndex > 0 && CEnvir.TryGetCustomSkill(MagicInfoIndex, out _))
                return;

            CreateProjectile();
        }

        private CustomMonsterAnimationInfo GetCurrentCustomAnimation()
        {
            if (CustomAnimationMap != null && CustomAnimationMap.TryGetValue(CurrentAnimation, out CustomMonsterAnimationInfo animation))
                return animation;

            return null;
        }

        private MirLibrary GetCurrentBodyLibrary()
        {
            if (UsingCustomVisual && CustomAnimationLibraryMap != null && CustomAnimationLibraryMap.TryGetValue(CurrentAnimation, out MirLibrary library) && library != null)
                return library;

            return BodyLibrary;
        }

        private Point GetCurrentCustomAnimationOffset()
        {
            CustomMonsterAnimationInfo animation = GetCurrentCustomAnimation();
            return animation == null ? Point.Empty : new Point(animation.OffsetX, animation.OffsetY);
        }

        private int GetCustomActionTriggerFrame(CustomMonsterAnimationInfo animation)
        {
            int maxFrame = Math.Max(0, (CurrentFrame?.FrameCount ?? 1) - 1);

            if (animation != null && animation.EffectTriggerFrame >= 0)
                return Math.Min(maxFrame, animation.EffectTriggerFrame);

            return CurrentAction switch
            {
                MirAction.Attack => Math.Min(maxFrame, 1),
                MirAction.RangeAttack => Math.Min(maxFrame, 4),
                MirAction.Spell => Math.Min(maxFrame, 1),
                MirAction.Struck => 0,
                MirAction.Die => 0,
                MirAction.Show => 0,
                MirAction.Hide => 0,
                _ => 0,
            };
        }

        private void PlayCustomActionSound(CustomMonsterAnimationInfo animation)
        {
            if (CurrentAction == MirAction.Struck && !GameScene.Game.StruckEnabled)
                return;

            SoundIndex sound = animation?.Sound ?? SoundIndex.None;

            if (sound == SoundIndex.None)
            {
                sound = CurrentAction switch
                {
                    MirAction.Attack => CustomVisual?.AttackSound ?? SoundIndex.None,
                    MirAction.RangeAttack => CustomVisual?.RangeAttackSound ?? CustomVisual?.AttackSound ?? SoundIndex.None,
                    MirAction.Spell => CustomVisual?.MagicSound ?? CustomVisual?.AttackSound ?? SoundIndex.None,
                    MirAction.Struck => CustomVisual?.StruckSound ?? SoundIndex.None,
                    MirAction.Die => CustomVisual?.DieSound ?? SoundIndex.None,
                    MirAction.Show => CustomVisual?.ShowSound ?? SoundIndex.None,
                    MirAction.Hide => CustomVisual?.HideSound ?? SoundIndex.None,
                    _ => SoundIndex.None,
                };
            }

            if (sound != SoundIndex.None)
                DXSoundManager.Play(sound);

            if (CurrentAction == MirAction.Struck && GameScene.Game.StruckEnabled)
                DXSoundManager.Play(SoundIndex.GenericStruckMonster);
        }

        private void CreateCustomAnimationEffect(CustomMonsterAnimationInfo animation)
        {
            if (animation?.EffectResource == null || !CEnvir.TryGetCustomLibrary(animation.EffectResource, out MirLibrary library))
                return;

            switch (animation.EffectTarget)
            {
                case CustomMonsterEffectTarget.CurrentTarget:
                    CreateCustomAnimationEffect(animation, library, AttackTargets?.FirstOrDefault());
                    break;
                case CustomMonsterEffectTarget.AttackTargets:
                    if (AttackTargets == null || AttackTargets.Count == 0)
                        CreateCustomAnimationEffect(animation, library, this);
                    else
                        foreach (MapObject target in AttackTargets)
                            CreateCustomAnimationEffect(animation, library, target);
                    break;
                case CustomMonsterEffectTarget.CurrentLocation:
                    CreateCustomAnimationEffect(animation, library, CurrentLocation);
                    break;
                case CustomMonsterEffectTarget.MagicLocations:
                    if (MagicLocations == null || MagicLocations.Count == 0)
                        CreateCustomAnimationEffect(animation, library, CurrentLocation);
                    else
                        foreach (Point point in MagicLocations)
                            CreateCustomAnimationEffect(animation, library, point);
                    break;
                case CustomMonsterEffectTarget.Self:
                default:
                    CreateCustomAnimationEffect(animation, library, this);
                    break;
            }
        }

        private void CreateCustomAnimationEffect(CustomMonsterAnimationInfo animation, MirLibrary library, MapObject target)
        {
            if (target == null) return;

            MirEffect effect = BuildCustomAnimationEffect(animation, library);
            effect.Target = target;
            Effects.Add(effect);
            effect.Process();
        }

        private void CreateCustomAnimationEffect(CustomMonsterAnimationInfo animation, MirLibrary library, Point target)
        {
            MirEffect effect = BuildCustomAnimationEffect(animation, library);
            effect.MapTarget = target;
            Effects.Add(effect);
            effect.Process();
        }

        private static MirEffect BuildCustomAnimationEffect(CustomMonsterAnimationInfo animation, MirLibrary library)
        {
            int frameCount = animation.EffectFrameCount > 0
                ? animation.EffectFrameCount
                : Math.Max(1, animation.EffectEndIndex - animation.EffectStartIndex + 1);

            ReportMissingCustomAnimationEffectFrames(animation, library, frameCount);

            MirEffect effect = new(
                animation.EffectStartIndex,
                frameCount,
                TimeSpan.FromMilliseconds(Math.Max(1, animation.EffectFrameDelay)),
                library,
                animation.EffectStartLight,
                animation.EffectEndLight,
                Globals.NoneColour)
            {
                AdditionalOffSet = new Point(animation.EffectOffsetX, animation.EffectOffsetY),
                Blend = animation.EffectBlend,
                Loop = animation.EffectLoop,
                StartTime = CEnvir.Now.AddMilliseconds(Math.Max(0, animation.EffectStartDelay)),
            };

            if (animation.EffectDuration > 0)
                effect.ExpireTime = effect.StartTime.AddMilliseconds(animation.EffectDuration);

            return effect;
        }

        private static void ReportMissingCustomAnimationEffectFrames(CustomMonsterAnimationInfo animation, MirLibrary library, int frameCount)
        {
            if (animation == null || library == null || animation.EffectResource == null) return;

            int startIndex = animation.EffectStartIndex;
            int maxIndex = startIndex + Math.Max(1, frameCount) - 1;

            if (library.HasImage(startIndex) && library.HasImage(maxIndex)) return;

            CEnvir.AddDynamicLibraryDiagnostic(
                $"自定义怪物动作特效帧缺失: Action={animation.Action} Variant={animation.Variant} Resource={GetCustomResourceName(animation.EffectResource)} Start={animation.EffectStartIndex} End={animation.EffectEndIndex} FrameCount={frameCount} MaxIndex={maxIndex}");
        }

        public override void SetAction(ObjectAction action)
        {
            if (UsingCustomVisual)
            {
                base.SetAction(action);
                return;
            }

            switch (Image)
            {
                case MonsterImage.Shinsu:
                    switch (CurrentAction) // Old Action
                    {
                        case MirAction.Hide:
                            Extra = false;
                            UpdateLibraries();
                            break;
                        case MirAction.Dead:
                            Visible = true;
                            break;
                    }
                    switch (action.Action) //Mew Action
                    {
                        case MirAction.Show:
                            Extra = true;
                            DXSoundManager.Play(SoundIndex.ShinsuShow);
                            UpdateLibraries();
                            break;
                        case MirAction.Hide:
                            DXSoundManager.Play(SoundIndex.ShinsuBigAttack);
                            break;
                        case MirAction.Dead:
                            Visible = false;
                            break;
                    }
                    break;
                case MonsterImage.InfernalSoldier:
                    switch (CurrentAction) // Old Action
                    {
                        case MirAction.Dead:
                            Visible = true;
                            break;
                    }
                    switch (action.Action) //Mew Action
                    {
                        case MirAction.Dead:
                            Visible = false;
                            break;
                    }
                    break;
                case MonsterImage.CarnivorousPlant:
                case MonsterImage.LordNiJae:
                    if (CurrentAction == MirAction.Hide)
                        Visible = false;

                    if (action.Action == MirAction.Show)
                        Visible = true;
                    break;
                case MonsterImage.StoneGolem:
                    switch (action.Action)
                    {
                        case MirAction.Show:
                            DXSoundManager.Play(SoundIndex.StoneGolemAppear);
                            new MirEffect(200, 1, TimeSpan.FromMinutes(1), LibraryFile.ProgUse, 0, 0, Globals.NoneColour)
                            {
                                MapTarget = action.Location,
                                DrawType = DrawType.Floor,
                                Direction = Direction,
                                Skip = 1,
                            };
                            break;
                    }
                    break;
                case MonsterImage.ZumaFanatic:
                case MonsterImage.ZumaGuardian:
                    switch (CurrentAction)
                    {
                        case MirAction.Show:
                            Extra = true;
                            break;
                    }
                    break;
                case MonsterImage.ZumaKing:
                    switch (CurrentAction)
                    {
                        case MirAction.Show:
                            Extra = true;
                            new MirEffect(210, 1, TimeSpan.FromMinutes(1), LibraryFile.ProgUse, 0, 0, Globals.NoneColour)
                            {
                                MapTarget = action.Location,
                                DrawType = DrawType.Floor,
                            };
                            break;
                    }
                    break;
            }

            base.SetAction(action);

            switch (Image)
            {
                case MonsterImage.Scarecrow:
                    switch (action.Action)
                    {
                        case MirAction.Die:
                            Effects.Add(new MirEffect(680, 10, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagic, 20, 40, Globals.FireColour)
                            {
                                Target = this,
                                Blend = true,
                            });
                            break;
                    }
                    break;
                case MonsterImage.Skeleton:
                case MonsterImage.SkeletonAxeThrower:
                case MonsterImage.SkeletonWarrior:
                case MonsterImage.SkeletonLord:
                    switch (action.Action)
                    {
                        case MirAction.Die:
                            Effects.Add(new MirEffect(1920, 10, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagic, 20, 40, Globals.FireColour)
                            {
                                Target = this,
                                Blend = true,
                            });
                            break;
                    }
                    break;
                case MonsterImage.GhostSorcerer:
                    switch (action.Action)
                    {
                        case MirAction.Attack:
                            Effects.Add(new MirEffect(600, 6, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagic, 20, 40, Globals.LightningColour)
                            {
                                Target = this,
                                Direction = action.Direction,
                                Blend = true,
                            });
                            break;
                        case MirAction.Die:
                            Effects.Add(new MirEffect(700, 8, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagic, 20, 40, Globals.LightningColour)
                            {
                                Target = this,
                                Blend = true,
                            });
                            break;
                    }
                    break;
                case MonsterImage.CaveMaggot:
                    switch (action.Action)
                    {
                        case MirAction.Attack:
                            Effects.Add(new MirEffect(1940, 5, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagic, 0, 0, Globals.DarkColour)
                            {
                                Target = this,
                                Direction = action.Direction,
                                StartTime = CEnvir.Now.AddMilliseconds(200),
                                Blend = true,
                            });
                            break;
                    }
                    break;
                case MonsterImage.LordNiJae:
                    switch (action.Action)
                    {
                        case MirAction.Attack:
                            MirEffect effect;
                            Effects.Add(effect = new MirEffect(361, 9, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagic, 0, 0, Globals.DarkColour)
                            {
                                Target = this,
                                Direction = action.Direction,
                                StartTime = CEnvir.Now.AddMilliseconds(400),
                                Blend = true,
                            });
                            effect.Process();

                            break;
                    }
                    break;
                case MonsterImage.RottingGhoul:
                    switch (action.Action)
                    {
                        case MirAction.Die:
                            Effects.Add(new MirEffect(490, 10, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx, 20, 40, Globals.LightningColour)
                            {
                                Target = this,
                                Blend = true,
                            });
                            break;
                    }
                    break;
                case MonsterImage.DecayingGhoul:
                    switch (action.Action)
                    {
                        case MirAction.Attack:
                            Effects.Add(new MirEffect(310, 6, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx, 20, 40, Globals.LightningColour)
                            {
                                Target = this,
                                Direction = action.Direction,
                                StartTime = CEnvir.Now.AddMilliseconds(400),
                                Blend = true,
                            });
                            break;
                        case MirAction.Die:
                            Effects.Add(new MirEffect(490, 10, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx, 20, 40, Globals.LightningColour)
                            {
                                Target = this,
                                Blend = true,
                            });
                            break;
                    }
                    break;
                case MonsterImage.UmaFlameThrower:
                    switch (action.Action)
                    {
                        case MirAction.Attack:
                            Effects.Add(new MirEffect(520, 6, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagic, 20, 40, Globals.FireColour)
                            {
                                Target = this,
                                Direction = action.Direction,
                                Blend = true,
                            });
                            break;
                    }
                    break;
                case MonsterImage.UmaKing:
                    switch (action.Action)
                    {
                        case MirAction.Attack:
                            Effects.Add(new MirEffect(440, 6, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagic, 50, 80, Globals.LightningColour)
                            {
                                Target = this,
                                Direction = action.Direction,
                                Blend = true,
                            });
                            break;
                    }
                    break;
                case MonsterImage.ZumaKing:
                    switch (action.Action)
                    {
                        case MirAction.Attack:
                            Effects.Add(new MirEffect(720, 8, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagic, 0, 0, Globals.FireColour)
                            {
                                Target = this,
                                Direction = action.Direction,
                                Blend = true,
                            });
                            break;
                        case MirAction.Show:
                            DXSoundManager.Play(SoundIndex.ZumaKingAppear);
                            break;
                    }
                    break;
                case MonsterImage.BanyaLeftGuard:
                    switch (action.Action)
                    {
                        case MirAction.Attack:
                            Effects.Add(new MirEffect(100, 6, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx, 0, 0, Globals.FireColour)
                            {
                                Target = this,
                                Direction = action.Direction,
                                Blend = true,
                            });
                            break;
                        case MirAction.Die:
                            Effects.Add(new MirEffect(200, 5, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx, 0, 0, Globals.FireColour)
                            {
                                Target = this,
                                Blend = true,
                            });
                            break;
                    }
                    break;
                case MonsterImage.BanyaRightGuard:
                    switch (action.Action)
                    {
                        case MirAction.Attack:
                            Effects.Add(new MirEffect(0, 6, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx, 0, 0, Globals.LightningColour)
                            {
                                Target = this,
                                Direction = action.Direction,
                                Blend = true,
                            });
                            break;
                        case MirAction.Die:
                            Effects.Add(new MirEffect(90, 5, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx, 0, 0, Globals.LightningColour)
                            {
                                Target = this,
                                Blend = true,
                            });
                            break;
                    }
                    break;
                case MonsterImage.EmperorSaWoo:
                    switch (action.Action)
                    {
                        case MirAction.Attack:
                            Effects.Add(new MirEffect(510, 6, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx, 0, 0, Globals.FireColour)
                            {
                                Target = this,
                                Direction = action.Direction,
                                Blend = true,
                            });
                            break;
                    }
                    break;
                case MonsterImage.BoneArcher:
                case MonsterImage.BoneSoldier:
                case MonsterImage.BoneBladesman:
                    switch (action.Action)
                    {
                        case MirAction.Die:
                            Effects.Add(new MirEffect(630, 8, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx, 0, 0, Globals.NoneColour)
                            {
                                Target = this,
                                Blend = true,
                            });
                            break;
                    }
                    break;
                case MonsterImage.BoneCaptain:
                    switch (action.Action)
                    {
                        case MirAction.Die:
                            Effects.Add(new MirEffect(650, 10, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx, 0, 0, Globals.NoneColour)
                            {
                                Target = this,
                                Direction = action.Direction,
                                Blend = true,
                            });
                            break;
                    }
                    break;
                case MonsterImage.ArchLichTaedu:
                    switch (action.Action)
                    {
                        case MirAction.RangeAttack:
                            Effects.Add(new MirEffect(1470, 6, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx, 0, 0, Globals.NoneColour)
                            {
                                Target = this,
                                Direction = action.Direction,
                                Blend = true,
                            });
                            break;
                        case MirAction.Show:
                            Effects.Add(new MirEffect(1390, 6, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx, 0, 0, Globals.NoneColour)
                            {
                                Target = this,
                                Direction = action.Direction,
                                Blend = true,
                            });
                            break;
                        case MirAction.Die:
                            Effects.Add(new MirEffect(1630, 17, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx, 0, 0, Globals.NoneColour)
                            {
                                Target = this,
                                Direction = action.Direction,
                                Blend = true,
                                Skip = 20,
                            });
                            break;
                    }
                    break;
                case MonsterImage.RazorTusk:
                    switch (action.Action)
                    {
                        case MirAction.Attack:
                            Effects.Add(new MirEffect(1800, 6, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx, 20, 40, Globals.FireColour)
                            {
                                Target = this,
                                Direction = action.Direction,
                                Blend = true,
                            });
                            break;
                    }
                    break;
                case MonsterImage.Shinsu:
                    switch (action.Action)
                    {
                        case MirAction.Attack:
                            Effects.Add(new MirEffect(980, 6, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagic, 20, 40, Globals.PhantomColour)
                            {
                                Target = this,
                                Blend = true,
                                Direction = action.Direction,
                                StartTime = CEnvir.Now.AddMilliseconds(400),
                            });
                            break;
                    }
                    break;
                case MonsterImage.Stomper:
                    switch (action.Action)
                    {
                        case MirAction.Attack:
                            Effects.Add(new MirEffect(1779, 8, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagic, 0, 0, Globals.NoneColour)
                            {
                                Target = this,
                                Blend = true,
                            });
                            break;
                    }
                    break;
                case MonsterImage.PachonTheChaosBringer:
                    switch (action.Action)
                    {
                        case MirAction.Attack:
                            Effects.Add(new MirEffect(1800, 10, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagic, 0, 0, Globals.NoneColour)
                            {
                                Target = this,
                                Direction = action.Direction,
                                Blend = true,
                            });
                            break;
                        case MirAction.Die:
                            Effects.Add(new MirEffect(1890, 18, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagic, 0, 0, Globals.NoneColour)
                            {
                                Target = this,
                                Blend = true,
                            });
                            break;
                    }
                    break;
                case MonsterImage.JinchonDevil:
                    switch (CurrentAction)
                    {
                        case MirAction.RangeAttack:
                            Effects.Add(new MirEffect(760, 9, TimeSpan.FromMilliseconds(70), LibraryFile.MonMagicEx2, 10, 35, Globals.DarkColour)
                            {
                                Blend = true,
                                Target = this,
                                Direction = Direction,
                            });
                            break;
                        case MirAction.Attack:
                            Effects.Add(new MirEffect(990, 9, TimeSpan.FromMilliseconds(70), LibraryFile.MonMagicEx2, 10, 35, Globals.DarkColour)
                            {
                                Blend = true,
                                Target = this,
                                Direction = Direction,
                            });
                            break;
                    }
                    break;
                case MonsterImage.EmeraldDancer:
                    switch (CurrentAction)
                    {
                        case MirAction.Attack:
                            Effects.Add(new MirEffect(290, 20, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx5, 10, 35, Globals.DarkColour)
                            {
                                Blend = true,
                                Target = this,
                                Direction = Direction,
                                Skip = 20,
                            });
                            break;
                        case MirAction.RangeAttack:
                            Effects.Add(new MirEffect(540, 20, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx5, 10, 35, Globals.DarkColour)
                            {
                                Blend = true,
                                Target = this,
                            });
                            break;
                    }
                    break;
                case MonsterImage.FieryDancer:
                    switch (CurrentAction)
                    {
                        case MirAction.Attack:
                            Effects.Add(new MirEffect(570, 10, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx5, 10, 35, Globals.FireColour)
                            {
                                Blend = true,
                                Target = this,
                            });
                            break;
                        case MirAction.RangeAttack:
                            Effects.Add(new MirEffect(620, 10, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx5, 10, 35, Globals.FireColour)
                            {
                                Blend = true,
                                Target = this,
                            });
                            break;
                    }
                    break;
                case MonsterImage.QueenOfDawn:
                    switch (CurrentAction)
                    {
                        case MirAction.Attack:
                            Effects.Add(new MirEffect(680, 6, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx5, 10, 35, Globals.HolyColour)
                            {
                                Blend = true,
                                Target = this,
                                Direction = Direction,
                            });
                            break;
                        case MirAction.RangeAttack:
                            Effects.Add(new MirEffect(460, 11, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx5, 30, 80, Globals.HolyColour)
                            {
                                Blend = true,
                                Target = this,
                            });
                            break;
                    }
                    break;
                case MonsterImage.OYoungBeast:
                    switch (CurrentAction)
                    {
                        case MirAction.RangeAttack:
                            Effects.Add(new MirEffect(600, 10, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx6, 0, 0, Globals.NoneColour)
                            {
                                Blend = true,
                                Target = this,
                                Direction = Direction
                            });
                            break;
                    }
                    break;
                case MonsterImage.MaWarlord:
                    switch (CurrentAction)
                    {
                        case MirAction.Attack:
                            Effects.Add(new MirEffect(1100, 10, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx6, 0, 0, Globals.NoneColour)
                            {
                                Blend = true,
                                Target = this,
                                Direction = Direction
                            });
                            break;
                    }
                    break;
                case MonsterImage.DragonQueen:
                    switch (CurrentAction)
                    {
                        case MirAction.Attack:
                            Effects.Add(new MirEffect(500, 20, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx6, 10, 35, Globals.DarkColour)
                            {
                                Blend = true,
                                Target = this,
                            });
                            break;
                    }
                    break;
                case MonsterImage.FerociousIceTiger:
                    switch (CurrentAction)
                    {
                        case MirAction.Attack:
                            Effects.Add(new MirEffect(700, 7, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx7, 0, 0, Globals.NoneColour)
                            {
                                Blend = true,
                                MapTarget = Functions.Move(CurrentLocation, Direction, 3),
                                StartTime = CEnvir.Now.AddMilliseconds(600)
                            });
                            break;
                        case MirAction.RangeAttack:
                            Effects.Add(new MirEffect(801, 16, TimeSpan.FromMilliseconds(40), LibraryFile.MonMagicEx7, 0, 0, Globals.NoneColour)
                            {
                                Target = this,
                                Blend = true,
                            });
                            Effects.Add(new MirEffect(801, 16, TimeSpan.FromMilliseconds(40), LibraryFile.MonMagicEx7, 0, 0, Globals.NoneColour)
                            {
                                Target = this,
                                Blend = true,
                                StartTime = CEnvir.Now.AddMilliseconds(150),
                            });
                            Effects.Add(new MirEffect(801, 16, TimeSpan.FromMilliseconds(40), LibraryFile.MonMagicEx7, 0, 0, Globals.NoneColour)
                            {
                                Target = this,
                                Blend = true,
                                StartTime = CEnvir.Now.AddMilliseconds(300),
                            });
                            Effects.Add(new MirEffect(801, 16, TimeSpan.FromMilliseconds(40), LibraryFile.MonMagicEx7, 0, 0, Globals.NoneColour)
                            {
                                Target = this,
                                Blend = true,
                                StartTime = CEnvir.Now.AddMilliseconds(450),
                            });
                            break;
                    }
                    break;
                case MonsterImage.NewMob1:
                    switch (action.Action)
                    {
                        case MirAction.Attack:
                            Effects.Add(new MirEffect(1500, 7, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx20, 20, 40, Color.Purple)
                            {
                                Target = this,
                                Direction = action.Direction,
                                StartTime = CEnvir.Now.AddMilliseconds(200),
                                Blend = true,
                            });
                            break;
                        case MirAction.RangeAttack:
                            Effects.Add(new MirEffect(1500, 7, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx20, 20, 50, Globals.IceColour)
                            {
                                Target = this,
                                Direction = action.Direction,
                                StartTime = CEnvir.Now.AddMilliseconds(200),
                                Blend = true,
                            });
                            break;
                    }
                    break;
                case MonsterImage.MonasteryMon4:
                    switch (action.Action)
                    {
                        case MirAction.Attack:
                            Effects.Add(new MirEffect(2600, 7, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx23, 20, 40, Color.GreenYellow)
                            {
                                Target = this,
                                Direction = action.Direction,
                                StartTime = CEnvir.Now.AddMilliseconds(200),
                                Blend = true,
                            });
                            break;
                        case MirAction.RangeAttack:
                            Effects.Add(new MirEffect(2600, 7, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx23, 20, 50, Color.GreenYellow)
                            {
                                Target = this,
                                Direction = action.Direction,
                                StartTime = CEnvir.Now.AddMilliseconds(200),
                                Blend = true,
                            });
                            break;
                    }
                    break;
                case MonsterImage.NewMob3:
                    switch (action.Action)
                    {
                        case MirAction.Attack:
                            Effects.Add(new MirEffect(2700, 7, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx20, 20, 50, Globals.IceColour)
                            {
                                Target = this,
                                Direction = action.Direction,
                                StartTime = CEnvir.Now.AddMilliseconds(200),
                                Blend = true,
                            });
                            break;
                    }
                    break;
                case MonsterImage.NewMob10:
                    switch (action.Action)
                    {
                        case MirAction.Show:
                            Effects.Add(new MirEffect(3100, 18, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx20, 20, 90, Color.Purple)
                            {
                                Target = this,
                                Direction = action.Direction,
                                Blend = true,
                                Skip = 0,
                            });
                            break;
                    }
                    break;
                case MonsterImage.NewMob6:
                    switch (action.Action)
                    {
                        case MirAction.Attack:
                            Effects.Add(new MirEffect(2900, 6, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx20, 0, 0, Globals.NoneColour)
                            {
                                Target = this,
                                Direction = action.Direction,
                                Blend = true,
                            });
                            break;
                    }
                    break;
                case MonsterImage.NewMob8:
                    switch (action.Action)
                    {
                        case MirAction.Show:
                            Effects.Add(new MirEffect(3220, 10, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx20, 0, 0, Globals.NoneColour)
                            {
                                Target = this,
                                Blend = true,
                            });
                            break;
                        case MirAction.Attack:
                            Effects.Add(new MirEffect(3200, 8, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx20, 0, 0, Globals.NoneColour)
                            {
                                Target = this,
                                Blend = true,
                            });
                            break;
                    }
                    break;
                case MonsterImage.NewMob4:
                case MonsterImage.NewMob5:
                    switch (action.Action)
                    {
                        case MirAction.Attack:
                            Effects.Add(new MirEffect(3200, 7, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx20, 0, 0, Globals.NoneColour)
                            {
                                Target = this,
                                Blend = true,
                            });
                            break;
                    }
                    break;
                case MonsterImage.HellBringer:
                    switch (CurrentAction)
                    {
                        case MirAction.RangeAttack:
                            DXSoundManager.Play(SoundIndex.HellBringerAttack2);
                            Effects.Add(new MirEffect(963, 10, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx13, 10, 35, Globals.FireColour)
                            {
                                Blend = true,
                                Target = this,
                                Direction = Direction,
                            });
                            break;
                        case MirAction.Attack:
                            DXSoundManager.Play(SoundIndex.HellBringerAttack);
                            Effects.Add(new MirEffect(760, 10, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx13, 10, 35, Globals.FireColour)
                            {
                                Blend = true,
                                Target = this,
                                Direction = Direction,
                            });
                            break;
                        case MirAction.Spell:
                            DXSoundManager.Play(SoundIndex.HellBringerAttack3);
                            switch (CurrentAnimation)
                            {
                                case MirAnimation.Combat3:
                                    Effects.Add(new MirEffect(870, 10, TimeSpan.FromMilliseconds(80), LibraryFile.MonMagicEx13, 10, 35, Globals.FireColour)
                                    {
                                        Blend = true,
                                        Target = this,
                                        Direction = Direction,
                                    });
                                    break;
                                case MirAnimation.Combat4:
                                    Effects.Add(new MirEffect(1180, 14, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx13, 10, 35, Globals.NoneColour)
                                    {
                                        Blend = true,
                                        Target = this,
                                        Direction = Direction,
                                    });
                                    break;
                            }
                            break;
                        case MirAction.Die:
                            Effects.Add(new MirEffect(1180, 14, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx13, 10, 35, Globals.NoneColour)
                            {
                                Blend = true,
                                Target = this,
                                Direction = MirDirection.Up,
                            });
                            break;
                    }
                    break;
                case MonsterImage.WhiteBeardedTiger:
                    switch (CurrentAction)
                    {
                        case MirAction.Spell:
                            Effects.Add(new MirEffect(1270, 5, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx13, 0, 0, Globals.NoneColour)
                            {
                                Blend = true,
                                Target = this,
                                Direction = Direction,
                            });
                            break;
                    }
                    break;
                case MonsterImage.CursedSlave1:
                    switch (CurrentAction)
                    {
                        case MirAction.Attack:
                            DXSoundManager.Play(SoundIndex.CursedSlave1Attack);
                            Effects.Add(new MirEffect(1570, 6, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx13, 0, 0, Globals.NoneColour)
                            {
                                Blend = true,
                                Target = this,
                                Direction = Direction,
                            });
                            break;
                        case MirAction.Spell:
                            DXSoundManager.Play(SoundIndex.CursedSlave1Attack2);
                            Effects.Add(new MirEffect(1650, 7, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx13, 0, 0, Globals.NoneColour)
                            {
                                Blend = true,
                                Target = this,
                                Direction = Direction,
                            });
                            break;
                    }
                    break;
                case MonsterImage.CursedSlave2:
                    switch (CurrentAction)
                    {
                        case MirAction.Attack:
                            Effects.Add(new MirEffect(2050, 6, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx13, 0, 0, Globals.NoneColour)
                            {
                                Blend = true,
                                Target = this,
                                Direction = Direction,
                            });
                            break;
                        case MirAction.RangeAttack:
                            Effects.Add(new MirEffect(2140, 10, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx13, 10, 35, Globals.NoneColour)
                            {
                                Blend = true,
                                Target = this,
                                Direction = Direction,
                            });
                            foreach (MapObject attacktarget in AttackTargets)
                            {
                                Effects.Add(new MirEffect(2160, 10, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx13, 0, 0, Globals.NoneColour)
                                {
                                    Blend = true,
                                    Target = attacktarget,
                                    Direction = Direction,
                                });
                            }
                            break;
                    }
                    break;
                case MonsterImage.CursedSlave3:
                    switch (CurrentAction)
                    {
                        case MirAction.Attack:
                            DXSoundManager.Play(SoundIndex.CursedSlave3Attack);
                            Effects.Add(new MirEffect(1850, 7, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx13, 0, 0, Globals.NoneColour)
                            {
                                Blend = true,
                                Target = this,
                                Direction = Direction,
                            });
                            break;
                        case MirAction.RangeAttack:
                            DXSoundManager.Play(SoundIndex.CursedSlave3Attack2);
                            Effects.Add(new MirEffect(1940, 6, TimeSpan.FromMilliseconds(130), LibraryFile.MonMagicEx13, 10, 35, Globals.NoneColour)
                            {
                                Blend = true,
                                Target = this,
                            });
                            break;
                    }
                    break;
                case MonsterImage.PoisonousGolem:
                    switch (CurrentAction)
                    {
                        case MirAction.Attack:
                            DXSoundManager.Play(SoundIndex.PoisonousGolemAttack);
                            break;
                        case MirAction.Spell:
                            DXSoundManager.Play(SoundIndex.PoisonousGolemAttack2);
                            break;
                    }
                    break;
                case MonsterImage.GardenSoldier:
                    switch (CurrentAction)
                    {
                        case MirAction.Attack:
                            DXSoundManager.Play(SoundIndex.GardenSoldierAttack);
                            Effects.Add(new MirEffect(0, 7, TimeSpan.FromMilliseconds(120), LibraryFile.MonMagicEx14, 0, 0, Globals.NoneColour)
                            {
                                Blend = true,
                                Target = this,
                                Direction = Direction,
                            });
                            break;
                        case MirAction.RangeAttack:
                            DXSoundManager.Play(SoundIndex.GardenSoldierAttack2);
                            foreach (MapObject attacktarget in AttackTargets)
                            {
                                Effects.Add(new MirEffect(270, 8, TimeSpan.FromMilliseconds(130), LibraryFile.MonMagicEx14, 10, 35, Globals.NoneColour)
                                {
                                    Blend = true,
                                    Target = attacktarget,
                                });
                            }
                            break;
                    }
                    break;
                case MonsterImage.GardenDefender:
                    switch (CurrentAction)
                    {
                        case MirAction.Attack:
                            DXSoundManager.Play(SoundIndex.GardenDefenderAttack);
                            break;
                        case MirAction.RangeAttack:
                            DXSoundManager.Play(SoundIndex.GardenDefenderAttack2);
                            Effects.Add(new MirEffect(300, 7, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx14, 10, 35, Globals.NoneColour)
                            {
                                Blend = true,
                                Target = this,
                            });
                            break;
                    }
                    break;
                case MonsterImage.RedBlossom:
                    switch (CurrentAction)
                    {
                        case MirAction.RangeAttack:
                            DXSoundManager.Play(SoundIndex.RedBlossomAttack2);
                            Effects.Add(new MirEffect(500, 10, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx14, 10, 35, Globals.FireColour)
                            {
                                Blend = true,
                                Target = this,
                            });
                            break;
                    }
                    break;
                case MonsterImage.BlueBlossom:
                    switch (CurrentAction)
                    {
                        case MirAction.RangeAttack:
                            Effects.Add(new MirEffect(600, 10, TimeSpan.FromMilliseconds(100), LibraryFile.MonMagicEx14, 10, 35, Globals.NoneColour)
                            {
                                Blend = true,
                                Target = this,
                            });
                            break;
                    }
                    break;
                case MonsterImage.FireBird:
                    switch (CurrentAction)
                    {
                        case MirAction.Spell:
                            switch (CurrentAnimation)
                            {
                                case MirAnimation.Combat4:
                                    DXSoundManager.Play(SoundIndex.FireBirdAttack3);
                                    break;
                                case MirAnimation.Combat3:
                                    DXSoundManager.Play(SoundIndex.FireBirdAttack2);
                                    Effects.Add(new MirEffect(800, 10, TimeSpan.FromMilliseconds(130), LibraryFile.MonMagicEx14, 20, 55, Globals.FireColour)
                                    {
                                        Blend = true,
                                        Target = this,
                                        Direction = Direction,
                                    });
                                    break;
                            }
                            break;
                    }
                    break;
            }
        }

        public override void PlayAttackSound()
        {
            DXSoundManager.Play(AttackSound);
        }
        public override void PlayStruckSound()
        {
            DXSoundManager.Play(StruckSound);

            DXSoundManager.Play(SoundIndex.GenericStruckMonster);
        }
        public override void PlayDieSound()
        {
            DXSoundManager.Play(DieSound);
        }

        public override void UpdateQuests()
        {
            if (GameScene.Game.HasQuest(MonsterInfo, GameScene.Game.MapControl.MapInfo)) //Todo Optimize by variable.
                Title = "(Quest)";
            else
                Title = string.Empty;
        }
    }
}

