$ErrorActionPreference = 'Stop'
if (Get-Variable -Name PSStyle -ErrorAction Ignore) {
    $PSStyle.OutputRendering = 'PlainText'
}

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$pluginRoot = Join-Path $repoRoot 'plugins\okno'
$resolverPath = Join-Path $pluginRoot 'resolve-okno-repo-root.ps1'
$hintPath = Join-Path $pluginRoot '.okno-repo-root.txt'

if (-not (Test-Path $pluginRoot -PathType Container)) {
    throw "Plugin root '$pluginRoot' not found."
}

if (-not (Test-Path $resolverPath -PathType Leaf)) {
    throw "Plugin repo-root resolver '$resolverPath' not found."
}

$normalizedRepoRoot = [System.IO.Path]::GetFullPath($repoRoot)
[System.IO.File]::WriteAllText(
    $hintPath,
    $normalizedRepoRoot + [Environment]::NewLine,
    (New-Object System.Text.UTF8Encoding($false))
)

$resolvedRepoRoot = & $resolverPath -PluginRoot $pluginRoot
if (-not [string]::Equals($resolvedRepoRoot, $normalizedRepoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Resolver validation failed: expected '$normalizedRepoRoot', got '$resolvedRepoRoot'."
}

Write-Output "Updated $hintPath"
Write-Output "Resolved repo root: $resolvedRepoRoot"
