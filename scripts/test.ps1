. (Join-Path $PSScriptRoot 'common.ps1')

$repoRoot = Get-RepoRoot -ScriptRoot $PSScriptRoot
Set-Location $repoRoot

Invoke-NativeCommand -Description 'dotnet test' -Command {
    dotnet test WinBridge.sln --no-build
}
