using WinBridge.Runtime.Tooling;

namespace WinBridge.Runtime.Tests;

public sealed class ToolContractManifestTests
{
    [Fact]
    public void AllToolNamesAreUnique()
    {
        string[] names = ToolContractManifest.All.Select(descriptor => descriptor.Name).ToArray();

        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void ImplementedDescriptorsContainRequiredMetadata()
    {
        Assert.All(
            ToolContractManifest.Implemented,
            descriptor =>
            {
                Assert.False(string.IsNullOrWhiteSpace(descriptor.Summary));
                Assert.False(string.IsNullOrWhiteSpace(descriptor.Capability));
                Assert.True(
                    Enum.IsDefined(typeof(ToolSafetyClass), descriptor.SafetyClass),
                    $"Unexpected safety class '{descriptor.SafetyClass}'.");
            });
    }

    [Fact]
    public void DeferredDescriptorsContainPhaseAndAlternative()
    {
        Assert.All(
            ToolContractManifest.Deferred,
            descriptor =>
            {
                Assert.False(string.IsNullOrWhiteSpace(descriptor.PlannedPhase));
                Assert.False(string.IsNullOrWhiteSpace(descriptor.SuggestedAlternative));
            });
    }

    [Fact]
    public void SmokeRequiredNamesAreSubsetOfImplementedTools()
    {
        HashSet<string> implemented = ToolContractManifest.ImplementedNames.ToHashSet(StringComparer.Ordinal);

        Assert.All(
            ToolContractManifest.SmokeRequiredToolNames,
            toolName => Assert.Contains(toolName, implemented));
    }

    [Fact]
    public void WindowsCaptureIsImplementedAndSmokeRequired()
    {
        ToolDescriptor descriptor = Assert.Single(
            ToolContractManifest.Implemented,
            item => item.Name == ToolNames.WindowsCapture);

        Assert.Equal(ToolLifecycle.Implemented, descriptor.Lifecycle);
        Assert.Equal(ToolSafetyClass.OsSideEffect, descriptor.SafetyClass);
        Assert.Contains(ToolNames.WindowsCapture, ToolContractManifest.SmokeRequiredToolNames);
        Assert.DoesNotContain(
            ToolContractManifest.Deferred,
            item => item.Name == ToolNames.WindowsCapture);
    }

    [Fact]
    public void WindowsUiaSnapshotIsImplementedAndSmokeRequired()
    {
        ToolDescriptor descriptor = Assert.Single(
            ToolContractManifest.Implemented,
            item => item.Name == ToolNames.WindowsUiaSnapshot);

        Assert.Equal(ToolLifecycle.Implemented, descriptor.Lifecycle);
        Assert.Equal(ToolSafetyClass.ReadOnly, descriptor.SafetyClass);
        Assert.Contains(ToolNames.WindowsUiaSnapshot, ToolContractManifest.SmokeRequiredToolNames);
        Assert.DoesNotContain(ToolContractManifest.Deferred, item => item.Name == ToolNames.WindowsUiaSnapshot);
    }

    [Fact]
    public void WindowsWaitRemainsDeferredWithOsSideEffectSafetyClass()
    {
        ToolDescriptor descriptor = Assert.Single(
            ToolContractManifest.Deferred,
            item => item.Name == ToolNames.WindowsWait);

        Assert.Equal(ToolLifecycle.Deferred, descriptor.Lifecycle);
        Assert.Equal(ToolSafetyClass.OsSideEffect, descriptor.SafetyClass);
        Assert.DoesNotContain(ToolNames.WindowsWait, ToolContractManifest.SmokeRequiredToolNames);
    }
}
