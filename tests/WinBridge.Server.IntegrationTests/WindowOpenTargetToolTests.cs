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

public sealed class WindowOpenTargetToolTests
{
    private const string RunId = "open-target-tool-tests";
    private const string DocumentTarget = @"C:\Docs\report.pdf";
    private const string FolderTarget = @"C:\Docs";
    private const string HttpsUrlTarget = "https://example.test/docs?q=hidden#fragment";
    private const string MailtoUrlTarget = "mailto:user@example.test";

    [Fact]
    public async Task OpenTargetReturnsBlockedPayloadWithoutInvokingRuntimeService()
    {
        TestContext context = CreateContext(
            kind: ToolExecutionDecisionKind.Blocked,
            reasonCode: GuardReasonCodeValues.CapabilityNotImplemented,
            severity: GuardSeverityValues.Blocked);

        CallToolResult result = await context.Tools.OpenTarget(DocumentRequest());

        JsonElement payload = AssertPayload(result, expectedIsError: true);
        AssertStatusAndDecision(payload, OpenTargetStatusValues.Blocked);
        Assert.Equal(0, context.OpenTargetService.Calls);
        Assert.Equal(1, context.Gate.Calls);
        Assert.True(payload.TryGetProperty("preview", out _));
    }

    [Fact]
    public async Task OpenTargetReturnsNeedsConfirmationPayloadWithoutInvokingRuntimeService()
    {
        TestContext context = CreateContext(
            kind: ToolExecutionDecisionKind.NeedsConfirmation,
            requiresConfirmation: true);

        CallToolResult result = await context.Tools.OpenTarget(DocumentRequest());

        JsonElement payload = AssertPayload(result, expectedIsError: true);
        AssertStatusAndDecision(payload, OpenTargetStatusValues.NeedsConfirmation);
        Assert.True(payload.GetProperty("requiresConfirmation").GetBoolean());
        Assert.Equal(0, context.OpenTargetService.Calls);
        Assert.True(payload.TryGetProperty("preview", out _));
    }

    [Fact]
    public async Task OpenTargetReturnsDryRunOnlyPayloadWithoutInvokingRuntimeService()
    {
        TestContext context = CreateContext(
            kind: ToolExecutionDecisionKind.DryRunOnly,
            mode: ToolExecutionMode.DryRun,
            reasonCode: GuardReasonCodeValues.CapabilityDryRunPreviewUnavailable,
            severity: GuardSeverityValues.Blocked);

        CallToolResult result = await context.Tools.OpenTarget(FolderRequest());

        JsonElement payload = AssertPayload(result, expectedIsError: true);
        AssertStatusAndDecision(payload, OpenTargetStatusValues.DryRunOnly);
        Assert.Equal(0, context.OpenTargetService.Calls);
        Assert.True(payload.TryGetProperty("preview", out _));
    }

    [Fact]
    public async Task OpenTargetAllowedDryRunReturnsPreviewWithoutInvokingRuntimeService()
    {
        TestContext context = CreateContext(mode: ToolExecutionMode.DryRun);

        CallToolResult result = await context.Tools.OpenTarget(UrlRequest(HttpsUrlTarget, dryRun: true));

        JsonElement payload = AssertPayload(result, expectedIsError: false);
        AssertStatusAndDecision(payload, OpenTargetStatusValues.Done);
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
        FakeOpenTargetService openTargetService = ReturningOpenTargetResult(
            new OpenTargetResult(
                Status: OpenTargetStatusValues.Done,
                Decision: OpenTargetStatusValues.Done,
                ResultMode: OpenTargetResultModeValues.HandlerProcessObserved,
                TargetKind: OpenTargetKindValues.Document,
                TargetIdentity: "report.pdf",
                AcceptedAtUtc: new DateTimeOffset(2026, 4, 8, 13, 20, 0, TimeSpan.Zero),
                HandlerProcessId: 4242,
                ArtifactPath: @"C:\artifacts\diagnostics\launch\open-target-20260408T132000000-demo.json"));
        TestContext context = CreateContext(openTargetService: openTargetService);

        CallToolResult result = await context.Tools.OpenTarget(DocumentRequest(confirm: true));

        JsonElement payload = AssertPayload(result, expectedIsError: false);
        Assert.Equal(OpenTargetStatusValues.Done, payload.GetProperty("status").GetString());
        Assert.Equal(OpenTargetResultModeValues.HandlerProcessObserved, payload.GetProperty("resultMode").GetString());
        Assert.Equal(4242, payload.GetProperty("handlerProcessId").GetInt32());
        Assert.Equal(1, context.OpenTargetService.Calls);
        Assert.True(context.Gate.LastIntent?.ConfirmationGranted);
    }

