// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Guards;
using WinBridge.Runtime.Tooling;

namespace WinBridge.Runtime.Diagnostics;

internal sealed class AuditToolContext
{
    private static readonly Dictionary<string, ToolExecutionRedactionClass> InternalRedactionClassMap =
        new Dictionary<string, ToolExecutionRedactionClass>(StringComparer.Ordinal)
        {
            [ToolNames.WindowsWait] = ToolExecutionRedactionClass.TextPayload,
            [ToolNames.WindowsUiaSnapshot] = ToolExecutionRedactionClass.TargetMetadata,
            [ToolNames.ComputerUseWinListApps] = ToolExecutionRedactionClass.TargetMetadata,
            [ToolNames.ComputerUseWinGetAppState] = ToolExecutionRedactionClass.TargetMetadata,
            [ToolNames.ComputerUseWinClick] = ToolExecutionRedactionClass.TargetMetadata,
            [ToolNames.ComputerUseWinDrag] = ToolExecutionRedactionClass.TargetMetadata,
            [ToolNames.ComputerUseWinPressKey] = ToolExecutionRedactionClass.TargetMetadata,
            [ToolNames.ComputerUseWinPerformSecondaryAction] = ToolExecutionRedactionClass.TargetMetadata,
            [ToolNames.ComputerUseWinScroll] = ToolExecutionRedactionClass.TargetMetadata,
            [ToolNames.ComputerUseWinSetValue] = ToolExecutionRedactionClass.TargetMetadata,
            [ToolNames.ComputerUseWinTypeText] = ToolExecutionRedactionClass.TargetMetadata,
        };

    private AuditToolContext(
        string toolName,
        ToolExecutionPolicyDescriptor? executionPolicy,
        ToolExecutionRedactionClass redactionClass)
    {
        ToolName = toolName;
        ExecutionPolicy = executionPolicy;
        RedactionClass = redactionClass;
    }

    public string ToolName { get; }

    public ToolExecutionPolicyDescriptor? ExecutionPolicy { get; }

    public ToolExecutionRedactionClass RedactionClass { get; }

    public ToolExecutionDecision? Decision { get; private set; }

    public static AuditToolContext Resolve(string toolName, ToolExecutionPolicyDescriptor? executionPolicy = null)
    {
        ToolExecutionPolicyDescriptor? resolvedPolicy = executionPolicy ?? ToolContractManifest.ResolveExecutionPolicy(toolName);

        ToolExecutionRedactionClass redactionClass =
            resolvedPolicy?.RedactionClass
            ?? (InternalRedactionClassMap.TryGetValue(toolName, out ToolExecutionRedactionClass mappedClass)
                ? mappedClass
                : ToolExecutionRedactionClass.None);

        return new AuditToolContext(toolName, resolvedPolicy, redactionClass);
    }

    public void SetDecision(ToolExecutionDecision decision)
    {
        Decision = decision;
    }
}
