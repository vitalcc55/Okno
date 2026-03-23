namespace WinBridge.Runtime.Waiting;

public sealed record WaitOptions
{
    public WaitOptions(TimeSpan pollInterval)
    {
        if (pollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(pollInterval), "PollInterval для wait должен быть строго положительным.");
        }

        PollInterval = pollInterval;
    }

    public TimeSpan PollInterval { get; }

    public static WaitOptions Default { get; } = new(TimeSpan.FromMilliseconds(100));
}
