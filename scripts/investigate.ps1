. (Join-Path $PSScriptRoot 'common.ps1')

$repoRoot = Get-RepoRoot -ScriptRoot $PSScriptRoot
Set-Location $repoRoot

$latestSmokeReport = Get-LatestFile -Path (Join-Path $repoRoot 'artifacts/smoke') -Filter 'report.json'
$latestAudit = $null
$latestSmoke = $null

if ($null -eq $latestSmokeReport) {
    $latestAudit = Get-LatestFile -Path (Join-Path $repoRoot 'artifacts/diagnostics') -Filter 'summary.md'
}
else {
    $latestSmoke = Join-Path $latestSmokeReport.DirectoryName 'summary.md'
    $smokeReport = Get-Content $latestSmokeReport.FullName -Raw | ConvertFrom-Json
    if ($null -ne $smokeReport.health -and -not [string]::IsNullOrWhiteSpace($smokeReport.health.artifactsDirectory)) {
        $latestAudit = Join-Path $smokeReport.health.artifactsDirectory 'summary.md'
    }
}

if ($null -eq $latestAudit -and $null -eq $latestSmokeReport) {
    throw 'No diagnostics artifacts found yet. Run scripts/smoke.ps1 first.'
}

if ($null -ne $latestAudit) {
    if ($latestAudit -is [System.IO.FileInfo]) {
        "# Latest audit summary: $($latestAudit.FullName)"
        Get-Content $latestAudit.FullName
    }
    elseif (Test-Path $latestAudit) {
        "# Latest audit summary: $latestAudit"
        Get-Content $latestAudit
    }
}

if ($null -ne $latestSmokeReport) {
    "# Latest smoke summary: $latestSmoke"
    Get-Content $latestSmoke
}
elseif ($null -ne $latestSmoke) {
    "# Latest smoke summary: $($latestSmoke.FullName)"
    Get-Content $latestSmoke.FullName
}
