using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;

namespace WinBridge.Runtime.Windows.Input;

internal sealed class InputArtifactWriter(AuditLogOptions auditLogOptions)
{
    private static readonly Encoding FileEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    public string Write(
        InputRequest request,
        InputExecutionContext context,
        InputResult result,
        DateTimeOffset capturedAtUtc,
        InputFailureDiagnostics? failureDiagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(result);

        string? tempPath = null;

        try
        {
            string directory = Path.Combine(auditLogOptions.RunDirectory, "input");
            Directory.CreateDirectory(directory);

            string path = Path.Combine(directory, InputArtifactNameBuilder.Create(capturedAtUtc.UtcDateTime));
            tempPath = Path.Combine(directory, Path.GetRandomFileName() + ".tmp");
            bool redactSensitiveDragEvidence = InputObservabilityPolicy.RequiresSensitiveDragRedaction(request, result);
            InputResult artifactResult = redactSensitiveDragEvidence
                ? InputObservabilityPolicy.RedactDragCoordinates(result)
                : result;
            InputSanitizedResult sanitizedResult = InputSanitizedResult.FromResult(
                artifactResult with { ArtifactPath = path },
                InputResultMaterializer.ResolveCommittedSideEffectEvidence(result, failureDiagnostics?.FailureStage));
            InputArtifactPayload payload = new(
                RequestSummary: InputRequestSummary.FromRequest(request, context, result),
                TargetSummary: InputTargetSummary.FromContext(context, result),
                Result: sanitizedResult,
                CapturedAtUtc: capturedAtUtc,
                FailureDiagnostics: failureDiagnostics);
            string document = JsonSerializer.Serialize(payload, JsonOptions);
            File.WriteAllText(tempPath, document, FileEncoding);
            File.Move(tempPath, path);
            return path;
        }
        catch (Exception exception) when (IsArtifactWriteFailure(exception))
        {
            TryDeleteTempArtifactFile(tempPath);
            throw new InputArtifactException("Runtime не смог записать input artifact на диск.", exception);
        }
    }

