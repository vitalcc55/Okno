$ErrorActionPreference = 'Stop'
if (Get-Variable -Name PSStyle -ErrorAction Ignore) {
    $PSStyle.OutputRendering = 'PlainText'
}

$env:COMPUTER_USE_WIN_PLUGIN_ROOT = $PSScriptRoot

Set-Location $PSScriptRoot

$runtimeRoot = Join-Path $PSScriptRoot 'runtime\win-x64'
$serverExePath = Join-Path $runtimeRoot 'Okno.Server.exe'
$requiredRuntimeFiles = @(
    'Okno.Server.exe',
    'Okno.Server.dll',
    'Okno.Server.deps.json',
    'Okno.Server.runtimeconfig.json'
)

if (-not (Test-Path $serverExePath -PathType Leaf)) {
    throw @"
Не найден plugin-local runtime bundle для `computer-use-win`.

Ожидался apphost:
$serverExePath

Сначала подготовь install artifact командой:
powershell -ExecutionPolicy Bypass -File scripts/codex/publish-computer-use-win-plugin.ps1
"@
}

$missingRuntimeFiles = @(
    $requiredRuntimeFiles | Where-Object {
        -not (Test-Path (Join-Path $runtimeRoot $_) -PathType Leaf)
    }
)

if ($missingRuntimeFiles.Count -gt 0) {
    throw @"
Plugin-local runtime bundle для `computer-use-win` неполон.

Ожидался полный runtime bundle в:
$runtimeRoot

Отсутствуют файлы:
$($missingRuntimeFiles -join [Environment]::NewLine)

Сначала заново подготовь install artifact командой:
powershell -ExecutionPolicy Bypass -File scripts/codex/publish-computer-use-win-plugin.ps1
"@
}

& $serverExePath --tool-surface-profile computer-use-win
