using System.ComponentModel;
using System.Diagnostics;
using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Launch;

public sealed class ProcessLaunchService : IProcessLaunchService
{
    private readonly IProcessLaunchPlatform _platform;
    private readonly TimeProvider _timeProvider;
    private readonly ProcessLaunchOptions _options;

    internal ProcessLaunchService(
        IProcessLaunchPlatform platform,
        TimeProvider timeProvider,
        ProcessLaunchOptions options)
    {
        _platform = platform;
        _timeProvider = timeProvider;
        _options = options;
    }

    public async Task<LaunchProcessResult> LaunchAsync(LaunchProcessRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string executableIdentity = LaunchProcessExecutableTarget.TryResolveSafeExecutableIdentity(request.Executable) ?? string.Empty;
        string notRequestedStatus = request.WaitForWindow
            ? null!
            : LaunchMainWindowObservationStatusValues.NotRequested;

        if (!LaunchProcessRequestValidator.TryValidate(request, out string? failureCode, out string? reason))
        {
            return CreateFailureResult(
                failureCode ?? LaunchProcessFailureCodeValues.InvalidRequest,
                reason ?? "Launch request не прошёл validation.",
                executableIdentity,
                mainWindowObservationStatus: request.WaitForWindow ? null : notRequestedStatus);
        }

        cancellationToken.ThrowIfCancellationRequested();

        ProcessStartInfo startInfo = CreateStartInfo(request);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using IStartedProcessHandle? processHandle = _platform.Start(startInfo);
            if (processHandle is null)
            {
                return CreateFailureResult(
                    LaunchProcessFailureCodeValues.ProcessObjectUnavailable,
                    "Runtime не получил process object после Process.Start(...).",
                    executableIdentity,
                    mainWindowObservationStatus: request.WaitForWindow ? null : notRequestedStatus);
            }

            int? processId = TryGetProcessId(processHandle);
            if (processId is null or <= 0)
            {
                return CreateFailureResult(
                    LaunchProcessFailureCodeValues.ProcessObjectUnavailable,
                    "Runtime не смог зафиксировать stable process id после старта.",
                    executableIdentity,
                    mainWindowObservationStatus: request.WaitForWindow ? null : notRequestedStatus);
            }

            DateTimeOffset startedAtUtc = _timeProvider.GetUtcNow();
            if (request.WaitForWindow)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return CreateStartedResult(
                        executableIdentity,
                        processId.Value,
                        startedAtUtc,
                        CaptureFreshResultState(processHandle),
                        LaunchMainWindowObservationStatusValues.NotObserved);
                }

