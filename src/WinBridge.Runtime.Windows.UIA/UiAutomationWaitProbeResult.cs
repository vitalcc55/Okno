// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.UIA;

public sealed record UiAutomationWaitProbeResult
{
    public ObservedWindowDescriptor? Window { get; init; }

    public IReadOnlyList<UiaElementSnapshot> Matches { get; init; } = [];

    public string? MatchedText { get; init; }

    public string? MatchedTextSource { get; init; }

    public string? FailureStage { get; init; }

    public string? DiagnosticArtifactPath { get; init; }

    public string? Reason { get; init; }
}
