// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Runtime.InteropServices;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Shell;
using Native = WinBridge.Runtime.Windows.Input.Win32InputNativeMethods;

namespace WinBridge.Runtime.Windows.Input;

internal sealed class Win32InputPlatform : IInputPlatform
{
    public InputProcessSecurityContext ProbeCurrentProcessSecurity() =>
        Win32InputSecurityProbe.ProbeCurrentProcessSecurity();

    public InputTargetSecurityInfo ProbeTargetSecurity(long hwnd, int? processIdHint) =>
        Win32InputSecurityProbe.ProbeTargetSecurity(hwnd, processIdHint);

    public bool TrySetCursorPosition(InputPoint screenPoint) =>
        Native.SetCursorPos(screenPoint.X, screenPoint.Y);

    public bool TryGetCursorPosition(out InputPoint screenPoint)
    {
        if (Native.GetCursorPos(out Native.POINT point))
        {
            screenPoint = new InputPoint(point.X, point.Y);
            return true;
        }

        screenPoint = new InputPoint(0, 0);
        return false;
    }

    public InputPointerSideEffectBoundaryResult ValidatePointerSideEffectBoundary(WindowDescriptor admittedTargetWindow) =>
        Win32PointerBoundaryValidator.ValidatePointerSideEffectBoundary(admittedTargetWindow);

    public InputClickDispatchResult DispatchClick(InputClickDispatchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        InputDispatchEnvironmentResult environmentResult = Win32PointerBoundaryValidator.ValidateDispatchEnvironment(context);
        if (!environmentResult.Success)
        {
            return InputClickDispatchResult.PreDispatchFailure(
                environmentResult.FailureCode ?? InputFailureCodeValues.InputDispatchFailed,
                environmentResult.Reason ?? "Runtime не смог подтвердить click dispatch environment.");
        }

        bool mouseButtonsSwapped = environmentResult.MouseButtonsSwapped;
        (uint downFlag, uint upFlag) = InputMouseButtonSemantics.GetDispatchFlags(context.LogicalButton, mouseButtonsSwapped);

        Native.INPUT[] inputs =
        [
            Win32MouseInputSequenceBuilder.CreateMouseInput(downFlag),
            Win32MouseInputSequenceBuilder.CreateMouseInput(upFlag),
        ];

        uint sent = Native.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Native.INPUT>());
        if (sent == inputs.Length)
        {
            return InputClickDispatchResult.Succeeded();
        }

        uint compensationSent = 0;
        if (sent > 0)
        {
            Native.INPUT[] compensationInputs =
            [
                Win32MouseInputSequenceBuilder.CreateMouseInput(upFlag),
            ];
            compensationSent = Native.SendInput((uint)compensationInputs.Length, compensationInputs, Marshal.SizeOf<Native.INPUT>());
        }

