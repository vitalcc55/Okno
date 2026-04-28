. (Join-Path $PSScriptRoot 'common.ps1')

$repoRoot = Get-RepoRoot -ScriptRoot $PSScriptRoot
${null} = Initialize-WinBridgeExecutionContext -RepoRoot $repoRoot -UseArtifactsRoot
Set-Location $repoRoot

$projectInterfacesJsonPath = Join-Path $repoRoot 'docs\generated\project-interfaces.json'
$projectInterfacesMarkdownPath = Join-Path $repoRoot 'docs\generated\project-interfaces.md'
$computerUseWinInterfacesJsonPath = Join-Path $repoRoot 'docs\generated\computer-use-win-interfaces.json'
$computerUseWinInterfacesMarkdownPath = Join-Path $repoRoot 'docs\generated\computer-use-win-interfaces.md'
$commandsMarkdownPath = Join-Path $repoRoot 'docs\generated\commands.md'
$testMatrixMarkdownPath = Join-Path $repoRoot 'docs\generated\test-matrix.md'
$stackInventoryMarkdownPath = Join-Path $repoRoot 'docs\generated\stack-inventory.md'
$bootstrapStatusJsonPath = Join-Path $repoRoot 'docs\bootstrap\bootstrap-status.json'

Invoke-WinBridgeSolutionBuild -Description 'dotnet build before generated docs refresh'

$bundle = Use-OknoTestBundle -RepoRoot $repoRoot
$serverDll = [string]$bundle.serverDll

Invoke-NativeCommand -Description 'tool contract export' -Command {
    dotnet "$serverDll" --export-tool-contract-json "$projectInterfacesJsonPath" --export-tool-contract-markdown "$projectInterfacesMarkdownPath"
}
Invoke-NativeCommand -Description 'computer-use-win tool contract export' -Command {
    dotnet "$serverDll" --tool-surface-profile computer-use-win --export-tool-contract-json "$computerUseWinInterfacesJsonPath" --export-tool-contract-markdown "$computerUseWinInterfacesMarkdownPath"
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
    $smokeCommandLiteral = Get-SmokeCommandLiteral
    $smokeCommandPurpose = Get-SmokeCommandPurpose

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
        '| `powershell -ExecutionPolicy Bypass -File scripts/build.ps1` | solution build with analyzers into .NET artifacts root |',
        '| `powershell -ExecutionPolicy Bypass -File scripts/test.ps1` | unit + integration tests with staged server/helper bundle |',
        "| $smokeCommandLiteral | $smokeCommandPurpose |",
        '| `powershell -ExecutionPolicy Bypass -File scripts/refresh-generated-docs.ps1` | regenerate deterministic generated docs and bootstrap status |',
        '| `powershell -ExecutionPolicy Bypass -File scripts/ci.ps1` | local CI equivalent |',
        '| `powershell -ExecutionPolicy Bypass -File scripts/investigate.ps1` | open latest local audit/smoke summaries |',
        '| `powershell -ExecutionPolicy Bypass -File scripts/codex/bootstrap.ps1` | Codex bootstrap handshake |',
        '| `powershell -ExecutionPolicy Bypass -File scripts/codex/prepare-okno-test-bundle.ps1` | stage immutable server/helper run bundle for integration and smoke |',
        '| `powershell -ExecutionPolicy Bypass -File scripts/codex/resolve-okno-test-bundle.ps1` | resolve or materialize the effective staged bundle for the current verification context |',
        '| `powershell -ExecutionPolicy Bypass -File scripts/codex/resolve-okno-server-launch-target.ps1` | resolve the effective staged Windows launch target from pinned `artifacts_root` (`Okno.Server.exe` preferred, `dotnet + .dll` fallback) |',
        '| `powershell -ExecutionPolicy Bypass -File scripts/codex/publish-computer-use-win-plugin.ps1` | publish self-contained `computer-use-win` runtime bundle into `plugins/computer-use-win/runtime/win-x64/` |',
        '| `powershell -ExecutionPolicy Bypass -File scripts/codex/verify.ps1` | Codex verify handshake |',
        '| `powershell -ExecutionPolicy Bypass -File scripts/codex/write-okno-plugin-repo-root-hint.ps1` | stamp repo-root hint into internal okno plugin install surface before reinstall or refresh |',
        '| `dotnet run --project src/WinBridge.Server/WinBridge.Server.csproj --no-build` | run MCP server manually |',
        '',
        '## Validation Entry Points',
        '',
        '> Этот раздел перечисляет канонические validation commands и не зависит от конкретного run id. Для evidence конкретного запуска смотри `artifacts/smoke/<run_id>/` или используй `scripts/investigate.ps1`.',
        '',
        '- `dotnet build WinBridge.sln --no-restore`',
        '- `dotnet test WinBridge.sln --configuration Debug`',
        '- `powershell -ExecutionPolicy Bypass -File scripts/smoke.ps1`',
        '- `powershell -ExecutionPolicy Bypass -File scripts/refresh-generated-docs.ps1`',
        '- `powershell -ExecutionPolicy Bypass -File scripts/codex/verify.ps1`',
        '- `powershell -ExecutionPolicy Bypass -File scripts/ci.ps1`',
        '',
        '## Artifact Layout',
        '',
        '- `artifacts/diagnostics/<run_id>/events.jsonl`',
        '- `artifacts/diagnostics/<run_id>/summary.md`',
        '- `artifacts/diagnostics/<run_id>/captures/<capture_id>.png`',
        '- `artifacts/diagnostics/<run_id>/launch/<launch_id>.json`',
        '- `artifacts/diagnostics/<run_id>/uia/<snapshot_id>.json`',
        '- `artifacts/diagnostics/<run_id>/wait/<wait_id>.json`',
        '- `artifacts/diagnostics/<run_id>/input/input-*.json`',
        '- `artifacts/diagnostics/<run_id>/wait/visual/<visual_wait_artifact>.png`',
        '- `artifacts/smoke/<run_id>/report.json`',
        '- `artifacts/smoke/<run_id>/summary.md`'
    )

    return $lines -join [Environment]::NewLine
}

