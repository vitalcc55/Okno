using WinBridge.Runtime;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Guards;
using WinBridge.Runtime.Session;
using WinBridge.Runtime.Tooling;
using WinBridge.Runtime.Windows.Display;
using WinBridge.Server.Tools;

namespace WinBridge.Server.IntegrationTests;

public sealed class AdminToolTests
{
    [Fact]
    public void HealthReturnsProbeBackedReadinessSnapshot()
    {
        string root = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        AuditLogOptions options = new(
            ContentRootPath: root,
            EnvironmentName: "Tests",
            RunId: "admin-tool-tests",
            DiagnosticsRoot: Path.Combine(root, "artifacts", "diagnostics"),
            RunDirectory: Path.Combine(root, "artifacts", "diagnostics", "admin-tool-tests"),
            EventsPath: Path.Combine(root, "artifacts", "diagnostics", "admin-tool-tests", "events.jsonl"),
            SummaryPath: Path.Combine(root, "artifacts", "diagnostics", "admin-tool-tests", "summary.md"));

        AuditLog auditLog = new(options, TimeProvider.System);
        RuntimeInfo runtimeInfo = new(options);
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("admin-tool-tests"));
        RuntimeGuardAssessment assessment = CreateAssessment(new FakeMonitorManager());
        AdminTools tools = new(auditLog, runtimeInfo, sessionManager, new FakeRuntimeGuardService(assessment));

        HealthResult result = tools.Health();

        Assert.Equal("Okno", result.Service);
        Assert.NotEqual(default, result.Readiness.CapturedAtUtc);
        Assert.Equal(
            [
                ReadinessDomainValues.DesktopSession,
                ReadinessDomainValues.SessionAlignment,
                ReadinessDomainValues.Integrity,
                ReadinessDomainValues.UiAccess,
            ],
            result.Readiness.Domains.Select(item => item.Domain).ToArray());
        Assert.Equal(GuardStatusValues.Ready, Assert.Single(result.Readiness.Domains, item => item.Domain == ReadinessDomainValues.DesktopSession).Status);
        Assert.Equal(GuardStatusValues.Ready, Assert.Single(result.Readiness.Domains, item => item.Domain == ReadinessDomainValues.SessionAlignment).Status);
        Assert.Equal(GuardStatusValues.Degraded, Assert.Single(result.Readiness.Domains, item => item.Domain == ReadinessDomainValues.Integrity).Status);
        Assert.Equal(GuardStatusValues.Blocked, Assert.Single(result.Readiness.Domains, item => item.Domain == ReadinessDomainValues.UiAccess).Status);

        Assert.Equal(
            [
                CapabilitySummaryValues.Capture,
                CapabilitySummaryValues.Uia,
                CapabilitySummaryValues.Wait,
                CapabilitySummaryValues.Input,
                CapabilitySummaryValues.Clipboard,
                CapabilitySummaryValues.Launch,
            ],
            result.Readiness.Capabilities.Select(item => item.Capability).ToArray());
        Assert.Equal(
            GuardStatusValues.Ready,
            Assert.Single(result.Readiness.Capabilities, item => item.Capability == CapabilitySummaryValues.Capture).Status);
        Assert.Equal(
            GuardStatusValues.Degraded,
            Assert.Single(result.Readiness.Capabilities, item => item.Capability == CapabilitySummaryValues.Uia).Status);
        Assert.Equal(
            GuardStatusValues.Degraded,
            Assert.Single(result.Readiness.Capabilities, item => item.Capability == CapabilitySummaryValues.Wait).Status);
        Assert.Equal(
            GuardStatusValues.Degraded,
            Assert.Single(result.Readiness.Capabilities, item => item.Capability == CapabilitySummaryValues.Input).Status);
        Assert.Equal(
            GuardStatusValues.Blocked,
            Assert.Single(result.Readiness.Capabilities, item => item.Capability == CapabilitySummaryValues.Clipboard).Status);
        Assert.Equal(
            GuardStatusValues.Degraded,
            Assert.Single(result.Readiness.Capabilities, item => item.Capability == CapabilitySummaryValues.Launch).Status);

        Assert.Equal(
            [
                CapabilitySummaryValues.Clipboard,
            ],
            result.BlockedCapabilities.Select(item => item.Capability).ToArray());

