// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.RegularExpressions;
using WinBridge.Runtime.Windows.Shell;

namespace WinBridge.Runtime.Tests;

public sealed class TitlePatternMatcherTests
{
    [Fact]
    public void IsMatchUsesBoundedRegexTimeout()
    {
        string title = new string('a', 4000) + "!";

        Assert.Throws<RegexMatchTimeoutException>(
            () => TitlePatternMatcher.IsMatch(title, "(a+)+$", TimeSpan.FromMilliseconds(1)));
    }
}