function New-TestMatrixMarkdown {
    $smokeCommandLiteral = Get-SmokeCommandLiteral
    $smokeCoverageNarrative = Get-SmokeCoverageNarrative

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
        '| Unit | `dotnet test tests/WinBridge.Runtime.Tests/WinBridge.Runtime.Tests.csproj` | audit schema routing, launch exporter drift, launch runtime/status/evidence policy, input contract/runtime/materializer policy, display identity pipeline, monitor id formatting, activation decision logic, wait runtime/status/evidence policy, UIA runtime packaging/evidence, session dedupe, session mutation |',
        '| Integration | `dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj` | raw stdio MCP protocol through staged server/helper bundle with run-aware resolver semantics, public `windows.launch_process`, `windows.open_target` and click-first `windows.input` schema/result mapping, public `computer-use-win` action wave (`press_key`, `set_value`, `type_text`, `scroll`, `perform_secondary_action`, `drag`) including helper-backed drag proof, attach/focus/activate contract semantics, live `windows.uia_snapshot` target policy/result shape, public `windows.wait` schema/result mapping, monitor inventory, desktop capture by `monitorId`, desktop capture by explicit `hwnd`, capture result shape |',
        "| Smoke | $smokeCommandLiteral | $smokeCoverageNarrative |",
        '| Local CI | `powershell -ExecutionPolicy Bypass -File scripts/ci.ps1` | restore + build + test + smoke |',
        '',
        '## Чего пока не хватает',
        '',
        '- Production-coverage для следующих slices: clipboard и broad input actions beyond click-first.',
        '- Отдельный monitor-select contract beyond `windows.list_monitors` + `monitorId` targeting, если позже понадобится richer multi-monitor workflow.',
        '- Boundary tests на проектные зависимости, если слоёв станет больше.',
        '- Coverage reporting как отдельный отчётный шаг.'
    )

    return $lines -join [Environment]::NewLine
}

