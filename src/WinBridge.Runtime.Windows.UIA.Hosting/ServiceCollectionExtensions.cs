using Microsoft.Extensions.DependencyInjection;
using WinBridge.Runtime.Diagnostics;

namespace WinBridge.Runtime.Windows.UIA;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWinBridgeRuntimeWindowsUia(
        this IServiceCollection services,
        string contentRootPath,
        string environmentName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);

        services.AddWinBridgeRuntimeDiagnostics(contentRootPath, environmentName);
        services.AddSingleton<IUiAutomationService, Win32UiAutomationService>();
        return services;
    }
}
