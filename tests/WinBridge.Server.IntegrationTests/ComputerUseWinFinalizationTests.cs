using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Session;
using WinBridge.Runtime.Tooling;
using WinBridge.Runtime.Windows.Input;
using WinBridge.Server.ComputerUse;

namespace WinBridge.Server.IntegrationTests;

public sealed class ComputerUseWinFinalizationTests
{
    [Fact]
    public void FailureCompletionPreservesSanitizedExceptionMetadataInAudit()
    {
        string root = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            AuditLogOptions options = CreateAuditOptions(root, "computer-use-win-failure-completion-tests");
            AuditLog auditLog = new(options, TimeProvider.System);
            using AuditInvocationScope invocation = auditLog.BeginInvocation(
                ToolNames.ComputerUseWinGetAppState,
                new { hwnd = 101 },
                new InMemorySessionManager(TimeProvider.System, new SessionContext("computer-use-win-failure-completion-tests")).GetSnapshot());

            ComputerUseWinFailureDetails failure = new(
                ComputerUseWinFailureCodeValues.ObservationFailed,
                "Computer Use for Windows не смог завершить observation stage для get_app_state.",
                new InvalidOperationException("secret observation failure"));

            ComputerUseWinFailureCompletion.CompleteFailure(
                invocation,
                failure.Reason,
                failure.FailureCode,
                targetHwnd: 101,
                auditException: failure.AuditException);

            string completedEvent = File.ReadLines(options.EventsPath)
                .Single(line => line.Contains("\"event_name\":\"tool.invocation.completed\"", StringComparison.Ordinal));
            Assert.Contains("\"exception_type\":\"System.InvalidOperationException\"", completedEvent, StringComparison.Ordinal);
            Assert.DoesNotContain("secret observation failure", completedEvent, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void FinalizerUsesBestEffortAuditAfterSharedStateCommit()
    {
        string root = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            AuditLogOptions options = new(
                ContentRootPath: root,
                EnvironmentName: "Tests",
                RunId: "computer-use-win-finalizer-tests",
                DiagnosticsRoot: Path.Combine(root, "artifacts", "diagnostics"),
                RunDirectory: Path.Combine(root, "artifacts", "diagnostics", "computer-use-win-finalizer-tests"),
                EventsPath: Path.Combine(root, "artifacts", "diagnostics", "computer-use-win-finalizer-tests", "events.jsonl"),
                SummaryPath: Path.Combine(root, "artifacts", "diagnostics", "computer-use-win-finalizer-tests", "summary.md"));
            AuditLog auditLog = new(options, TimeProvider.System);
            InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-finalizer-tests"));
            ComputerUseWinStateStore stateStore = new(TimeProvider.System, TimeSpan.FromSeconds(30), maxEntries: 4);
            WindowDescriptor selectedWindow = CreateWindow();
            ComputerUseWinPreparedAppState preparedState = CreatePreparedState(selectedWindow);

            using AuditInvocationScope invocation = auditLog.BeginInvocation(
                ToolNames.ComputerUseWinGetAppState,
                new { hwnd = selectedWindow.Hwnd },
                sessionManager.GetSnapshot());

            File.Delete(options.EventsPath);
            Directory.CreateDirectory(options.EventsPath);
            File.Delete(options.SummaryPath);
            Directory.CreateDirectory(options.SummaryPath);

            ComputerUseWinExecutionTarget target = CreateExecutionTarget(selectedWindow);
            CallToolResult result = ComputerUseWinGetAppStateFinalizer.FinalizeSuccess(
                invocation,
                target,
                selectedWindow,
                preparedState,
                stateStore,
                sessionManager);

            Assert.False(result.IsError);
            JsonElement payload = result.StructuredContent!.Value;
            string stateToken = payload.GetProperty("stateToken").GetString()!;
            Assert.True(stateStore.TryGet(stateToken, out ComputerUseWinStoredState? storedState));
            Assert.NotNull(storedState);
            Assert.NotEqual(default, storedState!.IssuedAtUtc);
            Assert.Equal(preparedState.StoredState with { IssuedAtUtc = storedState.IssuedAtUtc }, storedState);
            Assert.Equal(selectedWindow.Hwnd, sessionManager.GetAttachedWindow()?.Window.Hwnd);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void FinalizerDoesNotLeakStateTokenInCompletionAudit()
    {
        string root = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            AuditLogOptions options = CreateAuditOptions(root, "computer-use-win-finalizer-audit-state-token-tests");
            AuditLog auditLog = new(options, TimeProvider.System);
            InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-finalizer-audit-state-token-tests"));
            ComputerUseWinStateStore stateStore = new(TimeProvider.System, TimeSpan.FromSeconds(30), maxEntries: 4);
            WindowDescriptor selectedWindow = CreateWindow();
            ComputerUseWinPreparedAppState preparedState = CreatePreparedState(selectedWindow);

            using AuditInvocationScope invocation = auditLog.BeginInvocation(
                ToolNames.ComputerUseWinGetAppState,
                new { hwnd = selectedWindow.Hwnd },
                sessionManager.GetSnapshot());

            _ = ComputerUseWinGetAppStateFinalizer.FinalizeSuccess(
                invocation,
                CreateExecutionTarget(selectedWindow),
                selectedWindow,
                preparedState,
                stateStore,
                sessionManager);

            string completedEvent = File.ReadLines(options.EventsPath)
                .Single(line => line.Contains("\"event_name\":\"tool.invocation.completed\"", StringComparison.Ordinal));
            Assert.DoesNotContain("\"state_token\":", completedEvent, StringComparison.Ordinal);
            Assert.Contains("\"state_token_present\":\"true\"", completedEvent, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void ActionFinalizerUsesBestEffortAuditAfterCommittedSideEffect()
    {
        string root = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            AuditLogOptions options = CreateAuditOptions(root, "computer-use-win-action-finalizer-tests");
            AuditLog auditLog = new(options, TimeProvider.System);
            using AuditInvocationScope invocation = auditLog.BeginInvocation(
                ToolNames.ComputerUseWinClick,
                new { stateToken = "token-1", elementIndex = 1 },
                new InMemorySessionManager(TimeProvider.System, new SessionContext("computer-use-win-action-finalizer-tests")).GetSnapshot());

            File.Delete(options.EventsPath);
            Directory.CreateDirectory(options.EventsPath);
            File.Delete(options.SummaryPath);
            Directory.CreateDirectory(options.SummaryPath);

            CallToolResult result = ComputerUseWinActionFinalizer.FinalizeResult(
                invocation,
                ToolNames.ComputerUseWinClick,
                targetHwnd: 101,
                elementIndex: 1,
                new InputResult(
                    Status: InputStatusValues.Done,
                    Decision: InputStatusValues.Done,
                    FailureCode: null,
                    Reason: null,
                    TargetHwnd: 101));

            Assert.False(result.IsError);
            JsonElement payload = result.StructuredContent!.Value;
            Assert.Equal(ComputerUseWinStatusValues.Done, payload.GetProperty("status").GetString());
            Assert.True(payload.GetProperty("refreshStateRecommended").GetBoolean());
            Assert.Equal(101, payload.GetProperty("targetHwnd").GetInt64());
            Assert.Equal(1, payload.GetProperty("elementIndex").GetInt32());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void ActionFinalizerTranslatesInternalReasonToPublicMessage()
    {
        string root = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            AuditLogOptions options = CreateAuditOptions(root, "computer-use-win-action-reason-translation-tests");
            AuditLog auditLog = new(options, TimeProvider.System);
            using AuditInvocationScope invocation = auditLog.BeginInvocation(
                ToolNames.ComputerUseWinClick,
                new { stateToken = "token-1", elementIndex = 1 },
                new InMemorySessionManager(TimeProvider.System, new SessionContext("computer-use-win-action-reason-translation-tests")).GetSnapshot());

            CallToolResult result = ComputerUseWinActionFinalizer.FinalizeResult(
                invocation,
                ToolNames.ComputerUseWinClick,
                targetHwnd: 101,
                elementIndex: 1,
                new InputResult(
                    Status: InputStatusValues.Failed,
                    Decision: InputStatusValues.Failed,
                    FailureCode: InputFailureCodeValues.TargetNotForeground,
                    Reason: "windows.input target_not_foreground preflight rejected dispatch.",
                    TargetHwnd: 101));

            Assert.True(result.IsError);
            JsonElement payload = result.StructuredContent!.Value;
            Assert.Equal(ComputerUseWinFailureCodeValues.TargetNotForeground, payload.GetProperty("failureCode").GetString());
            string reason = payload.GetProperty("reason").GetString()!;
            Assert.DoesNotContain("windows.input", reason, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("target_not_foreground", reason, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void ActionFinalizerDoesNotLeakRawReasonInCompletionAudit()
    {
        string root = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            AuditLogOptions options = CreateAuditOptions(root, "computer-use-win-action-raw-reason-audit-tests");
            AuditLog auditLog = new(options, TimeProvider.System);
            using AuditInvocationScope invocation = auditLog.BeginInvocation(
                ToolNames.ComputerUseWinClick,
                new { stateToken = "token-1", elementIndex = 1 },
                new InMemorySessionManager(TimeProvider.System, new SessionContext("computer-use-win-action-raw-reason-audit-tests")).GetSnapshot());

            _ = ComputerUseWinActionFinalizer.FinalizeResult(
                invocation,
                ToolNames.ComputerUseWinClick,
                targetHwnd: 101,
                elementIndex: 1,
                new InputResult(
                    Status: InputStatusValues.Failed,
                    Decision: InputStatusValues.Failed,
                    FailureCode: InputFailureCodeValues.TargetNotForeground,
                    Reason: "windows.input target_not_foreground preflight rejected dispatch.",
                    TargetHwnd: 101));

            string completedEvent = File.ReadLines(options.EventsPath)
                .Single(line => line.Contains("\"event_name\":\"tool.invocation.completed\"", StringComparison.Ordinal));
            Assert.DoesNotContain("\"raw_reason\":", completedEvent, StringComparison.Ordinal);
            Assert.DoesNotContain("windows.input target_not_foreground", completedEvent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"public_reason\":", completedEvent, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void ActionFinalizerPublishesFailureCodeForUnexpectedInternalFailure()
    {
        string root = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            AuditLogOptions options = CreateAuditOptions(root, "computer-use-win-action-unexpected-code-tests");
            AuditLog auditLog = new(options, TimeProvider.System);
            using AuditInvocationScope invocation = auditLog.BeginInvocation(
                ToolNames.ComputerUseWinClick,
                new { stateToken = "token-1", elementIndex = 1 },
                new InMemorySessionManager(TimeProvider.System, new SessionContext("computer-use-win-action-unexpected-code-tests")).GetSnapshot());

            CallToolResult result = ComputerUseWinActionFinalizer.FinalizeUnexpectedFailure(
                invocation,
                ToolNames.ComputerUseWinClick,
                targetHwnd: 101,
                elementIndex: 1,
                exception: new InvalidOperationException("secret setup failure"));

            Assert.True(result.IsError);
            JsonElement payload = result.StructuredContent!.Value;
            Assert.Equal(ComputerUseWinFailureCodeValues.UnexpectedInternalFailure, payload.GetProperty("failureCode").GetString());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void ActionFinalizerMapsInternalFailureCodesToPublicVocabulary()
    {
        string root = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            AuditLogOptions options = CreateAuditOptions(root, "computer-use-win-action-code-mapping-tests");
            AuditLog auditLog = new(options, TimeProvider.System);
            using AuditInvocationScope invocation = auditLog.BeginInvocation(
                ToolNames.ComputerUseWinClick,
                new { stateToken = "token-1", elementIndex = 1 },
                new InMemorySessionManager(TimeProvider.System, new SessionContext("computer-use-win-action-code-mapping-tests")).GetSnapshot());

            CallToolResult result = ComputerUseWinActionFinalizer.FinalizeResult(
                invocation,
                ToolNames.ComputerUseWinClick,
                targetHwnd: 101,
                elementIndex: 1,
                new InputResult(
                    Status: InputStatusValues.Failed,
                    Decision: InputStatusValues.Failed,
                    FailureCode: InputFailureCodeValues.UnsupportedActionType,
                    Reason: "unsupported action type",
                    TargetHwnd: 101));

            Assert.True(result.IsError);
            JsonElement payload = result.StructuredContent!.Value;
            Assert.Equal(ComputerUseWinFailureCodeValues.UnsupportedAction, payload.GetProperty("failureCode").GetString());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void ActionFinalizerIncludesEvidenceInTopLevelCompletionAudit()
    {
        string root = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            AuditLogOptions options = CreateAuditOptions(root, "computer-use-win-action-evidence-tests");
            AuditLog auditLog = new(options, TimeProvider.System);
            using AuditInvocationScope invocation = auditLog.BeginInvocation(
                ToolNames.ComputerUseWinClick,
                new { stateToken = "token-1", elementIndex = 1 },
                new InMemorySessionManager(TimeProvider.System, new SessionContext("computer-use-win-action-evidence-tests")).GetSnapshot());

            _ = ComputerUseWinActionFinalizer.FinalizeResult(
                invocation,
                ToolNames.ComputerUseWinClick,
                targetHwnd: 101,
                elementIndex: 1,
                new InputResult(
                    Status: InputStatusValues.VerifyNeeded,
                    Decision: InputStatusValues.VerifyNeeded,
                    ResultMode: "dispatch_only",
                    FailureCode: null,
                    Reason: "Проверь результат клика по приложению вручную.",
                    TargetHwnd: 101,
                    TargetSource: InputTargetSourceValues.Attached,
                    CompletedActionCount: 1,
                    FailedActionIndex: null,
                    ArtifactPath: "C:\\temp\\click.json"));

            string completedEvent = File.ReadLines(options.EventsPath)
                .Single(line => line.Contains("\"event_name\":\"tool.invocation.completed\"", StringComparison.Ordinal));
            Assert.Contains("\"completed_action_count\":\"1\"", completedEvent, StringComparison.Ordinal);
            Assert.Contains("\"artifact_path\":\"C:\\\\temp\\\\click.json\"", completedEvent, StringComparison.Ordinal);
            Assert.Contains("\"result_mode\":\"dispatch_only\"", completedEvent, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void ActionFinalizerDoesNotClaimPostDispatchForPreDispatchUnexpectedFailure()
    {
        string root = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            AuditLogOptions options = CreateAuditOptions(root, "computer-use-win-action-pre-dispatch-failure-tests");
            AuditLog auditLog = new(options, TimeProvider.System);
            using AuditInvocationScope invocation = auditLog.BeginInvocation(
                ToolNames.ComputerUseWinClick,
                new { stateToken = "token-1", elementIndex = 1 },
                new InMemorySessionManager(TimeProvider.System, new SessionContext("computer-use-win-action-pre-dispatch-failure-tests")).GetSnapshot());

            CallToolResult result = ComputerUseWinActionFinalizer.FinalizeUnexpectedFailure(
                invocation,
                ToolNames.ComputerUseWinClick,
                targetHwnd: 101,
                elementIndex: 1,
                exception: new InvalidOperationException("secret setup failure"));

            Assert.True(result.IsError);
            JsonElement payload = result.StructuredContent!.Value;
            Assert.Equal(ComputerUseWinStatusValues.Failed, payload.GetProperty("status").GetString());
            Assert.False(payload.GetProperty("refreshStateRecommended").GetBoolean());
            string reason = payload.GetProperty("reason").GetString()!;
            Assert.Contains("до подтверждённого action dispatch", reason, StringComparison.Ordinal);
            Assert.DoesNotContain("после action dispatch", reason, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData(ComputerUseWinFailureCodeValues.StateRequired)]
    [InlineData(ComputerUseWinFailureCodeValues.StaleState)]
    [InlineData(ComputerUseWinFailureCodeValues.CaptureReferenceRequired)]
    public void StructuredBoundaryFailuresThatNeedFreshStateRecommendRefresh(string failureCode)
    {
        string root = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            ComputerUseWinActionResult payload = ComputerUseWinActionFinalizer.CreateStructuredFailurePayload(
                failureCode,
                "Refresh is required.",
                targetHwnd: 101,
                elementIndex: null,
                phase: ComputerUseWinActionLifecyclePhase.BeforeActivation);

            Assert.True(payload.RefreshStateRecommended);
            Assert.Equal(failureCode, payload.FailureCode);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void BoundaryActionFailureDoesNotRecommendRefreshForMalformedRequestBeforeActivation()
    {
        ComputerUseWinActionResult payload = ComputerUseWinActionFinalizer.CreateStructuredFailurePayload(
            ComputerUseWinFailureCodeValues.InvalidRequest,
            "Malformed click request.",
            targetHwnd: 101,
            elementIndex: null,
            phase: ComputerUseWinActionLifecyclePhase.BeforeActivation);

        Assert.False(payload.RefreshStateRecommended);
        Assert.Equal(ComputerUseWinFailureCodeValues.InvalidRequest, payload.FailureCode);
    }

    [Fact]
    public void BoundaryActionApprovalRequiredDoesNotRecommendRefreshBeforeActivation()
    {
        ComputerUseWinActionResult payload = ComputerUseWinActionFinalizer.CreateStructuredApprovalRequiredPayload(
            "Confirm required.",
            targetHwnd: 101,
            elementIndex: 1,
            phase: ComputerUseWinActionLifecyclePhase.BeforeActivation);

        Assert.False(payload.RefreshStateRecommended);
        Assert.Equal(ComputerUseWinFailureCodeValues.ApprovalRequired, payload.FailureCode);
    }

    [Fact]
    public void StructuredActionFailureRecommendsRefreshAfterActivationBeforeDispatch()
    {
        ComputerUseWinActionResult payload = ComputerUseWinActionFinalizer.CreateStructuredFailurePayload(
            ComputerUseWinFailureCodeValues.StaleState,
            "State became stale after activation.",
            targetHwnd: 101,
            elementIndex: 1,
            phase: ComputerUseWinActionLifecyclePhase.AfterActivationBeforeDispatch);

        Assert.True(payload.RefreshStateRecommended);
    }

    [Fact]
    public void StructuredActionApprovalRequiredRecommendsRefreshAfterRetryReresolution()
    {
        ComputerUseWinActionResult payload = ComputerUseWinActionFinalizer.CreateStructuredApprovalRequiredPayload(
            "Confirm required after retry.",
            targetHwnd: 101,
            elementIndex: 1,
            phase: ComputerUseWinActionLifecyclePhase.AfterRevalidationBeforeDispatch);

        Assert.True(payload.RefreshStateRecommended);
        Assert.Equal(ComputerUseWinFailureCodeValues.ApprovalRequired, payload.FailureCode);
    }

    [Fact]
    public void ActionFinalizerRecommendsRefreshWhenPreDispatchFailureMayFollowActivation()
    {
        string root = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            AuditLogOptions options = CreateAuditOptions(root, "computer-use-win-action-pre-dispatch-activation-tests");
            AuditLog auditLog = new(options, TimeProvider.System);
            using AuditInvocationScope invocation = auditLog.BeginInvocation(
                ToolNames.ComputerUseWinClick,
                new { stateToken = "token-1", elementIndex = 1 },
                new InMemorySessionManager(TimeProvider.System, new SessionContext("computer-use-win-action-pre-dispatch-activation-tests")).GetSnapshot());

            CallToolResult result = ComputerUseWinActionFinalizer.FinalizeUnexpectedFailure(
                invocation,
                ToolNames.ComputerUseWinClick,
                targetHwnd: 101,
                elementIndex: 1,
                exception: new InvalidOperationException("secret setup failure"),
                factualFailure: null,
                preDispatchStateMutationPossible: true);

            Assert.True(result.IsError);
            JsonElement payload = result.StructuredContent!.Value;
            Assert.True(payload.GetProperty("refreshStateRecommended").GetBoolean());
            string reason = payload.GetProperty("reason").GetString()!;
            Assert.Contains("возможной активации окна", reason, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void ActionFinalizerUsesBestEffortSanitizedAuditForUnexpectedFactualFailure()
    {
        string root = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            AuditLogOptions options = CreateAuditOptions(root, "computer-use-win-action-failure-finalizer-tests");
            AuditLog auditLog = new(options, TimeProvider.System);
            using AuditInvocationScope invocation = auditLog.BeginInvocation(
                ToolNames.ComputerUseWinClick,
                new { stateToken = "token-1", elementIndex = 1 },
                new InMemorySessionManager(TimeProvider.System, new SessionContext("computer-use-win-action-failure-finalizer-tests")).GetSnapshot());

            File.Delete(options.EventsPath);
            Directory.CreateDirectory(options.EventsPath);
            File.Delete(options.SummaryPath);
            Directory.CreateDirectory(options.SummaryPath);

            CallToolResult result = ComputerUseWinActionFinalizer.FinalizeUnexpectedFailure(
                invocation,
                ToolNames.ComputerUseWinClick,
                targetHwnd: 101,
                elementIndex: 1,
                exception: new InvalidOperationException("secret click failure"),
                factualFailure: new InputResult(
                    Status: InputStatusValues.Failed,
                    Decision: InputStatusValues.Failed,
                    FailureCode: InputFailureCodeValues.InputDispatchFailed,
                    Reason: "Runtime столкнулся с unexpected failure после committed input side effect; retry без явной проверки результата небезопасен.",
                    TargetHwnd: 101,
                    CompletedActionCount: 0,
                    FailedActionIndex: 0));

            Assert.True(result.IsError);
            JsonElement payload = result.StructuredContent!.Value;
            Assert.Equal(ComputerUseWinStatusValues.Failed, payload.GetProperty("status").GetString());
            Assert.Equal(ComputerUseWinFailureCodeValues.InputDispatchFailed, payload.GetProperty("failureCode").GetString());
            Assert.Equal(101, payload.GetProperty("targetHwnd").GetInt64());
            Assert.Equal(1, payload.GetProperty("elementIndex").GetInt32());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void ActionFinalizerIncludesFactualFailureEvidenceInTopLevelAudit()
    {
        string root = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            AuditLogOptions options = CreateAuditOptions(root, "computer-use-win-action-factual-evidence-tests");
            AuditLog auditLog = new(options, TimeProvider.System);
            using AuditInvocationScope invocation = auditLog.BeginInvocation(
                ToolNames.ComputerUseWinClick,
                new { stateToken = "token-1", elementIndex = 1 },
                new InMemorySessionManager(TimeProvider.System, new SessionContext("computer-use-win-action-factual-evidence-tests")).GetSnapshot());

            _ = ComputerUseWinActionFinalizer.FinalizeUnexpectedFailure(
                invocation,
                ToolNames.ComputerUseWinClick,
                targetHwnd: 101,
                elementIndex: 1,
                exception: new InvalidOperationException("secret click failure"),
                factualFailure: new InputResult(
                    Status: InputStatusValues.Failed,
                    Decision: InputStatusValues.Failed,
                    ResultMode: "dispatch_only",
                    FailureCode: InputFailureCodeValues.InputDispatchFailed,
                    Reason: "Runtime столкнулся с unexpected failure после committed input side effect; retry без явной проверки результата небезопасен.",
                    TargetHwnd: 101,
                    TargetSource: InputTargetSourceValues.Attached,
                    CompletedActionCount: 1,
                    FailedActionIndex: 0,
                    ArtifactPath: "C:\\temp\\click-failure.json"));

            string completedEvent = File.ReadLines(options.EventsPath)
                .Single(line => line.Contains("\"event_name\":\"tool.invocation.completed\"", StringComparison.Ordinal));
            Assert.Contains("\"failure_code\":\"input_dispatch_failed\"", completedEvent, StringComparison.Ordinal);
            Assert.Contains("\"completed_action_count\":\"1\"", completedEvent, StringComparison.Ordinal);
            Assert.Contains("\"failed_action_index\":\"0\"", completedEvent, StringComparison.Ordinal);
            Assert.Contains("\"artifact_path\":\"C:\\\\temp\\\\click-failure.json\"", completedEvent, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static AuditLogOptions CreateAuditOptions(string root, string runId) =>
        new(
            ContentRootPath: root,
            EnvironmentName: "Tests",
            RunId: runId,
            DiagnosticsRoot: Path.Combine(root, "artifacts", "diagnostics"),
            RunDirectory: Path.Combine(root, "artifacts", "diagnostics", runId),
            EventsPath: Path.Combine(root, "artifacts", "diagnostics", runId, "events.jsonl"),
            SummaryPath: Path.Combine(root, "artifacts", "diagnostics", runId, "summary.md"));

    private static WindowDescriptor CreateWindow() =>
        new(
            Hwnd: 101,
            Title: "Test window",
            ProcessName: "explorer",
            ProcessId: 1001,
            ThreadId: 2002,
            ClassName: "TestWindow",
            Bounds: new Bounds(0, 0, 640, 480),
            IsForeground: true,
            IsVisible: true);

    private static ComputerUseWinPreparedAppState CreatePreparedState(WindowDescriptor selectedWindow)
    {
        ComputerUseWinAppSession session = new("explorer", "cw_explorer_101", selectedWindow.Hwnd, selectedWindow.Title, selectedWindow.ProcessName, selectedWindow.ProcessId);
        ComputerUseWinStoredState storedState = new(
            session,
            selectedWindow,
            CaptureReference: null,
            Elements: new Dictionary<int, ComputerUseWinStoredElement>(),
            Observation: new ComputerUseWinObservationEnvelope(UiaSnapshotDefaults.Depth, 128),
            CapturedAtUtc: DateTimeOffset.UtcNow);

        return new(
            Session: session,
            StoredState: storedState,
            Capture: new CaptureMetadata(
                Scope: "window",
                TargetKind: "window",
                Hwnd: selectedWindow.Hwnd,
                Title: selectedWindow.Title,
                ProcessName: selectedWindow.ProcessName,
                Bounds: selectedWindow.Bounds,
                CoordinateSpace: "physical_pixels",
                PixelWidth: 320,
                PixelHeight: 200,
                CapturedAtUtc: DateTimeOffset.UtcNow,
                ArtifactPath: Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png"),
                MimeType: "image/png",
                ByteSize: 3,
                SessionRunId: "tests",
                EffectiveDpi: 96,
                DpiScale: 1.0,
                CaptureReference: null),
            AccessibilityTree: [],
            Instructions: [],
            Warnings: [],
            PngBytes: [1, 2, 3],
            MimeType: "image/png");
    }

    private static ComputerUseWinExecutionTarget CreateExecutionTarget(WindowDescriptor selectedWindow) =>
        new(
            new ComputerUseWinApprovalKey("explorer"),
            new ComputerUseWinWindowInstanceIdentity("cw_explorer_101"),
            selectedWindow);
}
