. (Join-Path $PSScriptRoot 'common.ps1')

$repoRoot = Get-RepoRoot -ScriptRoot $PSScriptRoot
Set-Location $repoRoot

Invoke-NativeCommand -Description 'dotnet restore' -Command {
    dotnet restore WinBridge.sln
}
