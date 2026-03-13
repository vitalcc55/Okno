using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Capture;

namespace WinBridge.Runtime.Tests;

public sealed class CaptureBackendSelectorTests
{
    [Theory]
    [InlineData(CaptureScope.Window, true, 0)]
    [InlineData(CaptureScope.Desktop, true, 0)]
    [InlineData(CaptureScope.Desktop, false, 1)]
    [InlineData(CaptureScope.Window, false, 2)]
    public void SelectReturnsExpectedBackend(
        CaptureScope scope,
        bool isWindowsGraphicsCaptureSupported,
        int expected)
    {
        int actual = (int)CaptureBackendSelector.Select(scope, isWindowsGraphicsCaptureSupported);

        Assert.Equal(expected, actual);
    }
}
