# Mir2 怪物掉落迁移说明

## 迁移方式

Mir2 怪物掉落使用 `Envir\Drops` 下的文本文件，运行时解析后由怪物死亡逻辑执行。Mir3-Zircon 迁移后支持两种方式：

- 启动覆盖模式：服务端先读取数据库掉落，再读取服务端运行目录的 `.\Envir\Drops`，同名怪物 txt 会覆盖数据库掉落的内存结果，然后构建运行期掉落缓存。这个模式默认开启，不会保存回数据库。
- 手动导入模式：通过数据库编辑器的“导入 Mir2 掉落”按钮一次性导入文本，保存后写入数据库。

无论哪种方式，怪物死亡时只读取 `SEnvir.MonsterDropTable` 预编译缓存，不读取文本文件。

## 支持的 Mir2 语法

- `1/N ItemName`：导入为普通 `DropInfo`。
- `1/N Gold Amount` 或 `1/N 金币掉落物品名 Amount=数量`：导入为金币掉落，`Mir2Gold = true`，运行时按 Mir2 的 `Amount / 2` 到 `Amount + Amount / 2` 范围生成金币。
- `Q`：识别为任务专属行，不导入普通掉落，导入报告会列出，后续应迁移到 Mir3 任务系统。
- `#INSERT [file]`：导入或启动覆盖阶段在当前位置展开；运行时不保留 include。
- `GROUP`、`GROUP*`、`GROUP^`：导入为 `DropGroupInfo`，分别对应全部命中、命中后随机取一个物品候选、首个命中即停止。`GROUP*` 中命中的金币会按 Mir2 原逻辑直接累计，随机只作用于物品候选。
- 嵌套 `GROUP`：导入为 `DropGroupInfo.Parent` 关联。
- 注释：支持整行 `;` 注释，也支持未加引号区域内的行尾 `;` 或 `//` 注释。

## 字段映射

- Mir2 文件名匹配 Mir3 `MonsterInfo.MonsterName`，匹配时会忽略空格并忽略大小写。
- 数据库中存在同名怪物时，导出器会把后续同名文件写成 `怪物名#MonsterInfoIndex.txt`；导入和本地校验会按 Index 定位对应怪物。
- 如果怪物使用了 Mir2 的 `DropPath`，可在掉落目录放置 `DropPathMap.txt`，每行使用 `MonsterName=DropPath` 或 `MonsterName,DropPath`；支持行尾 `;`/`//` 注释，但映射路径必须留在掉落目录内。
- Mir2 物品名匹配 Mir3 `ItemInfo.ItemName`，匹配时会忽略空格并忽略大小写。
- Mir2 `1/N` 的 `N` 写入 `Chance`。
- Mir2 金币行使用 `1/N Gold 数量`，数量写入 `Amount`，并设置 `Mir2Gold`；导入器也兼容旧导出格式 `Gold Amount=数量`，以及本地化金币掉落物品名格式如 `金币 Amount=数量`。如确实需要固定数量金币物品，可显式追加 `Mir2Gold=False`。
- Mir2 `Q` 不写入普通掉落表，避免任务道具变成世界掉落。

## 性能设计

服务器启动时会构建 `SEnvir.MonsterDropTable`，把普通掉落和组掉落编译为数组。启动覆盖模式只在启动阶段解析 `.\Envir\Drops`，怪物死亡时不进行文件 IO、正则解析、字符串查找或数据库全表扫描。如果某个怪物文本不可读、解析出错、`#INSERT` 缺失，或引用了缺失物品，会跳过该怪物的文本覆盖并保留数据库掉落，避免用空/半成品结果遮掉原掉落。

`AccountInfo` 会维护 `UserDropCache`，掉落保底进度按 `ItemInfo` 字典查询，避免每次掉落时线性扫描 `UserDrops`。

## 启动覆盖模式

1. 将掉落文本放到服务端 exe 目录下的 `.\Envir\Drops`。
2. 每只怪物一个 txt，文件名默认匹配 `MonsterInfo.MonsterName`。
3. 如需一个文件服务多个怪物，在目录中添加 `DropPathMap.txt`。
4. 启动服务端后查看日志中的 `[DropTextOverride]` 统计和缺失项。
5. 文本覆盖只影响本次运行内存，不会修改数据库；删除 txt 或关闭配置后会回到数据库掉落。

相关配置在 `Server.ini` 的配置项中：

- `DropTextOverrideEnabled`：是否启用启动文本覆盖，默认 `true`。
- `DropTextOverridePath`：文本目录，默认 `.\Envir\Drops\`。

## 手动导入数据库

1. 打开服务端数据库编辑器。
2. 点击“导入 Mir2 掉落”。
3. 选择 Mir2 服务端运行目录中的 `Envir\Drops` 文件夹。
4. 推荐选择清空目标怪物现有掉落后重新导入，避免重复掉落。
5. 查看导入报告，重点处理缺失物品、缺失怪物、任务专属行和失败行。选择清空导入时，如果某个怪物文件存在解析错误或缺失物品，该怪物会跳过覆盖并保留原掉落。
6. 保存数据库。

本仓库已包含一份 `Envir\Drops` 文本数据；部署时需要将它放到服务端 exe 目录下，或通过 `DropTextOverridePath` 指向实际目录。

## 本地一键校验

本地开发时可在仓库根目录执行：

```powershell
.\Tools\LocalValidate.ps1
```

该脚本会使用本地调试数据库，统一校验 `Envir\Custom`、`Envir\NPCs` 和 `Envir\Drops`；任一项失败会返回非零退出码。
