namespace WinBridge.Runtime.Windows.Shell;

public sealed record WindowActivationOptions(
    TimeSpan RestoreTimeout,
    TimeSpan FocusTimeout,
    TimeSpan PollInterval)
{
    public static WindowActivationOptions Default { get; } = new(
        RestoreTimeout: TimeSpan.FromSeconds(1),
        FocusTimeout: TimeSpan.FromSeconds(1),
        PollInterval: TimeSpan.FromMilliseconds(50));
}
