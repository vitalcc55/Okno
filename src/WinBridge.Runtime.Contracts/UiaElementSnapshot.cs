// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Runtime.Contracts;

public sealed record UiaElementSnapshot
{
    public string ElementId { get; init; } = string.Empty;

    public string? ParentElementId { get; init; }

    public int Depth { get; init; }

    public int Ordinal { get; init; }

    public string? Name { get; init; }

    public string? AutomationId { get; init; }

    public string? ClassName { get; init; }

    public string? FrameworkId { get; init; }

    public string ControlType { get; init; } = string.Empty;

    public int ControlTypeId { get; init; }

    public string? LocalizedControlType { get; init; }

    public bool IsControlElement { get; init; }

    public bool IsContentElement { get; init; }

    public bool IsEnabled { get; init; }

    public bool IsOffscreen { get; init; }

    public bool HasKeyboardFocus { get; init; }

    public IReadOnlyList<string> Patterns { get; init; } = [];

    public bool? IsReadOnly { get; init; }

    public string? Value { get; init; }

    public Bounds? BoundingRectangle { get; init; }

    public long? NativeWindowHandle { get; init; }

    public IReadOnlyList<UiaElementSnapshot> Children { get; init; } = [];
}
