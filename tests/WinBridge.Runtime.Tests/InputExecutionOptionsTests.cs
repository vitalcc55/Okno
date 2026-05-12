// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Windows.Input;

namespace WinBridge.Runtime.Tests;

public sealed class InputExecutionOptionsTests
{
    [Fact]
    public void ConstructorRejectsNegativeDoubleClickDelay()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InputExecutionOptions(TimeSpan.FromMilliseconds(-1)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(250)]
    public void ConstructorAcceptsZeroAndPositiveDoubleClickDelay(int milliseconds)
    {
        InputExecutionOptions options = new(TimeSpan.FromMilliseconds(milliseconds));

        Assert.Equal(TimeSpan.FromMilliseconds(milliseconds), options.DoubleClickDelay);
    }

    [Fact]
    public void DefaultUsesFiftyMillisecondDoubleClickDelay()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(50), InputExecutionOptions.Default.DoubleClickDelay);
    }
}
