// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Shell;

namespace WinBridge.Runtime.Tests;

public sealed class UiaSnapshotContractAndPolicyTests
{
    [Fact]
    public void UiaSnapshotRequestUsesExpectedDefaults()
    {
        UiaSnapshotRequest request = new();

        Assert.Equal(UiaSnapshotDefaults.Depth, request.Depth);
        Assert.Equal(256, request.MaxNodes);
    }

    [Fact]
    public void UiaSnapshotResultUsesExpectedDefaults()
    {
        UiaSnapshotResult result = new(UiaSnapshotStatusValues.Failed);

        Assert.Equal(UiaSnapshotStatusValues.Failed, result.Status);
        Assert.Equal(UiaSnapshotViewValues.Control, result.View);
        Assert.Equal(UiaSnapshotDefaults.Depth, result.RequestedDepth);
        Assert.Equal(UiaSnapshotDefaults.MaxNodes, result.RequestedMaxNodes);
        Assert.Equal(0, result.RealizedDepth);
        Assert.Equal(0, result.NodeCount);
        Assert.False(result.Truncated);
        Assert.False(result.DepthBoundaryReached);
        Assert.False(result.NodeBudgetBoundaryReached);
        Assert.Null(result.Root);
        Assert.Null(result.Window);
    }

    [Fact]
    public void UiaSnapshotStatusValuesExposeExpectedLiterals()
    {
        Assert.Equal("done", UiaSnapshotStatusValues.Done);
        Assert.Equal("failed", UiaSnapshotStatusValues.Failed);
        Assert.Equal("unsupported", UiaSnapshotStatusValues.Unsupported);
    }

    [Fact]
    public void ResolveUiaSnapshotTargetPrefersExplicitWindowOverAttachedAndActive()
    {
        WindowDescriptor explicitWindow = CreateWindow(hwnd: 101, title: "Explicit", isForeground: false);
        WindowDescriptor attachedWindow = CreateWindow(hwnd: 202, title: "Attached", isForeground: false);
        WindowDescriptor activeWindow = CreateWindow(hwnd: 303, title: "Active", isForeground: true);
        WindowTargetResolver resolver = new(new FakeWindowManager([explicitWindow, attachedWindow, activeWindow]));

        UiaSnapshotTargetResolution resolution = resolver.ResolveUiaSnapshotTarget(explicitWindow.Hwnd, attachedWindow);

        Assert.Equal(explicitWindow.Hwnd, resolution.Window?.Hwnd);
        Assert.Equal(UiaSnapshotTargetSourceValues.Explicit, resolution.Source);
        Assert.Null(resolution.FailureCode);
    }

    [Fact]
    public void ResolveUiaSnapshotTargetReturnsStaleExplicitWithoutFallback()
    {
        WindowDescriptor attachedWindow = CreateWindow(hwnd: 202, title: "Attached", isForeground: false);
        WindowDescriptor activeWindow = CreateWindow(hwnd: 303, title: "Active", isForeground: true);
        WindowTargetResolver resolver = new(new FakeWindowManager([attachedWindow, activeWindow]));

        UiaSnapshotTargetResolution resolution = resolver.ResolveUiaSnapshotTarget(explicitHwnd: 404, attachedWindow);

        Assert.Null(resolution.Window);
        Assert.Null(resolution.Source);
        Assert.Equal(UiaSnapshotTargetFailureValues.StaleExplicitTarget, resolution.FailureCode);
    }

    [Fact]
    public void ResolveUiaSnapshotTargetUsesAttachedWindowWhenExplicitIsMissing()
    {
        WindowDescriptor attachedWindow = CreateWindow(hwnd: 202, title: "Attached", isForeground: false);
        WindowDescriptor renamedLiveWindow = attachedWindow with { Title = "Attached renamed" };
        WindowDescriptor activeWindow = CreateWindow(hwnd: 303, title: "Active", isForeground: true);
        WindowTargetResolver resolver = new(new FakeWindowManager([renamedLiveWindow, activeWindow]));

        UiaSnapshotTargetResolution resolution = resolver.ResolveUiaSnapshotTarget(explicitHwnd: null, attachedWindow);

        Assert.Equal(attachedWindow.Hwnd, resolution.Window?.Hwnd);
        Assert.Equal(UiaSnapshotTargetSourceValues.Attached, resolution.Source);
        Assert.Null(resolution.FailureCode);
    }

    [Fact]
    public void ResolveUiaSnapshotTargetReturnsStaleAttachedWithoutFallback()
    {
        WindowDescriptor attachedWindow = CreateWindow(hwnd: 202, title: "Attached", isForeground: false);
        WindowDescriptor reusedLiveWindow = CreateWindow(hwnd: 202, title: "Different", threadId: 999, isForeground: false);
        WindowDescriptor activeWindow = CreateWindow(hwnd: 303, title: "Active", isForeground: true);
        WindowTargetResolver resolver = new(new FakeWindowManager([reusedLiveWindow, activeWindow]));

        UiaSnapshotTargetResolution resolution = resolver.ResolveUiaSnapshotTarget(explicitHwnd: null, attachedWindow);

        Assert.Null(resolution.Window);
        Assert.Null(resolution.Source);
        Assert.Equal(UiaSnapshotTargetFailureValues.StaleAttachedTarget, resolution.FailureCode);
    }

