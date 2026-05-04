// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Runtime.Tooling;

public enum ToolExecutionPolicyGroup
{
    Observe,
    SessionMutation,
    Launch,
    Input,
    Clipboard,
    UiaAction,
}

public enum ToolExecutionRiskLevel
{
    Low,
    Medium,
    High,
    Destructive,
}

public enum ToolExecutionConfirmationMode
{
    None,
    Required,
    Conditional,
}

public enum ToolExecutionRedactionClass
{
    None,
    TargetMetadata,
    TextPayload,
    ClipboardPayload,
    LaunchPayload,
    ArtifactReference,
}

public sealed record ToolExecutionPolicyDescriptor(
    ToolExecutionPolicyGroup PolicyGroup,
    ToolExecutionRiskLevel RiskLevel,
    string GuardCapability,
    bool SupportsDryRun,
    ToolExecutionConfirmationMode ConfirmationMode,
    ToolExecutionRedactionClass RedactionClass);