    [Fact]
    public async Task OpenTargetAllowedLiveRuntimeFailureReturnsFailedDecision()
    {
        FakeOpenTargetService openTargetService = ReturningOpenTargetResult(
            new OpenTargetResult(
                Status: OpenTargetStatusValues.Failed,
                Decision: OpenTargetStatusValues.Failed,
                FailureCode: OpenTargetFailureCodeValues.TargetNotFound,
                Reason: "Shell-open target не найден.",
                TargetKind: OpenTargetKindValues.Document,
                TargetIdentity: "report.pdf",
                ArtifactPath: @"C:\artifacts\diagnostics\launch\open-target-20260408T132500000-failed.json"));
        TestContext context = CreateContext(openTargetService: openTargetService);

        CallToolResult result = await context.Tools.OpenTarget(DocumentRequest(@"C:\Docs\missing.pdf", confirm: true));

        JsonElement payload = AssertPayload(result, expectedIsError: true);
        AssertStatusAndDecision(payload, OpenTargetStatusValues.Failed);
        Assert.Equal(OpenTargetFailureCodeValues.TargetNotFound, payload.GetProperty("failureCode").GetString());
        Assert.Equal(1, context.OpenTargetService.Calls);
    }

    [Fact]
    public async Task OpenTargetAllowedLiveUnexpectedServiceFailureDoesNotDowncastToShellRejectedTarget()
    {
        FakeOpenTargetService openTargetService = new((_, _) => throw new InvalidOperationException("boom"));
        TestContext context = CreateContext(openTargetService: openTargetService);

        CallToolResult result = await context.Tools.OpenTarget(DocumentRequest(confirm: true));

        JsonElement payload = AssertPayload(result, expectedIsError: true);
        AssertStatusAndDecision(payload, OpenTargetStatusValues.Failed);
        Assert.False(payload.TryGetProperty("failureCode", out _));
        Assert.Equal(1, context.OpenTargetService.Calls);
    }

