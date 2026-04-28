using System.Text.Json;
using System.Reflection;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Guards;
using WinBridge.Runtime.Tooling;

namespace WinBridge.Runtime.Tests;

public sealed class AuditLogTests
{
    [Fact]
    public void BeginInvocationWritesStartedAndCompletedEvents()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = new(
            ContentRootPath: root,
            EnvironmentName: "Tests",
            RunId: "run-001",
            DiagnosticsRoot: Path.Combine(root, "artifacts", "diagnostics"),
            RunDirectory: Path.Combine(root, "artifacts", "diagnostics", "run-001"),
            EventsPath: Path.Combine(root, "artifacts", "diagnostics", "run-001", "events.jsonl"),
            SummaryPath: Path.Combine(root, "artifacts", "diagnostics", "run-001", "summary.md"));
        AuditLog auditLog = new(options, TimeProvider.System);
        SessionSnapshot snapshot = SessionSnapshot.CreateInitial("run-001", DateTimeOffset.UtcNow);

        using (AuditInvocationScope invocation = auditLog.BeginInvocation("okno.health", new { probe = "test" }, snapshot))
        {
            invocation.Complete("done", "Тестовый вызов завершён.");
        }

        string[] lines = File.ReadAllLines(options.EventsPath);
        Assert.Equal(2, lines.Length);
        Assert.Contains("\"event_name\":\"tool.invocation.started\"", lines[0], StringComparison.Ordinal);
        Assert.Contains("\"event_name\":\"tool.invocation.completed\"", lines[1], StringComparison.Ordinal);
        Assert.Contains("\"schema_version\":\"1.0.0\"", lines[0], StringComparison.Ordinal);

        string summary = File.ReadAllText(options.SummaryPath);
        Assert.Contains("run-001", summary, StringComparison.Ordinal);
        Assert.Contains("Тестовый вызов завершён.", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void RecordSessionAttachedDeduplicatesIdenticalStateTransitions()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = new(
            ContentRootPath: root,
            EnvironmentName: "Tests",
            RunId: "run-002",
            DiagnosticsRoot: Path.Combine(root, "artifacts", "diagnostics"),
            RunDirectory: Path.Combine(root, "artifacts", "diagnostics", "run-002"),
            EventsPath: Path.Combine(root, "artifacts", "diagnostics", "run-002", "events.jsonl"),
            SummaryPath: Path.Combine(root, "artifacts", "diagnostics", "run-002", "summary.md"));
        AuditLog auditLog = new(options, TimeProvider.System);
        WindowDescriptor window = new(
            Hwnd: 42,
            Title: "Calculator",
            ProcessName: "CalculatorApp",
            ProcessId: 42,
            ThreadId: 84,
            ClassName: "CalcWindow",
            Bounds: new Bounds(0, 0, 800, 600),
            IsForeground: true,
            IsVisible: true);
        SessionSnapshot before = SessionSnapshot.CreateInitial("run-002", DateTimeOffset.UtcNow);
        SessionSnapshot after = new("window", new AttachedWindow(window, "hwnd"), DateTimeOffset.UtcNow, "run-002");

        auditLog.RecordSessionAttached(before, after);
        auditLog.RecordSessionAttached(after, after);

        string[] lines = File.ReadAllLines(options.EventsPath);
        Assert.Single(lines);

        Assert.Contains("\"event_name\":\"session.attached\"", lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void BeginInvocationRedactsSensitiveRequestSummaryAndWritesMarkers()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = new(
            ContentRootPath: root,
            EnvironmentName: "Tests",
            RunId: "run-003",
            DiagnosticsRoot: Path.Combine(root, "artifacts", "diagnostics"),
            RunDirectory: Path.Combine(root, "artifacts", "diagnostics", "run-003"),
            EventsPath: Path.Combine(root, "artifacts", "diagnostics", "run-003", "events.jsonl"),
            SummaryPath: Path.Combine(root, "artifacts", "diagnostics", "run-003", "summary.md"));
        AuditLog auditLog = new(options, TimeProvider.System);
        SessionSnapshot snapshot = SessionSnapshot.CreateInitial("run-003", DateTimeOffset.UtcNow);

        using (AuditInvocationScope invocation = auditLog.BeginInvocation(
                   "windows.wait",
                   new
                   {
                       condition = "text_appears",
                       expectedText = "super secret text",
                       selector = new
                       {
                           name = "Sensitive field",
                           automationId = "SearchBox",
                       },
                       timeoutMs = 500,
                   },
                   snapshot))
        {
            invocation.Complete("done", "Тестовый wait вызов завершён.");
        }

        string startedEvent = File.ReadAllLines(options.EventsPath)[0];
        Assert.Contains("\"event_name\":\"tool.invocation.started\"", startedEvent, StringComparison.Ordinal);
        Assert.Contains("\"redaction_applied\":\"true\"", startedEvent, StringComparison.Ordinal);
        Assert.Contains("\"redaction_class\":\"text_payload\"", startedEvent, StringComparison.Ordinal);
        Assert.Contains("\"redacted_fields\":\"expectedText,selector.name\"", startedEvent, StringComparison.Ordinal);
        Assert.DoesNotContain("super secret text", startedEvent, StringComparison.Ordinal);
        Assert.DoesNotContain("Sensitive field", startedEvent, StringComparison.Ordinal);
        Assert.Contains("\"request_summary\":", startedEvent, StringComparison.Ordinal);
    }

    [Fact]
    public void BeginInvocationSuppressesRequestSummaryWhenRedactorFails()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = new(
            ContentRootPath: root,
            EnvironmentName: "Tests",
            RunId: "run-004",
            DiagnosticsRoot: Path.Combine(root, "artifacts", "diagnostics"),
            RunDirectory: Path.Combine(root, "artifacts", "diagnostics", "run-004"),
            EventsPath: Path.Combine(root, "artifacts", "diagnostics", "run-004", "events.jsonl"),
            SummaryPath: Path.Combine(root, "artifacts", "diagnostics", "run-004", "summary.md"));
        AuditLog auditLog = new(options, TimeProvider.System, new ThrowingAuditPayloadRedactor());
        SessionSnapshot snapshot = SessionSnapshot.CreateInitial("run-004", DateTimeOffset.UtcNow);

        using (AuditInvocationScope invocation = auditLog.BeginInvocation(
                   "windows.wait",
                   new
                   {
                       expectedText = "super secret text",
                   },
                   snapshot))
        {
            invocation.Complete("done", "Тестовый wait вызов завершён.");
        }

        string startedEvent = File.ReadAllLines(options.EventsPath)[0];
        Assert.Contains("\"request_summary_suppressed\":\"true\"", startedEvent, StringComparison.Ordinal);
        Assert.DoesNotContain("\"request_summary\":", startedEvent, StringComparison.Ordinal);
        Assert.DoesNotContain("super secret text", startedEvent, StringComparison.Ordinal);
    }

