// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Capture;
using WinBridge.Runtime.Windows.UIA;

namespace WinBridge.Server.ComputerUse;

internal sealed class ComputerUseWinAppStateObserver(
    ICaptureService captureService,
    IUiAutomationService uiAutomationService,
    IComputerUseWinInstructionProvider instructionProvider)
{
    public async Task<ComputerUseWinAppStateObservationOutcome> ObserveAsync(
        WindowDescriptor selectedWindow,
        string appId,
        string? windowId,
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
                    ComputerUseWinFailureDetails.Expected(
                        ComputerUseWinFailureCodeValues.ObservationFailed,
                        snapshot.Reason ?? "UIA snapshot did not complete successfully."));
            }

            IReadOnlyDictionary<int, ComputerUseWinStoredElement> elements = ComputerUseWinAccessibilityProjector.Flatten(snapshot.Root);
            ComputerUseWinAppSession session = new(
                AppId: appId,
                WindowId: windowId,
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
            List<string> effectiveWarnings = [.. warnings];
            IReadOnlyList<string> instructions = [];

            try
            {
                instructions = instructionProvider.GetInstructions(selectedWindow.ProcessName);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (ComputerUseWinInstructionUnavailableException)
            {
                effectiveWarnings.Add("Computer Use for Windows не смог загрузить advisory instructions для этого приложения; observation result сохранён без playbook hints.");
            }

            ComputerUseWinPreparedAppState preparedState = new(
                Session: session,
                StoredState: storedState,
                Capture: capture.Metadata,
                AccessibilityTree: ComputerUseWinAccessibilityProjector.CreatePublicTree(elements),
                Instructions: instructions,
                Warnings: effectiveWarnings,
                PngBytes: capture.PngBytes,
                MimeType: capture.Metadata.MimeType);

            return ComputerUseWinAppStateObservationOutcome.Success(preparedState);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            ComputerUseWinFailureDetails failure = ComputerUseWinObservationFailureTranslator.Translate(
                exception,
                "Computer Use for Windows не смог завершить observation stage для get_app_state.");
            return ComputerUseWinAppStateObservationOutcome.Failure(failure);
        }
    }
}

internal sealed record ComputerUseWinAppStateObservationOutcome(
    bool IsSuccess,
    ComputerUseWinPreparedAppState? PreparedState,
    ComputerUseWinFailureDetails? FailureDetails)
{
    public string? FailureCode => FailureDetails?.FailureCode;

    public string? Reason => FailureDetails?.Reason;

    public static ComputerUseWinAppStateObservationOutcome Success(ComputerUseWinPreparedAppState preparedState) =>
        new(true, preparedState, null);

    public static ComputerUseWinAppStateObservationOutcome Failure(ComputerUseWinFailureDetails failure) =>
        new(false, null, failure);
}

internal sealed record ComputerUseWinPreparedAppState(
    ComputerUseWinAppSession Session,
    ComputerUseWinStoredState StoredState,
    CaptureMetadata Capture,
    IReadOnlyList<ComputerUseWinAccessibilityElement> AccessibilityTree,
    IReadOnlyList<string> Instructions,
    IReadOnlyList<string> Warnings,
    byte[] PngBytes,
    string MimeType)
{
    public ComputerUseWinGetAppStateResult CreatePayload(string stateToken) =>
        new(
            Status: ComputerUseWinStatusValues.Ok,
            Session: Session,
            StateToken: stateToken,
            Capture: Capture,
            AccessibilityTree: AccessibilityTree,
            Instructions: Instructions,
            Warnings: Warnings);
}
