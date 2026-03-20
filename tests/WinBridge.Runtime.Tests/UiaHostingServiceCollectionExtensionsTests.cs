using Microsoft.Extensions.DependencyInjection;
using WinBridge.Runtime;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Waiting;
using WinBridge.Runtime.Windows.Capture;
using WinBridge.Runtime.Windows.UIA;
using WinBridge.Runtime.Windows.Shell;

namespace WinBridge.Runtime.Tests;

public sealed class UiaHostingServiceCollectionExtensionsTests
{
    [Fact]
    public void AddWinBridgeRuntimeWindowsUiaResolvesMinimalConsumerBoundary()
    {
        string root = CreateTempDirectory();
        ServiceCollection services = new();

        services.AddWinBridgeRuntimeWindowsUia(root, "Tests");

        using ServiceProvider provider = services.BuildServiceProvider();

        IUiAutomationService service = provider.GetRequiredService<IUiAutomationService>();
        AuditLog auditLog = provider.GetRequiredService<AuditLog>();
        AuditLogOptions options = provider.GetRequiredService<AuditLogOptions>();
        TimeProvider timeProvider = provider.GetRequiredService<TimeProvider>();

        Assert.IsType<Win32UiAutomationService>(service);
        Assert.NotNull(auditLog);
        Assert.Same(TimeProvider.System, timeProvider);
        Assert.Equal(root, options.ContentRootPath);
        Assert.Equal("Tests", options.EnvironmentName);
        Assert.StartsWith(Path.Combine(root, "artifacts", "diagnostics"), options.RunDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.Null(provider.GetService<IWindowTargetResolver>());
    }

    [Fact]
    public void CombinedRuntimeAndUiaRegistrationResolvesWaitService()
    {
        string root = CreateTempDirectory();
        ServiceCollection services = new();

        services.AddWinBridgeRuntime(root, "Tests");
        services.AddWinBridgeRuntimeWindowsUia(root, "Tests");

        using ServiceProvider provider = services.BuildServiceProvider();

        IWaitService waitService = provider.GetRequiredService<IWaitService>();
        IUiAutomationWaitProbe waitProbe = provider.GetRequiredService<IUiAutomationWaitProbe>();
        IWaitVisualProbe visualProbe = provider.GetRequiredService<IWaitVisualProbe>();

        Assert.IsType<PollingWaitService>(waitService);
        Assert.IsType<ProcessIsolatedUiAutomationWaitProbe>(waitProbe);
        Assert.IsType<GraphicsCaptureService>(visualProbe);
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
