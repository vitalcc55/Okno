$ErrorActionPreference = 'Stop'
if (Get-Variable -Name PSStyle -ErrorAction Ignore) {
    $PSStyle.OutputRendering = 'PlainText'
}

$repoRoot = & (Join-Path $PSScriptRoot 'resolve-okno-repo-root.ps1') -PluginRoot $PSScriptRoot
Set-Location $repoRoot

$serverDll = & (Join-Path $repoRoot 'scripts\codex\resolve-okno-server-dll.ps1') -RepoRoot $repoRoot

& dotnet $serverDll
