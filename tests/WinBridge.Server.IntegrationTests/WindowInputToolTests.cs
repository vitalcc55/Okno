using System.Text.Json;
using ModelContextProtocol.Protocol;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Guards;
using WinBridge.Runtime.Session;
using WinBridge.Runtime.Tooling;
using WinBridge.Runtime.Waiting;
using WinBridge.Runtime.Windows.Capture;
using WinBridge.Runtime.Windows.Input;
using WinBridge.Runtime.Windows.Launch;
using WinBridge.Runtime.Windows.Shell;
using WinBridge.Server.Tools;

namespace WinBridge.Server.IntegrationTests;

public sealed class WindowInputToolTests
{
    [Fact]
    public async Task InputReturnsBlockedPayloadWithoutInvokingRuntimeService()
    {
        WindowDescriptor attachedWindow = CreateWindow();
        TestContext context = CreateContext(
            decision: CreateDecision(
                ToolExecutionDecisionKind.Blocked,
                ToolExecutionMode.Live,
                GuardReasonCodeValues.InputUipiBarrierPresent,
                GuardSeverityValues.Blocked),
            attachedWindow: attachedWindow);

        CallToolResult result = await context.Tools.Input(new InputRequest
        {
            Actions =
            [
                CreateClickAction(),
            ],
        });

        JsonElement payload = AssertStructuredPayload(result);
        Assert.True(result.IsError);
        Assert.Equal(InputStatusValues.Blocked, payload.GetProperty("status").GetString());
        Assert.Equal(InputStatusValues.Blocked, payload.GetProperty("decision").GetString());
        Assert.Equal(attachedWindow.Hwnd, payload.GetProperty("targetHwnd").GetInt64());
        Assert.Equal(0, context.InputService.Calls);
        Assert.Equal(1, context.Gate.Calls);
    }

    [Fact]
    public async Task InputReturnsNeedsConfirmationPayloadWithoutInvokingRuntimeService()
    {
        WindowDescriptor attachedWindow = CreateWindow();
        TestContext context = CreateContext(
            decision: CreateDecision(
                ToolExecutionDecisionKind.NeedsConfirmation,
                ToolExecutionMode.Live,
                GuardReasonCodeValues.InputUipiBarrierPresent,
                GuardSeverityValues.Warning,
                requiresConfirmation: true),
            attachedWindow: attachedWindow);

        CallToolResult result = await context.Tools.Input(new InputRequest
        {
            Actions =
            [
                CreateClickAction(),
            ],
        });

        JsonElement payload = AssertStructuredPayload(result);
        Assert.True(result.IsError);
        Assert.Equal(InputStatusValues.NeedsConfirmation, payload.GetProperty("status").GetString());
        Assert.Equal(InputStatusValues.NeedsConfirmation, payload.GetProperty("decision").GetString());
        Assert.Equal(attachedWindow.Hwnd, payload.GetProperty("targetHwnd").GetInt64());
        Assert.True(payload.GetProperty("requiresConfirmation").GetBoolean());
        Assert.Equal(0, context.InputService.Calls);
        Assert.Equal(1, context.Gate.Calls);
    }

    [Fact]
    public async Task InputInvalidRequestReturnsFailedPayloadWithoutRuntimeInvocation()
    {
        TestContext context = CreateContext(
            decision: CreateDecision(
                ToolExecutionDecisionKind.Allowed,
                ToolExecutionMode.Live,
                GuardReasonCodeValues.InputUipiBarrierPresent,
                GuardSeverityValues.Warning));

        using JsonDocument extraFieldDocument = JsonDocument.Parse("true");
        CallToolResult result = await context.Tools.Input(new InputRequest
        {
            Actions =
            [
                CreateClickAction(),
            ],
            AdditionalProperties = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["dryRun"] = extraFieldDocument.RootElement.Clone(),
            },
        });

        JsonElement payload = AssertStructuredPayload(result);
        Assert.True(result.IsError);
        Assert.Equal(InputStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.Equal(InputStatusValues.Failed, payload.GetProperty("decision").GetString());
        Assert.Equal(InputFailureCodeValues.InvalidRequest, payload.GetProperty("failureCode").GetString());
        Assert.Equal(0, context.InputService.Calls);
        Assert.Equal(0, context.Gate.Calls);
    }

