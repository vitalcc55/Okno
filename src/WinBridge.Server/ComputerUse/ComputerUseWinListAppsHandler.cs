using ModelContextProtocol.Protocol;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;

namespace WinBridge.Server.ComputerUse;

internal sealed class ComputerUseWinListAppsHandler(ComputerUseWinAppDiscoveryService appDiscoveryService)
{
    public CallToolResult Execute(AuditInvocationScope invocation)
    {
        IReadOnlyList<ComputerUseWinAppDescriptor> apps = appDiscoveryService.ListVisibleApps();
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
