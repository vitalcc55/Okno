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

function New-CommandsMarkdown {
    param(
        [object] $SmokeReport,
        [string] $SmokeRunId,
        [string] $AuditDirectory,
        [string] $SmokeReportPath
    )

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
        '| `powershell -ExecutionPolicy Bypass -File scripts/refresh-generated-docs.ps1` | regenerate generated docs and bootstrap status |',
        '| `powershell -ExecutionPolicy Bypass -File scripts/ci.ps1` | local CI equivalent |',
        '| `powershell -ExecutionPolicy Bypass -File scripts/investigate.ps1` | open latest audit/smoke summaries |',
        '| `powershell -ExecutionPolicy Bypass -File scripts/codex/bootstrap.ps1` | Codex bootstrap handshake |',
        '| `powershell -ExecutionPolicy Bypass -File scripts/codex/verify.ps1` | Codex verify handshake |',
        '| `dotnet run --project src/WinBridge.Server/WinBridge.Server.csproj --no-build` | run MCP server manually |',
        '',
        '## Validation Entry Points',
        '',
        '> Этот раздел перечисляет канонические validation commands, но не утверждает факт успешного последнего прогона. Реальное smoke evidence публикуется ниже, а full validation state должен подтверждаться отдельными run artifacts или `scripts/codex/verify.ps1`.',
        '',
        '- `dotnet build WinBridge.sln --no-restore`',
        '- `dotnet test WinBridge.sln`',
        '- `powershell -ExecutionPolicy Bypass -File scripts/smoke.ps1`',
        '- `powershell -ExecutionPolicy Bypass -File scripts/refresh-generated-docs.ps1`',
        '- `powershell -ExecutionPolicy Bypass -File scripts/ci.ps1`',
        '',
        '## Latest Smoke Evidence',
        '',
        ('- smoke run id: ' + $SmokeRunId),
        ('- monitor count: ' + $SmokeReport.monitors.count),
        ('- desktop monitor id: ' + $SmokeReport.desktop_capture.monitorId),
        ('- audit directory: ' + $AuditDirectory),
        ('- capture artifact: ' + (Convert-ToRepoRelative -Path $SmokeReport.capture.artifactPath)),
        ('- helper capture artifact: ' + (Convert-ToRepoRelative -Path $SmokeReport.helper_capture.artifactPath)),
        ('- smoke report: ' + $SmokeReportPath)
    )

    return $lines -join [Environment]::NewLine
}

function Get-OptionalValue {
    param(
        [Parameter(Mandatory)]
        [object] $Object,
        [Parameter(Mandatory)]
        [string[]] $PathSegments,
        [object] $Default = $null
    )

    $current = $Object
    foreach ($segment in $PathSegments) {
        if ($null -eq $current) {
            return $Default
        }

        $property = $current.PSObject.Properties[$segment]
        if ($null -eq $property) {
            return $Default
        }

        $current = $property.Value
    }

    return $current
}

function Normalize-SmokeReport {
    param(
        [Parameter(Mandatory)]
        [object] $RawSmokeReport
    )

    $healthArtifactsDirectory = Get-OptionalValue -Object $RawSmokeReport -PathSegments @('health', 'artifactsDirectory')
    $captureArtifactPath = Get-OptionalValue -Object $RawSmokeReport -PathSegments @('capture', 'artifactPath')
    $helperCaptureArtifactPath = Get-OptionalValue -Object $RawSmokeReport -PathSegments @('helper_capture', 'artifactPath')

    return [pscustomobject]@{
        run_id = Get-OptionalValue -Object $RawSmokeReport -PathSegments @('run_id')
        initialized_protocol = Get-OptionalValue -Object $RawSmokeReport -PathSegments @('initialized_protocol')
        server_name = Get-OptionalValue -Object $RawSmokeReport -PathSegments @('server_name')
        declared_tools = @(Get-OptionalValue -Object $RawSmokeReport -PathSegments @('declared_tools') -Default @())
        health = [pscustomobject]@{
            artifactsDirectory = $healthArtifactsDirectory
        }
        monitors = [pscustomobject]@{
            count = Get-OptionalValue -Object $RawSmokeReport -PathSegments @('monitors', 'count') -Default 0
            diagnostics = Get-OptionalValue -Object $RawSmokeReport -PathSegments @('monitors', 'diagnostics') -Default $null
        }
        windows = [pscustomobject]@{
            count = Get-OptionalValue -Object $RawSmokeReport -PathSegments @('windows', 'count') -Default 0
        }
        attached_window = [pscustomobject]@{
            attachedWindow = [pscustomobject]@{
                window = [pscustomobject]@{
                    hwnd = Get-OptionalValue -Object $RawSmokeReport -PathSegments @('attached_window', 'attachedWindow', 'window', 'hwnd')
                }
            }
        }
        capture = [pscustomobject]@{
            artifactPath = $captureArtifactPath
        }
        desktop_capture = [pscustomobject]@{
            monitorId = Get-OptionalValue -Object $RawSmokeReport -PathSegments @('desktop_capture', 'monitorId')
        }
        helper_capture = [pscustomobject]@{
            artifactPath = $helperCaptureArtifactPath
        }
        helper_window = [pscustomobject]@{
            hwnd = Get-OptionalValue -Object $RawSmokeReport -PathSegments @('helper_window', 'hwnd')
        }
        helper_activate = [pscustomobject]@{
            status = Get-OptionalValue -Object $RawSmokeReport -PathSegments @('helper_activate', 'status')
        }
    }
}

