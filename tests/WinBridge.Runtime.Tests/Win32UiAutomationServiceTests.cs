using System.Text.Json;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Windows.UIA;

namespace WinBridge.Runtime.Tests;

public sealed class Win32UiAutomationServiceTests
{
    [Fact]
    public async Task SnapshotAsyncBuildsArtifactAndAuditEventForSuccessfulRuntimePath()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateAuditLogOptions(root, "run-uia-success");
        AuditLog auditLog = new(options, TimeProvider.System);
        UiaSnapshotResult backendResult = CreateSuccessResult();
        Win32UiAutomationService service = new(
            new FakeBackend(CreateBackendResult(backendResult)),
            new UiaSnapshotArtifactWriter(options),
            auditLog,
            TimeProvider.System);

        UiaSnapshotResult result = await service.SnapshotAsync(CreateWindow(), new UiaSnapshotRequest(), CancellationToken.None);

        Assert.Equal(UiaSnapshotStatusValues.Done, result.Status);
        Assert.Equal("element_from_handle", result.AcquisitionMode);
        Assert.Null(result.Session);
        Assert.NotNull(result.ArtifactPath);
        Assert.Contains(Path.Combine("artifacts", "diagnostics", "run-uia-success", "uia"), result.ArtifactPath!, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(result.ArtifactPath));

        using JsonDocument artifact = JsonDocument.Parse(await File.ReadAllTextAsync(result.ArtifactPath));
        JsonElement rootPayload = artifact.RootElement;
        Assert.Equal("done", rootPayload.GetProperty("status").GetString());
        Assert.Equal("element_from_handle", rootPayload.GetProperty("acquisition_mode").GetString());
        Assert.Equal(backendResult.NodeCount, rootPayload.GetProperty("node_count").GetInt32());
        Assert.False(rootPayload.GetProperty("depth_boundary_reached").GetBoolean());
        Assert.False(rootPayload.GetProperty("node_budget_boundary_reached").GetBoolean());
        Assert.Equal("rid:1.2", rootPayload.GetProperty("root").GetProperty("element_id").GetString());
        Assert.False(rootPayload.TryGetProperty("session", out _));

