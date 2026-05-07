// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

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
using WinBridge.Runtime.Windows.Shell;
using WinBridge.Server.Tools;

namespace WinBridge.Server.IntegrationTests;

public sealed class WindowInputToolTests
{
    private const string RuntimeCompletedEventName = "input.runtime.completed";
    private const string ToolInvocationStartedEventName = "tool.invocation.started";
    private const string ToolInvocationCompletedEventName = "tool.invocation.completed";
    private const string SecretRuntimeFailureMessage = "secret runtime failure";
    private const string CommittedInputFailureReason =
        "Runtime столкнулся с unexpected failure после committed input side effect; retry без явной проверки результата небезопасен.";

    [Fact]
    public async Task InputReturnsBlockedPayloadWithoutInvokingRuntimeService()
    {
        WindowDescriptor attachedWindow = CreateWindow();
        TestContext context = CreateContext(CreateBlockedDecision(), attachedWindow: attachedWindow);

        CallToolResult result = await context.Tools.Input(CreateClickRequest());

        AssertGateRejectedWithoutRuntime(result, context, InputStatusValues.Blocked, attachedWindow);
    }

    [Fact]
    public async Task InputReturnsNeedsConfirmationPayloadWithoutInvokingRuntimeService()
    {
        WindowDescriptor attachedWindow = CreateWindow();
        TestContext context = CreateContext(CreateNeedsConfirmationDecision(), attachedWindow: attachedWindow);

        CallToolResult result = await context.Tools.Input(CreateClickRequest());

        JsonElement payload = AssertGateRejectedWithoutRuntime(result, context, InputStatusValues.NeedsConfirmation, attachedWindow);
        Assert.True(payload.GetProperty("requiresConfirmation").GetBoolean());
    }

    [Fact]
    public async Task InputInvalidRequestReturnsFailedPayloadWithoutRuntimeInvocation()
    {
        TestContext context = CreateContext();
        using JsonDocument extraFieldDocument = JsonDocument.Parse("true");

        CallToolResult result = await context.Tools.Input(new InputRequest
        {
            Actions = [CreateClickAction()],
            AdditionalProperties = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["dryRun"] = extraFieldDocument.RootElement.Clone(),
            },
        });

