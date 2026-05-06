param(
    [Parameter(Mandatory)]
    [string] $Version,
    [string] $Rid = 'win-x64',
    [string] $OutputRoot = ''
)

$ErrorActionPreference = 'Stop'
if (Get-Variable -Name PSStyle -ErrorAction Ignore) {
    $PSStyle.OutputRendering = 'PlainText'
}

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$projectPath = Join-Path $repoRoot 'src\WinBridge.Setup.App\WinBridge.Setup.App.csproj'
$descriptorPath = Join-Path $repoRoot 'plugins\computer-use-win\runtime-release.json'
$assetName = "okno-setup-unsigned-$Version-$Rid.zip"
$checksumFileName = "okno-setup-unsigned-$Version-SHA256SUMS.txt"

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot ".tmp\.codex\release-packaging\setup-app\$Version\$Rid"
}

$OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
$publishRoot = Join-Path $OutputRoot ('publish-' + [Guid]::NewGuid().ToString('N'))
$stagingRoot = Join-Path $OutputRoot ('bundle-' + [Guid]::NewGuid().ToString('N'))
$archivePath = Join-Path $OutputRoot $assetName
$checksumPath = Join-Path $OutputRoot $checksumFileName

if ($Rid -ne 'win-x64') {
    throw "Unsupported RID '$Rid'. The first installer wave is win-x64-first."
}

if ($Version.StartsWith('v', [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Version '$Version' must not include a leading 'v'."
}

if ($Version -notmatch '^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$') {
    throw "Version '$Version' does not match the expected release contract."
}

if (-not (Test-Path $descriptorPath -PathType Leaf)) {
    throw "Runtime release descriptor '$descriptorPath' is missing."
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

Remove-DirectoryIfExists -Path $publishRoot
Remove-DirectoryIfExists -Path $stagingRoot
if (Test-Path $archivePath -PathType Leaf) {
    Remove-Item -LiteralPath $archivePath -Force
}
if (Test-Path $checksumPath -PathType Leaf) {
    Remove-Item -LiteralPath $checksumPath -Force
}

New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null
New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null

try {
    $publishOutput = & dotnet publish $projectPath `
        -c Release `
        -r $Rid `
        --disable-build-servers `
        --self-contained true `
        -p:PublishSingleFile=false `
        -p:Version=$Version `
        -o $publishRoot 2>&1
    if ($LASTEXITCODE -ne 0) {
        $publishOutputText = if ($publishOutput) { ($publishOutput | Out-String).Trim() } else { '' }
        if ([string]::IsNullOrWhiteSpace($publishOutputText)) {
            throw "dotnet publish for setup app failed."
        }

        throw "dotnet publish for setup app failed.`n$publishOutputText"
    }

    Copy-Item -Path (Join-Path $publishRoot '*') -Destination $stagingRoot -Recurse -Force
    Copy-Item -LiteralPath $descriptorPath -Destination (Join-Path $stagingRoot 'runtime-release.json') -Force

    $originalExePath = Join-Path $stagingRoot 'WinBridge.Setup.App.exe'
    $renamedExePath = Join-Path $stagingRoot 'Okno Setup.exe'
    if (-not (Test-Path $originalExePath -PathType Leaf)) {
        throw "Published setup app does not contain WinBridge.Setup.App.exe."
    }

    if (Test-Path $renamedExePath -PathType Leaf) {
        Remove-Item -LiteralPath $renamedExePath -Force
    }

    Rename-Item -LiteralPath $originalExePath -NewName 'Okno Setup.exe'

    Compress-Archive -Path (Join-Path $stagingRoot '*') -DestinationPath $archivePath -CompressionLevel Optimal
    $archiveSha256 = Get-FileSha256 -Path $archivePath
    "$archiveSha256 *$assetName" | Set-Content -Path $checksumPath -Encoding UTF8
}
finally {
    Remove-DirectoryIfExists -Path $publishRoot
    Remove-DirectoryIfExists -Path $stagingRoot
}

[pscustomobject]@{
    version = $Version
    rid = $Rid
    assetName = $assetName
    archivePath = $archivePath
    checksumPath = $checksumPath
    sha256 = $archiveSha256
} | ConvertTo-Json -Compress
