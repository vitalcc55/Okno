// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.ComponentModel;
using ModelContextProtocol.Server;
using WinBridge.Runtime;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Guards;
using WinBridge.Runtime.Session;
using WinBridge.Runtime.Tooling;

namespace WinBridge.Server.Tools;

[McpServerToolType]
public sealed class AdminTools
{
    private readonly AuditLog _auditLog;
    private readonly IRuntimeGuardService _runtimeGuardService;
    private readonly RuntimeInfo _runtimeInfo;
    private readonly ISessionManager _sessionManager;

    public AdminTools(
        AuditLog auditLog,
        RuntimeInfo runtimeInfo,
        ISessionManager sessionManager,
        IRuntimeGuardService runtimeGuardService)
    {
        _auditLog = auditLog;
        _runtimeGuardService = runtimeGuardService;
        _runtimeInfo = runtimeInfo;
        _sessionManager = sessionManager;
    }

    [Description(ToolDescriptions.OknoHealthTool)]
    [McpServerTool(Name = ToolNames.OknoHealth)]
    public HealthResult Health()
        => ToolExecution.Run(
            _auditLog,
            _sessionManager.GetSnapshot(),
            ToolNames.OknoHealth,
            new { probe = "health" },
            invocation =>
            {
                RuntimeGuardAssessment assessment = _runtimeGuardService.GetSnapshot();

                HealthResult result = new(
                    Service: _runtimeInfo.ServiceName,
                    Version: _runtimeInfo.Version,
                    Transport: _runtimeInfo.Transport,
                    AuditSchemaVersion: _runtimeInfo.AuditSchemaVersion,
                    RunId: _runtimeInfo.RunId,
                    ArtifactsDirectory: _runtimeInfo.ArtifactsDirectory,
                    ActiveMonitorCount: assessment.Topology.Monitors.Count,
                    DisplayIdentity: assessment.Topology.Diagnostics,
                    ImplementedTools: ToolContractManifest.ImplementedNames,
                    DeferredTools: ToolContractManifest.DeferredPhaseMap,
                    Readiness: assessment.Readiness,
                    BlockedCapabilities: assessment.BlockedCapabilities,
                    Warnings: assessment.Warnings);

                invocation.Complete("done", "Возвращена сводка состояния runtime и консервативный readiness snapshot.");
                return result;
            });

    [Description(ToolDescriptions.OknoContractTool)]
    [McpServerTool(Name = ToolNames.OknoContract)]
    public ContractSummaryResult Contract()
        => ToolExecution.Run(
            _auditLog,
            _sessionManager.GetSnapshot(),
            ToolNames.OknoContract,
            null,
            invocation =>
            {
                ContractSummaryResult result = new(
                    ImplementedTools: ToolContractManifest.Implemented.Select(ContractToolDescriptorFactory.FromToolDescriptor).ToArray(),
                    DeferredTools: ToolContractManifest.Deferred.Select(ContractToolDescriptorFactory.FromToolDescriptor).ToArray(),
                    Notes: ToolContractManifest.ContractNotes);

                invocation.Complete("done", "Возвращён текущий MCP contract runtime.");
                return result;
            });

    [Description(ToolDescriptions.OknoSessionStateTool)]
    [McpServerTool(Name = ToolNames.OknoSessionState)]
    public SessionSnapshot SessionState()
        => ToolExecution.Run(
            _auditLog,
            _sessionManager.GetSnapshot(),
            ToolNames.OknoSessionState,
            null,
            invocation =>
            {
                SessionSnapshot snapshot = _sessionManager.GetSnapshot();
                invocation.Complete("done", "Возвращён текущий session snapshot.", snapshot.AttachedWindow?.Window.Hwnd);
                return snapshot;
            });
}
