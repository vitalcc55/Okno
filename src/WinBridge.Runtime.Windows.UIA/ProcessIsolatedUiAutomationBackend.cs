using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;

namespace WinBridge.Runtime.Windows.UIA;

internal sealed class ProcessIsolatedUiAutomationBackend : IUiaSnapshotBackend
{
    private static readonly Encoding Utf8Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly TimeProvider _timeProvider;
    private readonly UiAutomationExecutionOptions _executionOptions;
    private readonly Func<UiaWorkerLaunchSpec> _workerLaunchSpecResolver;
    private readonly UiaWorkerProcessDiagnosticArtifactWriter? _diagnosticArtifactWriter;

    public ProcessIsolatedUiAutomationBackend(TimeProvider timeProvider, AuditLogOptions auditLogOptions)
        : this(
            timeProvider,
            UiAutomationExecutionOptions.Default,
            ResolveWorkerLaunchSpec,
            diagnosticAuditLogOptions: auditLogOptions)
    {
    }

    internal ProcessIsolatedUiAutomationBackend(
        TimeProvider timeProvider,
        UiAutomationExecutionOptions executionOptions,
        string workerExecutablePath,
        string? workerArguments,
        AuditLogOptions? diagnosticAuditLogOptions = null)
        : this(
            timeProvider,
            executionOptions,
            () => new UiaWorkerLaunchSpec(workerExecutablePath, workerArguments ?? string.Empty),
            diagnosticAuditLogOptions)
    {
    }

    internal ProcessIsolatedUiAutomationBackend(
        TimeProvider timeProvider,
        UiAutomationExecutionOptions executionOptions,
        Func<UiaWorkerLaunchSpec> workerLaunchSpecResolver,
        AuditLogOptions? diagnosticAuditLogOptions = null)
    {
        _timeProvider = timeProvider;
        _executionOptions = executionOptions;
        _workerLaunchSpecResolver = workerLaunchSpecResolver;
        _diagnosticArtifactWriter = diagnosticAuditLogOptions is null
            ? null
            : new UiaWorkerProcessDiagnosticArtifactWriter(diagnosticAuditLogOptions);
    }

