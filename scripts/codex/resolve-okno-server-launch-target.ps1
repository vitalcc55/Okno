param(
    [string]$RepoRoot = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)),
    [string]$ManifestPath,
    [string]$RunId,
    [string]$RunRoot,
    [string]$ArtifactsRoot,
    [ValidateSet('artifacts_root', 'fallback_build_cache')]
    [string]$PreferredSourceContextName,
    [string]$PreferredRelativeSourcePath,
    [switch]$ForcePrepare
)

$ErrorActionPreference = 'Stop'

$serverDll = $null

if ($ForcePrepare) {
    $prepareArgs = @{
        RepoRoot = $RepoRoot
    }

    if ($PSBoundParameters.ContainsKey('RunId')) {
        $prepareArgs.RunId = $RunId
    }

    if ($PSBoundParameters.ContainsKey('RunRoot')) {
        $prepareArgs.RunRoot = $RunRoot
    }

    if ($PSBoundParameters.ContainsKey('ArtifactsRoot')) {
        $prepareArgs.ArtifactsRoot = $ArtifactsRoot
    }

    if ($PSBoundParameters.ContainsKey('PreferredSourceContextName')) {
        $prepareArgs.PreferredSourceContextName = $PreferredSourceContextName
    }

    if ($PSBoundParameters.ContainsKey('PreferredRelativeSourcePath')) {
        $prepareArgs.PreferredRelativeSourcePath = $PreferredRelativeSourcePath
    }

    $preparedManifest = & (Join-Path $PSScriptRoot 'prepare-okno-test-bundle.ps1') @prepareArgs | ConvertFrom-Json
    $serverDll = [string]$preparedManifest.serverDll
}
else {
    $resolveArgs = @{
        RepoRoot = $RepoRoot
    }

    if ($PSBoundParameters.ContainsKey('ManifestPath')) {
        $resolveArgs.ManifestPath = $ManifestPath
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

    if ($PSBoundParameters.ContainsKey('PreferredRelativeSourcePath')) {
        $resolveArgs.PreferredRelativeSourcePath = $PreferredRelativeSourcePath
    }

    $serverDll = & (Join-Path $PSScriptRoot 'resolve-okno-server-dll.ps1') @resolveArgs
}

$serverDirectory = Split-Path -Parent $serverDll
$serverExe = Join-Path $serverDirectory 'Okno.Server.exe'
$useAppHost = Test-Path -LiteralPath $serverExe -PathType Leaf

$payload = [ordered]@{
    launchMode   = if ($useAppHost) { 'apphost' } else { 'dotnet' }
    launchTarget = if ($useAppHost) { [System.IO.Path]::GetFullPath($serverExe) } else { [System.IO.Path]::GetFullPath($serverDll) }
    serverDll    = [System.IO.Path]::GetFullPath($serverDll)
    serverExe    = if ($useAppHost) { [System.IO.Path]::GetFullPath($serverExe) } else { $null }
}

$payload | ConvertTo-Json -Depth 3 -Compress
