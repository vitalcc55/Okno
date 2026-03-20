using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;

namespace WinBridge.Runtime.Waiting;

internal sealed class WaitArtifactWriter(AuditLogOptions auditLogOptions)
{
    private static readonly Encoding FileEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    public string Write(
        WaitRequest request,
        WaitTargetResolution target,
        WaitOptions options,
        IReadOnlyList<WaitAttemptSummary> attempts,
        WaitResult result,
        DateTimeOffset capturedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(attempts);
        ArgumentNullException.ThrowIfNull(result);

        try
        {
            string directory = Path.Combine(auditLogOptions.RunDirectory, "wait");
            Directory.CreateDirectory(directory);

            string handle = result.Window is not null
                ? result.Window.Hwnd.ToString(CultureInfo.InvariantCulture)
                : target.Window is not null
                    ? target.Window.Hwnd.ToString(CultureInfo.InvariantCulture)
                    : "unknown";
            string fileName = WaitArtifactNameBuilder.Create(result.Condition, handle, capturedAtUtc.UtcDateTime);
            string path = Path.Combine(directory, fileName);

            WaitArtifactPayload payload = new(
                Request: request,
                ResolvedTarget: target,
                PollSettings: new WaitPollSettings(result.TimeoutMs, (int)Math.Round(options.PollInterval.TotalMilliseconds, MidpointRounding.AwayFromZero)),
                Attempts: attempts,
                Result: result with { ArtifactPath = path },
                CapturedAtUtc: capturedAtUtc);
            string document = JsonSerializer.Serialize(payload, JsonOptions);
            File.WriteAllText(path, document, FileEncoding);
            return path;
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new WaitArtifactException("Runtime не смог записать wait artifact на диск.", exception);
        }
        catch (IOException exception)
        {
            throw new WaitArtifactException("Runtime не смог записать wait artifact на диск.", exception);
        }
    }
}

internal sealed record WaitArtifactPayload(
    WaitRequest Request,
    WaitTargetResolution ResolvedTarget,
    WaitPollSettings PollSettings,
    IReadOnlyList<WaitAttemptSummary> Attempts,
    WaitResult Result,
    DateTimeOffset CapturedAtUtc);

internal sealed record WaitPollSettings(
    int TimeoutMs,
    int PollIntervalMs);

internal sealed record WaitAttemptSummary(
    int Attempt,
    string Outcome,
    DateTimeOffset ObservedAtUtc,
    int? MatchCount = null,
    bool? TargetIsForeground = null,
    string? MatchedElementId = null,
    string? MatchedTextSource = null,
    string? DiagnosticArtifactPath = null,
    string? Detail = null);
