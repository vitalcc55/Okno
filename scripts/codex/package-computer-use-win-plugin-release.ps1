param(
    [Parameter(Mandatory)]
    [string] $Version,
    [Parameter(Mandatory)]
    [string] $RuntimePackagingResultPath,
    [string] $PluginSourceRoot = '',
    [string] $OutputRoot = ''
)

$ErrorActionPreference = 'Stop'
if (Get-Variable -Name PSStyle -ErrorAction Ignore) {
    $PSStyle.OutputRendering = 'PlainText'
}

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

. (Join-Path $PSScriptRoot 'computer-use-win-plugin-bundle-common.ps1')

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$runtimeDescriptorPath = Resolve-ComputerUseWinRuntimePackagingDescriptorPath `
    -RuntimePackagingResultPath $RuntimePackagingResultPath `
    -ExpectedVersion $Version `
    -ExpectedRid 'win-x64'
$assetName = "okno-computer-use-win-plugin-$Version.zip"
$checksumFileName = "okno-computer-use-win-plugin-$Version-SHA256SUMS.txt"

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot ".tmp\.codex\release-packaging\computer-use-win\plugin\$Version"
}

$OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
$stagingRoot = Join-Path $OutputRoot ('bundle-' + [Guid]::NewGuid().ToString('N'))
$archivePath = Join-Path $OutputRoot $assetName
$checksumPath = Join-Path $OutputRoot $checksumFileName

if ($Version.StartsWith('v', [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Version '$Version' must not include a leading 'v'. The tag is derived as 'v$Version'."
}

if ($Version -notmatch '^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$') {
    throw "Version '$Version' does not match the expected release contract."
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

Remove-DirectoryIfExists -Path $stagingRoot
if (Test-Path $archivePath -PathType Leaf) {
    Remove-Item -LiteralPath $archivePath -Force
}
if (Test-Path $checksumPath -PathType Leaf) {
    Remove-Item -LiteralPath $checksumPath -Force
}
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null

try {
    $metadata = Publish-ComputerUseWinPluginBundleToDirectory `
        -RepoRoot $repoRoot `
        -DestinationRoot $stagingRoot `
        -PluginSourceRoot $PluginSourceRoot `
        -RuntimeDescriptorPath $runtimeDescriptorPath
    if ($metadata.pluginVersion -ne $Version) {
        throw "Requested version '$Version' does not match plugin manifest version '$($metadata.pluginVersion)'."
    }

    $archiveInputs = Get-ChildItem -LiteralPath $stagingRoot -Force | ForEach-Object { $_.FullName }
    Compress-Archive -LiteralPath $archiveInputs -DestinationPath $archivePath -CompressionLevel Optimal
    $archiveSha256 = Get-FileSha256 -Path $archivePath
    "$archiveSha256 *$assetName" | Set-Content -Path $checksumPath -Encoding UTF8
}
finally {
    Remove-DirectoryIfExists -Path $stagingRoot
}

[pscustomobject]@{
    version = $Version
    pluginId = [string]$metadata.pluginId
    pluginVersion = [string]$metadata.pluginVersion
    runtimeVersion = [string]$metadata.runtimeVersion
    runtimeRid = [string]$metadata.runtimeRid
    assetName = $assetName
    archivePath = $archivePath
    checksumPath = $checksumPath
    sha256 = $archiveSha256
} | ConvertTo-Json -Compress
