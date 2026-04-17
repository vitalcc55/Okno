using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Windows.Input;
using WinBridge.Runtime.Windows.Shell;

namespace WinBridge.Runtime.Tests;

public sealed class Win32InputServiceTests
{
    [Fact]
    public async Task ExecuteAsyncReturnsVerifyNeededForMoveWhenCursorVerificationSucceeds()
    {
        WindowDescriptor targetWindow = CreateWindow();
        FakeWindowTargetResolver resolver = new(
            new InputTargetResolution(targetWindow, InputTargetSourceValues.Explicit),
            [targetWindow]);
        FakeInputPlatform platform = new()
        {
            CurrentProcessSecurity = CreateCurrentProcessSecurity(),
            TargetSecurity = CreateTargetSecurity(),
        };
        Win32InputService service = new(resolver, platform, TimeProvider.System);

        InputResult result = await service.ExecuteAsync(
            new InputRequest
            {
                Hwnd = targetWindow.Hwnd,
                Actions =
                [
                    CreateAction(InputActionTypeValues.Move, InputCoordinateSpaceValues.Screen, new InputPoint(140, 260)),
                ],
            },
            new InputExecutionContext(),
            CancellationToken.None);

        Assert.Equal(InputStatusValues.VerifyNeeded, result.Status);
        Assert.Equal(InputStatusValues.VerifyNeeded, result.Decision);
        Assert.Equal(InputResultModeValues.DispatchOnly, result.ResultMode);
        Assert.Equal(1, result.CompletedActionCount);
        Assert.Null(result.FailedActionIndex);
        Assert.Equal(new InputPoint(140, 260), Assert.Single(platform.MovedPoints));
        InputActionResult actionResult = Assert.Single(result.Actions!);
        Assert.Equal(InputStatusValues.VerifyNeeded, actionResult.Status);
        Assert.Equal(new InputPoint(140, 260), actionResult.ResolvedScreenPoint);
    }

    [Fact]
    public async Task ExecuteAsyncMaterializesArtifactWhenMaterializerIsProvided()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateAuditLogOptions(root, "run-input-service-materialized");
        AuditLog auditLog = new(options, TimeProvider.System);
        InputResultMaterializer materializer = new(auditLog, options, TimeProvider.System);
        WindowDescriptor targetWindow = CreateWindow();
        FakeWindowTargetResolver resolver = new(
            new InputTargetResolution(targetWindow, InputTargetSourceValues.Explicit),
            [targetWindow]);
        FakeInputPlatform platform = new()
        {
            CurrentProcessSecurity = CreateCurrentProcessSecurity(),
            TargetSecurity = CreateTargetSecurity(),
        };
        Win32InputService service = new(resolver, platform, TimeProvider.System, materializer);

        InputResult result = await service.ExecuteAsync(
            new InputRequest
            {
                Hwnd = targetWindow.Hwnd,
                Actions =
                [
                    CreateAction(InputActionTypeValues.Click, InputCoordinateSpaceValues.Screen, new InputPoint(140, 260)),
                ],
            },
            new InputExecutionContext(),
            CancellationToken.None);

        Assert.Equal(InputStatusValues.VerifyNeeded, result.Status);
        Assert.NotNull(result.ArtifactPath);
        Assert.True(File.Exists(result.ArtifactPath));
        string eventLine = Assert.Single(File.ReadAllLines(options.EventsPath));
        Assert.Contains("\"event_name\":\"input.runtime.completed\"", eventLine, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsyncPreservesPartialDispatchEvidenceInArtifact()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateAuditLogOptions(root, "run-input-service-partial-dispatch");
        AuditLog auditLog = new(options, TimeProvider.System);
        InputResultMaterializer materializer = new(auditLog, options, TimeProvider.System);
        WindowDescriptor targetWindow = CreateWindow();
        FakeWindowTargetResolver resolver = new(
            new InputTargetResolution(targetWindow, InputTargetSourceValues.Explicit),
            [targetWindow, targetWindow]);
        FakeInputPlatform platform = new()
        {
            CurrentProcessSecurity = CreateCurrentProcessSecurity(),
            TargetSecurity = CreateTargetSecurity(),
            ClickDispatchResults =
            [
                new InputClickDispatchResult(
                    Success: false,
                    OutcomeKind: InputClickDispatchOutcomeKind.PartialDispatchUncompensated,
                    FailureCode: InputFailureCodeValues.InputDispatchFailed,
                    Reason: "Partial dispatch with already committed events."),
            ],
        };
        Win32InputService service = new(resolver, platform, TimeProvider.System, materializer);

        InputResult result = await service.ExecuteAsync(
            new InputRequest
            {
                Hwnd = targetWindow.Hwnd,
                Actions =
                [
                    CreateAction(InputActionTypeValues.Click, InputCoordinateSpaceValues.Screen, new InputPoint(140, 260)),
                ],
            },
            new InputExecutionContext(),
            CancellationToken.None);

        Assert.Equal(InputStatusValues.Failed, result.Status);
        Assert.Equal(InputFailureCodeValues.InputDispatchFailed, result.FailureCode);
        Assert.Equal(0, result.FailedActionIndex);
        Assert.NotNull(result.ArtifactPath);
        using JsonDocument artifact = JsonDocument.Parse(await File.ReadAllTextAsync(result.ArtifactPath));
        JsonElement rootElement = artifact.RootElement;
        Assert.Equal(
            InputFailureStageValues.ClickDispatchPartialUncompensated,
            rootElement.GetProperty("failure_diagnostics").GetProperty("failure_stage").GetString());
        Assert.Equal(
            "partial_dispatch_uncompensated",
            rootElement.GetProperty("result").GetProperty("committed_side_effect_evidence").GetString());
    }

    [Fact]
    public async Task ExecuteAsyncMaterializesRefreshedMoveCancellationStage()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateAuditLogOptions(root, "run-input-service-refreshed-move-cancel");
        AuditLog auditLog = new(options, TimeProvider.System);
        InputResultMaterializer materializer = new(auditLog, options, TimeProvider.System);
        WindowDescriptor targetWindow = CreateWindow();
        WindowDescriptor shiftedWindow = targetWindow with
        {
            Bounds = new Bounds(101, 201, 421, 561),
            IsForeground = true,
        };
        FakeWindowTargetResolver resolver = new(
            new InputTargetResolution(targetWindow, InputTargetSourceValues.Explicit),
            [targetWindow, shiftedWindow]);
        using CancellationTokenSource cancellation = new();
        FakeInputPlatform platform = new()
        {
            CurrentProcessSecurity = CreateCurrentProcessSecurity(),
            TargetSecurity = CreateTargetSecurity(),
            OnMoveSideEffect = moveCount =>
            {
                if (moveCount == 2)
                {
                    cancellation.Cancel();
                }
            },
        };
        Win32InputService service = new(resolver, platform, TimeProvider.System, materializer);

        InputResult result = await service.ExecuteAsync(
            new InputRequest
            {
                Hwnd = targetWindow.Hwnd,
                Actions =
                [
                    new InputAction
                    {
                        Type = InputActionTypeValues.Click,
                        CoordinateSpace = InputCoordinateSpaceValues.CapturePixels,
                        Point = new InputPoint(0, 0),
                        CaptureReference = new InputCaptureReference(
                            new InputBounds(100, 200, 420, 560),
                            320,
                            360,
                            96,
                            DateTimeOffset.UtcNow),
                    },
                ],
            },
            new InputExecutionContext(),
            cancellation.Token);

        Assert.Equal(InputStatusValues.Failed, result.Status);
        Assert.Equal(InputFailureCodeValues.InputDispatchFailed, result.FailureCode);
        Assert.NotNull(result.ArtifactPath);
        using JsonDocument artifact = JsonDocument.Parse(await File.ReadAllTextAsync(result.ArtifactPath));
        Assert.Equal(
            InputFailureStageValues.CancellationAfterCommittedSideEffect,
            artifact.RootElement.GetProperty("failure_diagnostics").GetProperty("failure_stage").GetString());

