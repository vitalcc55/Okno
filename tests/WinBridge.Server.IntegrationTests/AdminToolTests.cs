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
            GuardStatusValues.Unknown,
            Assert.Single(result.Readiness.Capabilities, item => item.Capability == CapabilitySummaryValues.Capture).Status);
        Assert.Equal(
            GuardStatusValues.Unknown,
            Assert.Single(result.Readiness.Capabilities, item => item.Capability == CapabilitySummaryValues.Uia).Status);
        Assert.Equal(
            GuardStatusValues.Unknown,
            Assert.Single(result.Readiness.Capabilities, item => item.Capability == CapabilitySummaryValues.Wait).Status);
        Assert.Equal(
            GuardStatusValues.Blocked,
            Assert.Single(result.Readiness.Capabilities, item => item.Capability == CapabilitySummaryValues.Input).Status);
        Assert.Equal(
            GuardStatusValues.Blocked,
            Assert.Single(result.Readiness.Capabilities, item => item.Capability == CapabilitySummaryValues.Clipboard).Status);
        Assert.Equal(
            GuardStatusValues.Blocked,
            Assert.Single(result.Readiness.Capabilities, item => item.Capability == CapabilitySummaryValues.Launch).Status);

        Assert.Equal(
            [
                CapabilitySummaryValues.Input,
                CapabilitySummaryValues.Clipboard,
                CapabilitySummaryValues.Launch,
            ],
            result.BlockedCapabilities.Select(item => item.Capability).ToArray());

        GuardReason warning = Assert.Single(result.Warnings);
        Assert.Equal(GuardReasonCodeValues.IntegrityRequiresEqualOrLowerTarget, warning.Code);
        Assert.Equal(GuardSeverityValues.Warning, warning.Severity);
        Assert.Equal(ReadinessDomainValues.Integrity, warning.Source);
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
                    Status: GuardStatusValues.Unknown,
                    Reasons:
                    [
                        new(
                            Code: GuardReasonCodeValues.AssessmentNotImplemented,
                            Severity: GuardSeverityValues.Warning,
                            MessageHuman: "Probe-backed capability derivation для этого capability будет добавлена в Package C; статус остаётся консервативно unknown.",
                            Source: CapabilitySummaryValues.Capture)
                    ]),
                new(
                    Capability: CapabilitySummaryValues.Uia,
                    Status: GuardStatusValues.Unknown,
                    Reasons:
                    [
                        new(
                            Code: GuardReasonCodeValues.AssessmentNotImplemented,
                            Severity: GuardSeverityValues.Warning,
                            MessageHuman: "Probe-backed capability derivation для этого capability будет добавлена в Package C; статус остаётся консервативно unknown.",
                            Source: CapabilitySummaryValues.Uia)
                    ]),
                new(
                    Capability: CapabilitySummaryValues.Wait,
                    Status: GuardStatusValues.Unknown,
                    Reasons:
                    [
                        new(
                            Code: GuardReasonCodeValues.AssessmentNotImplemented,
                            Severity: GuardSeverityValues.Warning,
                            MessageHuman: "Probe-backed capability derivation для этого capability будет добавлена в Package C; статус остаётся консервативно unknown.",
                            Source: CapabilitySummaryValues.Wait)
                    ]),
                new(
                    Capability: CapabilitySummaryValues.Input,
                    Status: GuardStatusValues.Blocked,
                    Reasons:
                    [
                        new(
                            Code: GuardReasonCodeValues.CapabilityNotImplemented,
                            Severity: GuardSeverityValues.Blocked,
                            MessageHuman: "Эта capability пока не реализована в текущем runtime surface и не может считаться готовой.",
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
                            Source: CapabilitySummaryValues.Clipboard)
                    ]),
                new(
                    Capability: CapabilitySummaryValues.Launch,
                    Status: GuardStatusValues.Blocked,
                    Reasons:
                    [
                        new(
                            Code: GuardReasonCodeValues.CapabilityNotImplemented,
                            Severity: GuardSeverityValues.Blocked,
                            MessageHuman: "Эта capability пока не реализована в текущем runtime surface и не может считаться готовой.",
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
            ]);
    }

    private sealed class FakeRuntimeGuardService(RuntimeGuardAssessment assessment) : IRuntimeGuardService
    {
        public RuntimeGuardAssessment GetSnapshot() => assessment;
    }
}
