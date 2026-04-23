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

    $restoredBackup = $false
    try {
        if (Test-Path $runtimeRoot -PathType Container) {
            Move-Item -LiteralPath $runtimeRoot -Destination $backupRoot
        }

        if ($env:COMPUTER_USE_WIN_TEST_FAIL_AFTER_BACKUP -eq '1') {
            throw "Synthetic publish promote failure after backup handoff."
        }

        Move-Item -LiteralPath $swapRoot -Destination $runtimeRoot
        Remove-DirectoryIfExists -Path $backupRoot
    }
    catch {
        if (-not (Test-Path $runtimeRoot -PathType Container) -and (Test-Path $backupRoot -PathType Container)) {
            Move-Item -LiteralPath $backupRoot -Destination $runtimeRoot
            $restoredBackup = $true
        }

        if (-not $restoredBackup -and (Test-Path $backupRoot -PathType Container) -and -not (Test-Path $runtimeRoot -PathType Container)) {
            throw
        }

        throw
    }
}
finally {
    Remove-DirectoryIfExists -Path $stagingRoot
    Remove-DirectoryIfExists -Path $swapRoot
    Remove-DirectoryIfExists -Path $backupRoot
}

[pscustomobject]@{
    runtimeRoot = $runtimeRoot
    serverExe = Join-Path $runtimeRoot 'Okno.Server.exe'
} | ConvertTo-Json -Compress
