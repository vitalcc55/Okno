using System.Globalization;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;

namespace WinBridge.Runtime.Windows.UIA;

public sealed class Win32UiAutomationService : IUiAutomationService
{
    private const string RuntimeCompletedEventName = "uia.snapshot.runtime.completed";
    private const string AcquisitionMode = "element_from_handle";
    private readonly IUiaSnapshotBackend _backend;
    private readonly UiaSnapshotArtifactWriter _artifactWriter;
    private readonly AuditLog _auditLog;
    private readonly TimeProvider _timeProvider;

    public Win32UiAutomationService(
        AuditLog auditLog,
        AuditLogOptions auditLogOptions,
        TimeProvider timeProvider)
        : this(
            new ProcessIsolatedUiAutomationBackend(timeProvider, auditLogOptions),
            new UiaSnapshotArtifactWriter(auditLogOptions),
            auditLog,
            timeProvider)
    {
    }

    internal Win32UiAutomationService(
        IUiaSnapshotBackend backend,
        UiaSnapshotArtifactWriter artifactWriter,
        AuditLog auditLog,
        TimeProvider timeProvider)
    {
        _backend = backend;
        _artifactWriter = artifactWriter;
        _auditLog = auditLog;
        _timeProvider = timeProvider;
    }

    public async Task<UiaSnapshotResult> SnapshotAsync(
        WindowDescriptor targetWindow,
        UiaSnapshotRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(targetWindow);
        ArgumentNullException.ThrowIfNull(request);

        if (request.Depth < 0)
        {
            UiaSnapshotResult invalidDepthResult = CreateFailedResult(
                "Параметр depth для UIA snapshot должен быть >= 0.",
                targetWindow,
                request,
                _timeProvider.GetUtcNow());
            RecordRuntimeEvent(invalidDepthResult, UiaSnapshotFailureStageValues.RequestValidation);
            return invalidDepthResult;
        }

        if (request.MaxNodes < 1)
        {
            UiaSnapshotResult invalidBudgetResult = CreateFailedResult(
                "Параметр maxNodes для UIA snapshot должен быть >= 1.",
                targetWindow,
                request,
                _timeProvider.GetUtcNow());
            RecordRuntimeEvent(invalidBudgetResult, UiaSnapshotFailureStageValues.RequestValidation);
            return invalidBudgetResult;
        }

        UiaSnapshotBackendResult backendResult = await _backend
            .CaptureAsync(targetWindow, request, cancellationToken)
            .ConfigureAwait(false);

        if (!backendResult.Success || backendResult.Root is null)
        {
            UiaSnapshotResult failedResult = CreateFailedResult(
                backendResult.Reason ?? "UI Automation не смогла построить snapshot выбранного окна.",
                targetWindow,
                request,
                backendResult.CapturedAtUtc);
            RecordRuntimeEvent(failedResult, backendResult.FailureStage, backendResult.DiagnosticArtifactPath);
            return failedResult;
        }

        UiaSnapshotResult successResult = new(
            Status: UiaSnapshotStatusValues.Done,
            Reason: null,
            Window: targetWindow,
            View: UiaSnapshotViewValues.Control,
            RequestedDepth: request.Depth,
            RequestedMaxNodes: request.MaxNodes,
            RealizedDepth: backendResult.RealizedDepth,
            NodeCount: backendResult.NodeCount,
            Truncated: backendResult.Truncated,
            DepthBoundaryReached: backendResult.DepthBoundaryReached,
            NodeBudgetBoundaryReached: backendResult.NodeBudgetBoundaryReached,
            AcquisitionMode: AcquisitionMode,
            ArtifactPath: null,
            CapturedAtUtc: backendResult.CapturedAtUtc,
            Root: backendResult.Root,
            Session: null);

        try
        {
            string artifactPath = _artifactWriter.Write(successResult);
            UiaSnapshotResult materializedResult = successResult with { ArtifactPath = artifactPath };
            RecordRuntimeEvent(materializedResult, failureStage: null);
            return materializedResult;
        }
        catch (UiaSnapshotArtifactException exception)
        {
            UiaSnapshotResult artifactFailureResult = CreateFailedResult(
                exception.Message,
                targetWindow,
                request,
                backendResult.CapturedAtUtc,
                realizedDepth: backendResult.RealizedDepth,
                nodeCount: backendResult.NodeCount,
                truncated: backendResult.Truncated,
                depthBoundaryReached: backendResult.DepthBoundaryReached,
                nodeBudgetBoundaryReached: backendResult.NodeBudgetBoundaryReached,
                root: backendResult.Root);
            RecordRuntimeEvent(artifactFailureResult, UiaSnapshotFailureStageValues.ArtifactWrite);
            return artifactFailureResult;
        }
    }

