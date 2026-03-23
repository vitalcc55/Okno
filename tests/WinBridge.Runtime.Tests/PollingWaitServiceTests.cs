using System.Diagnostics;
using System.Text.Json;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Waiting;
using WinBridge.Runtime.Windows.Capture;
using WinBridge.Runtime.Windows.Shell;
using WinBridge.Runtime.Windows.UIA;

namespace WinBridge.Runtime.Tests;

public sealed class PollingWaitServiceTests
{
    [Fact]
    public async Task WaitAsyncReturnsFailedForInvalidRequestAndStillWritesArtifact()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateAuditLogOptions(root, "run-wait-invalid");
        PollingWaitService service = CreateService(
            options,
            new FakeWindowManager([CreateWindow(hwnd: 501, isForeground: false)]),
            new FakeWindowTargetResolver(window => window),
            new SequenceWaitProbe());

        WaitResult result = await service.WaitAsync(
            CreateTarget(CreateWindow(hwnd: 501, isForeground: false)),
            new WaitRequest(
                WaitConditionValues.TextAppears,
                new WaitElementSelector(AutomationId: "SearchBox")),
            CancellationToken.None);

        Assert.Equal(WaitStatusValues.Failed, result.Status);
        Assert.NotNull(result.ArtifactPath);
        Assert.True(File.Exists(result.ArtifactPath));

        using JsonDocument artifact = JsonDocument.Parse(await File.ReadAllTextAsync(result.ArtifactPath));
        Assert.Equal("failed", artifact.RootElement.GetProperty("result").GetProperty("status").GetString());

        string[] eventLines = await File.ReadAllLinesAsync(options.EventsPath);
        Assert.Single(eventLines);
        Assert.Contains("\"event_name\":\"wait.runtime.completed\"", eventLines[0], StringComparison.Ordinal);
        Assert.Contains("\"failure_stage\":\"request_validation\"", eventLines[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task WaitAsyncReturnsDoneForStableForegroundTarget()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateAuditLogOptions(root, "run-wait-active-done");
        WindowDescriptor targetWindow = CreateWindow(hwnd: 601, isForeground: true);
        PollingWaitService service = CreateService(
            options,
            new FakeWindowManager([targetWindow]),
            new FakeWindowTargetResolver(window => targetWindow),
            new SequenceWaitProbe());

        WaitResult result = await service.WaitAsync(
            CreateTarget(targetWindow, WaitTargetSourceValues.Attached),
            new WaitRequest(WaitConditionValues.ActiveWindowMatches, TimeoutMs: 50),
            CancellationToken.None);

        Assert.Equal(WaitStatusValues.Done, result.Status);
        Assert.Equal(2, result.AttemptCount);
        Assert.Equal(WaitTargetSourceValues.Attached, result.TargetSource);
        Assert.NotNull(result.Window);
        Assert.True(result.LastObserved?.TargetIsForeground);
        Assert.NotNull(result.ArtifactPath);
    }

    [Fact]
    public async Task WaitAsyncReturnsAmbiguousForMultipleForegroundWindows()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateAuditLogOptions(root, "run-wait-active-ambiguous");
        WindowDescriptor targetWindow = CreateWindow(hwnd: 602, isForeground: true);
        PollingWaitService service = CreateService(
            options,
            new FakeWindowManager(
            [
                targetWindow,
                CreateWindow(hwnd: 603, isForeground: true, threadId: 999),
            ]),
            new FakeWindowTargetResolver(window => targetWindow),
            new SequenceWaitProbe());

        WaitResult result = await service.WaitAsync(
            CreateTarget(targetWindow, WaitTargetSourceValues.Attached),
            new WaitRequest(WaitConditionValues.ActiveWindowMatches, TimeoutMs: 50),
            CancellationToken.None);

        Assert.Equal(WaitStatusValues.Ambiguous, result.Status);
        Assert.Equal(1, result.AttemptCount);
        Assert.Equal(2, result.LastObserved?.MatchCount);
        Assert.NotNull(result.ArtifactPath);
    }

    [Fact]
    public async Task WaitAsyncReturnsFailedWhenResolvedTargetBecomesStale()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateAuditLogOptions(root, "run-wait-stale");
        WindowDescriptor targetWindow = CreateWindow(hwnd: 604, isForeground: false);
        PollingWaitService service = CreateService(
            options,
            new FakeWindowManager([]),
            new FakeWindowTargetResolver(_ => null),
            new SequenceWaitProbe());

        WaitResult result = await service.WaitAsync(
            CreateTarget(targetWindow, WaitTargetSourceValues.Attached),
            new WaitRequest(WaitConditionValues.ActiveWindowMatches, TimeoutMs: 50),
            CancellationToken.None);

