using System.Reflection;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Session;
using WinBridge.Runtime.Windows.Capture;
using WinBridge.Runtime.Windows.Shell;
using WinBridge.Server.Tools;

namespace WinBridge.Server.IntegrationTests;

public sealed class WindowUiaSnapshotDeferredToolTests
{
    [Fact]
    public void UiaSnapshotRemainsDeferredUnsupportedDuringPackageA()
    {
        WindowTools tools = CreateTools();

        DeferredToolResult result = tools.UiaSnapshot();

        Assert.Equal("unsupported", result.Status);
        Assert.Equal("roadmap stage 6", result.PlannedPhase);
        Assert.Equal("windows.uia_snapshot", result.ToolName);
        Assert.Contains("ещё не реализован", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeferredHandlerDepthDefaultMatchesCanonicalSnapshotDefaults()
    {
        ParameterInfo depthParameter = typeof(WindowTools)
            .GetMethod(nameof(WindowTools.UiaSnapshot))!
            .GetParameters()
            .Single(parameter => string.Equals(parameter.Name, "depth", StringComparison.Ordinal));

        Assert.Equal(UiaSnapshotDefaults.Depth, Assert.IsType<int>(depthParameter.DefaultValue));
    }

    private static WindowTools CreateTools()
    {
        string root = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        AuditLogOptions options = new(
            ContentRootPath: root,
            EnvironmentName: "Tests",
            RunId: "window-uia-snapshot-tests",
            DiagnosticsRoot: Path.Combine(root, "artifacts", "diagnostics"),
            RunDirectory: Path.Combine(root, "artifacts", "diagnostics", "window-uia-snapshot-tests"),
            EventsPath: Path.Combine(root, "artifacts", "diagnostics", "window-uia-snapshot-tests", "events.jsonl"),
            SummaryPath: Path.Combine(root, "artifacts", "diagnostics", "window-uia-snapshot-tests", "summary.md"));
        AuditLog auditLog = new(options, TimeProvider.System);
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("window-uia-snapshot-tests"));
        FakeWindowManager windowManager = new([]);

        return new WindowTools(
            auditLog,
            sessionManager,
            windowManager,
            new NoopCaptureService(),
            new FakeMonitorManager(),
            new FakeWindowActivationService(),
            new WindowTargetResolver(windowManager));
    }

    private sealed class NoopCaptureService : ICaptureService
    {
        public Task<CaptureResult> CaptureAsync(CaptureTarget target, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Capture не должен вызываться в deferred UIA snapshot tests.");
    }

    private sealed class FakeWindowManager(IReadOnlyList<WindowDescriptor> windows) : IWindowManager
    {
        public IReadOnlyList<WindowDescriptor> ListWindows(bool includeInvisible = false) => windows;

        public WindowDescriptor? FindWindow(WindowSelector selector)
        {
            selector.Validate();
            return windows.FirstOrDefault(window => window.Hwnd == selector.Hwnd);
        }

        public bool TryFocus(long hwnd) => windows.Any(window => window.Hwnd == hwnd);
    }
}
