// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

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
        services.AddSingleton<IToolExecutionGate, ToolExecutionGate>();
        return services;
    }
}
