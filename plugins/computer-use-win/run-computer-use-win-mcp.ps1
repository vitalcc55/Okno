$ErrorActionPreference = 'Stop'
if (Get-Variable -Name PSStyle -ErrorAction Ignore) {
    $PSStyle.OutputRendering = 'PlainText'
}

$env:COMPUTER_USE_WIN_PLUGIN_ROOT = $PSScriptRoot

Set-Location $PSScriptRoot

$runtimeRid = 'win-x64'
$runtimeRoot = Join-Path $PSScriptRoot "runtime\$runtimeRid"
$serverExeRelativePath = 'Okno.Server.exe'
$serverExePath = Join-Path $runtimeRoot $serverExeRelativePath
$runtimeManifestPath = Join-Path $runtimeRoot 'okno-runtime-bundle-manifest.json'
$descriptorOverridePath = $env:COMPUTER_USE_WIN_RUNTIME_RELEASE_DESCRIPTOR_OVERRIDE
$runtimeDescriptorPath = if ([string]::IsNullOrWhiteSpace($descriptorOverridePath)) {
    Join-Path $PSScriptRoot 'runtime-release.json'
}
else {
    [System.IO.Path]::GetFullPath($descriptorOverridePath)
}
$runtimeWorkingRoot = Join-Path $PSScriptRoot 'runtime'
$resolutionLockPath = Join-Path $runtimeWorkingRoot "$runtimeRid.resolve.lock"

function Read-RuntimeReleaseDescriptor {
    param(
        [Parameter(Mandatory)]
        [string] $DescriptorPath
    )

    if (-not (Test-Path $DescriptorPath -PathType Leaf)) {
        throw "Runtime release descriptor not found: $DescriptorPath"
    }

    $descriptor = Get-Content -Path $DescriptorPath -Raw | ConvertFrom-Json
    if ($descriptor.formatVersion -ne 1) {
        throw "Unsupported runtime release descriptor version '$($descriptor.formatVersion)'."
    }

    foreach ($propertyName in @('version', 'rid', 'tag', 'assetName', 'downloadUrl', 'sha256', 'serverExeRelativePath', 'bundleManifestName')) {
        if (-not $descriptor.PSObject.Properties.Name.Contains($propertyName) -or [string]::IsNullOrWhiteSpace([string]$descriptor.$propertyName)) {
            throw "Runtime release descriptor '$DescriptorPath' is missing required field '$propertyName'."
        }
    }

    if ([string]$descriptor.rid -ne $runtimeRid) {
        throw "Runtime release descriptor '$DescriptorPath' expects RID '$($descriptor.rid)', but this launcher supports '$runtimeRid'."
    }

    if ([string]$descriptor.sha256 -eq 'REPLACE_ON_RELEASE') {
        throw "Runtime release descriptor '$DescriptorPath' is not finalized yet. Replace the placeholder SHA256 before relying on release-backed runtime resolution."
    }

    return $descriptor
}

