namespace WinBridge.Runtime.Contracts;

public sealed record ContractSummaryResult(
    IReadOnlyList<string> ImplementedTools,
    IReadOnlyDictionary<string, string> DeferredTools,
    string Notes);
