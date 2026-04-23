using WinBridge.Runtime.Diagnostics;

namespace WinBridge.Server.ComputerUse;

internal static class ComputerUseWinFailureCompletion
{
    public static void CompleteFailure(
        AuditInvocationScope invocation,
        ComputerUseWinFailureDetails failure,
        long? targetHwnd = null,
        bool bestEffort = false,
        IReadOnlyDictionary<string, string?>? data = null)
    {
        ArgumentNullException.ThrowIfNull(failure);
        CompleteFailure(invocation, failure.Reason, failure.FailureCode, targetHwnd, failure.AuditException, bestEffort, data);
    }

    public static void CompleteFailure(
        AuditInvocationScope invocation,
        string publicMessage,
        string? failureCode = null,
        long? targetHwnd = null,
        Exception? auditException = null,
        bool bestEffort = false,
        IReadOnlyDictionary<string, string?>? data = null)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentException.ThrowIfNullOrWhiteSpace(publicMessage);

        Dictionary<string, string?>? payload = null;
        if (data is not null || !string.IsNullOrWhiteSpace(failureCode))
        {
            payload = data is null
                ? new Dictionary<string, string?>(StringComparer.Ordinal)
                : new Dictionary<string, string?>(data, StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(failureCode))
            {
                payload["failure_code"] = failureCode;
            }
        }

        if (auditException is null)
        {
            if (bestEffort)
            {
                invocation.CompleteBestEffort("failed", publicMessage, targetHwnd, payload);
            }
            else
            {
                invocation.Complete("failed", publicMessage, targetHwnd, payload);
            }

            return;
        }

        if (bestEffort)
        {
            invocation.CompleteSanitizedFailureBestEffort(publicMessage, auditException, targetHwnd, payload);
        }
        else
        {
            invocation.CompleteSanitizedFailure(publicMessage, auditException, targetHwnd, payload);
        }
    }
}
