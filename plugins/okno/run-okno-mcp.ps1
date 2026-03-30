$ErrorActionPreference = 'Stop'
if (Get-Variable -Name PSStyle -ErrorAction Ignore) {
    $PSStyle.OutputRendering = 'PlainText'
}

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Set-Location $repoRoot

$serverDll = & (Join-Path $repoRoot 'scripts\codex\resolve-okno-server-dll.ps1') -RepoRoot $repoRoot

& dotnet $serverDll
