# Legend of Mir 3 Zircon

Visit our forum at [LOMCN](http://www.lomcn.org/forum/forumdisplay.php?735) for discussions, help, and updates.

## 本地校验

提交文本配置改动前运行：

```powershell
.\Tools\LocalValidate.ps1
```

如果当前终端受 PowerShell 执行策略影响，也可以运行等价的 cmd 包装器：

```cmd
Tools\LocalValidate.cmd
```

默认数据库路径优先级为：命令参数、`MIR3_DATABASE_ROOT`、`ZIRCON_DATABASE_ROOT`、`D:\MirProject\Mir3\Debug\Server\Database\`。

生成机器可读摘要：

```powershell
.\Tools\LocalValidate.ps1 -SummaryFile .\generated\local-validate.json
```
