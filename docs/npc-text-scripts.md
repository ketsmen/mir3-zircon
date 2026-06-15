# Mir3 文本 NPC 脚本

服务端启动时会在 `Config.NpcTextOverrideEnabled = true` 时读取 `Config.NpcTextPath`，默认目录是 `.\Envir\NPCs\`。文本 NPC 只在服务端内存中生效，不会自动写回数据库，适合快速调试、临时覆盖和新 NPC 开发。

需要持久化时，使用数据库编辑器里的 NPC 文本工具把脚本导入数据库。导入后 NPC 会转换成旧数据库对象，关闭 `NpcTextOverrideEnabled` 或删除脚本目录后仍按原来的数据库 NPC 模式运行。

## NPCInfo.txt

每个有效行定义一个 NPC：

```text
MapFile X Y NPCName Image FaceImage ScriptFile [MapIcon=None] [Direction=Down] [MinLevel=0] [MaxLevel=0] [Class=All] [Days=All] [OverrideIndex=0]
```

示例：

```text
0 330 330 "测试商人" 1 0 example-merchant.txt MapIcon=Building Direction=Down Class=All Days=All
```

`OverrideIndex` 指向已有数据库 NPC 时，不新增坐标点，而是在运行时覆盖该 NPC 的脚本和展示信息。

## 脚本结构

```text
[@MAIN]
#SAY
你好，需要什么？
<购买/@BUY>
<修理/@REPAIR>
<离开/@EXIT>

[@BUY]
#SAY
这里可以买卖物品。
[TRADE]
"回城卷" 1.0
[TYPES]
Consumable Scroll Weapon Armour
```

支持 `#INSERT [file]` 和 `#INCLUDE [file]`。解析器会检测循环引用。

## 条件

`#IF` 后支持：

```text
LEVEL >= 10
CHECKGOLD 1000
CHECKCURRENCY "声望" >= 10
CHECKITEM "回城卷" 1
CHECKCLASS Warrior
CHECKPKPOINT <= 200
RANDOM 10
CHECKNAMELIST DailyGift User
CHECKVALUE DailyCount >= 1 User
```

`#ELSESAY` 和 `#ELSEACT` 会在条件失败时执行。

`#SUCCESS @PAGE` 可以指定特殊窗口完成后的返回页，当前主要用于 `[@ROLLDIE]` 和 `[@ROLLYUT]`。

`#DIALOGTYPE Repair` 可以显式设置页面窗口类型，主要用于数据库 NPC 导出后保持原页面的 `NPCDialogType`。

## 动作

`#ACT` 和 `#ELSEACT` 支持：

```text
GOTO @MAIN
GIVEGOLD 1000
TAKEGOLD 1000
GIVECURRENCY "声望" 10
TAKECURRENCY "声望" 10
GIVEITEM "回城卷" 1
TAKEITEM "回城卷" 1
MOVE 0 330 330
ADDNAMELIST DailyGift User
DELNAMELIST DailyGift User
SETVALUE DailyCount 1 User
CHANGEVALUE DailyCount 1 User
LOCALMESSAGE 已完成操作。
```

严格模式 `NpcTextStrictMode = true` 时，未知命令、缺地图、缺物品、缺目标页、循环 include 会记录错误并跳过对应 NPC。

## 特殊页

以下页面名会映射到现有客户端窗口：

```text
[@BUY] [@SELL] [@BUYSELL] [@REPAIR] [@REFINE] [@REFINERETRIEVE]
[@COMPANIONMANAGE] [@WEDDINGRING] [@REFINEMENTSTONE] [@MASTERREFINE]
[@WEAPONRESET] [@ITEMFRAGMENT] [@ACCESSORYREFINEUPGRADE]
[@ACCESSORYREFINELEVEL] [@ACCESSORYRESET] [@WEAPONCRAFT]
[@ACCESSORYREFINE] [@ROLLDIE] [@ROLLYUT]
```

商品列表写在 `[TRADE]` 后，出售/修理类型写在 `[TYPES]` 后。货币默认使用金币，可用 `[CURRENCY]` 指定数据库中的货币名或 `CurrencyType`。脚本条件和动作也可以用 `CHECKCURRENCY/GIVECURRENCY/TAKECURRENCY` 直接操作数据库中的任意 `CurrencyInfo`。

## 数据库双向工具

数据库编辑器工具栏提供：

- `导出 NPC 文本`：把选中 NPC 或全部 NPC 导出为 `NPCInfo.txt` 和脚本文件。
- `验证文本 NPC`：只解析和检查脚本，不写数据库。
- `导入文本 NPC`：把文本脚本转换成 `NPCInfo/NPCPage/NPCButton/NPCCheck/NPCAction/NPCGood/NPCType`。

推荐工作流：

```text
导出 NPC 文本 -> 修改脚本 -> 验证文本 NPC -> 导入文本 NPC -> 保存数据库 -> 关闭文本读取或删除脚本目录验证旧模式
```

导入模式：

- 覆盖：清理匹配 NPC 的旧页面树，再导入新页面，推荐用于避免重复页面。
- 合并：保留旧页面树，导入新页面并把入口页切到新脚本。
- 只验证：不写数据库。

匹配目标 NPC 时优先使用 `OverrideIndex`，其次使用 `Map + X + Y + NPCName`，找不到则新建 `NPCInfo` 和单点 `MapRegion`。

导入数据库只接受能无损映射到旧模式的脚本。当前限制：

- `CHECKCLASS` 导入数据库只支持 `All/None` 或单职业；多职业组合仍可用于文本运行时，但不能无损导入旧 DB 条件。
- 带对话文本的 `GOTO` 不能导入数据库；无对话文本的 `GOTO` 会转换成旧 `SuccessPage`。
- `LOCALMESSAGE` 当前只支持文本运行时，导入数据库时会报映射错误。
- `CHECKCURRENCY/GIVECURRENCY/TAKECURRENCY` 可以无损导入和导出数据库 NPC。
- 无法导出的旧 DB 功能会写成 `; TODO` 注释，并出现在导出报告中。

验证和导入报告会尽量保留脚本来源行号；例如无法无损映射的条件或动作会显示 `脚本文件:行号`，方便直接回到文本脚本修正。

## 管理员导出命令

游戏内管理员命令：

```text
@EXPORTNPCS
```

该命令会导出到 `NpcTextPath` 目录，生成 `NPCInfo.txt` 和对应脚本。复杂条件和动作会用 `TODO` 注释保留，需要人工校对。
