// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Runtime.InteropServices;
using System.Windows.Automation;
using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.UIA;

internal sealed class Win32UiAutomationBackend : IUiaSnapshotBackend
{
    private readonly TimeProvider _timeProvider;

    public Win32UiAutomationBackend(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public async Task<UiaSnapshotBackendResult> CaptureAsync(
        WindowDescriptor targetWindow,
        UiaSnapshotRequest request,
        CancellationToken cancellationToken)
    {
        return await UiAutomationMtaRunner.RunAsync(
            cancellationTokenInner => CaptureCore(targetWindow, request, cancellationTokenInner),
            cancellationToken).ConfigureAwait(false);
    }

    private UiaSnapshotBackendResult CaptureCore(
        WindowDescriptor targetWindow,
        UiaSnapshotRequest request,
        CancellationToken cancellationToken)
    {
        DateTimeOffset capturedAtUtc = _timeProvider.GetUtcNow();
        CacheRequest cacheRequest = AutomationSnapshotNode.CreateControlViewCacheRequest();

        AutomationElement root;
        try
        {
            using (cacheRequest.Activate())
            {
                root = AutomationElement.FromHandle(new IntPtr(targetWindow.Hwnd));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ElementNotAvailableException)
        {
            return Failed("UI Automation не смогла получить root element для выбранного hwnd.", UiaSnapshotFailureStageValues.RootAcquisition, capturedAtUtc);
        }
        catch (ArgumentException)
        {
            return Failed("UI Automation не смогла получить root element для выбранного hwnd.", UiaSnapshotFailureStageValues.RootAcquisition, capturedAtUtc);
        }
        catch (COMException)
        {
            return Failed("UI Automation не смогла получить root element для выбранного hwnd.", UiaSnapshotFailureStageValues.RootAcquisition, capturedAtUtc);
        }

        ObservedWindowDescriptor? observedWindow = null;
        try
        {
            AutomationSnapshotNode rootNode = new(root, cacheRequest);
            observedWindow = ObservedWindowBuilder.Create(targetWindow, root, rootNode.GetData());
            UiaSnapshotTreeBuilder.UiaSnapshotTreeBuildResult tree = UiaSnapshotTreeBuilder.Build(
                rootNode,
                request.Depth,
                request.MaxNodes,
                cancellationToken);

            return new(
                Success: true,
                Reason: null,
                FailureStage: null,
                CapturedAtUtc: capturedAtUtc,
                ObservedWindow: observedWindow,
                Root: tree.Root,
                RealizedDepth: tree.RealizedDepth,
                NodeCount: tree.NodeCount,
                Truncated: tree.Truncated,
                DepthBoundaryReached: tree.DepthBoundaryReached,
                NodeBudgetBoundaryReached: tree.NodeBudgetBoundaryReached);
        }
        catch (InvalidOperationException exception)
        {
            return Failed(exception.Message, UiaSnapshotFailureStageValues.Traversal, capturedAtUtc, observedWindow);
        }
        catch (ElementNotAvailableException)
        {
            return Failed("UI Automation не смогла материализовать snapshot tree для выбранного hwnd.", UiaSnapshotFailureStageValues.Traversal, capturedAtUtc, observedWindow);
        }
        catch (COMException)
        {
            return Failed("UI Automation не смогла материализовать snapshot tree для выбранного hwnd.", UiaSnapshotFailureStageValues.Traversal, capturedAtUtc, observedWindow);
        }
    }

    private static UiaSnapshotBackendResult Failed(
        string reason,
        string failureStage,
        DateTimeOffset capturedAtUtc,
        ObservedWindowDescriptor? observedWindow = null) =>
        new(
            Success: false,
            Reason: reason,
            FailureStage: failureStage,
            CapturedAtUtc: capturedAtUtc,
            ObservedWindow: observedWindow,
            Root: null,
            RealizedDepth: 0,
            NodeCount: 0,
            Truncated: false,
            DepthBoundaryReached: false,
            NodeBudgetBoundaryReached: false);
}
