// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Input;

internal enum InputAmbientInputProofStatus
{
    Neutral,
    NonNeutral,
    Unknown,
}

internal sealed record InputAmbientInputProbeContext(
    bool CanReadAsyncState,
    bool MouseButtonsSwapped,
    string? UnknownReason);

internal sealed record InputAmbientInputProbeResult(
    InputAmbientInputProofStatus Status,
    string? FailureCode = null,
    string? Reason = null);

internal static class InputAmbientInputPolicy
{
    private const short KeyPressedMask = unchecked((short)0x8000);
    private const int VkShift = 0x10;
    private const int VkLShift = 0xA0;
    private const int VkRShift = 0xA1;
    private const int VkControl = 0x11;
    private const int VkLControl = 0xA2;
    private const int VkRControl = 0xA3;
    private const int VkMenu = 0x12;
    private const int VkLMenu = 0xA4;
    private const int VkRMenu = 0xA5;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;

    public static InputAmbientInputProbeResult Probe(
        InputAmbientInputProbeContext context,
        Func<int, short> getAsyncKeyState,
        string failureCode = InputFailureCodeValues.InputDispatchFailed)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(getAsyncKeyState);

        if (!context.CanReadAsyncState)
        {
            return new(
                InputAmbientInputProofStatus.Unknown,
                FailureCode: failureCode,
                Reason: context.UnknownReason ?? "Runtime не смог доказать neutral ambient input state перед click dispatch.");
        }

        List<string> activeInputs = [];
        if (IsAnyPressed(getAsyncKeyState, VkControl, VkLControl, VkRControl))
        {
            activeInputs.Add(InputModifierKeyValues.Ctrl);
        }

        if (IsAnyPressed(getAsyncKeyState, VkShift, VkLShift, VkRShift))
        {
            activeInputs.Add(InputModifierKeyValues.Shift);
        }

        if (IsAnyPressed(getAsyncKeyState, VkMenu, VkLMenu, VkRMenu))
        {
            activeInputs.Add(InputModifierKeyValues.Alt);
        }

        if (IsAnyPressed(getAsyncKeyState, VkLWin, VkRWin))
        {
            activeInputs.Add(InputModifierKeyValues.Win);
        }

        foreach (string activeButton in InputMouseButtonSemantics.GetActiveLogicalButtons(getAsyncKeyState, context.MouseButtonsSwapped))
        {
            activeInputs.Add(activeButton);
        }

        if (activeInputs.Count > 0)
        {
            return new(
                InputAmbientInputProofStatus.NonNeutral,
                FailureCode: failureCode,
                Reason: $"Runtime заблокировал click dispatch: ambient input state не нейтрален ({string.Join(", ", activeInputs)}).");
        }

        return new(InputAmbientInputProofStatus.Neutral);
    }

    private static bool IsAnyPressed(Func<int, short> getAsyncKeyState, params int[] virtualKeys)
    {
        foreach (int virtualKey in virtualKeys)
        {
            if (IsPressed(getAsyncKeyState, virtualKey))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPressed(Func<int, short> getAsyncKeyState, int virtualKey) =>
        (getAsyncKeyState(virtualKey) & KeyPressedMask) != 0;
}
