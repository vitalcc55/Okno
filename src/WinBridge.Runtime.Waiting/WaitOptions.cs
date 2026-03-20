namespace WinBridge.Runtime.Waiting;

public sealed record WaitOptions(TimeSpan PollInterval)
{
    public static WaitOptions Default { get; } = new(TimeSpan.FromMilliseconds(100));
}
