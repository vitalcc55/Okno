param(
    [ValidateSet('codex', 'runtime-only')]
    [string] $Mode = '',
    [string] $DescriptorPath = '',
    [string] $PayloadArchivePath = '',
    [string] $PayloadChecksumPath = '',
    [string] $PayloadRoot = '',
    [string] $Rid = 'win-x64',
    [switch] $UnsafeSkipIntegrityCheck,
    [switch] $Silent,
    [switch] $Json
)

$ErrorActionPreference = 'Stop'
if (Get-Variable -Name PSStyle -ErrorAction Ignore) {
    $PSStyle.OutputRendering = 'PlainText'
}

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

function Remove-DirectoryIfExists {
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    if (Test-Path $Path -PathType Container) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
}

function Get-FileSha256 {
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $sha256 = [System.Security.Cryptography.SHA256]::Create()
        try {
            return ([System.BitConverter]::ToString($sha256.ComputeHash($stream)) -replace '-', '').ToLowerInvariant()
        }
        finally {
            $sha256.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Resolve-Mode {
    param(
        [string] $CurrentMode,
        [switch] $SilentMode
    )

    if (-not [string]::IsNullOrWhiteSpace($CurrentMode)) {
        return $CurrentMode
    }

    if ($SilentMode) {
        return 'codex'
    }

    while ($true) {
        $selection = Read-Host 'Select install mode: 1 = Codex, 2 = Runtime only for MCP'
        switch ($selection) {
            '1' { return 'codex' }
            '2' { return 'runtime-only' }
            default { Write-Host 'Choose 1 or 2.' }
        }
    }
}

function Get-DescriptorObject {
    param(
        [string] $DescriptorPathOverride
    )

    $candidate = $DescriptorPathOverride
    if ([string]::IsNullOrWhiteSpace($candidate)) {
        $candidate = Join-Path $repoRoot 'plugins\computer-use-win\runtime-release.json'
    }

    $fullPath = [System.IO.Path]::GetFullPath($candidate)
    if (-not (Test-Path $fullPath -PathType Leaf)) {
        throw "Runtime release descriptor '$fullPath' is missing."
    }

    return (Get-Content $fullPath -Raw | ConvertFrom-Json)
}

function Resolve-PayloadInfo {
    param(
        [string] $DescriptorPathOverride,
        [string] $PayloadArchiveOverride,
        [string] $PayloadChecksumOverride,
        [switch] $AllowUnsafeSkipIntegrityCheck,
        [string] $TargetRid
    )

    if (-not [string]::IsNullOrWhiteSpace($PayloadArchiveOverride)) {
        $fullArchivePath = [System.IO.Path]::GetFullPath($PayloadArchiveOverride)
        if (-not (Test-Path $fullArchivePath -PathType Leaf)) {
            throw "Setup payload archive '$fullArchivePath' is missing."
        }

        $expectedSha256 = $null
        if (-not $AllowUnsafeSkipIntegrityCheck) {
            $assetName = [System.IO.Path]::GetFileName($fullArchivePath)
            $checksumPath = if (-not [string]::IsNullOrWhiteSpace($PayloadChecksumOverride)) {
                [System.IO.Path]::GetFullPath($PayloadChecksumOverride)
            }
            else {
                $expectedSuffix = "-$TargetRid.zip"
                if (-not $assetName.EndsWith($expectedSuffix, [System.StringComparison]::OrdinalIgnoreCase)) {
                    throw "Setup payload archive '$fullArchivePath' does not match the expected '$TargetRid' asset naming contract. Pass -PayloadChecksumPath explicitly or use -UnsafeSkipIntegrityCheck for a dev-only override."
                }

                $checksumName = $assetName.Substring(0, $assetName.Length - $expectedSuffix.Length) + '-SHA256SUMS.txt'
                Join-Path (Split-Path -Parent $fullArchivePath) $checksumName
            }

            if (-not (Test-Path $checksumPath -PathType Leaf)) {
                throw "Setup payload checksum '$checksumPath' is missing. Supply -PayloadChecksumPath or use -UnsafeSkipIntegrityCheck for a dev-only override."
            }

            $expectedSha256 = Get-ChecksumValue -ChecksumPath $checksumPath -AssetName $assetName
        }

        return [pscustomobject]@{
            ArchivePath = $fullArchivePath
            ArchiveUri = $null
            ExpectedSha256 = $expectedSha256
            AssetName = [System.IO.Path]::GetFileName($fullArchivePath)
        }
    }

    $descriptor = Get-DescriptorObject -DescriptorPathOverride $DescriptorPathOverride
    $assetName = "okno-setup-cli-payload-$($descriptor.version)-$TargetRid.zip"
    $checksumName = "okno-setup-cli-payload-$($descriptor.version)-SHA256SUMS.txt"
    $downloadUri = [Uri]$descriptor.downloadUrl

    if ($downloadUri.IsFile) {
        $releaseRoot = Split-Path -Parent $downloadUri.LocalPath
        $archivePath = Join-Path $releaseRoot $assetName
        $checksumPath = Join-Path $releaseRoot $checksumName
        if (-not (Test-Path $archivePath -PathType Leaf)) {
            throw "Setup payload archive '$archivePath' is missing next to runtime release asset."
        }

        if (-not (Test-Path $checksumPath -PathType Leaf)) {
            throw "Setup payload checksum '$checksumPath' is missing next to runtime release asset."
        }

        $expectedSha256 = Get-ChecksumValue -ChecksumPath $checksumPath -AssetName $assetName
        return [pscustomobject]@{
            ArchivePath = $archivePath
            ArchiveUri = $null
            ExpectedSha256 = $expectedSha256
            AssetName = $assetName
        }
    }

    $baseUri = [Uri]::new([Uri]::new($descriptor.downloadUrl), '.')
    $archiveUri = [Uri]::new($baseUri, $assetName)
    $checksumUri = [Uri]::new($baseUri, $checksumName)
    $checksumFile = Download-ToTempFile -SourceUri $checksumUri -FileName $checksumName
    try {
        $expectedSha256 = Get-ChecksumValue -ChecksumPath $checksumFile -AssetName $assetName
    }
    finally {
        if (Test-Path $checksumFile -PathType Leaf) {
            Remove-Item -LiteralPath $checksumFile -Force
        }
    }

    return [pscustomobject]@{
        ArchivePath = $null
        ArchiveUri = $archiveUri
        ExpectedSha256 = $expectedSha256
        AssetName = $assetName
    }
}

function Download-ToTempFile {
    param(
        [Parameter(Mandatory)]
        [Uri] $SourceUri,
        [Parameter(Mandatory)]
        [string] $FileName
    )

    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('okno-setup-bootstrap-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
    $destinationPath = Join-Path $tempRoot $FileName
    Invoke-WebRequest -Uri $SourceUri -OutFile $destinationPath
    return $destinationPath
}

function Get-ChecksumValue {
    param(
        [Parameter(Mandatory)]
        [string] $ChecksumPath,
        [Parameter(Mandatory)]
        [string] $AssetName
    )

    foreach ($rawLine in Get-Content $ChecksumPath) {
        $line = $rawLine.Trim()
        if ($line.EndsWith("*$AssetName", [System.StringComparison]::Ordinal)) {
            return ($line -split '\s+', 2)[0].Trim().ToLowerInvariant()
        }
    }

    throw "Checksum file '$ChecksumPath' does not contain '$AssetName'."
}

function Resolve-PayloadRoot {
    param(
        [string] $DescriptorPathOverride,
        [string] $PayloadArchiveOverride,
        [string] $PayloadChecksumOverride,
        [string] $PayloadRootOverride,
        [switch] $AllowUnsafeSkipIntegrityCheck,
        [string] $TargetRid
    )

    if (-not [string]::IsNullOrWhiteSpace($PayloadRootOverride)) {
        if (-not $AllowUnsafeSkipIntegrityCheck) {
            throw "Setup payload root override '$PayloadRootOverride' is a dev-only path. Pass -UnsafeSkipIntegrityCheck to use an unpacked payload root."
        }

        $fullPayloadRoot = [System.IO.Path]::GetFullPath($PayloadRootOverride)
        if (-not (Test-Path $fullPayloadRoot -PathType Container)) {
            throw "Setup payload root '$fullPayloadRoot' is missing."
        }

        return [pscustomobject]@{
            Root = $fullPayloadRoot
            TemporaryRoot = $null
        }
    }

    $payloadInfo = Resolve-PayloadInfo `
        -DescriptorPathOverride $DescriptorPathOverride `
        -PayloadArchiveOverride $PayloadArchiveOverride `
        -PayloadChecksumOverride $PayloadChecksumOverride `
        -AllowUnsafeSkipIntegrityCheck:$AllowUnsafeSkipIntegrityCheck `
        -TargetRid $TargetRid
    $archivePath = $payloadInfo.ArchivePath
    $temporaryArchiveRoot = $null
    if ($null -eq $archivePath) {
        $temporaryArchiveRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('okno-setup-bootstrap-download-' + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $temporaryArchiveRoot -Force | Out-Null
        $archivePath = Join-Path $temporaryArchiveRoot $payloadInfo.AssetName
        Invoke-WebRequest -Uri $payloadInfo.ArchiveUri -OutFile $archivePath
    }

    if ($null -ne $payloadInfo.ExpectedSha256) {
        $actualSha256 = Get-FileSha256 -Path $archivePath
        if ($actualSha256 -ne $payloadInfo.ExpectedSha256) {
            throw "Setup payload archive '$archivePath' failed SHA256 verification."
        }
    }

    $temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('okno-setup-bootstrap-extract-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $temporaryRoot -Force | Out-Null
    Expand-Archive -LiteralPath $archivePath -DestinationPath $temporaryRoot

    if ($null -ne $temporaryArchiveRoot) {
        Remove-DirectoryIfExists -Path $temporaryArchiveRoot
    }

    return [pscustomobject]@{
        Root = $temporaryRoot
        TemporaryRoot = $temporaryRoot
    }
}

$resolvedMode = Resolve-Mode -CurrentMode $Mode -SilentMode:$Silent
$payload = Resolve-PayloadRoot `
    -DescriptorPathOverride $DescriptorPath `
    -PayloadArchiveOverride $PayloadArchivePath `
    -PayloadChecksumOverride $PayloadChecksumPath `
    -PayloadRootOverride $PayloadRoot `
    -AllowUnsafeSkipIntegrityCheck:$UnsafeSkipIntegrityCheck `
    -TargetRid $Rid

try {
    $setupExePath = Join-Path $payload.Root 'WinBridge.Setup.Cli.exe'
    $setupDllPath = Join-Path $payload.Root 'WinBridge.Setup.Cli.dll'
    $command = $null
    $setupArguments = @()
    if (Test-Path $setupExePath -PathType Leaf) {
        $command = $setupExePath
    }
    elseif (Test-Path $setupDllPath -PathType Leaf) {
        $command = 'dotnet'
        $setupArguments += $setupDllPath
    }
    else {
        throw "Setup payload '$($payload.Root)' does not contain WinBridge.Setup.Cli executable."
    }

    $setupArguments += 'install'
    $setupArguments += $resolvedMode
    if (-not [string]::IsNullOrWhiteSpace($DescriptorPath)) {
        $setupArguments += '--descriptor-path'
        $setupArguments += [System.IO.Path]::GetFullPath($DescriptorPath)
    }
    $setupArguments += '--json'

    Push-Location $payload.Root
    try {
        $setupOutput = & $command @setupArguments 2>&1 | Out-String
        $setupOutput = $setupOutput.Trim()
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    if ($exitCode -ne 0) {
        throw "Setup CLI failed. output='$setupOutput'"
    }

    if ($Json) {
        Write-Output $setupOutput
    }
    else {
        $result = $setupOutput | ConvertFrom-Json
        if ($resolvedMode -eq 'codex') {
            Write-Host "Codex installation completed. Runtime: $($result.runtimeRoot)"
            Write-Host "Plugin: $($result.pluginSourceRoot)"
            Write-Host 'Restart Codex to load the installed plugin.'
        }
        else {
            Write-Host "Runtime installed: $($result.runtimeRoot)"
            if (-not [string]::IsNullOrWhiteSpace($result.snippet)) {
                Write-Host ''
                Write-Host 'Ready MCP snippet:'
                Write-Host $result.snippet
            }
        }
    }
}
finally {
    if ($null -ne $payload.TemporaryRoot) {
        Remove-DirectoryIfExists -Path $payload.TemporaryRoot
    }
}
