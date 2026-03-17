using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Capture;

namespace WinBridge.Runtime.Tests;

public sealed class WgcAcquisitionFailurePolicyTests
{
    [Fact]
    public void EvaluateReturnsDesktopFallbackForDesktopScope()
    {
        WgcAcquisitionFailureAction action = WgcAcquisitionFailurePolicy.Evaluate(CaptureScope.Desktop);

        Assert.Equal(WgcAcquisitionFailureAction.FallbackToDesktopGdi, action);
    }

    [Fact]
    public void EvaluateReturnsToolErrorForWindowScope()
    {
        WgcAcquisitionFailureAction action = WgcAcquisitionFailurePolicy.Evaluate(CaptureScope.Window);

        Assert.Equal(WgcAcquisitionFailureAction.ThrowToolError, action);
    }
}
