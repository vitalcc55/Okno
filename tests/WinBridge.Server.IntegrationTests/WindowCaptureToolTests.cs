// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Session;
using WinBridge.Runtime.Waiting;
using WinBridge.Runtime.Windows.Capture;
using WinBridge.Runtime.Windows.Display;
using WinBridge.Runtime.Windows.Shell;
using WinBridge.Server.Tools;

namespace WinBridge.Server.IntegrationTests;

public sealed class WindowCaptureToolTests
{
    private const string CaptureRunId = "capture-tests";
    private const string PrimaryMonitorId = "display-source:0000000100000000:1";
    private const string SecondaryMonitorId = "display-source:0000000100000000:2";
    private const string PrimaryMonitorName = "Primary monitor";
    private const string SecondaryMonitorName = "Secondary monitor";

    [Fact]
    public async Task CapturePrefersExplicitHwndOverAttachedWindow()
    {
        WindowDescriptor attachedWindow = CreateWindow(hwnd: 101, title: "Attached");
        WindowDescriptor explicitWindow = CreateWindow(hwnd: 202, title: "Explicit");
        FakeCaptureService captureService = new(CreateCaptureResult(explicitWindow, "window"));
        WindowTools tools = CreateTools([attachedWindow, explicitWindow], captureService, attachedWindow);

        CallToolResult result = await tools.Capture(hwnd: explicitWindow.Hwnd);

        JsonElement payload = AssertSuccessfulPayload(result);
        AssertCaptureTarget(captureService.LastTarget, CaptureScope.Window, explicitWindow.Hwnd);
        Assert.Equal("window", payload.GetProperty("scope").GetString());
        Assert.Equal(explicitWindow.Hwnd, payload.GetProperty("hwnd").GetInt64());
        Assert.Equal(CaptureCoordinateSpaceValues.PhysicalPixels, payload.GetProperty("coordinateSpace").GetString());
        Assert.Equal(96, payload.GetProperty("effectiveDpi").GetInt32());
    }

    [Fact]
    public async Task CaptureUsesExplicitHwndEvenWhenStableIdentitySignalsAreMissing()
    {
        WindowDescriptor explicitWindow = CreateWindowWithoutStableIdentity(hwnd: 202, title: "Weak explicit");
        FakeCaptureService captureService = new(CreateCaptureResult(explicitWindow, "window"));
        WindowTools tools = CreateTools([explicitWindow], captureService);

        CallToolResult result = await tools.Capture(hwnd: explicitWindow.Hwnd);

        Assert.False(result.IsError);
        AssertCaptureTarget(captureService.LastTarget, CaptureScope.Window, explicitWindow.Hwnd);
    }

    [Fact]
    public async Task WindowCaptureKeepsObservePayloadWhenInputTargetIdentityIsUnavailable()
    {
        WindowDescriptor explicitWindow = CreateWindowWithoutStableIdentity(hwnd: 203, title: "Weak capture");
        FakeCaptureService captureService = new(CreateCaptureResult(explicitWindow, "window", includeCaptureReference: false));
        WindowTools tools = CreateTools([explicitWindow], captureService);

        CallToolResult result = await tools.Capture(hwnd: explicitWindow.Hwnd);

        JsonElement payload = AssertSuccessfulPayload(result);
        Assert.Equal(explicitWindow.Hwnd, payload.GetProperty("hwnd").GetInt64());
        AssertMissingProperties(payload, "captureReference");
    }

    [Fact]
    public async Task CaptureUsesAttachedWindowWhenHwndIsMissing()
    {
        WindowDescriptor attachedWindow = CreateWindow(hwnd: 303, title: "Attached");
        FakeCaptureService captureService = new(CreateCaptureResult(attachedWindow, "window"));
        WindowTools tools = CreateTools([attachedWindow], captureService, attachedWindow);

        CallToolResult result = await tools.Capture();

        Assert.False(result.IsError);
        AssertCaptureTarget(captureService.LastTarget, CaptureScope.Window, attachedWindow.Hwnd);
    }

    [Fact]
    public async Task CaptureDoesNotFallbackToAttachedWindowForExplicitZeroHwnd()
    {
        WindowDescriptor attachedWindow = CreateWindow(hwnd: 303, title: "Attached");
        FakeCaptureService captureService = new(CreateCaptureResult(attachedWindow, "window"));
        WindowTools tools = CreateTools([attachedWindow], captureService, attachedWindow);

        CallToolResult result = await tools.Capture(hwnd: 0);

        Assert.Null(captureService.LastTarget);
        AssertToolError(result, "по указанному hwnd больше не найдено");
    }

