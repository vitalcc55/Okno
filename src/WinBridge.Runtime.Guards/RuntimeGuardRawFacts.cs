// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Windows.Shell;

namespace WinBridge.Runtime.Guards;

internal sealed record RuntimeGuardRawFacts(
    DesktopSessionProbeResult DesktopSession,
    SessionAlignmentProbeResult SessionAlignment,
    TokenProbeResult Token)
{
    public InputAsyncStateReadabilityProbeResult InputAsyncStateReadability { get; init; } = new(
        InputAsyncStateReadabilityStatus.Readable);

    public CaptureCapabilityProbeResult Capture { get; init; } = new(
        FactResolved: false,
        WindowsGraphicsCaptureSupported: false);

    public UiaCapabilityProbeResult Uia { get; init; } = new(
        FactResolved: false,
        WorkerLaunchSpecResolved: false,
        FailureReason: null);
}

internal sealed record DesktopSessionProbeResult(
    bool InputDesktopAvailable,
    int? ErrorCode,
    bool DesktopNameResolved,
    string? DesktopName);

internal sealed record SessionAlignmentProbeResult(
    bool ProcessSessionResolved,
    uint? ProcessSessionId,
    uint ActiveConsoleSessionId,
    SessionConnectState? ConnectState,
    ushort? ClientProtocolType);

internal sealed record TokenProbeResult(
    bool IntegrityResolved,
    RuntimeIntegrityLevel? IntegrityLevel,
    int? IntegrityRid,
    bool ElevationResolved,
    bool IsElevated,
    TokenElevationTypeValue? ElevationType,
    bool UiAccessResolved,
    bool UiAccess);

internal enum RuntimeIntegrityLevel
{
    Untrusted,
    Low,
    Medium,
    High,
    SystemOrAbove,
}

internal enum TokenElevationTypeValue
{
    Default = 1,
    Full = 2,
    Limited = 3,
}

internal enum SessionConnectState
{
    Active = 0,
    Connected = 1,
    ConnectQuery = 2,
    Shadow = 3,
    Disconnected = 4,
    Idle = 5,
    Listen = 6,
    Reset = 7,
    Down = 8,
    Init = 9,
}
