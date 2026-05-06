. (Join-Path $PSScriptRoot 'computer-use-win-runtime-bundle-common.ps1')

$script:ComputerUseWinPluginBundleManifestFileName = 'okno-plugin-bundle-manifest.json'

function Get-ComputerUseWinPluginBundleManifestPath {
    param(
        [Parameter(Mandatory)]
        [string] $RootPath
    )

    Join-Path $RootPath $script:ComputerUseWinPluginBundleManifestFileName
}

function Get-ComputerUseWinPluginBundleMetadata {
    param(
        [Parameter(Mandatory)]
        [string] $PluginRoot
    )

    $pluginManifestPath = Join-Path $PluginRoot '.codex-plugin\plugin.json'
    $runtimeReleasePath = Join-Path $PluginRoot 'runtime-release.json'

    if (-not (Test-Path $pluginManifestPath -PathType Leaf)) {
        throw "Plugin manifest '$pluginManifestPath' is missing."
    }

    if (-not (Test-Path $runtimeReleasePath -PathType Leaf)) {
        throw "Runtime release descriptor '$runtimeReleasePath' is missing."
    }

    $pluginManifest = Get-Content -Path $pluginManifestPath -Raw | ConvertFrom-Json
    $runtimeRelease = Get-Content -Path $runtimeReleasePath -Raw | ConvertFrom-Json

    $pluginId = [string]$pluginManifest.name
    $pluginVersion = [string]$pluginManifest.version
    $runtimeVersion = [string]$runtimeRelease.version
    $runtimeRid = [string]$runtimeRelease.rid
    $runtimeTag = [string]$runtimeRelease.tag
    $runtimeAssetName = [string]$runtimeRelease.assetName

    if ([string]::IsNullOrWhiteSpace($pluginId)) {
        throw "Plugin manifest '$pluginManifestPath' is missing 'name'."
    }

    if ([string]::IsNullOrWhiteSpace($pluginVersion)) {
        throw "Plugin manifest '$pluginManifestPath' is missing 'version'."
    }

    if ([string]::IsNullOrWhiteSpace($runtimeVersion)) {
        throw "Runtime release descriptor '$runtimeReleasePath' is missing 'version'."
    }

    if ([string]::IsNullOrWhiteSpace($runtimeRid)) {
        throw "Runtime release descriptor '$runtimeReleasePath' is missing 'rid'."
    }

    if ([string]::IsNullOrWhiteSpace($runtimeTag)) {
        throw "Runtime release descriptor '$runtimeReleasePath' is missing 'tag'."
    }

    if ([string]::IsNullOrWhiteSpace($runtimeAssetName)) {
        throw "Runtime release descriptor '$runtimeReleasePath' is missing 'assetName'."
    }

    if ($pluginVersion -ne $runtimeVersion) {
        throw "Plugin version '$pluginVersion' does not match runtime release version '$runtimeVersion'."
    }

    [pscustomobject]@{
        pluginRoot = [System.IO.Path]::GetFullPath($PluginRoot)
        pluginId = $pluginId
        pluginVersion = $pluginVersion
        runtimeVersion = $runtimeVersion
        runtimeRid = $runtimeRid
        runtimeTag = $runtimeTag
        runtimeAssetName = $runtimeAssetName
    }
}

