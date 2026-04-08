using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinBridge.Runtime.Contracts;

public sealed record OpenTargetRequest
{
    private string _targetKind = string.Empty;
    private string _target = string.Empty;

    [JsonPropertyName("targetKind")]
    public string TargetKind
    {
        get => _targetKind;
        init => _targetKind = (value ?? string.Empty).Trim();
    }

    [JsonPropertyName("target")]
    public string Target
    {
        get => _target;
        init => _target = (value ?? string.Empty).Trim();
    }

    [JsonPropertyName("dryRun")]
    public bool DryRun { get; init; }

    [JsonPropertyName("confirm")]
    public bool Confirm { get; init; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalProperties { get; init; }
}
