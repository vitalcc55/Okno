param(
    [Parameter(Mandatory)]
    [string] $Version,
    [string] $Rid = 'win-x64',
    [string] $RuntimeDownloadBaseUrl = '',
    [string] $PublishSourceRoot = '',
    [string] $OutputRoot = ''
)

$ErrorActionPreference = 'Stop'
if (Get-Variable -Name PSStyle -ErrorAction Ignore) {
    $PSStyle.OutputRendering = 'PlainText'
}

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

. (Join-Path $PSScriptRoot 'computer-use-win-runtime-bundle-common.ps1')

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$assetName = "okno-computer-use-win-runtime-$Version-$Rid.zip"
$checksumFileName = "okno-computer-use-win-runtime-$Version-SHA256SUMS.txt"

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot ".tmp\.codex\release-packaging\computer-use-win\$Version\$Rid"
}

$OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
$stagingRoot = Join-Path $OutputRoot ('bundle-' + [Guid]::NewGuid().ToString('N'))
$archivePath = Join-Path $OutputRoot $assetName
$checksumPath = Join-Path $OutputRoot $checksumFileName
$descriptorPath = Join-Path $OutputRoot 'runtime-release.json'
$resultPath = Join-Path $OutputRoot 'runtime-packaging-result.json'

if ($Rid -ne 'win-x64') {
    throw "Unsupported RID '$Rid'. The first release wave is win-x64-first, even though the contract stays RID-aware."
}

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
if (Test-Path $descriptorPath -PathType Leaf) {
    Remove-Item -LiteralPath $descriptorPath -Force
}
if (Test-Path $resultPath -PathType Leaf) {
    Remove-Item -LiteralPath $resultPath -Force
}
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null

try {
    Publish-ComputerUseWinRuntimeBundleToDirectory -RepoRoot $repoRoot -DestinationRoot $stagingRoot -Rid $Rid -PublishSourceRoot $PublishSourceRoot
    Compress-Archive -Path (Join-Path $stagingRoot '*') -DestinationPath $archivePath -CompressionLevel Optimal
    $archiveSha256 = Get-FileSha256 -Path $archivePath
    "$archiveSha256 *$assetName" | Set-Content -Path $checksumPath -Encoding UTF8

    $downloadUrl = if ([string]::IsNullOrWhiteSpace($RuntimeDownloadBaseUrl)) {
        [Uri]::new([System.IO.Path]::GetFullPath($archivePath)).AbsoluteUri
    }
    else {
        [Uri]::new([Uri]::new($RuntimeDownloadBaseUrl), $assetName).AbsoluteUri
    }

    Write-ComputerUseWinRuntimeReleaseDescriptor `
        -DescriptorPath $descriptorPath `
        -Version $Version `
        -Rid $Rid `
        -AssetName $assetName `
        -DownloadUrl $downloadUrl `
        -Sha256 $archiveSha256

    [pscustomobject]@{
        version = $Version
        rid = $Rid
        tag = "v$Version"
        assetName = $assetName
        archivePath = $archivePath
        checksumPath = $checksumPath
        descriptorPath = $descriptorPath
        downloadUrl = $downloadUrl
        sha256 = $archiveSha256
    } | ConvertTo-Json -Depth 6 | Set-Content -Path $resultPath -Encoding UTF8
}
finally {
    Remove-DirectoryIfExists -Path $stagingRoot
}

[pscustomobject]@{
    version = $Version
    rid = $Rid
    tag = "v$Version"
    assetName = $assetName
    archivePath = $archivePath
    checksumPath = $checksumPath
    descriptorPath = $descriptorPath
    resultPath = $resultPath
    downloadUrl = $downloadUrl
    sha256 = $archiveSha256
} | ConvertTo-Json -Compress
