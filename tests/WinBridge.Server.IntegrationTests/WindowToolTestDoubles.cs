using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Guards;
using WinBridge.Runtime.Tooling;
using WinBridge.Runtime.Windows.Display;
using WinBridge.Runtime.Windows.Input;
using WinBridge.Runtime.Waiting;
using WinBridge.Runtime.Windows.Launch;
using WinBridge.Runtime.Windows.Shell;
using WinBridge.Runtime.Windows.UIA;

namespace WinBridge.Server.IntegrationTests;

internal static class WindowToolTestData
{
    public static MonitorInfo CreateMonitor(
        string monitorId = "display-source:0000000100000000:1",
        string friendlyName = "Primary monitor",
        string gdiDeviceName = @"\\.\DISPLAY1",
        bool isPrimary = true,
        long handle = 501) =>
        new(
            new MonitorDescriptor(
                MonitorId: monitorId,
                FriendlyName: friendlyName,
                GdiDeviceName: gdiDeviceName,
                Bounds: new Bounds(0, 0, 1920, 1080),
                WorkArea: new Bounds(0, 0, 1920, 1040),
                IsPrimary: isPrimary),
            handle,
            [handle]);
}

internal sealed class FakeMonitorManager(
    IReadOnlyList<MonitorInfo>? monitors = null,
    DisplayIdentityDiagnostics? diagnostics = null,
    IReadOnlyDictionary<long, string>? windowToMonitorMap = null) : IMonitorManager
{
    private readonly IReadOnlyList<MonitorInfo> _monitors = monitors ?? [WindowToolTestData.CreateMonitor()];
    private readonly DisplayIdentityDiagnostics _diagnostics = diagnostics ?? new(
        IdentityMode: DisplayIdentityModeValues.DisplayConfigStrong,
        FailedStage: null,
        ErrorCode: null,
        ErrorName: null,
        MessageHuman: "Strong monitor identity resolved through QueryDisplayConfig for all active desktop monitors.",
        CapturedAtUtc: DateTimeOffset.UtcNow);
    private readonly IReadOnlyDictionary<long, string> _windowToMonitorMap = windowToMonitorMap ?? new Dictionary<long, string>();

    public DisplayTopologySnapshot GetTopologySnapshot() => new(_monitors, _diagnostics);

    public MonitorInfo? FindMonitorById(string monitorId, DisplayTopologySnapshot? snapshot = null) =>
        (snapshot?.Monitors ?? _monitors).FirstOrDefault(
            monitor => string.Equals(
                monitor.Descriptor.MonitorId,
                monitorId,
                StringComparison.OrdinalIgnoreCase));

    public MonitorInfo? FindMonitorByHandle(long handle, DisplayTopologySnapshot? snapshot = null)
    {
        IReadOnlyList<MonitorInfo> source = snapshot?.Monitors ?? _monitors;
        return source.FirstOrDefault(monitor => monitor.Handles.Contains(handle));
    }

    public long? GetMonitorHandleForWindow(long hwnd)
    {
        MonitorInfo? monitor = FindMonitorForWindow(hwnd);
        return monitor?.CaptureHandle;
    }

    public MonitorInfo? FindMonitorForWindow(long hwnd, DisplayTopologySnapshot? snapshot = null)
    {
        if (_windowToMonitorMap.TryGetValue(hwnd, out string? monitorId))
        {
            return FindMonitorById(monitorId, snapshot);
        }

        IReadOnlyList<MonitorInfo> source = snapshot?.Monitors ?? _monitors;
        return source.Count > 0 ? source[0] : null;
    }

    public MonitorInfo? GetPrimaryMonitor(DisplayTopologySnapshot? snapshot = null)
    {
        IReadOnlyList<MonitorInfo> source = snapshot?.Monitors ?? _monitors;
        for (int index = 0; index < source.Count; index++)
        {
            if (source[index].Descriptor.IsPrimary)
            {
                return source[index];
            }
        }

        return source.Count > 0 ? source[0] : null;
    }
}

internal sealed class FakeWindowActivationService(Func<WindowDescriptor, ActivateWindowResult>? handler = null) : IWindowActivationService
{
    public long? LastHwnd { get; private set; }

    public Task<ActivateWindowResult> ActivateAsync(WindowDescriptor targetWindow, CancellationToken cancellationToken)
    {
        LastHwnd = targetWindow.Hwnd;
        if (handler is null)
        {
            throw new NotSupportedException("ActivateWindow не должен вызываться в этом тесте.");
        }

        return Task.FromResult(handler(targetWindow));
    }
}

