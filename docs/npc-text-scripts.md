# Mir3 文本 NPC 脚本

服务端启动时会在 `Config.NpcTextOverrideEnabled = true` 时读取 `Config.NpcTextPath`，默认目录是 `.\Envir\NPCs\`。文本 NPC 只在服务端内存中生效，不会写回数据库。

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
CHECKITEM "回城卷" 1
CHECKCLASS Warrior
CHECKPKPOINT <= 200
RANDOM 10
CHECKNAMELIST DailyGift User
CHECKVALUE DailyCount >= 1 User
```

`#ELSESAY` 和 `#ELSEACT` 会在条件失败时执行。

`#SUCCESS @PAGE` 可以指定特殊窗口完成后的返回页，当前主要用于 `[@ROLLDIE]` 和 `[@ROLLYUT]`。

## 动作

`#ACT` 和 `#ELSEACT` 支持：

```text
GOTO @MAIN
GIVEGOLD 1000
TAKEGOLD 1000
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
[@MASTERREFINE] [@ITEMFRAGMENT] [@ROLLDIE] [@ROLLYUT]
```

商品列表写在 `[TRADE]` 后，出售/修理类型写在 `[TYPES]` 后。货币默认使用金币，可用 `[CURRENCY]` 指定数据库中的货币名或 `CurrencyType`。

## 导出现有 NPC

游戏内管理员命令：

```text
@EXPORTNPCS
```

该命令会导出到 `NpcTextPath` 目录，生成 `NPCInfo.export.txt` 和对应脚本。复杂条件和动作会用 `TODO` 注释保留，需要人工校对。
