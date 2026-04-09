using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using WinBridge.Runtime;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Windows.Launch;

namespace WinBridge.Runtime.Tests;

public sealed class OpenTargetServiceTests
{
    [Fact]
    public async Task OpenAsyncDoesNotCallPlatformWhenValidationFails()
    {
        FakeOpenTargetPlatform platform = new(_ => throw new InvalidOperationException("platform should not be called"));
        OpenTargetService service = CreateService(platform);

        OpenTargetResult result = await service.OpenAsync(
            new OpenTargetRequest
            {
                TargetKind = OpenTargetKindValues.Url,
                Target = "mailto:user@example.test",
            },
            CancellationToken.None);

        Assert.Equal(OpenTargetStatusValues.Failed, result.Status);
        Assert.Equal(OpenTargetFailureCodeValues.UnsupportedUriScheme, result.FailureCode);
        Assert.Equal(0, platform.CallCount);
        Assert.Null(result.AcceptedAtUtc);
        Assert.Null(result.ArtifactPath);
    }

    [Fact]
    public async Task OpenAsyncDoesNotCallPlatformWhenExistingPathDoesNotMatchDeclaredKind()
    {
        string filePath = CreateTempFilePath("report.pdf");
        File.WriteAllText(filePath, "stub");
        FakeOpenTargetPlatform platform = new(_ => throw new InvalidOperationException("platform should not be called"));
        FakeOpenTargetPathInspector pathInspector = new(_ => OpenTargetResolvedPathKind.ExistingFile);
        OpenTargetService service = CreateService(platform, pathInspector: pathInspector);

        OpenTargetResult result = await service.OpenAsync(
            new OpenTargetRequest
            {
                TargetKind = OpenTargetKindValues.Folder,
                Target = filePath,
            },
            CancellationToken.None);

        Assert.Equal(OpenTargetStatusValues.Failed, result.Status);
        Assert.Equal(OpenTargetFailureCodeValues.UnsupportedTargetKind, result.FailureCode);
        Assert.Equal(0, platform.CallCount);
        Assert.Equal(1, pathInspector.CallCount);
        Assert.NotNull(result.ArtifactPath);
        Assert.True(File.Exists(result.ArtifactPath));
    }

    [Fact]
    public async Task OpenAsyncDoesNotCallPlatformWhenExistingDirectoryDoesNotMatchDeclaredDocumentKind()
    {
        string directoryPath = CreateWorkingDirectory();
        FakeOpenTargetPlatform platform = new(_ => throw new InvalidOperationException("platform should not be called"));
        FakeOpenTargetPathInspector pathInspector = new(_ => OpenTargetResolvedPathKind.ExistingDirectory);
        OpenTargetService service = CreateService(platform, pathInspector: pathInspector);

        OpenTargetResult result = await service.OpenAsync(
            new OpenTargetRequest
            {
                TargetKind = OpenTargetKindValues.Document,
                Target = directoryPath,
            },
            CancellationToken.None);

        Assert.Equal(OpenTargetStatusValues.Failed, result.Status);
        Assert.Equal(OpenTargetFailureCodeValues.UnsupportedTargetKind, result.FailureCode);
        Assert.Equal(0, platform.CallCount);
        Assert.Equal(1, pathInspector.CallCount);
        Assert.NotNull(result.ArtifactPath);
        Assert.True(File.Exists(result.ArtifactPath));
    }

    [Fact]
    public async Task OpenAsyncAllowsUnresolvedPathToProceedToPlatform()
    {
        string path = @"\\server\share\missing-report.pdf";
        FakeOpenTargetPlatform platform = new(_ => new OpenTargetPlatformResult(
            IsAccepted: false,
            FailureCode: OpenTargetFailureCodeValues.TargetNotFound,
            FailureReason: "Shell-open target не найден."));
        FakeOpenTargetPathInspector pathInspector = new(_ => OpenTargetResolvedPathKind.Unresolved);
        OpenTargetService service = CreateService(platform, pathInspector: pathInspector);

        OpenTargetResult result = await service.OpenAsync(
            new OpenTargetRequest
            {
                TargetKind = OpenTargetKindValues.Document,
                Target = path,
            },
            CancellationToken.None);

        Assert.Equal(OpenTargetStatusValues.Failed, result.Status);
        Assert.Equal(OpenTargetFailureCodeValues.TargetNotFound, result.FailureCode);
        Assert.Equal(1, platform.CallCount);
        Assert.Equal(1, pathInspector.CallCount);
    }