internal sealed class FakeUiAutomationService(
    Func<WindowDescriptor, UiaSnapshotRequest, CancellationToken, Task<UiaSnapshotResult>>? handler = null) : IUiAutomationService
{
    public int Calls { get; private set; }

    public WindowDescriptor? LastWindow { get; private set; }

    public UiaSnapshotRequest? LastRequest { get; private set; }

    public Task<UiaSnapshotResult> SnapshotAsync(
        WindowDescriptor targetWindow,
        UiaSnapshotRequest request,
        CancellationToken cancellationToken)
    {
        Calls++;
        LastWindow = targetWindow;
        LastRequest = request;

        if (handler is null)
        {
            throw new NotSupportedException("UIA snapshot не должен вызываться в этом тесте.");
        }

        return handler(targetWindow, request, cancellationToken);
    }
}

internal sealed class FakeWaitService(
    Func<WaitTargetResolution, WaitRequest, CancellationToken, Task<WaitResult>>? handler = null) : IWaitService
{
    public int Calls { get; private set; }

    public WaitTargetResolution? LastTarget { get; private set; }

    public WaitRequest? LastRequest { get; private set; }

    public Task<WaitResult> WaitAsync(
        WaitTargetResolution target,
        WaitRequest request,
        CancellationToken cancellationToken)
    {
        Calls++;
        LastTarget = target;
        LastRequest = request;

        if (handler is null)
        {
            throw new NotSupportedException("Wait service не должен вызываться в этом тесте.");
        }

        return handler(target, request, cancellationToken);
    }
}

internal sealed class FakeProcessLaunchService(
    Func<LaunchProcessRequest, CancellationToken, Task<LaunchProcessResult>>? handler = null) : IProcessLaunchService
{
    public int Calls { get; private set; }

    public LaunchProcessRequest? LastRequest { get; private set; }

    public Task<LaunchProcessResult> LaunchAsync(LaunchProcessRequest request, CancellationToken cancellationToken)
    {
        Calls++;
        LastRequest = request;

        if (handler is null)
        {
            throw new NotSupportedException("Launch service не должен вызываться в этом тесте.");
        }

        return handler(request, cancellationToken);
    }
}

internal sealed class FakeOpenTargetService(
    Func<OpenTargetRequest, CancellationToken, Task<OpenTargetResult>>? handler = null) : IOpenTargetService
{
    public int Calls { get; private set; }

    public OpenTargetRequest? LastRequest { get; private set; }

    public Task<OpenTargetResult> OpenAsync(OpenTargetRequest request, CancellationToken cancellationToken)
    {
        Calls++;
        LastRequest = request;

        if (handler is null)
        {
            throw new NotSupportedException("OpenTarget service не должен вызываться в этом тесте.");
        }

        return handler(request, cancellationToken);
    }
}

internal sealed class FakeInputService(
    Func<InputRequest, InputExecutionContext, CancellationToken, Task<InputResult>>? handler = null) : IInputService
{
    public int Calls { get; private set; }

    public InputRequest? LastRequest { get; private set; }

    public InputExecutionContext? LastContext { get; private set; }

    public Task<InputResult> ExecuteAsync(
        InputRequest request,
        InputExecutionContext context,
        CancellationToken cancellationToken)
        => ExecuteAsync(request, context, InputExecutionProfileValues.ClickFirstPublic, cancellationToken);

    public Task<InputResult> ExecuteAsync(
        InputRequest request,
        InputExecutionContext context,
        string executionProfile,
        CancellationToken cancellationToken)
    {
        Calls++;
        LastRequest = request;
        LastContext = context;

        if (handler is null)
        {
            throw new NotSupportedException("Input service не должен вызываться в этом тесте.");
        }

        return handler(request, context, cancellationToken);
    }
}

internal sealed class FakeToolExecutionGate(
    Func<ToolExecutionPolicyDescriptor, ToolExecutionIntent, ToolExecutionDecision>? handler = null) : IToolExecutionGate
{
    public int Calls { get; private set; }

    public ToolExecutionIntent? LastIntent { get; private set; }

    public ToolExecutionDecision Evaluate(ToolExecutionPolicyDescriptor policy, ToolExecutionIntent intent)
    {
        Calls++;
        LastIntent = intent;

        if (handler is null)
        {
            throw new NotSupportedException("Shared gate не должен вызываться в этом тесте.");
        }

        return handler(policy, intent);
    }

    public ToolExecutionDecision Evaluate(
        ToolExecutionPolicyDescriptor policy,
        RuntimeGuardAssessment assessment,
        ToolExecutionIntent intent)
    {
        Calls++;
        LastIntent = intent;

        if (handler is null)
        {
            throw new NotSupportedException("Shared gate не должен вызываться в этом тесте.");
        }

        return handler(policy, intent);
    }
}
