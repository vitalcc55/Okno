using System.Text.Json;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Windows.Input;

namespace WinBridge.Runtime.Tests;

public sealed class InputResultMaterializerTests
{
    [Fact]
    public void MaterializeWritesInputArtifactAndRuntimeEventForVerifyNeededResult()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateOptions(root, "run-input-materialize");
        AuditLog auditLog = new(options, TimeProvider.System);
        FakeTimeProvider timeProvider = new(new DateTimeOffset(2026, 4, 17, 10, 0, 0, TimeSpan.Zero));
        InputResultMaterializer materializer = new(auditLog, options, timeProvider);
        InputRequest request = CreateRequest();

        InputResult result = materializer.Materialize(
            request,
            new InputExecutionContext(CreateWindow(hwnd: 4242)),
            new InputResult(
                Status: InputStatusValues.VerifyNeeded,
                Decision: InputStatusValues.VerifyNeeded,
                ResultMode: InputResultModeValues.DispatchOnly,
                TargetHwnd: 4242,
                TargetSource: InputTargetSourceValues.Attached,
                CompletedActionCount: 1,
                Actions:
                [
                    new InputActionResult(
                        Type: InputActionTypeValues.Click,
                        Status: InputStatusValues.VerifyNeeded,
                        ResultMode: InputResultModeValues.DispatchOnly,
                        CoordinateSpace: InputCoordinateSpaceValues.Screen,
                        RequestedPoint: new InputPoint(140, 260),
                        ResolvedScreenPoint: new InputPoint(140, 260),
                        Button: InputButtonValues.Left),
                ]));

        Assert.NotNull(result.ArtifactPath);
        Assert.Contains($"{Path.DirectorySeparatorChar}input{Path.DirectorySeparatorChar}input-", result.ArtifactPath, StringComparison.Ordinal);
        Assert.True(File.Exists(result.ArtifactPath));

        using JsonDocument artifact = JsonDocument.Parse(File.ReadAllText(result.ArtifactPath));
        JsonElement payload = artifact.RootElement;
        JsonElement requestSummary = payload.GetProperty("request_summary");
        Assert.Equal(1, requestSummary.GetProperty("action_count").GetInt32());
        Assert.Equal(InputActionTypeValues.Click, requestSummary.GetProperty("action_types")[0].GetString());
        Assert.Equal(InputCoordinateSpaceValues.Screen, requestSummary.GetProperty("coordinate_spaces")[0].GetString());
        Assert.Equal(4242, requestSummary.GetProperty("target_hwnd").GetInt64());
        Assert.Equal(InputTargetSourceValues.Attached, requestSummary.GetProperty("target_source").GetString());

        JsonElement artifactResult = payload.GetProperty("result");
        Assert.Equal(InputStatusValues.VerifyNeeded, artifactResult.GetProperty("status").GetString());
        Assert.Equal(result.ArtifactPath, artifactResult.GetProperty("artifact_path").GetString());
        Assert.Equal("completed_actions_committed", artifactResult.GetProperty("committed_side_effect_evidence").GetString());
        Assert.Equal(140, artifactResult.GetProperty("actions")[0].GetProperty("resolved_screen_point").GetProperty("x").GetInt32());
        Assert.False(artifactResult.GetProperty("actions")[0].TryGetProperty("keys", out _));

        string artifactText = File.ReadAllText(result.ArtifactPath);
        Assert.DoesNotContain("\"text\"", artifactText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"key\"", artifactText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", artifactText, StringComparison.OrdinalIgnoreCase);

        string eventLine = Assert.Single(File.ReadAllLines(options.EventsPath));
        Assert.Contains("\"event_name\":\"input.runtime.completed\"", eventLine, StringComparison.Ordinal);
        Assert.Contains("\"tool_name\":\"windows.input\"", eventLine, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"verify_needed\"", eventLine, StringComparison.Ordinal);
        Assert.Contains("\"action_types\":\"click\"", eventLine, StringComparison.Ordinal);
        Assert.Contains("\"coordinate_spaces\":\"screen\"", eventLine, StringComparison.Ordinal);
        Assert.Contains("\"committed_side_effect_evidence\":\"completed_actions_committed\"", eventLine, StringComparison.Ordinal);
    }

    [Fact]
    public void MaterializePreservesPartialDispatchEvidence()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateOptions(root, "run-input-partial-dispatch");
        AuditLog auditLog = new(options, TimeProvider.System);
        InputResultMaterializer materializer = new(auditLog, options, TimeProvider.System);

