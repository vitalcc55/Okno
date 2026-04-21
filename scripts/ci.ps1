. (Join-Path $PSScriptRoot 'common.ps1')

$repoRoot = Get-RepoRoot -ScriptRoot $PSScriptRoot
${null} = Initialize-WinBridgeExecutionContext -RepoRoot $repoRoot -DefaultRunId ("ci-" + (Get-Date -Format 'yyyyMMddTHHmmssfff')) -UseArtifactsRoot
Set-Location $repoRoot

$ciStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$stepDurations = [ordered]@{}

function Invoke-CiTimedStep {
    param(
        [Parameter(Mandatory)]
        [string] $Key,
        [Parameter(Mandatory)]
        [string] $Description,
        [Parameter(Mandatory)]
        [string] $ScriptPath
    )

    $stepStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    Invoke-ScriptProcessStep -Description $Description -ScriptPath $ScriptPath
    $stepStopwatch.Stop()
    $stepDurations[$Key] = $stepStopwatch.Elapsed
}

Invoke-CiTimedStep -Key 'bootstrap' -Description 'bootstrap step' -ScriptPath (Join-Path $PSScriptRoot 'bootstrap.ps1')
Invoke-CiTimedStep -Key 'build' -Description 'build step' -ScriptPath (Join-Path $PSScriptRoot 'build.ps1')
Invoke-CiTimedStep -Key 'test' -Description 'test step' -ScriptPath (Join-Path $PSScriptRoot 'test.ps1')
Invoke-CiTimedStep -Key 'smoke' -Description 'smoke step' -ScriptPath (Join-Path $PSScriptRoot 'smoke.ps1')
Invoke-CiTimedStep -Key 'refresh_generated_docs' -Description 'refresh generated docs step' -ScriptPath (Join-Path $PSScriptRoot 'refresh-generated-docs.ps1')

$ciStopwatch.Stop()
Write-Host '# WinBridge CI timing'
Write-Host ''
foreach ($entry in $stepDurations.GetEnumerator()) {
    Write-Host ("- {0}: {1}" -f $entry.Key, $entry.Value)
}
Write-Host ("- total: {0}" -f $ciStopwatch.Elapsed)