function New-StackInventoryMarkdown {
    $targetFramework = ([string]([xml](Get-Content -Path (Join-Path $repoRoot 'Directory.Build.props') -Raw)).Project.PropertyGroup.TargetFramework).Trim()
    $sdkVersion = ([string](Get-Content -Path (Join-Path $repoRoot 'global.json') -Raw | ConvertFrom-Json).sdk.version).Trim()
    [xml]$packageProps = Get-Content -Path (Join-Path $repoRoot 'Directory.Packages.props') -Raw
    $mcpVersionNode = @($packageProps.Project.ItemGroup.PackageVersion | Where-Object { $_.Include -eq 'ModelContextProtocol' })[0]
    $mcpVersion = if ($null -ne $mcpVersionNode) { ([string]$mcpVersionNode.Version).Trim() } else { 'unknown' }
    $runtimeProjects = @(Get-RuntimeProjectDirectories)
    $runtimeProjectSet = @{}
    foreach ($project in $runtimeProjects) {
        $runtimeProjectSet[$project] = $true
    }

    $projectInterfaceDocument = Get-Content -Path $projectInterfacesJsonPath -Raw | ConvertFrom-Json
    $implementedToolNames = @($projectInterfaceDocument.tools.implemented_names)
    $deferredToolNames = @($projectInterfaceDocument.tools.deferred_phase_map.PSObject.Properties.Name)

    $rows = @(
        @{
            Label = 'Runtime composition root'
            Path = 'src/WinBridge.Runtime'
            Stack = $targetFramework
            Status = $(if ($runtimeProjectSet.ContainsKey('src/WinBridge.Runtime')) { 'Работает' } else { 'Отсутствует' })
        },
        @{
            Label = 'Runtime contracts'
            Path = 'src/WinBridge.Runtime.Contracts'
            Stack = $targetFramework
            Status = $(if ($runtimeProjectSet.ContainsKey('src/WinBridge.Runtime.Contracts')) { 'Работает' } else { 'Отсутствует' })
        },
        @{
            Label = 'Runtime tooling'
            Path = 'src/WinBridge.Runtime.Tooling'
            Stack = $targetFramework
            Status = $(if ($runtimeProjectSet.ContainsKey('src/WinBridge.Runtime.Tooling')) { 'Работает' } else { 'Отсутствует' })
        },
        @{
            Label = 'Runtime diagnostics'
            Path = 'src/WinBridge.Runtime.Diagnostics'
            Stack = $targetFramework
            Status = $(if ($runtimeProjectSet.ContainsKey('src/WinBridge.Runtime.Diagnostics')) { 'Работает' } else { 'Отсутствует' })
        },
        @{
            Label = 'Runtime session'
            Path = 'src/WinBridge.Runtime.Session'
            Stack = $targetFramework
            Status = $(if ($runtimeProjectSet.ContainsKey('src/WinBridge.Runtime.Session')) { 'Работает' } else { 'Отсутствует' })
        },
        @{
            Label = 'Windows shell'
            Path = 'src/WinBridge.Runtime.Windows.Shell'
            Stack = $targetFramework
            Status = $(if ($runtimeProjectSet.ContainsKey('src/WinBridge.Runtime.Windows.Shell')) { 'Работает' } else { 'Отсутствует' })
        },
        @{
            Label = 'Windows UIA slice'
            Path = 'src/WinBridge.Runtime.Windows.UIA, src/WinBridge.Runtime.Windows.UIA.Hosting, src/WinBridge.Runtime.Windows.UIA.Worker'
            Stack = $targetFramework
            Status = $(if ($runtimeProjectSet.ContainsKey('src/WinBridge.Runtime.Windows.UIA') -and $runtimeProjectSet.ContainsKey('src/WinBridge.Runtime.Windows.UIA.Hosting') -and $runtimeProjectSet.ContainsKey('src/WinBridge.Runtime.Windows.UIA.Worker')) { 'Работает' } else { 'Частично' })
        },
        @{
            Label = 'Public wait slice'
            Path = 'src/WinBridge.Runtime.Waiting'
            Stack = $targetFramework
            Status = $(if ($runtimeProjectSet.ContainsKey('src/WinBridge.Runtime.Waiting') -and ($implementedToolNames -contains 'windows.wait')) { 'Работает' } else { 'Подготовлены' })
        },
        @{
            Label = 'Input and clipboard capability seams'
            Path = 'src/WinBridge.Runtime.Windows.Input, src/WinBridge.Runtime.Windows.Clipboard'
            Stack = $targetFramework
            Status = $(if (($implementedToolNames -contains 'windows.input') -and ($deferredToolNames -contains 'windows.clipboard_get') -and ($deferredToolNames -contains 'windows.clipboard_set')) { 'Input click-first работает; clipboard deferred' } else { 'См. manifest' })
        },
        @{
            Label = 'MCP host'
            Path = 'src/WinBridge.Server'
            Stack = "$targetFramework, ModelContextProtocol $mcpVersion"
            Status = $(if (Test-Path (Join-Path $repoRoot 'src/WinBridge.Server/WinBridge.Server.csproj')) { 'Работает' } else { 'Отсутствует' })
        },
        @{
            Label = 'Unit tests'
            Path = 'tests/WinBridge.Runtime.Tests'
            Stack = 'xUnit'
            Status = $(if (Test-Path (Join-Path $repoRoot 'tests/WinBridge.Runtime.Tests/WinBridge.Runtime.Tests.csproj')) { 'Работает' } else { 'Отсутствует' })
        },
        @{
            Label = 'Integration smoke'
            Path = 'tests/WinBridge.Server.IntegrationTests'
            Stack = 'xUnit + raw stdio JSON-RPC'
            Status = $(if (Test-Path (Join-Path $repoRoot 'tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj')) { 'Работает' } else { 'Отсутствует' })
        },
        @{
            Label = 'Dev control plane'
            Path = 'scripts/*.ps1'
            Stack = 'PowerShell'
            Status = $(if (Test-Path (Join-Path $repoRoot 'scripts')) { 'Работает' } else { 'Отсутствует' })
        },
        @{
            Label = 'Repo memory'
            Path = 'AGENTS.md, docs/'
            Stack = 'Markdown'
            Status = $(if ((Test-Path (Join-Path $repoRoot 'AGENTS.md')) -and (Test-Path (Join-Path $repoRoot 'docs'))) { 'Работает' } else { 'Отсутствует' })
        }
    )

    $lines = @(
        '# Stack Inventory',
        '',
        '> Generated file. Refreshed by `scripts/refresh-generated-docs.ps1`.',
        '',
        '## Текущий срез репозитория',
        '',
        '`Okno` больше не пустой репозиторий со спецификацией без реализации. Текущий runtime уже закрепляет boring baseline для Windows-native MCP.',
        '',
        '## Языки и рантаймы',
        '',
        '- `C# 12 / .NET 8` через SDK-style projects.',
        '- `PowerShell` для локального control plane и smoke/investigation workflows.',
        '- `Markdown` для durable repo memory.',
        '',
        '## Текущее дерево подсистем',
        '',
        '| Подсистема | Путь | Стек | Статус |',
        '| --- | --- | --- | --- |'
    )

    foreach ($row in $rows) {
        $lines += '| ' + $row.Label + ' | `' + $row.Path + '` | `' + $row.Stack + '` | ' + $row.Status + ' |'
    }

    $lines += ''
    $lines += '## Package/build/tooling map'
    $lines += ''
    $lines += ('- `global.json` -> SDK ' + $sdkVersion)
    $lines += '- Central package versions: `Directory.Packages.props`'
    $lines += '- Build/analyzer baseline: `Directory.Build.props`'
    $lines += '- Formatting/style baseline: `.editorconfig`'
    $lines += '- Package manager: NuGet via `dotnet`'
    $lines += '- Tool contract source of truth: `ToolNames` + `ToolContractManifest`'
    $lines += ''
    $lines += '## Осознанно отсутствует в bootstrap'
    $lines += ''
    $lines += '- Docker/Compose/devcontainer'
    $lines += '- HTTP transport как рабочий delivery mode'
    $lines += '- clipboard production implementation and broad input actions beyond click-first'
    $lines += '- external observability backend'

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
        'Input broad actions beyond click-first',
        'Clipboard',
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
        '    "test": "dotnet test WinBridge.sln --configuration Debug",',
        '    "smoke": "powershell -ExecutionPolicy Bypass -File scripts/smoke.ps1",',
        '    "refresh_generated_docs": "powershell -ExecutionPolicy Bypass -File scripts/refresh-generated-docs.ps1",',
        '    "ci": "powershell -ExecutionPolicy Bypass -File scripts/ci.ps1"',
        '  },',
        '  "artifact_layout": {',
        '    "diagnostics_events": "artifacts/diagnostics/<run_id>/events.jsonl",',
        '    "diagnostics_summary": "artifacts/diagnostics/<run_id>/summary.md",',
        '    "capture_artifact": "artifacts/diagnostics/<run_id>/captures/<capture_id>.png",',
        '    "uia_artifact": "artifacts/diagnostics/<run_id>/uia/<snapshot_id>.json",',
        '    "wait_artifact": "artifacts/diagnostics/<run_id>/wait/<wait_id>.json",',
        '    "input_artifact": "artifacts/diagnostics/<run_id>/input/input-*.json",',
        '    "wait_visual_artifact": "artifacts/diagnostics/<run_id>/wait/visual/<visual_wait_artifact>.png",',
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

Write-Utf8DeterministicFile -Path $stackInventoryMarkdownPath -Content (New-StackInventoryMarkdown)

Write-Utf8DeterministicFile -Path $bootstrapStatusJsonPath -Content (New-BootstrapStatusJson)
