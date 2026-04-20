. (Join-Path $PSScriptRoot 'common.ps1')

$repoRoot = Get-RepoRoot -ScriptRoot $PSScriptRoot
${null} = Initialize-WinBridgeExecutionContext -RepoRoot $repoRoot -UseArtifactsRoot
Set-Location $repoRoot
$verificationContext = Resolve-WinBridgeVerificationContext -RepoRoot $repoRoot

$env:WINBRIDGE_RUN_ID = [string]$verificationContext.RunId
$env:WINBRIDGE_RUN_ROOT = [string]$verificationContext.RunRoot
if ([string]::IsNullOrWhiteSpace([string]$verificationContext.EffectiveArtifactsRoot)) {
    Remove-Item Env:WINBRIDGE_ARTIFACTS_ROOT -ErrorAction SilentlyContinue
}
else {
    $env:WINBRIDGE_ARTIFACTS_ROOT = [string]$verificationContext.EffectiveArtifactsRoot
}

$bundleArgs = @{
    RepoRoot                   = $repoRoot
    RunId                      = [string]$verificationContext.RunId
    RunRoot                    = [string]$verificationContext.RunRoot
    PreferredSourceContextName = [string]$verificationContext.BundleSourceContextName
    PreferredRelativeSourcePath = [string]$verificationContext.BundleSourceRelativePath
}
if (-not [string]::IsNullOrWhiteSpace([string]$verificationContext.EffectiveArtifactsRoot)) {
    $bundleArgs.ArtifactsRoot = [string]$verificationContext.EffectiveArtifactsRoot
}

${null} = Use-OknoTestBundle @bundleArgs

Invoke-NativeCommand -Description 'dotnet test' -Command {
    dotnet @($verificationContext.DotnetTestArguments)
}
