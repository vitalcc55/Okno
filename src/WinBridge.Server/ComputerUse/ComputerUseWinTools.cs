using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Session;
using WinBridge.Runtime.Tooling;
using RuntimeToolExecution = WinBridge.Runtime.Diagnostics.ToolExecution;

namespace WinBridge.Server.ComputerUse;

internal sealed class ComputerUseWinTools
{
    private readonly AuditLog auditLog;
    private readonly ComputerUseWinClickHandler clickHandler;
    private readonly ComputerUseWinGetAppStateHandler getAppStateHandler;
    private readonly ComputerUseWinListAppsHandler listAppsHandler;
    private readonly ISessionManager sessionManager;

    public ComputerUseWinTools(
        AuditLog auditLog,
        ISessionManager sessionManager,
        ComputerUseWinListAppsHandler listAppsHandler,
        ComputerUseWinGetAppStateHandler getAppStateHandler,
        ComputerUseWinClickHandler clickHandler)
    {
        this.auditLog = auditLog;
        this.sessionManager = sessionManager;
        this.listAppsHandler = listAppsHandler;
        this.getAppStateHandler = getAppStateHandler;
        this.clickHandler = clickHandler;
    }

    public CallToolResult ListApps()
        => RuntimeToolExecution.Run(
            auditLog,
            sessionManager.GetSnapshot(),
            ToolNames.ComputerUseWinListApps,
            new { },
            listAppsHandler.Execute);

    public Task<CallToolResult> GetAppState(
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken)
    {
        Binding<ComputerUseWinGetAppStateRequest> binding = BindRequest(requestContext, new ComputerUseWinGetAppStateRequest());
        return RuntimeToolExecution.RunAsync(
            auditLog,
            sessionManager.GetSnapshot(),
            ToolNames.ComputerUseWinGetAppState,
            binding.Request,
            invocation => binding.IsSuccess
                ? getAppStateHandler.ExecuteAsync(invocation, binding.Request, cancellationToken)
                : Task.FromResult(ComputerUseWinToolResultFactory.CreateStateFailure(invocation, binding.FailureCode!, binding.Reason!)));
    }

    public Task<CallToolResult> Click(
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken)
    {
        Binding<ComputerUseWinClickRequest> binding = BindRequest(requestContext, new ComputerUseWinClickRequest());
        return RuntimeToolExecution.RunAsync(
            auditLog,
            sessionManager.GetSnapshot(),
            ToolNames.ComputerUseWinClick,
            binding.Request,
            invocation => binding.IsSuccess
                ? clickHandler.ExecuteAsync(invocation, binding.Request, cancellationToken)
                : Task.FromResult(ComputerUseWinToolResultFactory.CreateActionFailure(invocation, ToolNames.ComputerUseWinClick, binding.FailureCode!, binding.Reason!)));
    }

    private static Binding<T> BindRequest<T>(RequestContext<CallToolRequestParams> requestContext, T fallbackRequest)
        => ToolRequestBinder.TryBind(
            requestContext.Params?.Arguments,
            fallbackRequest,
            out T request,
            out string? reason,
            ComputerUseWinRequestContractValidator.Validate)
            ? new(true, request, null, null)
            : new(false, fallbackRequest, ComputerUseWinFailureCodeValues.InvalidRequest, $"Transport arguments не прошли binding: {reason}");

    private readonly record struct Binding<T>(
        bool IsSuccess,
        T Request,
        string? FailureCode,
        string? Reason);
}