        Assert.Equal(WaitStatusValues.Failed, result.Status);
        Assert.Equal(WaitTargetFailureValues.StaleAttachedTarget, result.TargetFailureCode);
        Assert.Equal(1, result.AttemptCount);
    }

    [Fact]
    public async Task WaitAsyncReturnsAmbiguousWhenTargetResolutionIsAmbiguousActive()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateAuditLogOptions(root, "run-wait-target-ambiguous");
        PollingWaitService service = CreateService(
            options,
            new FakeWindowManager([]),
            new FakeWindowTargetResolver(_ => throw new NotSupportedException("Should not resolve live identity for missing target.")),
            new SequenceWaitProbe());

        WaitResult result = await service.WaitAsync(
            CreateTarget(window: null, source: null, failureCode: WaitTargetFailureValues.AmbiguousActiveTarget),
            new WaitRequest(WaitConditionValues.ActiveWindowMatches, TimeoutMs: 50),
            CancellationToken.None);

        Assert.Equal(WaitStatusValues.Ambiguous, result.Status);
        Assert.Equal(WaitTargetFailureValues.AmbiguousActiveTarget, result.TargetFailureCode);
        Assert.NotNull(result.ArtifactPath);
    }

    [Fact]
    public async Task WaitAsyncReturnsDoneForElementGoneAfterStableRecheck()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateAuditLogOptions(root, "run-wait-gone");
        WindowDescriptor targetWindow = CreateWindow(hwnd: 605, isForeground: false);
        SequenceWaitProbe probe = new(
        [
            new UiAutomationWaitProbeResult
            {
                Window = CreateObservedWindow(targetWindow),
                Matches = [],
            },
            new UiAutomationWaitProbeResult
            {
                Window = CreateObservedWindow(targetWindow),
                Matches = [],
            },
        ]);
        PollingWaitService service = CreateService(
            options,
            new FakeWindowManager([targetWindow]),
            new FakeWindowTargetResolver(_ => targetWindow),
            probe);

        WaitResult result = await service.WaitAsync(
            CreateTarget(targetWindow, WaitTargetSourceValues.Attached),
            new WaitRequest(
                WaitConditionValues.ElementGone,
                new WaitElementSelector(AutomationId: "LoadingSpinner"),
                TimeoutMs: 50),
            CancellationToken.None);

        Assert.Equal(WaitStatusValues.Done, result.Status);
        Assert.Equal(2, result.AttemptCount);
        Assert.Null(result.MatchedElement);
    }

    [Fact]
    public async Task WaitAsyncReturnsAmbiguousForMultipleUiMatches()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateAuditLogOptions(root, "run-wait-uia-ambiguous");
        WindowDescriptor targetWindow = CreateWindow(hwnd: 606, isForeground: false);
        SequenceWaitProbe probe = new(
        [
            new UiAutomationWaitProbeResult
            {
                Window = CreateObservedWindow(targetWindow),
                Matches =
                [
                    CreateElement("rid:1"),
                    CreateElement("rid:2"),
                ],
            },
        ]);
        PollingWaitService service = CreateService(
            options,
            new FakeWindowManager([targetWindow]),
            new FakeWindowTargetResolver(_ => targetWindow),
            probe);

        WaitResult result = await service.WaitAsync(
            CreateTarget(targetWindow, WaitTargetSourceValues.Attached),
            new WaitRequest(
                WaitConditionValues.ElementExists,
                new WaitElementSelector(Name: "Save"),
                TimeoutMs: 50),
            CancellationToken.None);

        Assert.Equal(WaitStatusValues.Ambiguous, result.Status);
        Assert.Equal(1, result.AttemptCount);
        Assert.Equal(2, result.LastObserved?.MatchCount);
    }

    [Fact]
    public async Task WaitAsyncReturnsDoneForTextAppearsAndPreservesMatchedSource()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateAuditLogOptions(root, "run-wait-text");
        WindowDescriptor targetWindow = CreateWindow(hwnd: 607, isForeground: false);
        UiaElementSnapshot element = CreateElement("rid:7");
        SequenceWaitProbe probe = new(
        [
            new UiAutomationWaitProbeResult
            {
                Window = CreateObservedWindow(targetWindow),
                Matches = [element],
            },
            new UiAutomationWaitProbeResult
            {
                Window = CreateObservedWindow(targetWindow),
                Matches = [element],
                MatchedText = "Ready to submit",
                MatchedTextSource = "value_pattern",
            },
            new UiAutomationWaitProbeResult
            {
                Window = CreateObservedWindow(targetWindow),
                Matches = [element],
                MatchedText = "Ready to submit",
                MatchedTextSource = "value_pattern",
            },
        ]);
        PollingWaitService service = CreateService(
            options,
            new FakeWindowManager([targetWindow]),
            new FakeWindowTargetResolver(_ => targetWindow),
            probe);

        WaitResult result = await service.WaitAsync(
            CreateTarget(targetWindow, WaitTargetSourceValues.Attached),
            new WaitRequest(
                WaitConditionValues.TextAppears,
                new WaitElementSelector(AutomationId: "SubmitButton"),
                ExpectedText: "Ready",
                TimeoutMs: 50),
            CancellationToken.None);

        Assert.Equal(WaitStatusValues.Done, result.Status);
        Assert.Equal(3, result.AttemptCount);
        Assert.Equal("value_pattern", result.LastObserved?.MatchedTextSource);
        Assert.Equal(element.ElementId, result.MatchedElement?.ElementId);
    }

    [Fact]
    public async Task WaitAsyncReturnsDoneForStableFocusedElement()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateAuditLogOptions(root, "run-wait-focus");
        WindowDescriptor targetWindow = CreateWindow(hwnd: 6071, isForeground: false);
        UiaElementSnapshot focusedElement = CreateElement("rid:1.2;path:0/1")
            with
            {
                AutomationId = "SearchBox",
                ControlType = "edit",
                HasKeyboardFocus = true,
            };
        SequenceWaitProbe probe = new(
        [
            new UiAutomationWaitProbeResult
            {
                Window = CreateObservedWindow(targetWindow),
                Matches = [focusedElement],
            },
            new UiAutomationWaitProbeResult
            {
                Window = CreateObservedWindow(targetWindow),
                Matches = [focusedElement],
            },
        ]);
        PollingWaitService service = CreateService(
            options,
            new FakeWindowManager([targetWindow]),
            new FakeWindowTargetResolver(_ => targetWindow),
            probe);

        WaitResult result = await service.WaitAsync(
            CreateTarget(targetWindow, WaitTargetSourceValues.Attached),
            new WaitRequest(
                WaitConditionValues.FocusIs,
                new WaitElementSelector(AutomationId: "SearchBox", ControlType: "edit"),
                TimeoutMs: 50),
            CancellationToken.None);

        Assert.Equal(WaitStatusValues.Done, result.Status);
        Assert.Equal(2, result.AttemptCount);
        Assert.Equal(focusedElement.ElementId, result.MatchedElement?.ElementId);
    }

    [Fact]
    public async Task WaitAsyncReturnsTimeoutWhenFocusedElementDoesNotStabilize()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateAuditLogOptions(root, "run-wait-focus-timeout");
        WindowDescriptor targetWindow = CreateWindow(hwnd: 6072, isForeground: false);
        PollingWaitService service = CreateService(
            options,
            new FakeWindowManager([targetWindow]),
            new FakeWindowTargetResolver(_ => targetWindow),
            new SequenceWaitProbe(),
            new WaitOptions(TimeSpan.FromMilliseconds(1)));

        WaitResult result = await service.WaitAsync(
            CreateTarget(targetWindow, WaitTargetSourceValues.Attached),
            new WaitRequest(
                WaitConditionValues.FocusIs,
                new WaitElementSelector(AutomationId: "SearchBox"),
                TimeoutMs: 15),
            CancellationToken.None);

        Assert.Equal(WaitStatusValues.Timeout, result.Status);
        Assert.NotNull(result.ArtifactPath);
        Assert.Null(result.MatchedElement);
    }

    [Fact]
    public async Task WaitAsyncReturnsFailedForFocusProbeRuntimeFailure()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateAuditLogOptions(root, "run-wait-focus-failed");
        WindowDescriptor targetWindow = CreateWindow(hwnd: 6073, isForeground: false);
        SequenceWaitProbe probe = new(
        [
            new UiAutomationWaitProbeResult
            {
                Reason = "focus provider failure",
                FailureStage = "worker_process",
            },
        ]);
        PollingWaitService service = CreateService(
            options,
            new FakeWindowManager([targetWindow]),
            new FakeWindowTargetResolver(_ => targetWindow),
            probe);

        WaitResult result = await service.WaitAsync(
            CreateTarget(targetWindow, WaitTargetSourceValues.Attached),
            new WaitRequest(
                WaitConditionValues.FocusIs,
                new WaitElementSelector(AutomationId: "SearchBox"),
                TimeoutMs: 50),
            CancellationToken.None);

        Assert.Equal(WaitStatusValues.Failed, result.Status);
        Assert.Equal("focus provider failure", result.Reason);
    }

    [Fact]
    public async Task WaitAsyncReturnsDoneForVisualChangedAfterStableRecheck()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateAuditLogOptions(root, "run-wait-visual");
        WindowDescriptor targetWindow = CreateWindow(hwnd: 6074, isForeground: false);
        SequenceVisualProbe visualProbe = new(
        [
            CreateVisualFrame(targetWindow, changedCells: 0),
            CreateVisualFrame(targetWindow, changedCells: 16),
            CreateVisualFrame(targetWindow, changedCells: 16),
        ]);
        PollingWaitService service = CreateService(
            options,
            new FakeWindowManager([targetWindow]),
            new FakeWindowTargetResolver(_ => targetWindow),
            new SequenceWaitProbe(),
            visualProbe: visualProbe);

        WaitResult result = await service.WaitAsync(
            CreateTarget(targetWindow, WaitTargetSourceValues.Attached),
            new WaitRequest(WaitConditionValues.VisualChanged, TimeoutMs: 200),
            CancellationToken.None);

        Assert.Equal(WaitStatusValues.Done, result.Status);
        Assert.Equal(3, result.AttemptCount);
        Assert.NotNull(result.LastObserved?.VisualBaselineArtifactPath);
        Assert.NotNull(result.LastObserved?.VisualCurrentArtifactPath);
        Assert.True(File.Exists(result.LastObserved!.VisualBaselineArtifactPath!));
        Assert.True(File.Exists(result.LastObserved.VisualCurrentArtifactPath!));
        Assert.Equal(2, visualProbe.WrittenArtifactPaths.Count);
    }

    [Fact]
    public async Task WaitAsyncWaitsForConfiguredPollIntervalBeforeVisualConfirmationRecheck()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateAuditLogOptions(root, "run-wait-visual-gap");
        WindowDescriptor targetWindow = CreateWindow(hwnd: 60741, isForeground: false);
        WaitOptions waitOptions = new(TimeSpan.FromMilliseconds(20));
        SequenceVisualProbe visualProbe = new(
        [
            CreateVisualFrame(targetWindow, changedCells: 0),
            CreateVisualFrame(targetWindow, changedCells: 16),
            CreateVisualFrame(targetWindow, changedCells: 16),
        ]);
        PollingWaitService service = CreateService(
            options,
            new FakeWindowManager([targetWindow]),
            new FakeWindowTargetResolver(_ => targetWindow),
            new SequenceWaitProbe(),
            waitOptions,
            visualProbe);

        WaitResult result = await service.WaitAsync(
            CreateTarget(targetWindow, WaitTargetSourceValues.Attached),
            new WaitRequest(WaitConditionValues.VisualChanged, TimeoutMs: 200),
            CancellationToken.None);

        Assert.Equal(WaitStatusValues.Done, result.Status);
        Assert.True(visualProbe.CaptureTimestamps.Count >= 3);
        Assert.True(
            visualProbe.CaptureTimestamps[2] - visualProbe.CaptureTimestamps[1] >= TimeSpan.FromMilliseconds(15),
            "Visual confirmation recheck должен происходить после normal poll gap, а не сразу на том же cadence.");
    }

    [Fact]
    public async Task WaitAsyncReturnsTimeoutForVisualNoiseBelowThreshold()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateAuditLogOptions(root, "run-wait-visual-timeout");
        WindowDescriptor targetWindow = CreateWindow(hwnd: 6075, isForeground: false);
        SequenceVisualProbe visualProbe = new(
        [
            CreateVisualFrame(targetWindow, changedCells: 0),
            CreateVisualFrame(targetWindow, changedCells: 15),
            CreateVisualFrame(targetWindow, changedCells: 15),
        ],
        fallbackFrame: CreateVisualFrame(targetWindow, changedCells: 15));
        PollingWaitService service = CreateService(
            options,
            new FakeWindowManager([targetWindow]),
            new FakeWindowTargetResolver(_ => targetWindow),
            new SequenceWaitProbe(),
            new WaitOptions(TimeSpan.FromMilliseconds(1)),
            visualProbe);

        WaitResult result = await service.WaitAsync(
            CreateTarget(targetWindow, WaitTargetSourceValues.Attached),
            new WaitRequest(WaitConditionValues.VisualChanged, TimeoutMs: 100),
            CancellationToken.None);

        Assert.Equal(WaitStatusValues.Timeout, result.Status);
        Assert.NotNull(result.LastObserved?.VisualBaselineArtifactPath);
        Assert.Null(result.LastObserved?.VisualCurrentArtifactPath);
        Assert.Single(visualProbe.WrittenArtifactPaths);
    }

    [Fact]
    public async Task WaitAsyncReturnsFailedWhenVisualProbeFailsAfterBaseline()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateAuditLogOptions(root, "run-wait-visual-failed");
        WindowDescriptor targetWindow = CreateWindow(hwnd: 6076, isForeground: false);
        SequenceVisualProbe visualProbe = new(
        [
            CreateVisualFrame(targetWindow, changedCells: 0),
        ],
        fallbackFailure: new CaptureOperationException("visual probe failure"));
        PollingWaitService service = CreateService(
            options,
            new FakeWindowManager([targetWindow]),
            new FakeWindowTargetResolver(_ => targetWindow),
            new SequenceWaitProbe(),
            new WaitOptions(TimeSpan.FromMilliseconds(1)),
            visualProbe);

        WaitResult result = await service.WaitAsync(
            CreateTarget(targetWindow, WaitTargetSourceValues.Attached),
            new WaitRequest(WaitConditionValues.VisualChanged, TimeoutMs: 50),
            CancellationToken.None);

        Assert.Equal(WaitStatusValues.Failed, result.Status);
        Assert.Equal("visual probe failure", result.Reason);
        Assert.NotNull(result.LastObserved?.VisualBaselineArtifactPath);
        Assert.Null(result.LastObserved?.VisualCurrentArtifactPath);
    }

    [Fact]
    public async Task WaitAsyncReturnsFailedWhenVisualArtifactWriteThrowsIoError()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateAuditLogOptions(root, "run-wait-visual-artifact-io");
        WindowDescriptor targetWindow = CreateWindow(hwnd: 60761, isForeground: false);
        SequenceVisualProbe visualProbe = new(
        [
            CreateVisualFrame(targetWindow, changedCells: 0),
        ],
        writeFailures:
        [
            new UnauthorizedAccessException("visual artifact denied"),
        ]);
        PollingWaitService service = CreateService(
            options,
            new FakeWindowManager([targetWindow]),
            new FakeWindowTargetResolver(_ => targetWindow),
            new SequenceWaitProbe(),
            new WaitOptions(TimeSpan.FromMilliseconds(1)),
            visualProbe);

        WaitResult result = await service.WaitAsync(
            CreateTarget(targetWindow, WaitTargetSourceValues.Attached),
            new WaitRequest(WaitConditionValues.VisualChanged, TimeoutMs: 50),
            CancellationToken.None);

        Assert.Equal(WaitStatusValues.Failed, result.Status);
        Assert.Equal("Runtime не смог записать visual wait artifact на диск.", result.Reason);
    }

    [Fact]
    public async Task WaitAsyncReturnsTimeoutWhenBaselineVisualMaterializationExceedsRemainingBudget()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateAuditLogOptions(root, "run-wait-visual-baseline-timeout");
        WindowDescriptor targetWindow = CreateWindow(hwnd: 607611, isForeground: false);
        SequenceVisualProbe visualProbe = new(
        [
            CreateVisualFrame(targetWindow, changedCells: 0),
        ],
        writeDelays:
        [
            TimeSpan.FromMilliseconds(500),
        ]);
        PollingWaitService service = CreateService(
            options,
            new FakeWindowManager([targetWindow]),
            new FakeWindowTargetResolver(_ => targetWindow),
            new SequenceWaitProbe(),
            new WaitOptions(TimeSpan.FromMilliseconds(1)),
            visualProbe);

        WaitResult result = await service.WaitAsync(
            CreateTarget(targetWindow, WaitTargetSourceValues.Attached),
            new WaitRequest(WaitConditionValues.VisualChanged, TimeoutMs: 200),
            CancellationToken.None);

        Assert.Equal(WaitStatusValues.Timeout, result.Status);
        Assert.Equal(1, visualProbe.CanceledWriteCount);
    }

    [Fact]
    public async Task WaitAsyncReturnsTimeoutWhenFinalVisualMaterializationExceedsRemainingBudget()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateAuditLogOptions(root, "run-wait-visual-materialization-timeout");
        WindowDescriptor targetWindow = CreateWindow(hwnd: 60762, isForeground: false);
        SequenceVisualProbe visualProbe = new(
        [
            CreateVisualFrame(targetWindow, changedCells: 0),
            CreateVisualFrame(targetWindow, changedCells: 16),
            CreateVisualFrame(targetWindow, changedCells: 16),
        ],
        writeDelays:
        [
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(120),
        ]);
        PollingWaitService service = CreateService(
            options,
            new FakeWindowManager([targetWindow]),
            new FakeWindowTargetResolver(_ => targetWindow),
            new SequenceWaitProbe(),
            new WaitOptions(TimeSpan.FromMilliseconds(1)),
            visualProbe);

        WaitResult result = await service.WaitAsync(
            CreateTarget(targetWindow, WaitTargetSourceValues.Attached),
            new WaitRequest(WaitConditionValues.VisualChanged, TimeoutMs: 80),
            CancellationToken.None);

        Assert.Equal(WaitStatusValues.Timeout, result.Status);
    }

    [Fact]
    public async Task WaitAsyncReturnsTimeoutWhenConditionDoesNotStabilize()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateAuditLogOptions(root, "run-wait-timeout");
        WindowDescriptor targetWindow = CreateWindow(hwnd: 608, isForeground: false);
        PollingWaitService service = CreateService(
            options,
            new FakeWindowManager([targetWindow]),
            new FakeWindowTargetResolver(_ => targetWindow),
            new SequenceWaitProbe(),
            new WaitOptions(TimeSpan.FromMilliseconds(5)));

        WaitResult result = await service.WaitAsync(
            CreateTarget(targetWindow, WaitTargetSourceValues.Attached),
            new WaitRequest(WaitConditionValues.ActiveWindowMatches, TimeoutMs: 15),
            CancellationToken.None);

        Assert.Equal(WaitStatusValues.Timeout, result.Status);
        Assert.True(result.AttemptCount >= 1);
        Assert.NotNull(result.ArtifactPath);
        Assert.True(File.Exists(result.ArtifactPath));
    }

    [Fact]
    public async Task WaitAsyncBoundsUiAutomationProbeByRemainingTimeout()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateAuditLogOptions(root, "run-wait-bounded-probe");
        WindowDescriptor targetWindow = CreateWindow(hwnd: 609, isForeground: false);
        DelayedWaitProbe probe = new(TimeSpan.FromMilliseconds(250));
        PollingWaitService service = CreateService(
            options,
            new FakeWindowManager([targetWindow]),
            new FakeWindowTargetResolver(_ => targetWindow),
            probe,
            new WaitOptions(TimeSpan.FromMilliseconds(1)));
        Stopwatch stopwatch = Stopwatch.StartNew();

        WaitResult result = await service.WaitAsync(
            CreateTarget(targetWindow, WaitTargetSourceValues.Attached),
            new WaitRequest(
                WaitConditionValues.ElementExists,
                new WaitElementSelector(AutomationId: "SearchBox"),
                TimeoutMs: 20),
            CancellationToken.None);

        stopwatch.Stop();

        Assert.Equal(WaitStatusValues.Timeout, result.Status);
        Assert.True(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(150));
    }

    [Fact]
    public async Task WaitAsyncDoesNotReturnDoneForSiblingForegroundWindowWithSameStableIdentity()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateAuditLogOptions(root, "run-wait-sibling-foreground");
        WindowDescriptor targetWindow = CreateWindow(hwnd: 610, isForeground: false, processId: 100, threadId: 200, className: "SharedWindow");
        WindowDescriptor siblingForegroundWindow = CreateWindow(hwnd: 611, isForeground: true, processId: 100, threadId: 200, className: "SharedWindow");
        PollingWaitService service = CreateService(
            options,
            new FakeWindowManager([targetWindow, siblingForegroundWindow]),
            new FakeWindowTargetResolver(_ => targetWindow),
            new SequenceWaitProbe(),
            new WaitOptions(TimeSpan.FromMilliseconds(1)));

        WaitResult result = await service.WaitAsync(
            CreateTarget(targetWindow, WaitTargetSourceValues.Attached),
            new WaitRequest(WaitConditionValues.ActiveWindowMatches, TimeoutMs: 15),
            CancellationToken.None);

        Assert.Equal(WaitStatusValues.Timeout, result.Status);
        Assert.False(result.LastObserved?.TargetIsForeground);
    }

    [Fact]
    public async Task WaitAsyncRequiresSameTextSourceOnFinalRecheck()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateAuditLogOptions(root, "run-wait-text-same-source");
        WindowDescriptor targetWindow = CreateWindow(hwnd: 612, isForeground: false);
        UiaElementSnapshot element = CreateElement("rid:12");
        SequenceWaitProbe probe = new(
        [
            new UiAutomationWaitProbeResult
            {
                Window = CreateObservedWindow(targetWindow),
                Matches = [element],
                MatchedText = "Ready",
                MatchedTextSource = "value_pattern",
            },
            new UiAutomationWaitProbeResult
            {
                Window = CreateObservedWindow(targetWindow),
                Matches = [element],
                MatchedText = "Ready",
                MatchedTextSource = "name",
            },
        ]);
        PollingWaitService service = CreateService(
            options,
            new FakeWindowManager([targetWindow]),
            new FakeWindowTargetResolver(_ => targetWindow),
            probe,
            new WaitOptions(TimeSpan.FromMilliseconds(1)));

        WaitResult result = await service.WaitAsync(
            CreateTarget(targetWindow, WaitTargetSourceValues.Attached),
            new WaitRequest(
                WaitConditionValues.TextAppears,
                new WaitElementSelector(AutomationId: "SubmitButton"),
                ExpectedText: "Ready",
                TimeoutMs: 15),
            CancellationToken.None);

        Assert.Equal(WaitStatusValues.Timeout, result.Status);
    }

    private static PollingWaitService CreateService(
        AuditLogOptions options,
        IWindowManager windowManager,
        IWindowTargetResolver windowTargetResolver,
        IUiAutomationWaitProbe probe,
        WaitOptions? waitOptions = null,
        IWaitVisualProbe? visualProbe = null)
    {
        AuditLog auditLog = new(options, TimeProvider.System);
        return new PollingWaitService(
            windowManager,
            windowTargetResolver,
            probe,
            visualProbe ?? new SequenceVisualProbe([CreateVisualFrame(CreateWindow(hwnd: 499, isForeground: false), changedCells: 0)]),
            auditLog,
            options,
            TimeProvider.System,
            waitOptions ?? new WaitOptions(TimeSpan.FromMilliseconds(1)));
    }

    private static WaitTargetResolution CreateTarget(
        WindowDescriptor? window,
        string? source = WaitTargetSourceValues.Attached,
        string? failureCode = null) =>
        new(window, source, failureCode);

    private static WindowDescriptor CreateWindow(
        long hwnd,
        bool isForeground,
        int processId = 123,
        int threadId = 456,
        string className = "OknoWindow") =>
        new(
            Hwnd: hwnd,
            Title: "Window",
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

    private static ObservedWindowDescriptor CreateObservedWindow(WindowDescriptor window) =>
        new(
            Hwnd: window.Hwnd,
            Title: window.Title,
            ProcessName: window.ProcessName,
            ProcessId: window.ProcessId,
            ThreadId: window.ThreadId,
            ClassName: window.ClassName,
            Bounds: window.Bounds,
            IsForeground: window.IsForeground,
            IsVisible: window.IsVisible,
            EffectiveDpi: window.EffectiveDpi,
            DpiScale: window.DpiScale,
            WindowState: window.WindowState,
            MonitorId: window.MonitorId,
            MonitorFriendlyName: window.MonitorFriendlyName);

    private static UiaElementSnapshot CreateElement(string elementId) =>
        new()
        {
            ElementId = elementId,
            Depth = 0,
            Ordinal = 0,
            Name = "Element",
            AutomationId = "Element",
            ClassName = "Button",
            ControlType = "button",
            ControlTypeId = 50000,
            IsControlElement = true,
            IsContentElement = true,
            IsEnabled = true,
            Children = [],
        };

    private static WaitVisualFrame CreateVisualFrame(WindowDescriptor window, int changedCells)
    {
        const int pixelWidth = 16;
        const int pixelHeight = 16;
        const int rowStride = pixelWidth * 4;
        byte[] pixelBytes = new byte[rowStride * pixelHeight];

        for (int y = 0; y < pixelHeight; y++)
        {
            int rowOffset = y * rowStride;
            for (int x = 0; x < pixelWidth; x++)
            {
                int offset = rowOffset + (x * 4);
                byte value = (byte)((y * pixelWidth) + x < changedCells ? 65 : 50);
                pixelBytes[offset] = value;
                pixelBytes[offset + 1] = value;
                pixelBytes[offset + 2] = value;
                pixelBytes[offset + 3] = 255;
            }
        }

        return new WaitVisualFrame(
            window with { Bounds = new Bounds(0, 0, pixelWidth, pixelHeight) },
            pixelWidth,
            pixelHeight,
            rowStride,
            pixelBytes);
    }

    private static AuditLogOptions CreateAuditLogOptions(string root, string runId) =>
        new(
            ContentRootPath: root,
            EnvironmentName: "Tests",
            RunId: runId,
            DiagnosticsRoot: Path.Combine(root, "artifacts", "diagnostics"),
            RunDirectory: Path.Combine(root, "artifacts", "diagnostics", runId),
            EventsPath: Path.Combine(root, "artifacts", "diagnostics", runId, "events.jsonl"),
            SummaryPath: Path.Combine(root, "artifacts", "diagnostics", runId, "summary.md"));

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public async Task WaitAsyncIncludesProbeDiagnosticArtifactPathInFailureEvidence()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateAuditLogOptions(root, "run-wait-diagnostic-artifact");
        WindowDescriptor targetWindow = CreateWindow(hwnd: 613, isForeground: false);
        string diagnosticArtifactPath = Path.Combine(root, "artifacts", "diagnostics", "worker.json");
        SequenceWaitProbe probe = new(
        [
            new UiAutomationWaitProbeResult
            {
                Reason = "worker timeout",
                FailureStage = "worker_process",
                DiagnosticArtifactPath = diagnosticArtifactPath,
            },
        ]);
        PollingWaitService service = CreateService(
            options,
            new FakeWindowManager([targetWindow]),
            new FakeWindowTargetResolver(_ => targetWindow),
            probe);

        WaitResult result = await service.WaitAsync(
            CreateTarget(targetWindow, WaitTargetSourceValues.Attached),
            new WaitRequest(
                WaitConditionValues.ElementExists,
                new WaitElementSelector(AutomationId: "SearchBox"),
                TimeoutMs: 50),
            CancellationToken.None);

        Assert.Equal(WaitStatusValues.Failed, result.Status);
        Assert.Equal(diagnosticArtifactPath, result.LastObserved?.DiagnosticArtifactPath);

        using JsonDocument artifact = JsonDocument.Parse(await File.ReadAllTextAsync(result.ArtifactPath!));
        Assert.Equal(
            diagnosticArtifactPath,
            artifact.RootElement.GetProperty("result").GetProperty("last_observed").GetProperty("diagnostic_artifact_path").GetString());
    }

    [Fact]
    public async Task WaitAsyncDoesNotConvertDelayedProbeFailureIntoTimeout()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateAuditLogOptions(root, "run-wait-delayed-failure");
        WindowDescriptor targetWindow = CreateWindow(hwnd: 614, isForeground: false);
        LateFailureWaitProbe probe = new();
        PollingWaitService service = CreateService(
            options,
            new FakeWindowManager([targetWindow]),
            new FakeWindowTargetResolver(_ => targetWindow),
            probe,
            new WaitOptions(TimeSpan.FromMilliseconds(1)));

        WaitResult result = await service.WaitAsync(
            CreateTarget(targetWindow, WaitTargetSourceValues.Attached),
            new WaitRequest(
                WaitConditionValues.ElementExists,
                new WaitElementSelector(AutomationId: "SearchBox"),
                TimeoutMs: 10),
            CancellationToken.None);

        Assert.Equal(WaitStatusValues.Failed, result.Status);
        Assert.Equal("worker failure", result.Reason);
        Assert.Equal("worker_process", result.LastObserved?.Detail is null ? null : "worker_process");
    }

    [Fact]
    public async Task WaitAsyncReturnsFailedWithArtifactWhenProbeThrowsUnexpectedException()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateAuditLogOptions(root, "run-wait-unhandled-probe");
        WindowDescriptor targetWindow = CreateWindow(hwnd: 615, isForeground: false);
        PollingWaitService service = CreateService(
            options,
            new FakeWindowManager([targetWindow]),
            new FakeWindowTargetResolver(_ => targetWindow),
            new ThrowingWaitProbe());

        WaitResult result = await service.WaitAsync(
            CreateTarget(targetWindow, WaitTargetSourceValues.Attached),
            new WaitRequest(
                WaitConditionValues.ElementExists,
                new WaitElementSelector(AutomationId: "SearchBox"),
                TimeoutMs: 50),
            CancellationToken.None);

        Assert.Equal(WaitStatusValues.Failed, result.Status);
        Assert.Equal("Runtime не смог завершить wait request.", result.Reason);
        Assert.NotNull(result.ArtifactPath);
        Assert.True(File.Exists(result.ArtifactPath));

        using JsonDocument artifact = JsonDocument.Parse(await File.ReadAllTextAsync(result.ArtifactPath));
        JsonElement failureDiagnostics = artifact.RootElement.GetProperty("failure_diagnostics");
        Assert.Equal("runtime_unhandled", failureDiagnostics.GetProperty("failure_stage").GetString());
        Assert.Equal(typeof(InvalidOperationException).FullName, failureDiagnostics.GetProperty("exception_type").GetString());
        Assert.Equal("secret probe failure", failureDiagnostics.GetProperty("exception_message").GetString());

        string[] eventLines = await File.ReadAllLinesAsync(options.EventsPath);
        Assert.Single(eventLines);
        Assert.Contains("\"failure_stage\":\"runtime_unhandled\"", eventLines[0], StringComparison.Ordinal);
        Assert.Contains("\"exception_type\":\"System.InvalidOperationException\"", eventLines[0], StringComparison.Ordinal);
        Assert.Contains("\"exception_message\":\"secret probe failure\"", eventLines[0], StringComparison.Ordinal);
    }

    private sealed class SequenceWaitProbe(IReadOnlyList<UiAutomationWaitProbeResult>? results = null) : IUiAutomationWaitProbe
    {
        private readonly Queue<UiAutomationWaitProbeResult> _results = results is null
            ? new Queue<UiAutomationWaitProbeResult>()
            : new Queue<UiAutomationWaitProbeResult>(results);

        public Task<UiAutomationWaitProbeExecutionResult> ProbeAsync(
            WindowDescriptor targetWindow,
            WaitRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            if (_results.Count == 0)
            {
                return Task.FromResult(
                    new UiAutomationWaitProbeExecutionResult(
                        new UiAutomationWaitProbeResult
                        {
                            Window = CreateObservedWindow(targetWindow),
                            Matches = [],
                        },
                        DateTimeOffset.UtcNow));
            }

            return Task.FromResult(new UiAutomationWaitProbeExecutionResult(_results.Dequeue(), DateTimeOffset.UtcNow));
        }
    }

    private sealed class DelayedWaitProbe(TimeSpan delay) : IUiAutomationWaitProbe
    {
        public async Task<UiAutomationWaitProbeExecutionResult> ProbeAsync(
            WindowDescriptor targetWindow,
            WaitRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            await Task.Delay(delay, CancellationToken.None);

            return new UiAutomationWaitProbeExecutionResult(
                new UiAutomationWaitProbeResult
                {
                    Window = CreateObservedWindow(targetWindow),
                    Matches = [],
                },
                DateTimeOffset.UtcNow);
        }
    }

    private sealed class LateFailureWaitProbe : IUiAutomationWaitProbe
    {
        public Task<UiAutomationWaitProbeExecutionResult> ProbeAsync(
            WindowDescriptor targetWindow,
            WaitRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new UiAutomationWaitProbeExecutionResult(
                    new UiAutomationWaitProbeResult
                    {
                        FailureStage = "worker_process",
                        Reason = "worker failure",
                    },
                    DateTimeOffset.UtcNow.AddSeconds(1),
                    TimedOut: false,
                    DiagnosticArtifactPath: null));
    }

    private sealed class ThrowingWaitProbe : IUiAutomationWaitProbe
    {
        public Task<UiAutomationWaitProbeExecutionResult> ProbeAsync(
            WindowDescriptor targetWindow,
            WaitRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("secret probe failure");
    }

    private sealed class SequenceVisualProbe(
        IReadOnlyList<WaitVisualFrame> frames,
        WaitVisualFrame? fallbackFrame = null,
        CaptureOperationException? fallbackFailure = null,
        IReadOnlyList<TimeSpan>? writeDelays = null,
        IReadOnlyList<Exception>? writeFailures = null) : IWaitVisualProbe
    {
        private readonly Queue<WaitVisualFrame> _frames = new(frames);
        private readonly WaitVisualFrame _fallbackFrame = fallbackFrame ?? frames[^1];
        private readonly CaptureOperationException? _fallbackFailure = fallbackFailure;
        private readonly Queue<TimeSpan> _writeDelays = writeDelays is null ? new Queue<TimeSpan>() : new Queue<TimeSpan>(writeDelays);
        private readonly Queue<Exception> _writeFailures = writeFailures is null ? new Queue<Exception>() : new Queue<Exception>(writeFailures);

        public List<DateTimeOffset> CaptureTimestamps { get; } = [];
        public int CanceledWriteCount { get; private set; }
        public List<string> WrittenArtifactPaths { get; } = [];

        public Task<WaitVisualFrame> CaptureVisualAsync(
            WindowDescriptor targetWindow,
            CancellationToken cancellationToken)
        {
            CaptureTimestamps.Add(DateTimeOffset.UtcNow);
            if (_frames.Count > 0)
            {
                return Task.FromResult(_frames.Dequeue());
            }

            if (_fallbackFailure is not null)
            {
                throw _fallbackFailure;
            }

            return Task.FromResult(_fallbackFrame);
        }

        public async Task WriteVisualArtifactAsync(
            WaitVisualFrame frame,
            string path,
            CancellationToken cancellationToken)
        {
            if (_writeDelays.Count > 0)
            {
                try
                {
                    await Task.Delay(_writeDelays.Dequeue(), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    CanceledWriteCount++;
                    throw;
                }
            }

            if (_writeFailures.Count > 0)
            {
                throw _writeFailures.Dequeue();
            }

            WrittenArtifactPaths.Add(path);
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllBytesAsync(path, [137, 80, 78, 71], cancellationToken);
        }
    }

    private sealed class FakeWindowTargetResolver(Func<WindowDescriptor, WindowDescriptor?> resolver) : IWindowTargetResolver
    {
        public WindowDescriptor? ResolveExplicitOrAttachedWindow(long? explicitHwnd, WindowDescriptor? attachedWindow) =>
            throw new NotSupportedException();

        public WindowDescriptor? ResolveLiveWindowByIdentity(WindowDescriptor expectedWindow) => resolver(expectedWindow);

        public UiaSnapshotTargetResolution ResolveUiaSnapshotTarget(long? explicitHwnd, WindowDescriptor? attachedWindow) =>
            throw new NotSupportedException();

        public WaitTargetResolution ResolveWaitTarget(long? explicitHwnd, WindowDescriptor? attachedWindow) =>
            throw new NotSupportedException();
    }

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
