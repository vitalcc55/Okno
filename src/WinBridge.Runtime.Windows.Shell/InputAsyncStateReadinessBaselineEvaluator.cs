// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Runtime.Windows.Shell;

internal static class InputAsyncStateReadinessBaselineEvaluator
{
    public static InputAsyncStateReadabilityProbeResult Probe(
        Func<InputAsyncStateReadabilityMode, InputAsyncStateReadabilityProbeResult> probe)
    {
        ArgumentNullException.ThrowIfNull(probe);
        return probe(InputAsyncStateReadabilityMode.CrossProcessForeground);
    }
}
