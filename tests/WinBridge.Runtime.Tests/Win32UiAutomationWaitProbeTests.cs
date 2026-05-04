// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Runtime.InteropServices;
using System.Windows.Automation;
using WinBridge.Runtime.Windows.UIA;

namespace WinBridge.Runtime.Tests;

public sealed class Win32UiAutomationWaitProbeTests
{
    [Fact]
    public void IsTransientFocusedElementErrorReturnsTrueOnlyForElementNotAvailableCases()
    {
        Exception transientComException = Marshal.GetExceptionForHR(new ElementNotAvailableException().HResult)!;
        Exception nonTransientComException = Marshal.GetExceptionForHR(unchecked((int)0x80004005))!;

        Assert.True(Win32UiAutomationWaitProbe.IsTransientFocusedElementError(new ElementNotAvailableException()));
        Assert.IsType<COMException>(transientComException);
        Assert.True(Win32UiAutomationWaitProbe.IsTransientFocusedElementError(transientComException));
        Assert.False(Win32UiAutomationWaitProbe.IsTransientFocusedElementError(new InvalidOperationException("provider failure")));
        Assert.IsType<COMException>(nonTransientComException);
        Assert.False(Win32UiAutomationWaitProbe.IsTransientFocusedElementError(nonTransientComException));
    }
}