function Get-SmokeDerivedMetadata {
    param(
        [Parameter(Mandatory)]
        [object] $SmokeReport
    )

    $artifactsDirectory = $SmokeReport.health.artifactsDirectory
    $auditSummaryPath = $null
    if (-not [string]::IsNullOrWhiteSpace($artifactsDirectory)) {
        $auditSummaryPath = Convert-ToRepoRelative -Path (Join-Path $artifactsDirectory 'summary.md')
    }

    return [pscustomobject]@{
        audit_directory = Convert-ToRepoRelative -Path $artifactsDirectory
        audit_summary = $auditSummaryPath
        capture_artifact = Convert-ToRepoRelative -Path $SmokeReport.capture.artifactPath
        helper_capture_artifact = Convert-ToRepoRelative -Path $SmokeReport.helper_capture.artifactPath
        smoke_run_id = [string]$SmokeReport.run_id
        smoke_server = $SmokeReport.server_name
        smoke_protocol = $SmokeReport.initialized_protocol
        smoke_declared_tools = @($SmokeReport.declared_tools).Count
        smoke_monitor_count = $SmokeReport.monitors.count
        smoke_desktop_monitor_id = $SmokeReport.desktop_capture.monitorId
        smoke_visible_windows = $SmokeReport.windows.count
        smoke_attached_hwnd = $SmokeReport.attached_window.attachedWindow.window.hwnd
        smoke_helper_window_hwnd = $SmokeReport.helper_window.hwnd
        smoke_helper_activation_status = $SmokeReport.helper_activate.status
    }
}

function New-TestMatrixMarkdown {
    $lines = @(
        '# Test Matrix',
        '',
        '> Generated file. Refreshed by `scripts/refresh-generated-docs.ps1`.',
        '',
        '> Матрица ниже описывает coverage и entry points. Она не утверждает факт успешного последнего прогона; смотри `docs/generated/commands.md` и `docs/bootstrap/bootstrap-status.json` для latest verified validation.',
        '',
        '| Layer | Command | Coverage now |',
        '| --- | --- | --- |',
        '| Static/analyzers | `dotnet build WinBridge.sln --no-restore` | compile, nullability, analyzers, warnings-as-errors |',
        '| Unit | `dotnet test tests/WinBridge.Runtime.Tests/WinBridge.Runtime.Tests.csproj` | audit schema routing, display identity pipeline, monitor id formatting, activation decision logic, session dedupe, session mutation |',
        '| Integration | `dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj` | raw stdio MCP protocol, attach/focus/activate contract semantics, monitor inventory, desktop capture by `monitorId`, desktop capture by explicit `hwnd`, capture result shape |',
        '| Smoke | `powershell -ExecutionPolicy Bypass -File scripts/smoke.ps1` | init -> tools/list -> health -> list monitors -> desktop capture by monitorId -> list windows -> attach -> session_state -> capture -> helper minimize/activate/window capture |',
        '| Local CI | `powershell -ExecutionPolicy Bypass -File scripts/ci.ps1` | restore + build + test + smoke |',
        '',
        '## Чего пока не хватает',
        '',
        '- Contract tests на конкретную JSON-форму каждого deferred tool.',
        '- Production-coverage для следующих slices: UIA, input, clipboard, wait.',
        '- Отдельный monitor-select contract beyond `windows.list_monitors` + `monitorId` targeting, если позже понадобится richer multi-monitor workflow.',
        '- Boundary tests на проектные зависимости, если слоёв станет больше.',
        '- Coverage reporting как отдельный отчётный шаг.'
    )

    return $lines -join [Environment]::NewLine
}

