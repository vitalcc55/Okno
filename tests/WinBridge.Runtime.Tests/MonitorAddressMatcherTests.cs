using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Display;

namespace WinBridge.Runtime.Tests;

public sealed class MonitorAddressMatcherTests
{
    [Fact]
    public void MatchesReturnsTrueForExactStrongIdentity()
    {
        MonitorDescriptor descriptor = CreateDescriptor(
            monitorId: "display-source:abcd12340000beef:7",
            gdiDeviceName: @"\\.\DISPLAY2");

        bool result = MonitorAddressMatcher.Matches("display-source:abcd12340000beef:7", descriptor);

        Assert.True(result);
    }

    [Fact]
    public void MatchesReturnsTrueForGdiFallbackAgainstStrongIdentityDescriptor()
    {
        MonitorDescriptor descriptor = CreateDescriptor(
            monitorId: "display-source:abcd12340000beef:7",
            gdiDeviceName: @"\\.\DISPLAY2");

        bool result = MonitorAddressMatcher.Matches("gdi:display2", descriptor);

        Assert.True(result);
    }

    [Fact]
    public void MatchesReturnsFalseForStaleStrongIdentityAgainstFallbackDescriptor()
    {
        MonitorDescriptor descriptor = CreateDescriptor(
            monitorId: "gdi:display2",
            gdiDeviceName: @"\\.\DISPLAY2");

        bool result = MonitorAddressMatcher.Matches("display-source:abcd12340000beef:7", descriptor);

        Assert.False(result);
    }

    [Fact]
    public void MatchesReturnsFalseForDifferentGdiAddress()
    {
        MonitorDescriptor descriptor = CreateDescriptor(
            monitorId: "display-source:abcd12340000beef:7",
            gdiDeviceName: @"\\.\DISPLAY2");

        bool result = MonitorAddressMatcher.Matches("gdi:display3", descriptor);

        Assert.False(result);
    }

    private static MonitorDescriptor CreateDescriptor(string monitorId, string gdiDeviceName) =>
        new(
            MonitorId: monitorId,
            FriendlyName: "Monitor",
            GdiDeviceName: gdiDeviceName,
            Bounds: new Bounds(0, 0, 1920, 1080),
            WorkArea: new Bounds(0, 0, 1920, 1040),
            IsPrimary: true);
}
