using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Display;

namespace WinBridge.Runtime.Tests;

public sealed class DisplayIdentityFailureSemanticsTests
{
    [Fact]
    public void IdentityBreakingStagesHaveHigherPriorityThanAuxiliaryDegradation()
    {
        Assert.True(
            DisplayIdentityFailureSemantics.GetPriority(DisplayIdentityFailureStageValues.GetSourceName)
            > DisplayIdentityFailureSemantics.GetPriority(DisplayIdentityFailureStageValues.GetTargetName));

        Assert.True(
            DisplayIdentityFailureSemantics.GetPriority(DisplayIdentityFailureStageValues.GetMonitorInfo)
            > DisplayIdentityFailureSemantics.GetPriority(DisplayIdentityFailureStageValues.GetTargetName));

        Assert.True(
            DisplayIdentityFailureSemantics.GetPriority(DisplayIdentityFailureStageValues.QueryDisplayConfig)
            > DisplayIdentityFailureSemantics.GetPriority(DisplayIdentityFailureStageValues.GetMonitorInfo));
    }

    [Fact]
    public void CoverageGapIsIdentityBreaking()
    {
        Assert.True(DisplayIdentityFailureSemantics.IsIdentityBreaking(DisplayIdentityFailureStageValues.CoverageGap));
    }
}
