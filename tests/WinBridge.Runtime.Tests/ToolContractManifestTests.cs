using WinBridge.Runtime.Contracts;
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
    public void ImplementedDescriptorsOnlyPublishExecutionPolicyForPolicyBearingTools()
    {
        Assert.All(
            ToolContractManifest.Implemented,
            descriptor =>
            {
                if (descriptor.Name == ToolNames.WindowsLaunchProcess)
                {
                    AssertExecutionPolicy(
                        descriptor.ExecutionPolicy,
                        ToolExecutionPolicyGroup.Launch,
                        ToolExecutionRiskLevel.High,
                        CapabilitySummaryValues.Launch,
                        supportsDryRun: true,
                        ToolExecutionConfirmationMode.Required,
                        ToolExecutionRedactionClass.LaunchPayload);
                    return;
                }

                if (descriptor.Name == ToolNames.WindowsOpenTarget)
                {
                    AssertExecutionPolicy(
                        descriptor.ExecutionPolicy,
                        ToolExecutionPolicyGroup.Launch,
                        ToolExecutionRiskLevel.Medium,
                        CapabilitySummaryValues.Launch,
                        supportsDryRun: true,
                        ToolExecutionConfirmationMode.Required,
                        ToolExecutionRedactionClass.LaunchPayload);
                    return;
                }

                if (descriptor.Name == ToolNames.WindowsInput)
                {
                    AssertExecutionPolicy(
                        descriptor.ExecutionPolicy,
                        ToolExecutionPolicyGroup.Input,
                        ToolExecutionRiskLevel.Destructive,
                        CapabilitySummaryValues.Input,
                        supportsDryRun: false,
                        ToolExecutionConfirmationMode.Required,
                        ToolExecutionRedactionClass.TextPayload);
                    return;
                }

                Assert.Null(descriptor.ExecutionPolicy);
            });
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
    public void FutureLaunchPresetsAreEmptyAfterOpenTargetPublication()
    {
        Assert.Empty(ToolContractManifest.FutureLaunchFamilyPolicyPresets);

        Assert.Contains(ToolContractManifest.All, descriptor => descriptor.Name == ToolNames.WindowsLaunchProcess);
        Assert.Contains(ToolContractManifest.All, descriptor => descriptor.Name == ToolNames.WindowsOpenTarget);
        Assert.Contains(ToolContractManifest.ImplementedNames, toolName => toolName == ToolNames.WindowsLaunchProcess);
        Assert.Contains(ToolContractManifest.ImplementedNames, toolName => toolName == ToolNames.WindowsOpenTarget);
        Assert.DoesNotContain(ToolContractManifest.DeferredPhaseMap.Keys, toolName => toolName == ToolNames.WindowsOpenTarget);
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
    public void FutureLaunchProcessDescriptorCarriesFinalFrozenMetadata()
    {
        ToolDescriptor descriptor = ToolContractManifest.FutureLaunchProcessDescriptor;

        Assert.Equal(ToolNames.WindowsLaunchProcess, descriptor.Name);
        Assert.Equal("windows.launch", descriptor.Capability);
        Assert.Equal(ToolLifecycle.Implemented, descriptor.Lifecycle);
        Assert.Equal(ToolSafetyClass.OsSideEffect, descriptor.SafetyClass);
        Assert.Equal(ToolDescriptions.WindowsLaunchProcessTool, descriptor.Summary);
        Assert.True(descriptor.SmokeRequired);
        Assert.Null(descriptor.PlannedPhase);
        Assert.Null(descriptor.SuggestedAlternative);
        AssertExecutionPolicy(
            descriptor.ExecutionPolicy,
            ToolExecutionPolicyGroup.Launch,
            ToolExecutionRiskLevel.High,
            CapabilitySummaryValues.Launch,
            supportsDryRun: true,
            ToolExecutionConfirmationMode.Required,
            ToolExecutionRedactionClass.LaunchPayload);
        Assert.Same(
            descriptor.ExecutionPolicy,
            ToolContractManifest.ResolveExecutionPolicy(ToolNames.WindowsLaunchProcess));
        Assert.Contains(
            ToolContractManifest.Implemented,
            implementedDescriptor => ReferenceEquals(implementedDescriptor, descriptor));
    }

    [Fact]
    public void FutureOpenTargetDescriptorCarriesFinalFrozenMetadataWithPublication()
    {
        ToolDescriptor descriptor = ToolContractManifest.FutureOpenTargetDescriptor;

        Assert.Equal(ToolNames.WindowsOpenTarget, descriptor.Name);
        Assert.Equal("windows.launch", descriptor.Capability);
        Assert.Equal(ToolLifecycle.Implemented, descriptor.Lifecycle);
        Assert.Equal(ToolSafetyClass.OsSideEffect, descriptor.SafetyClass);
        Assert.Equal(ToolDescriptions.WindowsOpenTargetTool, descriptor.Summary);
        Assert.True(descriptor.SmokeRequired);
        Assert.Null(descriptor.PlannedPhase);
        Assert.Null(descriptor.SuggestedAlternative);
        AssertExecutionPolicy(
            descriptor.ExecutionPolicy,
            ToolExecutionPolicyGroup.Launch,
            ToolExecutionRiskLevel.Medium,
            CapabilitySummaryValues.Launch,
            supportsDryRun: true,
            ToolExecutionConfirmationMode.Required,
            ToolExecutionRedactionClass.LaunchPayload);
        Assert.Same(
            descriptor.ExecutionPolicy,
            ToolContractManifest.ResolveExecutionPolicy(ToolNames.WindowsOpenTarget));
        Assert.Contains(
            ToolContractManifest.All,
            implementedDescriptor => ReferenceEquals(implementedDescriptor, descriptor));
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
    public void WindowsLaunchProcessIsImplementedAndSmokeRequired()
    {
        ToolDescriptor descriptor = Assert.Single(
            ToolContractManifest.Implemented,
            item => item.Name == ToolNames.WindowsLaunchProcess);

        Assert.Equal(ToolLifecycle.Implemented, descriptor.Lifecycle);
        Assert.Equal(ToolSafetyClass.OsSideEffect, descriptor.SafetyClass);
        Assert.Contains(ToolNames.WindowsLaunchProcess, ToolContractManifest.SmokeRequiredToolNames);
        Assert.DoesNotContain(ToolContractManifest.Deferred, item => item.Name == ToolNames.WindowsLaunchProcess);
    }

    [Fact]
    public void WindowsOpenTargetIsImplementedAndSmokeRequired()
    {
        ToolDescriptor descriptor = Assert.Single(
            ToolContractManifest.Implemented,
            item => item.Name == ToolNames.WindowsOpenTarget);

        Assert.Equal(ToolLifecycle.Implemented, descriptor.Lifecycle);
        Assert.Equal(ToolSafetyClass.OsSideEffect, descriptor.SafetyClass);
        Assert.Contains(ToolNames.WindowsOpenTarget, ToolContractManifest.SmokeRequiredToolNames);
        Assert.DoesNotContain(ToolContractManifest.Deferred, item => item.Name == ToolNames.WindowsOpenTarget);
    }

    [Fact]
    public void WindowsInputIsImplementedAndSmokeRequiredAfterPackageE()
    {
        ToolDescriptor descriptor = Assert.Single(
            ToolContractManifest.Implemented,
            item => item.Name == ToolNames.WindowsInput);

        Assert.Equal(ToolLifecycle.Implemented, descriptor.Lifecycle);
        Assert.Equal(ToolSafetyClass.OsSideEffect, descriptor.SafetyClass);
        Assert.Contains(ToolNames.WindowsInput, ToolContractManifest.SmokeRequiredToolNames);
        Assert.DoesNotContain(ToolContractManifest.Deferred, item => item.Name == ToolNames.WindowsInput);
    }

    [Fact]
    public void DeferredActionDescriptorsUseExpectedExecutionPolicyMetadata()
    {
        ToolDescriptor clipboardGet = Assert.Single(ToolContractManifest.Deferred, item => item.Name == ToolNames.WindowsClipboardGet);
        ToolDescriptor clipboardSet = Assert.Single(ToolContractManifest.Deferred, item => item.Name == ToolNames.WindowsClipboardSet);
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
