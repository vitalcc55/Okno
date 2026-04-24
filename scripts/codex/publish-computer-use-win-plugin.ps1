$ErrorActionPreference = 'Stop'
if (Get-Variable -Name PSStyle -ErrorAction Ignore) {
    $PSStyle.OutputRendering = 'PlainText'
}

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$serverProjectPath = Join-Path $repoRoot 'src\WinBridge.Server\WinBridge.Server.csproj'
$runtimeParent = Join-Path $repoRoot 'plugins\computer-use-win\runtime'
$runtimeRoot = Join-Path $runtimeParent 'win-x64'
$runtimeBundleManifestFileName = 'okno-runtime-bundle-manifest.json'
$publishRoot = Join-Path $repoRoot '.tmp\.codex\publish\computer-use-win\win-x64'
$stagingRoot = Join-Path $publishRoot ('staging-' + [Guid]::NewGuid().ToString('N'))
$swapRoot = Join-Path $runtimeParent ('win-x64.publish-' + [Guid]::NewGuid().ToString('N'))
$backupRoot = Join-Path $runtimeParent ('win-x64.backup-' + [Guid]::NewGuid().ToString('N'))
$repairRoot = Join-Path $runtimeParent ('win-x64.repair-' + [Guid]::NewGuid().ToString('N'))

function Remove-DirectoryIfExists {
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    if (Test-Path $Path -PathType Container) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
}

function Write-StderrDiagnostic {
    param(
        [Parameter(Mandatory)]
        [string] $Message
    )

    [Console]::Error.WriteLine($Message)
}

function Invoke-NativeCommandToStderr {
    param(
        [Parameter(Mandatory)]
        [scriptblock] $Command,
        [Parameter(Mandatory)]
        [string] $FailureMessage
    )

    $output = & $Command 2>&1
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        foreach ($line in $output) {
            if ($null -ne $line) {
                Write-StderrDiagnostic -Message ([string]$line)
            }
        }

        throw "$FailureMessage ExitCode=$exitCode."
    }
}

function Remove-DirectoryBestEffort {
    param(
        [Parameter(Mandatory)]
        [string] $Path,
        [Parameter(Mandatory)]
        [string] $Description
    )

    try {
        if ($env:COMPUTER_USE_WIN_TEST_FAIL_BACKUP_CLEANUP -eq '1' -and [System.IO.Path]::GetFullPath($Path) -eq [System.IO.Path]::GetFullPath($backupRoot)) {
            throw "Synthetic backup cleanup failure."
        }

        Remove-DirectoryIfExists -Path $Path
    }
    catch {
        Write-StderrDiagnostic -Message "$Description failed: $($_.Exception.Message)"
    }
}

