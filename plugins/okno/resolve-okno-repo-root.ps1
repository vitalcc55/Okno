param(
    [string]$PluginRoot = $PSScriptRoot
)

$ErrorActionPreference = 'Stop'

function Test-OknoRepoRootCandidate {
    param(
        [AllowEmptyString()]
        [string] $CandidatePath
    )

    if ([string]::IsNullOrWhiteSpace($CandidatePath)) {
        return $false
    }

    $fullPath = [System.IO.Path]::GetFullPath($CandidatePath)
    if (-not (Test-Path $fullPath -PathType Container)) {
        return $false
    }

    $requiredPaths = @(
        'WinBridge.sln',
        'scripts\codex\resolve-okno-server-dll.ps1',
        'src\WinBridge.Server\WinBridge.Server.csproj'
    )

    foreach ($relativePath in $requiredPaths) {
        if (-not (Test-Path (Join-Path $fullPath $relativePath))) {
            return $false
        }
    }

    return $true
}

function Resolve-OknoRepoRootFromAncestors {
    param(
        [Parameter(Mandatory)]
        [string] $StartPath
    )

    $current = [System.IO.Path]::GetFullPath($StartPath)
    while (-not [string]::IsNullOrWhiteSpace($current)) {
        if (Test-OknoRepoRootCandidate -CandidatePath $current) {
            return $current
        }

        $parent = Split-Path -Parent $current
        if ([string]::Equals($parent, $current, [System.StringComparison]::OrdinalIgnoreCase)) {
            break
        }

        $current = $parent
    }

    return $null
}

function Resolve-OknoRepoRootFromHint {
    param(
        [Parameter(Mandatory)]
        [string] $HintPath
    )

    if (-not (Test-Path $HintPath -PathType Leaf)) {
        return $null
    }

    $hintValue = Get-Content -LiteralPath $HintPath -ErrorAction Stop |
        Select-Object -First 1

    if ([string]::IsNullOrWhiteSpace($hintValue)) {
        return $null
    }

    return $hintValue.Trim()
}

$pluginRootFullPath = [System.IO.Path]::GetFullPath($PluginRoot)
$repoRootFromAncestors = Resolve-OknoRepoRootFromAncestors -StartPath $pluginRootFullPath
if ($null -ne $repoRootFromAncestors) {
    $repoRootFromAncestors
    return
}

$repoRootFromEnvironment = $env:OKNO_REPO_ROOT
if (Test-OknoRepoRootCandidate -CandidatePath $repoRootFromEnvironment) {
    [System.IO.Path]::GetFullPath($repoRootFromEnvironment)
    return
}

$hintPath = Join-Path $pluginRootFullPath '.okno-repo-root.txt'
$repoRootFromHint = Resolve-OknoRepoRootFromHint -HintPath $hintPath
if (Test-OknoRepoRootCandidate -CandidatePath $repoRootFromHint) {
    [System.IO.Path]::GetFullPath($repoRootFromHint)
    return
}

throw @"
Не удалось определить repo root для plugin `okno`.

Проверено:
- ancestor search от plugin root `$pluginRootFullPath`;
- переменная окружения `OKNO_REPO_ROOT`;
- hint file `$hintPath`.

Это expected failure для install cache, если plugin был установлен без repo-root hint. Codex загружает установленную local plugin copy из `~/.codex/plugins/cache/.../local`, а не напрямую из repo `source.path`.

Что делать:
1. В корне репозитория запустить `powershell -ExecutionPolicy Bypass -File scripts/codex/write-okno-plugin-repo-root-hint.ps1`.
2. Обновить install/cache copy plugin `okno` из repo marketplace.
3. Перезапустить Codex и открыть новый тред.

Временный fallback: выставить `OKNO_REPO_ROOT` в absolute path к checkout репозитория.
"@
