$ErrorActionPreference = 'Stop'
if (Get-Variable -Name PSStyle -ErrorAction Ignore) {
    $PSStyle.OutputRendering = 'PlainText'
}

$env:COMPUTER_USE_WIN_PLUGIN_ROOT = $PSScriptRoot

Set-Location $PSScriptRoot

$runtimeRid = 'win-x64'
$serverExeRelativePath = 'Okno.Server.exe'
$bundleManifestName = 'okno-runtime-bundle-manifest.json'
$pluginLocalRuntimeRoot = Join-Path $PSScriptRoot "runtime\$runtimeRid"
$pluginLocalServerExePath = Join-Path $pluginLocalRuntimeRoot $serverExeRelativePath
$pluginLocalRuntimeManifestPath = Join-Path $pluginLocalRuntimeRoot $bundleManifestName
$descriptorOverridePath = $env:COMPUTER_USE_WIN_RUNTIME_RELEASE_DESCRIPTOR_OVERRIDE
$runtimeDescriptorPath = if ([string]::IsNullOrWhiteSpace($descriptorOverridePath)) {
    Join-Path $PSScriptRoot 'runtime-release.json'
}
else {
    [System.IO.Path]::GetFullPath($descriptorOverridePath)
}

$localAppDataRoot = if (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
    [System.IO.Path]::GetFullPath($env:LOCALAPPDATA)
}
elseif (-not [string]::IsNullOrWhiteSpace($env:USERPROFILE)) {
    Join-Path ([System.IO.Path]::GetFullPath($env:USERPROFILE)) 'AppData\Local'
}
else {
    throw 'Neither LOCALAPPDATA nor USERPROFILE is available for shared runtime resolution.'
}

$sharedRuntimeStoreRoot = Join-Path $localAppDataRoot 'Okno\computer-use-win'
$sharedRuntimesRoot = Join-Path $sharedRuntimeStoreRoot 'runtimes'
$sharedStateRoot = Join-Path $sharedRuntimeStoreRoot 'state'
$sharedCurrentStatePath = Join-Path $sharedStateRoot 'current-runtime.json'
$sharedLocksRoot = Join-Path $sharedRuntimeStoreRoot 'locks'
$resolutionLockPath = Join-Path $sharedLocksRoot "$runtimeRid.install.lock"
$effectiveRuntimeRoot = $null

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

