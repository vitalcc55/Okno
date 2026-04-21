. (Join-Path $PSScriptRoot 'common.ps1')

$repoRoot = Get-RepoRoot -ScriptRoot $PSScriptRoot
${null} = Initialize-WinBridgeExecutionContext -RepoRoot $repoRoot -UseArtifactsRoot
Set-Location $repoRoot

$runId = Get-Date -Format 'yyyyMMddTHHmmssfff'
$artifactRoot = Join-Path $repoRoot "artifacts\\smoke\\$runId"
$reportPath = Join-Path $artifactRoot 'report.json'
$summaryPath = Join-Path $artifactRoot 'summary.md'
$bundle = Use-OknoTestBundle -RepoRoot $repoRoot
$serverDll = [string]$bundle.serverDll
$helperExe = [string]$bundle.helperExe
$contractPath = Join-Path $artifactRoot 'project-interfaces.json'
$script:NextMcpRequestId = 1
New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null
Remove-StaleSmokeUnownedProbeRoots -CurrentRunId $runId

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

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

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
$wmAppArmVisualHeartbeat = 0x8003
$waitTimeoutForegroundMs = 5000
$waitTimeoutFocusMs = 5000
$waitTimeoutSemanticUiMs = 5000
$waitTimeoutElementGoneMs = 5000
$waitTimeoutVisualMs = 6000
$helperLaunchWaitTimeoutMs = 10000
$helperWindowMaterializationTimeoutMs = 10000
$helperLifetimeSafetyBufferMs = 30000
$helperVisualBurstMs = $waitTimeoutVisualMs + 2000
$helperTitleResolutionPollIntervalMs = 100
$smokeStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$helperClosedEarly = $false
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

    $allRunEvents = @(Get-AuditEvents -ArtifactsDirectory ([string]$Payload.artifactsDirectory))

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

function Get-ProcessByIdWithRetry {
    param(
        [Parameter(Mandatory)]
        [int] $ProcessId,
        [int] $TimeoutMilliseconds = 10000,
        [int] $PollMilliseconds = 100
    )

    $deadline = (Get-Date).AddMilliseconds($TimeoutMilliseconds)
    do {
        try {
            $candidate = [System.Diagnostics.Process]::GetProcessById($ProcessId)
            $candidate.Refresh()
            if (-not $candidate.HasExited) {
                return $candidate
            }
        }
        catch [System.ArgumentException] {
        }

        Start-Sleep -Milliseconds $PollMilliseconds
    }
    while ((Get-Date) -lt $deadline)

    throw "Smoke helper process '$ProcessId' did not become observable in time."
}

function Get-SmokeHelperProcessIds {
    param(
        [Parameter(Mandatory)]
        [string] $ProcessName,
        [Parameter(Mandatory)]
        [string] $ExpectedOwnershipMarker
    )

    $imageName = if ($ProcessName.EndsWith('.exe', [System.StringComparison]::OrdinalIgnoreCase)) {
        $ProcessName
    }
    else {
        "$ProcessName.exe"
    }

    return @(
        Get-CimInstance Win32_Process -Filter "Name = '$imageName'" -ErrorAction SilentlyContinue |
            Where-Object {
                $commandLine = [string]$_.CommandLine
                -not [string]::IsNullOrWhiteSpace($commandLine) -and
                ($commandLine.IndexOf($ExpectedOwnershipMarker, [System.StringComparison]::Ordinal) -ge 0)
            } |
            ForEach-Object { [int]$_.ProcessId } |
            Sort-Object -Unique)
}

function Get-SmokeHelperLaunchArguments {
    param(
        [Parameter(Mandatory)]
        [string] $Title,
        [Parameter(Mandatory)]
        [string] $OwnershipMarker,
        [Parameter(Mandatory)]
        [int] $LifetimeMs,
        [Parameter(Mandatory)]
        [int] $VisualBurstMs
    )

    return @(
        '--title',
        $Title,
        '--smoke-run-id',
        $OwnershipMarker,
        '--lifetime-ms',
        $LifetimeMs.ToString([System.Globalization.CultureInfo]::InvariantCulture),
        '--visual-burst-ms',
        $VisualBurstMs.ToString([System.Globalization.CultureInfo]::InvariantCulture))
}

function Get-SmokeHelperProcessLeakIds {
    param(
        [Parameter(Mandatory)]
        [string] $HelperProcessName,
        [Parameter(Mandatory)]
        [string] $ExpectedOwnershipMarker,
        [AllowEmptyCollection()]
        [Parameter(Mandatory)]
        [int[]] $BaselineProcessIds
    )

    $currentProcessIds = @(Get-SmokeHelperProcessIds -ProcessName $HelperProcessName -ExpectedOwnershipMarker $ExpectedOwnershipMarker)
    return @($currentProcessIds | Where-Object { $_ -notin $BaselineProcessIds })
}

function Get-SmokeHelperWindowLeaks {
    param(
        [Parameter(Mandatory)]
        [System.Diagnostics.Process] $Process,
        [Parameter(Mandatory)]
        [string] $ExpectedTitle,
        [Parameter(Mandatory)]
        [string] $RequestName
    )

    $windowInventory = Invoke-ToolCall -Process $Process -Name 'windows.list_windows' -Arguments @{ includeInvisible = $true } -RequestName $RequestName
    return @(
        $windowInventory.Payload.windows |
            Where-Object { [string]$_.title -eq $ExpectedTitle })
}

function Get-SmokeHelperCandidateProcessIds {
    param(
        [Parameter(Mandatory)]
        [string] $HelperProcessName,
        [Parameter(Mandatory)]
        [string] $ExpectedExecutablePath,
        [AllowEmptyCollection()]
        [Parameter(Mandatory)]
        [int[]] $BaselineProcessIds
    )

    $imageName = if ($HelperProcessName.EndsWith('.exe', [System.StringComparison]::OrdinalIgnoreCase)) {
        $HelperProcessName
    }
    else {
        "$HelperProcessName.exe"
    }

    $normalizedExecutablePath = [System.IO.Path]::GetFullPath($ExpectedExecutablePath)

    return @(
        Get-CimInstance Win32_Process -Filter "Name = '$imageName'" -ErrorAction SilentlyContinue |
            Where-Object {
                $executablePath = [string]$_.ExecutablePath
                -not [string]::IsNullOrWhiteSpace($executablePath) -and
                ([string]::Equals(
                    [System.IO.Path]::GetFullPath($executablePath),
                    $normalizedExecutablePath,
                    [System.StringComparison]::OrdinalIgnoreCase))
            } |
            ForEach-Object { [int]$_.ProcessId } |
            Where-Object { $_ -notin $BaselineProcessIds } |
            Sort-Object -Unique)
}

