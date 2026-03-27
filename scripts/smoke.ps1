. (Join-Path $PSScriptRoot 'common.ps1')

$repoRoot = Get-RepoRoot -ScriptRoot $PSScriptRoot
Set-Location $repoRoot

$runId = Get-Date -Format 'yyyyMMddTHHmmssfff'
$artifactRoot = Join-Path $repoRoot "artifacts\\smoke\\$runId"
$reportPath = Join-Path $artifactRoot 'report.json'
$summaryPath = Join-Path $artifactRoot 'summary.md'
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
        public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool ShowWindowAsync(IntPtr hwnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hwnd);
    }
}
'@
}

$wmAppArmElementGone = 0x8001
$wmAppPrepareFocus = 0x8002
$waitTimeoutForegroundMs = 1500
$waitTimeoutSemanticUiMs = 3000
$waitTimeoutElementGoneMs = 5000
$waitTimeoutVisualMs = 6000
$expectedHealthDomains = @('desktop_session', 'session_alignment', 'integrity', 'uiaccess')
$expectedHealthCapabilities = @('capture', 'uia', 'wait', 'input', 'clipboard', 'launch')
$allowedGuardStatuses = @('ready', 'degraded', 'blocked', 'unknown')
$allowedGuardSeverities = @('info', 'warning', 'blocked')
$expectedAuditSchemaVersion = '1.0.0'
$allowedDisplayIdentityModes = @('display_config_strong', 'gdi_fallback')
$allowedDisplayIdentityFailureStages = @('display_config_coverage_gap', 'get_monitor_info', 'get_buffer_sizes', 'query_display_config', 'get_source_name', 'get_target_name')

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

function Join-ContractValues {
    param(
        [Parameter(Mandatory)]
        [object[]] $Values
    )

    return [string]::Join(',', @($Values | ForEach-Object { [string]$_ }))
}

function Get-HealthStatusDigest {
    param(
        [Parameter(Mandatory)]
        [object[]] $Items,
        [Parameter(Mandatory)]
        [string] $NameProperty
    )

    return (@(
            $Items | ForEach-Object {
                $name = [string]$_.PSObject.Properties[$NameProperty].Value
                $status = [string]$_.status
                "$name=$status"
            }) -join ', ')
}

function Get-HealthReasonSignature {
    param(
        [Parameter(Mandatory)]
        [object] $Reason
    )

    return [string]::Join(
        [string][char]31,
        @(
            [string]$Reason.source,
            [string]$Reason.code,
            [string]$Reason.severity,
            [string]$Reason.messageHuman
        ))
}

function Get-HealthReasonSignatures {
    param(
        [Parameter(Mandatory)]
        [object[]] $Reasons
    )

    return @($Reasons | ForEach-Object { Get-HealthReasonSignature -Reason $_ })
}

function Get-ContractMapSignatures {
    param(
        [Parameter(Mandatory)]
        [object] $Map
    )

    return @(
        $Map.PSObject.Properties |
            Sort-Object Name |
            ForEach-Object { '{0}={1}' -f [string]$_.Name, [string]$_.Value })
}

function Get-HealthBlockedProjection {
    param(
        [Parameter(Mandatory)]
        [object] $Payload
    )

    return @($Payload.readiness.capabilities | Where-Object { [string]$_.status -eq 'blocked' })
}

function Get-HealthWarningProjection {
    param(
        [Parameter(Mandatory)]
        [object] $Payload
    )

    $domainWarnings = @(
        $Payload.readiness.domains |
            ForEach-Object { @($_.reasons) } |
            Where-Object { [string]$_.severity -eq 'warning' })
    $capabilityWarnings = @(
        $Payload.readiness.capabilities |
            Where-Object { [string]$_.status -ne 'blocked' } |
            ForEach-Object { @($_.reasons) } |
            Where-Object { [string]$_.severity -eq 'warning' })

    return @($domainWarnings + $capabilityWarnings)
}

function Assert-ReasonList {
    param(
        [Parameter(Mandatory)]
        [object[]] $Reasons,
        [Parameter(Mandatory)]
        [string] $ExpectedSource
    )

    Assert-Condition -Condition ($Reasons.Count -gt 0) -Message "Health contract for '$ExpectedSource' must contain at least one reason."
    foreach ($reason in $Reasons) {
        Assert-Condition -Condition (-not [string]::IsNullOrWhiteSpace([string]$reason.code)) -Message "Health reason for '$ExpectedSource' is missing code."
        Assert-Condition -Condition ($allowedGuardSeverities -contains [string]$reason.severity) -Message "Health reason for '$ExpectedSource' returned unexpected severity '$($reason.severity)'."
        Assert-Condition -Condition ([string]$reason.source -eq $ExpectedSource) -Message "Health reason for '$ExpectedSource' returned unexpected source '$($reason.source)'."
        Assert-Condition -Condition (-not [string]::IsNullOrWhiteSpace([string]$reason.messageHuman)) -Message "Health reason for '$ExpectedSource' is missing human message."
    }
}