    [Fact]
    public void BeginInvocationRedactsComputerUseWinStateTokenFromRequestSummary()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = new(
            ContentRootPath: root,
            EnvironmentName: "Tests",
            RunId: "run-004b",
            DiagnosticsRoot: Path.Combine(root, "artifacts", "diagnostics"),
            RunDirectory: Path.Combine(root, "artifacts", "diagnostics", "run-004b"),
            EventsPath: Path.Combine(root, "artifacts", "diagnostics", "run-004b", "events.jsonl"),
            SummaryPath: Path.Combine(root, "artifacts", "diagnostics", "run-004b", "summary.md"));
        AuditLog auditLog = new(options, TimeProvider.System);
        SessionSnapshot snapshot = SessionSnapshot.CreateInitial("run-004b", DateTimeOffset.UtcNow);

        using (AuditInvocationScope invocation = auditLog.BeginInvocation(
                   ToolNames.ComputerUseWinClick,
                   new
                   {
                       stateToken = "super-secret-state-token",
                       elementIndex = 1,
                   },
                   snapshot))
        {
            invocation.Complete("done", "Тестовый computer-use-win click вызов завершён.");
        }

        string startedEvent = File.ReadAllLines(options.EventsPath)[0];
        Assert.Contains("\"event_name\":\"tool.invocation.started\"", startedEvent, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret-state-token", startedEvent, StringComparison.Ordinal);
        Assert.Contains("\"redaction_applied\":\"true\"", startedEvent, StringComparison.Ordinal);
    }

    [Fact]
    public void BeginInvocationRedactsComputerUseWinPressKeyRequestSummary()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = new(
            ContentRootPath: root,
            EnvironmentName: "Tests",
            RunId: "run-004c",
            DiagnosticsRoot: Path.Combine(root, "artifacts", "diagnostics"),
            RunDirectory: Path.Combine(root, "artifacts", "diagnostics", "run-004c"),
            EventsPath: Path.Combine(root, "artifacts", "diagnostics", "run-004c", "events.jsonl"),
            SummaryPath: Path.Combine(root, "artifacts", "diagnostics", "run-004c", "summary.md"));
        AuditLog auditLog = new(options, TimeProvider.System);
        SessionSnapshot snapshot = SessionSnapshot.CreateInitial("run-004c", DateTimeOffset.UtcNow);

        using (AuditInvocationScope invocation = auditLog.BeginInvocation(
                   ToolNames.ComputerUseWinPressKey,
                   new
                   {
                       stateToken = "super-secret-state-token",
                       key = "ctrl+shift+w",
                       repeat = 2,
                       confirm = true,
                   },
                   snapshot))
        {
            invocation.Complete("done", "Тестовый computer-use-win press_key вызов завершён.");
        }

        string startedEvent = File.ReadAllLines(options.EventsPath)[0];
        Assert.Contains("\"event_name\":\"tool.invocation.started\"", startedEvent, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret-state-token", startedEvent, StringComparison.Ordinal);
        Assert.DoesNotContain("ctrl+shift+w", startedEvent, StringComparison.Ordinal);
        Assert.Contains("\"redaction_applied\":\"true\"", startedEvent, StringComparison.Ordinal);
    }