function Resolve-SmokeHelperTitleProcessIds {
    param(
        [Parameter(Mandatory)]
        [string] $HelperProcessName,
        [Parameter(Mandatory)]
        [string] $ExpectedExecutablePath,
        [AllowEmptyCollection()]
        [Parameter(Mandatory)]
        [int[]] $BaselineProcessIds,
        [AllowEmptyCollection()]
        [Parameter(Mandatory)]
        [int[]] $AdditionalCandidateProcessIds,
        [Parameter(Mandatory)]
        [string] $ExpectedTitle,
        [Parameter(Mandatory)]
        [int] $TimeoutMs
    )

    $candidateProcessIds = @([int[]]$AdditionalCandidateProcessIds)
    $candidateProcessIds = @($candidateProcessIds | Sort-Object -Unique)
    $deadline = (Get-Date).AddMilliseconds($TimeoutMs)
    do {
        $discoveredCandidateProcessIds = @(Get-SmokeHelperCandidateProcessIds `
            -HelperProcessName $HelperProcessName `
            -ExpectedExecutablePath $ExpectedExecutablePath `
            -BaselineProcessIds $BaselineProcessIds)
        $candidateProcessIds = @($candidateProcessIds + $discoveredCandidateProcessIds)
        $candidateProcessIds = @($candidateProcessIds | Sort-Object -Unique)

        $titleProcessIds = foreach ($processId in $candidateProcessIds) {
            $candidateProcess = Get-Process -Id $processId -ErrorAction SilentlyContinue
            if ($null -ne $candidateProcess -and -not $candidateProcess.HasExited) {
                $candidateProcess.Refresh()
                if ([string]$candidateProcess.MainWindowTitle -eq $ExpectedTitle) {
                    [int]$processId
                }
            }
        }
        $titleProcessIds = @($titleProcessIds | Sort-Object -Unique)

        if ($titleProcessIds.Count -gt 0) {
            return [PSCustomObject]@{
                CandidateProcessIds = $candidateProcessIds
                TitleProcessIds = $titleProcessIds
            }
        }

        Start-Sleep -Milliseconds $helperTitleResolutionPollIntervalMs
    }
    while ((Get-Date) -lt $deadline)

    return [PSCustomObject]@{
        CandidateProcessIds = $candidateProcessIds
        TitleProcessIds = @()
    }
}

function Get-SmokeHelperBaseReconciliationState {
    param(
        [Parameter(Mandatory)]
        [string] $HelperProcessName,
        [Parameter(Mandatory)]
        [string] $ExpectedOwnershipMarker,
        [Parameter(Mandatory)]
        [string] $ExpectedExecutablePath,
        [Parameter(Mandatory)]
        [string] $ExpectedTitle,
        [AllowEmptyCollection()]
        [Parameter(Mandatory)]
        [int[]] $BaselineProcessIds
    )

    $markerProcessIds = @(Get-SmokeHelperProcessLeakIds -HelperProcessName $HelperProcessName -ExpectedOwnershipMarker $ExpectedOwnershipMarker -BaselineProcessIds $BaselineProcessIds)
    $titleCandidateProcessIds = @(Get-SmokeHelperCandidateProcessIds -HelperProcessName $HelperProcessName -ExpectedExecutablePath $ExpectedExecutablePath -BaselineProcessIds $BaselineProcessIds)
    return [PSCustomObject]@{
        HelperProcessName = $HelperProcessName
        ExpectedOwnershipMarker = $ExpectedOwnershipMarker
        ExpectedExecutablePath = $ExpectedExecutablePath
        ExpectedTitle = $ExpectedTitle
        BaselineProcessIds = $BaselineProcessIds
        MarkerProcessIds = $markerProcessIds
        TitleCandidateProcessIds = $titleCandidateProcessIds
    }
}

function Resolve-SmokeHelperCleanupProcessIds {
    param(
        [Parameter(Mandatory)]
        [object] $ReconciliationState,
        [Parameter(Mandatory)]
        [int] $TitleResolutionTimeoutMs
    )

    $markerProcessIds = @(Get-SmokeHelperProcessLeakIds `
        -HelperProcessName ([string]$ReconciliationState.HelperProcessName) `
        -ExpectedOwnershipMarker ([string]$ReconciliationState.ExpectedOwnershipMarker) `
        -BaselineProcessIds @([int[]]$ReconciliationState.BaselineProcessIds))
    $titleResolution = (Resolve-SmokeHelperTitleProcessIds `
        -HelperProcessName ([string]$ReconciliationState.HelperProcessName) `
        -ExpectedExecutablePath ([string]$ReconciliationState.ExpectedExecutablePath) `
        -BaselineProcessIds @([int[]]$ReconciliationState.BaselineProcessIds) `
        -AdditionalCandidateProcessIds @([int[]]$ReconciliationState.TitleCandidateProcessIds) `
        -ExpectedTitle ([string]$ReconciliationState.ExpectedTitle) `
        -TimeoutMs $TitleResolutionTimeoutMs)
    $titleCandidateProcessIds = @([int[]]$titleResolution.CandidateProcessIds)
    $titleProcessIds = @([int[]]$titleResolution.TitleProcessIds)

    return [PSCustomObject]@{
        MarkerProcessIds = $markerProcessIds
        TitleCandidateProcessIds = $titleCandidateProcessIds
        TitleProcessIds = $titleProcessIds
        CleanupProcessIds = @($markerProcessIds + $titleProcessIds | Sort-Object -Unique)
    }
}

function Stop-SmokeHelperLeaks {
    param(
        [Parameter(Mandatory)]
        [int[]] $ProcessIds
    )

    foreach ($processId in @($ProcessIds | Sort-Object -Unique)) {
        try {
            $process = [System.Diagnostics.Process]::GetProcessById([int]$processId)
            if (-not $process.HasExited) {
                $process.Kill()
                $process.WaitForExit()
            }
        }
        catch [System.ArgumentException] {
        }
    }
}

function Assert-NoDryRunLaunchSideEffects {
    param(
        [AllowEmptyCollection()]
        [Parameter(Mandatory)]
        [int[]] $ProcessLeakIds,
        [AllowEmptyCollection()]
        [Parameter(Mandatory)]
        [object[]] $WindowLeaks
    )

    Assert-Condition -Condition ($ProcessLeakIds.Count -eq 0) -Message "Dry-run launch unexpectedly created helper process ids: $($ProcessLeakIds -join ', ')."
    Assert-Condition -Condition ($WindowLeaks.Count -eq 0) -Message 'Dry-run launch unexpectedly materialized helper window.'
}

function Assert-DryRunPreviewRuntimeEvent {
    param(
        [Parameter(Mandatory)]
        [string] $ArtifactsDirectory,
        [Parameter(Mandatory)]
        [int] $BaselineEventCount
    )

    $currentEventCount = Get-AuditEventCount -ArtifactsDirectory $ArtifactsDirectory -ToolName 'windows.launch_process' -EventName 'launch.preview.completed'
    Assert-Condition -Condition ($currentEventCount -eq ($BaselineEventCount + 1)) -Message 'Dry-run launch did not record the preview-only runtime event.'
}

function Assert-NoDryRunRuntimeEvent {
    param(
        [Parameter(Mandatory)]
        [string] $ArtifactsDirectory,
        [Parameter(Mandatory)]
        [int] $BaselineEventCount
    )

    $currentEventCount = Get-AuditEventCount -ArtifactsDirectory $ArtifactsDirectory -ToolName 'windows.launch_process' -EventName 'launch.runtime.completed'
    Assert-Condition -Condition ($currentEventCount -eq $BaselineEventCount) -Message 'Dry-run launch unexpectedly entered the factual runtime event path.'
}

function Assert-OpenTargetPreviewRuntimeEvent {
    param(
        [Parameter(Mandatory)]
        [string] $ArtifactsDirectory,
        [Parameter(Mandatory)]
        [int] $BaselineEventCount
    )

    $currentEventCount = Get-AuditEventCount -ArtifactsDirectory $ArtifactsDirectory -ToolName 'windows.open_target' -EventName 'open_target.preview.completed'
    Assert-Condition -Condition ($currentEventCount -eq ($BaselineEventCount + 1)) -Message 'Dry-run open_target did not record the preview-only runtime event.'
}

function Assert-NoOpenTargetDryRunRuntimeEvent {
    param(
        [Parameter(Mandatory)]
        [string] $ArtifactsDirectory,
        [Parameter(Mandatory)]
        [int] $BaselineEventCount
    )

    $currentEventCount = Get-AuditEventCount -ArtifactsDirectory $ArtifactsDirectory -ToolName 'windows.open_target' -EventName 'open_target.runtime.completed'
    Assert-Condition -Condition ($currentEventCount -eq $BaselineEventCount) -Message 'Dry-run open_target unexpectedly entered the factual runtime event path.'
}

function Get-OpenTargetArtifactCount {
    param(
        [Parameter(Mandatory)]
        [string] $ArtifactsDirectory
    )

    $launchDirectory = Join-Path $ArtifactsDirectory 'launch'
    if (-not (Test-Path $launchDirectory)) {
        return 0
    }

    return @(
        Get-ChildItem -Path $launchDirectory -Filter 'open-target-*.json' -File -ErrorAction SilentlyContinue
    ).Count
}

function Assert-OpenTargetPreview {
    param(
        [Parameter(Mandatory)]
        [object] $ToolCall,
        [Parameter(Mandatory)]
        [string] $ExpectedTargetKind,
        [Parameter(Mandatory)]
        [string] $ExpectedTargetIdentity
    )

    Assert-Condition -Condition (-not [bool]$ToolCall.Json.result.isError) -Message 'Dry-run open_target preview returned isError=true.'
    $payload = $ToolCall.Payload
    Assert-Condition -Condition ([string]$payload.status -eq 'done') -Message "Dry-run open_target returned unexpected status '$($payload.status)'."
    Assert-Condition -Condition ([string]$payload.decision -eq 'done') -Message "Dry-run open_target returned unexpected decision '$($payload.decision)'."
    Assert-Condition -Condition ($null -ne $payload.preview) -Message 'Dry-run open_target did not return preview.'
    Assert-Condition -Condition ([string]$payload.preview.targetKind -eq $ExpectedTargetKind) -Message 'Dry-run open_target preview targetKind does not match request.'
    Assert-Condition -Condition ([string]$payload.preview.targetIdentity -eq $ExpectedTargetIdentity) -Message 'Dry-run open_target preview targetIdentity does not match folder basename.'
    Assert-Condition -Condition ($null -eq $payload.preview.uriScheme) -Message 'Dry-run open_target folder preview must not return uriScheme.'
    Assert-Condition -Condition ([string]$payload.targetKind -eq $ExpectedTargetKind) -Message 'Dry-run open_target targetKind does not match request.'
    Assert-Condition -Condition ([string]$payload.targetIdentity -eq $ExpectedTargetIdentity) -Message 'Dry-run open_target targetIdentity does not match folder basename.'
    Assert-Condition -Condition ($null -eq $payload.uriScheme) -Message 'Dry-run open_target folder payload must not return uriScheme.'
    Assert-Condition -Condition ($null -eq $payload.acceptedAtUtc) -Message 'Dry-run open_target must not return acceptedAtUtc.'
    Assert-Condition -Condition ($null -eq $payload.handlerProcessId) -Message 'Dry-run open_target must not return handlerProcessId.'
    Assert-Condition -Condition ($null -eq $payload.resultMode) -Message 'Dry-run open_target must not return resultMode.'
    Assert-Condition -Condition ($null -eq $payload.artifactPath) -Message 'Dry-run open_target must not return artifactPath.'

    $previewProperties = @($payload.preview.PSObject.Properties.Name | Sort-Object)
    Assert-Condition -Condition (
        (Join-ContractValues -Values $previewProperties) -eq 'targetIdentity,targetKind'
    ) -Message 'Dry-run open_target preview exposed unexpected fields beyond the safe preview contract.'
}

function Assert-OpenTargetLiveResult {
    param(
        [Parameter(Mandatory)]
        [object] $ToolCall,
        [Parameter(Mandatory)]
        [string] $ExpectedTargetKind,
        [Parameter(Mandatory)]
        [string] $ExpectedTargetIdentity
    )

    Assert-Condition -Condition (-not [bool]$ToolCall.Json.result.isError) -Message 'Live open_target returned isError=true.'
    $payload = $ToolCall.Payload
    $resultMode = [string]$payload.resultMode
    Assert-Condition -Condition ([string]$payload.status -eq 'done') -Message "Live open_target returned unexpected status '$($payload.status)'."
    Assert-Condition -Condition ([string]$payload.decision -eq 'done') -Message "Live open_target returned unexpected decision '$($payload.decision)'."
    Assert-Condition -Condition (@('target_open_requested', 'handler_process_observed') -contains $resultMode) -Message "Live open_target returned unexpected resultMode '$resultMode'."
    Assert-Condition -Condition ([string]$payload.targetKind -eq $ExpectedTargetKind) -Message 'Live open_target targetKind does not match request.'
    Assert-Condition -Condition ([string]$payload.targetIdentity -eq $ExpectedTargetIdentity) -Message 'Live open_target targetIdentity does not match folder basename.'
    Assert-Condition -Condition ($null -eq $payload.uriScheme) -Message 'Live open_target folder payload must not return uriScheme.'
    Assert-Condition -Condition (-not [string]::IsNullOrWhiteSpace([string]$payload.acceptedAtUtc)) -Message 'Live open_target did not return acceptedAtUtc.'
    Assert-Condition -Condition (-not [string]::IsNullOrWhiteSpace([string]$payload.artifactPath)) -Message 'Live open_target did not return artifactPath.'
    Assert-Condition -Condition (Test-Path ([string]$payload.artifactPath)) -Message "Live open_target artifact '$($payload.artifactPath)' was not created."

    if ($resultMode -eq 'handler_process_observed') {
        Assert-Condition -Condition ([int]$payload.handlerProcessId -gt 0) -Message 'Live open_target reported handler_process_observed without positive handlerProcessId.'
    }
    else {
        Assert-Condition -Condition ($null -eq $payload.handlerProcessId) -Message 'Live open_target returned handlerProcessId without handler_process_observed resultMode.'
    }
}

function Assert-DateTimeOffsetSameInstant {
    param(
        [Parameter(Mandatory)]
        [string] $Actual,
        [Parameter(Mandatory)]
        [string] $Expected,
        [Parameter(Mandatory)]
        [string] $Message
    )

    $actualOffset = [DateTimeOffset]::Parse($Actual, [System.Globalization.CultureInfo]::InvariantCulture)
    $expectedOffset = [DateTimeOffset]::Parse($Expected, [System.Globalization.CultureInfo]::InvariantCulture)
    Assert-Condition -Condition ($actualOffset.ToUniversalTime() -eq $expectedOffset.ToUniversalTime()) -Message $Message
}

function Assert-OpenTargetArtifact {
    param(
        [Parameter(Mandatory)]
        [string] $ArtifactsDirectory,
        [Parameter(Mandatory)]
        [object] $OpenTargetPayload
    )

    $artifactPath = [System.IO.Path]::GetFullPath([string]$OpenTargetPayload.artifactPath)
    $expectedLaunchDirectory = [System.IO.Path]::GetFullPath((Join-Path $ArtifactsDirectory 'launch'))
    $expectedLaunchPrefix = $expectedLaunchDirectory + [System.IO.Path]::DirectorySeparatorChar
    Assert-Condition -Condition ($artifactPath.StartsWith($expectedLaunchPrefix, [System.StringComparison]::OrdinalIgnoreCase)) -Message 'Open-target artifact path is outside the canonical diagnostics launch directory.'
    Assert-Condition -Condition ([System.IO.Path]::GetExtension($artifactPath) -eq '.json') -Message 'Open-target artifact must be a JSON file.'
    Assert-Condition -Condition ([System.IO.Path]::GetFileName($artifactPath) -match '^open-target-.+\.json$') -Message 'Open-target artifact file name does not match the canonical naming pattern.'

    $artifact = Get-Content $artifactPath -Raw | ConvertFrom-Json
    Assert-Condition -Condition (-not ($artifact.PSObject.Properties.Name -contains 'failure_diagnostics')) -Message 'Successful open-target artifact must not publish failure_diagnostics.'
    Assert-Condition -Condition ([string]$artifact.result.status -eq [string]$OpenTargetPayload.status) -Message 'Open-target artifact status diverges from public payload.'
    Assert-Condition -Condition ([string]$artifact.result.decision -eq [string]$OpenTargetPayload.decision) -Message 'Open-target artifact decision diverges from public payload.'
    Assert-Condition -Condition ([string]$artifact.result.result_mode -eq [string]$OpenTargetPayload.resultMode) -Message 'Open-target artifact resultMode diverges from public payload.'
    Assert-Condition -Condition ([string]$artifact.result.target_kind -eq [string]$OpenTargetPayload.targetKind) -Message 'Open-target artifact targetKind diverges from public payload.'
    Assert-Condition -Condition ([string]$artifact.result.target_identity -eq [string]$OpenTargetPayload.targetIdentity) -Message 'Open-target artifact targetIdentity diverges from public payload.'
    Assert-Condition -Condition ([string]$artifact.result.uri_scheme -eq [string]$OpenTargetPayload.uriScheme) -Message 'Open-target artifact uriScheme diverges from public payload.'
    Assert-DateTimeOffsetSameInstant -Actual ([string]$artifact.result.accepted_at_utc) -Expected ([string]$OpenTargetPayload.acceptedAtUtc) -Message 'Open-target artifact acceptedAtUtc diverges from public payload.'
    Assert-Condition -Condition ([string]$artifact.result.artifact_path -eq [string]$OpenTargetPayload.artifactPath) -Message 'Open-target artifact nested artifactPath diverges from public payload.'

    if ($null -eq $OpenTargetPayload.handlerProcessId) {
        Assert-Condition -Condition ($null -eq $artifact.result.handler_process_id) -Message 'Open-target artifact unexpectedly returned handlerProcessId.'
    }
    else {
        Assert-Condition -Condition ([int]$artifact.result.handler_process_id -eq [int]$OpenTargetPayload.handlerProcessId) -Message 'Open-target artifact handlerProcessId diverges from public payload.'
    }
}

function Assert-OpenTargetRuntimeEvent {
    param(
        [Parameter(Mandatory)]
        [string] $ArtifactsDirectory,
        [Parameter(Mandatory)]
        [object] $OpenTargetPayload
    )

    $eventsPath = Join-Path $ArtifactsDirectory 'events.jsonl'
    Assert-Condition -Condition (Test-Path $eventsPath) -Message "Open-target smoke expected events file '$eventsPath'."

    $openTargetEvents = @(
        Get-AuditEvents -ArtifactsDirectory $ArtifactsDirectory |
            Where-Object {
                ([string]$_.tool_name -eq 'windows.open_target') -and
                ([string]$_.event_name -eq 'open_target.runtime.completed')
            })

    Assert-Condition -Condition ($openTargetEvents.Count -eq 1) -Message "Open-target smoke expected exactly one open_target.runtime.completed event, got $($openTargetEvents.Count)."

    $openTargetEvent = $openTargetEvents[0]
    $eventDataProperties = @($openTargetEvent.data.PSObject.Properties.Name | Sort-Object)
    Assert-Condition -Condition (
        (Join-ContractValues -Values $eventDataProperties) -eq 'accepted_at_utc,artifact_path,decision,exception_type,failure_code,failure_stage,handler_process_id,result_mode,status,target_identity,target_kind,uri_scheme'
    ) -Message 'Open-target runtime event exposed unexpected fields outside the safe runtime-event contract.'
    Assert-Condition -Condition ([string]$openTargetEvent.outcome -eq 'done') -Message "Open-target runtime event returned unexpected outcome '$($openTargetEvent.outcome)'."
    Assert-Condition -Condition ([string]$openTargetEvent.data.status -eq [string]$OpenTargetPayload.status) -Message 'Open-target runtime event status diverges from public payload.'
    Assert-Condition -Condition ([string]$openTargetEvent.data.decision -eq [string]$OpenTargetPayload.decision) -Message 'Open-target runtime event decision diverges from public payload.'
    Assert-Condition -Condition ([string]$openTargetEvent.data.result_mode -eq [string]$OpenTargetPayload.resultMode) -Message 'Open-target runtime event result_mode diverges from public payload.'
    Assert-Condition -Condition ([string]$openTargetEvent.data.target_kind -eq [string]$OpenTargetPayload.targetKind) -Message 'Open-target runtime event target_kind diverges from public payload.'
    Assert-Condition -Condition ([string]$openTargetEvent.data.target_identity -eq [string]$OpenTargetPayload.targetIdentity) -Message 'Open-target runtime event target_identity diverges from public payload.'
    Assert-Condition -Condition ([string]$openTargetEvent.data.uri_scheme -eq [string]$OpenTargetPayload.uriScheme) -Message 'Open-target runtime event uri_scheme diverges from public payload.'
    Assert-DateTimeOffsetSameInstant -Actual ([string]$openTargetEvent.data.accepted_at_utc) -Expected ([string]$OpenTargetPayload.acceptedAtUtc) -Message 'Open-target runtime event accepted_at_utc diverges from public payload.'
    Assert-Condition -Condition ([string]$openTargetEvent.data.artifact_path -eq [string]$OpenTargetPayload.artifactPath) -Message 'Open-target runtime event artifact_path diverges from public payload.'

    if ($null -eq $OpenTargetPayload.handlerProcessId) {
        Assert-Condition -Condition ($null -eq $openTargetEvent.data.handler_process_id) -Message 'Open-target runtime event unexpectedly returned handler_process_id.'
    }
    else {
        Assert-Condition -Condition ([int]$openTargetEvent.data.handler_process_id -eq [int]$OpenTargetPayload.handlerProcessId) -Message 'Open-target runtime event handler_process_id diverges from public payload.'
    }

    Assert-Condition -Condition ($null -eq $openTargetEvent.data.failure_code) -Message 'Successful open-target runtime event must not publish failure_code.'
    Assert-Condition -Condition ($null -eq $openTargetEvent.data.failure_stage) -Message 'Successful open-target runtime event must not publish failure_stage.'
    Assert-Condition -Condition ($null -eq $openTargetEvent.data.exception_type) -Message 'Successful open-target runtime event must not publish exception_type.'
}

function Assert-LaunchProcessPreview {
    param(
        [Parameter(Mandatory)]
        [object] $ToolCall,
        [Parameter(Mandatory)]
        [string] $ExpectedExecutableIdentity,
        [Parameter(Mandatory)]
        [int] $ExpectedArgumentCount,
        [Parameter(Mandatory)]
        [bool] $ExpectedWorkingDirectoryProvided,
        [Parameter(Mandatory)]
        [bool] $ExpectedWaitForWindow,
        [Parameter(Mandatory)]
        [int] $ExpectedTimeoutMs
    )

    Assert-Condition -Condition (-not [bool]$ToolCall.Json.result.isError) -Message 'Dry-run launch preview returned isError=true.'
    $payload = $ToolCall.Payload
    Assert-Condition -Condition ([string]$payload.status -eq 'done') -Message "Dry-run launch returned unexpected status '$($payload.status)'."
    Assert-Condition -Condition ([string]$payload.decision -eq 'done') -Message "Dry-run launch returned unexpected decision '$($payload.decision)'."
    Assert-Condition -Condition ($null -ne $payload.preview) -Message 'Dry-run launch did not return preview.'
    Assert-Condition -Condition ([string]$payload.preview.executableIdentity -eq $ExpectedExecutableIdentity) -Message 'Dry-run launch preview executableIdentity does not match helper basename.'
    Assert-Condition -Condition ([string]$payload.preview.resolutionMode -eq 'absolute_path') -Message "Dry-run launch preview returned unexpected resolutionMode '$($payload.preview.resolutionMode)'."
    Assert-Condition -Condition ([int]$payload.preview.argumentCount -eq $ExpectedArgumentCount) -Message 'Dry-run launch preview argumentCount does not match helper invocation.'
    Assert-Condition -Condition ([bool]$payload.preview.workingDirectoryProvided -eq $ExpectedWorkingDirectoryProvided) -Message 'Dry-run launch preview workingDirectoryProvided does not match request.'
    Assert-Condition -Condition ([bool]$payload.preview.waitForWindow -eq $ExpectedWaitForWindow) -Message 'Dry-run launch preview waitForWindow does not match request.'
    Assert-Condition -Condition ([int]$payload.preview.timeoutMs -eq $ExpectedTimeoutMs) -Message 'Dry-run launch preview timeoutMs does not match request.'

    Assert-Condition -Condition ([string]$payload.executableIdentity -eq $ExpectedExecutableIdentity) -Message 'Dry-run launch executableIdentity does not match helper basename.'
    Assert-Condition -Condition ($null -eq $payload.processId) -Message 'Dry-run launch must not return processId.'
    Assert-Condition -Condition ($null -eq $payload.startedAtUtc) -Message 'Dry-run launch must not return startedAtUtc.'
    Assert-Condition -Condition ($null -eq $payload.hasExited) -Message 'Dry-run launch must not return hasExited.'
    Assert-Condition -Condition ($null -eq $payload.exitCode) -Message 'Dry-run launch must not return exitCode.'
    Assert-Condition -Condition (-not [bool]$payload.mainWindowObserved) -Message 'Dry-run launch must not report mainWindowObserved=true.'
    Assert-Condition -Condition ($null -eq $payload.mainWindowHandle) -Message 'Dry-run launch must not return mainWindowHandle.'
    Assert-Condition -Condition ($null -eq $payload.mainWindowObservationStatus) -Message 'Dry-run launch must not return mainWindowObservationStatus.'
    Assert-Condition -Condition ($null -eq $payload.resultMode) -Message 'Dry-run launch must not return resultMode.'
    Assert-Condition -Condition ($null -eq $payload.artifactPath) -Message 'Dry-run launch must not return artifactPath.'

    $previewProperties = @($payload.preview.PSObject.Properties.Name | Sort-Object)
    Assert-Condition -Condition (
        (Join-ContractValues -Values $previewProperties) -eq 'argumentCount,executableIdentity,resolutionMode,timeoutMs,waitForWindow,workingDirectoryProvided'
    ) -Message 'Dry-run launch preview exposed unexpected fields beyond the safe preview contract.'
}

function Assert-LaunchProcessLiveResult {
    param(
        [Parameter(Mandatory)]
        [object] $ToolCall,
        [Parameter(Mandatory)]
        [string] $ExpectedExecutableIdentity
    )

    Assert-Condition -Condition (-not [bool]$ToolCall.Json.result.isError) -Message 'Live launch returned isError=true.'
    $payload = $ToolCall.Payload
    Assert-Condition -Condition ([string]$payload.status -eq 'done') -Message "Live launch returned unexpected status '$($payload.status)'."
    Assert-Condition -Condition ([string]$payload.decision -eq 'done') -Message "Live launch returned unexpected decision '$($payload.decision)'."
    Assert-Condition -Condition ([string]$payload.resultMode -eq 'window_observed') -Message "Live launch returned unexpected resultMode '$($payload.resultMode)'."
    Assert-Condition -Condition ([string]$payload.executableIdentity -eq $ExpectedExecutableIdentity) -Message 'Live launch executableIdentity does not match helper basename.'
    Assert-Condition -Condition ([int]$payload.processId -gt 0) -Message 'Live launch did not return a positive processId.'
    Assert-Condition -Condition ([bool]$payload.mainWindowObserved) -Message 'Live launch did not confirm mainWindowObserved=true.'
    Assert-Condition -Condition ([int64]$payload.mainWindowHandle -ne 0) -Message 'Live launch did not return a non-zero mainWindowHandle.'
    Assert-Condition -Condition ([string]$payload.mainWindowObservationStatus -eq 'observed') -Message "Live launch returned unexpected mainWindowObservationStatus '$($payload.mainWindowObservationStatus)'."
    Assert-Condition -Condition (-not [bool]$payload.hasExited) -Message 'Live launch helper exited before smoke cross-check completed.'
    Assert-Condition -Condition (-not [string]::IsNullOrWhiteSpace([string]$payload.startedAtUtc)) -Message 'Live launch did not return startedAtUtc.'
    Assert-Condition -Condition (-not [string]::IsNullOrWhiteSpace([string]$payload.artifactPath)) -Message 'Live launch did not return artifactPath.'
    Assert-Condition -Condition (Test-Path ([string]$payload.artifactPath)) -Message "Live launch artifact '$($payload.artifactPath)' was not created."
}

function Assert-LaunchArtifact {
    param(
        [Parameter(Mandatory)]
        [string] $ArtifactsDirectory,
        [Parameter(Mandatory)]
        [object] $LaunchPayload
    )

    $artifactPath = [System.IO.Path]::GetFullPath([string]$LaunchPayload.artifactPath)
    $expectedLaunchDirectory = [System.IO.Path]::GetFullPath((Join-Path $ArtifactsDirectory 'launch'))
    $expectedLaunchPrefix = $expectedLaunchDirectory + [System.IO.Path]::DirectorySeparatorChar
    Assert-Condition -Condition ($artifactPath.StartsWith($expectedLaunchPrefix, [System.StringComparison]::OrdinalIgnoreCase)) -Message 'Launch artifact path is outside the canonical diagnostics launch directory.'
    Assert-Condition -Condition ([System.IO.Path]::GetExtension($artifactPath) -eq '.json') -Message 'Launch artifact must be a JSON file.'
    Assert-Condition -Condition ([System.IO.Path]::GetFileName($artifactPath) -match '^launch-.+\.json$') -Message 'Launch artifact file name does not match the canonical launch artifact naming pattern.'

    $artifact = Get-Content $artifactPath -Raw | ConvertFrom-Json
    Assert-Condition -Condition (-not ($artifact.PSObject.Properties.Name -contains 'failure_diagnostics')) -Message 'Successful launch artifact must not publish failure_diagnostics.'
    Assert-Condition -Condition ([string]$artifact.result.status -eq [string]$LaunchPayload.status) -Message 'Launch artifact status diverges from public payload.'
    Assert-Condition -Condition ([string]$artifact.result.decision -eq [string]$LaunchPayload.decision) -Message 'Launch artifact decision diverges from public payload.'
    Assert-Condition -Condition ([string]$artifact.result.result_mode -eq [string]$LaunchPayload.resultMode) -Message 'Launch artifact resultMode diverges from public payload.'
    Assert-Condition -Condition ([string]$artifact.result.executable_identity -eq [string]$LaunchPayload.executableIdentity) -Message 'Launch artifact executableIdentity diverges from public payload.'
    Assert-Condition -Condition ([int]$artifact.result.process_id -eq [int]$LaunchPayload.processId) -Message 'Launch artifact processId diverges from public payload.'
    Assert-DateTimeOffsetSameInstant -Actual ([string]$artifact.result.started_at_utc) -Expected ([string]$LaunchPayload.startedAtUtc) -Message 'Launch artifact startedAtUtc diverges from public payload.'
    Assert-Condition -Condition ([bool]$artifact.result.has_exited -eq [bool]$LaunchPayload.hasExited) -Message 'Launch artifact hasExited diverges from public payload.'
    Assert-Condition -Condition ([bool]$artifact.result.main_window_observed -eq [bool]$LaunchPayload.mainWindowObserved) -Message 'Launch artifact mainWindowObserved diverges from public payload.'
    Assert-Condition -Condition ([int64]$artifact.result.main_window_handle -eq [int64]$LaunchPayload.mainWindowHandle) -Message 'Launch artifact mainWindowHandle diverges from public payload.'
    Assert-Condition -Condition ([string]$artifact.result.main_window_observation_status -eq [string]$LaunchPayload.mainWindowObservationStatus) -Message 'Launch artifact mainWindowObservationStatus diverges from public payload.'
    Assert-Condition -Condition ([string]$artifact.result.artifact_path -eq [string]$LaunchPayload.artifactPath) -Message 'Launch artifact nested artifactPath diverges from public payload.'
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
        [Parameter(Mandatory)]
        [string] $ExpectedTitle,
        [int] $TimeoutMilliseconds = 10000,
        [int] $PollMilliseconds = 100
    )

    $deadline = (Get-Date).AddMilliseconds($TimeoutMilliseconds)
    do {
        $result = Invoke-ToolCall -Process $Process -Name 'windows.list_windows' -Arguments @{ includeInvisible = $false } -RequestName 'windows.list_windows(visible helper readiness)'

        if ($result.Payload.count -gt 0) {
            $matchingWindow = @($result.Payload.windows | Where-Object { [int64]$_.hwnd -eq $HelperHwnd })
            if ($matchingWindow.Count -eq 1) {
                Assert-Condition -Condition ([string]$matchingWindow[0].title -eq $ExpectedTitle) -Message 'Smoke helper window title did not match the launched helper title.'
                return $result
            }
        }

        Start-Sleep -Milliseconds $PollMilliseconds
    }
    while ((Get-Date) -lt $deadline)

    throw 'Smoke helper window did not appear in visible windows.list_windows inventory in time.'
}

