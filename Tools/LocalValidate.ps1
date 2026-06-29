param(
    [string]$DatabaseRoot = "D:\MirProject\Mir3\Debug\Server\Database\",
    [string]$CustomRoot,
    [string]$NpcRoot,
    [string]$DropRoot,
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "Tools\DropExportRunner\DropExportRunner.csproj"

if ([string]::IsNullOrWhiteSpace($CustomRoot)) {
    $CustomRoot = Join-Path $repoRoot "Envir\Custom"
}

if ([string]::IsNullOrWhiteSpace($NpcRoot)) {
    $NpcRoot = Join-Path $repoRoot "Envir\NPCs"
}

if ([string]::IsNullOrWhiteSpace($DropRoot)) {
    $DropRoot = Join-Path $repoRoot "Envir\Drops"
}

& dotnet run --project $project -c $Configuration -- local-validate $DatabaseRoot $CustomRoot $NpcRoot $DropRoot
exit $LASTEXITCODE
