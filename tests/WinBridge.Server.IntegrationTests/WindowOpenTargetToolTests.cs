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
using WinBridge.Runtime.Windows.UIA;
using WinBridge.Server.Tools;

namespace WinBridge.Server.IntegrationTests;

public sealed class WindowOpenTargetToolTests
{
    [Fact]
    public async Task OpenTargetReturnsBlockedPayloadWithoutInvokingRuntimeService()
    {
        TestContext context = CreateContext(
            decision: CreateDecision(
                ToolExecutionDecisionKind.Blocked,
                ToolExecutionMode.Live,
                GuardReasonCodeValues.CapabilityNotImplemented,
                GuardSeverityValues.Blocked));

        CallToolResult result = await context.Tools.OpenTarget(new OpenTargetRequest
        {
            TargetKind = OpenTargetKindValues.Document,
            Target = @"C:\Docs\report.pdf",
        });

        JsonElement payload = AssertStructuredPayload(result);
        Assert.True(result.IsError);
        Assert.Equal(OpenTargetStatusValues.Blocked, payload.GetProperty("status").GetString());
        Assert.Equal(OpenTargetStatusValues.Blocked, payload.GetProperty("decision").GetString());
        Assert.Equal(0, context.OpenTargetService.Calls);
        Assert.Equal(1, context.Gate.Calls);
        Assert.True(payload.TryGetProperty("preview", out _));
    }

