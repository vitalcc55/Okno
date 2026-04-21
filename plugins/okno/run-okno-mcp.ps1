$ErrorActionPreference = 'Stop'
if (Get-Variable -Name PSStyle -ErrorAction Ignore) {
    $PSStyle.OutputRendering = 'PlainText'
}

$repoRoot = & (Join-Path $PSScriptRoot 'resolve-okno-repo-root.ps1') -PluginRoot $PSScriptRoot
Set-Location $repoRoot

$artifactsRoot = Join-Path $repoRoot '.tmp\.codex\artifacts\local'
$launchTarget = & (Join-Path $repoRoot 'scripts\codex\resolve-okno-server-launch-target.ps1') `
    -RepoRoot $repoRoot `
    -ArtifactsRoot $artifactsRoot `
    -PreferredSourceContextName artifacts_root `
    -ForcePrepare | ConvertFrom-Json

if ($launchTarget.launchMode -eq 'apphost') {
    & $launchTarget.launchTarget
}
else {
    & dotnet $launchTarget.serverDll
}