function New-BootstrapStatusObject {
    param(
        [object] $SmokeReport,
        [string] $SmokeRunId,
        [string] $AuditDirectory,
        [string] $SmokeReportPath,
        [string] $LatestAuditSummaryPath
    )

    return [ordered]@{
        generated_at_utc = (Get-Date).ToUniversalTime().ToString('o')
        product_name = 'Okno'
        transport = [ordered]@{
            kind = 'stdio'
            delivery_status = 'product-ready target'
        }
        tool_contract_source = [ordered]@{
            tool_names = 'src/WinBridge.Runtime.Tooling/ToolNames.cs'
            tool_manifest = 'src/WinBridge.Runtime.Tooling/ToolContractManifest.cs'
            export_script = 'scripts/refresh-generated-docs.ps1'
        }
        runtime_projects = @(Get-RuntimeProjectDirectories)
        latest_validation = [ordered]@{
            build = 'dotnet build WinBridge.sln --no-restore'
            test = 'dotnet test WinBridge.sln'
            smoke = 'powershell -ExecutionPolicy Bypass -File scripts/smoke.ps1'
            refresh_generated_docs = 'powershell -ExecutionPolicy Bypass -File scripts/refresh-generated-docs.ps1'
            ci = 'powershell -ExecutionPolicy Bypass -File scripts/ci.ps1'
        }
        latest_smoke = [ordered]@{
            run_id = $SmokeRunId
            server = $SmokeReport.server_name
            protocol = $SmokeReport.initialized_protocol
            declared_tools = @($SmokeReport.declared_tools).Count
            monitor_count = $SmokeReport.monitors.count
            desktop_monitor_id = $SmokeReport.desktop_capture.monitorId
            visible_windows = $SmokeReport.windows.count
            attached_hwnd = $SmokeReport.attached_window.attachedWindow.window.hwnd
            capture_artifact = (Convert-ToRepoRelative -Path $SmokeReport.capture.artifactPath)
            helper_window_hwnd = $SmokeReport.helper_window.hwnd
            helper_activation_status = $SmokeReport.helper_activate.status
            audit_directory = $AuditDirectory
            smoke_report = $SmokeReportPath
            audit_summary = $LatestAuditSummaryPath
        }
        deferred_scope = @(
            'UIA',
            'Input',
            'Clipboard',
            'Waiting',
            'HTTP transport'
        )
    }
}

$latestSmokeReport = Get-LatestFile -Path (Join-Path $repoRoot 'artifacts/smoke') -Filter 'report.json'
if ($null -eq $latestSmokeReport) {
    throw 'Cannot refresh generated docs without at least one smoke report. Run scripts/smoke.ps1 first.'
}

$smokeReport = Normalize-SmokeReport -RawSmokeReport ((Get-Content $latestSmokeReport.FullName -Raw | ConvertFrom-Json))
$smokeDerivedMetadata = Get-SmokeDerivedMetadata -SmokeReport $smokeReport
$smokeRunId = [string]$smokeReport.run_id
$auditDirectory = $smokeDerivedMetadata.audit_directory
$smokeReportRelativePath = Convert-ToRepoRelative -Path $latestSmokeReport.FullName
$latestAuditSummaryPath = $smokeDerivedMetadata.audit_summary

New-CommandsMarkdown -SmokeReport $smokeReport -SmokeRunId $smokeRunId -AuditDirectory $auditDirectory -SmokeReportPath $smokeReportRelativePath |
    Set-Content -Path $commandsMarkdownPath -Encoding utf8

New-TestMatrixMarkdown |
    Set-Content -Path $testMatrixMarkdownPath -Encoding utf8

New-BootstrapStatusObject -SmokeReport $smokeReport -SmokeRunId $smokeRunId -AuditDirectory $auditDirectory -SmokeReportPath $smokeReportRelativePath -LatestAuditSummaryPath $latestAuditSummaryPath |
    ConvertTo-Json -Depth 12 |
    Set-Content -Path $bootstrapStatusJsonPath -Encoding utf8
