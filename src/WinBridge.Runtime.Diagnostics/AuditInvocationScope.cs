using System.Diagnostics;

namespace WinBridge.Runtime.Diagnostics;

public sealed class AuditInvocationScope : IDisposable
{
    private readonly AuditLog _auditLog;
    private readonly Activity? _activity;
    private readonly string _toolName;
    private bool _completed;

    internal AuditInvocationScope(AuditLog auditLog, Activity? activity, string toolName)
    {
        _auditLog = auditLog;
        _activity = activity;
        _toolName = toolName;
    }

    public void Complete(
        string outcome,
        string message,
        long? windowHwnd = null,
        IReadOnlyDictionary<string, string?>? data = null)
    {
        if (_completed)
        {
            return;
        }

        _auditLog.WriteToolCompleted(_toolName, outcome, message, windowHwnd, data);
        _completed = true;
    }

    public void Fail(Exception exception, long? windowHwnd = null)
    {
        if (_completed)
        {
            return;
        }

        _auditLog.WriteToolCompleted(
            _toolName,
            "failed",
            exception.Message,
            windowHwnd,
            new Dictionary<string, string?>
            {
                ["exception_type"] = exception.GetType().FullName,
            });

        _completed = true;
    }

    public void Dispose()
    {
        if (!_completed)
        {
            Complete("cancelled", "Tool scope closed without explicit completion.");
        }

        _activity?.Dispose();
    }
}
