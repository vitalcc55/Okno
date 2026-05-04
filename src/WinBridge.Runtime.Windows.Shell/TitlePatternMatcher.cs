// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.RegularExpressions;

namespace WinBridge.Runtime.Windows.Shell;

internal static class TitlePatternMatcher
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMilliseconds(250);

    public static bool IsMatch(string title, string pattern) =>
        IsMatch(title, pattern, DefaultTimeout);

    internal static bool IsMatch(string title, string pattern, TimeSpan timeout)
    {
        Regex regex = new(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, timeout);
        return regex.IsMatch(title);
    }
}
