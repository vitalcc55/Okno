param(
    [string] $SourcePluginRoot = '',
    [string] $CachePluginRoot = ''
)

$ErrorActionPreference = 'Stop'
if (Get-Variable -Name PSStyle -ErrorAction Ignore) {
    $PSStyle.OutputRendering = 'PlainText'
}

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if ([string]::IsNullOrWhiteSpace($SourcePluginRoot)) {
    $SourcePluginRoot = Join-Path $repoRoot 'plugins\computer-use-win'
}
if ([string]::IsNullOrWhiteSpace($CachePluginRoot)) {
    $CachePluginRoot = Join-Path $env:USERPROFILE '.codex\plugins\cache\computer-use-win-local\computer-use-win\0.2.3'
}

$resolvedSourcePluginRoot = [System.IO.Path]::GetFullPath($SourcePluginRoot)
$resolvedCachePluginRoot = [System.IO.Path]::GetFullPath($CachePluginRoot)

if (-not (Test-Path $resolvedSourcePluginRoot -PathType Container)) {
    throw "Source plugin root not found: $resolvedSourcePluginRoot"
}

if (Test-Path $resolvedCachePluginRoot -PathType Container) {
    Remove-Item -LiteralPath $resolvedCachePluginRoot -Recurse -Force
}

$cachePluginParent = Split-Path -Parent $resolvedCachePluginRoot
if (-not [string]::IsNullOrWhiteSpace($cachePluginParent)) {
    New-Item -ItemType Directory -Path $cachePluginParent -Force | Out-Null
}

Copy-Item -LiteralPath $resolvedSourcePluginRoot -Destination $resolvedCachePluginRoot -Recurse -Force

[pscustomobject]@{
    sourcePluginRoot = $resolvedSourcePluginRoot
    cachePluginRoot = $resolvedCachePluginRoot
} | ConvertTo-Json -Compress
