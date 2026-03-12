using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Session;

namespace WinBridge.Runtime.Tests;

public sealed class SessionManagerTests
{
    [Fact]
    public void AttachChangesSnapshotOnlyWhenWindowChanges()
    {
        string root = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        SessionContext context = new("run-003");
        InMemorySessionManager manager = new(TimeProvider.System, context);
        WindowDescriptor firstWindow = new(
            Hwnd: 100,
            Title: "First",
            ProcessName: "proc",
            Bounds: new Bounds(0, 0, 640, 480),
            IsForeground: true,
            IsVisible: true);

        SessionMutation firstAttach = manager.Attach(firstWindow, "hwnd");
        SessionMutation repeatedAttach = manager.Attach(firstWindow, "hwnd");

        Assert.True(firstAttach.Changed);
        Assert.False(repeatedAttach.Changed);
        Assert.Equal(100, manager.GetSnapshot().AttachedWindow?.Window.Hwnd);
    }
}