        Assert.Equal(
            [
                GuardReasonCodeValues.IntegrityRequiresEqualOrLowerTarget,
                GuardReasonCodeValues.UiaWorkerLaunchabilityUnverified,
                GuardReasonCodeValues.WaitShellVisualAvailable,
                GuardReasonCodeValues.InputUipiBarrierPresent,
                GuardReasonCodeValues.LaunchElevationBoundaryUnconfirmed,
            ],
            result.Warnings.Select(item => item.Code).ToArray());
        Assert.Equal(
            [
                ReadinessDomainValues.Integrity,
                CapabilitySummaryValues.Uia,
                CapabilitySummaryValues.Wait,
                CapabilitySummaryValues.Input,
                CapabilitySummaryValues.Launch,
            ],
            result.Warnings.Select(item => item.Source).ToArray());
        Assert.Contains(ToolNames.WindowsLaunchProcess, result.ImplementedTools);
        Assert.Contains(ToolNames.WindowsOpenTarget, result.ImplementedTools);
        Assert.Contains(ToolNames.WindowsInput, result.ImplementedTools);
        Assert.False(result.DeferredTools.ContainsKey(ToolNames.WindowsLaunchProcess));
        Assert.False(result.DeferredTools.ContainsKey(ToolNames.WindowsOpenTarget));
        Assert.False(result.DeferredTools.ContainsKey(ToolNames.WindowsInput));
    }

    [Fact]
    public void ContractUsesCanonicalSnakeCaseLiterals()
    {
        string root = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        AuditLogOptions options = new(
            ContentRootPath: root,
            EnvironmentName: "Tests",
            RunId: "admin-tool-tests",
            DiagnosticsRoot: Path.Combine(root, "artifacts", "diagnostics"),
            RunDirectory: Path.Combine(root, "artifacts", "diagnostics", "admin-tool-tests"),
            EventsPath: Path.Combine(root, "artifacts", "diagnostics", "admin-tool-tests", "events.jsonl"),
            SummaryPath: Path.Combine(root, "artifacts", "diagnostics", "admin-tool-tests", "summary.md"));

        AuditLog auditLog = new(options, TimeProvider.System);
        RuntimeInfo runtimeInfo = new(options);
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("admin-tool-tests"));
        RuntimeGuardAssessment assessment = CreateAssessment(new FakeMonitorManager());
        AdminTools tools = new(auditLog, runtimeInfo, sessionManager, new FakeRuntimeGuardService(assessment));

        ContractSummaryResult result = tools.Contract();

        ContractToolDescriptor attachDescriptor = Assert.Single(
            result.ImplementedTools,
            descriptor => descriptor.Name == ToolNames.WindowsAttachWindow);
        Assert.Equal("implemented", attachDescriptor.Lifecycle);
        Assert.Equal("session_mutation", attachDescriptor.SafetyClass);

        ContractToolDescriptor waitDescriptor = Assert.Single(
            result.ImplementedTools,
            descriptor => descriptor.Name == ToolNames.WindowsWait);
        Assert.Equal("implemented", waitDescriptor.Lifecycle);
        Assert.Equal("os_side_effect", waitDescriptor.SafetyClass);

        ContractToolDescriptor launchDescriptor = Assert.Single(
            result.ImplementedTools,
            descriptor => descriptor.Name == ToolNames.WindowsLaunchProcess);
        ContractToolExecutionPolicyDescriptor launchPolicy = Assert.IsType<ContractToolExecutionPolicyDescriptor>(launchDescriptor.ExecutionPolicy);
        Assert.Equal("launch", launchPolicy.PolicyGroup);
        Assert.Equal("high", launchPolicy.RiskLevel);
        Assert.Equal("launch", launchPolicy.GuardCapability);
        Assert.True(launchPolicy.SupportsDryRun);
        Assert.Equal("required", launchPolicy.ConfirmationMode);
        Assert.Equal("launch_payload", launchPolicy.RedactionClass);

        ContractToolDescriptor openTargetDescriptor = Assert.Single(
            result.ImplementedTools,
            descriptor => descriptor.Name == ToolNames.WindowsOpenTarget);
        ContractToolExecutionPolicyDescriptor openTargetPolicy = Assert.IsType<ContractToolExecutionPolicyDescriptor>(openTargetDescriptor.ExecutionPolicy);
        Assert.Equal("launch", openTargetPolicy.PolicyGroup);
        Assert.Equal("medium", openTargetPolicy.RiskLevel);
        Assert.Equal("launch", openTargetPolicy.GuardCapability);
        Assert.True(openTargetPolicy.SupportsDryRun);
        Assert.Equal("required", openTargetPolicy.ConfirmationMode);
        Assert.Equal("launch_payload", openTargetPolicy.RedactionClass);

        ContractToolDescriptor inputDescriptor = Assert.Single(
            result.ImplementedTools,
            descriptor => descriptor.Name == ToolNames.WindowsInput);
        ContractToolExecutionPolicyDescriptor inputPolicy = Assert.IsType<ContractToolExecutionPolicyDescriptor>(inputDescriptor.ExecutionPolicy);
        Assert.Equal("input", inputPolicy.PolicyGroup);
        Assert.Equal("destructive", inputPolicy.RiskLevel);
        Assert.Equal("input", inputPolicy.GuardCapability);
        Assert.False(inputPolicy.SupportsDryRun);
        Assert.Equal("required", inputPolicy.ConfirmationMode);
        Assert.Equal("text_payload", inputPolicy.RedactionClass);
        Assert.Null(inputDescriptor.PlannedPhase);
        Assert.Null(inputDescriptor.SuggestedAlternative);
        Assert.DoesNotContain(result.DeferredTools, descriptor => descriptor.Name == ToolNames.WindowsLaunchProcess);
        Assert.DoesNotContain(result.DeferredTools, descriptor => descriptor.Name == ToolNames.WindowsOpenTarget);
        Assert.DoesNotContain(result.DeferredTools, descriptor => descriptor.Name == ToolNames.WindowsInput);
        Assert.Contains("artifacts/events/materializer уже закрыты Package D", result.Notes, StringComparison.Ordinal);
        Assert.Contains("smoke/fresh-host acceptance", result.Notes, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("artifacts/events/materializer rollout остаются отдельным follow-up", result.Notes, StringComparison.Ordinal);
    }

    private static RuntimeGuardAssessment CreateAssessment(FakeMonitorManager monitorManager)
    {
        DisplayTopologySnapshot topology = monitorManager.GetTopologySnapshot();
        RuntimeReadinessSnapshot readiness = new(
            CapturedAtUtc: DateTimeOffset.UtcNow,
            Domains:
            [
                new(
                    Domain: ReadinessDomainValues.DesktopSession,
                    Status: GuardStatusValues.Ready,
                    Reasons:
                    [
                        new(
                            Code: GuardReasonCodeValues.InputDesktopAvailable,
                            Severity: GuardSeverityValues.Info,
                            MessageHuman: "Runtime успешно открыл input desktop текущей interactive session.",
                            Source: ReadinessDomainValues.DesktopSession)
                    ]),
                new(
                    Domain: ReadinessDomainValues.SessionAlignment,
                    Status: GuardStatusValues.Ready,
                    Reasons:
                    [
                        new(
                            Code: GuardReasonCodeValues.SessionAlignedWithActiveConsole,
                            Severity: GuardSeverityValues.Info,
                            MessageHuman: "Session текущего процесса совпадает с active console session.",
                            Source: ReadinessDomainValues.SessionAlignment)
                    ]),
                new(
                    Domain: ReadinessDomainValues.Integrity,
                    Status: GuardStatusValues.Degraded,
                    Reasons:
                    [
                        new(
                            Code: GuardReasonCodeValues.IntegrityRequiresEqualOrLowerTarget,
                            Severity: GuardSeverityValues.Warning,
                            MessageHuman: "Текущий token имеет medium integrity; interaction с higher-integrity target нельзя обещать по умолчанию.",
                            Source: ReadinessDomainValues.Integrity)
                    ]),
                new(
                    Domain: ReadinessDomainValues.UiAccess,
                    Status: GuardStatusValues.Blocked,
                    Reasons:
                    [
                        new(
                            Code: GuardReasonCodeValues.UiAccessMissing,
                            Severity: GuardSeverityValues.Blocked,
                            MessageHuman: "В текущем token отсутствует uiAccess; bypass обычного UIPI barrier нельзя считать доступным.",
                            Source: ReadinessDomainValues.UiAccess)
                    ]),
            ],
            Capabilities:
            [
                new(
                    Capability: CapabilitySummaryValues.Capture,
                    Status: GuardStatusValues.Ready,
                    Reasons:
                    [
                        new(
                            Code: GuardReasonCodeValues.CaptureReady,
                            Severity: GuardSeverityValues.Info,
                            MessageHuman: "Runtime может честно обещать current shipped capture semantics: strong display identity и Windows Graphics Capture доступны.",
                            Source: CapabilitySummaryValues.Capture)
                    ]),
                new(
                    Capability: CapabilitySummaryValues.Uia,
                    Status: GuardStatusValues.Degraded,
                    Reasons:
                    [
                        new(
                            Code: GuardReasonCodeValues.UiaWorkerLaunchabilityUnverified,
                            Severity: GuardSeverityValues.Warning,
                            MessageHuman: "Worker launch spec resolved, но runtime startability UIA boundary не подтверждена в reporting-first health path.",
                            Source: CapabilitySummaryValues.Uia),
                        new(
                            Code: GuardReasonCodeValues.UiaObserveScopeLimited,
                            Severity: GuardSeverityValues.Info,
                            MessageHuman: "Current UIA semantics ограничены window-scoped ElementFromHandle/control-view path и не обещают cross-user Run as reachability.",
                            Source: CapabilitySummaryValues.Uia)
                    ]),
                new(
                    Capability: CapabilitySummaryValues.Wait,
                    Status: GuardStatusValues.Degraded,
                    Reasons:
                    [
                        new(
                            Code: GuardReasonCodeValues.WaitShellVisualAvailable,
                            Severity: GuardSeverityValues.Warning,
                            MessageHuman: "windows.wait может честно обещать active_window_matches и visual_changed.",
                            Source: CapabilitySummaryValues.Wait),
                        new(
                            Code: GuardReasonCodeValues.WaitUiaBranchLaunchabilityUnverified,
                            Severity: GuardSeverityValues.Info,
                            MessageHuman: "UIA worker boundary только configured: launch spec resolved, но startability не подтверждена, поэтому UIA-based wait conditions не advertised как usable subset.",
                            Source: CapabilitySummaryValues.Wait)
                    ]),
                new(
                    Capability: CapabilitySummaryValues.Input,
                    Status: GuardStatusValues.Degraded,
                    Reasons:
                    [
                        new(
                            Code: GuardReasonCodeValues.InputUipiBarrierPresent,
                            Severity: GuardSeverityValues.Warning,
                            MessageHuman: "Общий input baseline допускает только equal-or-lower target path: medium integrity без uiAccess не подтверждает safe interaction с higher-integrity или protected UI targets.",
                            Source: CapabilitySummaryValues.Input)
                    ]),
                new(
                    Capability: CapabilitySummaryValues.Clipboard,
                    Status: GuardStatusValues.Blocked,
                    Reasons:
                    [
                        new(
                            Code: GuardReasonCodeValues.CapabilityNotImplemented,
                            Severity: GuardSeverityValues.Blocked,
                            MessageHuman: "Эта capability пока не реализована в текущем runtime surface и не может считаться готовой.",
                            Source: CapabilitySummaryValues.Clipboard),
                        new(
                            Code: GuardReasonCodeValues.ClipboardIntegrityLimited,
                            Severity: GuardSeverityValues.Blocked,
                            MessageHuman: "Clipboard path пока не должен обещать операции при неполном integrity profile. Текущий token имеет medium integrity; interaction с higher-integrity target нельзя обещать по умолчанию.",
                            Source: CapabilitySummaryValues.Clipboard)
                    ]),
                new(
                    Capability: CapabilitySummaryValues.Launch,
                    Status: GuardStatusValues.Degraded,
                    Reasons:
                    [
                        new(
                            Code: GuardReasonCodeValues.LaunchElevationBoundaryUnconfirmed,
                            Severity: GuardSeverityValues.Warning,
                            MessageHuman: "Live launch path остаётся confirmation-worthy: higher-integrity boundary заранее не подтверждена. Текущий token имеет medium integrity; interaction с higher-integrity target нельзя обещать по умолчанию.",
                            Source: CapabilitySummaryValues.Launch)
                    ]),
            ]);

        return new RuntimeGuardAssessment(
            Topology: topology,
            Readiness: readiness,
            BlockedCapabilities:
            [
                .. readiness.Capabilities.Where(item => item.Status == GuardStatusValues.Blocked),
            ],
            Warnings:
            [
                .. readiness.Domains.SelectMany(item => item.Reasons).Where(reason => reason.Severity == GuardSeverityValues.Warning),
                .. readiness.Capabilities
                    .Where(item => item.Status != GuardStatusValues.Blocked)
                    .SelectMany(item => item.Reasons)
                    .Where(reason => reason.Severity == GuardSeverityValues.Warning),
            ]);
    }

    private sealed class FakeRuntimeGuardService(RuntimeGuardAssessment assessment) : IRuntimeGuardService
    {
        public RuntimeGuardAssessment GetSnapshot() => assessment;
    }
}
