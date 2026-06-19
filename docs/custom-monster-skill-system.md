# 自定义怪物与技能文本系统

## 目录结构

默认文本目录：

```text
Envir\Custom\Resources.txt
Envir\Custom\Monsters\*.txt
Envir\Custom\Skills\*.txt
Envir\Custom\Monsters\Templates\*.txt
Envir\Custom\Skills\Templates\*.txt
```

文本用于快速开发和调试；稳定后可以通过数据库编辑器导入数据库。导入后可以删除文本目录或关闭文本覆盖，运行时仍按数据库配置工作。

`Monsters\Templates` 和 `Skills\Templates` 用于保存完整可验证样板，只在“验证自定义系统”时参与校验；运行时文本覆盖和正式导入数据库不会读取这些模板。`Examples` 目录里的 `.example` 文件只是片段说明，不参与校验。

仓库内置的 `Envir\Custom\Resources.txt`、`Templates` 和 `Examples` 会在构建 Server 项目时复制到服务端输出目录。根目录正式怪物/技能脚本不会由构建自动生成，避免覆盖正在调试或正式运行的文本配置。

文本验证和导入会先预索引本批次 `Monsters\*.txt` 的 `[Monster]` 名称；验证模式还会预索引 `Monsters\Templates\*.txt`。因此 `Skills\*.txt` 可以直接用 `SummonMonster="怪物名"` 引用同批文本里的新怪物，不需要先把怪物单独导入数据库。运行时文本覆盖仍要求客户端和数据库已经知道基础 `MonsterInfo/MagicInfo` 主对象；但怪物视觉动作和自定义技能特效会由服务端随怪物对象包、技能列表、技能动作包下发轻量快照，客户端不需要先存在对应的 `CustomMonsterVisualInfo/CustomSkillInfo` 数据库记录。全新怪物或全新技能稳定后仍建议导入数据库，保证删除文本目录后完全回到数据库模式。

布尔字段支持 `True/False`、`1/0` 和 `yes/no`，导出时会统一写成 `True/False`。文本支持单独成行的 `#INSERT [file]` 和 `#INCLUDE [file]`，路径必须位于 `Envir\Custom` 内；未写扩展名时按 `.txt` 解析，可在行尾追加 `;` 或 `//` 注释。

## 资源

`Resources.txt` 每行定义一个资源。可以引用客户端已加载的 `LibraryFile`，也可以引用客户端 `Data\Custom` 目录内的相对路径；`Path`/`RelativePath` 不能指向 `Data\Custom` 之外。

```text
EmperorSaWooLoaded LibraryFile=Mon_14 DisplayName="原客户端 Mon-14 牛魔王图库" Scope=Monster Enabled=True
CustomFire Path=Data\Custom\Magic\fire.zl DisplayName="自定义火焰" Scope=Skill Enabled=True
```

`LibraryFile=Mon_14` 表示直接复用客户端现有图库。`FrameBase` 用于指定图库内某个怪物块的起始偏移，例如牛魔王是 `Mon_14 + BodyShape=9`，可以写 `FrameBase=9000`。

## 怪物视觉

怪物文件包含 `[Monster]`、`[Visual]`、多个 `[Animation]`、`[Behavior]`、`[AIAction]` 和 `[AIRule]`。

```text
[Monster]
Name="天之牛魔王"
Image=EmperorSaWoo
AI=41
Level=250
ViewRange=10
AttackDelay=900
MoveDelay=600
IsBoss=True

[Stats]
Health=21000
MinAC=54
MaxAC=54
MinMR=61
MaxMR=61
MinDC=255
MaxDC=360
Accuracy=40
Agility=48

[Visual]
Resource=EmperorSaWooLoaded
Scale=1
DrawShadow=True
Blend=False
KeepCorpse=True
DefaultFrameDelay=100
FrameBase=9000
AttackSound=EmperorSaWooAttack
RangeAttackSound=EmperorSaWooAttack
MagicSound=EmperorSaWooAttack
StruckSound=EmperorSaWooStruck
DieSound=EmperorSaWooDie

[Animation]
Action=Walking Resource=EmperorSaWooLoaded Start=80 End=139 FrameCount=6 DirectionCount=8 DirectionOffset=10 FrameDelay=100

[Animation]
Action=Attack Resource=EmperorSaWooLoaded Start=160 End=219 FrameCount=6 DirectionCount=8 DirectionOffset=10 FrameDelay=100
Sound=EmperorSaWooAttack
EffectResource=CustomFire
EffectTarget=Self
EffectStart=0
EffectEnd=6
EffectFrameDelay=100
EffectDuration=1200
EffectTriggerFrame=1
EffectBlend=True
```

`Start/End` 是相对 `FrameBase` 的图片编号。`[Animation] Resource` 可以和 `[Visual] Resource` 不同；客户端会按当前动作切换到该动作指定的 ZL 图库，未配置时才回退到 `[Visual] Resource`。这样同一个怪物可以把走路、攻击、死亡、出现等动作拆在不同 ZL 文件里，而不需要在 `MonsterImage` 里写死。`OffsetX/OffsetY` 会作用在该动作的绘制、阴影和鼠标命中上，适合修正不同资源帧中心点不一致的问题。

客户端会把动态资源诊断写入 `Errors\DynamicLibraryDiagnostics.txt`。诊断覆盖缺少 ZL、Hash 不匹配、客户端已加载图库未找到、怪物动作帧段越界、怪物动作特效帧段越界、自定义技能特效帧段越界。文本覆盖和数据库自定义怪物视觉都会参与这些诊断，因为服务端会把编译后的视觉快照随 `ObjectMonster/DataObjectMonster` 下发。配置 `Mon_14`、`Mon_20` 这类多怪共用图库时，优先用 `FrameBase + Start/End + FrameCount + DirectionOffset` 定位具体怪物动作；如果配置越过图库范围，日志会包含怪物名、动作、资源、`FrameBase` 和计算后的 `MaxIndex`。

`Variant=0` 表示默认动作；`Variant=1..8` 对应客户端 `Combat1..Combat8` 动作槽。`[AIAction]` 和 `[Skill]` 可以配置 `AnimationVariant=1..8`，让某次攻击、远程攻击或自定义技能释放播放指定动作组：

```text
[Animation]
Action=Magic
Variant=4
Resource=BossMagic
Start=0
End=47
FrameCount=6
DirectionOffset=10

[AIAction]
Name=BossIceBurst
Type=CastSkill
MagicInfo="自定义冰爆"
AnimationVariant=4
```

`[Visual]` 可配置动作默认音效：

- `AttackSound`、`RangeAttackSound`、`MagicSound`
- `StruckSound`、`DieSound`
- `ShowSound`、`HideSound`

`[Animation]` 可用 `Sound` 覆盖该动作的声音，也可以配置动作特效。`EffectResource` 指向 `Resources.txt` 里的资源 Key；`EffectTarget` 支持 `Self`、`CurrentTarget`、`AttackTargets`、`CurrentLocation`、`MagicLocations`；`EffectTriggerFrame` 为空或小于 0 时使用动作默认触发帧，攻击默认第 1 帧，远程默认第 4 帧，受击/死亡/显隐默认第 0 帧；`EffectDuration` 是可选毫秒数，配合 `EffectLoop=True` 可以让循环特效自动结束。这样新怪物的攻击光效、受击光效、出现特效不需要再写进 `Client\Models\MonsterObject.cs` 的 `MonsterImage` 分支。

`[Stats]` 可以写任意 `Stat` 枚举名，也可以用多个 `[Stat]` 段表达：

```text
[Stat]
Stat=Health
Amount=21000
```

## 怪物掉落

