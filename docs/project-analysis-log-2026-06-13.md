# 项目详细分析日志（2026-06-13）

## 1. 基本快照

- 项目名称：Legend of Mir 3 Zircon
- 仓库路径：`D:\GitHub\mir3-zircon`
- 解决方案：`Zircon Server.sln`
- 技术栈主线：C#、.NET 10、Windows Forms、SharpDX、Silk.NET Vulkan、DevExpress、Dapper、SQL Server
- 当前文档目录已有内容：
  - `docs/multilanguage-migration-plan.md`：多语言迁移计划
  - `docs/vortice-migration.md`：Vortice 迁移说明

本日志记录一次面向项目维护、架构梳理和后续改造排期的整体分析。它不是发布说明，而是方便后续开发者快速进入项目上下文的工作日志。

## 2. 解决方案组成

解决方案当前包含以下主要工程：

- `Client`：客户端主程序，输出 `Zircon`，目标框架为 `net10.0-windows8.0`，使用 Windows Forms、自研 DX UI、SharpDX、Silk.NET Vulkan、NAudio、Sentry。
- `Server`：服务端管理 UI，目标框架为 `net10.0-windows8.0`，依赖 `LibraryCore`、`PluginCore`、`ServerLibrary`，使用 DevExpress、Dapper、SQL Server、SharpDX。
- `ServerCore`：服务端控制台/核心入口，目标框架为 `net10.0`，依赖 `LibraryCore` 和 `ServerLibrary`。
- `ServerLibrary`：服务端领域逻辑与数据模型库，目标框架为 `net10.0`，依赖 `LibraryCore`。
- `LibraryCore`：共享基础库，目标框架为 `net10.0`，承担枚举、配置、网络协议、共享模型等核心职责。
- `Launcher`、`Patcher`、`PatchManager`：启动器、补丁客户端和补丁管理工具，整体基于 Windows Forms/DevExpress。
- `ImageManager`、`LibraryEditor`：资源、图库和美术资产相关工具。
- `PluginCore`、`PluginStandalone`：插件基础能力与独立插件宿主。

从项目引用看，`LibraryCore` 是横向共享底座；`Client` 直接依赖 `LibraryCore`；`Server` 同时依赖 `LibraryCore`、`PluginCore`、`ServerLibrary`。服务端核心逻辑更多集中在 `ServerLibrary`，管理界面则集中在 `Server`。

## 3. 客户端分析

`Client` 是当前最复杂、变化风险最高的工程之一。它同时承担游戏窗口、渲染管线、音频、网络、UI、场景、模型绘制和本地化加载。

主要观察：

- 目标框架为 `net10.0-windows8.0`，说明客户端仍明确绑定 Windows 桌面环境。
- UI 基于项目自研 `DXControl` 系列控件，而不是标准 WinForms 控件树。配置界面、弹窗、场景窗口等都通过自绘控件完成。
- 渲染层已从单一 SharpDX 路线扩展到多渲染管线：`DirectX 9`、`DirectX 11`、`OpenGL`、`Vulkan`。当前工程内能看到 `Client/Rendering` 下的统一管线管理和各后端实现。
- 近期渲染工作重点包括 Vulkan、显示器选择、全屏/无边框、多显示器居中和分辨率处理。
- 音频依赖 `NAudio`、`NAudio.Vorbis`、`NVorbis`，客户端内同时保留 SharpDX DirectSound 相关依赖。
- 本地化通过 `Client/Envir/Translations/StringMessages.cs` 抽象语言键，再由 `EnglishMessages.cs` 和 `ChineseMessages.cs` 提供默认值。运行时可能还会从 `Translations/*.ini` 加载覆盖值。

客户端风险点：

- 渲染管线数量增加后，配置项、窗口状态、显示器状态、分辨率状态需要保持跨后端一致。相关改动应尽量通过 `RenderingPipelineManager` 的统一接口完成。
- 自绘 UI 的文字宽度、中文长度、控件布局高度容易互相影响；补翻译时不只要看资源，还要检查实际控件尺寸。
- `SharpDX.Desktop 4.2.0` 在当前 `net10.0-windows8.0` 下构建会出现兼容性警告，短期可接受，但长期迁移成本需要单独评估。
- Vulkan 相关代码中存在大量 Silk.NET 过时 API 警告，暂不影响构建，但后续升级 Silk.NET 时可能集中爆发。

## 4. 服务端分析

服务端分成 UI 管理端和核心逻辑库两层：

- `Server`：面向管理员的 Windows Forms/DevExpress 管理界面，包含大量 `Views/*View.cs` 与 `.resx`。
- `ServerLibrary`：领域模型、服务端环境、角色、怪物、技能、地图、掉落、任务等核心逻辑。
- `ServerCore`：更轻量的服务端入口，适合承载无 UI 或后台启动能力。

主要观察：

- 数据访问使用 `Dapper` 和 `System.Data.SqlClient`。
- DevExpress 版本已更新到 `25.2.6`，相关 UI 代码对 DevExpress 类型依赖较重。
- 服务端和客户端共享 `LibraryCore`，协议、枚举、配置模型变更需要两端同步验证。
- `ServerLibrary` 的中文翻译资源与客户端分离，存在独立的 `ServerLibrary/Envir/Translations`。

服务端风险点：

- 管理 UI 与业务逻辑边界需要继续保持清晰，避免在 `Server` 视图层写入太多领域规则。
- 数据库模型、网络协议和客户端显示字段往往互相耦合，修改共享枚举或包结构时要做端到端验证。
- DevExpress 设计器生成代码容易产生大面积格式或资源变更，提交时需要控制差异范围。

## 5. 共享库与协议层

`LibraryCore` 是项目的核心耦合点。它承担：

