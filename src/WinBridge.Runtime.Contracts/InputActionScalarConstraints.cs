// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Runtime.Contracts;

public static class InputActionScalarConstraints
{
    public const int MinimumRepeat = 1;
    public const int MaximumKeypressRepeat = 10;
    public const int MaximumScrollPages = 10;
    public const int InvalidScrollDelta = 0;
    public const int MinimumCapturePixelDimension = 1;
    public const string NonWhitespacePattern = @"\S";

    public static bool HasNonWhitespace(string? value) =>
        !string.IsNullOrWhiteSpace(value);
}
