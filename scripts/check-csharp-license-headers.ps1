Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptPath = Join-Path $PSScriptRoot "sync-csharp-license-headers.ps1"
& $scriptPath
if (-not $?) {
    $lastExit = Get-Variable LASTEXITCODE -ValueOnly -ErrorAction SilentlyContinue
    if ($null -ne $lastExit) {
        exit $lastExit
    }

    exit 1
}
