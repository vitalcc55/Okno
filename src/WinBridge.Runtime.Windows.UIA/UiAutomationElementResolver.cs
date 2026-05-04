// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

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
            if (TryParseRuntimeId(elementId, out int[]? runtimeId)
                && runtimeId is not null
                && TryFindByRuntimeId(root, cacheRequest, runtimeId, out element))
            {
                return true;
            }

            if (TryParsePath(elementId, out int[]? ordinals)
                && ordinals is not null
                && TryFollowPath(root, cacheRequest, ordinals, out element))
            {
                return true;
            }
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

        return false;
    }

    private static bool TryFindByRuntimeId(
        AutomationElement root,
        CacheRequest cacheRequest,
        int[] runtimeId,
        out AutomationElement? element)
    {
        if (RuntimeIdsEqual(new AutomationSnapshotNode(root, cacheRequest).GetData().RuntimeId, runtimeId))
        {
            element = root;
            return true;
        }

        for (AutomationElement? child = TreeWalker.ControlViewWalker.GetFirstChild(root, cacheRequest);
            child is not null;
            child = TreeWalker.ControlViewWalker.GetNextSibling(child, cacheRequest))
        {
            if (TryFindByRuntimeId(child, cacheRequest, runtimeId, out element))
            {
                return true;
            }
        }

        element = null;
        return false;
    }

    private static bool TryFollowPath(
        AutomationElement root,
        CacheRequest cacheRequest,
        int[] ordinals,
        out AutomationElement? element)
    {
        AutomationElement current = root;
        foreach (int ordinal in ordinals)
        {
            AutomationElement? child = TreeWalker.ControlViewWalker.GetFirstChild(current, cacheRequest);
            int currentOrdinal = 0;
            while (child is not null && currentOrdinal < ordinal)
            {
                child = TreeWalker.ControlViewWalker.GetNextSibling(child, cacheRequest);
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

    private static bool TryParseRuntimeId(string elementId, out int[]? runtimeId)
    {
        runtimeId = null;
        if (!elementId.StartsWith("rid:", StringComparison.Ordinal))
        {
            return false;
        }

        int terminatorIndex = elementId.IndexOf(';');
        string raw = terminatorIndex >= 0
            ? elementId["rid:".Length..terminatorIndex]
            : elementId["rid:".Length..];
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        string[] segments = raw.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            return false;
        }

        List<int> parsed = [];
        foreach (string segment in segments)
        {
            if (!int.TryParse(segment, out int value))
            {
                runtimeId = null;
                return false;
            }

            parsed.Add(value);
        }

        runtimeId = [.. parsed];
        return true;
    }

    private static bool TryParsePath(string elementId, out int[]? ordinals)
    {
        ordinals = null;
        int pathIndex = elementId.IndexOf("path:", StringComparison.Ordinal);
        if (pathIndex < 0)
        {
            return false;
        }

        string rawPath = elementId[(pathIndex + "path:".Length)..];
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return false;
        }

        if (string.Equals(rawPath, "0", StringComparison.Ordinal))
        {
            ordinals = [];
            return true;
        }

        string[] segments = rawPath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0 || !string.Equals(segments[0], "0", StringComparison.Ordinal))
        {
            return false;
        }

        List<int> parsed = [];
        foreach (string segment in segments.Skip(1))
        {
            if (!int.TryParse(segment, out int ordinal) || ordinal < 0)
            {
                ordinals = null;
                return false;
            }

            parsed.Add(ordinal);
        }

        ordinals = [.. parsed];
        return true;
    }

    private static bool RuntimeIdsEqual(int[]? left, int[]? right)
    {
        if (left is null || right is null || left.Length != right.Length)
        {
            return false;
        }

        for (int index = 0; index < left.Length; index++)
        {
            if (left[index] != right[index])
            {
                return false;
            }
        }

        return true;
    }
}
