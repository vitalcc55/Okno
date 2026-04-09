using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Launch;

public sealed class OpenTargetService : IOpenTargetService
{
    private readonly IOpenTargetPlatform _platform;
    private readonly OpenTargetResultMaterializer _resultMaterializer;
    private readonly TimeProvider _timeProvider;
    private readonly IOpenTargetPathInspector _pathInspector;

    internal OpenTargetService(
        IOpenTargetPlatform platform,
        TimeProvider timeProvider,
        IOpenTargetPathInspector pathInspector,
        OpenTargetResultMaterializer resultMaterializer)
    {
        _platform = platform;
        _timeProvider = timeProvider;
        _pathInspector = pathInspector;
        _resultMaterializer = resultMaterializer;
    }

    public Task<OpenTargetResult> OpenAsync(OpenTargetRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!OpenTargetRequestValidator.TryValidate(request, out OpenTargetClassification classification, out string? failureCode, out string? reason))
        {
            return Task.FromResult(
                new OpenTargetResult(
                    Status: OpenTargetStatusValues.Failed,
                    Decision: OpenTargetStatusValues.Failed,
                    FailureCode: failureCode ?? OpenTargetFailureCodeValues.InvalidRequest,
                    Reason: reason ?? "Open target request не прошёл validation.",
                    ArtifactPath: null));
        }

        cancellationToken.ThrowIfCancellationRequested();

        OpenTargetResult liveResult;
        if (TryValidateResolvedPathKind(request, classification, out OpenTargetResult? kindMismatchResult))
        {
            liveResult = kindMismatchResult!;
        }
        else
        {
            OpenTargetPlatformResult platformResult = _platform.Open(
                new OpenTargetPlatformRequest(
                    TargetKind: classification.TargetKind,
                    Target: request.Target));

            if (platformResult.IsAccepted)
            {
                int? handlerProcessId = platformResult.HandlerProcessId is > 0
                    ? platformResult.HandlerProcessId
                    : null;

                liveResult = new OpenTargetResult(
                    Status: OpenTargetStatusValues.Done,
                    Decision: OpenTargetStatusValues.Done,
                    ResultMode: handlerProcessId is null
                        ? OpenTargetResultModeValues.TargetOpenRequested
                        : OpenTargetResultModeValues.HandlerProcessObserved,
                    TargetKind: classification.TargetKind,
                    TargetIdentity: classification.TargetIdentity,
                    UriScheme: classification.UriScheme,
                    AcceptedAtUtc: _timeProvider.GetUtcNow(),
                    HandlerProcessId: handlerProcessId,
                    ArtifactPath: null);
            }
            else
            {
                liveResult = new OpenTargetResult(
                    Status: OpenTargetStatusValues.Failed,
                    Decision: OpenTargetStatusValues.Failed,
                    FailureCode: platformResult.FailureCode ?? OpenTargetFailureCodeValues.ShellRejectedTarget,
                    Reason: platformResult.FailureReason ?? "Shell не принял open request для target.",
                    TargetKind: classification.TargetKind,
                    TargetIdentity: classification.TargetIdentity,
                    UriScheme: classification.UriScheme,
                    ArtifactPath: null);
            }
        }

        return Task.FromResult(_resultMaterializer.Materialize(liveResult));
    }

    private bool TryValidateResolvedPathKind(
        OpenTargetRequest request,
        OpenTargetClassification classification,
        out OpenTargetResult? result)
    {
        result = null;
        if (!string.Equals(classification.TargetKind, OpenTargetKindValues.Document, StringComparison.Ordinal)
            && !string.Equals(classification.TargetKind, OpenTargetKindValues.Folder, StringComparison.Ordinal))
        {
            return false;
        }

        OpenTargetResolvedPathKind resolvedKind = _pathInspector.Inspect(request.Target);
        if (string.Equals(classification.TargetKind, OpenTargetKindValues.Document, StringComparison.Ordinal)
            && resolvedKind == OpenTargetResolvedPathKind.ExistingDirectory)
        {
            result = new OpenTargetResult(
                Status: OpenTargetStatusValues.Failed,
                Decision: OpenTargetStatusValues.Failed,
                FailureCode: OpenTargetFailureCodeValues.UnsupportedTargetKind,
                Reason: "V1 open_target с targetKind=document не принимает target, который в live phase resolved как существующая directory.",
                TargetKind: classification.TargetKind,
                TargetIdentity: classification.TargetIdentity,
                ArtifactPath: null);
            return true;
        }

        if (string.Equals(classification.TargetKind, OpenTargetKindValues.Folder, StringComparison.Ordinal)
            && resolvedKind == OpenTargetResolvedPathKind.ExistingFile)
        {
            result = new OpenTargetResult(
                Status: OpenTargetStatusValues.Failed,
                Decision: OpenTargetStatusValues.Failed,
                FailureCode: OpenTargetFailureCodeValues.UnsupportedTargetKind,
                Reason: "V1 open_target с targetKind=folder не принимает target, который в live phase resolved как существующий file.",
                TargetKind: classification.TargetKind,
                TargetIdentity: classification.TargetIdentity,
                ArtifactPath: null);
            return true;
        }

        return false;
    }
}
