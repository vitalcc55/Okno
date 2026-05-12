// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Input;

namespace WinBridge.Runtime.Tests;

public sealed class InputTargetSecurityProbeCacheTests
{
    [Fact]
    public void ProbeCachesResolvedSecurityForRepeatedProcessId()
    {
        CountingInputPlatform platform = new(
            CreateTargetSecurity(processId: 321));
        InputTargetSecurityProbeCache cache = new(platform);
        WindowDescriptor liveWindow = CreateWindow(processId: 321);

        InputTargetSecurityInfo first = cache.Probe(liveWindow);
        InputTargetSecurityInfo second = cache.Probe(liveWindow);

        Assert.Same(first, second);
        Assert.Equal(1, platform.ProbeCalls);
    }

    [Fact]
    public void ProbeDoesNotCacheUnresolvedSecurityInfo()
    {
        CountingInputPlatform platform = new(
            CreateTargetSecurity(processId: null),
            CreateTargetSecurity(processId: null));
        InputTargetSecurityProbeCache cache = new(platform);
        WindowDescriptor liveWindow = CreateWindow(processId: 321);

        InputTargetSecurityInfo first = cache.Probe(liveWindow);
        InputTargetSecurityInfo second = cache.Probe(liveWindow);

        Assert.NotSame(first, second);
        Assert.Equal(2, platform.ProbeCalls);
    }

    [Fact]
    public void ProbeRequeriesPlatformWhenLiveTargetProcessChanges()
    {
        CountingInputPlatform platform = new(
            CreateTargetSecurity(processId: 321),
            CreateTargetSecurity(processId: 654));
        InputTargetSecurityProbeCache cache = new(platform);

        _ = cache.Probe(CreateWindow(processId: 321));
        _ = cache.Probe(CreateWindow(hwnd: 202, processId: 654));

        Assert.Equal(2, platform.ProbeCalls);
    }

    [Fact]
    public void ProbeCachesByResolvedProcessIdEvenWhenInitialHintIsMissing()
    {
        CountingInputPlatform platform = new(
            CreateTargetSecurity(processId: 321));
        InputTargetSecurityProbeCache cache = new(platform);

        InputTargetSecurityInfo first = cache.Probe(CreateWindow(processId: null));
        InputTargetSecurityInfo second = cache.Probe(CreateWindow(hwnd: 202, processId: 321));

        Assert.Same(first, second);
        Assert.Equal(1, platform.ProbeCalls);
    }

    private static WindowDescriptor CreateWindow(long hwnd = 101, int? processId = 321) =>
        new(
            Hwnd: hwnd,
            Title: "Target",
            ProcessName: "target",
            ProcessId: processId,
            ThreadId: processId,
            ClassName: "TargetWindowClass",
            Bounds: new Bounds(100, 200, 420, 560),
            IsForeground: true,
            IsVisible: true,
            EffectiveDpi: 96,
            DpiScale: 1.0,
            WindowState: WindowStateValues.Normal);

    private static InputTargetSecurityInfo CreateTargetSecurity(int? processId) =>
        new(
            ProcessId: processId,
            SessionId: 1,
            SessionResolved: true,
            IntegrityLevel: InputIntegrityLevel.Medium,
            IntegrityResolved: true,
            Reason: null);

    private sealed class CountingInputPlatform(params InputTargetSecurityInfo[] probeResults) : IInputPlatform
    {
        private readonly Queue<InputTargetSecurityInfo> queuedProbeResults = new(probeResults);

        public int ProbeCalls { get; private set; }

        public InputProcessSecurityContext ProbeCurrentProcessSecurity() => throw new NotSupportedException();

        public InputTargetSecurityInfo ProbeTargetSecurity(long hwnd, int? processIdHint)
        {
            ProbeCalls++;
            return queuedProbeResults.Count > 0
                ? queuedProbeResults.Dequeue()
                : throw new InvalidOperationException("No more probe results were configured.");
        }

        public InputPointerSideEffectBoundaryResult ValidatePointerSideEffectBoundary(WindowDescriptor admittedTargetWindow) => throw new NotSupportedException();

        public bool TrySetCursorPosition(InputPoint screenPoint) => throw new NotSupportedException();

        public bool TryGetCursorPosition(out InputPoint screenPoint) => throw new NotSupportedException();

        public InputClickDispatchResult DispatchClick(InputClickDispatchContext context) => throw new NotSupportedException();

        public InputDispatchResult DispatchText(InputTextDispatchContext context) => throw new NotSupportedException();

        public InputDispatchResult DispatchKeypress(InputKeypressDispatchContext context) => throw new NotSupportedException();

        public InputDispatchResult DispatchScroll(InputScrollDispatchContext context) => throw new NotSupportedException();

        public InputDispatchResult DispatchDrag(InputDragDispatchContext context) => throw new NotSupportedException();
    }
}
