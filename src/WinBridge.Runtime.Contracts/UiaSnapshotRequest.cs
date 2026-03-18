namespace WinBridge.Runtime.Contracts;

public sealed record UiaSnapshotRequest
{
    public int Depth { get; init; } = UiaSnapshotDefaults.Depth;

    public int MaxNodes { get; init; } = UiaSnapshotDefaults.MaxNodes;
}