function Test-ComputerUseWinPluginBundleIncludedRelativePath {
    param(
        [Parameter(Mandatory)]
        [string] $RelativePath
    )

    $normalizedRelativePath = $RelativePath.Replace('/', '\').TrimStart('\')
    if ([string]::IsNullOrWhiteSpace($normalizedRelativePath)) {
        return $false
    }

    return -not $normalizedRelativePath.StartsWith('runtime\', [System.StringComparison]::OrdinalIgnoreCase)
}

function Copy-ComputerUseWinPluginBundleSourceToDirectory {
    param(
        [Parameter(Mandatory)]
        [string] $SourceRoot,
        [Parameter(Mandatory)]
        [string] $DestinationRoot
    )

    $normalizedSourceRoot = [System.IO.Path]::GetFullPath($SourceRoot).TrimEnd('\')
    Get-ChildItem -LiteralPath $SourceRoot -Recurse -Force -File | ForEach-Object {
        $sourcePath = [System.IO.Path]::GetFullPath($_.FullName)
        $relativePath = $sourcePath.Substring($normalizedSourceRoot.Length).TrimStart('\')
        if (-not (Test-ComputerUseWinPluginBundleIncludedRelativePath -RelativePath $relativePath)) {
            return
        }

        $destinationPath = Join-Path $DestinationRoot $relativePath
        $destinationDirectory = Split-Path -Parent $destinationPath
        if (-not (Test-Path $destinationDirectory -PathType Container)) {
            New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
        }

        Copy-Item -LiteralPath $_.FullName -Destination $destinationPath -Force
    }
}

function New-ComputerUseWinPluginBundleManifest {
    param(
        [Parameter(Mandatory)]
        [string] $RootPath,
        [Parameter(Mandatory)]
        [psobject] $Metadata
    )

    $normalizedRootPath = [System.IO.Path]::GetFullPath($RootPath).TrimEnd('\')
    $files = Get-ChildItem -LiteralPath $RootPath -Recurse -Force -File |
        Where-Object { -not [string]::Equals($_.Name, $script:ComputerUseWinPluginBundleManifestFileName, [System.StringComparison]::OrdinalIgnoreCase) } |
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
        pluginId = [string]$Metadata.pluginId
        pluginVersion = [string]$Metadata.pluginVersion
        runtimeVersion = [string]$Metadata.runtimeVersion
        runtimeRid = [string]$Metadata.runtimeRid
        runtimeTag = [string]$Metadata.runtimeTag
        runtimeAssetName = [string]$Metadata.runtimeAssetName
        files = @($files)
    }
}

function Write-ComputerUseWinPluginBundleManifest {
    param(
        [Parameter(Mandatory)]
        [string] $RootPath,
        [Parameter(Mandatory)]
        [psobject] $Metadata
    )

    $manifest = New-ComputerUseWinPluginBundleManifest -RootPath $RootPath -Metadata $Metadata
    $manifestPath = Get-ComputerUseWinPluginBundleManifestPath -RootPath $RootPath
    $manifest | ConvertTo-Json -Depth 6 -Compress | Set-Content -Path $manifestPath -Encoding UTF8
}

function Read-ComputerUseWinPluginBundleManifest {
    param(
        [Parameter(Mandatory)]
        [string] $RootPath
    )

    $manifestPath = Get-ComputerUseWinPluginBundleManifestPath -RootPath $RootPath
    if (-not (Test-Path $manifestPath -PathType Leaf)) {
        throw "Plugin bundle manifest '$manifestPath' is missing."
    }

    Get-Content -Path $manifestPath -Raw | ConvertFrom-Json
}

function Assert-ComputerUseWinPluginBundleMatchesManifest {
    param(
        [Parameter(Mandatory)]
        [string] $RootPath,
        [Parameter(Mandatory)]
        [string] $Description
    )

    $manifest = Read-ComputerUseWinPluginBundleManifest -RootPath $RootPath
    if ($manifest.formatVersion -ne 1) {
        throw "$Description uses unsupported plugin bundle manifest version '$($manifest.formatVersion)'."
    }

    $expectedEntries = @($manifest.files)
    $expectedMap = New-Object 'System.Collections.Generic.Dictionary[string,long]' ([System.StringComparer]::Ordinal)
    foreach ($entry in $expectedEntries) {
        $expectedMap[[string]$entry.path] = [int64]$entry.size
    }

    $normalizedRootPath = [System.IO.Path]::GetFullPath($RootPath).TrimEnd('\')
    $actualFiles = Get-ChildItem -LiteralPath $RootPath -Recurse -Force -File |
        Where-Object { -not [string]::Equals($_.Name, $script:ComputerUseWinPluginBundleManifestFileName, [System.StringComparison]::OrdinalIgnoreCase) } |
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

function Publish-ComputerUseWinPluginBundleToDirectory {
    param(
        [Parameter(Mandatory)]
        [string] $RepoRoot,
        [Parameter(Mandatory)]
        [string] $DestinationRoot,
        [string] $PluginSourceRoot = ''
    )

    if ([string]::IsNullOrWhiteSpace($PluginSourceRoot)) {
        $PluginSourceRoot = Join-Path $RepoRoot 'plugins\computer-use-win'
    }

    $metadata = Get-ComputerUseWinPluginBundleMetadata -PluginRoot $PluginSourceRoot

    Remove-DirectoryIfExists -Path $DestinationRoot
    New-Item -ItemType Directory -Path $DestinationRoot -Force | Out-Null

    Copy-ComputerUseWinPluginBundleSourceToDirectory -SourceRoot $PluginSourceRoot -DestinationRoot $DestinationRoot
    Write-ComputerUseWinPluginBundleManifest -RootPath $DestinationRoot -Metadata $metadata
    Assert-ComputerUseWinPluginBundleMatchesManifest -RootPath $DestinationRoot -Description "Published computer-use-win plugin bundle '$DestinationRoot'"

    $metadata
}
