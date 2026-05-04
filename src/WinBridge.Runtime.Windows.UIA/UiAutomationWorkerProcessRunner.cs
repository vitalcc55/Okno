// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using WinBridge.Runtime.Diagnostics;

namespace WinBridge.Runtime.Windows.UIA;

internal sealed class UiAutomationWorkerProcessRunner : IUiAutomationWorkerProcessRunner
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

    public UiAutomationWorkerProcessRunner(TimeProvider timeProvider, AuditLogOptions auditLogOptions)
        : this(
            timeProvider,
            UiAutomationExecutionOptions.Default,
            ResolveWorkerLaunchSpec,
            diagnosticAuditLogOptions: auditLogOptions)
    {
    }

    internal UiAutomationWorkerProcessRunner(
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

    internal UiAutomationWorkerProcessRunner(
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

    public async Task<UiAutomationWorkerProcessResult> ExecuteAsync(
        object invocation,
        long? windowHwnd,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);

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

        string payload = JsonSerializer.Serialize(invocation, JsonOptions);
        UiAutomationWorkerProcessResult? sendFailure = await TrySendInvocationAsync(
            process,
            payload,
            windowHwnd,
            capturedAtUtc,
            stdoutTask,
            stderrTask).ConfigureAwait(false);
        if (sendFailure is not null)
        {
            return sendFailure;
        }

        using CancellationTokenSource boundedExecution = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        TimeSpan? effectiveTimeout = timeout ?? _executionOptions.Timeout;
        if (effectiveTimeout is TimeSpan executionTimeout)
        {
            boundedExecution.CancelAfter(executionTimeout);
        }

        try
        {
            await process.WaitForExitAsync(boundedExecution.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            string stdout = await SafeReadAsync(stdoutTask).ConfigureAwait(false);
            string stderr = await SafeReadAsync(stderrTask).ConfigureAwait(false);
            return CreateWorkerProcessFailure(
                reason: "UI Automation worker process не уложился в допустимый timeout.",
                failureStage: UiaSnapshotFailureStageValues.Timeout,
                windowHwnd: windowHwnd,
                capturedAtUtc: capturedAtUtc,
                stdout: stdout,
                stderr: stderr,
                exitCode: TryGetExitCode(process));
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        string completedStdout = await stdoutTask.ConfigureAwait(false);
        string completedStderr = await stderrTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            return CreateWorkerProcessFailure(
                reason: "UIA worker process завершился с ошибкой.",
                failureStage: UiaSnapshotFailureStageValues.WorkerProcess,
                windowHwnd: windowHwnd,
                capturedAtUtc: capturedAtUtc,
                stdout: completedStdout,
                stderr: completedStderr,
                exitCode: process.ExitCode);
        }

        return new UiAutomationWorkerProcessResult(
            Success: true,
            Reason: null,
            FailureStage: null,
            CapturedAtUtc: capturedAtUtc,
            CompletedAtUtc: _timeProvider.GetUtcNow(),
            Stdout: completedStdout,
            Stderr: completedStderr,
            DiagnosticArtifactPath: null);
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

    private async Task<UiAutomationWorkerProcessResult?> TrySendInvocationAsync(
        Process process,
        string payload,
        long? windowHwnd,
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
                failureStage: UiaSnapshotFailureStageValues.WorkerProcess,
                windowHwnd: windowHwnd,
                capturedAtUtc: capturedAtUtc,
                stdout: stdout,
                stderr: stderr,
                exitCode: TryGetExitCode(process),
                exception: exception);
        }
    }

    private UiAutomationWorkerProcessResult CreateWorkerProcessFailure(
        string reason,
        string failureStage,
        long? windowHwnd,
        DateTimeOffset capturedAtUtc,
        string? stdout,
        string? stderr,
        int? exitCode,
        Exception? exception = null)
    {
        string? diagnosticArtifactPath = _diagnosticArtifactWriter?.TryWrite(
            new UiaWorkerProcessDiagnosticArtifact(
                Kind: "worker_process_failure",
                FailureStage: failureStage,
                ArtifactPath: null,
                CapturedAtUtc: capturedAtUtc,
                WindowHwnd: windowHwnd,
                ExitCode: exitCode,
                Stdout: string.IsNullOrWhiteSpace(stdout) ? null : stdout,
                Stderr: string.IsNullOrWhiteSpace(stderr) ? null : stderr,
                ExceptionType: exception?.GetType().FullName,
                ExceptionMessage: exception?.Message));

        return new UiAutomationWorkerProcessResult(
            Success: false,
            Reason: reason,
            FailureStage: failureStage,
            CapturedAtUtc: capturedAtUtc,
            CompletedAtUtc: _timeProvider.GetUtcNow(),
            Stdout: null,
            Stderr: null,
            DiagnosticArtifactPath: diagnosticArtifactPath);
    }

    private static UiAutomationWorkerProcessResult Failed(string reason, string failureStage, DateTimeOffset capturedAtUtc) =>
        new(
            Success: false,
            Reason: reason,
            FailureStage: failureStage,
            CapturedAtUtc: capturedAtUtc,
            CompletedAtUtc: capturedAtUtc,
            Stdout: null,
            Stderr: null,
            DiagnosticArtifactPath: null);

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

internal sealed record UiAutomationWorkerProcessResult(
    bool Success,
    string? Reason,
    string? FailureStage,
    DateTimeOffset CapturedAtUtc,
    DateTimeOffset CompletedAtUtc,
    string? Stdout,
    string? Stderr,
    string? DiagnosticArtifactPath);
