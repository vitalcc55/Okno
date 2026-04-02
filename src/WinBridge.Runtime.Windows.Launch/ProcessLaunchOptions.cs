namespace WinBridge.Runtime.Windows.Launch;

public sealed record ProcessLaunchOptions(
    TimeSpan MainWindowPollInterval,
    TimeSpan InputIdleWaitSlice)
{
    public static ProcessLaunchOptions Default { get; } = new(
        MainWindowPollInterval: TimeSpan.FromMilliseconds(100),
        InputIdleWaitSlice: TimeSpan.FromMilliseconds(100));
}