    [Fact]
    public void ResolveUiaSnapshotTargetUsesForegroundWindowWhenNoExplicitOrAttachedTargetExists()
    {
        WindowDescriptor activeWindow = CreateWindow(hwnd: 303, title: "Active", isForeground: true);
        WindowTargetResolver resolver = new(new FakeWindowManager([activeWindow]));

        UiaSnapshotTargetResolution resolution = resolver.ResolveUiaSnapshotTarget(explicitHwnd: null, attachedWindow: null);

        Assert.Equal(activeWindow.Hwnd, resolution.Window?.Hwnd);
        Assert.Equal(UiaSnapshotTargetSourceValues.Active, resolution.Source);
        Assert.Null(resolution.FailureCode);
    }

    [Fact]
    public void ResolveUiaSnapshotTargetReturnsMissingTargetWhenForegroundWindowDoesNotExist()
    {
        WindowTargetResolver resolver = new(new FakeWindowManager([]));

        UiaSnapshotTargetResolution resolution = resolver.ResolveUiaSnapshotTarget(explicitHwnd: null, attachedWindow: null);

        Assert.Null(resolution.Window);
        Assert.Null(resolution.Source);
        Assert.Equal(UiaSnapshotTargetFailureValues.MissingTarget, resolution.FailureCode);
    }

    [Fact]
    public void ResolveUiaSnapshotTargetReturnsMissingTargetWhenForegroundWindowCannotBeMapped()
    {
        WindowDescriptor unrelatedWindow = CreateWindow(hwnd: 101, title: "Other", isForeground: false);
        WindowTargetResolver resolver = new(new FakeWindowManager([unrelatedWindow]));

        UiaSnapshotTargetResolution resolution = resolver.ResolveUiaSnapshotTarget(explicitHwnd: null, attachedWindow: null);

        Assert.Null(resolution.Window);
        Assert.Null(resolution.Source);
        Assert.Equal(UiaSnapshotTargetFailureValues.MissingTarget, resolution.FailureCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ResolveUiaSnapshotTargetReturnsStaleExplicitForInvalidExplicitHwnd(long explicitHwnd)
    {
        WindowDescriptor attachedWindow = CreateWindow(hwnd: 202, title: "Attached", isForeground: false);
        WindowDescriptor activeWindow = CreateWindow(hwnd: 303, title: "Active", isForeground: true);
        WindowTargetResolver resolver = new(new FakeWindowManager([attachedWindow, activeWindow]));

        UiaSnapshotTargetResolution resolution = resolver.ResolveUiaSnapshotTarget(explicitHwnd, attachedWindow);

        Assert.Null(resolution.Window);
        Assert.Null(resolution.Source);
        Assert.Equal(UiaSnapshotTargetFailureValues.StaleExplicitTarget, resolution.FailureCode);
    }

    [Fact]
    public void ResolveUiaSnapshotTargetDeduplicatesForegroundCandidatesByHwnd()
    {
        WindowDescriptor firstCandidate = CreateWindow(hwnd: 404, title: "First", isForeground: true);
        WindowDescriptor duplicateCandidate = CreateWindow(hwnd: 404, title: "Second", isForeground: true, threadId: 999);
        WindowTargetResolver resolver = new(new FakeWindowManager([firstCandidate, duplicateCandidate]));

        UiaSnapshotTargetResolution resolution = resolver.ResolveUiaSnapshotTarget(explicitHwnd: null, attachedWindow: null);

        Assert.Equal(404, resolution.Window?.Hwnd);
        Assert.Equal(UiaSnapshotTargetSourceValues.Active, resolution.Source);
        Assert.Null(resolution.FailureCode);
    }

    [Fact]
    public void ResolveUiaSnapshotTargetReturnsAmbiguousActiveTargetWhenSnapshotContainsDifferentForegroundCandidates()
    {
        WindowDescriptor firstCandidate = CreateWindow(hwnd: 404, title: "First", isForeground: true);
        WindowDescriptor secondCandidate = CreateWindow(hwnd: 405, title: "Second", isForeground: true, threadId: 999);
        WindowTargetResolver resolver = new(new FakeWindowManager([firstCandidate, secondCandidate]));

        UiaSnapshotTargetResolution resolution = resolver.ResolveUiaSnapshotTarget(explicitHwnd: null, attachedWindow: null);

        Assert.Null(resolution.Window);
        Assert.Null(resolution.Source);
        Assert.Equal(UiaSnapshotTargetFailureValues.AmbiguousActiveTarget, resolution.FailureCode);
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
