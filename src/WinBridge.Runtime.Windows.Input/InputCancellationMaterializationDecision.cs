// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Runtime.Windows.Input;

internal sealed record InputCancellationMaterializationDecision(
    bool ShouldAppendFailedAction,
    int? FailedActionIndex,
    string FailureCode,
    string Reason);
