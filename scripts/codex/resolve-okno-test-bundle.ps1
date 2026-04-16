param(
    [string]$RepoRoot = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)),
    [string]$ManifestPath,
    [string]$AssemblyBaseDirectory,
    [string]$RunId,
    [string]$RunRoot,
    [string]$ArtifactsRoot,
    [ValidateSet('artifacts_root', 'fallback_build_cache')]
    [string]$PreferredSourceContextName
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

if ($PSBoundParameters.ContainsKey('AssemblyBaseDirectory')) {
    $resolveArgs.AssemblyBaseDirectory = $AssemblyBaseDirectory
}

if ($PSBoundParameters.ContainsKey('RunId')) {
    $resolveArgs.RunId = $RunId
}

if ($PSBoundParameters.ContainsKey('RunRoot')) {
    $resolveArgs.RunRoot = $RunRoot
}

if ($PSBoundParameters.ContainsKey('ArtifactsRoot')) {
    $resolveArgs.ArtifactsRoot = $ArtifactsRoot
}

if ($PSBoundParameters.ContainsKey('PreferredSourceContextName')) {
    $resolveArgs.PreferredSourceContextName = $PreferredSourceContextName
}

$resolution = Resolve-WinBridgeBundleResolution @resolveArgs

$payload = [ordered]@{
    resolutionMode        = [string]$resolution.ResolutionMode
    manifestPath          = [string]$resolution.ManifestPath
    serverDll             = [string]$resolution.ServerDll
    helperExe             = [string]$resolution.HelperExe
    runId                 = [string]$resolution.RunId
    runRoot               = [string]$resolution.RunRoot
    artifactsRoot         = [string]$resolution.ArtifactsRoot
    requestedArtifactsRoot = [string]$resolution.RequestedArtifactsRoot
    preferredSourceContext = [string]$resolution.PreferredSourceContext
    autoPrepared          = [bool]$resolution.AutoPrepared
}

$payload | ConvertTo-Json -Depth 6 -Compress
