using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Shell;

namespace WinBridge.Runtime.Tests;

public sealed class WaitContractAndPolicyTests
{
    [Fact]
    public void WaitRequestUsesExpectedDefaults()
    {
        WaitRequest request = new(WaitConditionValues.ElementExists);

        Assert.Equal(WaitConditionValues.ElementExists, request.Condition);
        Assert.Null(request.Selector);
        Assert.Equal(WaitDefaults.TimeoutMs, request.TimeoutMs);
    }

    [Fact]
    public void WaitResultUsesExpectedDefaults()
    {
        WaitResult result = new(WaitStatusValues.Failed, WaitConditionValues.VisualChanged);

        Assert.Equal(WaitStatusValues.Failed, result.Status);
        Assert.Equal(WaitConditionValues.VisualChanged, result.Condition);
        Assert.Equal(WaitDefaults.TimeoutMs, result.TimeoutMs);
        Assert.Equal(0, result.ElapsedMs);
        Assert.Equal(0, result.AttemptCount);
        Assert.Null(result.Reason);
    }

    [Fact]
    public void WaitStatusValuesExposeExpectedLiterals()
    {
        Assert.Equal("done", WaitStatusValues.Done);
        Assert.Equal("timeout", WaitStatusValues.Timeout);
        Assert.Equal("ambiguous", WaitStatusValues.Ambiguous);
        Assert.Equal("failed", WaitStatusValues.Failed);
    }

    [Fact]
    public void WaitConditionValuesExposeExpectedLiterals()
    {
        Assert.Equal("active_window_matches", WaitConditionValues.ActiveWindowMatches);
        Assert.Equal("element_exists", WaitConditionValues.ElementExists);
        Assert.Equal("element_gone", WaitConditionValues.ElementGone);
        Assert.Equal("text_appears", WaitConditionValues.TextAppears);
        Assert.Equal("visual_changed", WaitConditionValues.VisualChanged);
        Assert.Equal("focus_is", WaitConditionValues.FocusIs);
    }

    [Fact]
    public void ResolveWaitTargetPrefersExplicitWindowOverAttachedAndActive()
    {
        WindowDescriptor explicitWindow = CreateWindow(hwnd: 101, title: "Explicit", isForeground: false);
        WindowDescriptor attachedWindow = CreateWindow(hwnd: 202, title: "Attached", isForeground: false);
        WindowDescriptor activeWindow = CreateWindow(hwnd: 303, title: "Active", isForeground: true);
        WindowTargetResolver resolver = new(new FakeWindowManager([explicitWindow, attachedWindow, activeWindow]));

        WaitTargetResolution resolution = resolver.ResolveWaitTarget(explicitWindow.Hwnd, attachedWindow);

        Assert.Equal(explicitWindow.Hwnd, resolution.Window?.Hwnd);
        Assert.Equal(WaitTargetSourceValues.Explicit, resolution.Source);
        Assert.Null(resolution.FailureCode);
    }

    [Fact]
    public void ResolveWaitTargetReturnsStaleExplicitWithoutFallback()
    {
        WindowDescriptor attachedWindow = CreateWindow(hwnd: 202, title: "Attached", isForeground: false);
        WindowDescriptor activeWindow = CreateWindow(hwnd: 303, title: "Active", isForeground: true);
        WindowTargetResolver resolver = new(new FakeWindowManager([attachedWindow, activeWindow]));

        WaitTargetResolution resolution = resolver.ResolveWaitTarget(explicitHwnd: 404, attachedWindow);

        Assert.Null(resolution.Window);
        Assert.Null(resolution.Source);
        Assert.Equal(WaitTargetFailureValues.StaleExplicitTarget, resolution.FailureCode);
    }

