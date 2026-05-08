// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Setup.Core;

internal static class CodexConfigTomlSectionRewriter
{
    public static bool TryRemoveOwnedSections(
        string configText,
        IReadOnlyCollection<string[]> ownedSectionPaths,
        out string rewrittenText)
    {
        List<string> keptLines = [];
        bool removingOwnedSection = false;
        bool changed = false;

        foreach (string line in EnumerateLines(configText))
        {
            if (TryParseTableHeaderPath(line, out string[]? tablePath) && tablePath is not null)
            {
                removingOwnedSection = ownedSectionPaths.Any(candidate => TablePathsEqual(candidate, tablePath));
            }

            if (removingOwnedSection)
            {
                changed = true;
                continue;
            }

            keptLines.Add(line);
        }

        rewrittenText = changed ? string.Concat(keptLines) : configText;
        return changed;
    }

    private static IEnumerable<string> EnumerateLines(string text)
    {
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n')
            {
                continue;
            }

            yield return text[start..(i + 1)];
            start = i + 1;
        }

        if (start < text.Length)
        {
            yield return text[start..];
        }
    }

    private static bool TryParseTableHeaderPath(string line, out string[]? path)
    {
        path = null;
        string withoutComment = StripInlineComment(line).Trim();
        if (withoutComment.Length < 2 || withoutComment[0] != '[')
        {
            return false;
        }

        bool isArrayTable = withoutComment.StartsWith("[[", StringComparison.Ordinal);
        string opening = isArrayTable ? "[[" : "[";
        string closing = isArrayTable ? "]]" : "]";
        if (!withoutComment.StartsWith(opening, StringComparison.Ordinal)
            || !withoutComment.EndsWith(closing, StringComparison.Ordinal))
        {
            return false;
        }

        string inner = withoutComment[opening.Length..^closing.Length].Trim();
        if (string.IsNullOrWhiteSpace(inner))
        {
            return false;
        }

        path = ParseDottedKeyPath(inner);
        return path.Length > 0;
    }

    private static string StripInlineComment(string line)
    {
        bool inBasicString = false;
        bool inLiteralString = false;
        bool escaping = false;

        for (int i = 0; i < line.Length; i++)
        {
            char current = line[i];
            if (escaping)
            {
                escaping = false;
                continue;
            }

            if (inBasicString)
            {
                if (current == '\\')
                {
                    escaping = true;
                }
                else if (current == '"')
                {
                    inBasicString = false;
                }

                continue;
            }

            if (inLiteralString)
            {
                if (current == '\'')
                {
                    inLiteralString = false;
                }

                continue;
            }

            if (current == '#')
            {
                return line[..i];
            }

            if (current == '"')
            {
                inBasicString = true;
            }
            else if (current == '\'')
            {
                inLiteralString = true;
            }
        }

        return line;
    }

    private static string[] ParseDottedKeyPath(string inner)
    {
        List<string> segments = [];
        int index = 0;

        while (index < inner.Length)
        {
            while (index < inner.Length && char.IsWhiteSpace(inner[index]))
            {
                index++;
            }

            if (index >= inner.Length)
            {
                break;
            }

            string segment;
            if (inner[index] == '"' || inner[index] == '\'')
            {
                segment = ParseQuotedSegment(inner, ref index);
            }
            else
            {
                int start = index;
                while (index < inner.Length && inner[index] != '.')
                {
                    index++;
                }

                segment = inner[start..index].Trim();
            }

            if (string.IsNullOrEmpty(segment))
            {
                return [];
            }

            segments.Add(segment);

            while (index < inner.Length && char.IsWhiteSpace(inner[index]))
            {
                index++;
            }

            if (index >= inner.Length)
            {
                break;
            }

            if (inner[index] != '.')
            {
                return [];
            }

            index++;
        }

        return [.. segments];
    }

    private static string ParseQuotedSegment(string text, ref int index)
    {
        char quote = text[index++];
        bool escaping = false;
        System.Text.StringBuilder builder = new();

        while (index < text.Length)
        {
            char current = text[index++];
            if (quote == '"' && escaping)
            {
                builder.Append(current);
                escaping = false;
                continue;
            }

            if (quote == '"' && current == '\\')
            {
                escaping = true;
                continue;
            }

            if (current == quote)
            {
                return builder.ToString();
            }

            builder.Append(current);
        }

        return string.Empty;
    }

    private static bool TablePathsEqual(string[] left, string[] right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (int i = 0; i < left.Length; i++)
        {
            if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
