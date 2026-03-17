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
            SmokeRequired: descriptor.SmokeRequired);

    private static string ToContractLiteral<TEnum>(TEnum value)
        where TEnum : struct, Enum =>
        JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());
}
