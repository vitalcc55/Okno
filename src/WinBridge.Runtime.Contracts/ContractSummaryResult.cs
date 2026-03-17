namespace WinBridge.Runtime.Contracts;

public sealed record ContractSummaryResult(
    IReadOnlyList<ContractToolDescriptor> ImplementedTools,
    IReadOnlyList<ContractToolDescriptor> DeferredTools,
    string Notes);
