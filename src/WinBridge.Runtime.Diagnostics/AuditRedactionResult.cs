// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Runtime.Diagnostics;

public sealed record AuditRedactionResult(
    string? Summary,
    IReadOnlyDictionary<string, string?> SanitizedData,
    IReadOnlyList<string> RedactedFields,
    bool RedactionApplied,
    bool SummarySuppressed)
{
    public static AuditRedactionResult None(string? summary = null) =>
        new(
            Summary: summary,
            SanitizedData: new Dictionary<string, string?>(StringComparer.Ordinal),
            RedactedFields: Array.Empty<string>(),
            RedactionApplied: false,
            SummarySuppressed: false);
}
