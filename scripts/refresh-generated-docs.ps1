. (Join-Path $PSScriptRoot 'common.ps1')

$repoRoot = Get-RepoRoot -ScriptRoot $PSScriptRoot
Set-Location $repoRoot

$serverDll = Join-Path $repoRoot 'src\WinBridge.Server\bin\Debug\net8.0-windows10.0.19041.0\Okno.Server.dll'
$jsonPath = Join-Path $repoRoot 'docs\generated\project-interfaces.json'
$markdownPath = Join-Path $repoRoot 'docs\generated\project-interfaces.md'

Invoke-NativeCommand -Description 'dotnet build before generated docs refresh' -Command {
    dotnet build WinBridge.sln --no-restore
}

Invoke-NativeCommand -Description 'tool contract export' -Command {
    dotnet "$serverDll" --export-tool-contract-json "$jsonPath" --export-tool-contract-markdown "$markdownPath"
}
