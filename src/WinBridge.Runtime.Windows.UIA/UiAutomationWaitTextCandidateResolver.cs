namespace WinBridge.Runtime.Windows.UIA;

internal static class UiAutomationWaitTextCandidateResolver
{
    public static WaitTextCandidateMatch? Match(
        string expectedText,
        string? valueText,
        string? textPatternText,
        string? name)
    {
        foreach (WaitTextCandidateMatch candidate in EnumerateDistinctCandidates(valueText, textPatternText, name))
        {
            if (candidate.Text.Contains(expectedText, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        return null;
    }

    internal static IReadOnlyList<WaitTextCandidateMatch> EnumerateDistinctCandidates(
        string? valueText,
        string? textPatternText,
        string? name)
    {
        List<WaitTextCandidateMatch> candidates = [];
        HashSet<string> seen = new(StringComparer.Ordinal);

        AddCandidate(candidates, seen, "value_pattern", valueText);
        AddCandidate(candidates, seen, "text_pattern", textPatternText);
        AddCandidate(candidates, seen, "name", name);

        return candidates;
    }

    private static void AddCandidate(
        List<WaitTextCandidateMatch> candidates,
        HashSet<string> seen,
        string source,
        string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (!seen.Add(text))
        {
            return;
        }

        candidates.Add(new(text, source));
    }
}

internal sealed record WaitTextCandidateMatch(string Text, string Source);
