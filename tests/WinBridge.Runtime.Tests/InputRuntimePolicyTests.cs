using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Input;
using WinBridge.Runtime.Windows.Shell;

namespace WinBridge.Runtime.Tests;

public sealed class InputRuntimePolicyTests
{
    [Fact]
    public void MapActionUsesScreenPointAsIsWhenPointStaysInsideLiveWindow()
    {
        WindowDescriptor targetWindow = CreateWindow(bounds: new Bounds(100, 200, 420, 560));
        InputAction action = CreateAction(
            InputActionTypeValues.Click,
            InputCoordinateSpaceValues.Screen,
            new InputPoint(140, 260));

        bool mapped = InputCoordinateMapper.TryMap(action, targetWindow, out InputPoint? screenPoint, out string? failureCode, out string? reason);

        Assert.True(mapped);
        Assert.Equal(new InputPoint(140, 260), screenPoint);
        Assert.Null(failureCode);
        Assert.Null(reason);
    }

    [Fact]
    public void MapActionTranslatesCapturePixelsToScreenCoordinatesWithoutScaling()
    {
        WindowDescriptor targetWindow = CreateWindow(bounds: new Bounds(300, 400, 620, 780));
        InputAction action = CreateAction(
            InputActionTypeValues.Click,
            InputCoordinateSpaceValues.CapturePixels,
            new InputPoint(25, 40),
            CreateCaptureReference(
                bounds: new InputBounds(300, 400, 620, 780),
                pixelWidth: 320,
                pixelHeight: 380));

        bool mapped = InputCoordinateMapper.TryMap(action, targetWindow, out InputPoint? screenPoint, out string? failureCode, out string? reason);

        Assert.True(mapped);
        Assert.Equal(new InputPoint(325, 440), screenPoint);
        Assert.Null(failureCode);
        Assert.Null(reason);
    }

