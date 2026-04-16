. (Join-Path $PSScriptRoot 'common.ps1')

$repoRoot = Get-RepoRoot -ScriptRoot $PSScriptRoot
${null} = Initialize-WinBridgeExecutionContext -RepoRoot $repoRoot -UseArtifactsRoot
Set-Location $repoRoot

Invoke-WinBridgeSolutionRestore -Description 'dotnet restore'
