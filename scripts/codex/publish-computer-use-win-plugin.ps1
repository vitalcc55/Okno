$ErrorActionPreference = 'Stop'
if (Get-Variable -Name PSStyle -ErrorAction Ignore) {
    $PSStyle.OutputRendering = 'PlainText'
}

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$serverProjectPath = Join-Path $repoRoot 'src\WinBridge.Server\WinBridge.Server.csproj'
$runtimeParent = Join-Path $repoRoot 'plugins\computer-use-win\runtime'
$runtimeRoot = Join-Path $runtimeParent 'win-x64'
$publishRoot = Join-Path $repoRoot '.tmp\.codex\publish\computer-use-win\win-x64'
$stagingRoot = Join-Path $publishRoot ('staging-' + [Guid]::NewGuid().ToString('N'))
$swapRoot = Join-Path $runtimeParent ('win-x64.publish-' + [Guid]::NewGuid().ToString('N'))
$backupRoot = Join-Path $runtimeParent ('win-x64.backup-' + [Guid]::NewGuid().ToString('N'))

function Remove-DirectoryIfExists {
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    if (Test-Path $Path -PathType Container) {
        Remove-Item -LiteralPath $Path -Recurse -Force
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
        Write-Warning "$Description failed: $($_.Exception.Message)"
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
    }
}

Remove-DirectoryIfExists -Path $stagingRoot
Remove-DirectoryIfExists -Path $swapRoot
Remove-DirectoryIfExists -Path $backupRoot
New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null
New-Item -ItemType Directory -Path $runtimeParent -Force | Out-Null
$promoteCompleted = $false
$restoreCompleted = $false
$terminalState = 'publishing'

try {
    & dotnet publish $serverProjectPath `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        -p:UseAppHost=true `
        -p:PublishSingleFile=false `
        -p:PublishTrimmed=false `
        --output $stagingRoot

    $serverExePath = Join-Path $stagingRoot 'Okno.Server.exe'
    if (-not (Test-Path $serverExePath -PathType Leaf)) {
        throw "Publish did not produce expected apphost '$serverExePath'."
    }

    Copy-DirectoryContents -SourceRoot $stagingRoot -DestinationRoot $swapRoot

    $swappedServerExePath = Join-Path $swapRoot 'Okno.Server.exe'
    if (-not (Test-Path $swappedServerExePath -PathType Leaf)) {
        throw "Plugin runtime swap directory '$swapRoot' does not contain Okno.Server.exe."
    }

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
            try {
                if ($env:COMPUTER_USE_WIN_TEST_FAIL_RESTORE -eq '1') {
                    throw "Synthetic publish restore failure after promote error."
                }

                Move-Item -LiteralPath $backupRoot -Destination $runtimeRoot
            }
            catch {
                if (-not (Test-Path $runtimeRoot -PathType Container)) {
                    New-Item -ItemType Directory -Path $runtimeRoot -Force | Out-Null
                }

                Copy-DirectoryContents -SourceRoot $backupRoot -DestinationRoot $runtimeRoot
                $restoredServerExePath = Join-Path $runtimeRoot 'Okno.Server.exe'
                if (-not (Test-Path $restoredServerExePath -PathType Leaf)) {
                    throw
                }
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
    if ($promoteCompleted -or $restoreCompleted) {
        Remove-DirectoryBestEffort -Path $backupRoot -Description "backup cleanup after $terminalState"
    }
}

[pscustomobject]@{
    runtimeRoot = $runtimeRoot
    serverExe = Join-Path $runtimeRoot 'Okno.Server.exe'
} | ConvertTo-Json -Compress
