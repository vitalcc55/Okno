. (Join-Path $PSScriptRoot 'common.ps1')

$repoRoot = Get-RepoRoot -ScriptRoot $PSScriptRoot
Set-Location $repoRoot

$runId = Get-Date -Format 'yyyyMMddTHHmmssfff'
$artifactRoot = Join-Path $repoRoot "artifacts\\smoke\\$runId"
$serverDll = Join-Path $repoRoot 'src\WinBridge.Server\bin\Debug\net8.0-windows10.0.19041.0\Okno.Server.dll'
$helperExe = Join-Path $repoRoot 'tests\WinBridge.SmokeWindowHost\bin\Debug\net8.0-windows10.0.19041.0\WinBridge.SmokeWindowHost.exe'
$contractPath = Join-Path $artifactRoot 'project-interfaces.json'
$script:NextMcpRequestId = 1
New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null

if (-not ([System.Management.Automation.PSTypeName]'WinBridgeSmoke.User32').Type) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

namespace WinBridgeSmoke
{
    public static class User32
    {
        [DllImport("user32.dll")]
        public static extern bool ShowWindowAsync(IntPtr hwnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hwnd);
    }
}
'@
}

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
        [Parameter(Mandatory)]
        [int] $ExpectedId,
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
            if ([int]$doc.id -ne $ExpectedId) {
                throw "Received MCP response id '$($doc.id)' while waiting for '$RequestName' response id '$ExpectedId'."
            }

            return [PSCustomObject]@{
                Raw = $line
                Json = $doc
            }
        }
    }

    throw "Сервер завершился до получения ответа MCP."
}