    [Fact]
    public async Task CaptureUsesAttachedWindowMonitorForDesktopScope()
    {
        WindowDescriptor attachedWindow = CreateWindow(hwnd: 404, title: "Attached");
        FakeCaptureService captureService = new(CreateCaptureResult(attachedWindow, "desktop", "monitor"));
        WindowTools tools = CreateTools([attachedWindow], captureService, attachedWindow);

        CallToolResult result = await tools.Capture(scope: "desktop");

        JsonElement payload = AssertSuccessfulPayload(result);
        AssertCaptureTarget(captureService.LastTarget, CaptureScope.Desktop, attachedWindow.Hwnd);
        AssertDesktopMonitorPayload(payload);
        Assert.Equal(CaptureCoordinateSpaceValues.PhysicalPixels, payload.GetProperty("coordinateSpace").GetString());
        AssertMissingProperties(payload, "effectiveDpi", "frameBounds", "captureReference");
    }

    [Fact]
    public async Task CaptureUsesExplicitMonitorIdForDesktopScope()
    {
        FakeCaptureService captureService = new(CreateCaptureResult(
            window: null,
            scope: "desktop",
            targetKind: "monitor",
            monitorId: SecondaryMonitorId,
            monitorFriendlyName: SecondaryMonitorName));
        WindowTools tools = CreateTools([], captureService);

        CallToolResult result = await tools.Capture(scope: "desktop", monitorId: SecondaryMonitorId);

        JsonElement payload = AssertSuccessfulPayload(result);
        Assert.Equal(CaptureScope.Desktop, captureService.LastTarget?.Scope);
        Assert.Equal(SecondaryMonitorId, captureService.LastTarget?.MonitorId);
        Assert.Equal(SecondaryMonitorId, payload.GetProperty("monitorId").GetString());
        Assert.Equal(SecondaryMonitorName, payload.GetProperty("monitorFriendlyName").GetString());
        Assert.Equal(CaptureCoordinateSpaceValues.PhysicalPixels, payload.GetProperty("coordinateSpace").GetString());
    }

    [Fact]
    public async Task CaptureUsesExplicitHwndToResolveDesktopMonitorAndOverridesAttachedWindow()
    {
        WindowDescriptor attachedWindow = CreateWindow(hwnd: 410, title: "Attached");
        WindowDescriptor explicitWindow = CreateWindow(hwnd: 411, title: "Explicit");
        IReadOnlyList<MonitorInfo> monitors =
        [
            WindowToolTestData.CreateMonitor(monitorId: PrimaryMonitorId, friendlyName: PrimaryMonitorName, handle: 501),
            WindowToolTestData.CreateMonitor(
                monitorId: SecondaryMonitorId,
                friendlyName: SecondaryMonitorName,
                isPrimary: false,
                handle: 502),
        ];
        FakeMonitorManager monitorManager = new(
            monitors: monitors,
            windowToMonitorMap: new Dictionary<long, string>
            {
                [attachedWindow.Hwnd] = PrimaryMonitorId,
                [explicitWindow.Hwnd] = SecondaryMonitorId,
            });
        ResolvingCaptureService captureService = new(monitorManager);
        WindowTools tools = CreateTools([attachedWindow, explicitWindow], captureService, attachedWindow, monitorManager);

        CallToolResult result = await tools.Capture(scope: "desktop", hwnd: explicitWindow.Hwnd);

        JsonElement payload = AssertSuccessfulPayload(result);
        AssertCaptureTarget(captureService.LastTarget, CaptureScope.Desktop, explicitWindow.Hwnd);
        AssertDesktopMonitorPayload(payload);
        Assert.Equal(explicitWindow.Hwnd, payload.GetProperty("hwnd").GetInt64());
        Assert.Equal(SecondaryMonitorId, payload.GetProperty("monitorId").GetString());
        Assert.Equal(SecondaryMonitorName, payload.GetProperty("monitorFriendlyName").GetString());
    }

