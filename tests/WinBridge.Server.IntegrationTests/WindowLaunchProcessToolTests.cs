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
    private const string NotepadExecutable = "notepad.exe";
    private const string LaunchToolAuditName = "windows.launch_process";
    private const string LaunchPreviewCompletedEventName = "launch.preview.completed";
    private const string LaunchRuntimeCompletedEventName = "launch.runtime.completed";
    private const string AllowedGateDecisionAuditValue = "allowed";

    [Fact]
    public async Task LaunchProcessReturnsBlockedPayloadWithoutInvokingRuntimeService()
    {
        TestContext context = CreateContext(BlockedDecision());

        CallToolResult result = await context.Tools.LaunchProcess(NotepadLaunchRequest());

        JsonElement payload = AssertLaunchPayload(result, isError: true, status: LaunchProcessStatusValues.Blocked);
        Assert.Equal(0, context.LaunchService.Calls);
        Assert.Equal(1, context.Gate.Calls);
        Assert.True(payload.TryGetProperty("preview", out _));
    }

    [Fact]
    public async Task LaunchProcessReturnsNeedsConfirmationPayloadWithoutInvokingRuntimeService()
    {
        TestContext context = CreateContext(NeedsConfirmationDecision());

        CallToolResult result = await context.Tools.LaunchProcess(NotepadLaunchRequest());

        JsonElement payload = AssertLaunchPayload(result, isError: true, status: LaunchProcessStatusValues.NeedsConfirmation);
        Assert.True(payload.GetProperty("requiresConfirmation").GetBoolean());
        Assert.Equal(0, context.LaunchService.Calls);
    }

    [Fact]
    public async Task LaunchProcessReturnsDryRunOnlyPayloadWithoutInvokingRuntimeService()
    {
        TestContext context = CreateContext(DryRunOnlyDecision());

        CallToolResult result = await context.Tools.LaunchProcess(NotepadLaunchRequest());

        AssertLaunchPayload(result, isError: true, status: LaunchProcessStatusValues.DryRunOnly);
        Assert.Equal(0, context.LaunchService.Calls);
    }

    [Fact]
    public async Task LaunchProcessAllowedDryRunReturnsPreviewWithoutInvokingRuntimeService()
    {
        TestContext context = CreateContext(AllowedDryRunDecision());

        CallToolResult result = await context.Tools.LaunchProcess(DemoDryRunLaunchRequest());

        JsonElement payload = AssertLaunchPayload(result, isError: false, status: LaunchProcessStatusValues.Done);
        Assert.Equal("Demo.exe", payload.GetProperty("executableIdentity").GetString());
        Assert.True(payload.TryGetProperty("preview", out JsonElement preview));
        Assert.Equal("Demo.exe", preview.GetProperty("executableIdentity").GetString());
        Assert.Equal(1, preview.GetProperty("argumentCount").GetInt32());
        AssertMissingProperties(
            payload,
            "processId",
            "startedAtUtc",
            "hasExited",
            "exitCode",
            "mainWindowHandle",
            "resultMode",
            "artifactPath");
        Assert.Equal(0, context.LaunchService.Calls);

        string[] eventLines = ReadAuditEvents(context);
        Assert.Equal(3, eventLines.Length);
        AssertAuditLineContains(
            eventLines[1],
            JsonPropertyFragment("event_name", LaunchPreviewCompletedEventName),
            JsonPropertyFragment("tool_name", LaunchToolAuditName));
        Assert.DoesNotContain(
            eventLines,
            line => line.Contains(JsonPropertyFragment("event_name", LaunchRuntimeCompletedEventName), StringComparison.Ordinal));
    }

    [Fact]
    public async Task LaunchProcessAllowedDryRunReturnsPreviewWhenObservabilityWritesFailAfterDecision()
    {
        AuditLogOptions? auditOptions = null;
        TestContext context = CreateContext(
            AllowedDryRunDecision(),
            onGateEvaluate: () => ReplaceEventsFileWithDirectory(auditOptions));
        auditOptions = context.AuditOptions;

        CallToolResult result = await context.Tools.LaunchProcess(DemoDryRunLaunchRequest());

        AssertLaunchPayload(result, isError: false, status: LaunchProcessStatusValues.Done);
        Assert.Equal(0, context.LaunchService.Calls);
        Assert.True(Directory.Exists(context.AuditOptions.EventsPath));
    }

    [Fact]
    public async Task LaunchProcessAllowedLiveReturnsRuntimePayload()
    {
        FakeProcessLaunchService launchService = LaunchServiceReturning(ProcessStartedResult(processId: 4242));
        TestContext context = CreateContext(AllowedLiveDecision(), launchService);

        CallToolResult result = await context.Tools.LaunchProcess(ConfirmedNotepadLaunchRequest());

        JsonElement payload = AssertLaunchPayload(result, isError: false, status: LaunchProcessStatusValues.Done);
        Assert.Equal(4242, payload.GetProperty("processId").GetInt32());
        Assert.Equal(1, context.LaunchService.Calls);
        Assert.Equal(NotepadExecutable, context.LaunchService.LastRequest?.Executable);
        Assert.True(context.Gate.LastIntent?.ConfirmationGranted);
    }

    [Fact]
    public async Task LaunchProcessAllowedLiveIncludesArtifactPathInPayloadAndCompletionAudit()
    {
        const string artifactPath = @"C:\artifacts\diagnostics\launch\launch-20260406T140000000-test.json";
        TestContext context = CreateContext(
            AllowedLiveDecision(),
            LaunchServiceReturning(ProcessStartedResult(processId: 4243, artifactPath)));

        CallToolResult result = await context.Tools.LaunchProcess(ConfirmedNotepadLaunchRequest());

        JsonElement payload = AssertLaunchPayload(result, isError: false, status: LaunchProcessStatusValues.Done);
        Assert.Equal(artifactPath, payload.GetProperty("artifactPath").GetString());
        AssertCompletionAudit(context, LaunchProcessStatusValues.Done, artifactPath);
    }

    [Fact]
    public async Task LaunchProcessAllowedLiveReturnsFactualPayloadWhenCompletionAuditWriteFails()
    {
        AuditLogOptions? auditOptions = null;
        FakeProcessLaunchService launchService = new((_, _) =>
        {
            ReplaceEventsFileWithDirectory(auditOptions);
            return Task.FromResult(ProcessStartedResult(processId: 4244));
        });
        TestContext context = CreateContext(AllowedLiveDecision(), launchService);
        auditOptions = context.AuditOptions;

        CallToolResult result = await context.Tools.LaunchProcess(ConfirmedNotepadLaunchRequest());

        JsonElement payload = AssertLaunchPayload(result, isError: false, status: LaunchProcessStatusValues.Done);
        Assert.Equal(4244, payload.GetProperty("processId").GetInt32());
        Assert.True(Directory.Exists(context.AuditOptions.EventsPath));
    }

    [Fact]
    public async Task LaunchProcessAllowedLiveRuntimeFailureReturnsFailedDecision()
    {
        const string artifactPath = @"C:\artifacts\diagnostics\launch\launch-20260406T140500000-failed.json";
        TestContext context = CreateContext(
            AllowedLiveDecision(),
            LaunchServiceReturning(StartFailedResult(artifactPath)));

        CallToolResult result = await context.Tools.LaunchProcess(ConfirmedNotepadLaunchRequest());

        JsonElement payload = AssertLaunchPayload(result, isError: true, status: LaunchProcessStatusValues.Failed);
        Assert.Equal(LaunchProcessFailureCodeValues.StartFailed, payload.GetProperty("failureCode").GetString());
        Assert.Equal(artifactPath, payload.GetProperty("artifactPath").GetString());
        Assert.Equal(1, context.LaunchService.Calls);
        AssertCompletionAudit(context, LaunchProcessStatusValues.Failed, artifactPath);
    }

    [Fact]
    public async Task LaunchProcessInvalidRequestReturnsFailedPayloadWithoutRuntimeInvocation()
    {
        TestContext context = CreateContext(AllowedLiveDecision());

        CallToolResult result = await context.Tools.LaunchProcess(new LaunchProcessRequest
        {
            Executable = "https://example.test/demo.exe",
        });

        JsonElement payload = AssertLaunchPayload(result, isError: true, status: LaunchProcessStatusValues.Failed);
        Assert.Equal(LaunchProcessFailureCodeValues.UnsupportedTargetKind, payload.GetProperty("failureCode").GetString());
        Assert.Equal(1, context.Gate.Calls);
        Assert.Equal(0, context.LaunchService.Calls);
        Assert.False(payload.TryGetProperty("preview", out _));
        AssertCompletionAudit(context, LaunchProcessStatusValues.Failed);
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
        EmptyWindowManager windowManager = new();
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

    private static ToolExecutionDecision AllowedLiveDecision() =>
        CreateDecision(ToolExecutionDecisionKind.Allowed, ToolExecutionMode.Live);

    private static ToolExecutionDecision AllowedDryRunDecision() =>
        CreateDecision(ToolExecutionDecisionKind.Allowed, ToolExecutionMode.DryRun);

    private static ToolExecutionDecision BlockedDecision() =>
        CreateDecision(
            ToolExecutionDecisionKind.Blocked,
            ToolExecutionMode.Live,
            GuardReasonCodeValues.CapabilityNotImplemented,
            GuardSeverityValues.Blocked);

    private static ToolExecutionDecision NeedsConfirmationDecision() =>
        CreateDecision(
            ToolExecutionDecisionKind.NeedsConfirmation,
            ToolExecutionMode.Live,
            GuardReasonCodeValues.LaunchElevationBoundaryUnconfirmed,
            GuardSeverityValues.Warning,
            requiresConfirmation: true);

    private static ToolExecutionDecision DryRunOnlyDecision() =>
        CreateDecision(
            ToolExecutionDecisionKind.DryRunOnly,
            ToolExecutionMode.DryRun,
            GuardReasonCodeValues.CapabilityDryRunPreviewUnavailable,
            GuardSeverityValues.Blocked);

    private static ToolExecutionDecision CreateDecision(
        ToolExecutionDecisionKind kind,
        ToolExecutionMode mode,
        string reasonCode = GuardReasonCodeValues.LaunchElevationBoundaryUnconfirmed,
        string severity = GuardSeverityValues.Warning,
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

    private static LaunchProcessRequest NotepadLaunchRequest() =>
        new()
        {
            Executable = NotepadExecutable,
        };

    private static LaunchProcessRequest ConfirmedNotepadLaunchRequest() =>
        new()
        {
            Executable = NotepadExecutable,
            Confirm = true,
        };

    private static LaunchProcessRequest DemoDryRunLaunchRequest() =>
        new()
        {
            Executable = @"C:\Tools\Demo.exe",
            Args = ["--flag"],
            WorkingDirectory = @"C:\Tools",
            WaitForWindow = true,
            TimeoutMs = 4000,
            DryRun = true,
        };

    private static FakeProcessLaunchService LaunchServiceReturning(LaunchProcessResult result) =>
        new((_, _) => Task.FromResult(result));

    private static LaunchProcessResult ProcessStartedResult(int processId, string? artifactPath = null) =>
        new(
            Status: LaunchProcessStatusValues.Done,
            Decision: LaunchProcessStatusValues.Done,
            ResultMode: LaunchProcessResultModeValues.ProcessStarted,
            ExecutableIdentity: NotepadExecutable,
            ProcessId: processId,
            StartedAtUtc: new DateTimeOffset(2026, 4, 6, 10, 0, 0, TimeSpan.Zero),
            HasExited: false,
            MainWindowObserved: false,
            MainWindowObservationStatus: LaunchMainWindowObservationStatusValues.NotRequested,
            ArtifactPath: artifactPath);

    private static LaunchProcessResult StartFailedResult(string artifactPath) =>
        new(
            Status: LaunchProcessStatusValues.Failed,
            Decision: LaunchProcessStatusValues.Failed,
            FailureCode: LaunchProcessFailureCodeValues.StartFailed,
            Reason: "Process.Start failed.",
            ExecutableIdentity: NotepadExecutable,
            ArtifactPath: artifactPath);

    private static JsonElement AssertLaunchPayload(CallToolResult result, bool isError, string status, string? decision = null)
    {
        JsonElement payload = AssertStructuredPayload(result);
        Assert.Equal(isError, result.IsError);
        Assert.Equal(status, payload.GetProperty("status").GetString());
        Assert.Equal(decision ?? status, payload.GetProperty("decision").GetString());
        return payload;
    }

    private static JsonElement AssertStructuredPayload(CallToolResult result)
    {
        Assert.NotNull(result.StructuredContent);
        Assert.Single(result.Content);
        Assert.IsType<TextContentBlock>(result.Content[0]);
        return result.StructuredContent!.Value;
    }

    private static void AssertMissingProperties(JsonElement payload, params string[] propertyNames)
    {
        foreach (string propertyName in propertyNames)
        {
            Assert.False(payload.TryGetProperty(propertyName, out _), propertyName);
        }
    }

    private static void AssertCompletionAudit(TestContext context, string decision, string? artifactPath = null)
    {
        string[] eventLines = ReadAuditEvents(context);
        Assert.Equal(2, eventLines.Length);
        AssertAuditLineContains(
            eventLines[1],
            JsonPropertyFragment("decision", decision),
            JsonPropertyFragment("gate_decision", AllowedGateDecisionAuditValue));

        if (artifactPath is not null)
        {
            AssertAuditLineContains(eventLines[1], JsonPropertyFragment("artifact_path", artifactPath));
        }
    }

    private static void AssertAuditLineContains(string line, params string[] fragments)
    {
        foreach (string fragment in fragments)
        {
            Assert.Contains(fragment, line, StringComparison.Ordinal);
        }
    }

    private static string[] ReadAuditEvents(TestContext context) =>
        File.ReadAllLines(context.AuditOptions.EventsPath);

    private static string JsonPropertyFragment(string propertyName, string value) =>
        $"\"{propertyName}\":{JsonSerializer.Serialize(value)}";

    private static void ReplaceEventsFileWithDirectory(AuditLogOptions? auditOptions)
    {
        Assert.NotNull(auditOptions);
        File.Delete(auditOptions!.EventsPath);
        Directory.CreateDirectory(auditOptions.EventsPath);
    }

    private sealed record TestContext(
        WindowTools Tools,
        FakeToolExecutionGate Gate,
        FakeProcessLaunchService LaunchService,
        AuditLogOptions AuditOptions);

    private sealed class EmptyWindowManager : IWindowManager
    {
        private static readonly WindowDescriptor[] NoWindows = [];

        public IReadOnlyList<WindowDescriptor> ListWindows(bool includeInvisible) => NoWindows;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Implements IWindowManager test double contract.")]
        public WindowDescriptor? GetWindow(long hwnd) => null;

        public WindowDescriptor? FindWindow(WindowSelector selector) => null;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Implements IWindowManager test double contract.")]
        public WindowDescriptor? GetForegroundWindow() => null;

        public bool TryFocus(long hwnd) => false;
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

        public ToolExecutionDecision Evaluate(ToolExecutionPolicyDescriptor policy, ToolExecutionIntent intent) =>
            Evaluate(intent);

        public ToolExecutionDecision Evaluate(
            ToolExecutionPolicyDescriptor policy,
            RuntimeGuardAssessment assessment,
            ToolExecutionIntent intent) =>
            Evaluate(intent);

        private ToolExecutionDecision Evaluate(ToolExecutionIntent intent)
        {
            Calls++;
            LastIntent = intent;
            onEvaluate?.Invoke();
            return decision;
        }
    }
}