function Assert-RuntimeBundleMatchesManifest {
    param(
        [Parameter(Mandatory)]
        [string] $RootPath,
        [Parameter(Mandatory)]
        [string] $ManifestPath
    )

    if (-not (Test-Path $ManifestPath -PathType Leaf)) {
        throw "Runtime bundle manifest not found: $ManifestPath"
    }

    $manifest = Get-Content -Path $ManifestPath -Raw | ConvertFrom-Json
    if ($manifest.formatVersion -ne 1) {
        throw "Unsupported runtime bundle manifest version '$($manifest.formatVersion)'."
    }

    $expectedMap = New-Object 'System.Collections.Generic.Dictionary[string,long]' ([System.StringComparer]::Ordinal)
    foreach ($entry in @($manifest.files)) {
        $expectedMap[[string]$entry.path] = [int64]$entry.size
    }

    $normalizedRootPath = [System.IO.Path]::GetFullPath($RootPath).TrimEnd('\')
    $actualFiles = Get-ChildItem -LiteralPath $RootPath -Recurse -File |
        Where-Object { -not [string]::Equals($_.FullName, [System.IO.Path]::GetFullPath($ManifestPath), [System.StringComparison]::OrdinalIgnoreCase) } |
        Sort-Object FullName

    foreach ($file in $actualFiles) {
        $fullPath = [System.IO.Path]::GetFullPath($file.FullName)
        $relativePath = $fullPath.Substring($normalizedRootPath.Length).TrimStart('\')
        if (-not $expectedMap.ContainsKey($relativePath)) {
            throw "Runtime bundle contains unexpected file '$relativePath'."
        }

        if ([int64]$file.Length -ne $expectedMap[$relativePath]) {
            throw "Runtime bundle contains size drift for '$relativePath'."
        }

        $null = $expectedMap.Remove($relativePath)
    }

    if ($expectedMap.Count -gt 0) {
        throw "Runtime bundle is incomplete. Missing: $($expectedMap.Keys -join ', ')."
    }
}

function Test-RuntimeBundleIsUsable {
    if (-not (Test-Path $serverExePath -PathType Leaf)) {
        return $false
    }

    try {
        Assert-RuntimeBundleMatchesManifest -RootPath $runtimeRoot -ManifestPath $runtimeManifestPath
        return $true
    }
    catch {
        return $false
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

function Remove-DirectoryIfExists {
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    if (Test-Path $Path -PathType Container) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
}

function Acquire-ResolutionLock {
    param(
        [Parameter(Mandatory)]
        [string] $LockPath
    )

    New-Item -ItemType Directory -Path (Split-Path -Parent $LockPath) -Force | Out-Null
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    while ($true) {
        try {
            return [System.IO.File]::Open($LockPath, [System.IO.FileMode]::OpenOrCreate, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
        }
        catch {
            if ($stopwatch.Elapsed -gt [TimeSpan]::FromSeconds(30)) {
                throw "Failed to acquire runtime resolution lock: $LockPath"
            }

            Start-Sleep -Milliseconds 250
        }
    }
}

function Save-RemoteAssetToPath {
    param(
        [Parameter(Mandatory)]
        [string] $SourceUrl,
        [Parameter(Mandatory)]
        [string] $DestinationPath
    )

    $uri = [Uri]$SourceUrl
    if ($uri.IsFile) {
        Copy-Item -LiteralPath $uri.LocalPath -Destination $DestinationPath -Force
        return
    }

    if ($uri.Scheme -ne 'https' -and $uri.Scheme -ne 'http') {
        throw "Unsupported downloadUrl scheme '$($uri.Scheme)'."
    }

    Invoke-WebRequest -Uri $SourceUrl -OutFile $DestinationPath
}

function Resolve-RuntimeFromPinnedRelease {
    param(
        [Parameter(Mandatory)]
        [pscustomobject] $Descriptor
    )

    $assetName = [string]$Descriptor.assetName
    $assetExtension = [System.IO.Path]::GetExtension($assetName)
    if (-not [string]::Equals($assetExtension, '.zip', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Runtime release asset '$assetName' must be a .zip archive."
    }

    $downloadFileName = [System.IO.Path]::GetFileNameWithoutExtension($assetName) + '.download' + $assetExtension
    $zipPath = Join-Path $runtimeWorkingRoot $downloadFileName
    $stagingRoot = Join-Path $runtimeWorkingRoot ($Descriptor.rid + '.resolve-' + [Guid]::NewGuid().ToString('N'))
    $resolvedServerExePath = Join-Path $stagingRoot ([string]$Descriptor.serverExeRelativePath)
    $resolvedManifestPath = Join-Path $stagingRoot ([string]$Descriptor.bundleManifestName)

    if (Test-Path $zipPath -PathType Leaf) {
        Remove-Item -LiteralPath $zipPath -Force
    }

    Remove-DirectoryIfExists -Path $stagingRoot
    New-Item -ItemType Directory -Path $runtimeWorkingRoot -Force | Out-Null

    try {
        Save-RemoteAssetToPath -SourceUrl ([string]$Descriptor.downloadUrl) -DestinationPath $zipPath
        $actualSha256 = Get-FileSha256 -Path $zipPath
        if (-not [string]::Equals($actualSha256, ([string]$Descriptor.sha256).ToLowerInvariant(), [System.StringComparison]::Ordinal)) {
            throw "SHA256 mismatch for runtime release asset '$($Descriptor.assetName)'. Expected '$($Descriptor.sha256)', actual '$actualSha256'."
        }

        Expand-Archive -LiteralPath $zipPath -DestinationPath $stagingRoot -Force
        if (-not (Test-Path $resolvedServerExePath -PathType Leaf)) {
            throw "Runtime release asset '$($Descriptor.assetName)' does not contain server executable '$($Descriptor.serverExeRelativePath)'."
        }

        Assert-RuntimeBundleMatchesManifest -RootPath $stagingRoot -ManifestPath $resolvedManifestPath

        if (Test-Path $runtimeRoot -PathType Container) {
            Remove-DirectoryIfExists -Path $runtimeRoot
        }

        Move-Item -LiteralPath $stagingRoot -Destination $runtimeRoot
    }
    finally {
        if (Test-Path $zipPath -PathType Leaf) {
            Remove-Item -LiteralPath $zipPath -Force
        }

        if (Test-Path $stagingRoot -PathType Container) {
            Remove-DirectoryIfExists -Path $stagingRoot
        }
    }
}

if (-not (Test-RuntimeBundleIsUsable)) {
    $descriptor = Read-RuntimeReleaseDescriptor -DescriptorPath $runtimeDescriptorPath
    $lockStream = Acquire-ResolutionLock -LockPath $resolutionLockPath
    try {
        if (-not (Test-RuntimeBundleIsUsable)) {
            Resolve-RuntimeFromPinnedRelease -Descriptor $descriptor
        }
    }
    finally {
        $lockStream.Dispose()
    }
}

if (-not (Test-RuntimeBundleIsUsable)) {
    throw @"
Failed to prepare the runtime bundle for `computer-use-win`.

Expected apphost:
$serverExePath

Runtime descriptor used:
$runtimeDescriptorPath
"@
}

& $serverExePath --tool-surface-profile computer-use-win
