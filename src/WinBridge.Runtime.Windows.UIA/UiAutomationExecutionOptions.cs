namespace WinBridge.Runtime.Windows.UIA;

internal sealed record UiAutomationExecutionOptions(TimeSpan? Timeout)
{
    public static UiAutomationExecutionOptions Default { get; } = new(TimeSpan.FromSeconds(3));

    public static UiAutomationExecutionOptions Unbounded { get; } = new((TimeSpan?)null);
}
