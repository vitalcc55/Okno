namespace WinBridge.Runtime.Contracts;

public sealed record SessionSnapshot(
    string Mode,
    AttachedWindow? AttachedWindow,
    DateTimeOffset UpdatedAtUtc,
    string RunId)
{
    public static SessionSnapshot CreateInitial(string runId, DateTimeOffset updatedAtUtc) =>
        new("desktop", null, updatedAtUtc, runId);
}
