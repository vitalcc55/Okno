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
    [Fact]
    public async Task CapturePrefersExplicitHwndOverAttachedWindow()
    {
        WindowDescriptor attachedWindow = CreateWindow(hwnd: 101, title: "Attached");
        WindowDescriptor explicitWindow = CreateWindow(hwnd: 202, title: "Explicit");
        FakeCaptureService captureService = new(CreateCaptureResult(explicitWindow, "window"));
        WindowTools tools = CreateTools(
            windows: [attachedWindow, explicitWindow],
            captureService: captureService,
            attachedWindow: attachedWindow);

        CallToolResult result = await tools.Capture(hwnd: explicitWindow.Hwnd);

        Assert.False(result.IsError);
        Assert.Equal(explicitWindow.Hwnd, captureService.LastTarget?.Window?.Hwnd);

        JsonElement payload = AssertStructuredPayload(result);
        Assert.Equal("window", payload.GetProperty("scope").GetString());
        Assert.Equal(explicitWindow.Hwnd, payload.GetProperty("hwnd").GetInt64());
        Assert.Equal(CaptureCoordinateSpaceValues.PhysicalPixels, payload.GetProperty("coordinateSpace").GetString());
        Assert.Equal(96, payload.GetProperty("effectiveDpi").GetInt32());
    }

    [Fact]
    public async Task CaptureUsesExplicitHwndEvenWhenStableIdentitySignalsAreMissing()
    {
        WindowDescriptor explicitWindow = CreateWindow(hwnd: 202, title: "Weak explicit") with
        {
            ProcessId = null,
            ThreadId = null,
            ClassName = null,
        };
        FakeCaptureService captureService = new(CreateCaptureResult(explicitWindow, "window"));
        WindowTools tools = CreateTools(
            windows: [explicitWindow],
            captureService: captureService,
            attachedWindow: null);

        CallToolResult result = await tools.Capture(hwnd: explicitWindow.Hwnd);

        Assert.False(result.IsError);
        Assert.Equal(explicitWindow.Hwnd, captureService.LastTarget?.Window?.Hwnd);
    }

    [Fact]
    public async Task WindowCaptureKeepsObservePayloadWhenInputTargetIdentityIsUnavailable()
    {
        WindowDescriptor explicitWindow = CreateWindow(hwnd: 203, title: "Weak capture") with
        {
            ProcessId = null,
            ThreadId = null,
            ClassName = null,
        };
        FakeCaptureService captureService = new(CreateCaptureResult(explicitWindow, "window", includeCaptureReference: false));
        WindowTools tools = CreateTools(
            windows: [explicitWindow],
            captureService: captureService,
            attachedWindow: null);

        CallToolResult result = await tools.Capture(hwnd: explicitWindow.Hwnd);

        Assert.False(result.IsError);
        JsonElement payload = AssertStructuredPayload(result);
        Assert.Equal(explicitWindow.Hwnd, payload.GetProperty("hwnd").GetInt64());
        Assert.False(payload.TryGetProperty("captureReference", out _));
    }

    [Fact]
    public async Task CaptureUsesAttachedWindowWhenHwndIsMissing()
    {
        WindowDescriptor attachedWindow = CreateWindow(hwnd: 303, title: "Attached");
        FakeCaptureService captureService = new(CreateCaptureResult(attachedWindow, "window"));
        WindowTools tools = CreateTools(
            windows: [attachedWindow],
            captureService: captureService,
            attachedWindow: attachedWindow);

        CallToolResult result = await tools.Capture();

        Assert.False(result.IsError);
        Assert.Equal(attachedWindow.Hwnd, captureService.LastTarget?.Window?.Hwnd);
        Assert.Equal(CaptureScope.Window, captureService.LastTarget?.Scope);
    }

    [Fact]
    public async Task CaptureDoesNotFallbackToAttachedWindowForExplicitZeroHwnd()
    {
        WindowDescriptor attachedWindow = CreateWindow(hwnd: 303, title: "Attached");
        FakeCaptureService captureService = new(CreateCaptureResult(attachedWindow, "window"));
        WindowTools tools = CreateTools(
            windows: [attachedWindow],
            captureService: captureService,
            attachedWindow: attachedWindow);

        CallToolResult result = await tools.Capture(hwnd: 0);

        Assert.True(result.IsError);
        Assert.Null(captureService.LastTarget);

        JsonElement payload = AssertStructuredPayload(result);
        Assert.Equal("failed", payload.GetProperty("status").GetString());
        Assert.Contains("по указанному hwnd больше не найдено", payload.GetProperty("reason").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CaptureUsesAttachedWindowMonitorForDesktopScope()
    {
        WindowDescriptor attachedWindow = CreateWindow(hwnd: 404, title: "Attached");
        FakeCaptureService captureService = new(CreateCaptureResult(attachedWindow, "desktop", "monitor"));
        WindowTools tools = CreateTools(
            windows: [attachedWindow],
            captureService: captureService,
            attachedWindow: attachedWindow);

        CallToolResult result = await tools.Capture(scope: "desktop");

        Assert.False(result.IsError);
        Assert.Equal(CaptureScope.Desktop, captureService.LastTarget?.Scope);
        Assert.Equal(attachedWindow.Hwnd, captureService.LastTarget?.Window?.Hwnd);

        JsonElement payload = AssertStructuredPayload(result);
        Assert.Equal("desktop", payload.GetProperty("scope").GetString());
        Assert.Equal("monitor", payload.GetProperty("targetKind").GetString());
        Assert.Equal(CaptureCoordinateSpaceValues.PhysicalPixels, payload.GetProperty("coordinateSpace").GetString());
        Assert.False(payload.TryGetProperty("effectiveDpi", out _));
        Assert.False(payload.TryGetProperty("frameBounds", out _));
        Assert.False(payload.TryGetProperty("captureReference", out _));
    }

    [Fact]
    public async Task CaptureUsesExplicitMonitorIdForDesktopScope()
    {
        FakeCaptureService captureService = new(
            CreateCaptureResult(
                window: null,
                scope: "desktop",
                targetKind: "monitor",
                monitorId: "display-source:0000000100000000:2",
                monitorFriendlyName: "Secondary monitor"));
        WindowTools tools = CreateTools(
            windows: [],
            captureService: captureService,
            attachedWindow: null);

        CallToolResult result = await tools.Capture(scope: "desktop", monitorId: "display-source:0000000100000000:2");

        Assert.False(result.IsError);
        Assert.Equal(CaptureScope.Desktop, captureService.LastTarget?.Scope);
        Assert.Equal("display-source:0000000100000000:2", captureService.LastTarget?.MonitorId);

        JsonElement payload = AssertStructuredPayload(result);
        Assert.Equal("display-source:0000000100000000:2", payload.GetProperty("monitorId").GetString());
        Assert.Equal("Secondary monitor", payload.GetProperty("monitorFriendlyName").GetString());
        Assert.Equal(CaptureCoordinateSpaceValues.PhysicalPixels, payload.GetProperty("coordinateSpace").GetString());
    }

    [Fact]
    public async Task CaptureUsesExplicitHwndToResolveDesktopMonitorAndOverridesAttachedWindow()
    {
        WindowDescriptor attachedWindow = CreateWindow(hwnd: 410, title: "Attached");
        WindowDescriptor explicitWindow = CreateWindow(hwnd: 411, title: "Explicit");
        IReadOnlyList<MonitorInfo> monitors =
        [
            WindowToolTestData.CreateMonitor(
                monitorId: "display-source:0000000100000000:1",
                friendlyName: "Primary monitor",
                handle: 501),
            WindowToolTestData.CreateMonitor(
                monitorId: "display-source:0000000100000000:2",
                friendlyName: "Secondary monitor",
                isPrimary: false,
                handle: 502),
        ];
        FakeMonitorManager monitorManager = new(
            monitors: monitors,
            windowToMonitorMap: new Dictionary<long, string>
            {
                [attachedWindow.Hwnd] = "display-source:0000000100000000:1",
                [explicitWindow.Hwnd] = "display-source:0000000100000000:2",
            });
        ResolvingCaptureService captureService = new(monitorManager);
        WindowTools tools = CreateTools(
            windows: [attachedWindow, explicitWindow],
            captureService: captureService,
            attachedWindow: attachedWindow,
            monitorManager: monitorManager);

        CallToolResult result = await tools.Capture(scope: "desktop", hwnd: explicitWindow.Hwnd);

        Assert.False(result.IsError);
        Assert.Equal(CaptureScope.Desktop, captureService.LastTarget?.Scope);
        Assert.Equal(explicitWindow.Hwnd, captureService.LastTarget?.Window?.Hwnd);

        JsonElement payload = AssertStructuredPayload(result);
        Assert.Equal("desktop", payload.GetProperty("scope").GetString());
        Assert.Equal("monitor", payload.GetProperty("targetKind").GetString());
        Assert.Equal(explicitWindow.Hwnd, payload.GetProperty("hwnd").GetInt64());
        Assert.Equal("display-source:0000000100000000:2", payload.GetProperty("monitorId").GetString());
        Assert.Equal("Secondary monitor", payload.GetProperty("monitorFriendlyName").GetString());
    }

    [Fact]
    public async Task CaptureRejectsMonitorIdForWindowScope()
    {
        WindowTools tools = CreateTools(
            windows: [CreateWindow()],
            captureService: new FakeCaptureService(CreateCaptureResult(CreateWindow(), "window")),
            attachedWindow: null);

        CallToolResult result = await tools.Capture(scope: "window", monitorId: "display-source:0000000100000000:1");

        Assert.True(result.IsError);
        JsonElement payload = AssertStructuredPayload(result);
        Assert.Equal("failed", payload.GetProperty("status").GetString());
        Assert.Contains("только для desktop capture", payload.GetProperty("reason").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CaptureRejectsConflictingDesktopTargets()
    {
        WindowDescriptor window = CreateWindow(hwnd: 409, title: "Conflict");
        WindowTools tools = CreateTools(
            windows: [window],
            captureService: new FakeCaptureService(CreateCaptureResult(window: null, scope: "desktop", targetKind: "monitor")),
            attachedWindow: null);

        CallToolResult result = await tools.Capture(
            scope: "desktop",
            hwnd: window.Hwnd,
            monitorId: "display-source:0000000100000000:1");

        Assert.True(result.IsError);
        JsonElement payload = AssertStructuredPayload(result);
        Assert.Equal("failed", payload.GetProperty("status").GetString());
        Assert.Contains("одновременно передавать hwnd и monitorId", payload.GetProperty("reason").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CaptureFallsBackToPrimaryMonitorWhenAttachedDesktopWindowIsStale()
    {
        WindowDescriptor staleAttachedWindow = CreateWindow(hwnd: 405, title: "Stale");
        FakeCaptureService captureService = new(CreateCaptureResult(window: null, scope: "desktop", targetKind: "monitor"));
        WindowTools tools = CreateTools(
            windows: [],
            captureService: captureService,
            attachedWindow: staleAttachedWindow);

        CallToolResult result = await tools.Capture(scope: "desktop");

        Assert.False(result.IsError);
        Assert.Equal(CaptureScope.Desktop, captureService.LastTarget?.Scope);
        Assert.Null(captureService.LastTarget?.Window);

        JsonElement payload = AssertStructuredPayload(result);
        Assert.Equal("desktop", payload.GetProperty("scope").GetString());
        Assert.Equal("monitor", payload.GetProperty("targetKind").GetString());
        Assert.False(payload.TryGetProperty("hwnd", out _));
    }

    [Fact]
    public async Task CaptureFallsBackToPrimaryMonitorWhenAttachedDesktopHwndIsReusedInsideSameProcess()
    {
        WindowDescriptor attachedWindow = CreateWindow(hwnd: 406, title: "Original", processId: 123, threadId: 900, className: "MainWindow");
        WindowDescriptor reusedLiveWindow = CreateWindow(hwnd: 406, title: "Different", processId: 123, threadId: 901, className: "MainWindow");
        FakeCaptureService captureService = new(CreateCaptureResult(window: null, scope: "desktop", targetKind: "monitor"));
        WindowTools tools = CreateTools(
            windows: [reusedLiveWindow],
            captureService: captureService,
            attachedWindow: attachedWindow);

        CallToolResult result = await tools.Capture(scope: "desktop");

        Assert.False(result.IsError);
        Assert.Equal(CaptureScope.Desktop, captureService.LastTarget?.Scope);
        Assert.Null(captureService.LastTarget?.Window);

        JsonElement payload = AssertStructuredPayload(result);
        Assert.Equal("desktop", payload.GetProperty("scope").GetString());
        Assert.Equal("monitor", payload.GetProperty("targetKind").GetString());
        Assert.False(payload.TryGetProperty("hwnd", out _));
    }

    [Fact]
    public async Task CaptureKeepsAttachedWindowWhenOnlyTitleChanges()
    {
        WindowDescriptor attachedWindow = CreateWindow(hwnd: 407, title: "Original");
        WindowDescriptor renamedLiveWindow = attachedWindow with { Title = "Renamed" };
        FakeCaptureService captureService = new(CreateCaptureResult(renamedLiveWindow, "window"));
        WindowTools tools = CreateTools(
            windows: [renamedLiveWindow],
            captureService: captureService,
            attachedWindow: attachedWindow);

        CallToolResult result = await tools.Capture(scope: "window");

        Assert.False(result.IsError);
        Assert.Equal(CaptureScope.Window, captureService.LastTarget?.Scope);
        Assert.Equal(attachedWindow.Hwnd, captureService.LastTarget?.Window?.Hwnd);
    }

    [Fact]
    public async Task CaptureKeepsAttachedDesktopWindowWhenOnlyTitleChanges()
    {
        WindowDescriptor attachedWindow = CreateWindow(hwnd: 408, title: "Original");
        WindowDescriptor renamedLiveWindow = attachedWindow with { Title = "Renamed" };
        FakeCaptureService captureService = new(CreateCaptureResult(renamedLiveWindow, "desktop", "monitor"));
        WindowTools tools = CreateTools(
            windows: [renamedLiveWindow],
            captureService: captureService,
            attachedWindow: attachedWindow);

        CallToolResult result = await tools.Capture(scope: "desktop");

        Assert.False(result.IsError);
        Assert.Equal(CaptureScope.Desktop, captureService.LastTarget?.Scope);
        Assert.Equal(attachedWindow.Hwnd, captureService.LastTarget?.Window?.Hwnd);

        JsonElement payload = AssertStructuredPayload(result);
        Assert.Equal("desktop", payload.GetProperty("scope").GetString());
        Assert.Equal("monitor", payload.GetProperty("targetKind").GetString());
        Assert.Equal(attachedWindow.Hwnd, payload.GetProperty("hwnd").GetInt64());
    }

    [Fact]
    public async Task CaptureReturnsToolErrorWhenWindowTargetIsMissing()
    {
        FakeCaptureService captureService = new(CreateCaptureResult(CreateWindow(), "window"));
        WindowTools tools = CreateTools(
            windows: [],
            captureService: captureService,
            attachedWindow: null);

        CallToolResult result = await tools.Capture();

        Assert.True(result.IsError);
        Assert.Null(captureService.LastTarget);

        JsonElement payload = AssertStructuredPayload(result);
        Assert.Equal("failed", payload.GetProperty("status").GetString());
        Assert.Contains("сначала прикрепить окно", payload.GetProperty("reason").GetString(), StringComparison.OrdinalIgnoreCase);
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
        WindowTools tools = CreateTools(
            windows: [window],
            captureService: captureService,
            attachedWindow: window);

        CallToolResult result = await tools.Capture();

        Assert.False(result.IsError);
        Assert.NotNull(result.StructuredContent);
        Assert.Equal(2, result.Content.Count);

        TextContentBlock textBlock = Assert.IsType<TextContentBlock>(result.Content[0]);
        ImageContentBlock imageBlock = Assert.IsType<ImageContentBlock>(result.Content[1]);

        Assert.Contains("\"scope\":\"window\"", textBlock.Text, StringComparison.Ordinal);
        Assert.Contains("\"frameBounds\":", textBlock.Text, StringComparison.Ordinal);
        Assert.Contains("\"captureReference\":", textBlock.Text, StringComparison.Ordinal);
        Assert.Equal("image/png", imageBlock.MimeType);
        Assert.Equal(Convert.ToBase64String(pngBytes), Encoding.ASCII.GetString(imageBlock.Data.Span));

        JsonElement payload = AssertStructuredPayload(result);
        Assert.Equal(captureBounds.Left, payload.GetProperty("bounds").GetProperty("left").GetInt32());
        Assert.Equal(captureBounds.Right, payload.GetProperty("bounds").GetProperty("right").GetInt32());
        Assert.Equal(captureBounds.Width, payload.GetProperty("bounds").GetProperty("width").GetInt32());
        Assert.Equal(captureBounds.Height, payload.GetProperty("bounds").GetProperty("height").GetInt32());
        Assert.Equal(frameBounds.Left, payload.GetProperty("frameBounds").GetProperty("left").GetInt32());
        Assert.Equal(frameBounds.Right, payload.GetProperty("frameBounds").GetProperty("right").GetInt32());
        Assert.Equal(frameBounds.Width, payload.GetProperty("frameBounds").GetProperty("width").GetInt32());
        Assert.Equal(frameBounds.Height, payload.GetProperty("frameBounds").GetProperty("height").GetInt32());
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

    private static JsonElement AssertStructuredPayload(CallToolResult result)
    {
        Assert.NotNull(result.StructuredContent);
        return result.StructuredContent!.Value;
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
        WindowDescriptor? attachedWindow,
        FakeMonitorManager? monitorManager = null)
    {
        string root = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        AuditLogOptions options = new(
            ContentRootPath: root,
            EnvironmentName: "Tests",
            RunId: "capture-tests",
            DiagnosticsRoot: Path.Combine(root, "artifacts", "diagnostics"),
            RunDirectory: Path.Combine(root, "artifacts", "diagnostics", "capture-tests"),
            EventsPath: Path.Combine(root, "artifacts", "diagnostics", "capture-tests", "events.jsonl"),
            SummaryPath: Path.Combine(root, "artifacts", "diagnostics", "capture-tests", "summary.md"));
        AuditLog auditLog = new(options, TimeProvider.System);
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("capture-tests"));

        if (attachedWindow is not null)
        {
            sessionManager.Attach(attachedWindow, "hwnd");
        }

        FakeMonitorManager effectiveMonitorManager = monitorManager ?? new FakeMonitorManager();
        WaitResultMaterializer waitResultMaterializer = new(auditLog, options, WaitOptions.Default);

        return new WindowTools(
            auditLog,
            sessionManager,
            new FakeWindowManager(windows),
            captureService,
            effectiveMonitorManager,
            new FakeWindowActivationService(),
            new WindowTargetResolver(new FakeWindowManager(windows)),
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

    private static CaptureResult CreateCaptureResult(
        WindowDescriptor? window,
        string scope,
        string targetKind = "window",
        byte[]? pngBytes = null,
        string? monitorId = "display-source:0000000100000000:1",
        string? monitorFriendlyName = "Primary monitor",
        string? monitorGdiDeviceName = @"\\.\DISPLAY1",
        Bounds? frameBounds = null,
        bool includeCaptureReference = true)
    {
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
            CapturedAtUtc: DateTimeOffset.UtcNow,
            ArtifactPath: @"C:\artifacts\capture.png",
            MimeType: "image/png",
            ByteSize: (pngBytes ?? [1, 2, 3]).Length,
            SessionRunId: "capture-tests",
            EffectiveDpi: targetKind == "window" ? 96 : null,
            DpiScale: targetKind == "window" ? 1.0 : null,
            MonitorId: monitorId,
            MonitorFriendlyName: monitorFriendlyName,
            MonitorGdiDeviceName: monitorGdiDeviceName,
            FrameBounds: frameBounds,
            CaptureReference: includeCaptureReference && targetKind == "window" && window is not null
                ? new InputCaptureReference(
                    new InputBounds(window.Bounds.Left, window.Bounds.Top, window.Bounds.Right, window.Bounds.Bottom),
                    pixelWidth: 200,
                    pixelHeight: 200,
                    effectiveDpi: 96,
                    capturedAtUtc: DateTimeOffset.UtcNow,
                    frameBounds: frameBounds is null
                        ? null
                        : new InputBounds(frameBounds.Left, frameBounds.Top, frameBounds.Right, frameBounds.Bottom),
                    targetIdentity: new InputTargetIdentity(
                        window.Hwnd,
                        window.ProcessId ?? 123,
                        window.ThreadId ?? 456,
                        window.ClassName ?? "OknoWindow"))
                : null);

        return new CaptureResult(metadata, pngBytes ?? [1, 2, 3]);
    }

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
                SessionRunId: "capture-tests",
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
