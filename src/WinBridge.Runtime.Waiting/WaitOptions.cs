namespace WinBridge.Runtime.Waiting;

public sealed record WaitOptions
{
    public WaitOptions(TimeSpan pollInterval, TimeSpan? visualEvidenceBudget = null)
    {
        if (pollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(pollInterval), "PollInterval для wait должен быть строго положительным.");
        }

        TimeSpan effectiveVisualEvidenceBudget = visualEvidenceBudget ?? TimeSpan.FromMilliseconds(250);
        if (effectiveVisualEvidenceBudget <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(visualEvidenceBudget), "VisualEvidenceBudget для wait должен быть строго положительным.");
        }

        PollInterval = pollInterval;
        VisualEvidenceBudget = effectiveVisualEvidenceBudget;
    }

    public TimeSpan PollInterval { get; }
    public TimeSpan VisualEvidenceBudget { get; }

    public static WaitOptions Default { get; } = new(TimeSpan.FromMilliseconds(100));
}
