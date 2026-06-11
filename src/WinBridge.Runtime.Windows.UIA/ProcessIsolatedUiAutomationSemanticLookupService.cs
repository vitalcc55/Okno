// SPDX-FileCopyrightText: 2025-2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;

namespace WinBridge.Runtime.Windows.UIA;

public sealed class ProcessIsolatedUiAutomationSemanticLookupService : IUiAutomationSemanticLookupService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IUiAutomationWorkerProcessRunner _workerRunner;

    public ProcessIsolatedUiAutomationSemanticLookupService(TimeProvider timeProvider, AuditLogOptions auditLogOptions)
        : this(new UiAutomationWorkerProcessRunner(timeProvider, auditLogOptions))
    {
    }

    internal ProcessIsolatedUiAutomationSemanticLookupService(
        TimeProvider timeProvider,
        UiAutomationExecutionOptions executionOptions,
        string workerExecutablePath,
        string? workerArguments,
        AuditLogOptions? diagnosticAuditLogOptions = null)
        : this(
            new UiAutomationWorkerProcessRunner(
                timeProvider,
                executionOptions,
                workerExecutablePath,
                workerArguments,
                diagnosticAuditLogOptions))
    {
    }

    internal ProcessIsolatedUiAutomationSemanticLookupService(
        TimeProvider timeProvider,
        UiAutomationExecutionOptions executionOptions,
        Func<UiaWorkerLaunchSpec> workerLaunchSpecResolver,
        AuditLogOptions? diagnosticAuditLogOptions = null)
        : this(
            new UiAutomationWorkerProcessRunner(
                timeProvider,
                executionOptions,
                workerLaunchSpecResolver,
                diagnosticAuditLogOptions))
    {
    }

    internal ProcessIsolatedUiAutomationSemanticLookupService(IUiAutomationWorkerProcessRunner workerRunner)
    {
        _workerRunner = workerRunner;
    }

    public async Task<UiaSemanticLookupResult> LookupAsync(
        WindowDescriptor targetWindow,
        UiaSemanticLookupRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(targetWindow);
        ArgumentNullException.ThrowIfNull(request);

        if (!UiaSemanticLookupRequestValidator.TryValidate(request, out string? validationReason))
        {
            return Failed(
                UiaSemanticLookupFailureKindValues.InvalidRequest,
                validationReason!,
                request);
        }

        UiAutomationWorkerProcessResult execution = await _workerRunner
            .ExecuteAsync(
                new UiAutomationWorkerInvocation(
                    UiAutomationWorkerOperationValues.SemanticLookup,
                    targetWindow,
                    SnapshotRequest: null,
                    WaitProbeRequest: null,
                    SemanticLookupRequest: request),
                targetWindow.Hwnd,
                TimeSpan.FromMilliseconds(request.TimeoutMs),
                cancellationToken)
            .ConfigureAwait(false);

        if (!execution.Success)
        {
            return MaterializeWorkerFailure(execution, request);
        }

        try
        {
            UiaSemanticLookupResult? result = JsonSerializer.Deserialize<UiaSemanticLookupResult>(
                execution.Stdout ?? string.Empty,
                JsonOptions);
            return result ?? Failed(
                UiaSemanticLookupFailureKindValues.ProviderFailure,
                "UIA worker process вернул пустой semantic lookup payload.",
                request);
        }
        catch (JsonException)
        {
            return Failed(
                UiaSemanticLookupFailureKindValues.ProviderFailure,
                "UIA worker process вернул некорректный semantic lookup payload.",
                request);
        }
    }

    private static UiaSemanticLookupResult MaterializeWorkerFailure(
        UiAutomationWorkerProcessResult execution,
        UiaSemanticLookupRequest request)
    {
        string reason = execution.Reason ?? "UIA worker process не смог выполнить semantic lookup.";
        if (string.Equals(execution.FailureStage, UiaSnapshotFailureStageValues.Timeout, StringComparison.Ordinal))
        {
            return new UiaSemanticLookupResult(
                Status: UiaSemanticLookupStatusValues.Timeout,
                MaxDepth: request.MaxDepth,
                MaxNodes: request.MaxNodes,
                Reason: reason);
        }

        return Failed(
            UiaSemanticLookupFailureKindValues.ProviderFailure,
            reason,
            request);
    }

    private static UiaSemanticLookupResult Failed(
        string failureKind,
        string reason,
        UiaSemanticLookupRequest request) =>
        new(
            Status: UiaSemanticLookupStatusValues.Failed,
            MaxDepth: request.MaxDepth,
            MaxNodes: request.MaxNodes,
            FailureKind: failureKind,
            Reason: reason);
}