        string eventLine = Assert.Single(File.ReadAllLines(options.EventsPath));
        Assert.Contains("\"failure_stage\":\"cancellation_after_committed_side_effect\"", eventLine, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsyncReturnsVerifyNeededForRightClick()
    {
        WindowDescriptor targetWindow = CreateWindow();
        FakeWindowTargetResolver resolver = new(
            new InputTargetResolution(targetWindow, InputTargetSourceValues.Explicit),
            [targetWindow]);
        FakeInputPlatform platform = new()
        {
            CurrentProcessSecurity = CreateCurrentProcessSecurity(),
            TargetSecurity = CreateTargetSecurity(),
        };
        Win32InputService service = new(resolver, platform, TimeProvider.System);

        InputResult result = await service.ExecuteAsync(
            new InputRequest
            {
                Hwnd = targetWindow.Hwnd,
                Actions =
                [
                    CreateAction(
                        InputActionTypeValues.Click,
                        InputCoordinateSpaceValues.Screen,
                        new InputPoint(140, 260),
                        button: InputButtonValues.Right),
                ],
            },
            new InputExecutionContext(),
            CancellationToken.None);

        Assert.Equal(InputStatusValues.VerifyNeeded, result.Status);
        Assert.Equal([InputButtonValues.Right], platform.ClickButtons);
        InputActionResult actionResult = Assert.Single(result.Actions!);
        Assert.Equal(InputButtonValues.Right, actionResult.Button);
    }

    [Fact]
    public async Task ExecuteAsyncUsesOneMoveAndTwoClickDispatchesForDoubleClick()
    {
        WindowDescriptor targetWindow = CreateWindow();
        FakeWindowTargetResolver resolver = new(
            new InputTargetResolution(targetWindow, InputTargetSourceValues.Explicit),
            [targetWindow]);
        FakeInputPlatform platform = new()
        {
            CurrentProcessSecurity = CreateCurrentProcessSecurity(),
            TargetSecurity = CreateTargetSecurity(),
        };
        Win32InputService service = new(resolver, platform, TimeProvider.System);

        InputResult result = await service.ExecuteAsync(
            new InputRequest
            {
                Hwnd = targetWindow.Hwnd,
                Actions =
                [
                    CreateAction(InputActionTypeValues.DoubleClick, InputCoordinateSpaceValues.Screen, new InputPoint(140, 260)),
                ],
            },
            new InputExecutionContext(),
            CancellationToken.None);

        Assert.Equal(InputStatusValues.VerifyNeeded, result.Status);
        Assert.Equal([new InputPoint(140, 260)], platform.MovedPoints);
        Assert.Equal([InputButtonValues.Left, InputButtonValues.Left], platform.ClickButtons);
    }

    [Fact]
    public async Task ExecuteAsyncStopsOnFirstFailureAndMarksFailedActionIndex()
    {
        WindowDescriptor targetWindow = CreateWindow();
        FakeWindowTargetResolver resolver = new(
            new InputTargetResolution(targetWindow, InputTargetSourceValues.Explicit),
            [targetWindow, targetWindow]);
        FakeInputPlatform platform = new()
        {
            CurrentProcessSecurity = CreateCurrentProcessSecurity(),
            TargetSecurity = CreateTargetSecurity(),
            ClickResults = [true, false],
        };
        Win32InputService service = new(resolver, platform, TimeProvider.System);

        InputResult result = await service.ExecuteAsync(
            new InputRequest
            {
                Hwnd = targetWindow.Hwnd,
                Actions =
                [
                    CreateAction(InputActionTypeValues.Click, InputCoordinateSpaceValues.Screen, new InputPoint(140, 260)),
                    CreateAction(InputActionTypeValues.Click, InputCoordinateSpaceValues.Screen, new InputPoint(150, 270)),
                ],
            },
            new InputExecutionContext(),
            CancellationToken.None);

        Assert.Equal(InputStatusValues.Failed, result.Status);
        Assert.Equal(InputFailureCodeValues.InputDispatchFailed, result.FailureCode);
        Assert.Equal(1, result.CompletedActionCount);
        Assert.Equal(1, result.FailedActionIndex);
        Assert.Equal(2, result.Actions!.Count);
        Assert.Equal(InputStatusValues.Failed, result.Actions[1].Status);
        Assert.Equal([InputButtonValues.Left, InputButtonValues.Left], platform.ClickButtons);
    }

    [Fact]
    public async Task ExecuteAsyncReturnsInvalidRequestForHeldModifierKeysInClickFirstSubset()
    {
        WindowDescriptor targetWindow = CreateWindow();
        FakeWindowTargetResolver resolver = new(
            new InputTargetResolution(targetWindow, InputTargetSourceValues.Explicit),
            [targetWindow]);
        FakeInputPlatform platform = new()
        {
            CurrentProcessSecurity = CreateCurrentProcessSecurity(),
            TargetSecurity = CreateTargetSecurity(),
        };
        Win32InputService service = new(resolver, platform, TimeProvider.System);

        InputResult result = await service.ExecuteAsync(
            new InputRequest
            {
                Hwnd = targetWindow.Hwnd,
                Actions =
                [
                    new InputAction
                    {
                        Type = InputActionTypeValues.Click,
                        CoordinateSpace = InputCoordinateSpaceValues.Screen,
                        Point = new InputPoint(140, 260),
                        Keys = [InputModifierKeyValues.Ctrl],
                    },
                ],
            },
            new InputExecutionContext(),
            CancellationToken.None);

        Assert.Equal(InputStatusValues.Failed, result.Status);
        Assert.Equal(InputFailureCodeValues.InvalidRequest, result.FailureCode);
        Assert.Empty(platform.ClickButtons);
    }

    [Fact]
    public async Task ExecuteAsyncReturnsInvalidRequestForMiddleButtonInClickFirstSubset()
    {
        WindowDescriptor targetWindow = CreateWindow();
        FakeWindowTargetResolver resolver = new(
            new InputTargetResolution(targetWindow, InputTargetSourceValues.Explicit),
            [targetWindow]);
        FakeInputPlatform platform = new()
        {
            CurrentProcessSecurity = CreateCurrentProcessSecurity(),
            TargetSecurity = CreateTargetSecurity(),
        };
        Win32InputService service = new(resolver, platform, TimeProvider.System);

        InputResult result = await service.ExecuteAsync(
            new InputRequest
            {
                Hwnd = targetWindow.Hwnd,
                Actions =
                [
                    CreateAction(
                        InputActionTypeValues.Click,
                        InputCoordinateSpaceValues.Screen,
                        new InputPoint(140, 260),
                        button: InputButtonValues.Middle),
                ],
            },
            new InputExecutionContext(),
            CancellationToken.None);

        Assert.Equal(InputStatusValues.Failed, result.Status);
        Assert.Equal(InputFailureCodeValues.InvalidRequest, result.FailureCode);
        Assert.Empty(platform.ClickButtons);
    }

    [Fact]
    public async Task ExecuteAsyncRejectsUnsupportedBatchBeforeAnySideEffects()
    {
        WindowDescriptor targetWindow = CreateWindow();
        FakeWindowTargetResolver resolver = new(
            new InputTargetResolution(targetWindow, InputTargetSourceValues.Explicit),
            [targetWindow]);
        FakeInputPlatform platform = new()
        {
            CurrentProcessSecurity = CreateCurrentProcessSecurity(),
            TargetSecurity = CreateTargetSecurity(),
        };
        Win32InputService service = new(resolver, platform, TimeProvider.System);

        InputResult result = await service.ExecuteAsync(
            new InputRequest
            {
                Hwnd = targetWindow.Hwnd,
                Actions =
                [
                    CreateAction(InputActionTypeValues.Move, InputCoordinateSpaceValues.Screen, new InputPoint(140, 260)),
                    CreateAction(InputActionTypeValues.Click, InputCoordinateSpaceValues.Screen, new InputPoint(160, 280), button: InputButtonValues.Middle),
                ],
            },
            new InputExecutionContext(),
            CancellationToken.None);

        Assert.Equal(InputStatusValues.Failed, result.Status);
        Assert.Equal(InputFailureCodeValues.InvalidRequest, result.FailureCode);
        Assert.Empty(platform.MovedPoints);
        Assert.Empty(platform.ClickButtons);
        Assert.Null(result.Actions);
    }

    [Fact]
    public async Task ExecuteAsyncSerializesConcurrentBatchesAroundGlobalPointerDispatch()
    {
        WindowDescriptor targetWindow = CreateWindow();
        FakeWindowTargetResolver resolver = new(
            new InputTargetResolution(targetWindow, InputTargetSourceValues.Explicit),
            [targetWindow, targetWindow, targetWindow, targetWindow, targetWindow, targetWindow]);
        FakeInputPlatform platform = new()
        {
            CurrentProcessSecurity = CreateCurrentProcessSecurity(),
            TargetSecurity = CreateTargetSecurity(),
            BlockFirstDispatch = true,
        };
        Win32InputService service = new(resolver, platform, TimeProvider.System);

        Task<InputResult> firstTask = Task.Run(
            () => service.ExecuteAsync(
                new InputRequest
                {
                    Hwnd = targetWindow.Hwnd,
                    Actions =
                    [
                        CreateAction(InputActionTypeValues.Click, InputCoordinateSpaceValues.Screen, new InputPoint(140, 260)),
                    ],
                },
                new InputExecutionContext(),
                CancellationToken.None));

        Assert.True(platform.WaitForFirstDispatchEntered(TimeSpan.FromSeconds(2)));

        Task<InputResult> secondTask = Task.Run(
            () => service.ExecuteAsync(
                new InputRequest
                {
                    Hwnd = targetWindow.Hwnd,
                    Actions =
                    [
                        CreateAction(InputActionTypeValues.Click, InputCoordinateSpaceValues.Screen, new InputPoint(180, 300)),
                    ],
                },
                new InputExecutionContext(),
                CancellationToken.None));

        await Task.Delay(100);

        _ = Assert.Single(platform.MovedPoints);
        _ = Assert.Single(platform.ClickButtons);

        platform.ReleaseBlockedDispatch();

        InputResult[] results = await Task.WhenAll(firstTask, secondTask);

        Assert.All(results, result => Assert.Equal(InputStatusValues.VerifyNeeded, result.Status));
        Assert.Equal([new InputPoint(140, 260), new InputPoint(180, 300)], platform.MovedPoints);
    }

    [Fact]
    public async Task ExecuteAsyncRevalidatesTargetImmediatelyBeforeClickDispatch()
    {
        WindowDescriptor targetWindow = CreateWindow();
        FakeWindowTargetResolver resolver = new(
            new InputTargetResolution(targetWindow, InputTargetSourceValues.Explicit),
            [targetWindow, null]);
        FakeInputPlatform platform = new()
        {
            CurrentProcessSecurity = CreateCurrentProcessSecurity(),
            TargetSecurity = CreateTargetSecurity(),
        };
        Win32InputService service = new(resolver, platform, TimeProvider.System);

        InputResult result = await service.ExecuteAsync(
            new InputRequest
            {
                Hwnd = targetWindow.Hwnd,
                Actions =
                [
                    CreateAction(InputActionTypeValues.Click, InputCoordinateSpaceValues.Screen, new InputPoint(140, 260)),
                ],
            },
            new InputExecutionContext(),
            CancellationToken.None);

        Assert.Equal(InputStatusValues.Failed, result.Status);
        Assert.Equal(InputFailureCodeValues.StaleExplicitTarget, result.FailureCode);
        Assert.Empty(platform.ClickButtons);
    }

    [Fact]
    public async Task ExecuteAsyncRejectsCapturePixelsPlanThatTurnsStaleBeforeDispatch()
    {
        WindowDescriptor targetWindow = CreateWindow();
        WindowDescriptor shiftedWindow = targetWindow with
        {
            Bounds = new Bounds(102, 202, 422, 562),
            IsForeground = true,
        };
        FakeWindowTargetResolver resolver = new(
            new InputTargetResolution(targetWindow, InputTargetSourceValues.Explicit),
            [targetWindow, shiftedWindow]);
        FakeInputPlatform platform = new()
        {
            CurrentProcessSecurity = CreateCurrentProcessSecurity(),
            TargetSecurity = CreateTargetSecurity(),
        };
        Win32InputService service = new(resolver, platform, TimeProvider.System);

        InputResult result = await service.ExecuteAsync(
            new InputRequest
            {
                Hwnd = targetWindow.Hwnd,
                Actions =
                [
                    new InputAction
                    {
                        Type = InputActionTypeValues.Click,
                        CoordinateSpace = InputCoordinateSpaceValues.CapturePixels,
                        Point = new InputPoint(10, 20),
                        CaptureReference = new InputCaptureReference(
                            new InputBounds(100, 200, 420, 560),
                            320,
                            360,
                            96,
                            DateTimeOffset.UtcNow),
                    },
                ],
            },
            new InputExecutionContext(),
            CancellationToken.None);

        Assert.Equal(InputStatusValues.Failed, result.Status);
        Assert.Equal(InputFailureCodeValues.CaptureReferenceStale, result.FailureCode);
        Assert.Empty(platform.ClickButtons);
    }

    [Fact]
    public async Task ExecuteAsyncRefreshesCapturePixelsPointWhenOnePixelOriginDriftIsStillAdmissible()
    {
        WindowDescriptor targetWindow = CreateWindow();
        WindowDescriptor shiftedWindow = targetWindow with
        {
            Bounds = new Bounds(101, 201, 421, 561),
            IsForeground = true,
        };
        FakeWindowTargetResolver resolver = new(
            new InputTargetResolution(targetWindow, InputTargetSourceValues.Explicit),
            [targetWindow, shiftedWindow]);
        FakeInputPlatform platform = new()
        {
            CurrentProcessSecurity = CreateCurrentProcessSecurity(),
            TargetSecurity = CreateTargetSecurity(),
        };
        Win32InputService service = new(resolver, platform, TimeProvider.System);

        InputResult result = await service.ExecuteAsync(
            new InputRequest
            {
                Hwnd = targetWindow.Hwnd,
                Actions =
                [
                    new InputAction
                    {
                        Type = InputActionTypeValues.Click,
                        CoordinateSpace = InputCoordinateSpaceValues.CapturePixels,
                        Point = new InputPoint(0, 0),
                        CaptureReference = new InputCaptureReference(
                            new InputBounds(100, 200, 420, 560),
                            320,
                            360,
                            96,
                            DateTimeOffset.UtcNow),
                    },
                ],
            },
            new InputExecutionContext(),
            CancellationToken.None);

        Assert.Equal(InputStatusValues.VerifyNeeded, result.Status);
        Assert.Equal([new InputPoint(100, 200), new InputPoint(101, 201)], platform.MovedPoints);
        Assert.Equal([new InputPoint(101, 201)], platform.DispatchPoints);
        Assert.Equal(new InputPoint(101, 201), Assert.Single(result.Actions!).ResolvedScreenPoint);
    }

    [Fact]
    public async Task ExecuteAsyncFailsWhenCursorDriftsBeforeClickDispatch()
    {
        WindowDescriptor targetWindow = CreateWindow();
        FakeWindowTargetResolver resolver = new(
            new InputTargetResolution(targetWindow, InputTargetSourceValues.Explicit),
            [targetWindow, targetWindow]);
        FakeInputPlatform platform = new()
        {
            CurrentProcessSecurity = CreateCurrentProcessSecurity(),
            TargetSecurity = CreateTargetSecurity(),
            DriftCursorBeforeDispatch = new InputPoint(141, 261),
        };
        Win32InputService service = new(resolver, platform, TimeProvider.System);

        InputResult result = await service.ExecuteAsync(
            new InputRequest
            {
                Hwnd = targetWindow.Hwnd,
                Actions =
                [
                    CreateAction(InputActionTypeValues.Click, InputCoordinateSpaceValues.Screen, new InputPoint(140, 260)),
                ],
            },
            new InputExecutionContext(),
            CancellationToken.None);

        Assert.Equal(InputStatusValues.Failed, result.Status);
        Assert.Equal(InputFailureCodeValues.CursorMoveFailed, result.FailureCode);
        Assert.Empty(platform.ClickButtons);
    }

    [Fact]
    public async Task ExecuteAsyncFailsWhenAmbientModifierIsHeldBeforeClickDispatch()
    {
        WindowDescriptor targetWindow = CreateWindow();
        FakeWindowTargetResolver resolver = new(
            new InputTargetResolution(targetWindow, InputTargetSourceValues.Explicit),
            [targetWindow, targetWindow]);
        FakeInputPlatform platform = new()
        {
            CurrentProcessSecurity = CreateCurrentProcessSecurity(),
            TargetSecurity = CreateTargetSecurity(),
            AmbientInputsBeforeDispatchSequence =
            [
                [InputModifierKeyValues.Ctrl],
            ],
        };
        Win32InputService service = new(resolver, platform, TimeProvider.System);

        InputResult result = await service.ExecuteAsync(
            new InputRequest
            {
                Hwnd = targetWindow.Hwnd,
                Actions =
                [
                    CreateAction(InputActionTypeValues.Click, InputCoordinateSpaceValues.Screen, new InputPoint(140, 260)),
                ],
            },
            new InputExecutionContext(),
            CancellationToken.None);

        Assert.Equal(InputStatusValues.Failed, result.Status);
        Assert.Equal(InputFailureCodeValues.InputDispatchFailed, result.FailureCode);
        Assert.Contains(InputModifierKeyValues.Ctrl, result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(platform.ClickButtons);
        Assert.Empty(platform.DispatchPoints);
    }

    [Fact]
    public async Task ExecuteAsyncFailsBeforeMoveWhenAmbientModifierIsHeld()
    {
        WindowDescriptor targetWindow = CreateWindow();
        FakeWindowTargetResolver resolver = new(
            new InputTargetResolution(targetWindow, InputTargetSourceValues.Explicit),
            [targetWindow]);
        FakeInputPlatform platform = new()
        {
            CurrentProcessSecurity = CreateCurrentProcessSecurity(),
            TargetSecurity = CreateTargetSecurity(),
            AmbientInputsBeforePointerSideEffectSequence =
            [
                [InputModifierKeyValues.Ctrl],
            ],
        };
        Win32InputService service = new(resolver, platform, TimeProvider.System);

        InputResult result = await service.ExecuteAsync(
            new InputRequest
            {
                Hwnd = targetWindow.Hwnd,
                Actions =
                [
                    CreateAction(InputActionTypeValues.Click, InputCoordinateSpaceValues.Screen, new InputPoint(140, 260)),
                ],
            },
            new InputExecutionContext(),
            CancellationToken.None);

        Assert.Equal(InputStatusValues.Failed, result.Status);
        Assert.Equal(InputFailureCodeValues.InputDispatchFailed, result.FailureCode);
        Assert.Contains(InputModifierKeyValues.Ctrl, result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(platform.MovedPoints);
        Assert.Empty(platform.ClickButtons);
    }

    [Fact]
    public async Task ExecuteAsyncFailsWhenMouseButtonIsAlreadyHeldBeforeClickDispatch()
    {
        WindowDescriptor targetWindow = CreateWindow();
        FakeWindowTargetResolver resolver = new(
            new InputTargetResolution(targetWindow, InputTargetSourceValues.Explicit),
            [targetWindow, targetWindow]);
        FakeInputPlatform platform = new()
        {
            CurrentProcessSecurity = CreateCurrentProcessSecurity(),
            TargetSecurity = CreateTargetSecurity(),
            AmbientInputsBeforeDispatchSequence =
            [
                ["левая кнопка мыши"],
            ],
        };
        Win32InputService service = new(resolver, platform, TimeProvider.System);

        InputResult result = await service.ExecuteAsync(
            new InputRequest
            {
                Hwnd = targetWindow.Hwnd,
                Actions =
                [
                    CreateAction(InputActionTypeValues.Click, InputCoordinateSpaceValues.Screen, new InputPoint(140, 260)),
                ],
            },
            new InputExecutionContext(),
            CancellationToken.None);

        Assert.Equal(InputStatusValues.Failed, result.Status);
        Assert.Equal(InputFailureCodeValues.InputDispatchFailed, result.FailureCode);
        Assert.Contains("мыш", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(platform.ClickButtons);
        Assert.Empty(platform.DispatchPoints);
    }

    [Fact]
    public async Task ExecuteAsyncFailsBeforeStandaloneMoveWhenMouseButtonIsAlreadyHeld()
    {
        WindowDescriptor targetWindow = CreateWindow();
        FakeWindowTargetResolver resolver = new(
            new InputTargetResolution(targetWindow, InputTargetSourceValues.Explicit),
            [targetWindow]);
        FakeInputPlatform platform = new()
        {
            CurrentProcessSecurity = CreateCurrentProcessSecurity(),
            TargetSecurity = CreateTargetSecurity(),
            AmbientInputsBeforePointerSideEffectSequence =
            [
                ["левая кнопка мыши"],
            ],
        };
        Win32InputService service = new(resolver, platform, TimeProvider.System);

        InputResult result = await service.ExecuteAsync(
            new InputRequest
            {
                Hwnd = targetWindow.Hwnd,
                Actions =
                [
                    CreateAction(InputActionTypeValues.Move, InputCoordinateSpaceValues.Screen, new InputPoint(140, 260)),
                ],
            },
            new InputExecutionContext(),
            CancellationToken.None);

        Assert.Equal(InputStatusValues.Failed, result.Status);
        Assert.Equal(InputFailureCodeValues.InputDispatchFailed, result.FailureCode);
        Assert.Contains("мыш", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(platform.MovedPoints);
        Assert.Null(Assert.Single(result.Actions!).ResolvedScreenPoint);
    }

    [Fact]
    public async Task ExecuteAsyncDoesNotPublishResolvedPointWhenHeldInputBlocksClickBeforeFirstMove()
    {
        WindowDescriptor targetWindow = CreateWindow();
        FakeWindowTargetResolver resolver = new(
            new InputTargetResolution(targetWindow, InputTargetSourceValues.Explicit),
            [targetWindow]);
        FakeInputPlatform platform = new()
        {
            CurrentProcessSecurity = CreateCurrentProcessSecurity(),
            TargetSecurity = CreateTargetSecurity(),
            AmbientInputsBeforePointerSideEffectSequence =
            [
                ["левая кнопка мыши"],
            ],
        };
        Win32InputService service = new(resolver, platform, TimeProvider.System);

        InputResult result = await service.ExecuteAsync(
            new InputRequest
            {
                Hwnd = targetWindow.Hwnd,
                Actions =
                [
                    CreateAction(InputActionTypeValues.Click, InputCoordinateSpaceValues.Screen, new InputPoint(140, 260)),
                ],
            },
            new InputExecutionContext(),
            CancellationToken.None);

        Assert.Equal(InputStatusValues.Failed, result.Status);
        Assert.Equal(InputFailureCodeValues.InputDispatchFailed, result.FailureCode);
        InputActionResult actionResult = Assert.Single(result.Actions!);
        Assert.Null(actionResult.ResolvedScreenPoint);
        Assert.Equal(InputButtonValues.Left, actionResult.Button);
        Assert.Empty(platform.MovedPoints);
        Assert.Empty(platform.ClickButtons);
    }

    [Fact]
    public async Task ExecuteAsyncDoesNotPublishResolvedPointWhenSetCursorPosFailsBeforeAnyMoveSideEffect()
    {
        WindowDescriptor targetWindow = CreateWindow();
        FakeWindowTargetResolver resolver = new(
            new InputTargetResolution(targetWindow, InputTargetSourceValues.Explicit),
            [targetWindow, targetWindow]);
        FakeInputPlatform platform = new()
        {
            CurrentProcessSecurity = CreateCurrentProcessSecurity(),
            TargetSecurity = CreateTargetSecurity(),
            FailSetCursorAfterMoveCount = 1,
        };
        Win32InputService service = new(resolver, platform, TimeProvider.System);

        InputResult result = await service.ExecuteAsync(
            new InputRequest
            {
                Hwnd = targetWindow.Hwnd,
                Actions =
                [
                    CreateAction(InputActionTypeValues.Click, InputCoordinateSpaceValues.Screen, new InputPoint(140, 260)),
                ],
            },
            new InputExecutionContext(),
            CancellationToken.None);

        Assert.Equal(InputStatusValues.Failed, result.Status);
        Assert.Equal(InputFailureCodeValues.CursorMoveFailed, result.FailureCode);
        InputActionResult actionResult = Assert.Single(result.Actions!);
        Assert.Null(actionResult.ResolvedScreenPoint);
        Assert.Equal(InputButtonValues.Left, actionResult.Button);
        Assert.Empty(platform.MovedPoints);
        Assert.Empty(platform.ClickButtons);
    }

    [Fact]
    public async Task ExecuteAsyncFailsWhenAmbientModifierAppearsBeforeSecondDoubleClickTap()
    {
        WindowDescriptor targetWindow = CreateWindow();
        FakeWindowTargetResolver resolver = new(
            new InputTargetResolution(targetWindow, InputTargetSourceValues.Explicit),
            [targetWindow, targetWindow, targetWindow, targetWindow]);
        FakeInputPlatform platform = new()
        {
            CurrentProcessSecurity = CreateCurrentProcessSecurity(),
            TargetSecurity = CreateTargetSecurity(),
            AmbientInputsBeforeDispatchSequence =
            [
                [],
                [InputModifierKeyValues.Shift],
            ],
        };
        Win32InputService service = new(resolver, platform, TimeProvider.System);

        InputResult result = await service.ExecuteAsync(
            new InputRequest
            {
                Hwnd = targetWindow.Hwnd,
                Actions =
                [
                    CreateAction(InputActionTypeValues.DoubleClick, InputCoordinateSpaceValues.Screen, new InputPoint(140, 260)),
                ],
            },
            new InputExecutionContext(),
            CancellationToken.None);

        Assert.Equal(InputStatusValues.Failed, result.Status);
        Assert.Equal(InputFailureCodeValues.InputDispatchFailed, result.FailureCode);
        Assert.Contains(InputModifierKeyValues.Shift, result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal([InputButtonValues.Left], platform.ClickButtons);
        Assert.Equal([new InputPoint(140, 260)], platform.DispatchPoints);
    }

    [Fact]
    public async Task ExecuteAsyncThrowsFactualFailureCarrierWhenUnexpectedExceptionOccursAfterCommittedClickSideEffect()
    {
        WindowDescriptor targetWindow = CreateWindow();
        FakeWindowTargetResolver resolver = new(
            new InputTargetResolution(targetWindow, InputTargetSourceValues.Explicit),
            [targetWindow, targetWindow]);
        FakeInputPlatform platform = new()
        {
            CurrentProcessSecurity = CreateCurrentProcessSecurity(),
            TargetSecurity = CreateTargetSecurity(),
            DispatchClickException = new InvalidOperationException("unexpected dispatch exception"),
        };
        Win32InputService service = new(resolver, platform, TimeProvider.System);

        InputExecutionFailureException exception = await Assert.ThrowsAsync<InputExecutionFailureException>(
            () => service.ExecuteAsync(
                new InputRequest
                {
                    Hwnd = targetWindow.Hwnd,
                    Actions =
                    [
                        CreateAction(InputActionTypeValues.Click, InputCoordinateSpaceValues.Screen, new InputPoint(140, 260)),
                    ],
                },
                new InputExecutionContext(),
                CancellationToken.None));

        Assert.IsType<InvalidOperationException>(exception.InnerException);
        InputResult result = exception.Result;
        Assert.Equal(InputStatusValues.Failed, result.Status);
        Assert.Equal(InputFailureCodeValues.InputDispatchFailed, result.FailureCode);
        Assert.Equal(0, result.CompletedActionCount);
        Assert.Equal(0, result.FailedActionIndex);
        InputActionResult actionResult = Assert.Single(result.Actions!);
        Assert.Equal(InputStatusValues.Failed, actionResult.Status);
        Assert.Equal(new InputPoint(140, 260), actionResult.ResolvedScreenPoint);
        Assert.Equal(InputButtonValues.Left, actionResult.Button);
        Assert.Equal([InputButtonValues.Left], platform.ClickButtons);
        Assert.Equal([new InputPoint(140, 260)], platform.DispatchPoints);
    }

    [Fact]
    public async Task ExecuteAsyncPropagatesCleanFirstActionSetupExceptionWithoutFactualCarrier()
    {
        WindowDescriptor targetWindow = CreateWindow();
        FakeWindowTargetResolver resolver = new(
            new InputTargetResolution(targetWindow, InputTargetSourceValues.Explicit),
            [targetWindow]);
        FakeInputPlatform platform = new()
        {
            CurrentProcessSecurity = CreateCurrentProcessSecurity(),
            TargetSecurity = CreateTargetSecurity(),
            OnTargetSecurityProbe = probeCount =>
            {
                if (probeCount == 1)
                {
                    throw new InvalidOperationException("unexpected setup exception");
                }
            },
        };
        Win32InputService service = new(resolver, platform, TimeProvider.System);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ExecuteAsync(
                new InputRequest
                {
                    Hwnd = targetWindow.Hwnd,
                    Actions =
                    [
                        CreateAction(InputActionTypeValues.Click, InputCoordinateSpaceValues.Screen, new InputPoint(140, 260)),
                    ],
                },
                new InputExecutionContext(),
                CancellationToken.None));

        Assert.Empty(platform.MovedPoints);
        Assert.Empty(platform.ClickButtons);
    }

    [Fact]
    public async Task ExecuteAsyncDoesNotReusePreviousCommittedSideEffectForNextActionUnexpectedSetupFailure()
    {
        WindowDescriptor targetWindow = CreateWindow();
        FakeWindowTargetResolver resolver = new(
            new InputTargetResolution(targetWindow, InputTargetSourceValues.Explicit),
            [targetWindow, targetWindow]);
        FakeInputPlatform platform = new()
        {
            CurrentProcessSecurity = CreateCurrentProcessSecurity(),
            TargetSecurity = CreateTargetSecurity(),
            OnTargetSecurityProbe = probeCount =>
            {
                if (probeCount == 2)
                {
                    throw new InvalidOperationException("unexpected setup exception");
                }
            },
        };
        Win32InputService service = new(resolver, platform, TimeProvider.System);

        InputExecutionFailureException exception = await Assert.ThrowsAsync<InputExecutionFailureException>(
            () => service.ExecuteAsync(
                new InputRequest
                {
                    Hwnd = targetWindow.Hwnd,
                    Actions =
                    [
                        CreateAction(InputActionTypeValues.Move, InputCoordinateSpaceValues.Screen, new InputPoint(140, 260)),
                        CreateAction(InputActionTypeValues.Click, InputCoordinateSpaceValues.Screen, new InputPoint(150, 270)),
                    ],
                },
                new InputExecutionContext(),
                CancellationToken.None));

        Assert.IsType<InvalidOperationException>(exception.InnerException);
        InputResult result = exception.Result;
        Assert.Equal(InputStatusValues.Failed, result.Status);
        Assert.Equal(InputFailureCodeValues.InputDispatchFailed, result.FailureCode);
        Assert.Equal(1, result.CompletedActionCount);
        Assert.Null(result.FailedActionIndex);
        InputActionResult completedAction = Assert.Single(result.Actions!);
        Assert.Equal(InputStatusValues.VerifyNeeded, completedAction.Status);
        Assert.Equal(new InputPoint(140, 260), completedAction.ResolvedScreenPoint);
        Assert.Contains("before the next input side effect", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal([new InputPoint(140, 260)], platform.MovedPoints);
        Assert.Empty(platform.ClickButtons);
    }

    [Fact]
    public async Task ExecuteAsyncFailsWhenAdmittedTargetIsNoLongerForegroundAtFinalDispatchBoundary()
    {
        WindowDescriptor targetWindow = CreateWindow();
        FakeWindowTargetResolver resolver = new(
            new InputTargetResolution(targetWindow, InputTargetSourceValues.Explicit),
            [targetWindow]);
        FakeInputPlatform platform = new()
        {
            CurrentProcessSecurity = CreateCurrentProcessSecurity(),
            TargetSecurity = CreateTargetSecurity(),
            ForegroundHwndBeforeDispatch = targetWindow.Hwnd + 1,
        };
        Win32InputService service = new(resolver, platform, TimeProvider.System);

        InputResult result = await service.ExecuteAsync(
            new InputRequest
            {
                Hwnd = targetWindow.Hwnd,
                Actions =
                [
                    CreateAction(InputActionTypeValues.Click, InputCoordinateSpaceValues.Screen, new InputPoint(140, 260)),
                ],
            },
            new InputExecutionContext(),
            CancellationToken.None);

        Assert.Equal(InputStatusValues.Failed, result.Status);
        Assert.Equal(InputFailureCodeValues.TargetNotForeground, result.FailureCode);
        Assert.Empty(platform.ClickButtons);
        Assert.Empty(platform.DispatchPoints);
    }

    [Fact]
    public async Task ExecuteAsyncFailsWhenForegroundHandleIsReusedByDifferentWindowBeforeDispatch()
    {
        WindowDescriptor targetWindow = CreateWindow();
        FakeWindowTargetResolver resolver = new(
            new InputTargetResolution(targetWindow, InputTargetSourceValues.Explicit),
            [targetWindow]);
        FakeInputPlatform platform = new()
        {
            CurrentProcessSecurity = CreateCurrentProcessSecurity(),
            TargetSecurity = CreateTargetSecurity(),
            ForegroundHwndBeforeDispatch = targetWindow.Hwnd,
            ForegroundSnapshotBeforeDispatch = new ActivatedWindowVerificationSnapshot(
                Exists: true,
                ProcessId: targetWindow.ProcessId,
                ThreadId: targetWindow.ThreadId,
                ClassName: "ReusedWindowClass",
                IsForeground: true,
                IsMinimized: false),
        };
        Win32InputService service = new(resolver, platform, TimeProvider.System);

        InputResult result = await service.ExecuteAsync(
            new InputRequest
            {
                Hwnd = targetWindow.Hwnd,
                Actions =
                [
                    CreateAction(InputActionTypeValues.Click, InputCoordinateSpaceValues.Screen, new InputPoint(140, 260)),
                ],
            },
            new InputExecutionContext(),
            CancellationToken.None);

        Assert.Equal(InputStatusValues.Failed, result.Status);
        Assert.Equal(InputFailureCodeValues.TargetNotForeground, result.FailureCode);
        Assert.Empty(platform.ClickButtons);
        Assert.Empty(platform.DispatchPoints);
    }

    [Fact]
    public async Task ExecuteAsyncMaterializesFailureWhenCancelledDuringMoveActionAfterSideEffect()
    {
        WindowDescriptor targetWindow = CreateWindow();
        FakeWindowTargetResolver resolver = new(
            new InputTargetResolution(targetWindow, InputTargetSourceValues.Explicit),
            [targetWindow, targetWindow]);
        using CancellationTokenSource cancellation = new();
        FakeInputPlatform platform = new()
        {
            CurrentProcessSecurity = CreateCurrentProcessSecurity(),
            TargetSecurity = CreateTargetSecurity(),
            OnMoveSideEffect = moveCount =>
            {
                if (moveCount == 1)
                {
                    cancellation.Cancel();
                }
            },
        };
        Win32InputService service = new(resolver, platform, TimeProvider.System);

        InputResult result = await service.ExecuteAsync(
            new InputRequest
            {
                Hwnd = targetWindow.Hwnd,
                Actions =
                [
                    CreateAction(InputActionTypeValues.Move, InputCoordinateSpaceValues.Screen, new InputPoint(140, 260)),
                    CreateAction(InputActionTypeValues.Click, InputCoordinateSpaceValues.Screen, new InputPoint(150, 270)),
                ],
            },
            new InputExecutionContext(),
            cancellation.Token);

        Assert.Equal(InputStatusValues.Failed, result.Status);
        Assert.Equal(InputFailureCodeValues.InputDispatchFailed, result.FailureCode);
        Assert.Equal(0, result.CompletedActionCount);
        Assert.Equal(0, result.FailedActionIndex);
        InputActionResult actionResult = Assert.Single(result.Actions!);
        Assert.Equal(InputStatusValues.Failed, actionResult.Status);
        Assert.Single(platform.MovedPoints);
        Assert.Empty(platform.ClickButtons);
    }

    [Fact]
    public async Task ExecuteAsyncMaterializesFailureWhenCancelledBetweenDoubleClickTaps()
    {
        WindowDescriptor targetWindow = CreateWindow();
        FakeWindowTargetResolver resolver = new(
            new InputTargetResolution(targetWindow, InputTargetSourceValues.Explicit),
            [targetWindow, targetWindow, targetWindow]);
        using CancellationTokenSource cancellation = new();
        FakeInputPlatform platform = new()
        {
            CurrentProcessSecurity = CreateCurrentProcessSecurity(),
            TargetSecurity = CreateTargetSecurity(),
            OnDispatchSideEffect = dispatchCount =>
            {
                if (dispatchCount == 1)
                {
                    cancellation.Cancel();
                }
            },
        };
        Win32InputService service = new(resolver, platform, TimeProvider.System);

        InputResult result = await service.ExecuteAsync(
            new InputRequest
            {
                Hwnd = targetWindow.Hwnd,
                Actions =
                [
                    CreateAction(InputActionTypeValues.DoubleClick, InputCoordinateSpaceValues.Screen, new InputPoint(140, 260)),
                ],
            },
            new InputExecutionContext(),
            cancellation.Token);

        Assert.Equal(InputStatusValues.Failed, result.Status);
        Assert.Equal(InputFailureCodeValues.InputDispatchFailed, result.FailureCode);
        Assert.Equal(0, result.CompletedActionCount);
        Assert.Equal(0, result.FailedActionIndex);
        InputActionResult actionResult = Assert.Single(result.Actions!);
        Assert.Equal(InputStatusValues.Failed, actionResult.Status);
        Assert.Equal(InputButtonValues.Left, actionResult.Button);
        Assert.Equal([InputButtonValues.Left], platform.ClickButtons);
        Assert.Equal([new InputPoint(140, 260)], platform.DispatchPoints);
    }

    [Fact]
    public async Task ExecuteAsyncDoesNotDispatchClickWhenCancelledAfterMoveSideEffect()
    {
        WindowDescriptor targetWindow = CreateWindow();
        FakeWindowTargetResolver resolver = new(
            new InputTargetResolution(targetWindow, InputTargetSourceValues.Explicit),
            [targetWindow, targetWindow]);
        using CancellationTokenSource cancellation = new();
        FakeInputPlatform platform = new()
        {
            CurrentProcessSecurity = CreateCurrentProcessSecurity(),
            TargetSecurity = CreateTargetSecurity(),
            OnMoveSideEffect = moveCount =>
            {
                if (moveCount == 1)
                {
                    cancellation.Cancel();
                }
            },
        };
        Win32InputService service = new(resolver, platform, TimeProvider.System);

        InputResult result = await service.ExecuteAsync(
            new InputRequest
            {
                Hwnd = targetWindow.Hwnd,
                Actions =
                [
                    CreateAction(InputActionTypeValues.Click, InputCoordinateSpaceValues.Screen, new InputPoint(140, 260)),
                ],
            },
            new InputExecutionContext(),
            cancellation.Token);

        Assert.Equal(InputStatusValues.Failed, result.Status);
        Assert.Equal(InputFailureCodeValues.InputDispatchFailed, result.FailureCode);
        InputActionResult actionResult = Assert.Single(result.Actions!);
        Assert.Equal(InputButtonValues.Left, actionResult.Button);
        Assert.Empty(platform.ClickButtons);
        Assert.Equal([new InputPoint(140, 260)], platform.MovedPoints);
    }

    [Fact]
    public async Task ExecuteAsyncMaterializesLeftButtonWhenDoubleClickIsCancelledAfterInitialMoveSideEffect()
    {
        WindowDescriptor targetWindow = CreateWindow();
        FakeWindowTargetResolver resolver = new(
            new InputTargetResolution(targetWindow, InputTargetSourceValues.Explicit),
            [targetWindow, targetWindow]);
        using CancellationTokenSource cancellation = new();
        FakeInputPlatform platform = new()
        {
            CurrentProcessSecurity = CreateCurrentProcessSecurity(),
            TargetSecurity = CreateTargetSecurity(),
            OnMoveSideEffect = moveCount =>
            {
                if (moveCount == 1)
                {
                    cancellation.Cancel();
                }
            },
        };
        Win32InputService service = new(resolver, platform, TimeProvider.System);

        InputResult result = await service.ExecuteAsync(
            new InputRequest
            {
                Hwnd = targetWindow.Hwnd,
                Actions =
                [
                    CreateAction(InputActionTypeValues.DoubleClick, InputCoordinateSpaceValues.Screen, new InputPoint(140, 260)),
                ],
            },
            new InputExecutionContext(),
            cancellation.Token);

        Assert.Equal(InputStatusValues.Failed, result.Status);
        Assert.Equal(InputFailureCodeValues.InputDispatchFailed, result.FailureCode);
        Assert.Equal(0, result.CompletedActionCount);
        Assert.Equal(0, result.FailedActionIndex);
        InputActionResult actionResult = Assert.Single(result.Actions!);
        Assert.Equal(InputStatusValues.Failed, actionResult.Status);
        Assert.Equal(InputButtonValues.Left, actionResult.Button);
        Assert.Equal([new InputPoint(140, 260)], platform.MovedPoints);
        Assert.Empty(platform.ClickButtons);
    }

    [Fact]
    public async Task ExecuteAsyncMaterializesFailureWhenCancelledAfterFinalClickDispatch()
    {
        WindowDescriptor targetWindow = CreateWindow();
        FakeWindowTargetResolver resolver = new(
            new InputTargetResolution(targetWindow, InputTargetSourceValues.Explicit),
            [targetWindow, targetWindow]);
        using CancellationTokenSource cancellation = new();
        FakeInputPlatform platform = new()
        {
            CurrentProcessSecurity = CreateCurrentProcessSecurity(),
            TargetSecurity = CreateTargetSecurity(),
            OnDispatchSideEffect = dispatchCount =>
            {
                if (dispatchCount == 1)
                {
                    cancellation.Cancel();
                }
            },
        };
        Win32InputService service = new(resolver, platform, TimeProvider.System);

        InputResult result = await service.ExecuteAsync(
            new InputRequest
            {
                Hwnd = targetWindow.Hwnd,
                Actions =
                [
                    CreateAction(InputActionTypeValues.Click, InputCoordinateSpaceValues.Screen, new InputPoint(140, 260)),
                ],
            },
            new InputExecutionContext(),
            cancellation.Token);

        Assert.Equal(InputStatusValues.Failed, result.Status);
        Assert.Equal(InputFailureCodeValues.InputDispatchFailed, result.FailureCode);
        Assert.Equal(0, result.CompletedActionCount);
        Assert.Equal(0, result.FailedActionIndex);
        Assert.Equal([InputButtonValues.Left], platform.ClickButtons);
        Assert.Equal([new InputPoint(140, 260)], platform.DispatchPoints);
    }

    [Fact]
    public async Task ExecuteAsyncDoesNotStartNextActionMoveWhenCancelledDuringBetweenActionsSetup()
    {
        WindowDescriptor targetWindow = CreateWindow();
        FakeWindowTargetResolver resolver = new(
            new InputTargetResolution(targetWindow, InputTargetSourceValues.Explicit),
            [targetWindow, targetWindow]);
        using CancellationTokenSource cancellation = new();
        FakeInputPlatform platform = new()
        {
            CurrentProcessSecurity = CreateCurrentProcessSecurity(),
            TargetSecurity = CreateTargetSecurity(),
            OnTargetSecurityProbe = probeCount =>
            {
                if (probeCount == 2)
                {
                    cancellation.Cancel();
                }
            },
        };
        Win32InputService service = new(resolver, platform, TimeProvider.System);

        InputResult result = await service.ExecuteAsync(
            new InputRequest
            {
                Hwnd = targetWindow.Hwnd,
                Actions =
                [
                    CreateAction(InputActionTypeValues.Move, InputCoordinateSpaceValues.Screen, new InputPoint(140, 260)),
                    CreateAction(InputActionTypeValues.Click, InputCoordinateSpaceValues.Screen, new InputPoint(150, 270)),
                ],
            },
            new InputExecutionContext(),
            cancellation.Token);

        Assert.Equal(InputStatusValues.Failed, result.Status);
        Assert.Equal(InputFailureCodeValues.InputDispatchFailed, result.FailureCode);
        Assert.Equal(1, result.CompletedActionCount);
        Assert.Null(result.FailedActionIndex);
        InputActionResult firstAction = Assert.Single(result.Actions!);
        Assert.Equal(InputStatusValues.VerifyNeeded, firstAction.Status);
        Assert.Equal([new InputPoint(140, 260)], platform.MovedPoints);
        Assert.Empty(platform.ClickButtons);
    }

    [Fact]
    public async Task ExecuteAsyncDoesNotDispatchClickWhenCancelledAfterRefreshedMoveSideEffect()
    {
        WindowDescriptor targetWindow = CreateWindow();
        WindowDescriptor shiftedWindow = targetWindow with
        {
            Bounds = new Bounds(101, 201, 421, 561),
            IsForeground = true,
        };
        FakeWindowTargetResolver resolver = new(
            new InputTargetResolution(targetWindow, InputTargetSourceValues.Explicit),
            [targetWindow, shiftedWindow]);
        using CancellationTokenSource cancellation = new();
        FakeInputPlatform platform = new()
        {
            CurrentProcessSecurity = CreateCurrentProcessSecurity(),
            TargetSecurity = CreateTargetSecurity(),
            OnMoveSideEffect = moveCount =>
            {
                if (moveCount == 2)
                {
                    cancellation.Cancel();
                }
            },
        };
        Win32InputService service = new(resolver, platform, TimeProvider.System);

        InputResult result = await service.ExecuteAsync(
            new InputRequest
            {
                Hwnd = targetWindow.Hwnd,
                Actions =
                [
                    new InputAction
                    {
                        Type = InputActionTypeValues.Click,
                        CoordinateSpace = InputCoordinateSpaceValues.CapturePixels,
                        Point = new InputPoint(0, 0),
                        CaptureReference = new InputCaptureReference(
                            new InputBounds(100, 200, 420, 560),
                            320,
                            360,
                            96,
                            DateTimeOffset.UtcNow),
                    },
                ],
            },
            new InputExecutionContext(),
            cancellation.Token);

        Assert.Equal(InputStatusValues.Failed, result.Status);
        Assert.Equal(InputFailureCodeValues.InputDispatchFailed, result.FailureCode);
        InputActionResult actionResult = Assert.Single(result.Actions!);
        Assert.Equal(new InputPoint(101, 201), actionResult.ResolvedScreenPoint);
        Assert.Equal(InputButtonValues.Left, actionResult.Button);
        Assert.Empty(platform.ClickButtons);
        Assert.Equal([new InputPoint(100, 200), new InputPoint(101, 201)], platform.MovedPoints);
    }

    [Fact]
    public async Task ExecuteAsyncPreservesCommittedRefreshedMoveWhenPostMoveCursorVerificationFails()
    {
        WindowDescriptor targetWindow = CreateWindow();
        WindowDescriptor shiftedWindow = targetWindow with
        {
            Bounds = new Bounds(101, 201, 421, 561),
            IsForeground = true,
        };
        FakeWindowTargetResolver resolver = new(
            new InputTargetResolution(targetWindow, InputTargetSourceValues.Explicit),
            [targetWindow, shiftedWindow]);
        FakeInputPlatform platform = new()
        {
            CurrentProcessSecurity = CreateCurrentProcessSecurity(),
            TargetSecurity = CreateTargetSecurity(),
            FailCursorReadAfterMoveCount = 2,
        };
        Win32InputService service = new(resolver, platform, TimeProvider.System);

        InputResult result = await service.ExecuteAsync(
            new InputRequest
            {
                Hwnd = targetWindow.Hwnd,
                Actions =
                [
                    new InputAction
                    {
                        Type = InputActionTypeValues.Click,
                        CoordinateSpace = InputCoordinateSpaceValues.CapturePixels,
                        Point = new InputPoint(0, 0),
                        CaptureReference = new InputCaptureReference(
                            new InputBounds(100, 200, 420, 560),
                            320,
                            360,
                            96,
                            DateTimeOffset.UtcNow),
                    },
                ],
            },
            new InputExecutionContext(),
            CancellationToken.None);

        Assert.Equal(InputStatusValues.Failed, result.Status);
        Assert.Equal(InputFailureCodeValues.CursorMoveFailed, result.FailureCode);
        InputActionResult actionResult = Assert.Single(result.Actions!);
        Assert.Equal(new InputPoint(101, 201), actionResult.ResolvedScreenPoint);
        Assert.Equal(InputButtonValues.Left, actionResult.Button);
        Assert.Empty(platform.ClickButtons);
        Assert.Equal([new InputPoint(100, 200), new InputPoint(101, 201)], platform.MovedPoints);
    }

    [Fact]
    public async Task ExecuteAsyncPreservesObservedMovePointWhenPostMoveCursorVerificationDrifts()
    {
        WindowDescriptor targetWindow = CreateWindow();
        FakeWindowTargetResolver resolver = new(
            new InputTargetResolution(targetWindow, InputTargetSourceValues.Explicit),
            [targetWindow, targetWindow]);
        FakeInputPlatform platform = new()
        {
            CurrentProcessSecurity = CreateCurrentProcessSecurity(),
            TargetSecurity = CreateTargetSecurity(),
            DriftCursorReadAfterMoveCount = 1,
        };
        Win32InputService service = new(resolver, platform, TimeProvider.System);

        InputResult result = await service.ExecuteAsync(
            new InputRequest
            {
                Hwnd = targetWindow.Hwnd,
                Actions =
                [
                    CreateAction(InputActionTypeValues.Click, InputCoordinateSpaceValues.Screen, new InputPoint(140, 260)),
                ],
            },
            new InputExecutionContext(),
            CancellationToken.None);

        Assert.Equal(InputStatusValues.Failed, result.Status);
        Assert.Equal(InputFailureCodeValues.CursorMoveFailed, result.FailureCode);
        InputActionResult actionResult = Assert.Single(result.Actions!);
        Assert.Equal(new InputPoint(141, 261), actionResult.ResolvedScreenPoint);
        Assert.Equal(InputButtonValues.Left, actionResult.Button);
        Assert.Empty(platform.ClickButtons);
        Assert.Equal([new InputPoint(140, 260)], platform.MovedPoints);
    }

    [Fact]
    public async Task ExecuteAsyncPreservesObservedRefreshedMovePointWhenPostMoveCursorVerificationDrifts()
    {
        WindowDescriptor targetWindow = CreateWindow();
        WindowDescriptor shiftedWindow = targetWindow with
        {
            Bounds = new Bounds(101, 201, 421, 561),
            IsForeground = true,
        };
        FakeWindowTargetResolver resolver = new(
            new InputTargetResolution(targetWindow, InputTargetSourceValues.Explicit),
            [targetWindow, shiftedWindow]);
        FakeInputPlatform platform = new()
        {
            CurrentProcessSecurity = CreateCurrentProcessSecurity(),
            TargetSecurity = CreateTargetSecurity(),
            DriftCursorReadAfterMoveCount = 2,
        };
        Win32InputService service = new(resolver, platform, TimeProvider.System);

        InputResult result = await service.ExecuteAsync(
            new InputRequest
            {
                Hwnd = targetWindow.Hwnd,
                Actions =
                [
                    new InputAction
                    {
                        Type = InputActionTypeValues.Click,
                        CoordinateSpace = InputCoordinateSpaceValues.CapturePixels,
                        Point = new InputPoint(0, 0),
                        CaptureReference = new InputCaptureReference(
                            new InputBounds(100, 200, 420, 560),
                            320,
                            360,
                            96,
                            DateTimeOffset.UtcNow),
                    },
                ],
            },
            new InputExecutionContext(),
            CancellationToken.None);

        Assert.Equal(InputStatusValues.Failed, result.Status);
        Assert.Equal(InputFailureCodeValues.CursorMoveFailed, result.FailureCode);
        InputActionResult actionResult = Assert.Single(result.Actions!);
        Assert.Equal(new InputPoint(102, 202), actionResult.ResolvedScreenPoint);
        Assert.Equal(InputButtonValues.Left, actionResult.Button);
        Assert.Empty(platform.ClickButtons);
        Assert.Equal([new InputPoint(100, 200), new InputPoint(101, 201)], platform.MovedPoints);
    }

    [Fact]
    public async Task ExecuteAsyncReportsFullyDispatchedDoubleClickWhenCancelledAfterSecondTap()
    {
        WindowDescriptor targetWindow = CreateWindow();
        FakeWindowTargetResolver resolver = new(
            new InputTargetResolution(targetWindow, InputTargetSourceValues.Explicit),
            [targetWindow, targetWindow, targetWindow]);
        using CancellationTokenSource cancellation = new();
        FakeInputPlatform platform = new()
        {
            CurrentProcessSecurity = CreateCurrentProcessSecurity(),
            TargetSecurity = CreateTargetSecurity(),
            OnDispatchSideEffect = dispatchCount =>
            {
                if (dispatchCount == 2)
                {
                    cancellation.Cancel();
                }
            },
        };
        Win32InputService service = new(resolver, platform, TimeProvider.System);

        InputResult result = await service.ExecuteAsync(
            new InputRequest
            {
                Hwnd = targetWindow.Hwnd,
                Actions =
                [
                    CreateAction(InputActionTypeValues.DoubleClick, InputCoordinateSpaceValues.Screen, new InputPoint(140, 260)),
                ],
            },
            new InputExecutionContext(),
            cancellation.Token);

        Assert.Equal(InputStatusValues.Failed, result.Status);
        Assert.Equal(InputFailureCodeValues.InputDispatchFailed, result.FailureCode);
        Assert.Equal([InputButtonValues.Left, InputButtonValues.Left], platform.ClickButtons);
        Assert.Contains("both", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("second tap was not executed", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsyncRevalidatesTargetBeforeSecondClickInDoubleClick()
    {
        WindowDescriptor targetWindow = CreateWindow();
        WindowDescriptor movedWindow = targetWindow with
        {
            Bounds = new Bounds(500, 500, 800, 900),
            IsForeground = true,
        };
        FakeWindowTargetResolver resolver = new(
            new InputTargetResolution(targetWindow, InputTargetSourceValues.Explicit),
            [targetWindow, targetWindow, targetWindow, movedWindow]);
        FakeInputPlatform platform = new()
        {
            CurrentProcessSecurity = CreateCurrentProcessSecurity(),
            TargetSecurity = CreateTargetSecurity(),
            TargetSecuritySequence =
            [
                CreateTargetSecurity(),
                CreateTargetSecurity(),
            ],
        };
        Win32InputService service = new(resolver, platform, TimeProvider.System);

        InputResult result = await service.ExecuteAsync(
            new InputRequest
            {
                Hwnd = targetWindow.Hwnd,
                Actions =
                [
                    CreateAction(InputActionTypeValues.DoubleClick, InputCoordinateSpaceValues.Screen, new InputPoint(140, 260)),
                ],
            },
            new InputExecutionContext(),
            CancellationToken.None);

        Assert.Equal(InputStatusValues.Failed, result.Status);
        Assert.Equal(InputFailureCodeValues.PointOutOfBounds, result.FailureCode);
        Assert.Equal([InputButtonValues.Left], platform.ClickButtons);
    }

    [Fact]
    public async Task ExecuteAsyncFailsClosedWhenCapturePixelsPlanChangesBetweenDoubleClickTaps()
    {
        WindowDescriptor targetWindow = CreateWindow();
        WindowDescriptor shiftedWindow = targetWindow with
        {
            Bounds = new Bounds(101, 201, 421, 561),
            IsForeground = true,
        };
        FakeWindowTargetResolver resolver = new(
            new InputTargetResolution(targetWindow, InputTargetSourceValues.Explicit),
            [targetWindow, targetWindow, targetWindow, shiftedWindow]);
        FakeInputPlatform platform = new()
        {
            CurrentProcessSecurity = CreateCurrentProcessSecurity(),
            TargetSecurity = CreateTargetSecurity(),
        };
        Win32InputService service = new(resolver, platform, TimeProvider.System);

        InputResult result = await service.ExecuteAsync(
            new InputRequest
            {
                Hwnd = targetWindow.Hwnd,
                Actions =
                [
                    new InputAction
                    {
                        Type = InputActionTypeValues.DoubleClick,
                        CoordinateSpace = InputCoordinateSpaceValues.CapturePixels,
                        Point = new InputPoint(0, 0),
                        CaptureReference = new InputCaptureReference(
                            new InputBounds(100, 200, 420, 560),
                            320,
                            360,
                            96,
                            DateTimeOffset.UtcNow),
                    },
                ],
            },
            new InputExecutionContext(),
            CancellationToken.None);

        Assert.Equal(InputStatusValues.Failed, result.Status);
        Assert.Equal(InputFailureCodeValues.CaptureReferenceStale, result.FailureCode);
        Assert.Equal([InputButtonValues.Left], platform.ClickButtons);
        Assert.Equal([new InputPoint(100, 200)], platform.DispatchPoints);
    }

    [Fact]
    public async Task ExecuteAsyncReturnsStaleTargetWhenTargetCannotBeRevalidated()
    {
        WindowDescriptor targetWindow = CreateWindow();
        FakeWindowTargetResolver resolver = new(
            new InputTargetResolution(targetWindow, InputTargetSourceValues.Attached),
            [null]);
        FakeInputPlatform platform = new()
        {
            CurrentProcessSecurity = CreateCurrentProcessSecurity(),
            TargetSecurity = CreateTargetSecurity(),
        };
        Win32InputService service = new(resolver, platform, TimeProvider.System);

        InputResult result = await service.ExecuteAsync(
            new InputRequest
            {
                Actions =
                [
                    CreateAction(InputActionTypeValues.Click, InputCoordinateSpaceValues.Screen, new InputPoint(140, 260)),
                ],
            },
            new InputExecutionContext(targetWindow),
            CancellationToken.None);

        Assert.Equal(InputStatusValues.Failed, result.Status);
        Assert.Equal(InputFailureCodeValues.StaleAttachedTarget, result.FailureCode);
        Assert.Empty(platform.ClickButtons);
    }

    [Fact]
    public void AddWinBridgeRuntimeRegistersInputServiceAndPlatform()
    {
        string root = Path.Combine(Path.GetTempPath(), "winbridge-input-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        ServiceCollection services = new();
        services.AddWinBridgeRuntime(root, "Tests");

        using ServiceProvider provider = services.BuildServiceProvider();

        IInputService service = provider.GetRequiredService<IInputService>();
        IInputPlatform platform = provider.GetRequiredService<IInputPlatform>();
        InputResultMaterializer materializer = provider.GetRequiredService<InputResultMaterializer>();

        Assert.False(typeof(IInputService).IsPublic);
        Assert.IsType<Win32InputService>(service);
        Assert.IsType<Win32InputPlatform>(platform);
        Assert.NotNull(materializer);
    }

    private static WindowDescriptor CreateWindow() =>
        new(
            Hwnd: 101,
            Title: "Target",
            ProcessName: "target",
            ProcessId: 321,
            ThreadId: 654,
            ClassName: "TargetWindowClass",
            Bounds: new Bounds(100, 200, 420, 560),
            IsForeground: true,
            IsVisible: true,
            EffectiveDpi: 96,
            DpiScale: 1.0,
            WindowState: WindowStateValues.Normal);

    private static InputAction CreateAction(
        string type,
        string coordinateSpace,
        InputPoint point,
        string? button = null)
    {
        InputAction action = new()
        {
            Type = type,
            CoordinateSpace = coordinateSpace,
            Point = point,
        };

        if (button is not null)
        {
            action = action with { Button = button };
        }

        return action;
    }

    private static InputProcessSecurityContext CreateCurrentProcessSecurity() =>
        new(
            SessionId: 1,
            SessionResolved: true,
            IntegrityLevel: InputIntegrityLevel.High,
            IntegrityResolved: true,
            HasUiAccess: false,
            UiAccessResolved: true,
            Reason: null);

    private static InputTargetSecurityInfo CreateTargetSecurity() =>
        new(
            ProcessId: 321,
            SessionId: 1,
            SessionResolved: true,
            IntegrityLevel: InputIntegrityLevel.Medium,
            IntegrityResolved: true,
            Reason: null);

    private static AuditLogOptions CreateAuditLogOptions(string root, string runId) =>
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

    private sealed class FakeWindowTargetResolver(
        InputTargetResolution resolution,
        IReadOnlyList<WindowDescriptor?> liveWindows) : IWindowTargetResolver
    {
        private int _revalidationIndex;

        public WindowDescriptor? ResolveExplicitOrAttachedWindow(long? explicitHwnd, WindowDescriptor? attachedWindow) =>
            throw new NotSupportedException();

        public WindowDescriptor? ResolveLiveWindowByIdentity(WindowDescriptor expectedWindow)
        {
            if (_revalidationIndex >= liveWindows.Count)
            {
                return liveWindows.Count == 0 ? null : liveWindows[^1];
            }

            WindowDescriptor? resolvedWindow = liveWindows[_revalidationIndex++];
            return resolvedWindow is null
                ? null
                : resolvedWindow with
                {
                    ProcessId = expectedWindow.ProcessId,
                    ThreadId = expectedWindow.ThreadId,
                    ClassName = expectedWindow.ClassName,
                };
        }

        public UiaSnapshotTargetResolution ResolveUiaSnapshotTarget(long? explicitHwnd, WindowDescriptor? attachedWindow) =>
            throw new NotSupportedException();

        public InputTargetResolution ResolveInputTarget(long? explicitHwnd, WindowDescriptor? attachedWindow) => resolution;

        public WaitTargetResolution ResolveWaitTarget(long? explicitHwnd, WindowDescriptor? attachedWindow) =>
            throw new NotSupportedException();
    }

    private sealed class FakeInputPlatform : IInputPlatform, IDisposable
    {
        private readonly Queue<bool> _clickResults = new();
        private readonly Queue<InputClickDispatchResult> _clickDispatchResults = new();
        private readonly Queue<IReadOnlyList<string>> _ambientInputsBeforePointerSideEffect = new();
        private readonly Queue<IReadOnlyList<string>> _ambientModifiersBeforeDispatch = new();
        private readonly Queue<InputTargetSecurityInfo> _targetSecuritySequence = new();
        private readonly ManualResetEventSlim _firstDispatchEntered = new(false);
        private readonly ManualResetEventSlim _releaseBlockedDispatch = new(false);
        public List<InputPoint> MovedPoints { get; } = [];
        public List<InputPoint> DispatchPoints { get; } = [];
        public List<string> ClickButtons { get; } = [];
        public InputProcessSecurityContext CurrentProcessSecurity { get; set; } = null!;
        public InputTargetSecurityInfo TargetSecurity { get; set; } = null!;
        public bool BlockFirstDispatch { get; set; }
        public InputPoint? DriftCursorBeforeDispatch { get; set; }
        public long? ForegroundHwndBeforeDispatch { get; set; }
        public bool ForegroundWindowUnavailableBeforeDispatch { get; set; }
        public ActivatedWindowVerificationSnapshot? ForegroundSnapshotBeforeDispatch { get; set; }
        public int? FailSetCursorAfterMoveCount { get; set; }
        public int? FailCursorReadAfterMoveCount { get; set; }
        public int? DriftCursorReadAfterMoveCount { get; set; }
        public Action<int>? OnMoveSideEffect { get; set; }
        public Action<int>? OnDispatchSideEffect { get; set; }
        public Action<int>? OnTargetSecurityProbe { get; set; }
        public Exception? DispatchClickException { get; set; }

        public IReadOnlyList<bool> ClickResults
        {
            init
            {
                _clickResults.Clear();
                foreach (bool item in value)
                {
                    _clickResults.Enqueue(item);
                }
            }
        }

        public IReadOnlyList<InputClickDispatchResult> ClickDispatchResults
        {
            init
            {
                _clickDispatchResults.Clear();
                foreach (InputClickDispatchResult item in value)
                {
                    _clickDispatchResults.Enqueue(item);
                }
            }
        }

        public IReadOnlyList<InputTargetSecurityInfo> TargetSecuritySequence
        {
            init
            {
                _targetSecuritySequence.Clear();
                foreach (InputTargetSecurityInfo item in value)
                {
                    _targetSecuritySequence.Enqueue(item);
                }
            }
        }

        public IReadOnlyList<IReadOnlyList<string>> AmbientInputsBeforeDispatchSequence
        {
            init
            {
                _ambientModifiersBeforeDispatch.Clear();
                foreach (IReadOnlyList<string> item in value)
                {
                    _ambientModifiersBeforeDispatch.Enqueue(item);
                }
            }
        }

        public IReadOnlyList<IReadOnlyList<string>> AmbientInputsBeforePointerSideEffectSequence
        {
            init
            {
                _ambientInputsBeforePointerSideEffect.Clear();
                foreach (IReadOnlyList<string> item in value)
                {
                    _ambientInputsBeforePointerSideEffect.Enqueue(item);
                }
            }
        }

        public InputProcessSecurityContext ProbeCurrentProcessSecurity() => CurrentProcessSecurity;

        public InputTargetSecurityInfo ProbeTargetSecurity(long hwnd, int? processIdHint)
        {
            TargetSecurityProbeCount++;
            OnTargetSecurityProbe?.Invoke(TargetSecurityProbeCount);
            return _targetSecuritySequence.Count > 0 ? _targetSecuritySequence.Dequeue() : TargetSecurity;
        }

        public InputPointerSideEffectBoundaryResult ValidatePointerSideEffectBoundary(WindowDescriptor admittedTargetWindow)
        {
            long? foregroundHwnd =
                ForegroundWindowUnavailableBeforeDispatch
                    ? null
                    : ForegroundHwndBeforeDispatch ?? admittedTargetWindow.Hwnd;
            ActivatedWindowVerificationSnapshot foregroundSnapshot =
                ForegroundWindowUnavailableBeforeDispatch
                    ? new(
                        Exists: false,
                        ProcessId: null,
                        ThreadId: null,
                        ClassName: null,
                        IsForeground: false,
                        IsMinimized: false)
                    : ForegroundSnapshotBeforeDispatch ?? new ActivatedWindowVerificationSnapshot(
                        Exists: true,
                        ProcessId: admittedTargetWindow.ProcessId,
                        ThreadId: admittedTargetWindow.ThreadId,
                        ClassName: admittedTargetWindow.ClassName,
                        IsForeground: true,
                        IsMinimized: false);
            if (!InputForegroundTargetBoundaryPolicy.TryValidate(
                    foregroundHwnd,
                    foregroundSnapshot,
                    admittedTargetWindow,
                    out _,
                    out string? foregroundFailureCode,
                    out string? foregroundReason))
            {
                return new(
                    Success: false,
                    FailureCode: foregroundFailureCode,
                    Reason: foregroundReason);
            }

            IReadOnlyList<string> activeInputs =
                _ambientInputsBeforePointerSideEffect.Count > 0
                    ? _ambientInputsBeforePointerSideEffect.Dequeue()
                    : Array.Empty<string>();
            if (activeInputs.Count > 0)
            {
                return new(
                    Success: false,
                    FailureCode: InputFailureCodeValues.InputDispatchFailed,
                    Reason: $"Held inputs detected before pointer side effect: {string.Join(", ", activeInputs)}.");
            }

            return new(Success: true);
        }

        public bool TrySetCursorPosition(InputPoint screenPoint)
        {
            if (FailSetCursorAfterMoveCount is int failSetCursorAfterMoveCount && MovedPoints.Count + 1 == failSetCursorAfterMoveCount)
            {
                return false;
            }

            MovedPoints.Add(screenPoint);
            CurrentCursorPosition = screenPoint;
            OnMoveSideEffect?.Invoke(MovedPoints.Count);
            return true;
        }

        public bool TryGetCursorPosition(out InputPoint screenPoint)
        {
            if (FailCursorReadAfterMoveCount is int failAfterMoveCount && MovedPoints.Count == failAfterMoveCount)
            {
                screenPoint = new InputPoint(0, 0);
                return false;
            }

            if (DriftCursorReadAfterMoveCount is int driftAfterMoveCount && MovedPoints.Count == driftAfterMoveCount)
            {
                screenPoint = new InputPoint(
                    CurrentCursorPosition?.X + 1 ?? 1,
                    CurrentCursorPosition?.Y + 1 ?? 1);
                return true;
            }

            screenPoint = CurrentCursorPosition ?? new InputPoint(0, 0);
            return CurrentCursorPosition is not null;
        }

        public InputClickDispatchResult DispatchClick(InputClickDispatchContext context)
        {
            if (DriftCursorBeforeDispatch is not null)
            {
                CurrentCursorPosition = DriftCursorBeforeDispatch;
            }

            if (CurrentCursorPosition is null)
            {
                return new(
                    Success: false,
                    FailureCode: InputFailureCodeValues.CursorMoveFailed,
                    Reason: "Cursor position is unavailable before dispatch.");
            }

            if (!Equals(CurrentCursorPosition, context.ExpectedScreenPoint))
            {
                return new(
                    Success: false,
                    FailureCode: InputFailureCodeValues.CursorMoveFailed,
                    Reason: "Cursor position drifted before dispatch.");
            }

            long? foregroundHwnd =
                ForegroundWindowUnavailableBeforeDispatch
                    ? null
                    : ForegroundHwndBeforeDispatch ?? context.AdmittedTargetWindow.Hwnd;
            ActivatedWindowVerificationSnapshot foregroundSnapshot =
                ForegroundWindowUnavailableBeforeDispatch
                    ? new(
                        Exists: false,
                        ProcessId: null,
                        ThreadId: null,
                        ClassName: null,
                        IsForeground: false,
                        IsMinimized: false)
                    : ForegroundSnapshotBeforeDispatch ?? new ActivatedWindowVerificationSnapshot(
                        Exists: true,
                        ProcessId: context.AdmittedTargetWindow.ProcessId,
                        ThreadId: context.AdmittedTargetWindow.ThreadId,
                        ClassName: context.AdmittedTargetWindow.ClassName,
                        IsForeground: true,
                        IsMinimized: false);
            if (!InputForegroundTargetBoundaryPolicy.TryValidate(
                    foregroundHwnd,
                    foregroundSnapshot,
                    context.AdmittedTargetWindow,
                    out _,
                    out string? foregroundFailureCode,
                    out string? foregroundReason))
            {
                return new(
                    Success: false,
                    FailureCode: foregroundFailureCode,
                    Reason: foregroundReason);
            }

            IReadOnlyList<string> activeModifiers =
                _ambientModifiersBeforeDispatch.Count > 0
                    ? _ambientModifiersBeforeDispatch.Dequeue()
                    : Array.Empty<string>();
            if (activeModifiers.Count > 0)
            {
                return new(
                    Success: false,
                    FailureCode: InputFailureCodeValues.InputDispatchFailed,
                    Reason: $"Held modifiers detected before dispatch: {string.Join(", ", activeModifiers)}.");
            }

            ClickButtons.Add(context.LogicalButton);
            DispatchPoints.Add(context.ExpectedScreenPoint);
            OnDispatchSideEffect?.Invoke(ClickButtons.Count);
            if (DispatchClickException is not null)
            {
                throw DispatchClickException;
            }
            if (BlockFirstDispatch && ClickButtons.Count == 1)
            {
                _firstDispatchEntered.Set();
                _releaseBlockedDispatch.Wait();
            }

            if (_clickDispatchResults.Count > 0)
            {
                return _clickDispatchResults.Dequeue();
            }

            bool success = _clickResults.Count == 0 || _clickResults.Dequeue();
            return success
                ? new(Success: true)
                : new(
                    Success: false,
                    FailureCode: InputFailureCodeValues.InputDispatchFailed,
                    Reason: "Dispatch failed.");
        }

        public bool WaitForFirstDispatchEntered(TimeSpan timeout) =>
            _firstDispatchEntered.Wait(timeout);

        public void ReleaseBlockedDispatch() =>
            _releaseBlockedDispatch.Set();

        public void Dispose()
        {
            _firstDispatchEntered.Dispose();
            _releaseBlockedDispatch.Dispose();
        }

        private InputPoint? CurrentCursorPosition { get; set; }
        private int TargetSecurityProbeCount { get; set; }
    }
}
