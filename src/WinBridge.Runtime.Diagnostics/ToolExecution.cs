using WinBridge.Runtime.Contracts;

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
}
