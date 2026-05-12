// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Input;
using Native = WinBridge.Runtime.Windows.Input.Win32InputNativeMethods;

namespace WinBridge.Runtime.Tests;

public sealed class Win32KeyboardInputSequenceBuilderTests
{
    [Fact]
    public void TryBuildKeypressInputsBuildsModifierComboWithRepeat()
    {
        bool built = Win32KeyboardInputSequenceBuilder.TryBuildKeypressInputs(
            "ctrl+shift+a",
            repeat: 2,
            out Native.INPUT[]? inputs,
            out string? failureCode,
            out string? reason);

        Assert.True(built);
        Assert.Null(failureCode);
        Assert.Null(reason);
        Assert.NotNull(inputs);
        Assert.Equal(8, inputs.Length);
        Assert.Equal(0x11, inputs[0].union.ki.wVk);
        Assert.Equal(0x10, inputs[1].union.ki.wVk);
        Assert.Equal('A', inputs[2].union.ki.wVk);
        Assert.Equal(0u, inputs[2].union.ki.dwFlags);
        Assert.Equal(Native.KeyeventfKeyup, inputs[3].union.ki.dwFlags);
        Assert.Equal(0x10, inputs[6].union.ki.wVk);
        Assert.Equal(Native.KeyeventfKeyup, inputs[6].union.ki.dwFlags);
        Assert.Equal(0x11, inputs[7].union.ki.wVk);
        Assert.Equal(Native.KeyeventfKeyup, inputs[7].union.ki.dwFlags);
    }

    [Fact]
    public void TryBuildKeypressInputsNormalizesNamedExtendedKeys()
    {
        bool built = Win32KeyboardInputSequenceBuilder.TryBuildKeypressInputs(
            "pageup",
            repeat: 1,
            out Native.INPUT[]? inputs,
            out string? failureCode,
            out string? reason);

        Assert.True(built);
        Assert.Null(failureCode);
        Assert.Null(reason);
        Assert.NotNull(inputs);
        Assert.Equal((ushort)0x21, inputs[0].union.ki.wVk);
        Assert.Equal(Native.KeyeventfExtendedkey, inputs[0].union.ki.dwFlags);
        Assert.Equal(Native.KeyeventfExtendedkey | Native.KeyeventfKeyup, inputs[1].union.ki.dwFlags);
    }

    [Fact]
    public void TryBuildKeypressInputsRejectsUnsupportedModifierCombo()
    {
        bool built = Win32KeyboardInputSequenceBuilder.TryBuildKeypressInputs(
            "meta+a",
            repeat: 1,
            out _,
            out string? failureCode,
            out string? reason);

        Assert.False(built);
        Assert.Equal(InputFailureCodeValues.UnsupportedKey, failureCode);
        Assert.Contains("modifier combo", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildKeypressInputsRejectsRepeatOutsideSupportedRange()
    {
        bool built = Win32KeyboardInputSequenceBuilder.TryBuildKeypressInputs(
            "a",
            InputActionScalarConstraints.MaximumKeypressRepeat + 1,
            out _,
            out string? failureCode,
            out string? reason);

        Assert.False(built);
        Assert.Equal(InputFailureCodeValues.InvalidRequest, failureCode);
        Assert.Contains("repeat", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateKeypressCompensationInputsReleasesPressedKeysInReverseOrder()
    {
        bool built = Win32KeyboardInputSequenceBuilder.TryBuildKeypressInputs(
            "ctrl+a",
            repeat: 1,
            out Native.INPUT[]? attemptedInputs,
            out _,
            out _);

        Assert.True(built);
        Native.INPUT[] compensationInputs = Win32KeyboardInputSequenceBuilder.CreateKeypressCompensationInputs(
            attemptedInputs!,
            sentInputs: 2);

        Assert.Equal(2, compensationInputs.Length);
        Assert.Equal('A', compensationInputs[0].union.ki.wVk);
        Assert.Equal(Native.KeyeventfKeyup, compensationInputs[0].union.ki.dwFlags);
        Assert.Equal(0x11, compensationInputs[1].union.ki.wVk);
        Assert.Equal(Native.KeyeventfKeyup, compensationInputs[1].union.ki.dwFlags);
    }

    [Fact]
    public void TryBuildTextInputsRejectsEmptyString()
    {
        bool built = Win32KeyboardInputSequenceBuilder.TryBuildTextInputs(
            string.Empty,
            out _,
            out string? failureCode,
            out string? reason);

        Assert.False(built);
        Assert.Equal(InputFailureCodeValues.InvalidRequest, failureCode);
        Assert.Contains("empty string", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildTextInputsBuildsUnicodeDownAndUpPairs()
    {
        bool built = Win32KeyboardInputSequenceBuilder.TryBuildTextInputs(
            "A",
            out Native.INPUT[]? inputs,
            out string? failureCode,
            out string? reason);

        Assert.True(built);
        Assert.Null(failureCode);
        Assert.Null(reason);
        Assert.NotNull(inputs);
        Assert.Equal(2, inputs.Length);
        Assert.Equal(0u, inputs[0].union.ki.wVk);
        Assert.Equal('A', inputs[0].union.ki.wScan);
        Assert.Equal(Native.KeyeventfUnicode, inputs[0].union.ki.dwFlags);
        Assert.Equal(Native.KeyeventfUnicode | Native.KeyeventfKeyup, inputs[1].union.ki.dwFlags);
    }
}