怪物文件可以包含 `[Drops]` 段。段内复用现有 Mir2 掉落工具语法，支持普通物品、金币、`GROUP`、`GROUP*`、`GROUP^`、`Amount`、`DropSet`、`PartOnly`、`EasterEvent`、`Mir2Gold`。金币行可以写 `Gold 数量`，也可以写数据库配置的金币掉落物品名加 `Amount=数量`；默认按 Mir2 金币随机范围处理，需要固定数量时显式写 `Mir2Gold=False`。

```text
[Drops]
1/20 Gold 5000
1/200 "祝福油"
1/1000 GROUP*
{
    1/1 "裁决之杖"
    1/1 "骨玉权杖"
    1/1 "龙纹剑"
}
```

文本运行时模式会把 `[Drops]` 编译成内存掉落覆盖；导入数据库时会写入 `DropInfo/DropGroupInfo/DropGroupItemInfo`。数据库导入会先验证缺失物品；`ReplaceExisting` 只会在对应怪物掉落段解析通过后清空原掉落并重建，避免失败文本把旧掉落清空。`Merge` 会追加掉落。导出自定义怪物时，如果数据库里已有掉落，会内联导出成 `[Drops]`。

## RuleSet AI

`Template=RuleSet` 后，怪物使用可配置 AI 规则，不再依赖旧的 C# 派生类 AI。

`Template=PickupCompanion` 或 `IsPickupCompanion=True` 表示轻量拾取随从行为：该怪物被自定义召唤技能召出并拥有 `PetOwner` 时，会跟随主人、搜索主人可见范围内可拾取的掉落，并调用玩家拾取逻辑把物品放入主人背包。完整的随从背包、外观解锁、随从商店仍使用原有 `CompanionInfo/UserCompanion` 系统。

旧 AI 常见的基础运行时字段可以直接写在 `[Behavior]`：

- `Passive=True`：被动怪，不主动搜索攻击目标。
- `NeedHarvest=True`、`HarvestCount=2`：死亡后需要采集，次数按旧采集逻辑递减。
- `AvoidFireWall=True`：沿用怪物原有避火墙标记。
- `IgnoreShield=True`：怪物基础攻击和自定义怪物技能忽略目标护盾。
- `IgnoreArmour=True`：怪物基础攻击和自定义怪物技能不扣除目标 AC/MR。
- `PoisonType=Green`、`PoisonRate=10`、`PoisonTicks=5`、`PoisonFrequency=2`：基础攻击命中后按旧怪物逻辑附毒，`PoisonFrequency` 单位是秒。`PoisonRate=0` 表示每次命中必定附毒，大于 0 表示 1/N 概率；`PoisonValue=0` 时使用怪物 SC，`PoisonCanKill=False` 时毒伤不会致死。动作级 `AIAction.TickRate` 仍使用毫秒，不要把 `TickRate=1000` 直接搬到 `PoisonFrequency`。
- `PoisonStackable=True`：同类型毒允许叠加，不会替换目标身上已有的同类毒，适合强绿毒、持续压血类怪物。
- `MaxMinions=20`：限制该怪物维护的召唤物数量；配合 `SpawnMinions` 使用。
- `DisableMovement=True`：强制 `MoveDelay=0`，用于固定炮台、树、门、柱子。
- `DisableAttack=True`：强制 `AttackDelay=0`，用于纯阻挡或纯采集物体。
- `DisableRegen=True`：关闭自然回血，适合 QuartzTree 这类阶段召唤物或固定 Boss。
- `DisableBlocking=True`：不阻挡格子，适合 Tornado、地面危险区、临时场控物体。
- `AlwaysActive=True`：即使附近没有玩家也保持激活，适合传送门、陷阱、光环、全局事件功能怪。
- `DamageProxy=True`：把该怪物变成伤害代理对象，适合 CursedDoll/替身娃娃。技能生成时可绑定施法者和目标，代理怪受击后不伤自身，而是把伤害转发到绑定目标。
- `UseBaseFallback=False`：RuleSet 未执行任何规则时不再退回旧 `MonsterObject.ProcessTarget/Attack`。推荐新自定义怪物关闭它，让文本规则完全接管行为，避免技能冷却时触发旧硬编码攻击。

隐藏态控制：

- `HiddenBlocks=False`：隐藏时不阻挡格子，适合雕像、伏地、潜行类怪物。
- `HiddenCanAttack=False`：隐藏时不能攻击。
- `HiddenCanBeAttacked=False`：隐藏时不能被攻击或中毒。

保护源控制：

- `ProtectedByMonsterFlag=BloodStone`：地图中存在指定 `MonsterFlag` 的存活怪物时，本怪物免疫伤害和中毒。
- `ProtectedByMonsterFlags=BloodStone|SamaSorcerer`：多个保护源，和 `ProtectedByMonsterFlag` 兼容叠加。
- `ProtectionRadius=0`：全地图检查；大于 0 时只检查自身半径范围。
- `ProtectionCheckInterval=1000`：保护源检查缓存毫秒数，避免每次受击都扫全地图。

常用触发器：

- `OnSpawn`：生成后执行。
- `OnTick`：存活时的常规循环执行，不要求当前已有目标。
- `OnTargetTick`：目标处理循环执行。
- `OnAttack`：进入攻击流程时执行。
- `OnStruck`：受击后执行。
- `OnDeath`：死亡后执行。
- `OnMove`：坐标变化后执行，适合清理移动前的临时状态，例如 FerociousIceTiger 的旋转攻击标记。

常用条件：

- `TargetExists`、`TargetMissing`
- `TargetInAttackRange`、`TargetOutOfAttackRange`
- `TargetInLine`、`TargetOutOfLine`，判断当前目标是否处于 8 方向直线上
- `SameCellAsTarget`
- `TargetDistanceBelow`、`TargetDistanceAbove`
- `TargetIsPlayer`、`TargetIsHero`、`TargetIsPlayerOrHero`
- `TargetIsMonster`、`TargetIsPetMonster`、`TargetIsWildMonster`、`TargetIsBossMonster`
- `TargetClassWarrior`、`TargetClassWizard`、`TargetClassTaoist`、`TargetClassAssassin`
- `TargetCountAtLeast`，可配合 `Radius`
- `HealthBelow`、`HealthAbove`
- `TargetHealthBelow`、`TargetHealthAbove`
- `MinionCountBelow`、`MinionCountAbove`
- `SelfHasBuff`、`SelfMissingBuff`
- `TargetHasBuff`、`TargetMissingBuff`
- `CounterBelow`、`CounterAbove`、`CounterEquals`
- `Visible`、`Hidden`
- `CanAttack`、`CanMove`

血量条件使用 `Value` 作为百分比。`HealthBelow/HealthAbove` 判断自身生命百分比，`TargetHealthBelow/TargetHealthAbove` 判断当前目标生命百分比。Buff 条件必须配置 `BuffType`，适合做破盾、狂暴、目标已被标记时切换技能等旧 C# AI 常见分支。计数器条件使用 `CounterName` 和 `Value`，配合 `SetCounter/ChangeCounter` 可以表达 Boss 阶段、已触发机制、循环计数等原来需要 C# 字段保存的状态。

`AIRule` 支持组合条件：

```text
[AIRule]
Name=PhaseTwoCone
Trigger=OnTargetTick
Condition=TargetExists
Conditions=HealthBelow:50;CounterEquals:1:Phase
ConditionJoin=And
Action=BossCone
Priority=8
Cooldown=6000
```

`Conditions` 用分号分隔，单项格式是 `条件名:Value:Radius`；Buff 条件可以写 `TargetMissingBuff:MagicShield`，计数器条件可以写 `CounterEquals:1:Phase`。`ConditionJoin=And/Or` 控制组合方式，`InvertCondition=True` 可以反转整条规则结果，单个条件前加 `!` 可以只反转该条件。

目标类型条件适合迁移旧 `CanPvPRange`、守卫、治疗怪和召唤物相关 AI。比如只允许对非玩家目标远程：

