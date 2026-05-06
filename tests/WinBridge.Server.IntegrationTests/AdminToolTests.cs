// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Guards;
using WinBridge.Runtime.Session;
using WinBridge.Runtime.Tooling;
using WinBridge.Runtime.Windows.Display;
using WinBridge.Server.Tools;

namespace WinBridge.Server.IntegrationTests;

public sealed class AdminToolTests
{
    private const string TestRunId = "admin-tool-tests";

    private static readonly string[] ImplementedInteractiveToolNames =
    [
        ToolNames.WindowsLaunchProcess,
        ToolNames.WindowsOpenTarget,
        ToolNames.WindowsInput,
    ];

    [Fact]
    public void HealthReturnsProbeBackedReadinessSnapshot()
    {
        AdminTools tools = CreateAdminTools();

        HealthResult result = tools.Health();

        Assert.Equal("Okno", result.Service);
        Assert.NotEqual(default, result.Readiness.CapturedAtUtc);
        Assert.Equal(
            new[]
            {
                (ReadinessDomainValues.DesktopSession, GuardStatusValues.Ready),
                (ReadinessDomainValues.SessionAlignment, GuardStatusValues.Ready),
                (ReadinessDomainValues.Integrity, GuardStatusValues.Degraded),
                (ReadinessDomainValues.UiAccess, GuardStatusValues.Blocked),
            },
            result.Readiness.Domains.Select(item => (item.Domain, item.Status)).ToArray());

        Assert.Equal(
            new[]
            {
                (CapabilitySummaryValues.Capture, GuardStatusValues.Ready),
                (CapabilitySummaryValues.Uia, GuardStatusValues.Degraded),
                (CapabilitySummaryValues.Wait, GuardStatusValues.Degraded),
                (CapabilitySummaryValues.Input, GuardStatusValues.Degraded),
                (CapabilitySummaryValues.Clipboard, GuardStatusValues.Blocked),
                (CapabilitySummaryValues.Launch, GuardStatusValues.Degraded),
            },
            result.Readiness.Capabilities.Select(item => (item.Capability, item.Status)).ToArray());

        Assert.Equal(
            new[]
            {
                CapabilitySummaryValues.Clipboard,
            },
            result.BlockedCapabilities.Select(item => item.Capability).ToArray());

        Assert.Equal(
            new[]
            {
                (GuardReasonCodeValues.IntegrityRequiresEqualOrLowerTarget, ReadinessDomainValues.Integrity),
                (GuardReasonCodeValues.UiaWorkerLaunchabilityUnverified, CapabilitySummaryValues.Uia),
                (GuardReasonCodeValues.WaitShellVisualAvailable, CapabilitySummaryValues.Wait),
                (GuardReasonCodeValues.InputUipiBarrierPresent, CapabilitySummaryValues.Input),
                (GuardReasonCodeValues.LaunchElevationBoundaryUnconfirmed, CapabilitySummaryValues.Launch),
            },
            result.Warnings.Select(item => (item.Code, item.Source)).ToArray());

        foreach (string toolName in ImplementedInteractiveToolNames)
        {
            Assert.Contains(toolName, result.ImplementedTools);
            Assert.False(result.DeferredTools.ContainsKey(toolName));
        }
    }

