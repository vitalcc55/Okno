. (Join-Path $PSScriptRoot 'common.ps1')

$repoRoot = Get-RepoRoot -ScriptRoot $PSScriptRoot
$proofRunId = Get-Date -Format 'yyyyMMddTHHmmssfff'
if ([string]::IsNullOrWhiteSpace($env:WINBRIDGE_ARTIFACTS_ROOT)) {
    ${null} = Initialize-WinBridgeExecutionContext -RepoRoot $repoRoot -DefaultRunId ("computer-use-win-observation-completeness-proof-smoke-" + $proofRunId)
}
Set-Location $repoRoot

$artifactRoot = Join-Path $repoRoot "artifacts\smoke\computer-use-win-observation-completeness\$proofRunId"
$summaryPath = Join-Path $artifactRoot 'summary.md'
$reportPath = Join-Path $artifactRoot 'report.json'
$trxFileName = 'computer-use-win-observation-completeness-proof-smoke.trx'
$trxPath = Join-Path $artifactRoot $trxFileName
New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null

$scenarioNames = @(
    'ComputerUseWinGetAppStatePublishesIncompleteSemanticPreviewWithImageWhenNodeBudgetIsBounded',
    'ComputerUseWinClickUpdatesDeepSemanticMirrorThroughSelectorOutsidePreview'
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

function Get-ProofSmokeScenarioResults {
    param(
        [Parameter(Mandatory)]
        [string] $TrxPath,
        [Parameter(Mandatory)]
        [string[]] $ScenarioNames
    )

    if (-not (Test-Path $TrxPath -PathType Leaf)) {
        throw "Proof-smoke TRX not found: $TrxPath"
    }

    [xml]$trx = Get-Content -Path $TrxPath -Raw
    $namespaceManager = New-Object System.Xml.XmlNamespaceManager($trx.NameTable)
    $namespaceManager.AddNamespace('trx', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
    $unitResults = @($trx.SelectNodes('//trx:UnitTestResult', $namespaceManager))
    if ($unitResults.Count -ne $ScenarioNames.Count) {
        throw "Proof-smoke expected $($ScenarioNames.Count) executed scenarios, but TRX contains $($unitResults.Count)."
    }

    $scenarioReports = @()
    foreach ($scenarioName in $ScenarioNames) {
        $scenarioMatches = @(
            $unitResults |
                Where-Object {
                    $testName = [string]$_.testName
                    $testName -eq $scenarioName -or $testName.EndsWith(".$scenarioName", [System.StringComparison]::Ordinal)
                })

        if ($scenarioMatches.Count -ne 1) {
            throw "Proof-smoke TRX must contain exactly one result for scenario '$scenarioName', but found $($scenarioMatches.Count)."
        }

        $match = $scenarioMatches[0]
        $outcome = [string]$match.outcome
        if ($outcome -ne 'Passed') {
            throw "Proof-smoke scenario '$scenarioName' finished with outcome '$outcome'."
        }

        $scenarioReports += [PSCustomObject]@{
            name = $scenarioName
            outcome = $outcome
            duration = [string]$match.duration
        }
    }

    return $scenarioReports
}

$startedAtUtc = [DateTimeOffset]::UtcNow
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$status = 'failed'
$errorMessage = $null
$scenarioReports = @()
$proofCoverage = @(
    'screenshot-backed get_app_state returns image content with incomplete semanticPreview metadata under bounded maxNodes',
    'selector-backed click reaches a deep AutomationId target outside the compact preview and carries observeAfter successorState'
)

try {
    Invoke-NativeCommand -Description 'dotnet test computer-use-win observation completeness proof smoke' -Command {
        dotnet @dotnetArguments
    }

    $scenarioReports = @(Get-ProofSmokeScenarioResults -TrxPath $trxPath -ScenarioNames $scenarioNames)
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
    $summaryLines.Add('# Computer Use Win Observation Completeness Proof Smoke')
    $summaryLines.Add('')
    $summaryLines.Add("- status: $status")
    $summaryLines.Add("- duration: $($stopwatch.Elapsed)")
    $summaryLines.Add('- artifact_root: `' + $artifactRoot + '`')
    $summaryLines.Add('- trx: `' + $trxDisplay + '`')
    $summaryLines.Add('')
    $summaryLines.Add('## Scenarios')
    $summaryLines.Add('')
    foreach ($scenarioReport in $scenarioReports) {
        $summaryLines.Add("- $($scenarioReport.name): $($scenarioReport.outcome) ($($scenarioReport.duration))")
    }
    $summaryLines.Add('')
    $summaryLines.Add('## Command')
    $summaryLines.Add('')
    $summaryLines.Add('```powershell')
    $summaryLines.Add($commandText)
    $summaryLines.Add('```')

    Set-Content -Path $summaryPath -Value $summaryLines
}

Write-Host "Computer Use Win observation completeness proof smoke: $status"
Write-Host "Artifacts: $artifactRoot"