```text
[AIRule]
Name=RangeNonPlayer
Trigger=OnTargetTick
Condition=TargetDistanceAbove
Value=1
Conditions=!TargetIsPlayer;!TargetIsHero;TargetDistanceBelow:7
ConditionJoin=And
Action=RangeShot
Priority=30
Cooldown=5000
```

常用动作：

- 移动/目标：`SearchTarget`、`MoveToTarget`、`RandomStepWhenSameCell`、`HazardRoam`、`FaceTarget`、`ClearTarget`
- 距离控制：`MoveAwayFromTarget` 可让远程怪在目标靠太近时后撤。
- 攻击：`MeleeAttack`、`RangeAttack`、`LineAttack`、`AttackMagic`、`AttackAoE`、`LineAoE`、`MassAreaDamage`、`CastSkill`
- 召唤/位移：`SpawnMinions`、`RecallMonstersByFlag`、`SwapRandomMinionLocations`、`TeleportSelf`、`TeleportNearTarget`、`TeleportTarget`、`TeleportTargetsRandom`、`TeleportTargetsToRegion`、`PullTarget`、`PushTarget`
- 状态：`ApplyPoison`、`PurifyTargets`、`AddBuff`、`RemoveBuff`
- 计数器：`SetCounter`、`ChangeCounter`
- 组合：`WeightedChoice`、`Sequence`、`ScheduleAction`
- 显隐/生命周期：`Show`、`Hide`、`WakeNearby`、`ReviveSelf`、`DelayActionTime`、`DelayMoveTime`、`DelayCellTime`、`KillSelf`、`KillMinions`、`DespawnSelf`
- 位置状态：`StoreLocation`、`ReturnToStoredLocation`
- 表现特效：`MapEffect`、`ObjectEffect`、`BroadcastMessage`。特效动作通过 `Options=Effect=SummonSkeleton` 播放现有地图或对象特效。

`WeightedChoice` 和 `Sequence` 的 `Options` 格式：

```text
Options=MassCyclone:1,TargetAreaHit:2
```

`Sequence` 默认会尽量执行所有动作；需要前一步失败就停止时，可以在 `Options` 里追加 `RequireAll=True`：

```text
Options=TeleportNearTarget:1,MarkAmbush:1,RequireAll=True
```

`ScheduleAction` 用 `Options` 引用同一怪物里的一个或多个 `AIAction`，并在 `Delay` 毫秒后执行。它适合迁移旧 C# 里常见的“先播隐藏动画，900ms 后才真正不可见”或“延迟触发下一段动作”。延迟动作会保留触发时的规则上下文目标，例如 `OnStruck` 的攻击者或 `OnHitAction` 的命中目标。

`HazardRoam` 用于迁移 Tornado、移动毒云、移动火墙这类“非阻挡但会移动并造成接触伤害”的临时怪物。推荐配合 `[Behavior] DisableBlocking=True`、`UseBaseFallback=False` 使用。常用选项：

- `ForwardChance=80`：本次按当前方向前进的概率，未命中时随机转向。
- `AttackWhenBlocked=True`：当前格或前方格有可攻击阻挡对象时触发碰撞伤害。
- `AttackCurrentCell=True`：检查当前格，匹配旧 Tornado 和目标重叠时伤害。
- `AttackBlockedCell=True`：前方被目标阻挡时也能伤害，适合比旧逻辑更直观的移动危险区。
- `CollisionRadius=1`：碰撞触发后按指定半径结算伤害。
- `CollisionAction=动作名`：碰撞时改为执行另一个 `AIAction`，可组合特效、召唤或更复杂的伤害逻辑。

```text
[AIAction]
Name=HazardMove
Type=HazardRoam
Radius=1
Delay=100
Power=DC
PowerRate=100
Magic=Tornado
Element=Wind
Options=AttackWhenBlocked=True,AttackCurrentCell=True,AttackBlockedCell=True,ForwardChance=80,CollisionRadius=1
```

`StoreLocation` 和 `ReturnToStoredLocation` 用于迁移 DarkKing 这类“先保存原位，瞬移连击，最后回到原位”的 Boss 机制。两者用同一个 `CounterName` 或 `Options=Key=名称` 绑定。`ReturnToStoredLocation` 支持 `Options=LeaveEffect=False,EnterEffect=False,RestoreDirection=True,Clear=True`：

```text
[AIAction]
Name=StoreOrigin
Type=StoreLocation
CounterName=ComboOrigin

[AIAction]
Name=ReturnOrigin
Type=ReturnToStoredLocation
CounterName=ComboOrigin
Options=LeaveEffect=False,EnterEffect=False
```

```text
[AIAction]
Name=PlayHide
Type=Hide
Options=PacketOnly=True

[AIAction]
Name=SetHidden
Type=Hide
Options=StateOnly=True

[AIAction]
Name=HideAfterAnimation
Type=ScheduleAction
Options=SetHidden:1
Delay=900
```

`AIRule` 支持 `MaxExecutions`，用于 Boss 阶段、一次性变身、一次性召唤等旧 C# AI 常见逻辑。默认 `0` 表示不限次数。

```text
[AIRule]
Name=SpawnAtHalfHp
Trigger=OnTick
Condition=HealthBelow
Value=50
Action=SpawnMinions
Priority=5
MaxExecutions=1
```

`OnMove` 可以和计数器组合表达“移动后重置状态”。例如旋转攻击命中后设置 `Spinning=1`，只要怪物不移动就持续旋转；移动后由 `OnMove` 清空：

```text
[AIAction]
Name=ClearSpin
Type=SetCounter
CounterName=Spinning
FixedCount=0

[AIRule]
Name=ClearSpinAfterMove
Trigger=OnMove
Condition=Always
Action=ClearSpin
Priority=1
```

`SpawnMinions` 使用 `FixedCount + RandomCount` 决定召唤数量，`Radius` 决定在动作位置周围寻找出生点，`SpawnMonster` 为空时使用怪物原有 `SpawnList`。默认位置是自身；配置 `Target=CurrentTarget` 或 `Target=TargetRadius` 时可在目标附近召唤；配置 `Target=ExperienceOwner` 或 `ExperienceOwnerGroup` 时可在击杀归属玩家或队伍成员附近召唤。`Options=PerTarget=True,MaxTotal=12` 可按每个目标分别生成并限制总数。`MinionDuration=0` 表示永久 minion；大于 0 时表示毫秒持续时间，到期后直接消失，不触发死亡掉落。召唤物会继承当前规则上下文目标，例如 `OnStruck` 触发时会优先攻击本次攻击者。

Boss 护卫补位可以在 `SpawnMinions` 上配置 `Options=MinionLeashRadius=10`。执行召唤前会先清理已经死亡、失效、离开当前地图或离主人超过半径的 minion，再按 `MaxMinions` 和本次数量补召唤物。`LeashRadius` 是兼容别名。

`SwapRandomMinionLocations` 和 `KillMinions` 用于迁移 DemonicLunatic 这类“本体 + 分身”机制。`SwapRandomMinionLocations` 默认把自身也纳入候选，随机选择两个存活 minion/自身交换位置；`Options=Repeat=18,LeaveEffect=False,EnterEffect=False` 可做出生后的无特效洗牌。`KillMinions` 会清理当前 `MinionList`，死亡触发时也能使用死亡前保存的 minion 快照，适合本体死亡后清掉所有分身：

```text
[AIAction]
Name=InitialShuffle
Type=SwapRandomMinionLocations
Options=IncludeSelf=True,LeaveEffect=False,EnterEffect=False,Repeat=18,Attempts=20

[AIAction]
Name=KillMirrors
Type=KillMinions
Options=Despawn=False,Clear=True
```

