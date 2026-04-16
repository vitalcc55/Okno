namespace WinBridge.Runtime.Windows.Shell;

internal static class InputAsyncStateReadinessBaselineEvaluator
{
    public static InputAsyncStateReadabilityProbeResult Probe(
        Func<InputAsyncStateReadabilityMode, InputAsyncStateReadabilityProbeResult> probe)
    {
        ArgumentNullException.ThrowIfNull(probe);
        return probe(InputAsyncStateReadabilityMode.CrossProcessForeground);
    }
}