    [Fact]
    public void ContractUsesCanonicalSnakeCaseLiterals()
    {
        AdminTools tools = CreateAdminTools();

        ContractSummaryResult result = tools.Contract();

        ContractToolDescriptor attachDescriptor = ImplementedTool(ToolNames.WindowsAttachWindow);
        Assert.Equal(("implemented", "session_mutation"), (attachDescriptor.Lifecycle, attachDescriptor.SafetyClass));

        ContractToolDescriptor waitDescriptor = ImplementedTool(ToolNames.WindowsWait);
        Assert.Equal(("implemented", "os_side_effect"), (waitDescriptor.Lifecycle, waitDescriptor.SafetyClass));

        ContractToolDescriptor launchDescriptor = ImplementedTool(ToolNames.WindowsLaunchProcess);
        ContractToolExecutionPolicyDescriptor launchPolicy = Assert.IsType<ContractToolExecutionPolicyDescriptor>(launchDescriptor.ExecutionPolicy);
        Assert.Equal(
            ("launch", "high", "launch", true, "required", "launch_payload"),
            (launchPolicy.PolicyGroup,
                launchPolicy.RiskLevel,
                launchPolicy.GuardCapability,
                launchPolicy.SupportsDryRun,
                launchPolicy.ConfirmationMode,
                launchPolicy.RedactionClass));

        ContractToolDescriptor openTargetDescriptor = ImplementedTool(ToolNames.WindowsOpenTarget);
        ContractToolExecutionPolicyDescriptor openTargetPolicy = Assert.IsType<ContractToolExecutionPolicyDescriptor>(openTargetDescriptor.ExecutionPolicy);
        Assert.Equal(
            ("launch", "medium", "launch", true, "required", "launch_payload"),
            (openTargetPolicy.PolicyGroup,
                openTargetPolicy.RiskLevel,
                openTargetPolicy.GuardCapability,
                openTargetPolicy.SupportsDryRun,
                openTargetPolicy.ConfirmationMode,
                openTargetPolicy.RedactionClass));

        ContractToolDescriptor inputDescriptor = ImplementedTool(ToolNames.WindowsInput);
        ContractToolExecutionPolicyDescriptor inputPolicy = Assert.IsType<ContractToolExecutionPolicyDescriptor>(inputDescriptor.ExecutionPolicy);
        Assert.Equal(
            ("input", "destructive", "input", false, "required", "text_payload"),
            (inputPolicy.PolicyGroup,
                inputPolicy.RiskLevel,
                inputPolicy.GuardCapability,
                inputPolicy.SupportsDryRun,
                inputPolicy.ConfirmationMode,
                inputPolicy.RedactionClass));
        Assert.Null(inputDescriptor.PlannedPhase);
        Assert.Null(inputDescriptor.SuggestedAlternative);

        foreach (string toolName in ImplementedInteractiveToolNames)
        {
            Assert.DoesNotContain(result.DeferredTools, descriptor => descriptor.Name == toolName);
        }

        Assert.Contains("artifacts/events/materializer уже закрыты Package D", result.Notes, StringComparison.Ordinal);
        Assert.Contains("smoke/fresh-host acceptance закрыты Package E", result.Notes, StringComparison.Ordinal);
        Assert.DoesNotContain("artifacts/events/materializer rollout остаются отдельным follow-up", result.Notes, StringComparison.Ordinal);
        Assert.DoesNotContain("smoke/fresh-host acceptance остаются Package E", result.Notes, StringComparison.Ordinal);

        ContractToolDescriptor ImplementedTool(string name) =>
            Assert.Single(result.ImplementedTools, descriptor => descriptor.Name == name);
    }

    private static AdminTools CreateAdminTools()
    {
        AuditLogOptions options = CreateAuditLogOptions();
        AuditLog auditLog = new(options, TimeProvider.System);
        RuntimeInfo runtimeInfo = new(options);
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext(TestRunId));
        RuntimeGuardAssessment assessment = CreateAssessment(new FakeMonitorManager());

