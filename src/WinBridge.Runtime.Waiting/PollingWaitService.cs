using System.Globalization;
using System.Runtime.InteropServices;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Windows.Capture;
using WinBridge.Runtime.Windows.Shell;
using WinBridge.Runtime.Windows.UIA;

namespace WinBridge.Runtime.Waiting;

public sealed class PollingWaitService(
    IWindowManager windowManager,
    IWindowTargetResolver windowTargetResolver,
    IUiAutomationWaitProbe uiAutomationWaitProbe,
    IWaitVisualProbe waitVisualProbe,
    AuditLogOptions auditLogOptions,
    TimeProvider timeProvider,
    WaitOptions options,
    WaitResultMaterializer resultMaterializer) : IWaitService
{
    private readonly WaitResultMaterializer _resultMaterializer = resultMaterializer;
    private readonly string _visualArtifactDirectory = Path.Combine(auditLogOptions.RunDirectory, "wait", "visual");

    public async Task<WaitResult> WaitAsync(
        WaitTargetResolution target,
        WaitRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(request);

        long startTimestamp = timeProvider.GetTimestamp();
        DateTimeOffset startedAtUtc = timeProvider.GetUtcNow();
        List<WaitAttemptSummary> attempts = [];
        WaitProbeSnapshot? lastProbe = null;
        WindowDescriptor? lastResolvedWindow = target.Window;
        int attemptCount = 0;
        WaitVisualState? visualState = null;

        try
        {
            if (!WaitRequestValidator.TryValidate(request, out string? validationReason))
            {
                return FinalizeResult(
                    request,
                    target,
                    attempts,
                    startedAtUtc,
                    new WaitResult(
                        Status: WaitStatusValues.Failed,
                        Condition: request.Condition,
                        TargetSource: target.Source,
                        TargetFailureCode: target.FailureCode,
                        Reason: validationReason,
                        TimeoutMs: request.TimeoutMs,
                        ElapsedMs: GetElapsedMs(startTimestamp),
                        AttemptCount: 0),
                    failureStage: "request_validation");
            }

            if (target.Window is null)
            {
                string failureCode = target.FailureCode ?? WaitTargetFailureValues.MissingTarget;
                return FinalizeResult(
                    request,
                    target,
                    attempts,
                    startedAtUtc,
                    new WaitResult(
                        Status: CreateTargetResolutionStatus(failureCode),
                        Condition: request.Condition,
                        TargetSource: target.Source,
                        TargetFailureCode: failureCode,
                        Reason: CreateTargetFailureReason(failureCode),
                        TimeoutMs: request.TimeoutMs,
                        ElapsedMs: GetElapsedMs(startTimestamp),
                        AttemptCount: 0),
                    failureStage: "target_resolution");
            }

            WindowDescriptor expectedWindow = target.Window;
            DateTimeOffset deadlineUtc = startedAtUtc + TimeSpan.FromMilliseconds(request.TimeoutMs);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                WaitProbeSnapshot probe = await ProbeOnceAsync(expectedWindow, target.Source, request, deadlineUtc, visualState, cancellationToken).ConfigureAwait(false);
                expectedWindow = probe.ResolvedTargetWindow ?? expectedWindow;
                lastResolvedWindow = probe.ResolvedTargetWindow ?? lastResolvedWindow;
                visualState = probe.VisualState ?? visualState;
                lastProbe = probe;
                attemptCount++;
                attempts.Add(CreateAttemptSummary(attemptCount, probe));

                if (probe.Outcome == WaitProbeOutcome.TimedOut)
                {
                    return FinalizeResult(
                        request,
                        target,
                        attempts,
                        startedAtUtc,
                        CreateTimeoutResult(request, target.Source, probe, startTimestamp, attemptCount));
                }

                if (probe.Outcome == WaitProbeOutcome.Failed)
                {
                    return FinalizeResult(
                        request,
                        target,
                        attempts,
                        startedAtUtc,
                        CreateNonSuccessResult(WaitStatusValues.Failed, request, target.Source, probe.TargetFailureCode, probe, startTimestamp, attemptCount),
                        failureStage: "probe_runtime");
                }

                if (probe.Outcome == WaitProbeOutcome.Ambiguous)
                {
                    return FinalizeResult(
                        request,
                        target,
                        attempts,
                        startedAtUtc,
                        CreateNonSuccessResult(WaitStatusValues.Ambiguous, request, target.Source, probe.TargetFailureCode, probe, startTimestamp, attemptCount));
                }

                if (probe.Outcome == WaitProbeOutcome.Candidate)
                {
                    if (string.Equals(request.Condition, WaitConditionValues.VisualChanged, StringComparison.Ordinal))
                    {
                        TimeSpan remainingBeforeRecheck = deadlineUtc - timeProvider.GetUtcNow();
                        if (remainingBeforeRecheck > TimeSpan.Zero)
                        {
                            TimeSpan confirmationDelay = remainingBeforeRecheck < options.PollInterval
                                ? remainingBeforeRecheck
                                : options.PollInterval;
                            await Task.Delay(confirmationDelay, cancellationToken).ConfigureAwait(false);
                        }
                    }

                    WaitProbeSnapshot recheck = await ProbeOnceAsync(expectedWindow, target.Source, request, deadlineUtc, visualState, cancellationToken).ConfigureAwait(false);
                    if (string.Equals(request.Condition, WaitConditionValues.VisualChanged, StringComparison.Ordinal))
                    {
                        DisposeVisualEvidenceFrame(probe.VisualEvidenceFrame);
                    }

                    expectedWindow = recheck.ResolvedTargetWindow ?? expectedWindow;
                    visualState = recheck.VisualState ?? visualState;
                    lastProbe = recheck;
                    attemptCount++;
                    attempts.Add(CreateAttemptSummary(attemptCount, recheck));

                    if (recheck.Outcome == WaitProbeOutcome.TimedOut)
                    {
                        return FinalizeResult(
                            request,
                            target,
                            attempts,
                            startedAtUtc,
                            CreateTimeoutResult(request, target.Source, recheck, startTimestamp, attemptCount));
                    }

                    if (recheck.Outcome == WaitProbeOutcome.Failed)
                    {
                        return FinalizeResult(
                            request,
                            target,
                            attempts,
                            startedAtUtc,
                            CreateNonSuccessResult(WaitStatusValues.Failed, request, target.Source, recheck.TargetFailureCode, recheck, startTimestamp, attemptCount),
                            failureStage: "probe_runtime");
                    }

                    if (recheck.Outcome == WaitProbeOutcome.Ambiguous)
                    {
                        return FinalizeResult(
                            request,
                            target,
                            attempts,
                            startedAtUtc,
                            CreateNonSuccessResult(WaitStatusValues.Ambiguous, request, target.Source, recheck.TargetFailureCode, recheck, startTimestamp, attemptCount));
                    }

                    if (recheck.Outcome == WaitProbeOutcome.Candidate && IsStableRecheck(request.Condition, probe, recheck))
                    {
                        if (string.Equals(request.Condition, WaitConditionValues.VisualChanged, StringComparison.Ordinal))
                        {
                            recheck = await MaterializeVisualSuccessEvidenceAsync(
                                recheck,
                                visualState,
                                request,
                                cancellationToken).ConfigureAwait(false);
                            lastProbe = recheck;
                            attempts[^1] = CreateAttemptSummary(attemptCount, recheck);
                        }

                        WaitResult doneResult = new(
                            Status: WaitStatusValues.Done,
                            Condition: request.Condition,
                            TargetSource: target.Source,
                            TargetFailureCode: null,
                            Reason: null,
                            Window: recheck.Window,
                            MatchedElement: ResolveStableMatchedElement(request.Condition, probe, recheck),
                            LastObserved: recheck.Observation,
                            TimeoutMs: request.TimeoutMs,
                            ElapsedMs: GetElapsedMs(startTimestamp),
                            AttemptCount: attemptCount);
                        return FinalizeResult(request, target, attempts, startedAtUtc, doneResult);
                    }
                }

                if (timeProvider.GetUtcNow() >= deadlineUtc)
                {
                    WaitResult timeoutResult = new(
                        Status: WaitStatusValues.Timeout,
                        Condition: request.Condition,
                        TargetSource: target.Source,
                        TargetFailureCode: null,
                        Reason: CreateTimeoutReason(request.Condition),
                        Window: lastProbe?.Window,
                        MatchedElement: lastProbe?.MatchedElement,
                        LastObserved: lastProbe?.Observation,
                        TimeoutMs: request.TimeoutMs,
                        ElapsedMs: GetElapsedMs(startTimestamp),
                        AttemptCount: attemptCount);
                    return FinalizeResult(request, target, attempts, startedAtUtc, timeoutResult);
                }

                TimeSpan remaining = deadlineUtc - timeProvider.GetUtcNow();
                if (remaining <= TimeSpan.Zero)
                {
                    continue;
                }

                TimeSpan delay = remaining < options.PollInterval ? remaining : options.PollInterval;
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            WaitResult failedResult = new(
                Status: WaitStatusValues.Failed,
                Condition: request.Condition,
                TargetSource: target.Source,
                TargetFailureCode: lastProbe?.TargetFailureCode ?? target.FailureCode,
                Reason: "Runtime не смог завершить wait request.",
                Window: lastProbe?.Window ?? (lastResolvedWindow is null ? null : ToObservedWindow(lastResolvedWindow)),
                MatchedElement: lastProbe?.MatchedElement,
                LastObserved: lastProbe?.Observation,
                TimeoutMs: request.TimeoutMs,
                ElapsedMs: GetElapsedMs(startTimestamp),
                AttemptCount: attemptCount);
            return FinalizeResult(request, target, attempts, startedAtUtc, failedResult, failureStage: "runtime_unhandled", failureException: exception);
        }
        finally
        {
            DisposeVisualState(visualState);
            DisposeVisualEvidenceFrame(lastProbe?.VisualEvidenceFrame);
        }
    }

    private async Task<WaitProbeSnapshot> ProbeOnceAsync(
        WindowDescriptor expectedWindow,
        string? targetSource,
        WaitRequest request,
        DateTimeOffset deadlineUtc,
        WaitVisualState? visualState,
        CancellationToken cancellationToken)
    {
        if (string.Equals(request.Condition, WaitConditionValues.ActiveWindowMatches, StringComparison.Ordinal))
        {
            return ProbeActiveWindow(expectedWindow, targetSource);
        }

        if (string.Equals(request.Condition, WaitConditionValues.ElementExists, StringComparison.Ordinal)
            || string.Equals(request.Condition, WaitConditionValues.ElementGone, StringComparison.Ordinal)
            || string.Equals(request.Condition, WaitConditionValues.TextAppears, StringComparison.Ordinal)
            || string.Equals(request.Condition, WaitConditionValues.FocusIs, StringComparison.Ordinal))
        {
            return await ProbeUiAutomationAsync(expectedWindow, targetSource, request, deadlineUtc, cancellationToken).ConfigureAwait(false);
        }

        if (string.Equals(request.Condition, WaitConditionValues.VisualChanged, StringComparison.Ordinal))
        {
            return await ProbeVisualChangedAsync(expectedWindow, targetSource, deadlineUtc, visualState, cancellationToken).ConfigureAwait(false);
        }

        return new WaitProbeSnapshot(
            Outcome: WaitProbeOutcome.Failed,
            ObservedAtUtc: timeProvider.GetUtcNow(),
            Window: ToObservedWindow(expectedWindow),
            MatchedElement: null,
            Observation: new WaitObservation(Detail: $"Условие wait '{request.Condition}' пока не поддерживается."),
            Reason: $"Условие wait '{request.Condition}' пока не поддерживается.",
            TargetFailureCode: null,
            ResolvedTargetWindow: expectedWindow);
    }

    private WaitProbeSnapshot ProbeActiveWindow(WindowDescriptor expectedWindow, string? targetSource)
    {
        DateTimeOffset observedAtUtc = timeProvider.GetUtcNow();
        WindowDescriptor? liveTarget = windowTargetResolver.ResolveLiveWindowByIdentity(expectedWindow);
        if (liveTarget is null)
        {
            string failureCode = CreateStaleTargetFailureCode(targetSource);
            return new WaitProbeSnapshot(
                Outcome: WaitProbeOutcome.Failed,
                ObservedAtUtc: observedAtUtc,
                Window: null,
                MatchedElement: null,
                Observation: new WaitObservation(Detail: CreateTargetFailureReason(failureCode)),
                Reason: CreateTargetFailureReason(failureCode),
                TargetFailureCode: failureCode,
                ResolvedTargetWindow: null);
        }

        WindowDescriptor[] foregroundCandidates = windowManager.ListWindows(includeInvisible: true)
            .Where(candidate => candidate.IsForeground)
            .GroupBy(candidate => candidate.Hwnd)
            .Select(group => group.First())
            .Take(2)
            .ToArray();

        bool targetIsForeground = foregroundCandidates.Length == 1
            && foregroundCandidates[0].Hwnd == liveTarget.Hwnd
            && WindowIdentityValidator.MatchesStableIdentity(foregroundCandidates[0], expectedWindow);
        WaitObservation observation = new(
            MatchCount: foregroundCandidates.Length,
            TargetIsForeground: targetIsForeground,
            Detail: targetIsForeground
                ? "Resolved target является foreground window."
                : foregroundCandidates.Length > 1
                    ? "Foreground window snapshot неоднозначен."
                    : "Resolved target ещё не стал foreground window.");

        if (foregroundCandidates.Length > 1)
        {
            return new WaitProbeSnapshot(
                Outcome: WaitProbeOutcome.Ambiguous,
                ObservedAtUtc: observedAtUtc,
                Window: ToObservedWindow(liveTarget),
                MatchedElement: null,
                Observation: observation,
                Reason: "Foreground window snapshot неоднозначен: найдено несколько live candidates.",
                TargetFailureCode: null,
                ResolvedTargetWindow: liveTarget);
        }

        return new WaitProbeSnapshot(
            Outcome: targetIsForeground ? WaitProbeOutcome.Candidate : WaitProbeOutcome.Pending,
            ObservedAtUtc: observedAtUtc,
            Window: ToObservedWindow(liveTarget),
            MatchedElement: null,
            Observation: observation,
            Reason: null,
            TargetFailureCode: null,
            ResolvedTargetWindow: liveTarget);
    }

    private async Task<WaitProbeSnapshot> ProbeUiAutomationAsync(
        WindowDescriptor expectedWindow,
        string? targetSource,
        WaitRequest request,
        DateTimeOffset deadlineUtc,
        CancellationToken cancellationToken)
    {
        DateTimeOffset observedAtUtc = timeProvider.GetUtcNow();
        WindowDescriptor? liveTarget = windowTargetResolver.ResolveLiveWindowByIdentity(expectedWindow);
        if (liveTarget is null)
        {
            string failureCode = CreateStaleTargetFailureCode(targetSource);
            return new WaitProbeSnapshot(
                Outcome: WaitProbeOutcome.Failed,
                ObservedAtUtc: observedAtUtc,
                Window: null,
                MatchedElement: null,
                Observation: new WaitObservation(Detail: CreateTargetFailureReason(failureCode)),
                Reason: CreateTargetFailureReason(failureCode),
                TargetFailureCode: failureCode,
                ResolvedTargetWindow: null);
        }

        UiAutomationProbeExecutionResult probeExecution = await ExecuteUiAutomationProbeWithinDeadlineAsync(
            liveTarget,
            request,
            deadlineUtc,
            cancellationToken).ConfigureAwait(false);

        if (probeExecution.TimedOut)
        {
            return CreateUiAutomationTimeoutSnapshot(observedAtUtc, liveTarget, probeExecution.DiagnosticArtifactPath);
        }

        WaitProbeSnapshot semanticSnapshot = CreateUiAutomationSemanticSnapshot(
            observedAtUtc,
            liveTarget,
            request,
            probeExecution);
        if (probeExecution.EffectiveCompletedAtUtc > deadlineUtc && ShouldDowngradeLateUiAutomationProbeToTimeout(semanticSnapshot))
        {
            return CreateUiAutomationTimeoutSnapshot(
                observedAtUtc,
                liveTarget,
                semanticSnapshot.Observation.DiagnosticArtifactPath);
        }

        return semanticSnapshot;
    }

    private async Task<WaitProbeSnapshot> ProbeVisualChangedAsync(
        WindowDescriptor expectedWindow,
        string? targetSource,
        DateTimeOffset deadlineUtc,
        WaitVisualState? visualState,
        CancellationToken cancellationToken)
    {
        DateTimeOffset observedAtUtc = timeProvider.GetUtcNow();
        WindowDescriptor? liveTarget = windowTargetResolver.ResolveLiveWindowByIdentity(expectedWindow);
        if (liveTarget is null)
        {
            string failureCode = CreateStaleTargetFailureCode(targetSource);
            return new WaitProbeSnapshot(
                Outcome: WaitProbeOutcome.Failed,
                ObservedAtUtc: observedAtUtc,
                Window: null,
                MatchedElement: null,
                Observation: new WaitObservation(
                    Detail: CreateTargetFailureReason(failureCode),
                    VisualDifferenceThreshold: WaitVisualComparisonPolicy.DifferenceRatioThreshold,
                    VisualEvidenceStatus: WaitVisualEvidenceStatusValues.Skipped),
                Reason: CreateTargetFailureReason(failureCode),
                TargetFailureCode: failureCode,
                ResolvedTargetWindow: null,
                VisualState: visualState,
                VisualEvidenceFrame: null);
        }

        WaitVisualExecutionResult probeExecution = await ExecuteVisualProbeWithinDeadlineAsync(
            liveTarget,
            deadlineUtc,
            cancellationToken).ConfigureAwait(false);

        if (probeExecution.TimedOut)
        {
            return CreateVisualTimeoutSnapshot(observedAtUtc, liveTarget);
        }

        if (!string.IsNullOrWhiteSpace(probeExecution.FailureReason))
        {
            return new WaitProbeSnapshot(
                Outcome: WaitProbeOutcome.Failed,
                ObservedAtUtc: observedAtUtc,
                Window: ToObservedWindow(liveTarget),
                MatchedElement: null,
                Observation: new WaitObservation(
                    Detail: probeExecution.FailureReason,
                    VisualDifferenceThreshold: WaitVisualComparisonPolicy.DifferenceRatioThreshold,
                    VisualEvidenceStatus: WaitVisualEvidenceStatusValues.Skipped),
                Reason: probeExecution.FailureReason,
                TargetFailureCode: null,
                ResolvedTargetWindow: liveTarget,
                VisualState: visualState,
                VisualEvidenceFrame: null);
        }

        WaitVisualSample currentSample = probeExecution.Sample!;
        WindowDescriptor observedWindow = currentSample.Window;
        if (probeExecution.CompletedAtUtc > deadlineUtc)
        {
            DisposeVisualEvidenceFrame(currentSample.EvidenceFrame);
            return CreateVisualTimeoutSnapshot(observedAtUtc, observedWindow);
        }

        if (visualState is null)
        {
            WaitVisualState nextState = new(
                currentSample.ComparisonData,
                currentSample.PixelWidth,
                currentSample.PixelHeight,
                observedAtUtc,
                currentSample.EvidenceFrame);
            return new WaitProbeSnapshot(
                Outcome: WaitProbeOutcome.Pending,
                ObservedAtUtc: observedAtUtc,
                Window: ToObservedWindow(observedWindow),
                MatchedElement: null,
                Observation: new WaitObservation(
                    Detail: "Визуальный baseline зафиксирован; runtime ждёт подтверждённого изменения.",
                    VisualDifferenceRatio: 0.0,
                    VisualDifferenceThreshold: WaitVisualComparisonPolicy.DifferenceRatioThreshold,
                    VisualEvidenceStatus: WaitVisualEvidenceStatusValues.Skipped),
                Reason: null,
                TargetFailureCode: null,
                ResolvedTargetWindow: observedWindow,
                VisualState: nextState,
                VisualEvidenceFrame: null);
        }

        WaitVisualComparisonResult comparison = WaitVisualComparisonPolicy.Compare(
            visualState.BaselineComparisonData,
            visualState.BaselinePixelWidth,
            visualState.BaselinePixelHeight,
            currentSample);
        WaitVisualEvidenceFrame? currentEvidenceFrame = comparison.IsCandidate
            ? currentSample.EvidenceFrame
            : null;
        if (!comparison.IsCandidate)
        {
            DisposeVisualEvidenceFrame(currentSample.EvidenceFrame);
        }

        return new WaitProbeSnapshot(
            Outcome: comparison.IsCandidate ? WaitProbeOutcome.Candidate : WaitProbeOutcome.Pending,
            ObservedAtUtc: observedAtUtc,
            Window: ToObservedWindow(observedWindow),
            MatchedElement: null,
            Observation: new WaitObservation(
                Detail: comparison.Detail,
                VisualDifferenceRatio: comparison.DifferenceRatio,
                VisualDifferenceThreshold: comparison.EffectiveThresholdRatio,
                VisualEvidenceStatus: WaitVisualEvidenceStatusValues.Skipped),
            Reason: null,
            TargetFailureCode: null,
            ResolvedTargetWindow: observedWindow,
            VisualState: visualState,
            VisualEvidenceFrame: currentEvidenceFrame);
    }

    private async Task<WaitVisualExecutionResult> ExecuteVisualProbeWithinDeadlineAsync(
        WindowDescriptor liveTarget,
        DateTimeOffset deadlineUtc,
        CancellationToken cancellationToken)
    {
        if (!TryCreateRemainingBudgetCancellation(
            deadlineUtc,
            cancellationToken,
            out CancellationTokenSource? probeCancellation))
        {
            return new WaitVisualExecutionResult(
                Sample: null,
                CompletedAtUtc: timeProvider.GetUtcNow(),
                TimedOut: true,
                FailureReason: null);
        }

        using (probeCancellation)
        {
            try
            {
                WaitVisualSample sample = await waitVisualProbe
                    .CaptureVisualSampleAsync(liveTarget, probeCancellation!.Token)
                    .ConfigureAwait(false);
                return new WaitVisualExecutionResult(
                    Sample: sample,
                    CompletedAtUtc: timeProvider.GetUtcNow(),
                    TimedOut: false,
                    FailureReason: null);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && probeCancellation!.IsCancellationRequested)
            {
                return new WaitVisualExecutionResult(
                    Sample: null,
                    CompletedAtUtc: timeProvider.GetUtcNow(),
                    TimedOut: true,
                    FailureReason: null);
            }
            catch (CaptureOperationException exception)
            {
                return new WaitVisualExecutionResult(
                    Sample: null,
                    CompletedAtUtc: timeProvider.GetUtcNow(),
                    TimedOut: false,
                    FailureReason: exception.Message);
            }
        }
    }

    private async Task<string> WriteVisualArtifactAsync(
        WaitVisualEvidenceFrame frame,
        string phase,
        DateTimeOffset capturedAtUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            string path = Path.Combine(
                _visualArtifactDirectory,
                WaitVisualArtifactNameBuilder.Create(
                    phase,
                    frame.Window.Hwnd.ToString(CultureInfo.InvariantCulture),
                    capturedAtUtc.UtcDateTime));
            await waitVisualProbe.WriteVisualEvidenceAsync(frame, path, cancellationToken).ConfigureAwait(false);
            return path;
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new CaptureOperationException("Runtime не смог записать visual wait artifact на диск.", exception);
        }
        catch (IOException exception)
        {
            throw new CaptureOperationException("Runtime не смог записать visual wait artifact на диск.", exception);
        }
        catch (ExternalException exception)
        {
            throw new CaptureOperationException("Runtime не смог закодировать visual wait artifact в PNG.", exception);
        }
    }

    private async Task<WaitProbeSnapshot> MaterializeVisualSuccessEvidenceAsync(
        WaitProbeSnapshot probe,
        WaitVisualState? visualState,
        WaitRequest request,
        CancellationToken cancellationToken)
    {
        if (visualState?.BaselineEvidenceFrame is null
            || probe.VisualEvidenceFrame is null)
        {
            return probe with
            {
                Observation = probe.Observation with
                {
                    VisualEvidenceStatus = WaitVisualEvidenceStatusValues.Skipped,
                    VisualBaselineArtifactPath = null,
                    VisualCurrentArtifactPath = null,
                },
            };
        }

        WaitVisualEvidenceFrame baselineEvidenceFrame = visualState.BaselineEvidenceFrame;
        DateTimeOffset baselineObservedAtUtc = visualState.BaselineObservedAtUtc;

        if (!TryCreateVisualEvidenceBudgetCancellation(
            request,
            cancellationToken,
            out CancellationTokenSource? evidenceCancellation,
            out DateTimeOffset evidenceDeadlineUtc))
        {
            return probe with
            {
                Observation = probe.Observation with
                {
                    VisualEvidenceStatus = WaitVisualEvidenceStatusValues.Skipped,
                },
            };
        }

        CancellationTokenSource evidenceBudgetCancellation = evidenceCancellation!;
        using (evidenceBudgetCancellation)
        {
            CancellationToken evidenceBudgetToken = evidenceBudgetCancellation.Token;
            VisualEvidenceWriteResult baselineWrite = await WriteVisualArtifactWithinBudgetAsync(
                baselineEvidenceFrame,
                "baseline",
                baselineObservedAtUtc,
                evidenceDeadlineUtc,
                evidenceBudgetToken,
                cancellationToken).ConfigureAwait(false);
            if (baselineWrite.TimedOut)
            {
                return probe with
                {
                    Observation = probe.Observation with
                    {
                        Detail = "PNG evidence для подтверждённого visual change превысил отдельный evidence budget.",
                        VisualEvidenceStatus = WaitVisualEvidenceStatusValues.Timeout,
                        VisualBaselineArtifactPath = baselineWrite.ArtifactPath,
                        VisualCurrentArtifactPath = null,
                    },
                };
            }

            if (!baselineWrite.Success)
            {
                return probe with
                {
                    Observation = probe.Observation with
                    {
                        Detail = baselineWrite.Reason,
                        VisualEvidenceStatus = WaitVisualEvidenceStatusValues.Failed,
                        VisualBaselineArtifactPath = baselineWrite.ArtifactPath,
                        VisualCurrentArtifactPath = null,
                    },
                };
            }

            VisualEvidenceWriteResult currentWrite = await WriteVisualArtifactWithinBudgetAsync(
                probe.VisualEvidenceFrame,
                "current",
                probe.ObservedAtUtc,
                evidenceDeadlineUtc,
                evidenceBudgetToken,
                cancellationToken).ConfigureAwait(false);
            if (currentWrite.TimedOut)
            {
                return probe with
                {
                    Observation = probe.Observation with
                    {
                        Detail = "PNG evidence для подтверждённого visual change превысил отдельный evidence budget.",
                        VisualEvidenceStatus = WaitVisualEvidenceStatusValues.Timeout,
                        VisualBaselineArtifactPath = baselineWrite.ArtifactPath,
                        VisualCurrentArtifactPath = currentWrite.ArtifactPath,
                    },
                };
            }

            if (!currentWrite.Success)
            {
                return probe with
                {
                    Observation = probe.Observation with
                    {
                        Detail = currentWrite.Reason,
                        VisualEvidenceStatus = WaitVisualEvidenceStatusValues.Failed,
                        VisualBaselineArtifactPath = baselineWrite.ArtifactPath,
                        VisualCurrentArtifactPath = currentWrite.ArtifactPath,
                    },
                };
            }

            return probe with
            {
                Observation = probe.Observation with
                {
                    VisualEvidenceStatus = WaitVisualEvidenceStatusValues.Materialized,
                    VisualBaselineArtifactPath = baselineWrite.ArtifactPath,
                    VisualCurrentArtifactPath = currentWrite.ArtifactPath,
                },
            };
        }
    }

    private bool TryCreateRemainingBudgetCancellation(
        DateTimeOffset deadlineUtc,
        CancellationToken cancellationToken,
        out CancellationTokenSource? budgetCancellation)
    {
        TimeSpan remaining = deadlineUtc - timeProvider.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
        {
            budgetCancellation = null;
            return false;
        }

        budgetCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budgetCancellation.CancelAfter(remaining);
        return true;
    }

    private bool TryCreateVisualEvidenceBudgetCancellation(
        WaitRequest request,
        CancellationToken cancellationToken,
        out CancellationTokenSource? budgetCancellation,
        out DateTimeOffset budgetDeadlineUtc)
    {
        double budgetMilliseconds = Math.Min(request.TimeoutMs, options.VisualEvidenceBudget.TotalMilliseconds);
        if (budgetMilliseconds <= 0)
        {
            budgetCancellation = null;
            budgetDeadlineUtc = timeProvider.GetUtcNow();
            return false;
        }

        budgetDeadlineUtc = timeProvider.GetUtcNow() + TimeSpan.FromMilliseconds(budgetMilliseconds);
        budgetCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budgetCancellation.CancelAfter(TimeSpan.FromMilliseconds(budgetMilliseconds));
        return true;
    }

    private async Task<VisualEvidenceWriteResult> WriteVisualArtifactWithinBudgetAsync(
        WaitVisualEvidenceFrame frame,
        string phase,
        DateTimeOffset capturedAtUtc,
        DateTimeOffset deadlineUtc,
        CancellationToken evidenceCancellationToken,
        CancellationToken cancellationToken)
    {
        try
        {
            string artifactPath = await WriteVisualArtifactAsync(
                frame,
                phase,
                capturedAtUtc,
                evidenceCancellationToken).ConfigureAwait(false);
            if (timeProvider.GetUtcNow() > deadlineUtc)
            {
                return new(
                    TimedOut: true,
                    ArtifactPath: artifactPath,
                    Reason: null);
            }

            return new(
                TimedOut: false,
                ArtifactPath: artifactPath,
                Reason: null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && evidenceCancellationToken.IsCancellationRequested)
        {
            return new(
                TimedOut: true,
                ArtifactPath: null,
                Reason: null);
        }
        catch (CaptureOperationException exception)
        {
            return new(
                TimedOut: false,
                ArtifactPath: null,
                Reason: exception.Message);
        }
    }

    private static WaitProbeSnapshot CreateUiAutomationSemanticSnapshot(
        DateTimeOffset observedAtUtc,
        WindowDescriptor liveTarget,
        WaitRequest request,
        UiAutomationProbeExecutionResult probeExecution)
    {
        UiAutomationWaitProbeResult probeResult = probeExecution.Result.Result;
        string? diagnosticArtifactPath = probeExecution.DiagnosticArtifactPath ?? probeResult.DiagnosticArtifactPath;

        if (string.Equals(probeResult.FailureStage, "timeout", StringComparison.Ordinal))
        {
            return CreateUiAutomationTimeoutSnapshot(observedAtUtc, liveTarget, diagnosticArtifactPath);
        }

        string? failureReason = CreateUiAutomationProbeFailureReason(probeResult);
        ObservedWindowDescriptor observedWindow = probeResult.Window ?? ToObservedWindow(liveTarget);
        if (failureReason is not null)
        {
            return new WaitProbeSnapshot(
                Outcome: WaitProbeOutcome.Failed,
                ObservedAtUtc: observedAtUtc,
                Window: observedWindow,
                MatchedElement: null,
                Observation: new WaitObservation(
                    DiagnosticArtifactPath: diagnosticArtifactPath,
                    Detail: failureReason),
                Reason: failureReason,
                TargetFailureCode: null,
                ResolvedTargetWindow: liveTarget);
        }

        int matchCount = probeResult.Matches.Count;
        UiaElementSnapshot[] candidateElements = probeResult.Matches.ToArray();
        UiaElementSnapshot? matchedElement = matchCount == 1 ? probeResult.Matches[0] : null;
        WaitObservation observation = new(
            MatchCount: matchCount,
            MatchedText: probeResult.MatchedText,
            MatchedTextSource: probeResult.MatchedTextSource,
            DiagnosticArtifactPath: diagnosticArtifactPath,
            Detail: CreateUiObservationDetail(request.Condition, matchCount, probeResult.MatchedTextSource));

        if (string.Equals(request.Condition, WaitConditionValues.ElementExists, StringComparison.Ordinal))
        {
            return new WaitProbeSnapshot(
                Outcome: matchCount >= 1 ? WaitProbeOutcome.Candidate : WaitProbeOutcome.Pending,
                ObservedAtUtc: observedAtUtc,
                Window: observedWindow,
                MatchedElement: matchedElement,
                Observation: observation,
                Reason: null,
                TargetFailureCode: null,
                ResolvedTargetWindow: liveTarget,
                CandidateElements: candidateElements);
        }

        if (string.Equals(request.Condition, WaitConditionValues.ElementGone, StringComparison.Ordinal))
        {
            return new WaitProbeSnapshot(
                Outcome: matchCount == 0 ? WaitProbeOutcome.Candidate : WaitProbeOutcome.Pending,
                ObservedAtUtc: observedAtUtc,
                Window: observedWindow,
                MatchedElement: matchedElement,
                Observation: observation,
                Reason: null,
                TargetFailureCode: null,
                ResolvedTargetWindow: liveTarget,
                CandidateElements: candidateElements);
        }

        if (string.Equals(request.Condition, WaitConditionValues.FocusIs, StringComparison.Ordinal))
        {
            if (matchCount > 1)
            {
                return new WaitProbeSnapshot(
                    Outcome: WaitProbeOutcome.Ambiguous,
                    ObservedAtUtc: observedAtUtc,
                    Window: observedWindow,
                    MatchedElement: null,
                    Observation: observation,
                    Reason: "Focused element совпал с несколькими live candidates.",
                    TargetFailureCode: null,
                    ResolvedTargetWindow: liveTarget,
                    CandidateElements: candidateElements);
            }

            return new WaitProbeSnapshot(
                Outcome: matchCount == 1 ? WaitProbeOutcome.Candidate : WaitProbeOutcome.Pending,
                ObservedAtUtc: observedAtUtc,
                Window: observedWindow,
                MatchedElement: matchedElement,
                Observation: observation,
                Reason: null,
                TargetFailureCode: null,
                ResolvedTargetWindow: liveTarget,
                CandidateElements: candidateElements);
        }

        if (matchCount > 1)
        {
            return new WaitProbeSnapshot(
                Outcome: WaitProbeOutcome.Ambiguous,
                ObservedAtUtc: observedAtUtc,
                Window: observedWindow,
                MatchedElement: null,
                Observation: observation,
                Reason: "UIA selector совпал с несколькими text-qualified live elements.",
                TargetFailureCode: null,
                ResolvedTargetWindow: liveTarget,
                CandidateElements: candidateElements);
        }

        bool textMatched = matchCount == 1 && !string.IsNullOrWhiteSpace(probeResult.MatchedTextSource);
        return new WaitProbeSnapshot(
            Outcome: textMatched ? WaitProbeOutcome.Candidate : WaitProbeOutcome.Pending,
            ObservedAtUtc: observedAtUtc,
            Window: observedWindow,
            MatchedElement: matchedElement,
            Observation: observation,
            Reason: null,
            TargetFailureCode: null,
            ResolvedTargetWindow: liveTarget,
            CandidateElements: candidateElements);
    }

    private static bool ShouldDowngradeLateUiAutomationProbeToTimeout(WaitProbeSnapshot semanticSnapshot) =>
        semanticSnapshot.Outcome is WaitProbeOutcome.Pending or WaitProbeOutcome.Candidate;

    private WaitResult FinalizeResult(
        WaitRequest request,
        WaitTargetResolution target,
        IReadOnlyList<WaitAttemptSummary> attempts,
        DateTimeOffset startedAtUtc,
        WaitResult result,
        string? failureStage = null,
        Exception? failureException = null)
        => _resultMaterializer.Materialize(request, target, attempts, startedAtUtc, result, failureStage, failureException);

    private WaitResult CreateNonSuccessResult(
        string status,
        WaitRequest request,
        string? targetSource,
        string? targetFailureCode,
        WaitProbeSnapshot probe,
        long startTimestamp,
        int attemptCount) =>
        new(
            Status: status,
            Condition: request.Condition,
            TargetSource: targetSource,
            TargetFailureCode: targetFailureCode,
            Reason: probe.Reason,
            Window: probe.Window,
            MatchedElement: probe.MatchedElement,
            LastObserved: probe.Observation,
            TimeoutMs: request.TimeoutMs,
            ElapsedMs: GetElapsedMs(startTimestamp),
            AttemptCount: attemptCount);

    private WaitResult CreateTimeoutResult(
        WaitRequest request,
        string? targetSource,
        WaitProbeSnapshot? probe,
        long startTimestamp,
        int attemptCount) =>
        new(
            Status: WaitStatusValues.Timeout,
            Condition: request.Condition,
            TargetSource: targetSource,
            TargetFailureCode: null,
            Reason: CreateTimeoutReason(request.Condition),
            Window: probe?.Window,
            MatchedElement: probe?.MatchedElement,
            LastObserved: probe?.Observation,
            TimeoutMs: request.TimeoutMs,
            ElapsedMs: GetElapsedMs(startTimestamp),
            AttemptCount: attemptCount);

    private static string CreateUiObservationDetail(string condition, int matchCount, string? matchedTextSource)
    {
        if (string.Equals(condition, WaitConditionValues.ElementExists, StringComparison.Ordinal))
        {
            return matchCount == 0 ? "Элемент ещё не найден." : "Элемент найден.";
        }

        if (string.Equals(condition, WaitConditionValues.ElementGone, StringComparison.Ordinal))
        {
            return matchCount == 0 ? "Элемент больше не найден." : "Элемент всё ещё присутствует.";
        }

        if (string.Equals(condition, WaitConditionValues.FocusIs, StringComparison.Ordinal))
        {
            return matchCount == 0
                ? "Focused element ещё не совпадает с selector внутри resolved window."
                : "Focused element совпал с selector.";
        }

        if (!string.IsNullOrWhiteSpace(matchedTextSource))
        {
            return "Ожидаемый текст найден.";
        }

        return matchCount == 0
            ? "Элемент для текстовой проверки ещё не найден."
            : "Ожидаемый текст ещё не появился.";
    }

    private static bool IsStableRecheck(string condition, WaitProbeSnapshot firstProbe, WaitProbeSnapshot secondProbe)
    {
        if (string.Equals(condition, WaitConditionValues.ActiveWindowMatches, StringComparison.Ordinal)
            || string.Equals(condition, WaitConditionValues.ElementGone, StringComparison.Ordinal)
            || string.Equals(condition, WaitConditionValues.VisualChanged, StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(condition, WaitConditionValues.ElementExists, StringComparison.Ordinal))
        {
            return TryGetStableCandidateOverlap(firstProbe, secondProbe, out _);
        }

        if (firstProbe.MatchedElement is null || secondProbe.MatchedElement is null)
        {
            return false;
        }

        if (string.Equals(condition, WaitConditionValues.TextAppears, StringComparison.Ordinal))
        {
            return string.Equals(firstProbe.MatchedElement.ElementId, secondProbe.MatchedElement.ElementId, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(firstProbe.Observation.MatchedTextSource)
                && string.Equals(firstProbe.Observation.MatchedTextSource, secondProbe.Observation.MatchedTextSource, StringComparison.Ordinal);
        }

        return string.Equals(firstProbe.MatchedElement.ElementId, secondProbe.MatchedElement.ElementId, StringComparison.Ordinal);
    }

    private static UiaElementSnapshot? ResolveStableMatchedElement(string condition, WaitProbeSnapshot firstProbe, WaitProbeSnapshot secondProbe)
    {
        if (string.Equals(condition, WaitConditionValues.ElementExists, StringComparison.Ordinal)
            && TryGetStableCandidateOverlap(firstProbe, secondProbe, out UiaElementSnapshot? stableCandidate))
        {
            return stableCandidate;
        }

        return secondProbe.MatchedElement;
    }

    private static bool TryGetStableCandidateOverlap(
        WaitProbeSnapshot firstProbe,
        WaitProbeSnapshot secondProbe,
        out UiaElementSnapshot? stableCandidate)
    {
        stableCandidate = null;

        if (firstProbe.CandidateElements.Count == 0 || secondProbe.CandidateElements.Count == 0)
        {
            return false;
        }

        Dictionary<string, UiaElementSnapshot> secondCandidates = secondProbe.CandidateElements
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.ElementId))
            .ToDictionary(candidate => candidate.ElementId!, candidate => candidate, StringComparer.Ordinal);
        List<UiaElementSnapshot> overlap = firstProbe.CandidateElements
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.ElementId))
            .Select(candidate => candidate.ElementId!)
            .Where(secondCandidates.ContainsKey)
            .Distinct(StringComparer.Ordinal)
            .Select(elementId => secondCandidates[elementId])
            .ToList();
        if (overlap.Count == 0)
        {
            return false;
        }

        if (overlap.Count == 1)
        {
            stableCandidate = overlap[0];
        }

        return true;
    }

    private async Task<UiAutomationProbeExecutionResult> ExecuteUiAutomationProbeWithinDeadlineAsync(
        WindowDescriptor liveTarget,
        WaitRequest request,
        DateTimeOffset deadlineUtc,
        CancellationToken cancellationToken)
    {
        TimeSpan remaining = deadlineUtc - timeProvider.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
        {
            return new UiAutomationProbeExecutionResult(
                new UiAutomationWaitProbeExecutionResult(
                    new UiAutomationWaitProbeResult(),
                    timeProvider.GetUtcNow(),
                    TimedOut: true),
                TimedOut: true);
        }

        using CancellationTokenSource probeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        probeCancellation.CancelAfter(remaining);

        try
        {
            UiAutomationWaitProbeExecutionResult probeResult = await uiAutomationWaitProbe
                .ProbeAsync(liveTarget, request, remaining, probeCancellation.Token)
                .ConfigureAwait(false);
            return new UiAutomationProbeExecutionResult(probeResult, TimedOut: false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && probeCancellation.IsCancellationRequested)
        {
            return new UiAutomationProbeExecutionResult(
                new UiAutomationWaitProbeExecutionResult(
                    new UiAutomationWaitProbeResult(),
                    timeProvider.GetUtcNow(),
                    TimedOut: true),
                TimedOut: true);
        }
    }

    private static string CreateTargetFailureReason(string? failureCode) =>
        failureCode switch
        {
            WaitTargetFailureValues.MissingTarget => "Для wait нужно передать hwnd, прикрепить окно или иметь единственный foreground top-level window.",
            WaitTargetFailureValues.StaleExplicitTarget => "Окно для wait по указанному hwnd больше не найдено или explicit hwnd недействителен.",
            WaitTargetFailureValues.StaleAttachedTarget => "Прикрепленное окно для wait больше не найдено или больше не совпадает с live target.",
            WaitTargetFailureValues.AmbiguousActiveTarget => "Foreground window для wait неоднозначен: найдено несколько live top-level candidates.",
            _ => "Runtime wait не смог разрешить целевой target.",
        };

    private static string CreateTargetResolutionStatus(string failureCode) =>
        string.Equals(failureCode, WaitTargetFailureValues.AmbiguousActiveTarget, StringComparison.Ordinal)
            ? WaitStatusValues.Ambiguous
            : WaitStatusValues.Failed;

    private static string CreateTimeoutReason(string condition) =>
        $"Условие wait '{condition}' не стабилизировалось до истечения timeout.";

    private static string? CreateUiAutomationProbeFailureReason(UiAutomationWaitProbeResult probeResult)
    {
        if (!string.IsNullOrWhiteSpace(probeResult.Reason))
        {
            return probeResult.Reason;
        }

        if (string.IsNullOrWhiteSpace(probeResult.FailureStage)
            || string.Equals(probeResult.FailureStage, "timeout", StringComparison.Ordinal))
        {
            return null;
        }

        return $"UIA probe завершился ошибкой на стадии '{probeResult.FailureStage}'.";
    }

    private static WaitProbeSnapshot CreateUiAutomationTimeoutSnapshot(
        DateTimeOffset observedAtUtc,
        WindowDescriptor liveTarget,
        string? diagnosticArtifactPath) =>
        new(
            Outcome: WaitProbeOutcome.TimedOut,
            ObservedAtUtc: observedAtUtc,
            Window: ToObservedWindow(liveTarget),
            MatchedElement: null,
            Observation: new WaitObservation(
                DiagnosticArtifactPath: diagnosticArtifactPath,
                Detail: "UIA probe превысил оставшийся timeout текущего wait вызова."),
            Reason: null,
            TargetFailureCode: null,
            ResolvedTargetWindow: liveTarget);

    private static WaitProbeSnapshot CreateVisualTimeoutSnapshot(
        DateTimeOffset observedAtUtc,
        WindowDescriptor liveTarget) =>
        new(
            Outcome: WaitProbeOutcome.TimedOut,
            ObservedAtUtc: observedAtUtc,
            Window: ToObservedWindow(liveTarget),
            MatchedElement: null,
            Observation: new WaitObservation(
                Detail: "Visual probe превысил оставшийся timeout текущего wait вызова.",
                VisualDifferenceThreshold: WaitVisualComparisonPolicy.DifferenceRatioThreshold,
                VisualEvidenceStatus: WaitVisualEvidenceStatusValues.Skipped),
            Reason: null,
            TargetFailureCode: null,
            ResolvedTargetWindow: liveTarget);

    private static string CreateStaleTargetFailureCode(string? targetSource) =>
        targetSource switch
        {
            WaitTargetSourceValues.Explicit => WaitTargetFailureValues.StaleExplicitTarget,
            WaitTargetSourceValues.Attached => WaitTargetFailureValues.StaleAttachedTarget,
            _ => WaitTargetFailureValues.MissingTarget,
        };

    private static ObservedWindowDescriptor ToObservedWindow(WindowDescriptor window) =>
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

    private static WaitAttemptSummary CreateAttemptSummary(int attempt, WaitProbeSnapshot probe) =>
        new(
            Attempt: attempt,
            Outcome: probe.Outcome.ToString().ToLowerInvariant(),
            ObservedAtUtc: probe.ObservedAtUtc,
            MatchCount: probe.Observation.MatchCount,
            TargetIsForeground: probe.Observation.TargetIsForeground,
            MatchedElementId: probe.MatchedElement?.ElementId,
            MatchedTextSource: probe.Observation.MatchedTextSource,
            DiagnosticArtifactPath: probe.Observation.DiagnosticArtifactPath,
            Detail: probe.Observation.Detail,
            VisualDifferenceRatio: probe.Observation.VisualDifferenceRatio,
            VisualDifferenceThreshold: probe.Observation.VisualDifferenceThreshold,
            VisualEvidenceStatus: probe.Observation.VisualEvidenceStatus,
            VisualBaselineArtifactPath: probe.Observation.VisualBaselineArtifactPath,
            VisualCurrentArtifactPath: probe.Observation.VisualCurrentArtifactPath);

    private static void DisposeVisualState(WaitVisualState? visualState) =>
        visualState?.BaselineEvidenceFrame?.Dispose();

    private static void DisposeVisualEvidenceFrame(WaitVisualEvidenceFrame? evidenceFrame) =>
        evidenceFrame?.Dispose();

    private int GetElapsedMs(long startTimestamp) =>
        (int)Math.Round(timeProvider.GetElapsedTime(startTimestamp).TotalMilliseconds, MidpointRounding.AwayFromZero);

    private enum WaitProbeOutcome
    {
        Pending,
        Candidate,
        TimedOut,
        Failed,
        Ambiguous,
    }

    private sealed record UiAutomationProbeExecutionResult(
        UiAutomationWaitProbeExecutionResult Result,
        bool TimedOut)
    {
        public DateTimeOffset CompletedAtUtc => Result.CompletedAtUtc;

        public DateTimeOffset EffectiveCompletedAtUtc => Result.WorkerCompletedAtUtc ?? Result.CompletedAtUtc;

        public string? DiagnosticArtifactPath => Result.DiagnosticArtifactPath;
    }

    private sealed record WaitVisualExecutionResult(
        WaitVisualSample? Sample,
        DateTimeOffset CompletedAtUtc,
        bool TimedOut,
        string? FailureReason);

    private sealed record WaitVisualState(
        WaitVisualComparisonData BaselineComparisonData,
        int BaselinePixelWidth,
        int BaselinePixelHeight,
        DateTimeOffset BaselineObservedAtUtc,
        WaitVisualEvidenceFrame? BaselineEvidenceFrame);

    private sealed record VisualEvidenceWriteResult(
        bool TimedOut,
        string? ArtifactPath,
        string? Reason)
    {
        public bool Success => !TimedOut && string.IsNullOrWhiteSpace(Reason);
    }

    private sealed record WaitProbeSnapshot(
        WaitProbeOutcome Outcome,
        DateTimeOffset ObservedAtUtc,
        ObservedWindowDescriptor? Window,
        UiaElementSnapshot? MatchedElement,
        WaitObservation Observation,
        string? Reason,
        string? TargetFailureCode,
        WindowDescriptor? ResolvedTargetWindow,
        WaitVisualState? VisualState = null,
        WaitVisualEvidenceFrame? VisualEvidenceFrame = null,
        IReadOnlyList<UiaElementSnapshot>? CandidateElements = null)
    {
        public IReadOnlyList<UiaElementSnapshot> CandidateElements { get; } = CandidateElements ?? [];
    }
}