function Copy-DirectoryContents {
    param(
        [Parameter(Mandatory)]
        [string] $SourceRoot,
        [Parameter(Mandatory)]
        [string] $DestinationRoot
    )

    $normalizedSourceRoot = [System.IO.Path]::GetFullPath($SourceRoot).TrimEnd('\')
    Get-ChildItem -LiteralPath $SourceRoot -Recurse -File | ForEach-Object {
        $sourcePath = [System.IO.Path]::GetFullPath($_.FullName)
        $relativePath = $sourcePath.Substring($normalizedSourceRoot.Length).TrimStart('\')
        $destinationPath = Join-Path $DestinationRoot $relativePath
        $destinationDirectory = Split-Path -Parent $destinationPath
        if (-not (Test-Path $destinationDirectory -PathType Container)) {
            New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
        }

        Copy-Item -LiteralPath $_.FullName -Destination $destinationPath -Force

        if ($env:COMPUTER_USE_WIN_TEST_FAIL_REPAIR_COPY_AFTER_SERVER -eq '1' `
            -and [System.IO.Path]::GetFullPath($DestinationRoot) -eq [System.IO.Path]::GetFullPath($repairRoot) `
            -and [string]::Equals($relativePath, 'Okno.Server.exe', [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Synthetic repair copy failure after Okno.Server.exe."
        }
    }
}

function Get-RuntimeBundleManifestPath {
    param(
        [Parameter(Mandatory)]
        [string] $RootPath
    )

    Join-Path $RootPath $runtimeBundleManifestFileName
}

function New-RuntimeBundleManifest {
    param(
        [Parameter(Mandatory)]
        [string] $RootPath
    )

    $normalizedRootPath = [System.IO.Path]::GetFullPath($RootPath).TrimEnd('\')
    $files = Get-ChildItem -LiteralPath $RootPath -Recurse -File |
        Where-Object { -not [string]::Equals($_.Name, $runtimeBundleManifestFileName, [System.StringComparison]::OrdinalIgnoreCase) } |
        Sort-Object FullName |
        ForEach-Object {
            $fullPath = [System.IO.Path]::GetFullPath($_.FullName)
            $relativePath = $fullPath.Substring($normalizedRootPath.Length).TrimStart('\')
            [pscustomobject]@{
                path = $relativePath
                size = [int64]$_.Length
            }
        }

    [pscustomobject]@{
        formatVersion = 1
        files = @($files)
    }
}

function Write-RuntimeBundleManifest {
    param(
        [Parameter(Mandatory)]
        [string] $RootPath
    )

    $manifest = New-RuntimeBundleManifest -RootPath $RootPath
    $manifestPath = Get-RuntimeBundleManifestPath -RootPath $RootPath
    $manifest | ConvertTo-Json -Depth 6 -Compress | Set-Content -Path $manifestPath -Encoding UTF8
}

function Read-RuntimeBundleManifest {
    param(
        [Parameter(Mandatory)]
        [string] $RootPath
    )

    $manifestPath = Get-RuntimeBundleManifestPath -RootPath $RootPath
    if (-not (Test-Path $manifestPath -PathType Leaf)) {
        throw "Runtime bundle manifest '$manifestPath' is missing."
    }

    Get-Content -Path $manifestPath -Raw | ConvertFrom-Json
}

function Test-RuntimeBundleManifestExists {
    param(
        [Parameter(Mandatory)]
        [string] $RootPath
    )

    Test-Path (Get-RuntimeBundleManifestPath -RootPath $RootPath) -PathType Leaf
}

function Assert-RuntimeBundleMatchesManifest {
    param(
        [Parameter(Mandatory)]
        [string] $RootPath,
        [Parameter(Mandatory)]
        [string] $Description
    )

    $manifest = Read-RuntimeBundleManifest -RootPath $RootPath
    if ($manifest.formatVersion -ne 1) {
        throw "$Description uses unsupported runtime bundle manifest version '$($manifest.formatVersion)'."
    }

    $expectedEntries = @($manifest.files)
    $expectedMap = New-Object 'System.Collections.Generic.Dictionary[string,long]' ([System.StringComparer]::Ordinal)
    foreach ($entry in $expectedEntries) {
        $expectedMap[[string]$entry.path] = [int64]$entry.size
    }

    $normalizedRootPath = [System.IO.Path]::GetFullPath($RootPath).TrimEnd('\')
    $actualFiles = Get-ChildItem -LiteralPath $RootPath -Recurse -File |
        Where-Object { -not [string]::Equals($_.Name, $runtimeBundleManifestFileName, [System.StringComparison]::OrdinalIgnoreCase) } |
        Sort-Object FullName

    foreach ($file in $actualFiles) {
        $fullPath = [System.IO.Path]::GetFullPath($file.FullName)
        $relativePath = $fullPath.Substring($normalizedRootPath.Length).TrimStart('\')
        if (-not $expectedMap.ContainsKey($relativePath)) {
            throw "$Description contains unexpected file '$relativePath'."
        }

        if ([int64]$file.Length -ne $expectedMap[$relativePath]) {
            throw "$Description contains size drift for '$relativePath'."
        }

        $null = $expectedMap.Remove($relativePath)
    }

    if ($expectedMap.Count -gt 0) {
        throw "$Description is incomplete. Missing: $($expectedMap.Keys -join ', ')."
    }
}

function Assert-LegacyRuntimeBundleCanBeMigrated {
    param(
        [Parameter(Mandatory)]
        [string] $RootPath,
        [Parameter(Mandatory)]
        [string] $Description
    )

    $requiredFiles = @(
        'Okno.Server.exe',
        'Okno.Server.dll',
        'Okno.Server.deps.json',
        'Okno.Server.runtimeconfig.json',
        'hostfxr.dll',
        'hostpolicy.dll',
        'coreclr.dll'
    )

    $missing = @(
        $requiredFiles | Where-Object {
            -not (Test-Path (Join-Path $RootPath $_) -PathType Leaf)
        }
    )

    if ($missing.Count -gt 0) {
        throw "$Description cannot be migrated to manifest-owned runtime bundle. Missing: $($missing -join ', ')."
    }
}

function Assert-OrCreateRuntimeBundleManifest {
    param(
        [Parameter(Mandatory)]
        [string] $RootPath,
        [Parameter(Mandatory)]
        [string] $Description
    )

    if (Test-RuntimeBundleManifestExists -RootPath $RootPath) {
        Assert-RuntimeBundleMatchesManifest -RootPath $RootPath -Description $Description
        return
    }

    Assert-LegacyRuntimeBundleCanBeMigrated -RootPath $RootPath -Description $Description
    Write-RuntimeBundleManifest -RootPath $RootPath
    Assert-RuntimeBundleMatchesManifest -RootPath $RootPath -Description $Description
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
    Assert-OrCreateRuntimeBundleManifest -RootPath $DestinationRoot -Description $Description
}

function Publish-RuntimeBundleToStaging {
    if (-not [string]::IsNullOrWhiteSpace($env:COMPUTER_USE_WIN_TEST_PUBLISH_SOURCE_ROOT)) {
        $publishSourceRoot = $env:COMPUTER_USE_WIN_TEST_PUBLISH_SOURCE_ROOT
        Assert-RuntimeBundleMatchesManifest -RootPath $publishSourceRoot -Description "Test publish source runtime bundle '$publishSourceRoot'"
        Copy-DirectoryContents -SourceRoot $publishSourceRoot -DestinationRoot $stagingRoot
        Assert-RuntimeBundleMatchesManifest -RootPath $stagingRoot -Description "Published computer-use-win runtime bundle '$stagingRoot'"
        return
    }

    Invoke-NativeCommandToStderr -FailureMessage "dotnet publish failed for computer-use-win runtime bundle." -Command {
        & dotnet publish $serverProjectPath `
            --configuration Release `
            --runtime win-x64 `
            --self-contained true `
            -p:UseAppHost=true `
            -p:PublishSingleFile=false `
            -p:PublishTrimmed=false `
            --output $stagingRoot
    }

    Write-RuntimeBundleManifest -RootPath $stagingRoot
    Assert-RuntimeBundleMatchesManifest -RootPath $stagingRoot -Description "Published computer-use-win runtime bundle '$stagingRoot'"
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

    Assert-RuntimeBundleMatchesManifest -RootPath $SourceRoot -Description $Description

    if (Test-Path $DestinationRoot -PathType Container) {
        Remove-DirectoryIfExists -Path $DestinationRoot
    }

    if ($env:COMPUTER_USE_WIN_TEST_FAIL_REPAIR_HANDOFF -eq '1' `
        -and [System.IO.Path]::GetFullPath($SourceRoot) -eq [System.IO.Path]::GetFullPath($repairRoot)) {
        throw "Synthetic repair handoff failure."
    }

    Move-Item -LiteralPath $SourceRoot -Destination $DestinationRoot
    Assert-RuntimeBundleMatchesManifest -RootPath $DestinationRoot -Description $Description
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
            Stage-RuntimeBundleFromBackup -DestinationRoot $runtimeRoot -Description $Description
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
New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null
New-Item -ItemType Directory -Path $runtimeParent -Force | Out-Null
$promoteCompleted = $false
$restoreCompleted = $false
$terminalState = 'publishing'

try {
    Publish-RuntimeBundleToStaging
    Copy-DirectoryContents -SourceRoot $stagingRoot -DestinationRoot $swapRoot

    Assert-RuntimeBundleMatchesManifest -RootPath $swapRoot -Description "Plugin runtime swap directory '$swapRoot'"

    try {
        if (Test-Path $runtimeRoot -PathType Container) {
            Move-Item -LiteralPath $runtimeRoot -Destination $backupRoot
        }

        if ($env:COMPUTER_USE_WIN_TEST_FAIL_AFTER_BACKUP -eq '1') {
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
                if ($env:COMPUTER_USE_WIN_TEST_FAIL_RESTORE -eq '1') {
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
    if ($promoteCompleted -or $restoreCompleted) {
        Remove-DirectoryBestEffort -Path $backupRoot -Description "backup cleanup after $terminalState"
    }
}

[pscustomobject]@{
    runtimeRoot = $runtimeRoot
    serverExe = Join-Path $runtimeRoot 'Okno.Server.exe'
} | ConvertTo-Json -Compress
