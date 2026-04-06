using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;

namespace WinBridge.Runtime.Windows.Launch;

internal sealed class LaunchArtifactWriter(AuditLogOptions auditLogOptions)
{
    private static readonly Encoding FileEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    public string Write(
        LaunchProcessResult result,
        DateTimeOffset capturedAtUtc,
        LaunchFailureDiagnostics? failureDiagnostics = null,
        LaunchProcessPreview? requestPreview = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        string? tempPath = null;

        try
        {
            string directory = Path.Combine(auditLogOptions.RunDirectory, "launch");
            Directory.CreateDirectory(directory);

            string path = Path.Combine(directory, LaunchArtifactNameBuilder.Create(capturedAtUtc.UtcDateTime));
            tempPath = Path.Combine(directory, Path.GetRandomFileName() + ".tmp");
            LaunchArtifactPayload payload = new(
                Result: result with { ArtifactPath = path },
                CapturedAtUtc: capturedAtUtc,
                RequestPreview: requestPreview,
                FailureDiagnostics: failureDiagnostics);
            string document = JsonSerializer.Serialize(payload, JsonOptions);
            File.WriteAllText(tempPath, document, FileEncoding);
            File.Move(tempPath, path);
            return path;
        }
        catch (Exception exception) when (IsArtifactWriteFailure(exception))
        {
            TryDeleteTempArtifactFile(tempPath);
            throw new LaunchArtifactException("Runtime не смог записать launch artifact на диск.", exception);
        }
    }

    private static bool IsArtifactWriteFailure(Exception exception) =>
        exception is UnauthorizedAccessException or IOException or NotSupportedException;

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

internal sealed record LaunchArtifactPayload(
    LaunchProcessResult Result,
    DateTimeOffset CapturedAtUtc,
    LaunchProcessPreview? RequestPreview = null,
    LaunchFailureDiagnostics? FailureDiagnostics = null);

internal sealed record LaunchFailureDiagnostics(
    string? FailureStage = null,
    string? ExceptionType = null,
    bool ExceptionMessageSuppressed = false);

internal sealed class LaunchArtifactException(string message, Exception innerException)
    : Exception(message, innerException);
