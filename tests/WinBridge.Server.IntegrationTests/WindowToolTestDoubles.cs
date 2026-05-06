// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

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

internal static class WindowToolFakeCall
{
    public static NotSupportedException Unexpected(string operationName) =>
        new($"{operationName} не должен вызываться в этом тесте.");
}

internal abstract class WindowToolFakePairService<TFirst, TSecond, TResult>(
    Func<TFirst, TSecond, CancellationToken, Task<TResult>>? handler,
    string operationName)
{
    public int Calls { get; private set; }

    protected TFirst? LastFirst { get; private set; }

    protected TSecond? LastSecond { get; private set; }

    protected Task<TResult> ExecuteCoreAsync(TFirst first, TSecond second, CancellationToken cancellationToken)
    {
        Calls++;
        LastFirst = first;
        LastSecond = second;

        return handler is null
            ? throw WindowToolFakeCall.Unexpected(operationName)
            : handler(first, second, cancellationToken);
    }
}

internal abstract class WindowToolFakeWindowRequestService<TRequest, TResult>(
    Func<WindowDescriptor, TRequest, CancellationToken, Task<TResult>>? handler,
    string operationName) : WindowToolFakePairService<WindowDescriptor, TRequest, TResult>(handler, operationName)
{
    public WindowDescriptor? LastWindow => LastFirst;

    public TRequest? LastRequest => LastSecond;
}

internal abstract class WindowToolFakeRequestService<TRequest, TResult>(
    Func<TRequest, CancellationToken, Task<TResult>>? handler,
    string operationName)
{
    public int Calls { get; private set; }

    public TRequest? LastRequest { get; private set; }

    protected Task<TResult> ExecuteCoreAsync(TRequest request, CancellationToken cancellationToken)
    {
        Calls++;
        LastRequest = request;

        return handler is null
            ? throw WindowToolFakeCall.Unexpected(operationName)
            : handler(request, cancellationToken);
    }
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

    public MonitorInfo? FindMonitorByHandle(long handle, DisplayTopologySnapshot? snapshot = null) =>
        (snapshot?.Monitors ?? _monitors).FirstOrDefault(monitor => monitor.Handles.Contains(handle));

    public long? GetMonitorHandleForWindow(long hwnd) =>
        FindMonitorForWindow(hwnd)?.CaptureHandle;

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

        return handler is null
            ? throw WindowToolFakeCall.Unexpected("ActivateWindow")
            : Task.FromResult(handler(targetWindow));
    }
}

internal sealed class FakeUiAutomationService(
    Func<WindowDescriptor, UiaSnapshotRequest, CancellationToken, Task<UiaSnapshotResult>>? handler = null)
    : WindowToolFakeWindowRequestService<UiaSnapshotRequest, UiaSnapshotResult>(handler, "UIA snapshot"),
      IUiAutomationService
{
    public Task<UiaSnapshotResult> SnapshotAsync(
        WindowDescriptor targetWindow,
        UiaSnapshotRequest request,
        CancellationToken cancellationToken) =>
        ExecuteCoreAsync(targetWindow, request, cancellationToken);
}

internal sealed class FakeUiAutomationSetValueService(
    Func<WindowDescriptor, UiaSetValueRequest, CancellationToken, Task<UiaSetValueResult>>? handler = null)
    : WindowToolFakeWindowRequestService<UiaSetValueRequest, UiaSetValueResult>(handler, "UIA set_value"),
      IUiAutomationSetValueService
{
    public Task<UiaSetValueResult> SetValueAsync(
        WindowDescriptor targetWindow,
        UiaSetValueRequest request,
        CancellationToken cancellationToken) =>
        ExecuteCoreAsync(targetWindow, request, cancellationToken);
}

internal sealed class FakeUiAutomationScrollService(
    Func<WindowDescriptor, UiaScrollRequest, CancellationToken, Task<UiaScrollResult>>? handler = null)
    : WindowToolFakeWindowRequestService<UiaScrollRequest, UiaScrollResult>(handler, "UIA scroll"),
      IUiAutomationScrollService
{
    public Task<UiaScrollResult> ScrollAsync(
        WindowDescriptor targetWindow,
        UiaScrollRequest request,
        CancellationToken cancellationToken) =>
        ExecuteCoreAsync(targetWindow, request, cancellationToken);
}

