. (Join-Path $PSScriptRoot 'common.ps1')

$repoRoot = Get-RepoRoot -ScriptRoot $PSScriptRoot
${null} = Initialize-WinBridgeExecutionContext -RepoRoot $repoRoot -UseArtifactsRoot
Set-Location $repoRoot

$testContext = Resolve-WinBridgeTestProjectContext -RepoRoot $repoRoot -TestProjectName 'WinBridge.InstallSurface.AcceptanceTests'

Invoke-NativeCommand -Description 'dotnet test install surface acceptance' -Command {
    dotnet @($testContext.DotnetTestArguments)
}
