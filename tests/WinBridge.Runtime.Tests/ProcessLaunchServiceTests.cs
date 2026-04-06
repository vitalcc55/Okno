using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Windows.Launch;

namespace WinBridge.Runtime.Tests;

public sealed class ProcessLaunchServiceTests
{
    [Fact]
    public async Task LaunchAsyncBuildsDirectProcessStartInfoWithArgumentListOnly()
    {
        string executablePath = CreateExecutablePath();
        string workingDirectory = CreateWorkingDirectory();
        FakeProcessLaunchPlatform platform = new(
            _ => new FakeStartedProcessHandle(
                id: 4242,
                hasExitedSequence: [false],
                exitCode: null,
                mainWindowHandles: [0]));
        ProcessLaunchService service = CreateService(platform);

        LaunchProcessResult result = await service.LaunchAsync(
            new LaunchProcessRequest
            {
                Executable = executablePath,
                Args = ["--flag", "value"],
                WorkingDirectory = workingDirectory,
            },
            CancellationToken.None);

        Assert.NotNull(platform.LastStartInfo);
        Assert.Equal(executablePath, platform.LastStartInfo!.FileName);
        Assert.False(platform.LastStartInfo.UseShellExecute);
        Assert.Equal(string.Empty, platform.LastStartInfo.Arguments);
        Assert.Equal(["--flag", "value"], platform.LastStartInfo.ArgumentList);
        Assert.Equal(workingDirectory, platform.LastStartInfo.WorkingDirectory);
        Assert.Equal(string.Empty, platform.LastStartInfo.Verb);
        Assert.Equal(string.Empty, platform.LastStartInfo.UserName);
        Assert.Equal(LaunchProcessStatusValues.Done, result.Status);
        Assert.Equal(LaunchProcessResultModeValues.ProcessStarted, result.ResultMode);
        Assert.Equal("fake-tool.exe", result.ExecutableIdentity);
        Assert.Equal(4242, result.ProcessId);
        Assert.False(result.HasExited);
    }

    [Fact]
    public async Task LaunchAsyncReturnsExecutableNotFoundFromAuthoritativeStartFailure()
    {
        FakeProcessLaunchPlatform platform = new(_ => throw new FileNotFoundException("missing"));
        ProcessLaunchService service = CreateService(platform);

        LaunchProcessResult result = await service.LaunchAsync(
            new LaunchProcessRequest
            {
                Executable = Path.Combine(CreateWorkingDirectory(), "missing-tool.exe"),
            },
            CancellationToken.None);

        Assert.Equal(LaunchProcessStatusValues.Failed, result.Status);
        Assert.Equal(LaunchProcessFailureCodeValues.ExecutableNotFound, result.FailureCode);
        Assert.NotNull(platform.LastStartInfo);
    }

    [Fact]
    public async Task LaunchAsyncReturnsWorkingDirectoryNotFoundFromAuthoritativeStartFailure()
    {
        string executablePath = CreateExecutablePath();
        string missingDirectory = Path.Combine(CreateWorkingDirectory(), "missing");
        FakeProcessLaunchPlatform platform = new(_ => throw new DirectoryNotFoundException("missing"));
        ProcessLaunchService service = CreateService(platform);

        LaunchProcessResult result = await service.LaunchAsync(
            new LaunchProcessRequest
            {
                Executable = executablePath,
                WorkingDirectory = missingDirectory,
            },
            CancellationToken.None);

        Assert.Equal(LaunchProcessStatusValues.Failed, result.Status);
        Assert.Equal(LaunchProcessFailureCodeValues.WorkingDirectoryNotFound, result.FailureCode);
        Assert.NotNull(platform.LastStartInfo);
    }

