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
    }
}
