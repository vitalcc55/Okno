// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Runtime.Contracts;

public sealed record InputActionResult(
    string Type,
    string Status,
    string? ResultMode = null,
    string? FailureCode = null,
    string? Reason = null,
    string? CoordinateSpace = null,
    InputPoint? RequestedPoint = null,
    InputPoint? ResolvedScreenPoint = null,
    string? Button = null,
    IReadOnlyList<string>? Keys = null);
