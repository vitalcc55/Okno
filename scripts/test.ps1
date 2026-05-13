. (Join-Path $PSScriptRoot 'common.ps1')

$repoRoot = Get-RepoRoot -ScriptRoot $PSScriptRoot
${null} = Initialize-WinBridgeExecutionContext -RepoRoot $repoRoot -UseArtifactsRoot
Set-Location $repoRoot

$runtimeTestContext = Resolve-WinBridgeTestProjectContext -RepoRoot $repoRoot -TestProjectName 'WinBridge.Runtime.Tests'
$integrationTestContext = Resolve-WinBridgeVerificationContext -RepoRoot $repoRoot -TestProjectName 'WinBridge.Server.IntegrationTests'

Invoke-NativeCommand -Description 'dotnet test runtime' -Command {
    dotnet @($runtimeTestContext.DotnetTestArguments)
}

$env:WINBRIDGE_RUN_ID = [string]$integrationTestContext.RunId
$env:WINBRIDGE_RUN_ROOT = [string]$integrationTestContext.RunRoot
if ([string]::IsNullOrWhiteSpace([string]$integrationTestContext.EffectiveArtifactsRoot)) {
    Remove-Item Env:WINBRIDGE_ARTIFACTS_ROOT -ErrorAction SilentlyContinue
}
else {
    $env:WINBRIDGE_ARTIFACTS_ROOT = [string]$integrationTestContext.EffectiveArtifactsRoot
}

$bundleArgs = @{
    RepoRoot                   = $repoRoot
    RunId                      = [string]$integrationTestContext.RunId
    RunRoot                    = [string]$integrationTestContext.RunRoot
    PreferredSourceContextName = [string]$integrationTestContext.BundleSourceContextName
    PreferredRelativeSourcePath = [string]$integrationTestContext.BundleSourceRelativePath
}
if (-not [string]::IsNullOrWhiteSpace([string]$integrationTestContext.EffectiveArtifactsRoot)) {
    $bundleArgs.ArtifactsRoot = [string]$integrationTestContext.EffectiveArtifactsRoot
}

${null} = Use-OknoTestBundle @bundleArgs

Invoke-NativeCommand -Description 'dotnet test integration' -Command {
    dotnet @($integrationTestContext.DotnetTestArguments)
}
