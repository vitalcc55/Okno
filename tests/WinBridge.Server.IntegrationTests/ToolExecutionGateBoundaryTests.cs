// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Guards;
using WinBridge.Runtime.Session;
using WinBridge.Runtime.Tooling;
using WinBridge.Runtime.Windows.Display;
using RuntimeToolExecution = WinBridge.Runtime.Diagnostics.ToolExecution;

namespace WinBridge.Server.IntegrationTests;

public sealed class ToolExecutionGateBoundaryTests
{
    [Fact]
    public void ExecuteReturnsBlockedPayloadWithoutInvokingAllowedPath()
    {
        SyntheticGatedBoundaryHost host = CreateHost();

        CallToolResult result = host.Execute(
            CreatePolicy(
                ToolExecutionPolicyGroup.Input,
                ToolExecutionRiskLevel.Destructive,
                CapabilitySummaryValues.Input,
                supportsDryRun: false,
                ToolExecutionConfirmationMode.Required),
            ToolExecutionIntent.Default);

        JsonElement payload = AssertStructuredPayload(result);
        Assert.True(result.IsError);
        Assert.Equal("blocked", payload.GetProperty("status").GetString());
        Assert.Equal("blocked", payload.GetProperty("decision").GetString());
        Assert.Equal("destructive", payload.GetProperty("riskLevel").GetString());
        Assert.Equal(CapabilitySummaryValues.Input, payload.GetProperty("guardCapability").GetString());
        Assert.False(payload.GetProperty("requiresConfirmation").GetBoolean());
        Assert.False(payload.GetProperty("dryRunSupported").GetBoolean());
        Assert.Equal(0, host.AllowedInvocationCount);
        Assert.Equal(1, host.RejectedInvocationCount);
        string completedEvent = host.ReadCompletedEvent();
        Assert.Contains("\"gate_decision\":\"blocked\"", completedEvent, StringComparison.Ordinal);
        Assert.Contains("\"gate_risk_level\":\"destructive\"", completedEvent, StringComparison.Ordinal);
        Assert.Contains("\"gate_guard_capability\":\"input\"", completedEvent, StringComparison.Ordinal);
        Assert.Contains("\"gate_reason_codes\":\"input_uipi_barrier_present\"", completedEvent, StringComparison.Ordinal);
    }

    [Fact]
    public void ExecuteReturnsNeedsConfirmationPayloadWithoutInvokingAllowedPath()
    {
        SyntheticGatedBoundaryHost host = CreateHost();

        CallToolResult result = host.Execute(
            CreatePolicy(
                ToolExecutionPolicyGroup.Launch,
                ToolExecutionRiskLevel.High,
                CapabilitySummaryValues.Launch,
                supportsDryRun: true,
                ToolExecutionConfirmationMode.Required),
            ToolExecutionIntent.Default);

        JsonElement payload = AssertStructuredPayload(result);
        Assert.True(result.IsError);
        Assert.Equal("needs_confirmation", payload.GetProperty("status").GetString());
        Assert.Equal("needs_confirmation", payload.GetProperty("decision").GetString());
        Assert.Equal("high", payload.GetProperty("riskLevel").GetString());
        Assert.Equal(CapabilitySummaryValues.Launch, payload.GetProperty("guardCapability").GetString());
        Assert.True(payload.GetProperty("requiresConfirmation").GetBoolean());
        Assert.True(payload.GetProperty("dryRunSupported").GetBoolean());
        Assert.Equal(0, host.AllowedInvocationCount);
        Assert.Equal(1, host.RejectedInvocationCount);
    }

    [Fact]
    public async Task ExecuteAsyncReturnsDryRunOnlyPayloadWithoutInvokingAllowedPath()
    {
        SyntheticGatedBoundaryHost host = CreateHost();

        CallToolResult result = await host.ExecuteAsync(
            CreatePolicy(
                ToolExecutionPolicyGroup.Clipboard,
                ToolExecutionRiskLevel.High,
                CapabilitySummaryValues.Clipboard,
                supportsDryRun: true,
                ToolExecutionConfirmationMode.Required),
            new ToolExecutionIntent(
                IsDryRunRequested: false,
                ConfirmationGranted: false,
                PreviewAvailable: true));

        JsonElement payload = AssertStructuredPayload(result);
        Assert.True(result.IsError);
        Assert.Equal("dry_run_only", payload.GetProperty("status").GetString());
        Assert.Equal("dry_run_only", payload.GetProperty("decision").GetString());
        Assert.Equal("high", payload.GetProperty("riskLevel").GetString());
        Assert.Equal(CapabilitySummaryValues.Clipboard, payload.GetProperty("guardCapability").GetString());
        Assert.False(payload.GetProperty("requiresConfirmation").GetBoolean());
        Assert.True(payload.GetProperty("dryRunSupported").GetBoolean());
        Assert.Equal(0, host.AllowedInvocationCount);
        Assert.Equal(1, host.RejectedInvocationCount);
    }

