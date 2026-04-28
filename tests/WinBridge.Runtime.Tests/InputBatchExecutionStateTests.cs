using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Input;
using WinBridge.Runtime.Windows.Shell;

namespace WinBridge.Runtime.Tests;

public sealed class InputBatchExecutionStateTests
{
    [Fact]
    public void RecordCommittedSideEffectPreservesCurrentActionOwnershipAndResolvedPoint()
    {
        InputBatchExecutionState batch = CreateBatch();
        batch.BeginAction(0, CreateAction(InputActionTypeValues.Move, InputCoordinateSpaceValues.Screen, new InputPoint(140, 260)), effectiveButton: null);
        batch.UpdateResolvedPoint(new InputPoint(140, 260));
        batch.UpdateTargetHwnd(101);

        batch.RecordCommittedSideEffect(InputIrreversiblePhase.AfterMove);

        InputCommittedSideEffectContext committed = Assert.IsType<InputCommittedSideEffectContext>(batch.CommittedSideEffectContext);
        Assert.Equal(0, committed.ActionIndex);
        Assert.Equal(InputIrreversiblePhase.AfterMove, committed.Phase);
        Assert.Equal(new InputPoint(140, 260), committed.ResolvedScreenPoint);
        Assert.Null(committed.Button);
        Assert.Equal(101, committed.TargetHwnd);
    }

    [Fact]
    public void RecordCommittedSideEffectUsesEffectiveButtonKnownAtActionStart()
    {
        InputBatchExecutionState batch = CreateBatch();
        batch.BeginAction(0, CreateAction(InputActionTypeValues.Click, InputCoordinateSpaceValues.Screen, new InputPoint(140, 260)), effectiveButton: InputButtonValues.Left);
        batch.UpdateResolvedPoint(new InputPoint(140, 260));
        batch.UpdateTargetHwnd(101);

        batch.RecordCommittedSideEffect(InputIrreversiblePhase.AfterMove);

        InputCommittedSideEffectContext committed = Assert.IsType<InputCommittedSideEffectContext>(batch.CommittedSideEffectContext);
        Assert.Equal(InputButtonValues.Left, committed.Button);
    }

    [Fact]
    public void CancellationAfterCommittedRefreshMoveUsesCommittedRefreshedPoint()
    {
        InputBatchExecutionState batch = CreateBatch();
        batch.BeginAction(0, CreateAction(InputActionTypeValues.Click, InputCoordinateSpaceValues.CapturePixels, new InputPoint(20, 30)), effectiveButton: InputButtonValues.Left);
        batch.UpdateResolvedPoint(new InputPoint(155, 275));
        batch.UpdateTargetHwnd(101);
        batch.RecordCommittedSideEffect(InputIrreversiblePhase.AfterMove);

        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        bool materialized = batch.TryMaterializeCancellationAfterCommittedSideEffect(cancellation.Token, out InputResult? result);

        Assert.True(materialized);
        InputResult failure = Assert.IsType<InputResult>(result);
        Assert.Equal(InputStatusValues.Failed, failure.Status);
        Assert.Equal(0, failure.FailedActionIndex);
        InputActionResult failedAction = Assert.Single(failure.Actions!);
        Assert.Equal(new InputPoint(155, 275), failedAction.ResolvedScreenPoint);
    }

    [Fact]
    public void BetweenActionsCancellationStaysAttachedToLastCommittedAction()
    {
        InputBatchExecutionState batch = CreateBatch();
        batch.BeginAction(0, CreateAction(InputActionTypeValues.Click, InputCoordinateSpaceValues.Screen, new InputPoint(140, 260)), effectiveButton: InputButtonValues.Left);
        batch.UpdateResolvedPoint(new InputPoint(140, 260));
        batch.UpdateTargetHwnd(101);
        batch.RecordCommittedSideEffect(InputIrreversiblePhase.AfterClickTap);
        batch.CompleteCurrentActionSuccess();

        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        bool materialized = batch.TryMaterializeCancellationBetweenActions(cancellation.Token, out InputResult? result);

        Assert.True(materialized);
        InputResult failure = Assert.IsType<InputResult>(result);
        Assert.Equal(InputStatusValues.Failed, failure.Status);
        Assert.Null(failure.FailedActionIndex);
        Assert.Equal(1, failure.CompletedActionCount);
        InputActionResult committedAction = Assert.Single(failure.Actions!);
        Assert.Equal(InputStatusValues.VerifyNeeded, committedAction.Status);
        Assert.Equal(InputActionTypeValues.Click, committedAction.Type);
    }

