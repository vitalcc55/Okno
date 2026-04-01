namespace WinBridge.Runtime.Contracts;

public sealed record LaunchProcessPreview(
    string ExecutableIdentity,
    string ResolutionMode,
    int ArgumentCount,
    bool WorkingDirectoryProvided,
    bool WaitForWindow,
    int? TimeoutMs);
