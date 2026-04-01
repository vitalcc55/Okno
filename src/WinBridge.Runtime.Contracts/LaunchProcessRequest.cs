using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinBridge.Runtime.Contracts;

public sealed record LaunchProcessRequest
{
    private IReadOnlyList<string> _args = [];
    private string _executable = string.Empty;
    private string? _workingDirectory;

    [JsonPropertyName("executable")]
    public string Executable
    {
        get => _executable;
        init => _executable = (value ?? string.Empty).Trim();
    }

    [JsonPropertyName("args")]
    public IReadOnlyList<string> Args
    {
        get => _args;
        init => _args = value ?? [];
    }

    [JsonPropertyName("workingDirectory")]
    public string? WorkingDirectory
    {
        get => _workingDirectory;
        init => _workingDirectory = value?.Trim();
    }

    [JsonPropertyName("waitForWindow")]
    public bool WaitForWindow { get; init; }

    [JsonPropertyName("timeoutMs")]
    public int? TimeoutMs { get; init; }

    [JsonPropertyName("dryRun")]
    public bool DryRun { get; init; }

    [JsonPropertyName("confirm")]
    public bool Confirm { get; init; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalProperties { get; init; }
}