function Assert-HealthTopLevelContract {
    param(
        [Parameter(Mandatory)]
        [object] $Payload,
        [Parameter(Mandatory)]
        [object] $Manifest
    )

    Assert-Condition -Condition ($Payload.service -eq 'Okno') -Message "Health payload returned unexpected service name '$($Payload.service)'."
    Assert-Condition -Condition (-not [string]::IsNullOrWhiteSpace([string]$Payload.version)) -Message 'Health payload did not return version.'
    Assert-Condition -Condition ([string]$Payload.transport -eq [string]$Manifest.transport.kind) -Message "Health payload returned unexpected transport '$($Payload.transport)'."
    Assert-Condition -Condition ([string]$Payload.auditSchemaVersion -eq $expectedAuditSchemaVersion) -Message "Health payload returned unexpected auditSchemaVersion '$($Payload.auditSchemaVersion)'."
    Assert-Condition -Condition (-not [string]::IsNullOrWhiteSpace([string]$Payload.runId)) -Message 'Health payload did not return runId.'
    Assert-Condition -Condition (-not [string]::IsNullOrWhiteSpace([string]$Payload.artifactsDirectory)) -Message 'Health payload did not return artifactsDirectory.'
    Assert-Condition -Condition (Test-Path ([string]$Payload.artifactsDirectory)) -Message "Health artifacts directory '$($Payload.artifactsDirectory)' does not exist."
    Assert-Condition -Condition ($Payload.activeMonitorCount -gt 0) -Message 'Health payload reported non-positive activeMonitorCount.'
    Assert-DisplayIdentityContract -DisplayIdentity $Payload.displayIdentity
    Assert-Condition -Condition (-not ($Payload.PSObject.Properties.Name -contains 'artifactPath')) -Message 'Health payload unexpectedly advertises artifactPath although no dedicated health artifact is materialized.'

    $implementedTools = @($Payload.implementedTools | ForEach-Object { [string]$_ })
    $expectedImplementedTools = @($Manifest.tools.implemented_names | ForEach-Object { [string]$_ })
    Assert-Condition -Condition ((Join-ContractValues -Values $implementedTools) -eq (Join-ContractValues -Values $expectedImplementedTools)) -Message 'Health implementedTools diverge from current manifest.'

    $deferredToolSignatures = Get-ContractMapSignatures -Map $Payload.deferredTools
    $expectedDeferredToolSignatures = Get-ContractMapSignatures -Map $Manifest.tools.deferred_phase_map
    Assert-Condition -Condition ((Join-ContractValues -Values $deferredToolSignatures) -eq (Join-ContractValues -Values $expectedDeferredToolSignatures)) -Message 'Health deferredTools diverge from current manifest.'
}

function Assert-DisplayIdentityContract {
    param(
        [Parameter(Mandatory)]
        [object] $DisplayIdentity
    )

    Assert-Condition -Condition ($allowedDisplayIdentityModes -contains [string]$DisplayIdentity.identityMode) -Message "Health payload returned unexpected displayIdentity.identityMode '$($DisplayIdentity.identityMode)'."
    Assert-Condition -Condition (-not [string]::IsNullOrWhiteSpace([string]$DisplayIdentity.messageHuman)) -Message 'Health payload did not return displayIdentity.messageHuman.'
    Assert-Condition -Condition (-not [string]::IsNullOrWhiteSpace([string]$DisplayIdentity.capturedAtUtc)) -Message 'Health payload did not return displayIdentity.capturedAtUtc.'

    $hasFailedStage = ($DisplayIdentity.PSObject.Properties.Name -contains 'failedStage') -and ($null -ne $DisplayIdentity.failedStage)
    $hasErrorCode = ($DisplayIdentity.PSObject.Properties.Name -contains 'errorCode') -and ($null -ne $DisplayIdentity.errorCode)
    $hasErrorName = ($DisplayIdentity.PSObject.Properties.Name -contains 'errorName') -and ($null -ne $DisplayIdentity.errorName)

    if (-not $hasFailedStage) {
        Assert-Condition -Condition (-not $hasErrorCode) -Message 'Health displayIdentity returned errorCode without failedStage.'
        Assert-Condition -Condition (-not $hasErrorName) -Message 'Health displayIdentity returned errorName without failedStage.'
        return
    }

    Assert-Condition -Condition ($allowedDisplayIdentityFailureStages -contains [string]$DisplayIdentity.failedStage) -Message "Health payload returned unexpected displayIdentity.failedStage '$($DisplayIdentity.failedStage)'."
    if ([string]$DisplayIdentity.failedStage -eq 'display_config_coverage_gap') {
        Assert-Condition -Condition (-not $hasErrorCode) -Message 'Health displayIdentity returned errorCode for display_config_coverage_gap.'
        Assert-Condition -Condition (-not $hasErrorName) -Message 'Health displayIdentity returned errorName for display_config_coverage_gap.'
        return
    }

    Assert-Condition -Condition $hasErrorCode -Message "Health displayIdentity stage '$($DisplayIdentity.failedStage)' must publish errorCode."
    Assert-Condition -Condition $hasErrorName -Message "Health displayIdentity stage '$($DisplayIdentity.failedStage)' must publish errorName."
    if ($hasErrorName) {
        Assert-Condition -Condition (-not [string]::IsNullOrWhiteSpace([string]$DisplayIdentity.errorName)) -Message 'Health displayIdentity returned empty errorName.'
    }
}

function Assert-HealthTopologyConsistency {
    param(
        [Parameter(Mandatory)]
        [object] $HealthPayload,
        [Parameter(Mandatory)]
        [object] $MonitorsPayload
    )

    Assert-Condition -Condition ([int]$HealthPayload.activeMonitorCount -eq [int]$MonitorsPayload.count) -Message 'Health activeMonitorCount diverges from windows.list_monitors count.'
    Assert-Condition -Condition ([string]$HealthPayload.displayIdentity.identityMode -eq [string]$MonitorsPayload.diagnostics.identityMode) -Message 'Health displayIdentity.identityMode diverges from windows.list_monitors diagnostics.identityMode.'
}

