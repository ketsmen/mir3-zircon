# ServerCapabilities 协议说明

`ServerCapabilities` 用来给新旧客户端、服务端之间的协议字段做能力协商。反射序列化会按属性顺序读写字段，所以在已有包中新增字段时，必须明确它是否会影响旧端布局。

## 握手

- `G.Connected.Capabilities` 是尾部可选字段。
- 服务端先发送自己的 `ServerCapabilities.Current`。
- 新客户端回传自己的 `ServerCapabilities.Current`。
- 旧客户端或旧服务端没有这个尾字段时，能力值保持 `None`。

`StartInformation.ServerCapabilities` 只在对端已经通过握手声明能力协议时读写；否则跳过该字段，避免旧布局中后续字段错位。

## 当前能力位

| 能力位 | 方向 | 影响 |
| --- | --- | --- |
| `HeroItemSyncIndex` | 服务端到客户端 | 英雄物品同步包携带 `HeroIndex`，客户端能把变更应用到正确英雄。 |
| `HeroItemRequestIndex` | 客户端到服务端 | 英雄物品请求包携带 `HeroIndex`，服务端能知道请求目标英雄。 |
| `HeroMultiInspectCache` | 服务端到客户端 | `S.HeroInformation.Inspect` 可标识非当前英雄详情，客户端可缓存多个已查看英雄。 |

`Stable` 只应包含稳定、可对外协商的能力位。后续灰度能力先放进 `Experimental`，确认旧端/旧服回退路径后再并入 `Stable`；`Current` 由两者组合而成。

## 诊断入口

- `Config.CapabilityAuditLog=true` 时，服务端会在握手、登录进入选角、进入游戏阶段写入 `[Capabilities]` 审计日志。
- GM 可用 `@CAPABILITIES [角色名]` 查看目标连接能力摘要。
- 服务器诊断窗口“连接能力”页会列出在线连接能力位；“只看旧端/缺能力”可过滤混端连接，旧端/未知和缺能力连接会高亮显示。
- 客户端开启调试标签时，调试信息会显示当前连接协商到的 `ServerCapabilities`。

## 英雄物品索引

`HeroItemSyncIndex` 覆盖这些服务端包中的 `HeroIndex`：

- `S.ItemMove`
- `S.ItemSort`
- `S.ItemSplit`
- `S.ItemLock`
- `S.ItemChanged`
- `S.ItemsChanged`
- `S.NPCRepair`
- `S.ItemStatsChanged`
- `S.ItemStatsRefreshed`
- `S.ItemDurability`
- `S.ItemExperience`
- `S.ItemAcessoryRefined`

`HeroItemRequestIndex` 覆盖这些客户端包中的 `HeroIndex`：

- `C.ItemMove`
- `C.ItemSort`
- `C.ItemSplit`
- `C.ItemUse`
- `C.ItemLock`
- `C.NPCRepair`

缺失 `HeroIndex` 时按 `0` 处理，业务层回退为当前英雄。旧端只能安全操作当前英雄，不能表达非当前英雄目标。

## 多英雄查看缓存

开启 `HeroMultiInspectCache` 时，服务端可以给非当前英雄发送 `S.HeroInformation { Inspect = true }`，客户端把详情存入查看缓存。

未开启时：

- 客户端不请求非当前英雄详情，只展示英雄列表摘要。
- 服务端不向该客户端发送非当前英雄详情。
- 客户端收到未带 `Inspect` 的非当前英雄详情时会丢弃，避免覆盖当前英雄。

## 新增字段规则

- 只有尾部字段才可以使用 `[OptionalPacketProperty]`。
- 插入到已有字段中间、或后面还有字段的新增属性，必须用 `[PacketCapability]` 裁剪读写。
- 对已经稳定存在、但名字也叫 `HeroIndex` 的旧字段，可以显式使用 `[PacketCapability(ServerCapabilities.None)]` 表示该字段始终读写，不参与裁剪。
- 嵌套对象中的新增字段也要遵守同样规则；如果会影响外层后续字段，不能只靠 optional。
- 新能力位需要同时说明新端新服行为、旧端新服行为、新端旧服行为。

## ProtocolVersion 与 ServerCapabilities 边界

`ProtocolVersion` 仍然负责“结构不可兼容”的硬切换；`ServerCapabilities` 负责“结构可裁剪、行为可降级”的软协商。

必须提升 `ProtocolVersion` 的情况：

- 包 ID 排序会变化，例如新增、删除、重命名 `Packet` 类型，导致旧二进制无法定位同一个包。
- 已有字段被删除、改名、改类型，或字段顺序变化，无法通过读写裁剪保持旧布局。
- 新语义没有安全默认值，旧端或旧服继续处理会产生错误状态、复制物品、丢物品等不可接受结果。
- 客户端资源、数据库结构或启动流程强依赖新版，旧端即使收包成功也无法正确运行。

适合只加 `ServerCapabilities` 的情况：

- 新字段可以在对端缺能力时完全跳过，旧布局后续字段仍能对齐。
- 新字段是尾字段，或使用 `[PacketCapability]` 同时裁剪读写，缺失时业务层有明确默认值。
- 新行为可以退化成旧行为，例如英雄物品请求缺 `HeroIndex` 时只操作当前英雄。
- 服务端能按客户端能力决定是否发送某类包、某个字段或某段行为。

新增协议点检查清单：

- 新字段是否影响同一个包后续字段？影响则必须用 `[PacketCapability]`，不能只用 `[OptionalPacketProperty]`。
- 能力缺失时默认值是什么？是否已经在客户端和服务端业务分支里处理？
- 是否需要新增固定旧包字节样本，防止反射属性顺序变化？
- 是否需要更新 `@CAPABILITIES`、诊断窗体和回归清单？
- 如果 `CheckVersion=true`，旧二进制仍会先被版本校验挡住；能力协商主要覆盖允许混端/灰度时的兼容窗口。
