using Microsoft.Extensions.DependencyInjection;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Guards;
using WinBridge.Runtime.Session;
using WinBridge.Runtime.Waiting;
using WinBridge.Runtime.Windows.Capture;
using WinBridge.Runtime.Windows.Display;
using WinBridge.Runtime.Windows.Launch;
using WinBridge.Runtime.Windows.Shell;

namespace WinBridge.Runtime;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWinBridgeRuntime(
        this IServiceCollection services,
        string contentRootPath,
        string environmentName)
    {
        services.AddWinBridgeRuntimeDiagnostics(contentRootPath, environmentName);
        services.AddWinBridgeRuntimeGuards();
        services.AddSingleton<ICaptureGuardFactSource, GraphicsCaptureGuardFactSource>();
        services.AddSingleton(sp => new SessionContext(sp.GetRequiredService<AuditLogOptions>().RunId));
        services.AddSingleton<ISessionManager, InMemorySessionManager>();
        services.AddSingleton<IMonitorManager, Win32MonitorManager>();
        services.AddSingleton<GraphicsCaptureService>();
        services.AddSingleton<ICaptureService>(sp => sp.GetRequiredService<GraphicsCaptureService>());
        services.AddSingleton<IWaitVisualProbe>(sp => sp.GetRequiredService<GraphicsCaptureService>());
        services.AddSingleton<IWindowManager, Win32WindowManager>();
        services.AddSingleton<IWindowTargetResolver, WindowTargetResolver>();
        services.AddSingleton(WindowActivationOptions.Default);
        services.AddSingleton<IWindowActivationPlatform, Win32WindowActivationPlatform>();
        services.AddSingleton<IWindowActivationService, WindowActivationService>();
        services.AddSingleton(ProcessLaunchOptions.Default);
        services.AddSingleton<IProcessLaunchPlatform, SystemProcessLaunchPlatform>();
        services.AddSingleton<LaunchResultMaterializer>();
        services.AddSingleton<IProcessLaunchService>(
            sp => new ProcessLaunchService(
                sp.GetRequiredService<IProcessLaunchPlatform>(),
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<ProcessLaunchOptions>(),
                sp.GetRequiredService<LaunchResultMaterializer>()));
        services.AddSingleton<IOpenTargetPathInspector, FileSystemOpenTargetPathInspector>();
        services.AddSingleton<IOpenTargetPlatform, ShellExecuteOpenTargetPlatform>();
        services.AddSingleton<IOpenTargetService>(
            sp => new OpenTargetService(
                sp.GetRequiredService<IOpenTargetPlatform>(),
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<IOpenTargetPathInspector>()));
        services.AddSingleton(WaitOptions.Default);
        services.AddSingleton<WaitResultMaterializer>();
        services.AddSingleton<IWaitService, PollingWaitService>();
        services.AddSingleton<RuntimeInfo>();

        return services;
    }
}