    [Fact]
    public void BeginInvocationRedactsComputerUseWinDragRequestSummary()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = new(
            ContentRootPath: root,
            EnvironmentName: "Tests",
            RunId: "run-004d",
            DiagnosticsRoot: Path.Combine(root, "artifacts", "diagnostics"),
            RunDirectory: Path.Combine(root, "artifacts", "diagnostics", "run-004d"),
            EventsPath: Path.Combine(root, "artifacts", "diagnostics", "run-004d", "events.jsonl"),
            SummaryPath: Path.Combine(root, "artifacts", "diagnostics", "run-004d", "summary.md"));
        AuditLog auditLog = new(options, TimeProvider.System);
        SessionSnapshot snapshot = SessionSnapshot.CreateInitial("run-004d", DateTimeOffset.UtcNow);

        using (AuditInvocationScope invocation = auditLog.BeginInvocation(
                   ToolNames.ComputerUseWinDrag,
                   new
                   {
                       stateToken = "super-secret-state-token",
                       fromPoint = new { x = 20, y = 30 },
                       toPoint = new { x = 140, y = 90 },
                       confirm = true,
                   },
                   snapshot))
        {
            invocation.Complete("done", "Тестовый computer-use-win drag вызов завершён.");
        }

        string startedEvent = File.ReadAllLines(options.EventsPath)[0];
        Assert.Contains("\"event_name\":\"tool.invocation.started\"", startedEvent, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret-state-token", startedEvent, StringComparison.Ordinal);
        Assert.DoesNotContain("\"x\":20", startedEvent, StringComparison.Ordinal);
        Assert.DoesNotContain("\"y\":30", startedEvent, StringComparison.Ordinal);
        Assert.Contains("\"redaction_applied\":\"true\"", startedEvent, StringComparison.Ordinal);
    }

    [Fact]
    public void AuditToolContextCoversImplementedComputerUseWinActionsWithRedaction()
    {
        string[] implementedActionToolNames =
            ToolContractManifest.GetProfile(ToolSurfaceProfileValues.ComputerUseWin)
                .ImplementedNames
                .Where(toolName => toolName is not ToolNames.ComputerUseWinListApps and not ToolNames.ComputerUseWinGetAppState)
                .ToArray();
        Type auditToolContextType = typeof(AuditLog).Assembly.GetType("WinBridge.Runtime.Diagnostics.AuditToolContext")!;
        MethodInfo resolveMethod = auditToolContextType.GetMethod("Resolve", BindingFlags.Public | BindingFlags.Static)!;
        PropertyInfo redactionClassProperty = auditToolContextType.GetProperty("RedactionClass", BindingFlags.Instance | BindingFlags.Public)!;

        foreach (string toolName in implementedActionToolNames)
        {
            object context = resolveMethod.Invoke(null, [toolName, null])!;
            ToolExecutionRedactionClass redactionClass = (ToolExecutionRedactionClass)redactionClassProperty.GetValue(context)!;
            Assert.NotEqual(ToolExecutionRedactionClass.None, redactionClass);
        }
    }

    [Fact]
    public void BeginInvocationRedactsComputerUseWinSetValueRequestSummary()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = new(
            ContentRootPath: root,
            EnvironmentName: "Tests",
            RunId: "run-004d",
            DiagnosticsRoot: Path.Combine(root, "artifacts", "diagnostics"),
            RunDirectory: Path.Combine(root, "artifacts", "diagnostics", "run-004d"),
            EventsPath: Path.Combine(root, "artifacts", "diagnostics", "run-004d", "events.jsonl"),
            SummaryPath: Path.Combine(root, "artifacts", "diagnostics", "run-004d", "summary.md"));
        AuditLog auditLog = new(options, TimeProvider.System);
        SessionSnapshot snapshot = SessionSnapshot.CreateInitial("run-004d", DateTimeOffset.UtcNow);

        using (AuditInvocationScope invocation = auditLog.BeginInvocation(
                   ToolNames.ComputerUseWinSetValue,
                   new
                   {
                       stateToken = "super-secret-state-token",
                       elementIndex = 1,
                       valueKind = "text",
                       textValue = "super-secret-semantic-value",
                   },
                   snapshot))
        {
            invocation.Complete("done", "Тестовый computer-use-win set_value вызов завершён.");
        }