    public async Task<UiaSnapshotBackendResult> CaptureAsync(
        WindowDescriptor targetWindow,
        UiaSnapshotRequest request,
        CancellationToken cancellationToken)
    {
        DateTimeOffset capturedAtUtc = _timeProvider.GetUtcNow();
        UiaWorkerLaunchSpec workerLaunchSpec;
        try
        {
            workerLaunchSpec = _workerLaunchSpecResolver();
        }
        catch (FileNotFoundException)
        {
            return Failed("UIA worker process не найден рядом с host output.", UiaSnapshotFailureStageValues.WorkerProcess, capturedAtUtc);
        }

        using Process process = new()
        {
            StartInfo = CreateWorkerStartInfo(workerLaunchSpec),
        };

        try
        {
            if (!process.Start())
            {
                return Failed("UIA worker process не запустился.", UiaSnapshotFailureStageValues.WorkerProcess, capturedAtUtc);
            }
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or FileNotFoundException or DirectoryNotFoundException)
        {
            return Failed("UIA worker process не удалось запустить.", UiaSnapshotFailureStageValues.WorkerProcess, capturedAtUtc);
        }

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        Task<string> stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

        string payload = JsonSerializer.Serialize(new UiaSnapshotWorkerInvocation(targetWindow, request), JsonOptions);
        UiaSnapshotBackendResult? sendFailure = await TrySendInvocationAsync(
            process,
            payload,
            targetWindow,
            capturedAtUtc,
            stdoutTask,
            stderrTask).ConfigureAwait(false);
        if (sendFailure is not null)
        {
            return sendFailure;
        }

        using CancellationTokenSource boundedExecution = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (_executionOptions.Timeout is TimeSpan timeout)
        {
            boundedExecution.CancelAfter(timeout);
        }

        try
        {
            await process.WaitForExitAsync(boundedExecution.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            return Failed("UI Automation worker process не уложился в допустимый timeout.", UiaSnapshotFailureStageValues.Timeout, capturedAtUtc);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        string stdout = await stdoutTask.ConfigureAwait(false);
        string stderr = await stderrTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            return CreateWorkerProcessFailure(
                reason: "UIA worker process завершился с ошибкой.",
                targetWindow,
                capturedAtUtc,
                stdout,
                stderr,
                exitCode: process.ExitCode);
        }

        try
        {
            UiaSnapshotBackendResult? result = JsonSerializer.Deserialize<UiaSnapshotBackendResult>(stdout, JsonOptions);
            return result ?? CreateWorkerProcessFailure(
                reason: "UIA worker process вернул пустой result payload.",
                targetWindow,
                capturedAtUtc,
                stdout,
                stderr,
                exitCode: process.ExitCode);
        }
        catch (JsonException)
        {
            return CreateWorkerProcessFailure(
                reason: "UIA worker process вернул некорректный result payload.",
                targetWindow,
                capturedAtUtc,
                stdout,
                stderr,
                exitCode: process.ExitCode);
        }
    }

    internal static ProcessStartInfo CreateWorkerStartInfo(UiaWorkerLaunchSpec workerLaunchSpec) =>
        new()
        {
            FileName = workerLaunchSpec.FileName,
            Arguments = workerLaunchSpec.Arguments,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Utf8Encoding,
            StandardOutputEncoding = Utf8Encoding,
            StandardErrorEncoding = Utf8Encoding,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

    private async Task<UiaSnapshotBackendResult?> TrySendInvocationAsync(
        Process process,
        string payload,
        WindowDescriptor targetWindow,
        DateTimeOffset capturedAtUtc,
        Task<string> stdoutTask,
        Task<string> stderrTask)
    {
        try
        {
            await process.StandardInput.WriteAsync(payload).ConfigureAwait(false);
            await process.StandardInput.FlushAsync(CancellationToken.None).ConfigureAwait(false);
            process.StandardInput.Close();
            return null;
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
            TryKill(process);
            string stdout = await SafeReadAsync(stdoutTask).ConfigureAwait(false);
            string stderr = await SafeReadAsync(stderrTask).ConfigureAwait(false);
            return CreateWorkerProcessFailure(
                reason: "UIA worker process не смог принять invocation payload.",
                targetWindow,
                capturedAtUtc,
                stdout,
                stderr,
                exitCode: TryGetExitCode(process),
                exception: exception);
        }
    }

    private UiaSnapshotBackendResult CreateWorkerProcessFailure(
        string reason,
        WindowDescriptor targetWindow,
        DateTimeOffset capturedAtUtc,
        string? stdout,
        string? stderr,
        int? exitCode,
        Exception? exception = null)
    {
        string? diagnosticArtifactPath = _diagnosticArtifactWriter?.TryWrite(
            new UiaWorkerProcessDiagnosticArtifact(
                Kind: "worker_process_failure",
                FailureStage: UiaSnapshotFailureStageValues.WorkerProcess,
                ArtifactPath: null,
                CapturedAtUtc: capturedAtUtc,
                WindowHwnd: targetWindow.Hwnd,
                ExitCode: exitCode,
                Stdout: string.IsNullOrWhiteSpace(stdout) ? null : stdout,
                Stderr: string.IsNullOrWhiteSpace(stderr) ? null : stderr,
                ExceptionType: exception?.GetType().FullName,
                ExceptionMessage: exception?.Message));

        return new UiaSnapshotBackendResult(
            Success: false,
            Reason: reason,
            FailureStage: UiaSnapshotFailureStageValues.WorkerProcess,
            CapturedAtUtc: capturedAtUtc,
            DiagnosticArtifactPath: diagnosticArtifactPath);
    }

    private static int? TryGetExitCode(Process process)
    {
        try
        {
            return process.HasExited ? process.ExitCode : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (Win32Exception)
        {
            return null;
        }
    }

    private static async Task<string> SafeReadAsync(Task<string> streamTask)
    {
        try
        {
            return await streamTask.ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            return string.Empty;
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (Win32Exception)
        {
        }
    }

    private static UiaSnapshotBackendResult Failed(string reason, string failureStage, DateTimeOffset capturedAtUtc) =>
        new(
            Success: false,
            Reason: reason,
            FailureStage: failureStage,
            CapturedAtUtc: capturedAtUtc,
            Root: null,
            RealizedDepth: 0,
            NodeCount: 0,
            Truncated: false,
            DepthBoundaryReached: false,
            NodeBudgetBoundaryReached: false,
            DiagnosticArtifactPath: null);

    internal static UiaWorkerLaunchSpec ResolveWorkerLaunchSpec()
    {
        return ResolveWorkerLaunchSpec(AppContext.BaseDirectory, Environment.ProcessPath);
    }

    internal static UiaWorkerLaunchSpec ResolveWorkerLaunchSpec(string baseDirectory, string? currentHostPath)
    {
        string executablePath = Path.Combine(baseDirectory, "WinBridge.Runtime.Windows.UIA.Worker.exe");
        if (File.Exists(executablePath))
        {
            return new UiaWorkerLaunchSpec(executablePath, string.Empty);
        }

        string workerDllPath = Path.Combine(baseDirectory, "WinBridge.Runtime.Windows.UIA.Worker.dll");
        string workerRuntimeConfigPath = Path.Combine(baseDirectory, "WinBridge.Runtime.Windows.UIA.Worker.runtimeconfig.json");
        if (File.Exists(workerDllPath) && File.Exists(workerRuntimeConfigPath) && IsDotNetHost(currentHostPath))
        {
            return new UiaWorkerLaunchSpec(currentHostPath!, QuoteArgument(workerDllPath));
        }

        throw new FileNotFoundException("Не найден launchable `WinBridge.Runtime.Windows.UIA.Worker` рядом с host output для isolated UIA execution boundary.");
    }

    private static bool IsDotNetHost(string? currentHostPath)
    {
        if (string.IsNullOrWhiteSpace(currentHostPath))
        {
            return false;
        }

        string fileName = Path.GetFileName(currentHostPath);
        return string.Equals(fileName, "dotnet", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, "dotnet.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static string QuoteArgument(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }
}

internal sealed record UiaWorkerLaunchSpec(string FileName, string Arguments);
