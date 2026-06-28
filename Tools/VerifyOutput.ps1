param(
    [string]$OutputRoot = "D:\MirProject\Mir3",
    [string[]]$Configurations = @("Debug", "Release")
)

$ErrorActionPreference = "Stop"
$issues = New-Object System.Collections.Generic.List[string]

foreach ($configuration in $Configurations) {
    $root = Join-Path $OutputRoot $configuration
    if (!(Test-Path -LiteralPath $root)) {
        continue
    }

    $legacyRenderingDirs = Get-ChildItem -LiteralPath $root -Recurse -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match 'Rendering\\SharpDXD3D(9|11)$' }
    foreach ($dir in $legacyRenderingDirs) {
        $issues.Add("Legacy rendering directory: $($dir.FullName)")
    }

    $legacyRenderingDlls = Get-ChildItem -LiteralPath $root -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Name -in @(
                "SharpDX.D3DCompiler.dll",
                "SharpDX.Desktop.dll",
                "SharpDX.Direct2D1.dll",
                "SharpDX.Direct3D11.dll",
                "SharpDX.Direct3D9.dll",
                "SharpDX.DXGI.dll",
                "SharpDX.Mathematics.dll"
            )
        }
    foreach ($file in $legacyRenderingDlls) {
        $issues.Add("Legacy rendering DLL: $($file.FullName)")
    }

    $serverRoot = Join-Path $root "Server"
    if (Test-Path -LiteralPath $serverRoot) {
        $serverRenderingFiles = Get-ChildItem -LiteralPath $serverRoot -Recurse -File -ErrorAction SilentlyContinue |
            Where-Object {
                $_.Name -match '^(RenderingCore|Silk\.NET|BCnEncoder|CommunityToolkit|SharpDX)' -or
                $_.Name -like '*shaderc*'
            }
        foreach ($file in $serverRenderingFiles) {
            $issues.Add("Server rendering dependency: $($file.FullName)")
        }

        $serverRenderingDirs = Get-ChildItem -LiteralPath $serverRoot -Recurse -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match 'Server\\Rendering\\Silk' }
        foreach ($dir in $serverRenderingDirs) {
            $issues.Add("Server rendering directory: $($dir.FullName)")
        }
    }

    $nonWinX64Shaderc = Get-ChildItem -LiteralPath $root -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object {
            ($_.FullName -match 'runtimes\\linux-.*\\native\\libshaderc_shared\.so$') -or
            ($_.FullName -match 'runtimes\\osx-.*\\native\\libshaderc_shared\.dylib$') -or
            ($_.FullName -match 'runtimes\\win-arm64\\native\\shaderc_shared\.dll$') -or
            ($_.FullName -match 'runtimes\\win-x86\\native\\shaderc_shared\.dll$')
        }
    foreach ($file in $nonWinX64Shaderc) {
        $issues.Add("Non-win-x64 shaderc native file: $($file.FullName)")
    }
}

if ($issues.Count -gt 0) {
    $issues | Sort-Object | ForEach-Object { Write-Output "ERROR: $_" }
    exit 1
}

Write-Output "Output verification passed."
