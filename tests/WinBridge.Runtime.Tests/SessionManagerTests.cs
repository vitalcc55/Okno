// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

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
            ProcessId: 1000,
            ThreadId: 2000,
            ClassName: "TestWindow",
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
