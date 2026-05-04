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

. (Join-Path $PSScriptRoot 'computer-use-win-runtime-bundle-common.ps1')

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$serverProjectPath = Join-Path $repoRoot 'src\WinBridge.Server\WinBridge.Server.csproj'
$runtimeParent = Join-Path $repoRoot 'plugins\computer-use-win\runtime'
$runtimeRoot = Join-Path $runtimeParent 'win-x64'
$publishRoot = Join-Path $repoRoot '.tmp\.codex\publish\computer-use-win\win-x64'
$stagingRoot = Join-Path $publishRoot ('staging-' + [Guid]::NewGuid().ToString('N'))
$swapRoot = Join-Path $runtimeParent ('win-x64.publish-' + [Guid]::NewGuid().ToString('N'))
$backupRoot = Join-Path $runtimeParent ('win-x64.backup-' + [Guid]::NewGuid().ToString('N'))
$repairRoot = Join-Path $runtimeParent ('win-x64.repair-' + [Guid]::NewGuid().ToString('N'))
$repairFallbackRoot = Join-Path $runtimeParent ('win-x64.repair-fallback-' + [Guid]::NewGuid().ToString('N'))

function Remove-DirectoryBestEffort {
    param(
        [Parameter(Mandatory)]
        [string] $Path,
        [Parameter(Mandatory)]
        [string] $Description
    )

    try {
        if ($FailBackupCleanup -and [System.IO.Path]::GetFullPath($Path) -eq [System.IO.Path]::GetFullPath($backupRoot)) {
            throw "Synthetic backup cleanup failure."
        }

        Remove-DirectoryIfExists -Path $Path
    }
    catch {
        Write-StderrDiagnostic -Message "$Description failed: $($_.Exception.Message)"
    }
}

function Stage-RuntimeBundleFromBackup {
    param(
        [Parameter(Mandatory)]
        [string] $DestinationRoot,
        [Parameter(Mandatory)]
        [string] $Description
    )

    Remove-DirectoryIfExists -Path $DestinationRoot
    Copy-DirectoryContents -SourceRoot $backupRoot -DestinationRoot $DestinationRoot
    Assert-ComputerUseWinRuntimeBundleHasExistingManifest -RootPath $DestinationRoot -Description $Description
}

function Publish-RuntimeBundleToStaging {
    Publish-ComputerUseWinRuntimeBundleToDirectory -RepoRoot $repoRoot -DestinationRoot $stagingRoot -Rid 'win-x64' -PublishSourceRoot $PublishSourceRoot
}

function Promote-ValidatedRuntimeBundle {
    param(
        [Parameter(Mandatory)]
        [string] $SourceRoot,
        [Parameter(Mandatory)]
        [string] $DestinationRoot,
        [Parameter(Mandatory)]
        [string] $Description
    )

    Assert-ComputerUseWinRuntimeBundleMatchesManifest -RootPath $SourceRoot -Description $Description

    if (Test-Path $DestinationRoot -PathType Container) {
        Remove-DirectoryIfExists -Path $DestinationRoot
    }

    if ($FailRepairHandoff `
        -and [System.IO.Path]::GetFullPath($SourceRoot) -eq [System.IO.Path]::GetFullPath($repairRoot)) {
        throw "Synthetic repair handoff failure."
    }

    if ($FailRepairFallbackHandoff `
        -and [System.IO.Path]::GetFullPath($SourceRoot) -eq [System.IO.Path]::GetFullPath($repairFallbackRoot)) {
        throw "Synthetic repair fallback handoff failure."
    }

    Move-Item -LiteralPath $SourceRoot -Destination $DestinationRoot
    Assert-ComputerUseWinRuntimeBundleMatchesManifest -RootPath $DestinationRoot -Description $Description
}

function Restore-CanonicalRuntimeFromBackup {
    param(
        [Parameter(Mandatory)]
        [string] $Description
    )

    try {
        Stage-RuntimeBundleFromBackup -DestinationRoot $repairRoot -Description "Staged runtime repair directory '$repairRoot'"
        Promote-ValidatedRuntimeBundle -SourceRoot $repairRoot -DestinationRoot $runtimeRoot -Description $Description
    }
    catch {
        if (Test-Path $runtimeRoot -PathType Container) {
            Remove-DirectoryIfExists -Path $runtimeRoot
        }

        try {
            Stage-RuntimeBundleFromBackup -DestinationRoot $repairFallbackRoot -Description "Staged runtime fallback repair directory '$repairFallbackRoot'"
            Promote-ValidatedRuntimeBundle -SourceRoot $repairFallbackRoot -DestinationRoot $runtimeRoot -Description $Description
        }
        catch {
            if (Test-Path $runtimeRoot -PathType Container) {
                Remove-DirectoryIfExists -Path $runtimeRoot
            }

            throw
        }
    }
}

Remove-DirectoryIfExists -Path $stagingRoot
Remove-DirectoryIfExists -Path $swapRoot
Remove-DirectoryIfExists -Path $backupRoot
Remove-DirectoryIfExists -Path $repairRoot
Remove-DirectoryIfExists -Path $repairFallbackRoot
New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null
New-Item -ItemType Directory -Path $runtimeParent -Force | Out-Null
$promoteCompleted = $false
$restoreCompleted = $false
$terminalState = 'publishing'

try {
    Publish-RuntimeBundleToStaging
    Copy-DirectoryContents -SourceRoot $stagingRoot -DestinationRoot $swapRoot

    Assert-ComputerUseWinRuntimeBundleMatchesManifest -RootPath $swapRoot -Description "Plugin runtime swap directory '$swapRoot'"

    try {
        if (Test-Path $runtimeRoot -PathType Container) {
            Move-Item -LiteralPath $runtimeRoot -Destination $backupRoot
        }

        if ($FailAfterBackup) {
            throw "Synthetic publish promote failure after backup handoff."
        }

        Move-Item -LiteralPath $swapRoot -Destination $runtimeRoot
        $promoteCompleted = $true
        $terminalState = 'promote_succeeded'
    }
    catch {
        if (-not (Test-Path $runtimeRoot -PathType Container) -and (Test-Path $backupRoot -PathType Container)) {
            $restoreStarted = $false
            try {
                if ($FailRestore) {
                    throw "Synthetic publish restore failure after promote error."
                }

                $restoreStarted = $true
                Restore-CanonicalRuntimeFromBackup -Description "Canonical runtime path '$runtimeRoot' after restore"
            }
            catch {
                if ($restoreStarted) {
                    throw
                }

                Restore-CanonicalRuntimeFromBackup -Description "Canonical runtime path '$runtimeRoot' after restore repair"
            }

            $restoreCompleted = $true
            $terminalState = 'restore_succeeded'
        }
        else {
            $terminalState = 'recovery_incomplete'
        }

        throw
    }
}
finally {
    Remove-DirectoryBestEffort -Path $stagingRoot -Description 'staging cleanup'
    Remove-DirectoryBestEffort -Path $swapRoot -Description 'swap cleanup'
    Remove-DirectoryBestEffort -Path $repairRoot -Description 'repair cleanup'
    Remove-DirectoryBestEffort -Path $repairFallbackRoot -Description 'repair fallback cleanup'
    if ($promoteCompleted -or $restoreCompleted) {
        Remove-DirectoryBestEffort -Path $backupRoot -Description "backup cleanup after $terminalState"
    }
}

[pscustomobject]@{
    runtimeRoot = $runtimeRoot
    serverExe = Join-Path $runtimeRoot 'Okno.Server.exe'
} | ConvertTo-Json -Compress
