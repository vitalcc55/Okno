// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using Microsoft.Extensions.DependencyInjection;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Guards;

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
        services.AddSingleton<IUiAutomationSetValueService, Win32UiAutomationSetValueService>();
        services.AddSingleton<IUiAutomationScrollService, Win32UiAutomationScrollService>();
        services.AddSingleton<IUiAutomationSecondaryActionService, Win32UiAutomationSecondaryActionService>();
        services.AddSingleton<IUiAutomationWaitProbe, ProcessIsolatedUiAutomationWaitProbe>();
        services.AddSingleton<IUiaGuardFactSource, UiaWorkerGuardFactSource>();
        return services;
    }
}
