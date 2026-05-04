// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Tooling;

namespace WinBridge.Runtime.Diagnostics;

public sealed record AuditPayloadRedactionContext(
    string ToolName,
    AuditPayloadKind PayloadKind,
    ToolExecutionRedactionClass RedactionClass,
    string? EventName = null);
