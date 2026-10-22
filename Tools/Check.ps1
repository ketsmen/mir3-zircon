param(
    [string]$Configuration = "Debug",
    [string]$DatabaseRoot,
    [string]$OutputRoot = $env:MIR3_OUTPUT_ROOT,
    [switch]$NoRestore,
    [switch]$SkipLocalValidate,
    [switch]$RequireLocalValidate,
    [switch]$SkipOutputVerify,
    [switch]$RequireOutputVerify,
    [switch]$SkipBench,
    [switch]$SkipConfigSchema
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$protocolTests = Join-Path $repoRoot "Tests\ProtocolTests\ProtocolTests.csproj"
$packetBench = Join-Path $repoRoot "Tools\PacketBench\PacketBench.csproj"
$configSchema = Join-Path $repoRoot "Tools\ConfigSchema\ConfigSchema.csproj"
$localValidate = Join-Path $PSScriptRoot "LocalValidate.ps1"
$outputVerify = Join-Path $PSScriptRoot "VerifyOutput.ps1"

function Resolve-DatabaseRoot {
    param([string]$RequestedRoot)

    if (![string]::IsNullOrWhiteSpace($RequestedRoot)) {
        return $RequestedRoot
    }

    if (![string]::IsNullOrWhiteSpace($env:MIR3_DATABASE_ROOT)) {
        return $env:MIR3_DATABASE_ROOT
    }

    if (![string]::IsNullOrWhiteSpace($env:ZIRCON_DATABASE_ROOT)) {
        return $env:ZIRCON_DATABASE_ROOT
    }

    return "D:\MirProject\Mir3\Debug\Server\Database\"
}

function Resolve-OutputRoot {
    param([string]$RequestedRoot)

    if (![string]::IsNullOrWhiteSpace($RequestedRoot)) {
        return $RequestedRoot
    }

    return "D:\MirProject\Mir3"
}

function Invoke-Step {
    param(
        [string]$Name,
        [scriptblock]$Action
    )

    Write-Host ""
    Write-Host "==> $Name"
    & $Action
}

Invoke-Step "协议测试" {
    $testArgs = @("test", $protocolTests, "-c", $Configuration, "-v", "minimal")
    if ($NoRestore) {
        $testArgs += "--no-restore"
    }

    & dotnet @testArgs
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

if (!$SkipBench) {
    Invoke-Step "协议序列化性能烟测" {
        $benchArgs = @("run", "--project", $packetBench, "-c", $Configuration)
        if ($NoRestore) {
            $benchArgs += "--no-restore"
        }
        $benchArgs += "--"
        $benchArgs += "--iterations"
        $benchArgs += "5000"

        & dotnet @benchArgs
        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }
    }
}

if (!$SkipConfigSchema) {
    Invoke-Step "配置 Schema 生成烟测" {
        $schemaPath = Join-Path $repoRoot "generated\server-config.schema.json"
        $schemaArgs = @("run", "--project", $configSchema, "-c", $Configuration)
        if ($NoRestore) {
            $schemaArgs += "--no-restore"
        }
        $schemaArgs += "--"
        $schemaArgs += "--output"
        $schemaArgs += $schemaPath

        & dotnet @schemaArgs
        $exitCode = $LASTEXITCODE
        if (Test-Path -LiteralPath $schemaPath) {
            Remove-Item -LiteralPath $schemaPath
        }
        $schemaDirectory = Split-Path -Parent $schemaPath
        if ((Test-Path -LiteralPath $schemaDirectory) -and !(Get-ChildItem -LiteralPath $schemaDirectory -Force)) {
            Remove-Item -LiteralPath $schemaDirectory
        }
        if ($exitCode -ne 0) {
            exit $exitCode
        }
    }
}

if (!$SkipLocalValidate) {
    $resolvedDatabaseRoot = Resolve-DatabaseRoot $DatabaseRoot
    $databaseFile = Join-Path $resolvedDatabaseRoot "System.db"

    if ((Test-Path -LiteralPath $databaseFile) -or $RequireLocalValidate) {
        Invoke-Step "本地文本配置校验" {
            & $localValidate -DatabaseRoot $resolvedDatabaseRoot -Configuration $Configuration
            if ($LASTEXITCODE -ne 0) {
                exit $LASTEXITCODE
            }
        }
    }
    else {
        Write-Warning "跳过本地文本配置校验，未找到数据库: $databaseFile"
    }
}

if (!$SkipOutputVerify) {
    $resolvedOutputRoot = Resolve-OutputRoot $OutputRoot

    if ((Test-Path -LiteralPath $resolvedOutputRoot) -or $RequireOutputVerify) {
        Invoke-Step "输出目录校验" {
            & $outputVerify -OutputRoot $resolvedOutputRoot -Configurations @($Configuration)
            if ($LASTEXITCODE -ne 0) {
                exit $LASTEXITCODE
            }
        }
    }
    else {
        Write-Warning "跳过输出目录校验，未找到目录: $resolvedOutputRoot"
    }
}

Write-Host ""
Write-Host "所有可运行校验已通过。"