    [Fact]
    public void ExecuteReturnsBlockedPayloadForUnsupportedDryRunInsteadOfThrowing()
    {
        SyntheticGatedBoundaryHost host = CreateHost();

        CallToolResult result = host.Execute(
            CreatePolicy(
                ToolExecutionPolicyGroup.Input,
                ToolExecutionRiskLevel.Destructive,
                CapabilitySummaryValues.Input,
                supportsDryRun: false,
                ToolExecutionConfirmationMode.Required),
            new ToolExecutionIntent(
                IsDryRunRequested: true,
                ConfirmationGranted: false,
                PreviewAvailable: true));

        JsonElement payload = AssertStructuredPayload(result);
        Assert.True(result.IsError);
        Assert.Equal("blocked", payload.GetProperty("status").GetString());
        Assert.Equal("blocked", payload.GetProperty("decision").GetString());
        Assert.Equal(CapabilitySummaryValues.Input, payload.GetProperty("guardCapability").GetString());
        Assert.Equal(0, host.AllowedInvocationCount);
        Assert.Equal(1, host.RejectedInvocationCount);
        Assert.Equal(
            GuardReasonCodeValues.CapabilityDryRunNotSupported,
            payload.GetProperty("reasons")[0].GetProperty("code").GetString());
    }

    private static SyntheticGatedBoundaryHost CreateHost()
    {
        RuntimeGuardAssessment assessment = CreateAssessment(
            CreateCapability(
                CapabilitySummaryValues.Input,
                GuardStatusValues.Blocked,
                CreateReason(
                    GuardReasonCodeValues.InputUipiBarrierPresent,
                    GuardSeverityValues.Blocked,
                    CapabilitySummaryValues.Input,
                    "Future input path не может обещать higher-integrity interaction без uiAccess.")),
            CreateCapability(
                CapabilitySummaryValues.Clipboard,
                GuardStatusValues.Blocked,
                CreateReason(
                    GuardReasonCodeValues.ClipboardIntegrityLimited,
                    GuardSeverityValues.Blocked,
                    CapabilitySummaryValues.Clipboard,
                    "Clipboard path пока не должен обещать операции при неполном integrity profile.")),
            CreateCapability(
                CapabilitySummaryValues.Launch,
                GuardStatusValues.Degraded,
                CreateReason(
                    GuardReasonCodeValues.LaunchElevationBoundaryUnconfirmed,
                    GuardSeverityValues.Warning,
                    CapabilitySummaryValues.Launch,
                    "Live launch path остаётся confirmation-worthy: higher-integrity boundary заранее не подтверждена.")));

        return new SyntheticGatedBoundaryHost(assessment);
    }

    private static ToolExecutionPolicyDescriptor CreatePolicy(
        ToolExecutionPolicyGroup policyGroup,
        ToolExecutionRiskLevel riskLevel,
        string guardCapability,
        bool supportsDryRun,
        ToolExecutionConfirmationMode confirmationMode) =>
        new(
            PolicyGroup: policyGroup,
            RiskLevel: riskLevel,
            GuardCapability: guardCapability,
            SupportsDryRun: supportsDryRun,
            ConfirmationMode: confirmationMode,
            RedactionClass: ToolExecutionRedactionClass.None);

    private static RuntimeGuardAssessment CreateAssessment(params CapabilityGuardSummary[] capabilities)
    {
        RuntimeReadinessSnapshot readiness = new(
            CapturedAtUtc: DateTimeOffset.UtcNow,
            Domains: [],
            Capabilities: capabilities);

        return new RuntimeGuardAssessment(
            Topology: new DisplayTopologySnapshot(
                Monitors: [],
                Diagnostics: new DisplayIdentityDiagnostics(
                    IdentityMode: DisplayIdentityModeValues.DisplayConfigStrong,
                    FailedStage: null,
                    ErrorCode: null,
                    ErrorName: null,
                    MessageHuman: "Strong monitor identity resolved through QueryDisplayConfig for all active desktop monitors.",
                    CapturedAtUtc: DateTimeOffset.UtcNow)),
            Readiness: readiness,
            BlockedCapabilities:
            [
                .. capabilities.Where(item => item.Status == GuardStatusValues.Blocked),
            ],
            Warnings: []);
    }

    private static CapabilityGuardSummary CreateCapability(
        string capability,
        string status,
        params GuardReason[] reasons) =>
        new(
            Capability: capability,
            Status: status,
            Reasons: reasons);

    private static GuardReason CreateReason(
        string code,
        string severity,
        string source,
        string message) =>
        new(
            Code: code,
            Severity: severity,
            MessageHuman: message,
            Source: source);

    private static JsonElement AssertStructuredPayload(CallToolResult result)
    {
        Assert.NotNull(result.StructuredContent);
        return result.StructuredContent!.Value;
    }

    private sealed class SyntheticGatedBoundaryHost
    {
        private static readonly JsonSerializerOptions PayloadJsonOptions = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        private readonly AuditLog _auditLog;
        private readonly string _eventsPath;
        private readonly IToolExecutionGate _gate;
        private readonly InMemorySessionManager _sessionManager;

