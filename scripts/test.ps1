. (Join-Path $PSScriptRoot 'common.ps1')

$repoRoot = Get-RepoRoot -ScriptRoot $PSScriptRoot
${null} = Initialize-WinBridgeExecutionContext -RepoRoot $repoRoot -UseArtifactsRoot
Set-Location $repoRoot

$runtimeTestContext = Resolve-WinBridgeTestProjectContext -RepoRoot $repoRoot -TestProjectName 'WinBridge.Runtime.Tests'
$integrationTestContext = Resolve-WinBridgeVerificationContext -RepoRoot $repoRoot -TestProjectName 'WinBridge.Server.IntegrationTests'
$skipInteractiveDesktopProof = [string]::Equals($env:WINBRIDGE_SKIP_INTERACTIVE_DESKTOP_PROOF, '1', [System.StringComparison]::Ordinal)

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

$integrationArguments = @($integrationTestContext.DotnetTestArguments)
if ($skipInteractiveDesktopProof) {
    $interactiveDesktopScenarios = @(
        'ComputerUseWinClickUsesStateTokenAndElementIndexAfterApprovedAppState',
        'ComputerUseWinPressKeyMovesKeyboardFocusThroughApprovedAppState',
        'ComputerUseWinSetValueUpdatesSemanticMirrorThroughApprovedAppState',
        'ComputerUseWinSetValueUpdatesRangeMirrorThroughApprovedAppState',
        'ComputerUseWinClickUpdatesDeepSemanticMirrorThroughSelectorOutsidePreview',
        'ComputerUseWinTypeTextUpdatesQueryMirrorAfterExplicitFocusProof',
        'ComputerUseWinTypeTextFocusedFallbackUpdatesPoorUiaMirror',
        'ComputerUseWinTypeTextCoordinateConfirmedFallbackUpdatesMirror',
        'ComputerUseWinScrollUpdatesScrollMirrorThroughApprovedAppState',
        'ComputerUseWinPerformSecondaryActionTogglesCheckboxStateThroughApprovedAppState',
        'ComputerUseWinDragUpdatesDragMirrorThroughApprovedAppState'
    )
    $integrationFilter = [string]::Join('&', @($interactiveDesktopScenarios | ForEach-Object { "FullyQualifiedName!~$_" }))
    Write-Host "Skipping interactive desktop integration scenarios for hosted proof mode: $($interactiveDesktopScenarios -join ', ')"
    $integrationArguments += '--filter'
    $integrationArguments += $integrationFilter
}

Invoke-NativeCommand -Description 'dotnet test integration' -Command {
    dotnet @integrationArguments
}
