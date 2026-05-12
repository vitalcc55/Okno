// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;
using Native = WinBridge.Runtime.Windows.Input.Win32InputNativeMethods;

namespace WinBridge.Runtime.Windows.Input;

internal static class Win32KeyboardInputSequenceBuilder
{
    private const ushort VkShift = 0x10;
    private const ushort VkControl = 0x11;
    private const ushort VkMenu = 0x12;
    private const ushort VkLwin = 0x5B;

    private static readonly Dictionary<string, ushort> NamedKeyVirtualKeys =
        new(StringComparer.Ordinal)
        {
            ["tab"] = 0x09,
            ["enter"] = 0x0D,
            ["escape"] = 0x1B,
            ["delete"] = 0x2E,
            ["backspace"] = 0x08,
            ["space"] = 0x20,
            ["up"] = 0x26,
            ["down"] = 0x28,
            ["left"] = 0x25,
            ["right"] = 0x27,
            ["home"] = 0x24,
            ["end"] = 0x23,
            ["page_up"] = 0x21,
            ["page_down"] = 0x22,
            ["insert"] = 0x2D,
            ["f1"] = 0x70,
            ["f2"] = 0x71,
            ["f3"] = 0x72,
            ["f4"] = 0x73,
            ["f5"] = 0x74,
            ["f6"] = 0x75,
            ["f7"] = 0x76,
            ["f8"] = 0x77,
            ["f9"] = 0x78,
            ["f10"] = 0x79,
            ["f11"] = 0x7A,
            ["f12"] = 0x7B,
        };

    private static readonly HashSet<ushort> ExtendedVirtualKeys =
        [0x25, 0x26, 0x27, 0x28, 0x21, 0x22, 0x23, 0x24, 0x2D, 0x2E, VkLwin];

    public static bool TryBuildKeypressInputs(
        string keyLiteral,
        int repeat,
        out Native.INPUT[]? inputs,
        out string? failureCode,
        out string? reason)
    {
        ArgumentNullException.ThrowIfNull(keyLiteral);

        inputs = null;
        failureCode = null;
        reason = null;

        if (repeat < InputActionScalarConstraints.MinimumRepeat
            || repeat > InputActionScalarConstraints.MaximumKeypressRepeat)
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Runtime не поддерживает repeat вне диапазона {InputActionScalarConstraints.MinimumRepeat}..{InputActionScalarConstraints.MaximumKeypressRepeat} для keypress dispatch.";
            return false;
        }

