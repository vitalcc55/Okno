namespace WinBridge.Runtime.Contracts;

public sealed record CaptureMetadata(
    string Scope,
    string TargetKind,
    long? Hwnd,
    string? Title,
    string? ProcessName,
    Bounds Bounds,
    double DpiScale,
    int PixelWidth,
    int PixelHeight,
    DateTimeOffset CapturedAtUtc,
    string ArtifactPath,
    string MimeType,
    int ByteSize,
    string SessionRunId,
    string? MonitorId = null,
    string? MonitorFriendlyName = null,
    string? MonitorGdiDeviceName = null);
