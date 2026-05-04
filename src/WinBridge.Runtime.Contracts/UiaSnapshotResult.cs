// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Runtime.Contracts;

public sealed record UiaSnapshotResult(
    string Status,
    string? Reason = null,
    ObservedWindowDescriptor? Window = null,
    string View = UiaSnapshotDefaults.View,
    int RequestedDepth = UiaSnapshotDefaults.Depth,
    int RequestedMaxNodes = UiaSnapshotDefaults.MaxNodes,
    int RealizedDepth = 0,
    int NodeCount = 0,
    bool Truncated = false,
    bool DepthBoundaryReached = false,
    bool NodeBudgetBoundaryReached = false,
    string? AcquisitionMode = null,
    string? ArtifactPath = null,
    DateTimeOffset CapturedAtUtc = default,
    UiaElementSnapshot? Root = null,
    SessionSnapshot? Session = null);
