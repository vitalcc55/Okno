using Microsoft.Extensions.DependencyInjection;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Session;
using WinBridge.Runtime.Windows.Capture;
using WinBridge.Runtime.Windows.Shell;

namespace WinBridge.Runtime;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWinBridgeRuntime(
        this IServiceCollection services,
        string contentRootPath,
        string environmentName)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(_ => AuditLogOptions.Create(contentRootPath, environmentName));
        services.AddSingleton(sp => new SessionContext(sp.GetRequiredService<AuditLogOptions>().RunId));
        services.AddSingleton<AuditLog>();
        services.AddSingleton<ISessionManager, InMemorySessionManager>();
        services.AddSingleton<ICaptureService, GraphicsCaptureService>();
        services.AddSingleton<IWindowManager, Win32WindowManager>();
        services.AddSingleton<RuntimeInfo>();

        return services;
    }
}
