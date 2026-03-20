using System.Globalization;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Windows.Shell;
using WinBridge.Runtime.Windows.UIA;

namespace WinBridge.Runtime.Waiting;

public sealed class PollingWaitService(
    IWindowManager windowManager,
    IWindowTargetResolver windowTargetResolver,
    IUiAutomationWaitProbe uiAutomationWaitProbe,
    AuditLog auditLog,
    AuditLogOptions auditLogOptions,
    TimeProvider timeProvider,
    WaitOptions options) : IWaitService
{
    private const string RuntimeCompletedEventName = "wait.runtime.completed";
    private readonly WaitArtifactWriter _artifactWriter = new(auditLogOptions);

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
                    Status: WaitStatusValues.Failed,
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
        WaitProbeSnapshot? lastProbe = null;
        int attemptCount = 0;
        DateTimeOffset deadlineUtc = startedAtUtc + TimeSpan.FromMilliseconds(request.TimeoutMs);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            WaitProbeSnapshot probe = await ProbeOnceAsync(expectedWindow, target.Source, request, deadlineUtc, cancellationToken).ConfigureAwait(false);
            expectedWindow = probe.ResolvedTargetWindow ?? expectedWindow;
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
                WaitProbeSnapshot recheck = await ProbeOnceAsync(expectedWindow, target.Source, request, deadlineUtc, cancellationToken).ConfigureAwait(false);
                expectedWindow = recheck.ResolvedTargetWindow ?? expectedWindow;
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
                    WaitResult doneResult = new(
                        Status: WaitStatusValues.Done,
                        Condition: request.Condition,
                        TargetSource: target.Source,
                        TargetFailureCode: null,
                        Reason: null,
                        Window: recheck.Window,
                        MatchedElement: recheck.MatchedElement,
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

    private async Task<WaitProbeSnapshot> ProbeOnceAsync(
        WindowDescriptor expectedWindow,
        string? targetSource,
        WaitRequest request,
        DateTimeOffset deadlineUtc,
        CancellationToken cancellationToken)
    {
        if (string.Equals(request.Condition, WaitConditionValues.ActiveWindowMatches, StringComparison.Ordinal))
        {
            return ProbeActiveWindow(expectedWindow, targetSource);
        }

        if (string.Equals(request.Condition, WaitConditionValues.ElementExists, StringComparison.Ordinal)
            || string.Equals(request.Condition, WaitConditionValues.ElementGone, StringComparison.Ordinal)
            || string.Equals(request.Condition, WaitConditionValues.TextAppears, StringComparison.Ordinal))
        {
            return await ProbeUiAutomationAsync(expectedWindow, targetSource, request, deadlineUtc, cancellationToken).ConfigureAwait(false);
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
        if (probeExecution.CompletedAtUtc > deadlineUtc && ShouldDowngradeLateUiAutomationProbeToTimeout(semanticSnapshot))
        {
            return CreateUiAutomationTimeoutSnapshot(
                observedAtUtc,
                liveTarget,
                semanticSnapshot.Observation.DiagnosticArtifactPath);
        }

        return semanticSnapshot;
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
        UiaElementSnapshot? matchedElement = matchCount > 0 ? probeResult.Matches[0] : null;
        WaitObservation observation = new(
            MatchCount: matchCount,
            MatchedText: probeResult.MatchedText,
            MatchedTextSource: probeResult.MatchedTextSource,
            DiagnosticArtifactPath: diagnosticArtifactPath,
            Detail: CreateUiObservationDetail(request.Condition, matchCount, probeResult.MatchedTextSource));

        if (matchCount > 1)
        {
            return new WaitProbeSnapshot(
                Outcome: WaitProbeOutcome.Ambiguous,
                ObservedAtUtc: observedAtUtc,
                Window: observedWindow,
                MatchedElement: matchedElement,
                Observation: observation,
                Reason: "UIA selector совпал с несколькими live elements.",
                TargetFailureCode: null,
                ResolvedTargetWindow: liveTarget);
        }

        if (string.Equals(request.Condition, WaitConditionValues.ElementExists, StringComparison.Ordinal))
        {
            return new WaitProbeSnapshot(
                Outcome: matchCount == 1 ? WaitProbeOutcome.Candidate : WaitProbeOutcome.Pending,
                ObservedAtUtc: observedAtUtc,
                Window: observedWindow,
                MatchedElement: matchedElement,
                Observation: observation,
                Reason: null,
                TargetFailureCode: null,
                ResolvedTargetWindow: liveTarget);
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
                ResolvedTargetWindow: liveTarget);
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
            ResolvedTargetWindow: liveTarget);
    }

    private static bool ShouldDowngradeLateUiAutomationProbeToTimeout(WaitProbeSnapshot semanticSnapshot) =>
        semanticSnapshot.Outcome is WaitProbeOutcome.Pending or WaitProbeOutcome.Candidate;

    private WaitResult FinalizeResult(
        WaitRequest request,
        WaitTargetResolution target,
        IReadOnlyList<WaitAttemptSummary> attempts,
        DateTimeOffset startedAtUtc,
        WaitResult result,
        string? failureStage = null)
    {
        try
        {
            string artifactPath = _artifactWriter.Write(request, target, options, attempts, result, startedAtUtc);
            WaitResult materialized = result with { ArtifactPath = artifactPath };
            RecordRuntimeEvent(materialized, failureStage);
            return materialized;
        }
        catch (WaitArtifactException exception)
        {
            WaitResult artifactFailure = result with
            {
                Status = WaitStatusValues.Failed,
                Reason = exception.Message,
                ArtifactPath = null,
            };
            RecordRuntimeEvent(artifactFailure, "artifact_write");
            return artifactFailure;
        }
    }

    private void RecordRuntimeEvent(WaitResult result, string? failureStage)
    {
        string severity = result.Status == WaitStatusValues.Done ? "info" : "warning";
        string message = result.Status == WaitStatusValues.Done
            ? "Runtime wait condition подтверждено."
            : result.Reason ?? "Runtime wait завершился без подтверждения condition.";

        auditLog.RecordRuntimeEvent(
            eventName: RuntimeCompletedEventName,
            severity: severity,
            messageHuman: message,
            toolName: "windows.wait",
            outcome: result.Status,
            windowHwnd: result.Window?.Hwnd,
            data: new Dictionary<string, string?>
            {
                ["condition"] = result.Condition,
                ["target_source"] = result.TargetSource,
                ["target_failure_code"] = result.TargetFailureCode,
                ["attempt_count"] = result.AttemptCount.ToString(CultureInfo.InvariantCulture),
                ["elapsed_ms"] = result.ElapsedMs.ToString(CultureInfo.InvariantCulture),
                ["artifact_path"] = result.ArtifactPath,
                ["failure_stage"] = failureStage,
                ["matched_element_id"] = result.MatchedElement?.ElementId,
                ["matched_text_source"] = result.LastObserved?.MatchedTextSource,
                ["diagnostic_artifact_path"] = result.LastObserved?.DiagnosticArtifactPath,
            });
    }

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
            || string.Equals(condition, WaitConditionValues.ElementGone, StringComparison.Ordinal))
        {
            return true;
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
            Detail: probe.Observation.Detail);

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

        public string? DiagnosticArtifactPath => Result.DiagnosticArtifactPath;
    }

    private sealed record WaitProbeSnapshot(
        WaitProbeOutcome Outcome,
        DateTimeOffset ObservedAtUtc,
        ObservedWindowDescriptor? Window,
        UiaElementSnapshot? MatchedElement,
        WaitObservation Observation,
        string? Reason,
        string? TargetFailureCode,
        WindowDescriptor? ResolvedTargetWindow);
}
