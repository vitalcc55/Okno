// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Runtime.Contracts;

public sealed record WindowDescriptor(
    long Hwnd,
    string Title,
    string? ProcessName,
    int? ProcessId,
    int? ThreadId,
    string? ClassName,
    Bounds Bounds,
    bool IsForeground,
    bool IsVisible,
    int EffectiveDpi = 96,
    double DpiScale = 1.0,
    string WindowState = WindowStateValues.Unknown,
    string? MonitorId = null,
    string? MonitorFriendlyName = null);