    [Fact]
    public async Task InputRejectsEmptyKeysArrayAsInvalidRequest()
    {
        TestContext context = CreateContext(
            decision: CreateDecision(
                ToolExecutionDecisionKind.Allowed,
                ToolExecutionMode.Live,
                GuardReasonCodeValues.InputUipiBarrierPresent,
                GuardSeverityValues.Warning));

        CallToolResult result = await context.Tools.Input(new InputRequest
        {
            Actions =
            [
                new InputAction
                {
                    Type = InputActionTypeValues.Click,
                    CoordinateSpace = InputCoordinateSpaceValues.Screen,
                    Point = new InputPoint(100, 100),
                    Keys = [],
                },
            ],
        });

        JsonElement payload = AssertStructuredPayload(result);
        Assert.True(result.IsError);
        Assert.Equal(InputStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.Equal(InputFailureCodeValues.InvalidRequest, payload.GetProperty("failureCode").GetString());
        Assert.Equal(0, context.InputService.Calls);
        Assert.Equal(0, context.Gate.Calls);
    }

    [Fact]
    public async Task InputRejectsNullActionElementAsInvalidRequestWithoutAuditProjectionCrash()
    {
        TestContext context = CreateContext(
            decision: CreateDecision(
                ToolExecutionDecisionKind.Allowed,
                ToolExecutionMode.Live,
                GuardReasonCodeValues.InputUipiBarrierPresent,
                GuardSeverityValues.Warning));

        CallToolResult result = await context.Tools.Input(new InputRequest
        {
            Actions = [null!],
        });

        JsonElement payload = AssertStructuredPayload(result);
        Assert.True(result.IsError);
        Assert.Equal(InputStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.Equal(InputFailureCodeValues.InvalidRequest, payload.GetProperty("failureCode").GetString());
        Assert.Equal(0, context.InputService.Calls);
        Assert.Equal(0, context.Gate.Calls);

        string startedEvent = File.ReadLines(context.AuditOptions.EventsPath)
            .Single(line => line.Contains("\"event_name\":\"tool.invocation.started\"", StringComparison.Ordinal));
        Assert.Contains("\"request_summary\":", startedEvent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InputRejectsOverLimitBatchBeforeGateWithBoundedAuditSummary()
    {
        TestContext context = CreateContext(
            decision: CreateDecision(
                ToolExecutionDecisionKind.Allowed,
                ToolExecutionMode.Live,
                GuardReasonCodeValues.InputUipiBarrierPresent,
                GuardSeverityValues.Warning));

        InputAction[] actions = Enumerable.Range(0, 20)
            .Select(index => new InputAction
            {
                Type = InputActionTypeValues.Move,
                CoordinateSpace = InputCoordinateSpaceValues.Screen,
                Point = new InputPoint(100 + index, 100 + index),
            })
            .ToArray();

        CallToolResult result = await context.Tools.Input(new InputRequest
        {
            Actions = actions,
        });

        JsonElement payload = AssertStructuredPayload(result);
        Assert.True(result.IsError);
        Assert.Equal(InputStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.Equal(InputFailureCodeValues.InvalidRequest, payload.GetProperty("failureCode").GetString());
        Assert.Equal(0, context.InputService.Calls);
        Assert.Equal(0, context.Gate.Calls);

        string startedEvent = File.ReadLines(context.AuditOptions.EventsPath)
            .Single(line => line.Contains("\"event_name\":\"tool.invocation.started\"", StringComparison.Ordinal));
        Assert.Contains("\\u0022actionCount\\u0022:20", startedEvent, StringComparison.Ordinal);
        Assert.Contains("\\u0022truncated\\u0022:true", startedEvent, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u0022x\\u0022:116", startedEvent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InputRejectsMissingTargetBeforeGate()
    {
        TestContext context = CreateContext(
            decision: CreateDecision(
                ToolExecutionDecisionKind.NeedsConfirmation,
                ToolExecutionMode.Live,
                GuardReasonCodeValues.InputUipiBarrierPresent,
                GuardSeverityValues.Warning,
                requiresConfirmation: true));

        CallToolResult result = await context.Tools.Input(new InputRequest
        {
            Actions =
            [
                CreateClickAction(),
            ],
        });

        JsonElement payload = AssertStructuredPayload(result);
        Assert.True(result.IsError);
        Assert.Equal(InputStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.Equal(InputFailureCodeValues.MissingTarget, payload.GetProperty("failureCode").GetString());
        Assert.Equal(0, context.InputService.Calls);
        Assert.Equal(0, context.Gate.Calls);
    }

    [Fact]
    public async Task InputRejectsStaleExplicitTargetBeforeGate()
    {
        TestContext context = CreateContext(
            decision: CreateDecision(
                ToolExecutionDecisionKind.NeedsConfirmation,
                ToolExecutionMode.Live,
                GuardReasonCodeValues.InputUipiBarrierPresent,
                GuardSeverityValues.Warning,
                requiresConfirmation: true));

        CallToolResult result = await context.Tools.Input(new InputRequest
        {
            Hwnd = 9090,
            Actions =
            [
                CreateClickAction(),
            ],
        });

        JsonElement payload = AssertStructuredPayload(result);
        Assert.True(result.IsError);
        Assert.Equal(InputStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.Equal(InputFailureCodeValues.StaleExplicitTarget, payload.GetProperty("failureCode").GetString());
        Assert.Equal(9090, payload.GetProperty("targetHwnd").GetInt64());
        Assert.Equal(0, context.InputService.Calls);
        Assert.Equal(0, context.Gate.Calls);
    }

    [Fact]
    public async Task InputMaterializesResolverExceptionAsPreGateToolFailure()
    {
        WindowDescriptor attachedWindow = CreateWindow();
        TestContext context = CreateContext(
            decision: CreateDecision(
                ToolExecutionDecisionKind.Allowed,
                ToolExecutionMode.Live,
                GuardReasonCodeValues.InputUipiBarrierPresent,
                GuardSeverityValues.Warning),
            attachedWindow: attachedWindow,
            windowTargetResolver: new ThrowingWindowTargetResolver(new InvalidOperationException("resolver failed with secret")));

        CallToolResult result = await context.Tools.Input(new InputRequest
        {
            Actions =
            [
                CreateClickAction(),
            ],
        });

        JsonElement payload = AssertStructuredPayload(result);
        Assert.True(result.IsError);
        Assert.Equal(InputStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.Equal(InputFailureCodeValues.TargetPreflightFailed, payload.GetProperty("failureCode").GetString());
        Assert.Equal(attachedWindow.Hwnd, payload.GetProperty("targetHwnd").GetInt64());
        Assert.DoesNotContain("secret", payload.GetProperty("reason").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, context.InputService.Calls);
        Assert.Equal(0, context.Gate.Calls);
    }

    [Fact]
    public async Task InputAllowedLiveReturnsRuntimePayload()
    {
        WindowDescriptor attachedWindow = CreateWindow();
        FakeInputService inputService = new(
            (_, inputContext, _) => Task.FromResult(
                new InputResult(
                    Status: InputStatusValues.VerifyNeeded,
                    Decision: InputStatusValues.VerifyNeeded,
                    ResultMode: InputResultModeValues.DispatchOnly,
                    TargetHwnd: inputContext.AttachedWindow?.Hwnd,
                    TargetSource: InputTargetSourceValues.Attached,
                    CompletedActionCount: 1,
                    Actions:
                    [
                        new InputActionResult(
                            Type: InputActionTypeValues.Click,
                            Status: InputStatusValues.VerifyNeeded,
                            ResultMode: InputResultModeValues.DispatchOnly,
                            CoordinateSpace: InputCoordinateSpaceValues.Screen,
                            RequestedPoint: new InputPoint(100, 100),
                            ResolvedScreenPoint: new InputPoint(100, 100),
                            Button: InputButtonValues.Left),
                    ])));
        TestContext context = CreateContext(
            decision: CreateDecision(
                ToolExecutionDecisionKind.Allowed,
                ToolExecutionMode.Live,
                GuardReasonCodeValues.InputUipiBarrierPresent,
                GuardSeverityValues.Warning),
            inputService: inputService,
            attachedWindow: attachedWindow);

        CallToolResult result = await context.Tools.Input(new InputRequest
        {
            Actions =
            [
                CreateClickAction(),
            ],
            Confirm = true,
        });

        JsonElement payload = AssertStructuredPayload(result);
        Assert.False(result.IsError);
        Assert.Equal(InputStatusValues.VerifyNeeded, payload.GetProperty("status").GetString());
        Assert.Equal(InputStatusValues.VerifyNeeded, payload.GetProperty("decision").GetString());
        Assert.Equal(1, payload.GetProperty("completedActionCount").GetInt32());
        Assert.Equal(1, context.InputService.Calls);
        Assert.True(context.Gate.LastIntent?.ConfirmationGranted);
        Assert.Equal(attachedWindow.Hwnd, context.InputService.LastContext?.AttachedWindow?.Hwnd);
    }

    [Fact]
    public async Task InputNeedsConfirmationPreservesEffectiveAttachedTargetHwnd()
    {
        WindowDescriptor attachedWindow = CreateWindow(hwnd: 5353);
        TestContext context = CreateContext(
            decision: CreateDecision(
                ToolExecutionDecisionKind.NeedsConfirmation,
                ToolExecutionMode.Live,
                GuardReasonCodeValues.InputUipiBarrierPresent,
                GuardSeverityValues.Warning,
                requiresConfirmation: true),
            attachedWindow: attachedWindow);

        CallToolResult result = await context.Tools.Input(new InputRequest
        {
            Actions =
            [
                CreateClickAction(),
            ],
        });

        JsonElement payload = AssertStructuredPayload(result);
        Assert.True(result.IsError);
        Assert.Equal(InputStatusValues.NeedsConfirmation, payload.GetProperty("status").GetString());
        Assert.Equal(attachedWindow.Hwnd, payload.GetProperty("targetHwnd").GetInt64());
        Assert.Equal(0, context.InputService.Calls);
        Assert.Equal(1, context.Gate.Calls);
    }

    [Fact]
    public async Task InputUsesAttachedWindowFromInvocationSnapshot()
    {
        string root = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        AuditLogOptions options = new(
            ContentRootPath: root,
            EnvironmentName: "Tests",
            RunId: "input-tool-tests-snapshot",
            DiagnosticsRoot: Path.Combine(root, "artifacts", "diagnostics"),
            RunDirectory: Path.Combine(root, "artifacts", "diagnostics", "input-tool-tests-snapshot"),
            EventsPath: Path.Combine(root, "artifacts", "diagnostics", "input-tool-tests-snapshot", "events.jsonl"),
            SummaryPath: Path.Combine(root, "artifacts", "diagnostics", "input-tool-tests-snapshot", "summary.md"));
        AuditLog auditLog = new(options, TimeProvider.System);
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("input-tool-tests-snapshot"));
        WindowDescriptor firstWindow = CreateWindow(hwnd: 4242, title: "First");
        WindowDescriptor secondWindow = CreateWindow(hwnd: 4343, title: "Second");
        sessionManager.Attach(firstWindow, "tests");
        FakeWindowManager windowManager = new([firstWindow, secondWindow]);
        WaitResultMaterializer waitResultMaterializer = new(auditLog, options, WaitOptions.Default);
        FakeInputService inputService = new(
            (_, inputContext, _) => Task.FromResult(
                new InputResult(
                    Status: InputStatusValues.VerifyNeeded,
                    Decision: InputStatusValues.VerifyNeeded,
                    ResultMode: InputResultModeValues.DispatchOnly,
                    TargetHwnd: inputContext.AttachedWindow?.Hwnd,
                    TargetSource: InputTargetSourceValues.Attached)));
        FakeToolExecutionGate gate = new(
            (_, _) =>
            {
                sessionManager.Attach(secondWindow, "tests");
                return CreateDecision(
                    ToolExecutionDecisionKind.Allowed,
                    ToolExecutionMode.Live,
                    GuardReasonCodeValues.InputUipiBarrierPresent,
                    GuardSeverityValues.Warning);
            });

        WindowTools tools = new(
            auditLog,
            sessionManager,
            windowManager,
            new NoopCaptureService(),
            new FakeMonitorManager(),
            new FakeWindowActivationService(),
            new WindowTargetResolver(windowManager),
            new FakeUiAutomationService(),
            new FakeWaitService(),
            waitResultMaterializer,
            gate,
            inputService,
            new FakeProcessLaunchService(),
            new FakeOpenTargetService());

        CallToolResult result = await tools.Input(new InputRequest
        {
            Actions =
            [
                CreateClickAction(),
            ],
            Confirm = true,
        });

        JsonElement payload = AssertStructuredPayload(result);
        Assert.False(result.IsError);
        Assert.Equal(firstWindow.Hwnd, payload.GetProperty("targetHwnd").GetInt64());
        Assert.Equal(firstWindow.Hwnd, inputService.LastContext?.AttachedWindow?.Hwnd);
        Assert.Equal(secondWindow.Hwnd, sessionManager.GetAttachedWindow()?.Window.Hwnd);
    }

    [Fact]
    public async Task InputAllowedLiveFailureReturnsFailedDecision()
    {
        WindowDescriptor attachedWindow = CreateWindow();
        FakeInputService inputService = new(
            (_, _, _) => Task.FromResult(
                new InputResult(
                    Status: InputStatusValues.Failed,
                    Decision: InputStatusValues.Failed,
                    FailureCode: InputFailureCodeValues.InputDispatchFailed,
                    Reason: "Input dispatch failed.")));
        TestContext context = CreateContext(
            decision: CreateDecision(
                ToolExecutionDecisionKind.Allowed,
                ToolExecutionMode.Live,
                GuardReasonCodeValues.InputUipiBarrierPresent,
                GuardSeverityValues.Warning),
            inputService: inputService,
            attachedWindow: attachedWindow);

        CallToolResult result = await context.Tools.Input(new InputRequest
        {
            Actions =
            [
                CreateClickAction(),
            ],
            Confirm = true,
        });

        JsonElement payload = AssertStructuredPayload(result);
        Assert.True(result.IsError);
        Assert.Equal(InputStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.Equal(InputStatusValues.Failed, payload.GetProperty("decision").GetString());
        Assert.Equal(InputFailureCodeValues.InputDispatchFailed, payload.GetProperty("failureCode").GetString());
        Assert.Equal(1, context.InputService.Calls);
    }

    [Fact]
    public async Task InputAllowedLiveUnexpectedServiceFailureReturnsGenericFailedPayload()
    {
        WindowDescriptor attachedWindow = CreateWindow();
        FakeInputService inputService = new((_, _, _) => throw new InvalidOperationException("boom"));
        TestContext context = CreateContext(
            decision: CreateDecision(
                ToolExecutionDecisionKind.Allowed,
                ToolExecutionMode.Live,
                GuardReasonCodeValues.InputUipiBarrierPresent,
                GuardSeverityValues.Warning),
            inputService: inputService,
            attachedWindow: attachedWindow);

        CallToolResult result = await context.Tools.Input(new InputRequest
        {
            Actions =
            [
                CreateClickAction(),
            ],
            Confirm = true,
        });

        JsonElement payload = AssertStructuredPayload(result);
        Assert.True(result.IsError);
        Assert.Equal(InputStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.Equal(InputStatusValues.Failed, payload.GetProperty("decision").GetString());
        Assert.False(payload.TryGetProperty("failureCode", out _));
        Assert.Equal(1, context.InputService.Calls);
    }

    [Fact]
    public async Task InputAllowedLiveFactualRuntimeExceptionPreservesExceptionMetadataAndPayload()
    {
        WindowDescriptor attachedWindow = CreateWindow();
        FakeInputService inputService = new((_, _, _) =>
            throw new InputExecutionFailureException(
                new InputResult(
                    Status: InputStatusValues.Failed,
                    Decision: InputStatusValues.Failed,
                    FailureCode: InputFailureCodeValues.InputDispatchFailed,
                    Reason: "Runtime столкнулся с unexpected failure после committed input side effect; retry без явной проверки результата небезопасен.",
                    TargetHwnd: 101,
                    TargetSource: InputTargetSourceValues.Attached,
                    CompletedActionCount: 0,
                    FailedActionIndex: 0,
                    Actions:
                    [
                        new InputActionResult(
                            Type: InputActionTypeValues.Click,
                            Status: InputStatusValues.Failed,
                            FailureCode: InputFailureCodeValues.InputDispatchFailed,
                            Reason: "Runtime столкнулся с unexpected failure после committed input side effect; retry без явной проверки результата небезопасен.",
                            CoordinateSpace: InputCoordinateSpaceValues.Screen,
                            RequestedPoint: new InputPoint(100, 100),
                            ResolvedScreenPoint: new InputPoint(100, 100),
                            Button: InputButtonValues.Left),
                    ]),
                new InvalidOperationException("secret runtime failure")));
        TestContext context = CreateContext(
            decision: CreateDecision(
                ToolExecutionDecisionKind.Allowed,
                ToolExecutionMode.Live,
                GuardReasonCodeValues.InputUipiBarrierPresent,
                GuardSeverityValues.Warning),
            inputService: inputService,
            attachedWindow: attachedWindow);

        CallToolResult result = await context.Tools.Input(new InputRequest
        {
            Actions =
            [
                CreateClickAction(),
            ],
            Confirm = true,
        });

        JsonElement payload = AssertStructuredPayload(result);
        Assert.True(result.IsError);
        Assert.Equal(InputStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.Equal(InputFailureCodeValues.InputDispatchFailed, payload.GetProperty("failureCode").GetString());
        Assert.Equal(0, payload.GetProperty("failedActionIndex").GetInt32());

        string completedEvent = File.ReadLines(context.AuditOptions.EventsPath)
            .Single(line => line.Contains("\"event_name\":\"tool.invocation.completed\"", StringComparison.Ordinal));
        Assert.Contains("\"exception_type\":\"System.InvalidOperationException\"", completedEvent, StringComparison.Ordinal);
        Assert.DoesNotContain("secret runtime failure", completedEvent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InputAllowedLiveFactualRuntimeExceptionReturnsPayloadWhenAuditCompletionFails()
    {
        AuditLogOptions? auditOptions = null;
        FakeInputService inputService = new((_, _, _) =>
        {
            File.Delete(auditOptions!.SummaryPath);
            Directory.CreateDirectory(auditOptions.SummaryPath);
            throw new InputExecutionFailureException(
                new InputResult(
                    Status: InputStatusValues.Failed,
                    Decision: InputStatusValues.Failed,
                    FailureCode: InputFailureCodeValues.InputDispatchFailed,
                    Reason: "Runtime столкнулся с unexpected failure после committed input side effect; retry без явной проверки результата небезопасен.",
                    TargetHwnd: 101,
                    TargetSource: InputTargetSourceValues.Attached,
                    CompletedActionCount: 0,
                    FailedActionIndex: 0,
                    Actions:
                    [
                        new InputActionResult(
                            Type: InputActionTypeValues.Click,
                            Status: InputStatusValues.Failed,
                            FailureCode: InputFailureCodeValues.InputDispatchFailed,
                            Reason: "Runtime столкнулся с unexpected failure после committed input side effect; retry без явной проверки результата небезопасен.",
                            CoordinateSpace: InputCoordinateSpaceValues.Screen,
                            RequestedPoint: new InputPoint(100, 100),
                            ResolvedScreenPoint: new InputPoint(100, 100),
                            Button: InputButtonValues.Left),
                    ]),
                new InvalidOperationException("secret runtime failure"));
        });
        TestContext context = CreateContext(
            decision: CreateDecision(
                ToolExecutionDecisionKind.Allowed,
                ToolExecutionMode.Live,
                GuardReasonCodeValues.InputUipiBarrierPresent,
                GuardSeverityValues.Warning),
            inputService: inputService,
            attachedWindow: CreateWindow());
        auditOptions = context.AuditOptions;

        CallToolResult result = await context.Tools.Input(new InputRequest
        {
            Actions =
            [
                CreateClickAction(),
            ],
            Confirm = true,
        });

        JsonElement payload = AssertStructuredPayload(result);
        Assert.True(result.IsError);
        Assert.Equal(InputStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.Equal(InputFailureCodeValues.InputDispatchFailed, payload.GetProperty("failureCode").GetString());
        Assert.Equal(0, payload.GetProperty("failedActionIndex").GetInt32());
        Assert.Equal(1, context.InputService.Calls);
    }

    [Fact]
    public async Task InputStartedAuditSummaryDoesNotExposeKeyboardLikeRejectedPayload()
    {
        TestContext context = CreateContext(
            decision: CreateDecision(
                ToolExecutionDecisionKind.Allowed,
                ToolExecutionMode.Live,
                GuardReasonCodeValues.InputUipiBarrierPresent,
                GuardSeverityValues.Warning));

        CallToolResult result = await context.Tools.Input(new InputRequest
        {
            Actions =
            [
                new InputAction
                {
                    Type = InputActionTypeValues.Keypress,
                    Key = "Ctrl+V",
                },
            ],
        });

        JsonElement payload = AssertStructuredPayload(result);
        Assert.True(result.IsError);
        Assert.Equal(InputStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.Equal(InputFailureCodeValues.UnsupportedActionType, payload.GetProperty("failureCode").GetString());

        string startedEvent = File.ReadLines(context.AuditOptions.EventsPath)
            .Single(line => line.Contains("\"event_name\":\"tool.invocation.started\"", StringComparison.Ordinal));
        Assert.Contains("\"request_summary\":", startedEvent, StringComparison.Ordinal);
        Assert.DoesNotContain("Ctrl+V", startedEvent, StringComparison.Ordinal);
        Assert.DoesNotContain("\"key\"", startedEvent, StringComparison.Ordinal);
        Assert.DoesNotContain("\"keys\"", startedEvent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InputStartedAuditSummaryDoesNotExposeNestedRejectedPayload()
    {
        TestContext context = CreateContext(
            decision: CreateDecision(
                ToolExecutionDecisionKind.Allowed,
                ToolExecutionMode.Live,
                GuardReasonCodeValues.InputUipiBarrierPresent,
                GuardSeverityValues.Warning));

        InputRequest request = JsonSerializer.Deserialize<InputRequest>(
            """
            {
              "actions": [
                {
                  "type": "click",
                  "coordinateSpace": "capture_pixels",
                  "point": {
                    "x": 10,
                    "y": 20,
                    "secret": "nested-point-secret"
                  },
                  "captureReference": {
                    "bounds": {
                      "left": 100,
                      "top": 200,
                      "right": 420,
                      "bottom": 560,
                      "note": "nested-bounds-secret"
                    },
                    "pixelWidth": 320,
                    "pixelHeight": 360,
                    "effectiveDpi": 96,
                    "secret": "nested-capture-secret"
                  }
                }
              ]
            }
            """)!;

        CallToolResult result = await context.Tools.Input(request);

        JsonElement payload = AssertStructuredPayload(result);
        Assert.True(result.IsError);
        Assert.Equal(InputStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.Equal(InputFailureCodeValues.InvalidRequest, payload.GetProperty("failureCode").GetString());
        Assert.Equal(0, context.InputService.Calls);

        string startedEvent = File.ReadLines(context.AuditOptions.EventsPath)
            .Single(line => line.Contains("\"event_name\":\"tool.invocation.started\"", StringComparison.Ordinal));
        Assert.Contains("\"request_summary\":", startedEvent, StringComparison.Ordinal);
        Assert.DoesNotContain("nested-point-secret", startedEvent, StringComparison.Ordinal);
        Assert.DoesNotContain("nested-capture-secret", startedEvent, StringComparison.Ordinal);
        Assert.DoesNotContain("nested-bounds-secret", startedEvent, StringComparison.Ordinal);
        Assert.DoesNotContain("\"secret\"", startedEvent, StringComparison.Ordinal);
        Assert.DoesNotContain("\"note\"", startedEvent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InputStartedAuditSummaryDoesNotExposeRejectedEnumLikeLiterals()
    {
        TestContext context = CreateContext(
            decision: CreateDecision(
                ToolExecutionDecisionKind.Allowed,
                ToolExecutionMode.Live,
                GuardReasonCodeValues.InputUipiBarrierPresent,
                GuardSeverityValues.Warning));

        CallToolResult result = await context.Tools.Input(new InputRequest
        {
            Actions =
            [
                new InputAction
                {
                    Type = "secret-type-token",
                },
                new InputAction
                {
                    Type = InputActionTypeValues.Click,
                    CoordinateSpace = InputCoordinateSpaceValues.Screen,
                    Point = new InputPoint(100, 100),
                    Button = "secret-button-token",
                },
                new InputAction
                {
                    Type = InputActionTypeValues.Click,
                    CoordinateSpace = "secret-coordinate-token",
                    Point = new InputPoint(100, 100),
                },
            ],
        });

        JsonElement payload = AssertStructuredPayload(result);
        Assert.True(result.IsError);
        Assert.Equal(InputStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.Equal(InputFailureCodeValues.UnsupportedActionType, payload.GetProperty("failureCode").GetString());
        Assert.Equal(0, context.InputService.Calls);

        string startedEvent = File.ReadLines(context.AuditOptions.EventsPath)
            .Single(line => line.Contains("\"event_name\":\"tool.invocation.started\"", StringComparison.Ordinal));
        Assert.Contains("\"request_summary\":", startedEvent, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-type-token", startedEvent, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-button-token", startedEvent, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-coordinate-token", startedEvent, StringComparison.Ordinal);
    }

    private static TestContext CreateContext(
        ToolExecutionDecision decision,
        FakeInputService? inputService = null,
        WindowDescriptor? attachedWindow = null,
        IWindowTargetResolver? windowTargetResolver = null)
    {
        string root = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        AuditLogOptions options = new(
            ContentRootPath: root,
            EnvironmentName: "Tests",
            RunId: "input-tool-tests",
            DiagnosticsRoot: Path.Combine(root, "artifacts", "diagnostics"),
            RunDirectory: Path.Combine(root, "artifacts", "diagnostics", "input-tool-tests"),
            EventsPath: Path.Combine(root, "artifacts", "diagnostics", "input-tool-tests", "events.jsonl"),
            SummaryPath: Path.Combine(root, "artifacts", "diagnostics", "input-tool-tests", "summary.md"));

        AuditLog auditLog = new(options, TimeProvider.System);
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("input-tool-tests"));
        if (attachedWindow is not null)
        {
            sessionManager.Attach(attachedWindow, "tests");
        }

        FakeWindowManager windowManager = new(attachedWindow is null ? [] : [attachedWindow]);
        FakeToolExecutionGate gate = new((_, _) => decision);
        FakeInputService effectiveInputService = inputService ?? new FakeInputService();
        WaitResultMaterializer waitResultMaterializer = new(auditLog, options, WaitOptions.Default);

        return new TestContext(
            new WindowTools(
                auditLog,
                sessionManager,
                windowManager,
                new NoopCaptureService(),
                new FakeMonitorManager(),
                new FakeWindowActivationService(),
                windowTargetResolver ?? new WindowTargetResolver(windowManager),
                new FakeUiAutomationService(),
                new FakeWaitService(),
                waitResultMaterializer,
                gate,
                effectiveInputService,
                new FakeProcessLaunchService(),
                new FakeOpenTargetService()),
            gate,
            effectiveInputService,
            options);
    }

    private static ToolExecutionDecision CreateDecision(
        ToolExecutionDecisionKind kind,
        ToolExecutionMode mode,
        string reasonCode,
        string severity,
        bool requiresConfirmation = false) =>
        new(
            Kind: kind,
            Mode: mode,
            RiskLevel: ToolExecutionRiskLevel.Destructive,
            Reasons:
            [
                new GuardReason(
                    reasonCode,
                    severity,
                    "Input boundary test reason.",
                    CapabilitySummaryValues.Input),
            ],
            RequiresConfirmation: requiresConfirmation,
            DryRunSupported: false,
            GuardCapability: CapabilitySummaryValues.Input);

    private static JsonElement AssertStructuredPayload(CallToolResult result)
    {
        Assert.NotNull(result.StructuredContent);
        Assert.Single(result.Content);
        Assert.IsType<TextContentBlock>(result.Content[0]);
        return result.StructuredContent!.Value;
    }

    private static InputAction CreateClickAction() =>
        new()
        {
            Type = InputActionTypeValues.Click,
            CoordinateSpace = InputCoordinateSpaceValues.Screen,
            Point = new InputPoint(100, 100),
        };

    private static WindowDescriptor CreateWindow(long hwnd = 4242, string title = "Input Test Window") =>
        new(
            Hwnd: hwnd,
            Title: title,
            ProcessName: "SmokeWindowHost",
            ProcessId: 1010,
            ThreadId: 2020,
            ClassName: "InputTestClass",
            Bounds: new Bounds(0, 0, 640, 480),
            IsForeground: true,
            IsVisible: true,
            WindowState: WindowStateValues.Normal);

    private sealed record TestContext(
        WindowTools Tools,
        FakeToolExecutionGate Gate,
        FakeInputService InputService,
        AuditLogOptions AuditOptions);

    private sealed class FakeWindowManager(IReadOnlyList<WindowDescriptor> windows) : IWindowManager
    {
        public IReadOnlyList<WindowDescriptor> ListWindows(bool includeInvisible)
        {
            if (includeInvisible)
            {
                return windows;
            }

            return windows.Where(window => window.IsVisible).ToArray();
        }

        public WindowDescriptor? GetWindow(long hwnd) =>
            windows.FirstOrDefault(window => window.Hwnd == hwnd);

        public WindowDescriptor? FindWindow(WindowSelector selector)
        {
            if (selector.Hwnd is long hwnd)
            {
                return GetWindow(hwnd);
            }

            return windows.Count > 0 ? windows[0] : null;
        }

        public WindowDescriptor? GetForegroundWindow()
        {
            for (int index = 0; index < windows.Count; index++)
            {
                if (windows[index].IsForeground)
                {
                    return windows[index];
                }
            }

            return null;
        }

        public bool TryFocus(long hwnd) => windows.Any(window => window.Hwnd == hwnd);
    }

    private sealed class ThrowingWindowTargetResolver(Exception exception) : IWindowTargetResolver
    {
        public WindowDescriptor? ResolveExplicitOrAttachedWindow(long? explicitHwnd, WindowDescriptor? attachedWindow) =>
            throw exception;

        public WindowDescriptor? ResolveLiveWindowByIdentity(WindowDescriptor expectedWindow) =>
            throw exception;

        public UiaSnapshotTargetResolution ResolveUiaSnapshotTarget(long? explicitHwnd, WindowDescriptor? attachedWindow) =>
            throw exception;

        public InputTargetResolution ResolveInputTarget(long? explicitHwnd, WindowDescriptor? attachedWindow) =>
            throw exception;

        public WaitTargetResolution ResolveWaitTarget(long? explicitHwnd, WindowDescriptor? attachedWindow) =>
            throw exception;
    }

    private sealed class NoopCaptureService : ICaptureService
    {
        public Task<CaptureResult> CaptureAsync(CaptureTarget target, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Capture не должен вызываться в input boundary tests.");
    }
}
