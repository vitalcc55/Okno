// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

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