function Get-VisibleWindowsInventory {
    param(
        [Parameter(Mandatory)]
        [System.Diagnostics.Process] $Process,
        [Parameter(Mandatory)]
        [string] $RequestName
    )

    return Invoke-ToolCall -Process $Process -Name 'windows.list_windows' -Arguments @{ includeInvisible = $false } -RequestName $RequestName
}

function Resolve-SmokeOwnedOpenTargetWindow {
    param(
        [Parameter(Mandatory)]
        [System.Diagnostics.Process] $Process,
        [Parameter(Mandatory)]
        [string] $ExpectedTitle,
        [AllowEmptyCollection()]
        [Parameter(Mandatory)]
        [int64[]] $BaselineWindowHwnds,
        [int] $TimeoutMilliseconds = 3000,
        [int] $PollMilliseconds = 150
    )

    $deadline = (Get-Date).AddMilliseconds($TimeoutMilliseconds)
    do {
        $inventory = Get-VisibleWindowsInventory -Process $Process -RequestName 'windows.list_windows(open_target owned window resolution)'
        $matchingWindows = @(
            $inventory.Payload.windows |
                Where-Object {
                    ([string]$_.title -eq $ExpectedTitle) -and
                    ([int64]$_.hwnd -notin $BaselineWindowHwnds)
                })

        if ($matchingWindows.Count -eq 1) {
            return [PSCustomObject]@{
                Inventory = $inventory
                Hwnd = [int64]$matchingWindows[0].hwnd
                Title = [string]$matchingWindows[0].title
            }
        }

        Start-Sleep -Milliseconds $PollMilliseconds
    }
    while ((Get-Date) -lt $deadline)

    return $null
}

function Close-SmokeOwnedWindow {
    param(
        [Parameter(Mandatory)]
        [System.Diagnostics.Process] $Process,
        [Parameter(Mandatory)]
        [int64] $Hwnd,
        [int] $TimeoutMilliseconds = 5000,
        [int] $PollMilliseconds = 150
    )

    $wmClose = 0x0010
    $posted = [WinBridgeSmoke.User32]::PostMessage([IntPtr]::new($Hwnd), $wmClose, [IntPtr]::Zero, [IntPtr]::Zero)
    Assert-Condition -Condition $posted -Message "Smoke could not post WM_CLOSE to owned open_target window hwnd '$Hwnd'."

    $closed = Wait-Until -TimeoutMilliseconds $TimeoutMilliseconds -PollMilliseconds $PollMilliseconds -Predicate {
        $inventory = Get-VisibleWindowsInventory -Process $Process -RequestName 'windows.list_windows(open_target close verification)'
        -not (@($inventory.Payload.windows | ForEach-Object { [int64]$_.hwnd }) -contains $Hwnd)
    }

    Assert-Condition -Condition $closed -Message "Owned open_target window hwnd '$Hwnd' did not close after WM_CLOSE."
}

