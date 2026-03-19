namespace WinBridge.Runtime.Windows.UIA;

internal static class UiaSnapshotFailureStageValues
{
    public const string RequestValidation = "request_validation";
    public const string WorkerProcess = "worker_process";
    public const string RootAcquisition = "root_acquisition";
    public const string Traversal = "traversal";
    public const string Timeout = "timeout";
    public const string ArtifactWrite = "artifact_write";
}