已有护卫或地图内指定怪物的回位可以用 `RecallMonstersByFlag`。它会扫描当前地图，按 `SpawnMonster` 或 `Options=MonsterFlag=...` 匹配怪物，`Distance` 表示超过这个半径才回位，`Radius` 表示传送回目标点附近的搜索半径。目标点默认是自身，也可以用 `Options=OriginDirection=DownLeft,OriginDistance=2` 配置相对偏移，适合 SamaProphet 这类“护卫必须站在 Boss 附近固定方向”的旧 AI：

```text
[AIAction]
Name=RecallProtector
Type=RecallMonstersByFlag
Distance=9
Radius=1
MaxTargets=1
Options=MonsterFlag=SamaSorcerer,OriginDirection=DownLeft,OriginDistance=2
```

```text
[AIAction]
Name=SpawnTimedAdds
Type=SpawnMinions
Target=CurrentTarget
SpawnMonster="自定义小怪"
FixedCount=3
RandomCount=2
Radius=5
MinionDuration=30000

[AIAction]
Name=DeathSpawnNearParty
Type=SpawnMinions
Target=ExperienceOwnerGroup
SpawnMonster="自定义追击小怪"
FixedCount=2
Radius=3
Options=PerTarget=True,MaxTotal=12
```

`TeleportNearTarget` 用于迁移旧 Banyo/Goru/Oma 类“瞬移到目标附近再强化下一击”的 AI。`MinOffset` 是离目标最近距离，`Radius` 或 `Distance` 是最大尝试距离。下一击强化建议用计数器组合表达，而不是写死在瞬移动作里：

```text
[AIAction]
Name=TeleportAmbush
Type=Sequence
Options=TeleportBehindTarget:1,MarkAmbush:1,RequireAll=True

[AIAction]
Name=TeleportBehindTarget
Type=TeleportNearTarget
Target=CurrentTarget
MinOffset=1
Radius=1

[AIAction]
Name=MarkAmbush
Type=SetCounter
CounterName=Ambush
FixedCount=1
```

`TeleportSelf` 用于把怪物自身随机传送到某个参考点周围。默认参考点由 `Target` 决定，`Target=Self` 表示自身当前位置，`Target=CurrentTarget` 表示目标位置；`MinOffset` 是最小随机距离，`Radius` 或 `Distance` 是最大随机距离。旧 C# 里的 `TeleportNearby(7, 12)` 可以写成：

```text
[AIAction]
Name=FarBlink
Type=TeleportSelf
Target=Self
MinOffset=7
Radius=12
```

`PullTarget` 和 `PushTarget` 支持集合目标。`Target=AllInView` 时可以批量拉近或推开目标，适合 FrostLordHwa 这类全屏拉人后叠加多毒的 Boss：

```text
[AIAction]
Name=PullAllTargets
Type=PullTarget
Target=AllInView
Radius=2
```

`TeleportTargetsRandom` 用于迁移 QueenOfDawn 这类场控 Boss。默认按动作 `Target` 选择集合目标并随机传送；`Options=AllMap=True` 会扫描当前地图所有可攻击目标，`Attempts=25` 控制每个目标寻找随机有效格子的尝试次数。配置 `Radius` 时会改为随机传送到动作位置半径内：

```text
[AIAction]
Name=RandomTeleportAllEnemies
Type=TeleportTargetsRandom
Options=AllMap=True,Attempts=25

[AIRule]
Name=MapWideRandomTeleport
Trigger=OnTick
Condition=Always
Action=RandomTeleportAllEnemies
Cooldown=60000
```

`TeleportTargetsToRegion` 用于迁移 NetherworldGate、JinamStoneGate 这类事件传送门。它按自身半径扫描玩家、野怪或宠物，并随机传送到指定 `MapRegion` 的点集。`SpecialRegion=MysteryShip` 使用 `Config.MysteryShipRegionIndex` 对应区域，`SpecialRegion=Lair` 使用 `Config.LairRegionIndex` 对应区域；也可以用 `RegionIndex=123`、`RegionDescription=地图 - 区域` 或 `RegionName=区域名` 指向任意数据库区域。

```text
[Behavior]
Template=RuleSet
UseBaseFallback=False
AlwaysActive=True
Passive=True
CanBeKilled=False
DisableMovement=True
DisableAttack=True
AttackRange=0

[AIAction]
Name=GateOpenMessage
Type=BroadcastMessage
Options=Message=异界之门开启，地图：{Map}，坐标：{Location},Scope=All,MessageType=System

[AIAction]
Name=ScheduleClose
Type=ScheduleAction
Delay=1200000
Options=CloseGate

[AIAction]
Name=CloseGate
Type=Sequence
Options=GateCloseMessage,DespawnGate

[AIAction]
Name=TeleportToMysteryShip
Type=TeleportTargetsToRegion
Radius=8
Options=SpecialRegion=MysteryShip,Players=True,Monsters=True,WildMonsters=True,Pets=True,IgnoreSafeZone=False

[AIRule]
Name=TeleportNearbyTargets
Trigger=OnTick
Condition=Always
Action=TeleportToMysteryShip
Cooldown=3000
```

`Options` 中 ASCII 逗号用于分隔选项；如果单个值本身要包含 ASCII 逗号，请只给该值加引号，例如 `Options=Message="第一段, 第二段",Scope=All`。

`DamageProxy` 用于迁移旧 `Doll` 子类。当前 `CursedDoll` 技能仍保留旧 `Doll` 分支；如果 `MonsterFlag=CursedDoll` 的怪物配置了 `DamageProxy=True`，技能会生成 `CustomMonsterObject` 并调用通用代理逻辑。常用字段：

- `DamageProxyRevealDelay=500`：生成后多少毫秒显现。
- `DamageProxySpawnEffect=CursedDoll`：生成时播放的地图特效。
- `DamageProxyRange=0`：攻击者必须离绑定目标多近才允许转嫁伤害；0 表示使用 `Globals.MagicRange`。
- `DamageProxyNameFormat="{Target} Doll"`：下发给客户端的显示名格式，支持 `{Target}`、`{Name}`、`{Monster}`。

```text
[Behavior]
Template=RuleSet
UseBaseFallback=False
AlwaysActive=True
Passive=True
DamageProxy=True
CanBeKilled=False
DisableMovement=True
DisableAttack=True
AttackRange=0
```

`PurifyTargets` 复用旧 `MapObject.Purify` 逻辑，可清理友方毒和部分负面 Buff，也可驱散敌方部分有益 Buff。默认按动作 `Target` 选择目标，`Options=AllMap=True` 会扫描当前地图所有对象；`MaxTargets/RandomTargetCount/TargetChance` 可限制目标数量：

```text
[AIAction]
Name=MassPurify
Type=PurifyTargets
Target=AllInView
Radius=12
Delay=500

[AIAction]
Name=MapWidePurify
Type=PurifyTargets
Options=AllMap=True
```

计数器动作示例：

```text
[AIAction]
Name=EnterPhaseTwo
Type=SetCounter
CounterName=Phase
FixedCount=1

[AIRule]
Name=EnterPhaseTwoAtHalfHp
Trigger=OnTick
Condition=HealthBelow
Value=50
Conditions=CounterBelow:1:Phase
Action=EnterPhaseTwo
Priority=1
MaxExecutions=1
```

```text
[AIRule]
Name=UseExecuteWhenTargetLow
Trigger=OnAttack
Condition=TargetHealthBelow
Value=30
Action=ExecuteSkill
Priority=3
Cooldown=5000

[AIRule]
Name=RecastShield
Trigger=OnTick
Condition=SelfMissingBuff
BuffType=MagicShield
Action=SelfShield
Priority=4
Cooldown=8000
```

`[Behavior]` 下的 `[Skill]` 是轻量技能轮询列表，适合远程怪、法术怪、Boss 的常规技能。`Target` 可省略，默认 `CurrentTarget`；也可以写 `Self`、`AllyRadius`、`RandomInView`、`LowestHealthAllyInView` 等选择器。治疗和 Buff 技能会按友方规则校验目标，不会再强制只对自己生效。