    [Fact]
    public async Task LaunchAsyncDoesNotMaterializeEvidenceForValidationOnlyFailure()
    {
        string root = CreateWorkingDirectory();
        AuditLogOptions auditOptions = CreateAuditLogOptions(root, "run-launch-validation-no-evidence");
        AuditLog auditLog = new(auditOptions, TimeProvider.System);
        LaunchResultMaterializer materializer = new(auditLog, auditOptions, TimeProvider.System);
        FakeProcessLaunchPlatform platform = new(_ => throw new InvalidOperationException("platform should not be called"));
        ProcessLaunchService service = new(
            platform,
            TimeProvider.System,
            new ProcessLaunchOptions(
                MainWindowPollInterval: TimeSpan.FromMilliseconds(1),
                InputIdleWaitSlice: TimeSpan.FromMilliseconds(10)),
            materializer);

        LaunchProcessResult result = await service.LaunchAsync(
            new LaunchProcessRequest
            {
                Executable = "https://host/app.exe?token=super-secret",
            },
            CancellationToken.None);

        Assert.Equal(LaunchProcessStatusValues.Failed, result.Status);
        Assert.Equal(LaunchProcessFailureCodeValues.UnsupportedTargetKind, result.FailureCode);
        Assert.Null(result.ArtifactPath);
        Assert.False(File.Exists(auditOptions.EventsPath));
        Assert.False(Directory.Exists(Path.Combine(auditOptions.RunDirectory, "launch")));

        string summary = File.ReadAllText(auditOptions.SummaryPath);
        Assert.DoesNotContain("launch.runtime.completed", summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LaunchAsyncMaterializesLaunchArtifactAndRuntimeEvent()
    {
        string root = CreateWorkingDirectory();
        string executablePath = CreateExecutablePath();
        FakeProcessLaunchPlatform platform = new(
            _ => new FakeStartedProcessHandle(
                id: 4242,
                hasExitedSequence: [false],
                exitCode: null,
                mainWindowHandles: [0]));

        ServiceCollection services = new();
        services.AddWinBridgeRuntime(root, "Tests");
        services.AddSingleton<IProcessLaunchPlatform>(platform);

        using ServiceProvider provider = services.BuildServiceProvider();
        AuditLogOptions auditOptions = provider.GetRequiredService<AuditLogOptions>();
        IProcessLaunchService service = provider.GetRequiredService<IProcessLaunchService>();

        LaunchProcessResult result = await service.LaunchAsync(
            new LaunchProcessRequest
            {
                Executable = executablePath,
            },
            CancellationToken.None);

        Assert.NotNull(result.ArtifactPath);
        Assert.True(File.Exists(result.ArtifactPath));

        using JsonDocument artifact = JsonDocument.Parse(await File.ReadAllTextAsync(result.ArtifactPath));
        JsonElement artifactResult = artifact.RootElement.GetProperty("result");
        Assert.Equal(result.ArtifactPath, artifactResult.GetProperty("artifact_path").GetString());
        Assert.Equal(result.ProcessId, artifactResult.GetProperty("process_id").GetInt32());

        string eventLine = Assert.Single(File.ReadAllLines(auditOptions.EventsPath));
        Assert.Contains("\"event_name\":\"launch.runtime.completed\"", eventLine, StringComparison.Ordinal);
        Assert.Contains("\"artifact_path\"", eventLine, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LaunchAsyncKeepsFactualOutcomeWhenLaunchRuntimeEventWriteFails()
    {
        string root = CreateWorkingDirectory();
        string executablePath = CreateExecutablePath();
        AuditLogOptions auditOptions = CreateAuditLogOptions(root, "run-launch-event-write-failure");
        AuditLog auditLog = new(auditOptions, TimeProvider.System);
        Directory.CreateDirectory(auditOptions.EventsPath);
        LaunchResultMaterializer materializer = new(auditLog, auditOptions, TimeProvider.System);
        FakeProcessLaunchPlatform platform = new(
            _ => new FakeStartedProcessHandle(
                id: 4342,
                hasExitedSequence: [false],
                exitCode: null,
                mainWindowHandles: [0]));
        ProcessLaunchService service = new(
            platform,
            TimeProvider.System,
            new ProcessLaunchOptions(
                MainWindowPollInterval: TimeSpan.FromMilliseconds(1),
                InputIdleWaitSlice: TimeSpan.FromMilliseconds(10)),
            materializer);

        LaunchProcessResult result = await service.LaunchAsync(
            new LaunchProcessRequest
            {
                Executable = executablePath,
            },
            CancellationToken.None);

        Assert.Equal(LaunchProcessStatusValues.Done, result.Status);
        Assert.Equal(LaunchProcessResultModeValues.ProcessStarted, result.ResultMode);
        Assert.NotNull(result.ArtifactPath);
        Assert.True(File.Exists(result.ArtifactPath));
        Assert.True(Directory.Exists(auditOptions.EventsPath));
    }

    [Fact]
    public async Task LaunchAsyncKeepsFactualOutcomeWhenLaunchArtifactWriteFails()
    {
        string root = CreateWorkingDirectory();
        string executablePath = CreateExecutablePath();
        FakeProcessLaunchPlatform platform = new(
            _ => new FakeStartedProcessHandle(
                id: 4343,
                hasExitedSequence: [false],
                exitCode: null,
                mainWindowHandles: [0]));

        ServiceCollection services = new();
        services.AddWinBridgeRuntime(root, "Tests");
        services.AddSingleton<IProcessLaunchPlatform>(platform);

        using ServiceProvider provider = services.BuildServiceProvider();
        AuditLogOptions auditOptions = provider.GetRequiredService<AuditLogOptions>();
        Directory.CreateDirectory(auditOptions.RunDirectory);
        File.WriteAllText(Path.Combine(auditOptions.RunDirectory, "launch"), "block-launch-directory");
        IProcessLaunchService service = provider.GetRequiredService<IProcessLaunchService>();

        LaunchProcessResult result = await service.LaunchAsync(
            new LaunchProcessRequest
            {
                Executable = executablePath,
            },
            CancellationToken.None);

        Assert.Equal(LaunchProcessStatusValues.Done, result.Status);
        Assert.Equal(LaunchProcessResultModeValues.ProcessStarted, result.ResultMode);
        Assert.Null(result.ArtifactPath);

        string eventLine = Assert.Single(File.ReadAllLines(auditOptions.EventsPath));
        Assert.Contains("\"event_name\":\"launch.runtime.completed\"", eventLine, StringComparison.Ordinal);
        Assert.Contains("\"failure_stage\":\"artifact_write\"", eventLine, StringComparison.Ordinal);
        Assert.Contains("\"exception_type\"", eventLine, StringComparison.Ordinal);
        Assert.DoesNotContain("exception_message", eventLine, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LaunchAsyncMaterializesArtifactAndRuntimeEventForFactualFailedRuntimeResult()
    {
        string root = CreateWorkingDirectory();
        string executablePath = CreateExecutablePath();
        AuditLogOptions auditOptions = CreateAuditLogOptions(root, "run-launch-failed-runtime-evidence");
        AuditLog auditLog = new(auditOptions, TimeProvider.System);
        LaunchResultMaterializer materializer = new(auditLog, auditOptions, TimeProvider.System);
        FakeProcessLaunchPlatform platform = new(
            _ => new FakeStartedProcessHandle(
                id: 654,
                hasExitedSequence: [false, false, true],
                exitCode: 7,
                waitForInputIdleResults: [true],
                mainWindowHandles: [0]));
        ProcessLaunchService service = new(
            platform,
            TimeProvider.System,
            new ProcessLaunchOptions(
                MainWindowPollInterval: TimeSpan.FromMilliseconds(1),
                InputIdleWaitSlice: TimeSpan.FromMilliseconds(10)),
            materializer);

        LaunchProcessResult result = await service.LaunchAsync(
            new LaunchProcessRequest
            {
                Executable = executablePath,
                WaitForWindow = true,
                TimeoutMs = 25,
            },
            CancellationToken.None);

        Assert.Equal(LaunchProcessStatusValues.Failed, result.Status);
        Assert.Equal(LaunchProcessFailureCodeValues.ProcessExitedBeforeWindow, result.FailureCode);
        Assert.NotNull(result.ArtifactPath);
        Assert.True(File.Exists(result.ArtifactPath));

        using JsonDocument artifact = JsonDocument.Parse(await File.ReadAllTextAsync(result.ArtifactPath));
        JsonElement artifactResult = artifact.RootElement.GetProperty("result");
        Assert.Equal(result.ArtifactPath, artifactResult.GetProperty("artifact_path").GetString());
        Assert.Equal("process_exited_before_window", artifactResult.GetProperty("failure_code").GetString());

        string eventLine = Assert.Single(File.ReadAllLines(auditOptions.EventsPath));
        Assert.Contains("\"event_name\":\"launch.runtime.completed\"", eventLine, StringComparison.Ordinal);
        Assert.Contains("\"artifact_path\"", eventLine, StringComparison.Ordinal);
        Assert.Contains("\"failure_code\":\"process_exited_before_window\"", eventLine, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LaunchAsyncReturnsProcessStartedAndExitedWhenProcessAlreadyExited()
    {
        string executablePath = CreateExecutablePath();
        FakeProcessLaunchPlatform platform = new(
            _ => new FakeStartedProcessHandle(
                id: 123,
                hasExitedSequence: [true],
                exitCode: 0,
                mainWindowHandles: [0]));
        ProcessLaunchService service = CreateService(platform);

        LaunchProcessResult result = await service.LaunchAsync(
            new LaunchProcessRequest
            {
                Executable = executablePath,
            },
            CancellationToken.None);

        Assert.Equal(LaunchProcessStatusValues.Done, result.Status);
        Assert.Equal(LaunchProcessResultModeValues.ProcessStartedAndExited, result.ResultMode);
        Assert.True(result.HasExited);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task LaunchAsyncBuildsConsistentStartedSnapshotFromSingleHasExitedRead()
    {
        string executablePath = CreateExecutablePath();
        FakeProcessLaunchPlatform platform = new(
            _ => new FakeStartedProcessHandle(
                id: 124,
                hasExitedSequence: [false, true],
                exitCode: 9,
                mainWindowHandles: [0]));
        ProcessLaunchService service = CreateService(platform);

        LaunchProcessResult result = await service.LaunchAsync(
            new LaunchProcessRequest
            {
                Executable = executablePath,
            },
            CancellationToken.None);

        Assert.Equal(LaunchProcessStatusValues.Done, result.Status);
        Assert.Equal(LaunchProcessResultModeValues.ProcessStarted, result.ResultMode);
        Assert.False(result.HasExited);
        Assert.Null(result.ExitCode);
    }

    [Fact]
    public async Task LaunchAsyncUsesFreshStartedSnapshotForNoWaitResult()
    {
        string executablePath = CreateExecutablePath();
        FakeProcessLaunchPlatform platform = new(
            _ => new FakeStartedProcessHandle(
                id: 125,
                hasExitedSequence: [false, true],
                exitCode: 9,
                mainWindowHandles: [0],
                hasExitedChangesOnRefresh: true));
        ProcessLaunchService service = CreateService(platform);

        LaunchProcessResult result = await service.LaunchAsync(
            new LaunchProcessRequest
            {
                Executable = executablePath,
            },
            CancellationToken.None);

        Assert.Equal(LaunchProcessStatusValues.Done, result.Status);
        Assert.Equal(LaunchProcessResultModeValues.ProcessStartedAndExited, result.ResultMode);
        Assert.True(result.HasExited);
        Assert.Equal(9, result.ExitCode);
    }

    [Fact]
    public async Task LaunchAsyncReturnsWindowObservedWhenMainWindowAppears()
    {
        string executablePath = CreateExecutablePath();
        FakeProcessLaunchPlatform platform = new(
            _ => new FakeStartedProcessHandle(
                id: 321,
                hasExitedSequence: [false, false, false],
                exitCode: null,
                waitForInputIdleResults: [false, true],
                mainWindowHandles: [0, 777]));
        ProcessLaunchService service = CreateService(platform);

        LaunchProcessResult result = await service.LaunchAsync(
            new LaunchProcessRequest
            {
                Executable = executablePath,
                WaitForWindow = true,
                TimeoutMs = 50,
            },
            CancellationToken.None);

        Assert.Equal(LaunchProcessStatusValues.Done, result.Status);
        Assert.Equal(LaunchProcessResultModeValues.WindowObserved, result.ResultMode);
        Assert.True(result.MainWindowObserved);
        Assert.Equal(777, result.MainWindowHandle);
        Assert.Equal(LaunchMainWindowObservationStatusValues.Observed, result.MainWindowObservationStatus);
        Assert.Equal(2, platform.WaitForInputIdleCallCount);
        Assert.Equal(10, platform.LastObservedWaitForInputIdleMilliseconds);
    }

    [Fact]
    public async Task LaunchAsyncReturnsProcessExitedBeforeWindowWhenProcessExitsDuringObservation()
    {
        string executablePath = CreateExecutablePath();
        FakeProcessLaunchPlatform platform = new(
            _ => new FakeStartedProcessHandle(
                id: 654,
                hasExitedSequence: [false, false, true],
                exitCode: 7,
                waitForInputIdleResults: [true],
                mainWindowHandles: [0]));
        ProcessLaunchService service = CreateService(platform);

        LaunchProcessResult result = await service.LaunchAsync(
            new LaunchProcessRequest
            {
                Executable = executablePath,
                WaitForWindow = true,
                TimeoutMs = 25,
            },
            CancellationToken.None);

        Assert.Equal(LaunchProcessStatusValues.Failed, result.Status);
        Assert.Equal(LaunchProcessFailureCodeValues.ProcessExitedBeforeWindow, result.FailureCode);
        Assert.True(result.HasExited);
        Assert.Equal(7, result.ExitCode);
        Assert.Equal(LaunchMainWindowObservationStatusValues.ProcessExited, result.MainWindowObservationStatus);
    }

    [Fact]
    public async Task LaunchAsyncReturnsMainWindowTimeoutWhenInputIdleBudgetExpires()
    {
        string executablePath = CreateExecutablePath();
        FakeProcessLaunchPlatform platform = new(
            _ => new FakeStartedProcessHandle(
                id: 777,
                hasExitedSequence: [false],
                exitCode: null,
                waitForInputIdleResults: [false, false],
                mainWindowHandles: [0]));
        ProcessLaunchService service = CreateService(platform);

        LaunchProcessResult result = await service.LaunchAsync(
            new LaunchProcessRequest
            {
                Executable = executablePath,
                WaitForWindow = true,
                TimeoutMs = 10,
            },
            CancellationToken.None);

        Assert.Equal(LaunchProcessStatusValues.Failed, result.Status);
        Assert.Equal(LaunchProcessFailureCodeValues.MainWindowTimeout, result.FailureCode);
        Assert.False(result.MainWindowObserved);
        Assert.Equal(LaunchMainWindowObservationStatusValues.TimedOut, result.MainWindowObservationStatus);
    }

    [Fact]
    public async Task LaunchAsyncReturnsWindowObservedWhenInputIdleTimesOutButWindowAlreadyExists()
    {
        string executablePath = CreateExecutablePath();
        ManualTimeProvider timeProvider = new(DateTimeOffset.UtcNow);
        FakeProcessLaunchPlatform platform = new(
            _ => new FakeStartedProcessHandle(
                id: 778,
                hasExitedSequence: [false, false],
                exitCode: null,
                waitForInputIdleResults: [false],
                mainWindowHandles: [7001])
            {
                OnWaitForInputIdle = _ => timeProvider.Advance(TimeSpan.FromMilliseconds(10)),
            });
        ProcessLaunchService service = CreateService(platform, timeProvider: timeProvider);

        LaunchProcessResult result = await service.LaunchAsync(
            new LaunchProcessRequest
            {
                Executable = executablePath,
                WaitForWindow = true,
                TimeoutMs = 10,
            },
            CancellationToken.None);

        Assert.Equal(LaunchProcessStatusValues.Done, result.Status);
        Assert.Equal(LaunchProcessResultModeValues.WindowObserved, result.ResultMode);
        Assert.True(result.MainWindowObserved);
        Assert.Equal(7001, result.MainWindowHandle);
        Assert.Equal(LaunchMainWindowObservationStatusValues.Observed, result.MainWindowObservationStatus);
    }

    [Fact]
    public async Task LaunchAsyncDoesNotReturnWindowObservedAfterDeadlineSnapshot()
    {
        string executablePath = CreateExecutablePath();
        ManualTimeProvider timeProvider = new(DateTimeOffset.UtcNow);
        FakeProcessLaunchPlatform platform = new(
            _ => new FakeStartedProcessHandle(
                id: 7780,
                hasExitedSequence: [false, false],
                exitCode: null,
                waitForInputIdleResults: [true],
                mainWindowHandles: [0, 9002])
            {
                OnWaitForInputIdle = _ => timeProvider.Advance(TimeSpan.FromMilliseconds(10)),
                OnRefresh = () => timeProvider.Advance(TimeSpan.FromMilliseconds(1)),
            });
        ProcessLaunchService service = CreateService(platform, timeProvider: timeProvider);

        LaunchProcessResult result = await service.LaunchAsync(
            new LaunchProcessRequest
            {
                Executable = executablePath,
                WaitForWindow = true,
                TimeoutMs = 10,
            },
            CancellationToken.None);

        Assert.Equal(LaunchProcessStatusValues.Failed, result.Status);
        Assert.Equal(LaunchProcessFailureCodeValues.MainWindowNotObserved, result.FailureCode);
        Assert.False(result.MainWindowObserved);
    }

    [Fact]
    public async Task LaunchAsyncRefreshesBeforeFinalDeadlineSnapshot()
    {
        string executablePath = CreateExecutablePath();
        ManualTimeProvider timeProvider = new(DateTimeOffset.UtcNow);
        FakeProcessLaunchPlatform platform = new(
            _ => new FakeStartedProcessHandle(
                id: 7781,
                hasExitedSequence: [false, false],
                exitCode: null,
                waitForInputIdleResults: [true],
                mainWindowHandles: [0, 8001])
            {
                OnWaitForInputIdle = _ => timeProvider.Advance(TimeSpan.FromMilliseconds(10)),
            });
        ProcessLaunchService service = CreateService(platform, timeProvider: timeProvider);

        LaunchProcessResult result = await service.LaunchAsync(
            new LaunchProcessRequest
            {
                Executable = executablePath,
                WaitForWindow = true,
                TimeoutMs = 10,
            },
            CancellationToken.None);

        Assert.Equal(LaunchProcessStatusValues.Done, result.Status);
        Assert.Equal(LaunchProcessResultModeValues.WindowObserved, result.ResultMode);
        Assert.True(result.MainWindowObserved);
        Assert.Equal(8001, result.MainWindowHandle);
        Assert.Equal(LaunchMainWindowObservationStatusValues.Observed, result.MainWindowObservationStatus);
    }

    [Fact]
    public async Task LaunchAsyncTreatsInvalidInputIdleAsConservativeNotObservedResult()
    {
        string executablePath = CreateExecutablePath();
        ManualTimeProvider timeProvider = new(DateTimeOffset.UtcNow);
        FakeStartedProcessHandle handle = new(
            id: 888,
            hasExitedSequence: [false, false],
            exitCode: null,
            waitForInputIdleException: new InvalidOperationException("no gui"),
            mainWindowHandles: [0])
        {
            OnWaitForInputIdle = _ => timeProvider.Advance(TimeSpan.FromMilliseconds(100)),
        };
        FakeProcessLaunchPlatform platform = new(_ => handle);
        ProcessLaunchService service = CreateService(platform, timeProvider: timeProvider);

        LaunchProcessResult result = await service.LaunchAsync(
            new LaunchProcessRequest
            {
                Executable = executablePath,
                WaitForWindow = true,
                TimeoutMs = 100,
            },
            CancellationToken.None);

        Assert.Equal(LaunchProcessStatusValues.Failed, result.Status);
        Assert.Equal(LaunchProcessFailureCodeValues.MainWindowNotObserved, result.FailureCode);
        Assert.Equal(LaunchMainWindowObservationStatusValues.NotObserved, result.MainWindowObservationStatus);
    }

    [Fact]
    public async Task LaunchAsyncReturnsMainWindowNotObservedWhenDeadlineExpiresAfterIdle()
    {
        string executablePath = CreateExecutablePath();
        ManualTimeProvider timeProvider = new(DateTimeOffset.UtcNow);
        FakeProcessLaunchPlatform platform = new(
            _ => new FakeStartedProcessHandle(
                id: 999,
                hasExitedSequence: [false, false],
                exitCode: null,
                waitForInputIdleResults: [true],
                mainWindowHandles: [0])
            {
                OnWaitForInputIdle = _ => timeProvider.Advance(TimeSpan.FromMilliseconds(10)),
            });
        ProcessLaunchService service = CreateService(platform, timeProvider: timeProvider);

        LaunchProcessResult result = await service.LaunchAsync(
            new LaunchProcessRequest
            {
                Executable = executablePath,
                WaitForWindow = true,
                TimeoutMs = 10,
            },
            CancellationToken.None);

        Assert.Equal(LaunchProcessStatusValues.Failed, result.Status);
        Assert.Equal(LaunchProcessFailureCodeValues.MainWindowNotObserved, result.FailureCode);
        Assert.NotEqual(LaunchProcessResultModeValues.WindowObserved, result.ResultMode);
    }

    [Fact]
    public async Task LaunchAsyncPrefersProcessExitedBeforeWindowOverTimedOutSnapshot()
    {
        string executablePath = CreateExecutablePath();
        ManualTimeProvider timeProvider = new(DateTimeOffset.UtcNow);
        FakeProcessLaunchPlatform platform = new(
            _ => new FakeStartedProcessHandle(
                id: 1000,
                hasExitedSequence: [false, true],
                exitCode: 12,
                waitForInputIdleResults: [false],
                mainWindowHandles: [0])
            {
                OnWaitForInputIdle = _ => timeProvider.Advance(TimeSpan.FromMilliseconds(10)),
            });
        ProcessLaunchService service = CreateService(platform, timeProvider: timeProvider);

        LaunchProcessResult result = await service.LaunchAsync(
            new LaunchProcessRequest
            {
                Executable = executablePath,
                WaitForWindow = true,
                TimeoutMs = 10,
            },
            CancellationToken.None);

        Assert.Equal(LaunchProcessStatusValues.Failed, result.Status);
        Assert.Equal(LaunchProcessFailureCodeValues.ProcessExitedBeforeWindow, result.FailureCode);
        Assert.True(result.HasExited);
        Assert.Equal(12, result.ExitCode);
        Assert.Equal(LaunchMainWindowObservationStatusValues.ProcessExited, result.MainWindowObservationStatus);
    }

    [Fact]
    public async Task LaunchAsyncReturnsStartedResultWhenCancelledAfterStart()
    {
        string executablePath = CreateExecutablePath();
        using CancellationTokenSource cts = new();
        FakeProcessLaunchPlatform platform = new(
            _ =>
            {
                cts.Cancel();
                return new FakeStartedProcessHandle(
                    id: 1001,
                    hasExitedSequence: [false],
                    exitCode: null,
                    waitForInputIdleResults: [true],
                    mainWindowHandles: [0]);
            });
        ProcessLaunchService service = CreateService(platform);

        LaunchProcessResult result = await service.LaunchAsync(
            new LaunchProcessRequest
            {
                Executable = executablePath,
                WaitForWindow = true,
                TimeoutMs = 500,
            },
            cts.Token);

        Assert.Equal(LaunchProcessStatusValues.Done, result.Status);
        Assert.Equal(LaunchProcessResultModeValues.ProcessStarted, result.ResultMode);
        Assert.Equal(LaunchMainWindowObservationStatusValues.NotObserved, result.MainWindowObservationStatus);
        Assert.Equal(0, platform.WaitForInputIdleCallCount);
    }

    [Fact]
    public async Task LaunchAsyncReturnsFreshStartedAndExitedResultWhenCancelledDuringWaitForInputIdle()
    {
        string executablePath = CreateExecutablePath();
        using CancellationTokenSource cts = new();
        FakeProcessLaunchPlatform platform = new(
            _ => new FakeStartedProcessHandle(
                id: 10011,
                hasExitedSequence: [false, true],
                exitCode: 17,
                waitForInputIdleResults: [false],
                mainWindowHandles: [0],
                hasExitedChangesOnRefresh: true)
            {
                OnWaitForInputIdle = _ => cts.Cancel(),
            });
        ProcessLaunchService service = CreateService(platform);

        LaunchProcessResult result = await service.LaunchAsync(
            new LaunchProcessRequest
            {
                Executable = executablePath,
                WaitForWindow = true,
                TimeoutMs = 100,
            },
            cts.Token);

        Assert.Equal(LaunchProcessStatusValues.Done, result.Status);
        Assert.Equal(LaunchProcessResultModeValues.ProcessStartedAndExited, result.ResultMode);
        Assert.True(result.HasExited);
        Assert.Equal(17, result.ExitCode);
        Assert.Equal(LaunchMainWindowObservationStatusValues.NotObserved, result.MainWindowObservationStatus);
    }

    [Fact]
    public async Task LaunchAsyncUsesProviderAwareDelayForObservationPolling()
    {
        string executablePath = CreateExecutablePath();
        ManualTimeProvider timeProvider = new(DateTimeOffset.UtcNow);
        FakeProcessLaunchPlatform platform = new(
            _ => new FakeStartedProcessHandle(
                id: 1002,
                hasExitedSequence: [false, false, false],
                exitCode: null,
                waitForInputIdleResults: [true],
                mainWindowHandles: [0]));
        ProcessLaunchService service = CreateService(
            platform,
            options: new ProcessLaunchOptions(
                MainWindowPollInterval: TimeSpan.FromSeconds(1),
                InputIdleWaitSlice: TimeSpan.FromMilliseconds(10)),
            timeProvider: timeProvider);

        Task<LaunchProcessResult> launchTask = service.LaunchAsync(
            new LaunchProcessRequest
            {
                Executable = executablePath,
                WaitForWindow = true,
                TimeoutMs = 1000,
            },
            CancellationToken.None);

        for (int attempt = 0; attempt < 20 && timeProvider.ActiveTimerCount == 0 && !launchTask.IsCompleted; attempt++)
        {
            await Task.Delay(1);
        }

        Assert.False(launchTask.IsCompleted);
        Assert.True(timeProvider.ActiveTimerCount > 0);

        timeProvider.Advance(TimeSpan.FromSeconds(1));

        LaunchProcessResult result = await launchTask.WaitAsync(TimeSpan.FromMilliseconds(100));

        Assert.Equal(LaunchProcessStatusValues.Failed, result.Status);
        Assert.Equal(LaunchProcessFailureCodeValues.MainWindowNotObserved, result.FailureCode);
    }

    [Fact]
    public async Task LaunchAsyncThrowsCancellationBeforeStartSideEffect()
    {
        string executablePath = CreateExecutablePath();
        FakeProcessLaunchPlatform platform = new(_ => throw new InvalidOperationException("platform should not be called"));
        ProcessLaunchService service = CreateService(platform);
        using CancellationTokenSource cts = new();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.LaunchAsync(
                new LaunchProcessRequest
                {
                    Executable = executablePath,
                },
                cts.Token));

        Assert.Null(platform.LastStartInfo);
    }

    [Fact]
    public async Task LaunchAsyncPrefersProcessExitedBeforeWindowWhenExitedSnapshotStillCarriesWindowHandle()
    {
        string executablePath = CreateExecutablePath();
        FakeProcessLaunchPlatform platform = new(
            _ => new FakeStartedProcessHandle(
                id: 4001,
                hasExitedSequence: [true],
                exitCode: 5,
                waitForInputIdleResults: [true],
                mainWindowHandles: [9001]));
        ProcessLaunchService service = CreateService(platform);

        LaunchProcessResult result = await service.LaunchAsync(
            new LaunchProcessRequest
            {
                Executable = executablePath,
                WaitForWindow = true,
                TimeoutMs = 100,
            },
            CancellationToken.None);

        Assert.Equal(LaunchProcessStatusValues.Failed, result.Status);
        Assert.Equal(LaunchProcessFailureCodeValues.ProcessExitedBeforeWindow, result.FailureCode);
        Assert.True(result.HasExited);
        Assert.Equal(5, result.ExitCode);
        Assert.False(result.MainWindowObserved);
    }

    [Fact]
    public async Task LaunchAsyncReconcilesExitWhenMainWindowHandleReadFails()
    {
        string executablePath = CreateExecutablePath();
        FakeProcessLaunchPlatform platform = new(
            _ => new FakeStartedProcessHandle(
                id: 4002,
                hasExitedSequence: [false, true],
                exitCode: 11,
                waitForInputIdleResults: [true],
                mainWindowHandles: [0],
                mainWindowHandleException: new InvalidOperationException("process exited")));
        ProcessLaunchService service = CreateService(platform);

        LaunchProcessResult result = await service.LaunchAsync(
            new LaunchProcessRequest
            {
                Executable = executablePath,
                WaitForWindow = true,
                TimeoutMs = 100,
            },
            CancellationToken.None);

        Assert.Equal(LaunchProcessStatusValues.Failed, result.Status);
        Assert.Equal(LaunchProcessFailureCodeValues.ProcessExitedBeforeWindow, result.FailureCode);
        Assert.True(result.HasExited);
        Assert.Equal(11, result.ExitCode);
    }

    [Fact]
    public async Task LaunchAsyncStripsUriQueryFromRejectedExecutableIdentity()
    {
        FakeProcessLaunchPlatform platform = new(_ => throw new InvalidOperationException("platform should not be called"));
        ProcessLaunchService service = CreateService(platform);

        LaunchProcessResult result = await service.LaunchAsync(
            new LaunchProcessRequest
            {
                Executable = "https://host/app.exe?token=super-secret#frag",
            },
            CancellationToken.None);

        Assert.Equal(LaunchProcessStatusValues.Failed, result.Status);
        Assert.Equal(LaunchProcessFailureCodeValues.UnsupportedTargetKind, result.FailureCode);
        Assert.Equal("app.exe", result.ExecutableIdentity);
        Assert.DoesNotContain("super-secret", result.ExecutableIdentity, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LaunchAsyncReturnsStartFailedForBareExecutableWin32PathFailureWithWorkingDirectory()
    {
        string workingDirectory = Path.Combine(CreateWorkingDirectory(), "missing");
        FakeProcessLaunchPlatform platform = new(_ => throw new Win32Exception(3));
        ProcessLaunchService service = CreateService(platform);

        LaunchProcessResult result = await service.LaunchAsync(
            new LaunchProcessRequest
            {
                Executable = "fake-tool.exe",
                WorkingDirectory = workingDirectory,
            },
            CancellationToken.None);

        Assert.Equal(LaunchProcessStatusValues.Failed, result.Status);
        Assert.Equal(LaunchProcessFailureCodeValues.StartFailed, result.FailureCode);
    }

    [Fact]
    public async Task LaunchAsyncReturnsStartFailedForAmbiguousWin32PathFailure()
    {
        string executablePath = CreateExecutablePath();
        string workingDirectory = Path.Combine(CreateWorkingDirectory(), "missing");
        FakeProcessLaunchPlatform platform = new(_ => throw new Win32Exception(3));
        ProcessLaunchService service = CreateService(platform);

        LaunchProcessResult result = await service.LaunchAsync(
            new LaunchProcessRequest
            {
                Executable = executablePath,
                WorkingDirectory = workingDirectory,
            },
            CancellationToken.None);

        Assert.Equal(LaunchProcessStatusValues.Failed, result.Status);
        Assert.Equal(LaunchProcessFailureCodeValues.StartFailed, result.FailureCode);
    }

    [Fact]
    public void AddWinBridgeRuntimeResolvesLaunchService()
    {
        string root = CreateWorkingDirectory();
        ServiceCollection services = new();

        services.AddWinBridgeRuntime(root, "Tests");

        using ServiceProvider provider = services.BuildServiceProvider();

        IProcessLaunchService service = provider.GetRequiredService<IProcessLaunchService>();

        Assert.IsType<ProcessLaunchService>(service);
    }

    private static ProcessLaunchService CreateService(
        FakeProcessLaunchPlatform platform,
        ProcessLaunchOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        TimeProvider resolvedTimeProvider = timeProvider ?? TimeProvider.System;
        AuditLogOptions auditOptions = CreateAuditLogOptions(CreateWorkingDirectory(), Guid.NewGuid().ToString("N"));
        AuditLog auditLog = new(auditOptions, resolvedTimeProvider);
        LaunchResultMaterializer materializer = new(auditLog, auditOptions, resolvedTimeProvider);

        return new ProcessLaunchService(
            platform,
            resolvedTimeProvider,
            options ?? new ProcessLaunchOptions(
                MainWindowPollInterval: TimeSpan.FromMilliseconds(1),
                InputIdleWaitSlice: TimeSpan.FromMilliseconds(10)),
            materializer);
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

    private static string CreateExecutablePath()
    {
        string directory = CreateWorkingDirectory();
        string path = Path.Combine(directory, "fake-tool.exe");
        File.WriteAllText(path, "stub");
        return path;
    }

    private static string CreateWorkingDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FakeProcessLaunchPlatform(Func<ProcessStartInfo, IStartedProcessHandle?> startHandler) : IProcessLaunchPlatform
    {
        public ProcessStartInfo? LastStartInfo { get; private set; }

        public int? LastObservedWaitForInputIdleMilliseconds { get; private set; }

        public int WaitForInputIdleCallCount { get; private set; }

        public IStartedProcessHandle? Start(ProcessStartInfo startInfo)
        {
            LastStartInfo = startInfo;
            IStartedProcessHandle? handle = startHandler(startInfo);
            if (handle is FakeStartedProcessHandle fakeHandle)
            {
                Action<int>? previous = fakeHandle.OnWaitForInputIdle;
                fakeHandle.OnWaitForInputIdle = milliseconds =>
                {
                    WaitForInputIdleCallCount++;
                    LastObservedWaitForInputIdleMilliseconds = milliseconds;
                    previous?.Invoke(milliseconds);
                };
            }

            return handle;
        }
    }

    private sealed class FakeStartedProcessHandle : IStartedProcessHandle
    {
        private readonly IReadOnlyList<bool> _hasExitedSequence;
        private readonly IReadOnlyList<long> _mainWindowHandles;
        private readonly int? _exitCode;
        private readonly IReadOnlyList<bool>? _waitForInputIdleResults;
        private readonly Exception? _waitForInputIdleException;
        private readonly Exception? _mainWindowHandleException;
        private readonly bool _hasExitedChangesOnRefresh;
        private int _hasExitedReadCount;
        private int _refreshCount;
        private int _waitForInputIdleReadCount;

        public FakeStartedProcessHandle(
            int id,
            IReadOnlyList<bool> hasExitedSequence,
            int? exitCode,
            IReadOnlyList<long> mainWindowHandles,
            IReadOnlyList<bool>? waitForInputIdleResults = null,
            Exception? waitForInputIdleException = null,
            Exception? mainWindowHandleException = null,
            bool hasExitedChangesOnRefresh = false)
        {
            Id = id;
            _hasExitedSequence = hasExitedSequence;
            _exitCode = exitCode;
            _mainWindowHandles = mainWindowHandles;
            _waitForInputIdleResults = waitForInputIdleResults;
            _waitForInputIdleException = waitForInputIdleException;
            _mainWindowHandleException = mainWindowHandleException;
            _hasExitedChangesOnRefresh = hasExitedChangesOnRefresh;
        }

        public Action<int>? OnWaitForInputIdle { get; set; }

        public Action? OnRefresh { get; set; }

        public int RefreshCount => _refreshCount;

        public int Id { get; }

        public bool HasExited
        {
            get
            {
                int index = _hasExitedChangesOnRefresh
                    ? Math.Min(_refreshCount, _hasExitedSequence.Count - 1)
                    : Math.Min(_hasExitedReadCount, _hasExitedSequence.Count - 1);
                if (!_hasExitedChangesOnRefresh)
                {
                    _hasExitedReadCount++;
                }

                return _hasExitedSequence[index];
            }
        }

        public int ExitCode => _exitCode ?? throw new InvalidOperationException("ExitCode was not configured.");

        public long MainWindowHandle
        {
            get
            {
                if (_mainWindowHandleException is not null)
                {
                    throw _mainWindowHandleException;
                }

                if (_mainWindowHandles.Count == 0)
                {
                    return 0;
                }

                int index = _refreshCount == 0
                    ? 0
                    : Math.Min(_refreshCount, _mainWindowHandles.Count - 1);
                return _mainWindowHandles[index];
            }
        }

        public bool WaitForInputIdle(int milliseconds)
        {
            OnWaitForInputIdle?.Invoke(milliseconds);
            if (_waitForInputIdleException is not null)
            {
                throw _waitForInputIdleException;
            }

            if (_waitForInputIdleResults is null || _waitForInputIdleResults.Count == 0)
            {
                return true;
            }

            int index = Math.Min(_waitForInputIdleReadCount, _waitForInputIdleResults.Count - 1);
            _waitForInputIdleReadCount++;
            return _waitForInputIdleResults[index];
        }

        public void Refresh()
        {
            _refreshCount++;
            OnRefresh?.Invoke();
        }

        public void Dispose()
        {
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset initialUtcNow) : TimeProvider
    {
        private readonly object _sync = new();
        private readonly List<ManualTimer> _timers = [];
        private DateTimeOffset _utcNow = initialUtcNow;

        public int ActiveTimerCount
        {
            get
            {
                lock (_sync)
                {
                    return _timers.Count(static timer => !timer.IsDisposed);
                }
            }
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            lock (_sync)
            {
                ManualTimer timer = new(this, callback, state, dueTime, period);
                _timers.Add(timer);
                return timer;
            }
        }

        public void Advance(TimeSpan delta)
        {
            List<(TimerCallback Callback, object? State)> callbacks = [];

            lock (_sync)
            {
                _utcNow = _utcNow.Add(delta);

                foreach (ManualTimer timer in _timers.ToArray())
                {
                    timer.CollectDueCallbacks(_utcNow, callbacks);
                }

                _timers.RemoveAll(static timer => timer.IsDisposed);
            }

            foreach ((TimerCallback callback, object? state) in callbacks)
            {
                callback(state);
            }
        }

        private sealed class ManualTimer : ITimer
        {
            private readonly ManualTimeProvider _owner;
            private readonly TimerCallback _callback;
            private readonly object? _state;
            private TimeSpan _period;
            private DateTimeOffset? _nextDueUtc;

            public ManualTimer(
                ManualTimeProvider owner,
                TimerCallback callback,
                object? state,
                TimeSpan dueTime,
                TimeSpan period)
            {
                _owner = owner;
                _callback = callback;
                _state = state;
                Change(dueTime, period);
            }

            public bool IsDisposed { get; private set; }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (IsDisposed)
                {
                    return false;
                }

                _period = period;
                _nextDueUtc = dueTime == Timeout.InfiniteTimeSpan
                    ? null
                    : _owner._utcNow.Add(dueTime);
                return true;
            }

            public void CollectDueCallbacks(DateTimeOffset utcNow, List<(TimerCallback Callback, object? State)> callbacks)
            {
                if (IsDisposed || _nextDueUtc is null)
                {
                    return;
                }

                while (!IsDisposed && _nextDueUtc is not null && utcNow >= _nextDueUtc.Value)
                {
                    callbacks.Add((_callback, _state));
                    if (_period == Timeout.InfiniteTimeSpan)
                    {
                        _nextDueUtc = null;
                        break;
                    }

                    _nextDueUtc = _nextDueUtc.Value.Add(_period);
                }
            }

            public void Dispose()
            {
                IsDisposed = true;
                _nextDueUtc = null;
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
