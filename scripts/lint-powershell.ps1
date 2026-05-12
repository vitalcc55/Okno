$ErrorActionPreference = 'Stop'
if (Get-Variable -Name PSStyle -ErrorAction Ignore) {
    $PSStyle.OutputRendering = 'PlainText'
}
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$repoRoot = Split-Path -Parent $PSScriptRoot
$settingsPath = Join-Path $repoRoot 'PSScriptAnalyzerSettings.psd1'
$minimumVersion = [Version]'1.25.0'
$minimumPowerShellVersion = [Version]'7.2.11'
$trackedPowerShellPathSpecs = @(
    'scripts/*.ps1'
    'scripts/**/*.ps1'
    'plugins/*.ps1'
    'plugins/**/*.ps1'
)

if ($PSVersionTable.PSEdition -ne 'Core' -or [Version]$PSVersionTable.PSVersion -lt $minimumPowerShellVersion) {
    throw "Run PowerShell static analysis with pwsh $minimumPowerShellVersion or newer. Found $($PSVersionTable.PSEdition) $($PSVersionTable.PSVersion)."
}

if (-not (Test-Path $settingsPath -PathType Leaf)) {
    throw "PSScriptAnalyzer settings file not found: $settingsPath"
}

$module = Get-Module -ListAvailable PSScriptAnalyzer |
    Sort-Object Version -Descending |
    Select-Object -First 1

if ($null -eq $module) {
    throw "PSScriptAnalyzer module is required. Install version $minimumVersion or newer with: Install-Module PSScriptAnalyzer -Scope CurrentUser -Force"
}

if ([Version]$module.Version -lt $minimumVersion) {
    throw "PSScriptAnalyzer $minimumVersion or newer is required. Found $($module.Version) at $($module.Path)."
}

Import-Module PSScriptAnalyzer -MinimumVersion $minimumVersion -Force

$relativeAnalysisFiles = @(git -C $repoRoot ls-files --cached --modified --others --exclude-standard -- $trackedPowerShellPathSpecs)
if ($LASTEXITCODE -ne 0) {
    throw 'Failed to enumerate non-ignored PowerShell source files with git ls-files.'
}

$analysisFiles = @(
    $relativeAnalysisFiles |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Sort-Object -Unique |
        ForEach-Object { Join-Path $repoRoot ($_.Replace('/', [System.IO.Path]::DirectorySeparatorChar)) } |
        Where-Object { Test-Path $_ -PathType Leaf }
)

if ($analysisFiles.Count -eq 0) {
    throw 'No non-ignored PowerShell source files found under scripts/ or plugins/.'
}

$diagnostics = @(
    foreach ($analysisFile in $analysisFiles) {
        if (-not (Test-Path $analysisFile -PathType Leaf)) {
            throw "Tracked PowerShell source file not found: $analysisFile"
        }

        try {
            Invoke-ScriptAnalyzer -Path $analysisFile -Settings $settingsPath
        }
        catch {
            throw "PSScriptAnalyzer failed while analyzing '$analysisFile': $($_.Exception.Message)"
        }
    }
)

if ($diagnostics.Count -eq 0) {
    Write-Output "PSScriptAnalyzer passed for $($analysisFiles.Count) non-ignored scripts/plugins PowerShell source file(s) with PSScriptAnalyzer $($module.Version)."
    exit 0
}

Write-Output "PSScriptAnalyzer found $($diagnostics.Count) diagnostic(s):"
Write-Output ''
$diagnostics |
    Group-Object Severity, RuleName |
    Sort-Object Count -Descending |
    ForEach-Object {
        Write-Output ("- {0}: {1}" -f $_.Name, $_.Count)
    }

Write-Output ''
$diagnostics |
    Sort-Object ScriptPath, Line, Column, RuleName |
    Format-Table -AutoSize ScriptName, Line, Column, Severity, RuleName, Message

exit 1