        InputResult result = materializer.Materialize(
            CreateRequest(),
            new InputExecutionContext(CreateWindow(hwnd: 4242)),
            new InputResult(
                Status: InputStatusValues.Failed,
                Decision: InputStatusValues.Failed,
                FailureCode: InputFailureCodeValues.InputDispatchFailed,
                Reason: "Button dispatch was partial; secret exception text must not be used.",
                TargetHwnd: 4242,
                TargetSource: InputTargetSourceValues.Attached,
                CompletedActionCount: 0,
                FailedActionIndex: 0,
                Actions:
                [
                    new InputActionResult(
                        Type: InputActionTypeValues.Click,
                        Status: InputStatusValues.Failed,
                        FailureCode: InputFailureCodeValues.InputDispatchFailed,
                        CoordinateSpace: InputCoordinateSpaceValues.Screen,
                        RequestedPoint: new InputPoint(140, 260),
                        ResolvedScreenPoint: new InputPoint(140, 260),
                        Button: InputButtonValues.Left),
                ]),
            failureStage: InputFailureStageValues.ClickDispatchPartialUncompensated);

        Assert.NotNull(result.ArtifactPath);
        using JsonDocument artifact = JsonDocument.Parse(File.ReadAllText(result.ArtifactPath));
        JsonElement artifactResult = artifact.RootElement.GetProperty("result");
        Assert.Equal(0, artifactResult.GetProperty("failed_action_index").GetInt32());
        Assert.Equal(InputFailureCodeValues.InputDispatchFailed, artifactResult.GetProperty("failure_code").GetString());
        Assert.Equal("partial_dispatch_uncompensated", artifactResult.GetProperty("committed_side_effect_evidence").GetString());
        Assert.Equal(InputFailureStageValues.ClickDispatchPartialUncompensated, artifact.RootElement.GetProperty("failure_diagnostics").GetProperty("failure_stage").GetString());

