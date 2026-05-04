// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Runtime.Windows.Launch;

internal interface IOpenTargetPlatform
{
    OpenTargetPlatformResult Open(OpenTargetPlatformRequest request, CancellationToken cancellationToken);
}

internal readonly record struct OpenTargetPlatformRequest(
    string TargetKind,
    string Target);

internal readonly record struct OpenTargetPlatformResult(
    bool IsAccepted,
    string? FailureCode = null,
    string? FailureReason = null,
    int? HandlerProcessId = null);
