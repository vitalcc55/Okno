$ErrorActionPreference = 'Stop'
if (Get-Variable -Name PSStyle -ErrorAction Ignore) {
    $PSStyle.OutputRendering = 'PlainText'
}
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

function Get-RepoRoot {
    param(
        [Parameter(Mandatory)]
        [string] $ScriptRoot
    )

    return Split-Path -Parent $ScriptRoot
}

function Invoke-NativeCommand {
    param(
        [Parameter(Mandatory)]
        [string] $Description,
        [Parameter(Mandatory)]
        [scriptblock] $Command
    )

    $global:LASTEXITCODE = 0
    & $Command

    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "$Description failed with exit code $exitCode."
    }
}

function Invoke-Step {
    param(
        [Parameter(Mandatory)]
        [string] $Description,
        [Parameter(Mandatory)]
        [scriptblock] $Command
    )

    $global:LASTEXITCODE = 0
    & $Command

    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "$Description failed with exit code $exitCode."
    }
}

function Get-LatestFile {
    param(
        [Parameter(Mandatory)]
        [string] $Path,
        [Parameter(Mandatory)]
        [string] $Filter
    )

    if (-not (Test-Path $Path)) {
        return $null
    }

    return Get-ChildItem -Path $Path -Filter $Filter -Recurse |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
}

function Get-SmokeNarrativeSteps {
    return @(
        'init',
        'tools/list',
        'health',
        '`windows.launch_process` dry-run/live helper launch',
        'list monitors',
        'list windows',
        'attach',
        'session_state',
        'uia_snapshot',
        'capture',
        'helper minimize/activate/window capture',
        'wait active/exists/gone/text/focus/visual',
        'terminal `windows.open_target` dry-run/live folder proof',
        'open_target and launch artifact/event cross-check'
    )
}

function Get-SmokeCoverageNarrative {
    return [string]::Join(' -> ', @(Get-SmokeNarrativeSteps))
}

function Get-SmokeCommandPurpose {
    return 'stdio MCP smoke with owned helper scenario, terminal `windows.open_target` folder proof and artifact report'
}

function Get-SmokeCommandLiteral {
    return '`powershell -ExecutionPolicy Bypass -File scripts/smoke.ps1`'
}

function Get-SmokeUnownedProbeBasePath {
    return Join-Path ([System.IO.Path]::GetTempPath()) 'WinBridge\SmokeUnowned'
}

function Remove-StaleSmokeUnownedProbeRoots {
    param(
        [Parameter(Mandatory)]
        [string] $CurrentRunId,
        [int] $MaxAgeDays = 1
    )

    $basePath = Get-SmokeUnownedProbeBasePath
    if (-not (Test-Path $basePath)) {
        return
    }

    $cutoff = (Get-Date).AddDays(-$MaxAgeDays)
    foreach ($directory in @(Get-ChildItem -Path $basePath -Directory -ErrorAction SilentlyContinue)) {
        if ($directory.Name -eq $CurrentRunId) {
            continue
        }

        if ($directory.LastWriteTime -ge $cutoff) {
            continue
        }

        try {
            Remove-Item -LiteralPath $directory.FullName -Force -Recurse -ErrorAction Stop
        }
        catch {
        }
    }
}

function New-SmokeUnownedProbeFolder {
    param(
        [Parameter(Mandatory)]
        [string] $RunId,
        [Parameter(Mandatory)]
        [string] $FolderName
    )

    $root = Join-Path (Get-SmokeUnownedProbeBasePath) $RunId
    $folderPath = Join-Path $root $FolderName
    New-Item -ItemType Directory -Force -Path $folderPath | Out-Null
    return $folderPath
}