function Get-AuditEvents {
    param(
        [Parameter(Mandatory)]
        [string] $ArtifactsDirectory
    )

    $eventsPath = Join-Path $ArtifactsDirectory 'events.jsonl'
    if (-not (Test-Path $eventsPath)) {
        return @()
    }

    $rawContent = Get-Content $eventsPath -Raw
    if ([string]::IsNullOrEmpty($rawContent)) {
        return @()
    }

    $lastLineFeedIndex = $rawContent.LastIndexOf("`n", [System.StringComparison]::Ordinal)
    if ($lastLineFeedIndex -lt 0) {
        return @()
    }

    $completedContent = $rawContent.Substring(0, $lastLineFeedIndex + 1)
    return @(
        $completedContent -split "`r?`n" |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            ForEach-Object { $_ | ConvertFrom-Json })
}

function Get-AuditEventCount {
    param(
        [Parameter(Mandatory)]
        [string] $ArtifactsDirectory,
        [Parameter(Mandatory)]
        [string] $ToolName,
        [Parameter(Mandatory)]
        [string] $EventName
    )

    return @(
        Get-AuditEvents -ArtifactsDirectory $ArtifactsDirectory |
            Where-Object {
                ([string]$_.tool_name -eq $ToolName) -and
                ([string]$_.event_name -eq $EventName)
            }).Count
}

function Get-AuditEventsForTool {
    param(
        [Parameter(Mandatory)]
        [string] $ArtifactsDirectory,
        [Parameter(Mandatory)]
        [string] $ToolName,
        [Parameter(Mandatory)]
        [string] $EventName
    )

    return @(
        Get-AuditEvents -ArtifactsDirectory $ArtifactsDirectory |
            Where-Object {
                ([string]$_.tool_name -eq $ToolName) -and
                ([string]$_.event_name -eq $EventName)
            })
}

function Assert-NoInputSensitiveLeakage {
    param(
        [Parameter(Mandatory)]
        [string] $Text,
        [Parameter(Mandatory)]
        [string] $EvidenceName
    )

    Assert-Condition -Condition ($Text -notmatch '"keys?"\s*:') -Message "$EvidenceName exposed raw key/keys fields."
    Assert-Condition -Condition ($Text -notmatch '"text"\s*:') -Message "$EvidenceName exposed raw text field."
    Assert-Condition -Condition ($Text -notmatch 'exception_message') -Message "$EvidenceName exposed raw exception_message."
    Assert-Condition -Condition ($Text -notmatch 'semantic text') -Message "$EvidenceName exposed helper textbox text."
}

function Assert-InputCompatibleBoundsPayload {
    param(
        [Parameter(Mandatory)]
        [object] $Bounds,
        [Parameter(Mandatory)]
        [string] $EvidenceName
    )

    $properties = @($Bounds.PSObject.Properties.Name)
    foreach ($required in @('left', 'top', 'right', 'bottom')) {
        Assert-Condition -Condition ($properties -contains $required) -Message "$EvidenceName is missing required '$required' edge."
    }

    foreach ($unexpected in @('width', 'height')) {
        Assert-Condition -Condition (-not ($properties -contains $unexpected)) -Message "$EvidenceName exposes derived '$unexpected' field and is not input-compatible."
    }

    Assert-Condition -Condition ($properties.Count -eq 4) -Message "$EvidenceName must contain only left/top/right/bottom for input-compatible copy-through."
}

function New-InputCaptureReference {
    param(
        [Parameter(Mandatory)]
        [object] $CapturePayload
    )

    $payloadProperties = @($CapturePayload.PSObject.Properties.Name)
    Assert-Condition -Condition ($payloadProperties -contains 'captureReference') -Message 'Input smoke capture payload does not contain copy-through captureReference.'

    $reference = $CapturePayload.captureReference
    Assert-Condition -Condition ($null -ne $reference) -Message 'Input smoke capture payload contains null captureReference.'
    Assert-InputCompatibleBoundsPayload -Bounds $reference.bounds -EvidenceName 'Input smoke captureReference.bounds'

    $bounds = $CapturePayload.bounds
    Assert-Condition -Condition ($null -ne $bounds) -Message 'Input smoke capture payload does not contain bounds.'
    Assert-Condition -Condition ([int]$reference.bounds.left -eq [int]$bounds.left) -Message 'captureReference.bounds.left must match capture payload bounds.left.'
    Assert-Condition -Condition ([int]$reference.bounds.top -eq [int]$bounds.top) -Message 'captureReference.bounds.top must match capture payload bounds.top.'
    Assert-Condition -Condition ([int]$reference.bounds.right -eq [int]$bounds.right) -Message 'captureReference.bounds.right must match capture payload bounds.right.'
    Assert-Condition -Condition ([int]$reference.bounds.bottom -eq [int]$bounds.bottom) -Message 'captureReference.bounds.bottom must match capture payload bounds.bottom.'
    Assert-Condition -Condition ([int]$reference.pixelWidth -eq [int]$CapturePayload.pixelWidth) -Message 'captureReference.pixelWidth must match capture payload pixelWidth.'
    Assert-Condition -Condition ([int]$reference.pixelHeight -eq [int]$CapturePayload.pixelHeight) -Message 'captureReference.pixelHeight must match capture payload pixelHeight.'
    Assert-Condition -Condition ($null -ne $reference.targetIdentity) -Message 'Input smoke captureReference is missing targetIdentity.'
    Assert-Condition -Condition ([int64]$reference.targetIdentity.hwnd -eq [int64]$CapturePayload.hwnd) -Message 'captureReference.targetIdentity.hwnd must match capture payload hwnd.'
    Assert-Condition -Condition ([int]$reference.targetIdentity.processId -gt 0) -Message 'captureReference.targetIdentity.processId must be positive.'
    Assert-Condition -Condition ([int]$reference.targetIdentity.threadId -gt 0) -Message 'captureReference.targetIdentity.threadId must be positive.'
    Assert-Condition -Condition (-not [string]::IsNullOrWhiteSpace([string]$reference.targetIdentity.className)) -Message 'captureReference.targetIdentity.className must be populated.'

    if (($payloadProperties -contains 'frameBounds') -and $null -ne $CapturePayload.frameBounds) {
        $frameBounds = $CapturePayload.frameBounds
        Assert-Condition -Condition ($null -ne $reference.frameBounds) -Message 'Input smoke captureReference is missing frameBounds while capture payload publishes frameBounds.'
        Assert-InputCompatibleBoundsPayload -Bounds $reference.frameBounds -EvidenceName 'Input smoke captureReference.frameBounds'
        Assert-Condition -Condition ([int]$reference.frameBounds.left -eq [int]$frameBounds.left) -Message 'captureReference.frameBounds.left must match capture payload frameBounds.left.'
        Assert-Condition -Condition ([int]$reference.frameBounds.top -eq [int]$frameBounds.top) -Message 'captureReference.frameBounds.top must match capture payload frameBounds.top.'
        Assert-Condition -Condition ([int]$reference.frameBounds.right -eq [int]$frameBounds.right) -Message 'captureReference.frameBounds.right must match capture payload frameBounds.right.'
        Assert-Condition -Condition ([int]$reference.frameBounds.bottom -eq [int]$frameBounds.bottom) -Message 'captureReference.frameBounds.bottom must match capture payload frameBounds.bottom.'
    }

    return $reference
}

function Get-CaptureRelativeCenterPoint {
    param(
        [Parameter(Mandatory)]
        [object] $UiaNode,
        [Parameter(Mandatory)]
        [object] $CapturePayload
    )

    $nodeBounds = $UiaNode.boundingRectangle
    Assert-Condition -Condition ($null -ne $nodeBounds) -Message 'Input smoke UIA target does not contain boundingRectangle.'
    $captureBounds = $CapturePayload.bounds
    Assert-Condition -Condition ($null -ne $captureBounds) -Message 'Input smoke capture payload does not contain bounds.'

    $centerScreenX = [int][Math]::Floor((([double]$nodeBounds.left + [double]$nodeBounds.right) / 2.0))
    $centerScreenY = [int][Math]::Floor((([double]$nodeBounds.top + [double]$nodeBounds.bottom) / 2.0))
    $captureX = $centerScreenX - [int]$captureBounds.left
    $captureY = $centerScreenY - [int]$captureBounds.top

    Assert-Condition -Condition ($captureX -ge 0 -and $captureX -lt [int]$CapturePayload.pixelWidth) -Message 'Input smoke textbox center X is outside capture_pixels raster.'
    Assert-Condition -Condition ($captureY -ge 0 -and $captureY -lt [int]$CapturePayload.pixelHeight) -Message 'Input smoke textbox center Y is outside capture_pixels raster.'

    return @{
        x = [int]$captureX
        y = [int]$captureY
    }
}

function Assert-InputLiveResult {
    param(
        [Parameter(Mandatory)]
        [object] $ToolCall,
        [Parameter(Mandatory)]
        [int64] $ExpectedHwnd
    )

    Assert-Condition -Condition (-not [bool]$ToolCall.Json.result.isError) -Message 'windows.input live click returned isError=true.'
    Assert-Condition -Condition (@($ToolCall.Json.result.content).Count -eq 1) -Message 'windows.input must return exactly one text content block.'
    Assert-Condition -Condition ([string]$ToolCall.Json.result.content[0].type -eq 'text') -Message 'windows.input content block must be text-only.'
    Assert-Condition -Condition (@($ToolCall.Json.result.content | Where-Object { $_.type -eq 'image' }).Count -eq 0) -Message 'windows.input must not return image content blocks.'

    $payload = $ToolCall.Payload
    Assert-Condition -Condition ([string]$payload.status -eq 'verify_needed') -Message "windows.input live click returned status '$($payload.status)' instead of verify_needed."
    Assert-Condition -Condition ([string]$payload.decision -eq 'verify_needed') -Message "windows.input live click returned decision '$($payload.decision)' instead of verify_needed."
    Assert-Condition -Condition ([string]$payload.resultMode -eq 'dispatch_only') -Message "windows.input live click returned resultMode '$($payload.resultMode)' instead of dispatch_only."
    Assert-Condition -Condition ([int64]$payload.targetHwnd -eq $ExpectedHwnd) -Message 'windows.input live click targetHwnd diverges from helper hwnd.'
    Assert-Condition -Condition ([string]$payload.targetSource -eq 'explicit') -Message "windows.input targetSource is '$($payload.targetSource)', expected explicit."
    Assert-Condition -Condition ([int]$payload.completedActionCount -eq 1) -Message 'windows.input live click did not report one completed action.'
    Assert-Condition -Condition ($null -eq $payload.failedActionIndex) -Message 'windows.input live click unexpectedly reported failedActionIndex.'
    Assert-Condition -Condition (-not [string]::IsNullOrWhiteSpace([string]$payload.artifactPath)) -Message 'windows.input live click did not return artifactPath.'
    Assert-Condition -Condition (Test-Path ([string]$payload.artifactPath)) -Message "windows.input artifact '$($payload.artifactPath)' was not created."

    $actions = @($payload.actions)
    Assert-Condition -Condition ($actions.Count -eq 1) -Message "windows.input live click returned $($actions.Count) action results instead of one."
    Assert-Condition -Condition ([string]$actions[0].type -eq 'click') -Message 'windows.input live click action result did not preserve type=click.'
    Assert-Condition -Condition ([string]$actions[0].status -eq 'verify_needed') -Message 'windows.input live click action result did not preserve verify_needed status.'
    Assert-Condition -Condition ([string]$actions[0].coordinateSpace -eq 'capture_pixels') -Message 'windows.input live click action result did not preserve capture_pixels coordinate space.'
    Assert-Condition -Condition ([string]$actions[0].button -eq 'left') -Message 'windows.input live click action result did not preserve button=left.'
}

