namespace WinBridge.Runtime.Windows.Shell;

internal static class InputAsyncStateReadabilityEvaluator
{
    public static InputAsyncStateReadabilityProbeResult ProbeForForegroundOwner(
        int? foregroundOwnerProcessId,
        int currentProcessId,
        Func<InputAsyncStateReadabilityMode, InputAsyncStateReadabilityProbeResult> probe)
    {
        ArgumentNullException.ThrowIfNull(probe);
        return probe(InputAsyncStateReadabilityModeSelector.Select(foregroundOwnerProcessId, currentProcessId));
    }
}