```text
[Skill]
Magic="自定义怪物治疗"
Target=LowestHealthAllyInView
Chance=35
Cooldown=3000
Range=8
```

## AIAction 参数

常用参数：

```text
[AIAction]
Name=TargetAreaHit
Type=RangeAttack
Target=TargetRadius
Radius=2
Delay=400
Power=DC
PowerRate=100
Element=None
MagicInfo="自定义远程特效"
```

`MagicInfo` 可绑定一个数据库里的 `MagicInfo`。如果该技能有 `CustomSkillInfo/Effect`，客户端会用自定义技能特效显示 `RangeAttack/ObjectMagic/ObjectProjectile`，减少 `MonsterImage` switch 硬编码。文本覆盖和数据库自定义技能都会在登录技能列表、获得新技能、广播动作包时附带轻量技能快照，客户端收到后写入本次连接的服务端快照覆盖缓存；本地数据库里已有 `CustomSkillInfo` 时仍可作为回退路径。

`Distance` 对 `RangeAttack`、`AttackMagic`、`AttackAoE` 是硬限制：配置了 `Distance` 后，目标或落点超出距离时动作不会执行。这样模板可以把射程写在动作上，而不是每条规则都重复写 `TargetDistanceBelow`。

`AIAction.Options` 支持 `ClassPowerRates`，可按玩家或英雄职业覆盖 `PowerRate`。它用于迁移 DoomClaw 这类“同一个范围攻击命中不同职业时伤害倍率不同”的旧 C# AI。格式用 `|` 分隔，避免和 `Options` 的逗号分隔冲突：

```text
[AIAction]
Name=ClassScaledWave
Type=MassAreaDamage
Target=SelfRadius
Radius=10
Delay=400
Power=DC
PowerRate=100
Options=ClassPowerRates=Warrior:60|Wizard:70|Taoist:40|Assassin:80
```

攻击类动作还支持目标最大生命附加伤害，用来迁移 DarkKing、StrongBoss 这类 Boss 技能：

- `TargetHealthRate=1`：额外附加目标最大生命的 1%。
- `TargetHealthSourceRate=100`：额外附加 `PowerSource * 目标最大生命 / 100`。
- `TargetHealthPower=SC`：只覆盖目标生命附加部分的来源，基础伤害仍按 `Power` 计算。它适合迁移 StrongBoss 这类 `DC + SC * 目标生命 / 100` 的旧 C# 公式。

```text
[AIAction]
Name=TeleportComboHit
Type=MeleeAttack
Power=DC
PowerRate=100
Options=TargetHealthRate=1

[AIAction]
Name=MassFireWithTargetHealth
Type=MassAreaDamage
Target=SelfRadius
Radius=12
Power=SC
PowerRate=100
Element=Fire
Options=TargetHealthSourceRate=100,TargetHealthPower=SC
```

攻击类动作支持 `Options=NoDamage=True`，只播放对应攻击包，不结算伤害。它适合和 `AddBuff`、`SpawnMinions`、`ObjectEffect` 组合，迁移“先播施法动作，再上 Debuff 或召唤”的旧 AI：

```text
[AIAction]
Name=DebuffCastVisual
Type=RangeAttack
Target=SelfRadius
Radius=3
Options=NoDamage=True
```

攻击类动作也支持 `Options=DrainMP=True`，在伤害成功后按实际伤害扣除目标 MP，适合迁移冰灵将军这类“暗属性远程攻击同时扣蓝”的旧 AI。

攻击类动作支持 `Options=IgnoreShield=True` 和 `Options=IgnoreArmour=True`，只让本次 `MeleeAttack`、`RangeAttack`、`LineAttack`、`AttackMagic`、`AttackAoE`、`LineAoE`、`MassAreaDamage` 等动作忽略目标护盾或 AC/MR。`[Behavior] IgnoreShield/IgnoreArmour` 是怪物全局开关；动作级配置适合同一个怪物里“普通攻击正常结算，但某个声波、吐息或破甲攻击特殊结算”的旧 C# AI。

攻击类动作支持 `Options=OnHitAction=动作名`，伤害成功后会把本次命中的目标作为规则上下文执行指定动作。配合 `Sequence` 和 `Options=ActionChance=百分比`，可以迁移 LordNiJae 这类“命中后 20% 绿毒、10% 麻痹”的旧 C# 逻辑。`RangeAttack Travel=True` 会按释放者到目标的距离追加弹道延迟：

```text
[AIAction]
Name=FullViewStrike
Type=RangeAttack
Target=AllInView
Radius=8
Delay=500
Travel=True
Power=DC
PowerRate=100
Options=OnHitAction=OnHitPoisons

[AIAction]
Name=OnHitPoisons
Type=Sequence
Options=GreenPoison:1,ParalysisPoison:1

[AIAction]
Name=GreenPoison
Type=ApplyPoison
Target=CurrentTarget
PoisonType=Green
Power=SC
PowerRate=100
Duration=20000
TickRate=2000
Options=ActionChance=20
```

`AIAction` 的集合目标动作支持 `MaxTargets` 和 `RandomTargetCount`。`MaxTargets=3` 表示按选择器排序后最多命中 3 个；`RandomTargetCount=3` 表示从候选集合里随机取 3 个，适合 Boss 随机点名，不需要再拆多个随机动作。

`KillSelf` 用于自爆怪和陷阱怪。死亡触发器里如果要继续执行范围伤害，给 `MassAreaDamage` 写 `Options=AllowDead=True`：

```text
[AIAction]
Name=ExplodeOnDeath
Type=MassAreaDamage
Target=SelfRadius
Radius=3
Delay=800
Power=DC
PowerRate=100
Options=AllowDead=True
```

`MapEffect` 可播放现有 `Effect` 枚举里的地图特效，适合迁移 WhiteBone/Shinsu 这类出生召唤特效；`ObjectEffect` 绑定在怪物对象上，适合迁移 DepartedMonster 这类死亡或变身特效：

```text
[AIAction]
Name=SpawnEffect
Type=MapEffect
Target=Self
Options=Effect=SummonSkeleton

[AIAction]
Name=DeathObjectEffect
Type=ObjectEffect
Options=Effect=PuppetFire
```

`LineAttack` 用来迁移蜘蛛、直线法师、声波蜥蜴这类旧 C# 直线物理攻击。`Distance` 是直线长度，`TargetInLine` 负责判断目标是否在 8 方向直线上，`MinOffset/MaxOffset` 可做扇形多线，`Options=AllDirections=True` 可做 8 方向同时扫线，`Options=RangePacket=True` 可使用远程攻击动作包。

`LineAoE` 用来迁移 Sama 守护、火鸟、火焰恶魔这类线型法术范围。`MinOffset/MaxOffset` 仍表示相对当前朝向的偏移；`Options=AllDirections=True` 会从自身向 8 个方向同时铺线，适合火鸟八方向灼地爆发。命中主线按动作配置伤害结算，副格按一半伤害结算。

`Target=Line` 是通用目标选择器，不会自己造成伤害。它从怪物当前位置沿当前目标方向扫线，没有当前目标时沿怪物当前朝向扫线；`Distance` 控制长度，`MinOffset/MaxOffset` 控制相对方向偏移，`Options=AllDirections=True` 会扫描 8 个方向。它适合给 `MassAreaDamage`、`CastSkill`、`ApplyPoison`、`PullTarget`、`PushTarget` 等集合动作使用；如果只是要做旧 C# 直线普通攻击，优先用 `LineAttack`。