function Assert-InputArtifact {
    param(
        [Parameter(Mandatory)]
        [string] $ArtifactsDirectory,
        [Parameter(Mandatory)]
        [object] $InputPayload,
        [Parameter(Mandatory)]
        [int64] $ExpectedHwnd
    )

    $artifactPath = [System.IO.Path]::GetFullPath([string]$InputPayload.artifactPath)
    $expectedInputDirectory = [System.IO.Path]::GetFullPath((Join-Path $ArtifactsDirectory 'input'))
    $expectedInputPrefix = $expectedInputDirectory + [System.IO.Path]::DirectorySeparatorChar
    Assert-Condition -Condition ($artifactPath.StartsWith($expectedInputPrefix, [System.StringComparison]::OrdinalIgnoreCase)) -Message 'Input artifact path is outside the canonical diagnostics input directory.'
    Assert-Condition -Condition ([System.IO.Path]::GetExtension($artifactPath) -eq '.json') -Message 'Input artifact must be a JSON file.'
    Assert-Condition -Condition ([System.IO.Path]::GetFileName($artifactPath) -match '^input-.+\.json$') -Message 'Input artifact file name does not match the canonical naming pattern.'

    $artifactText = Get-Content $artifactPath -Raw
    Assert-NoInputSensitiveLeakage -Text $artifactText -EvidenceName 'Input artifact'
    $artifact = $artifactText | ConvertFrom-Json

    Assert-Condition -Condition (-not ($artifact.PSObject.Properties.Name -contains 'failure_diagnostics')) -Message 'Successful input artifact must not publish failure_diagnostics.'
    Assert-Condition -Condition ([int]$artifact.request_summary.action_count -eq 1) -Message 'Input artifact request_summary action_count drifted.'
    Assert-Condition -Condition ([string]$artifact.request_summary.action_types[0] -eq 'click') -Message 'Input artifact request_summary action_types drifted.'
    Assert-Condition -Condition ([string]$artifact.request_summary.coordinate_spaces[0] -eq 'capture_pixels') -Message 'Input artifact request_summary coordinate_spaces drifted.'
    Assert-Condition -Condition ([int64]$artifact.request_summary.target_hwnd -eq $ExpectedHwnd) -Message 'Input artifact request_summary target_hwnd drifted.'
    Assert-Condition -Condition ([string]$artifact.request_summary.target_source -eq 'explicit') -Message 'Input artifact request_summary target_source drifted.'
    Assert-Condition -Condition ([int64]$artifact.target_summary.target_hwnd -eq $ExpectedHwnd) -Message 'Input artifact target_summary target_hwnd drifted.'
    Assert-Condition -Condition ([string]$artifact.result.status -eq [string]$InputPayload.status) -Message 'Input artifact status diverges from public payload.'
    Assert-Condition -Condition ([string]$artifact.result.decision -eq [string]$InputPayload.decision) -Message 'Input artifact decision diverges from public payload.'
    Assert-Condition -Condition ([string]$artifact.result.result_mode -eq [string]$InputPayload.resultMode) -Message 'Input artifact resultMode diverges from public payload.'
    Assert-Condition -Condition ([int]$artifact.result.completed_action_count -eq [int]$InputPayload.completedActionCount) -Message 'Input artifact completed_action_count diverges from public payload.'
    Assert-Condition -Condition ([string]$artifact.result.artifact_path -eq [string]$InputPayload.artifactPath) -Message 'Input artifact nested artifactPath diverges from public payload.'
    Assert-Condition -Condition ([string]$artifact.result.committed_side_effect_evidence -eq 'completed_actions_committed') -Message 'Input artifact committed_side_effect_evidence drifted.'

    $artifactActions = @($artifact.result.actions)
    Assert-Condition -Condition ($artifactActions.Count -eq 1) -Message 'Input artifact must contain exactly one action result.'
    Assert-Condition -Condition ([string]$artifactActions[0].type -eq 'click') -Message 'Input artifact action type drifted.'
    Assert-Condition -Condition ([string]$artifactActions[0].coordinate_space -eq 'capture_pixels') -Message 'Input artifact action coordinate_space drifted.'
    Assert-Condition -Condition ($null -ne $artifactActions[0].resolved_screen_point) -Message 'Input artifact action missing resolved_screen_point.'
}

function Assert-InputRuntimeEvent {
    param(
        [Parameter(Mandatory)]
        [string] $ArtifactsDirectory,
        [Parameter(Mandatory)]
        [int] $BaselineEventCount,
        [Parameter(Mandatory)]
        [object] $InputPayload
    )

    $inputEvents = @(Get-AuditEventsForTool -ArtifactsDirectory $ArtifactsDirectory -ToolName 'windows.input' -EventName 'input.runtime.completed')
    Assert-Condition -Condition ($inputEvents.Count -eq ($BaselineEventCount + 1)) -Message "Input smoke expected exactly one new input.runtime.completed event, got baseline=$BaselineEventCount current=$($inputEvents.Count)."
    $inputEvent = $inputEvents[-1]
    $eventText = $inputEvent | ConvertTo-Json -Compress -Depth 12
    Assert-NoInputSensitiveLeakage -Text $eventText -EvidenceName 'Input runtime event'
    Assert-Condition -Condition ([string]$inputEvent.outcome -eq 'verify_needed') -Message "Input runtime event returned unexpected outcome '$($inputEvent.outcome)'."
    Assert-Condition -Condition ([string]$inputEvent.data.status -eq [string]$InputPayload.status) -Message 'Input runtime event status diverges from public payload.'
    Assert-Condition -Condition ([string]$inputEvent.data.decision -eq [string]$InputPayload.decision) -Message 'Input runtime event decision diverges from public payload.'
    Assert-Condition -Condition ([string]$inputEvent.data.result_mode -eq [string]$InputPayload.resultMode) -Message 'Input runtime event result_mode diverges from public payload.'
    Assert-Condition -Condition ([string]$inputEvent.data.target_hwnd -eq [string]$InputPayload.targetHwnd) -Message 'Input runtime event target_hwnd diverges from public payload.'
    Assert-Condition -Condition ([string]$inputEvent.data.target_source -eq [string]$InputPayload.targetSource) -Message 'Input runtime event target_source diverges from public payload.'
    Assert-Condition -Condition ([string]$inputEvent.data.completed_action_count -eq [string]$InputPayload.completedActionCount) -Message 'Input runtime event completed_action_count diverges from public payload.'
    Assert-Condition -Condition ([string]$inputEvent.data.action_types -eq 'click') -Message 'Input runtime event action_types drifted.'
    Assert-Condition -Condition ([string]$inputEvent.data.coordinate_spaces -eq 'capture_pixels') -Message 'Input runtime event coordinate_spaces drifted.'
    Assert-Condition -Condition ([string]$inputEvent.data.artifact_path -eq [string]$InputPayload.artifactPath) -Message 'Input runtime event artifact_path diverges from public payload.'
    Assert-Condition -Condition ([string]$inputEvent.data.committed_side_effect_evidence -eq 'completed_actions_committed') -Message 'Input runtime event committed_side_effect_evidence drifted.'
    Assert-Condition -Condition ($null -eq $inputEvent.data.failure_code) -Message 'Successful input runtime event must not publish failure_code.'
    Assert-Condition -Condition ($null -eq $inputEvent.data.failed_action_index) -Message 'Successful input runtime event must not publish failed_action_index.'
    Assert-Condition -Condition ($null -eq $inputEvent.data.failure_stage) -Message 'Successful input runtime event must not publish failure_stage.'
    Assert-Condition -Condition ($null -eq $inputEvent.data.exception_type) -Message 'Successful input runtime event must not publish exception_type.'

    return $inputEvent
}

