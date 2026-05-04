// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Runtime.Windows.UIA;

internal static class UiaSnapshotArtifactNameBuilder
{
    public static string Create(
        string targetKind,
        string handle,
        DateTime capturedAtUtc,
        string nonce) =>
        $"uia-{targetKind}-{handle}-{capturedAtUtc:yyyyMMddTHHmmssfff}-{nonce}.json";

    public static string Create(
        string targetKind,
        string handle,
        DateTime capturedAtUtc) =>
        Create(targetKind, handle, capturedAtUtc, Guid.NewGuid().ToString("N"));
}
