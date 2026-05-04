// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Runtime.Contracts;

public static class InputTargetFailureValues
{
    public const string MissingTarget = InputFailureCodeValues.MissingTarget;
    public const string StaleExplicitTarget = InputFailureCodeValues.StaleExplicitTarget;
    public const string StaleAttachedTarget = InputFailureCodeValues.StaleAttachedTarget;
}
