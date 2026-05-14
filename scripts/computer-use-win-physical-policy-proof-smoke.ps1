. (Join-Path $PSScriptRoot 'common.ps1')

$repoRoot = Get-RepoRoot -ScriptRoot $PSScriptRoot
$proofRunId = Get-Date -Format 'yyyyMMddTHHmmssfff'
if ([string]::IsNullOrWhiteSpace($env:WINBRIDGE_ARTIFACTS_ROOT)) {
    ${null} = Initialize-WinBridgeExecutionContext -RepoRoot $repoRoot -DefaultRunId ("computer-use-win-physical-policy-proof-smoke-" + $proofRunId)
}
Set-Location $repoRoot

$artifactRoot = Join-Path $repoRoot "artifacts\smoke\computer-use-win-physical-policy-phase-1\$proofRunId"
$summaryPath = Join-Path $artifactRoot 'summary.md'
$reportPath = Join-Path $artifactRoot 'report.json'
$trxFileName = 'computer-use-win-physical-policy-proof-smoke.trx'
$trxPath = Join-Path $artifactRoot $trxFileName
New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null

$scenarioNames = @(
    'ComputerUseWinSetValueUpdatesSemanticMirrorThroughApprovedAppState',
    'ComputerUseWinClickUsesStateTokenAndElementIndexAfterApprovedAppState',
    'ComputerUseWinTypeTextUpdatesQueryMirrorAfterExplicitFocusProof',
    'ComputerUseWinTypeTextFocusedFallbackUpdatesPoorUiaMirror',
    'ComputerUseWinTypeTextCoordinateConfirmedFallbackUpdatesMirror'
)
$filterFragments = foreach ($scenarioName in $scenarioNames) {
    "FullyQualifiedName~$scenarioName"
}
$filter = [string]::Join('|', @($filterFragments))
$testContext = Resolve-WinBridgeTestProjectContext -RepoRoot $repoRoot -TestProjectName 'WinBridge.Server.IntegrationTests'
$dotnetArguments = @()
$dotnetArguments += $testContext.DotnetTestArguments
$dotnetArguments += '--filter'
$dotnetArguments += $filter
$dotnetArguments += '--logger'
$dotnetArguments += "trx;LogFileName=$trxFileName"
$dotnetArguments += '--results-directory'
$dotnetArguments += $artifactRoot

$commandParts = foreach ($argument in $dotnetArguments) {
    $text = [string]$argument
    if ($text -match '\s') {
        '"' + $text + '"'
    }
    else {
        $text
    }
}
$commandText = 'dotnet ' + [string]::Join(' ', @($commandParts))

$startedAtUtc = [DateTimeOffset]::UtcNow
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$status = 'failed'
$errorMessage = $null

try {
    Invoke-NativeCommand -Description 'dotnet test computer-use-win physical policy proof smoke' -Command {
        dotnet @dotnetArguments
    }

    $status = 'passed'
}
catch {
    $errorMessage = $_.Exception.Message
    throw
}
finally {
    $stopwatch.Stop()
    $trxExists = Test-Path $trxPath
    $trxDisplay = if ($trxExists) { $trxPath } else { 'missing' }
    $trxPathOrNull = if ($trxExists) { $trxPath } else { $null }
    $scenarioReports = foreach ($scenarioName in $scenarioNames) {
        [ordered]@{
            name = $scenarioName
        }
    }
    $proofCoverage = @(
        'semantic set_value path',
        'expected_physical click path with successorState',
        'expected_physical type_text path after explicit focus proof',
        'fallback_physical focused type_text path with observeAfter',
        'fallback_physical coordinate-confirmed type_text path with observeAfter'
    )

    $report = [ordered]@{
        status = $status
        startedAtUtc = $startedAtUtc.ToString('O')
        finishedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        duration = $stopwatch.Elapsed.ToString()
        command = $commandText
        artifactRoot = $artifactRoot
        trxPath = $trxPathOrNull
        scenarios = @($scenarioReports)
        proofCoverage = $proofCoverage
        error = $errorMessage
    }

    Set-Content -Path $reportPath -Value ($report | ConvertTo-Json -Depth 6)

    $summaryLines = New-Object System.Collections.Generic.List[string]
    $summaryLines.Add('# Computer Use Win Physical Policy Phase 1 Proof Smoke')
    $summaryLines.Add('')
    $summaryLines.Add("- status: $status")
    $summaryLines.Add("- duration: $($stopwatch.Elapsed)")
    $summaryLines.Add('- artifact_root: `' + $artifactRoot + '`')
    $summaryLines.Add('- trx: `' + $trxDisplay + '`')
    $summaryLines.Add('')
    $summaryLines.Add('## Scenarios')
    $summaryLines.Add('')
    foreach ($scenarioName in $scenarioNames) {
        $summaryLines.Add("- $scenarioName")
    }
    $summaryLines.Add('')
    $summaryLines.Add('## Command')
    $summaryLines.Add('')
    $summaryLines.Add('```powershell')
    $summaryLines.Add($commandText)
    $summaryLines.Add('```')

    Set-Content -Path $summaryPath -Value $summaryLines
}

Write-Host "Computer Use Win physical policy phase-1 proof smoke: $status"
Write-Host "Artifacts: $artifactRoot"
