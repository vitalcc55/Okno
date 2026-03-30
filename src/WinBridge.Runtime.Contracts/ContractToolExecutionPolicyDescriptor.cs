using System.Text.Json.Serialization;

namespace WinBridge.Runtime.Contracts;

public sealed record ContractToolExecutionPolicyDescriptor(
    [property: JsonPropertyName("policy_group")]
    string PolicyGroup,
    [property: JsonPropertyName("risk_level")]
    string RiskLevel,
    [property: JsonPropertyName("guard_capability")]
    string GuardCapability,
    [property: JsonPropertyName("supports_dry_run")]
    bool SupportsDryRun,
    [property: JsonPropertyName("confirmation_mode")]
    string ConfirmationMode,
    [property: JsonPropertyName("redaction_class")]
    string RedactionClass);