function Start-ToolCallRequest {
    param(
        [Parameter(Mandatory)]
        [System.Diagnostics.Process] $Process,
        [Parameter(Mandatory)]
        [string] $Name,
        [Parameter(Mandatory)]
        [hashtable] $Arguments
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

    return [PSCustomObject]@{
        Id = $requestId
        RawRequest = $rawRequest
    }
}

function Complete-ToolCallRequest {
    param(
        [Parameter(Mandatory)]
        [System.Diagnostics.Process] $Process,
        [Parameter(Mandatory)]
        [object] $PendingRequest,
        [Parameter(Mandatory)]
        [string] $RequestName
    )

    $response = Read-Response -Process $Process -RequestName $RequestName -ExpectedId ([int]$PendingRequest.Id)
    return [PSCustomObject]@{
        Id = $PendingRequest.Id
        RawRequest = $PendingRequest.RawRequest
        RawResponse = $response.Raw
        Json = $response.Json
        Payload = Get-ToolPayload -ToolResponse $response.Json
    }
}

function Assert-LaunchRuntimeEvent {
    param(
        [Parameter(Mandatory)]
        [string] $ArtifactsDirectory,
        [Parameter(Mandatory)]
        [object] $LaunchPayload
    )

    $eventsPath = Join-Path $ArtifactsDirectory 'events.jsonl'
    Assert-Condition -Condition (Test-Path $eventsPath) -Message "Launch smoke expected events file '$eventsPath'."

    $launchEvents = @(
        Get-AuditEvents -ArtifactsDirectory $ArtifactsDirectory |
            Where-Object {
                ([string]$_.tool_name -eq 'windows.launch_process') -and
                ([string]$_.event_name -eq 'launch.runtime.completed')
            })

    Assert-Condition -Condition ($launchEvents.Count -eq 1) -Message "Launch smoke expected exactly one launch.runtime.completed event, got $($launchEvents.Count)."

    $launchEvent = $launchEvents[0]
    $expectedHasExited = if ([bool]$LaunchPayload.hasExited) { 'true' } else { 'false' }
    $expectedMainWindowObserved = if ([bool]$LaunchPayload.mainWindowObserved) { 'true' } else { 'false' }
    $eventDataProperties = @($launchEvent.data.PSObject.Properties.Name | Sort-Object)
    Assert-Condition -Condition (
        (Join-ContractValues -Values $eventDataProperties) -eq 'artifact_path,decision,exception_type,executable_identity,exit_code,failure_code,failure_stage,has_exited,main_window_handle,main_window_observation_status,main_window_observed,process_id,result_mode,started_at_utc,status'
    ) -Message 'Launch runtime event exposed unexpected fields outside the safe runtime-event contract.'
    Assert-Condition -Condition ([string]$launchEvent.outcome -eq 'done') -Message "Launch runtime event returned unexpected outcome '$($launchEvent.outcome)'."
    Assert-Condition -Condition ([string]$launchEvent.data.status -eq [string]$LaunchPayload.status) -Message 'Launch runtime event status diverges from public payload.'
    Assert-Condition -Condition ([string]$launchEvent.data.decision -eq [string]$LaunchPayload.decision) -Message 'Launch runtime event decision diverges from public payload.'
    Assert-Condition -Condition ([string]$launchEvent.data.result_mode -eq [string]$LaunchPayload.resultMode) -Message 'Launch runtime event result_mode diverges from public payload.'
    Assert-Condition -Condition ([string]$launchEvent.data.executable_identity -eq [string]$LaunchPayload.executableIdentity) -Message 'Launch runtime event executable_identity diverges from public payload.'
    Assert-Condition -Condition ([int]$launchEvent.data.process_id -eq [int]$LaunchPayload.processId) -Message 'Launch runtime event process_id diverges from public payload.'
    Assert-DateTimeOffsetSameInstant -Actual ([string]$launchEvent.data.started_at_utc) -Expected ([string]$LaunchPayload.startedAtUtc) -Message 'Launch runtime event started_at_utc diverges from public payload.'
    Assert-Condition -Condition ([string]$launchEvent.data.has_exited -eq $expectedHasExited) -Message 'Launch runtime event has_exited diverges from public payload.'
    Assert-Condition -Condition ([string]$launchEvent.data.main_window_observed -eq $expectedMainWindowObserved) -Message 'Launch runtime event main_window_observed diverges from public payload.'
    Assert-Condition -Condition ([int64]$launchEvent.data.main_window_handle -eq [int64]$LaunchPayload.mainWindowHandle) -Message 'Launch runtime event main_window_handle diverges from public payload.'
    Assert-Condition -Condition ([string]$launchEvent.data.main_window_observation_status -eq [string]$LaunchPayload.mainWindowObservationStatus) -Message 'Launch runtime event main_window_observation_status diverges from public payload.'
    Assert-Condition -Condition ([string]$launchEvent.data.artifact_path -eq [string]$LaunchPayload.artifactPath) -Message 'Launch runtime event artifact_path diverges from public payload.'
    Assert-Condition -Condition ($null -eq $launchEvent.data.failure_code) -Message 'Successful launch runtime event must not publish failure_code.'
    Assert-Condition -Condition ($null -eq $launchEvent.data.exit_code) -Message 'Successful launch runtime event must not publish exit_code.'
    Assert-Condition -Condition ($null -eq $launchEvent.data.failure_stage) -Message 'Successful launch runtime event must not publish failure_stage.'
    Assert-Condition -Condition ($null -eq $launchEvent.data.exception_type) -Message 'Successful launch runtime event must not publish exception_type.'
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

function Invoke-ActivateWindowUntilForeground {
    param(
        [Parameter(Mandatory)]
        [System.Diagnostics.Process] $Process,
        [Parameter(Mandatory)]
        [int64] $ExpectedHwnd,
        [int] $TimeoutMilliseconds = 5000,
        [int] $PollMilliseconds = 250
    )

    $deadline = (Get-Date).AddMilliseconds($TimeoutMilliseconds)
    $attempts = @()
    $wasMinimizedObserved = $false
    $lastCall = $null
    $lastPayload = $null

    do {
        $attemptNumber = $attempts.Count + 1
        $toolCall = Invoke-ToolCall -Process $Process -Name 'windows.activate_window' -Arguments @{} -RequestName "windows.activate_window(attempt $attemptNumber)"
        $result = $toolCall.Json.result
        $payload = $result.structuredContent
        $status = [string]$payload.status
        $windowPayload = $payload.window
        $hasExpectedWindow = $null -ne $windowPayload -and $null -ne $windowPayload.hwnd -and ([long]$windowPayload.hwnd -eq $ExpectedHwnd)
        $retriableForegroundFailure = $status -eq 'failed' -and $hasExpectedWindow
        Assert-Condition -Condition ((@('done', 'ambiguous') -contains $status) -or $retriableForegroundFailure) -Message "ActivateWindow returned non-retriable status '$status'."
        Assert-Condition -Condition ([bool]$result.isError -eq ($status -ne 'done')) -Message 'ActivateWindow isError does not match done/error semantics.'
        Assert-Condition -Condition $hasExpectedWindow -Message 'ActivateWindow payload hwnd does not match helper window.'
        Assert-Condition -Condition ([bool]$payload.isForeground -eq ($status -eq 'done')) -Message 'ActivateWindow payload isForeground does not match done/error semantics.'

        if ([bool]$payload.wasMinimized) {
            $wasMinimizedObserved = $true
        }

        $attempts += [ordered]@{
            status = $status
            isForeground = [bool]$payload.isForeground
            wasMinimized = [bool]$payload.wasMinimized
            reason = [string]$payload.reason
        }
        $lastCall = $toolCall
        $lastPayload = $payload

        if ($status -eq 'done' -and [bool]$payload.isForeground) {
            return [PSCustomObject]@{
                ToolCall = $toolCall
                Payload = $payload
                Attempts = $attempts
                WasMinimizedObserved = $wasMinimizedObserved
            }
        }

        Start-Sleep -Milliseconds $PollMilliseconds
    }
    while ((Get-Date) -lt $deadline)

    $lastStatus = if ($null -ne $lastPayload) { [string]$lastPayload.status } else { '<none>' }
    throw "ActivateWindow did not confirm foreground helper window in time. Last status: $lastStatus; attempts: $($attempts.Count)."
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
        [string] $Description,
        [switch] $Synchronous
    )

    if ($Synchronous) {
        [void][WinBridgeSmoke.User32]::SendMessage([IntPtr]::new($Hwnd), $Message, [IntPtr]::Zero, [IntPtr]::Zero)
        return
    }

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

function Start-StagedMcpHost {
    param(
        [Parameter(Mandatory)]
        [string] $ServerDll,
        [Parameter(Mandatory)]
        [string] $WorkingDirectory
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'dotnet'
    $startInfo.Arguments = "`"$ServerDll`""
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.UseShellExecute = $false
    $startInfo.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $startInfo.StandardErrorEncoding = [System.Text.Encoding]::UTF8

    $serverProcess = [System.Diagnostics.Process]::new()
    $serverProcess.StartInfo = $startInfo
    $serverProcess.Start() | Out-Null
    return $serverProcess
}

function Stop-StagedMcpHost {
    param(
        [AllowNull()]
        [System.Diagnostics.Process] $Process
    )

    if ($null -eq $Process) {
        return
    }

    if (-not $Process.HasExited) {
        try {
            $Process.StandardInput.Close()
        }
        catch [System.InvalidOperationException] {
        }
    }

    if (-not $Process.HasExited -and -not $Process.WaitForExit(5000)) {
        $Process.Kill()
        $Process.WaitForExit()
    }
}

function Stop-SmokeOwnedHelperProcess {
    param(
        [AllowNull()]
        [System.Diagnostics.Process] $Process,
        [AllowNull()]
        [int] $ProcessId
    )

    if ($null -ne $Process -and -not $Process.HasExited) {
        $Process.Kill()
        $Process.WaitForExit()
        return
    }

    if ($null -ne $ProcessId -and $ProcessId -gt 0) {
        try {
            $fallback = [System.Diagnostics.Process]::GetProcessById([int]$ProcessId)
            if (-not $fallback.HasExited) {
                $fallback.Kill()
                $fallback.WaitForExit()
            }
        }
        catch [System.ArgumentException] {
        }
    }
}

$process = Start-StagedMcpHost -ServerDll $serverDll -WorkingDirectory $repoRoot
$freshProcess = $null
$helperProcess = $null
$helperProcessId = $null
$helperProcessIdsBeforeLive = $null
$liveReconciliation = $null
$helperHwnd = $null

try {
    Invoke-NativeCommand -Description 'tool contract export for smoke' -Command {
        dotnet "$serverDll" --export-tool-contract-json "$contractPath"
    }

    $manifest = Get-Content $contractPath -Raw | ConvertFrom-Json
    $requiredTools = Get-RequiredToolNames -Manifest $manifest

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

    $helperTitle = "Okno Launch Smoke $runId"
    $helperOwnershipMarker = "okno-smoke-run:$runId"
    $helperExecutableIdentity = [System.IO.Path]::GetFileName($helperExe)
    $helperProcessName = [System.IO.Path]::GetFileNameWithoutExtension($helperExe)
    $helperLifetimeMs = $helperLaunchWaitTimeoutMs + $helperWindowMaterializationTimeoutMs + $waitTimeoutForegroundMs + $waitTimeoutFocusMs + (3 * $waitTimeoutSemanticUiMs) + $waitTimeoutElementGoneMs + $waitTimeoutVisualMs + $helperLifetimeSafetyBufferMs
    $helperLaunchArgs = Get-SmokeHelperLaunchArguments -Title $helperTitle -OwnershipMarker $helperOwnershipMarker -LifetimeMs $helperLifetimeMs -VisualBurstMs $helperVisualBurstMs
    Assert-Condition -Condition (Test-Path $helperExe) -Message "Smoke helper executable '$helperExe' does not exist. Build the solution before running smoke."
    $helperProcessIdsBeforeDryRun = @(Get-SmokeHelperProcessIds -ProcessName $helperProcessName -ExpectedOwnershipMarker $helperOwnershipMarker)
    $dryRunPreviewEventCountBefore = Get-AuditEventCount -ArtifactsDirectory ([string]$healthPayload.artifactsDirectory) -ToolName 'windows.launch_process' -EventName 'launch.preview.completed'
    $dryRunLaunchRuntimeEventCountBefore = Get-AuditEventCount -ArtifactsDirectory ([string]$healthPayload.artifactsDirectory) -ToolName 'windows.launch_process' -EventName 'launch.runtime.completed'

    $launchDryRunCall = Invoke-ToolCall -Process $process -Name 'windows.launch_process' -Arguments @{
        executable = $helperExe
        args = $helperLaunchArgs
        workingDirectory = $repoRoot
        waitForWindow = $true
        timeoutMs = 10000
        dryRun = $true
        confirm = $false
    } -RequestName 'windows.launch_process(dry_run helper)'
    $rawLaunchDryRunRequest = $launchDryRunCall.RawRequest
    $launchDryRunResponse = [PSCustomObject]@{
        Raw = $launchDryRunCall.RawResponse
        Json = $launchDryRunCall.Json
    }
    $launchDryRunPayload = $launchDryRunCall.Payload
    $dryRunReconciliation = Get-SmokeHelperBaseReconciliationState `
        -HelperProcessName $helperProcessName `
        -ExpectedOwnershipMarker $helperOwnershipMarker `
        -ExpectedExecutablePath $helperExe `
        -ExpectedTitle $helperTitle `
        -BaselineProcessIds $helperProcessIdsBeforeDryRun
    try {
        Assert-LaunchProcessPreview `
            -ToolCall $launchDryRunCall `
            -ExpectedExecutableIdentity $helperExecutableIdentity `
            -ExpectedArgumentCount $helperLaunchArgs.Count `
            -ExpectedWorkingDirectoryProvided $true `
            -ExpectedWaitForWindow $true `
            -ExpectedTimeoutMs 10000
        Assert-DryRunPreviewRuntimeEvent -ArtifactsDirectory ([string]$healthPayload.artifactsDirectory) -BaselineEventCount $dryRunPreviewEventCountBefore
        Assert-NoDryRunRuntimeEvent -ArtifactsDirectory ([string]$healthPayload.artifactsDirectory) -BaselineEventCount $dryRunLaunchRuntimeEventCountBefore
        $dryRunWindowLeaks = @(Get-SmokeHelperWindowLeaks -Process $process -ExpectedTitle $helperTitle -RequestName 'windows.list_windows(dry_run side_effect_check)')
        $dryRunResolvedReconciliation = Resolve-SmokeHelperCleanupProcessIds -ReconciliationState $dryRunReconciliation -TitleResolutionTimeoutMs $helperWindowMaterializationTimeoutMs
        Assert-NoDryRunLaunchSideEffects -ProcessLeakIds $dryRunResolvedReconciliation.CleanupProcessIds -WindowLeaks $dryRunWindowLeaks
    }
    finally {
        if ($null -eq $dryRunReconciliation) {
            $dryRunReconciliation = Get-SmokeHelperBaseReconciliationState `
                -HelperProcessName $helperProcessName `
                -ExpectedOwnershipMarker $helperOwnershipMarker `
                -ExpectedExecutablePath $helperExe `
                -ExpectedTitle $helperTitle `
                -BaselineProcessIds $helperProcessIdsBeforeDryRun
        }

        $dryRunResolvedReconciliation = Resolve-SmokeHelperCleanupProcessIds -ReconciliationState $dryRunReconciliation -TitleResolutionTimeoutMs $helperWindowMaterializationTimeoutMs
        if ($dryRunResolvedReconciliation.CleanupProcessIds.Count -gt 0) {
            Stop-SmokeHelperLeaks -ProcessIds $dryRunResolvedReconciliation.CleanupProcessIds
        }
    }

    $helperProcessIdsBeforeLive = @(Get-SmokeHelperProcessIds -ProcessName $helperProcessName -ExpectedOwnershipMarker $helperOwnershipMarker)
    $launchLiveCall = Invoke-ToolCall -Process $process -Name 'windows.launch_process' -Arguments @{
        executable = $helperExe
        args = $helperLaunchArgs
        workingDirectory = $repoRoot
        waitForWindow = $true
        timeoutMs = 10000
        dryRun = $false
        confirm = $true
    } -RequestName 'windows.launch_process(live helper)'
    $rawLaunchLiveRequest = $launchLiveCall.RawRequest
    $launchLiveResponse = [PSCustomObject]@{
        Raw = $launchLiveCall.RawResponse
        Json = $launchLiveCall.Json
    }
    $launchLivePayload = $launchLiveCall.Payload
    $liveReconciliation = Get-SmokeHelperBaseReconciliationState `
        -HelperProcessName $helperProcessName `
        -ExpectedOwnershipMarker $helperOwnershipMarker `
        -ExpectedExecutablePath $helperExe `
        -ExpectedTitle $helperTitle `
        -BaselineProcessIds $helperProcessIdsBeforeLive
    $helperProcessId = [int]$launchLivePayload.processId
    $helperProcess = Get-ProcessByIdWithRetry -ProcessId $helperProcessId
    Assert-LaunchProcessLiveResult -ToolCall $launchLiveCall -ExpectedExecutableIdentity $helperExecutableIdentity
    Assert-LaunchArtifact -ArtifactsDirectory ([string]$healthPayload.artifactsDirectory) -LaunchPayload $launchLivePayload
    $helperHwnd = Wait-ForMainWindowHandle -Process $helperProcess
    Assert-Condition -Condition ($helperHwnd -eq [int64]$launchLivePayload.mainWindowHandle) -Message 'Live launch cross-check observed a different helper hwnd than the public payload.'
    $helperProcess.Refresh()
    Assert-Condition -Condition ([string]$helperProcess.MainWindowTitle -eq $helperTitle) -Message 'Live launch cross-check observed an unexpected helper window title.'

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

    $visibleWindowResult = Wait-ForVisibleHelperWindow -Process $process -HelperHwnd $helperHwnd -ExpectedTitle $helperTitle -TimeoutMilliseconds $helperWindowMaterializationTimeoutMs
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
    $inputPlanningUiaSnapshotPayload = $null
    $inputClickPayload = $null
    $inputClickPoint = $null
    $inputRuntimeEvent = $null
    $inputFocusWaitPayload = $null
    $elementWaitPayload = $null
    $transientExistsWaitPayload = $null
    $textWaitPayload = $null
    $elementGoneWaitPayload = $null
    $visualWaitPayload = $null
    $freshHostAcceptance = $null

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

    $activateProof = Invoke-ActivateWindowUntilForeground -Process $process -ExpectedHwnd $helperHwnd -TimeoutMilliseconds $waitTimeoutForegroundMs
    $activateCall = $activateProof.ToolCall
    $rawActivateRequest = $activateCall.RawRequest
    $activateResponse = [PSCustomObject]@{
        Raw = $activateCall.RawResponse
        Json = $activateCall.Json
    }
    $activateResult = $activateCall.Json.result
    $activatePayload = $activateProof.Payload
    $activateAttempts = $activateProof.Attempts
    $activateStatus = [string]$activatePayload.status
    Assert-Condition -Condition ([string]$activateStatus -eq 'done') -Message 'ActivateWindow proof must end with status=done before input smoke continues.'
    Assert-Condition -Condition ([bool]$activateProof.WasMinimizedObserved) -Message 'ActivateWindow proof must observe wasMinimized=true for helper window.'
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

    $postCaptureActivateProof = Invoke-ActivateWindowUntilForeground -Process $process -ExpectedHwnd $helperHwnd -TimeoutMilliseconds $waitTimeoutForegroundMs
    Assert-Condition -Condition ([string]$postCaptureActivateProof.Payload.status -eq 'done') -Message 'Post-capture activation must end with status=done before active_window_matches.'
    $activeWaitCall = Invoke-WaitToolCall -Process $process -Condition 'active_window_matches' -TimeoutMs $waitTimeoutForegroundMs -RequestName 'windows.wait(active_window_matches)'
    $rawActiveWaitRequest = $activeWaitCall.RawRequest
    $activeWaitResponse = [PSCustomObject]@{
        Raw = $activeWaitCall.RawResponse
        Json = $activeWaitCall.Json
    }
    $activeWaitPayload = Assert-WaitSuccess -ToolCall $activeWaitCall -Condition 'active_window_matches'
    Assert-Condition -Condition ([bool]$activeWaitPayload.lastObserved.targetIsForeground) -Message 'active_window_matches must confirm foreground=true in lastObserved.'

    Send-HelperCommand -Hwnd $helperHwnd -Message $wmAppPrepareFocus -Description 'prepare_focus' -Synchronous
    $prepareFocusActivateProof = Invoke-ActivateWindowUntilForeground -Process $process -ExpectedHwnd $helperHwnd -TimeoutMilliseconds $waitTimeoutForegroundMs
    Assert-Condition -Condition ([string]$prepareFocusActivateProof.Payload.status -eq 'done') -Message 'Prepare-focus foreground proof must end with status=done before focus_is.'
    Send-HelperCommand -Hwnd $helperHwnd -Message $wmAppPrepareFocus -Description 'prepare_focus_after_foreground' -Synchronous
    $focusWaitCall = Invoke-WaitToolCall -Process $process -Condition 'focus_is' -Selector @{ name = 'Run semantic smoke'; controlType = 'button' } -TimeoutMs $waitTimeoutFocusMs -RequestName 'windows.wait(focus_is)'
    $rawFocusWaitRequest = $focusWaitCall.RawRequest
    $focusWaitResponse = [PSCustomObject]@{
        Raw = $focusWaitCall.RawResponse
        Json = $focusWaitCall.Json
    }
    $focusWaitPayload = Assert-WaitSuccess -ToolCall $focusWaitCall -Condition 'focus_is'
    Assert-Condition -Condition ([string]$focusWaitPayload.matchedElement.controlType -eq 'button') -Message 'focus_is did not resolve the expected focused helper button.'

    $inputPlanningUiaSnapshotCall = Wait-ForSemanticUiaSnapshot -Process $process
    $rawInputPlanningUiaSnapshotRequest = $inputPlanningUiaSnapshotCall.RawRequest
    $inputPlanningUiaSnapshotResponse = [PSCustomObject]@{
        Raw = $inputPlanningUiaSnapshotCall.RawResponse
        Json = $inputPlanningUiaSnapshotCall.Json
    }
    $inputPlanningUiaSnapshotPayload = $inputPlanningUiaSnapshotCall.Payload
    $inputPlanningEditNodes = @(Find-UiaNodes -Node $inputPlanningUiaSnapshotPayload.root -ControlType 'edit' -Name 'Smoke query input')
    Assert-Condition -Condition ($inputPlanningEditNodes.Count -gt 0) -Message 'Input planning UIA snapshot did not include Smoke query input edit control.'
    $inputClickPoint = Get-CaptureRelativeCenterPoint -UiaNode $inputPlanningEditNodes[0] -CapturePayload $helperCapturePayload
    Assert-Condition -Condition (($helperCapturePayload.PSObject.Properties.Name -contains 'frameBounds') -and $null -ne $helperCapturePayload.frameBounds) -Message 'Input smoke capture payload does not contain capture-time frameBounds.'
    $inputCaptureReference = New-InputCaptureReference -CapturePayload $helperCapturePayload
    $inputPreflightActivateProof = Invoke-ActivateWindowUntilForeground -Process $process -ExpectedHwnd $helperHwnd -TimeoutMilliseconds $waitTimeoutForegroundMs
    Assert-Condition -Condition ([string]$inputPreflightActivateProof.Payload.status -eq 'done') -Message 'Input preflight activation must end with status=done before windows.input.'
    $inputRuntimeEventCountBefore = Get-AuditEventCount -ArtifactsDirectory ([string]$healthPayload.artifactsDirectory) -ToolName 'windows.input' -EventName 'input.runtime.completed'
    $activationStartedCountBeforeInput = Get-AuditEventCount -ArtifactsDirectory ([string]$healthPayload.artifactsDirectory) -ToolName 'windows.activate_window' -EventName 'tool.invocation.started'
    $focusStartedCountBeforeInput = Get-AuditEventCount -ArtifactsDirectory ([string]$healthPayload.artifactsDirectory) -ToolName 'windows.focus_window' -EventName 'tool.invocation.started'

    $inputClickCall = Invoke-ToolCall -Process $process -Name 'windows.input' -Arguments @{
        hwnd = $helperHwnd
        confirm = $true
        actions = @(
            @{
                type = 'click'
                coordinateSpace = 'capture_pixels'
                point = $inputClickPoint
                captureReference = $inputCaptureReference
            })
    } -RequestName 'windows.input(click Smoke query input)'
    $rawInputClickRequest = $inputClickCall.RawRequest
    $inputClickResponse = [PSCustomObject]@{
        Raw = $inputClickCall.RawResponse
        Json = $inputClickCall.Json
    }
    $inputClickPayload = $inputClickCall.Payload
    Assert-InputLiveResult -ToolCall $inputClickCall -ExpectedHwnd $helperHwnd
    Assert-InputArtifact -ArtifactsDirectory ([string]$healthPayload.artifactsDirectory) -InputPayload $inputClickPayload -ExpectedHwnd $helperHwnd
    $inputRuntimeEvent = Assert-InputRuntimeEvent -ArtifactsDirectory ([string]$healthPayload.artifactsDirectory) -BaselineEventCount $inputRuntimeEventCountBefore -InputPayload $inputClickPayload
    Assert-Condition -Condition ((Get-AuditEventCount -ArtifactsDirectory ([string]$healthPayload.artifactsDirectory) -ToolName 'windows.activate_window' -EventName 'tool.invocation.started') -eq $activationStartedCountBeforeInput) -Message 'windows.input smoke observed unexpected public activate_window invocation during input call.'
    Assert-Condition -Condition ((Get-AuditEventCount -ArtifactsDirectory ([string]$healthPayload.artifactsDirectory) -ToolName 'windows.focus_window' -EventName 'tool.invocation.started') -eq $focusStartedCountBeforeInput) -Message 'windows.input smoke observed unexpected public focus_window invocation during input call.'

    $inputFocusWaitCall = Invoke-WaitToolCall -Process $process -Condition 'focus_is' -Selector @{ name = 'Smoke query input'; controlType = 'edit' } -TimeoutMs $waitTimeoutFocusMs -RequestName 'windows.wait(focus_is Smoke query input after input)'
    $rawInputFocusWaitRequest = $inputFocusWaitCall.RawRequest
    $inputFocusWaitResponse = [PSCustomObject]@{
        Raw = $inputFocusWaitCall.RawResponse
        Json = $inputFocusWaitCall.Json
    }
    $inputFocusWaitPayload = Assert-WaitSuccess -ToolCall $inputFocusWaitCall -Condition 'focus_is'
    Assert-Condition -Condition ([string]$inputFocusWaitPayload.matchedElement.controlType -eq 'edit') -Message 'post-input focus_is did not resolve the expected helper edit control.'
    Assert-Condition -Condition ([string]$inputFocusWaitPayload.matchedElement.name -eq 'Smoke query input') -Message 'post-input focus_is did not resolve Smoke query input.'

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

    $visualBaselineCapturedCount = Get-AuditEventCount -ArtifactsDirectory ([string]$healthPayload.artifactsDirectory) -ToolName 'windows.wait' -EventName 'wait.visual.baseline_captured'
    $visualWaitPendingRequest = Start-ToolCallRequest -Process $process -Name 'windows.wait' -Arguments @{
        condition = 'visual_changed'
        timeoutMs = $waitTimeoutVisualMs
    }
    $rawVisualWaitRequest = $visualWaitPendingRequest.RawRequest
    $visualBaselineCaptured = Wait-Until -TimeoutMilliseconds 5000 -Predicate {
        (Get-AuditEventCount -ArtifactsDirectory ([string]$healthPayload.artifactsDirectory) -ToolName 'windows.wait' -EventName 'wait.visual.baseline_captured') -gt $visualBaselineCapturedCount
    }
    Assert-Condition -Condition $visualBaselineCaptured -Message 'Smoke did not observe visual baseline capture before arming helper burst.'
    Send-HelperCommand -Hwnd $helperHwnd -Message $wmAppArmVisualHeartbeat -Description 'arm_visual_heartbeat'
    $visualWaitCall = Complete-ToolCallRequest -Process $process -PendingRequest $visualWaitPendingRequest -RequestName 'windows.wait(visual_changed)'
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

    Stop-SmokeOwnedHelperProcess -Process $helperProcess -ProcessId $helperProcessId
    $helperClosedEarly = $true
    $helperProcess = $null

    # Terminal unowned shell-open proof: run after all owned helper-based checks and keep the
    # probe folder as retained evidence unless smoke can prove that a new Explorer window hwnd
    # was materialized specifically for this probe and close that exact window safely.
    $openTargetFolderPath = New-SmokeUnownedProbeFolder -RunId $runId -FolderName 'open-target-folder'
    $openTargetFolderIdentity = Split-Path -Leaf $openTargetFolderPath
    $openTargetPreviewEventCountBefore = Get-AuditEventCount -ArtifactsDirectory ([string]$healthPayload.artifactsDirectory) -ToolName 'windows.open_target' -EventName 'open_target.preview.completed'
    $openTargetRuntimeEventCountBefore = Get-AuditEventCount -ArtifactsDirectory ([string]$healthPayload.artifactsDirectory) -ToolName 'windows.open_target' -EventName 'open_target.runtime.completed'
    $openTargetArtifactCountBefore = Get-OpenTargetArtifactCount -ArtifactsDirectory ([string]$healthPayload.artifactsDirectory)
    $openTargetBaselineWindows = Get-VisibleWindowsInventory -Process $process -RequestName 'windows.list_windows(open_target baseline)'
    $openTargetBaselineWindowHwnds = @($openTargetBaselineWindows.Payload.windows | ForEach-Object { [int64]$_.hwnd })

    $openTargetDryRunCall = Invoke-ToolCall -Process $process -Name 'windows.open_target' -Arguments @{
        targetKind = 'folder'
        target = $openTargetFolderPath
        dryRun = $true
        confirm = $false
    } -RequestName 'windows.open_target(dry_run folder)'
    $rawOpenTargetDryRunRequest = $openTargetDryRunCall.RawRequest
    $openTargetDryRunResponse = [PSCustomObject]@{
        Raw = $openTargetDryRunCall.RawResponse
        Json = $openTargetDryRunCall.Json
    }
    $openTargetDryRunPayload = $openTargetDryRunCall.Payload
    Assert-OpenTargetPreview `
        -ToolCall $openTargetDryRunCall `
        -ExpectedTargetKind 'folder' `
        -ExpectedTargetIdentity $openTargetFolderIdentity
    Assert-OpenTargetPreviewRuntimeEvent -ArtifactsDirectory ([string]$healthPayload.artifactsDirectory) -BaselineEventCount $openTargetPreviewEventCountBefore
    Assert-NoOpenTargetDryRunRuntimeEvent -ArtifactsDirectory ([string]$healthPayload.artifactsDirectory) -BaselineEventCount $openTargetRuntimeEventCountBefore
    Assert-Condition -Condition ((Get-OpenTargetArtifactCount -ArtifactsDirectory ([string]$healthPayload.artifactsDirectory)) -eq $openTargetArtifactCountBefore) -Message 'Dry-run open_target unexpectedly materialized a runtime artifact.'

    $openTargetLiveCall = Invoke-ToolCall -Process $process -Name 'windows.open_target' -Arguments @{
        targetKind = 'folder'
        target = $openTargetFolderPath
        dryRun = $false
        confirm = $true
    } -RequestName 'windows.open_target(live folder)'
    $rawOpenTargetLiveRequest = $openTargetLiveCall.RawRequest
    $openTargetLiveResponse = [PSCustomObject]@{
        Raw = $openTargetLiveCall.RawResponse
        Json = $openTargetLiveCall.Json
    }
    $openTargetLivePayload = $openTargetLiveCall.Payload
    Assert-OpenTargetLiveResult `
        -ToolCall $openTargetLiveCall `
        -ExpectedTargetKind 'folder' `
        -ExpectedTargetIdentity $openTargetFolderIdentity
    Assert-Condition -Condition ((Get-OpenTargetArtifactCount -ArtifactsDirectory ([string]$healthPayload.artifactsDirectory)) -eq ($openTargetArtifactCountBefore + 1)) -Message 'Live open_target did not materialize exactly one runtime artifact.'
    Assert-OpenTargetArtifact -ArtifactsDirectory ([string]$healthPayload.artifactsDirectory) -OpenTargetPayload $openTargetLivePayload
    $openTargetProbeDisposition = 'external_retained_evidence'
    $openTargetOwnedWindow = Resolve-SmokeOwnedOpenTargetWindow `
        -Process $process `
        -ExpectedTitle $openTargetFolderIdentity `
        -BaselineWindowHwnds $openTargetBaselineWindowHwnds
    if ($null -ne $openTargetOwnedWindow) {
        Close-SmokeOwnedWindow -Process $process -Hwnd ([int64]$openTargetOwnedWindow.Hwnd)
        $openTargetProbeDisposition = 'owned_window_closed'
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
    Assert-OpenTargetRuntimeEvent -ArtifactsDirectory ([string]$healthPayload.artifactsDirectory) -OpenTargetPayload $openTargetLivePayload
    Assert-LaunchRuntimeEvent -ArtifactsDirectory ([string]$healthPayload.artifactsDirectory) -LaunchPayload $launchLivePayload

    $freshProcess = Start-StagedMcpHost -ServerDll $serverDll -WorkingDirectory $repoRoot
    try {
        $freshInitializeCall = Invoke-McpRequest -Process $freshProcess -Method 'initialize' -Params @{
            protocolVersion = '2025-06-18'
            capabilities = @{}
            clientInfo = @{
                name = 'Okno.Smoke.FreshHost'
                version = '0.1.0'
            }
        } -RequestName 'fresh initialize'
        [void](Send-Json -Process $freshProcess -Payload @{
            jsonrpc = '2.0'
            method = 'notifications/initialized'
        })

        $freshListCall = Invoke-McpRequest -Process $freshProcess -Method 'tools/list' -Params @{} -RequestName 'fresh tools/list'
        $freshToolNames = @($freshListCall.Json.result.tools | ForEach-Object { [string]$_.name })
        Assert-Condition -Condition ($freshToolNames -contains 'windows.input') -Message 'Fresh host tools/list does not advertise windows.input.'
        $freshInputTool = @($freshListCall.Json.result.tools | Where-Object { [string]$_.name -eq 'windows.input' })
        Assert-Condition -Condition ($freshInputTool.Count -eq 1) -Message "Fresh host tools/list returned $($freshInputTool.Count) windows.input descriptors."
        $freshInputActionProperties = $freshInputTool[0].inputSchema.properties.actions.items.properties
        Assert-Condition -Condition ($null -ne $freshInputActionProperties.type) -Message 'Fresh host windows.input schema is missing action type.'
        Assert-Condition -Condition ($null -ne $freshInputActionProperties.point) -Message 'Fresh host windows.input schema is missing action point.'
        Assert-Condition -Condition ($null -ne $freshInputActionProperties.captureReference.properties.frameBounds) -Message 'Fresh host windows.input schema is missing captureReference.frameBounds.'
        Assert-Condition -Condition ($null -ne $freshInputActionProperties.captureReference.properties.targetIdentity) -Message 'Fresh host windows.input schema is missing captureReference.targetIdentity.'
        Assert-Condition -Condition ($null -eq $freshInputActionProperties.text) -Message 'Fresh host windows.input schema unexpectedly exposes text.'
        Assert-Condition -Condition ($null -eq $freshInputActionProperties.key) -Message 'Fresh host windows.input schema unexpectedly exposes key.'
        Assert-Condition -Condition ($null -eq $freshInputActionProperties.keys) -Message 'Fresh host windows.input schema unexpectedly exposes keys.'

        $freshHealthCall = Invoke-ToolCall -Process $freshProcess -Name 'okno.health' -Arguments @{} -RequestName 'fresh okno.health'
        $freshHealthPayload = $freshHealthCall.Payload
        Assert-HealthPayload -ToolCall $freshHealthCall -Payload $freshHealthPayload -Manifest $manifest

        $freshContractCall = Invoke-ToolCall -Process $freshProcess -Name 'okno.contract' -Arguments @{} -RequestName 'fresh okno.contract'
        $freshContractPayload = $freshContractCall.Payload
        $freshImplementedInputDescriptors = @($freshContractPayload.implementedTools | Where-Object { [string]$_.name -eq 'windows.input' })
        Assert-Condition -Condition ($freshImplementedInputDescriptors.Count -eq 1) -Message "Fresh okno.contract returned $($freshImplementedInputDescriptors.Count) implemented windows.input descriptors."
        $freshInputContract = $freshImplementedInputDescriptors[0]
        Assert-Condition -Condition ([string]$freshInputContract.lifecycle -eq 'implemented') -Message 'Fresh okno.contract did not publish windows.input as implemented.'
        Assert-Condition -Condition ([string]$freshInputContract.safety_class -eq 'os_side_effect') -Message 'Fresh okno.contract windows.input safety_class drifted.'
        Assert-Condition -Condition ([string]$freshInputContract.execution_policy.policy_group -eq 'input') -Message 'Fresh okno.contract windows.input policy_group drifted.'
        Assert-Condition -Condition ([string]$freshInputContract.execution_policy.risk_level -eq 'destructive') -Message 'Fresh okno.contract windows.input risk_level drifted.'
        Assert-Condition -Condition ([string]$freshInputContract.execution_policy.guard_capability -eq 'input') -Message 'Fresh okno.contract windows.input guard_capability drifted.'
        Assert-Condition -Condition (-not [bool]$freshInputContract.execution_policy.supports_dry_run) -Message 'Fresh okno.contract windows.input supports_dry_run drifted.'
        Assert-Condition -Condition ([string]$freshInputContract.execution_policy.confirmation_mode -eq 'required') -Message 'Fresh okno.contract windows.input confirmation_mode drifted.'
        Assert-Condition -Condition ([string]$freshInputContract.execution_policy.redaction_class -eq 'text_payload') -Message 'Fresh okno.contract windows.input redaction_class drifted.'
        Assert-Condition -Condition (@($freshContractPayload.deferredTools | Where-Object { [string]$_.name -eq 'windows.input' }).Count -eq 0) -Message 'Fresh okno.contract still publishes windows.input as deferred.'

        $freshInputRuntimeEventCountBefore = Get-AuditEventCount -ArtifactsDirectory ([string]$freshHealthPayload.artifactsDirectory) -ToolName 'windows.input' -EventName 'input.runtime.completed'
        $freshInputNegativeCall = Invoke-ToolCall -Process $freshProcess -Name 'windows.input' -Arguments @{
            confirm = $true
            actions = @(
                @{
                    type = 'click'
                    coordinateSpace = 'screen'
                    point = @{
                        x = 1
                        y = 1
                    }
                })
        } -RequestName 'fresh windows.input(missing target)'
        $freshInputNegativePayload = $freshInputNegativeCall.Payload
        Assert-Condition -Condition ([bool]$freshInputNegativeCall.Json.result.isError) -Message 'Fresh missing-target windows.input must return isError=true.'
        Assert-Condition -Condition ([string]$freshInputNegativePayload.status -eq 'failed') -Message "Fresh missing-target windows.input returned status '$($freshInputNegativePayload.status)'."
        Assert-Condition -Condition ([string]$freshInputNegativePayload.failureCode -eq 'missing_target') -Message "Fresh missing-target windows.input returned failureCode '$($freshInputNegativePayload.failureCode)'."
        Assert-Condition -Condition ($null -eq $freshInputNegativePayload.artifactPath) -Message 'Fresh missing-target windows.input unexpectedly materialized artifactPath.'
        Assert-Condition -Condition ((Get-AuditEventCount -ArtifactsDirectory ([string]$freshHealthPayload.artifactsDirectory) -ToolName 'windows.input' -EventName 'input.runtime.completed') -eq $freshInputRuntimeEventCountBefore) -Message 'Fresh missing-target windows.input unexpectedly materialized input.runtime.completed.'

        Stop-StagedMcpHost -Process $freshProcess
        $freshStderr = $freshProcess.StandardError.ReadToEnd()
        $freshHostAcceptance = [ordered]@{
            initialize = $freshInitializeCall.Json.result
            declared_tools = $freshToolNames
            health = $freshHealthPayload
            contract_input = $freshInputContract
            input_missing_target = $freshInputNegativePayload
            input_runtime_event = 'not_materialized_for_pre_gate_missing_target'
            stderr = $freshStderr
            raw_requests = [ordered]@{
                initialize = $freshInitializeCall.RawRequest
                list_tools = $freshListCall.RawRequest
                health = $freshHealthCall.RawRequest
                contract = $freshContractCall.RawRequest
                input_missing_target = $freshInputNegativeCall.RawRequest
            }
            raw_responses = [ordered]@{
                initialize = $freshInitializeCall.RawResponse
                list_tools = $freshListCall.RawResponse
                health = $freshHealthCall.RawResponse
                contract = $freshContractCall.RawResponse
                input_missing_target = $freshInputNegativeCall.RawResponse
            }
        }
        $freshProcess = $null
    }
    finally {
        if ($null -ne $freshProcess) {
            Stop-StagedMcpHost -Process $freshProcess
            $freshProcess = $null
        }
    }

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
        launch_process = [ordered]@{
            dry_run = $launchDryRunPayload
            dry_run_preview_event = 'verified'
            dry_run_runtime_event = 'not_materialized'
            dry_run_side_effects = 'no_residual_process_or_window_effects'
            live = $launchLivePayload
            runtime_event_validation = 'verified'
            artifact_validation = 'verified'
            helper_lifetime_ms = $helperLifetimeMs
            helper_visual_burst_ms = $helperVisualBurstMs
            helper_closed_early = $helperClosedEarly
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
        helper_activate = [ordered]@{
            final = $activatePayload
            attempts = $activateAttempts
        }
        helper_capture = $helperCapturePayload
        wait_active_window_matches = $activeWaitPayload
        wait_focus_is = $focusWaitPayload
        input_click_first = [ordered]@{
            planning_uia_snapshot = $inputPlanningUiaSnapshotPayload
            click_point = $inputClickPoint
            live = $inputClickPayload
            runtime_event_validation = 'verified'
            runtime_event = $inputRuntimeEvent
            artifact_validation = 'verified'
            hidden_public_focus_activation_validation = 'verified_absent'
            post_focus_is = $inputFocusWaitPayload
        }
        wait_element_exists = $elementWaitPayload
        wait_transient_precondition = $transientExistsWaitPayload
        wait_element_gone = $elementGoneWaitPayload
        wait_text_appears = $textWaitPayload
        wait_visual_changed = $visualWaitPayload
        open_target = [ordered]@{
            dry_run = $openTargetDryRunPayload
            dry_run_preview_event = 'verified'
            dry_run_runtime_event = 'not_materialized'
            dry_run_artifact = 'not_materialized'
            live = $openTargetLivePayload
            runtime_event_validation = 'verified'
            artifact_validation = 'verified'
            probe_target_disposition = $openTargetProbeDisposition
        }
        fresh_host_acceptance = $freshHostAcceptance
        timings = [ordered]@{
            total_duration_ms = $smokeStopwatch.ElapsedMilliseconds
        }
        raw_requests = [ordered]@{
            initialize = $rawInitializeRequest
            list_tools = $rawListRequest
            health = $rawHealthRequest
            launch_process_dry_run = $rawLaunchDryRunRequest
            launch_process_live = $rawLaunchLiveRequest
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
            input_planning_uia_snapshot = $rawInputPlanningUiaSnapshotRequest
            windows_input_click = $rawInputClickRequest
            wait_input_focus_is = $rawInputFocusWaitRequest
            wait_element_exists = $rawElementWaitRequest
            wait_transient_precondition = $rawTransientExistsWaitRequest
            wait_element_gone = $rawElementGoneWaitRequest
            wait_text_appears = $rawTextWaitRequest
            wait_visual_changed = $rawVisualWaitRequest
            open_target_dry_run = $rawOpenTargetDryRunRequest
            open_target_live = $rawOpenTargetLiveRequest
        }
        raw_responses = [ordered]@{
            initialize = $initializeResponse.Raw
            list_tools = $listResponse.Raw
            health = $healthResponse.Raw
            launch_process_dry_run = $launchDryRunResponse.Raw
            launch_process_live = $launchLiveResponse.Raw
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
            input_planning_uia_snapshot = $inputPlanningUiaSnapshotResponse.Raw
            windows_input_click = $inputClickResponse.Raw
            wait_input_focus_is = $inputFocusWaitResponse.Raw
            wait_element_exists = $elementWaitResponse.Raw
            wait_transient_precondition = $transientExistsWaitResponse.Raw
            wait_element_gone = $elementGoneWaitResponse.Raw
            wait_text_appears = $textWaitResponse.Raw
            wait_visual_changed = $visualWaitResponse.Raw
            open_target_dry_run = $openTargetDryRunResponse.Raw
            open_target_live = $openTargetLiveResponse.Raw
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
        "- launch_dry_run_status: $($launchDryRunPayload.status)",
        "- launch_dry_run_preview: $($launchDryRunPayload.preview.executableIdentity) args=$($launchDryRunPayload.preview.argumentCount) waitForWindow=$($launchDryRunPayload.preview.waitForWindow)",
        "- launch_dry_run_preview_event: verified",
        "- launch_dry_run_runtime_event: not_materialized",
        "- launch_dry_run_side_effects: no_residual_process_or_window_effects",
        "- launch_live_status: $($launchLivePayload.status)",
        "- launch_live_result_mode: $($launchLivePayload.resultMode)",
        "- launch_live_artifact: $($launchLivePayload.artifactPath)",
        "- launch_runtime_event: verified",
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
        "- input_click_status: $($inputClickPayload.status)",
        "- input_click_result_mode: $($inputClickPayload.resultMode)",
        "- input_click_point_capture_pixels: x=$($inputClickPoint.x) y=$($inputClickPoint.y)",
        "- input_click_artifact: $($inputClickPayload.artifactPath)",
        "- input_runtime_event: verified",
        "- input_hidden_public_focus_activation: verified_absent",
        "- input_focus_is_artifact: $($inputFocusWaitPayload.artifactPath)",
        "- wait_element_exists_artifact: $($elementWaitPayload.artifactPath)",
        "- wait_transient_precondition_artifact: $($transientExistsWaitPayload.artifactPath)",
        "- wait_element_gone_artifact: $($elementGoneWaitPayload.artifactPath)",
        "- wait_text_appears_artifact: $($textWaitPayload.artifactPath)",
        "- wait_visual_changed_artifact: $($visualWaitPayload.artifactPath)",
        "- open_target_dry_run_status: $($openTargetDryRunPayload.status)",
        "- open_target_dry_run_preview: $($openTargetDryRunPayload.preview.targetKind) $($openTargetDryRunPayload.preview.targetIdentity)",
        "- open_target_dry_run_preview_event: verified",
        "- open_target_dry_run_runtime_event: not_materialized",
        "- open_target_dry_run_artifact: not_materialized",
        "- open_target_live_status: $($openTargetLivePayload.status)",
        "- open_target_live_result_mode: $($openTargetLivePayload.resultMode)",
        "- open_target_live_artifact: $($openTargetLivePayload.artifactPath)",
        "- open_target_runtime_event: verified",
        "- open_target_probe_target: external_retained_evidence",
        "- fresh_host_windows_input_tools_list: verified",
        "- fresh_host_windows_input_contract: verified",
        "- fresh_host_windows_input_missing_target: $($freshHostAcceptance.input_missing_target.status)/$($freshHostAcceptance.input_missing_target.failureCode)",
        "- helper_closed_early: $helperClosedEarly",
        "- smoke_duration_ms: $($smokeStopwatch.ElapsedMilliseconds)",
        "- audit_dir: $($healthPayload.artifactsDirectory)",
        "- report: $reportPath"
    )

    Write-SmokeArtifacts -Report $report -SummaryLines $summaryLines

    Get-Content $summaryPath
}
finally {
    $smokeStopwatch.Stop()

    if ($null -ne $helperProcess -and -not $helperProcess.HasExited) {
        $helperProcess.Kill()
        $helperProcess.WaitForExit()
    }
    elseif ($null -ne $helperProcessId) {
        try {
            $helperProcessFallback = [System.Diagnostics.Process]::GetProcessById([int]$helperProcessId)
            if (-not $helperProcessFallback.HasExited) {
                $helperProcessFallback.Kill()
                $helperProcessFallback.WaitForExit()
            }
        }
        catch [System.ArgumentException] {
        }
    }

    if ($null -ne $liveReconciliation) {
        $liveResolvedReconciliation = Resolve-SmokeHelperCleanupProcessIds -ReconciliationState $liveReconciliation -TitleResolutionTimeoutMs $helperWindowMaterializationTimeoutMs
        if ($liveResolvedReconciliation.CleanupProcessIds.Count -gt 0) {
            Stop-SmokeHelperLeaks -ProcessIds $liveResolvedReconciliation.CleanupProcessIds
        }
    }
    elseif ($null -ne $helperProcessIdsBeforeLive) {
        $liveFallbackReconciliation = Get-SmokeHelperBaseReconciliationState `
            -HelperProcessName $helperProcessName `
            -ExpectedOwnershipMarker $helperOwnershipMarker `
            -ExpectedExecutablePath $helperExe `
            -ExpectedTitle $helperTitle `
            -BaselineProcessIds $helperProcessIdsBeforeLive
        $liveResolvedReconciliation = Resolve-SmokeHelperCleanupProcessIds -ReconciliationState $liveFallbackReconciliation -TitleResolutionTimeoutMs $helperWindowMaterializationTimeoutMs
        if ($liveResolvedReconciliation.CleanupProcessIds.Count -gt 0) {
            Stop-SmokeHelperLeaks -ProcessIds $liveResolvedReconciliation.CleanupProcessIds
        }
    }

    if ($null -ne $freshProcess -and -not $freshProcess.HasExited) {
        Stop-StagedMcpHost -Process $freshProcess
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
