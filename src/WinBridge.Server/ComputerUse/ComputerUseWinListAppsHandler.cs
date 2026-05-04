// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using ModelContextProtocol.Protocol;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;

namespace WinBridge.Server.ComputerUse;

internal sealed class ComputerUseWinListAppsHandler(ComputerUseWinAppDiscoveryService appDiscoveryService)
{
    public CallToolResult Execute(AuditInvocationScope invocation)
    {
        IReadOnlyList<ComputerUseWinAppDescriptor> apps = appDiscoveryService.ListVisibleApps()
            .Select(static app => new ComputerUseWinAppDescriptor(
                AppId: app.ApprovalKey.Value,
                Windows: app.Windows.Select(static window => new ComputerUseWinWindowDescriptor(
                    WindowId: window.PublicWindowId
                        ?? throw new InvalidOperationException("Published discovery windows must carry a public windowId."),
                    Hwnd: window.Window.Hwnd,
                    Title: window.Window.Title,
                    ProcessName: window.Window.ProcessName,
                    ProcessId: window.Window.ProcessId,
                    IsForeground: window.Window.IsForeground,
                    IsVisible: window.Window.IsVisible))
                    .ToArray(),
                IsApproved: app.IsApproved,
                IsBlocked: app.IsBlocked,
                BlockReason: app.BlockReason))
            .ToArray();
        ComputerUseWinListAppsResult payload = new(
            Status: ComputerUseWinStatusValues.Ok,
            Apps: apps,
            Count: apps.Count);

        invocation.Complete(
            "done",
            $"Возвращено {apps.Count} app entries для Computer Use for Windows.");

        return ComputerUseWinToolResultFactory.CreateToolResult(payload, isError: false);
    }
}
