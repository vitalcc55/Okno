// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;
using ModelContextProtocol.Protocol;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Guards;
using WinBridge.Runtime.Session;
using WinBridge.Runtime.Tooling;
using WinBridge.Runtime.Waiting;
using WinBridge.Runtime.Windows.Capture;
using WinBridge.Runtime.Windows.Launch;
using WinBridge.Runtime.Windows.Shell;
using WinBridge.Server.Tools;

namespace WinBridge.Server.IntegrationTests;

public sealed class WindowLaunchProcessToolTests
{
    [Fact]
    public async Task LaunchProcessReturnsBlockedPayloadWithoutInvokingRuntimeService()
    {
        TestContext context = CreateContext(
            decision: CreateDecision(
                ToolExecutionDecisionKind.Blocked,
                ToolExecutionMode.Live,
                GuardReasonCodeValues.CapabilityNotImplemented,
                GuardSeverityValues.Blocked));

        CallToolResult result = await context.Tools.LaunchProcess(new LaunchProcessRequest
        {
            Executable = "notepad.exe",
        });

        JsonElement payload = AssertStructuredPayload(result);
        Assert.True(result.IsError);
        Assert.Equal(LaunchProcessStatusValues.Blocked, payload.GetProperty("status").GetString());
        Assert.Equal(LaunchProcessStatusValues.Blocked, payload.GetProperty("decision").GetString());
        Assert.Equal(0, context.LaunchService.Calls);
        Assert.Equal(1, context.Gate.Calls);
        Assert.True(payload.TryGetProperty("preview", out _));
    }

    [Fact]
    public async Task LaunchProcessReturnsNeedsConfirmationPayloadWithoutInvokingRuntimeService()
    {
        TestContext context = CreateContext(
            decision: CreateDecision(
                ToolExecutionDecisionKind.NeedsConfirmation,
                ToolExecutionMode.Live,
                GuardReasonCodeValues.LaunchElevationBoundaryUnconfirmed,
                GuardSeverityValues.Warning,
                requiresConfirmation: true));

        CallToolResult result = await context.Tools.LaunchProcess(new LaunchProcessRequest
        {
            Executable = "notepad.exe",
        });

        JsonElement payload = AssertStructuredPayload(result);
        Assert.True(result.IsError);
        Assert.Equal(LaunchProcessStatusValues.NeedsConfirmation, payload.GetProperty("status").GetString());
        Assert.Equal(LaunchProcessStatusValues.NeedsConfirmation, payload.GetProperty("decision").GetString());
        Assert.True(payload.GetProperty("requiresConfirmation").GetBoolean());
        Assert.Equal(0, context.LaunchService.Calls);
    }

    [Fact]
    public async Task LaunchProcessReturnsDryRunOnlyPayloadWithoutInvokingRuntimeService()
    {
        TestContext context = CreateContext(
            decision: CreateDecision(
                ToolExecutionDecisionKind.DryRunOnly,
                ToolExecutionMode.DryRun,
                GuardReasonCodeValues.CapabilityDryRunPreviewUnavailable,
                GuardSeverityValues.Blocked));

        CallToolResult result = await context.Tools.LaunchProcess(new LaunchProcessRequest
        {
            Executable = "notepad.exe",
        });

        JsonElement payload = AssertStructuredPayload(result);
        Assert.True(result.IsError);
        Assert.Equal(LaunchProcessStatusValues.DryRunOnly, payload.GetProperty("status").GetString());
        Assert.Equal(LaunchProcessStatusValues.DryRunOnly, payload.GetProperty("decision").GetString());
        Assert.Equal(0, context.LaunchService.Calls);
    }

