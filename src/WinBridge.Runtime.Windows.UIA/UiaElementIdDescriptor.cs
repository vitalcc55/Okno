// SPDX-FileCopyrightText: 2025-2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Runtime.Windows.UIA;

internal enum UiaElementIdPathKind
{
    Control,
    Raw,
}

internal readonly record struct UiaElementIdDescriptor(
    UiaElementIdPathKind PathKind,
    int[] Ordinals,
    int[]? ExpectedRuntimeId)
{
    public bool MatchesExpectedRuntimeId(int[]? actualRuntimeId)
    {
        if (ExpectedRuntimeId is not { Length: > 0 })
        {
            return true;
        }

        if (actualRuntimeId is null || actualRuntimeId.Length != ExpectedRuntimeId.Length)
        {
            return false;
        }

        for (int index = 0; index < ExpectedRuntimeId.Length; index++)
        {
            if (actualRuntimeId[index] != ExpectedRuntimeId[index])
            {
                return false;
            }
        }

        return true;
    }

    public static bool TryParse(string? elementId, out UiaElementIdDescriptor descriptor)
    {
        descriptor = default;
        if (string.IsNullOrWhiteSpace(elementId))
        {
            return false;
        }

        string remaining = elementId.Trim();
        int[]? expectedRuntimeId = null;
        if (remaining.StartsWith("rid:", StringComparison.Ordinal))
        {
            int separatorIndex = remaining.IndexOf(';', StringComparison.Ordinal);
            if (separatorIndex < 0
                || !TryParseRuntimeId(remaining["rid:".Length..separatorIndex], out expectedRuntimeId))
            {
                return false;
            }

            remaining = remaining[(separatorIndex + 1)..];
        }

        UiaElementIdPathKind pathKind;
        string rawPath;
        if (remaining.StartsWith("path:", StringComparison.Ordinal))
        {
            pathKind = UiaElementIdPathKind.Control;
            rawPath = remaining["path:".Length..];
        }
        else if (remaining.StartsWith("raw:", StringComparison.Ordinal))
        {
            pathKind = UiaElementIdPathKind.Raw;
            rawPath = remaining["raw:".Length..];
        }
        else
        {
            return false;
        }

        if (!TryParseOrdinalPath(rawPath, out int[]? ordinals) || ordinals is null)
        {
            return false;
        }

        descriptor = new UiaElementIdDescriptor(pathKind, ordinals, expectedRuntimeId);
        return true;
    }

    private static bool TryParseRuntimeId(string rawRuntimeId, out int[]? runtimeId)
    {
        runtimeId = null;
        if (string.IsNullOrWhiteSpace(rawRuntimeId))
        {
            return false;
        }

        string[] segments = rawRuntimeId.Split('.', StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            return false;
        }

        List<int> parsed = [];
        foreach (string segment in segments)
        {
            if (string.IsNullOrWhiteSpace(segment) || !int.TryParse(segment, out int value))
            {
                return false;
            }

            parsed.Add(value);
        }

        runtimeId = [.. parsed];
        return true;
    }

    private static bool TryParseOrdinalPath(string rawPath, out int[]? ordinals)
    {
        ordinals = null;
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
}
