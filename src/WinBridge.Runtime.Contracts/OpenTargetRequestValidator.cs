using System.Text.Json;

namespace WinBridge.Runtime.Contracts;

public static class OpenTargetRequestValidator
{
    public static bool TryCreatePreview(
        OpenTargetRequest request,
        out OpenTargetPreview? preview,
        out string? failureCode,
        out string? reason)
    {
        if (TryValidate(request, out OpenTargetClassification classification, out failureCode, out reason))
        {
            preview = classification.ToPreview();
            return true;
        }

        preview = null;
        return false;
    }

    public static bool TryValidate(
        OpenTargetRequest request,
        out string? failureCode,
        out string? reason) =>
        TryValidate(
            request,
            out _,
            out failureCode,
            out reason);

    internal static bool TryValidate(
        OpenTargetRequest request,
        out OpenTargetClassification classification,
        out string? failureCode,
        out string? reason)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.TargetKind))
        {
            classification = default;
            failureCode = OpenTargetFailureCodeValues.InvalidRequest;
            reason = "Параметр targetKind для open_target не должен быть пустым.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.Target))
        {
            classification = default;
            failureCode = OpenTargetFailureCodeValues.InvalidRequest;
            reason = "Параметр target для open_target не должен быть пустым.";
            return false;
        }

        if (!TryValidateAdditionalProperties(request.AdditionalProperties, out failureCode, out reason))
        {
            classification = default;
            return false;
        }

        return OpenTargetClassifier.TryClassify(request, out classification, out failureCode, out reason);
    }

    private static bool TryValidateAdditionalProperties(
        IDictionary<string, JsonElement>? additionalProperties,
        out string? failureCode,
        out string? reason)
    {
        if (additionalProperties is null || additionalProperties.Count == 0)
        {
            failureCode = null;
            reason = null;
            return true;
        }

        string key = additionalProperties.Keys
            .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
            .First();

        failureCode = OpenTargetFailureCodeValues.InvalidRequest;
        reason = $"Дополнительное поле '{key}' не входит в текущий request surface open_target.";
        return false;
    }
}
