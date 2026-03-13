namespace WinBridge.Runtime.Windows.Capture;

internal static class CaptureArtifactNameBuilder
{
    public static string Create(
        string scope,
        string targetKind,
        string handle,
        DateTime capturedAtUtc,
        string nonce) =>
        $"{scope}-{targetKind}-{handle}-{capturedAtUtc:yyyyMMddTHHmmssfff}-{nonce}.png";

    public static string Create(
        string scope,
        string targetKind,
        string handle,
        DateTime capturedAtUtc) =>
        Create(scope, targetKind, handle, capturedAtUtc, Guid.NewGuid().ToString("N"));
}
