using WinBridge.Runtime.Windows.Capture;

namespace WinBridge.Runtime.Tests;

public sealed class CaptureArtifactNameBuilderTests
{
    [Fact]
    public void CreateIncludesNonceAndStableShape()
    {
        DateTime timestampUtc = new(2026, 3, 13, 7, 30, 0, DateTimeKind.Utc);

        string fileName = CaptureArtifactNameBuilder.Create(
            scope: "window",
            targetKind: "window",
            handle: "42",
            capturedAtUtc: timestampUtc,
            nonce: "abc123");

        Assert.Equal("window-window-42-20260313T073000000-abc123.png", fileName);
    }

    [Fact]
    public void CreateWithDifferentNonceProducesDifferentFileNames()
    {
        DateTime timestampUtc = new(2026, 3, 13, 7, 30, 0, DateTimeKind.Utc);

        string first = CaptureArtifactNameBuilder.Create(
            scope: "window",
            targetKind: "window",
            handle: "42",
            capturedAtUtc: timestampUtc,
            nonce: "first");
        string second = CaptureArtifactNameBuilder.Create(
            scope: "window",
            targetKind: "window",
            handle: "42",
            capturedAtUtc: timestampUtc,
            nonce: "second");

        Assert.NotEqual(first, second);
    }
}