```text
[AIAction]
Name=LineSting
Type=LineAttack
Distance=5
Delay=400
Power=DC
PowerRate=100

[AIRule]
Name=LineAttackWhenAligned
Trigger=OnTargetTick
Condition=TargetInLine
Value=5
Action=LineSting
Priority=20
Cooldown=1500
```

如果要让 AIAction 直接执行完整的自定义技能模板，使用 `CastSkill`：

```text
[AIAction]
Name=BossWindSkill
Type=CastSkill
Target=CurrentTarget
MagicInfo="自定义旋风"
Delay=500
```

`CastSkill` 会读取 `CustomSkillInfo` 的 `Template/TargetRule/Range/Radius/Element/BuffType/PoisonType/SummonMonster` 等配置；缺少 `MagicInfo` 会在验证或导入时报错。

地面技能的默认落点由 `Target` 决定，例如 `CurrentTargetLocation` 会落在当前目标脚下，`Self` 会落在自身脚下。需要把落点偏到相邻格时，可以在 `Options` 里配置 `OffsetDirection`/`OriginDirection` 和 `OffsetDistance`/`OriginDistance`，例如 `Options=OffsetDirection=Up,OffsetDistance=1` 会把落点向上偏移一格。这对 `AreaDamage`、`PersistentAreaDamage`、`Move` 等位置型技能都生效。

`CastSkill` 默认只释放到一个目标或一个位置。对 `PersistentAreaDamage`、`AreaDamage`、`Move` 这类地面/坐标 payload 技能，如果要按目标集合逐个落点，可以加 `Options=PerTarget=True`。它会先按 `Target/Radius/MaxTargets/RandomTargetCount/TargetChance` 选出目标，再对每个目标位置分别创建技能结算，适合迁移 JinchonDevil、CastleLord 等多点 DeathCloud。`Move` 使用目标坐标时仍会检查格子是否为空；如果目标站在该格上，位移不会生效，需要用偏移或 `TeleportNearTarget` 表达贴近目标：

```text
[AIAction]
Name=FrenzyClouds
Type=CastSkill
Target=AllInView
Radius=10
MaxTargets=12
MagicInfo="模板持续毒云"
Distance=10
Options=PerTarget=True
```

常用 `Target` 选择器：

- `CurrentTarget`：当前仇恨目标；动作配置 `Radius>0` 时会以当前目标为中心选择范围目标。
- `Self`：自己。
- `CurrentTargetLocation`：当前目标所在坐标，常用于地面范围技能。
- `SelfRadius`、`AllyRadius`、`TargetRadius`、`AllInView`：自己周围全部目标、自己周围友方目标、目标周围、视野内目标集合。
- `Line`：沿当前目标方向或当前朝向扫描直线目标，支持 `Distance`、`MinOffset/MaxOffset` 和 `Options=AllDirections=True`。
- `RandomInView`：视野内随机敌对目标，适合随机点名。
- `NearestInView`、`FarthestInView`：视野内最近/最远敌对目标。
- `LowestHealthInView`、`HighestHealthInView`：视野内最低/最高生命百分比敌对目标。
- `RandomAllyInView`、`NearestAllyInView`、`LowestHealthAllyInView`：视野内友方目标，适合治疗怪、辅助怪、保护怪。
- `Summoner`、`SummonerGroup`：召唤者或召唤者队伍。玩家宠物优先解析为 `PetOwner`，怪物召唤物解析为 `Master`；`SummonerGroup` 对玩家宠物会选择主人队伍成员，对怪物召唤物退回 `Master`。

对地面类技能，`RandomInView`、`LowestHealthInView` 等选择器会把技能坐标放在已选目标的位置；对治疗和 Buff 技能，`AllyRadius`、`RandomAllyInView`、`LowestHealthAllyInView`、`Summoner` 和 `SummonerGroup` 会把选中的友方作为延迟结算目标。

状态动作示例：

动作 `Options` 可以额外过滤候选目标：

- `RequireInjured=True`：只选择当前生命低于最大生命的目标。
- `RequireHasBuff=BuffType`：只选择已有指定 Buff 的目标。
- `RequireMissingBuff=BuffType`：只选择没有指定 Buff 的目标。

这些过滤适合治疗怪和辅助怪，例如 `Options=RequireInjured=True,RequireMissingBuff=Heal` 可以避免重复给满血或已有治疗 Buff 的友军施法。

```text
[AIAction]
Name=PoisonNearTargets
Type=ApplyPoison
Target=SelfRadius
Radius=3
PoisonType=Green
Duration=5000
TickRate=1000
FixedPower=5
```

`AddBuff` 支持 `Options=BuffStatsFromTarget=来源Stat>目标Stat:倍率|...`，用于迁移 GardenDefender 这类“按被 Buff 目标属性计算增益”的旧 AI。下面会给自身周围友方添加 `Resilience`，并把每个目标自己的 `MaxAC` 一半分别写入 `MinAC/MaxAC`：

```text
[AIAction]
Name=DefendBuff
Type=AddBuff
Target=AllyRadius
Radius=1
BuffType=Resilience
BuffStats="PhysicalResistance:1"
Options=BuffStatsFromTarget=MaxAC>MinAC:50|MaxAC>MaxAC:50
Duration=6000
BuffVisible=True
```

随机点名示例：

```text
[AIAction]
Name=RandomMark
Type=CastSkill
Target=RandomInView
Radius=10
MagicInfo="自定义点名雷击"
Delay=500
```

治疗友军示例：

```text
[AIAction]
Name=HealLowestAlly
Type=CastSkill
Target=LowestHealthAllyInView
Radius=8
MagicInfo="自定义怪物治疗"
Delay=300
```

隐藏唤醒示例：

```text
[Behavior]
Template=RuleSet
HiddenBlocks=False
HiddenCanAttack=False
HiddenCanBeAttacked=False

[AIAction]
Name=StartHidden
Type=Hide

[AIAction]
Name=WakePack
Type=WakeNearby
Radius=7

[AIRule]
Name=HideOnSpawn
Trigger=OnSpawn
Condition=Always
Action=StartHidden
Priority=1

[AIRule]
Name=WakeWhenClose
Trigger=OnTargetTick
Condition=TargetDistanceBelow
Value=3
Action=WakePack
Priority=10
```

出生延迟显现示例：

```text
[AIAction]
Name=DelaySpawnAction
Type=DelayActionTime
Delay=2000

[AIAction]
Name=ReplayShow
Type=Show
Options=ForcePacket=True

[AIAction]
Name=SpawnShowSequence
Type=Sequence
Options=DelaySpawnAction:1,ReplayShow:1

[AIRule]
Name=DelayAndShowOnSpawn
Trigger=OnSpawn
Condition=Always
Action=SpawnShowSequence
Priority=1
MaxExecutions=1
```

`DelayActionTime/DelayMoveTime/DelayCellTime` 默认只延长已有时间闸门，不会把更晚的旧冷却缩短。需要强制覆盖时可写 `Options=ExtendOnly=False`；也可以在任意一个延迟动作里追加 `ActionTime=True,MoveTime=True,CellTime=True` 同时设置多个时间。

复活示例：

```text
[AIAction]
Name=ReviveTwice
Type=ReviveSelf
FixedCount=2
Delay=3000
PowerRate=50
Options=SuppressDrops=True

[AIRule]
Name=ReviveBeforeFinalDrop
Trigger=OnDeath
Condition=Always
Action=ReviveTwice
Priority=10
```

`ReviveSelf` 的 `FixedCount + RandomCount` 是总复活次数，`Delay` 是死亡后复活延迟毫秒，`PowerRate` 是复活后的生命百分比。`SuppressDrops=True` 时，未用完复活次数前不会掉落，最后一次死亡才正常结算掉落。`Options=HealthDecay=True` 会让每次复活生命按 `PowerRate / 2^复活次数` 递减，适合 VoraciousGhost 这类亡灵复活怪。

## 自定义技能

