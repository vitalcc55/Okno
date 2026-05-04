// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Runtime.Contracts;

public sealed record CaptureMetadata(
    string Scope,
    string TargetKind,
    long? Hwnd,
    string? Title,
    string? ProcessName,
    Bounds Bounds,
    string CoordinateSpace,
    int PixelWidth,
    int PixelHeight,
    DateTimeOffset CapturedAtUtc,
    string ArtifactPath,
    string MimeType,
    int ByteSize,
    string SessionRunId,
    int? EffectiveDpi = null,
    double? DpiScale = null,
    string? MonitorId = null,
    string? MonitorFriendlyName = null,
    string? MonitorGdiDeviceName = null,
    Bounds? FrameBounds = null,
    InputCaptureReference? CaptureReference = null);
