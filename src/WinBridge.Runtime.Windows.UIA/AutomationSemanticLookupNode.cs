// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Windows.Automation;

namespace WinBridge.Runtime.Windows.UIA;

internal sealed class AutomationSemanticLookupNode(AutomationElement element, CacheRequest cacheRequest) : IUiaSemanticLookupNode
{
    public UiaSnapshotNodeData GetData() =>
        new AutomationSnapshotNode(element, cacheRequest).GetData();

    public IUiaSemanticLookupNode? GetFirstChild()
    {
        AutomationElement? child = TreeWalker.RawViewWalker.GetFirstChild(element, cacheRequest);
        return child is null
            ? null
            : new AutomationSemanticLookupNode(child, cacheRequest);
    }

    public IUiaSemanticLookupNode? GetNextSibling()
    {
        AutomationElement? sibling = TreeWalker.RawViewWalker.GetNextSibling(element, cacheRequest);
        return sibling is null
            ? null
            : new AutomationSemanticLookupNode(sibling, cacheRequest);
    }
}
