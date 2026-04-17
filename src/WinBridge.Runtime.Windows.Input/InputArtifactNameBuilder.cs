namespace WinBridge.Runtime.Windows.Input;

internal static class InputArtifactNameBuilder
{
    public static string Create(DateTime capturedAtUtc, string nonce) =>
        $"input-{capturedAtUtc:yyyyMMddTHHmmssfff}-{nonce}.json";

    public static string Create(DateTime capturedAtUtc) =>
        Create(capturedAtUtc, Guid.NewGuid().ToString("N"));
}
