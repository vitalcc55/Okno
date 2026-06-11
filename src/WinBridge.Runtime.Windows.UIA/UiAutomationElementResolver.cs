// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace WinBridge.Runtime.Windows.UIA;

internal static class UiAutomationElementResolver
{
    public static bool TryResolveElement(
        AutomationElement root,
        CacheRequest cacheRequest,
        string? elementId,
        out AutomationElement? element)
    {
        element = null;

        if (root is null || string.IsNullOrWhiteSpace(elementId))
        {
            return false;
        }

        try
        {
            if (!UiaElementIdDescriptor.TryParse(elementId, out UiaElementIdDescriptor descriptor))
            {
                return false;
            }

            TreeWalker walker = descriptor.PathKind == UiaElementIdPathKind.Raw
                ? TreeWalker.RawViewWalker
                : TreeWalker.ControlViewWalker;
            if (!TryFollowPath(root, cacheRequest, descriptor.Ordinals, walker, out element)
                || element is null)
            {
                return false;
            }

            if (!MatchesExpectedRuntimeId(element, cacheRequest, descriptor))
            {
                element = null;
                return false;
            }

            return true;
        }
        catch (ElementNotAvailableException)
        {
            element = null;
            return false;
        }
        catch (InvalidOperationException)
        {
            element = null;
            return false;
        }
        catch (COMException)
        {
            element = null;
            return false;
        }
    }

    private static bool TryFollowPath(
        AutomationElement root,
        CacheRequest cacheRequest,
        int[] ordinals,
        TreeWalker walker,
        out AutomationElement? element)
    {
        AutomationElement current = root;
        foreach (int ordinal in ordinals)
        {
            AutomationElement? child = walker.GetFirstChild(current, cacheRequest);
            int currentOrdinal = 0;
            while (child is not null && currentOrdinal < ordinal)
            {
                child = walker.GetNextSibling(child, cacheRequest);
                currentOrdinal++;
            }

            if (child is null)
            {
                element = null;
                return false;
            }

            current = child;
        }

        element = current;
        return true;
    }

    private static bool MatchesExpectedRuntimeId(
        AutomationElement element,
        CacheRequest cacheRequest,
        UiaElementIdDescriptor descriptor)
    {
        if (descriptor.ExpectedRuntimeId is not { Length: > 0 })
        {
            return true;
        }

        UiaSnapshotNodeData data = new AutomationSnapshotNode(element, cacheRequest).GetData();
        return descriptor.MatchesExpectedRuntimeId(data.RuntimeId);
    }
}
