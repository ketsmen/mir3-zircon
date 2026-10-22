# Legend of Mir 3 Zircon

Visit our forum at [LOMCN](http://www.lomcn.org/forum/forumdisplay.php?735) for discussions, help, and updates.

## 本地校验

运行完整本地检查：

```powershell
.\Tools\Check.ps1
```

完整检查会运行协议/基础设施测试、协议序列化性能烟测、本地文本配置校验和输出目录校验；缺少数据库或输出目录时，对应步骤默认跳过。

如果当前终端受 PowerShell 执行策略影响，也可以运行等价的 cmd 包装器：

```cmd
Tools\Check.cmd
```

提交文本配置改动前运行：

```powershell
.\Tools\LocalValidate.ps1
```

如果当前终端受 PowerShell 执行策略影响，也可以运行等价的 cmd 包装器：

```cmd
Tools\LocalValidate.cmd
```

默认数据库路径优先级为：命令参数、`MIR3_DATABASE_ROOT`、`ZIRCON_DATABASE_ROOT`、`D:\MirProject\Mir3\Debug\Server\Database\`。

构建输出根目录默认是 `D:\MirProject\Mir3\`，可通过 MSBuild 属性 `Mir3OutputRoot` 或环境变量 `MIR3_OUTPUT_ROOT` 覆盖。

NuGet 包版本集中在 `Directory.Packages.props` 管理，项目文件只保留包引用。

协议包类型注册由 `Tools\PacketSourceGenerator` 在编译期生成；运行时仍保留反射回退路径。

单独运行协议序列化性能烟测：

```powershell
dotnet run --project .\Tools\PacketBench\PacketBench.csproj -c Debug -- --iterations 5000
```

服务端启动时会执行配置健康检查，阻止无效端口、无效 IP、错误 Web 前缀等高风险配置继续启动。

导出服务端配置 schema：

```powershell
dotnet run --project .\Tools\ConfigSchema\ConfigSchema.csproj -c Debug -- --output .\generated\server-config.schema.json
```

服务端日志会同时写入传统文本日志和 `Logs.jsonl` 结构化日志，并按大小滚动、按保留天数清理历史日志。

生成机器可读摘要：

```powershell
.\Tools\LocalValidate.ps1 -SummaryFile .\generated\local-validate.json
```