        string[] eventLines = await File.ReadAllLinesAsync(options.EventsPath);
        Assert.Single(eventLines);
        Assert.Contains("\"event_name\":\"uia.snapshot.runtime.completed\"", eventLines[0], StringComparison.Ordinal);
        Assert.Contains("\"tool_name\":\"windows.uia_snapshot\"", eventLines[0], StringComparison.Ordinal);
        Assert.Contains("\"requested_depth\":\"3\"", eventLines[0], StringComparison.Ordinal);
        Assert.Contains("\"requested_max_nodes\":\"256\"", eventLines[0], StringComparison.Ordinal);
        Assert.Contains("\"depth_boundary_reached\":\"False\"", eventLines[0], StringComparison.Ordinal);
        Assert.Contains("\"node_budget_boundary_reached\":\"False\"", eventLines[0], StringComparison.Ordinal);
        Assert.Contains("\"artifact_path\"", eventLines[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task SnapshotAsyncFailsEarlyForInvalidDepthWithoutCallingBackend()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateAuditLogOptions(root, "run-uia-invalid");
        AuditLog auditLog = new(options, TimeProvider.System);
        CountingBackend backend = new(CreateBackendResult(CreateSuccessResult()));
        Win32UiAutomationService service = new(
            backend,
            new UiaSnapshotArtifactWriter(options),
            auditLog,
            TimeProvider.System);

        UiaSnapshotResult result = await service.SnapshotAsync(
            CreateWindow(),
            new UiaSnapshotRequest { Depth = -1, MaxNodes = 1 },
            CancellationToken.None);

        Assert.Equal(UiaSnapshotStatusValues.Failed, result.Status);
        Assert.Equal(0, backend.Calls);
        Assert.Null(result.ArtifactPath);
        string[] eventLines = await File.ReadAllLinesAsync(options.EventsPath);
        Assert.Single(eventLines);
        Assert.Contains("\"outcome\":\"failed\"", eventLines[0], StringComparison.Ordinal);
        Assert.Contains("\"failure_stage\":\"request_validation\"", eventLines[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task SnapshotAsyncReturnsFailedWhenArtifactWriteFails()
    {
        string root = CreateTempDirectory();
        AuditLogOptions auditOptions = CreateAuditLogOptions(root, "run-uia-artifact-audit");
        AuditLog auditLog = new(auditOptions, TimeProvider.System);

        string blockingFilePath = Path.Combine(root, "artifact-blocker");
        await File.WriteAllTextAsync(blockingFilePath, "block");
        AuditLogOptions badArtifactOptions = auditOptions with { RunDirectory = blockingFilePath };

        Win32UiAutomationService service = new(
            new FakeBackend(CreateBackendResult(CreateSuccessResult())),
            new UiaSnapshotArtifactWriter(badArtifactOptions),
            auditLog,
            TimeProvider.System);

        UiaSnapshotResult result = await service.SnapshotAsync(CreateWindow(), new UiaSnapshotRequest(), CancellationToken.None);

        Assert.Equal(UiaSnapshotStatusValues.Failed, result.Status);
        Assert.Null(result.ArtifactPath);
        Assert.NotNull(result.Root);
        Assert.Equal("Runtime не смог записать UIA snapshot artifact на диск.", result.Reason);
        string[] eventLines = await File.ReadAllLinesAsync(auditOptions.EventsPath);
        Assert.Single(eventLines);
        Assert.Contains("\"outcome\":\"failed\"", eventLines[0], StringComparison.Ordinal);
        Assert.Contains("\"failure_stage\":\"artifact_write\"", eventLines[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task SnapshotAsyncReturnsFailedWhenBackendCannotResolveRoot()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateAuditLogOptions(root, "run-uia-missing-root");
        AuditLog auditLog = new(options, TimeProvider.System);
        Win32UiAutomationService service = new(
            new FakeBackend(new UiaSnapshotBackendResult(false, "UI Automation не смогла получить root element для выбранного hwnd.", UiaSnapshotFailureStageValues.RootAcquisition, DateTimeOffset.UtcNow)),
            new UiaSnapshotArtifactWriter(options),
            auditLog,
            TimeProvider.System);

        UiaSnapshotResult result = await service.SnapshotAsync(CreateWindow(), new UiaSnapshotRequest(), CancellationToken.None);

        Assert.Equal(UiaSnapshotStatusValues.Failed, result.Status);
        Assert.Null(result.Root);
        Assert.Null(result.ArtifactPath);
        Assert.Equal("element_from_handle", result.AcquisitionMode);
        string[] eventLines = await File.ReadAllLinesAsync(options.EventsPath);
        Assert.Single(eventLines);
        Assert.Contains("\"failure_stage\":\"root_acquisition\"", eventLines[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task SnapshotAsyncReturnsFailedResultWhenWorkerBinaryIsMissing()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateAuditLogOptions(root, "run-uia-missing-worker");
        AuditLog auditLog = new(options, TimeProvider.System);
        Win32UiAutomationService service = new(
            new ProcessIsolatedUiAutomationBackend(
                TimeProvider.System,
                UiAutomationExecutionOptions.Default,
                workerLaunchSpecResolver: () => throw new FileNotFoundException("missing")),
            new UiaSnapshotArtifactWriter(options),
            auditLog,
            TimeProvider.System);

        UiaSnapshotResult result = await service.SnapshotAsync(CreateWindow(), new UiaSnapshotRequest(), CancellationToken.None);

        Assert.Equal(UiaSnapshotStatusValues.Failed, result.Status);
        Assert.Null(result.Root);
        Assert.Null(result.ArtifactPath);
        Assert.Equal("UIA worker process не найден рядом с host output.", result.Reason);
        string[] eventLines = await File.ReadAllLinesAsync(options.EventsPath);
        Assert.Single(eventLines);
        Assert.Contains("\"failure_stage\":\"worker_process\"", eventLines[0], StringComparison.Ordinal);
    }

    private static AuditLogOptions CreateAuditLogOptions(string root, string runId) =>
        new(
            ContentRootPath: root,
            EnvironmentName: "Tests",
            RunId: runId,
            DiagnosticsRoot: Path.Combine(root, "artifacts", "diagnostics"),
            RunDirectory: Path.Combine(root, "artifacts", "diagnostics", runId),
            EventsPath: Path.Combine(root, "artifacts", "diagnostics", runId, "events.jsonl"),
            SummaryPath: Path.Combine(root, "artifacts", "diagnostics", runId, "summary.md"));

    private static WindowDescriptor CreateWindow() =>
        new(
            Hwnd: 42,
            Title: "Calculator",
            ProcessName: "CalculatorApp",
            ProcessId: 42,
            ThreadId: 84,
            ClassName: "CalcWindow",
            Bounds: new Bounds(0, 0, 800, 600),
            IsForeground: true,
            IsVisible: true);

    private static UiaSnapshotResult CreateSuccessResult() =>
        new(
            Status: UiaSnapshotStatusValues.Done,
            Reason: null,
            Window: CreateWindow(),
            View: UiaSnapshotViewValues.Control,
            RequestedDepth: 3,
            RequestedMaxNodes: UiaSnapshotDefaults.MaxNodes,
            RealizedDepth: 1,
            NodeCount: 2,
            Truncated: false,
            DepthBoundaryReached: false,
            NodeBudgetBoundaryReached: false,
            AcquisitionMode: "element_from_handle",
            ArtifactPath: null,
            CapturedAtUtc: new DateTimeOffset(2026, 3, 18, 12, 34, 56, TimeSpan.Zero),
            Root: new UiaElementSnapshot
            {
                ElementId = "rid:1.2",
                ParentElementId = null,
                Depth = 0,
                Ordinal = 0,
                Name = "Calculator",
                AutomationId = "Root",
                ClassName = "ApplicationFrameWindow",
                FrameworkId = "Win32",
                ControlType = "window",
                ControlTypeId = 50032,
                LocalizedControlType = "окно",
                IsControlElement = true,
                IsContentElement = true,
                IsEnabled = true,
                IsOffscreen = false,
                HasKeyboardFocus = true,
                Patterns = ["window"],
                Value = null,
                BoundingRectangle = new Bounds(0, 0, 800, 600),
                NativeWindowHandle = 42,
                Children =
                [
                    new UiaElementSnapshot
                    {
                        ElementId = "path:0/0",
                        ParentElementId = "rid:1.2",
                        Depth = 1,
                        Ordinal = 0,
                        Name = "Save",
                        AutomationId = "SaveButton",
                        ClassName = "Button",
                        FrameworkId = "Win32",
                        ControlType = "button",
                        ControlTypeId = 50000,
                        LocalizedControlType = "кнопка",
                        IsControlElement = true,
                        IsContentElement = true,
                        IsEnabled = true,
                        IsOffscreen = false,
                        HasKeyboardFocus = false,
                        Patterns = ["invoke"],
                        Value = null,
                        BoundingRectangle = new Bounds(20, 20, 60, 60),
                        NativeWindowHandle = 42,
                    },
                ],
            },
            Session: null);

    private static UiaSnapshotBackendResult CreateBackendResult(UiaSnapshotResult result) =>
        new(
            Success: true,
            Reason: null,
            FailureStage: null,
            CapturedAtUtc: result.CapturedAtUtc,
            Root: result.Root,
            RealizedDepth: result.RealizedDepth,
            NodeCount: result.NodeCount,
            Truncated: result.Truncated,
            DepthBoundaryReached: result.DepthBoundaryReached,
            NodeBudgetBoundaryReached: result.NodeBudgetBoundaryReached);

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private class FakeBackend(UiaSnapshotBackendResult result) : IUiaSnapshotBackend
    {
        public virtual Task<UiaSnapshotBackendResult> CaptureAsync(
            WindowDescriptor targetWindow,
            UiaSnapshotRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }

    private sealed class CountingBackend(UiaSnapshotBackendResult result) : FakeBackend(result)
    {
        public int Calls { get; private set; }

        public override Task<UiaSnapshotBackendResult> CaptureAsync(
            WindowDescriptor targetWindow,
            UiaSnapshotRequest request,
            CancellationToken cancellationToken)
        {
            Calls++;
            return base.CaptureAsync(targetWindow, request, cancellationToken);
        }
    }
}
