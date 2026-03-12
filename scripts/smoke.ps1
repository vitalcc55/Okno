. (Join-Path $PSScriptRoot 'common.ps1')

$repoRoot = Get-RepoRoot -ScriptRoot $PSScriptRoot
Set-Location $repoRoot

$runId = Get-Date -Format 'yyyyMMddTHHmmssfff'
$artifactRoot = Join-Path $repoRoot "artifacts\\smoke\\$runId"
$serverDll = Join-Path $repoRoot 'src\WinBridge.Server\bin\Debug\net8.0-windows10.0.19041.0\Okno.Server.dll'
$contractPath = Join-Path $artifactRoot 'project-interfaces.json'
New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null

function Send-Json {
    param(
        [Parameter(Mandatory)]
        [System.Diagnostics.Process] $Process,
        [Parameter(Mandatory)]
        [object] $Payload
    )

    $json = $Payload | ConvertTo-Json -Compress -Depth 12
    $Process.StandardInput.WriteLine($json)
    $Process.StandardInput.Flush()

    return $json
}

function Read-Response {
    param(
        [Parameter(Mandatory)]
        [System.Diagnostics.Process] $Process,
        [Parameter(Mandatory)]
        [string] $RequestName,
        [int] $TimeoutSeconds = 15
    )

    while (-not $Process.HasExited) {
        $readTask = $Process.StandardOutput.ReadLineAsync()
        if (-not $readTask.Wait([TimeSpan]::FromSeconds($TimeoutSeconds))) {
            throw "Timed out waiting for MCP response to '$RequestName' after $TimeoutSeconds seconds."
        }

        $line = $readTask.Result
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        $doc = $line | ConvertFrom-Json
        if ($null -ne $doc.id) {
            return [PSCustomObject]@{
                Raw = $line
                Json = $doc
            }
        }
    }

    throw "Сервер завершился до получения ответа MCP."
}

