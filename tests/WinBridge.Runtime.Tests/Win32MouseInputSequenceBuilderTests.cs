// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Input;
using Native = WinBridge.Runtime.Windows.Input.Win32InputNativeMethods;

namespace WinBridge.Runtime.Tests;

public sealed class Win32MouseInputSequenceBuilderTests
{
    [Fact]
    public void CreateMouseInputUsesMouseTypeAndRequestedFlags()
    {
        Native.INPUT input = Win32MouseInputSequenceBuilder.CreateMouseInput(InputMouseButtonSemantics.MouseEventfLeftDown);

        Assert.Equal(Native.InputMouse, input.type);
        Assert.Equal(InputMouseButtonSemantics.MouseEventfLeftDown, input.union.mi.dwFlags);
    }

    [Fact]
    public void TryCreateScrollInputBuildsVerticalWheelInput()
    {
        bool built = Win32MouseInputSequenceBuilder.TryCreateScrollInput(
            "down",
            120,
            out Native.INPUT input,
            out string? failureCode,
            out string? reason);

        Assert.True(built);
        Assert.Null(failureCode);
        Assert.Null(reason);
        Assert.Equal(Native.InputMouse, input.type);
        Assert.Equal(Native.MouseeventfWheel, input.union.mi.dwFlags);
        Assert.Equal(120u, input.union.mi.mouseData);
    }

    [Fact]
    public void TryCreateScrollInputBuildsHorizontalWheelInput()
    {
        bool built = Win32MouseInputSequenceBuilder.TryCreateScrollInput(
            "left",
            -120,
            out Native.INPUT input,
            out _,
            out _);

        Assert.True(built);
        Assert.Equal(Native.MouseeventfHwheel, input.union.mi.dwFlags);
        Assert.Equal(unchecked((uint)-120), input.union.mi.mouseData);
    }

    [Fact]
    public void TryCreateScrollInputRejectsZeroDelta()
    {
        bool built = Win32MouseInputSequenceBuilder.TryCreateScrollInput(
            "up",
            0,
            out _,
            out string? failureCode,
            out string? reason);

        Assert.False(built);
        Assert.Equal(InputFailureCodeValues.InvalidRequest, failureCode);
        Assert.Contains("zero delta", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryCreateScrollInputRejectsUnsupportedDirection()
    {
        bool built = Win32MouseInputSequenceBuilder.TryCreateScrollInput(
            "diagonal",
            120,
            out _,
            out string? failureCode,
            out string? reason);

        Assert.False(built);
        Assert.Equal(InputFailureCodeValues.UnsupportedActionType, failureCode);
        Assert.Contains("direction", reason, StringComparison.OrdinalIgnoreCase);
    }
}
