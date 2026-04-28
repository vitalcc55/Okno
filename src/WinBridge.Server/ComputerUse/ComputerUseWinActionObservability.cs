using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;

namespace WinBridge.Server.ComputerUse;

internal sealed record ComputerUseWinActionObservabilityContext(
    string ActionName,
    string RuntimeState,
    string AppId,
    bool WindowIdPresent,
    bool StateTokenPresent,
    string TargetMode,
    bool ElementIndexPresent,
    string? CoordinateSpace,
    bool CaptureReferencePresent,
    bool ConfirmationRequired,
    bool Confirmed,
    string? RiskClass,
    string? DispatchPath,
    string? KeyCategory = null,
    int? RepeatCount = null,
    bool? DangerousCombo = null,
    string? LayoutResolutionStatus = null,
    string? ValueKind = null,
    int? ValueLength = null,
    string? ValueBucket = null,
    int? TextLength = null,
    string? TextBucket = null,
    bool? ContainsNewline = null,
    bool? WhitespaceOnly = null,
    string? ScrollDirection = null,
    string? ScrollAmountBucket = null,
    string? ScrollUnit = null,
    bool? SemanticScrollSupported = null,
    bool? FallbackUsed = null,
    string? SemanticActionKind = null,
    bool? ContextMenuPathUsed = null,
    string? SourceMode = null,
    string? DestinationMode = null,
    string? PathPointCountBucket = null,
    bool? CoordinateFallbackUsed = null,
    string? ChildArtifactPath = null,
    string? FailureStage = null,
    string? ExceptionType = null);

internal static class ComputerUseWinActionObservability
{
    private const string EventName = "computer_use_win.action.completed";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    public static void RecordBestEffort(
        AuditInvocationScope invocation,
        string toolName,
        ComputerUseWinActionResult payload,
        ComputerUseWinActionObservabilityContext? context)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentNullException.ThrowIfNull(payload);

        ComputerUseWinActionObservabilityContext effectiveContext = context ?? CreateFallbackContext(toolName, payload);

        string? artifactPath = null;
        string? artifactFailureStage = null;
        string? artifactExceptionType = null;
        try
        {
            artifactPath = WriteArtifact(invocation.RunDirectory, payload, effectiveContext);
        }
        catch (Exception exception)
        {
            artifactFailureStage = "artifact_write";
            artifactExceptionType = exception.GetType().FullName;
        }