internal sealed class FakeUiAutomationSecondaryActionService(
    Func<WindowDescriptor, UiaSecondaryActionRequest, CancellationToken, Task<UiaSecondaryActionResult>>? handler = null)
    : WindowToolFakeWindowRequestService<UiaSecondaryActionRequest, UiaSecondaryActionResult>(handler, "UIA perform_secondary_action"),
      IUiAutomationSecondaryActionService
{
    public Task<UiaSecondaryActionResult> ExecuteAsync(
        WindowDescriptor targetWindow,
        UiaSecondaryActionRequest request,
        CancellationToken cancellationToken) =>
        ExecuteCoreAsync(targetWindow, request, cancellationToken);
}

internal sealed class FakeWaitService(
    Func<WaitTargetResolution, WaitRequest, CancellationToken, Task<WaitResult>>? handler = null)
    : WindowToolFakePairService<WaitTargetResolution, WaitRequest, WaitResult>(handler, "Wait service"),
      IWaitService
{
    public WaitTargetResolution? LastTarget => LastFirst;

    public WaitRequest? LastRequest => LastSecond;

    public Task<WaitResult> WaitAsync(
        WaitTargetResolution target,
        WaitRequest request,
        CancellationToken cancellationToken) =>
        ExecuteCoreAsync(target, request, cancellationToken);
}

internal sealed class FakeProcessLaunchService(
    Func<LaunchProcessRequest, CancellationToken, Task<LaunchProcessResult>>? handler = null)
    : WindowToolFakeRequestService<LaunchProcessRequest, LaunchProcessResult>(handler, "Launch service"),
      IProcessLaunchService
{
    public Task<LaunchProcessResult> LaunchAsync(LaunchProcessRequest request, CancellationToken cancellationToken) =>
        ExecuteCoreAsync(request, cancellationToken);
}

internal sealed class FakeOpenTargetService(
    Func<OpenTargetRequest, CancellationToken, Task<OpenTargetResult>>? handler = null)
    : WindowToolFakeRequestService<OpenTargetRequest, OpenTargetResult>(handler, "OpenTarget service"),
      IOpenTargetService
{
    public Task<OpenTargetResult> OpenAsync(OpenTargetRequest request, CancellationToken cancellationToken) =>
        ExecuteCoreAsync(request, cancellationToken);
}

internal sealed class FakeInputService(
    Func<InputRequest, InputExecutionContext, CancellationToken, Task<InputResult>>? handler = null)
    : WindowToolFakePairService<InputRequest, InputExecutionContext, InputResult>(handler, "Input service"),
      IInputService
{
    public InputRequest? LastRequest => LastFirst;

    public InputExecutionContext? LastContext => LastSecond;

    public Task<InputResult> ExecuteAsync(
        InputRequest request,
        InputExecutionContext context,
        CancellationToken cancellationToken) =>
        ExecuteAsync(request, context, InputExecutionProfileValues.ClickFirstPublic, cancellationToken);

    public Task<InputResult> ExecuteAsync(
        InputRequest request,
        InputExecutionContext context,
        string executionProfile,
        CancellationToken cancellationToken) =>
        ExecuteCoreAsync(request, context, cancellationToken);
}

internal sealed class FakeToolExecutionGate(
    Func<ToolExecutionPolicyDescriptor, ToolExecutionIntent, ToolExecutionDecision>? handler = null) : IToolExecutionGate
{
    public int Calls { get; private set; }

    public ToolExecutionIntent? LastIntent { get; private set; }

    public ToolExecutionDecision Evaluate(ToolExecutionPolicyDescriptor policy, ToolExecutionIntent intent) =>
        EvaluateCore(policy, intent);

    public ToolExecutionDecision Evaluate(
        ToolExecutionPolicyDescriptor policy,
        RuntimeGuardAssessment assessment,
        ToolExecutionIntent intent) =>
        EvaluateCore(policy, intent);

    private ToolExecutionDecision EvaluateCore(ToolExecutionPolicyDescriptor policy, ToolExecutionIntent intent)
    {
        Calls++;
        LastIntent = intent;

        return handler is null
            ? throw WindowToolFakeCall.Unexpected("Shared gate")
            : handler(policy, intent);
    }
}