function Assert-Condition {
    param(
        [Parameter(Mandatory)]
        [bool] $Condition,
        [Parameter(Mandatory)]
        [string] $Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Get-ToolPayload {
    param(
        [Parameter(Mandatory)]
        [object] $ToolResponse
    )

    $text = $ToolResponse.result.content[0].text
    if ([string]::IsNullOrWhiteSpace($text)) {
        throw "В ответе tool call отсутствует text payload."
    }

    return $text | ConvertFrom-Json
}

function Get-RequiredToolNames {
    param(
        [Parameter(Mandatory)]
        [object] $Manifest
    )

    return @($Manifest.tools.smoke_required_names)
}

$startInfo = [System.Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = 'dotnet'
$startInfo.Arguments = "`"$serverDll`""
$startInfo.WorkingDirectory = $repoRoot
$startInfo.RedirectStandardInput = $true
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
$startInfo.UseShellExecute = $false
$startInfo.StandardOutputEncoding = [System.Text.Encoding]::UTF8
$startInfo.StandardErrorEncoding = [System.Text.Encoding]::UTF8

$process = [System.Diagnostics.Process]::new()
$process.StartInfo = $startInfo
$process.Start() | Out-Null

try {
    Invoke-NativeCommand -Description 'tool contract export for smoke' -Command {
        dotnet "$serverDll" --export-tool-contract-json "$contractPath"
    }

    $manifest = Get-Content $contractPath -Raw | ConvertFrom-Json
    $requiredTools = Get-RequiredToolNames -Manifest $manifest

    $rawInitializeRequest = Send-Json -Process $process -Payload @{
        jsonrpc = '2.0'
        id = 1
        method = 'initialize'
        params = @{
            protocolVersion = '2025-06-18'
            capabilities = @{}
            clientInfo = @{
                name = 'Okno.Smoke'
                version = '0.1.0'
            }
        }
    }
    $initializeResponse = Read-Response -Process $process -RequestName 'initialize'

    [void](Send-Json -Process $process -Payload @{
        jsonrpc = '2.0'
        method = 'notifications/initialized'
    })

    $rawListRequest = Send-Json -Process $process -Payload @{
        jsonrpc = '2.0'
        id = 2
        method = 'tools/list'
        params = @{}
    }
    $listResponse = Read-Response -Process $process -RequestName 'tools/list'
    foreach ($requiredTool in $requiredTools) {
        Assert-Condition -Condition (@($listResponse.Json.result.tools | ForEach-Object { $_.name }) -contains $requiredTool) -Message "Required tool '$requiredTool' is missing from tools/list."
    }

    $rawHealthRequest = Send-Json -Process $process -Payload @{
        jsonrpc = '2.0'
        id = 3
        method = 'tools/call'
        params = @{
            name = 'okno.health'
            arguments = @{}
        }
    }
    $healthResponse = Read-Response -Process $process -RequestName 'okno.health'
    $healthPayload = Get-ToolPayload -ToolResponse $healthResponse.Json
    Assert-Condition -Condition ($healthPayload.service -eq 'Okno') -Message "Health payload returned unexpected service name '$($healthPayload.service)'."

    $rawWindowsRequest = Send-Json -Process $process -Payload @{
        jsonrpc = '2.0'
        id = 4
        method = 'tools/call'
        params = @{
            name = 'windows.list_windows'
            arguments = @{
                includeInvisible = $false
            }
        }
    }
    $windowsResponse = Read-Response -Process $process -RequestName 'windows.list_windows'
    $windowsPayload = Get-ToolPayload -ToolResponse $windowsResponse.Json
    Assert-Condition -Condition ($windowsPayload.count -gt 0) -Message 'Smoke requires at least one visible window to validate attach/session flow.'

    $attachedWindow = $null
    $sessionPayload = $null
    if ($windowsPayload.count -gt 0) {
        $firstWindow = $windowsPayload.windows[0]
        $rawAttachRequest = Send-Json -Process $process -Payload @{
            jsonrpc = '2.0'
            id = 5
            method = 'tools/call'
            params = @{
                name = 'windows.attach_window'
                arguments = @{
                    hwnd = [long]$firstWindow.hwnd
                }
            }
        }
        $attachResponse = Read-Response -Process $process -RequestName 'windows.attach_window'
        $attachedWindow = Get-ToolPayload -ToolResponse $attachResponse.Json
        $attachStatus = [string]$attachedWindow.status
        Assert-Condition -Condition (@('done', 'already_attached') -contains $attachStatus) -Message "Attach returned unexpected status '$attachStatus'."
        Assert-Condition -Condition ([long]$attachedWindow.attachedWindow.window.hwnd -eq [long]$firstWindow.hwnd) -Message 'Attach payload does not point to the requested hwnd.'

        $rawSessionRequest = Send-Json -Process $process -Payload @{
            jsonrpc = '2.0'
            id = 6
            method = 'tools/call'
            params = @{
                name = 'okno.session_state'
                arguments = @{}
            }
        }
        $sessionResponse = Read-Response -Process $process -RequestName 'okno.session_state'
        $sessionPayload = Get-ToolPayload -ToolResponse $sessionResponse.Json
        Assert-Condition -Condition ($sessionPayload.mode -eq 'window') -Message "Session snapshot mode is '$($sessionPayload.mode)', expected 'window'."
        Assert-Condition -Condition ([long]$sessionPayload.attachedWindow.window.hwnd -eq [long]$firstWindow.hwnd) -Message 'Session snapshot does not point to the attached hwnd.'
    }

    $attachedHwnd = $null
    if ($null -ne $attachedWindow) {
        $attachedHwnd = $attachedWindow.attachedWindow.window.hwnd
    }

    $process.StandardInput.Close()
    if (-not $process.WaitForExit(5000)) {
        $process.Kill()
        $process.WaitForExit()
    }

    $stderr = $process.StandardError.ReadToEnd()

    $report = [ordered]@{
        run_id = $runId
        initialized_protocol = $initializeResponse.Json.result.protocolVersion
        server_name = $initializeResponse.Json.result.serverInfo.name
        tool_contract = $manifest
        declared_tools = @($listResponse.Json.result.tools | ForEach-Object { $_.name })
        health = $healthPayload
        windows = $windowsPayload
        attached_window = $attachedWindow
        session = $sessionPayload
        raw_requests = [ordered]@{
            initialize = $rawInitializeRequest
            list_tools = $rawListRequest
            health = $rawHealthRequest
            list_windows = $rawWindowsRequest
        }
        raw_responses = [ordered]@{
            initialize = $initializeResponse.Raw
            list_tools = $listResponse.Raw
            health = $healthResponse.Raw
            list_windows = $windowsResponse.Raw
        }
        stderr = $stderr
    }

    if ($null -ne $attachedWindow) {
        $report.raw_requests.attach_window = $rawAttachRequest
        $report.raw_requests.session_state = $rawSessionRequest
        $report.raw_responses.attach_window = $attachResponse.Raw
        $report.raw_responses.session_state = $sessionResponse.Raw
    }

    $reportPath = Join-Path $artifactRoot 'report.json'
    $summaryPath = Join-Path $artifactRoot 'summary.md'

    $report | ConvertTo-Json -Depth 20 | Set-Content -Path $reportPath -Encoding utf8

    $summary = @(
        '# Okno smoke summary',
        '',
        "- run_id: $runId",
        "- server: $($initializeResponse.Json.result.serverInfo.name)",
        "- protocol: $($initializeResponse.Json.result.protocolVersion)",
        "- declared_tools: $(@($listResponse.Json.result.tools).Count)",
        "- visible_windows: $($windowsPayload.count)",
        "- attached_hwnd: $attachedHwnd",
        "- audit_dir: $($healthPayload.artifactsDirectory)",
        "- report: $reportPath"
    ) -join [Environment]::NewLine

    $summary | Set-Content -Path $summaryPath -Encoding utf8

    Get-Content $summaryPath
}
finally {
    if ($null -ne $process -and -not $process.HasExited) {
        try {
            $process.StandardInput.Close()
        }
        catch {
        }

        if (-not $process.WaitForExit(5000)) {
            $process.Kill()
            $process.WaitForExit()
        }
    }
}
