namespace WinBridge.Runtime.Contracts;

public sealed record CaptureResult(
    CaptureMetadata Metadata,
    byte[] PngBytes);
