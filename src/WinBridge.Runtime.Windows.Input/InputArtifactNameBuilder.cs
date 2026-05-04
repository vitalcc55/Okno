// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Runtime.Windows.Input;

internal static class InputArtifactNameBuilder
{
    public static string Create(DateTime capturedAtUtc, string nonce) =>
        $"input-{capturedAtUtc:yyyyMMddTHHmmssfff}-{nonce}.json";

    public static string Create(DateTime capturedAtUtc) =>
        Create(capturedAtUtc, Guid.NewGuid().ToString("N"));
}
