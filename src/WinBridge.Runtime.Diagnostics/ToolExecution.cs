using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Guards;
using WinBridge.Runtime.Tooling;

namespace WinBridge.Runtime.Diagnostics;

public static class ToolExecution
{
    public static TResult Run<TResult>(
        AuditLog auditLog,
        SessionSnapshot snapshot,
        string toolName,
        object? request,
        Func<AuditInvocationScope, TResult> callback)
    {
        using AuditInvocationScope invocation = auditLog.BeginInvocation(toolName, request, snapshot);

        try
        {
            return callback(invocation);
        }
        catch (Exception exception)
        {
            invocation.Fail(exception);
            throw;
        }
    }

    public static async Task<TResult> RunAsync<TResult>(
        AuditLog auditLog,
        SessionSnapshot snapshot,
        string toolName,
        object? request,
        Func<AuditInvocationScope, Task<TResult>> callback)
    {
        using AuditInvocationScope invocation = auditLog.BeginInvocation(toolName, request, snapshot);

        try
        {
            return await callback(invocation).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            invocation.Fail(exception);
            throw;
        }
    }

    public static TResult RunGated<TResult>(
        AuditLog auditLog,
        SessionSnapshot snapshot,
        string toolName,
        object? request,
        ToolExecutionPolicyDescriptor policy,
        ToolExecutionIntent intent,
        IToolExecutionGate gate,
        Func<AuditInvocationScope, ToolExecutionDecision, TResult> onAllowed,
        Func<AuditInvocationScope, ToolExecutionDecision, TResult> onRejected)
    {
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(onAllowed);
        ArgumentNullException.ThrowIfNull(onRejected);

        using AuditInvocationScope invocation = auditLog.BeginInvocation(toolName, request, snapshot);

        try
        {
            ToolExecutionDecision decision = gate.Evaluate(policy, intent);
            return decision.IsAllowed
                ? onAllowed(invocation, decision)
                : onRejected(invocation, decision);
        }
        catch (Exception exception)
        {
            invocation.Fail(exception);
            throw;
        }
    }

    public static async Task<TResult> RunGatedAsync<TResult>(
        AuditLog auditLog,
        SessionSnapshot snapshot,
        string toolName,
        object? request,
        ToolExecutionPolicyDescriptor policy,
        ToolExecutionIntent intent,
        IToolExecutionGate gate,
        Func<AuditInvocationScope, ToolExecutionDecision, Task<TResult>> onAllowed,
        Func<AuditInvocationScope, ToolExecutionDecision, Task<TResult>> onRejected)
    {
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(onAllowed);
        ArgumentNullException.ThrowIfNull(onRejected);

        using AuditInvocationScope invocation = auditLog.BeginInvocation(toolName, request, snapshot);

        try
        {
            ToolExecutionDecision decision = gate.Evaluate(policy, intent);
            return decision.IsAllowed
                ? await onAllowed(invocation, decision).ConfigureAwait(false)
                : await onRejected(invocation, decision).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            invocation.Fail(exception);
            throw;
        }
    }
}
