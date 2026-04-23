using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Session;
using WinBridge.Runtime.Tooling;
using WinBridge.Server.ComputerUse;

namespace WinBridge.Server.IntegrationTests;

public sealed class ComputerUseWinFinalizationTests
{
    [Fact]
    public void FinalizerUsesBestEffortAuditAfterSharedStateCommit()
    {
        string root = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            AuditLogOptions options = new(
                ContentRootPath: root,
                EnvironmentName: "Tests",
                RunId: "computer-use-win-finalizer-tests",
                DiagnosticsRoot: Path.Combine(root, "artifacts", "diagnostics"),
                RunDirectory: Path.Combine(root, "artifacts", "diagnostics", "computer-use-win-finalizer-tests"),
                EventsPath: Path.Combine(root, "artifacts", "diagnostics", "computer-use-win-finalizer-tests", "events.jsonl"),
                SummaryPath: Path.Combine(root, "artifacts", "diagnostics", "computer-use-win-finalizer-tests", "summary.md"));
            AuditLog auditLog = new(options, TimeProvider.System);
            InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-finalizer-tests"));
            ComputerUseWinStateStore stateStore = new(TimeProvider.System, TimeSpan.FromSeconds(30), maxEntries: 4);
            WindowDescriptor selectedWindow = CreateWindow();
            ComputerUseWinPreparedAppState preparedState = CreatePreparedState(selectedWindow);

            using AuditInvocationScope invocation = auditLog.BeginInvocation(
                ToolNames.ComputerUseWinGetAppState,
                new { hwnd = selectedWindow.Hwnd },
                sessionManager.GetSnapshot());

            File.Delete(options.EventsPath);
            Directory.CreateDirectory(options.EventsPath);
            File.Delete(options.SummaryPath);
            Directory.CreateDirectory(options.SummaryPath);

            CallToolResult result = ComputerUseWinGetAppStateFinalizer.FinalizeSuccess(
                invocation,
                appId: "explorer",
                selectedWindow,
                preparedState,
                stateStore,
                sessionManager);

            Assert.False(result.IsError);
            JsonElement payload = result.StructuredContent!.Value;
            string stateToken = payload.GetProperty("stateToken").GetString()!;
            Assert.True(stateStore.TryGet(stateToken, out ComputerUseWinStoredState? storedState));
            Assert.Equal(preparedState.StoredState, storedState);
            Assert.Equal(selectedWindow.Hwnd, sessionManager.GetAttachedWindow()?.Window.Hwnd);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static WindowDescriptor CreateWindow() =>
        new(
            Hwnd: 101,
            Title: "Test window",
            ProcessName: "explorer",
            ProcessId: 1001,
            ThreadId: 2002,
            ClassName: "TestWindow",
            Bounds: new Bounds(0, 0, 640, 480),
            IsForeground: true,
            IsVisible: true);

    private static ComputerUseWinPreparedAppState CreatePreparedState(WindowDescriptor selectedWindow)
    {
        ComputerUseWinAppSession session = new("explorer", selectedWindow.Hwnd, selectedWindow.Title, selectedWindow.ProcessName, selectedWindow.ProcessId);
        ComputerUseWinStoredState storedState = new(
            session,
            selectedWindow,
            CaptureReference: null,
            Elements: new Dictionary<int, ComputerUseWinStoredElement>(),
            Observation: new ComputerUseWinObservationEnvelope(UiaSnapshotDefaults.Depth, 128),
            CapturedAtUtc: DateTimeOffset.UtcNow);

        return new(
            Session: session,
            StoredState: storedState,
            Capture: new CaptureMetadata(
                Scope: "window",
                TargetKind: "window",
                Hwnd: selectedWindow.Hwnd,
                Title: selectedWindow.Title,
                ProcessName: selectedWindow.ProcessName,
                Bounds: selectedWindow.Bounds,
                CoordinateSpace: "physical_pixels",
                PixelWidth: 320,
                PixelHeight: 200,
                CapturedAtUtc: DateTimeOffset.UtcNow,
                ArtifactPath: Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png"),
                MimeType: "image/png",
                ByteSize: 3,
                SessionRunId: "tests",
                EffectiveDpi: 96,
                DpiScale: 1.0,
                CaptureReference: null),
            AccessibilityTree: [],
            Instructions: [],
            Warnings: [],
            PngBytes: [1, 2, 3],
            MimeType: "image/png");
    }
}
