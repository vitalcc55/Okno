param(
    [string]$RepoRoot = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)),
    [string]$ManifestPath
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..\common.ps1')

$resolveArgs = @{
    RepoRoot    = $RepoRoot
    AutoPrepare = $true
}
if ($PSBoundParameters.ContainsKey('ManifestPath')) {
    $resolveArgs.ManifestPath = $ManifestPath
}

$resolution = Resolve-WinBridgeBundleResolution @resolveArgs
[System.IO.Path]::GetFullPath([string]$resolution.ServerDll)
