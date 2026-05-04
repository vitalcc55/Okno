// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Runtime.Windows.Launch;

internal static class LaunchArtifactNameBuilder
{
    public static string CreateOpenTarget(DateTime capturedAtUtc, string nonce) =>
        $"open-target-{capturedAtUtc:yyyyMMddTHHmmssfff}-{nonce}.json";

    public static string CreateOpenTarget(DateTime capturedAtUtc) =>
        CreateOpenTarget(capturedAtUtc, Guid.NewGuid().ToString("N"));

    public static string Create(DateTime capturedAtUtc, string nonce) =>
        $"launch-{capturedAtUtc:yyyyMMddTHHmmssfff}-{nonce}.json";

    public static string Create(DateTime capturedAtUtc) =>
        Create(capturedAtUtc, Guid.NewGuid().ToString("N"));
}
