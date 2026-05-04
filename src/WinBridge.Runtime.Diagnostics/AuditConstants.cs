// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Diagnostics;

namespace WinBridge.Runtime.Diagnostics;

public static class AuditConstants
{
    public const string SchemaVersion = "1.0.0";

    public const string ServiceName = "Okno";

    public static readonly ActivitySource ActivitySource = new(ServiceName);
}