                return await ObserveMainWindowAsync(
                    processHandle,
                    request,
                    executableIdentity,
                    processId.Value,
                    startedAtUtc,
                    cancellationToken).ConfigureAwait(false);
            }

            return CreateStartedResult(
                executableIdentity,
                processId.Value,
                startedAtUtc,
                CaptureFreshResultState(processHandle),
                LaunchMainWindowObservationStatusValues.NotRequested);
        }
        catch (Exception exception) when (TryMapStartFailure(exception, startInfo, out string mappedFailureCode, out string mappedReason))
        {
            return CreateFailureResult(
                mappedFailureCode,
                mappedReason,
                executableIdentity,
                mainWindowObservationStatus: request.WaitForWindow ? null : notRequestedStatus);
        }
    }

    private async Task<LaunchProcessResult> ObserveMainWindowAsync(
        IStartedProcessHandle processHandle,
        LaunchProcessRequest request,
        string executableIdentity,
        int processId,
        DateTimeOffset startedAtUtc,
        CancellationToken cancellationToken)
    {
        int timeoutMs = request.TimeoutMs ?? LaunchProcessDefaults.TimeoutMs;
        DateTimeOffset deadlineUtc = _timeProvider.GetUtcNow().AddMilliseconds(timeoutMs);
        WaitForInputIdleOutcome waitOutcome = await WaitForInputIdleAsync(
            processHandle,
            deadlineUtc,
            cancellationToken).ConfigureAwait(false);

        switch (waitOutcome.Kind)
        {
            case WaitForInputIdleOutcomeKind.ProcessExited:
                return CreateFailureResult(
                    LaunchProcessFailureCodeValues.ProcessExitedBeforeWindow,
                    "Процесс завершился до наблюдения main window handle.",
                    executableIdentity,
                    processId,
                    startedAtUtc,
                    waitOutcome.Snapshot.HasExited,
                    waitOutcome.Snapshot.ExitCode,
                    mainWindowObservationStatus: LaunchMainWindowObservationStatusValues.ProcessExited);
            case WaitForInputIdleOutcomeKind.Cancelled:
                return CreateStartedResult(
                    executableIdentity,
                    processId,
                    startedAtUtc,
                    CaptureFreshResultState(processHandle),
                    LaunchMainWindowObservationStatusValues.NotObserved);
            case WaitForInputIdleOutcomeKind.InputIdleUnavailable:
            case WaitForInputIdleOutcomeKind.TimedOut:
            case WaitForInputIdleOutcomeKind.IdleReached:
                break;
        }

        while (true)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return CreateStartedResult(
                    executableIdentity,
                    processId,
                    startedAtUtc,
                    CaptureFreshResultState(processHandle),
                    LaunchMainWindowObservationStatusValues.NotObserved);
            }

            ProcessStateSnapshot pendingSnapshot = CaptureFreshObservationState(processHandle);
            if (pendingSnapshot.HasExited == true)
            {
                return CreateFailureResult(
                    LaunchProcessFailureCodeValues.ProcessExitedBeforeWindow,
                    "Процесс завершился до наблюдения main window handle.",
                    executableIdentity,
                    processId,
                    startedAtUtc,
                    pendingSnapshot.HasExited,
                    pendingSnapshot.ExitCode,
                    mainWindowObservationStatus: LaunchMainWindowObservationStatusValues.ProcessExited);
            }

            if (pendingSnapshot.MainWindowHandle is > 0 && pendingSnapshot.CapturedAtUtc <= deadlineUtc)
            {
                return CreateWindowObservedResult(
                    executableIdentity,
                    processId,
                    startedAtUtc,
                    pendingSnapshot);
            }

            if (pendingSnapshot.CapturedAtUtc >= deadlineUtc)
            {
                return CreateFailureResult(
                    waitOutcome.Kind == WaitForInputIdleOutcomeKind.TimedOut
                        ? LaunchProcessFailureCodeValues.MainWindowTimeout
                        : LaunchProcessFailureCodeValues.MainWindowNotObserved,
                    waitOutcome.Kind == WaitForInputIdleOutcomeKind.TimedOut
                        ? "Main window не появился в пределах timeout после WaitForInputIdle."
                        : "Процесс стартовал, но main window handle не был observed в пределах timeout.",
                    executableIdentity,
                    processId,
                    startedAtUtc,
                    pendingSnapshot.HasExited,
                    pendingSnapshot.ExitCode,
                    mainWindowObservationStatus: waitOutcome.Kind == WaitForInputIdleOutcomeKind.TimedOut
                        ? LaunchMainWindowObservationStatusValues.TimedOut
                        : LaunchMainWindowObservationStatusValues.NotObserved);
            }

            TimeSpan delay = GetRemainingDelay(deadlineUtc, _options.MainWindowPollInterval);
            if (delay <= TimeSpan.Zero)
            {
                continue;
            }

            try
            {
                await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return CreateStartedResult(
                    executableIdentity,
                    processId,
                    startedAtUtc,
                    CaptureFreshResultState(processHandle),
                    LaunchMainWindowObservationStatusValues.NotObserved);
            }
        }
    }

    private static ProcessStartInfo CreateStartInfo(LaunchProcessRequest request)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = request.Executable,
            UseShellExecute = false,
        };

        if (request.WorkingDirectory is not null)
        {
            startInfo.WorkingDirectory = request.WorkingDirectory;
        }

        foreach (string argument in request.Args)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private async Task<WaitForInputIdleOutcome> WaitForInputIdleAsync(
        IStartedProcessHandle processHandle,
        DateTimeOffset deadlineUtc,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return new WaitForInputIdleOutcome(
                    WaitForInputIdleOutcomeKind.Cancelled,
                    CaptureResultState(processHandle));
            }

            int? sliceMilliseconds = GetRemainingSliceMilliseconds(deadlineUtc, _options.InputIdleWaitSlice);
            if (sliceMilliseconds is null)
            {
                return new WaitForInputIdleOutcome(
                    WaitForInputIdleOutcomeKind.TimedOut,
                    CaptureResultState(processHandle));
            }

            try
            {
                if (processHandle.WaitForInputIdle(sliceMilliseconds.Value))
                {
                    return new WaitForInputIdleOutcome(
                        WaitForInputIdleOutcomeKind.IdleReached,
                        CaptureResultState(processHandle));
                }
            }
            catch (InvalidOperationException)
            {
                ProcessStateSnapshot invalidOperationSnapshot = CaptureResultState(processHandle);
                return new WaitForInputIdleOutcome(
                    invalidOperationSnapshot.HasExited == true
                        ? WaitForInputIdleOutcomeKind.ProcessExited
                        : WaitForInputIdleOutcomeKind.InputIdleUnavailable,
                    invalidOperationSnapshot);
            }

            ProcessStateSnapshot snapshot = CaptureResultState(processHandle);
            if (snapshot.HasExited == true)
            {
                return new WaitForInputIdleOutcome(
                    WaitForInputIdleOutcomeKind.ProcessExited,
                    snapshot);
            }

            await Task.Yield();
        }
    }

    private static bool TryMapStartFailure(Exception exception, ProcessStartInfo startInfo, out string failureCode, out string reason)
    {
        switch (exception)
        {
            case FileNotFoundException:
                failureCode = LaunchProcessFailureCodeValues.ExecutableNotFound;
                reason = "Executable для launch_process не найден при direct start.";
                return true;
            case DirectoryNotFoundException:
                failureCode = LaunchProcessFailureCodeValues.WorkingDirectoryNotFound;
                reason = "Working directory для launch_process не найден во время старта.";
                return true;
            case Win32Exception win32Exception when win32Exception.NativeErrorCode == 2:
                failureCode = LaunchProcessFailureCodeValues.ExecutableNotFound;
                reason = "Executable для launch_process не найден при direct start.";
                return true;
            case Win32Exception win32Exception when win32Exception.NativeErrorCode == 3:
                return TryMapPathNotFoundFailure(startInfo, out failureCode, out reason);
            case Win32Exception win32Exception when win32Exception.NativeErrorCode == 267:
                failureCode = LaunchProcessFailureCodeValues.WorkingDirectoryNotFound;
                reason = "Working directory для launch_process не найден во время старта.";
                return true;
            case Win32Exception:
            case InvalidOperationException:
                failureCode = LaunchProcessFailureCodeValues.StartFailed;
                reason = "Runtime не смог запустить процесс через direct ProcessStartInfo semantics.";
                return true;
            default:
                failureCode = string.Empty;
                reason = string.Empty;
                return false;
        }
    }

    private static bool TryMapPathNotFoundFailure(ProcessStartInfo startInfo, out string failureCode, out string reason)
    {
        if (string.IsNullOrWhiteSpace(startInfo.WorkingDirectory))
        {
            failureCode = LaunchProcessFailureCodeValues.ExecutableNotFound;
            reason = "Executable для launch_process не найден при direct start.";
            return true;
        }

        failureCode = LaunchProcessFailureCodeValues.StartFailed;
        reason = !Path.IsPathFullyQualified(startInfo.FileName)
            ? "Runtime не смог однозначно классифицировать ERROR_PATH_NOT_FOUND для bare executable name и working directory."
            : "Runtime не смог однозначно классифицировать ERROR_PATH_NOT_FOUND между executable path и working directory.";
        return true;
    }

    private static int? TryGetProcessId(IStartedProcessHandle processHandle)
    {
        try
        {
            return processHandle.Id;
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            return null;
        }
    }

    private static bool? TryGetHasExited(IStartedProcessHandle processHandle)
    {
        try
        {
            return processHandle.HasExited;
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            return null;
        }
    }

    private static int? TryGetExitCode(IStartedProcessHandle processHandle)
    {
        try
        {
            return processHandle.ExitCode;
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            return null;
        }
    }

    private static long? TryGetMainWindowHandle(IStartedProcessHandle processHandle)
    {
        try
        {
            return processHandle.MainWindowHandle;
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            return null;
        }
    }

    private ProcessStateSnapshot CaptureResultState(IStartedProcessHandle processHandle) =>
        CaptureProcessState(processHandle, refresh: false, includeMainWindowHandle: false);

    private ProcessStateSnapshot CaptureFreshResultState(IStartedProcessHandle processHandle) =>
        CaptureProcessState(processHandle, refresh: true, includeMainWindowHandle: false);

    private ProcessStateSnapshot CaptureFreshObservationState(IStartedProcessHandle processHandle) =>
        CaptureProcessState(processHandle, refresh: true, includeMainWindowHandle: true);

    private ProcessStateSnapshot CaptureProcessState(
        IStartedProcessHandle processHandle,
        bool refresh,
        bool includeMainWindowHandle)
    {
        if (refresh)
        {
            processHandle.Refresh();
        }

        bool? hasExited = TryGetHasExited(processHandle);
        int? exitCode = hasExited == true ? TryGetExitCode(processHandle) : null;
        long? mainWindowHandle = null;

        if (includeMainWindowHandle && hasExited != true)
        {
            mainWindowHandle = TryCaptureObservationMainWindowHandle(processHandle, ref hasExited, ref exitCode);
        }

        return new ProcessStateSnapshot(
            CapturedAtUtc: _timeProvider.GetUtcNow(),
            HasExited: hasExited,
            ExitCode: exitCode,
            MainWindowHandle: mainWindowHandle);
    }

    private TimeSpan GetRemainingDelay(DateTimeOffset deadlineUtc, TimeSpan slice)
    {
        TimeSpan remaining = deadlineUtc - _timeProvider.GetUtcNow();
        return remaining < slice ? remaining : slice;
    }

    private static long? TryCaptureObservationMainWindowHandle(
        IStartedProcessHandle processHandle,
        ref bool? hasExited,
        ref int? exitCode)
    {
        try
        {
            long mainWindowHandle = processHandle.MainWindowHandle;
            bool? exitedAfterHandleRead = TryGetHasExited(processHandle);
            if (exitedAfterHandleRead == true)
            {
                hasExited = true;
                exitCode = TryGetExitCode(processHandle);
                return null;
            }

            if (hasExited is null)
            {
                hasExited = exitedAfterHandleRead;
            }

            return mainWindowHandle;
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            bool? exitedAfterHandleFailure = TryGetHasExited(processHandle);
            if (exitedAfterHandleFailure == true)
            {
                hasExited = true;
                exitCode = TryGetExitCode(processHandle);
            }
            else if (hasExited is null)
            {
                hasExited = exitedAfterHandleFailure;
            }

            return null;
        }
    }

    private int? GetRemainingSliceMilliseconds(DateTimeOffset deadlineUtc, TimeSpan configuredSlice)
    {
        TimeSpan remaining = deadlineUtc - _timeProvider.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
        {
            return null;
        }

        TimeSpan slice = remaining < configuredSlice ? remaining : configuredSlice;
        return Math.Max(1, (int)Math.Ceiling(slice.TotalMilliseconds));
    }

    private static LaunchProcessResult CreateStartedResult(
        string executableIdentity,
        int processId,
        DateTimeOffset startedAtUtc,
        ProcessStateSnapshot snapshot,
        string mainWindowObservationStatus)
    {
        string resultMode = snapshot.HasExited == true
            ? LaunchProcessResultModeValues.ProcessStartedAndExited
            : LaunchProcessResultModeValues.ProcessStarted;

        return new LaunchProcessResult(
            Status: LaunchProcessStatusValues.Done,
            Decision: LaunchProcessStatusValues.Done,
            ResultMode: resultMode,
            ExecutableIdentity: executableIdentity,
            ProcessId: processId,
            StartedAtUtc: startedAtUtc,
            HasExited: snapshot.HasExited,
            ExitCode: snapshot.ExitCode,
            MainWindowObserved: false,
            MainWindowHandle: null,
            MainWindowObservationStatus: mainWindowObservationStatus);
    }

    private static LaunchProcessResult CreateWindowObservedResult(
        string executableIdentity,
        int processId,
        DateTimeOffset startedAtUtc,
        ProcessStateSnapshot snapshot) =>
        new(
            Status: LaunchProcessStatusValues.Done,
            Decision: LaunchProcessStatusValues.Done,
            ResultMode: LaunchProcessResultModeValues.WindowObserved,
            ExecutableIdentity: executableIdentity,
            ProcessId: processId,
            StartedAtUtc: startedAtUtc,
            HasExited: snapshot.HasExited,
            ExitCode: snapshot.ExitCode,
            MainWindowObserved: true,
            MainWindowHandle: snapshot.MainWindowHandle,
            MainWindowObservationStatus: LaunchMainWindowObservationStatusValues.Observed);

    private static LaunchProcessResult CreateFailureResult(
        string failureCode,
        string reason,
        string executableIdentity,
        int? processId = null,
        DateTimeOffset? startedAtUtc = null,
        bool? hasExited = null,
        int? exitCode = null,
        string? mainWindowObservationStatus = null) =>
        new(
            Status: LaunchProcessStatusValues.Failed,
            Decision: LaunchProcessStatusValues.Failed,
            FailureCode: failureCode,
            Reason: reason,
            ExecutableIdentity: string.IsNullOrWhiteSpace(executableIdentity) ? null : executableIdentity,
            ProcessId: processId,
            StartedAtUtc: startedAtUtc,
            HasExited: hasExited,
            ExitCode: exitCode,
            MainWindowObserved: false,
            MainWindowHandle: null,
            MainWindowObservationStatus: mainWindowObservationStatus);

    private readonly record struct ProcessStateSnapshot(
        DateTimeOffset CapturedAtUtc,
        bool? HasExited,
        int? ExitCode,
        long? MainWindowHandle);

    private readonly record struct WaitForInputIdleOutcome(
        WaitForInputIdleOutcomeKind Kind,
        ProcessStateSnapshot Snapshot);

    private enum WaitForInputIdleOutcomeKind
    {
        IdleReached,
        TimedOut,
        InputIdleUnavailable,
        ProcessExited,
        Cancelled,
    }
}
