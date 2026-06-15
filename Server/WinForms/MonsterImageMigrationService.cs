using Library;
using Library.SystemModels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Server.WinForms
{
    public sealed class MonsterImageMigrationService
    {
        private static readonly string[] OldMonsterNames =
        {
            "None",
            "Guard",
            "Chicken",
            "Pig",
            "Deer",
            "Cow",
            "Sheep",
            "ClawCat",
            "Wolf",
            "ForestYeti",
            "ChestnutTree",
            "CarnivorousPlant",
            "Oma",
            "TigerSnake",
            "SpittingSpider",
            "Scarecrow",
            "OmaHero",
            "CaveBat",
            "Scorpion",
            "Skeleton",
            "SkeletonAxeMan",
            "SkeletonAxeThrower",
            "SkeletonWarrior",
            "SkeletonLord",
            "CaveMaggot",
            "GhostSorcerer",
            "GhostMage",
            "VoraciousGhost",
            "DevouringGhost",
            "CorpseRaisingGhost",
            "GhoulChampion",
            "ArmoredAnt",
            "AntSoldier",
            "AntHealer",
            "AntNeedler",
            "ShellNipper",
            "Beetle",
            "VisceralWorm",
            "MutantFlea",
            "PoisonousMutantFlea",
            "BlasterMutantFlea",
            "WasHatchling",
            "Centipede",
            "ButterflyWorm",
            "MutantMaggot",
            "Earwig",
            "IronLance",
            "LordNiJae",
            "RottingGhoul",
            "DecayingGhoul",
            "BloodThirstyGhoul",
            "SpinedDarkLizard",
            "UmaInfidel",
            "UmaFlameThrower",
            "UmaAnguisher",
            "UmaKing",
            "SpiderBat",
            "ArachnidGazer",
            "Larva",
            "RedMoonGuardian",
            "RedMoonProtector",
            "VenomousArachnid",
            "DarkArachnid",
            "RedMoonTheFallen",
            "ZumaSharpShooter",
            "ZumaFanatic",
            "ZumaGuardian",
            "ViciousRat",
            "ZumaKing",
            "EvilFanatic",
            "Monkey",
            "EvilElephant",
            "CannibalFanatic",
            "SpikedBeetle",
            "NumaGrunt",
            "NumaMage",
            "NumaElite",
            "SandShark",
            "StoneGolem",
            "WindfurySorceress",
            "CursedCactus",
            "NetherWorldGate",
            "RagingLizard",
            "SawToothLizard",
            "MutantLizard",
            "VenomSpitter",
            "SonicLizard",
            "GiantLizard",
            "CrazedLizard",
            "TaintedTerror",
            "DeathLordJichon",
            "Minotaur",
            "FrostMinotaur",
            "ShockMinotaur",
            "FlameMinotaur",
            "FuryMinotaur",
            "BanyaLeftGuard",
            "BanyaRightGuard",
            "EmperorSaWoo",
            "BoneArcher",
            "BoneBladesman",
            "BoneCaptain",
            "BoneSoldier",
            "ArchLichTaedu",
            "WedgeMothLarva",
            "LesserWedgeMoth",
            "WedgeMoth",
            "RedBoar",
            "ClawSerpent",
            "BlackBoar",
            "TuskLord",
            "RazorTusk",
            "PinkGoddess",
            "GreenGoddess",
            "MutantCaptain",
            "StoneGriffin",
            "FlameGriffin",
            "WhiteBone",
            "Shinsu",
            "InfernalSoldier",
            "InfernalGuardian",
            "InfernalWarrior",
            "CorpseStalker",
            "LightArmedSoldier",
            "CorrosivePoisonSpitter",
            "PhantomSoldier",
            "MutatedOctopus",
            "AquaLizard",
            "Stomper",
            "CrimsonNecromancer",
            "ChaosKnight",
            "PachonTheChaosBringer",
            "NumaCavalry",
            "NumaHighMage",
            "NumaStoneThrower",
            "NumaRoyalGuard",
            "NumaArmoredSoldier",
            "IcyRanger",
            "IcyGoddess",
            "IcySpiritWarrior",
            "IcySpiritGeneral",
            "GhostKnight",
            "IcySpiritSpearman",
            "Werewolf",
            "Whitefang",
            "IcySpiritSolider",
            "WildBoar",
            "JinamStoneGate",
            "FrostLordHwa",
            "Companion_Pig",
            "Companion_TuskLord",
            "Companion_SkeletonLord",
            "Companion_Griffin",
            "Companion_Dragon",
            "Companion_Donkey",
            "Companion_Sheep",
            "Companion_BanyoLordGuzak",
            "Companion_Panda",
            "Companion_Rabbit",
            "JinchonDevil",
            "OmaWarlord",
            "EscortCommander",
            "FieryDancer",
            "EmeraldDancer",
            "QueenOfDawn",
            "OYoungBeast",
            "YumgonWitch",
            "MaWarlord",
            "JinhwanSpirit",
            "JinhwanGuardian",
            "YumgonGeneral",
            "ChiwooGeneral",
            "DragonQueen",
            "DragonLord",
            "FerociousIceTiger",
            "SamaFireGuardian",
            "SamaIceGuardian",
            "SamaLightningGuardian",
            "SamaWindGuardian",
            "Phoenix",
            "BlackTortoise",
            "BlueDragon",
            "WhiteTiger",
            "SamaCursedBladesman",
            "SamaCursedSlave",
            "SamaCursedFlameMage",
            "SamaProphet",
            "SamaSorcerer",
            "EnshrinementBox",
            "BloodStone",
            "OrangeTiger",
            "RegularTiger",
            "RedTiger",
            "SnowTiger",
            "BlackTiger",
            "BigBlackTiger",
            "BigWhiteTiger",
            "OrangeBossTiger",
            "BigBossTiger",
            "WildMonkey",
            "FrostYeti",
            "EvilSnake",
            "Salamander",
            "SandGolem",
            "SDMob4",
            "SDMob5",
            "SDMob6",
            "SDMob7",
            "OmaMage",
            "SDMob9",
            "SDMob10",
            "SDMob11",
            "SDMob12",
            "SDMob13",
            "SDMob14",
            "CrystalGolem",
            "DustDevil",
            "TwinTailScorpion",
            "BloodyMole",
            "SDMob19",
            "SDMob20",
            "SDMob21",
            "SDMob22",
            "SDMob23",
            "SDMob24",
            "SDMob25",
            "GangSpider",
            "VenomSpider",
            "SDMob26",
            "LobsterLord",
            "LobsterSpawn",
            "NewMob1",
            "NewMob2",
            "NewMob3",
            "NewMob4",
            "NewMob5",
            "NewMob6",
            "NewMob7",
            "NewMob8",
            "NewMob9",
            "NewMob10",
            "MonasteryMon0",
            "MonasteryMon1",
            "MonasteryMon2",
            "MonasteryMon3",
            "MonasteryMon4",
            "MonasteryMon5",
            "MonasteryMon6",
            "CastleFlag",
            "Tornado"
        };

        private static readonly Dictionary<string, string> NameAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["WasHatchling"] = "WaspHatchling"
        };

        public MonsterImageMigrationReport Preview(IEnumerable<MonsterInfo> monsters)
        {
            return Migrate(monsters, dryRun: true);
        }

        public MonsterImageMigrationReport Migrate(IEnumerable<MonsterInfo> monsters)
        {
            return Migrate(monsters, dryRun: false);
        }

        private static MonsterImageMigrationReport Migrate(IEnumerable<MonsterInfo> monsters, bool dryRun)
        {
            MonsterImageMigrationReport report = new MonsterImageMigrationReport
            {
                DryRun = dryRun
            };

            foreach (MonsterInfo monster in monsters.Where(x => x != null))
            {
                report.TotalChecked++;

                int oldValue = (int)monster.Image;
                if (oldValue > 0 && oldValue < 10)
                    report.LegacyMarkers++;

                if (oldValue <= 0)
                    continue;

                if (oldValue >= OldMonsterNames.Length)
                {
                    report.SkippedOutOfOldRange++;
                    continue;
                }

                string oldName = OldMonsterNames[oldValue];
                if (NameAliases.TryGetValue(oldName, out string alias))
                    oldName = alias;

                report.Candidates++;
                if (!Enum.TryParse(oldName, ignoreCase: true, out MonsterImage newImage))
                {
                    report.Unmatched++;
                    report.AddUnmatchedSample($"{monster.MonsterName} #{monster.Index}: 旧值 {oldValue} ({OldMonsterNames[oldValue]})");
                    continue;
                }

                if (monster.Image.Equals(newImage))
                {
                    report.AlreadyCurrent++;
                    continue;
                }

                report.Updated++;
                report.AddUpdatedSample($"{monster.MonsterName} #{monster.Index}: {oldValue} ({OldMonsterNames[oldValue]}) -> {(int)newImage} ({newImage})");

                if (!dryRun)
                    monster.Image = newImage;
            }

            return report;
        }
    }

    public sealed class MonsterImageMigrationReport
    {
        private readonly List<string> _updatedSamples = new List<string>();
        private readonly List<string> _unmatchedSamples = new List<string>();

        public bool DryRun { get; set; }
        public int TotalChecked { get; set; }
        public int LegacyMarkers { get; set; }
        public int Candidates { get; set; }
        public int Updated { get; set; }
        public int AlreadyCurrent { get; set; }
        public int Unmatched { get; set; }
        public int SkippedOutOfOldRange { get; set; }
        public IReadOnlyList<string> UpdatedSamples => _updatedSamples;
        public IReadOnlyList<string> UnmatchedSamples => _unmatchedSamples;

        public void AddUpdatedSample(string sample)
        {
            if (_updatedSamples.Count < 20)
                _updatedSamples.Add(sample);
        }

        public void AddUnmatchedSample(string sample)
        {
            if (_unmatchedSamples.Count < 20)
                _unmatchedSamples.Add(sample);
        }

        public string ToDisplayText()
        {
            List<string> lines = new List<string>
            {
                DryRun ? "旧怪物形象迁移预览" : "旧怪物形象迁移完成",
                $"检查怪物: {TotalChecked.ToString("#,##0", CultureInfo.CurrentCulture)}",
                $"旧格式标记: {LegacyMarkers.ToString("#,##0", CultureInfo.CurrentCulture)}",
                $"可按旧枚举解释: {Candidates.ToString("#,##0", CultureInfo.CurrentCulture)}",
                DryRun ? $"预计更新: {Updated.ToString("#,##0", CultureInfo.CurrentCulture)}" : $"已更新: {Updated.ToString("#,##0", CultureInfo.CurrentCulture)}",
                $"已是目标值: {AlreadyCurrent.ToString("#,##0", CultureInfo.CurrentCulture)}",
                $"无法匹配: {Unmatched.ToString("#,##0", CultureInfo.CurrentCulture)}",
                $"跳过当前新枚举范围值: {SkippedOutOfOldRange.ToString("#,##0", CultureInfo.CurrentCulture)}"
            };

            if (_updatedSamples.Count > 0)
            {
                lines.Add(string.Empty);
                lines.Add(DryRun ? "预计更新示例:" : "更新示例:");
                lines.AddRange(_updatedSamples);
            }

            if (_unmatchedSamples.Count > 0)
            {
                lines.Add(string.Empty);
                lines.Add("无法匹配示例:");
                lines.AddRange(_unmatchedSamples);
            }

            return string.Join(Environment.NewLine, lines);
        }
    }
}
