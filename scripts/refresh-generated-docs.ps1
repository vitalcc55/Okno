. (Join-Path $PSScriptRoot 'common.ps1')

$repoRoot = Get-RepoRoot -ScriptRoot $PSScriptRoot
Set-Location $repoRoot

$serverDll = Join-Path $repoRoot 'src\WinBridge.Server\bin\Debug\net8.0-windows10.0.19041.0\Okno.Server.dll'
$projectInterfacesJsonPath = Join-Path $repoRoot 'docs\generated\project-interfaces.json'
$projectInterfacesMarkdownPath = Join-Path $repoRoot 'docs\generated\project-interfaces.md'
$commandsMarkdownPath = Join-Path $repoRoot 'docs\generated\commands.md'
$testMatrixMarkdownPath = Join-Path $repoRoot 'docs\generated\test-matrix.md'
$bootstrapStatusJsonPath = Join-Path $repoRoot 'docs\bootstrap\bootstrap-status.json'

Invoke-NativeCommand -Description 'dotnet build before generated docs refresh' -Command {
    dotnet build WinBridge.sln --no-restore
}

Invoke-NativeCommand -Description 'tool contract export' -Command {
    dotnet "$serverDll" --export-tool-contract-json "$projectInterfacesJsonPath" --export-tool-contract-markdown "$projectInterfacesMarkdownPath"
}

function Convert-ToRepoRelative {
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $null
    }

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $rootWithSlash = [System.IO.Path]::GetFullPath($repoRoot + [System.IO.Path]::DirectorySeparatorChar)

    if ($fullPath.StartsWith($rootWithSlash, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $fullPath.Substring($rootWithSlash.Length).Replace('\', '/')
    }

    return $fullPath
}

function Get-RuntimeProjectDirectories {
    return Get-ChildItem -Path (Join-Path $repoRoot 'src') -Filter '*.csproj' -Recurse |
        Where-Object { $_.BaseName -like 'WinBridge.Runtime*' } |
        Sort-Object FullName |
        ForEach-Object { Convert-ToRepoRelative -Path $_.DirectoryName }
}

function Write-Utf8DeterministicFile {
    param(
        [Parameter(Mandatory)]
        [string] $Path,
        [Parameter(Mandatory)]
        [string] $Content
    )

    $directory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        [System.IO.Directory]::CreateDirectory($directory) | Out-Null
    }

    $normalizedContent = $Content.Replace("`r`n", "`n").Replace("`r", "`n").Replace("`n", "`r`n")
    $encoding = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $normalizedContent, $encoding)
}

