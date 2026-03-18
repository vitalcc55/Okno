using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Tooling;

public sealed record ToolTransportDescriptor(
    string Kind,
    string ProtocolVersion,
    string ServerEntry,
    string DeliveryStatus);

public sealed record FutureTransportDescriptor(
    string Kind,
    string Status,
    string Policy);

public sealed record ToolContractToolSection(
    IReadOnlyList<ContractToolDescriptor> Implemented,
    IReadOnlyList<ContractToolDescriptor> Deferred,
    IReadOnlyList<string> ImplementedNames,
    IReadOnlyList<string> SmokeRequiredNames,
    IReadOnlyDictionary<string, string> DeferredPhaseMap);

public sealed record ToolContractExportDocument(
    ToolTransportDescriptor Transport,
    IReadOnlyList<FutureTransportDescriptor> FutureTransports,
    ToolContractToolSection Tools,
    IReadOnlyList<string> Scripts,
    IReadOnlyList<string> Artifacts);
