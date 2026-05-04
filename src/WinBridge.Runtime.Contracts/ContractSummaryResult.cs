// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Runtime.Contracts;

public sealed record ContractSummaryResult(
    IReadOnlyList<ContractToolDescriptor> ImplementedTools,
    IReadOnlyList<ContractToolDescriptor> DeferredTools,
    string Notes);
