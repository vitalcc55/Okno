namespace WinBridge.Runtime.Windows.Launch;

internal interface IOpenTargetPlatform
{
    OpenTargetPlatformResult Open(OpenTargetPlatformRequest request);
}

internal readonly record struct OpenTargetPlatformRequest(
    string TargetKind,
    string Target);

internal readonly record struct OpenTargetPlatformResult(
    bool IsAccepted,
    string? FailureCode = null,
    string? FailureReason = null,
    int? HandlerProcessId = null);