        string[] rawTokens = keyLiteral
            .Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeKeypressToken)
            .ToArray();
        if (rawTokens.Length == 0)
        {
            failureCode = InputFailureCodeValues.UnsupportedKey;
            reason = "Runtime не смог разобрать key literal для keypress dispatch.";
            return false;
        }

        string baseToken = rawTokens[^1];
        string[] modifiers = rawTokens[..^1];
        if (modifiers.Any(token => ResolveModifierVirtualKey(token) is null))
        {
            failureCode = InputFailureCodeValues.UnsupportedKey;
            reason = $"Runtime не поддерживает modifier combo '{keyLiteral}' для keypress dispatch.";
            return false;
        }

        if (!TryResolveBaseVirtualKey(baseToken, out ushort baseVirtualKey, out bool baseIsExtended, out failureCode, out reason))
        {
            return false;
        }

        int totalInputCount = checked(modifiers.Length + (repeat * 2) + modifiers.Length);
        Native.INPUT[] sequence = new Native.INPUT[totalInputCount];
        int inputIndex = 0;

        foreach (string modifier in modifiers)
        {
            sequence[inputIndex++] = CreateKeyInput(ResolveModifierVirtualKey(modifier)!.Value, keyUp: false, isExtended: modifier == "win");
        }

        for (int index = 0; index < repeat; index++)
        {
            sequence[inputIndex++] = CreateKeyInput(baseVirtualKey, keyUp: false, isExtended: baseIsExtended);
            sequence[inputIndex++] = CreateKeyInput(baseVirtualKey, keyUp: true, isExtended: baseIsExtended);
        }

        for (int index = modifiers.Length - 1; index >= 0; index--)
        {
            string modifier = modifiers[index];
            sequence[inputIndex++] = CreateKeyInput(ResolveModifierVirtualKey(modifier)!.Value, keyUp: true, isExtended: modifier == "win");
        }

        inputs = sequence;
        return true;
    }

    public static bool TryBuildTextInputs(
        string text,
        out Native.INPUT[]? inputs,
        out string? failureCode,
        out string? reason)
    {
        ArgumentNullException.ThrowIfNull(text);

        inputs = null;
        failureCode = null;
        reason = null;

        if (text.Length == 0)
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = "Runtime не поддерживает empty string для text dispatch.";
            return false;
        }

        Native.INPUT[] sequence = new Native.INPUT[checked(text.Length * 2)];
        int inputIndex = 0;
        foreach (char codeUnit in text)
        {
            sequence[inputIndex++] = CreateUnicodeKeyInput(codeUnit, keyUp: false);
            sequence[inputIndex++] = CreateUnicodeKeyInput(codeUnit, keyUp: true);
        }

        inputs = sequence;
        return true;
    }

    public static Native.INPUT[] CreateKeypressCompensationInputs(Native.INPUT[] attemptedInputs, uint sentInputs)
    {
        ArgumentNullException.ThrowIfNull(attemptedInputs);

        List<KeypressCompensationKey> pressedKeys = [];
        int observedCount = Math.Min(attemptedInputs.Length, checked((int)sentInputs));
        for (int index = 0; index < observedCount; index++)
        {
            Native.INPUT input = attemptedInputs[index];
            if (input.type != Native.InputKeyboard)
            {
                continue;
            }

            Native.KEYBDINPUT keyInput = input.union.ki;
            if ((keyInput.dwFlags & Native.KeyeventfUnicode) != 0u)
            {
                continue;
            }

            KeypressCompensationKey key = new(
                keyInput.wVk,
                (keyInput.dwFlags & Native.KeyeventfExtendedkey) != 0u);
            bool keyUp = (keyInput.dwFlags & Native.KeyeventfKeyup) != 0u;
            if (!keyUp)
            {
                pressedKeys.Add(key);
                continue;
            }

            for (int pressedIndex = pressedKeys.Count - 1; pressedIndex >= 0; pressedIndex--)
            {
                if (pressedKeys[pressedIndex].Equals(key))
                {
                    pressedKeys.RemoveAt(pressedIndex);
                    break;
                }
            }
        }

        if (pressedKeys.Count == 0)
        {
            return [];
        }

        Native.INPUT[] compensationInputs = new Native.INPUT[pressedKeys.Count];
        for (int index = 0; index < pressedKeys.Count; index++)
        {
            KeypressCompensationKey key = pressedKeys[^(index + 1)];
            compensationInputs[index] = CreateKeyInput(key.VirtualKey, keyUp: true, key.IsExtended);
        }

        return compensationInputs;
    }

    private static bool TryResolveBaseVirtualKey(
        string baseToken,
        out ushort virtualKey,
        out bool isExtended,
        out string? failureCode,
        out string? reason)
    {
        if (NamedKeyVirtualKeys.TryGetValue(baseToken, out virtualKey))
        {
            isExtended = ExtendedVirtualKeys.Contains(virtualKey);
            failureCode = null;
            reason = null;
            return true;
        }

        if (baseToken.Length == 1 && char.IsLetterOrDigit(baseToken[0]))
        {
            char normalizedBaseKey = char.ToUpperInvariant(baseToken[0]);
            if (char.IsAsciiLetter(normalizedBaseKey) || char.IsAsciiDigit(normalizedBaseKey))
            {
                virtualKey = normalizedBaseKey;
                isExtended = false;
                failureCode = null;
                reason = null;
                return true;
            }
        }

        isExtended = false;
        virtualKey = 0;
        failureCode = InputFailureCodeValues.UnsupportedKey;
        reason = $"Runtime не поддерживает key literal '{baseToken}' для keypress dispatch.";
        return false;
    }

    private static ushort? ResolveModifierVirtualKey(string token) =>
        token switch
        {
            "ctrl" => VkControl,
            "alt" => VkMenu,
            "shift" => VkShift,
            "win" => VkLwin,
            _ => null,
        };

    private static string NormalizeKeypressToken(string token)
    {
        string normalized = token.Trim().ToLowerInvariant();
        return normalized switch
        {
            "control" => "ctrl",
            "esc" => "escape",
            "return" => "enter",
            "arrow_up" => "up",
            "arrow_down" => "down",
            "arrow_left" => "left",
            "arrow_right" => "right",
            "pageup" => "page_up",
            "pagedown" => "page_down",
            _ => normalized,
        };
    }

    private static Native.INPUT CreateKeyInput(ushort virtualKey, bool keyUp, bool isExtended)
    {
        uint flags = keyUp ? Native.KeyeventfKeyup : 0u;
        if (isExtended)
        {
            flags |= Native.KeyeventfExtendedkey;
        }

        return new Native.INPUT
        {
            type = Native.InputKeyboard,
            union = new Native.INPUTUNION
            {
                ki = new Native.KEYBDINPUT
                {
                    wVk = virtualKey,
                    wScan = 0,
                    dwFlags = flags,
                    dwExtraInfo = IntPtr.Zero,
                    time = 0,
                },
            },
        };
    }

    private static Native.INPUT CreateUnicodeKeyInput(char codeUnit, bool keyUp)
    {
        uint flags = Native.KeyeventfUnicode;
        if (keyUp)
        {
            flags |= Native.KeyeventfKeyup;
        }

        return new Native.INPUT
        {
            type = Native.InputKeyboard,
            union = new Native.INPUTUNION
            {
                ki = new Native.KEYBDINPUT
                {
                    wVk = 0,
                    wScan = codeUnit,
                    dwFlags = flags,
                    dwExtraInfo = IntPtr.Zero,
                    time = 0,
                },
            },
        };
    }

    private readonly record struct KeypressCompensationKey(ushort VirtualKey, bool IsExtended);
}
