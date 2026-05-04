// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Globalization;

namespace WinBridge.Runtime.Diagnostics;

public sealed record AuditLogOptions(
    string ContentRootPath,
    string EnvironmentName,
    string RunId,
    string DiagnosticsRoot,
    string RunDirectory,
    string EventsPath,
    string SummaryPath)
{
    public static AuditLogOptions Create(string contentRootPath, string environmentName)
    {
        string timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfff", CultureInfo.InvariantCulture);
        string runId = $"{timestamp}-{Guid.NewGuid():N}"[..24];
        string diagnosticsRoot = Path.Combine(contentRootPath, "artifacts", "diagnostics");
        string runDirectory = Path.Combine(diagnosticsRoot, runId);

        return new AuditLogOptions(
            ContentRootPath: contentRootPath,
            EnvironmentName: environmentName,
            RunId: runId,
            DiagnosticsRoot: diagnosticsRoot,
            RunDirectory: runDirectory,
            EventsPath: Path.Combine(runDirectory, "events.jsonl"),
            SummaryPath: Path.Combine(runDirectory, "summary.md"));
    }
}
