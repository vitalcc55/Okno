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
                    MaxNodes = maxNodes <= 0 ? 128 : maxNodes,
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
            string stateToken = stateStore.Create(
                new ComputerUseWinStoredState(
                    session,
                    selectedWindow,
                    capture.Metadata.CaptureReference,
                    elements,
                    capture.Metadata.CapturedAtUtc));

            ComputerUseWinGetAppStateResult payload = new(
                Status: ComputerUseWinStatusValues.Ok,
                Session: session,
                StateToken: stateToken,
                Capture: capture.Metadata,
                AccessibilityTree: ComputerUseWinAccessibilityProjector.CreatePublicTree(elements),
                Instructions: playbookProvider.GetInstructions(selectedWindow.ProcessName),
                Warnings: warnings);

            return ComputerUseWinAppStateObservationOutcome.Success(payload, capture.PngBytes, capture.Metadata.MimeType);
        }
        catch (CaptureOperationException exception)
        {
            return ComputerUseWinAppStateObservationOutcome.Failure(
                ComputerUseWinFailureCodeValues.ObservationFailed,
                exception.Message);
        }
    }
}

internal sealed record ComputerUseWinAppStateObservationOutcome(
    bool IsSuccess,
    ComputerUseWinGetAppStateResult? Payload,
    byte[]? PngBytes,
    string? MimeType,
    string? FailureCode,
    string? Reason)
{
    public static ComputerUseWinAppStateObservationOutcome Success(
        ComputerUseWinGetAppStateResult payload,
        byte[] pngBytes,
        string mimeType) =>
        new(true, payload, pngBytes, mimeType, null, null);

    public static ComputerUseWinAppStateObservationOutcome Failure(string failureCode, string reason) =>
        new(false, null, null, null, failureCode, reason);
}
