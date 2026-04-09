using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;

namespace WinBridge.Runtime.Windows.Launch;

internal sealed class OpenTargetArtifactWriter(AuditLogOptions auditLogOptions)
{
    private static readonly Encoding FileEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    public string Write(
        OpenTargetResult result,
        DateTimeOffset capturedAtUtc,
        OpenTargetFailureDiagnostics? failureDiagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        string? tempPath = null;

        try
        {
            string directory = Path.Combine(auditLogOptions.RunDirectory, "launch");
            Directory.CreateDirectory(directory);

            string path = Path.Combine(directory, LaunchArtifactNameBuilder.CreateOpenTarget(capturedAtUtc.UtcDateTime));
            tempPath = Path.Combine(directory, Path.GetRandomFileName() + ".tmp");
            OpenTargetArtifactPayload payload = new(
                Result: result with { ArtifactPath = path },
                CapturedAtUtc: capturedAtUtc,
                FailureDiagnostics: failureDiagnostics);
            string document = JsonSerializer.Serialize(payload, JsonOptions);
            File.WriteAllText(tempPath, document, FileEncoding);
            File.Move(tempPath, path);
            return path;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or NotSupportedException)
        {
            TryDeleteTempArtifactFile(tempPath);
            throw new OpenTargetArtifactException("Runtime не смог записать open_target artifact на диск.", exception);
        }
    }

    private static void TryDeleteTempArtifactFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
        }
    }
}

internal sealed record OpenTargetArtifactPayload(
    OpenTargetResult Result,
    DateTimeOffset CapturedAtUtc,
    OpenTargetFailureDiagnostics? FailureDiagnostics = null);

internal sealed record OpenTargetFailureDiagnostics(
    string? FailureStage = null,
    string? ExceptionType = null,
    bool ExceptionMessageSuppressed = false);

internal sealed class OpenTargetArtifactException(string message, Exception innerException)
    : Exception(message, innerException);
