// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Display;

namespace WinBridge.Runtime.Windows.Capture;

internal sealed record CaptureResolvedTarget(
    CaptureScope Scope,
    string TargetKind,
    WindowDescriptor? Window,
    Bounds Bounds,
    string CoordinateSpace,
    int? EffectiveDpi,
    double? DpiScale,
    MonitorInfo? Monitor,
    Bounds? FrameBounds = null,
    CaptureReferenceEligibility CaptureReferenceEligibility = CaptureReferenceEligibility.ObserveOnly);

internal enum CaptureReferenceEligibility
{
    ObserveOnly,
    Eligible,
}