function Assert-BlockedCapabilitiesProjection {
    param(
        [Parameter(Mandatory)]
        [object] $Payload
    )

    $expectedBlockedCapabilities = @(Get-HealthBlockedProjection -Payload $Payload)
    $actualBlockedCapabilities = @($Payload.blockedCapabilities)
    $expectedNames = @($expectedBlockedCapabilities | ForEach-Object { [string]$_.capability })
    $actualNames = @($actualBlockedCapabilities | ForEach-Object { [string]$_.capability })

    Assert-Condition -Condition ((Join-ContractValues -Values $actualNames) -eq (Join-ContractValues -Values $expectedNames)) -Message "Health blockedCapabilities do not match readiness blocked subset: '$((Join-ContractValues -Values $actualNames))'."

    foreach ($expectedCapability in $expectedBlockedCapabilities) {
        $capabilityName = [string]$expectedCapability.capability
        $actualCapability = @($actualBlockedCapabilities | Where-Object { [string]$_.capability -eq $capabilityName })
        Assert-Condition -Condition ($actualCapability.Count -eq 1) -Message "Health blockedCapabilities projection for '$capabilityName' is missing or duplicated."
        Assert-Condition -Condition ([string]$actualCapability[0].status -eq 'blocked') -Message "Health blockedCapabilities projection for '$capabilityName' returned non-blocked status '$($actualCapability[0].status)'."
        Assert-Condition -Condition ((Join-ContractValues -Values (Get-HealthReasonSignatures -Reasons @($actualCapability[0].reasons))) -eq (Join-ContractValues -Values (Get-HealthReasonSignatures -Reasons @($expectedCapability.reasons)))) -Message "Health blockedCapabilities reasons diverge from readiness projection for '$capabilityName'."
    }
}

function Assert-WarningsProjection {
    param(
        [Parameter(Mandatory)]
        [object] $Payload
    )

    $expectedWarnings = @(Get-HealthWarningProjection -Payload $Payload)
    $actualWarnings = @($Payload.warnings)
    Assert-Condition -Condition ((Join-ContractValues -Values (Get-HealthReasonSignatures -Reasons $actualWarnings)) -eq (Join-ContractValues -Values (Get-HealthReasonSignatures -Reasons $expectedWarnings))) -Message 'Health warnings do not mirror warning projection from readiness snapshot.'
}

function Assert-HealthPayload {
    param(
        [Parameter(Mandatory)]
        [object] $ToolCall,
        [Parameter(Mandatory)]
        [object] $Payload,
        [Parameter(Mandatory)]
        [object] $Manifest
    )

    Assert-Condition -Condition (-not [bool]$ToolCall.Json.result.isError) -Message 'okno.health returned isError=true despite successful snapshot construction.'
    Assert-HealthTopLevelContract -Payload $Payload -Manifest $Manifest
    Assert-Condition -Condition ($Payload.PSObject.Properties.Name -contains 'readiness') -Message 'Health payload is missing readiness snapshot.'
    Assert-Condition -Condition ($Payload.PSObject.Properties.Name -contains 'blockedCapabilities') -Message 'Health payload is missing blockedCapabilities.'
    Assert-Condition -Condition ($Payload.PSObject.Properties.Name -contains 'warnings') -Message 'Health payload is missing warnings.'
    Assert-Condition -Condition (-not [string]::IsNullOrWhiteSpace([string]$Payload.readiness.capturedAtUtc)) -Message 'Health readiness snapshot is missing capturedAtUtc.'

    $domainNames = @($Payload.readiness.domains | ForEach-Object { [string]$_.domain })
    $capabilityNames = @($Payload.readiness.capabilities | ForEach-Object { [string]$_.capability })
    Assert-Condition -Condition ((Join-ContractValues -Values $domainNames) -eq (Join-ContractValues -Values $expectedHealthDomains)) -Message "Health domains differ from canonical contract: '$((Join-ContractValues -Values $domainNames))'."
    Assert-Condition -Condition ((Join-ContractValues -Values $capabilityNames) -eq (Join-ContractValues -Values $expectedHealthCapabilities)) -Message "Health capabilities differ from canonical contract: '$((Join-ContractValues -Values $capabilityNames))'."

    foreach ($domain in @($Payload.readiness.domains)) {
        $domainName = [string]$domain.domain
        Assert-Condition -Condition ($allowedGuardStatuses -contains [string]$domain.status) -Message "Health domain '$domainName' returned unexpected status '$($domain.status)'."
        Assert-ReasonList -Reasons @($domain.reasons) -ExpectedSource $domainName
    }

    foreach ($capability in @($Payload.readiness.capabilities)) {
        $capabilityName = [string]$capability.capability
        Assert-Condition -Condition ($allowedGuardStatuses -contains [string]$capability.status) -Message "Health capability '$capabilityName' returned unexpected status '$($capability.status)'."
        Assert-ReasonList -Reasons @($capability.reasons) -ExpectedSource $capabilityName
    }

    $knownWarningSources = @($expectedHealthDomains + $expectedHealthCapabilities)
    foreach ($warning in @($Payload.warnings)) {
        Assert-Condition -Condition ([string]$warning.severity -eq 'warning') -Message "Health warnings list returned non-warning severity '$($warning.severity)'."
        Assert-Condition -Condition ($knownWarningSources -contains [string]$warning.source) -Message "Health warnings list returned unknown source '$($warning.source)'."
        Assert-Condition -Condition (-not [string]::IsNullOrWhiteSpace([string]$warning.code)) -Message "Health warnings list contains warning without code."
        Assert-Condition -Condition (-not [string]::IsNullOrWhiteSpace([string]$warning.messageHuman)) -Message "Health warnings list contains warning without message."
    }

    Assert-BlockedCapabilitiesProjection -Payload $Payload
    Assert-WarningsProjection -Payload $Payload
}

