// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Input;

namespace WinBridge.Runtime.Tests;

public sealed class InputActionSemanticsTests
{
    [Fact]
    public void ResolveEffectiveButtonDefaultsToLeftButton()
    {
        string button = InputActionSemantics.ResolveEffectiveButton(
            new InputAction
            {
                Type = InputActionTypeValues.Click,
                CoordinateSpace = InputCoordinateSpaceValues.Screen,
                Point = new InputPoint(140, 260),
            });

        Assert.Equal(InputButtonValues.Left, button);
    }

    [Fact]
    public void ResolveEffectiveButtonForActionUsesLeftButtonForDoubleClick()
    {
        string? button = InputActionSemantics.ResolveEffectiveButtonForAction(
            new InputAction
            {
                Type = InputActionTypeValues.DoubleClick,
                CoordinateSpace = InputCoordinateSpaceValues.Screen,
                Point = new InputPoint(140, 260),
            });

        Assert.Equal(InputButtonValues.Left, button);
    }

    [Fact]
    public void ResolveActionKeysPrefersExplicitKeysCollection()
    {
        IReadOnlyList<string>? keys = InputActionSemantics.ResolveActionKeys(
            new InputAction
            {
                Type = InputActionTypeValues.Keypress,
                Key = "ctrl+s",
                Keys = ["ctrl+s", "ctrl+shift+s"],
            });

        Assert.Equal(["ctrl+s", "ctrl+shift+s"], keys);
    }

    [Fact]
    public void ResolveActionKeysFallsBackToSingleKeyLiteral()
    {
        IReadOnlyList<string>? keys = InputActionSemantics.ResolveActionKeys(
            new InputAction
            {
                Type = InputActionTypeValues.Keypress,
                Key = "ctrl+s",
            });

        Assert.Equal(["ctrl+s"], keys);
    }

    [Fact]
    public void RequiresResolvedPointIsTrueForPointerActionsAndFalseForType()
    {
        bool pointerRequiresPoint = InputActionSemantics.RequiresResolvedPoint(
            new InputAction
            {
                Type = InputActionTypeValues.Click,
            });
        bool textRequiresPoint = InputActionSemantics.RequiresResolvedPoint(
            new InputAction
            {
                Type = InputActionTypeValues.Type,
                Text = "typed text",
            });

        Assert.True(pointerRequiresPoint);
        Assert.False(textRequiresPoint);
    }

    [Fact]
    public void SamePointComparesCoordinatesOnly()
    {
        Assert.True(InputActionSemantics.SamePoint(new InputPoint(10, 20), new InputPoint(10, 20)));
        Assert.False(InputActionSemantics.SamePoint(new InputPoint(10, 20), new InputPoint(11, 20)));
    }
}
