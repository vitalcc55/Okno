// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Guards;
using WinBridge.Runtime.Tooling;

namespace WinBridge.Runtime.Tests;

public sealed class ToolExecutionTests
{
    private static readonly string[] LaunchProcessArguments =
    [
        "--token=super-secret",
    ];

    [Fact]
    public void RunLogsFailedOutcomeWhenCallbackThrows()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = new(
            ContentRootPath: root,
            EnvironmentName: "Tests",
            RunId: "run-004",
            DiagnosticsRoot: Path.Combine(root, "artifacts", "diagnostics"),
            RunDirectory: Path.Combine(root, "artifacts", "diagnostics", "run-004"),
            EventsPath: Path.Combine(root, "artifacts", "diagnostics", "run-004", "events.jsonl"),
            SummaryPath: Path.Combine(root, "artifacts", "diagnostics", "run-004", "summary.md"));
        AuditLog auditLog = new(options, TimeProvider.System);
        SessionSnapshot snapshot = SessionSnapshot.CreateInitial("run-004", DateTimeOffset.UtcNow);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => ToolExecution.Run<int>(
                auditLog,
                snapshot,
                "okno.health",
                null,
                _ => throw new InvalidOperationException("boom")));

        Assert.Equal("boom", exception.Message);

        string[] lines = File.ReadAllLines(options.EventsPath);
        Assert.Equal(2, lines.Length);
        Assert.Contains("\"outcome\":\"failed\"", lines[1], StringComparison.Ordinal);
        Assert.Contains("\"exception_type\":\"System.InvalidOperationException\"", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void RunRejectsPolicyBearingToolDescriptorsOnRawPath()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateOptions(root, "run-raw-policy-reject");
        AuditLog auditLog = new(options, TimeProvider.System);
        SessionSnapshot snapshot = SessionSnapshot.CreateInitial("run-raw-policy-reject", DateTimeOffset.UtcNow);
        bool callbackCalled = false;

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => ToolExecution.Run<int>(
                auditLog,
                snapshot,
                ToolNames.WindowsInput,
                new { action = "click" },
                _ =>
                {
                    callbackCalled = true;
                    return 0;
                }));

        Assert.False(callbackCalled);
        Assert.Contains(ToolNames.WindowsInput, exception.Message, StringComparison.Ordinal);
        Assert.Contains("RunGated", exception.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(options.EventsPath));
    }

    [Fact]
    public async Task RunAsyncLogsFailedOutcomeWhenCallbackThrows()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateOptions(root, "run-005");
        AuditLog auditLog = new(options, TimeProvider.System);
        SessionSnapshot snapshot = SessionSnapshot.CreateInitial("run-005", DateTimeOffset.UtcNow);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ToolExecution.RunAsync<int>(
                auditLog,
                snapshot,
                "okno.contract",
                null,
                _ => throw new InvalidOperationException("boom-async")));

        Assert.Equal("boom-async", exception.Message);

        string[] lines = File.ReadAllLines(options.EventsPath);
        Assert.Equal(2, lines.Length);
        Assert.Contains("\"outcome\":\"failed\"", lines[1], StringComparison.Ordinal);
        Assert.Contains("\"exception_type\":\"System.InvalidOperationException\"", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsyncRejectsPolicyBearingToolDescriptorsOnRawPath()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateOptions(root, "run-raw-policy-reject-async");
        AuditLog auditLog = new(options, TimeProvider.System);
        SessionSnapshot snapshot = SessionSnapshot.CreateInitial("run-raw-policy-reject-async", DateTimeOffset.UtcNow);
        bool callbackCalled = false;

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ToolExecution.RunAsync<int>(
                auditLog,
                snapshot,
                ToolNames.WindowsClipboardSet,
                new { text = "secret" },
                _ =>
                {
                    callbackCalled = true;
                    return Task.FromResult(0);
                }));

        Assert.False(callbackCalled);
        Assert.Contains(ToolNames.WindowsClipboardSet, exception.Message, StringComparison.Ordinal);
        Assert.Contains("RunGatedAsync", exception.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(options.EventsPath));
    }

    [Fact]
    public void RunDeferredAllowsDeferredPolicyBearingDescriptor()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateOptions(root, "run-deferred-allowed");
        AuditLog auditLog = new(options, TimeProvider.System);
        SessionSnapshot snapshot = SessionSnapshot.CreateInitial("run-deferred-allowed", DateTimeOffset.UtcNow);

        string result = ToolExecution.RunDeferred(
            auditLog,
            snapshot,
            ToolNames.WindowsClipboardGet,
            new { deferred = true },
            invocation =>
            {
                invocation.Complete("unsupported", "deferred");
                return "deferred";
            });

        Assert.Equal("deferred", result);

        string[] lines = File.ReadAllLines(options.EventsPath);
        Assert.Equal(2, lines.Length);
        Assert.Contains("\"outcome\":\"unsupported\"", lines[1], StringComparison.Ordinal);
        Assert.Contains($"\"tool_name\":\"{ToolNames.WindowsClipboardGet}\"", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void RunDeferredRejectsImplementedDescriptor()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateOptions(root, "run-deferred-reject");
        AuditLog auditLog = new(options, TimeProvider.System);
        SessionSnapshot snapshot = SessionSnapshot.CreateInitial("run-deferred-reject", DateTimeOffset.UtcNow);
        bool callbackCalled = false;

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => ToolExecution.RunDeferred(
                auditLog,
                snapshot,
                ToolNames.WindowsLaunchProcess,
                new { dryRun = true },
                _ =>
                {
                    callbackCalled = true;
                    return 0;
                }));

        Assert.False(callbackCalled);
        Assert.Contains(ToolNames.WindowsLaunchProcess, exception.Message, StringComparison.Ordinal);
        Assert.Contains("RunDeferred", exception.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(options.EventsPath));
    }

    [Fact]
    public void RunGatedRoutesRejectedDecisionWithoutCallingAllowedCallback()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateOptions(root, "run-006");
        AuditLog auditLog = new(options, TimeProvider.System);
        SessionSnapshot snapshot = SessionSnapshot.CreateInitial("run-006", DateTimeOffset.UtcNow);
        bool allowedCalled = false;

        string result = ToolExecution.RunGated(
            auditLog,
            snapshot,
            "windows.input",
            new { dryRun = false },
            CreatePolicy(supportsDryRun: false),
            ToolExecutionIntent.Default,
            new StubToolExecutionGate(
                new ToolExecutionDecision(
                    Kind: ToolExecutionDecisionKind.Blocked,
                    Mode: ToolExecutionMode.Live,
                    RiskLevel: ToolExecutionRiskLevel.Destructive,
                    Reasons:
                    [
                        new GuardReason(
                            GuardReasonCodeValues.InputUipiBarrierPresent,
                            GuardSeverityValues.Blocked,
                            "Future input path не может обещать higher-integrity interaction без uiAccess.",
                            CapabilitySummaryValues.Input),
                    ],
                    RequiresConfirmation: false,
                    DryRunSupported: false,
                    GuardCapability: CapabilitySummaryValues.Input)),
            (invocation, decision) =>
            {
                allowedCalled = true;
                invocation.Complete("done", "allowed");
                return "allowed";
            },
            (invocation, decision) =>
            {
                invocation.Complete("blocked", "blocked");
                return "rejected";
            });

        Assert.False(allowedCalled);
        Assert.Equal("rejected", result);

        string completedEvent = File.ReadAllLines(options.EventsPath)[1];
        Assert.Contains("\"gate_decision\":\"blocked\"", completedEvent, StringComparison.Ordinal);
        Assert.Contains("\"gate_risk_level\":\"destructive\"", completedEvent, StringComparison.Ordinal);
        Assert.Contains("\"gate_guard_capability\":\"input\"", completedEvent, StringComparison.Ordinal);
        Assert.Contains("\"gate_requires_confirmation\":\"false\"", completedEvent, StringComparison.Ordinal);
        Assert.Contains("\"gate_dry_run_supported\":\"false\"", completedEvent, StringComparison.Ordinal);
        Assert.Contains("\"gate_reason_codes\":\"input_uipi_barrier_present\"", completedEvent, StringComparison.Ordinal);
    }

    [Fact]
    public void RunGatedRoutesDryRunDecisionToAllowedCallback()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateOptions(root, "run-007");
        AuditLog auditLog = new(options, TimeProvider.System);
        SessionSnapshot snapshot = SessionSnapshot.CreateInitial("run-007", DateTimeOffset.UtcNow);
        ToolExecutionMode? observedMode = null;

        string result = ToolExecution.RunGated(
            auditLog,
            snapshot,
            "windows.clipboard_set",
            new { dryRun = true },
            CreatePolicy(supportsDryRun: true),
            new ToolExecutionIntent(
                IsDryRunRequested: true,
                ConfirmationGranted: false,
                PreviewAvailable: true),
            new StubToolExecutionGate(
                new ToolExecutionDecision(
                    Kind: ToolExecutionDecisionKind.Allowed,
                    Mode: ToolExecutionMode.DryRun,
                    RiskLevel: ToolExecutionRiskLevel.High,
                    Reasons: [],
                    RequiresConfirmation: false,
                    DryRunSupported: true,
                    GuardCapability: CapabilitySummaryValues.Clipboard)),
            (invocation, decision) =>
            {
                observedMode = decision.Mode;
                invocation.Complete("done", "dry-run");
                return "allowed";
            },
            (invocation, decision) =>
            {
                invocation.Complete("blocked", "blocked");
                return "rejected";
            });

        Assert.Equal("allowed", result);
        Assert.Equal(ToolExecutionMode.DryRun, observedMode);
    }

    [Fact]
    public void RunGatedLogsFailedOutcomeWhenAllowedCallbackThrows()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateOptions(root, "run-008");
        AuditLog auditLog = new(options, TimeProvider.System);
        SessionSnapshot snapshot = SessionSnapshot.CreateInitial("run-008", DateTimeOffset.UtcNow);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => ToolExecution.RunGated<int>(
                auditLog,
                snapshot,
                "windows.launch_process",
                new { dryRun = false },
                CreatePolicy(supportsDryRun: true),
                new ToolExecutionIntent(
                    IsDryRunRequested: false,
                    ConfirmationGranted: true,
                    PreviewAvailable: true),
                new StubToolExecutionGate(
                    new ToolExecutionDecision(
                        Kind: ToolExecutionDecisionKind.Allowed,
                        Mode: ToolExecutionMode.Live,
                        RiskLevel: ToolExecutionRiskLevel.High,
                        Reasons: [],
                        RequiresConfirmation: false,
                        DryRunSupported: true,
                        GuardCapability: CapabilitySummaryValues.Launch)),
                (_, _) => throw new InvalidOperationException("boom-gated"),
                (invocation, decision) =>
                {
                    invocation.Complete("blocked", "blocked");
                    return 0;
                }));

        Assert.Equal("boom-gated", exception.Message);

        string[] lines = File.ReadAllLines(options.EventsPath);
        Assert.Equal(2, lines.Length);
        Assert.Contains("\"outcome\":\"failed\"", lines[1], StringComparison.Ordinal);
        Assert.Contains("\"exception_type\":\"System.InvalidOperationException\"", lines[1], StringComparison.Ordinal);
        Assert.DoesNotContain("boom-gated", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void RunGatedSuppressesRawUnexpectedExceptionMessageForRedactedTool()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateOptions(root, "run-009");
        AuditLog auditLog = new(options, TimeProvider.System);
        SessionSnapshot snapshot = SessionSnapshot.CreateInitial("run-009", DateTimeOffset.UtcNow);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => ToolExecution.RunGated<int>(
                auditLog,
                snapshot,
                "windows.launch_process",
                new
                {
                    executable = @"C:\tools\demo.exe",
                    args = LaunchProcessArguments,
                },
                new ToolExecutionPolicyDescriptor(
                    PolicyGroup: ToolExecutionPolicyGroup.Launch,
                    RiskLevel: ToolExecutionRiskLevel.High,
                    GuardCapability: CapabilitySummaryValues.Launch,
                    SupportsDryRun: true,
                    ConfirmationMode: ToolExecutionConfirmationMode.Required,
                    RedactionClass: ToolExecutionRedactionClass.LaunchPayload),
                new ToolExecutionIntent(
                    IsDryRunRequested: false,
                    ConfirmationGranted: true,
                    PreviewAvailable: true),
                new StubToolExecutionGate(
                    new ToolExecutionDecision(
                        Kind: ToolExecutionDecisionKind.Allowed,
                        Mode: ToolExecutionMode.Live,
                        RiskLevel: ToolExecutionRiskLevel.High,
                        Reasons: [],
                        RequiresConfirmation: false,
                        DryRunSupported: true,
                        GuardCapability: CapabilitySummaryValues.Launch)),
                (_, _) => throw new InvalidOperationException("launch failed with --token=super-secret"),
                (invocation, decision) =>
                {
                    invocation.Complete("blocked", "blocked");
                    return 0;
                }));

        Assert.Equal("launch failed with --token=super-secret", exception.Message);

        string completedEvent = File.ReadAllLines(options.EventsPath)[1];
        Assert.Contains("\"redaction_applied\":\"true\"", completedEvent, StringComparison.Ordinal);
        Assert.Contains("\"redaction_class\":\"launch_payload\"", completedEvent, StringComparison.Ordinal);
        Assert.Contains("\"redacted_fields\":\"exception_message\"", completedEvent, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret", completedEvent, StringComparison.Ordinal);
    }

    private static AuditLogOptions CreateOptions(string root, string runId) =>
        new(
            ContentRootPath: root,
            EnvironmentName: "Tests",
            RunId: runId,
            DiagnosticsRoot: Path.Combine(root, "artifacts", "diagnostics"),
            RunDirectory: Path.Combine(root, "artifacts", "diagnostics", runId),
            EventsPath: Path.Combine(root, "artifacts", "diagnostics", runId, "events.jsonl"),
            SummaryPath: Path.Combine(root, "artifacts", "diagnostics", runId, "summary.md"));

    private static ToolExecutionPolicyDescriptor CreatePolicy(bool supportsDryRun) =>
        new(
            PolicyGroup: ToolExecutionPolicyGroup.Input,
            RiskLevel: ToolExecutionRiskLevel.Destructive,
            GuardCapability: CapabilitySummaryValues.Input,
            SupportsDryRun: supportsDryRun,
            ConfirmationMode: ToolExecutionConfirmationMode.Required,
            RedactionClass: ToolExecutionRedactionClass.None);

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class StubToolExecutionGate(ToolExecutionDecision decision) : IToolExecutionGate
    {
        public ToolExecutionDecision Evaluate(ToolExecutionPolicyDescriptor policy, ToolExecutionIntent intent) => decision;

        public ToolExecutionDecision Evaluate(
            ToolExecutionPolicyDescriptor policy,
            RuntimeGuardAssessment assessment,
            ToolExecutionIntent intent) => decision;
    }
}
