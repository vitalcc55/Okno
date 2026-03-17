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
}
