// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Runtime.InteropServices;
using System.Windows.Automation;
using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.UIA;

public sealed class Win32UiAutomationSemanticLookupService(TimeProvider timeProvider) : IUiAutomationSemanticLookupService
{
    public Task<UiaSemanticLookupResult> LookupAsync(
        WindowDescriptor targetWindow,
        UiaSemanticLookupRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(targetWindow);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!UiaSemanticLookupRequestValidator.TryValidate(request, out string? validationReason))
        {
            return Task.FromResult(new UiaSemanticLookupResult(
                Status: UiaSemanticLookupStatusValues.Failed,
                MaxDepth: request.MaxDepth,
                MaxNodes: request.MaxNodes,
                FailureKind: UiaSemanticLookupFailureKindValues.InvalidRequest,
                Reason: validationReason));
        }

        try
        {
            CacheRequest cacheRequest = AutomationSnapshotNode.CreateRawViewCacheRequest();
            using (cacheRequest.Activate())
            {
                AutomationElement root = AutomationElement.FromHandle(new IntPtr(targetWindow.Hwnd));
                AutomationSemanticLookupNode rootNode = new(root, cacheRequest);
                ObservedWindowDescriptor observedWindow = ObservedWindowBuilder.Create(targetWindow, root, rootNode.GetData());
                UiaSemanticLookupResult result = new UiaSemanticLookupEngine(timeProvider)
                    .Search(rootNode, observedWindow, request, cancellationToken);
                return Task.FromResult(result);
            }
        }
        catch (ElementNotAvailableException)
        {
            return Task.FromResult(ProviderFailure(request, UiaSemanticLookupFailureKindValues.RootUnavailable));
        }
        catch (ArgumentException)
        {
            return Task.FromResult(ProviderFailure(request, UiaSemanticLookupFailureKindValues.RootUnavailable));
        }
        catch (InvalidOperationException)
        {
            return Task.FromResult(ProviderFailure(request, UiaSemanticLookupFailureKindValues.ProviderFailure));
        }
        catch (COMException)
        {
            return Task.FromResult(ProviderFailure(request, UiaSemanticLookupFailureKindValues.ProviderFailure));
        }
    }

    private static UiaSemanticLookupResult ProviderFailure(UiaSemanticLookupRequest request, string failureKind) =>
        new(
            Status: UiaSemanticLookupStatusValues.Failed,
            MaxDepth: request.MaxDepth,
            MaxNodes: request.MaxNodes,
            FailureKind: failureKind,
            Reason: "UI Automation не смогла выполнить bounded semantic lookup для выбранного окна.");
}
