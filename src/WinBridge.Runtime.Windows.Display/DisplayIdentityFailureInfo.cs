// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Runtime.Windows.Display;

internal sealed record DisplayIdentityFailureInfo(
    string FailedStage,
    int ErrorCode,
    string ErrorName,
    string MessageHuman);

internal static class DisplayIdentityFailureAggregator
{
    public static DisplayIdentityFailureInfo? SelectMoreSignificant(
        DisplayIdentityFailureInfo? current,
        DisplayIdentityFailureInfo? candidate)
    {
        if (candidate is null)
        {
            return current;
        }

        if (current is null)
        {
            return candidate;
        }

        return DisplayIdentityFailureSemantics.GetPriority(candidate.FailedStage)
            > DisplayIdentityFailureSemantics.GetPriority(current.FailedStage)
            ? candidate
            : current;
    }
}
