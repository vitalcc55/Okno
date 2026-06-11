. (Join-Path $PSScriptRoot 'common.ps1')

$repoRoot = Get-RepoRoot -ScriptRoot $PSScriptRoot
${null} = Initialize-WinBridgeExecutionContext -RepoRoot $repoRoot -UseArtifactsRoot
Set-Location $repoRoot

$testContext = Resolve-WinBridgeTestProjectContext -RepoRoot $repoRoot -TestProjectName 'WinBridge.InstallSurface.AcceptanceTests'
$skipInteractiveDesktopProof = [string]::Equals($env:WINBRIDGE_SKIP_INTERACTIVE_DESKTOP_PROOF, '1', [System.StringComparison]::Ordinal)

$testArguments = @($testContext.DotnetTestArguments)
if ($skipInteractiveDesktopProof) {
    $interactiveDesktopScenarios = @(
        'PackagedOknoSetupAppLaunchesFromOwnAndExternalWorkingDirectories'
    )
    $testFilter = [string]::Join('&', @($interactiveDesktopScenarios | ForEach-Object { "FullyQualifiedName!~$_" }))
    Write-Host "Skipping interactive install-surface scenarios for hosted proof mode: $($interactiveDesktopScenarios -join ', ')"
    $testArguments += '--filter'
    $testArguments += $testFilter
}

Invoke-NativeCommand -Description 'dotnet test install surface acceptance' -Command {
    dotnet @testArguments
}
