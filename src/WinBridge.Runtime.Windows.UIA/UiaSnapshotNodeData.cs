// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.UIA;

internal sealed record UiaSnapshotNodeData(
    int[]? RuntimeId,
    string? Name,
    string? AutomationId,
    string? ClassName,
    string? FrameworkId,
    string ControlType,
    int ControlTypeId,
    string? LocalizedControlType,
    bool IsControlElement,
    bool IsContentElement,
    bool IsEnabled,
    bool IsOffscreen,
    bool HasKeyboardFocus,
    bool IsPassword,
    bool? IsReadOnly,
    string[] Patterns,
    Bounds? BoundingRectangle,
    long? NativeWindowHandle);
