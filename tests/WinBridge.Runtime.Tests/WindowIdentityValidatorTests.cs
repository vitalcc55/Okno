using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Shell;

namespace WinBridge.Runtime.Tests;

public sealed class WindowIdentityValidatorTests
{
    [Fact]
    public void TryValidateStableIdentityReturnsTrueForCompleteSnapshot()
    {
        bool result = WindowIdentityValidator.TryValidateStableIdentity(CreateWindow(), out string? reason);

        Assert.True(result);
        Assert.Null(reason);
    }

    [Fact]
    public void TryValidateStableIdentityReturnsFalseWhenProcessIdIsMissing()
    {
        bool result = WindowIdentityValidator.TryValidateStableIdentity(
            CreateWindow() with { ProcessId = null },
            out string? reason);

        Assert.False(result);
        Assert.Contains("ProcessId", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void MatchesStableIdentityReturnsFalseWhenThreadIdDiffers()
    {
        WindowDescriptor expected = CreateWindow() with { ThreadId = 456 };
        WindowDescriptor live = CreateWindow() with { ThreadId = 999 };

        bool result = WindowIdentityValidator.MatchesStableIdentity(live, expected);

        Assert.False(result);
    }

    [Fact]
    public void MatchesStableIdentityReturnsFalseWhenExpectedSnapshotIsIncomplete()
    {
        WindowDescriptor expected = CreateWindow() with { ClassName = null };
        WindowDescriptor live = CreateWindow();

        bool result = WindowIdentityValidator.MatchesStableIdentity(live, expected);

        Assert.False(result);
    }

    private static WindowDescriptor CreateWindow() =>
        new(
            Hwnd: 42,
            Title: "Window",
            ProcessName: "okno-tests",
            ProcessId: 123,
            ThreadId: 456,
            ClassName: "OknoWindow",
            Bounds: new Bounds(10, 20, 210, 220),
            IsForeground: true,
            IsVisible: true,
            WindowState: WindowStateValues.Normal,
            MonitorId: "display-source:0000000100000000:1",
            MonitorFriendlyName: "Primary monitor");
}
