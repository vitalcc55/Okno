// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Runtime.Windows.Capture;

internal static class CaptureArtifactNameBuilder
{
    public static string Create(
        string scope,
        string targetKind,
        string handle,
        DateTime capturedAtUtc,
        string nonce) =>
        $"{scope}-{targetKind}-{handle}-{capturedAtUtc:yyyyMMddTHHmmssfff}-{nonce}.png";

    public static string Create(
        string scope,
        string targetKind,
        string handle,
        DateTime capturedAtUtc) =>
        Create(scope, targetKind, handle, capturedAtUtc, Guid.NewGuid().ToString("N"));
}