    [Fact]
    public void MapActionRejectsCaptureReferenceWhenLiveWindowMovedTooFar()
    {
        WindowDescriptor targetWindow = CreateWindow(bounds: new Bounds(304, 400, 624, 780));
        InputAction action = CreateAction(
            InputActionTypeValues.Click,
            InputCoordinateSpaceValues.CapturePixels,
            new InputPoint(25, 40),
            CreateCaptureReference(
                bounds: new InputBounds(300, 400, 620, 780),
                pixelWidth: 320,
                pixelHeight: 380));

        bool mapped = InputCoordinateMapper.TryMap(action, targetWindow, out InputPoint? screenPoint, out string? failureCode, out string? reason);

        Assert.False(mapped);
        Assert.Null(screenPoint);
        Assert.Equal(InputFailureCodeValues.CaptureReferenceStale, failureCode);
        Assert.Contains("capture", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MapActionRejectsCaptureReferenceWhenOriginDeltaWouldOverflowIntArithmetic()
    {
        WindowDescriptor targetWindow = CreateWindow(bounds: new Bounds(0, 100, 200, 460));
        InputAction action = CreateAction(
            InputActionTypeValues.Click,
            InputCoordinateSpaceValues.CapturePixels,
            new InputPoint(25, 40),
            CreateCaptureReference(
                bounds: new InputBounds(int.MinValue, 100, int.MinValue + 200, 460),
                pixelWidth: 200,
                pixelHeight: 360));

        Exception? exception = Record.Exception(
            () => InputCoordinateMapper.TryMap(action, targetWindow, out _, out string? failureCode, out string? reason));

        Assert.Null(exception);

        bool mapped = InputCoordinateMapper.TryMap(action, targetWindow, out InputPoint? screenPoint, out string? failureCode, out string? reason);

        Assert.False(mapped);
        Assert.Null(screenPoint);
        Assert.Equal(InputFailureCodeValues.CaptureReferenceStale, failureCode);
        Assert.Contains("capture", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MapActionRejectsPointOutsideCapturePixelsGeometry()
    {
        WindowDescriptor targetWindow = CreateWindow(bounds: new Bounds(300, 400, 620, 780));
        InputAction action = CreateAction(
            InputActionTypeValues.Click,
            InputCoordinateSpaceValues.CapturePixels,
            new InputPoint(320, 40),
            CreateCaptureReference(
                bounds: new InputBounds(300, 400, 620, 780),
                pixelWidth: 320,
                pixelHeight: 380));

        bool mapped = InputCoordinateMapper.TryMap(action, targetWindow, out InputPoint? screenPoint, out string? failureCode, out string? reason);

        Assert.False(mapped);
        Assert.Null(screenPoint);
        Assert.Equal(InputFailureCodeValues.PointOutOfBounds, failureCode);
        Assert.Contains("point", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MapActionRejectsScreenPointOutsideLiveWindowBounds()
    {
        WindowDescriptor targetWindow = CreateWindow(bounds: new Bounds(300, 400, 620, 780));
        InputAction action = CreateAction(
            InputActionTypeValues.Click,
            InputCoordinateSpaceValues.Screen,
            new InputPoint(620, 500));

        bool mapped = InputCoordinateMapper.TryMap(action, targetWindow, out InputPoint? screenPoint, out string? failureCode, out string? reason);

        Assert.False(mapped);
        Assert.Null(screenPoint);
        Assert.Equal(InputFailureCodeValues.PointOutOfBounds, failureCode);
        Assert.Contains("window", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateDispatchPlanRejectsCaptureReferenceWhenOriginDeltaWouldOverflowIntArithmetic()
    {
        InputAction action = CreateAction(
            InputActionTypeValues.Click,
            InputCoordinateSpaceValues.CapturePixels,
            new InputPoint(25, 40),
            CreateCaptureReference(
                bounds: new InputBounds(int.MinValue, 100, int.MinValue + 200, 460),
                pixelWidth: 200,
                pixelHeight: 360));
        InputPointerDispatchPlan dispatchPlan = new(action, new InputPoint(int.MinValue + 25, 140));
        WindowDescriptor liveTargetWindow = CreateWindow(bounds: new Bounds(0, 100, 200, 460));

        Exception? exception = Record.Exception(
            () => InputCoordinateMapper.TryValidateDispatchPlan(dispatchPlan, liveTargetWindow, out _, out string? failureCode, out string? reason));

        Assert.Null(exception);

        bool valid = InputCoordinateMapper.TryValidateDispatchPlan(
            dispatchPlan,
            liveTargetWindow,
            out InputPointerDispatchPlan? validatedDispatchPlan,
            out string? failureCode,
            out string? reason);

        Assert.False(valid);
        Assert.Null(validatedDispatchPlan);
        Assert.Equal(InputFailureCodeValues.CaptureReferenceStale, failureCode);
        Assert.Contains("capture", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvaluateTargetPreflightRejectsTargetThatIsNoLongerForeground()
    {
        WindowDescriptor liveWindow = CreateWindow(isForeground: false);
        InputProcessSecurityContext currentProcess = CreateCurrentProcessSecurity();
        InputTargetSecurityInfo targetSecurity = CreateTargetSecurity();

        InputTargetPreflightResult result = InputTargetPreflightPolicy.Evaluate(
            InputTargetSourceValues.Explicit,
            liveWindow,
            currentProcess,
            targetSecurity);

        Assert.False(result.IsAllowed);
        Assert.Equal(InputFailureCodeValues.TargetNotForeground, result.FailureCode);
    }

    [Fact]
    public void EvaluateTargetPreflightRejectsMinimizedWindow()
    {
        WindowDescriptor liveWindow = CreateWindow(windowState: WindowStateValues.Minimized, isForeground: true);
        InputProcessSecurityContext currentProcess = CreateCurrentProcessSecurity();
        InputTargetSecurityInfo targetSecurity = CreateTargetSecurity();

        InputTargetPreflightResult result = InputTargetPreflightPolicy.Evaluate(
            InputTargetSourceValues.Attached,
            liveWindow,
            currentProcess,
            targetSecurity);

        Assert.False(result.IsAllowed);
        Assert.Equal(InputFailureCodeValues.TargetMinimized, result.FailureCode);
    }

    [Fact]
    public void EvaluateTargetPreflightPrefersMinimizedReasonOverForegroundLoss()
    {
        WindowDescriptor liveWindow = CreateWindow(windowState: WindowStateValues.Minimized, isForeground: false);
        InputProcessSecurityContext currentProcess = CreateCurrentProcessSecurity();
        InputTargetSecurityInfo targetSecurity = CreateTargetSecurity();

        InputTargetPreflightResult result = InputTargetPreflightPolicy.Evaluate(
            InputTargetSourceValues.Attached,
            liveWindow,
            currentProcess,
            targetSecurity);

        Assert.False(result.IsAllowed);
        Assert.Equal(InputFailureCodeValues.TargetMinimized, result.FailureCode);
    }

    [Fact]
    public void EvaluateTargetPreflightRejectsTargetInDifferentSession()
    {
        WindowDescriptor liveWindow = CreateWindow();
        InputProcessSecurityContext currentProcess = CreateCurrentProcessSecurity(sessionId: 1);
        InputTargetSecurityInfo targetSecurity = CreateTargetSecurity(sessionId: 2);

        InputTargetPreflightResult result = InputTargetPreflightPolicy.Evaluate(
            InputTargetSourceValues.Explicit,
            liveWindow,
            currentProcess,
            targetSecurity);

        Assert.False(result.IsAllowed);
        Assert.Equal(InputFailureCodeValues.TargetIntegrityBlocked, result.FailureCode);
        Assert.Contains("session", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvaluateTargetPreflightRejectsHigherIntegrityTargetWhenUiAccessIsMissing()
    {
        WindowDescriptor liveWindow = CreateWindow();
        InputProcessSecurityContext currentProcess = CreateCurrentProcessSecurity(
            integrityLevel: InputIntegrityLevel.Medium,
            hasUiAccess: false);
        InputTargetSecurityInfo targetSecurity = CreateTargetSecurity(integrityLevel: InputIntegrityLevel.High);

        InputTargetPreflightResult result = InputTargetPreflightPolicy.Evaluate(
            InputTargetSourceValues.Explicit,
            liveWindow,
            currentProcess,
            targetSecurity);

        Assert.False(result.IsAllowed);
        Assert.Equal(InputFailureCodeValues.TargetIntegrityBlocked, result.FailureCode);
        Assert.Contains("integrity", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvaluateTargetPreflightRejectsTargetWhenSecurityProbeIsUnresolved()
    {
        WindowDescriptor liveWindow = CreateWindow();
        InputProcessSecurityContext currentProcess = CreateCurrentProcessSecurity();
        InputTargetSecurityInfo targetSecurity = new(
            ProcessId: 321,
            SessionId: null,
            SessionResolved: false,
            IntegrityLevel: null,
            IntegrityResolved: false,
            Reason: "Token probe failed.");

        InputTargetPreflightResult result = InputTargetPreflightPolicy.Evaluate(
            InputTargetSourceValues.Explicit,
            liveWindow,
            currentProcess,
            targetSecurity);

        Assert.False(result.IsAllowed);
        Assert.Equal(InputFailureCodeValues.TargetIntegrityBlocked, result.FailureCode);
        Assert.Contains("token", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AmbientInputPolicyAllowsDispatchWhenKeyboardAndMouseStateAreNeutral()
    {
        InputAmbientInputProbeResult result = InputAmbientInputPolicy.Probe(
            new InputAmbientInputProbeContext(
                CanReadAsyncState: true,
                MouseButtonsSwapped: false,
                UnknownReason: null),
            _ => 0);

        Assert.Equal(InputAmbientInputProofStatus.Neutral, result.Status);
        Assert.Null(result.FailureCode);
        Assert.Null(result.Reason);
    }

    [Fact]
    public void AmbientInputPolicyRejectsDispatchWhenModifierIsHeld()
    {
        InputAmbientInputProbeResult result = InputAmbientInputPolicy.Probe(
            new InputAmbientInputProbeContext(
                CanReadAsyncState: true,
                MouseButtonsSwapped: false,
                UnknownReason: null),
            virtualKey => virtualKey == 0x11 ? unchecked((short)0x8000) : (short)0);

        Assert.Equal(InputAmbientInputProofStatus.NonNeutral, result.Status);
        Assert.Equal(InputFailureCodeValues.InputDispatchFailed, result.FailureCode);
        Assert.Contains(InputModifierKeyValues.Ctrl, result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AmbientInputPolicyRejectsDispatchWhenMouseButtonIsAlreadyHeld()
    {
        InputAmbientInputProbeResult result = InputAmbientInputPolicy.Probe(
            new InputAmbientInputProbeContext(
                CanReadAsyncState: true,
                MouseButtonsSwapped: true,
                UnknownReason: null),
            virtualKey => virtualKey == 0x01 ? unchecked((short)0x8000) : (short)0);

        Assert.Equal(InputAmbientInputProofStatus.NonNeutral, result.Status);
        Assert.Equal(InputFailureCodeValues.InputDispatchFailed, result.FailureCode);
        Assert.Contains("прав", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("мыш", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AmbientInputPolicyRejectsDispatchWhenAmbientStateCannotBeProven()
    {
        InputAmbientInputProbeResult result = InputAmbientInputPolicy.Probe(
            new InputAmbientInputProbeContext(
                CanReadAsyncState: false,
                MouseButtonsSwapped: false,
                UnknownReason: "Current thread is not attached to the active input desktop."),
            _ => 0);

        Assert.Equal(InputAmbientInputProofStatus.Unknown, result.Status);
        Assert.Equal(InputFailureCodeValues.InputDispatchFailed, result.FailureCode);
        Assert.Contains("desktop", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MouseButtonSemanticsMapsLogicalLeftToPhysicalRightWhenButtonsAreSwapped()
    {
        (uint downFlag, uint upFlag) = InputMouseButtonSemantics.GetDispatchFlags(
            InputButtonValues.Left,
            mouseButtonsSwapped: true);

        Assert.Equal(0x0008u, downFlag);
        Assert.Equal(0x0010u, upFlag);
    }

    [Fact]
    public void MouseButtonSemanticsMapsLogicalRightToPhysicalLeftWhenButtonsAreSwapped()
    {
        (uint downFlag, uint upFlag) = InputMouseButtonSemantics.GetDispatchFlags(
            InputButtonValues.Right,
            mouseButtonsSwapped: true);

        Assert.Equal(0x0002u, downFlag);
        Assert.Equal(0x0004u, upFlag);
    }

    [Fact]
    public void ClickDispatchOutcomePolicyTreatsZeroInsertedEventsAsCleanFailure()
    {
        InputClickDispatchResult result = InputClickDispatchOutcomePolicy.FromSendInputCounts(
            logicalButton: InputButtonValues.Left,
            insertedEvents: 0,
            expectedEvents: 2,
            compensationInsertedEvents: 0,
            compensationExpectedEvents: 0);

        Assert.False(result.Success);
        Assert.Equal(InputClickDispatchOutcomeKind.CleanFailure, result.OutcomeKind);
        Assert.Contains("не был подтвержд", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ClickDispatchOutcomePolicyTreatsSingleInsertedEventAsPartialDispatchEvenWhenCompensated()
    {
        InputClickDispatchResult result = InputClickDispatchOutcomePolicy.FromSendInputCounts(
            logicalButton: InputButtonValues.Left,
            insertedEvents: 1,
            expectedEvents: 2,
            compensationInsertedEvents: 1,
            compensationExpectedEvents: 1);

        Assert.False(result.Success);
        Assert.Equal(InputClickDispatchOutcomeKind.PartialDispatchCompensated, result.OutcomeKind);
        Assert.Contains("частично", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AsyncStateReadabilityModeSelectorUsesSameProcessModeForSameProcessTarget()
    {
        InputAsyncStateReadabilityMode mode = InputAsyncStateReadabilityModeSelector.Select(
            foregroundOwnerProcessId: 42,
            currentProcessId: 42);

        Assert.Equal(InputAsyncStateReadabilityMode.SameProcessForeground, mode);
    }

    [Fact]
    public void AsyncStateReadabilityModeSelectorUsesCrossProcessModeForDifferentProcessTarget()
    {
        InputAsyncStateReadabilityMode mode = InputAsyncStateReadabilityModeSelector.Select(
            foregroundOwnerProcessId: 41,
            currentProcessId: 42);

        Assert.Equal(InputAsyncStateReadabilityMode.CrossProcessForeground, mode);
    }

    [Fact]
    public void AsyncStateReadabilityEvaluatorUsesSameProcessModeForForegroundOwnerOfCurrentProcess()
    {
        InputAsyncStateReadabilityMode? capturedMode = null;

        InputAsyncStateReadabilityProbeResult result = InputAsyncStateReadabilityEvaluator.ProbeForForegroundOwner(
            foregroundOwnerProcessId: 42,
            currentProcessId: 42,
            probe: mode =>
            {
                capturedMode = mode;
                return new InputAsyncStateReadabilityProbeResult(InputAsyncStateReadabilityStatus.Readable);
            });

        Assert.Equal(InputAsyncStateReadabilityMode.SameProcessForeground, capturedMode);
        Assert.Equal(InputAsyncStateReadabilityStatus.Readable, result.Status);
    }

    [Fact]
    public void AsyncStateReadinessBaselineEvaluatorStaysOnCrossProcessModeEvenWhenForegroundOwnerMatchesCurrentProcess()
    {
        InputAsyncStateReadabilityMode? capturedMode = null;

        InputAsyncStateReadabilityProbeResult result = InputAsyncStateReadinessBaselineEvaluator.Probe(
            probe: mode =>
            {
                capturedMode = mode;
                return new InputAsyncStateReadabilityProbeResult(InputAsyncStateReadabilityStatus.Readable);
            });

        Assert.Equal(InputAsyncStateReadabilityMode.CrossProcessForeground, capturedMode);
        Assert.Equal(InputAsyncStateReadabilityStatus.Readable, result.Status);
    }

    [Fact]
    public void AsyncStateReadabilityProbeAcceptsHookControlWithoutJournalRecordForCrossProcessPath()
    {
        FakeInputAsyncStateReadabilityPlatform platform = new()
        {
            ThreadDesktop = new IntPtr(1),
            ThreadDesktopReceivesInput = true,
            OpenInputDesktopResults =
            {
                [InputAsyncStateReadabilityProbe.DesktopHookControlAccess] = new IntPtr(2),
                [InputAsyncStateReadabilityProbe.DesktopJournalRecordAccess] = IntPtr.Zero,
            },
        };

        InputAsyncStateReadabilityProbeResult result = InputAsyncStateReadabilityProbe.ProbeForCurrentThread(
            InputAsyncStateReadabilityMode.CrossProcessForeground,
            platform);

        Assert.Equal(InputAsyncStateReadabilityStatus.Readable, result.Status);
        Assert.Equal(
            [InputAsyncStateReadabilityProbe.DesktopHookControlAccess],
            platform.OpenInputDesktopAttempts);
    }

    [Fact]
    public void AsyncStateReadabilityProbeAcceptsJournalRecordWithoutHookControlForCrossProcessPath()
    {
        FakeInputAsyncStateReadabilityPlatform platform = new()
        {
            ThreadDesktop = new IntPtr(1),
            ThreadDesktopReceivesInput = true,
            OpenInputDesktopResults =
            {
                [InputAsyncStateReadabilityProbe.DesktopHookControlAccess] = IntPtr.Zero,
                [InputAsyncStateReadabilityProbe.DesktopJournalRecordAccess] = new IntPtr(3),
            },
        };

        InputAsyncStateReadabilityProbeResult result = InputAsyncStateReadabilityProbe.ProbeForCurrentThread(
            InputAsyncStateReadabilityMode.CrossProcessForeground,
            platform);

        Assert.Equal(InputAsyncStateReadabilityStatus.Readable, result.Status);
        Assert.Equal(
            [
                InputAsyncStateReadabilityProbe.DesktopHookControlAccess,
                InputAsyncStateReadabilityProbe.DesktopJournalRecordAccess,
            ],
            platform.OpenInputDesktopAttempts);
    }

    [Fact]
    public void CancellationMaterializationPolicyUsesCommittedContextForBetweenActionsCancellation()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        InputCommittedSideEffectContext committedContext = new(
            ActionIndex: 0,
            Action: CreateAction(InputActionTypeValues.Click, InputCoordinateSpaceValues.Screen, new InputPoint(140, 260)),
            Phase: InputIrreversiblePhase.AfterClickTap,
            ResolvedScreenPoint: new InputPoint(140, 260),
            Button: InputButtonValues.Left,
            TargetHwnd: 101);

        bool materialized = InputCancellationMaterializationPolicy.TryCreate(
            committedContext,
            InputCancellationObservationContext.BetweenActions,
            cancellation.Token,
            out InputCancellationMaterializationDecision? decision);

        Assert.True(materialized);
        Assert.NotNull(decision);
        Assert.False(decision.ShouldAppendFailedAction);
        Assert.Null(decision.FailedActionIndex);
        Assert.Contains("next action was not started", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CancellationMaterializationPolicyReportsFullyDispatchedDoubleClickAfterSecondTap()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        InputCommittedSideEffectContext committedContext = new(
            ActionIndex: 0,
            Action: CreateAction(InputActionTypeValues.DoubleClick, InputCoordinateSpaceValues.Screen, new InputPoint(140, 260)),
            Phase: InputIrreversiblePhase.AfterDoubleClickSecondTap,
            ResolvedScreenPoint: new InputPoint(140, 260),
            Button: InputButtonValues.Left,
            TargetHwnd: 101);

        bool materialized = InputCancellationMaterializationPolicy.TryCreate(
            committedContext,
            InputCancellationObservationContext.InFlightAction,
            cancellation.Token,
            out InputCancellationMaterializationDecision? decision);

        Assert.True(materialized);
        Assert.NotNull(decision);
        Assert.True(decision.ShouldAppendFailedAction);
        Assert.Equal(0, decision.FailedActionIndex);
        Assert.Contains("both", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CancellationMaterializationPolicyDistinguishesFinalBatchCancellationFromBetweenActions()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        InputCommittedSideEffectContext committedContext = new(
            ActionIndex: 0,
            Action: CreateAction(InputActionTypeValues.Click, InputCoordinateSpaceValues.Screen, new InputPoint(140, 260)),
            Phase: InputIrreversiblePhase.AfterClickTap,
            ResolvedScreenPoint: new InputPoint(140, 260),
            Button: InputButtonValues.Left,
            TargetHwnd: 101);

        bool materialized = InputCancellationMaterializationPolicy.TryCreate(
            committedContext,
            InputCancellationObservationContext.AfterBatchCompletedBeforeSuccessReturn,
            cancellation.Token,
            out InputCancellationMaterializationDecision? decision);

        Assert.True(materialized);
        Assert.NotNull(decision);
        Assert.False(decision.ShouldAppendFailedAction);
        Assert.Null(decision.FailedActionIndex);
        Assert.Contains("full batch", decision.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("next action was not started", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BatchStateMaterializesExceptionCancellationAsBetweenActionsBeforeNextActionStarts()
    {
        InputBatchExecutionState batch = CreateBatchState();
        batch.BeginAction(0, CreateAction(InputActionTypeValues.Click, InputCoordinateSpaceValues.Screen, new InputPoint(140, 260)), effectiveButton: InputButtonValues.Left);
        batch.UpdateResolvedPoint(new InputPoint(140, 260));
        batch.UpdateTargetHwnd(101);
        batch.RecordCommittedSideEffect(InputIrreversiblePhase.AfterClickTap);
        batch.CompleteCurrentActionSuccess();
        batch.BeginAction(1, CreateAction(InputActionTypeValues.Click, InputCoordinateSpaceValues.Screen, new InputPoint(150, 270)), effectiveButton: InputButtonValues.Left);

        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        InputResult result = batch.MaterializeExceptionCancellation(cancellation.Token);

        Assert.Null(result.FailedActionIndex);
        Assert.Contains("next action was not started", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BatchStateMaterializesExceptionCancellationAsInFlightAfterCommittedTap()
    {
        InputBatchExecutionState batch = CreateBatchState();
        batch.BeginAction(0, CreateAction(InputActionTypeValues.DoubleClick, InputCoordinateSpaceValues.Screen, new InputPoint(140, 260)), effectiveButton: InputButtonValues.Left);
        batch.UpdateResolvedPoint(new InputPoint(140, 260));
        batch.UpdateTargetHwnd(101);
        batch.RecordCommittedSideEffect(InputIrreversiblePhase.AfterDoubleClickFirstTap);

        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        InputResult result = batch.MaterializeExceptionCancellation(cancellation.Token);

        Assert.Equal(0, result.FailedActionIndex);
        Assert.Contains("first double_click tap", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    private static InputAction CreateAction(
        string type,
        string coordinateSpace,
        InputPoint point,
        InputCaptureReference? captureReference = null) =>
        new()
        {
            Type = type,
            Point = point,
            CoordinateSpace = coordinateSpace,
            CaptureReference = captureReference,
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

    private static InputBatchExecutionState CreateBatchState() =>
        new(
            CreateWindow(),
            InputTargetSourceValues.Explicit,
            CreateCurrentProcessSecurity());

    private static InputCaptureReference CreateCaptureReference(
        InputBounds? bounds = null,
        int pixelWidth = 320,
        int pixelHeight = 360,
        int? effectiveDpi = 96) =>
        new(
            bounds ?? new InputBounds(100, 200, 420, 560),
            pixelWidth,
            pixelHeight,
            effectiveDpi,
            capturedAtUtc: DateTimeOffset.UtcNow);

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

    private static InputTargetSecurityInfo CreateTargetSecurity(
        int sessionId = 1,
        InputIntegrityLevel integrityLevel = InputIntegrityLevel.Medium) =>
        new(
            ProcessId: 321,
            SessionId: sessionId,
            SessionResolved: true,
            IntegrityLevel: integrityLevel,
            IntegrityResolved: true,
            Reason: null);

    private sealed class FakeInputAsyncStateReadabilityPlatform : IInputAsyncStateReadabilityPlatform
    {
        public Dictionary<uint, IntPtr> OpenInputDesktopResults { get; } = [];

        public List<uint> OpenInputDesktopAttempts { get; } = [];

        public IntPtr ThreadDesktop { get; init; }

        public bool ThreadDesktopReceivesInput { get; init; }

        public uint GetCurrentThreadId() => 1;

        public IntPtr GetThreadDesktop(uint threadId) => ThreadDesktop;

        public bool TryQueryDesktopReceivesInput(IntPtr desktopHandle, out bool receivesInput)
        {
            receivesInput = ThreadDesktopReceivesInput;
            return true;
        }

        public IntPtr OpenInputDesktop(uint desiredAccess)
        {
            OpenInputDesktopAttempts.Add(desiredAccess);
            return OpenInputDesktopResults.TryGetValue(desiredAccess, out IntPtr handle)
                ? handle
                : IntPtr.Zero;
        }

        public void CloseDesktop(IntPtr hDesktop)
        {
        }
    }
}