        public SyntheticGatedBoundaryHost(RuntimeGuardAssessment assessment)
        {
            string root = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            AuditLogOptions options = new(
                ContentRootPath: root,
                EnvironmentName: "Tests",
                RunId: "tool-execution-gate-boundary",
                DiagnosticsRoot: Path.Combine(root, "artifacts", "diagnostics"),
                RunDirectory: Path.Combine(root, "artifacts", "diagnostics", "tool-execution-gate-boundary"),
                EventsPath: Path.Combine(root, "artifacts", "diagnostics", "tool-execution-gate-boundary", "events.jsonl"),
                SummaryPath: Path.Combine(root, "artifacts", "diagnostics", "tool-execution-gate-boundary", "summary.md"));

            _auditLog = new AuditLog(options, TimeProvider.System);
            _eventsPath = options.EventsPath;
            _sessionManager = new InMemorySessionManager(TimeProvider.System, new SessionContext("tool-execution-gate-boundary"));
            _gate = new ToolExecutionGate(new FakeRuntimeGuardService(assessment));
        }

        public int AllowedInvocationCount { get; private set; }

        public int RejectedInvocationCount { get; private set; }

        public string ReadCompletedEvent() =>
            File.ReadAllLines(_eventsPath)
                .Single(line => line.Contains("\"event_name\":\"tool.invocation.completed\"", StringComparison.Ordinal));

        public CallToolResult Execute(ToolExecutionPolicyDescriptor policy, ToolExecutionIntent intent) =>
            RuntimeToolExecution.RunGated(
                _auditLog,
                _sessionManager.GetSnapshot(),
                "tests.synthetic_gate",
                new { intent.IsDryRunRequested, intent.ConfirmationGranted, intent.PreviewAvailable },
                policy,
                intent,
                _gate,
                (invocation, decision) =>
                {
                    AllowedInvocationCount++;
                    invocation.Complete("done", "Synthetic execution path allowed.");
                    return CreateToolResult("done", decision, isError: false);
                },
                (invocation, decision) =>
                {
                    RejectedInvocationCount++;
                    string status = ToStatus(decision.Kind);
                    invocation.Complete(status, $"Synthetic execution path returned `{status}`.");
                    return CreateToolResult(status, decision, isError: true);
                });

        public Task<CallToolResult> ExecuteAsync(ToolExecutionPolicyDescriptor policy, ToolExecutionIntent intent) =>
            RuntimeToolExecution.RunGatedAsync(
                _auditLog,
                _sessionManager.GetSnapshot(),
                "tests.synthetic_gate_async",
                new { intent.IsDryRunRequested, intent.ConfirmationGranted, intent.PreviewAvailable },
                policy,
                intent,
                _gate,
                (invocation, decision) =>
                {
                    AllowedInvocationCount++;
                    invocation.Complete("done", "Synthetic execution path allowed.");
                    return Task.FromResult(CreateToolResult("done", decision, isError: false));
                },
                (invocation, decision) =>
                {
                    RejectedInvocationCount++;
                    string status = ToStatus(decision.Kind);
                    invocation.Complete(status, $"Synthetic execution path returned `{status}`.");
                    return Task.FromResult(CreateToolResult(status, decision, isError: true));
                });

        private static CallToolResult CreateToolResult(string status, ToolExecutionDecision decision, bool isError)
        {
            JsonElement structuredContent = JsonSerializer.SerializeToElement(
                new SyntheticBoundaryPayload(
                    Status: status,
                    Decision: ToStatus(decision.Kind),
                    RiskLevel: ToSnakeCase(decision.RiskLevel),
                    GuardCapability: decision.GuardCapability,
                    RequiresConfirmation: decision.RequiresConfirmation,
                    DryRunSupported: decision.DryRunSupported,
                    Reasons: decision.Reasons),
                PayloadJsonOptions);

            return new CallToolResult
            {
                IsError = isError,
                StructuredContent = structuredContent,
                Content =
                [
                    new TextContentBlock
                    {
                        Text = JsonSerializer.Serialize(structuredContent, PayloadJsonOptions),
                    },
                ],
            };
        }

        private static string ToStatus(ToolExecutionDecisionKind kind) =>
            kind switch
            {
                ToolExecutionDecisionKind.Allowed => "done",
                ToolExecutionDecisionKind.Blocked => "blocked",
                ToolExecutionDecisionKind.NeedsConfirmation => "needs_confirmation",
                ToolExecutionDecisionKind.DryRunOnly => "dry_run_only",
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
            };

        private static string ToSnakeCase<TEnum>(TEnum value)
            where TEnum : struct, Enum =>
            JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());
    }

    private sealed class FakeRuntimeGuardService(RuntimeGuardAssessment assessment) : IRuntimeGuardService
    {
        public RuntimeGuardAssessment GetSnapshot() => assessment;
    }

    private sealed record SyntheticBoundaryPayload(
        string Status,
        string Decision,
        string RiskLevel,
        string GuardCapability,
        bool RequiresConfirmation,
        bool DryRunSupported,
        IReadOnlyList<GuardReason> Reasons);
}
