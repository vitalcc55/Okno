using WinBridge.Runtime;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Session;
using WinBridge.Runtime.Tooling;
using WinBridge.Server.Tools;

namespace WinBridge.Server.IntegrationTests;

public sealed class AdminToolTests
{
    [Fact]
    public void HealthReturnsConservativeReadinessSnapshot()
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
        AdminTools tools = new(auditLog, runtimeInfo, sessionManager, new FakeMonitorManager());

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
        Assert.All(result.Readiness.Domains, item => Assert.Equal(GuardStatusValues.Unknown, item.Status));

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
        Assert.Equal(GuardReasonCodeValues.AssessmentNotImplemented, warning.Code);
        Assert.Equal(GuardSeverityValues.Warning, warning.Severity);
        Assert.Equal(ToolNames.OknoHealth, warning.Source);
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
        AdminTools tools = new(auditLog, runtimeInfo, sessionManager, new FakeMonitorManager());

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
}
