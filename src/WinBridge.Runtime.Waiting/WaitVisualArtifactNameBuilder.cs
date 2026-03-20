namespace WinBridge.Runtime.Waiting;

internal static class WaitVisualArtifactNameBuilder
{
    public static string Create(
        string phase,
        string handle,
        DateTime capturedAtUtc,
        string nonce) =>
        $"wait-visual-{phase}-{handle}-{capturedAtUtc:yyyyMMddTHHmmssfff}-{nonce}.png";

    public static string Create(
        string phase,
        string handle,
        DateTime capturedAtUtc) =>
        Create(phase, handle, capturedAtUtc, Guid.NewGuid().ToString("N"));
}
