// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Runtime.Diagnostics;

public interface IAuditPayloadRedactor
{
    AuditRedactionResult Redact(AuditPayloadRedactionContext context, object? payload);
}
