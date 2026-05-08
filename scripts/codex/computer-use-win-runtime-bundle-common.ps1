$script:ComputerUseWinRuntimeBundleManifestFileName = 'okno-runtime-bundle-manifest.json'
$script:ComputerUseWinRuntimeServerExeRelativePath = 'Okno.Server.exe'
$script:ComputerUseWinFirstWaveRuntimeRid = 'win-x64'

function Get-ComputerUseWinRuntimeBundleManifestPath {
    param(
        [Parameter(Mandatory)]
        [string] $RootPath
    )

    Join-Path $RootPath $script:ComputerUseWinRuntimeBundleManifestFileName
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

function New-ComputerUseWinRuntimeReleaseDescriptor {
    param(
        [Parameter(Mandatory)]
        [string] $Version,
        [Parameter(Mandatory)]
        [string] $Rid,
        [Parameter(Mandatory)]
        [string] $AssetName,
        [Parameter(Mandatory)]
        [string] $DownloadUrl,
        [Parameter(Mandatory)]
        [string] $Sha256
    )

    [pscustomobject]@{
        formatVersion = 1
        version = $Version
        rid = $Rid
        tag = "v$Version"
        assetName = $AssetName
        downloadUrl = $DownloadUrl
        sha256 = $Sha256
        serverExeRelativePath = $script:ComputerUseWinRuntimeServerExeRelativePath
        bundleManifestName = $script:ComputerUseWinRuntimeBundleManifestFileName
    }
}

function Write-ComputerUseWinRuntimeReleaseDescriptor {
    param(
        [Parameter(Mandatory)]
        [string] $DescriptorPath,
        [Parameter(Mandatory)]
        [string] $Version,
        [Parameter(Mandatory)]
        [string] $Rid,
        [Parameter(Mandatory)]
        [string] $AssetName,
        [Parameter(Mandatory)]
        [string] $DownloadUrl,
        [Parameter(Mandatory)]
        [string] $Sha256
    )

    $descriptorDirectory = Split-Path -Parent $DescriptorPath
    if (-not [string]::IsNullOrWhiteSpace($descriptorDirectory)) {
        New-Item -ItemType Directory -Path $descriptorDirectory -Force | Out-Null
    }

    New-ComputerUseWinRuntimeReleaseDescriptor `
        -Version $Version `
        -Rid $Rid `
        -AssetName $AssetName `
        -DownloadUrl $DownloadUrl `
        -Sha256 $Sha256 |
        ConvertTo-Json -Depth 6 |
        Set-Content -Path $DescriptorPath -Encoding UTF8
}

function Read-ComputerUseWinRuntimeReleaseDescriptor {
    param(
        [Parameter(Mandatory)]
        [string] $DescriptorPath
    )

    $resolvedDescriptorPath = [System.IO.Path]::GetFullPath($DescriptorPath)
    if (-not (Test-Path $resolvedDescriptorPath -PathType Leaf)) {
        throw "Runtime release descriptor '$resolvedDescriptorPath' is missing."
    }

    $descriptor = Get-Content -Path $resolvedDescriptorPath -Raw | ConvertFrom-Json
    if ($null -eq $descriptor) {
        throw "Runtime release descriptor '$resolvedDescriptorPath' is empty."
    }

    return $descriptor
}

function Read-ComputerUseWinRuntimePackagingResult {
    param(
        [Parameter(Mandatory)]
        [string] $ResultPath
    )

    $resolvedResultPath = [System.IO.Path]::GetFullPath($ResultPath)
    if (-not (Test-Path $resolvedResultPath -PathType Leaf)) {
        throw "Runtime packaging result '$resolvedResultPath' is missing."
    }

    $result = Get-Content -Path $resolvedResultPath -Raw | ConvertFrom-Json
    if ($null -eq $result) {
        throw "Runtime packaging result '$resolvedResultPath' is empty."
    }

    return $result
}

function Get-ComputerUseWinRuntimeAssetSha256 {
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

function Get-ComputerUseWinChecksumFileEntry {
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

    throw "Checksum file '$ChecksumPath' does not contain an entry for '$AssetName'."
}

function Get-ComputerUseWinSupportedPackagingRid {
    return $script:ComputerUseWinFirstWaveRuntimeRid
}

function Get-ComputerUseWinNuGetPackageRoot {
    if (-not [string]::IsNullOrWhiteSpace($env:NUGET_PACKAGES)) {
        return [System.IO.Path]::GetFullPath($env:NUGET_PACKAGES)
    }

    if ([string]::IsNullOrWhiteSpace($env:USERPROFILE)) {
        throw 'Unable to resolve NuGet package cache because neither NUGET_PACKAGES nor USERPROFILE is set.'
    }

    return [System.IO.Path]::GetFullPath((Join-Path $env:USERPROFILE '.nuget\packages'))
}

function Get-ComputerUseWinDotNetRoot {
    if (-not [string]::IsNullOrWhiteSpace($env:DOTNET_ROOT)) {
        return [System.IO.Path]::GetFullPath($env:DOTNET_ROOT)
    }

    $dotnetCommand = Get-Command dotnet -ErrorAction Stop
    return [System.IO.Path]::GetFullPath((Split-Path -Parent $dotnetCommand.Source))
}

function Normalize-ComputerUseWinFileVersion {
    param(
        [AllowEmptyString()]
        [string] $FileVersion
    )

    if ([string]::IsNullOrWhiteSpace($FileVersion)) {
        return ''
    }

    $match = [System.Text.RegularExpressions.Regex]::Match($FileVersion, '\d+(?:[.,]\d+){1,3}')
    if (-not $match.Success) {
        return $FileVersion.Trim()
    }

    return ($match.Value -replace ',', '.').Trim()
}

function Get-ComputerUseWinDepsPackageAssetEntries {
    param(
        [Parameter(Mandatory)]
        [string] $DepsJsonPath
    )

    $deps = Get-Content -Path $DepsJsonPath -Raw | ConvertFrom-Json
    if ($null -eq $deps) {
        throw "deps.json '$DepsJsonPath' is empty."
    }

    $target = $deps.targets.PSObject.Properties |
        Where-Object { @($_.Value.PSObject.Properties).Count -gt 0 } |
        Select-Object -First 1
    if ($null -eq $target) {
        throw "deps.json '$DepsJsonPath' does not define any targets."
    }

    $entries = New-Object 'System.Collections.Generic.List[object]'
    foreach ($library in $target.Value.PSObject.Properties) {
        $libraryMetadata = $deps.libraries.PSObject.Properties |
            Where-Object { [string]::Equals([string]$_.Name, [string]$library.Name, [System.StringComparison]::Ordinal) } |
            Select-Object -First 1
        $libraryType = [string]$libraryMetadata.Value.type
        if ($libraryType -ne 'package' -and $libraryType -ne 'runtimepack') {
            continue
        }

        $packagePath = [string]$libraryMetadata.Value.path
        if ($libraryType -eq 'package' -and [string]::IsNullOrWhiteSpace($packagePath)) {
            continue
        }

        foreach ($assetGroupName in @('runtime', 'native')) {
            $assetGroup = $library.Value.$assetGroupName
            if ($null -eq $assetGroup) {
                continue
            }

            foreach ($asset in $assetGroup.PSObject.Properties) {
                $assetPath = [string]$asset.Name
                $fileVersion = [string]$asset.Value.fileVersion

                $entries.Add([pscustomobject]@{
                    libraryName = [string]$library.Name
                    libraryType = $libraryType
                    packagePath = $packagePath
                    assetKind = $assetGroupName
                    assetPath = $assetPath
                    fileName = [System.IO.Path]::GetFileName($assetPath)
                    fileVersion = $fileVersion
                }) | Out-Null
            }
        }
    }

    return $entries.ToArray()
}

function Resolve-ComputerUseWinDepsAssetSourcePath {
    param(
        [Parameter(Mandatory)]
        [object] $Entry
    )

    if ([string]$Entry.libraryType -eq 'package') {
        $nugetRoot = Get-ComputerUseWinNuGetPackageRoot
        return Join-Path (Join-Path $nugetRoot $Entry.packagePath) $Entry.assetPath
    }

    if ([string]$Entry.libraryType -eq 'runtimepack') {
        $runtimeRuntimepackMatch = [System.Text.RegularExpressions.Regex]::Match(
            [string]$Entry.libraryName,
            '^runtimepack\.(?<framework>.+?)\.Runtime\.(?<rid>[^/]+)/(?<version>[^/]+)$')
        if ($runtimeRuntimepackMatch.Success) {
            $dotnetRoot = Get-ComputerUseWinDotNetRoot
            $frameworkName = $runtimeRuntimepackMatch.Groups['framework'].Value
            $rid = $runtimeRuntimepackMatch.Groups['rid'].Value
            $frameworkVersion = $runtimeRuntimepackMatch.Groups['version'].Value
            $candidates = @(
                (Join-Path (Join-Path (Join-Path $dotnetRoot 'shared') $frameworkName) (Join-Path $frameworkVersion $Entry.fileName)),
                (Join-Path (Join-Path (Join-Path $dotnetRoot 'host\fxr') $frameworkVersion) $Entry.fileName),
                (Join-Path (Join-Path (Join-Path (Join-Path $dotnetRoot 'packs') "$frameworkName.Host.$rid") $frameworkVersion) (Join-Path "runtimes\$rid\native" $Entry.fileName))
            )

            foreach ($candidate in $candidates) {
                if (Test-Path $candidate -PathType Leaf) {
                    return $candidate
                }
            }

            return $candidates[0]
        }

        $refRuntimepackMatch = [System.Text.RegularExpressions.Regex]::Match(
            [string]$Entry.libraryName,
            '^runtimepack\.(?<package>.+?\.Ref)/(?<version>[^/]+)$')
        if ($refRuntimepackMatch.Success) {
            $nugetRoot = Get-ComputerUseWinNuGetPackageRoot
            $packageName = $refRuntimepackMatch.Groups['package'].Value.ToLowerInvariant()
            $packageVersion = $refRuntimepackMatch.Groups['version'].Value
            $packageRoot = Join-Path (Join-Path $nugetRoot $packageName) $packageVersion
            if (-not (Test-Path $packageRoot -PathType Container)) {
                return Join-Path $packageRoot $Entry.fileName
            }

            $match = Get-ChildItem -LiteralPath $packageRoot -Recurse -File |
                Where-Object { [string]::Equals($_.Name, [string]$Entry.fileName, [System.StringComparison]::OrdinalIgnoreCase) } |
                Select-Object -First 1
            if ($null -ne $match) {
                return $match.FullName
            }

            return Join-Path $packageRoot $Entry.fileName
        }

        throw "Runtime bundle asset '$($Entry.fileName)' comes from unsupported runtimepack identity '$($Entry.libraryName)'."
    }

    throw "Runtime bundle asset '$($Entry.fileName)' uses unsupported library type '$($Entry.libraryType)'."
}

function Repair-ComputerUseWinRuntimeBundleDepsAssets {
    param(
        [Parameter(Mandatory)]
        [string] $RootPath,
        [Parameter(Mandatory)]
        [string] $DepsJsonPath
    )

    foreach ($entry in Get-ComputerUseWinDepsPackageAssetEntries -DepsJsonPath $DepsJsonPath) {
        $destinationPath = Join-Path $RootPath $entry.fileName
        $needsPackageAsset = -not (Test-Path $destinationPath -PathType Leaf)
        $sourcePath = Resolve-ComputerUseWinDepsAssetSourcePath -Entry $entry
        if (-not (Test-Path $sourcePath -PathType Leaf)) {
            throw "Runtime bundle '$RootPath' requires $($entry.libraryType) $($entry.assetKind) asset '$($entry.assetPath)' from '$($entry.libraryName)', but '$sourcePath' is missing."
        }

        if (-not $needsPackageAsset) {
            $actualSha256 = Get-ComputerUseWinRuntimeAssetSha256 -Path $destinationPath
            $expectedSha256 = Get-ComputerUseWinRuntimeAssetSha256 -Path $sourcePath
            $needsPackageAsset = $actualSha256 -ne $expectedSha256
        }

        if (-not $needsPackageAsset) {
            continue
        }

        Copy-Item -LiteralPath $sourcePath -Destination $destinationPath -Force
    }
}

function Assert-ComputerUseWinRuntimeBundleMatchesDepsJson {
    param(
        [Parameter(Mandatory)]
        [string] $RootPath,
        [Parameter(Mandatory)]
        [string] $Description
    )

    $depsJsonPath = Join-Path $RootPath 'Okno.Server.deps.json'
    if (-not (Test-Path $depsJsonPath -PathType Leaf)) {
        throw "$Description is missing 'Okno.Server.deps.json'."
    }

    foreach ($entry in Get-ComputerUseWinDepsPackageAssetEntries -DepsJsonPath $depsJsonPath) {
        $destinationPath = Join-Path $RootPath $entry.fileName
        if (-not (Test-Path $destinationPath -PathType Leaf)) {
            throw "$Description is missing package $($entry.assetKind) asset '$($entry.fileName)' required by '$($entry.libraryName)'."
        }

        $sourcePath = Resolve-ComputerUseWinDepsAssetSourcePath -Entry $entry
        if (-not (Test-Path $sourcePath -PathType Leaf)) {
            throw "$Description cannot prove '$($entry.fileName)' because source asset '$sourcePath' is missing for '$($entry.libraryName)'."
        }

        $actualSha256 = Get-ComputerUseWinRuntimeAssetSha256 -Path $destinationPath
        $expectedSha256 = Get-ComputerUseWinRuntimeAssetSha256 -Path $sourcePath
        if ($actualSha256 -ne $expectedSha256) {
            throw "$Description contains '$($entry.fileName)' that does not match expected $($entry.libraryType) $($entry.assetKind) asset proof from '$($entry.libraryName)'."
        }
    }
}

function Assert-ComputerUseWinRuntimeDescriptorMatchesPackagingArguments {
    param(
        [Parameter(Mandatory)]
        [string] $DescriptorPath,
        [Parameter(Mandatory)]
        [string] $Version,
        [Parameter(Mandatory)]
        [string] $Rid,
        [string] $ExpectedAssetName = '',
        [string] $ExpectedDownloadUrl = '',
        [string] $ExpectedSha256 = '',
        [string] $ExpectedServerExeRelativePath = $script:ComputerUseWinRuntimeServerExeRelativePath,
        [string] $ExpectedBundleManifestName = $script:ComputerUseWinRuntimeBundleManifestFileName
    )

    $resolvedDescriptorPath = [System.IO.Path]::GetFullPath($DescriptorPath)
    $descriptor = Read-ComputerUseWinRuntimeReleaseDescriptor -DescriptorPath $resolvedDescriptorPath

    $expectedTag = "v$Version"
    if ([string]::IsNullOrWhiteSpace($ExpectedAssetName)) {
        $ExpectedAssetName = "okno-computer-use-win-runtime-$Version-$Rid.zip"
    }

    if ([int]$descriptor.formatVersion -ne 1) {
        throw "Runtime release descriptor '$resolvedDescriptorPath' uses unsupported formatVersion '$($descriptor.formatVersion)'."
    }

    $hasMissingRequiredField =
        [string]::IsNullOrWhiteSpace([string]$descriptor.version) `
        -or [string]::IsNullOrWhiteSpace([string]$descriptor.rid) `
        -or [string]::IsNullOrWhiteSpace([string]$descriptor.tag) `
        -or [string]::IsNullOrWhiteSpace([string]$descriptor.assetName) `
        -or [string]::IsNullOrWhiteSpace([string]$descriptor.downloadUrl) `
        -or [string]::IsNullOrWhiteSpace([string]$descriptor.sha256) `
        -or [string]::IsNullOrWhiteSpace([string]$descriptor.serverExeRelativePath) `
        -or [string]::IsNullOrWhiteSpace([string]$descriptor.bundleManifestName)
    if ($hasMissingRequiredField) {
        throw "Runtime release descriptor '$resolvedDescriptorPath' is missing one or more required fields."
    }

    if ([string]$descriptor.version -ne $Version) {
        throw "Runtime release descriptor '$resolvedDescriptorPath' version '$($descriptor.version)' does not match packaging version '$Version'."
    }

    if ([string]$descriptor.rid -ne $Rid) {
        throw "Runtime release descriptor '$resolvedDescriptorPath' RID '$($descriptor.rid)' does not match packaging RID '$Rid'."
    }

    if ([string]$descriptor.tag -ne $expectedTag) {
        throw "Runtime release descriptor '$resolvedDescriptorPath' tag '$($descriptor.tag)' does not match expected '$expectedTag'."
    }

    if ([string]$descriptor.assetName -ne $ExpectedAssetName) {
        throw "Runtime release descriptor '$resolvedDescriptorPath' assetName '$($descriptor.assetName)' does not match expected '$ExpectedAssetName'."
    }

    $hasUnexpectedDownloadUrl = -not [string]::IsNullOrWhiteSpace($ExpectedDownloadUrl) `
        -and [string]$descriptor.downloadUrl -ne $ExpectedDownloadUrl
    if ($hasUnexpectedDownloadUrl) {
        throw "Runtime release descriptor '$resolvedDescriptorPath' downloadUrl '$($descriptor.downloadUrl)' does not match expected '$ExpectedDownloadUrl'."
    }

    $hasUnexpectedSha256 = -not [string]::IsNullOrWhiteSpace($ExpectedSha256) `
        -and [string]$descriptor.sha256 -ne $ExpectedSha256
    if ($hasUnexpectedSha256) {
        throw "Runtime release descriptor '$resolvedDescriptorPath' sha256 '$($descriptor.sha256)' does not match expected '$ExpectedSha256'."
    }

    if ([string]$descriptor.serverExeRelativePath -ne $ExpectedServerExeRelativePath) {
        throw "Runtime release descriptor '$resolvedDescriptorPath' serverExeRelativePath '$($descriptor.serverExeRelativePath)' does not match expected '$ExpectedServerExeRelativePath'."
    }

    if ([string]$descriptor.bundleManifestName -ne $ExpectedBundleManifestName) {
        throw "Runtime release descriptor '$resolvedDescriptorPath' bundleManifestName '$($descriptor.bundleManifestName)' does not match expected '$ExpectedBundleManifestName'."
    }

    if (-not ([string]$descriptor.sha256 -match '^[0-9a-f]{64}$')) {
        throw "Runtime release descriptor '$resolvedDescriptorPath' sha256 must be a 64-character lowercase hex string."
    }

    try {
        $descriptorUri = [Uri]::new([string]$descriptor.downloadUrl)
        if (-not $descriptorUri.IsAbsoluteUri) {
            throw [System.InvalidOperationException]::new("downloadUrl must be absolute.")
        }
    }
    catch {
        throw "Runtime release descriptor '$resolvedDescriptorPath' has invalid downloadUrl '$($descriptor.downloadUrl)'."
    }
}

function Resolve-ComputerUseWinRuntimePackagingDescriptorPath {
    param(
        [Parameter(Mandatory)]
        [string] $RuntimePackagingResultPath,
        [Parameter(Mandatory)]
        [string] $ExpectedVersion,
        [Parameter(Mandatory)]
        [string] $ExpectedRid
    )

    $resolvedResultPath = [System.IO.Path]::GetFullPath($RuntimePackagingResultPath)
    $result = Read-ComputerUseWinRuntimePackagingResult -ResultPath $resolvedResultPath

    $requiredFields = @(
        'version',
        'rid',
        'tag',
        'assetName',
        'archivePath',
        'checksumPath',
        'descriptorPath',
        'downloadUrl',
        'sha256')
    foreach ($field in $requiredFields) {
        if ([string]::IsNullOrWhiteSpace([string]$result.$field)) {
            throw "Runtime packaging result '$resolvedResultPath' is missing required field '$field'."
        }
    }

    if ([string]$result.version -ne $ExpectedVersion) {
        throw "Runtime packaging result '$resolvedResultPath' version '$($result.version)' does not match expected '$ExpectedVersion'."
    }

    if ([string]$result.rid -ne $ExpectedRid) {
        throw "Runtime packaging result '$resolvedResultPath' RID '$($result.rid)' does not match expected '$ExpectedRid'."
    }

    if ([string]$result.tag -ne "v$ExpectedVersion") {
        throw "Runtime packaging result '$resolvedResultPath' tag '$($result.tag)' does not match expected 'v$ExpectedVersion'."
    }

    if ([string]$result.assetName -ne "okno-computer-use-win-runtime-$ExpectedVersion-$ExpectedRid.zip") {
        throw "Runtime packaging result '$resolvedResultPath' assetName '$($result.assetName)' does not match expected runtime release contract."
    }

    $resolvedArchivePath = [System.IO.Path]::GetFullPath([string]$result.archivePath)
    $resolvedChecksumPath = [System.IO.Path]::GetFullPath([string]$result.checksumPath)
    $resolvedDescriptorPath = [System.IO.Path]::GetFullPath([string]$result.descriptorPath)

    if (-not (Test-Path $resolvedArchivePath -PathType Leaf)) {
        throw "Runtime packaging result '$resolvedResultPath' references missing archive '$resolvedArchivePath'."
    }

    if (-not (Test-Path $resolvedChecksumPath -PathType Leaf)) {
        throw "Runtime packaging result '$resolvedResultPath' references missing checksum file '$resolvedChecksumPath'."
    }

    if (-not (Test-Path $resolvedDescriptorPath -PathType Leaf)) {
        throw "Runtime packaging result '$resolvedResultPath' references missing descriptor '$resolvedDescriptorPath'."
    }

    if ([System.IO.Path]::GetFileName($resolvedArchivePath) -ne [string]$result.assetName) {
        throw "Runtime packaging result '$resolvedResultPath' archivePath '$resolvedArchivePath' does not match assetName '$($result.assetName)'."
    }

    $checksumSha256 = Get-ComputerUseWinChecksumFileEntry -ChecksumPath $resolvedChecksumPath -AssetName ([string]$result.assetName)
    if ($checksumSha256 -ne ([string]$result.sha256).ToLowerInvariant()) {
        throw "Runtime packaging result '$resolvedResultPath' checksum file proof does not match declared sha256 '$($result.sha256)'."
    }

    $archiveSha256 = Get-ComputerUseWinRuntimeAssetSha256 -Path $resolvedArchivePath
    if ($archiveSha256 -ne ([string]$result.sha256).ToLowerInvariant()) {
        throw "Runtime packaging result '$resolvedResultPath' archive proof does not match declared sha256 '$($result.sha256)'."
    }

    Assert-ComputerUseWinRuntimeDescriptorMatchesPackagingArguments `
        -DescriptorPath $resolvedDescriptorPath `
        -Version $ExpectedVersion `
        -Rid $ExpectedRid `
        -ExpectedAssetName ([string]$result.assetName) `
        -ExpectedDownloadUrl ([string]$result.downloadUrl) `
        -ExpectedSha256 ([string]$result.sha256)

    return $resolvedDescriptorPath
}

function New-ComputerUseWinRuntimeBundleManifest {
    param(
        [Parameter(Mandatory)]
        [string] $RootPath
    )

    $normalizedRootPath = [System.IO.Path]::GetFullPath($RootPath).TrimEnd('\')
    $files = Get-ChildItem -LiteralPath $RootPath -Recurse -File |
        Where-Object { -not [string]::Equals($_.Name, $script:ComputerUseWinRuntimeBundleManifestFileName, [System.StringComparison]::OrdinalIgnoreCase) } |
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

function Write-ComputerUseWinRuntimeBundleManifest {
    param(
        [Parameter(Mandatory)]
        [string] $RootPath
    )

    $manifest = New-ComputerUseWinRuntimeBundleManifest -RootPath $RootPath
    $manifestPath = Get-ComputerUseWinRuntimeBundleManifestPath -RootPath $RootPath
    $manifest | ConvertTo-Json -Depth 6 -Compress | Set-Content -Path $manifestPath -Encoding UTF8
}

function Read-ComputerUseWinRuntimeBundleManifest {
    param(
        [Parameter(Mandatory)]
        [string] $RootPath
    )

    $manifestPath = Get-ComputerUseWinRuntimeBundleManifestPath -RootPath $RootPath
    if (-not (Test-Path $manifestPath -PathType Leaf)) {
        throw "Runtime bundle manifest '$manifestPath' is missing."
    }

    Get-Content -Path $manifestPath -Raw | ConvertFrom-Json
}

function Test-ComputerUseWinRuntimeBundleManifestExists {
    param(
        [Parameter(Mandatory)]
        [string] $RootPath
    )

    Test-Path (Get-ComputerUseWinRuntimeBundleManifestPath -RootPath $RootPath) -PathType Leaf
}

function Assert-ComputerUseWinRuntimeBundleMatchesManifest {
    param(
        [Parameter(Mandatory)]
        [string] $RootPath,
        [Parameter(Mandatory)]
        [string] $Description
    )

    $manifest = Read-ComputerUseWinRuntimeBundleManifest -RootPath $RootPath
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
        Where-Object { -not [string]::Equals($_.Name, $script:ComputerUseWinRuntimeBundleManifestFileName, [System.StringComparison]::OrdinalIgnoreCase) } |
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

    Assert-ComputerUseWinRuntimeBundleMatchesDepsJson -RootPath $RootPath -Description $Description
}

function Assert-ComputerUseWinRuntimeBundleHasExistingManifest {
    param(
        [Parameter(Mandatory)]
        [string] $RootPath,
        [Parameter(Mandatory)]
        [string] $Description
    )

    if (Test-ComputerUseWinRuntimeBundleManifestExists -RootPath $RootPath) {
        Assert-ComputerUseWinRuntimeBundleMatchesManifest -RootPath $RootPath -Description $Description
        return
    }

    throw "$Description cannot be accepted as a runtime bundle because '$script:ComputerUseWinRuntimeBundleManifestFileName' is missing."
}

function Publish-ComputerUseWinRuntimeBundleToDirectory {
    param(
        [Parameter(Mandatory)]
        [string] $RepoRoot,
        [Parameter(Mandatory)]
        [string] $DestinationRoot,
        [string] $Rid = 'win-x64',
        [string] $PublishSourceRoot = ''
    )

    $serverProjectPath = Join-Path $RepoRoot 'src\WinBridge.Server\WinBridge.Server.csproj'
    Remove-DirectoryIfExists -Path $DestinationRoot
    New-Item -ItemType Directory -Path $DestinationRoot -Force | Out-Null

    if (-not [string]::IsNullOrWhiteSpace($PublishSourceRoot)) {
        Assert-ComputerUseWinRuntimeBundleMatchesManifest -RootPath $PublishSourceRoot -Description "Test publish source runtime bundle '$PublishSourceRoot'"
        Copy-DirectoryContents -SourceRoot $PublishSourceRoot -DestinationRoot $DestinationRoot
        Assert-ComputerUseWinRuntimeBundleMatchesManifest -RootPath $DestinationRoot -Description "Published computer-use-win runtime bundle '$DestinationRoot'"
        return
    }

    Invoke-NativeCommandToStderr -FailureMessage "dotnet publish failed for computer-use-win runtime bundle." -Command {
        & dotnet publish $serverProjectPath `
            --configuration Release `
            --runtime $Rid `
            --disable-build-servers `
            --self-contained true `
            -p:UseAppHost=true `
            -p:UiaWorkerPublishSelfContained=true `
            -p:PublishSingleFile=false `
            -p:PublishTrimmed=false `
            --output $DestinationRoot
    }

    Repair-ComputerUseWinRuntimeBundleDepsAssets -RootPath $DestinationRoot -DepsJsonPath (Join-Path $DestinationRoot 'Okno.Server.deps.json')
    Write-ComputerUseWinRuntimeBundleManifest -RootPath $DestinationRoot
    Assert-ComputerUseWinRuntimeBundleMatchesManifest -RootPath $DestinationRoot -Description "Published computer-use-win runtime bundle '$DestinationRoot'"
}
