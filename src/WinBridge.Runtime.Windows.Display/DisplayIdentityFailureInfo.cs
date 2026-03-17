namespace WinBridge.Runtime.Windows.Display;

internal sealed record DisplayIdentityFailureInfo(
    string FailedStage,
    int ErrorCode,
    string ErrorName,
    string MessageHuman);

internal static class DisplayIdentityFailureAggregator
{
    public static DisplayIdentityFailureInfo? SelectMoreSignificant(
        DisplayIdentityFailureInfo? current,
        DisplayIdentityFailureInfo? candidate)
    {
        if (candidate is null)
        {
            return current;
        }

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