    private static bool IsArtifactWriteFailure(Exception exception) =>
        exception is UnauthorizedAccessException or IOException or NotSupportedException or SecurityException;

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

internal sealed record InputArtifactPayload(
    InputRequestSummary RequestSummary,
    InputTargetSummary TargetSummary,
    InputSanitizedResult Result,
    DateTimeOffset CapturedAtUtc,
    InputFailureDiagnostics? FailureDiagnostics = null);

internal sealed record InputRequestSummary(
    int ActionCount,
    IReadOnlyList<string> ActionTypes,
    IReadOnlyList<string> CoordinateSpaces,
    long? TargetHwnd,
    string? TargetSource,
    IReadOnlyList<InputRequestActionSummary> Actions)
{
    public static InputRequestSummary FromRequest(
        InputRequest request,
        InputExecutionContext context,
        InputResult result)
    {
        IReadOnlyList<InputAction> actions = request.Actions ?? Array.Empty<InputAction>();
        return new(
            ActionCount: actions.Count,
            ActionTypes: DistinctNonEmpty(actions.Select(action => action.Type)),
            CoordinateSpaces: DistinctNonEmpty(actions.Select(action => action.CoordinateSpace)),
            TargetHwnd: result.TargetHwnd ?? request.Hwnd ?? context.AttachedWindow?.Hwnd,
            TargetSource: result.TargetSource,
            Actions: actions
                .Select((action, index) => InputRequestActionSummary.FromAction(index, action))
                .ToArray());
    }

    private static string[] DistinctNonEmpty(IEnumerable<string?> values) =>
        values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
}

internal sealed record InputRequestActionSummary(
    int Index,
    string? Type,
    string? CoordinateSpace,
    string? Button,
    bool HasPoint,
    bool HasCaptureReference)
{
    public static InputRequestActionSummary FromAction(int index, InputAction action) =>
        new(
            Index: index,
            Type: action.Type,
            CoordinateSpace: action.CoordinateSpace,
            Button: action.Button,
            HasPoint: action.Point is not null,
            HasCaptureReference: action.CaptureReference is not null);
}

internal sealed record InputTargetSummary(
    long? TargetHwnd,
    string? TargetSource,
    long? AttachedHwnd,
    string? AttachedProcessName,
    int? AttachedProcessId)
{
    public static InputTargetSummary FromContext(InputExecutionContext context, InputResult result) =>
        new(
            TargetHwnd: result.TargetHwnd ?? context.AttachedWindow?.Hwnd,
            TargetSource: result.TargetSource,
            AttachedHwnd: context.AttachedWindow?.Hwnd,
            AttachedProcessName: context.AttachedWindow?.ProcessName,
            AttachedProcessId: context.AttachedWindow?.ProcessId);
}

internal sealed record InputSanitizedResult(
    string Status,
    string Decision,
    string? ResultMode,
    string? FailureCode,
    string? Reason,
    long? TargetHwnd,
    string? TargetSource,
    int CompletedActionCount,
    int? FailedActionIndex,
    IReadOnlyList<InputSanitizedActionResult>? Actions,
    string? ArtifactPath,
    string? RiskLevel,
    string? GuardCapability,
    bool RequiresConfirmation,
    bool DryRunSupported,
    string CommittedSideEffectEvidence)
{
    public static InputSanitizedResult FromResult(InputResult result, string committedSideEffectEvidence) =>
        new(
            Status: result.Status,
            Decision: result.Decision,
            ResultMode: result.ResultMode,
            FailureCode: result.FailureCode,
            Reason: result.Reason,
            TargetHwnd: result.TargetHwnd,
            TargetSource: result.TargetSource,
            CompletedActionCount: result.CompletedActionCount,
            FailedActionIndex: result.FailedActionIndex,
            Actions: result.Actions?.Select(InputSanitizedActionResult.FromResult).ToArray(),
            ArtifactPath: result.ArtifactPath,
            RiskLevel: result.RiskLevel,
            GuardCapability: result.GuardCapability,
            RequiresConfirmation: result.RequiresConfirmation,
            DryRunSupported: result.DryRunSupported,
            CommittedSideEffectEvidence: committedSideEffectEvidence);
}

internal sealed record InputSanitizedActionResult(
    string Type,
    string Status,
    string? ResultMode,
    string? FailureCode,
    string? Reason,
    string? CoordinateSpace,
    InputSafePoint? RequestedPoint,
    InputSafePoint? ResolvedScreenPoint,
    string? Button)
{
    public static InputSanitizedActionResult FromResult(InputActionResult result) =>
        new(
            Type: result.Type,
            Status: result.Status,
            ResultMode: result.ResultMode,
            FailureCode: result.FailureCode,
            Reason: result.Reason,
            CoordinateSpace: result.CoordinateSpace,
            RequestedPoint: InputSafePoint.FromPoint(result.RequestedPoint),
            ResolvedScreenPoint: InputSafePoint.FromPoint(result.ResolvedScreenPoint),
            Button: result.Button);
}

internal sealed record InputSafePoint(int X, int Y)
{
    public static InputSafePoint? FromPoint(InputPoint? point) =>
        point is null ? null : new InputSafePoint(point.X, point.Y);
}

internal sealed record InputFailureDiagnostics(
    string? FailureStage = null,
    string? ExceptionType = null,
    bool ExceptionMessageSuppressed = false);

internal sealed class InputArtifactException(string message, Exception innerException)
    : Exception(message, innerException);

internal static class InputObservabilityPolicy
{
    private const string SafeDragReason = "Runtime drag завершился без безопасного раскрытия coordinate detail.";

    public static bool RequiresSensitiveDragRedaction(InputRequest request, InputResult result)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);

        return ContainsDragRequestAction(request.Actions)
            || ContainsDragResultAction(result.Actions);
    }

    public static InputResult RedactDragCoordinates(InputResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result with
        {
            Reason = RedactReasonIfPresent(result.Reason),
            Actions = result.Actions?.Select(RedactAction).ToArray(),
        };
    }

    private static bool ContainsDragRequestAction(IReadOnlyList<InputAction>? actions) =>
        actions?.Any(static action => string.Equals(action.Type, InputActionTypeValues.Drag, StringComparison.Ordinal)) == true;

    private static bool ContainsDragResultAction(IReadOnlyList<InputActionResult>? actions) =>
        actions?.Any(static action => string.Equals(action.Type, InputActionTypeValues.Drag, StringComparison.Ordinal)) == true;

    private static InputActionResult RedactAction(InputActionResult action)
    {
        if (!string.Equals(action.Type, InputActionTypeValues.Drag, StringComparison.Ordinal))
        {
            return action;
        }

        return action with
        {
            Reason = RedactReasonIfPresent(action.Reason),
            RequestedPoint = null,
            ResolvedScreenPoint = null,
        };
    }

    private static string? RedactReasonIfPresent(string? reason) =>
        string.IsNullOrWhiteSpace(reason) ? reason : SafeDragReason;
}
