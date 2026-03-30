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
    public void ImplementedDescriptorsDoNotPublishExecutionPolicyMetadata()
    {
        Assert.All(
            ToolContractManifest.Implemented,
            descriptor => Assert.Null(descriptor.ExecutionPolicy));
    }

    [Fact]
    public void DeferredDescriptorsPublishExecutionPolicyMetadata()
    {
        Assert.All(
            ToolContractManifest.Deferred,
            descriptor =>
            {
                ToolExecutionPolicyDescriptor policy = Assert.IsType<ToolExecutionPolicyDescriptor>(descriptor.ExecutionPolicy);
                Assert.False(string.IsNullOrWhiteSpace(policy.GuardCapability));
                Assert.True(Enum.IsDefined(typeof(ToolExecutionPolicyGroup), policy.PolicyGroup));
                Assert.True(Enum.IsDefined(typeof(ToolExecutionRiskLevel), policy.RiskLevel));
                Assert.True(Enum.IsDefined(typeof(ToolExecutionConfirmationMode), policy.ConfirmationMode));
                Assert.True(Enum.IsDefined(typeof(ToolExecutionRedactionClass), policy.RedactionClass));
            });
    }

    [Fact]
    public void FutureLaunchPresetsExistWithoutManifestPublication()
    {
        Assert.Equal(2, ToolContractManifest.FutureLaunchFamilyPolicyPresets.Count);
        Assert.True(ToolContractManifest.FutureLaunchFamilyPolicyPresets.ContainsKey("windows.launch_process"));
        Assert.True(ToolContractManifest.FutureLaunchFamilyPolicyPresets.ContainsKey("windows.open_target"));

        Assert.DoesNotContain(ToolContractManifest.All, descriptor => descriptor.Name == "windows.launch_process");
        Assert.DoesNotContain(ToolContractManifest.All, descriptor => descriptor.Name == "windows.open_target");
        Assert.DoesNotContain(ToolContractManifest.DeferredPhaseMap.Keys, toolName => toolName == "windows.launch_process");
        Assert.DoesNotContain(ToolContractManifest.DeferredPhaseMap.Keys, toolName => toolName == "windows.open_target");
    }

    [Fact]
    public void FutureLaunchPresetsAreNotPublishedAsPublicApi()
    {
        Assert.Null(typeof(ToolContractManifest).GetProperty(
            "FutureLaunchFamilyPolicyPresets",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static));
        Assert.NotNull(typeof(ToolContractManifest).GetProperty(
            "FutureLaunchFamilyPolicyPresets",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static));
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
    public void WindowsWaitIsImplementedAndSmokeRequired()
    {
        ToolDescriptor descriptor = Assert.Single(
            ToolContractManifest.Implemented,
            item => item.Name == ToolNames.WindowsWait);

        Assert.Equal(ToolLifecycle.Implemented, descriptor.Lifecycle);
        Assert.Equal(ToolSafetyClass.OsSideEffect, descriptor.SafetyClass);
        Assert.Contains(ToolNames.WindowsWait, ToolContractManifest.SmokeRequiredToolNames);
        Assert.DoesNotContain(ToolContractManifest.Deferred, item => item.Name == ToolNames.WindowsWait);
    }

    [Fact]
    public void DeferredActionDescriptorsUseExpectedExecutionPolicyMetadata()
    {
        ToolDescriptor clipboardGet = Assert.Single(ToolContractManifest.Deferred, item => item.Name == ToolNames.WindowsClipboardGet);
        ToolDescriptor clipboardSet = Assert.Single(ToolContractManifest.Deferred, item => item.Name == ToolNames.WindowsClipboardSet);
        ToolDescriptor input = Assert.Single(ToolContractManifest.Deferred, item => item.Name == ToolNames.WindowsInput);
        ToolDescriptor uiaAction = Assert.Single(ToolContractManifest.Deferred, item => item.Name == ToolNames.WindowsUiaAction);

        AssertExecutionPolicy(
            clipboardGet.ExecutionPolicy,
            ToolExecutionPolicyGroup.Clipboard,
            ToolExecutionRiskLevel.Medium,
            "clipboard",
            supportsDryRun: false,
            ToolExecutionConfirmationMode.Required,
            ToolExecutionRedactionClass.ClipboardPayload);
        AssertExecutionPolicy(
            clipboardSet.ExecutionPolicy,
            ToolExecutionPolicyGroup.Clipboard,
            ToolExecutionRiskLevel.High,
            "clipboard",
            supportsDryRun: true,
            ToolExecutionConfirmationMode.Required,
            ToolExecutionRedactionClass.ClipboardPayload);
        AssertExecutionPolicy(
            input.ExecutionPolicy,
            ToolExecutionPolicyGroup.Input,
            ToolExecutionRiskLevel.Destructive,
            "input",
            supportsDryRun: false,
            ToolExecutionConfirmationMode.Required,
            ToolExecutionRedactionClass.TextPayload);
        AssertExecutionPolicy(
            uiaAction.ExecutionPolicy,
            ToolExecutionPolicyGroup.UiaAction,
            ToolExecutionRiskLevel.High,
            "uia",
            supportsDryRun: false,
            ToolExecutionConfirmationMode.Required,
            ToolExecutionRedactionClass.TargetMetadata);
    }

    private static void AssertExecutionPolicy(
        ToolExecutionPolicyDescriptor? policy,
        ToolExecutionPolicyGroup expectedGroup,
        ToolExecutionRiskLevel expectedRiskLevel,
        string expectedGuardCapability,
        bool supportsDryRun,
        ToolExecutionConfirmationMode expectedConfirmationMode,
        ToolExecutionRedactionClass expectedRedactionClass)
    {
        ToolExecutionPolicyDescriptor typedPolicy = Assert.IsType<ToolExecutionPolicyDescriptor>(policy);
        Assert.Equal(expectedGroup, typedPolicy.PolicyGroup);
        Assert.Equal(expectedRiskLevel, typedPolicy.RiskLevel);
        Assert.Equal(expectedGuardCapability, typedPolicy.GuardCapability);
        Assert.Equal(supportsDryRun, typedPolicy.SupportsDryRun);
        Assert.Equal(expectedConfirmationMode, typedPolicy.ConfirmationMode);
        Assert.Equal(expectedRedactionClass, typedPolicy.RedactionClass);
    }
}
