// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;
using ModelContextProtocol.Protocol;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Session;
using WinBridge.Runtime.Tooling;
using WinBridge.Server.ComputerUse;

namespace WinBridge.Server.IntegrationTests;

public sealed class ComputerUseWinFinalizationTests
{
    private const int TargetHwnd = 101;
    private const int ElementIndex = 1;
    private const string StateToken = "token-1";
    private const string DispatchOnlyResultMode = "dispatch_only";
    private const string ClickArtifactPath = "C:\\temp\\click.json";
    private const string ClickFailureArtifactPath = "C:\\temp\\click-failure.json";
    private const string ManualClickVerificationReason = "Проверь результат клика по приложению вручную.";
    private const string RawTargetNotForegroundReason = "windows.input target_not_foreground preflight rejected dispatch.";

    [Fact]
    public void FailureCompletionPreservesSanitizedExceptionMetadataInAudit()
    {
        using TestHarness test = new("computer-use-win-failure-completion-tests");
        using AuditInvocationScope invocation = test.BeginInvocation(
            ToolNames.ComputerUseWinGetAppState,
            new { hwnd = TargetHwnd });

        ComputerUseWinFailureDetails failure = new(
            ComputerUseWinFailureCodeValues.ObservationFailed,
            "Computer Use for Windows не смог завершить observation stage для get_app_state.",
            new InvalidOperationException("secret observation failure"));

        ComputerUseWinFailureCompletion.CompleteFailure(
            invocation,
            failure.Reason,
            failure.FailureCode,
            targetHwnd: TargetHwnd,
            auditException: failure.AuditException);

        string completedEvent = test.CompletedInvocationEvent();
        Assert.Contains("\"exception_type\":\"System.InvalidOperationException\"", completedEvent, StringComparison.Ordinal);
        Assert.DoesNotContain("secret observation failure", completedEvent, StringComparison.Ordinal);
    }

    [Fact]
    public void FinalizerUsesBestEffortAuditAfterSharedStateCommit()
    {
        using TestHarness test = new("computer-use-win-finalizer-tests");
        ComputerUseWinStateStore stateStore = CreateStateStore();
        WindowDescriptor selectedWindow = CreateWindow();
        ComputerUseWinPreparedAppState preparedState = CreatePreparedState(selectedWindow);

        using AuditInvocationScope invocation = test.BeginInvocation(
            ToolNames.ComputerUseWinGetAppState,
            new { hwnd = selectedWindow.Hwnd });

        test.ReplaceAuditOutputsWithDirectories();

        CallToolResult result = ComputerUseWinGetAppStateFinalizer.FinalizeSuccess(
            invocation,
            CreateExecutionTarget(selectedWindow),
            selectedWindow,
            preparedState,
            stateStore,
            test.SessionManager);

        Assert.False(result.IsError);
        JsonElement payload = result.StructuredContent!.Value;
        string stateToken = payload.GetProperty("stateToken").GetString()!;
        Assert.True(stateStore.TryGet(stateToken, out ComputerUseWinStoredState? storedState));
        Assert.NotNull(storedState);
        Assert.NotEqual(default, storedState!.IssuedAtUtc);
        Assert.Equal(preparedState.StoredState with { IssuedAtUtc = storedState.IssuedAtUtc }, storedState);
        Assert.Equal(selectedWindow.Hwnd, test.SessionManager.GetAttachedWindow()?.Window.Hwnd);
    }

