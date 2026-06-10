// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Windows.Automation;

namespace WinBridge.Runtime.Windows.UIA;

internal sealed class AutomationSemanticLookupNode(AutomationElement element, CacheRequest cacheRequest) : IUiaSemanticLookupNode
{
    public UiaSnapshotNodeData GetData() =>
        new AutomationSnapshotNode(element, cacheRequest).GetData();

    public IReadOnlyList<IUiaSemanticLookupNode> GetChildren()
    {
        List<IUiaSemanticLookupNode> children = [];
        for (AutomationElement? child = TreeWalker.RawViewWalker.GetFirstChild(element, cacheRequest);
            child is not null;
            child = TreeWalker.RawViewWalker.GetNextSibling(child, cacheRequest))
        {
            children.Add(new AutomationSemanticLookupNode(child, cacheRequest));
        }

        return children;
    }
}
