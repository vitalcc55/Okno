namespace WinBridge.Runtime.Windows.Display;

internal sealed record DisplayConfigFailureInfo(
    string FailedStage,
    int ErrorCode,
    string ErrorName,
    string MessageHuman);

internal static class DisplayConfigFailureAggregator
{
    public static DisplayConfigFailureInfo? SelectMoreSignificant(
        DisplayConfigFailureInfo? current,
        DisplayConfigFailureInfo candidate)
    {
        if (current is null)
        {
            return candidate;
        }

        return DisplayIdentityFailureSemantics.GetPriority(candidate.FailedStage)
            > DisplayIdentityFailureSemantics.GetPriority(current.FailedStage)
            ? candidate
            : current;
    }
}
