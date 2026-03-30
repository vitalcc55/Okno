param(
    [string]$RepoRoot = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
)

$ErrorActionPreference = 'Stop'

$serverBinRoot = Join-Path $RepoRoot 'src\WinBridge.Server\bin'
if (-not (Test-Path $serverBinRoot)) {
    throw "Okno server output root '$serverBinRoot' not found. Run .\scripts\codex\bootstrap.ps1 and .\scripts\codex\verify.ps1 from repo root first."
}

$serverDll = Get-ChildItem -Path $serverBinRoot -Recurse -Filter 'Okno.Server.dll' -File |
    Sort-Object LastWriteTimeUtc -Descending |
    Select-Object -First 1

if ($null -eq $serverDll) {
    throw "Okno.Server.dll not found under '$serverBinRoot'. Run .\scripts\codex\bootstrap.ps1 and .\scripts\codex\verify.ps1 from repo root first."
}

$serverDll.FullName