        string startedEvent = File.ReadAllLines(options.EventsPath)[0];
        Assert.Contains("\"event_name\":\"tool.invocation.started\"", startedEvent, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret-state-token", startedEvent, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret-semantic-value", startedEvent, StringComparison.Ordinal);
        Assert.Contains("\"redaction_applied\":\"true\"", startedEvent, StringComparison.Ordinal);
    }

    [Fact]
    public void BeginInvocationRedactsComputerUseWinSetValueNumericRequestSummary()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = new(
            ContentRootPath: root,
            EnvironmentName: "Tests",
            RunId: "run-004e",
            DiagnosticsRoot: Path.Combine(root, "artifacts", "diagnostics"),
            RunDirectory: Path.Combine(root, "artifacts", "diagnostics", "run-004e"),
            EventsPath: Path.Combine(root, "artifacts", "diagnostics", "run-004e", "events.jsonl"),
            SummaryPath: Path.Combine(root, "artifacts", "diagnostics", "run-004e", "summary.md"));
        AuditLog auditLog = new(options, TimeProvider.System);
        SessionSnapshot snapshot = SessionSnapshot.CreateInitial("run-004e", DateTimeOffset.UtcNow);

        using (AuditInvocationScope invocation = auditLog.BeginInvocation(
                   ToolNames.ComputerUseWinSetValue,
                   new
                   {
                       stateToken = "super-secret-state-token",
                       elementIndex = 1,
                       valueKind = "number",
                       numberValue = 9.5,
                   },
                   snapshot))
        {
            invocation.Complete("done", "Тестовый computer-use-win numeric set_value вызов завершён.");
        }

        string startedEvent = File.ReadAllLines(options.EventsPath)[0];
        Assert.Contains("\"event_name\":\"tool.invocation.started\"", startedEvent, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret-state-token", startedEvent, StringComparison.Ordinal);
        Assert.DoesNotContain("9.5", startedEvent, StringComparison.Ordinal);
        Assert.Contains("\"redaction_applied\":\"true\"", startedEvent, StringComparison.Ordinal);
    }

    [Fact]
    public void BeginInvocationRedactsComputerUseWinTypeTextRequestSummary()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = new(
            ContentRootPath: root,
            EnvironmentName: "Tests",
            RunId: "run-004f",
            DiagnosticsRoot: Path.Combine(root, "artifacts", "diagnostics"),
            RunDirectory: Path.Combine(root, "artifacts", "diagnostics", "run-004f"),
            EventsPath: Path.Combine(root, "artifacts", "diagnostics", "run-004f", "events.jsonl"),
            SummaryPath: Path.Combine(root, "artifacts", "diagnostics", "run-004f", "summary.md"));
        AuditLog auditLog = new(options, TimeProvider.System);
        SessionSnapshot snapshot = SessionSnapshot.CreateInitial("run-004f", DateTimeOffset.UtcNow);

        using (AuditInvocationScope invocation = auditLog.BeginInvocation(
                   ToolNames.ComputerUseWinTypeText,
                   new
                   {
                       stateToken = "super-secret-state-token",
                       text = "  stage five raw text  ",
                   },
                   snapshot))
        {
            invocation.Complete("done", "Тестовый computer-use-win type_text вызов завершён.");
        }

        string startedEvent = File.ReadAllLines(options.EventsPath)[0];
        Assert.Contains("\"event_name\":\"tool.invocation.started\"", startedEvent, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret-state-token", startedEvent, StringComparison.Ordinal);
        Assert.DoesNotContain("stage five raw text", startedEvent, StringComparison.Ordinal);
        Assert.Contains("\"redaction_applied\":\"true\"", startedEvent, StringComparison.Ordinal);
    }

    [Fact]
    public void RecordRuntimeEventFailsClosedWhenRedactorThrowsOnEventData()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = new(
            ContentRootPath: root,
            EnvironmentName: "Tests",
            RunId: "run-005",
            DiagnosticsRoot: Path.Combine(root, "artifacts", "diagnostics"),
            RunDirectory: Path.Combine(root, "artifacts", "diagnostics", "run-005"),
            EventsPath: Path.Combine(root, "artifacts", "diagnostics", "run-005", "events.jsonl"),
            SummaryPath: Path.Combine(root, "artifacts", "diagnostics", "run-005", "summary.md"));
        AuditLog auditLog = new(options, TimeProvider.System, new ThrowingAuditPayloadRedactor());

        auditLog.RecordRuntimeEvent(
            eventName: "wait.runtime.completed",
            severity: "warning",
            messageHuman: "Безопасное сообщение.",
            toolName: "windows.wait",
            outcome: "failed",
            windowHwnd: 42,
            data: new Dictionary<string, string?>
            {
                ["exception_message"] = "secret runtime payload",
                ["user_text"] = "super secret text",
            });

        string eventLine = Assert.Single(File.ReadAllLines(options.EventsPath));
        Assert.Contains("\"event_data_suppressed\":\"true\"", eventLine, StringComparison.Ordinal);
        Assert.DoesNotContain("secret runtime payload", eventLine, StringComparison.Ordinal);
        Assert.DoesNotContain("super secret text", eventLine, StringComparison.Ordinal);
        Assert.DoesNotContain("\"user_text\":", eventLine, StringComparison.Ordinal);
    }

    [Fact]
    public void CompleteFailsClosedWhenRedactorThrowsOnCompletionData()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = new(
            ContentRootPath: root,
            EnvironmentName: "Tests",
            RunId: "run-006",
            DiagnosticsRoot: Path.Combine(root, "artifacts", "diagnostics"),
            RunDirectory: Path.Combine(root, "artifacts", "diagnostics", "run-006"),
            EventsPath: Path.Combine(root, "artifacts", "diagnostics", "run-006", "events.jsonl"),
            SummaryPath: Path.Combine(root, "artifacts", "diagnostics", "run-006", "summary.md"));
        AuditLog auditLog = new(options, TimeProvider.System, new ThrowingAuditPayloadRedactor());
        SessionSnapshot snapshot = SessionSnapshot.CreateInitial("run-006", DateTimeOffset.UtcNow);

        using (AuditInvocationScope invocation = auditLog.BeginInvocation("windows.wait", new { condition = "text_appears" }, snapshot))
        {
            invocation.Complete(
                "failed",
                "Безопасное сообщение.",
                data: new Dictionary<string, string?>
                {
                    ["exception_message"] = "secret completion payload",
                    ["user_text"] = "super secret text",
                });
        }

        string completedEvent = File.ReadAllLines(options.EventsPath)[1];
        Assert.Contains("\"event_data_suppressed\":\"true\"", completedEvent, StringComparison.Ordinal);
        Assert.DoesNotContain("secret completion payload", completedEvent, StringComparison.Ordinal);
        Assert.DoesNotContain("super secret text", completedEvent, StringComparison.Ordinal);
        Assert.DoesNotContain("\"user_text\":", completedEvent, StringComparison.Ordinal);
    }

    [Fact]
    public void CompletePreservesPublicDecisionWhenGateDecisionMetadataIsPresent()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = new(
            ContentRootPath: root,
            EnvironmentName: "Tests",
            RunId: "run-006b",
            DiagnosticsRoot: Path.Combine(root, "artifacts", "diagnostics"),
            RunDirectory: Path.Combine(root, "artifacts", "diagnostics", "run-006b"),
            EventsPath: Path.Combine(root, "artifacts", "diagnostics", "run-006b", "events.jsonl"),
            SummaryPath: Path.Combine(root, "artifacts", "diagnostics", "run-006b", "summary.md"));
        AuditLog auditLog = new(options, TimeProvider.System);
        SessionSnapshot snapshot = SessionSnapshot.CreateInitial("run-006b", DateTimeOffset.UtcNow);

        using (AuditInvocationScope invocation = auditLog.BeginInvocation(
                   "windows.launch_process",
                   new { executable = "notepad.exe" },
                   snapshot))
        {
            invocation.SetDecision(
                new ToolExecutionDecision(
                    Kind: ToolExecutionDecisionKind.Allowed,
                    Mode: ToolExecutionMode.Live,
                    RiskLevel: ToolExecutionRiskLevel.High,
                    Reasons:
                    [
                        new GuardReason(
                            GuardReasonCodeValues.LaunchElevationBoundaryUnconfirmed,
                            GuardSeverityValues.Warning,
                            "Launch требует подтверждённой integrity boundary.",
                            CapabilitySummaryValues.Launch),
                    ],
                    RequiresConfirmation: true,
                    DryRunSupported: true,
                    GuardCapability: CapabilitySummaryValues.Launch));
            invocation.Complete(
                "failed",
                "Launch завершился ошибкой.",
                data: new Dictionary<string, string?>
                {
                    ["status"] = "failed",
                    ["decision"] = "failed",
                    ["failure_code"] = "start_failed",
                    ["executable_identity"] = "notepad.exe",
                });
        }

        string completedEvent = File.ReadAllLines(options.EventsPath)[1];
        Assert.Contains("\"decision\":\"failed\"", completedEvent, StringComparison.Ordinal);
        Assert.Contains("\"gate_decision\":\"allowed\"", completedEvent, StringComparison.Ordinal);
        Assert.Contains("\"gate_risk_level\":\"high\"", completedEvent, StringComparison.Ordinal);
        Assert.Contains("\"gate_guard_capability\":\"launch\"", completedEvent, StringComparison.Ordinal);
        Assert.Contains("\"gate_requires_confirmation\":\"true\"", completedEvent, StringComparison.Ordinal);
        Assert.Contains("\"gate_dry_run_supported\":\"true\"", completedEvent, StringComparison.Ordinal);
        Assert.Contains("\"gate_reason_codes\":\"launch_elevation_boundary_unconfirmed\"", completedEvent, StringComparison.Ordinal);
    }

    [Fact]
    public void RecordRuntimeEventSanitizesFailureMessageForRedactedTool()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = new(
            ContentRootPath: root,
            EnvironmentName: "Tests",
            RunId: "run-007",
            DiagnosticsRoot: Path.Combine(root, "artifacts", "diagnostics"),
            RunDirectory: Path.Combine(root, "artifacts", "diagnostics", "run-007"),
            EventsPath: Path.Combine(root, "artifacts", "diagnostics", "run-007", "events.jsonl"),
            SummaryPath: Path.Combine(root, "artifacts", "diagnostics", "run-007", "summary.md"));
        AuditLog auditLog = new(options, TimeProvider.System);

        auditLog.RecordRuntimeEvent(
            eventName: "uia.snapshot.runtime.completed",
            severity: "warning",
            messageHuman: "secret traversal failure",
            toolName: "windows.uia_snapshot",
            outcome: "failed",
            windowHwnd: 42,
            data: new Dictionary<string, string?>
            {
                ["failure_stage"] = "traversal",
            });

        string eventLine = Assert.Single(File.ReadAllLines(options.EventsPath));
        string summary = File.ReadAllText(options.SummaryPath);
        Assert.DoesNotContain("secret traversal failure", eventLine, StringComparison.Ordinal);
        Assert.DoesNotContain("secret traversal failure", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void CompleteSanitizesFailureMessageForRedactedTool()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = new(
            ContentRootPath: root,
            EnvironmentName: "Tests",
            RunId: "run-008",
            DiagnosticsRoot: Path.Combine(root, "artifacts", "diagnostics"),
            RunDirectory: Path.Combine(root, "artifacts", "diagnostics", "run-008"),
            EventsPath: Path.Combine(root, "artifacts", "diagnostics", "run-008", "events.jsonl"),
            SummaryPath: Path.Combine(root, "artifacts", "diagnostics", "run-008", "summary.md"));
        AuditLog auditLog = new(options, TimeProvider.System);
        SessionSnapshot snapshot = SessionSnapshot.CreateInitial("run-008", DateTimeOffset.UtcNow);

        using (AuditInvocationScope invocation = auditLog.BeginInvocation("windows.wait", new { condition = "text_appears" }, snapshot))
        {
            invocation.Complete("failed", "secret wait failure from runtime");
        }

        string completedEvent = File.ReadAllLines(options.EventsPath)[1];
        string summary = File.ReadAllText(options.SummaryPath);
        Assert.DoesNotContain("secret wait failure from runtime", completedEvent, StringComparison.Ordinal);
        Assert.DoesNotContain("secret wait failure from runtime", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void RecordRuntimeEventSanitizesLaunchExecutableToBasename()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = new(
            ContentRootPath: root,
            EnvironmentName: "Tests",
            RunId: "run-009",
            DiagnosticsRoot: Path.Combine(root, "artifacts", "diagnostics"),
            RunDirectory: Path.Combine(root, "artifacts", "diagnostics", "run-009"),
            EventsPath: Path.Combine(root, "artifacts", "diagnostics", "run-009", "events.jsonl"),
            SummaryPath: Path.Combine(root, "artifacts", "diagnostics", "run-009", "summary.md"));
        AuditLog auditLog = new(options, TimeProvider.System);

        auditLog.RecordRuntimeEvent(
            eventName: "launch.runtime.completed",
            severity: "info",
            messageHuman: "Launch diagnostics.",
            toolName: "windows.launch_process",
            outcome: "done",
            windowHwnd: null,
            data: new Dictionary<string, string?>
            {
                ["executable"] = @"C:\tools\demo.exe",
                ["arguments"] = "--token=super-secret",
            });

        string eventLine = Assert.Single(File.ReadAllLines(options.EventsPath));
        using JsonDocument document = JsonDocument.Parse(eventLine);
        JsonElement data = document.RootElement.GetProperty("data");
        Assert.Equal("demo.exe", data.GetProperty("executable").GetString());
        Assert.False(data.TryGetProperty("arguments", out _));
        Assert.Equal("launch_payload", data.GetProperty("redaction_class").GetString());
    }

    [Fact]
    public void RecordRuntimeEventFailsClosedForLaunchRuntimeEventWhenRedactorThrows()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = new(
            ContentRootPath: root,
            EnvironmentName: "Tests",
            RunId: "run-010",
            DiagnosticsRoot: Path.Combine(root, "artifacts", "diagnostics"),
            RunDirectory: Path.Combine(root, "artifacts", "diagnostics", "run-010"),
            EventsPath: Path.Combine(root, "artifacts", "diagnostics", "run-010", "events.jsonl"),
            SummaryPath: Path.Combine(root, "artifacts", "diagnostics", "run-010", "summary.md"));
        AuditLog auditLog = new(options, TimeProvider.System, new ThrowingAuditPayloadRedactor());

        auditLog.RecordRuntimeEvent(
            eventName: "launch.runtime.completed",
            severity: "warning",
            messageHuman: "Launch завершился без раскрытия raw payload.",
            toolName: "windows.launch_process",
            outcome: "failed",
            windowHwnd: null,
            data: new Dictionary<string, string?>
            {
                ["executable_identity"] = @"C:\secret\demo.exe",
                ["working_directory"] = @"C:\Users\alice\private",
                ["arguments"] = "--token=super-secret",
            });

        string eventLine = Assert.Single(File.ReadAllLines(options.EventsPath));
        Assert.Contains("\"event_data_suppressed\":\"true\"", eventLine, StringComparison.Ordinal);
        Assert.DoesNotContain(@"C:\secret\demo.exe", eventLine, StringComparison.Ordinal);
        Assert.DoesNotContain(@"C:\Users\alice\private", eventLine, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret", eventLine, StringComparison.Ordinal);
    }

    [Fact]
    public void RecordRuntimeEventSanitizesLaunchFailureMessage()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = new(
            ContentRootPath: root,
            EnvironmentName: "Tests",
            RunId: "run-011",
            DiagnosticsRoot: Path.Combine(root, "artifacts", "diagnostics"),
            RunDirectory: Path.Combine(root, "artifacts", "diagnostics", "run-011"),
            EventsPath: Path.Combine(root, "artifacts", "diagnostics", "run-011", "events.jsonl"),
            SummaryPath: Path.Combine(root, "artifacts", "diagnostics", "run-011", "summary.md"));
        AuditLog auditLog = new(options, TimeProvider.System);

        auditLog.RecordRuntimeEvent(
            eventName: "launch.runtime.completed",
            severity: "warning",
            messageHuman: @"Launch failed for C:\secret\demo.exe --token=super-secret",
            toolName: "windows.launch_process",
            outcome: "failed",
            windowHwnd: null,
            data: new Dictionary<string, string?>
            {
                ["failure_code"] = "start_failed",
            });

        string eventLine = Assert.Single(File.ReadAllLines(options.EventsPath));
        string summary = File.ReadAllText(options.SummaryPath);
        Assert.DoesNotContain(@"C:\secret\demo.exe", eventLine, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret", eventLine, StringComparison.Ordinal);
        Assert.DoesNotContain(@"C:\secret\demo.exe", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void RecordRuntimeEventAddsSafeLaunchIdentifiersToSummary()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = new(
            ContentRootPath: root,
            EnvironmentName: "Tests",
            RunId: "run-012",
            DiagnosticsRoot: Path.Combine(root, "artifacts", "diagnostics"),
            RunDirectory: Path.Combine(root, "artifacts", "diagnostics", "run-012"),
            EventsPath: Path.Combine(root, "artifacts", "diagnostics", "run-012", "events.jsonl"),
            SummaryPath: Path.Combine(root, "artifacts", "diagnostics", "run-012", "summary.md"));
        AuditLog auditLog = new(options, TimeProvider.System);

        auditLog.RecordRuntimeEvent(
            eventName: "launch.runtime.completed",
            severity: "info",
            messageHuman: "Runtime launch завершён.",
            toolName: "windows.launch_process",
            outcome: "done",
            windowHwnd: null,
            data: new Dictionary<string, string?>
            {
                ["executable_identity"] = @"C:\tools\demo.exe",
                ["process_id"] = "4242",
                ["result_mode"] = "process_started",
                ["artifact_path"] = @"C:\artifacts\diagnostics\launch\launch-20260406T140000000-demo.json",
            });

        string summary = File.ReadAllText(options.SummaryPath);
        Assert.Contains("demo.exe", summary, StringComparison.Ordinal);
        Assert.Contains("4242", summary, StringComparison.Ordinal);
        Assert.Contains("process_started", summary, StringComparison.Ordinal);
        Assert.Contains(@"C:\artifacts\diagnostics\launch\launch-20260406T140000000-demo.json", summary, StringComparison.Ordinal);
        Assert.DoesNotContain(@"C:\tools\demo.exe", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void TryRecordRuntimeEventPreservesFailClosedRedactionMarkers()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = new(
            ContentRootPath: root,
            EnvironmentName: "Tests",
            RunId: "run-013",
            DiagnosticsRoot: Path.Combine(root, "artifacts", "diagnostics"),
            RunDirectory: Path.Combine(root, "artifacts", "diagnostics", "run-013"),
            EventsPath: Path.Combine(root, "artifacts", "diagnostics", "run-013", "events.jsonl"),
            SummaryPath: Path.Combine(root, "artifacts", "diagnostics", "run-013", "summary.md"));
        AuditLog auditLog = new(options, TimeProvider.System, new ThrowingAuditPayloadRedactor());

        bool recorded = auditLog.TryRecordRuntimeEvent(
            eventName: "wait.runtime.completed",
            severity: "warning",
            messageHuman: "Безопасное сообщение.",
            toolName: "windows.wait",
            outcome: "failed",
            windowHwnd: 42,
            data: new Dictionary<string, string?>
            {
                ["exception_message"] = "secret runtime payload",
                ["user_text"] = "super secret text",
            });

        Assert.True(recorded);
        string eventLine = Assert.Single(File.ReadAllLines(options.EventsPath));
        Assert.Contains("\"event_data_suppressed\":\"true\"", eventLine, StringComparison.Ordinal);
        Assert.DoesNotContain("secret runtime payload", eventLine, StringComparison.Ordinal);
        Assert.DoesNotContain("super secret text", eventLine, StringComparison.Ordinal);
    }

    [Fact]
    public void TryRecordRuntimeEventReturnsFalseWhenSinkWriteFails()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = new(
            ContentRootPath: root,
            EnvironmentName: "Tests",
            RunId: "run-014",
            DiagnosticsRoot: Path.Combine(root, "artifacts", "diagnostics"),
            RunDirectory: Path.Combine(root, "artifacts", "diagnostics", "run-014"),
            EventsPath: Path.Combine(root, "artifacts", "diagnostics", "run-014", "events.jsonl"),
            SummaryPath: Path.Combine(root, "artifacts", "diagnostics", "run-014", "summary.md"));
        AuditLog auditLog = new(options, TimeProvider.System);

        File.Delete(options.EventsPath);
        Directory.CreateDirectory(options.EventsPath);

        bool recorded = auditLog.TryRecordRuntimeEvent(
            eventName: "launch.runtime.completed",
            severity: "info",
            messageHuman: "Runtime launch завершён.",
            toolName: "windows.launch_process",
            outcome: "done",
            windowHwnd: null,
            data: new Dictionary<string, string?>
            {
                ["executable_identity"] = "demo.exe",
            });

        Assert.False(recorded);
        Assert.True(Directory.Exists(options.EventsPath));
    }

    [Fact]
    public void RecordRuntimeEventAddsSafeOpenTargetIdentifiersToSummary()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = new(
            ContentRootPath: root,
            EnvironmentName: "Tests",
            RunId: "run-open-target-summary",
            DiagnosticsRoot: Path.Combine(root, "artifacts", "diagnostics"),
            RunDirectory: Path.Combine(root, "artifacts", "diagnostics", "run-open-target-summary"),
            EventsPath: Path.Combine(root, "artifacts", "diagnostics", "run-open-target-summary", "events.jsonl"),
            SummaryPath: Path.Combine(root, "artifacts", "diagnostics", "run-open-target-summary", "summary.md"));
        AuditLog auditLog = new(options, TimeProvider.System);

        auditLog.RecordRuntimeEvent(
            eventName: "open_target.runtime.completed",
            severity: "info",
            messageHuman: "Runtime open target завершён.",
            toolName: "windows.open_target",
            outcome: "done",
            windowHwnd: null,
            data: new Dictionary<string, string?>
            {
                ["target_kind"] = "document",
                ["target_identity"] = @"C:\Docs\Quarterly\report.pdf",
                ["handler_process_id"] = "4242",
                ["artifact_path"] = @"C:\artifacts\diagnostics\launch\open-target-20260408T130000000-demo.json",
            });

        string summary = File.ReadAllText(options.SummaryPath);
        Assert.Contains("target_kind=document", summary, StringComparison.Ordinal);
        Assert.Contains("target_identity=report.pdf", summary, StringComparison.Ordinal);
        Assert.Contains("handler_process_id=4242", summary, StringComparison.Ordinal);
        Assert.Contains(@"C:\artifacts\diagnostics\launch\open-target-20260408T130000000-demo.json", summary, StringComparison.Ordinal);
        Assert.DoesNotContain(@"C:\Docs\Quarterly\report.pdf", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void RecordRuntimeEventSanitizesOpenTargetFailureMessage()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = new(
            ContentRootPath: root,
            EnvironmentName: "Tests",
            RunId: "run-open-target-failure-message",
            DiagnosticsRoot: Path.Combine(root, "artifacts", "diagnostics"),
            RunDirectory: Path.Combine(root, "artifacts", "diagnostics", "run-open-target-failure-message"),
            EventsPath: Path.Combine(root, "artifacts", "diagnostics", "run-open-target-failure-message", "events.jsonl"),
            SummaryPath: Path.Combine(root, "artifacts", "diagnostics", "run-open-target-failure-message", "summary.md"));
        AuditLog auditLog = new(options, TimeProvider.System);

        auditLog.RecordRuntimeEvent(
            eventName: "open_target.runtime.completed",
            severity: "warning",
            messageHuman: "Open target failed for https://example.test/docs?q=hidden#fragment",
            toolName: "windows.open_target",
            outcome: "failed",
            windowHwnd: null,
            data: new Dictionary<string, string?>
            {
                ["failure_code"] = "shell_rejected_target",
                ["uri_scheme"] = "https",
            });

        string eventLine = Assert.Single(File.ReadAllLines(options.EventsPath));
        string summary = File.ReadAllText(options.SummaryPath);
        Assert.DoesNotContain("https://example.test/docs?q=hidden#fragment", eventLine, StringComparison.Ordinal);
        Assert.DoesNotContain("https://example.test/docs?q=hidden#fragment", summary, StringComparison.Ordinal);
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class ThrowingAuditPayloadRedactor : IAuditPayloadRedactor
    {
        public AuditRedactionResult Redact(AuditPayloadRedactionContext context, object? payload)
            => throw new InvalidOperationException("Redactor should fail closed.");
    }
}
