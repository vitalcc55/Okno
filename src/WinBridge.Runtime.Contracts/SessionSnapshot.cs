// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Runtime.Contracts;

public sealed record SessionSnapshot(
    string Mode,
    AttachedWindow? AttachedWindow,
    DateTimeOffset UpdatedAtUtc,
    string RunId)
{
    public static SessionSnapshot CreateInitial(string runId, DateTimeOffset updatedAtUtc) =>
        new("desktop", null, updatedAtUtc, runId);
}