        return new AdminTools(auditLog, runtimeInfo, sessionManager, new FakeRuntimeGuardService(assessment));
    }

    private static AuditLogOptions CreateAuditLogOptions()
    {
        string root = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        string diagnosticsRoot = Path.Combine(root, "artifacts", "diagnostics");
        string runDirectory = Path.Combine(diagnosticsRoot, TestRunId);
        Directory.CreateDirectory(root);

        return new(
            ContentRootPath: root,
            EnvironmentName: "Tests",
            RunId: TestRunId,
            DiagnosticsRoot: diagnosticsRoot,
            RunDirectory: runDirectory,
            EventsPath: Path.Combine(runDirectory, "events.jsonl"),
            SummaryPath: Path.Combine(runDirectory, "summary.md"));
    }

    private static RuntimeGuardAssessment CreateAssessment(FakeMonitorManager monitorManager)
    {
        DisplayTopologySnapshot topology = monitorManager.GetTopologySnapshot();
        RuntimeReadinessSnapshot readiness = new(
            CapturedAtUtc: DateTimeOffset.UtcNow,
            Domains:
            [
                new(
                    Domain: ReadinessDomainValues.DesktopSession,
                    Status: GuardStatusValues.Ready,
                    Reasons:
                    [
                        new(Code: GuardReasonCodeValues.InputDesktopAvailable, Severity: GuardSeverityValues.Info,
                            MessageHuman: "Runtime успешно открыл input desktop текущей interactive session.",
                            Source: ReadinessDomainValues.DesktopSession)
                    ]),
                new(
                    Domain: ReadinessDomainValues.SessionAlignment,
                    Status: GuardStatusValues.Ready,
                    Reasons:
                    [
                        new(Code: GuardReasonCodeValues.SessionAlignedWithActiveConsole, Severity: GuardSeverityValues.Info,
                            MessageHuman: "Session текущего процесса совпадает с active console session.",
                            Source: ReadinessDomainValues.SessionAlignment)
                    ]),
                new(
                    Domain: ReadinessDomainValues.Integrity,
                    Status: GuardStatusValues.Degraded,
                    Reasons:
                    [
                        new(Code: GuardReasonCodeValues.IntegrityRequiresEqualOrLowerTarget, Severity: GuardSeverityValues.Warning,
                            MessageHuman: "Текущий token имеет medium integrity; interaction с higher-integrity target нельзя обещать по умолчанию.",
                            Source: ReadinessDomainValues.Integrity)
                    ]),
                new(
                    Domain: ReadinessDomainValues.UiAccess,
                    Status: GuardStatusValues.Blocked,
                    Reasons:
                    [
                        new(Code: GuardReasonCodeValues.UiAccessMissing, Severity: GuardSeverityValues.Blocked,
                            MessageHuman: "В текущем token отсутствует uiAccess; bypass обычного UIPI barrier нельзя считать доступным.",
                            Source: ReadinessDomainValues.UiAccess)
                    ]),
            ],
            Capabilities:
            [
                new(
                    Capability: CapabilitySummaryValues.Capture,
                    Status: GuardStatusValues.Ready,
                    Reasons:
                    [
                        new(Code: GuardReasonCodeValues.CaptureReady, Severity: GuardSeverityValues.Info,
                            MessageHuman: "Runtime может честно обещать current shipped capture semantics: strong display identity и Windows Graphics Capture доступны.",
                            Source: CapabilitySummaryValues.Capture)
                    ]),
                new(
                    Capability: CapabilitySummaryValues.Uia,
                    Status: GuardStatusValues.Degraded,
                    Reasons:
                    [
                        new(Code: GuardReasonCodeValues.UiaWorkerLaunchabilityUnverified, Severity: GuardSeverityValues.Warning,
                            MessageHuman: "Worker launch spec resolved, но runtime startability UIA boundary не подтверждена в reporting-first health path.",
                            Source: CapabilitySummaryValues.Uia),
                        new(Code: GuardReasonCodeValues.UiaObserveScopeLimited, Severity: GuardSeverityValues.Info,
                            MessageHuman: "Current UIA semantics ограничены window-scoped ElementFromHandle/control-view path и не обещают cross-user Run as reachability.",
                            Source: CapabilitySummaryValues.Uia)
                    ]),
                new(
                    Capability: CapabilitySummaryValues.Wait,
                    Status: GuardStatusValues.Degraded,
                    Reasons:
                    [
                        new(Code: GuardReasonCodeValues.WaitShellVisualAvailable, Severity: GuardSeverityValues.Warning,
                            MessageHuman: "windows.wait может честно обещать active_window_matches и visual_changed.",
                            Source: CapabilitySummaryValues.Wait),
                        new(Code: GuardReasonCodeValues.WaitUiaBranchLaunchabilityUnverified, Severity: GuardSeverityValues.Info,
                            MessageHuman: "UIA worker boundary только configured: launch spec resolved, но startability не подтверждена, поэтому UIA-based wait conditions не advertised как usable subset.",
                            Source: CapabilitySummaryValues.Wait)
                    ]),
                new(
                    Capability: CapabilitySummaryValues.Input,
                    Status: GuardStatusValues.Degraded,
                    Reasons:
                    [
                        new(Code: GuardReasonCodeValues.InputUipiBarrierPresent, Severity: GuardSeverityValues.Warning,
                            MessageHuman: "Общий input baseline допускает только equal-or-lower target path: medium integrity без uiAccess не подтверждает safe interaction с higher-integrity или protected UI targets.",
                            Source: CapabilitySummaryValues.Input)
                    ]),
                new(
                    Capability: CapabilitySummaryValues.Clipboard,
                    Status: GuardStatusValues.Blocked,
                    Reasons:
                    [
                        new(Code: GuardReasonCodeValues.CapabilityNotImplemented, Severity: GuardSeverityValues.Blocked,
                            MessageHuman: "Эта capability пока не реализована в текущем runtime surface и не может считаться готовой.",
                            Source: CapabilitySummaryValues.Clipboard),
                        new(Code: GuardReasonCodeValues.ClipboardIntegrityLimited, Severity: GuardSeverityValues.Blocked,
                            MessageHuman: "Clipboard path пока не должен обещать операции при неполном integrity profile. Текущий token имеет medium integrity; interaction с higher-integrity target нельзя обещать по умолчанию.",
                            Source: CapabilitySummaryValues.Clipboard)
                    ]),
                new(
                    Capability: CapabilitySummaryValues.Launch,
                    Status: GuardStatusValues.Degraded,
                    Reasons:
                    [
                        new(Code: GuardReasonCodeValues.LaunchElevationBoundaryUnconfirmed, Severity: GuardSeverityValues.Warning,
                            MessageHuman: "Live launch path остаётся confirmation-worthy: higher-integrity boundary заранее не подтверждена. Текущий token имеет medium integrity; interaction с higher-integrity target нельзя обещать по умолчанию.",
                            Source: CapabilitySummaryValues.Launch)
                    ]),
            ]);

        return new RuntimeGuardAssessment(
            Topology: topology,
            Readiness: readiness,
            BlockedCapabilities: [.. readiness.Capabilities.Where(item => item.Status == GuardStatusValues.Blocked)],
            Warnings:
            [
                .. readiness.Domains.SelectMany(item => item.Reasons).Where(reason => reason.Severity == GuardSeverityValues.Warning),
                .. readiness.Capabilities
                    .Where(item => item.Status != GuardStatusValues.Blocked)
                    .SelectMany(item => item.Reasons)
                    .Where(reason => reason.Severity == GuardSeverityValues.Warning),
            ]);
    }

    private sealed class FakeRuntimeGuardService(RuntimeGuardAssessment assessment) : IRuntimeGuardService
    {
        public RuntimeGuardAssessment GetSnapshot() => assessment;
    }
}
