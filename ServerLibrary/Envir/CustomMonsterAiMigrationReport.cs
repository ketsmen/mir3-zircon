using Library.SystemModels;
using MirDB;
using Server.DBModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Server.Envir
{
    public sealed class CustomMonsterAiMigrationReport
    {
        public List<CustomMonsterAiMigrationRow> Rows { get; } = [];
        public int MonsterCount { get; set; }
        public int CustomBehaviorCount { get; set; }

        public string ToDisplayText()
        {
            StringBuilder sb = new();
            sb.AppendLine("自定义怪物 AI 迁移报告");
            sb.AppendLine();
            sb.AppendLine($"怪物总数: {MonsterCount:#,##0}");
            sb.AppendLine($"已有自定义行为: {CustomBehaviorCount:#,##0}");
            sb.AppendLine($"旧 AI 分组: {Rows.Count:#,##0}");
            sb.AppendLine();
            sb.AppendLine("迁移优先级建议:");
            sb.AppendLine("- High: 普通近战、远程、范围、召唤、牛魔王类 Boss，当前 RuleSet 已有模板或接近完整覆盖。");
            sb.AppendLine("- Medium: 可由 RuleSet 组合覆盖大部分逻辑，但需要人工补技能参数、召唤列表、保护源或视觉帧。");
            sb.AppendLine("- Low: 城门、守卫、活动怪、复杂事件 Boss 等，建议暂时保留 C# 或后续拆专用原语。");
            sb.AppendLine();

            foreach (CustomMonsterAiMigrationRow row in Rows.OrderByDescending(x => x.PrioritySort).ThenByDescending(x => x.Count).ThenBy(x => x.AI))
            {
                sb.AppendLine($"AI {row.AI}: {row.Count:#,##0} 个，已自定义 {row.CustomCount:#,##0} 个");
                sb.AppendLine($"  当前实现: {row.CurrentImplementation}");
                sb.AppendLine($"  建议模板: {row.SuggestedTemplate}");
                sb.AppendLine($"  优先级: {row.Priority}");
                sb.AppendLine($"  说明: {row.Notes}");

                if (row.Examples.Count > 0)
                    sb.AppendLine($"  示例怪物: {string.Join(", ", row.Examples)}");

                sb.AppendLine();
            }

            return sb.ToString();
        }
    }

    public sealed class CustomMonsterAiMigrationRow
    {
        public int AI { get; init; }
        public int Count { get; init; }
        public int CustomCount { get; init; }
        public string CurrentImplementation { get; init; }
        public string SuggestedTemplate { get; init; }
        public string Priority { get; init; }
        public string Notes { get; init; }
        public List<string> Examples { get; init; } = [];

        public int PrioritySort => Priority switch
        {
            "High" => 3,
            "Medium" => 2,
            "Low" => 1,
            _ => 0,
        };
    }

    public static class CustomMonsterAiMigrationReportBuilder
    {
        private static readonly Dictionary<int, AiMigrationSuggestion> Suggestions = new()
        {
            [-1] = new("Guard", "Template=Guard", "Low", "守卫有玩家/城战相关规则，保留 Guard 分支更稳。"),
            [0] = new("MonsterObject", "Templates/01_普通近战模板.txt", "High", "默认近战可直接迁移到 RuleSet: MoveToTarget + MeleeAttack。"),
            [1] = new("MonsterObject Passive Harvest(2)", "Templates/73_被动采集2次固定物模板.txt", "High", "被动、固定、不可攻击、采集 2 次均已由 Behavior 字段表达，不再需要专用 C# 分支。"),
            [2] = new("MonsterObject Passive Harvest(3)", "Templates/06_被动采集固定物模板.txt", "High", "被动、固定、不可攻击、采集 3 次均已由 Behavior 字段表达，不再需要专用 C# 分支。"),
            [3] = new("MonsterObject Harvest(3)", "Templates/74_可战斗采集3次近战模板.txt", "High", "战斗和采集计数都可文本化，掉落仍走 Drops 文本或数据库。"),
            [4] = new("TreeMonster", "Templates/06_被动采集固定物模板.txt", "High", "固定不移动、不攻击、被动采集和采集次数都可由 Behavior 字段表达，旧 TreeMonster 不再需要专用 C#。"),
            [5] = new("CarnivorousPlant", "Templates/75_伏地采集植物模板.txt", "High", "固定伏地植物、靠近显现、远离隐藏、不可见时不可攻击和采集字段均已模板化。"),
            [6] = new("SpittingSpider Green Poison Harvest", "Templates/76_绿毒直线采集模板.txt", "High", "蜘蛛类 2 格直线攻击、命中绿毒和采集次数均已由 TargetInLine、LineAttack 与 Behavior.PoisonType 表达。"),
            [7] = new("SkeletonAxeThrower", "Templates/02_远程怪模板.txt", "High", "标准远程怪，可迁移到 CastSkill/RangeAttack。"),
            [8] = new("MonsterObject Paralysis Poison", "Templates/79_麻痹近战模板.txt", "High", "近战麻痹毒可直接用 Behavior 级命中附毒迁移，不需要为每个毒怪写派生类。"),
            [9] = new("GhostSorcerer", "Templates/09_直线攻击模板.txt", "High", "直线法师可用 TargetInLine + LineAttack 表达，不再需要派生类里的 LineAttack 方法。"),
            [10] = new("GhostMage", "Templates/03_范围怪模板.txt", "High", "法术/范围怪可用 CastSkill + AreaDamage/PersistentAreaDamage。"),
            [11] = new("VoraciousGhost", "Templates/32_随机复活亡灵模板.txt", "High", "随机 0-3 次复活、复活前不掉落、复活血量按次数递减和普通近战都可由 ReviveSelf RandomCount、HealthDecay、SuppressDrops 表达。"),
            [12] = new("HealerAnt", "Templates/38_友方持续治疗模板.txt", "High", "治疗友军可用 LowestHealthAllyInView + 持续治疗 Buff 技能模板表达。"),
            [13] = new("LordNiJae", "Templates/28_命中后附毒全视野Boss模板.txt", "High", "全视野延迟攻击、按距离追加命中延迟、命中后 20% 绿毒和 10% 麻痹可由 RangeAttack Travel、OnHitAction、ActionChance 表达；植物显隐可叠加出生显隐模板。"),
            [14] = new("SpittingSpider Green Poison", "Templates/77_绿毒直线模板.txt", "High", "远程绿毒液怪可用 LineAttack 加 Behavior 级命中附毒表达。"),
            [15] = new("MonsterObject", "Templates/01_普通近战模板.txt", "High", "普通近战怪可直接由 RuleSet 基础模板接管。"),
            [16] = new("UmaKing", "Templates/05_牛魔王Boss模板.txt", "High", "牛魔王式近战、远程范围技能随机选择、追击和 Boss 阶段召唤都可用 WeightedChoice、MassAreaDamage、RangeAttack、SpawnMinions 表达。"),
            [17] = new("ArachnidGrazer + Larva SpawnList", "Templates/04_召唤怪模板.txt", "High", "召唤怪可用 SpawnMinions + MinionDuration/SpawnMonster。"),
            [18] = new("Larva Self Destruct", "Templates/97_绿毒自爆模板.txt", "High", "靠近目标自爆、死亡范围伤害和绿毒参数已拆成独立模板。"),
            [19] = new("RedMoonTheFallen Fixed Map Wide Range", "Templates/49_固定全屏远程模板.txt", "High", "固定不移动、全屏范围远程攻击可用 DisableMovement + RangeAttack AllInView 表达。"),
            [23] = new("Monkey Green Poison Range", "Templates/80_绿毒远近切换模板.txt", "High", "猴子类 2 格远近切换和命中绿毒可配置化。"),
            [24] = new("Monkey Red Poison Range", "Templates/81_红毒远近切换模板.txt", "High", "猴子类 2 格远近切换和命中红毒可配置化。"),
            [25] = new("EvilElephant Poison Area Choice", "Templates/50_毒踩踏近战模板.txt", "High", "普通攻击和小范围毒击可用 WeightedChoice、SelfRadius RangeAttack 和 OnHitAction ApplyPoison 表达。"),
            [20] = new("SkeletonAxeThrower Fear", "Templates/58_保持距离投掷恐惧模板.txt", "High", "远程投掷、靠近后后撤、投掷后概率短暂停手都已用 MoveAwayFromTarget、WeightedChoice、ScheduleAction 和计数器表达。"),
            [21] = new("ZumaGuardian", "Templates/98_隐藏守护唤醒模板.txt", "High", "隐藏守护、靠近唤醒和群体唤醒已拆成独立模板。"),
            [22] = new("ZumaKing + SpawnList", "Templates/57_隐藏阶段召唤Boss模板.txt", "High", "隐藏唤醒、火墙/灼地线型远程、七阶段召唤都可由 RuleSet、WakeNearby、WeightedChoice、HealthBelow 和 SpawnMinions 表达。"),
            [26] = new("NumaMage", "Templates/03_范围怪模板.txt", "High", "法术怪可迁移到 CastSkill。"),
            [27] = new("GhostMage", "Templates/03_范围怪模板.txt", "High", "法术怪可迁移到 CastSkill。"),
            [28] = new("WindfurySorcerer", "Templates/03_范围怪模板.txt 或 03_模板扇形控制", "High", "风系范围/控制技能已可模板化。"),
            [29] = new("SkeletonAxeThrower", "Templates/02_远程怪模板.txt", "High", "标准远程怪。"),
            [30] = new("NetherworldGate", "Templates/124_定时传送门模板.txt", "High", "传送门已拆为 AlwaysActive 功能怪：OnSpawn 广播并排程关闭，OnTick 周期 TeleportTargetsToRegion 到 MysteryShip。"),
            [31] = new("SonicLizard", "Templates/09_直线攻击模板.txt", "High", "声波蜥蜴直线攻击和近战切换可用 TargetInLine、LineAttack、MeleeAttack 组合迁移。"),
            [33] = new("GiantLizard AttackRange=9 IgnoreShield", "Templates/84_破盾远近切换模板.txt", "High", "远近切换、远程冷却、破盾远程攻击和玩家/非玩家目标差异可用 TargetIsPlayer/TargetIsHero 条件表达。"),
            [34] = new("SkeletonAxeThrower AttackRange=9", "Templates/88_九格远程投掷模板.txt", "High", "9 格远程投掷怪已拆成独立模板，可直接配置飞行速度、延迟和 AttackRange。"),
            [35] = new("MonsterObject", "Templates/01_普通近战模板.txt", "High", "普通近战怪。"),
            [36] = new("NumaMage", "Templates/03_范围怪模板.txt", "High", "法术怪可迁移到 CastSkill 或 AttackAoE。"),
            [37] = new("MonsterObject", "Templates/01_普通近战模板.txt", "High", "普通近战怪。"),
            [38] = new("BanyaLeftGuard FireBall", "Templates/54_火球近战守卫模板.txt", "High", "近战/火球随机切换可用 OnTargetTick 多规则 + AttackMagic FireBall 表达。"),
            [39] = new("MonsterObject", "Templates/01_普通近战模板.txt", "High", "普通近战怪。"),
            [40] = new("MonsterObject", "Templates/01_普通近战模板.txt", "High", "普通近战怪。"),
            [42] = new("SpittingSpider", "Templates/09_直线攻击模板.txt", "High", "蜘蛛直线攻击可迁移到 TargetInLine + LineAttack。"),
            [43] = new("ArchLichTaedu Stage Spawn", "Templates/55_七阶段召唤Boss模板.txt", "High", "按血量分段召唤可用 HealthBelow、Counter/MaxExecutions、SpawnMinions 和 MaxMinions=50 表达。"),
            [44] = new("WedgeMothLarva Fixed Summoner", "Templates/56_固定吐幼虫模板.txt", "High", "固定炮台式延迟召唤可用 DisableMovement、RangeAttack NoDamage、ScheduleAction 和 SpawnMinions。"),
            [45] = new("RazorTusk Range Switch", "Templates/89_三格远近切换模板.txt", "High", "3 格远近切换已拆成独立模板，不需要复制通用远近切换模板后再手动改范围。"),
            [46] = new("SpittingSpider Red Poison", "Templates/78_红毒直线模板.txt", "High", "红毒直线怪可用 LineAttack 加 Behavior 毒参数表达。"),
            [47] = new("SpittingSpider Green Poison", "Templates/77_绿毒直线模板.txt", "High", "绿毒直线怪可用 LineAttack 加 Behavior 毒参数表达。"),
            [48] = new("SonicLizard IgnoreShield", "Templates/83_破盾直线声波模板.txt", "High", "声波蜥蜴直线攻击和忽略护盾均可配置。"),
            [52] = new("WhiteBone Spawn Effect", "Templates/99_出生特效显现模板.txt", "High", "出生隐藏、召唤地图特效和延迟显现已拆成独立模板。"),
            [53] = new("Shinsu Spawn/Mode", "Templates/100_神兽显隐战斗模板.txt", "High", "出生召唤特效、显现、战斗距离判断和离身隐藏已拆成独立模板。"),
            [41] = new("EmperorSaWoo", "Templates/05_牛魔王Boss模板.txt", "High", "牛魔王类 Boss 已有可验证模板，覆盖近战、范围、MassCyclone、阶段和召唤。"),
            [75] = new("DepartedMonster Death Effect Spawn", "Templates/112_死亡特效队伍召唤模板.txt", "High", "死亡对象特效、范围伤害和按击杀归属队伍附近召唤已整合成独立模板。"),
            [76] = new("DepartedMonster Death Effect Spawn", "Templates/112_死亡特效队伍召唤模板.txt", "High", "同 AI 75，按不同 SpawnMonster 调整死亡召唤物。"),
            [49] = new("GiantLizard Paralysis Range", "Templates/82_麻痹远近切换模板.txt", "High", "远近切换和命中麻痹都已配置化。"),
            [50] = new("GiantLizard Range", "Templates/10_远程近战切换模板.txt", "High", "标准远近切换怪可迁移到 RuleSet。"),
            [56] = new("CorrosivePoisonSpitter Teleport Poison", "Templates/101_绿毒破盾瞬移模板.txt", "High", "远距离瞬移、绿毒和动作级忽略护盾已拆成独立模板。"),
            [57] = new("CorrosivePoisonSpitter Teleport", "Templates/08_瞬移偷袭模板.txt", "High", "远距离瞬移靠近目标可用 TeleportNearTarget + Cooldown。"),
            [58] = new("Stomper Area Melee", "Templates/90_近身范围踩踏模板.txt", "High", "近身范围普攻已拆成独立踩踏模板，用 MassAreaDamage 表达中心范围伤害。"),
            [59] = new("CrimsonNecromancer MagicWeakness", "Templates/17_范围Debuff怪模板.txt", "High", "范围虚弱 Debuff 可用 RangeAttack NoDamage + AddBuff MagicWeakness 表达。"),
            [60] = new("ChaosKnight PoisonousCloud", "Templates/113_近身周期持续毒云模板.txt", "High", "近身周期毒云已整合成独立模板，通过 CastSkill 调用持续毒云技能。"),
            [61] = new("PachontheChaosbringer Teleport Repulse", "Templates/102_瞬移击退模板.txt", "High", "远距离瞬移和近身击退已用 TeleportNearTarget + PushTarget 拆成独立模板。"),
            [62] = new("NumaHighMage Line Range", "Templates/09_直线攻击模板.txt", "High", "直线远程法师可用 TargetInLine + LineAttack。"),
            [63] = new("NumaStoneThrower Target Radius", "Templates/91_目标点半径投掷模板.txt", "High", "目标点半径投掷已拆成独立模板，用 TargetRadius、Radius=2 和 Travel 表达落点范围命中。"),
            [64] = new("Monkey Range", "Templates/10_远程近战切换模板.txt", "High", "2 格远近切换可调整 AttackRange。"),
            [65] = new("IcyGoddess Ranged Plant", "Templates/103_远程植物显隐模板.txt", "High", "固定远程植物、靠近显现、远离隐藏和冰系远程攻击已拆成独立模板。"),
            [66] = new("IcySpiritWarrior Range/AoE Poison", "Templates/94_冰爆麻痹远近模板.txt", "High", "冰爆范围、远近切换和命中麻痹已拆成独立模板，不需要借用扣蓝模板。"),
            [67] = new("IcySpiritGeneral DrainMP", "Templates/105_扣蓝破盾远近模板.txt", "High", "暗属性远程扣 MP、范围冰爆和动作级忽略护盾已拆成独立模板。"),
            [68] = new("Warewolf IgnoreShield", "Templates/85_破盾近战模板.txt", "High", "普通近战加忽略护盾可直接配置。"),
            [69] = new("JinamStoneGate Lair Portal", "Templates/124_定时传送门模板.txt", "High", "赤龙石门可复用定时传送门模板，把 Image 改为 JinamStoneGate、消息改为赤龙石门、SpecialRegion 改为 Lair。"),
            [70] = new("FrostLordHwa Multi Poison Aura", "Templates/31_全屏拉人多毒Boss模板.txt", "High", "每分钟全屏拉人、Abyss/Silenced/Red/WraithGrip 多毒、远距离贴近和近战都可由 PullTarget 集合目标、ApplyPoison、TeleportNearTarget 表达；日替属性建议用具体怪物 Stats 配置。"),
            [72] = new("BanyoCaptain FireBall/Purification", "Templates/26_净化法师模板.txt", "High", "距离分段固定伤害可用 TargetDistanceAbove 优先级规则表达；Purification 可由 PurifyTargets 直接复用旧净化逻辑。"),
            [74] = new("BanyoLordGuzak Purify + Captain Spawn", "Templates/39_全图净化补护卫Boss模板.txt", "High", "周期净化全屏目标、45 秒补满护卫和护卫离身清理都可用 OnTick、PurifyTargets AllMap、SpawnMinions、MinionLeashRadius 表达。"),
            [54] = new("GiantLizard RangeCooldown", "Templates/10_远程近战切换模板.txt", "High", "RangeCooldown 可用 AIRule.Cooldown 表达。"),
            [77] = new("EnragedLordNiJae Periodic Spawn", "Templates/119_大上限周期召唤模板.txt", "High", "30 秒清理远离护卫并补满召唤物已整合成独立模板。"),
            [78] = new("JinchonDevil DeathCloud", "Templates/27_多点持续毒云模板.txt", "High", "近战/直线攻击、半血前概率多点 DeathCloud、半血后全量多点 DeathCloud 已由 PerTarget CastSkill 和持续毒云技能表达。"),
            [79] = new("GiantLizard RangeCooldown", "Templates/10_远程近战切换模板.txt", "High", "RangeCooldown 可用 AIRule.Cooldown 表达。"),
            [80] = new("SunFeralWarrior Spawn OnStruck", "Templates/43_受击召唤火法战士模板.txt", "High", "首次受击召唤、火球/火墙二选一和继承 BanyaLeftGuard 远程模式可由 OnStruck、MaxExecutions、SpawnMinions、WeightedChoice 表达。"),
            [82] = new("OxFeralGeneral Multi LineAoE", "Templates/45_四元素线型Boss模板.txt", "High", "四元素随机线型范围技可用 WeightedChoice 选择多个 LineAoE 动作表达。"),
            [83] = new("FlameDemon LineAoE", "Templates/46_火焰线型炮台模板.txt", "High", "扇形/多线火焰地面技可用 LineAoE 和 MinOffset/MaxOffset 表达。"),
            [84] = new("WingedHorror Multi Range", "Templates/120_多范围技能Boss模板.txt", "High", "多种远程范围技已用 WeightedChoice、LineAoE、MassAreaDamage 整合成独立模板。"),
            [85] = new("EmperorSaWoo Paralysis Poison", "Templates/115_麻痹牛魔王Boss模板.txt", "High", "牛魔王类 Boss 加命中麻痹毒已整合成独立模板。"),
            [86] = new("Passive FlameDemon LineAoE", "Templates/114_被动火焰线型炮台模板.txt", "High", "被动火焰炮台、固定不移动和火焰线型范围技已整合成独立模板。"),
            [92] = new("GiantLizard Range", "Templates/10_远程近战切换模板.txt", "High", "标准远近切换怪可迁移到 RuleSet。"),
            [94] = new("FieryDancer TargetRadius", "Templates/92_远程目标点半径模板.txt", "High", "目标点半径范围攻击已拆成独立模板，用远程落点范围伤害表达。"),
            [95] = new("FieryDancer Paralysis", "Templates/104_麻痹目标点半径模板.txt", "High", "目标点半径攻击和命中麻痹毒已拆成独立模板。"),
            [96] = new("QueenOfDawn Teleport/AoE", "Templates/24_随机传送场控Boss模板.txt", "High", "瞬移偷袭、近身范围攻击和每分钟全地图随机传送敌人都可由 TeleportNearTarget、MassAreaDamage、TeleportTargetsRandom 表达。"),
            [97] = new("SonicLizard Range=5 IgnoreShield", "Templates/83_破盾直线声波模板.txt", "High", "直线声波攻击、范围和忽略护盾可配置化。"),
            [98] = new("YumgonWitch Lightning AoE", "Templates/93_近身雷暴远近切换模板.txt", "High", "远近切换和近身 3 格范围雷电攻击已拆成独立模板。"),
            [100] = new("YumgonWitch AoE", "Templates/93_近身雷暴远近切换模板.txt", "High", "同 AI 98，按元素、伤害和特效参数调整即可。"),
            [103] = new("InfernalSoldier Range", "Templates/106_出生延迟远近切换模板.txt", "High", "远近切换、出生 2 秒行动延迟和 ObjectShow 重播已拆成独立模板。"),
            [104] = new("FerociousIceTiger", "Templates/33_移动重置旋转攻击模板.txt", "High", "3 格攻击范围、20% 进入旋转、旋转持续到移动后清除、旋转范围伤害和战士减伤都可由 OnMove、Counter、RangeAttack、ClassPowerRates 表达。"),
            [105] = new("GiantLizard PvP Range", "Templates/84_破盾远近切换模板.txt", "High", "可按需要保留或删除 !TargetIsPlayer/!TargetIsHero 条件来控制是否允许对玩家远程，破盾远程动作可独立配置。"),
            [106] = new("GiantLizard PvP Range", "Templates/10_远程近战切换模板.txt", "High", "可按需要保留或删除 !TargetIsPlayer/!TargetIsHero 条件来控制是否允许对玩家远程。"),
            [71] = new("BanyoWarrior Teleport Bonus", "Templates/40_瞬移强化攻击模板.txt", "High", "瞬移到目标附近和下一击强化可用 TeleportNearTarget、SetCounter、Sequence 表达。"),
            [81] = new("MoonFeralWarrior Teleport Bonus", "Templates/40_瞬移强化攻击模板.txt", "High", "瞬移偷袭类 AI 可配置化，不再需要每种怪物一个 TeleportTime/Bonus 字段。"),
            [87] = new("OmaWarlord Teleport Bonus", "Templates/36_受击远距闪现模板.txt", "High", "半血受击远距随机闪现和双倍攻击已由 TeleportSelf、计数器和 Sequence 表达。"),
            [88] = new("GoruSpearman Teleport Bonus", "Templates/116_受击闪现直线强化模板.txt", "High", "半血受击远距随机闪现和后续直线强化攻击已整合成独立模板。"),
            [89] = new("GoruArcher Teleport Bonus", "Templates/117_受击闪现远程强化模板.txt", "High", "弓手远距随机闪现偷袭和强化远程攻击已整合成独立模板。"),
            [90] = new("OmaWarlord Teleport Bonus", "Templates/36_受击远距闪现模板.txt", "High", "同 AI 87，按怪物参数调整触发血量、冷却和强化倍率。"),
            [91] = new("EnragedArchLichTaedu Teleport Bonus Spawn", "Templates/118_受击闪现召唤红毒模板.txt", "High", "半血远距随机闪现、双倍攻击、阶段召唤和红毒参数已整合成独立模板。"),
            [93] = new("EscortCommander Lightning Choice", "Templates/107_近身雷暴三雷模板.txt", "High", "近身雷暴和远程三雷随机选择已拆成独立模板。"),
            [99] = new("JinhwanSpirit Death Spawn Self", "Templates/71_死亡同类爆发召唤模板.txt", "High", "死亡时生成同类 20 只已用 OnDeath + SpawnMinions 表达，SpawnMonster 指向模板自身，Radius=10。"),
            [101] = new("DragonQueen Yumgon Boss", "Templates/29_补护卫死亡召唤Boss模板.txt", "High", "15 秒补护卫、护卫距离清理、死亡生成子 Boss 和目标点范围攻击都可由 SpawnMinions、MinionLeashRadius、OnDeath 与 RangeAttack TargetRadius 表达；实际怪物名按正式 SpawnList 替换。"),
            [102] = new("DragonLord Large Radius Range Boss", "Templates/29_补护卫死亡召唤Boss模板.txt 改 AttackRange=12", "High", "12 格目标点群伤和周期补护卫可迁移；Boss 自身作为 SpawnList 保护项建议用明确 SpawnMonster 动作拆开，避免递归召唤。"),
            [107] = new("SamaFireGuardian", "Templates/59_Sama火守护模板.txt", "High", "2 格近战、移动中小概率远程、四选一火系技能已由远近切换规则和 WeightedChoice 表达。"),
            [108] = new("SamaIceGuardian", "Templates/60_Sama冰守护模板.txt", "High", "2 格近战、移动中小概率远程、IceBlades/GreaterFrozenEarth/IceStorm/SamaGuardianIce 已模板化。"),
            [109] = new("SamaLightningGuardian", "Templates/61_Sama雷守护模板.txt", "High", "2 格近战、移动中小概率远程、ThunderBolt/LightningBeam/LightningWave/SamaGuardianLightning 已模板化。"),
            [110] = new("SamaWindGuardian", "Templates/62_Sama风守护模板.txt", "High", "2 格近战、移动中小概率远程、Cyclone/BlowEarth/DragonTornado/SamaGuardianWind 已模板化。"),
            [111] = new("SamaPhoenix", "Templates/63_Sama凤凰强化模板.txt", "High", "火系四选一外加 SamaPhoenixFire 双倍伤害已用第五个 WeightedChoice 动作和 PowerRate=200 表达。"),
            [112] = new("SamaBlack", "Templates/64_Sama黑冰强化模板.txt", "High", "冰系四选一外加 SamaBlackIce 双倍伤害已用 WeightedChoice + PowerRate=200 表达。"),
            [113] = new("SamaBlue", "Templates/65_Sama蓝雷强化模板.txt", "High", "雷系四选一外加 SamaBlueLightning 双倍伤害已用 WeightedChoice + PowerRate=200 表达。"),
            [114] = new("SamaWhite", "Templates/66_Sama白风强化模板.txt", "High", "风系四选一外加 SamaWhiteWind 双倍伤害已用 WeightedChoice + PowerRate=200 表达。"),
            [115] = new("SamaProphet Protected Caster", "Templates/22_多保护源施法怪模板.txt", "High", "出生召唤、远程大范围三选一、拉目标、BloodStone/SamaSorcerer 多保护源和 SamaSorcerer 护卫回位都已模板化。"),
            [116] = new("SamaScorcer Protected Melee", "Templates/72_Sama护石近战保护模板.txt", "High", "3 格近战、远处目标传送到身边和 BloodStone 保护已用 TeleportTarget 与 ProtectedByMonsterFlag 表达；多保护源可继续改 ProtectedByMonsterFlags。"),
            [117] = new("BanyoWarrior DoubleDamage", "Templates/40_瞬移强化攻击模板.txt", "High", "双倍伤害偷袭可用计数器选择 PowerRate=200 的攻击动作表达。"),
            [118] = new("OmaMage Lightning Choice", "Templates/42_随机三雷法师模板.txt", "High", "LightningBall、ThunderBolt、LightningWave 三选一可直接用 WeightedChoice，倍率分别配置 PowerRate。"),
            [119] = new("Silence Poison MonsterObject", "Templates/108_沉默毒近战模板.txt", "High", "沉默毒普通近战怪已拆成独立模板。"),
            [120] = new("DoomClaw Area Boss", "Templates/41_击退职业倍率Boss模板.txt", "High", "大范围波、拍击、喷吐、击退和按职业减伤可由 MassAreaDamage、PushTarget、WeightedChoice、Options.ClassPowerRates 表达。"),
            [121] = new("PinkBat Phantom Ranged", "Templates/69_粉蝙蝠幻影远近模板.txt", "High", "近战优先、追击中概率粉色火球、近身 20% 粉色火球已由分段 AIRule 和 AttackMagic 表达。"),
            [122] = new("QuartzTurtleSub Periodic Spawn", "Templates/70_水晶龟周期召唤模板.txt", "High", "每 20 秒补召唤物到上限、召唤点围绕自身 10 格已用 OnTick、SpawnMinions、MaxMinions 和 MinionLeashRadius 表达。"),
            [123] = new("Larva Range Self Destruct", "Templates/109_三格感应自爆模板.txt", "High", "3 格感应触发和 3 格死亡范围伤害已拆成独立模板。"),
            [124] = new("QuartzTree SubBoss + Periodic Spawn", "Templates/30_禁回血阶段子Boss召唤模板.txt", "High", "四分之一血生成子 Boss、60 秒重播显示包、补水晶/蝙蝠、护卫距离清理和禁止自然回血都可由 DisableRegen、SpawnMinions、Show ForcePacket、MinionLeashRadius 表达。"),
            [125] = new("CarnivorousPlant Hidden Ambush", "Templates/75_伏地采集植物模板.txt", "High", "伏地植物的显隐、固定、采集可配置化，HideRange/FindRange 可用 TargetDistance 条件表达。"),
            [126] = new("MonasteryBoss Death Sacrifice", "Templates/121_死亡献祭召唤模板.txt", "High", "远近切换攻击和首次死亡召唤 Sacrifice 已整合成独立模板。"),
            [127] = new("JinchonDevil Faster DeathCloud", "Templates/27_多点持续毒云模板.txt", "High", "同 AI 78，按需把 CloudsAfterHalf.Cooldown 调整为 8000，并用技能 Duration/TickRate 表达持续时间。"),
            [128] = new("Doll Damage Proxy", "Templates/127_伤害代理替身模板.txt", "High", "替身娃娃已拆成 DamageProxy 行为；CursedDoll 技能可生成 CustomMonsterObject 并配置显现延迟、出生特效、名字格式和伤害转嫁范围。"),
            [129] = new("Tornado Passive Moving Hazard", "Templates/25_非阻挡伤害风场模板.txt", "High", "非阻挡、短生命周期、出生行动延迟、随机前进/转向和碰撞触发范围伤害都已由 HazardRoam 模板表达。"),
            [130] = new("UndeadSoul Spawn Effect Show Delay", "Templates/99_出生特效显现模板.txt", "High", "出生隐藏、地图特效和延迟显现可复用出生特效显现模板，按需调整 Effect 与 Delay。"),
            [131] = new("Terracotta Hidden Wake", "Templates/11_出生显隐延迟模板.txt", "High", "初始隐藏、靠近显现和隐藏时不可攻击可用 Hidden 字段和 Show/Hide 表达。"),
            [132] = new("Terracotta Phase", "Templates/11_出生显隐延迟模板.txt", "High", "PhaseOut 的先播隐藏包、900ms 后改状态已由 Hide PacketOnly + ScheduleAction + Hide StateOnly 表达。"),
            [133] = new("TerracottaSub Phase + Cone Melee", "Templates/67_陶俑潜行扇形模板.txt", "High", "隐身显现、4 格外概率潜行、扇形近战和麻痹毒已由 Show/Hide、LineAttack、Behavior.PoisonType 表达。"),
            [134] = new("TerracottaBoss LineAoE/Melee", "Templates/68_陶俑暗线Boss模板.txt", "High", "11 格追击、12 格暗系直线和近身范围挥击已由 WeightedChoice、LineAoE、MassAreaDamage 表达。"),
            [135] = new("JinchonDevil Faster DeathCloud", "Templates/27_多点持续毒云模板.txt", "High", "同 AI 127，可直接复用标准多点持续毒云模板并调整冷却、半径、目标上限和持续时间。"),
            [136] = new("KingsSpawner Spider", "Templates/122_击杀归属队伍召唤模板.txt", "High", "死亡后按击杀归属玩家或队伍附近生成怪物已整合成独立模板；正式迁移时把 SpawnMonster 替换成 KingsSpider。"),
            [137] = new("KingsSpawner Salamander", "Templates/122_击杀归属队伍召唤模板.txt", "High", "同 AI 136，正式迁移时把 SpawnMonster 替换成 KingsSalamander。"),
            [139] = new("InfernalSoldier Range", "Templates/106_出生延迟远近切换模板.txt", "High", "远近切换和出生延迟已拆成独立模板，CanPvPRange 可通过 TargetIsPlayer/TargetIsHero 条件保留或关闭。"),
            [140] = new("DarkKing Teleport Combo", "Templates/21_目标生命倍率Boss模板.txt", "High", "连击期间加魔盾、快速瞬移到随机目标背后、按目标最大生命附加伤害和回到原位可用 RandomInView、ScheduleAction、AddBuff、TargetHealthRate、StoreLocation/ReturnToStoredLocation 表达。"),
            [141] = new("DemonicLunatic Mirror Swap", "Templates/125_镜像换位Boss模板.txt", "High", "镜像生成、初始洗牌、战斗中随机换位和死亡清理分身已由 SpawnMinions、SwapRandomMinionLocations、KillMinions 表达。"),
            [142] = new("DemonicLunaticMirror", "Templates/126_镜像换位分身模板.txt", "High", "镜像分身走普通 RuleSet 远程模板，生命周期由本体的 MinionList 和 KillMinions 管理。"),
            [143] = new("Ent StrongGreen IgnoreArmour", "Templates/86_强绿毒破防树人模板.txt", "High", "强绿毒、必中毒和动作级忽略防御都已配置化。"),
            [144] = new("ArachnidGrazer Fixed Spawner", "Templates/110_固定上限召唤炮台模板.txt", "High", "固定不移动、攻击时延迟召唤和最大召唤数 30 已拆成独立模板。"),
            [145] = new("StrongBoss Teleport Mass Fire", "Templates/47_瞬移全屏火Boss模板.txt", "High", "瞬移到目标附近后全视野火系范围伤害可由 TeleportNearTarget + MassAreaDamage 表达，DC 基础伤害加 SC 按目标最大生命放大可用 TargetHealthPower=SC 精确迁移。"),
            [146] = new("Ent Stackable StrongGreen", "Templates/87_叠加强绿毒破盾破防模板.txt", "High", "可叠加强绿毒、动作级破盾和破防现在都可配置，不再需要专用 C# 初始化。"),
            [147] = new("Tachinid Fixed Teleport Strike", "Templates/48_固定瞬移刺杀模板.txt", "High", "固定怪直接瞬移到目标附近攻击可用 DisableMovement、TeleportNearTarget、Sequence RequireAll 表达。"),
            [148] = new("HellBringer Bats + Silence", "Templates/51_沉默召唤Boss模板.txt", "High", "四分之一血召蝙蝠、沉默范围技、目标点范围打击和近战可由 HealthBelow、SpawnMinions、ApplyPoison、RangeAttack 表达。"),
            [149] = new("DuelHitMonster Dual Animation", "Templates/52_双动作近战模板.txt", "High", "两种攻击包/动画随机切换可用 WeightedChoice 和 AnimationVariant 表达。"),
            [150] = new("CrawlerSlave Poison DuelHit", "Templates/53_毒双动作近战模板.txt", "High", "第二攻击命中后绿毒可用 MeleeAttack OnHitAction + ApplyPoison，伤害与毒值分别用 DC/SC 配置。"),
            [151] = new("CursedSlave Range/Melee", "Templates/95_七格远程奴仆模板.txt", "High", "7 格远程单体与近战切换已拆成奴仆模板，可直接迁移。"),
            [152] = new("EvilCursedSlave Range/Mass", "Templates/96_五格范围奴仆模板.txt", "High", "5 格远程全身范围攻击和近战切换已拆成奴仆模板，可直接迁移。"),
            [153] = new("PoisonousGolem Green Poison", "Templates/07_命中附毒怪模板.txt", "High", "基础毒怪可通过 Behavior.PoisonType/PoisonTicks/PoisonRate 配置。"),
            [154] = new("GardenSoldier Range/Melee Cone", "Templates/20_扇形近战远程模板.txt", "High", "7 格远程单体、2 格内三方向扇形近战可由 RangeAttack + LineAttack MinOffset=-1 MaxOffset=1 表达。"),
            [155] = new("GardenDefender Ally Resilience", "Templates/37_友方韧性守卫模板.txt", "High", "半血后高概率给周围友军加 Resilience，可用 AllyRadius、BuffStatsFromTarget、HealthBelow、Chance 和 AddBuff 表达。"),
            [156] = new("RedBlossom AllDirection Lines", "Templates/44_红花八方向直线模板.txt", "High", "直线目标时单线攻击，非直线小概率八方向线攻击，可用 TargetInLine/TargetOutOfLine、LineAttack Options=AllDirections=True 表达。"),
            [157] = new("BlueBlossom Ally Healer", "Templates/38_友方持续治疗模板.txt", "High", "友方低血目标搜索和飞行治疗可用 LowestHealthAllyInView 与持续治疗 Buff 技能模板表达。"),
            [158] = new("FireBird Cyclone/ScorchedEarth Burst", "Templates/23_八方向线型法术模板.txt", "High", "近战、旋风全视野、八方向灼地爆发均可由 WeightedChoice、MassAreaDamage、LineAoE Options=AllDirections=True 组合。"),
            [159] = new("Octopus Boss Fixed Range/Melee", "Templates/111_固定扇形远程Boss模板.txt", "High", "固定不动、7 格远程单体和 2 格内扇形近战已拆成独立 Boss 模板。"),
            [999] = new("TestAI Range/Melee Cone", "Templates/20_扇形近战远程模板.txt", "High", "测试 AI 与 Octopus/GardenSoldier 行为接近，可作为模板验证用例。"),
            [1001] = new("CastleFlag", "暂保留 C#", "Low", "城旗涉及行会归属、攻城参与者、全服消息和旗帜颜色协议，不属于通用怪物/技能系统。"),
            [1002] = new("CastleGate", "暂保留 C#", "Low", "城门涉及阻挡物、开关门、攻城修理和地图门墙状态，建议保留城战专用对象。"),
            [1003] = new("CastleGuard", "暂保留 C#", "Low", "城卫攻击目标和受击权限依赖攻城战行会逻辑，保留 CastleObject 分支更稳。"),
        };

        public static CustomMonsterAiMigrationReport Build(Session session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            List<MonsterInfo> monsters = session.GetCollection<MonsterInfo>().Binding.Where(x => x != null).ToList();
            HashSet<int> customMonsterIndexes = session.GetCollection<CustomMonsterBehaviorInfo>().Binding
                .Where(x => x?.Monster != null && x.Template != CustomMonsterBehaviorTemplate.None)
                .Select(x => x.Monster.Index)
                .ToHashSet();

            CustomMonsterAiMigrationReport report = new()
            {
                MonsterCount = monsters.Count,
                CustomBehaviorCount = customMonsterIndexes.Count,
            };

            foreach (IGrouping<int, MonsterInfo> group in monsters.GroupBy(x => x.AI).OrderBy(x => x.Key))
            {
                AiMigrationSuggestion suggestion = Suggestions.TryGetValue(group.Key, out AiMigrationSuggestion known)
                    ? known
                    : InferSuggestion(group.Key);

                report.Rows.Add(new CustomMonsterAiMigrationRow
                {
                    AI = group.Key,
                    Count = group.Count(),
                    CustomCount = group.Count(x => customMonsterIndexes.Contains(x.Index)),
                    CurrentImplementation = suggestion.CurrentImplementation,
                    SuggestedTemplate = suggestion.SuggestedTemplate,
                    Priority = suggestion.Priority,
                    Notes = suggestion.Notes,
                    Examples = group.Take(8).Select(x => x.MonsterName).Where(x => !string.IsNullOrWhiteSpace(x)).ToList(),
                });
            }

            return report;
        }

        private static AiMigrationSuggestion InferSuggestion(int ai)
        {
            if (ai <= 0)
                return new AiMigrationSuggestion("MonsterObject/Guard", "按具体用途选择 Guard 或普通模板", "Medium", "需要结合怪物名称和用途判断。");

            return new AiMigrationSuggestion(
                $"C# AI {ai}",
                "先导出为迁移报告 TODO，再按派生类拆 RuleSet",
                "Low",
                "当前没有内置迁移建议，建议先查看 MonsterObject.GetMonster 对应分支和派生类。");
        }

        private readonly record struct AiMigrationSuggestion(string CurrentImplementation, string SuggestedTemplate, string Priority, string Notes);
    }
}
