// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Runtime.Contracts;

public static class InputCoordinateSpaceValues
{
    public const string CapturePixels = "capture_pixels";
    public const string Screen = "screen";

    public static IReadOnlySet<string> All { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            CapturePixels,
            Screen,
        };
}
