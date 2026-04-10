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
        EnsureRawExecutionAllowed(toolName);
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
        EnsureRawExecutionAllowed(toolName);
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

    public static TResult RunDeferred<TResult>(
        AuditLog auditLog,
        SessionSnapshot snapshot,
        string toolName,
        object? request,
        Func<AuditInvocationScope, TResult> callback)
    {
        ToolExecutionPolicyDescriptor? executionPolicy = EnsureDeferredExecutionAllowed(toolName);
        using AuditInvocationScope invocation = auditLog.BeginInvocation(toolName, request, snapshot, executionPolicy);

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

        using AuditInvocationScope invocation = auditLog.BeginInvocation(toolName, request, snapshot, policy);

        try
        {
            ToolExecutionDecision decision = gate.Evaluate(policy, intent);
            invocation.SetDecision(decision);
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

        using AuditInvocationScope invocation = auditLog.BeginInvocation(toolName, request, snapshot, policy);

        try
        {
            ToolExecutionDecision decision = gate.Evaluate(policy, intent);
            invocation.SetDecision(decision);
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

    private static void EnsureRawExecutionAllowed(string toolName)
    {
        ToolExecutionPolicyDescriptor? executionPolicy = ToolContractManifest.ResolveExecutionPolicy(toolName);
        if (executionPolicy is null)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Tool '{toolName}' declares execution policy and must use {nameof(RunGated)}/{nameof(RunGatedAsync)} to enforce the shared safety gate.");
    }

    private static ToolExecutionPolicyDescriptor? EnsureDeferredExecutionAllowed(string toolName)
    {
        ToolDescriptor? descriptor = ToolContractManifest.All.SingleOrDefault(item => string.Equals(item.Name, toolName, StringComparison.Ordinal));
        if (descriptor is null || descriptor.Lifecycle != ToolLifecycle.Deferred)
        {
            throw new InvalidOperationException(
                $"Tool '{toolName}' is not a deferred descriptor and cannot use {nameof(RunDeferred)}.");
        }

        return descriptor.ExecutionPolicy;
    }
}
