// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Runtime.Contracts;

public sealed record WindowSelector(long? Hwnd, string? TitlePattern, string? ProcessName)
{
    public void Validate()
    {
        if (Hwnd is null
            && string.IsNullOrWhiteSpace(TitlePattern)
            && string.IsNullOrWhiteSpace(ProcessName))
        {
            throw new ArgumentException(
                "Нужно указать хотя бы один селектор: hwnd, titlePattern или processName.");
        }
    }

    public string MatchStrategy =>
        Hwnd is not null
            ? "hwnd"
            : !string.IsNullOrWhiteSpace(ProcessName)
                ? "process_name"
                : "title_pattern";
}