    [Fact]
    public async Task OpenAsyncReturnsTargetOpenRequestedWithoutHandlerProcessIdWhenShellAcceptsWithoutProcess()
    {
        FakeTimeProvider timeProvider = new(new DateTimeOffset(2026, 4, 8, 12, 0, 0, TimeSpan.Zero));
        FakeOpenTargetPlatform platform = new(_ => new OpenTargetPlatformResult(IsAccepted: true));
        OpenTargetService service = CreateService(platform, timeProvider);

        OpenTargetResult result = await service.OpenAsync(
            new OpenTargetRequest
            {
                TargetKind = OpenTargetKindValues.Url,
                Target = "https://example.test/docs?q=hidden#fragment",
            },
            CancellationToken.None);

        Assert.Equal(OpenTargetStatusValues.Done, result.Status);
        Assert.Equal(OpenTargetStatusValues.Done, result.Decision);
        Assert.Equal(OpenTargetResultModeValues.TargetOpenRequested, result.ResultMode);
        Assert.Equal(OpenTargetKindValues.Url, result.TargetKind);
        Assert.Null(result.TargetIdentity);
        Assert.Equal("https", result.UriScheme);
        Assert.Equal(timeProvider.GetUtcNow(), result.AcceptedAtUtc);
        Assert.Null(result.HandlerProcessId);
        Assert.NotNull(result.ArtifactPath);
        Assert.EndsWith(".json", result.ArtifactPath, StringComparison.Ordinal);
        Assert.Equal(1, platform.CallCount);
    }

    [Fact]
    public async Task OpenAsyncReturnsHandlerProcessObservedWhenShellReturnsHandlerProcessId()
    {
        FakeTimeProvider timeProvider = new(new DateTimeOffset(2026, 4, 8, 12, 1, 0, TimeSpan.Zero));
        FakeOpenTargetPlatform platform = new(_ => new OpenTargetPlatformResult(
            IsAccepted: true,
            HandlerProcessId: 4242));
        OpenTargetService service = CreateService(platform, timeProvider);

        OpenTargetResult result = await service.OpenAsync(
            new OpenTargetRequest
            {
                TargetKind = OpenTargetKindValues.Document,
                Target = @"C:\Docs\Quarterly\report.pdf",
            },
            CancellationToken.None);

        Assert.Equal(OpenTargetStatusValues.Done, result.Status);
        Assert.Equal(OpenTargetResultModeValues.HandlerProcessObserved, result.ResultMode);
        Assert.Equal(OpenTargetKindValues.Document, result.TargetKind);
        Assert.Equal("report.pdf", result.TargetIdentity);
        Assert.Equal(4242, result.HandlerProcessId);
        Assert.Equal(timeProvider.GetUtcNow(), result.AcceptedAtUtc);
        Assert.NotNull(result.ArtifactPath);
    }

    [Fact]
    public async Task OpenAsyncReturnsRuntimeFailureWithSafeClassifiedMetadata()
    {
        FakeOpenTargetPlatform platform = new(_ => new OpenTargetPlatformResult(
            IsAccepted: false,
            FailureCode: OpenTargetFailureCodeValues.TargetNotFound,
            FailureReason: "Shell-open target не найден."));
        OpenTargetService service = CreateService(platform);

        OpenTargetResult result = await service.OpenAsync(
            new OpenTargetRequest
            {
                TargetKind = OpenTargetKindValues.Folder,
                Target = @"C:\Workspace\Reports",
            },
            CancellationToken.None);

        Assert.Equal(OpenTargetStatusValues.Failed, result.Status);
        Assert.Equal(OpenTargetFailureCodeValues.TargetNotFound, result.FailureCode);
        Assert.Equal(OpenTargetKindValues.Folder, result.TargetKind);
        Assert.Equal("Reports", result.TargetIdentity);
        Assert.Null(result.UriScheme);
        Assert.Null(result.AcceptedAtUtc);
        Assert.Null(result.HandlerProcessId);
        Assert.NotNull(result.ArtifactPath);
    }