    [Fact]
    public async Task CaptureRejectsMonitorIdForWindowScope()
    {
        WindowDescriptor window = CreateWindow();
        WindowTools tools = CreateTools([window], new FakeCaptureService(CreateCaptureResult(window, "window")));

        CallToolResult result = await tools.Capture(scope: "window", monitorId: PrimaryMonitorId);

        AssertToolError(result, "только для desktop capture");
    }

    [Fact]
    public async Task CaptureRejectsConflictingDesktopTargets()
    {
        WindowDescriptor window = CreateWindow(hwnd: 409, title: "Conflict");
        WindowTools tools = CreateTools(
            [window],
            new FakeCaptureService(CreateCaptureResult(window: null, scope: "desktop", targetKind: "monitor")));

        CallToolResult result = await tools.Capture(scope: "desktop", hwnd: window.Hwnd, monitorId: PrimaryMonitorId);

        AssertToolError(result, "одновременно передавать hwnd и monitorId");
    }

    [Fact]
    public async Task CaptureFallsBackToPrimaryMonitorWhenAttachedDesktopWindowIsStale()
    {
        WindowDescriptor staleAttachedWindow = CreateWindow(hwnd: 405, title: "Stale");
        FakeCaptureService captureService = new(CreateCaptureResult(window: null, scope: "desktop", targetKind: "monitor"));
        WindowTools tools = CreateTools([], captureService, staleAttachedWindow);

        CallToolResult result = await tools.Capture(scope: "desktop");

        JsonElement payload = AssertSuccessfulPayload(result);
        AssertCaptureTarget(captureService.LastTarget, CaptureScope.Desktop, hwnd: null);
        AssertDesktopMonitorPayload(payload);
        AssertMissingProperties(payload, "hwnd");
    }

    [Fact]
    public async Task CaptureFallsBackToPrimaryMonitorWhenAttachedDesktopHwndIsReusedInsideSameProcess()
    {
        WindowDescriptor attachedWindow = CreateWindow(hwnd: 406, title: "Original", processId: 123, threadId: 900, className: "MainWindow");
        WindowDescriptor reusedLiveWindow = CreateWindow(hwnd: 406, title: "Different", processId: 123, threadId: 901, className: "MainWindow");
        FakeCaptureService captureService = new(CreateCaptureResult(window: null, scope: "desktop", targetKind: "monitor"));
        WindowTools tools = CreateTools([reusedLiveWindow], captureService, attachedWindow);

        CallToolResult result = await tools.Capture(scope: "desktop");

        JsonElement payload = AssertSuccessfulPayload(result);
        AssertCaptureTarget(captureService.LastTarget, CaptureScope.Desktop, hwnd: null);
        AssertDesktopMonitorPayload(payload);
        AssertMissingProperties(payload, "hwnd");
    }

    [Fact]
    public async Task CaptureKeepsAttachedWindowWhenOnlyTitleChanges()
    {
        WindowDescriptor attachedWindow = CreateWindow(hwnd: 407, title: "Original");
        WindowDescriptor renamedLiveWindow = attachedWindow with { Title = "Renamed" };
        FakeCaptureService captureService = new(CreateCaptureResult(renamedLiveWindow, "window"));
        WindowTools tools = CreateTools([renamedLiveWindow], captureService, attachedWindow);

        CallToolResult result = await tools.Capture(scope: "window");

        Assert.False(result.IsError);
        AssertCaptureTarget(captureService.LastTarget, CaptureScope.Window, attachedWindow.Hwnd);
    }

    [Fact]
    public async Task CaptureKeepsAttachedDesktopWindowWhenOnlyTitleChanges()
    {
        WindowDescriptor attachedWindow = CreateWindow(hwnd: 408, title: "Original");
        WindowDescriptor renamedLiveWindow = attachedWindow with { Title = "Renamed" };
        FakeCaptureService captureService = new(CreateCaptureResult(renamedLiveWindow, "desktop", "monitor"));
        WindowTools tools = CreateTools([renamedLiveWindow], captureService, attachedWindow);

        CallToolResult result = await tools.Capture(scope: "desktop");

        JsonElement payload = AssertSuccessfulPayload(result);
        AssertCaptureTarget(captureService.LastTarget, CaptureScope.Desktop, attachedWindow.Hwnd);
        AssertDesktopMonitorPayload(payload);
        Assert.Equal(attachedWindow.Hwnd, payload.GetProperty("hwnd").GetInt64());
    }