    [Fact]
    public async Task OpenTargetAllowedDryRunReturnsPreviewWithoutInvokingRuntimeService()
    {
        TestContext context = CreateContext(
            decision: CreateDecision(
                ToolExecutionDecisionKind.Allowed,
                ToolExecutionMode.DryRun,
                GuardReasonCodeValues.LaunchElevationBoundaryUnconfirmed,
                GuardSeverityValues.Warning));

        CallToolResult result = await context.Tools.OpenTarget(new OpenTargetRequest
        {
            TargetKind = OpenTargetKindValues.Url,
            Target = "https://example.test/docs?q=hidden#fragment",
            DryRun = true,
        });

        JsonElement payload = AssertStructuredPayload(result);
        Assert.False(result.IsError);
        Assert.Equal(OpenTargetStatusValues.Done, payload.GetProperty("status").GetString());
        Assert.Equal(OpenTargetStatusValues.Done, payload.GetProperty("decision").GetString());
        Assert.True(payload.TryGetProperty("preview", out JsonElement preview));
        Assert.Equal(OpenTargetKindValues.Url, preview.GetProperty("targetKind").GetString());
        Assert.Equal("https", preview.GetProperty("uriScheme").GetString());
        Assert.False(payload.TryGetProperty("artifactPath", out _));
        Assert.Equal(0, context.OpenTargetService.Calls);

        string[] eventLines = File.ReadAllLines(context.AuditOptions.EventsPath);
        Assert.Equal(3, eventLines.Length);
        Assert.Contains("\"event_name\":\"open_target.preview.completed\"", eventLines[1], StringComparison.Ordinal);
        Assert.DoesNotContain(eventLines, line => line.Contains("\"event_name\":\"open_target.runtime.completed\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OpenTargetAllowedLiveReturnsRuntimePayload()
    {
        FakeOpenTargetService openTargetService = new(
            (_, _) => Task.FromResult(
                new OpenTargetResult(
                    Status: OpenTargetStatusValues.Done,
                    Decision: OpenTargetStatusValues.Done,
                    ResultMode: OpenTargetResultModeValues.HandlerProcessObserved,
                    TargetKind: OpenTargetKindValues.Document,
                    TargetIdentity: "report.pdf",
                    AcceptedAtUtc: new DateTimeOffset(2026, 4, 8, 13, 20, 0, TimeSpan.Zero),
                    HandlerProcessId: 4242,
                    ArtifactPath: @"C:\artifacts\diagnostics\launch\open-target-20260408T132000000-demo.json")));
        TestContext context = CreateContext(
            decision: CreateDecision(
                ToolExecutionDecisionKind.Allowed,
                ToolExecutionMode.Live,
                GuardReasonCodeValues.LaunchElevationBoundaryUnconfirmed,
                GuardSeverityValues.Warning),
            openTargetService: openTargetService);

        CallToolResult result = await context.Tools.OpenTarget(new OpenTargetRequest
        {
            TargetKind = OpenTargetKindValues.Document,
            Target = @"C:\Docs\report.pdf",
            Confirm = true,
        });

        JsonElement payload = AssertStructuredPayload(result);
        Assert.False(result.IsError);
        Assert.Equal(OpenTargetStatusValues.Done, payload.GetProperty("status").GetString());
        Assert.Equal(OpenTargetResultModeValues.HandlerProcessObserved, payload.GetProperty("resultMode").GetString());
        Assert.Equal(4242, payload.GetProperty("handlerProcessId").GetInt32());
        Assert.Equal(1, context.OpenTargetService.Calls);
        Assert.True(context.Gate.LastIntent?.ConfirmationGranted);
    }

    [Fact]
    public async Task OpenTargetAllowedLiveRuntimeFailureReturnsFailedDecision()
    {
        FakeOpenTargetService openTargetService = new(
            (_, _) => Task.FromResult(
                new OpenTargetResult(
                    Status: OpenTargetStatusValues.Failed,
                    Decision: OpenTargetStatusValues.Failed,
                    FailureCode: OpenTargetFailureCodeValues.TargetNotFound,
                    Reason: "Shell-open target не найден.",
                    TargetKind: OpenTargetKindValues.Document,
                    TargetIdentity: "report.pdf",
                    ArtifactPath: @"C:\artifacts\diagnostics\launch\open-target-20260408T132500000-failed.json")));
        TestContext context = CreateContext(
            decision: CreateDecision(
                ToolExecutionDecisionKind.Allowed,
                ToolExecutionMode.Live,
                GuardReasonCodeValues.LaunchElevationBoundaryUnconfirmed,
                GuardSeverityValues.Warning),
            openTargetService: openTargetService);

        CallToolResult result = await context.Tools.OpenTarget(new OpenTargetRequest
        {
            TargetKind = OpenTargetKindValues.Document,
            Target = @"C:\Docs\missing.pdf",
            Confirm = true,
        });

        JsonElement payload = AssertStructuredPayload(result);
        Assert.True(result.IsError);
        Assert.Equal(OpenTargetStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.Equal(OpenTargetStatusValues.Failed, payload.GetProperty("decision").GetString());
        Assert.Equal(OpenTargetFailureCodeValues.TargetNotFound, payload.GetProperty("failureCode").GetString());
        Assert.Equal(1, context.OpenTargetService.Calls);
    }

    [Fact]
    public async Task OpenTargetAllowedLiveUnexpectedServiceFailureDoesNotDowncastToShellRejectedTarget()
    {
        FakeOpenTargetService openTargetService = new(
            (_, _) => throw new InvalidOperationException("boom"));
        TestContext context = CreateContext(
            decision: CreateDecision(
                ToolExecutionDecisionKind.Allowed,
                ToolExecutionMode.Live,
                GuardReasonCodeValues.LaunchElevationBoundaryUnconfirmed,
                GuardSeverityValues.Warning),
            openTargetService: openTargetService);

        CallToolResult result = await context.Tools.OpenTarget(new OpenTargetRequest
        {
            TargetKind = OpenTargetKindValues.Document,
            Target = @"C:\Docs\report.pdf",
            Confirm = true,
        });

        JsonElement payload = AssertStructuredPayload(result);
        Assert.True(result.IsError);
        Assert.Equal(OpenTargetStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.Equal(OpenTargetStatusValues.Failed, payload.GetProperty("decision").GetString());
        Assert.False(payload.TryGetProperty("failureCode", out _));
        Assert.Equal(1, context.OpenTargetService.Calls);
    }

    [Fact]
    public async Task OpenTargetInvalidRequestReturnsFailedPayloadWithoutRuntimeInvocation()
    {
        TestContext context = CreateContext(
            decision: CreateDecision(
                ToolExecutionDecisionKind.Allowed,
                ToolExecutionMode.Live,
                GuardReasonCodeValues.LaunchElevationBoundaryUnconfirmed,
                GuardSeverityValues.Warning));

        CallToolResult result = await context.Tools.OpenTarget(new OpenTargetRequest
        {
            TargetKind = OpenTargetKindValues.Url,
            Target = "mailto:user@example.test",
        });

        JsonElement payload = AssertStructuredPayload(result);
        Assert.True(result.IsError);
        Assert.Equal(OpenTargetStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.Equal(OpenTargetFailureCodeValues.UnsupportedUriScheme, payload.GetProperty("failureCode").GetString());
        Assert.Equal(1, context.Gate.Calls);
        Assert.Equal(0, context.OpenTargetService.Calls);
        Assert.False(payload.TryGetProperty("preview", out _));
    }

    private static TestContext CreateContext(
        ToolExecutionDecision decision,
        FakeOpenTargetService? openTargetService = null,
        Action? onGateEvaluate = null)
    {
        string root = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        AuditLogOptions options = new(
            ContentRootPath: root,
            EnvironmentName: "Tests",
            RunId: "open-target-tool-tests",
            DiagnosticsRoot: Path.Combine(root, "artifacts", "diagnostics"),
            RunDirectory: Path.Combine(root, "artifacts", "diagnostics", "open-target-tool-tests"),
            EventsPath: Path.Combine(root, "artifacts", "diagnostics", "open-target-tool-tests", "events.jsonl"),
            SummaryPath: Path.Combine(root, "artifacts", "diagnostics", "open-target-tool-tests", "summary.md"));

        AuditLog auditLog = new(options, TimeProvider.System);
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("open-target-tool-tests"));
        FakeWindowManager windowManager = new([]);
        FakeToolExecutionGate gate = new(decision, onGateEvaluate);
        FakeOpenTargetService effectiveOpenTargetService = openTargetService ?? new FakeOpenTargetService();
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
                new FakeProcessLaunchService(),
                effectiveOpenTargetService),
            gate,
            effectiveOpenTargetService,
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
            RiskLevel: ToolExecutionRiskLevel.Medium,
            Reasons:
            [
                new GuardReason(
                    reasonCode,
                    severity,
                    "Open target boundary test reason.",
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
        FakeOpenTargetService OpenTargetService,
        AuditLogOptions AuditOptions);

    private sealed class FakeWindowManager(IReadOnlyList<WindowDescriptor> windows) : IWindowManager
    {
        public IReadOnlyList<WindowDescriptor> ListWindows(bool includeInvisible) => windows;

        public WindowDescriptor? GetWindow(long hwnd) =>
            windows.FirstOrDefault(window => window.Hwnd == hwnd);

        public WindowDescriptor? FindWindow(WindowSelector selector) =>
            windows.Count > 0 ? windows[0] : null;

        public WindowDescriptor? GetForegroundWindow() =>
            windows.FirstOrDefault(window => window.IsForeground);

        public bool TryFocus(long hwnd) => windows.Any(window => window.Hwnd == hwnd);
    }

    private sealed class NoopCaptureService : ICaptureService
    {
        public Task<CaptureResult> CaptureAsync(CaptureTarget target, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Capture не должен вызываться в open_target boundary tests.");
    }

    private sealed class FakeProcessLaunchService : IProcessLaunchService
    {
        public Task<LaunchProcessResult> LaunchAsync(LaunchProcessRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Launch service не должен вызываться в open_target boundary tests.");
    }

    private sealed class FakeOpenTargetService(
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
