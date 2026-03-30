using System.Text.Json.Serialization;

namespace WinBridge.Runtime.Contracts;

public sealed record ContractToolDescriptor(
    [property: JsonPropertyName("name")]
    string Name,
    [property: JsonPropertyName("capability")]
    string Capability,
    [property: JsonPropertyName("lifecycle")]
    string Lifecycle,
    [property: JsonPropertyName("safety_class")]
    string SafetyClass,
    [property: JsonPropertyName("summary")]
    string Summary,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    [property: JsonPropertyName("planned_phase")]
    string? PlannedPhase,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    [property: JsonPropertyName("suggested_alternative")]
    string? SuggestedAlternative,
    [property: JsonPropertyName("smoke_required")]
    bool SmokeRequired,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    [property: JsonPropertyName("execution_policy")]
    ContractToolExecutionPolicyDescriptor? ExecutionPolicy);
