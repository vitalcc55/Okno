// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Runtime.Contracts;

public sealed record DeferredToolResult(
    string ToolName,
    string Status,
    string Reason,
    string PlannedPhase,
    string SuggestedAlternative);