    [Fact]
    public void AfterBatchCompletedCancellationUsesDedicatedReasonInsteadOfBetweenActionsReason()
    {
        InputBatchExecutionState batch = CreateBatch();
        batch.BeginAction(0, CreateAction(InputActionTypeValues.Click, InputCoordinateSpaceValues.Screen, new InputPoint(140, 260)), effectiveButton: InputButtonValues.Left);
        batch.UpdateResolvedPoint(new InputPoint(140, 260));
        batch.UpdateTargetHwnd(101);
        batch.RecordCommittedSideEffect(InputIrreversiblePhase.AfterClickTap);
        batch.CompleteCurrentActionSuccess();

        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        bool materialized = batch.TryMaterializeCancellationAfterBatchCompleted(cancellation.Token, out InputResult? result);

        Assert.True(materialized);
        InputResult failure = Assert.IsType<InputResult>(result);
        Assert.Contains("full batch", failure.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("next action was not started", failure.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CancellationAfterSecondTapKeepsFullyDispatchedDoubleClickReason()
    {
        InputBatchExecutionState batch = CreateBatch();
        batch.BeginAction(0, CreateAction(InputActionTypeValues.DoubleClick, InputCoordinateSpaceValues.Screen, new InputPoint(140, 260)), effectiveButton: InputButtonValues.Left);
        batch.UpdateResolvedPoint(new InputPoint(140, 260));
        batch.UpdateTargetHwnd(101);
        batch.RecordCommittedSideEffect(InputIrreversiblePhase.AfterDoubleClickSecondTap);

        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        bool materialized = batch.TryMaterializeCancellationAfterCommittedSideEffect(cancellation.Token, out InputResult? result);

        Assert.True(materialized);
        InputResult failure = Assert.IsType<InputResult>(result);
        Assert.Contains("both", failure.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("second tap was not executed", failure.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CancellationAfterTypeTextDispatchUsesTextSpecificReason()
    {
        InputBatchExecutionState batch = CreateBatch();
        batch.BeginAction(0, CreateKeyboardAction(InputActionTypeValues.Type, text: "typed text"), effectiveButton: null);
        batch.UpdateTargetHwnd(101);
        batch.RecordCommittedSideEffect(InputIrreversiblePhase.AfterTypeTextDispatch);

        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        bool materialized = batch.TryMaterializeCancellationAfterCommittedSideEffect(cancellation.Token, out InputResult? result);

        Assert.True(materialized);
        InputResult failure = Assert.IsType<InputResult>(result);
        Assert.Contains("text dispatch", failure.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pointer", failure.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CancellationAfterKeypressDispatchUsesKeyboardSpecificReason()
    {
        InputBatchExecutionState batch = CreateBatch();
        batch.BeginAction(0, CreateKeyboardAction(InputActionTypeValues.Keypress, key: "ctrl+s"), effectiveButton: null);
        batch.UpdateTargetHwnd(101);
        batch.RecordCommittedSideEffect(InputIrreversiblePhase.AfterKeypressDispatch);

        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        bool materialized = batch.TryMaterializeCancellationAfterCommittedSideEffect(cancellation.Token, out InputResult? result);

        Assert.True(materialized);
        InputResult failure = Assert.IsType<InputResult>(result);
        Assert.Contains("keypress dispatch", failure.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pointer", failure.Reason, StringComparison.OrdinalIgnoreCase);
    }

    private static InputBatchExecutionState CreateBatch() =>
        new(
            CreateWindow(),
            InputTargetSourceValues.Explicit,
            CreateCurrentProcessSecurity());

    private static InputAction CreateAction(
        string type,
        string coordinateSpace,
        InputPoint point) =>
        new()
        {
            Type = type,
            Point = point,
            CoordinateSpace = coordinateSpace,
        };

    private static InputAction CreateKeyboardAction(
        string type,
        string? text = null,
        string? key = null) =>
        new()
        {
            Type = type,
            Text = text,
            Key = key,
        };

    private static WindowDescriptor CreateWindow(
        Bounds? bounds = null,
        bool isForeground = true,
        string windowState = WindowStateValues.Normal) =>
        new(
            Hwnd: 101,
            Title: "Target",
            ProcessName: "target",
            ProcessId: 321,
            ThreadId: 654,
            ClassName: "TargetWindowClass",
            Bounds: bounds ?? new Bounds(100, 200, 420, 560),
            IsForeground: isForeground,
            IsVisible: true,
            EffectiveDpi: 96,
            DpiScale: 1.0,
            WindowState: windowState);

    private static InputProcessSecurityContext CreateCurrentProcessSecurity(
        int sessionId = 1,
        InputIntegrityLevel integrityLevel = InputIntegrityLevel.High,
        bool hasUiAccess = false) =>
        new(
            SessionId: sessionId,
            SessionResolved: true,
            IntegrityLevel: integrityLevel,
            IntegrityResolved: true,
            HasUiAccess: hasUiAccess,
            UiAccessResolved: true,
            Reason: null);
}