        AssertPreGateFailure(result, context, InputFailureCodeValues.InvalidRequest, assertFailedDecision: true);
        AssertRuntimeCompletionWasNotAudited(context);
    }

    [Fact]
    public async Task InputRejectsEmptyKeysArrayAsInvalidRequest()
    {
        TestContext context = CreateContext();

        CallToolResult result = await context.Tools.Input(CreateInputRequest(new InputAction
        {
            Type = InputActionTypeValues.Click,
            CoordinateSpace = InputCoordinateSpaceValues.Screen,
            Point = new InputPoint(100, 100),
            Keys = [],
        }));

        AssertPreGateFailure(result, context, InputFailureCodeValues.InvalidRequest);
    }

    [Fact]
    public async Task InputRejectsNullActionElementAsInvalidRequestWithoutAuditProjectionCrash()
    {
        TestContext context = CreateContext();

        CallToolResult result = await context.Tools.Input(new InputRequest
        {
            Actions = [null!],
        });

        AssertPreGateFailure(result, context, InputFailureCodeValues.InvalidRequest);
        Assert.Contains("\"request_summary\":", ReadSingleAuditEvent(context, ToolInvocationStartedEventName), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InputRejectsOverLimitBatchBeforeGateWithBoundedAuditSummary()
    {
        TestContext context = CreateContext();
        InputAction[] actions = Enumerable.Range(0, 20).Select(CreateMoveAction).ToArray();

        CallToolResult result = await context.Tools.Input(CreateInputRequest(actions));

        AssertPreGateFailure(result, context, InputFailureCodeValues.InvalidRequest);
        string startedEvent = ReadSingleAuditEvent(context, ToolInvocationStartedEventName);
        Assert.Contains("\\u0022actionCount\\u0022:20", startedEvent, StringComparison.Ordinal);
        Assert.Contains("\\u0022truncated\\u0022:true", startedEvent, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u0022x\\u0022:116", startedEvent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InputRejectsMissingTargetBeforeGate()
    {
        TestContext context = CreateContext(CreateNeedsConfirmationDecision());

        CallToolResult result = await context.Tools.Input(CreateClickRequest());

        AssertPreGateFailure(result, context, InputFailureCodeValues.MissingTarget);
    }

    [Fact]
    public async Task InputRejectsStaleExplicitTargetBeforeGate()
    {
        const long staleHwnd = 9090;
        TestContext context = CreateContext(CreateNeedsConfirmationDecision());

        CallToolResult result = await context.Tools.Input(CreateClickRequest(staleHwnd));

        JsonElement payload = AssertPreGateFailure(result, context, InputFailureCodeValues.StaleExplicitTarget);
        Assert.Equal(staleHwnd, payload.GetProperty("targetHwnd").GetInt64());
    }

    [Fact]
    public async Task InputMaterializesResolverExceptionAsPreGateToolFailure()
    {
        WindowDescriptor attachedWindow = CreateWindow();
        TestContext context = CreateContext(
            attachedWindow: attachedWindow,
            windowTargetResolver: new ThrowingWindowTargetResolver(new InvalidOperationException("resolver failed with secret")));

        CallToolResult result = await context.Tools.Input(CreateClickRequest());

        JsonElement payload = AssertPreGateFailure(result, context, InputFailureCodeValues.TargetPreflightFailed);
        Assert.Equal(attachedWindow.Hwnd, payload.GetProperty("targetHwnd").GetInt64());
        Assert.DoesNotContain("secret", payload.GetProperty("reason").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InputAllowedLiveReturnsRuntimePayload()
    {
        WindowDescriptor attachedWindow = CreateWindow();
        string artifactPath = CreateArtifactPath("input.json");
        FakeInputService inputService = new((_, inputContext, _) => Task.FromResult(
            CreateVerifyNeededClickResult(inputContext.AttachedWindow?.Hwnd, artifactPath)));
        TestContext context = CreateContext(inputService: inputService, attachedWindow: attachedWindow);

        CallToolResult result = await context.Tools.Input(CreateConfirmedClickRequest());

        JsonElement payload = AssertStructuredPayload(result);
        Assert.False(result.IsError);
        Assert.Equal(InputStatusValues.VerifyNeeded, payload.GetProperty("status").GetString());
        Assert.Equal(InputStatusValues.VerifyNeeded, payload.GetProperty("decision").GetString());
        Assert.Equal(1, payload.GetProperty("completedActionCount").GetInt32());
        Assert.Equal(artifactPath, payload.GetProperty("artifactPath").GetString());
        Assert.Equal(1, context.InputService.Calls);
        Assert.True(context.Gate.LastIntent?.ConfirmationGranted);
        Assert.Equal(attachedWindow.Hwnd, context.InputService.LastContext?.AttachedWindow?.Hwnd);
    }

    [Fact]
    public async Task InputNeedsConfirmationPreservesEffectiveAttachedTargetHwnd()
    {
        WindowDescriptor attachedWindow = CreateWindow(hwnd: 5353);
        TestContext context = CreateContext(CreateNeedsConfirmationDecision(), attachedWindow: attachedWindow);

        CallToolResult result = await context.Tools.Input(CreateClickRequest());

        AssertGateRejectedWithoutRuntime(result, context, InputStatusValues.NeedsConfirmation, attachedWindow);
    }

    [Fact]
    public async Task InputUsesAttachedWindowFromInvocationSnapshot()
    {
        TestInfrastructure infrastructure = CreateInfrastructure("input-tool-tests-snapshot");
        WindowDescriptor firstWindow = CreateWindow(hwnd: 4242, title: "First");
        WindowDescriptor secondWindow = CreateWindow(hwnd: 4343, title: "Second");
        infrastructure.SessionManager.Attach(firstWindow, "tests");

        FakeWindowManager windowManager = new([firstWindow, secondWindow]);
        FakeInputService inputService = new((_, inputContext, _) => Task.FromResult(
            CreateVerifyNeededAttachedResult(inputContext.AttachedWindow?.Hwnd)));
        FakeToolExecutionGate gate = new((_, _) =>
        {
            infrastructure.SessionManager.Attach(secondWindow, "tests");
            return CreateAllowedDecision();
        });
        WindowTools tools = CreateWindowTools(infrastructure, windowManager, gate, inputService);

        CallToolResult result = await tools.Input(CreateConfirmedClickRequest());

        JsonElement payload = AssertStructuredPayload(result);
        Assert.False(result.IsError);
        Assert.Equal(firstWindow.Hwnd, payload.GetProperty("targetHwnd").GetInt64());
        Assert.Equal(firstWindow.Hwnd, inputService.LastContext?.AttachedWindow?.Hwnd);
        Assert.Equal(secondWindow.Hwnd, infrastructure.SessionManager.GetAttachedWindow()?.Window.Hwnd);
    }

    [Fact]
    public async Task InputAllowedLiveFailureReturnsFailedDecision()
    {
        TestContext context = CreateContext(
            inputService: new FakeInputService((_, _, _) => Task.FromResult(CreateInputDispatchFailureResult())),
            attachedWindow: CreateWindow());

        CallToolResult result = await context.Tools.Input(CreateConfirmedClickRequest());

        AssertFailedPayload(result, InputFailureCodeValues.InputDispatchFailed, assertFailedDecision: true);
        Assert.Equal(1, context.InputService.Calls);
    }

    [Fact]
    public async Task InputAllowedLiveUnexpectedServiceFailureReturnsGenericFailedPayload()
    {
        TestContext context = CreateContext(
            inputService: new FakeInputService((_, _, _) => throw new InvalidOperationException("boom")),
            attachedWindow: CreateWindow());

        CallToolResult result = await context.Tools.Input(CreateConfirmedClickRequest());

        JsonElement payload = AssertFailedPayload(result, assertFailedDecision: true);
        Assert.False(payload.TryGetProperty("failureCode", out _));
        Assert.Equal(1, context.InputService.Calls);
    }

    [Fact]
    public async Task InputAllowedLiveFactualRuntimeExceptionPreservesExceptionMetadataAndPayload()
    {
        TestContext context = CreateContext(
            inputService: new FakeInputService((_, _, _) => throw CreateRuntimeFailureException()),
            attachedWindow: CreateWindow());

        CallToolResult result = await context.Tools.Input(CreateConfirmedClickRequest());

        JsonElement payload = AssertFailedPayload(result, InputFailureCodeValues.InputDispatchFailed);
        Assert.Equal(0, payload.GetProperty("failedActionIndex").GetInt32());

        string completedEvent = ReadSingleAuditEvent(context, ToolInvocationCompletedEventName);
        Assert.Contains("\"exception_type\":\"System.InvalidOperationException\"", completedEvent, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretRuntimeFailureMessage, completedEvent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InputAllowedLiveFactualRuntimeExceptionReturnsPayloadWhenAuditCompletionFails()
    {
        AuditLogOptions? auditOptions = null;
        FakeInputService inputService = new((_, _, _) =>
        {
            File.Delete(auditOptions!.SummaryPath);
            Directory.CreateDirectory(auditOptions.SummaryPath);
            throw CreateRuntimeFailureException();
        });
        TestContext context = CreateContext(inputService: inputService, attachedWindow: CreateWindow());
        auditOptions = context.AuditOptions;

        CallToolResult result = await context.Tools.Input(CreateConfirmedClickRequest());

        JsonElement payload = AssertFailedPayload(result, InputFailureCodeValues.InputDispatchFailed);
        Assert.Equal(0, payload.GetProperty("failedActionIndex").GetInt32());
        Assert.Equal(1, context.InputService.Calls);
    }

    [Fact]
    public async Task InputStartedAuditSummaryDoesNotExposeKeyboardLikeRejectedPayload()
    {
        TestContext context = CreateContext();

        CallToolResult result = await context.Tools.Input(CreateInputRequest(new InputAction
        {
            Type = InputActionTypeValues.Keypress,
            Key = "Ctrl+V",
        }));

        AssertFailedPayload(result, InputFailureCodeValues.UnsupportedActionType);
        Assert.Equal(0, context.InputService.Calls);

        string startedEvent = ReadSingleAuditEvent(context, ToolInvocationStartedEventName);
        Assert.Contains("\"request_summary\":", startedEvent, StringComparison.Ordinal);
        Assert.DoesNotContain("Ctrl+V", startedEvent, StringComparison.Ordinal);
        Assert.DoesNotContain("\"key\"", startedEvent, StringComparison.Ordinal);
        Assert.DoesNotContain("\"keys\"", startedEvent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InputStartedAuditSummaryDoesNotExposeNestedRejectedPayload()
    {
        TestContext context = CreateContext();
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

        AssertFailedPayload(result, InputFailureCodeValues.InvalidRequest);
        Assert.Equal(0, context.InputService.Calls);

        string startedEvent = ReadSingleAuditEvent(context, ToolInvocationStartedEventName);
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
        TestContext context = CreateContext();

        CallToolResult result = await context.Tools.Input(CreateInputRequest(
            new InputAction { Type = "secret-type-token" },
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
            }));

        AssertFailedPayload(result, InputFailureCodeValues.UnsupportedActionType);
        Assert.Equal(0, context.InputService.Calls);

        string startedEvent = ReadSingleAuditEvent(context, ToolInvocationStartedEventName);
        Assert.Contains("\"request_summary\":", startedEvent, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-type-token", startedEvent, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-button-token", startedEvent, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-coordinate-token", startedEvent, StringComparison.Ordinal);
    }

    private static TestContext CreateContext(
        ToolExecutionDecision? decision = null,
        FakeInputService? inputService = null,
        WindowDescriptor? attachedWindow = null,
        IWindowTargetResolver? windowTargetResolver = null)
    {
        TestInfrastructure infrastructure = CreateInfrastructure("input-tool-tests");
        if (attachedWindow is not null)
        {
            infrastructure.SessionManager.Attach(attachedWindow, "tests");
        }

        FakeWindowManager windowManager = new(attachedWindow is null ? [] : [attachedWindow]);
        FakeToolExecutionGate gate = new((_, _) => decision ?? CreateAllowedDecision());
        FakeInputService effectiveInputService = inputService ?? new FakeInputService();

        return new TestContext(
            CreateWindowTools(infrastructure, windowManager, gate, effectiveInputService, windowTargetResolver),
            gate,
            effectiveInputService,
            infrastructure.AuditOptions);
    }

    private static TestInfrastructure CreateInfrastructure(string runId)
    {
        string root = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        string diagnosticsRoot = Path.Combine(root, "artifacts", "diagnostics");
        string runDirectory = Path.Combine(diagnosticsRoot, runId);
        AuditLogOptions options = new(
            ContentRootPath: root,
            EnvironmentName: "Tests",
            RunId: runId,
            DiagnosticsRoot: diagnosticsRoot,
            RunDirectory: runDirectory,
            EventsPath: Path.Combine(runDirectory, "events.jsonl"),
            SummaryPath: Path.Combine(runDirectory, "summary.md"));

        return new TestInfrastructure(
            new AuditLog(options, TimeProvider.System),
            new InMemorySessionManager(TimeProvider.System, new SessionContext(runId)),
            options);
    }

    private static WindowTools CreateWindowTools(
        TestInfrastructure infrastructure,
        FakeWindowManager windowManager,
        FakeToolExecutionGate gate,
        FakeInputService inputService,
        IWindowTargetResolver? windowTargetResolver = null) =>
        new(
            infrastructure.AuditLog,
            infrastructure.SessionManager,
            windowManager,
            new NoopCaptureService(),
            new FakeMonitorManager(),
            new FakeWindowActivationService(),
            windowTargetResolver ?? new WindowTargetResolver(windowManager),
            new FakeUiAutomationService(),
            new FakeWaitService(),
            new WaitResultMaterializer(infrastructure.AuditLog, infrastructure.AuditOptions, WaitOptions.Default),
            gate,
            inputService,
            new FakeProcessLaunchService(),
            new FakeOpenTargetService());

    private static ToolExecutionDecision CreateAllowedDecision() =>
        CreateInputDecision(ToolExecutionDecisionKind.Allowed, GuardSeverityValues.Warning);

    private static ToolExecutionDecision CreateBlockedDecision() =>
        CreateInputDecision(ToolExecutionDecisionKind.Blocked, GuardSeverityValues.Blocked);

    private static ToolExecutionDecision CreateNeedsConfirmationDecision() =>
        CreateInputDecision(
            ToolExecutionDecisionKind.NeedsConfirmation,
            GuardSeverityValues.Warning,
            requiresConfirmation: true);

    private static ToolExecutionDecision CreateInputDecision(
        ToolExecutionDecisionKind kind,
        string severity,
        bool requiresConfirmation = false) =>
        new(
            Kind: kind,
            Mode: ToolExecutionMode.Live,
            RiskLevel: ToolExecutionRiskLevel.Destructive,
            Reasons:
            [
                new GuardReason(
                    GuardReasonCodeValues.InputUipiBarrierPresent,
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

    private static JsonElement AssertGateRejectedWithoutRuntime(
        CallToolResult result,
        TestContext context,
        string expectedStatus,
        WindowDescriptor expectedWindow)
    {
        JsonElement payload = AssertErrorPayload(result, expectedStatus, assertDecision: true);
        Assert.Equal(expectedWindow.Hwnd, payload.GetProperty("targetHwnd").GetInt64());
        Assert.Equal(0, context.InputService.Calls);
        Assert.Equal(1, context.Gate.Calls);
        AssertRuntimeCompletionWasNotAudited(context);
        return payload;
    }

    private static JsonElement AssertPreGateFailure(
        CallToolResult result,
        TestContext context,
        string expectedFailureCode,
        bool assertFailedDecision = false)
    {
        JsonElement payload = AssertFailedPayload(result, expectedFailureCode, assertFailedDecision);
        Assert.Equal(0, context.InputService.Calls);
        Assert.Equal(0, context.Gate.Calls);
        return payload;
    }

    private static JsonElement AssertFailedPayload(
        CallToolResult result,
        string? expectedFailureCode = null,
        bool assertFailedDecision = false)
    {
        JsonElement payload = AssertErrorPayload(result, InputStatusValues.Failed, assertFailedDecision);
        if (expectedFailureCode is not null)
        {
            Assert.Equal(expectedFailureCode, payload.GetProperty("failureCode").GetString());
        }

        return payload;
    }

    private static JsonElement AssertErrorPayload(CallToolResult result, string expectedStatus, bool assertDecision = false)
    {
        JsonElement payload = AssertStructuredPayload(result);
        Assert.True(result.IsError);
        Assert.Equal(expectedStatus, payload.GetProperty("status").GetString());
        if (assertDecision)
        {
            Assert.Equal(expectedStatus, payload.GetProperty("decision").GetString());
        }

        return payload;
    }

    private static void AssertRuntimeCompletionWasNotAudited(TestContext context) =>
        Assert.DoesNotContain(
            ReadAuditLines(context),
            line => line.Contains(AuditEventNeedle(RuntimeCompletedEventName), StringComparison.Ordinal));

    private static string ReadSingleAuditEvent(TestContext context, string eventName) =>
        ReadAuditLines(context).Single(line => line.Contains(AuditEventNeedle(eventName), StringComparison.Ordinal));

    private static IEnumerable<string> ReadAuditLines(TestContext context) =>
        File.ReadLines(context.AuditOptions.EventsPath);

    private static string AuditEventNeedle(string eventName) =>
        $"\"event_name\":\"{eventName}\"";

    private static InputRequest CreateClickRequest() =>
        new()
        {
            Actions = [CreateClickAction()],
        };

    private static InputRequest CreateConfirmedClickRequest() =>
        new()
        {
            Actions = [CreateClickAction()],
            Confirm = true,
        };

    private static InputRequest CreateClickRequest(long hwnd) =>
        new()
        {
            Hwnd = hwnd,
            Actions = [CreateClickAction()],
        };

    private static InputRequest CreateInputRequest(params InputAction[] actions) =>
        new()
        {
            Actions = actions,
        };

    private static InputAction CreateClickAction() =>
        new()
        {
            Type = InputActionTypeValues.Click,
            CoordinateSpace = InputCoordinateSpaceValues.Screen,
            Point = new InputPoint(100, 100),
        };

    private static InputAction CreateMoveAction(int offset) =>
        new()
        {
            Type = InputActionTypeValues.Move,
            CoordinateSpace = InputCoordinateSpaceValues.Screen,
            Point = new InputPoint(100 + offset, 100 + offset),
        };

    private static InputResult CreateVerifyNeededClickResult(long? targetHwnd, string artifactPath) =>
        new(
            Status: InputStatusValues.VerifyNeeded,
            Decision: InputStatusValues.VerifyNeeded,
            ResultMode: InputResultModeValues.DispatchOnly,
            TargetHwnd: targetHwnd,
            TargetSource: InputTargetSourceValues.Attached,
            CompletedActionCount: 1,
            Actions: [CreateVerifyNeededClickActionResult()],
            ArtifactPath: artifactPath);

    private static InputResult CreateVerifyNeededAttachedResult(long? targetHwnd) =>
        new(
            Status: InputStatusValues.VerifyNeeded,
            Decision: InputStatusValues.VerifyNeeded,
            ResultMode: InputResultModeValues.DispatchOnly,
            TargetHwnd: targetHwnd,
            TargetSource: InputTargetSourceValues.Attached);

    private static InputResult CreateInputDispatchFailureResult() =>
        new(
            Status: InputStatusValues.Failed,
            Decision: InputStatusValues.Failed,
            FailureCode: InputFailureCodeValues.InputDispatchFailed,
            Reason: "Input dispatch failed.");

    private static InputExecutionFailureException CreateRuntimeFailureException() =>
        new(CreateCommittedInputFailureResult(), new InvalidOperationException(SecretRuntimeFailureMessage));

    private static InputResult CreateCommittedInputFailureResult() =>
        new(
            Status: InputStatusValues.Failed,
            Decision: InputStatusValues.Failed,
            FailureCode: InputFailureCodeValues.InputDispatchFailed,
            Reason: CommittedInputFailureReason,
            TargetHwnd: 101,
            TargetSource: InputTargetSourceValues.Attached,
            CompletedActionCount: 0,
            FailedActionIndex: 0,
            Actions: [CreateFailedClickActionResult()]);

    private static InputActionResult CreateVerifyNeededClickActionResult() =>
        new(
            Type: InputActionTypeValues.Click,
            Status: InputStatusValues.VerifyNeeded,
            ResultMode: InputResultModeValues.DispatchOnly,
            CoordinateSpace: InputCoordinateSpaceValues.Screen,
            RequestedPoint: new InputPoint(100, 100),
            ResolvedScreenPoint: new InputPoint(100, 100),
            Button: InputButtonValues.Left);

    private static InputActionResult CreateFailedClickActionResult() =>
        new(
            Type: InputActionTypeValues.Click,
            Status: InputStatusValues.Failed,
            FailureCode: InputFailureCodeValues.InputDispatchFailed,
            Reason: CommittedInputFailureReason,
            CoordinateSpace: InputCoordinateSpaceValues.Screen,
            RequestedPoint: new InputPoint(100, 100),
            ResolvedScreenPoint: new InputPoint(100, 100),
            Button: InputButtonValues.Left);

    private static string CreateArtifactPath(string fileName) =>
        Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"), fileName);

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

    private sealed record TestInfrastructure(
        AuditLog AuditLog,
        InMemorySessionManager SessionManager,
        AuditLogOptions AuditOptions);

    private sealed class FakeWindowManager(IReadOnlyList<WindowDescriptor> windows) : IWindowManager
    {
        private readonly IReadOnlyList<WindowDescriptor> visibleWindows = windows.Where(window => window.IsVisible).ToArray();

        public IReadOnlyList<WindowDescriptor> ListWindows(bool includeInvisible) =>
            includeInvisible ? windows : visibleWindows;

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

        public bool TryFocus(long hwnd) =>
            windows.Any(window => window.Hwnd == hwnd);
    }

    private sealed class ThrowingWindowTargetResolver(Exception exception) : IWindowTargetResolver
    {
        public WindowDescriptor? ResolveExplicitOrAttachedWindow(long? explicitHwnd, WindowDescriptor? attachedWindow) =>
            throw exception;

        public LiveWindowIdentityResolution ResolveLiveWindowByIdentity(WindowDescriptor expectedWindow) =>
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
