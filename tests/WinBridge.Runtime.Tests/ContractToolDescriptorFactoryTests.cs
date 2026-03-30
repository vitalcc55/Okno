using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Tooling;

namespace WinBridge.Runtime.Tests;

public sealed class ContractToolDescriptorFactoryTests
{
    [Fact]
    public void FromToolDescriptorUsesSnakeCaseEnumLiterals()
    {
        ContractToolDescriptor descriptor = ContractToolDescriptorFactory.FromToolDescriptor(
            new ToolDescriptor(
                Name: "windows.attach_window",
                Capability: "windows.shell",
                Lifecycle: ToolLifecycle.Implemented,
                SafetyClass: ToolSafetyClass.SessionMutation,
                Summary: "Attach tool.",
                PlannedPhase: null,
                SuggestedAlternative: null,
                SmokeRequired: true));

        Assert.Equal("implemented", descriptor.Lifecycle);
        Assert.Equal("session_mutation", descriptor.SafetyClass);
    }

    [Fact]
    public void FromToolDescriptorExportsExecutionPolicyUsingSnakeCaseLiterals()
    {
        ContractToolDescriptor descriptor = ContractToolDescriptorFactory.FromToolDescriptor(
            new ToolDescriptor(
                Name: "windows.input",
                Capability: "windows.input",
                Lifecycle: ToolLifecycle.Deferred,
                SafetyClass: ToolSafetyClass.OsSideEffect,
                Summary: "Input tool.",
                PlannedPhase: "roadmap stage 5",
                SuggestedAlternative: "Use observe path first.",
                SmokeRequired: false,
                ExecutionPolicy: new ToolExecutionPolicyDescriptor(
                    PolicyGroup: ToolExecutionPolicyGroup.Input,
                    RiskLevel: ToolExecutionRiskLevel.Destructive,
                    GuardCapability: "input",
                    SupportsDryRun: false,
                    ConfirmationMode: ToolExecutionConfirmationMode.Required,
                    RedactionClass: ToolExecutionRedactionClass.TextPayload)));

        ContractToolExecutionPolicyDescriptor policy = Assert.IsType<ContractToolExecutionPolicyDescriptor>(descriptor.ExecutionPolicy);
        Assert.Equal("input", policy.PolicyGroup);
        Assert.Equal("destructive", policy.RiskLevel);
        Assert.Equal("input", policy.GuardCapability);
        Assert.False(policy.SupportsDryRun);
        Assert.Equal("required", policy.ConfirmationMode);
        Assert.Equal("text_payload", policy.RedactionClass);
    }
}