    [Fact]
    public void ResolveWaitTargetUsesAttachedWindowWhenExplicitIsMissing()
    {
        WindowDescriptor attachedWindow = CreateWindow(hwnd: 202, title: "Attached", isForeground: false);
        WindowDescriptor renamedLiveWindow = attachedWindow with { Title = "Attached renamed" };
        WindowDescriptor activeWindow = CreateWindow(hwnd: 303, title: "Active", isForeground: true);
        WindowTargetResolver resolver = new(new FakeWindowManager([renamedLiveWindow, activeWindow]));

        WaitTargetResolution resolution = resolver.ResolveWaitTarget(explicitHwnd: null, attachedWindow);

        Assert.Equal(attachedWindow.Hwnd, resolution.Window?.Hwnd);
        Assert.Equal(WaitTargetSourceValues.Attached, resolution.Source);
        Assert.Null(resolution.FailureCode);
    }

    [Fact]
    public void ResolveWaitTargetReturnsStaleAttachedWithoutFallback()
    {
        WindowDescriptor attachedWindow = CreateWindow(hwnd: 202, title: "Attached", isForeground: false);
        WindowDescriptor reusedLiveWindow = CreateWindow(hwnd: 202, title: "Different", threadId: 999, isForeground: false);
        WindowDescriptor activeWindow = CreateWindow(hwnd: 303, title: "Active", isForeground: true);
        WindowTargetResolver resolver = new(new FakeWindowManager([reusedLiveWindow, activeWindow]));

        WaitTargetResolution resolution = resolver.ResolveWaitTarget(explicitHwnd: null, attachedWindow);

        Assert.Null(resolution.Window);
        Assert.Null(resolution.Source);
        Assert.Equal(WaitTargetFailureValues.StaleAttachedTarget, resolution.FailureCode);
    }

    [Fact]
    public void ResolveWaitTargetReturnsMissingTargetWhenForegroundWindowDoesNotExist()
    {
        WindowTargetResolver resolver = new(new FakeWindowManager([]));

        WaitTargetResolution resolution = resolver.ResolveWaitTarget(explicitHwnd: null, attachedWindow: null);

        Assert.Null(resolution.Window);
        Assert.Null(resolution.Source);
        Assert.Equal(WaitTargetFailureValues.MissingTarget, resolution.FailureCode);
    }

    [Fact]
    public void ResolveWaitTargetReturnsAmbiguousActiveTargetWhenSnapshotContainsDifferentForegroundCandidates()
    {
        WindowDescriptor firstCandidate = CreateWindow(hwnd: 404, title: "First", isForeground: true);
        WindowDescriptor secondCandidate = CreateWindow(hwnd: 405, title: "Second", isForeground: true, threadId: 999);
        WindowTargetResolver resolver = new(new FakeWindowManager([firstCandidate, secondCandidate]));

        WaitTargetResolution resolution = resolver.ResolveWaitTarget(explicitHwnd: null, attachedWindow: null);

        Assert.Null(resolution.Window);
        Assert.Null(resolution.Source);
        Assert.Equal(WaitTargetFailureValues.AmbiguousActiveTarget, resolution.FailureCode);
    }

    private static WindowDescriptor CreateWindow(
        long hwnd,
        string title,
        bool isForeground,
        int processId = 123,
        int threadId = 456,
        string className = "OknoWindow") =>
        new(
            Hwnd: hwnd,
            Title: title,
            ProcessName: "okno-tests",
            ProcessId: processId,
            ThreadId: threadId,
            ClassName: className,
            Bounds: new Bounds(10, 20, 210, 220),
            IsForeground: isForeground,
            IsVisible: true,
            WindowState: WindowStateValues.Normal,
            MonitorId: "display-source:0000000100000000:1",
            MonitorFriendlyName: "Primary monitor");

    private sealed class FakeWindowManager(IReadOnlyList<WindowDescriptor> windows) : IWindowManager
    {
        public IReadOnlyList<WindowDescriptor> ListWindows(bool includeInvisible = false) => windows;

        public WindowDescriptor? FindWindow(WindowSelector selector)
        {
            selector.Validate();
            return windows.FirstOrDefault(window => window.Hwnd == selector.Hwnd);
        }

        public bool TryFocus(long hwnd) => windows.Any(window => window.Hwnd == hwnd);
    }
}