function Test-RuntimeBundleRootIsUsable {
    param(
        [Parameter(Mandatory)]
        [string] $RootPath,
        [Parameter(Mandatory)]
        [string] $ServerExeRelativePath,
        [Parameter(Mandatory)]
        [string] $ManifestName
    )

    $serverExePath = Join-Path $RootPath $ServerExeRelativePath
    if (-not (Test-Path $serverExePath -PathType Leaf)) {
        return $false
    }

    $manifestPath = Join-Path $RootPath $ManifestName
    try {
        Assert-RuntimeBundleMatchesManifest -RootPath $RootPath -ManifestPath $manifestPath
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

function Remove-FileIfExists {
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    if (Test-Path $Path -PathType Leaf) {
        Remove-Item -LiteralPath $Path -Force
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

function Get-SharedRuntimeVersionRoot {
    param(
        [Parameter(Mandatory)]
        [pscustomobject] $Descriptor
    )

    Join-Path (Join-Path $sharedRuntimesRoot ([string]$Descriptor.rid)) ([string]$Descriptor.version)
}

function Write-SharedRuntimeState {
    param(
        [Parameter(Mandatory)]
        [pscustomobject] $Descriptor,
        [Parameter(Mandatory)]
        [string] $RuntimeRoot
    )

    New-Item -ItemType Directory -Path $sharedStateRoot -Force | Out-Null
    $state = [pscustomobject]@{
        formatVersion = 1
        rid = [string]$Descriptor.rid
        version = [string]$Descriptor.version
        runtimeRoot = [System.IO.Path]::GetFullPath($RuntimeRoot)
        runtimeAssetName = [string]$Descriptor.assetName
        runtimeTag = [string]$Descriptor.tag
        runtimeSha256 = ([string]$Descriptor.sha256).ToLowerInvariant()
        installedAtUtc = [DateTime]::UtcNow.ToString('o')
    }

    $tempStatePath = $sharedCurrentStatePath + '.tmp-' + [Guid]::NewGuid().ToString('N')
    $state | ConvertTo-Json -Compress | Set-Content -Path $tempStatePath -Encoding UTF8
    if (Test-Path $sharedCurrentStatePath -PathType Leaf) {
        Remove-Item -LiteralPath $sharedCurrentStatePath -Force
    }

    Move-Item -LiteralPath $tempStatePath -Destination $sharedCurrentStatePath
}

function Get-UsableSharedRuntimeRoot {
    param(
        [Parameter(Mandatory)]
        [pscustomobject] $Descriptor
    )

    if (-not (Test-Path $sharedCurrentStatePath -PathType Leaf)) {
        return $null
    }

    try {
        $state = Get-Content -Path $sharedCurrentStatePath -Raw | ConvertFrom-Json
        if ($state.formatVersion -ne 1) {
            return $null
        }

        foreach ($propertyName in @('rid', 'version', 'runtimeRoot', 'runtimeAssetName', 'runtimeTag', 'runtimeSha256')) {
            if (-not $state.PSObject.Properties.Name.Contains($propertyName) -or [string]::IsNullOrWhiteSpace([string]$state.$propertyName)) {
                return $null
            }
        }

        if ([string]$state.rid -ne [string]$Descriptor.rid) {
            return $null
        }

        if ([string]$state.version -ne [string]$Descriptor.version) {
            return $null
        }

        if ([string]$state.runtimeTag -ne [string]$Descriptor.tag) {
            return $null
        }

        if ([string]$state.runtimeAssetName -ne [string]$Descriptor.assetName) {
            return $null
        }

        if (([string]$state.runtimeSha256).ToLowerInvariant() -ne ([string]$Descriptor.sha256).ToLowerInvariant()) {
            return $null
        }

        $runtimeRoot = [System.IO.Path]::GetFullPath([string]$state.runtimeRoot)
        $expectedRuntimeRoot = [System.IO.Path]::GetFullPath((Get-SharedRuntimeVersionRoot -Descriptor $Descriptor))
        if ($runtimeRoot -ne $expectedRuntimeRoot) {
            return $null
        }

        if (-not (Test-RuntimeBundleRootIsUsable -RootPath $runtimeRoot -ServerExeRelativePath ([string]$Descriptor.serverExeRelativePath) -ManifestName ([string]$Descriptor.bundleManifestName))) {
            return $null
        }

        return $runtimeRoot
    }
    catch {
        return $null
    }
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

    $ridRoot = Join-Path $sharedRuntimesRoot ([string]$Descriptor.rid)
    $versionRoot = Get-SharedRuntimeVersionRoot -Descriptor $Descriptor
    $downloadFileName = [System.IO.Path]::GetFileNameWithoutExtension($assetName) + '.download' + $assetExtension
    $zipPath = Join-Path $sharedLocksRoot $downloadFileName
    $stagingRoot = Join-Path $ridRoot (([string]$Descriptor.version) + '.install-' + [Guid]::NewGuid().ToString('N'))
    $resolvedServerExePath = Join-Path $stagingRoot ([string]$Descriptor.serverExeRelativePath)
    $resolvedManifestPath = Join-Path $stagingRoot ([string]$Descriptor.bundleManifestName)

    Remove-FileIfExists -Path $zipPath
    Remove-DirectoryIfExists -Path $stagingRoot
    New-Item -ItemType Directory -Path $sharedLocksRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $ridRoot -Force | Out-Null

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

        Remove-DirectoryIfExists -Path $versionRoot
        Move-Item -LiteralPath $stagingRoot -Destination $versionRoot
        Write-SharedRuntimeState -Descriptor $Descriptor -RuntimeRoot $versionRoot
        return [System.IO.Path]::GetFullPath($versionRoot)
    }
    finally {
        Remove-FileIfExists -Path $zipPath
        Remove-DirectoryIfExists -Path $stagingRoot
    }
}

$descriptor = Read-RuntimeReleaseDescriptor -DescriptorPath $runtimeDescriptorPath
$sharedRuntimeRoot = Get-UsableSharedRuntimeRoot -Descriptor $descriptor

if ($null -ne $sharedRuntimeRoot) {
    $effectiveRuntimeRoot = $sharedRuntimeRoot
}
elseif (Test-RuntimeBundleRootIsUsable -RootPath $pluginLocalRuntimeRoot -ServerExeRelativePath $serverExeRelativePath -ManifestName $bundleManifestName) {
    $effectiveRuntimeRoot = [System.IO.Path]::GetFullPath($pluginLocalRuntimeRoot)
}
else {
    $lockStream = Acquire-ResolutionLock -LockPath $resolutionLockPath
    try {
        $sharedRuntimeRoot = Get-UsableSharedRuntimeRoot -Descriptor $descriptor
        if ($null -eq $sharedRuntimeRoot) {
            $sharedRuntimeRoot = Resolve-RuntimeFromPinnedRelease -Descriptor $descriptor
        }

        $effectiveRuntimeRoot = $sharedRuntimeRoot
    }
    finally {
        $lockStream.Dispose()
    }
}

if ($null -eq $effectiveRuntimeRoot) {
    throw @"
Failed to prepare the runtime bundle for `computer-use-win`.

Plugin-local runtime root:
$pluginLocalRuntimeRoot

Shared runtime store root:
$sharedRuntimeStoreRoot

Runtime descriptor used:
$runtimeDescriptorPath
"@
}

$effectiveServerExePath = Join-Path $effectiveRuntimeRoot $serverExeRelativePath
if (-not (Test-Path $effectiveServerExePath -PathType Leaf)) {
    throw "Resolved runtime executable is missing: $effectiveServerExePath"
}

& $effectiveServerExePath --tool-surface-profile computer-use-win
