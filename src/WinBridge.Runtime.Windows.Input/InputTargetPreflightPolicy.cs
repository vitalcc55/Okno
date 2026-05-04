// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Input;

internal static class InputTargetPreflightPolicy
{
    public static InputTargetPreflightResult Evaluate(
        string targetSource,
        WindowDescriptor liveWindow,
        InputProcessSecurityContext currentProcess,
        InputTargetSecurityInfo targetSecurity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetSource);
        ArgumentNullException.ThrowIfNull(liveWindow);
        ArgumentNullException.ThrowIfNull(currentProcess);
        ArgumentNullException.ThrowIfNull(targetSecurity);

        InputTargetPreflightResult? usabilityFailure = ClassifyUsability(liveWindow);
        if (usabilityFailure is not null)
        {
            return usabilityFailure;
        }

        if (!currentProcess.SessionResolved || currentProcess.SessionId is null)
        {
            return Fail(
                InputFailureCodeValues.TargetIntegrityBlocked,
                "Runtime не смог подтвердить session текущего процесса для input preflight.");
        }

        if (!currentProcess.IntegrityResolved || currentProcess.IntegrityLevel is null)
        {
            return Fail(
                InputFailureCodeValues.TargetIntegrityBlocked,
                "Runtime не смог подтвердить integrity текущего процесса для input preflight.");
        }

        if (!targetSecurity.SessionResolved || targetSecurity.SessionId is null)
        {
            return Fail(
                InputFailureCodeValues.TargetIntegrityBlocked,
                targetSecurity.Reason
                ?? "Runtime не смог подтвердить session окна-цели для input preflight.");
        }

        if (!targetSecurity.IntegrityResolved || targetSecurity.IntegrityLevel is null)
        {
            return Fail(
                InputFailureCodeValues.TargetIntegrityBlocked,
                targetSecurity.Reason
                ?? "Runtime не смог подтвердить integrity окна-цели для input preflight.");
        }

        if (currentProcess.SessionId.Value != targetSecurity.SessionId.Value)
        {
            return Fail(
                InputFailureCodeValues.TargetIntegrityBlocked,
                "Окно-цель принадлежит другой session; cross-session input path не поддерживается.");
        }

        if (CompareIntegrity(currentProcess.IntegrityLevel.Value, targetSecurity.IntegrityLevel.Value) < 0
            && !CanBypassHigherIntegrity(currentProcess))
        {
            return Fail(
                InputFailureCodeValues.TargetIntegrityBlocked,
                "Окно-цель имеет более высокий integrity profile; без uiAccess runtime не может честно обещать такой input path.");
        }

        return new(IsAllowed: true);
    }

    private static InputTargetPreflightResult? ClassifyUsability(WindowDescriptor liveWindow)
    {
        if (string.Equals(liveWindow.WindowState, WindowStateValues.Minimized, StringComparison.Ordinal))
        {
            return new(
                IsAllowed: false,
                InputFailureCodeValues.TargetMinimized,
                "Окно-цель остаётся свернутым; runtime отклоняет dispatch без hidden restore.");
        }

        if (!liveWindow.IsForeground)
        {
            return new(
                IsAllowed: false,
                InputFailureCodeValues.TargetNotForeground,
                "Окно-цель больше не находится в foreground; windows.input не делает hidden focus recovery.");
        }

        return null;
    }

    private static bool CanBypassHigherIntegrity(InputProcessSecurityContext currentProcess) =>
        currentProcess.UiAccessResolved && currentProcess.HasUiAccess;

    private static int CompareIntegrity(InputIntegrityLevel left, InputIntegrityLevel right) =>
        left.CompareTo(right);

    private static InputTargetPreflightResult Fail(string failureCode, string reason) =>
        new(IsAllowed: false, failureCode, reason);
}
