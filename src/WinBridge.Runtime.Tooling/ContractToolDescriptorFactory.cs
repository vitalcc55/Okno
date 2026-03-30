using System.Text.Json;
using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Tooling;

public static class ContractToolDescriptorFactory
{
    public static ContractToolDescriptor FromToolDescriptor(ToolDescriptor descriptor) =>
        new(
            Name: descriptor.Name,
            Capability: descriptor.Capability,
            Lifecycle: ToContractLiteral(descriptor.Lifecycle),
            SafetyClass: ToContractLiteral(descriptor.SafetyClass),
            Summary: descriptor.Summary,
            PlannedPhase: descriptor.PlannedPhase,
            SuggestedAlternative: descriptor.SuggestedAlternative,
            SmokeRequired: descriptor.SmokeRequired,
            ExecutionPolicy: ToContractDescriptor(descriptor.ExecutionPolicy));

    private static ContractToolExecutionPolicyDescriptor? ToContractDescriptor(ToolExecutionPolicyDescriptor? descriptor) =>
        descriptor is null
            ? null
            : new ContractToolExecutionPolicyDescriptor(
                PolicyGroup: ToContractLiteral(descriptor.PolicyGroup),
                RiskLevel: ToContractLiteral(descriptor.RiskLevel),
                GuardCapability: descriptor.GuardCapability,
                SupportsDryRun: descriptor.SupportsDryRun,
                ConfirmationMode: ToContractLiteral(descriptor.ConfirmationMode),
                RedactionClass: ToContractLiteral(descriptor.RedactionClass));

    private static string ToContractLiteral<TEnum>(TEnum value)
        where TEnum : struct, Enum =>
        JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());
}