技能文件必须包含 `[Magic]` 和 `[Skill]`，可包含多个 `[Effect]`。

```text
[Magic]
Name="自定义远程特效"
Magic=Custom
Class=Warrior
School=Magic
NeedLevel1=1
Delay=1000

[Skill]
Enabled=True
Template=ProjectileDamage
TargetRule=Enemy
Range=8
Radius=0
MaxTargets=0
Shape=Circle
Length=0
Width=0
Angle=0
WaveCount=1
WaveInterval=0
KnockbackDistance=0
PullDistance=0
Element=Wind
PowerSource=Auto
IgnoreShield=False
IgnoreArmour=False
FixedPower=0
PowerRate=100
Duration=3000
TickRate=1000
BuffType=None
PoisonType=None
PoisonValue=0
PoisonCanKill=False
BuffStats=
BuffVisible=True
BuffPause=False
BuffHidden=False
BuffExtra=0
CastDelay=500
ProjectileSpeed=48
SummonMonster="模板召唤怪"
MaxSummons=1
SummonDuration=0

[Effect]
Stage=Projectile
Resource=CustomFire
Start=0
End=7
FrameCount=8
FrameDelay=80
Sound=FireBallTravel
Blend=True
Duration=1000
FollowTarget=True
```

`PowerSource=Auto` 是默认值，会按元素和 `MagicInfo.School` 推断 `DC/MC/SC`；需要完全配置化的新技能可以显式写 `PowerSource=DC`、`PowerSource=MC`、`PowerSource=SC` 或 `PowerSource=Fixed`。玩家和英雄自定义技能的基础公式是 `FixedPower + MagicInfo等级威力 + PowerSource * PowerRate / 100`；`Fixed` 只使用 `FixedPower + MagicInfo` 等级威力，不再叠加释放者攻击属性。怪物自定义技能没有玩家技能等级，使用 `FixedPower + PowerSource * PowerRate / 100`。

多个自定义技能可以共用 `Magic=Custom`。服务端和客户端会通过 `MagicInfo.Index` 区分具体技能，玩家学习、快捷键、释放、冷却、自定义特效和怪物/英雄释放都不需要为每个新技能新增硬编码 `MagicType`。旧技能仍按原 `MagicType` 路径运行。

`IgnoreShield=True` 会让自定义技能结算时忽略目标护盾；`IgnoreArmour=True` 会跳过 AC/MR 扣减。两个字段同时适用于玩家、英雄和怪物自定义技能。怪物技能还会继承 `[Behavior] IgnoreShield/IgnoreArmour`，适合迁移旧 C# AI 中直接设置在怪物对象上的特殊攻击。

`Template=DamageProxy` 用于迁移 `CursedDoll` 这类“生成替身，替身受击转嫁到目标”的技能。它使用 `[Skill] SummonMonster` 指向一个配置了 `[Behavior] DamageProxy=True` 的怪物，例如 `Templates/127_伤害代理替身模板.txt`；如果指向普通召唤怪，文本验证和导入会直接报错。玩家释放时会沿用旧诅咒娃娃的基础限制：目标必须可攻击、不能已经有代理、不能是 Boss、目标等级不能超过玩家 2 级；持续时间优先使用 `SummonDuration`，其次使用 `Duration`，都未配置时按旧技能等级使用 `10 + 等级 * 5` 秒。怪物释放时也可使用同一模板，代理会挂到怪物 `MinionList` 并随持续时间清理：

```text
[Skill]
Enabled=True
Template=DamageProxy
TargetRule=Enemy
Range=8
SummonMonster="模板伤害代理替身"
SummonDuration=0
CastDelay=500
```

英雄 v1 会从 `HeroInfo.Magics` 中选择已学习、已启用且冷却结束的敌对自定义技能，优先于旧的职业自动法术释放。当前支持 `DirectDamage`、`ProjectileDamage`、`ChainDamage`、`AreaDamage`、`PersistentAreaDamage`、`Debuff`、`Control`、`Move`，并会广播 `MagicInfoIndex` 和服务端技能快照让客户端播放自定义特效。英雄会按所有可用自定义敌对技能的有效射程追击；`TargetRule=Self` 的伤害、减益和控制技能按自身中心的 `Radius/Shape` 判断是否命中当前目标，适合近身光环、扇形斩和自身脚下范围技。道士英雄的支援循环会自动尝试自定义 `Heal`、`Buff` 和 `Summon`；`Heal/Buff` 目标限制为 `Self`、`Ally`、`AllyOrSelf`，优先给主人和自身补血或补缺失 Buff；`Summon` 使用 `SummonMonster/MaxSummons/SummonDuration` 挂到主人宠物列表，只有缺少对应宠物时才释放。英雄自动 `Move` 是保守突进：只在存在敌对目标时选择目标周围空格，落点必须在技能 `Range` 内、可站立，并且比当前位置更接近目标。英雄路径会读取 `CastDelay/ProjectileSpeed`、`Radius/Shape/MaxTargets`、`WaveCount/WaveInterval`，因此同一个敌对或支援技能模板可以同时给玩家、英雄和怪物复用。

`CastDelay` 控制服务端结算基础延迟，`ProjectileSpeed` 控制带弹道技能按距离追加的延迟，默认值与旧技能常用的 `500 + 距离 * 48ms` 保持一致。怪物自定义技能如果配置了 `Template=ProjectileDamage` 或存在 `Stage=Projectile` 特效，也会按距离延后结算，避免视觉弹道未到、伤害已经生效。

`Radius` 是圆形范围参数：`DirectDamage/ProjectileDamage` 会在目标周围做溅射伤害，`Heal/Buff` 会在目标或自身周围选择友方目标，`Debuff/Control` 会在目标周围选择敌对目标。`Shape=Circle` 且 `Radius=0` 时保持单目标行为；`Shape=Line/Cone/Rectangle` 使用 `Length/Width/Angle` 计算范围，可以保持 `Radius=0`。`MaxTargets=0` 表示不限制目标数量，正数表示范围内最多结算多少个目标，适合做最多命中 N 个目标的 Boss 点名、溅射、群疗或群控。

`Shape` 控制范围模板的几何形状：

- `Circle`：以目标点为圆心，使用 `Radius`。
- `Line`：以释放者朝向为中心线，使用 `Length` 和 `Width`。
- `Cone`：以释放者朝向为中心，使用 `Length` 和 `Angle`，`Angle=0` 时默认 90 度。
- `Rectangle`：以释放者朝向为长边，使用 `Length` 和 `Width`。

`WaveCount/WaveInterval` 用于多段命中。`WaveCount=3` 表示同一 payload 结算 3 次，`WaveInterval` 是后续波次间隔毫秒；未配置时会回退到 `TickRate`。`PersistentAreaDamage` 仍使用 `Duration/TickRate` 控制持续区域，不受 `WaveCount` 影响。

```text
[Skill]
Template=AreaDamage
TargetRule=EnemyOrGround
Range=8
Shape=Cone
Length=6
Angle=80
MaxTargets=8
WaveCount=2
WaveInterval=600
Element=Wind
PowerSource=MC
PowerRate=85
```

`Template=ChainDamage` 是链式伤害模板：先命中当前目标，再从上一个命中目标周围寻找下一个最近敌对目标。`Radius` 表示每次跳跃半径，未配置时默认 4；`MaxTargets` 表示链式总命中数，未配置时默认 3。它适合闪电链、弹跳火球、Boss 连锁点名等技能，不需要再为每个链式技能写 C# 类。

```text
[Skill]
Template=ChainDamage
TargetRule=Enemy
Range=8
Radius=4
MaxTargets=5
Element=Lightning
PowerSource=MC
PowerRate=90
CastDelay=500
ProjectileSpeed=48
```

