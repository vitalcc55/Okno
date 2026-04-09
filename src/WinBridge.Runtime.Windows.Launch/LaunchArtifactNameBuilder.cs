namespace WinBridge.Runtime.Windows.Launch;

internal static class LaunchArtifactNameBuilder
{
    public static string CreateOpenTarget(DateTime capturedAtUtc, string nonce) =>
        $"open-target-{capturedAtUtc:yyyyMMddTHHmmssfff}-{nonce}.json";

    public static string CreateOpenTarget(DateTime capturedAtUtc) =>
        CreateOpenTarget(capturedAtUtc, Guid.NewGuid().ToString("N"));

    public static string Create(DateTime capturedAtUtc, string nonce) =>
        $"launch-{capturedAtUtc:yyyyMMddTHHmmssfff}-{nonce}.json";

    public static string Create(DateTime capturedAtUtc) =>
        Create(capturedAtUtc, Guid.NewGuid().ToString("N"));
}