- 配置读取与写入
- 网络包结构
- 枚举和共享常量
- 共享模型、状态、属性定义

这类代码的特点是影响面大。即使是小字段、小枚举、小默认值改动，也可能同时影响客户端 UI、服务端逻辑、数据库序列化和插件兼容性。

维护建议：

- 修改共享协议前先搜索客户端和服务端所有引用。
- 协议新增字段优先保持向后兼容，尤其是网络包、存档、配置和数据库映射。
- 对共享枚举添加值时，检查 UI 显示、序列化默认值和历史数据加载路径。

## 6. 工具链分析

项目附带多套工具，说明资产流水线是项目维护的重要部分：

- `LibraryEditor`：图库/资源编辑器。
- `ImageManager`：图片或资源管理工具。
- `Patcher`：补丁客户端。
- `PatchManager`：补丁发布或管理工具，依赖 `WinSCP`。
- `Launcher`：启动器。

这些工具大多仍是 Windows 桌面程序，并依赖 DevExpress 或 WinForms。它们对最终游戏体验不是运行时主链路，但对发布、资源制作和运维很关键。

维护建议：

- 工具类工程修改后至少做构建验证，避免发布链路到最后才发现工具不可用。
- 资源格式相关改动应同时验证客户端读取、编辑器保存、补丁分发三条路径。
- 启动器和补丁器的配置路径、输出路径、版本号规则建议单独成文。

## 7. 本地化现状

项目已经有面向多语言的结构：

- 客户端：`Client/Envir/Translations`
- 服务端：`ServerLibrary/Envir/Translations`
- 文档：`docs/multilanguage-migration-plan.md`

当前本地化不是传统 `.resx` 多语言资源方案，而是通过 `StringMessages` 抽象类和派生默认值实现，再由配置系统落地到 ini 文件。这种方案利于游戏内动态配置，但也带来几个维护点：

- 新增语言键必须同步修改抽象基类和所有派生语言类。
- 中文默认值如果仍保留英文，编译不会失败，需要靠扫描和人工检查发现。
- UI 文案不能只检查 `ChineseMessages.cs`，还要查控件代码中的硬编码字符串。
- 运行目录中如果已经生成 `Translations/*.ini`，默认值修改未必立即覆盖用户本地配置，需要确认配置加载策略。

建议后续建立一个轻量检查脚本：

- 对比 `EnglishMessages.cs` 和 `ChineseMessages.cs` 的属性集合。
- 扫描中文资源中明显的英文占位值。
- 扫描客户端 UI 目录中直接赋给 `Text`、`Label.Text`、`Hint` 的英文硬编码。

## 8. 构建与输出路径

多个工程关闭了默认的 `TargetFramework` 和 `RuntimeIdentifier` 输出路径追加，并自定义输出目录：

- 客户端 Debug 输出到 `..\..\Debug\Client\`
- 服务端 Debug 输出到 `..\..\Debug\Server\`
- 部分 Release 输出到 `..\..\Release\...`

这对传统 SDK 风格项目来说比较特殊。优点是更贴近游戏项目发布目录；缺点是构建产物可能落在仓库外层目录，清理和定位问题时需要额外留意。

构建注意事项：

- 改客户端时优先验证：`dotnet build Client\Client.csproj --no-restore`
- 改共享库时至少验证客户端和服务端核心链路。
- 改解决方案级依赖或工程文件时再运行完整 solution 构建。

## 9. 当前工作树注意事项

创建本日志时，工作树已存在客户端本地化相关改动：

- `Client/Controls/DXConfigWindow.cs`
- `Client/Envir/Translations/ChineseMessages.cs`
- `Client/Envir/Translations/EnglishMessages.cs`
- `Client/Envir/Translations/StringMessages.cs`
- `Client/Rendering/DisplayMonitorInfo.cs`

这些改动与本日志文件是不同任务面向。后续提交时建议按主题拆分：

- 一次提交处理客户端图形配置中文本地化。
- 另一次提交添加项目分析日志。

## 10. 优先级建议

近期优先级建议如下：

1. 本地化补漏：继续围绕客户端配置页、帮助页、市场、组队、目标轮廓等玩家高频 UI 扫描英文残留。
2. 渲染稳定性：针对全屏、无边框、多显示器、VSync、渲染管线切换建立手动测试清单。
3. 构建警告治理：记录 SharpDX 兼容性警告和 Silk.NET Vulkan 过时 API 警告，区分“可接受噪音”和“需要升级”的警告。
4. 工具链文档：补充启动器、补丁器、资源编辑器的使用说明和发布流程。
5. 协议与共享模型测试：为 `LibraryCore` 中高影响区域建立更明确的回归验证方式。

## 11. 后续检查清单

- [ ] 完整构建 `Zircon Server.sln`
- [ ] 启动客户端并人工检查图形配置页中文显示
- [ ] 启动服务端管理 UI，确认 DevExpress 视图仍正常加载
- [ ] 检查 `Translations/*.ini` 生成和覆盖策略
- [ ] 梳理补丁发布目录结构
- [ ] 建立英文残留扫描脚本
- [ ] 给渲染管线切换补一份手动测试矩阵

## 12. 结论

该项目是典型的老牌 Windows 游戏服务端/客户端一体化仓库，核心价值集中在共享协议、客户端渲染、自绘 UI、服务端领域逻辑和资产工具链。当前代码已经迁移到 .NET 10，并在渲染侧引入了 Vulkan 等新后端，说明项目处在“现代化迁移与历史技术债并存”的阶段。

后续维护时，应优先控制改动范围，围绕高影响共享层和渲染层建立验证清单；文案、本地化和配置类改动虽然看似轻量，但在自绘 UI 和 ini 覆盖机制下也需要做实际界面检查。
