using System.Diagnostics;
using System.Text;
using System.Text.Json;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Windows.UIA;

namespace WinBridge.Runtime.Tests;

public sealed class ProcessIsolatedUiAutomationBackendTests
{
    [Fact]
    public void WorkerExecutableIsStagedIntoConsumerOutput()
    {
        string outputDirectory = AppContext.BaseDirectory;

        Assert.True(File.Exists(Path.Combine(outputDirectory, "WinBridge.Runtime.Windows.UIA.Worker.exe")));
        Assert.True(File.Exists(Path.Combine(outputDirectory, "WinBridge.Runtime.Windows.UIA.Worker.dll")));
        Assert.True(File.Exists(Path.Combine(outputDirectory, "WinBridge.Runtime.Windows.UIA.Worker.runtimeconfig.json")));
        Assert.True(File.Exists(Path.Combine(outputDirectory, "WinBridge.Runtime.Windows.UIA.Worker.deps.json")));
    }

    [Fact]
    public async Task CaptureAsyncReturnsTimeoutFailureWhenWorkerProcessDoesNotFinishInTime()
    {
        ProcessIsolatedUiAutomationBackend backend = new(
            TimeProvider.System,
            new UiAutomationExecutionOptions(TimeSpan.FromMilliseconds(100)),
            workerExecutablePath: "powershell.exe",
            workerArguments: "-NoLogo -NoProfile -Command Start-Sleep -Seconds 30");

        UiaSnapshotBackendResult result = await backend.CaptureAsync(
            CreateWindow(),
            new UiaSnapshotRequest(),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(UiaSnapshotFailureStageValues.Timeout, result.FailureStage);
    }

    [Fact]
    public async Task CaptureAsyncSanitizesWorkerStderrAndWritesDiagnosticArtifact()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateAuditLogOptions(root, "run-uia-worker-stderr");
        ProcessIsolatedUiAutomationBackend backend = new(
            TimeProvider.System,
            new UiAutomationExecutionOptions(TimeSpan.FromSeconds(5)),
            workerExecutablePath: "powershell.exe",
            workerArguments: "-NoLogo -NoProfile -Command [Console]::Error.WriteLine('C:\\secret\\stack trace'); exit 1",
            diagnosticAuditLogOptions: options);

        UiaSnapshotBackendResult result = await backend.CaptureAsync(
            CreateWindow(),
            new UiaSnapshotRequest(),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(UiaSnapshotFailureStageValues.WorkerProcess, result.FailureStage);
        Assert.Equal("UIA worker process завершился с ошибкой.", result.Reason);
        Assert.DoesNotContain("C:\\secret", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(result.DiagnosticArtifactPath);
        Assert.True(File.Exists(result.DiagnosticArtifactPath));
        using JsonDocument diagnosticPayload = JsonDocument.Parse(await File.ReadAllTextAsync(result.DiagnosticArtifactPath!));
        Assert.Equal("C:\\secret\\stack trace", diagnosticPayload.RootElement.GetProperty("stderr").GetString()?.TrimEnd());
    }

    [Fact]
    public void CreateWorkerStartInfoUsesUtf8ForRedirectedStreams()
    {
        ProcessStartInfo startInfo = ProcessIsolatedUiAutomationBackend.CreateWorkerStartInfo(new UiaWorkerLaunchSpec("worker.exe", "--arg"));

        Assert.True(startInfo.RedirectStandardInput);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
        Assert.Equal(Encoding.UTF8.WebName, startInfo.StandardInputEncoding?.WebName);
        Assert.Equal(Encoding.UTF8.WebName, startInfo.StandardOutputEncoding?.WebName);
        Assert.Equal(Encoding.UTF8.WebName, startInfo.StandardErrorEncoding?.WebName);
    }

    [Fact]
    public void ResolveWorkerLaunchSpecPrefersExecutableWhenPresent()
    {
        string root = CreateTempDirectory();
        string executablePath = Path.Combine(root, "WinBridge.Runtime.Windows.UIA.Worker.exe");
        File.WriteAllText(executablePath, "worker");

        UiaWorkerLaunchSpec launchSpec = ProcessIsolatedUiAutomationBackend.ResolveWorkerLaunchSpec(root, currentHostPath: null);

        Assert.Equal(executablePath, launchSpec.FileName);
        Assert.Equal(string.Empty, launchSpec.Arguments);
    }

    [Fact]
    public void ResolveWorkerLaunchSpecFallsBackToDotNetHostedDllWhenExecutableIsMissing()
    {
        string root = CreateTempDirectory();
        string workerDllPath = Path.Combine(root, "WinBridge.Runtime.Windows.UIA.Worker.dll");
        string workerRuntimeConfigPath = Path.Combine(root, "WinBridge.Runtime.Windows.UIA.Worker.runtimeconfig.json");
        File.WriteAllText(workerDllPath, "worker");
        File.WriteAllText(workerRuntimeConfigPath, "{}");

        UiaWorkerLaunchSpec launchSpec = ProcessIsolatedUiAutomationBackend.ResolveWorkerLaunchSpec(
            root,
            currentHostPath: @"C:\Program Files\dotnet\dotnet.exe");

        Assert.Equal(@"C:\Program Files\dotnet\dotnet.exe", launchSpec.FileName);
        Assert.Equal($"\"{workerDllPath}\"", launchSpec.Arguments);
    }

    private static WindowDescriptor CreateWindow() =>
        new(
            Hwnd: 42,
            Title: "Calculator",
            ProcessName: "CalculatorApp",
            ProcessId: 42,
            ThreadId: 84,
            ClassName: "CalcWindow",
            Bounds: new Bounds(0, 0, 800, 600),
            IsForeground: true,
            IsVisible: true);

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
}
