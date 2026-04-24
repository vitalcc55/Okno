$ErrorActionPreference = 'Stop'
if (Get-Variable -Name PSStyle -ErrorAction Ignore) {
    $PSStyle.OutputRendering = 'PlainText'
}

$env:COMPUTER_USE_WIN_PLUGIN_ROOT = $PSScriptRoot

Set-Location $PSScriptRoot

$runtimeRoot = Join-Path $PSScriptRoot 'runtime\win-x64'
$serverExePath = Join-Path $runtimeRoot 'Okno.Server.exe'
$runtimeManifestPath = Join-Path $runtimeRoot 'okno-runtime-bundle-manifest.json'

function Assert-RuntimeBundleMatchesManifest {
    param(
        [Parameter(Mandatory)]
        [string] $RootPath,
        [Parameter(Mandatory)]
        [string] $ManifestPath
    )

    if (-not (Test-Path $ManifestPath -PathType Leaf)) {
        throw "Runtime bundle manifest is missing: $ManifestPath"
    }

    $manifest = Get-Content -Path $ManifestPath -Raw | ConvertFrom-Json
    if ($manifest.formatVersion -ne 1) {
        throw "Unsupported runtime bundle manifest version '$($manifest.formatVersion)'."
    }

    $expectedMap = New-Object 'System.Collections.Generic.Dictionary[string,long]' ([System.StringComparer]::Ordinal)
    foreach ($entry in @($manifest.files)) {
        $expectedMap[[string]$entry.path] = [int64]$entry.size
    }

    $normalizedRootPath = [System.IO.Path]::GetFullPath($RootPath).TrimEnd('\')
    $actualFiles = Get-ChildItem -LiteralPath $RootPath -Recurse -File |
        Where-Object { -not [string]::Equals($_.FullName, [System.IO.Path]::GetFullPath($ManifestPath), [System.StringComparison]::OrdinalIgnoreCase) } |
        Sort-Object FullName

    foreach ($file in $actualFiles) {
        $fullPath = [System.IO.Path]::GetFullPath($file.FullName)
        $relativePath = $fullPath.Substring($normalizedRootPath.Length).TrimStart('\')
        if (-not $expectedMap.ContainsKey($relativePath)) {
            throw "Plugin-local runtime bundle contains unexpected file '$relativePath'."
        }

        if ([int64]$file.Length -ne $expectedMap[$relativePath]) {
            throw "Plugin-local runtime bundle contains size drift for '$relativePath'."
        }

        $null = $expectedMap.Remove($relativePath)
    }

    if ($expectedMap.Count -gt 0) {
        throw "Plugin-local runtime bundle is incomplete. Missing: $($expectedMap.Keys -join ', ')."
    }
}

if (-not (Test-Path $serverExePath -PathType Leaf)) {
    throw @"
Не найден plugin-local runtime bundle для `computer-use-win`.

Ожидался apphost:
$serverExePath

Сначала подготовь install artifact командой:
powershell -ExecutionPolicy Bypass -File scripts/codex/publish-computer-use-win-plugin.ps1
"@
}

Assert-RuntimeBundleMatchesManifest -RootPath $runtimeRoot -ManifestPath $runtimeManifestPath

& $serverExePath --tool-surface-profile computer-use-win
