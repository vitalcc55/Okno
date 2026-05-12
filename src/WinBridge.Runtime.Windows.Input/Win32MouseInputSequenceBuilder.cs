// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;
using Native = WinBridge.Runtime.Windows.Input.Win32InputNativeMethods;

namespace WinBridge.Runtime.Windows.Input;

internal static class Win32MouseInputSequenceBuilder
{
    public static Native.INPUT CreateMouseInput(uint flags) =>
        new()
        {
            type = Native.InputMouse,
            union = new Native.INPUTUNION
            {
                mi = new Native.MOUSEINPUT
                {
                    dx = 0,
                    dy = 0,
                    mouseData = 0,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero,
                },
            },
        };

    public static bool TryCreateScrollInput(
        string direction,
        int delta,
        out Native.INPUT input,
        out string? failureCode,
        out string? reason)
    {
        if (delta == 0)
        {
            input = default;
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = "Runtime не поддерживает zero delta для scroll dispatch.";
            return false;
        }

        uint flags = direction switch
        {
            "up" or "down" => Native.MouseeventfWheel,
            "left" or "right" => Native.MouseeventfHwheel,
            _ => 0u,
        };
        if (flags == 0u)
        {
            input = default;
            failureCode = InputFailureCodeValues.UnsupportedActionType;
            reason = $"Runtime не поддерживает direction '{direction}' для scroll dispatch.";
            return false;
        }

        input = new Native.INPUT
        {
            type = Native.InputMouse,
            union = new Native.INPUTUNION
            {
                mi = new Native.MOUSEINPUT
                {
                    dx = 0,
                    dy = 0,
                    mouseData = unchecked((uint)delta),
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero,
                },
            },
        };
        failureCode = null;
        reason = null;
        return true;
    }
}
