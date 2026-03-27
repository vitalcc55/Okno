using Microsoft.Extensions.DependencyInjection;

namespace WinBridge.Runtime.Guards;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWinBridgeRuntimeGuards(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IRuntimeGuardPlatform, Win32RuntimeGuardPlatform>();
        services.AddSingleton<ICaptureGuardFactSource, DefaultCaptureGuardFactSource>();
        services.AddSingleton<IUiaGuardFactSource, DefaultUiaGuardFactSource>();
        services.AddSingleton<IRuntimeGuardService, RuntimeGuardService>();
        return services;
    }
}
