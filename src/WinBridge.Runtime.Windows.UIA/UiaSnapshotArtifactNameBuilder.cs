namespace WinBridge.Runtime.Windows.UIA;

internal static class UiaSnapshotArtifactNameBuilder
{
    public static string Create(
        string targetKind,
        string handle,
        DateTime capturedAtUtc,
        string nonce) =>
        $"uia-{targetKind}-{handle}-{capturedAtUtc:yyyyMMddTHHmmssfff}-{nonce}.json";

    public static string Create(
        string targetKind,
        string handle,
        DateTime capturedAtUtc) =>
        Create(targetKind, handle, capturedAtUtc, Guid.NewGuid().ToString("N"));
}