function Assert-HealthObservabilityContract {
    param(
        [Parameter(Mandatory)]
        [object] $Payload
    )

    $eventsPath = Join-Path ([string]$Payload.artifactsDirectory) 'events.jsonl'
    Assert-Condition -Condition (Test-Path $eventsPath) -Message "Health observability contract expected events file '$eventsPath'."

    $allRunEvents = @(
        Get-Content $eventsPath |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            ForEach-Object { $_ | ConvertFrom-Json })

    $healthEvents = @(
        $allRunEvents |
            Where-Object { [string]$_.tool_name -eq 'okno.health' })

    $genericHealthEvents = @($healthEvents | ForEach-Object { [string]$_.event_name })
    Assert-Condition -Condition ($genericHealthEvents -contains 'tool.invocation.started') -Message 'Health observability contract is missing generic tool.invocation.started event.'
    Assert-Condition -Condition ($genericHealthEvents -contains 'tool.invocation.completed') -Message 'Health observability contract is missing generic tool.invocation.completed event.'

    $unexpectedHealthEvents = @(
        $allRunEvents |
            Where-Object {
                $eventName = [string]$_.event_name
                ($eventName -like 'health.*') -or ($eventName -like 'guard.runtime.*')
            })

    Assert-Condition -Condition ($unexpectedHealthEvents.Count -eq 0) -Message 'Health observability contract unexpectedly materialized dedicated runtime events.'
}

function Write-SmokeArtifacts {
    param(
        [Parameter(Mandatory)]
        [object] $Report,
        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string[]] $SummaryLines
    )

    $Report | ConvertTo-Json -Depth 20 | Set-Content -Path $reportPath -Encoding utf8
    ($SummaryLines -join [Environment]::NewLine) | Set-Content -Path $summaryPath -Encoding utf8
}

function Find-UiaNodes {
    param(
        [Parameter(Mandatory)]
        [object] $Node,
        [string] $ControlType,
        [string] $Name
    )

    if ($null -eq $Node) {
        return @()
    }

    $matches = @()
    $nodeControlType = if ($null -ne $Node.controlType) { [string]$Node.controlType } else { $null }
    $nodeName = if ($null -ne $Node.name) { [string]$Node.name } else { $null }

    if (([string]::IsNullOrWhiteSpace($ControlType) -or $nodeControlType -eq $ControlType) -and
        ([string]::IsNullOrWhiteSpace($Name) -or $nodeName -eq $Name)) {
        $matches += $Node
    }

    foreach ($child in @($Node.children)) {
        $matches += @(Find-UiaNodes -Node $child -ControlType $ControlType -Name $Name)
    }

    return $matches
}

