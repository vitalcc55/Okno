// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

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