        string eventLine = Assert.Single(File.ReadAllLines(options.EventsPath));
        Assert.Contains("\"failure_stage\":\"click_dispatch_partial_uncompensated\"", eventLine, StringComparison.Ordinal);
        Assert.Contains("\"committed_side_effect_evidence\":\"partial_dispatch_uncompensated\"", eventLine, StringComparison.Ordinal);
    }

    [Fact]
    public void MaterializePreservesCommittedTextDispatchEvidence()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateOptions(root, "run-input-text-dispatch");
        AuditLog auditLog = new(options, TimeProvider.System);
        InputResultMaterializer materializer = new(auditLog, options, TimeProvider.System);

        InputResult result = materializer.Materialize(
            new InputRequest
            {
                Hwnd = 4242,
                Actions =
                [
                    new InputAction
                    {
                        Type = InputActionTypeValues.Type,
                        Text = "typed text",
                    },
                ],
            },
            new InputExecutionContext(CreateWindow(hwnd: 4242)),
            new InputResult(
                Status: InputStatusValues.Failed,
                Decision: InputStatusValues.Failed,
                FailureCode: InputFailureCodeValues.InputDispatchFailed,
                Reason: "text dispatch failed after partial send",
                TargetHwnd: 4242,
                TargetSource: InputTargetSourceValues.Attached,
                CompletedActionCount: 0,
                FailedActionIndex: 0,
                Actions:
                [
                    new InputActionResult(
                        Type: InputActionTypeValues.Type,
                        Status: InputStatusValues.Failed,
                        FailureCode: InputFailureCodeValues.InputDispatchFailed),
                ]),
            failureStage: InputFailureStageValues.TextDispatchCommittedFailure);

        Assert.NotNull(result.ArtifactPath);
        using JsonDocument artifact = JsonDocument.Parse(File.ReadAllText(result.ArtifactPath));
        JsonElement artifactResult = artifact.RootElement.GetProperty("result");
        Assert.Equal("text_dispatch_committed_before_failure", artifactResult.GetProperty("committed_side_effect_evidence").GetString());
        Assert.Equal(InputFailureStageValues.TextDispatchCommittedFailure, artifact.RootElement.GetProperty("failure_diagnostics").GetProperty("failure_stage").GetString());

        string eventLine = Assert.Single(File.ReadAllLines(options.EventsPath));
        Assert.Contains("\"failure_stage\":\"text_dispatch_committed_failure\"", eventLine, StringComparison.Ordinal);
        Assert.Contains("\"committed_side_effect_evidence\":\"text_dispatch_committed_before_failure\"", eventLine, StringComparison.Ordinal);
    }

    [Fact]
    public void MaterializePreservesCommittedKeypressDispatchEvidence()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateOptions(root, "run-input-keypress-dispatch");
        AuditLog auditLog = new(options, TimeProvider.System);
        InputResultMaterializer materializer = new(auditLog, options, TimeProvider.System);

        InputResult result = materializer.Materialize(
            new InputRequest
            {
                Hwnd = 4242,
                Actions =
                [
                    new InputAction
                    {
                        Type = InputActionTypeValues.Keypress,
                        Key = "ctrl+s",
                    },
                ],
            },
            new InputExecutionContext(CreateWindow(hwnd: 4242)),
            new InputResult(
                Status: InputStatusValues.Failed,
                Decision: InputStatusValues.Failed,
                FailureCode: InputFailureCodeValues.InputDispatchFailed,
                Reason: "keypress dispatch failed after partial send",
                TargetHwnd: 4242,
                TargetSource: InputTargetSourceValues.Attached,
                CompletedActionCount: 0,
                FailedActionIndex: 0,
                Actions:
                [
                    new InputActionResult(
                        Type: InputActionTypeValues.Keypress,
                        Status: InputStatusValues.Failed,
                        FailureCode: InputFailureCodeValues.InputDispatchFailed),
                ]),
            failureStage: InputFailureStageValues.KeypressDispatchCommittedFailure);

        Assert.NotNull(result.ArtifactPath);
        using JsonDocument artifact = JsonDocument.Parse(File.ReadAllText(result.ArtifactPath));
        JsonElement artifactResult = artifact.RootElement.GetProperty("result");
        Assert.Equal("keyboard_dispatch_committed_before_failure", artifactResult.GetProperty("committed_side_effect_evidence").GetString());
        Assert.Equal(InputFailureStageValues.KeypressDispatchCommittedFailure, artifact.RootElement.GetProperty("failure_diagnostics").GetProperty("failure_stage").GetString());

        string eventLine = Assert.Single(File.ReadAllLines(options.EventsPath));
        Assert.Contains("\"failure_stage\":\"keypress_dispatch_committed_failure\"", eventLine, StringComparison.Ordinal);
        Assert.Contains("\"committed_side_effect_evidence\":\"keyboard_dispatch_committed_before_failure\"", eventLine, StringComparison.Ordinal);
    }

    [Fact]
    public void MaterializeRedactsDragCoordinatesAndSuppressesRuntimeArtifactLink()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateOptions(root, "run-input-drag-redaction");
        AuditLog auditLog = new(options, TimeProvider.System);
        InputResultMaterializer materializer = new(auditLog, options, TimeProvider.System);
        InputRequest request = new()
        {
            Hwnd = 4242,
            Actions =
            [
                new InputAction
                {
                    Type = InputActionTypeValues.Drag,
                    CoordinateSpace = InputCoordinateSpaceValues.Screen,
                    Point = new InputPoint(111, 222),
                    Path =
                    [
                        new InputPoint(111, 222),
                        new InputPoint(333, 444),
                    ],
                },
            ],
        };

        InputResult result = materializer.Materialize(
            request,
            new InputExecutionContext(CreateWindow(hwnd: 4242)),
            new InputResult(
                Status: InputStatusValues.VerifyNeeded,
                Decision: InputStatusValues.VerifyNeeded,
                ResultMode: InputResultModeValues.DispatchOnly,
                TargetHwnd: 4242,
                TargetSource: InputTargetSourceValues.Attached,
                CompletedActionCount: 1,
                Actions:
                [
                    new InputActionResult(
                        Type: InputActionTypeValues.Drag,
                        Status: InputStatusValues.VerifyNeeded,
                        ResultMode: InputResultModeValues.DispatchOnly,
                        CoordinateSpace: InputCoordinateSpaceValues.Screen,
                        RequestedPoint: new InputPoint(111, 222),
                        ResolvedScreenPoint: new InputPoint(333, 444),
                        Reason: "Cursor drifted between (111,222) and (333,444)."),
                ]));

        Assert.NotNull(result.ArtifactPath);
        using JsonDocument artifact = JsonDocument.Parse(File.ReadAllText(result.ArtifactPath));
        JsonElement action = artifact.RootElement.GetProperty("result").GetProperty("actions")[0];
        Assert.False(action.TryGetProperty("requested_point", out _));
        Assert.False(action.TryGetProperty("resolved_screen_point", out _));
        Assert.Equal("Runtime drag завершился без безопасного раскрытия coordinate detail.", action.GetProperty("reason").GetString());

        string eventLine = Assert.Single(File.ReadAllLines(options.EventsPath));
        Assert.Contains("\"event_name\":\"input.runtime.completed\"", eventLine, StringComparison.Ordinal);
        Assert.DoesNotContain("\"artifact_path\"", eventLine, StringComparison.Ordinal);
    }

    [Fact]
    public void MaterializeReturnsFactualResultWhenArtifactWriteFails()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateOptions(root, "run-input-artifact-failure");
        AuditLog auditLog = new(options, TimeProvider.System);
        string blockedRunDirectory = Path.Combine(root, "blocked-parent", "artifact.json");
        Directory.CreateDirectory(Path.GetDirectoryName(blockedRunDirectory)!);
        File.WriteAllText(blockedRunDirectory, "not-a-directory");
        InputArtifactWriter writer = new(new AuditLogOptions(
            options.ContentRootPath,
            options.EnvironmentName,
            options.RunId,
            options.DiagnosticsRoot,
            blockedRunDirectory,
            options.EventsPath,
            options.SummaryPath));
        InputResultMaterializer materializer = new(writer, auditLog, TimeProvider.System);

        InputResult result = materializer.Materialize(
            CreateRequest(),
            new InputExecutionContext(CreateWindow(hwnd: 4242)),
            new InputResult(
                Status: InputStatusValues.VerifyNeeded,
                Decision: InputStatusValues.VerifyNeeded,
                ResultMode: InputResultModeValues.DispatchOnly,
                TargetHwnd: 4242,
                TargetSource: InputTargetSourceValues.Attached,
                CompletedActionCount: 1));

        Assert.Equal(InputStatusValues.VerifyNeeded, result.Status);
        Assert.Equal(InputResultModeValues.DispatchOnly, result.ResultMode);
        Assert.Null(result.ArtifactPath);

        string eventLine = Assert.Single(File.ReadAllLines(options.EventsPath));
        Assert.Contains("\"event_name\":\"input.runtime.completed\"", eventLine, StringComparison.Ordinal);
        Assert.Contains("\"failure_stage\":\"artifact_write\"", eventLine, StringComparison.Ordinal);
        Assert.Contains("\"exception_type\":", eventLine, StringComparison.Ordinal);
        Assert.DoesNotContain("exception_message", eventLine, StringComparison.Ordinal);
    }

    [Fact]
    public void MaterializePreservesFactualResultWhenRuntimeEventWriteFails()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateOptions(root, "run-input-event-failure");
        AuditLog auditLog = new(options, TimeProvider.System);
        InputResultMaterializer materializer = new(auditLog, options, TimeProvider.System);
        File.Delete(options.EventsPath);
        Directory.CreateDirectory(options.EventsPath);

        InputResult result = materializer.Materialize(
            CreateRequest(),
            new InputExecutionContext(CreateWindow(hwnd: 4242)),
            new InputResult(
                Status: InputStatusValues.VerifyNeeded,
                Decision: InputStatusValues.VerifyNeeded,
                ResultMode: InputResultModeValues.DispatchOnly,
                TargetHwnd: 4242,
                TargetSource: InputTargetSourceValues.Attached,
                CompletedActionCount: 1));

        Assert.Equal(InputStatusValues.VerifyNeeded, result.Status);
        Assert.Equal(InputResultModeValues.DispatchOnly, result.ResultMode);
        Assert.NotNull(result.ArtifactPath);
        Assert.True(File.Exists(result.ArtifactPath));
        Assert.True(Directory.Exists(options.EventsPath));
    }

    private static InputRequest CreateRequest() =>
        new()
        {
            Hwnd = 4242,
            Actions =
            [
                new InputAction
                {
                    Type = InputActionTypeValues.Click,
                    CoordinateSpace = InputCoordinateSpaceValues.Screen,
                    Point = new InputPoint(140, 260),
                    Button = InputButtonValues.Left,
                },
            ],
        };

    private static WindowDescriptor CreateWindow(long hwnd) =>
        new(
            hwnd,
            "Input Test",
            "test.exe",
            1001,
            2002,
            "TestWindow",
            new Bounds(100, 200, 500, 700),
            IsForeground: true,
            IsVisible: true);

    private static AuditLogOptions CreateOptions(string root, string runId) =>
        new(
            ContentRootPath: root,
            EnvironmentName: "Tests",
            RunId: runId,
            DiagnosticsRoot: Path.Combine(root, "artifacts", "diagnostics"),
            RunDirectory: Path.Combine(root, "artifacts", "diagnostics", runId),
            EventsPath: Path.Combine(root, "artifacts", "diagnostics", runId, "events.jsonl"),
            SummaryPath: Path.Combine(root, "artifacts", "diagnostics", runId, "summary.md"));

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FakeTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
