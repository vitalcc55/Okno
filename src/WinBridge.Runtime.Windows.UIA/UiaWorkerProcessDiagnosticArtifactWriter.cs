// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WinBridge.Runtime.Diagnostics;

namespace WinBridge.Runtime.Windows.UIA;

internal sealed class UiaWorkerProcessDiagnosticArtifactWriter(AuditLogOptions auditLogOptions)
{
    private static readonly Encoding FileEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    public string? TryWrite(UiaWorkerProcessDiagnosticArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        try
        {
            string directory = Path.Combine(auditLogOptions.RunDirectory, "uia");
            Directory.CreateDirectory(directory);

            string handle = artifact.WindowHwnd?.ToString(CultureInfo.InvariantCulture) ?? "unknown";
            string fileName = UiaSnapshotArtifactNameBuilder.Create("worker", handle, artifact.CapturedAtUtc.UtcDateTime);
            string path = Path.Combine(directory, fileName);
            string payload = JsonSerializer.Serialize(artifact with { ArtifactPath = path }, JsonOptions);
            File.WriteAllText(path, payload, FileEncoding);
            return path;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}

internal sealed record UiaWorkerProcessDiagnosticArtifact(
    string Kind,
    string FailureStage,
    string? ArtifactPath,
    DateTimeOffset CapturedAtUtc,
    long? WindowHwnd,
    int? ExitCode,
    string? Stdout,
    string? Stderr,
    string? ExceptionType,
    string? ExceptionMessage);
