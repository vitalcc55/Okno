// SPDX-FileCopyrightText: 2025-2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;

namespace WinBridge.Runtime.Windows.UIA;

public sealed class ProcessIsolatedUiAutomationSetValueService : IUiAutomationSetValueService
{
    private readonly IUiAutomationWorkerProcessRunner _workerRunner;

    public ProcessIsolatedUiAutomationSetValueService(TimeProvider timeProvider, AuditLogOptions auditLogOptions)
        : this(new UiAutomationWorkerProcessRunner(timeProvider, auditLogOptions))
    {
    }

    internal ProcessIsolatedUiAutomationSetValueService(
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

    internal ProcessIsolatedUiAutomationSetValueService(IUiAutomationWorkerProcessRunner workerRunner)
    {
        _workerRunner = workerRunner;
    }

    public async Task<UiaSetValueResult> SetValueAsync(
        WindowDescriptor targetWindow,
        UiaSetValueRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(targetWindow);
        ArgumentNullException.ThrowIfNull(request);

        UiAutomationWorkerProcessResult execution = await _workerRunner
            .ExecuteAsync(
                new UiAutomationWorkerInvocation(
                    UiAutomationWorkerOperationValues.SetValue,
                    targetWindow,
                    SetValueRequest: request),
                targetWindow.Hwnd,
                timeout: null,
                cancellationToken)
            .ConfigureAwait(false);

        return execution.Success
            ? ProcessIsolatedUiAutomationActionResultMaterializer.Deserialize(
                execution.Stdout,
                static () => UiaSetValueResult.FailureResult(
                    UiaSetValueFailureKindValues.DispatchFailed,
                    "UIA worker process вернул пустой set_value payload."),
                static () => UiaSetValueResult.FailureResult(
                    UiaSetValueFailureKindValues.DispatchFailed,
                    "UIA worker process вернул некорректный set_value payload."))
            : UiaSetValueResult.FailureResult(
                UiaSetValueFailureKindValues.DispatchFailed,
                ProcessIsolatedUiAutomationActionResultMaterializer.ResolveWorkerFailureReason(
                    execution,
                    "UIA worker process не смог выполнить semantic set_value dispatch."));
    }
}

public sealed class ProcessIsolatedUiAutomationScrollService : IUiAutomationScrollService
{
    private readonly IUiAutomationWorkerProcessRunner _workerRunner;

    public ProcessIsolatedUiAutomationScrollService(TimeProvider timeProvider, AuditLogOptions auditLogOptions)
        : this(new UiAutomationWorkerProcessRunner(timeProvider, auditLogOptions))
    {
    }

    internal ProcessIsolatedUiAutomationScrollService(
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

    internal ProcessIsolatedUiAutomationScrollService(IUiAutomationWorkerProcessRunner workerRunner)
    {
        _workerRunner = workerRunner;
    }

    public async Task<UiaScrollResult> ScrollAsync(
        WindowDescriptor targetWindow,
        UiaScrollRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(targetWindow);
        ArgumentNullException.ThrowIfNull(request);

        UiAutomationWorkerProcessResult execution = await _workerRunner
            .ExecuteAsync(
                new UiAutomationWorkerInvocation(
                    UiAutomationWorkerOperationValues.Scroll,
                    targetWindow,
                    ScrollRequest: request),
                targetWindow.Hwnd,
                timeout: null,
                cancellationToken)
            .ConfigureAwait(false);

        return execution.Success
            ? ProcessIsolatedUiAutomationActionResultMaterializer.Deserialize(
                execution.Stdout,
                static () => UiaScrollResult.FailureResult(
                    UiaScrollFailureKindValues.DispatchFailed,
                    "UIA worker process вернул пустой scroll payload."),
                static () => UiaScrollResult.FailureResult(
                    UiaScrollFailureKindValues.DispatchFailed,
                    "UIA worker process вернул некорректный scroll payload."))
            : UiaScrollResult.FailureResult(
                UiaScrollFailureKindValues.DispatchFailed,
                ProcessIsolatedUiAutomationActionResultMaterializer.ResolveWorkerFailureReason(
                    execution,
                    "UIA worker process не смог выполнить semantic scroll dispatch."));
    }
}

public sealed class ProcessIsolatedUiAutomationSecondaryActionService : IUiAutomationSecondaryActionService
{
    private readonly IUiAutomationWorkerProcessRunner _workerRunner;

    public ProcessIsolatedUiAutomationSecondaryActionService(TimeProvider timeProvider, AuditLogOptions auditLogOptions)
        : this(new UiAutomationWorkerProcessRunner(timeProvider, auditLogOptions))
    {
    }

    internal ProcessIsolatedUiAutomationSecondaryActionService(
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

    internal ProcessIsolatedUiAutomationSecondaryActionService(IUiAutomationWorkerProcessRunner workerRunner)
    {
        _workerRunner = workerRunner;
    }

    public async Task<UiaSecondaryActionResult> ExecuteAsync(
        WindowDescriptor targetWindow,
        UiaSecondaryActionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(targetWindow);
        ArgumentNullException.ThrowIfNull(request);

        UiAutomationWorkerProcessResult execution = await _workerRunner
            .ExecuteAsync(
                new UiAutomationWorkerInvocation(
                    UiAutomationWorkerOperationValues.SecondaryAction,
                    targetWindow,
                    SecondaryActionRequest: request),
                targetWindow.Hwnd,
                timeout: null,
                cancellationToken)
            .ConfigureAwait(false);

        return execution.Success
            ? ProcessIsolatedUiAutomationActionResultMaterializer.Deserialize(
                execution.Stdout,
                () => UiaSecondaryActionResult.FailureResult(
                    request.ActionKind,
                    UiaSecondaryActionFailureKindValues.DispatchFailed,
                    "UIA worker process вернул пустой secondary action payload."),
                () => UiaSecondaryActionResult.FailureResult(
                    request.ActionKind,
                    UiaSecondaryActionFailureKindValues.DispatchFailed,
                    "UIA worker process вернул некорректный secondary action payload."))
            : UiaSecondaryActionResult.FailureResult(
                request.ActionKind,
                UiaSecondaryActionFailureKindValues.DispatchFailed,
                ProcessIsolatedUiAutomationActionResultMaterializer.ResolveWorkerFailureReason(
                    execution,
                    "UIA worker process не смог выполнить secondary semantic dispatch."));
    }
}

internal static class ProcessIsolatedUiAutomationActionResultMaterializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static TResult Deserialize<TResult>(
        string? stdout,
        Func<TResult> emptyPayloadFactory,
        Func<TResult> invalidPayloadFactory)
    {
        try
        {
            TResult? result = JsonSerializer.Deserialize<TResult>(stdout ?? string.Empty, JsonOptions);
            return result ?? emptyPayloadFactory();
        }
        catch (JsonException)
        {
            return invalidPayloadFactory();
        }
    }

    public static string ResolveWorkerFailureReason(UiAutomationWorkerProcessResult execution, string fallbackReason) =>
        execution.Reason ?? fallbackReason;
}
