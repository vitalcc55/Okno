// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Shell;
using Native = WinBridge.Runtime.Windows.Input.Win32InputNativeMethods;

namespace WinBridge.Runtime.Windows.Input;

internal static class Win32PointerBoundaryValidator
{
    public static InputPointerSideEffectBoundaryResult ValidatePointerSideEffectBoundary(WindowDescriptor admittedTargetWindow)
    {
        ArgumentNullException.ThrowIfNull(admittedTargetWindow);
        return ValidatePointerSideEffectBoundaryCore(admittedTargetWindow);
    }

    public static InputDispatchEnvironmentResult ValidateDispatchEnvironment(InputClickDispatchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!Native.GetCursorPos(out Native.POINT point))
        {
            return InputDispatchEnvironmentResult.Failure(
                InputFailureCodeValues.CursorMoveFailed,
                "Runtime не смог подтвердить cursor position непосредственно перед click dispatch.");
        }

        InputPoint currentCursorPoint = new(point.X, point.Y);
        if (!Equals(currentCursorPoint, context.ExpectedScreenPoint))
        {
            return InputDispatchEnvironmentResult.Failure(
                InputFailureCodeValues.CursorMoveFailed,
                $"Cursor position drifted before click dispatch: фактическая точка ({currentCursorPoint.X},{currentCursorPoint.Y}) не совпадает с ожидаемой ({context.ExpectedScreenPoint.X},{context.ExpectedScreenPoint.Y}).");
        }

        InputPointerSideEffectBoundaryResult boundaryResult = ValidatePointerSideEffectBoundaryCore(context.AdmittedTargetWindow);
        if (!boundaryResult.Success)
        {
            return InputDispatchEnvironmentResult.Failure(
                boundaryResult.FailureCode ?? InputFailureCodeValues.InputDispatchFailed,
                boundaryResult.Reason ?? "Runtime не смог подтвердить click dispatch boundary.");
        }

        return InputDispatchEnvironmentResult.Succeeded(boundaryResult.MouseButtonsSwapped);
    }

    public static InputPointerSideEffectBoundaryResult ValidateForegroundBoundaryDuringDrag(WindowDescriptor admittedTargetWindow)
    {
        GetForegroundWindowSnapshot(out long? foregroundHwnd, out ActivatedWindowVerificationSnapshot foregroundSnapshot);
        if (!InputForegroundTargetBoundaryPolicy.TryValidate(
                foregroundHwnd,
                foregroundSnapshot,
                admittedTargetWindow,
                out _,
                out string? foregroundFailureCode,
                out string? foregroundReason))
        {
            return InputPointerSideEffectBoundaryResult.Failure(
                foregroundFailureCode ?? InputFailureCodeValues.InputDispatchFailed,
                foregroundReason ?? "Runtime потерял foreground boundary во время drag dispatch.");
        }

        return InputPointerSideEffectBoundaryResult.Succeeded();
    }

    private static InputPointerSideEffectBoundaryResult ValidatePointerSideEffectBoundaryCore(WindowDescriptor admittedTargetWindow)
    {
        GetForegroundWindowSnapshot(out long? foregroundHwnd, out ActivatedWindowVerificationSnapshot foregroundSnapshot);
        if (!InputForegroundTargetBoundaryPolicy.TryValidate(
                foregroundHwnd,
                foregroundSnapshot,
                admittedTargetWindow,
                out int? validatedForegroundOwnerProcessId,
                out string? foregroundFailureCode,
                out string? foregroundReason))
        {
            return InputPointerSideEffectBoundaryResult.Failure(
                foregroundFailureCode ?? InputFailureCodeValues.InputDispatchFailed,
                foregroundReason ?? "Runtime не смог подтвердить pointer side effect boundary.");
        }

        InputAmbientInputProbeContext probeContext = CreateAmbientInputProbeContext(validatedForegroundOwnerProcessId);
        InputAmbientInputProbeResult ambientInput = InputAmbientInputPolicy.Probe(probeContext, Native.GetAsyncKeyState);
        if (ambientInput.Status != InputAmbientInputProofStatus.Neutral)
        {
            return InputPointerSideEffectBoundaryResult.Failure(
                ambientInput.FailureCode ?? InputFailureCodeValues.InputDispatchFailed,
                ambientInput.Reason ?? "Runtime не смог доказать neutral ambient input state.");
        }

        return InputPointerSideEffectBoundaryResult.Succeeded(probeContext.MouseButtonsSwapped);
    }

    private static InputAmbientInputProbeContext CreateAmbientInputProbeContext(int? foregroundOwnerProcessId)
    {
        InputAsyncStateReadabilityProbeResult readability = InputAsyncStateReadabilityEvaluator.ProbeForForegroundOwner(
            foregroundOwnerProcessId,
            Environment.ProcessId,
            InputAsyncStateReadabilityProbe.ProbeForCurrentThread);
        return readability.Status == InputAsyncStateReadabilityStatus.Readable
            ? new(
                CanReadAsyncState: true,
                MouseButtonsSwapped: Native.GetSystemMetrics(Native.SmSwapButton) != 0,
                UnknownReason: null)
            : new(
                CanReadAsyncState: false,
                MouseButtonsSwapped: false,
                UnknownReason: readability.Reason);
    }

    private static void GetForegroundWindowSnapshot(
        out long? foregroundHwnd,
        out ActivatedWindowVerificationSnapshot foregroundSnapshot)
    {
        IntPtr foregroundWindow = Native.GetForegroundWindow();
        if (foregroundWindow == IntPtr.Zero)
        {
            foregroundHwnd = null;
            foregroundSnapshot = new(
                Exists: false,
                ProcessId: null,
                ThreadId: null,
                ClassName: null,
                IsForeground: false,
                IsMinimized: false);
            return;
        }

        foregroundHwnd = foregroundWindow.ToInt64();
        foregroundSnapshot = ProbeForegroundWindowSnapshot(foregroundWindow);
    }

    private static ActivatedWindowVerificationSnapshot ProbeForegroundWindowSnapshot(IntPtr foregroundWindow)
    {
        uint threadId = Native.GetWindowThreadProcessId(foregroundWindow, out uint processId);
        return new(
            Exists: true,
            ProcessId: threadId == 0 ? null : checked((int)processId),
            ThreadId: threadId == 0 ? null : checked((int)threadId),
            ClassName: TryGetWindowClassName(foregroundWindow),
            IsForeground: true,
            IsMinimized: false);
    }

    private static string? TryGetWindowClassName(IntPtr hwnd)
    {
        char[] buffer = new char[256];
        int length = Native.GetClassName(hwnd, buffer, buffer.Length);
        return length > 0 ? new string(buffer, 0, length) : null;
    }
}