function Get-NextMcpRequestId {
    $id = $script:NextMcpRequestId
    $script:NextMcpRequestId++
    return $id
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

function Get-ImageBlock {
    param(
        [Parameter(Mandatory)]
        [object] $ToolResponse
    )

    foreach ($content in @($ToolResponse.result.content)) {
        if ($content.type -eq 'image') {
            return $content
        }
    }

    throw 'В ответе tool call отсутствует image content block.'
}

function Get-RequiredToolNames {
    param(
        [Parameter(Mandatory)]
        [object] $Manifest
    )

    return @($Manifest.tools.smoke_required_names)
}

function Wait-Until {
    param(
        [Parameter(Mandatory)]
        [scriptblock] $Predicate,
        [int] $TimeoutMilliseconds = 10000,
        [int] $PollMilliseconds = 100
    )

    $deadline = (Get-Date).AddMilliseconds($TimeoutMilliseconds)
    while ((Get-Date) -lt $deadline) {
        if (& $Predicate) {
            return $true
        }

        Start-Sleep -Milliseconds $PollMilliseconds
    }

    return [bool](& $Predicate)
}

function Start-SmokeHelperWindow {
    param(
        [Parameter(Mandatory)]
        [string] $ExecutablePath,
        [Parameter(Mandatory)]
        [string] $Title
    )

    Assert-Condition -Condition (Test-Path $ExecutablePath) -Message "Smoke helper executable '$ExecutablePath' does not exist. Build the solution before running smoke."

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $ExecutablePath
    $startInfo.Arguments = "--title `"$Title`""
    $startInfo.WorkingDirectory = $repoRoot
    $startInfo.UseShellExecute = $false

    $helperProcess = [System.Diagnostics.Process]::new()
    $helperProcess.StartInfo = $startInfo
    $helperProcess.Start() | Out-Null
    return $helperProcess
}

function Wait-ForMainWindowHandle {
    param(
        [Parameter(Mandatory)]
        [System.Diagnostics.Process] $Process,
        [int] $TimeoutMilliseconds = 10000
    )

    try {
        [void]$Process.WaitForInputIdle($TimeoutMilliseconds)
    }
    catch [System.InvalidOperationException] {
    }

    $found = Wait-Until -TimeoutMilliseconds $TimeoutMilliseconds -Predicate {
        $Process.Refresh()
        return $Process.MainWindowHandle -ne 0
    }

    Assert-Condition -Condition $found -Message 'Smoke helper window did not expose a main window handle in time.'
    $Process.Refresh()
    return [int64]$Process.MainWindowHandle
}

function Invoke-ToolCall {
    param(
        [Parameter(Mandatory)]
        [System.Diagnostics.Process] $Process,
        [Parameter(Mandatory)]
        [string] $Name,
        [Parameter(Mandatory)]
        [hashtable] $Arguments,
        [Parameter(Mandatory)]
        [string] $RequestName
    )

    $requestId = Get-NextMcpRequestId
    $rawRequest = Send-Json -Process $Process -Payload @{
        jsonrpc = '2.0'
        id = $requestId
        method = 'tools/call'
        params = @{
            name = $Name
            arguments = $Arguments
        }
    }
    $response = Read-Response -Process $Process -RequestName $RequestName -ExpectedId $requestId

    return [PSCustomObject]@{
        Id = $requestId
        RawRequest = $rawRequest
        RawResponse = $response.Raw
        Json = $response.Json
        Payload = Get-ToolPayload -ToolResponse $response.Json
    }
}

function Invoke-McpRequest {
    param(
        [Parameter(Mandatory)]
        [System.Diagnostics.Process] $Process,
        [Parameter(Mandatory)]
        [string] $Method,
        [Parameter(Mandatory)]
        [hashtable] $Params,
        [Parameter(Mandatory)]
        [string] $RequestName
    )

    $requestId = Get-NextMcpRequestId
    $rawRequest = Send-Json -Process $Process -Payload @{
        jsonrpc = '2.0'
        id = $requestId
        method = $Method
        params = $Params
    }
    $response = Read-Response -Process $Process -RequestName $RequestName -ExpectedId $requestId

    return [PSCustomObject]@{
        Id = $requestId
        RawRequest = $rawRequest
        RawResponse = $response.Raw
        Json = $response.Json
    }
}

function Wait-ForVisibleHelperWindow {
    param(
        [Parameter(Mandatory)]
        [System.Diagnostics.Process] $Process,
        [Parameter(Mandatory)]
        [int64] $HelperHwnd,
        [int] $TimeoutMilliseconds = 10000,
        [int] $PollMilliseconds = 100
    )

    $deadline = (Get-Date).AddMilliseconds($TimeoutMilliseconds)
    do {
        $result = Invoke-ToolCall -Process $Process -Name 'windows.list_windows' -Arguments @{ includeInvisible = $false } -RequestName 'windows.list_windows(visible helper readiness)'

        if ($result.Payload.count -gt 0 -and (@($result.Payload.windows | ForEach-Object { [int64]$_.hwnd }) -contains $HelperHwnd)) {
            return $result
        }

        Start-Sleep -Milliseconds $PollMilliseconds
    }
    while ((Get-Date) -lt $deadline)

    throw 'Smoke helper window did not appear in visible windows.list_windows inventory in time.'
}

function Wait-ForSuccessfulWindowCapture {
    param(
        [Parameter(Mandatory)]
        [System.Diagnostics.Process] $Process,
        [int] $TimeoutMilliseconds = 10000,
        [int] $PollMilliseconds = 100
    )

    $deadline = (Get-Date).AddMilliseconds($TimeoutMilliseconds)
    $lastReason = $null
    do {
        $toolCall = Invoke-ToolCall -Process $Process -Name 'windows.capture' -Arguments @{ scope = 'window' } -RequestName 'windows.capture(helper readiness after activate)'
        $result = $toolCall.Json.result
        if (-not [bool]$result.isError) {
            return [PSCustomObject]@{
                Id = $toolCall.Id
                RawRequest = $toolCall.RawRequest
                RawResponse = $toolCall.RawResponse
                Json = $toolCall.Json
                Payload = $result.structuredContent
                Image = Get-ImageBlock -ToolResponse $toolCall.Json
            }
        }

        $lastReason = [string]$result.structuredContent.reason
        Start-Sleep -Milliseconds $PollMilliseconds
    }
    while ((Get-Date) -lt $deadline)

    if ([string]::IsNullOrWhiteSpace($lastReason)) {
        throw 'Helper window did not become capturable after activation in time.'
    }

    throw "Helper window did not become capturable after activation in time. Last capture reason: $lastReason"
}

function Minimize-Window {
    param(
        [Parameter(Mandatory)]
        [int64] $Hwnd
    )

    return [WinBridgeSmoke.User32]::ShowWindowAsync([IntPtr]::new($Hwnd), 6)
}

function Test-IsIconic {
    param(
        [Parameter(Mandatory)]
        [int64] $Hwnd
    )

    return [WinBridgeSmoke.User32]::IsIconic([IntPtr]::new($Hwnd))
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
$helperProcess = $null

try {
    Invoke-NativeCommand -Description 'tool contract export for smoke' -Command {
        dotnet "$serverDll" --export-tool-contract-json "$contractPath"
    }

    $manifest = Get-Content $contractPath -Raw | ConvertFrom-Json
    $requiredTools = Get-RequiredToolNames -Manifest $manifest

    $helperTitle = "Okno Smoke Helper $runId"
    $helperProcess = Start-SmokeHelperWindow -ExecutablePath $helperExe -Title $helperTitle
    $helperHwnd = Wait-ForMainWindowHandle -Process $helperProcess

    $initializeCall = Invoke-McpRequest -Process $process -Method 'initialize' -Params @{
        protocolVersion = '2025-06-18'
        capabilities = @{}
        clientInfo = @{
            name = 'Okno.Smoke'
            version = '0.1.0'
        }
    } -RequestName 'initialize'
    $rawInitializeRequest = $initializeCall.RawRequest
    $initializeResponse = [PSCustomObject]@{
        Raw = $initializeCall.RawResponse
        Json = $initializeCall.Json
    }

    [void](Send-Json -Process $process -Payload @{
        jsonrpc = '2.0'
        method = 'notifications/initialized'
    })

    $listCall = Invoke-McpRequest -Process $process -Method 'tools/list' -Params @{} -RequestName 'tools/list'
    $rawListRequest = $listCall.RawRequest
    $listResponse = [PSCustomObject]@{
        Raw = $listCall.RawResponse
        Json = $listCall.Json
    }
    foreach ($requiredTool in $requiredTools) {
        Assert-Condition -Condition (@($listResponse.Json.result.tools | ForEach-Object { $_.name }) -contains $requiredTool) -Message "Required tool '$requiredTool' is missing from tools/list."
    }

    $healthCall = Invoke-ToolCall -Process $process -Name 'okno.health' -Arguments @{} -RequestName 'okno.health'
    $rawHealthRequest = $healthCall.RawRequest
    $healthResponse = [PSCustomObject]@{
        Raw = $healthCall.RawResponse
        Json = $healthCall.Json
    }
    $healthPayload = $healthCall.Payload
    Assert-Condition -Condition ($healthPayload.service -eq 'Okno') -Message "Health payload returned unexpected service name '$($healthPayload.service)'."

    $monitorsCall = Invoke-ToolCall -Process $process -Name 'windows.list_monitors' -Arguments @{} -RequestName 'windows.list_monitors'
    $rawMonitorsRequest = $monitorsCall.RawRequest
    $monitorsResponse = [PSCustomObject]@{
        Raw = $monitorsCall.RawResponse
        Json = $monitorsCall.Json
    }
    $monitorsPayload = $monitorsCall.Payload
    Assert-Condition -Condition ($monitorsPayload.count -gt 0) -Message 'Smoke requires at least one active monitor.'
    $primaryMonitorId = [string]$monitorsPayload.monitors[0].monitorId

    $visibleWindowResult = Wait-ForVisibleHelperWindow -Process $process -HelperHwnd $helperHwnd
    $rawWindowsRequest = $visibleWindowResult.RawRequest
    $windowsResponse = [PSCustomObject]@{
        Raw = $visibleWindowResult.RawResponse
        Json = $visibleWindowResult.Json
    }
    $windowsPayload = $visibleWindowResult.Payload
    Assert-Condition -Condition ($windowsPayload.count -gt 0) -Message 'Smoke requires at least one visible window to validate attach/session flow.'
    Assert-Condition -Condition (@($windowsPayload.windows | ForEach-Object { [int64]$_.hwnd }) -contains $helperHwnd) -Message 'Smoke helper window is missing from windows.list_windows payload.'

    $desktopCaptureCall = Invoke-ToolCall -Process $process -Name 'windows.capture' -Arguments @{
        scope = 'desktop'
        monitorId = $primaryMonitorId
    } -RequestName 'windows.capture(desktop monitorId)'
    $rawDesktopCaptureRequest = $desktopCaptureCall.RawRequest
    $desktopCaptureResponse = [PSCustomObject]@{
        Raw = $desktopCaptureCall.RawResponse
        Json = $desktopCaptureCall.Json
    }
    Assert-Condition -Condition (-not [bool]$desktopCaptureCall.Json.result.isError) -Message 'Desktop capture by explicit monitorId returned isError=true.'
    $desktopCapturePayload = $desktopCaptureCall.Json.result.structuredContent
    $desktopCaptureImage = Get-ImageBlock -ToolResponse $desktopCaptureCall.Json
    Assert-Condition -Condition ($desktopCapturePayload.scope -eq 'desktop') -Message "Desktop capture scope is '$($desktopCapturePayload.scope)', expected 'desktop'."
    Assert-Condition -Condition ($desktopCapturePayload.monitorId -eq $primaryMonitorId) -Message 'Desktop capture metadata does not preserve explicit monitorId.'
    Assert-Condition -Condition (-not [string]::IsNullOrWhiteSpace($desktopCaptureImage.data)) -Message 'Desktop capture image block does not contain PNG data.'

    $attachedWindow = $null
    $sessionPayload = $null
    $capturePayload = $null
    $captureImage = $null
    $activatePayload = $null
    $helperCapturePayload = $null
    $helperCaptureImage = $null

    $attachCall = Invoke-ToolCall -Process $process -Name 'windows.attach_window' -Arguments @{ hwnd = $helperHwnd } -RequestName 'windows.attach_window'
    $rawAttachRequest = $attachCall.RawRequest
    $attachResponse = [PSCustomObject]@{
        Raw = $attachCall.RawResponse
        Json = $attachCall.Json
    }
    $attachedWindow = $attachCall.Payload
    $attachStatus = [string]$attachedWindow.status
    Assert-Condition -Condition (@('done', 'already_attached') -contains $attachStatus) -Message "Attach helper window returned unexpected status '$attachStatus'."

    $sessionCall = Invoke-ToolCall -Process $process -Name 'okno.session_state' -Arguments @{} -RequestName 'okno.session_state'
    $rawSessionRequest = $sessionCall.RawRequest
    $sessionResponse = [PSCustomObject]@{
        Raw = $sessionCall.RawResponse
        Json = $sessionCall.Json
    }
    $sessionPayload = $sessionCall.Payload
    Assert-Condition -Condition ($sessionPayload.mode -eq 'window') -Message "Session snapshot mode is '$($sessionPayload.mode)', expected 'window'."
    Assert-Condition -Condition ([long]$sessionPayload.attachedWindow.window.hwnd -eq $helperHwnd) -Message 'Session snapshot does not point to the helper hwnd.'

    $captureCall = Invoke-ToolCall -Process $process -Name 'windows.capture' -Arguments @{ scope = 'window' } -RequestName 'windows.capture(window)'
    $rawCaptureRequest = $captureCall.RawRequest
    $captureResponse = [PSCustomObject]@{
        Raw = $captureCall.RawResponse
        Json = $captureCall.Json
    }
    Assert-Condition -Condition (-not [bool]$captureCall.Json.result.isError) -Message 'Helper window capture returned isError=true before minimize.'
    $capturePayload = $captureCall.Json.result.structuredContent
    $captureImage = Get-ImageBlock -ToolResponse $captureCall.Json
    Assert-Condition -Condition ($capturePayload.scope -eq 'window') -Message "Capture scope is '$($capturePayload.scope)', expected 'window'."
    Assert-Condition -Condition ([long]$capturePayload.hwnd -eq $helperHwnd) -Message 'Capture metadata hwnd does not match helper window.'
    Assert-Condition -Condition ([int]$capturePayload.pixelWidth -gt 0) -Message 'Capture metadata width must be positive.'
    Assert-Condition -Condition ([int]$capturePayload.pixelHeight -gt 0) -Message 'Capture metadata height must be positive.'
    Assert-Condition -Condition (-not [string]::IsNullOrWhiteSpace($captureImage.data)) -Message 'Capture image block does not contain PNG data.'
    Assert-Condition -Condition (Test-Path $capturePayload.artifactPath) -Message "Capture artifact '$($capturePayload.artifactPath)' was not created."

    Assert-Condition -Condition (Minimize-Window -Hwnd $helperHwnd) -Message 'Smoke helper window did not accept minimize request.'
    Assert-Condition -Condition (Wait-Until -Predicate { Test-IsIconic -Hwnd $helperHwnd }) -Message 'Smoke helper window did not become minimized in time.'

    $minimizedCaptureCall = Invoke-ToolCall -Process $process -Name 'windows.capture' -Arguments @{ scope = 'window' } -RequestName 'windows.capture(minimized helper window)'
    $rawMinimizedCaptureRequest = $minimizedCaptureCall.RawRequest
    $minimizedCaptureResponse = [PSCustomObject]@{
        Raw = $minimizedCaptureCall.RawResponse
        Json = $minimizedCaptureCall.Json
    }
    Assert-Condition -Condition ([bool]$minimizedCaptureCall.Json.result.isError) -Message 'Minimized helper window capture must return isError=true before activation.'
    $minimizedCapturePayload = $minimizedCaptureCall.Json.result.structuredContent
    Assert-Condition -Condition ([string]$minimizedCapturePayload.reason -like '*Свернутое окно*') -Message 'Minimized helper capture reason does not mention minimized-window policy.'

    $activateCall = Invoke-ToolCall -Process $process -Name 'windows.activate_window' -Arguments @{} -RequestName 'windows.activate_window'
    $rawActivateRequest = $activateCall.RawRequest
    $activateResponse = [PSCustomObject]@{
        Raw = $activateCall.RawResponse
        Json = $activateCall.Json
    }
    $activateResult = $activateCall.Json.result
    $activatePayload = $activateResult.structuredContent
    $activateStatus = [string]$activatePayload.status
    Assert-Condition -Condition (@('done', 'ambiguous') -contains $activateStatus) -Message "ActivateWindow returned unexpected status '$activateStatus'."
    Assert-Condition -Condition ([bool]$activateResult.isError -eq ($activateStatus -eq 'ambiguous')) -Message 'ActivateWindow isError does not match done/ambiguous semantics.'
    Assert-Condition -Condition ([bool]$activatePayload.wasMinimized) -Message 'ActivateWindow payload must report wasMinimized=true for helper window.'
    Assert-Condition -Condition ([long]$activatePayload.window.hwnd -eq $helperHwnd) -Message 'ActivateWindow payload hwnd does not match helper window.'
    Assert-Condition -Condition ([bool]$activatePayload.isForeground -eq ($activateStatus -eq 'done')) -Message 'ActivateWindow payload isForeground does not match done/ambiguous semantics.'
    $helperCaptureResult = Wait-ForSuccessfulWindowCapture -Process $process
    $rawHelperCaptureRequest = $helperCaptureResult.RawRequest
    $helperCaptureResponse = [PSCustomObject]@{
        Raw = $helperCaptureResult.RawResponse
        Json = $helperCaptureResult.Json
    }
    $helperCapturePayload = $helperCaptureResult.Payload
    $helperCaptureImage = $helperCaptureResult.Image
    Assert-Condition -Condition ([long]$helperCapturePayload.hwnd -eq $helperHwnd) -Message 'Helper capture metadata hwnd does not match helper window.'
    Assert-Condition -Condition (-not [string]::IsNullOrWhiteSpace($helperCaptureImage.data)) -Message 'Helper capture image block does not contain PNG data.'

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
        monitors = $monitorsPayload
        windows = $windowsPayload
        attached_window = $attachedWindow
        session = $sessionPayload
        desktop_capture = $desktopCapturePayload
        capture = $capturePayload
        helper_window = [ordered]@{
            hwnd = $helperHwnd
            title = $helperTitle
        }
        helper_activate = $activatePayload
        helper_capture = $helperCapturePayload
        raw_requests = [ordered]@{
            initialize = $rawInitializeRequest
            list_tools = $rawListRequest
            health = $rawHealthRequest
            list_monitors = $rawMonitorsRequest
            list_windows = $rawWindowsRequest
            desktop_capture = $rawDesktopCaptureRequest
            attach_window = $rawAttachRequest
            session_state = $rawSessionRequest
            capture = $rawCaptureRequest
            activate_window = $rawActivateRequest
            helper_window_capture = $rawHelperCaptureRequest
        }
        raw_responses = [ordered]@{
            initialize = $initializeResponse.Raw
            list_tools = $listResponse.Raw
            health = $healthResponse.Raw
            list_monitors = $monitorsResponse.Raw
            list_windows = $windowsResponse.Raw
            desktop_capture = $desktopCaptureResponse.Raw
            attach_window = $attachResponse.Raw
            session_state = $sessionResponse.Raw
            capture = $captureResponse.Raw
            activate_window = $activateResponse.Raw
            helper_window_capture = $helperCaptureResponse.Raw
        }
        stderr = $stderr
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
        "- monitor_count: $($monitorsPayload.count)",
        "- desktop_monitor_id: $primaryMonitorId",
        "- visible_windows: $($windowsPayload.count)",
        "- attached_hwnd: $attachedHwnd",
        "- capture_artifact: $($capturePayload.artifactPath)",
        "- helper_hwnd: $helperHwnd",
        "- helper_activation_status: $($activatePayload.status)",
        "- helper_capture_artifact: $($helperCapturePayload.artifactPath)",
        "- audit_dir: $($healthPayload.artifactsDirectory)",
        "- report: $reportPath"
    ) -join [Environment]::NewLine

    $summary | Set-Content -Path $summaryPath -Encoding utf8

    Get-Content $summaryPath
}
finally {
    if ($null -ne $helperProcess -and -not $helperProcess.HasExited) {
        $helperProcess.Kill()
        $helperProcess.WaitForExit()
    }

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
