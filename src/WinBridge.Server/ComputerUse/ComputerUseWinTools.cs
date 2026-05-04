// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

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
    private readonly ComputerUseWinDragHandler dragHandler;
    private readonly ComputerUseWinGetAppStateHandler getAppStateHandler;
    private readonly ComputerUseWinListAppsHandler listAppsHandler;
    private readonly ComputerUseWinPerformSecondaryActionHandler performSecondaryActionHandler;
    private readonly ComputerUseWinPressKeyHandler pressKeyHandler;
    private readonly ComputerUseWinScrollHandler scrollHandler;
    private readonly ComputerUseWinSetValueHandler setValueHandler;
    private readonly ComputerUseWinTypeTextHandler typeTextHandler;
    private readonly ISessionManager sessionManager;

    public ComputerUseWinTools(
        AuditLog auditLog,
        ISessionManager sessionManager,
        ComputerUseWinListAppsHandler listAppsHandler,
        ComputerUseWinGetAppStateHandler getAppStateHandler,
        ComputerUseWinClickHandler clickHandler,
        ComputerUseWinDragHandler dragHandler,
        ComputerUseWinPerformSecondaryActionHandler performSecondaryActionHandler,
        ComputerUseWinPressKeyHandler pressKeyHandler,
        ComputerUseWinScrollHandler scrollHandler,
        ComputerUseWinSetValueHandler setValueHandler,
        ComputerUseWinTypeTextHandler typeTextHandler)
    {
        this.auditLog = auditLog;
        this.sessionManager = sessionManager;
        this.listAppsHandler = listAppsHandler;
        this.getAppStateHandler = getAppStateHandler;
        this.clickHandler = clickHandler;
        this.dragHandler = dragHandler;
        this.performSecondaryActionHandler = performSecondaryActionHandler;
        this.pressKeyHandler = pressKeyHandler;
        this.scrollHandler = scrollHandler;
        this.setValueHandler = setValueHandler;
        this.typeTextHandler = typeTextHandler;
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

    public Task<CallToolResult> Drag(
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken)
    {
        Binding<ComputerUseWinDragRequest> binding = BindRequest(requestContext, new ComputerUseWinDragRequest());
        return RuntimeToolExecution.RunAsync(
            auditLog,
            sessionManager.GetSnapshot(),
            ToolNames.ComputerUseWinDrag,
            binding.Request,
            invocation => binding.IsSuccess
                ? dragHandler.ExecuteAsync(invocation, binding.Request, cancellationToken)
                : Task.FromResult(ComputerUseWinToolResultFactory.CreateActionFailure(invocation, ToolNames.ComputerUseWinDrag, binding.FailureCode!, binding.Reason!)));
    }

    public Task<CallToolResult> PressKey(
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken)
    {
        Binding<ComputerUseWinPressKeyRequest> binding = BindRequest(requestContext, new ComputerUseWinPressKeyRequest());
        return RuntimeToolExecution.RunAsync(
            auditLog,
            sessionManager.GetSnapshot(),
            ToolNames.ComputerUseWinPressKey,
            binding.Request,
            invocation => binding.IsSuccess
                ? pressKeyHandler.ExecuteAsync(invocation, binding.Request, cancellationToken)
                : Task.FromResult(ComputerUseWinToolResultFactory.CreateActionFailure(invocation, ToolNames.ComputerUseWinPressKey, binding.FailureCode!, binding.Reason!)));
    }

    public Task<CallToolResult> PerformSecondaryAction(
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken)
    {
        Binding<ComputerUseWinPerformSecondaryActionRequest> binding = BindRequest(requestContext, new ComputerUseWinPerformSecondaryActionRequest());
        return RuntimeToolExecution.RunAsync(
            auditLog,
            sessionManager.GetSnapshot(),
            ToolNames.ComputerUseWinPerformSecondaryAction,
            binding.Request,
            invocation => binding.IsSuccess
                ? performSecondaryActionHandler.ExecuteAsync(invocation, binding.Request, cancellationToken)
                : Task.FromResult(ComputerUseWinToolResultFactory.CreateActionFailure(invocation, ToolNames.ComputerUseWinPerformSecondaryAction, binding.FailureCode!, binding.Reason!)));
    }

    public Task<CallToolResult> SetValue(
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken)
    {
        Binding<ComputerUseWinSetValueRequest> binding = BindRequest(requestContext, new ComputerUseWinSetValueRequest());
        return RuntimeToolExecution.RunAsync(
            auditLog,
            sessionManager.GetSnapshot(),
            ToolNames.ComputerUseWinSetValue,
            binding.Request,
            invocation => binding.IsSuccess
                ? setValueHandler.ExecuteAsync(invocation, binding.Request, cancellationToken)
                : Task.FromResult(ComputerUseWinToolResultFactory.CreateActionFailure(invocation, ToolNames.ComputerUseWinSetValue, binding.FailureCode!, binding.Reason!)));
    }

    public Task<CallToolResult> Scroll(
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken)
    {
        Binding<ComputerUseWinScrollRequest> binding = BindRequest(requestContext, new ComputerUseWinScrollRequest());
        return RuntimeToolExecution.RunAsync(
            auditLog,
            sessionManager.GetSnapshot(),
            ToolNames.ComputerUseWinScroll,
            binding.Request,
            invocation => binding.IsSuccess
                ? scrollHandler.ExecuteAsync(invocation, binding.Request, cancellationToken)
                : Task.FromResult(ComputerUseWinToolResultFactory.CreateActionFailure(invocation, ToolNames.ComputerUseWinScroll, binding.FailureCode!, binding.Reason!)));
    }

    public Task<CallToolResult> TypeText(
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken)
    {
        Binding<ComputerUseWinTypeTextRequest> binding = BindRequest(requestContext, new ComputerUseWinTypeTextRequest());
        return RuntimeToolExecution.RunAsync(
            auditLog,
            sessionManager.GetSnapshot(),
            ToolNames.ComputerUseWinTypeText,
            binding.Request,
            invocation => binding.IsSuccess
                ? typeTextHandler.ExecuteAsync(invocation, binding.Request, cancellationToken)
                : Task.FromResult(ComputerUseWinToolResultFactory.CreateActionFailure(invocation, ToolNames.ComputerUseWinTypeText, binding.FailureCode!, binding.Reason!)));
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