    [Fact]
    public async Task CaptureReturnsToolErrorWhenWindowTargetIsMissing()
    {
        FakeCaptureService captureService = new(CreateCaptureResult(CreateWindow(), "window"));
        WindowTools tools = CreateTools([], captureService);

        CallToolResult result = await tools.Capture();

        Assert.Null(captureService.LastTarget);
        AssertToolError(result, "сначала прикрепить окно", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CaptureReturnsStructuredJsonTextAndImageBlocksOnSuccess()
    {
        WindowDescriptor window = CreateWindow(hwnd: 505, title: "Captured");
        byte[] pngBytes = [0, 1, 2, 255];
        Bounds captureBounds = new(10, 20, 210, 220);
        Bounds frameBounds = new(10, 20, 226, 232);
        FakeCaptureService captureService = new(CreateCaptureResult(
            window with { Bounds = captureBounds },
            "window",
            pngBytes: pngBytes,
            frameBounds: frameBounds));
        WindowTools tools = CreateTools([window], captureService, window);

        CallToolResult result = await tools.Capture();

        JsonElement payload = AssertSuccessfulPayload(result);
        Assert.Equal(2, result.Content.Count);

        TextContentBlock textBlock = Assert.IsType<TextContentBlock>(result.Content[0]);
        ImageContentBlock imageBlock = Assert.IsType<ImageContentBlock>(result.Content[1]);

        Assert.Contains("\"scope\":\"window\"", textBlock.Text, StringComparison.Ordinal);
        Assert.Contains("\"frameBounds\":", textBlock.Text, StringComparison.Ordinal);
        Assert.Contains("\"captureReference\":", textBlock.Text, StringComparison.Ordinal);
        Assert.Equal("image/png", imageBlock.MimeType);
        Assert.Equal(Convert.ToBase64String(pngBytes), Encoding.ASCII.GetString(imageBlock.Data.Span));

        AssertBounds(payload, "bounds", captureBounds);
        AssertBounds(payload, "frameBounds", frameBounds);

        JsonElement captureReference = payload.GetProperty("captureReference");
        AssertInputCompatibleBoundsWireShape(captureReference.GetProperty("bounds"));
        AssertInputCompatibleBoundsWireShape(captureReference.GetProperty("frameBounds"));
        JsonElement targetIdentity = captureReference.GetProperty("targetIdentity");
        Assert.Equal(window.Hwnd, targetIdentity.GetProperty("hwnd").GetInt64());
        Assert.Equal(window.ProcessId, targetIdentity.GetProperty("processId").GetInt32());
        Assert.Equal(window.ThreadId, targetIdentity.GetProperty("threadId").GetInt32());
        Assert.Equal(window.ClassName, targetIdentity.GetProperty("className").GetString());

        string inputJson = $$"""
            {
              "hwnd": 505,
              "confirm": true,
              "actions": [
                {
                  "type": "click",
                  "coordinateSpace": "capture_pixels",
                  "point": { "x": 1, "y": 1 },
                  "captureReference": {{captureReference.GetRawText()}}
                }
              ]
            }
            """;
        InputRequest inputRequest = JsonSerializer.Deserialize<InputRequest>(inputJson)
            ?? throw new InvalidOperationException("Input request did not deserialize.");
        Assert.True(
            InputRequestValidator.TryValidateStructure(inputRequest, out _, out string? reason),
            reason);
    }

    private static JsonElement AssertSuccessfulPayload(CallToolResult result)
    {
        Assert.False(result.IsError);
        return AssertStructuredPayload(result);
    }

    private static JsonElement AssertToolError(
        CallToolResult result,
        string reasonContains,
        StringComparison comparison = StringComparison.Ordinal)
    {
        Assert.True(result.IsError);
        JsonElement payload = AssertStructuredPayload(result);
        Assert.Equal("failed", payload.GetProperty("status").GetString());
        Assert.Contains(reasonContains, payload.GetProperty("reason").GetString(), comparison);
        return payload;
    }

    private static JsonElement AssertStructuredPayload(CallToolResult result)
    {
        Assert.NotNull(result.StructuredContent);
        return result.StructuredContent!.Value;
    }

    private static void AssertCaptureTarget(CaptureTarget? target, CaptureScope scope, long? hwnd)
    {
        Assert.NotNull(target);
        Assert.Equal(scope, target!.Scope);
        Assert.Equal(hwnd, target.Window?.Hwnd);
    }

    private static void AssertDesktopMonitorPayload(JsonElement payload)
    {
        Assert.Equal("desktop", payload.GetProperty("scope").GetString());
        Assert.Equal("monitor", payload.GetProperty("targetKind").GetString());
    }

    private static void AssertBounds(JsonElement payload, string propertyName, Bounds expected)
    {
        JsonElement bounds = payload.GetProperty(propertyName);
        Assert.Equal(expected.Left, bounds.GetProperty("left").GetInt32());
        Assert.Equal(expected.Right, bounds.GetProperty("right").GetInt32());
        Assert.Equal(expected.Width, bounds.GetProperty("width").GetInt32());
        Assert.Equal(expected.Height, bounds.GetProperty("height").GetInt32());
    }

    private static void AssertMissingProperties(JsonElement payload, params string[] propertyNames)
    {
        foreach (string propertyName in propertyNames)
        {
            Assert.False(payload.TryGetProperty(propertyName, out _), $"Payload must not contain '{propertyName}'.");
        }
    }

    private static void AssertInputCompatibleBoundsWireShape(JsonElement bounds)
    {
        string[] propertyNames = bounds.EnumerateObject()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["bottom", "left", "right", "top"], propertyNames);
    }

    private static WindowTools CreateTools(
        IReadOnlyList<WindowDescriptor> windows,
        ICaptureService captureService,
        WindowDescriptor? attachedWindow = null,
        FakeMonitorManager? monitorManager = null)
    {
        string root = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        string diagnosticsRoot = Path.Combine(root, "artifacts", "diagnostics");
        string runDirectory = Path.Combine(diagnosticsRoot, CaptureRunId);
        AuditLogOptions options = new(
            ContentRootPath: root,
            EnvironmentName: "Tests",
            RunId: CaptureRunId,
            DiagnosticsRoot: diagnosticsRoot,
            RunDirectory: runDirectory,
            EventsPath: Path.Combine(runDirectory, "events.jsonl"),
            SummaryPath: Path.Combine(runDirectory, "summary.md"));
        TimeProvider timeProvider = TimeProvider.System;
        AuditLog auditLog = new(options, timeProvider);
        InMemorySessionManager sessionManager = new(timeProvider, new SessionContext(CaptureRunId));

        if (attachedWindow is not null)
        {
            sessionManager.Attach(attachedWindow, "hwnd");
        }

        FakeWindowManager windowManager = new(windows);
        WaitResultMaterializer waitResultMaterializer = new(auditLog, options, WaitOptions.Default);

        return new WindowTools(
            auditLog,
            sessionManager,
            windowManager,
            captureService,
            monitorManager ?? new FakeMonitorManager(),
            new FakeWindowActivationService(),
            new WindowTargetResolver(windowManager),
            new FakeUiAutomationService(),
            new FakeWaitService(),
            waitResultMaterializer,
            new FakeToolExecutionGate(),
            new FakeInputService(),
            new FakeProcessLaunchService(),
            new FakeOpenTargetService());
    }

    private static WindowDescriptor CreateWindow(
        long hwnd = 42,
        string title = "Window",
        string processName = "okno-tests",
        int processId = 123,
        int threadId = 456,
        string className = "OknoWindow") =>
        new(
            Hwnd: hwnd,
            Title: title,
            ProcessName: processName,
            ProcessId: processId,
            ThreadId: threadId,
            ClassName: className,
            Bounds: new Bounds(10, 20, 210, 220),
            IsForeground: true,
            IsVisible: true);

    private static WindowDescriptor CreateWindowWithoutStableIdentity(long hwnd, string title) =>
        CreateWindow(hwnd: hwnd, title: title) with
        {
            ProcessId = null,
            ThreadId = null,
            ClassName = null,
        };

    private static CaptureResult CreateCaptureResult(
        WindowDescriptor? window,
        string scope,
        string targetKind = "window",
        byte[]? pngBytes = null,
        string? monitorId = PrimaryMonitorId,
        string? monitorFriendlyName = PrimaryMonitorName,
        string? monitorGdiDeviceName = @"\\.\DISPLAY1",
        Bounds? frameBounds = null,
        bool includeCaptureReference = true)
    {
        byte[] imageBytes = pngBytes ?? [1, 2, 3];
        bool isWindowTarget = targetKind == "window";
        DateTimeOffset capturedAtUtc = DateTimeOffset.UtcNow;
        InputCaptureReference? captureReference = includeCaptureReference && isWindowTarget && window is not null
            ? new InputCaptureReference(
                ToInputBounds(window.Bounds),
                pixelWidth: 200,
                pixelHeight: 200,
                effectiveDpi: 96,
                capturedAtUtc: capturedAtUtc,
                frameBounds: frameBounds is null ? null : ToInputBounds(frameBounds),
                targetIdentity: new InputTargetIdentity(
                    window.Hwnd,
                    window.ProcessId ?? 123,
                    window.ThreadId ?? 456,
                    window.ClassName ?? "OknoWindow"))
            : null;

        CaptureMetadata metadata = new(
            Scope: scope,
            TargetKind: targetKind,
            Hwnd: window?.Hwnd,
            Title: window?.Title,
            ProcessName: window?.ProcessName,
            Bounds: window?.Bounds ?? new Bounds(0, 0, 1920, 1080),
            CoordinateSpace: CaptureCoordinateSpaceValues.PhysicalPixels,
            PixelWidth: 200,
            PixelHeight: 200,
            CapturedAtUtc: capturedAtUtc,
            ArtifactPath: @"C:\artifacts\capture.png",
            MimeType: "image/png",
            ByteSize: imageBytes.Length,
            SessionRunId: CaptureRunId,
            EffectiveDpi: isWindowTarget ? 96 : null,
            DpiScale: isWindowTarget ? 1.0 : null,
            MonitorId: monitorId,
            MonitorFriendlyName: monitorFriendlyName,
            MonitorGdiDeviceName: monitorGdiDeviceName,
            FrameBounds: frameBounds,
            CaptureReference: captureReference);

        return new CaptureResult(metadata, imageBytes);
    }

    private static InputBounds ToInputBounds(Bounds bounds) =>
        new(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);

    private sealed class FakeCaptureService(CaptureResult result) : ICaptureService
    {
        public CaptureTarget? LastTarget { get; private set; }

        public Task<CaptureResult> CaptureAsync(CaptureTarget target, CancellationToken cancellationToken)
        {
            LastTarget = target;
            return Task.FromResult(result);
        }
    }

    private sealed class ResolvingCaptureService(FakeMonitorManager monitorManager) : ICaptureService
    {
        public CaptureTarget? LastTarget { get; private set; }

        public Task<CaptureResult> CaptureAsync(CaptureTarget target, CancellationToken cancellationToken)
        {
            LastTarget = target;
            DisplayTopologySnapshot topology = monitorManager.GetTopologySnapshot();
            MonitorInfo? monitor = DesktopCaptureMonitorResolver.Resolve(target.Window, target.MonitorId, monitorManager, topology);
            if (monitor is null)
            {
                throw new InvalidOperationException("Desktop monitor resolution failed in test capture service.");
            }

            CaptureMetadata metadata = new(
                Scope: "desktop",
                TargetKind: "monitor",
                Hwnd: target.Window?.Hwnd,
                Title: target.Window?.Title,
                ProcessName: target.Window?.ProcessName,
                Bounds: monitor.Descriptor.Bounds,
                CoordinateSpace: CaptureCoordinateSpaceValues.PhysicalPixels,
                PixelWidth: monitor.Descriptor.Bounds.Width,
                PixelHeight: monitor.Descriptor.Bounds.Height,
                CapturedAtUtc: DateTimeOffset.UtcNow,
                ArtifactPath: @"C:\artifacts\desktop-capture.png",
                MimeType: "image/png",
                ByteSize: 3,
                SessionRunId: CaptureRunId,
                MonitorId: monitor.Descriptor.MonitorId,
                MonitorFriendlyName: monitor.Descriptor.FriendlyName,
                MonitorGdiDeviceName: monitor.Descriptor.GdiDeviceName);

            return Task.FromResult(new CaptureResult(metadata, [1, 2, 3]));
        }
    }

    private sealed class FakeWindowManager(IReadOnlyList<WindowDescriptor> windows) : IWindowManager
    {
        public IReadOnlyList<WindowDescriptor> ListWindows(bool includeInvisible = false) => windows;

        public WindowDescriptor? FindWindow(WindowSelector selector)
        {
            selector.Validate();
            return windows.FirstOrDefault(window => selector.Hwnd == window.Hwnd);
        }

        public bool TryFocus(long hwnd) => windows.Any(window => window.Hwnd == hwnd);
    }
}
