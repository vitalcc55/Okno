namespace WinBridge.Runtime.Contracts;

public sealed record SessionMutation(
    SessionSnapshot Before,
    SessionSnapshot After,
    bool Changed);