    [Fact]
    public async Task OpenAsyncMaterializesTerminalFailureWhenPathInspectionThrows()
    {
        FakeOpenTargetPlatform platform = new(_ => throw new InvalidOperationException("platform should not be called"));
        FakeOpenTargetPathInspector pathInspector = new(_ => throw new IOException("boom"));
        OpenTargetService service = CreateService(platform, pathInspector: pathInspector);

        OpenTargetResult result = await service.OpenAsync(
            new OpenTargetRequest
            {
                TargetKind = OpenTargetKindValues.Document,
                Target = @"C:\Docs\report.pdf",
            },
            CancellationToken.None);

        Assert.Equal(OpenTargetStatusValues.Failed, result.Status);
        Assert.Null(result.FailureCode);
        Assert.Equal(OpenTargetKindValues.Document, result.TargetKind);
        Assert.Equal("report.pdf", result.TargetIdentity);
        Assert.NotNull(result.ArtifactPath);
        Assert.True(File.Exists(result.ArtifactPath));
        using (JsonDocument artifact = JsonDocument.Parse(File.ReadAllText(result.ArtifactPath)))
        {
            JsonElement diagnostics = artifact.RootElement.GetProperty("failure_diagnostics");
            Assert.Equal("path_inspection", diagnostics.GetProperty("failure_stage").GetString());
            Assert.Equal("System.IO.IOException", diagnostics.GetProperty("exception_type").GetString());
        }

        string eventsPath = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(result.ArtifactPath)!)!, "events.jsonl");
        string[] eventLines = File.ReadAllLines(eventsPath);
        Assert.Single(eventLines);
        Assert.Contains("\"failure_stage\":\"path_inspection\"", eventLines[0], StringComparison.Ordinal);
        Assert.Contains("\"exception_type\":\"System.IO.IOException\"", eventLines[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenAsyncMaterializesTerminalFailureWhenPlatformThrows()
    {
        FakeOpenTargetPlatform platform = new(_ => throw new InvalidOperationException("boom"));
        OpenTargetService service = CreateService(platform);

        OpenTargetResult result = await service.OpenAsync(
            new OpenTargetRequest
            {
                TargetKind = OpenTargetKindValues.Url,
                Target = "https://example.test/docs",
            },
            CancellationToken.None);

        Assert.Equal(OpenTargetStatusValues.Failed, result.Status);
        Assert.Null(result.FailureCode);
        Assert.Equal(OpenTargetKindValues.Url, result.TargetKind);
        Assert.Equal("https", result.UriScheme);
        Assert.NotNull(result.ArtifactPath);
        Assert.True(File.Exists(result.ArtifactPath));
        using (JsonDocument artifact = JsonDocument.Parse(File.ReadAllText(result.ArtifactPath)))
        {
            JsonElement diagnostics = artifact.RootElement.GetProperty("failure_diagnostics");
            Assert.Equal("platform_open", diagnostics.GetProperty("failure_stage").GetString());
            Assert.Equal("System.InvalidOperationException", diagnostics.GetProperty("exception_type").GetString());
        }

        string[] eventLines = File.ReadAllLines(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(result.ArtifactPath)!)!, "events.jsonl"));
        Assert.Single(eventLines);
        Assert.Contains("\"failure_stage\":\"platform_open\"", eventLines[0], StringComparison.Ordinal);
        Assert.Contains("\"exception_type\":\"System.InvalidOperationException\"", eventLines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void AddWinBridgeRuntimeRegistersOpenTargetServiceAndPlatform()
    {
        string root = CreateWorkingDirectory();
        ServiceCollection services = new();
        services.AddWinBridgeRuntime(root, "Tests");

        using ServiceProvider provider = services.BuildServiceProvider();

        IOpenTargetService service = provider.GetRequiredService<IOpenTargetService>();
        IOpenTargetPlatform platform = provider.GetRequiredService<IOpenTargetPlatform>();

        Assert.IsType<OpenTargetService>(service);
        Assert.IsType<ShellExecuteOpenTargetPlatform>(platform);
    }

    private static OpenTargetService CreateService(
        FakeOpenTargetPlatform platform,
        TimeProvider? timeProvider = null,
        FakeOpenTargetPathInspector? pathInspector = null)
    {
        string root = CreateWorkingDirectory();
        AuditLogOptions options = new(
            ContentRootPath: root,
            EnvironmentName: "Tests",
            RunId: Guid.NewGuid().ToString("N"),
            DiagnosticsRoot: Path.Combine(root, "artifacts", "diagnostics"),
            RunDirectory: Path.Combine(root, "artifacts", "diagnostics", "open-target-service-tests"),
            EventsPath: Path.Combine(root, "artifacts", "diagnostics", "open-target-service-tests", "events.jsonl"),
            SummaryPath: Path.Combine(root, "artifacts", "diagnostics", "open-target-service-tests", "summary.md"));
        TimeProvider effectiveTimeProvider = timeProvider ?? TimeProvider.System;
        AuditLog auditLog = new(options, effectiveTimeProvider);
        OpenTargetResultMaterializer materializer = new(auditLog, options, effectiveTimeProvider);

        return new OpenTargetService(
            platform,
            effectiveTimeProvider,
            pathInspector ?? new FakeOpenTargetPathInspector(_ => OpenTargetResolvedPathKind.Unresolved),
            materializer);
    }

    private static string CreateWorkingDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string CreateTempFilePath(string fileName)
    {
        string directory = CreateWorkingDirectory();
        return Path.Combine(directory, fileName);
    }

    private sealed class FakeOpenTargetPlatform(Func<OpenTargetPlatformRequest, OpenTargetPlatformResult> openHandler) : IOpenTargetPlatform
    {
        public OpenTargetPlatformRequest? LastRequest { get; private set; }

        public CancellationToken LastCancellationToken { get; private set; }

        public int CallCount { get; private set; }

        public OpenTargetPlatformResult Open(OpenTargetPlatformRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            LastCancellationToken = cancellationToken;
            return openHandler(request);
        }
    }

    private sealed class FakeOpenTargetPathInspector(Func<string, OpenTargetResolvedPathKind> inspectHandler) : IOpenTargetPathInspector
    {
        public int CallCount { get; private set; }

        public OpenTargetResolvedPathKind Inspect(string target)
        {
            CallCount++;
            return inspectHandler(target);
        }
    }

    private sealed class FakeTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