`Template=AreaDamage` 是一次性地面范围伤害；`Template=PersistentAreaDamage` 是持续地面伤害，会在目标点按 `Radius` 结算多次，次数由 `Duration / TickRate` 决定，最少 1 次。它适合火墙、毒云、雷区、Boss 地面危险区等技能，不需要再为每个持续区域新写一个 C# 技能类。

```text
[Skill]
Template=PersistentAreaDamage
TargetRule=Ground
Range=8
Radius=2
Element=Fire
PowerSource=MC
PowerRate=80
Duration=5000
TickRate=1000
```

视觉上可以给持续区域配置 `Stage=Area` 或 `Stage=Tick` 特效。`Stage=Projectile` 会创建从施法者当前位置飞向目标或地面点的动态弹道；命中爆点、治疗目标光效等应放在 `Stage=Hit`、`Stage=Buff` 或 `Stage=Area`。如果同一技能存在可用的 `Stage=Projectile`，客户端会等第一组弹道到达后再播放 `Stage=Hit`；没有弹道时，`Stage=Hit` 会按旧逻辑即时播放。`[Effect] Sound` 会在该段自定义特效创建时播放，可用于施法声、飞行声、命中声或持续区域声；没有 `Resource`、只有 `Sound` 的 `[Effect]` 也会生效，适合先补音效或做无视觉技能。旧 `MagicType` 分支不会再为自定义技能兜底播放音效。`[Effect] Duration` 是客户端特效持续毫秒数，配合 `Loop=True` 可以让地面火墙、毒云、雷区等循环视觉按时自动结束；`[Skill] Duration/TickRate` 仍然只控制服务端伤害结算。

`Debuff/Control` 可以用 `BuffType` 或 `PoisonType` 表达状态。`BuffStats=MaxAC:20,MaxMR:20` 这类写法会把属性写入 Buff；`BuffVisible/BuffPause/BuffHidden/BuffExtra` 直接映射运行时 Buff 参数。未配置 `PoisonType` 时，旧逻辑默认按 `Paralysis` 处理；`PoisonValue` 是每跳伤害或状态值，`PoisonCanKill` 控制毒伤是否允许击杀目标。`Control` 还支持 `KnockbackDistance` 和 `PullDistance`，可以模板化实现击退、拉拽、扇形推人、矩形冲击等旧 C# 技能常见效果。

`Template=Summon` 使用 `SummonMonster` 和 `MaxSummons` 创建召唤物。玩家技能会把召唤物作为宠物管理，`SummonDuration=0` 表示沿用长期召唤，正数表示毫秒持续时间；怪物技能召唤的 minion 由 `Master/MinionList` 管理，配置 `SummonDuration` 后会在时间到达时直接消失，不走死亡掉落。`Template=DamageProxy` 也使用 `SummonMonster`，但它不会创建普通宠物，而是创建伤害代理对象。

`Template=Move` 是地面位移模板，示例见 `Envir\Custom\Skills\Templates\07_模板位移技能.txt`。玩家手动释放和自定义怪物释放时，结算语义都是把释放者自身移动到目标坐标；目标坐标必须在 `Range` 内、地图格存在、没有其它移动对象且不阻挡当前对象。英雄自动 AI 使用 `Move` 时不会传到目标所在格，而是选择当前敌对目标周围的可站空格，且落点必须比当前位置更接近目标，适合突进、闪击、切入类自定义英雄技能。怪物 AI 如果要表达“瞬移到目标附近并追加攻击”，优先使用 `TeleportNearTarget`、`TeleportSelf` 或 `Sequence` 组合；`Move` 更适合明确落点的地面位移技能。`Move` 会走 `WaveCount/WaveInterval` 队列，但常规位移建议保持 `WaveCount=1`。

## 导入导出

数据库编辑器提供：

- 验证文本：只解析和检查，不写数据库。
- 导入文本到数据库：覆盖或合并资源、怪物视觉、怪物行为、自定义技能。
- 导出数据库到文本：输出 `Resources.txt`、怪物文本、技能文本。
- AI 迁移报告：按 `MonsterInfo.AI` 统计当前数据库怪物，列出旧 C# 实现、建议模板、迁移优先级和示例怪物。

导入模式建议：

- `ReplaceExisting`：正式导入推荐模式。会清空匹配怪物的 Stats、视觉动作、行为技能、AI 规则、AI 动作和匹配技能的特效，再按文本重建，最接近“文本就是最终配置”。
- `Merge`：适合小步补充或调试。资源、主怪物、主技能按 Key/Index/Name 更新；视觉动作按 `Action + Variant` 更新；行为技能按 `Magic + Target + AnimationVariant` 更新；`AIAction/AIRule` 优先按 `Name` 更新；技能特效按 `Stage + Resource + Start + End` 更新。重复导入同一份文本不会反复追加这些子对象。
- `[Drops]` 在 `Merge` 下仍按现有掉落工具语义追加；需要让文本掉落完全替代数据库掉落时，用 `ReplaceExisting`。
- 如果数据库里已经有早期重复导入留下的同名动作、同名规则或同一帧段特效，`Merge` 会先清理明确重复项，再更新当前文本。

文本验证会把同一文件内的重复导入键视为错误，例如重复的 `Animation Action+Variant`、重复的 `AIAction.Name`、重复的 `AIRule.Name`、重复的怪物 `[Skill] Magic+Target+AnimationVariant`，以及重复的技能 `[Effect] Stage+Resource+Start+End`。需要多组攻击或多组施法动作时，给它们配置不同 `Variant`；需要多条不同规则时，给每条规则使用不同 `Name`。

推荐工作流：

1. 从数据库导出已有配置。
2. 在文本里修改资源帧、AI 规则、技能模板和特效。
3. 用“验证文本”检查缺资源、缺怪物、缺技能、规则引用错误。
4. 调试期可以在游戏内用 GM 命令 `@RELOADCUSTOMSYSTEM` 重新加载文本覆盖，不必重启服务端；该命令会刷新自定义资源、怪物视觉、怪物行为、技能注册表，并重建怪物掉落表。命令执行者的技能缓存和当前视野对象会重新下发，便于立即看到文本技能和文本视觉调整。
5. 导入数据库。
6. 删除文本目录或关闭文本覆盖，确认数据库模式仍可运行。

迁移旧 C# AI 时，先打开“AI迁移报告”：

- `High`：普通近战、远程、范围、召唤、牛魔王类 Boss 等，当前 RuleSet 和 `Templates` 已基本覆盖。
- `Medium`：需要人工补充技能参数、召唤列表、保护源、忽略防御等字段。
- `Low`：城门、守卫、活动怪、事件 Boss 等，建议暂时保留 C#，后续再拆专用原语。

`Monsters\Templates` 中 `88-111` 进一步把迁移报告里常见的“模板 + 参数”固化成独立样板，适合直接复制为新怪物后改名字、数值、视觉和召唤目标：

- `88-96`：9 格投掷、3 格远近切换、近身踩踏、目标点半径投掷、远程目标点半径、近身雷暴、冰爆麻痹、7 格奴仆、5 格范围奴仆。
- `97-103`：绿毒自爆、隐藏守护唤醒、出生特效显现、神兽显隐战斗、绿毒破盾瞬移、瞬移击退、远程植物显隐。
- `104-111`：麻痹目标点半径、扣蓝破盾远近、出生延迟远近切换、近身雷暴三雷、沉默毒近战、3 格感应自爆、固定上限召唤炮台、固定扇形远程 Boss。
- `112-118`：死亡特效队伍召唤、近身周期持续毒云、被动火焰线型炮台、麻痹牛魔王 Boss、受击闪现直线强化、受击闪现远程强化、受击闪现召唤红毒 Boss。
- `119-127`：大上限周期召唤、多范围技能 Boss、死亡献祭召唤、击杀归属队伍召唤、主人辅助随从、定时传送门、镜像换位 Boss/分身、伤害代理替身。
