using Microsoft.Extensions.DependencyInjection;
using WinBridge.Runtime;
using WinBridge.Runtime.Contracts;
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
        Assert.Null(result.ArtifactPath);
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
        Assert.Null(result.ArtifactPath);
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
        Assert.Null(result.ArtifactPath);
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
        FakeOpenTargetPathInspector? pathInspector = null) =>
        new(
            platform,
            timeProvider ?? TimeProvider.System,
            pathInspector ?? new FakeOpenTargetPathInspector(_ => OpenTargetResolvedPathKind.Unresolved));

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

        public int CallCount { get; private set; }

        public OpenTargetPlatformResult Open(OpenTargetPlatformRequest request)
        {
            CallCount++;
            LastRequest = request;
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