    private void RecordRuntimeEvent(UiaSnapshotResult result, string? failureStage, string? diagnosticArtifactPath = null)
    {
        string severity = result.Status == UiaSnapshotStatusValues.Done ? "info" : "warning";
        string message = result.Status == UiaSnapshotStatusValues.Done
            ? "Runtime UIA snapshot построен."
            : result.Reason ?? "Runtime UIA snapshot завершился с ошибкой.";

        _auditLog.RecordRuntimeEvent(
            eventName: RuntimeCompletedEventName,
            severity: severity,
            messageHuman: message,
            toolName: "windows.uia_snapshot",
            outcome: result.Status,
            windowHwnd: result.Window?.Hwnd,
            data: new Dictionary<string, string?>
            {
                ["view"] = result.View,
                ["requested_depth"] = result.RequestedDepth.ToString(CultureInfo.InvariantCulture),
                ["requested_max_nodes"] = result.RequestedMaxNodes.ToString(CultureInfo.InvariantCulture),
                ["realized_depth"] = result.RealizedDepth.ToString(CultureInfo.InvariantCulture),
                ["node_count"] = result.NodeCount.ToString(CultureInfo.InvariantCulture),
                ["truncated"] = result.Truncated.ToString(CultureInfo.InvariantCulture),
                ["depth_boundary_reached"] = result.DepthBoundaryReached.ToString(CultureInfo.InvariantCulture),
                ["node_budget_boundary_reached"] = result.NodeBudgetBoundaryReached.ToString(CultureInfo.InvariantCulture),
                ["acquisition_mode"] = result.AcquisitionMode,
                ["artifact_path"] = result.ArtifactPath,
                ["window_hwnd"] = result.Window?.Hwnd.ToString(CultureInfo.InvariantCulture),
                ["failure_stage"] = failureStage,
                ["diagnostic_artifact_path"] = diagnosticArtifactPath,
            });
    }

    private static UiaSnapshotResult CreateFailedResult(
        string reason,
        WindowDescriptor targetWindow,
        UiaSnapshotRequest request,
        DateTimeOffset capturedAtUtc,
        int realizedDepth = 0,
        int nodeCount = 0,
        bool truncated = false,
        bool depthBoundaryReached = false,
        bool nodeBudgetBoundaryReached = false,
        UiaElementSnapshot? root = null) =>
        new(
            Status: UiaSnapshotStatusValues.Failed,
            Reason: reason,
            Window: targetWindow,
            View: UiaSnapshotViewValues.Control,
            RequestedDepth: request.Depth,
            RequestedMaxNodes: request.MaxNodes,
            RealizedDepth: realizedDepth,
            NodeCount: nodeCount,
            Truncated: truncated,
            DepthBoundaryReached: depthBoundaryReached,
            NodeBudgetBoundaryReached: nodeBudgetBoundaryReached,
            AcquisitionMode: AcquisitionMode,
            ArtifactPath: null,
            CapturedAtUtc: capturedAtUtc,
            Root: root,
            Session: null);
}
