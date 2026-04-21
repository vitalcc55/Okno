using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Capture;
using WinBridge.Runtime.Windows.UIA;

namespace WinBridge.Server.ComputerUse;

internal sealed class ComputerUseWinAppStateObserver(
    ICaptureService captureService,
    IUiAutomationService uiAutomationService,
    ComputerUseWinStateStore stateStore,
    ComputerUseWinPlaybookProvider playbookProvider)
{
    public async Task<ComputerUseWinAppStateObservationOutcome> ObserveAsync(
        WindowDescriptor selectedWindow,
        string appId,
        int maxNodes,
        IReadOnlyList<string> warnings,
        CancellationToken cancellationToken)
    {
        int effectiveMaxNodes = maxNodes <= 0 ? 128 : maxNodes;

        try
        {
            CaptureResult capture = await captureService.CaptureAsync(
                new CaptureTarget(CaptureScope.Window, selectedWindow),
                cancellationToken).ConfigureAwait(false);

            UiaSnapshotResult snapshot = await uiAutomationService.SnapshotAsync(
                selectedWindow,
                new UiaSnapshotRequest
                {
                    Depth = UiaSnapshotDefaults.Depth,
                    MaxNodes = effectiveMaxNodes,
                },
                cancellationToken).ConfigureAwait(false);

            if (!string.Equals(snapshot.Status, UiaSnapshotStatusValues.Done, StringComparison.Ordinal)
                || snapshot.Root is null)
            {
                return ComputerUseWinAppStateObservationOutcome.Failure(
                    ComputerUseWinFailureCodeValues.ObservationFailed,
                    snapshot.Reason ?? "UIA snapshot did not complete successfully.");
            }

            IReadOnlyDictionary<int, ComputerUseWinStoredElement> elements = ComputerUseWinAccessibilityProjector.Flatten(snapshot.Root);
            ComputerUseWinAppSession session = new(
                AppId: appId,
                Hwnd: selectedWindow.Hwnd,
                Title: selectedWindow.Title,
                ProcessName: selectedWindow.ProcessName,
                ProcessId: selectedWindow.ProcessId);
            ComputerUseWinStoredState storedState = new(
                session,
                selectedWindow,
                capture.Metadata.CaptureReference,
                elements,
                new ComputerUseWinObservationEnvelope(
                    RequestedDepth: UiaSnapshotDefaults.Depth,
                    RequestedMaxNodes: effectiveMaxNodes),
                capture.Metadata.CapturedAtUtc);
            string stateToken = stateStore.Create(storedState);

            ComputerUseWinGetAppStateResult payload = new(
                Status: ComputerUseWinStatusValues.Ok,
                Session: session,
                StateToken: stateToken,
                Capture: capture.Metadata,
                AccessibilityTree: ComputerUseWinAccessibilityProjector.CreatePublicTree(elements),
                Instructions: playbookProvider.GetInstructions(selectedWindow.ProcessName),
                Warnings: warnings);

            return ComputerUseWinAppStateObservationOutcome.Success(payload, capture.PngBytes, capture.Metadata.MimeType, storedState);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            ComputerUseWinObservationFailure failure = ComputerUseWinObservationFailureTranslator.Translate(
                exception,
                "Computer Use for Windows не смог завершить observation stage для get_app_state.");
            return ComputerUseWinAppStateObservationOutcome.Failure(failure.FailureCode, failure.Reason);
        }
    }
}

internal sealed record ComputerUseWinAppStateObservationOutcome(
    bool IsSuccess,
    ComputerUseWinGetAppStateResult? Payload,
    byte[]? PngBytes,
    string? MimeType,
    string? FailureCode,
    string? Reason,
    ComputerUseWinStoredState? StoredState)
{
    public static ComputerUseWinAppStateObservationOutcome Success(
        ComputerUseWinGetAppStateResult payload,
        byte[] pngBytes,
        string mimeType,
        ComputerUseWinStoredState storedState) =>
        new(true, payload, pngBytes, mimeType, null, null, storedState);

    public static ComputerUseWinAppStateObservationOutcome Failure(string failureCode, string reason) =>
        new(false, null, null, null, failureCode, reason, null);
}