    [Fact]
    public void FinalizerDoesNotLeakStateTokenInCompletionAudit()
    {
        using TestHarness test = new("computer-use-win-finalizer-audit-state-token-tests");
        ComputerUseWinStateStore stateStore = CreateStateStore();
        WindowDescriptor selectedWindow = CreateWindow();
        ComputerUseWinPreparedAppState preparedState = CreatePreparedState(selectedWindow);

        using AuditInvocationScope invocation = test.BeginInvocation(
            ToolNames.ComputerUseWinGetAppState,
            new { hwnd = selectedWindow.Hwnd });

        _ = ComputerUseWinGetAppStateFinalizer.FinalizeSuccess(
            invocation,
            CreateExecutionTarget(selectedWindow),
            selectedWindow,
            preparedState,
            stateStore,
            test.SessionManager);

        string completedEvent = test.CompletedInvocationEvent();
        Assert.DoesNotContain("\"state_token\":", completedEvent, StringComparison.Ordinal);
        Assert.Contains("\"state_token_present\":\"true\"", completedEvent, StringComparison.Ordinal);
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
        using TestHarness test = new("computer-use-win-action-finalizer-tests");
        using AuditInvocationScope invocation = test.BeginClickInvocation(ClickRequest());

        test.ReplaceAuditOutputsWithDirectories();

        CallToolResult result = FinalizeClickResult(invocation, DoneInputResult());

        Assert.False(result.IsError);
        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.Done, payload.GetProperty("status").GetString());
        Assert.True(payload.GetProperty("refreshStateRecommended").GetBoolean());
        Assert.Equal((long)TargetHwnd, payload.GetProperty("targetHwnd").GetInt64());
        Assert.Equal(ElementIndex, payload.GetProperty("elementIndex").GetInt32());
    }

    [Fact]
    public void ActionFinalizerTranslatesInternalReasonToPublicMessage()
    {
        using TestHarness test = new("computer-use-win-action-reason-translation-tests");
        using AuditInvocationScope invocation = test.BeginClickInvocation(ClickRequest());

        CallToolResult result = FinalizeClickResult(invocation, TargetNotForegroundFailureInputResult());

        Assert.True(result.IsError);
        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinFailureCodeValues.TargetNotForeground, payload.GetProperty("failureCode").GetString());
        string reason = payload.GetProperty("reason").GetString()!;
        Assert.DoesNotContain("windows.input", reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("target_not_foreground", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ActionFinalizerDoesNotLeakRawReasonInCompletionAudit()
    {
        using TestHarness test = new("computer-use-win-action-raw-reason-audit-tests");
        using AuditInvocationScope invocation = test.BeginClickInvocation(ClickRequest());

        _ = FinalizeClickResult(invocation, TargetNotForegroundFailureInputResult());

        string completedEvent = test.CompletedInvocationEvent();
        Assert.DoesNotContain("\"raw_reason\":", completedEvent, StringComparison.Ordinal);
        Assert.DoesNotContain("windows.input target_not_foreground", completedEvent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"public_reason\":", completedEvent, StringComparison.Ordinal);
    }

    [Fact]
    public void ActionFinalizerPublishesFailureCodeForUnexpectedInternalFailure()
    {
        using TestHarness test = new("computer-use-win-action-unexpected-code-tests");
        using AuditInvocationScope invocation = test.BeginClickInvocation(ClickRequest());

        CallToolResult result = FinalizeUnexpectedClickFailure(
            invocation,
            new InvalidOperationException("secret setup failure"));

        Assert.True(result.IsError);
        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinFailureCodeValues.UnexpectedInternalFailure, payload.GetProperty("failureCode").GetString());
    }

    [Fact]
    public void ActionFinalizerMapsInternalFailureCodesToPublicVocabulary()
    {
        using TestHarness test = new("computer-use-win-action-code-mapping-tests");
        using AuditInvocationScope invocation = test.BeginClickInvocation(ClickRequest());

        CallToolResult result = FinalizeClickResult(invocation, UnsupportedActionFailureInputResult());

        Assert.True(result.IsError);
        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinFailureCodeValues.UnsupportedAction, payload.GetProperty("failureCode").GetString());
    }

    [Fact]
    public void ActionFinalizerIncludesEvidenceInTopLevelCompletionAudit()
    {
        using TestHarness test = new("computer-use-win-action-evidence-tests");
        using AuditInvocationScope invocation = test.BeginClickInvocation(ClickRequest());

        _ = FinalizeClickResult(invocation, VerifyNeededClickInputResult());

        string completedEvent = test.CompletedInvocationEvent();
        Assert.Contains("\"completed_action_count\":\"1\"", completedEvent, StringComparison.Ordinal);
        Assert.Contains("\"artifact_path\":\"C:\\\\temp\\\\click.json\"", completedEvent, StringComparison.Ordinal);
        Assert.Contains("\"result_mode\":\"dispatch_only\"", completedEvent, StringComparison.Ordinal);
    }

    [Fact]
    public void ActionFinalizerMaterializesComputerUseWinActionArtifactAndRuntimeEvent()
    {
        using TestHarness test = new("computer-use-win-action-runtime-event-tests");
        using AuditInvocationScope invocation = test.BeginClickInvocation(ConfirmedClickRequest());

        _ = FinalizeClickResult(invocation, VerifyNeededClickInputResult());

        string actionEvent = test.CompletedActionEvent();
        Assert.Contains("\"tool_name\":\"click\"", actionEvent, StringComparison.Ordinal);
        Assert.Contains("\"artifact_path\":", actionEvent, StringComparison.Ordinal);
        Assert.DoesNotContain(StateToken, actionEvent, StringComparison.Ordinal);

        using JsonDocument artifact = test.ReadSingleActionArtifact();
        Assert.Equal("click", artifact.RootElement.GetProperty("action_name").GetString());
        Assert.Equal("verify_needed", artifact.RootElement.GetProperty("public_result").GetString());
        Assert.False(artifact.RootElement.TryGetProperty("state_token", out _));
    }

    [Fact]
    public void ActionFinalizerMaterializesSecondaryActionObservabilityMarkers()
    {
        using TestHarness test = new("computer-use-win-secondary-action-observability-tests");
        using AuditInvocationScope invocation = test.BeginInvocation(
            ToolNames.ComputerUseWinPerformSecondaryAction,
            new { stateToken = StateToken, elementIndex = ElementIndex, confirm = true });

        _ = ComputerUseWinActionFinalizer.FinalizeResult(
            invocation,
            ToolNames.ComputerUseWinPerformSecondaryAction,
            targetHwnd: TargetHwnd,
            elementIndex: ElementIndex,
            new InputResult(
                Status: InputStatusValues.Done,
                Decision: InputStatusValues.Done,
                ResultMode: InputResultModeValues.PostconditionVerified,
                TargetHwnd: TargetHwnd,
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

        string actionEvent = test.CompletedActionEvent();
        Assert.Contains("\"semantic_action_kind\":\"toggle\"", actionEvent, StringComparison.Ordinal);
        Assert.Contains("\"fallback_used\":\"false\"", actionEvent, StringComparison.Ordinal);
        Assert.Contains("\"context_menu_path_used\":\"false\"", actionEvent, StringComparison.Ordinal);

        using JsonDocument artifact = test.ReadSingleActionArtifact();
        Assert.Equal("toggle", artifact.RootElement.GetProperty("semantic_action_kind").GetString());
        Assert.False(artifact.RootElement.GetProperty("fallback_used").GetBoolean());
        Assert.False(artifact.RootElement.GetProperty("context_menu_path_used").GetBoolean());
    }

    [Fact]
    public void ActionFinalizerMaterializesDragObservabilityMarkersWithoutRawPoints()
    {
        using TestHarness test = new("computer-use-win-drag-observability-tests");
        using AuditInvocationScope invocation = test.BeginInvocation(
            ToolNames.ComputerUseWinDrag,
            new
            {
                stateToken = StateToken,
                fromPoint = new { x = 20, y = 30 },
                toPoint = new { x = 140, y = 90 },
                confirm = true,
            });

        _ = ComputerUseWinActionFinalizer.FinalizeResult(
            invocation,
            ToolNames.ComputerUseWinDrag,
            targetHwnd: TargetHwnd,
            elementIndex: null,
            new InputResult(
                Status: InputStatusValues.VerifyNeeded,
                Decision: InputStatusValues.VerifyNeeded,
                ResultMode: InputResultModeValues.DispatchOnly,
                TargetHwnd: TargetHwnd,
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

        string actionEvent = test.CompletedActionEvent();
        Assert.Contains("\"tool_name\":\"drag\"", actionEvent, StringComparison.Ordinal);
        Assert.Contains("\"source_mode\":\"element_index\"", actionEvent, StringComparison.Ordinal);
        Assert.Contains("\"destination_mode\":\"point\"", actionEvent, StringComparison.Ordinal);
        Assert.Contains("\"path_point_count_bucket\":\"two_points\"", actionEvent, StringComparison.Ordinal);
        Assert.Contains("\"coordinate_fallback_used\":\"true\"", actionEvent, StringComparison.Ordinal);
        Assert.DoesNotContain(StateToken, actionEvent, StringComparison.Ordinal);
        Assert.DoesNotContain("\"x\":20", actionEvent, StringComparison.Ordinal);
        Assert.DoesNotContain("\"y\":30", actionEvent, StringComparison.Ordinal);
        Assert.DoesNotContain("drag-input.json", actionEvent, StringComparison.Ordinal);

        string completedEvent = test.CompletedInvocationEvent();
        Assert.DoesNotContain("drag-input.json", completedEvent, StringComparison.Ordinal);

        using JsonDocument artifact = test.ReadSingleActionArtifact();
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

    [Fact]
    public void ActionFinalizerKeepsPublicResultWhenActionEventWriteFails()
    {
        using TestHarness test = new("computer-use-win-action-event-failure-tests");
        using AuditInvocationScope invocation = test.BeginClickInvocation(ConfirmedClickRequest());

        test.ReplaceEventsPathWithDirectory();

        CallToolResult result = FinalizeClickResult(invocation, VerifyNeededClickInputResultWithoutArtifact());

        Assert.False(result.IsError);
        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.VerifyNeeded, payload.GetProperty("status").GetString());
        Assert.Single(test.ActionArtifactPaths());
    }

    [Fact]
    public void UnexpectedActionFailureEventDoesNotLeakRawExceptionMessage()
    {
        using TestHarness test = new("computer-use-win-action-unexpected-event-tests");
        using AuditInvocationScope invocation = test.BeginClickInvocation(ConfirmedClickRequest());

        _ = FinalizeUnexpectedClickFailure(
            invocation,
            new InvalidOperationException("secret click failure"));

        string actionEvent = test.CompletedActionEvent();
        Assert.Contains("\"exception_type\":\"System.InvalidOperationException\"", actionEvent, StringComparison.Ordinal);
        Assert.DoesNotContain("secret click failure", actionEvent, StringComparison.Ordinal);
    }

    [Fact]
    public void StructuredActionFailureSanitizesObservationFailureReason()
    {
        using TestHarness test = new("computer-use-win-structured-action-failure-sanitizer-tests");
        using AuditInvocationScope invocation = test.BeginClickInvocation(ClickRequestWithObserveAfter());

        CallToolResult result = ComputerUseWinToolResultFactory.CreateActionFailure(
            invocation,
            ToolNames.ComputerUseWinClick,
            ComputerUseWinFailureCodeValues.ObservationFailed,
            "secret traversal failure",
            targetHwnd: TargetHwnd,
            elementIndex: ElementIndex,
            phase: ComputerUseWinActionLifecyclePhase.AfterRevalidationBeforeDispatch);

        Assert.True(result.IsError);
        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinFailureCodeValues.ObservationFailed, payload.GetProperty("failureCode").GetString());
        string reason = payload.GetProperty("reason").GetString()!;
        Assert.DoesNotContain("secret traversal failure", reason, StringComparison.Ordinal);
        Assert.Contains("get_app_state", reason, StringComparison.Ordinal);

        string completedEvent = test.CompletedInvocationEvent();
        Assert.DoesNotContain("secret traversal failure", completedEvent, StringComparison.Ordinal);
        Assert.DoesNotContain("secret traversal failure", test.SingleActionArtifactText(), StringComparison.Ordinal);
    }

    [Fact]
    public void ActionFinalizerKeepsPublicResultWhenActionArtifactWriteFails()
    {
        using TestHarness test = new("computer-use-win-action-artifact-failure-tests");
        test.OccupyComputerUseWinArtifactDirectory();
        using AuditInvocationScope invocation = test.BeginClickInvocation(ConfirmedClickRequest());

        CallToolResult result = FinalizeClickResult(invocation, DoneInputResultWithCompletedAction());

        Assert.False(result.IsError);
        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.Done, payload.GetProperty("status").GetString());

        string completedEvent = test.CompletedInvocationEvent();
        Assert.Contains("\"outcome\":\"done\"", completedEvent, StringComparison.Ordinal);
    }

    [Fact]
    public void ActionFinalizerDoesNotClaimPostDispatchForPreDispatchUnexpectedFailure()
    {
        using TestHarness test = new("computer-use-win-action-pre-dispatch-failure-tests");
        using AuditInvocationScope invocation = test.BeginClickInvocation(ClickRequest());

        CallToolResult result = FinalizeUnexpectedClickFailure(
            invocation,
            new InvalidOperationException("secret setup failure"));

        Assert.True(result.IsError);
        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.False(payload.GetProperty("refreshStateRecommended").GetBoolean());
        string reason = payload.GetProperty("reason").GetString()!;
        Assert.Contains("до подтверждённого action dispatch", reason, StringComparison.Ordinal);
        Assert.DoesNotContain("после action dispatch", reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ComputerUseWinFailureCodeValues.StateRequired)]
    [InlineData(ComputerUseWinFailureCodeValues.StaleState)]
    [InlineData(ComputerUseWinFailureCodeValues.CaptureReferenceRequired)]
    public void StructuredBoundaryFailuresThatNeedFreshStateRecommendRefresh(string failureCode)
    {
        ComputerUseWinActionResult payload = ComputerUseWinActionFinalizer.CreateStructuredFailurePayload(
            failureCode,
            "Refresh is required.",
            targetHwnd: TargetHwnd,
            elementIndex: null,
            phase: ComputerUseWinActionLifecyclePhase.BeforeActivation);

        Assert.True(payload.RefreshStateRecommended);
        Assert.Equal(failureCode, payload.FailureCode);
    }

    [Fact]
    public void BoundaryActionFailureDoesNotRecommendRefreshForMalformedRequestBeforeActivation()
    {
        ComputerUseWinActionResult payload = ComputerUseWinActionFinalizer.CreateStructuredFailurePayload(
            ComputerUseWinFailureCodeValues.InvalidRequest,
            "Malformed click request.",
            targetHwnd: TargetHwnd,
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
            targetHwnd: TargetHwnd,
            elementIndex: ElementIndex,
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
            targetHwnd: TargetHwnd,
            elementIndex: ElementIndex,
            phase: ComputerUseWinActionLifecyclePhase.AfterActivationBeforeDispatch);

        Assert.True(payload.RefreshStateRecommended);
    }

    [Fact]
    public void StructuredActionApprovalRequiredRecommendsRefreshAfterRetryReresolution()
    {
        ComputerUseWinActionResult payload = ComputerUseWinActionFinalizer.CreateStructuredApprovalRequiredPayload(
            "Confirm required after retry.",
            targetHwnd: TargetHwnd,
            elementIndex: ElementIndex,
            phase: ComputerUseWinActionLifecyclePhase.AfterRevalidationBeforeDispatch);

        Assert.True(payload.RefreshStateRecommended);
        Assert.Equal(ComputerUseWinFailureCodeValues.ApprovalRequired, payload.FailureCode);
    }

    [Fact]
    public void ActionFinalizerRecommendsRefreshWhenPreDispatchFailureMayFollowActivation()
    {
        using TestHarness test = new("computer-use-win-action-pre-dispatch-activation-tests");
        using AuditInvocationScope invocation = test.BeginClickInvocation(ClickRequest());

        CallToolResult result = FinalizeUnexpectedClickFailure(
            invocation,
            new InvalidOperationException("secret setup failure"),
            preDispatchStateMutationPossible: true);

        Assert.True(result.IsError);
        JsonElement payload = result.StructuredContent!.Value;
        Assert.True(payload.GetProperty("refreshStateRecommended").GetBoolean());
        string reason = payload.GetProperty("reason").GetString()!;
        Assert.Contains("возможной активации окна", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ActionFinalizerUsesBestEffortSanitizedAuditForUnexpectedFactualFailure()
    {
        using TestHarness test = new("computer-use-win-action-failure-finalizer-tests");
        using AuditInvocationScope invocation = test.BeginClickInvocation(ClickRequest());

        test.ReplaceAuditOutputsWithDirectories();

        CallToolResult result = FinalizeUnexpectedClickFailure(
            invocation,
            new InvalidOperationException("secret click failure"),
            factualFailure: InputDispatchFactualFailureWithoutEvidence());

        Assert.True(result.IsError);
        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.Equal(ComputerUseWinFailureCodeValues.InputDispatchFailed, payload.GetProperty("failureCode").GetString());
        Assert.Equal((long)TargetHwnd, payload.GetProperty("targetHwnd").GetInt64());
        Assert.Equal(ElementIndex, payload.GetProperty("elementIndex").GetInt32());
    }

    [Fact]
    public void ActionFinalizerIncludesFactualFailureEvidenceInTopLevelAudit()
    {
        using TestHarness test = new("computer-use-win-action-factual-evidence-tests");
        using AuditInvocationScope invocation = test.BeginClickInvocation(ClickRequest());

        _ = FinalizeUnexpectedClickFailure(
            invocation,
            new InvalidOperationException("secret click failure"),
            factualFailure: InputDispatchFactualFailureWithEvidence());

        string completedEvent = test.CompletedInvocationEvent();
        Assert.Contains("\"failure_code\":\"input_dispatch_failed\"", completedEvent, StringComparison.Ordinal);
        Assert.Contains("\"completed_action_count\":\"1\"", completedEvent, StringComparison.Ordinal);
        Assert.Contains("\"failed_action_index\":\"0\"", completedEvent, StringComparison.Ordinal);
        Assert.Contains("\"artifact_path\":\"C:\\\\temp\\\\click-failure.json\"", completedEvent, StringComparison.Ordinal);
    }

    private static CallToolResult FinalizeClickResult(AuditInvocationScope invocation, InputResult inputResult) =>
        ComputerUseWinActionFinalizer.FinalizeResult(
            invocation,
            ToolNames.ComputerUseWinClick,
            targetHwnd: TargetHwnd,
            elementIndex: ElementIndex,
            inputResult);

    private static CallToolResult FinalizeUnexpectedClickFailure(
        AuditInvocationScope invocation,
        Exception exception,
        InputResult? factualFailure = null,
        bool preDispatchStateMutationPossible = false) =>
        ComputerUseWinActionFinalizer.FinalizeUnexpectedFailure(
            invocation,
            ToolNames.ComputerUseWinClick,
            targetHwnd: TargetHwnd,
            elementIndex: ElementIndex,
            exception: exception,
            factualFailure: factualFailure,
            preDispatchStateMutationPossible: preDispatchStateMutationPossible);

    private static object ClickRequest() => new { stateToken = StateToken, elementIndex = ElementIndex };

    private static object ConfirmedClickRequest() => new { stateToken = StateToken, elementIndex = ElementIndex, confirm = true };

    private static object ClickRequestWithObserveAfter() => new { stateToken = StateToken, elementIndex = ElementIndex, observeAfter = true };

    private static InputResult DoneInputResult() =>
        new(
            Status: InputStatusValues.Done,
            Decision: InputStatusValues.Done,
            TargetHwnd: TargetHwnd);

    private static InputResult DoneInputResultWithCompletedAction() =>
        new(
            Status: InputStatusValues.Done,
            Decision: InputStatusValues.Done,
            TargetHwnd: TargetHwnd,
            CompletedActionCount: 1);

    private static InputResult VerifyNeededClickInputResult() =>
        new(
            Status: InputStatusValues.VerifyNeeded,
            Decision: InputStatusValues.VerifyNeeded,
            ResultMode: DispatchOnlyResultMode,
            FailureCode: null,
            Reason: ManualClickVerificationReason,
            TargetHwnd: TargetHwnd,
            TargetSource: InputTargetSourceValues.Attached,
            CompletedActionCount: 1,
            FailedActionIndex: null,
            ArtifactPath: ClickArtifactPath);

    private static InputResult VerifyNeededClickInputResultWithoutArtifact() =>
        new(
            Status: InputStatusValues.VerifyNeeded,
            Decision: InputStatusValues.VerifyNeeded,
            ResultMode: DispatchOnlyResultMode,
            FailureCode: null,
            Reason: ManualClickVerificationReason,
            TargetHwnd: TargetHwnd,
            TargetSource: InputTargetSourceValues.Attached,
            CompletedActionCount: 1,
            FailedActionIndex: null);

    private static InputResult TargetNotForegroundFailureInputResult() =>
        new(
            Status: InputStatusValues.Failed,
            Decision: InputStatusValues.Failed,
            FailureCode: InputFailureCodeValues.TargetNotForeground,
            Reason: RawTargetNotForegroundReason,
            TargetHwnd: TargetHwnd);

    private static InputResult UnsupportedActionFailureInputResult() =>
        new(
            Status: InputStatusValues.Failed,
            Decision: InputStatusValues.Failed,
            FailureCode: InputFailureCodeValues.UnsupportedActionType,
            Reason: "unsupported action type",
            TargetHwnd: TargetHwnd);

    private static InputResult InputDispatchFactualFailureWithoutEvidence() =>
        new(
            Status: InputStatusValues.Failed,
            Decision: InputStatusValues.Failed,
            FailureCode: InputFailureCodeValues.InputDispatchFailed,
            Reason: "Runtime столкнулся с unexpected failure после committed input side effect; retry без явной проверки результата небезопасен.",
            TargetHwnd: TargetHwnd,
            CompletedActionCount: 0,
            FailedActionIndex: 0);

    private static InputResult InputDispatchFactualFailureWithEvidence() =>
        new(
            Status: InputStatusValues.Failed,
            Decision: InputStatusValues.Failed,
            ResultMode: DispatchOnlyResultMode,
            FailureCode: InputFailureCodeValues.InputDispatchFailed,
            Reason: "Runtime столкнулся с unexpected failure после committed input side effect; retry без явной проверки результата небезопасен.",
            TargetHwnd: TargetHwnd,
            TargetSource: InputTargetSourceValues.Attached,
            CompletedActionCount: 1,
            FailedActionIndex: 0,
            ArtifactPath: ClickFailureArtifactPath);

    private static ComputerUseWinStateStore CreateStateStore() =>
        new(TimeProvider.System, TimeSpan.FromSeconds(30), maxEntries: 4);

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
            Hwnd: TargetHwnd,
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
        DateTimeOffset capturedAtUtc = DateTimeOffset.UtcNow;
        ComputerUseWinAppSession session = new("explorer", windowId, selectedWindow.Hwnd, selectedWindow.Title, selectedWindow.ProcessName, selectedWindow.ProcessId);
        ComputerUseWinStoredState storedState = new(
            session,
            selectedWindow,
            CaptureReference: null,
            Elements: new Dictionary<int, ComputerUseWinStoredElement>(),
            Observation: new ComputerUseWinObservationEnvelope(UiaSnapshotDefaults.Depth, 128),
            CapturedAtUtc: capturedAtUtc);

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
                CapturedAtUtc: capturedAtUtc,
                ArtifactPath: Path.Combine(Path.GetTempPath(), "winbridge-test-capture.png"),
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

    private sealed class TestHarness : IDisposable
    {
        public TestHarness(string runId)
        {
            Root = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            Options = CreateAuditOptions(Root, runId);
            AuditLog = new AuditLog(Options, TimeProvider.System);
            SessionManager = new InMemorySessionManager(TimeProvider.System, new SessionContext(runId));
        }

        public string Root { get; }

        public AuditLogOptions Options { get; }

        public AuditLog AuditLog { get; }

        public InMemorySessionManager SessionManager { get; }

        public AuditInvocationScope BeginInvocation(string toolName, object request) =>
            AuditLog.BeginInvocation(toolName, request, SessionManager.GetSnapshot());

        public AuditInvocationScope BeginClickInvocation(object request) =>
            BeginInvocation(ToolNames.ComputerUseWinClick, request);

        public void ReplaceAuditOutputsWithDirectories()
        {
            ReplaceFileWithDirectory(Options.EventsPath);
            ReplaceFileWithDirectory(Options.SummaryPath);
        }

        public void ReplaceEventsPathWithDirectory() =>
            ReplaceFileWithDirectory(Options.EventsPath);

        public void OccupyComputerUseWinArtifactDirectory()
        {
            Directory.CreateDirectory(Options.RunDirectory);
            File.WriteAllText(Path.Combine(Options.RunDirectory, "computer-use-win"), "occupied");
        }

        public string CompletedInvocationEvent() =>
            ReadSingleEvent("tool.invocation.completed");

        public string CompletedActionEvent() =>
            ReadSingleEvent("computer_use_win.action.completed");

        public string[] ActionArtifactPaths() =>
            Directory.GetFiles(ComputerUseWinArtifactDirectory, "action-*.json", SearchOption.TopDirectoryOnly);

        public JsonDocument ReadSingleActionArtifact() =>
            JsonDocument.Parse(File.ReadAllText(ActionArtifactPaths().Single()));

        public string SingleActionArtifactText() =>
            File.ReadAllText(ActionArtifactPaths().Single());

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private string ComputerUseWinArtifactDirectory =>
            Path.Combine(Options.RunDirectory, "computer-use-win");

        private string ReadSingleEvent(string eventName) =>
            File.ReadLines(Options.EventsPath)
                .Single(line => line.Contains($"\"event_name\":\"{eventName}\"", StringComparison.Ordinal));

        private static void ReplaceFileWithDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }

            File.Delete(path);
            Directory.CreateDirectory(path);
        }
    }
}
