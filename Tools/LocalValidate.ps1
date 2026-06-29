param(
    [string]$DatabaseRoot,
    [string]$CustomRoot,
    [string]$NpcRoot,
    [string]$DropRoot,
    [string]$Configuration = "Debug",
    [switch]$Json,
    [string]$SummaryFile
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "Tools\DropExportRunner\DropExportRunner.csproj"

if ([string]::IsNullOrWhiteSpace($DatabaseRoot)) {
    $DatabaseRoot = $env:MIR3_DATABASE_ROOT
}

if ([string]::IsNullOrWhiteSpace($DatabaseRoot)) {
    $DatabaseRoot = $env:ZIRCON_DATABASE_ROOT
}

if ([string]::IsNullOrWhiteSpace($DatabaseRoot)) {
    $DatabaseRoot = "D:\MirProject\Mir3\Debug\Server\Database\"
}

if ([string]::IsNullOrWhiteSpace($CustomRoot)) {
    $CustomRoot = Join-Path $repoRoot "Envir\Custom"
}

if ([string]::IsNullOrWhiteSpace($NpcRoot)) {
    $NpcRoot = Join-Path $repoRoot "Envir\NPCs"
}

if ([string]::IsNullOrWhiteSpace($DropRoot)) {
    $DropRoot = Join-Path $repoRoot "Envir\Drops"
}

$runnerArgs = @("local-validate", $DatabaseRoot, $CustomRoot, $NpcRoot, $DropRoot)

if ($Json) {
    $runnerArgs += "--json"
}

if (![string]::IsNullOrWhiteSpace($SummaryFile)) {
    $runnerArgs += "--summary-file"
    $runnerArgs += $SummaryFile
}

& dotnet run --project $project -c $Configuration -- @runnerArgs
exit $LASTEXITCODE
