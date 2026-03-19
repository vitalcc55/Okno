using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace WinBridge.Runtime.Diagnostics;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWinBridgeRuntimeDiagnostics(
        this IServiceCollection services,
        string contentRootPath,
        string environmentName,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);

        TimeProvider resolvedTimeProvider = timeProvider ?? TimeProvider.System;

        services.TryAddSingleton<TimeProvider>(resolvedTimeProvider);
        services.TryAddSingleton<AuditLogOptions>(_ => AuditLogOptions.Create(contentRootPath, environmentName));
        services.TryAddSingleton<AuditLog>();

        return services;
    }
}