function Test-UiaSemanticSubtreeReady {
    param(
        [Parameter(Mandatory)]
        [object] $Payload
    )

    if ($null -eq $Payload.root) {
        return $false
    }

    return (@(Find-UiaNodes -Node $Payload.root -ControlType 'button' -Name 'Run semantic smoke').Count -gt 0) -and
        (@(Find-UiaNodes -Node $Payload.root -ControlType 'check_box' -Name 'Remember semantic selection').Count -gt 0) -and
        (@(Find-UiaNodes -Node $Payload.root -ControlType 'edit' -Name 'Smoke query input').Count -gt 0) -and
        (@(Find-UiaNodes -Node $Payload.root -ControlType 'tree_item' -Name 'Inbox').Count -gt 0)
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

function Invoke-WaitToolCall {
    param(
        [Parameter(Mandatory)]
        [System.Diagnostics.Process] $Process,
        [Parameter(Mandatory)]
        [string] $Condition,
        [hashtable] $Selector,
        [string] $ExpectedText,
        [object] $Hwnd,
        [int] $TimeoutMs = 3000,
        [Parameter(Mandatory)]
        [string] $RequestName
    )

    $arguments = @{
        condition = $Condition
        timeoutMs = $TimeoutMs
    }

    if ($null -ne $Selector -and $Selector.Count -gt 0) {
        $arguments.selector = $Selector
    }

    if (-not [string]::IsNullOrWhiteSpace($ExpectedText)) {
        $arguments.expectedText = $ExpectedText
    }

    if ($null -ne $Hwnd) {
        $arguments.hwnd = [int64]$Hwnd
    }

    $toolCall = Invoke-ToolCall -Process $Process -Name 'windows.wait' -Arguments $arguments -RequestName $RequestName
    return [PSCustomObject]@{
        Id = $toolCall.Id
        RawRequest = $toolCall.RawRequest
        RawResponse = $toolCall.RawResponse
        Json = $toolCall.Json
        Payload = $toolCall.Json.result.structuredContent
    }
}

function Assert-WaitSuccess {
    param(
        [Parameter(Mandatory)]
        [object] $ToolCall,
        [Parameter(Mandatory)]
        [string] $Condition
    )

    $result = $ToolCall.Json.result
    Assert-Condition -Condition (-not [bool]$result.isError) -Message "windows.wait($Condition) returned isError=true."
    Assert-Condition -Condition (@($result.content).Count -eq 1) -Message "windows.wait($Condition) must return exactly one text content block."
    Assert-Condition -Condition ($result.content[0].type -eq 'text') -Message "windows.wait($Condition) content block must be text-only."

    $payload = $result.structuredContent
    Assert-Condition -Condition ([string]$payload.status -eq 'done') -Message "windows.wait($Condition) returned status '$($payload.status)' instead of 'done'."
    Assert-Condition -Condition ([string]$payload.condition -eq $Condition) -Message "windows.wait($Condition) payload condition drifted to '$($payload.condition)'."
    Assert-Condition -Condition (-not [string]::IsNullOrWhiteSpace([string]$payload.artifactPath)) -Message "windows.wait($Condition) did not return artifactPath."
    Assert-Condition -Condition (Test-Path $payload.artifactPath) -Message "windows.wait($Condition) artifact '$($payload.artifactPath)' was not created."
    return $payload
}

function Wait-ForSemanticUiaSnapshot {
    param(
        [Parameter(Mandatory)]
        [System.Diagnostics.Process] $Process,
        [int] $TimeoutMilliseconds = 10000,
        [int] $PollMilliseconds = 100
    )

    $deadline = (Get-Date).AddMilliseconds($TimeoutMilliseconds)
    $lastStatus = $null
    $lastArtifactPath = $null

    do {
        $toolCall = Invoke-ToolCall -Process $Process -Name 'windows.uia_snapshot' -Arguments @{
            depth = 5
            maxNodes = 128
        } -RequestName 'windows.uia_snapshot(attached helper window)'
        $result = $toolCall.Json.result
        $payload = $result.structuredContent

        if ((-not [bool]$result.isError) -and (Test-UiaSemanticSubtreeReady -Payload $payload)) {
            return [PSCustomObject]@{
                Id = $toolCall.Id
                RawRequest = $toolCall.RawRequest
                RawResponse = $toolCall.RawResponse
                Json = $toolCall.Json
                Payload = $payload
            }
        }

        $lastStatus = [string]$payload.status
        $lastArtifactPath = [string]$payload.artifactPath
        Start-Sleep -Milliseconds $PollMilliseconds
    }
    while ((Get-Date) -lt $deadline)

    throw "UIA semantic subtree did not materialize in time. Last status: $lastStatus. Last artifact: $lastArtifactPath"
}

function Minimize-Window {
    param(
        [Parameter(Mandatory)]
        [int64] $Hwnd
    )

    return [WinBridgeSmoke.User32]::ShowWindowAsync([IntPtr]::new($Hwnd), 6)
}

function Send-HelperCommand {
    param(
        [Parameter(Mandatory)]
        [int64] $Hwnd,
        [Parameter(Mandatory)]
        [uint32] $Message,
        [Parameter(Mandatory)]
        [string] $Description
    )

    $posted = [WinBridgeSmoke.User32]::PostMessage([IntPtr]::new($Hwnd), $Message, [IntPtr]::Zero, [IntPtr]::Zero)
    Assert-Condition -Condition $posted -Message "Smoke helper command '$Description' was not delivered."
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
    Assert-HealthPayload -ToolCall $healthCall -Payload $healthPayload -Manifest $manifest

    Write-SmokeArtifacts -Report ([ordered]@{
            run_id = $runId
            initialized_protocol = $initializeResponse.Json.result.protocolVersion
            server_name = $initializeResponse.Json.result.serverInfo.name
            tool_contract = $manifest
            declared_tools = @($listResponse.Json.result.tools | ForEach-Object { $_.name })
            health = $healthPayload
            health_digest = [ordered]@{
                domain_statuses = Get-HealthStatusDigest -Items @($healthPayload.readiness.domains) -NameProperty 'domain'
                capability_statuses = Get-HealthStatusDigest -Items @($healthPayload.readiness.capabilities) -NameProperty 'capability'
                blocked_capabilities = @($healthPayload.blockedCapabilities | ForEach-Object { [string]$_.capability })
                warning_codes = @($healthPayload.warnings | ForEach-Object { [string]$_.code })
                artifact_materialized = $false
                dedicated_runtime_event_validation = 'pending'
            }
            raw_requests = [ordered]@{
                initialize = $rawInitializeRequest
                list_tools = $rawListRequest
                health = $rawHealthRequest
            }
            raw_responses = [ordered]@{
                initialize = $initializeResponse.Raw
                list_tools = $listResponse.Raw
                health = $healthResponse.Raw
            }
            status = 'partial_after_health'
        }) -SummaryLines @(
            '# Okno smoke summary',
            '',
            "- run_id: $runId",
            "- server: $($initializeResponse.Json.result.serverInfo.name)",
            "- protocol: $($initializeResponse.Json.result.protocolVersion)",
            "- declared_tools: $(@($listResponse.Json.result.tools).Count)",
            "- health_domains: $(Get-HealthStatusDigest -Items @($healthPayload.readiness.domains) -NameProperty 'domain')",
            "- health_capabilities: $(Get-HealthStatusDigest -Items @($healthPayload.readiness.capabilities) -NameProperty 'capability')",
            "- health_blocked_capabilities: $((@($healthPayload.blockedCapabilities | ForEach-Object { [string]$_.capability }) -join ', '))",
            "- health_warning_codes: $((@($healthPayload.warnings | ForEach-Object { [string]$_.code }) -join ', '))",
            "- health_artifact: not_materialized",
            "- health_event: pending_final_validation",
            "- status: partial_after_health",
            "- report: $reportPath")

    $monitorsCall = Invoke-ToolCall -Process $process -Name 'windows.list_monitors' -Arguments @{} -RequestName 'windows.list_monitors'
    $rawMonitorsRequest = $monitorsCall.RawRequest
    $monitorsResponse = [PSCustomObject]@{
        Raw = $monitorsCall.RawResponse
        Json = $monitorsCall.Json
    }
    $monitorsPayload = $monitorsCall.Payload
    Assert-Condition -Condition ($monitorsPayload.count -gt 0) -Message 'Smoke requires at least one active monitor.'
    Assert-HealthTopologyConsistency -HealthPayload $healthPayload -MonitorsPayload $monitorsPayload
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
    $uiaSnapshotPayload = $null
    $capturePayload = $null
    $captureImage = $null
    $activatePayload = $null
    $helperCapturePayload = $null
    $helperCaptureImage = $null
    $activeWaitPayload = $null
    $focusWaitPayload = $null
    $elementWaitPayload = $null
    $transientExistsWaitPayload = $null
    $textWaitPayload = $null
    $elementGoneWaitPayload = $null
    $visualWaitPayload = $null

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

    $uiaSnapshotCall = Wait-ForSemanticUiaSnapshot -Process $process
    $rawUiaSnapshotRequest = $uiaSnapshotCall.RawRequest
    $uiaSnapshotResponse = [PSCustomObject]@{
        Raw = $uiaSnapshotCall.RawResponse
        Json = $uiaSnapshotCall.Json
    }
    Assert-Condition -Condition (-not [bool]$uiaSnapshotCall.Json.result.isError) -Message 'UIA snapshot for attached helper window returned isError=true.'
    Assert-Condition -Condition (@($uiaSnapshotCall.Json.result.content).Count -eq 1) -Message 'UIA snapshot must return exactly one text content block.'
    Assert-Condition -Condition ($uiaSnapshotCall.Json.result.content[0].type -eq 'text') -Message 'UIA snapshot content block must be text-only.'
    Assert-Condition -Condition (@($uiaSnapshotCall.Json.result.content | Where-Object { $_.type -eq 'image' }).Count -eq 0) -Message 'UIA snapshot must not return image content blocks.'
    $uiaSnapshotPayload = $uiaSnapshotCall.Json.result.structuredContent
    Assert-Condition -Condition ($uiaSnapshotPayload.status -eq 'done') -Message "UIA snapshot returned unexpected status '$($uiaSnapshotPayload.status)'."
    Assert-Condition -Condition ($uiaSnapshotPayload.targetSource -eq 'attached') -Message "UIA snapshot target source is '$($uiaSnapshotPayload.targetSource)', expected 'attached'."
    Assert-Condition -Condition ([long]$uiaSnapshotPayload.window.hwnd -eq $helperHwnd) -Message 'UIA snapshot hwnd does not match helper window.'
    Assert-Condition -Condition ([int]$uiaSnapshotPayload.requestedDepth -eq 5) -Message 'UIA snapshot did not preserve requested depth.'
    Assert-Condition -Condition ([int]$uiaSnapshotPayload.requestedMaxNodes -eq 128) -Message 'UIA snapshot did not preserve requested maxNodes.'
    Assert-Condition -Condition (Test-Path $uiaSnapshotPayload.artifactPath) -Message "UIA snapshot artifact '$($uiaSnapshotPayload.artifactPath)' was not created."

    $smokeButtonNodes = @(Find-UiaNodes -Node $uiaSnapshotPayload.root -ControlType 'button' -Name 'Run semantic smoke')
    Assert-Condition -Condition ($smokeButtonNodes.Count -gt 0) -Message 'UIA snapshot did not include expected smoke button.'
    Assert-Condition -Condition (@($smokeButtonNodes[0].patterns) -contains 'invoke') -Message 'UIA snapshot smoke button does not expose invoke pattern.'
    $smokeCheckboxNodes = @(Find-UiaNodes -Node $uiaSnapshotPayload.root -ControlType 'check_box' -Name 'Remember semantic selection')
    Assert-Condition -Condition ($smokeCheckboxNodes.Count -gt 0) -Message 'UIA snapshot did not include expected smoke checkbox.'
    Assert-Condition -Condition (@($smokeCheckboxNodes[0].patterns) -contains 'toggle') -Message 'UIA snapshot smoke checkbox does not expose toggle pattern.'
    $smokeEditNodes = @(Find-UiaNodes -Node $uiaSnapshotPayload.root -ControlType 'edit' -Name 'Smoke query input')
    Assert-Condition -Condition ($smokeEditNodes.Count -gt 0) -Message 'UIA snapshot did not include expected smoke edit control.'
    $editPatterns = @($smokeEditNodes[0].patterns)
    Assert-Condition -Condition (($editPatterns -contains 'value') -or ($editPatterns -contains 'text')) -Message 'UIA snapshot smoke edit control does not expose value/text pattern.'
    Assert-Condition -Condition (@(Find-UiaNodes -Node $uiaSnapshotPayload.root -ControlType 'tree' -Name 'Smoke navigation tree').Count -gt 0) -Message 'UIA snapshot did not include expected smoke tree.'
    Assert-Condition -Condition (@(Find-UiaNodes -Node $uiaSnapshotPayload.root -ControlType 'tree_item' -Name 'Workspace').Count -gt 0) -Message 'UIA snapshot did not include expected Workspace tree item.'
    Assert-Condition -Condition (@(Find-UiaNodes -Node $uiaSnapshotPayload.root -ControlType 'tree_item' -Name 'Inbox').Count -gt 0) -Message 'UIA snapshot did not include expected Inbox tree item.'

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

    $activeWaitCall = Invoke-WaitToolCall -Process $process -Condition 'active_window_matches' -TimeoutMs $waitTimeoutForegroundMs -RequestName 'windows.wait(active_window_matches)'
    $rawActiveWaitRequest = $activeWaitCall.RawRequest
    $activeWaitResponse = [PSCustomObject]@{
        Raw = $activeWaitCall.RawResponse
        Json = $activeWaitCall.Json
    }
    $activeWaitPayload = Assert-WaitSuccess -ToolCall $activeWaitCall -Condition 'active_window_matches'
    Assert-Condition -Condition ([bool]$activeWaitPayload.lastObserved.targetIsForeground) -Message 'active_window_matches must confirm foreground=true in lastObserved.'

    Send-HelperCommand -Hwnd $helperHwnd -Message $wmAppPrepareFocus -Description 'prepare_focus'
    $focusWaitCall = Invoke-WaitToolCall -Process $process -Condition 'focus_is' -Selector @{ name = 'Run semantic smoke'; controlType = 'button' } -TimeoutMs $waitTimeoutSemanticUiMs -RequestName 'windows.wait(focus_is)'
    $rawFocusWaitRequest = $focusWaitCall.RawRequest
    $focusWaitResponse = [PSCustomObject]@{
        Raw = $focusWaitCall.RawResponse
        Json = $focusWaitCall.Json
    }
    $focusWaitPayload = Assert-WaitSuccess -ToolCall $focusWaitCall -Condition 'focus_is'
    Assert-Condition -Condition ([string]$focusWaitPayload.matchedElement.controlType -eq 'button') -Message 'focus_is did not resolve the expected focused helper button.'

    $elementWaitCall = Invoke-WaitToolCall -Process $process -Condition 'element_exists' -Selector @{ name = 'Run semantic smoke'; controlType = 'button' } -TimeoutMs $waitTimeoutSemanticUiMs -RequestName 'windows.wait(element_exists)'
    $rawElementWaitRequest = $elementWaitCall.RawRequest
    $elementWaitResponse = [PSCustomObject]@{
        Raw = $elementWaitCall.RawResponse
        Json = $elementWaitCall.Json
    }
    $elementWaitPayload = Assert-WaitSuccess -ToolCall $elementWaitCall -Condition 'element_exists'
    Assert-Condition -Condition ([string]$elementWaitPayload.matchedElement.name -eq 'Run semantic smoke') -Message 'element_exists did not resolve the expected helper button.'

    Send-HelperCommand -Hwnd $helperHwnd -Message $wmAppArmElementGone -Description 'arm_element_gone'
    $transientExistsWaitCall = Invoke-WaitToolCall -Process $process -Condition 'element_exists' -Selector @{ name = 'Transient wait target'; controlType = 'button' } -TimeoutMs $waitTimeoutSemanticUiMs -RequestName 'windows.wait(element_exists transient precondition)'
    $rawTransientExistsWaitRequest = $transientExistsWaitCall.RawRequest
    $transientExistsWaitResponse = [PSCustomObject]@{
        Raw = $transientExistsWaitCall.RawResponse
        Json = $transientExistsWaitCall.Json
    }
    $transientExistsWaitPayload = Assert-WaitSuccess -ToolCall $transientExistsWaitCall -Condition 'element_exists'
    Assert-Condition -Condition ([string]$transientExistsWaitPayload.matchedElement.name -eq 'Transient wait target') -Message 'Transient precondition did not observe the expected helper button before element_gone.'

    $elementGoneWaitCall = Invoke-WaitToolCall -Process $process -Condition 'element_gone' -Selector @{ name = 'Transient wait target'; controlType = 'button' } -TimeoutMs $waitTimeoutElementGoneMs -RequestName 'windows.wait(element_gone)'
    $rawElementGoneWaitRequest = $elementGoneWaitCall.RawRequest
    $elementGoneWaitResponse = [PSCustomObject]@{
        Raw = $elementGoneWaitCall.RawResponse
        Json = $elementGoneWaitCall.Json
    }
    $elementGoneWaitPayload = Assert-WaitSuccess -ToolCall $elementGoneWaitCall -Condition 'element_gone'
    Assert-Condition -Condition ($null -eq $elementGoneWaitPayload.matchedElement) -Message 'element_gone must complete without matchedElement in final payload.'

    $textWaitCall = Invoke-WaitToolCall -Process $process -Condition 'text_appears' -Selector @{ name = 'Smoke query input'; controlType = 'edit' } -ExpectedText 'semantic text' -TimeoutMs $waitTimeoutSemanticUiMs -RequestName 'windows.wait(text_appears)'
    $rawTextWaitRequest = $textWaitCall.RawRequest
    $textWaitResponse = [PSCustomObject]@{
        Raw = $textWaitCall.RawResponse
        Json = $textWaitCall.Json
    }
    $textWaitPayload = Assert-WaitSuccess -ToolCall $textWaitCall -Condition 'text_appears'
    Assert-Condition -Condition (@('value_pattern', 'text_pattern', 'name') -contains [string]$textWaitPayload.lastObserved.matchedTextSource) -Message 'text_appears did not report a canonical matchedTextSource.'

    $visualWaitCall = Invoke-WaitToolCall -Process $process -Condition 'visual_changed' -TimeoutMs $waitTimeoutVisualMs -RequestName 'windows.wait(visual_changed)'
    $rawVisualWaitRequest = $visualWaitCall.RawRequest
    $visualWaitResponse = [PSCustomObject]@{
        Raw = $visualWaitCall.RawResponse
        Json = $visualWaitCall.Json
    }
    $visualWaitPayload = Assert-WaitSuccess -ToolCall $visualWaitCall -Condition 'visual_changed'
    $visualEvidenceStatus = [string]$visualWaitPayload.lastObserved.visualEvidenceStatus
    Assert-Condition -Condition (@('materialized', 'timeout', 'failed', 'skipped') -contains $visualEvidenceStatus) -Message "visual_changed returned unexpected visualEvidenceStatus '$visualEvidenceStatus'."
    $visualBaselineArtifactPath = [string]$visualWaitPayload.lastObserved.visualBaselineArtifactPath
    $visualCurrentArtifactPath = [string]$visualWaitPayload.lastObserved.visualCurrentArtifactPath
    if ($visualEvidenceStatus -eq 'materialized') {
        Assert-Condition -Condition (-not [string]::IsNullOrWhiteSpace($visualBaselineArtifactPath)) -Message 'visual_changed did not return baseline artifact path for materialized evidence.'
        Assert-Condition -Condition (-not [string]::IsNullOrWhiteSpace($visualCurrentArtifactPath)) -Message 'visual_changed did not return current artifact path for materialized evidence.'
    }

    if (-not [string]::IsNullOrWhiteSpace($visualBaselineArtifactPath)) {
        Assert-Condition -Condition (Test-Path $visualBaselineArtifactPath) -Message "visual_changed baseline artifact '$visualBaselineArtifactPath' was not created."
    }

    if (-not [string]::IsNullOrWhiteSpace($visualCurrentArtifactPath)) {
        Assert-Condition -Condition (Test-Path $visualCurrentArtifactPath) -Message "visual_changed current artifact '$visualCurrentArtifactPath' was not created."
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
    Assert-HealthObservabilityContract -Payload $healthPayload

    $report = [ordered]@{
        run_id = $runId
        initialized_protocol = $initializeResponse.Json.result.protocolVersion
        server_name = $initializeResponse.Json.result.serverInfo.name
        tool_contract = $manifest
        declared_tools = @($listResponse.Json.result.tools | ForEach-Object { $_.name })
        health = $healthPayload
        health_digest = [ordered]@{
            domain_statuses = Get-HealthStatusDigest -Items @($healthPayload.readiness.domains) -NameProperty 'domain'
            capability_statuses = Get-HealthStatusDigest -Items @($healthPayload.readiness.capabilities) -NameProperty 'capability'
            blocked_capabilities = @($healthPayload.blockedCapabilities | ForEach-Object { [string]$_.capability })
            warning_codes = @($healthPayload.warnings | ForEach-Object { [string]$_.code })
            artifact_materialized = $false
            dedicated_runtime_event_validation = 'verified_absent'
        }
        monitors = $monitorsPayload
        windows = $windowsPayload
        attached_window = $attachedWindow
        session = $sessionPayload
        desktop_capture = $desktopCapturePayload
        uia_snapshot = $uiaSnapshotPayload
        capture = $capturePayload
        helper_window = [ordered]@{
            hwnd = $helperHwnd
            title = $helperTitle
        }
        helper_activate = $activatePayload
        helper_capture = $helperCapturePayload
        wait_active_window_matches = $activeWaitPayload
        wait_focus_is = $focusWaitPayload
        wait_element_exists = $elementWaitPayload
        wait_transient_precondition = $transientExistsWaitPayload
        wait_element_gone = $elementGoneWaitPayload
        wait_text_appears = $textWaitPayload
        wait_visual_changed = $visualWaitPayload
        raw_requests = [ordered]@{
            initialize = $rawInitializeRequest
            list_tools = $rawListRequest
            health = $rawHealthRequest
            list_monitors = $rawMonitorsRequest
            list_windows = $rawWindowsRequest
            desktop_capture = $rawDesktopCaptureRequest
            attach_window = $rawAttachRequest
            session_state = $rawSessionRequest
            uia_snapshot = $rawUiaSnapshotRequest
            capture = $rawCaptureRequest
            activate_window = $rawActivateRequest
            helper_window_capture = $rawHelperCaptureRequest
            wait_active_window_matches = $rawActiveWaitRequest
            wait_focus_is = $rawFocusWaitRequest
            wait_element_exists = $rawElementWaitRequest
            wait_transient_precondition = $rawTransientExistsWaitRequest
            wait_element_gone = $rawElementGoneWaitRequest
            wait_text_appears = $rawTextWaitRequest
            wait_visual_changed = $rawVisualWaitRequest
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
            uia_snapshot = $uiaSnapshotResponse.Raw
            capture = $captureResponse.Raw
            activate_window = $activateResponse.Raw
            helper_window_capture = $helperCaptureResponse.Raw
            wait_active_window_matches = $activeWaitResponse.Raw
            wait_focus_is = $focusWaitResponse.Raw
            wait_element_exists = $elementWaitResponse.Raw
            wait_transient_precondition = $transientExistsWaitResponse.Raw
            wait_element_gone = $elementGoneWaitResponse.Raw
            wait_text_appears = $textWaitResponse.Raw
            wait_visual_changed = $visualWaitResponse.Raw
        }
        stderr = $stderr
    }

    $summaryLines = @(
        '# Okno smoke summary',
        '',
        "- run_id: $runId",
        "- server: $($initializeResponse.Json.result.serverInfo.name)",
        "- protocol: $($initializeResponse.Json.result.protocolVersion)",
        "- declared_tools: $(@($listResponse.Json.result.tools).Count)",
        "- health_domains: $(Get-HealthStatusDigest -Items @($healthPayload.readiness.domains) -NameProperty 'domain')",
        "- health_capabilities: $(Get-HealthStatusDigest -Items @($healthPayload.readiness.capabilities) -NameProperty 'capability')",
        "- health_blocked_capabilities: $((@($healthPayload.blockedCapabilities | ForEach-Object { [string]$_.capability }) -join ', '))",
        "- health_warning_codes: $((@($healthPayload.warnings | ForEach-Object { [string]$_.code }) -join ', '))",
        "- health_artifact: not_materialized",
        "- health_event: not_materialized",
        "- monitor_count: $($monitorsPayload.count)",
        "- desktop_monitor_id: $primaryMonitorId",
        "- visible_windows: $($windowsPayload.count)",
        "- attached_hwnd: $attachedHwnd",
        "- uia_snapshot_status: $($uiaSnapshotPayload.status)",
        "- uia_snapshot_artifact: $($uiaSnapshotPayload.artifactPath)",
        "- capture_artifact: $($capturePayload.artifactPath)",
        "- helper_hwnd: $helperHwnd",
        "- helper_activation_status: $($activatePayload.status)",
        "- helper_capture_artifact: $($helperCapturePayload.artifactPath)",
        "- wait_active_window_matches_artifact: $($activeWaitPayload.artifactPath)",
        "- wait_focus_is_artifact: $($focusWaitPayload.artifactPath)",
        "- wait_element_exists_artifact: $($elementWaitPayload.artifactPath)",
        "- wait_transient_precondition_artifact: $($transientExistsWaitPayload.artifactPath)",
        "- wait_element_gone_artifact: $($elementGoneWaitPayload.artifactPath)",
        "- wait_text_appears_artifact: $($textWaitPayload.artifactPath)",
        "- wait_visual_changed_artifact: $($visualWaitPayload.artifactPath)",
        "- audit_dir: $($healthPayload.artifactsDirectory)",
        "- report: $reportPath"
    )

    Write-SmokeArtifacts -Report $report -SummaryLines $summaryLines

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