function New-CommandsMarkdown {
    $lines = @(
        '# Commands Inventory',
        '',
        '> Generated file. Refreshed by `scripts/refresh-generated-docs.ps1`.',
        '',
        '## Canonical Entry Points',
        '',
        '| Command | Purpose |',
        '| --- | --- |',
        '| `powershell -ExecutionPolicy Bypass -File scripts/bootstrap.ps1` | `dotnet restore` |',
        '| `powershell -ExecutionPolicy Bypass -File scripts/build.ps1` | solution build with analyzers |',
        '| `powershell -ExecutionPolicy Bypass -File scripts/test.ps1` | unit + integration tests |',
        '| `powershell -ExecutionPolicy Bypass -File scripts/smoke.ps1` | stdio MCP smoke and artifact report |',
        '| `powershell -ExecutionPolicy Bypass -File scripts/refresh-generated-docs.ps1` | regenerate deterministic generated docs and bootstrap status |',
        '| `powershell -ExecutionPolicy Bypass -File scripts/ci.ps1` | local CI equivalent |',
        '| `powershell -ExecutionPolicy Bypass -File scripts/investigate.ps1` | open latest local audit/smoke summaries |',
        '| `powershell -ExecutionPolicy Bypass -File scripts/codex/bootstrap.ps1` | Codex bootstrap handshake |',
        '| `powershell -ExecutionPolicy Bypass -File scripts/codex/verify.ps1` | Codex verify handshake |',
        '| `dotnet run --project src/WinBridge.Server/WinBridge.Server.csproj --no-build` | run MCP server manually |',
        '',
        '## Validation Entry Points',
        '',
        '> Этот раздел перечисляет канонические validation commands и не зависит от конкретного run id. Для evidence конкретного запуска смотри `artifacts/smoke/<run_id>/` или используй `scripts/investigate.ps1`.',
        '',
        '- `dotnet build WinBridge.sln --no-restore`',
        '- `dotnet test WinBridge.sln`',
        '- `powershell -ExecutionPolicy Bypass -File scripts/smoke.ps1`',
        '- `powershell -ExecutionPolicy Bypass -File scripts/refresh-generated-docs.ps1`',
        '- `powershell -ExecutionPolicy Bypass -File scripts/ci.ps1`',
        '',
        '## Artifact Layout',
        '',
        '- `artifacts/diagnostics/<run_id>/events.jsonl`',
        '- `artifacts/diagnostics/<run_id>/summary.md`',
        '- `artifacts/diagnostics/<run_id>/captures/<capture_id>.png`',
        '- `artifacts/diagnostics/<run_id>/uia/<snapshot_id>.json`',
        '- `artifacts/smoke/<run_id>/report.json`',
        '- `artifacts/smoke/<run_id>/summary.md`'
    )

    return $lines -join [Environment]::NewLine
}

function New-TestMatrixMarkdown {
    $lines = @(
        '# Test Matrix',
        '',
        '> Generated file. Refreshed by `scripts/refresh-generated-docs.ps1`.',
        '',
        '> Матрица ниже описывает coverage и entry points. Она не утверждает факт конкретного успешного прогона; evidence отдельного запуска смотри в `artifacts/smoke/<run_id>/` и `artifacts/diagnostics/<run_id>/`.',
        '',
        '| Layer | Command | Coverage now |',
        '| --- | --- | --- |',
        '| Static/analyzers | `dotnet build WinBridge.sln --no-restore` | compile, nullability, analyzers, warnings-as-errors |',
        '| Unit | `dotnet test tests/WinBridge.Runtime.Tests/WinBridge.Runtime.Tests.csproj` | audit schema routing, display identity pipeline, monitor id formatting, activation decision logic, UIA runtime packaging/evidence, session dedupe, session mutation |',
        '| Integration | `dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj` | raw stdio MCP protocol, attach/focus/activate contract semantics, live `windows.uia_snapshot` target policy/result shape, monitor inventory, desktop capture by `monitorId`, desktop capture by explicit `hwnd`, capture result shape |',
        '| Smoke | `powershell -ExecutionPolicy Bypass -File scripts/smoke.ps1` | init -> tools/list -> health -> list monitors -> desktop capture by monitorId -> list windows -> attach -> session_state -> uia_snapshot -> capture -> helper minimize/activate/window capture |',
        '| Local CI | `powershell -ExecutionPolicy Bypass -File scripts/ci.ps1` | restore + build + test + smoke |',
        '',
        '## Чего пока не хватает',
        '',
        '- Contract tests на конкретную JSON-форму каждого deferred tool.',
        '- Production-coverage для следующих slices: input, clipboard, wait.',
        '- Отдельный monitor-select contract beyond `windows.list_monitors` + `monitorId` targeting, если позже понадобится richer multi-monitor workflow.',
        '- Boundary tests на проектные зависимости, если слоёв станет больше.',
        '- Coverage reporting как отдельный отчётный шаг.'
    )

    return $lines -join [Environment]::NewLine
}

