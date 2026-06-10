// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Runtime.Contracts;

public static class ElementSelectorPolicy
{
    public const int AmbiguousMatchThreshold = 2;

    public static bool HasCriteria(WaitElementSelector? selector) =>
        selector is not null
        && (!string.IsNullOrWhiteSpace(selector.Name)
            || !string.IsNullOrWhiteSpace(selector.AutomationId)
            || !string.IsNullOrWhiteSpace(selector.ControlType));

    public static bool Matches(
        WaitElementSelector selector,
        string? name,
        string? automationId,
        string? controlType)
    {
        ArgumentNullException.ThrowIfNull(selector);

        if (!string.IsNullOrWhiteSpace(selector.Name)
            && !string.Equals(name, selector.Name, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(selector.AutomationId)
            && !string.Equals(automationId, selector.AutomationId, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(selector.ControlType)
            && !string.Equals(controlType, selector.ControlType, StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    public static bool IsAmbiguous(int matchCount) =>
        matchCount >= AmbiguousMatchThreshold;

    public static string ClassifyMatchCount(int matchCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(matchCount);

        return matchCount switch
        {
            0 => ElementSelectorMatchCardinalityValues.None,
            1 => ElementSelectorMatchCardinalityValues.Unique,
            _ => ElementSelectorMatchCardinalityValues.Ambiguous,
        };
    }
}
