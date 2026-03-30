$ErrorActionPreference = 'Stop'
if (Get-Variable -Name PSStyle -ErrorAction Ignore) {
    $PSStyle.OutputRendering = 'PlainText'
}

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Set-Location $repoRoot

$serverDll = Join-Path $repoRoot 'src\WinBridge.Server\bin\Debug\net8.0-windows10.0.19041.0\Okno.Server.dll'

if (-not (Test-Path $serverDll)) {
    [Console]::Error.WriteLine("Okno.Server.dll not found. Run .\\scripts\\codex\\bootstrap.ps1 and .\\scripts\\codex\\verify.ps1 from repo root first.")
    exit 1
}

& dotnet $serverDll
