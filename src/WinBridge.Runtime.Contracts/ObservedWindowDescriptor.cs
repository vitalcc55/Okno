// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Runtime.Contracts;

public sealed record ObservedWindowDescriptor(
    long Hwnd,
    string? Title = null,
    string? ProcessName = null,
    int? ProcessId = null,
    int? ThreadId = null,
    string? ClassName = null,
    Bounds? Bounds = null,
    bool? IsForeground = null,
    bool? IsVisible = null,
    int? EffectiveDpi = null,
    double? DpiScale = null,
    string? WindowState = null,
    string? MonitorId = null,
    string? MonitorFriendlyName = null);