        string? failureStage = effectiveContext.FailureStage ?? artifactFailureStage;
        string? exceptionType = effectiveContext.ExceptionType ?? artifactExceptionType;
        invocation.TryRecordRuntimeEvent(
            eventName: EventName,
            severity: payload.Status is "done" or "verify_needed" && failureStage is null ? "info" : "warning",
            messageHuman: failureStage is "artifact_write"
                ? $"Computer Use action '{toolName}' завершён, но action artifact не materialized."
                : $"Computer Use action '{toolName}' завершён.",
            toolName: toolName,
            outcome: payload.Status,
            windowHwnd: payload.TargetHwnd,
            data: CreateEventData(payload, effectiveContext, artifactPath, failureStage, exceptionType));
    }

    private static string WriteArtifact(
        string runDirectory,
        ComputerUseWinActionResult payload,
        ComputerUseWinActionObservabilityContext context)
    {
        string directory = Path.Combine(runDirectory, "computer-use-win");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(
            directory,
            $"action-{DateTimeOffset.UtcNow.UtcDateTime:yyyyMMddTHHmmssfff}-{Guid.NewGuid():N}.json");

        ComputerUseWinActionArtifactPayload artifactPayload = new(
            ActionName: context.ActionName,
            Status: payload.Status,
            PublicResult: payload.Status,
            FailureCode: payload.FailureCode,
            RuntimeState: context.RuntimeState,
            LifecyclePhase: context.FailureStage ?? "post_dispatch",
            AppId: context.AppId,
            WindowIdPresent: context.WindowIdPresent,
            StateTokenPresent: context.StateTokenPresent,
            TargetMode: context.TargetMode,
            ElementIndexPresent: context.ElementIndexPresent,
            CoordinateSpace: context.CoordinateSpace,
            CaptureReferencePresent: context.CaptureReferencePresent,
            ConfirmationRequired: context.ConfirmationRequired,
            Confirmed: context.Confirmed,
            RiskClass: context.RiskClass,
            DispatchPath: context.DispatchPath,
            KeyCategory: context.KeyCategory,
            RepeatCount: context.RepeatCount,
            DangerousCombo: context.DangerousCombo,
            LayoutResolutionStatus: context.LayoutResolutionStatus,
            ValueKind: context.ValueKind,
            ValueLength: context.ValueLength,
            ValueBucket: context.ValueBucket,
            TextLength: context.TextLength,
            TextBucket: context.TextBucket,
            ContainsNewline: context.ContainsNewline,
            WhitespaceOnly: context.WhitespaceOnly,
            ScrollDirection: context.ScrollDirection,
            ScrollAmountBucket: context.ScrollAmountBucket,
            ScrollUnit: context.ScrollUnit,
            SemanticScrollSupported: context.SemanticScrollSupported,
            FallbackUsed: context.FallbackUsed,
            SemanticActionKind: context.SemanticActionKind,
            ContextMenuPathUsed: context.ContextMenuPathUsed,
            SourceMode: context.SourceMode,
            DestinationMode: context.DestinationMode,
            PathPointCountBucket: context.PathPointCountBucket,
            CoordinateFallbackUsed: context.CoordinateFallbackUsed,
            RefreshStateRecommended: payload.RefreshStateRecommended,
            VerifyStatus: payload.Status,
            ArtifactPath: path,
            ChildArtifactPaths: context.ChildArtifactPath is null ? [] : [context.ChildArtifactPath],
            FailureStage: context.FailureStage,
            ExceptionType: context.ExceptionType);
        File.WriteAllText(path, JsonSerializer.Serialize(artifactPayload, JsonOptions));
        return path;
    }

    private static Dictionary<string, string?> CreateEventData(
        ComputerUseWinActionResult payload,
        ComputerUseWinActionObservabilityContext context,
        string? artifactPath,
        string? failureStage,
        string? exceptionType) =>
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["action_name"] = context.ActionName,
            ["status"] = payload.Status,
            ["public_result"] = payload.Status,
            ["failure_code"] = payload.FailureCode,
            ["runtime_state"] = context.RuntimeState,
            ["lifecycle_phase"] = context.FailureStage ?? "post_dispatch",
            ["app_id"] = context.AppId,
            ["window_id_present"] = context.WindowIdPresent.ToString().ToLowerInvariant(),
            ["state_token_present"] = context.StateTokenPresent.ToString().ToLowerInvariant(),
            ["target_mode"] = context.TargetMode,
            ["element_index_present"] = context.ElementIndexPresent.ToString().ToLowerInvariant(),
            ["coordinate_space"] = context.CoordinateSpace,
            ["capture_reference_present"] = context.CaptureReferencePresent.ToString().ToLowerInvariant(),
            ["confirmation_required"] = context.ConfirmationRequired.ToString().ToLowerInvariant(),
            ["confirmed"] = context.Confirmed.ToString().ToLowerInvariant(),
            ["risk_class"] = context.RiskClass,
            ["dispatch_path"] = context.DispatchPath,
            ["key_category"] = context.KeyCategory,
            ["repeat_count"] = context.RepeatCount?.ToString(CultureInfo.InvariantCulture),
            ["dangerous_combo"] = context.DangerousCombo?.ToString().ToLowerInvariant(),
            ["layout_resolution_status"] = context.LayoutResolutionStatus,
            ["value_kind"] = context.ValueKind,
            ["value_length"] = context.ValueLength?.ToString(CultureInfo.InvariantCulture),
            ["value_bucket"] = context.ValueBucket,
            ["text_length"] = context.TextLength?.ToString(CultureInfo.InvariantCulture),
            ["text_bucket"] = context.TextBucket,
            ["contains_newline"] = context.ContainsNewline?.ToString().ToLowerInvariant(),
            ["whitespace_only"] = context.WhitespaceOnly?.ToString().ToLowerInvariant(),
            ["scroll_direction"] = context.ScrollDirection,
            ["scroll_amount_bucket"] = context.ScrollAmountBucket,
            ["scroll_unit"] = context.ScrollUnit,
            ["semantic_scroll_supported"] = context.SemanticScrollSupported?.ToString().ToLowerInvariant(),
            ["fallback_used"] = context.FallbackUsed?.ToString().ToLowerInvariant(),
            ["semantic_action_kind"] = context.SemanticActionKind,
            ["context_menu_path_used"] = context.ContextMenuPathUsed?.ToString().ToLowerInvariant(),
            ["source_mode"] = context.SourceMode,
            ["destination_mode"] = context.DestinationMode,
            ["path_point_count_bucket"] = context.PathPointCountBucket,
            ["coordinate_fallback_used"] = context.CoordinateFallbackUsed?.ToString().ToLowerInvariant(),
            ["refresh_state_recommended"] = payload.RefreshStateRecommended.ToString().ToLowerInvariant(),
            ["verify_status"] = payload.Status,
            ["artifact_path"] = artifactPath,
            ["child_artifact_paths"] = context.ChildArtifactPath,
            ["failure_stage"] = failureStage,
            ["exception_type"] = exceptionType,
        };

    private static ComputerUseWinActionObservabilityContext CreateFallbackContext(
        string toolName,
        ComputerUseWinActionResult payload) =>
        new(
            ActionName: toolName,
            RuntimeState: "observed",
            AppId: "unknown",
            WindowIdPresent: false,
            StateTokenPresent: false,
            TargetMode: payload.ElementIndex is null ? "unknown" : "element_index",
            ElementIndexPresent: payload.ElementIndex is not null,
            CoordinateSpace: null,
            CaptureReferencePresent: false,
            ConfirmationRequired: false,
            Confirmed: false,
            RiskClass: null,
            DispatchPath: null);
}

internal sealed record ComputerUseWinActionArtifactPayload(
    string ActionName,
    string Status,
    string PublicResult,
    string? FailureCode,
    string RuntimeState,
    string LifecyclePhase,
    string AppId,
    bool WindowIdPresent,
    bool StateTokenPresent,
    string TargetMode,
    bool ElementIndexPresent,
    string? CoordinateSpace,
    bool CaptureReferencePresent,
    bool ConfirmationRequired,
    bool Confirmed,
    string? RiskClass,
    string? DispatchPath,
    string? KeyCategory,
    int? RepeatCount,
    bool? DangerousCombo,
    string? LayoutResolutionStatus,
    string? ValueKind,
    int? ValueLength,
    string? ValueBucket,
    int? TextLength,
    string? TextBucket,
    bool? ContainsNewline,
    bool? WhitespaceOnly,
    string? ScrollDirection,
    string? ScrollAmountBucket,
    string? ScrollUnit,
    bool? SemanticScrollSupported,
    bool? FallbackUsed,
    string? SemanticActionKind,
    bool? ContextMenuPathUsed,
    string? SourceMode,
    string? DestinationMode,
    string? PathPointCountBucket,
    bool? CoordinateFallbackUsed,
    bool RefreshStateRecommended,
    string VerifyStatus,
    string ArtifactPath,
    IReadOnlyList<string> ChildArtifactPaths,
    string? FailureStage,
    string? ExceptionType);
