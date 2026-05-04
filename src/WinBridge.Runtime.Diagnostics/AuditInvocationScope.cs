// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Diagnostics;
using WinBridge.Runtime.Guards;

namespace WinBridge.Runtime.Diagnostics;

public sealed class AuditInvocationScope : IDisposable
{
    private readonly AuditLog _auditLog;
    private readonly Activity? _activity;
    private readonly AuditToolContext _toolContext;
    private bool _completed;

    internal AuditInvocationScope(AuditLog auditLog, Activity? activity, AuditToolContext toolContext)
    {
        _auditLog = auditLog;
        _activity = activity;
        _toolContext = toolContext;
    }

    public void SetDecision(ToolExecutionDecision decision)
    {
        _toolContext.SetDecision(decision);
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

        _auditLog.WriteToolCompleted(_toolContext, outcome, message, windowHwnd, data);
        _completed = true;
    }

    public void CompleteBestEffort(
        string outcome,
        string message,
        long? windowHwnd = null,
        IReadOnlyDictionary<string, string?>? data = null)
    {
        if (_completed)
        {
            return;
        }

        try
        {
            _auditLog.WriteToolCompleted(_toolContext, outcome, message, windowHwnd, data);
        }
        catch (Exception)
        {
        }

        _completed = true;
    }

    public string RunDirectory => _auditLog.RunDirectory;

    public bool TryRecordRuntimeEvent(
        string eventName,
        string severity,
        string messageHuman,
        string? toolName,
        string? outcome,
        long? windowHwnd,
        IReadOnlyDictionary<string, string?>? data = null) =>
        _auditLog.TryRecordRuntimeEvent(
            eventName,
            severity,
            messageHuman,
            toolName,
            outcome,
            windowHwnd,
            data);

    public void Fail(Exception exception, long? windowHwnd = null)
    {
        if (_completed)
        {
            return;
        }

        _auditLog.WriteToolCompleted(
            _toolContext,
            "failed",
            "Внутренняя ошибка при выполнении инструмента.",
            windowHwnd,
            data: null,
            exception: exception);

        _completed = true;
    }

    public void CompleteSanitizedFailure(
        string publicMessage,
        Exception exception,
        long? windowHwnd = null,
        IReadOnlyDictionary<string, string?>? data = null)
    {
        if (_completed)
        {
            return;
        }

        Dictionary<string, string?> payload = data is null
            ? []
            : new Dictionary<string, string?>(data, StringComparer.Ordinal);

        _auditLog.WriteToolCompleted(_toolContext, "failed", publicMessage, windowHwnd, payload, exception);
        _completed = true;
    }

    public void CompleteSanitizedFailureBestEffort(
        string publicMessage,
        Exception exception,
        long? windowHwnd = null,
        IReadOnlyDictionary<string, string?>? data = null)
    {
        if (_completed)
        {
            return;
        }

        try
        {
            Dictionary<string, string?> payload = data is null
                ? []
                : new Dictionary<string, string?>(data, StringComparer.Ordinal);

            _auditLog.WriteToolCompleted(_toolContext, "failed", publicMessage, windowHwnd, payload, exception);
        }
        catch (Exception)
        {
        }

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
