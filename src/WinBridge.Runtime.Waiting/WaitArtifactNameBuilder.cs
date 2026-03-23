namespace WinBridge.Runtime.Waiting;

internal static class WaitArtifactNameBuilder
{
    public static string Create(
        string condition,
        string handle,
        DateTime capturedAtUtc,
        string nonce) =>
        $"wait-{condition}-{handle}-{capturedAtUtc:yyyyMMddTHHmmssfff}-{nonce}.json";

    public static string Create(
        string condition,
        string handle,
        DateTime capturedAtUtc) =>
        Create(condition, handle, capturedAtUtc, Guid.NewGuid().ToString("N"));
}
