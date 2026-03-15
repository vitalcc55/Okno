using WinBridge.Runtime.Windows.Display;

namespace WinBridge.Runtime.Tests;

public sealed class MonitorIdFormatterTests
{
    [Fact]
    public void FromDisplayConfigBuildsStableMonitorId()
    {
        string monitorId = MonitorIdFormatter.FromDisplayConfig(unchecked((int)0xABCD1234), 0x0000BEEF, 7);

        Assert.Equal("displayconfig:abcd12340000beef:7", monitorId);
    }

    [Fact]
    public void FromGdiDeviceNameBuildsNormalizedFallbackId()
    {
        string monitorId = MonitorIdFormatter.FromGdiDeviceName(@"\\.\DISPLAY2");

        Assert.Equal("gdi:display2", monitorId);
    }
}
