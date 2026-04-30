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
    public void ObservedStateAuditOmitsWindowIdWhenNoPublicSelectorWasPublished()
    {
        WindowDescriptor selectedWindow = CreateWindow();
        ComputerUseWinGetAppStateResult payload = CreatePreparedState(
            selectedWindow,
            windowId: null).CreatePayload("state-token-1");
        ComputerUseWinExecutionTarget target = new(
            new ComputerUseWinApprovalKey("explorer"),
            new ComputerUseWinWindowInstanceIdentity("cw_execution_target_only"),
            PublicWindowId: null,
            selectedWindow);

        Dictionary<string, string?> data = ComputerUseWinAuditDataBuilder.CreateObservedStateCompletionData(target, payload);

        Assert.False(data.ContainsKey("window_id"));
        Assert.Equal("cw_execution_target_only", data["execution_target_id"]);
        Assert.Equal("true", data["state_token_present"]);
    }

    [Fact]
    public void ObservedStateAuditIncludesWindowIdOnlyForPublishedSelector()
    {
        WindowDescriptor selectedWindow = CreateWindow();
        ComputerUseWinGetAppStateResult payload = CreatePreparedState(
            selectedWindow,
            windowId: "cw_public_selector").CreatePayload("state-token-1");
        ComputerUseWinExecutionTarget target = new(
            new ComputerUseWinApprovalKey("explorer"),
            new ComputerUseWinWindowInstanceIdentity("cw_execution_target"),
            "cw_public_selector",
            selectedWindow);

        Dictionary<string, string?> data = ComputerUseWinAuditDataBuilder.CreateObservedStateCompletionData(target, payload);

        Assert.Equal("cw_public_selector", data["window_id"]);
        Assert.Equal("cw_execution_target", data["execution_target_id"]);
        Assert.Equal("true", data["state_token_present"]);
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
    public void ActionFinalizerMaterializesComputerUseWinActionArtifactAndRuntimeEvent()
    {
        string root = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            AuditLogOptions options = CreateAuditOptions(root, "computer-use-win-action-runtime-event-tests");
            AuditLog auditLog = new(options, TimeProvider.System);
            using AuditInvocationScope invocation = auditLog.BeginInvocation(
                ToolNames.ComputerUseWinClick,
                new { stateToken = "token-1", elementIndex = 1, confirm = true },
                new InMemorySessionManager(TimeProvider.System, new SessionContext("computer-use-win-action-runtime-event-tests")).GetSnapshot());

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

            string actionEvent = File.ReadLines(options.EventsPath)
                .Single(line => line.Contains("\"event_name\":\"computer_use_win.action.completed\"", StringComparison.Ordinal));
            Assert.Contains("\"tool_name\":\"click\"", actionEvent, StringComparison.Ordinal);
            Assert.Contains("\"artifact_path\":", actionEvent, StringComparison.Ordinal);
            Assert.DoesNotContain("token-1", actionEvent, StringComparison.Ordinal);

            string actionArtifactPath = Directory
                .GetFiles(Path.Combine(options.RunDirectory, "computer-use-win"), "action-*.json", SearchOption.TopDirectoryOnly)
                .Single();
            using JsonDocument artifact = JsonDocument.Parse(File.ReadAllText(actionArtifactPath));
            Assert.Equal("click", artifact.RootElement.GetProperty("action_name").GetString());
            Assert.Equal("verify_needed", artifact.RootElement.GetProperty("public_result").GetString());
            Assert.False(artifact.RootElement.TryGetProperty("state_token", out _));
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
    public void ActionFinalizerMaterializesSecondaryActionObservabilityMarkers()
    {
        string root = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            AuditLogOptions options = CreateAuditOptions(root, "computer-use-win-secondary-action-observability-tests");
            AuditLog auditLog = new(options, TimeProvider.System);
            using AuditInvocationScope invocation = auditLog.BeginInvocation(
                ToolNames.ComputerUseWinPerformSecondaryAction,
                new { stateToken = "token-1", elementIndex = 1, confirm = true },
                new InMemorySessionManager(TimeProvider.System, new SessionContext("computer-use-win-secondary-action-observability-tests")).GetSnapshot());

            _ = ComputerUseWinActionFinalizer.FinalizeResult(
                invocation,
                ToolNames.ComputerUseWinPerformSecondaryAction,
                targetHwnd: 101,
                elementIndex: 1,
                new InputResult(
                    Status: InputStatusValues.Done,
                    Decision: InputStatusValues.Done,
                    ResultMode: InputResultModeValues.PostconditionVerified,
                    TargetHwnd: 101,
                    CompletedActionCount: 1),
                new ComputerUseWinActionObservabilityContext(
                    ActionName: ToolNames.ComputerUseWinPerformSecondaryAction,
                    RuntimeState: "observed",
                    AppId: "explorer",
                    WindowIdPresent: true,
                    StateTokenPresent: true,
                    TargetMode: "element_index",
                    ElementIndexPresent: true,
                    CoordinateSpace: null,
                    CaptureReferencePresent: false,
                    ConfirmationRequired: false,
                    Confirmed: true,
                    RiskClass: "secondary_semantic",
                    DispatchPath: "uia_toggle_pattern",
                    SemanticActionKind: "toggle",
                    FallbackUsed: false,
                    ContextMenuPathUsed: false));

            string actionEvent = File.ReadLines(options.EventsPath)
                .Single(line => line.Contains("\"event_name\":\"computer_use_win.action.completed\"", StringComparison.Ordinal));
            Assert.Contains("\"semantic_action_kind\":\"toggle\"", actionEvent, StringComparison.Ordinal);
            Assert.Contains("\"fallback_used\":\"false\"", actionEvent, StringComparison.Ordinal);
            Assert.Contains("\"context_menu_path_used\":\"false\"", actionEvent, StringComparison.Ordinal);

            string actionArtifactPath = Directory
                .GetFiles(Path.Combine(options.RunDirectory, "computer-use-win"), "action-*.json", SearchOption.TopDirectoryOnly)
                .Single();
            using JsonDocument artifact = JsonDocument.Parse(File.ReadAllText(actionArtifactPath));
            Assert.Equal("toggle", artifact.RootElement.GetProperty("semantic_action_kind").GetString());
            Assert.False(artifact.RootElement.GetProperty("fallback_used").GetBoolean());
            Assert.False(artifact.RootElement.GetProperty("context_menu_path_used").GetBoolean());
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
    public void ActionFinalizerMaterializesDragObservabilityMarkersWithoutRawPoints()
    {
        string root = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            AuditLogOptions options = CreateAuditOptions(root, "computer-use-win-drag-observability-tests");
            AuditLog auditLog = new(options, TimeProvider.System);
            using AuditInvocationScope invocation = auditLog.BeginInvocation(
                ToolNames.ComputerUseWinDrag,
                new
                {
                    stateToken = "token-1",
                    fromPoint = new { x = 20, y = 30 },
                    toPoint = new { x = 140, y = 90 },
                    confirm = true,
                },
                new InMemorySessionManager(TimeProvider.System, new SessionContext("computer-use-win-drag-observability-tests")).GetSnapshot());

            _ = ComputerUseWinActionFinalizer.FinalizeResult(
                invocation,
                ToolNames.ComputerUseWinDrag,
                targetHwnd: 101,
                elementIndex: null,
                new InputResult(
                    Status: InputStatusValues.VerifyNeeded,
                    Decision: InputStatusValues.VerifyNeeded,
                    ResultMode: InputResultModeValues.DispatchOnly,
                    TargetHwnd: 101,
                    CompletedActionCount: 1,
                    ArtifactPath: "C:\\temp\\drag-input.json"),
                new ComputerUseWinActionObservabilityContext(
                    ActionName: ToolNames.ComputerUseWinDrag,
                    RuntimeState: "observed",
                    AppId: "explorer",
                    WindowIdPresent: true,
                    StateTokenPresent: true,
                    TargetMode: "element_index_to_point",
                    ElementIndexPresent: true,
                    CoordinateSpace: InputCoordinateSpaceValues.Screen,
                    CaptureReferencePresent: false,
                    ConfirmationRequired: true,
                    Confirmed: true,
                    RiskClass: "coordinate_drag",
                    DispatchPath: "screen_drag_input",
                    SourceMode: "element_index",
                    DestinationMode: "point",
                    PathPointCountBucket: "two_points",
                    CoordinateFallbackUsed: true));

            string actionEvent = File.ReadLines(options.EventsPath)
                .Single(line => line.Contains("\"event_name\":\"computer_use_win.action.completed\"", StringComparison.Ordinal));
            Assert.Contains("\"tool_name\":\"drag\"", actionEvent, StringComparison.Ordinal);
            Assert.Contains("\"source_mode\":\"element_index\"", actionEvent, StringComparison.Ordinal);
            Assert.Contains("\"destination_mode\":\"point\"", actionEvent, StringComparison.Ordinal);
            Assert.Contains("\"path_point_count_bucket\":\"two_points\"", actionEvent, StringComparison.Ordinal);
            Assert.Contains("\"coordinate_fallback_used\":\"true\"", actionEvent, StringComparison.Ordinal);
            Assert.DoesNotContain("token-1", actionEvent, StringComparison.Ordinal);
            Assert.DoesNotContain("\"x\":20", actionEvent, StringComparison.Ordinal);
            Assert.DoesNotContain("\"y\":30", actionEvent, StringComparison.Ordinal);
            Assert.DoesNotContain("drag-input.json", actionEvent, StringComparison.Ordinal);

            string completedEvent = File.ReadLines(options.EventsPath)
                .Single(line => line.Contains("\"event_name\":\"tool.invocation.completed\"", StringComparison.Ordinal));
            Assert.DoesNotContain("drag-input.json", completedEvent, StringComparison.Ordinal);

            string actionArtifactPath = Directory
                .GetFiles(Path.Combine(options.RunDirectory, "computer-use-win"), "action-*.json", SearchOption.TopDirectoryOnly)
                .Single();
            using JsonDocument artifact = JsonDocument.Parse(File.ReadAllText(actionArtifactPath));
            Assert.Equal("element_index", artifact.RootElement.GetProperty("source_mode").GetString());
            Assert.Equal("point", artifact.RootElement.GetProperty("destination_mode").GetString());
            Assert.Equal("two_points", artifact.RootElement.GetProperty("path_point_count_bucket").GetString());
            Assert.True(artifact.RootElement.GetProperty("coordinate_fallback_used").GetBoolean());
            Assert.True(artifact.RootElement.TryGetProperty("child_artifact_paths", out JsonElement childArtifactPaths));
            Assert.Equal(0, childArtifactPaths.GetArrayLength());
            Assert.False(artifact.RootElement.TryGetProperty("state_token", out _));
            Assert.False(artifact.RootElement.TryGetProperty("from_point", out _));
            Assert.False(artifact.RootElement.TryGetProperty("to_point", out _));
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
    public void ActionFinalizerKeepsPublicResultWhenActionEventWriteFails()
    {
        string root = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            AuditLogOptions options = CreateAuditOptions(root, "computer-use-win-action-event-failure-tests");
            AuditLog auditLog = new(options, TimeProvider.System);
            using AuditInvocationScope invocation = auditLog.BeginInvocation(
                ToolNames.ComputerUseWinClick,
                new { stateToken = "token-1", elementIndex = 1, confirm = true },
                new InMemorySessionManager(TimeProvider.System, new SessionContext("computer-use-win-action-event-failure-tests")).GetSnapshot());

            File.Delete(options.EventsPath);
            Directory.CreateDirectory(options.EventsPath);

            CallToolResult result = ComputerUseWinActionFinalizer.FinalizeResult(
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
                    FailedActionIndex: null));

            Assert.False(result.IsError);
            JsonElement payload = result.StructuredContent!.Value;
            Assert.Equal(ComputerUseWinStatusValues.VerifyNeeded, payload.GetProperty("status").GetString());
            Assert.Single(Directory.GetFiles(Path.Combine(options.RunDirectory, "computer-use-win"), "action-*.json", SearchOption.TopDirectoryOnly));
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
    public void UnexpectedActionFailureEventDoesNotLeakRawExceptionMessage()
    {
        string root = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            AuditLogOptions options = CreateAuditOptions(root, "computer-use-win-action-unexpected-event-tests");
            AuditLog auditLog = new(options, TimeProvider.System);
            using AuditInvocationScope invocation = auditLog.BeginInvocation(
                ToolNames.ComputerUseWinClick,
                new { stateToken = "token-1", elementIndex = 1, confirm = true },
                new InMemorySessionManager(TimeProvider.System, new SessionContext("computer-use-win-action-unexpected-event-tests")).GetSnapshot());

            _ = ComputerUseWinActionFinalizer.FinalizeUnexpectedFailure(
                invocation,
                ToolNames.ComputerUseWinClick,
                targetHwnd: 101,
                elementIndex: 1,
                exception: new InvalidOperationException("secret click failure"));

            string actionEvent = File.ReadLines(options.EventsPath)
                .Single(line => line.Contains("\"event_name\":\"computer_use_win.action.completed\"", StringComparison.Ordinal));
            Assert.Contains("\"exception_type\":\"System.InvalidOperationException\"", actionEvent, StringComparison.Ordinal);
            Assert.DoesNotContain("secret click failure", actionEvent, StringComparison.Ordinal);
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
    public void StructuredActionFailureSanitizesObservationFailureReason()
    {
        string root = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            AuditLogOptions options = CreateAuditOptions(root, "computer-use-win-structured-action-failure-sanitizer-tests");
            AuditLog auditLog = new(options, TimeProvider.System);
            using AuditInvocationScope invocation = auditLog.BeginInvocation(
                ToolNames.ComputerUseWinClick,
                new { stateToken = "token-1", elementIndex = 1, observeAfter = true },
                new InMemorySessionManager(TimeProvider.System, new SessionContext("computer-use-win-structured-action-failure-sanitizer-tests")).GetSnapshot());

            CallToolResult result = ComputerUseWinToolResultFactory.CreateActionFailure(
                invocation,
                ToolNames.ComputerUseWinClick,
                ComputerUseWinFailureCodeValues.ObservationFailed,
                "secret traversal failure",
                targetHwnd: 101,
                elementIndex: 1,
                phase: ComputerUseWinActionLifecyclePhase.AfterRevalidationBeforeDispatch);

            Assert.True(result.IsError);
            JsonElement payload = result.StructuredContent!.Value;
            Assert.Equal(ComputerUseWinFailureCodeValues.ObservationFailed, payload.GetProperty("failureCode").GetString());
            string reason = payload.GetProperty("reason").GetString()!;
            Assert.DoesNotContain("secret traversal failure", reason, StringComparison.Ordinal);
            Assert.Contains("get_app_state", reason, StringComparison.Ordinal);

            string completedEvent = File.ReadLines(options.EventsPath)
                .Single(line => line.Contains("\"event_name\":\"tool.invocation.completed\"", StringComparison.Ordinal));
            Assert.DoesNotContain("secret traversal failure", completedEvent, StringComparison.Ordinal);

            string actionArtifactPath = Directory
                .GetFiles(Path.Combine(options.RunDirectory, "computer-use-win"), "action-*.json", SearchOption.TopDirectoryOnly)
                .Single();
            string actionArtifact = File.ReadAllText(actionArtifactPath);
            Assert.DoesNotContain("secret traversal failure", actionArtifact, StringComparison.Ordinal);
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
    public void ActionFinalizerKeepsPublicResultWhenActionArtifactWriteFails()
    {
        string root = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            AuditLogOptions options = CreateAuditOptions(root, "computer-use-win-action-artifact-failure-tests");
            Directory.CreateDirectory(options.RunDirectory);
            File.WriteAllText(Path.Combine(options.RunDirectory, "computer-use-win"), "occupied");
            AuditLog auditLog = new(options, TimeProvider.System);
            using AuditInvocationScope invocation = auditLog.BeginInvocation(
                ToolNames.ComputerUseWinClick,
                new { stateToken = "token-1", elementIndex = 1, confirm = true },
                new InMemorySessionManager(TimeProvider.System, new SessionContext("computer-use-win-action-artifact-failure-tests")).GetSnapshot());

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
                    TargetHwnd: 101,
                    CompletedActionCount: 1));

            Assert.False(result.IsError);
            JsonElement payload = result.StructuredContent!.Value;
            Assert.Equal(ComputerUseWinStatusValues.Done, payload.GetProperty("status").GetString());

            string completedEvent = File.ReadLines(options.EventsPath)
                .Single(line => line.Contains("\"event_name\":\"tool.invocation.completed\"", StringComparison.Ordinal));
            Assert.Contains("\"outcome\":\"done\"", completedEvent, StringComparison.Ordinal);
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

    private static ComputerUseWinPreparedAppState CreatePreparedState(
        WindowDescriptor selectedWindow,
        string? windowId = "cw_explorer_101")
    {
        ComputerUseWinAppSession session = new("explorer", windowId, selectedWindow.Hwnd, selectedWindow.Title, selectedWindow.ProcessName, selectedWindow.ProcessId);
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
            "cw_explorer_101",
            selectedWindow);
}