function Convert-ToJsonStringLiteral {
    param(
        [AllowNull()]
        [string] $Value
    )

    if ($null -eq $Value) {
        return 'null'
    }

    $builder = New-Object System.Text.StringBuilder
    [void]$builder.Append('"')

    foreach ($char in $Value.ToCharArray()) {
        $code = [int][char]$char

        switch ($code) {
            8 {
                [void]$builder.Append('\b')
                continue
            }
            9 {
                [void]$builder.Append('\t')
                continue
            }
            10 {
                [void]$builder.Append('\n')
                continue
            }
            12 {
                [void]$builder.Append('\f')
                continue
            }
            13 {
                [void]$builder.Append('\r')
                continue
            }
            34 {
                [void]$builder.Append('\"')
                continue
            }
            92 {
                [void]$builder.Append('\\')
                continue
            }
        }

        if ($code -lt 0x20) {
            [void]$builder.AppendFormat('\u{0:x4}', $code)
            continue
        }

        [void]$builder.Append($char)
    }

    [void]$builder.Append('"')
    return $builder.ToString()
}

function New-BootstrapStatusJson {
    $runtimeProjects = @(Get-RuntimeProjectDirectories)
    $deferredScope = @(
        'Input',
        'Clipboard',
        'Waiting',
        'HTTP transport'
    )

    $lines = @(
        '{',
        '  "product_name": "Okno",',
        '  "transport": {',
        '    "kind": "stdio",',
        '    "delivery_status": "product-ready target"',
        '  },',
        '  "tool_contract_source": {',
        '    "tool_names": "src/WinBridge.Runtime.Tooling/ToolNames.cs",',
        '    "tool_manifest": "src/WinBridge.Runtime.Tooling/ToolContractManifest.cs",',
        '    "export_script": "scripts/refresh-generated-docs.ps1"',
        '  },',
        '  "runtime_projects": ['
    )

    for ($index = 0; $index -lt $runtimeProjects.Count; $index++) {
        $suffix = if ($index -lt ($runtimeProjects.Count - 1)) { ',' } else { '' }
        $lines += '    ' + (Convert-ToJsonStringLiteral $runtimeProjects[$index]) + $suffix
    }

    $lines += @(
        '  ],',
        '  "validation_entry_points": {',
        '    "build": "dotnet build WinBridge.sln --no-restore",',
        '    "test": "dotnet test WinBridge.sln",',
        '    "smoke": "powershell -ExecutionPolicy Bypass -File scripts/smoke.ps1",',
        '    "refresh_generated_docs": "powershell -ExecutionPolicy Bypass -File scripts/refresh-generated-docs.ps1",',
        '    "ci": "powershell -ExecutionPolicy Bypass -File scripts/ci.ps1"',
        '  },',
        '  "artifact_layout": {',
        '    "diagnostics_events": "artifacts/diagnostics/<run_id>/events.jsonl",',
        '    "diagnostics_summary": "artifacts/diagnostics/<run_id>/summary.md",',
        '    "capture_artifact": "artifacts/diagnostics/<run_id>/captures/<capture_id>.png",',
        '    "uia_artifact": "artifacts/diagnostics/<run_id>/uia/<snapshot_id>.json",',
        '    "smoke_report": "artifacts/smoke/<run_id>/report.json",',
        '    "smoke_summary": "artifacts/smoke/<run_id>/summary.md"',
        '  },',
        '  "deferred_scope": ['
    )

    for ($index = 0; $index -lt $deferredScope.Count; $index++) {
        $suffix = if ($index -lt ($deferredScope.Count - 1)) { ',' } else { '' }
        $lines += '    ' + (Convert-ToJsonStringLiteral $deferredScope[$index]) + $suffix
    }

    $lines += @(
        '  ]',
        '}'
    )

    return $lines -join [Environment]::NewLine
}

Write-Utf8DeterministicFile -Path $commandsMarkdownPath -Content (New-CommandsMarkdown)

Write-Utf8DeterministicFile -Path $testMatrixMarkdownPath -Content (New-TestMatrixMarkdown)

Write-Utf8DeterministicFile -Path $bootstrapStatusJsonPath -Content (New-BootstrapStatusJson)
