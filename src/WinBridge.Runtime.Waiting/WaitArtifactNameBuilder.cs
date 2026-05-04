// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Runtime.Waiting;

internal static class WaitArtifactNameBuilder
{
    public static string Create(
        string condition,
        string handle,
        DateTime capturedAtUtc,
        string nonce) =>
        $"wait-{condition}-{handle}-{capturedAtUtc:yyyyMMddTHHmmssfff}-{nonce}.json";

    public static string Create(
        string condition,
        string handle,
        DateTime capturedAtUtc) =>
        Create(condition, handle, capturedAtUtc, Guid.NewGuid().ToString("N"));
}
