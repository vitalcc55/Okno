param(
    [string] $PublishSourceRoot = '',
    [switch] $FailAfterBackup,
    [switch] $FailRestore,
    [switch] $FailRepairCopyAfterServer,
    [switch] $FailRepairHandoff,
    [switch] $FailRepairFallbackHandoff,
    [switch] $FailBackupCleanup
)

$ErrorActionPreference = 'Stop'
if (Get-Variable -Name PSStyle -ErrorAction Ignore) {
    $PSStyle.OutputRendering = 'PlainText'
}

$parameters = @{}
if (-not [string]::IsNullOrWhiteSpace($PublishSourceRoot)) {
    $parameters['PublishSourceRoot'] = $PublishSourceRoot
}

foreach ($entry in @(
    @{ Name = 'FailAfterBackup'; Value = $FailAfterBackup },
    @{ Name = 'FailRestore'; Value = $FailRestore },
    @{ Name = 'FailRepairCopyAfterServer'; Value = $FailRepairCopyAfterServer },
    @{ Name = 'FailRepairHandoff'; Value = $FailRepairHandoff },
    @{ Name = 'FailRepairFallbackHandoff'; Value = $FailRepairFallbackHandoff },
    @{ Name = 'FailBackupCleanup'; Value = $FailBackupCleanup }
)) {
    if ($entry.Value) {
        $parameters[$entry.Name] = $true
    }
}

& (Join-Path $PSScriptRoot 'publish-computer-use-win-plugin-core.ps1') @parameters
