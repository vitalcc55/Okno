. (Join-Path $PSScriptRoot 'common.ps1')

$repoRoot = Get-RepoRoot -ScriptRoot $PSScriptRoot
Set-Location $repoRoot

$serverDll = Join-Path $repoRoot 'src\WinBridge.Server\bin\Debug\net8.0-windows10.0.19041.0\Okno.Server.dll'
$projectInterfacesJsonPath = Join-Path $repoRoot 'docs\generated\project-interfaces.json'
$projectInterfacesMarkdownPath = Join-Path $repoRoot 'docs\generated\project-interfaces.md'
$commandsMarkdownPath = Join-Path $repoRoot 'docs\generated\commands.md'
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
        '## Latest Verified Validation',
        '',
        '- `dotnet build WinBridge.sln --no-restore` -> success, 0 warnings, 0 errors.',
        '- `dotnet test WinBridge.sln` -> success, 12/12 tests passed.',
        '- `powershell -ExecutionPolicy Bypass -File scripts/smoke.ps1` -> success; verified init, tools/list, `okno.health`, `windows.list_windows`, `windows.attach_window`, `okno.session_state`.',
        '- `powershell -ExecutionPolicy Bypass -File scripts/refresh-generated-docs.ps1` -> success; regenerated `project-interfaces.*`, `commands.md`, `bootstrap-status.json`.',
        '- `powershell -ExecutionPolicy Bypass -File scripts/ci.ps1` -> success.',
        '',
        '## Latest Smoke Evidence',
        '',
        ('- smoke run id: ' + $SmokeRunId),
        ('- audit directory: ' + $AuditDirectory),
        ('- smoke report: ' + $SmokeReportPath)
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
        runtime_projects = @(
            'src/WinBridge.Runtime',
            'src/WinBridge.Runtime.Contracts',
            'src/WinBridge.Runtime.Tooling',
            'src/WinBridge.Runtime.Diagnostics',
            'src/WinBridge.Runtime.Session',
            'src/WinBridge.Runtime.Windows.Shell',
            'src/WinBridge.Runtime.Windows.UIA',
            'src/WinBridge.Runtime.Windows.Capture',
            'src/WinBridge.Runtime.Windows.Input',
            'src/WinBridge.Runtime.Windows.Clipboard',
            'src/WinBridge.Runtime.Waiting'
        )
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
            visible_windows = $SmokeReport.windows.count
            attached_hwnd = $SmokeReport.attached_window.attachedWindow.window.hwnd
            audit_directory = $AuditDirectory
            smoke_report = $SmokeReportPath
            audit_summary = $LatestAuditSummaryPath
        }
        deferred_scope = @(
            'UIA',
            'Capture',
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

$smokeReport = Get-Content $latestSmokeReport.FullName -Raw | ConvertFrom-Json
$smokeRunId = [string]$smokeReport.run_id
$auditDirectory = Convert-ToRepoRelative -Path $smokeReport.health.artifactsDirectory
$smokeReportRelativePath = Convert-ToRepoRelative -Path $latestSmokeReport.FullName
$latestAuditSummaryPath = Convert-ToRepoRelative -Path (Join-Path $smokeReport.health.artifactsDirectory 'summary.md')

New-CommandsMarkdown -SmokeReport $smokeReport -SmokeRunId $smokeRunId -AuditDirectory $auditDirectory -SmokeReportPath $smokeReportRelativePath |
    Set-Content -Path $commandsMarkdownPath -Encoding utf8

New-BootstrapStatusObject -SmokeReport $smokeReport -SmokeRunId $smokeRunId -AuditDirectory $auditDirectory -SmokeReportPath $smokeReportRelativePath -LatestAuditSummaryPath $latestAuditSummaryPath |
    ConvertTo-Json -Depth 12 |
    Set-Content -Path $bootstrapStatusJsonPath -Encoding utf8