    [Fact]
    public async Task OpenTargetInvalidRequestReturnsFailedPayloadWithoutRuntimeInvocation()
    {
        TestContext context = CreateContext();

        CallToolResult result = await context.Tools.OpenTarget(UrlRequest(MailtoUrlTarget));

        JsonElement payload = AssertPayload(result, expectedIsError: true);
        Assert.Equal(OpenTargetStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.Equal(OpenTargetFailureCodeValues.UnsupportedUriScheme, payload.GetProperty("failureCode").GetString());
        Assert.Equal(1, context.Gate.Calls);
        Assert.Equal(0, context.OpenTargetService.Calls);
        Assert.False(payload.TryGetProperty("preview", out _));
    }

    private static TestContext CreateContext(
        ToolExecutionDecisionKind kind = ToolExecutionDecisionKind.Allowed,
        ToolExecutionMode mode = ToolExecutionMode.Live,
        string? reasonCode = null,
        string? severity = null,
        bool requiresConfirmation = false,
        FakeOpenTargetService? openTargetService = null)
    {
        AuditLogOptions options = CreateAuditLogOptions();
        AuditLog auditLog = new(options, TimeProvider.System);
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext(RunId));
        EmptyWindowManager windowManager = new();
        FakeToolExecutionGate gate = new(CreateDecision(kind, mode, reasonCode, severity, requiresConfirmation));
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
                new FakeInputService(),
                new FakeProcessLaunchService(),
                effectiveOpenTargetService),
            gate,
            effectiveOpenTargetService,
            options);
    }

    private static AuditLogOptions CreateAuditLogOptions()
    {
        string root = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        string diagnosticsRoot = Path.Combine(root, "artifacts", "diagnostics");
        string runDirectory = Path.Combine(diagnosticsRoot, RunId);
        Directory.CreateDirectory(root);

        return new AuditLogOptions(
            ContentRootPath: root,
            EnvironmentName: "Tests",
            RunId: RunId,
            DiagnosticsRoot: diagnosticsRoot,
            RunDirectory: runDirectory,
            EventsPath: Path.Combine(runDirectory, "events.jsonl"),
            SummaryPath: Path.Combine(runDirectory, "summary.md"));
    }

    private static ToolExecutionDecision CreateDecision(
        ToolExecutionDecisionKind kind,
        ToolExecutionMode mode,
        string? reasonCode,
        string? severity,
        bool requiresConfirmation) =>
        new(
            Kind: kind,
            Mode: mode,
            RiskLevel: ToolExecutionRiskLevel.Medium,
            Reasons:
            [
                new GuardReason(
                    reasonCode ?? GuardReasonCodeValues.LaunchElevationBoundaryUnconfirmed,
                    severity ?? GuardSeverityValues.Warning,
                    "Open target boundary test reason.",
                    CapabilitySummaryValues.Launch),
            ],
            RequiresConfirmation: requiresConfirmation,
            DryRunSupported: true,
            GuardCapability: CapabilitySummaryValues.Launch);

    private static OpenTargetRequest DocumentRequest(string target = DocumentTarget, bool confirm = false) =>
        confirm
            ? new() { TargetKind = OpenTargetKindValues.Document, Target = target, Confirm = true }
            : new() { TargetKind = OpenTargetKindValues.Document, Target = target };

    private static OpenTargetRequest FolderRequest() =>
        new() { TargetKind = OpenTargetKindValues.Folder, Target = FolderTarget };

    private static OpenTargetRequest UrlRequest(string target, bool dryRun = false) =>
        dryRun
            ? new() { TargetKind = OpenTargetKindValues.Url, Target = target, DryRun = true }
            : new() { TargetKind = OpenTargetKindValues.Url, Target = target };

    private static FakeOpenTargetService ReturningOpenTargetResult(OpenTargetResult result) =>
        new((_, _) => Task.FromResult(result));

    private static JsonElement AssertPayload(CallToolResult result, bool expectedIsError)
    {
        Assert.Equal(expectedIsError, result.IsError);
        Assert.NotNull(result.StructuredContent);
        Assert.Single(result.Content);
        Assert.IsType<TextContentBlock>(result.Content[0]);
        return result.StructuredContent!.Value;
    }

    private static void AssertStatusAndDecision(JsonElement payload, string expectedValue)
    {
        Assert.Equal(expectedValue, payload.GetProperty("status").GetString());
        Assert.Equal(expectedValue, payload.GetProperty("decision").GetString());
    }

    private sealed record TestContext(
        WindowTools Tools,
        FakeToolExecutionGate Gate,
        FakeOpenTargetService OpenTargetService,
        AuditLogOptions AuditOptions);

    private sealed class EmptyWindowManager : IWindowManager
    {
        private static readonly WindowDescriptor[] EmptyWindows = [];

        public IReadOnlyList<WindowDescriptor> ListWindows(bool includeInvisible) => EmptyWindows;

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

            return handler?.Invoke(request, cancellationToken)
                ?? throw new NotSupportedException("OpenTarget service не должен вызываться в этом тесте.");
        }
    }

    private sealed class FakeToolExecutionGate(ToolExecutionDecision decision) : IToolExecutionGate
    {
        public int Calls { get; private set; }

        public ToolExecutionIntent? LastIntent { get; private set; }

        public ToolExecutionDecision Evaluate(ToolExecutionPolicyDescriptor policy, ToolExecutionIntent intent) =>
            RecordIntentAndReturnDecision(intent);

        public ToolExecutionDecision Evaluate(
            ToolExecutionPolicyDescriptor policy,
            RuntimeGuardAssessment assessment,
            ToolExecutionIntent intent) =>
            RecordIntentAndReturnDecision(intent);

        private ToolExecutionDecision RecordIntentAndReturnDecision(ToolExecutionIntent intent)
        {
            Calls++;
            LastIntent = intent;
            return decision;
        }
    }
}