        return InputClickDispatchOutcomePolicy.FromSendInputCounts(
            logicalButton: context.LogicalButton,
            insertedEvents: sent,
            expectedEvents: (uint)inputs.Length,
            compensationInsertedEvents: compensationSent,
            compensationExpectedEvents: sent > 0 ? 1u : 0u);
    }

    public InputDispatchResult DispatchText(InputTextDispatchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        InputPointerSideEffectBoundaryResult boundaryResult = Win32PointerBoundaryValidator.ValidatePointerSideEffectBoundary(context.AdmittedTargetWindow);
        if (!boundaryResult.Success)
        {
            return new(
                Success: false,
                CommittedSideEffects: false,
                FailureCode: boundaryResult.FailureCode,
                Reason: boundaryResult.Reason);
        }

        if (!Win32KeyboardInputSequenceBuilder.TryBuildTextInputs(context.Text, out Native.INPUT[]? inputs, out string? failureCode, out string? reason))
        {
            return new(
                Success: false,
                CommittedSideEffects: false,
                FailureCode: failureCode ?? InputFailureCodeValues.InvalidRequest,
                Reason: reason ?? "Runtime не смог подготовить text dispatch.");
        }

        uint sent = Native.SendInput((uint)inputs!.Length, inputs, Marshal.SizeOf<Native.INPUT>());
        if (sent != inputs.Length)
        {
            return new(
                Success: false,
                CommittedSideEffects: sent > 0,
                FailureCode: InputFailureCodeValues.InputDispatchFailed,
                Reason: "SendInput не подтвердил полный text dispatch.");
        }

        return new(
            Success: true,
            CommittedSideEffects: true);
    }

    public InputDispatchResult DispatchKeypress(InputKeypressDispatchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        InputPointerSideEffectBoundaryResult boundaryResult = Win32PointerBoundaryValidator.ValidatePointerSideEffectBoundary(context.AdmittedTargetWindow);
        if (!boundaryResult.Success)
        {
            return new(
                Success: false,
                CommittedSideEffects: false,
                FailureCode: boundaryResult.FailureCode,
                Reason: boundaryResult.Reason);
        }

        if (!Win32KeyboardInputSequenceBuilder.TryBuildKeypressInputs(context.Key, context.Repeat, out Native.INPUT[]? inputs, out string? failureCode, out string? reason))
        {
            return new(
                Success: false,
                CommittedSideEffects: false,
                FailureCode: failureCode ?? InputFailureCodeValues.UnsupportedKey,
                Reason: reason ?? "Runtime не смог нормализовать key literal для keypress dispatch.");
        }

        uint sent = Native.SendInput((uint)inputs!.Length, inputs, Marshal.SizeOf<Native.INPUT>());
        if (sent != inputs.Length)
        {
            if (sent == 0u)
            {
                return new(
                    Success: false,
                    CommittedSideEffects: false,
                    FailureCode: InputFailureCodeValues.InputDispatchFailed,
                    Reason: "SendInput не подтвердил полный keypress dispatch.");
            }

            Native.INPUT[] compensationInputs = Win32KeyboardInputSequenceBuilder.CreateKeypressCompensationInputs(inputs, sent);
            bool compensationSucceeded = compensationInputs.Length == 0;
            if (!compensationSucceeded)
            {
                uint released = Native.SendInput((uint)compensationInputs.Length, compensationInputs, Marshal.SizeOf<Native.INPUT>());
                compensationSucceeded = released == compensationInputs.Length;
            }

            return new(
                Success: false,
                CommittedSideEffects: true,
                FailureCode: InputFailureCodeValues.InputDispatchFailed,
                Reason: compensationSucceeded
                    ? "SendInput не подтвердил полный keypress dispatch; key-up compensation succeeded."
                    : "SendInput не подтвердил полный keypress dispatch; key-up compensation failed.",
                FailureStageHint: compensationSucceeded
                    ? InputFailureStageValues.KeypressDispatchPartialCompensated
                    : InputFailureStageValues.KeypressDispatchPartialUncompensated);
        }

        return new(
            Success: true,
            CommittedSideEffects: true);
    }

    public InputDispatchResult DispatchScroll(InputScrollDispatchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        InputDispatchEnvironmentResult environmentResult = Win32PointerBoundaryValidator.ValidateDispatchEnvironment(
            new InputClickDispatchContext(
                context.ExpectedScreenPoint,
                InputButtonValues.Left,
                context.AdmittedTargetWindow));
        if (!environmentResult.Success)
        {
            return new(
                Success: false,
                CommittedSideEffects: false,
                FailureCode: environmentResult.FailureCode,
                Reason: environmentResult.Reason);
        }

        if (!Win32MouseInputSequenceBuilder.TryCreateScrollInput(context.Direction, context.Delta, out Native.INPUT scrollInput, out string? failureCode, out string? reason))
        {
            return new(
                Success: false,
                CommittedSideEffects: false,
                FailureCode: failureCode ?? InputFailureCodeValues.InvalidRequest,
                Reason: reason ?? "Runtime не смог подготовить scroll dispatch.");
        }

        uint sent = Native.SendInput(1u, [scrollInput], Marshal.SizeOf<Native.INPUT>());
        if (sent != 1u)
        {
            return new(
                Success: false,
                CommittedSideEffects: false,
                FailureCode: InputFailureCodeValues.InputDispatchFailed,
                Reason: "SendInput не подтвердил scroll dispatch.");
        }

        return new(
            Success: true,
            CommittedSideEffects: true);
    }

    public InputDispatchResult DispatchDrag(InputDragDispatchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.ScreenPath.Count < 2)
        {
            return new(
                Success: false,
                CommittedSideEffects: false,
                FailureCode: InputFailureCodeValues.InvalidRequest,
                Reason: "Drag dispatch требует screen path минимум из двух точек.");
        }

        InputPoint startPoint = context.ScreenPath[0];
        InputDispatchEnvironmentResult environmentResult = Win32PointerBoundaryValidator.ValidateDispatchEnvironment(
            new InputClickDispatchContext(
                startPoint,
                InputButtonValues.Left,
                context.AdmittedTargetWindow));
        if (!environmentResult.Success)
        {
            return new(
                Success: false,
                CommittedSideEffects: false,
                FailureCode: environmentResult.FailureCode,
                Reason: environmentResult.Reason);
        }

        bool mouseButtonsSwapped = environmentResult.MouseButtonsSwapped;
        (uint downFlag, uint upFlag) = InputMouseButtonSemantics.GetDispatchFlags(InputButtonValues.Left, mouseButtonsSwapped);

        uint downSent = Native.SendInput(1u, [Win32MouseInputSequenceBuilder.CreateMouseInput(downFlag)], Marshal.SizeOf<Native.INPUT>());
        if (downSent != 1u)
        {
            return new(
                Success: false,
                CommittedSideEffects: false,
                FailureCode: InputFailureCodeValues.InputDispatchFailed,
                Reason: "SendInput не подтвердил drag button-down dispatch.");
        }

        for (int index = 1; index < context.ScreenPath.Count; index++)
        {
            InputPoint dragPoint = context.ScreenPath[index];
            if (!TrySetCursorPosition(dragPoint))
            {
                return CreateDragPostDownFailure(
                    upFlag,
                    InputFailureCodeValues.CursorMoveFailed,
                    $"SetCursorPos вернул failure для drag path point {index}.");
            }

            if (!TryGetCursorPosition(out InputPoint observedPoint))
            {
                return CreateDragPostDownFailure(
                    upFlag,
                    InputFailureCodeValues.CursorMoveFailed,
                    $"Runtime не смог подтвердить cursor position после drag path point {index}.");
            }

            if (!Equals(observedPoint, dragPoint))
            {
                return CreateDragPostDownFailure(
                    upFlag,
                    InputFailureCodeValues.CursorMoveFailed,
                    $"Cursor position drifted during drag on path point {index}: фактическая точка ({observedPoint.X},{observedPoint.Y}) не совпадает с ожидаемой ({dragPoint.X},{dragPoint.Y}).");
            }

            InputPointerSideEffectBoundaryResult boundaryResult = Win32PointerBoundaryValidator.ValidateForegroundBoundaryDuringDrag(context.AdmittedTargetWindow);
            if (!boundaryResult.Success)
            {
                return CreateDragPostDownFailure(
                    upFlag,
                    boundaryResult.FailureCode ?? InputFailureCodeValues.InputDispatchFailed,
                    boundaryResult.Reason ?? "Runtime потерял foreground boundary во время drag dispatch.");
            }
        }

        uint upSent = Native.SendInput(1u, [Win32MouseInputSequenceBuilder.CreateMouseInput(upFlag)], Marshal.SizeOf<Native.INPUT>());
        if (upSent != 1u)
        {
            return new(
                Success: false,
                CommittedSideEffects: true,
                FailureCode: InputFailureCodeValues.InputDispatchFailed,
                Reason: "SendInput не подтвердил drag button-up dispatch.",
                FailureStageHint: InputFailureStageValues.DragDispatchPartialUncompensated);
        }

        return new(
            Success: true,
            CommittedSideEffects: true);
    }

    private static InputDispatchResult CreateDragPostDownFailure(
        uint upFlag,
        string failureCode,
        string reason)
    {
        uint releaseSent = Native.SendInput(1u, [Win32MouseInputSequenceBuilder.CreateMouseInput(upFlag)], Marshal.SizeOf<Native.INPUT>());
        bool releaseSucceeded = releaseSent == 1u;
        return new(
            Success: false,
            CommittedSideEffects: true,
            FailureCode: failureCode,
            Reason: releaseSucceeded
                ? $"{reason} Best-effort drag button-up compensation succeeded."
                : $"{reason} Best-effort drag button-up compensation also failed.",
            FailureStageHint: releaseSucceeded
                ? InputFailureStageValues.DragDispatchPartialCompensated
                : InputFailureStageValues.DragDispatchPartialUncompensated);
    }
}