    [Fact]
    public async Task LaunchProcessAllowedDryRunReturnsPreviewWithoutInvokingRuntimeService()
    {
        TestContext context = CreateContext(
            decision: CreateDecision(
                ToolExecutionDecisionKind.Allowed,
                ToolExecutionMode.DryRun,
                GuardReasonCodeValues.LaunchElevationBoundaryUnconfirmed,
                GuardSeverityValues.Warning));

        CallToolResult result = await context.Tools.LaunchProcess(new LaunchProcessRequest
        {
            Executable = @"C:\Tools\Demo.exe",
            Args = ["--flag"],
            WorkingDirectory = @"C:\Tools",
            WaitForWindow = true,
            TimeoutMs = 4000,
            DryRun = true,
        });

        JsonElement payload = AssertStructuredPayload(result);
        Assert.False(result.IsError);
        Assert.Equal(LaunchProcessStatusValues.Done, payload.GetProperty("status").GetString());
        Assert.Equal(LaunchProcessStatusValues.Done, payload.GetProperty("decision").GetString());
        Assert.Equal("Demo.exe", payload.GetProperty("executableIdentity").GetString());
        Assert.True(payload.TryGetProperty("preview", out JsonElement preview));
        Assert.Equal("Demo.exe", preview.GetProperty("executableIdentity").GetString());
        Assert.Equal(1, preview.GetProperty("argumentCount").GetInt32());
        Assert.False(payload.TryGetProperty("processId", out _));
        Assert.False(payload.TryGetProperty("startedAtUtc", out _));
        Assert.False(payload.TryGetProperty("hasExited", out _));
        Assert.False(payload.TryGetProperty("exitCode", out _));
        Assert.False(payload.TryGetProperty("mainWindowHandle", out _));
        Assert.False(payload.TryGetProperty("resultMode", out _));
        Assert.False(payload.TryGetProperty("artifactPath", out _));
        Assert.Equal(0, context.LaunchService.Calls);

        string[] eventLines = File.ReadAllLines(context.AuditOptions.EventsPath);
        Assert.Equal(3, eventLines.Length);
        Assert.Contains("\"event_name\":\"launch.preview.completed\"", eventLines[1], StringComparison.Ordinal);
        Assert.Contains("\"tool_name\":\"windows.launch_process\"", eventLines[1], StringComparison.Ordinal);
        Assert.DoesNotContain(eventLines, line => line.Contains("\"event_name\":\"launch.runtime.completed\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LaunchProcessAllowedDryRunReturnsPreviewWhenObservabilityWritesFailAfterDecision()
    {
        AuditLogOptions? auditOptions = null;
        TestContext context = CreateContext(
            decision: CreateDecision(
                ToolExecutionDecisionKind.Allowed,
                ToolExecutionMode.DryRun,
                GuardReasonCodeValues.LaunchElevationBoundaryUnconfirmed,
                GuardSeverityValues.Warning),
            onGateEvaluate: () =>
            {
                Assert.NotNull(auditOptions);
                File.Delete(auditOptions!.EventsPath);
                Directory.CreateDirectory(auditOptions.EventsPath);
            });
        auditOptions = context.AuditOptions;

        CallToolResult result = await context.Tools.LaunchProcess(new LaunchProcessRequest
        {
            Executable = @"C:\Tools\Demo.exe",
            Args = ["--flag"],
            WorkingDirectory = @"C:\Tools",
            WaitForWindow = true,
            TimeoutMs = 4000,
            DryRun = true,
        });

        JsonElement payload = AssertStructuredPayload(result);
        Assert.False(result.IsError);
        Assert.Equal(LaunchProcessStatusValues.Done, payload.GetProperty("status").GetString());
        Assert.Equal(LaunchProcessStatusValues.Done, payload.GetProperty("decision").GetString());
        Assert.Equal(0, context.LaunchService.Calls);
        Assert.True(Directory.Exists(context.AuditOptions.EventsPath));
    }

    [Fact]
    public async Task LaunchProcessAllowedLiveReturnsRuntimePayload()
    {
        FakeProcessLaunchService launchService = new(
            (_, _) => Task.FromResult(
                new LaunchProcessResult(
                    Status: LaunchProcessStatusValues.Done,
                    Decision: LaunchProcessStatusValues.Done,
                    ResultMode: LaunchProcessResultModeValues.ProcessStarted,
                    ExecutableIdentity: "notepad.exe",
                    ProcessId: 4242,
                    StartedAtUtc: new DateTimeOffset(2026, 4, 6, 10, 0, 0, TimeSpan.Zero),
                    HasExited: false,
                    MainWindowObserved: false,
                    MainWindowObservationStatus: LaunchMainWindowObservationStatusValues.NotRequested)));
        TestContext context = CreateContext(
            decision: CreateDecision(
                ToolExecutionDecisionKind.Allowed,
                ToolExecutionMode.Live,
                GuardReasonCodeValues.LaunchElevationBoundaryUnconfirmed,
                GuardSeverityValues.Warning),
            launchService: launchService);

        CallToolResult result = await context.Tools.LaunchProcess(new LaunchProcessRequest
        {
            Executable = "notepad.exe",
            Confirm = true,
        });

        JsonElement payload = AssertStructuredPayload(result);
        Assert.False(result.IsError);
        Assert.Equal(LaunchProcessStatusValues.Done, payload.GetProperty("status").GetString());
        Assert.Equal(LaunchProcessStatusValues.Done, payload.GetProperty("decision").GetString());
        Assert.Equal(4242, payload.GetProperty("processId").GetInt32());
        Assert.Equal(1, context.LaunchService.Calls);
        Assert.Equal("notepad.exe", context.LaunchService.LastRequest?.Executable);
        Assert.True(context.Gate.LastIntent?.ConfirmationGranted);
    }

    [Fact]
    public async Task LaunchProcessAllowedLiveIncludesArtifactPathInPayloadAndCompletionAudit()
    {
        const string artifactPath = @"C:\artifacts\diagnostics\launch\launch-20260406T140000000-test.json";
        FakeProcessLaunchService launchService = new(
            (_, _) => Task.FromResult(
                new LaunchProcessResult(
                    Status: LaunchProcessStatusValues.Done,
                    Decision: LaunchProcessStatusValues.Done,
                    ResultMode: LaunchProcessResultModeValues.ProcessStarted,
                    ExecutableIdentity: "notepad.exe",
                    ProcessId: 4243,
                    StartedAtUtc: new DateTimeOffset(2026, 4, 6, 10, 5, 0, TimeSpan.Zero),
                    HasExited: false,
                    MainWindowObserved: false,
                    MainWindowObservationStatus: LaunchMainWindowObservationStatusValues.NotRequested,
                    ArtifactPath: artifactPath)));
        TestContext context = CreateContext(
            decision: CreateDecision(
                ToolExecutionDecisionKind.Allowed,
                ToolExecutionMode.Live,
                GuardReasonCodeValues.LaunchElevationBoundaryUnconfirmed,
                GuardSeverityValues.Warning),
            launchService: launchService);

        CallToolResult result = await context.Tools.LaunchProcess(new LaunchProcessRequest
        {
            Executable = "notepad.exe",
            Confirm = true,
        });

        JsonElement payload = AssertStructuredPayload(result);
        Assert.Equal(artifactPath, payload.GetProperty("artifactPath").GetString());

        string[] eventLines = File.ReadAllLines(context.AuditOptions.EventsPath);
        Assert.Equal(2, eventLines.Length);
        Assert.Contains("\"decision\":\"done\"", eventLines[1], StringComparison.Ordinal);
        Assert.Contains("\"gate_decision\":\"allowed\"", eventLines[1], StringComparison.Ordinal);
        Assert.Contains("\"artifact_path\":\"C:\\\\artifacts\\\\diagnostics\\\\launch\\\\launch-20260406T140000000-test.json\"", eventLines[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task LaunchProcessAllowedLiveReturnsFactualPayloadWhenCompletionAuditWriteFails()
    {
        AuditLogOptions? auditOptions = null;
        FakeProcessLaunchService launchService = new(
            (_, _) =>
            {
                Assert.NotNull(auditOptions);
                File.Delete(auditOptions!.EventsPath);
                Directory.CreateDirectory(auditOptions.EventsPath);

                return Task.FromResult(
                    new LaunchProcessResult(
                        Status: LaunchProcessStatusValues.Done,
                        Decision: LaunchProcessStatusValues.Done,
                        ResultMode: LaunchProcessResultModeValues.ProcessStarted,
                        ExecutableIdentity: "notepad.exe",
                        ProcessId: 4244,
                        StartedAtUtc: new DateTimeOffset(2026, 4, 6, 10, 6, 0, TimeSpan.Zero),
                        HasExited: false,
                        MainWindowObserved: false,
                        MainWindowObservationStatus: LaunchMainWindowObservationStatusValues.NotRequested));
            });
        TestContext context = CreateContext(
            decision: CreateDecision(
                ToolExecutionDecisionKind.Allowed,
                ToolExecutionMode.Live,
                GuardReasonCodeValues.LaunchElevationBoundaryUnconfirmed,
                GuardSeverityValues.Warning),
            launchService: launchService);
        auditOptions = context.AuditOptions;

        CallToolResult result = await context.Tools.LaunchProcess(new LaunchProcessRequest
        {
            Executable = "notepad.exe",
            Confirm = true,
        });

        JsonElement payload = AssertStructuredPayload(result);
        Assert.False(result.IsError);
        Assert.Equal(LaunchProcessStatusValues.Done, payload.GetProperty("status").GetString());
        Assert.Equal(LaunchProcessStatusValues.Done, payload.GetProperty("decision").GetString());
        Assert.Equal(4244, payload.GetProperty("processId").GetInt32());
        Assert.True(Directory.Exists(context.AuditOptions.EventsPath));
    }

    [Fact]
    public async Task LaunchProcessAllowedLiveRuntimeFailureReturnsFailedDecision()
    {
        const string artifactPath = @"C:\artifacts\diagnostics\launch\launch-20260406T140500000-failed.json";
        FakeProcessLaunchService launchService = new(
            (_, _) => Task.FromResult(
                new LaunchProcessResult(
                    Status: LaunchProcessStatusValues.Failed,
                    Decision: LaunchProcessStatusValues.Failed,
                    FailureCode: LaunchProcessFailureCodeValues.StartFailed,
                    Reason: "Process.Start failed.",
                    ExecutableIdentity: "notepad.exe",
                    ArtifactPath: artifactPath)));
        TestContext context = CreateContext(
            decision: CreateDecision(
                ToolExecutionDecisionKind.Allowed,
                ToolExecutionMode.Live,
                GuardReasonCodeValues.LaunchElevationBoundaryUnconfirmed,
                GuardSeverityValues.Warning),
            launchService: launchService);

        CallToolResult result = await context.Tools.LaunchProcess(new LaunchProcessRequest
        {
            Executable = "notepad.exe",
            Confirm = true,
        });

        JsonElement payload = AssertStructuredPayload(result);
        Assert.True(result.IsError);
        Assert.Equal(LaunchProcessStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.Equal(LaunchProcessStatusValues.Failed, payload.GetProperty("decision").GetString());
        Assert.Equal(LaunchProcessFailureCodeValues.StartFailed, payload.GetProperty("failureCode").GetString());
        Assert.Equal(artifactPath, payload.GetProperty("artifactPath").GetString());
        Assert.Equal(1, context.LaunchService.Calls);

        string[] eventLines = File.ReadAllLines(context.AuditOptions.EventsPath);
        Assert.Equal(2, eventLines.Length);
        Assert.Contains("\"decision\":\"failed\"", eventLines[1], StringComparison.Ordinal);
        Assert.Contains("\"gate_decision\":\"allowed\"", eventLines[1], StringComparison.Ordinal);
        Assert.Contains("\"artifact_path\":\"C:\\\\artifacts\\\\diagnostics\\\\launch\\\\launch-20260406T140500000-failed.json\"", eventLines[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task LaunchProcessInvalidRequestReturnsFailedPayloadWithoutRuntimeInvocation()
    {
        TestContext context = CreateContext(
            decision: CreateDecision(
                ToolExecutionDecisionKind.Allowed,
                ToolExecutionMode.Live,
                GuardReasonCodeValues.LaunchElevationBoundaryUnconfirmed,
                GuardSeverityValues.Warning));

        CallToolResult result = await context.Tools.LaunchProcess(new LaunchProcessRequest
        {
            Executable = "https://example.test/demo.exe",
        });

        JsonElement payload = AssertStructuredPayload(result);
        Assert.True(result.IsError);
        Assert.Equal(LaunchProcessStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.Equal(LaunchProcessStatusValues.Failed, payload.GetProperty("decision").GetString());
        Assert.Equal(LaunchProcessFailureCodeValues.UnsupportedTargetKind, payload.GetProperty("failureCode").GetString());
        Assert.Equal(1, context.Gate.Calls);
        Assert.Equal(0, context.LaunchService.Calls);
        Assert.False(payload.TryGetProperty("preview", out _));

        string[] eventLines = File.ReadAllLines(context.AuditOptions.EventsPath);
        Assert.Equal(2, eventLines.Length);
        Assert.Contains("\"decision\":\"failed\"", eventLines[1], StringComparison.Ordinal);
        Assert.Contains("\"gate_decision\":\"allowed\"", eventLines[1], StringComparison.Ordinal);
    }

    private static TestContext CreateContext(
        ToolExecutionDecision decision,
        FakeProcessLaunchService? launchService = null,
        Action? onGateEvaluate = null)
    {
        string root = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        AuditLogOptions options = new(
            ContentRootPath: root,
            EnvironmentName: "Tests",
            RunId: "launch-process-tests",
            DiagnosticsRoot: Path.Combine(root, "artifacts", "diagnostics"),
            RunDirectory: Path.Combine(root, "artifacts", "diagnostics", "launch-process-tests"),
            EventsPath: Path.Combine(root, "artifacts", "diagnostics", "launch-process-tests", "events.jsonl"),
            SummaryPath: Path.Combine(root, "artifacts", "diagnostics", "launch-process-tests", "summary.md"));

        AuditLog auditLog = new(options, TimeProvider.System);
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("launch-process-tests"));
        FakeWindowManager windowManager = new([]);
        FakeToolExecutionGate gate = new(decision, onGateEvaluate);
        FakeProcessLaunchService effectiveLaunchService = launchService ?? new FakeProcessLaunchService();
        WaitResultMaterializer waitResultMaterializer = new(auditLog, options, WaitOptions.Default);

        return new TestContext(
            new WindowTools(
                auditLog,
                sessionManager,
                windowManager,
                new NoopCaptureService(),
                new FakeMonitorManager(),
                new FakeWindowActivationService(),
                new WindowTargetResolver(windowManager),
                new FakeUiAutomationService(),
                new FakeWaitService(),
                waitResultMaterializer,
                gate,
                new FakeInputService(),
                effectiveLaunchService,
                new FakeOpenTargetService()),
            gate,
            effectiveLaunchService,
            options);
    }

    private static ToolExecutionDecision CreateDecision(
        ToolExecutionDecisionKind kind,
        ToolExecutionMode mode,
        string reasonCode,
        string severity,
        bool requiresConfirmation = false) =>
        new(
            Kind: kind,
            Mode: mode,
            RiskLevel: ToolExecutionRiskLevel.High,
            Reasons:
            [
                new GuardReason(
                    reasonCode,
                    severity,
                    "Launch boundary test reason.",
                    CapabilitySummaryValues.Launch),
            ],
            RequiresConfirmation: requiresConfirmation,
            DryRunSupported: true,
            GuardCapability: CapabilitySummaryValues.Launch);

    private static JsonElement AssertStructuredPayload(CallToolResult result)
    {
        Assert.NotNull(result.StructuredContent);
        Assert.Single(result.Content);
        Assert.IsType<TextContentBlock>(result.Content[0]);
        return result.StructuredContent!.Value;
    }

    private sealed record TestContext(
        WindowTools Tools,
        FakeToolExecutionGate Gate,
        FakeProcessLaunchService LaunchService,
        AuditLogOptions AuditOptions);

    private sealed class FakeWindowManager(IReadOnlyList<WindowDescriptor> windows) : IWindowManager
    {
        public IReadOnlyList<WindowDescriptor> ListWindows(bool includeInvisible)
        {
            if (includeInvisible)
            {
                return windows;
            }

            return windows.Where(window => window.IsVisible).ToArray();
        }

        public WindowDescriptor? GetWindow(long hwnd) =>
            windows.FirstOrDefault(window => window.Hwnd == hwnd);

        public WindowDescriptor? FindWindow(WindowSelector selector)
        {
            if (selector.Hwnd is long hwnd)
            {
                return GetWindow(hwnd);
            }

            return windows.Count > 0 ? windows[0] : null;
        }

        public WindowDescriptor? GetForegroundWindow()
        {
            for (int index = 0; index < windows.Count; index++)
            {
                if (windows[index].IsForeground)
                {
                    return windows[index];
                }
            }

            return null;
        }

        public bool TryFocus(long hwnd) => windows.Any(window => window.Hwnd == hwnd);
    }

    private sealed class NoopCaptureService : ICaptureService
    {
        public Task<CaptureResult> CaptureAsync(CaptureTarget target, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Capture не должен вызываться в launch boundary tests.");
    }

    private sealed class FakeProcessLaunchService(
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

    private sealed class FakeToolExecutionGate(ToolExecutionDecision decision, Action? onEvaluate = null) : IToolExecutionGate
    {
        public int Calls { get; private set; }

        public ToolExecutionIntent? LastIntent { get; private set; }

        public ToolExecutionDecision Evaluate(ToolExecutionPolicyDescriptor policy, ToolExecutionIntent intent)
        {
            Calls++;
            LastIntent = intent;
            onEvaluate?.Invoke();
            return decision;
        }

        public ToolExecutionDecision Evaluate(
            ToolExecutionPolicyDescriptor policy,
            RuntimeGuardAssessment assessment,
            ToolExecutionIntent intent)
        {
            Calls++;
            LastIntent = intent;
            onEvaluate?.Invoke();
            return decision;
        }
    }
}
