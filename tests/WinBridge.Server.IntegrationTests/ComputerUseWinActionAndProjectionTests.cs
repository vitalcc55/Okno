// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;
using ModelContextProtocol.Protocol;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Session;
using WinBridge.Runtime.Tooling;
using WinBridge.Runtime.Windows.Capture;
using WinBridge.Runtime.Windows.Shell;
using WinBridge.Runtime.Windows.UIA;
using WinBridge.Server.ComputerUse;

namespace WinBridge.Server.IntegrationTests;

public sealed class ComputerUseWinActionAndProjectionTests
{
    [Fact]
    public void ListAppsPublishesSelectableWindowInstancesInsideEachAppGroup()
    {
        using TempDirectoryScope temp = new();
        ComputerUseWinApprovalStore approvalStore = new(
            new ComputerUseWinOptions(
                PluginRoot: temp.Root,
                AppInstructionsRoot: Path.Combine(temp.Root, "references", "AppInstructions"),
                ApprovalStorePath: Path.Combine(temp.Root, "AppApprovals.json")));
        approvalStore.Approve("explorer");

        ComputerUseWinTools tools = CreateComputerUseWinTools(
            new FakeListAppsWindowManager(
            [
                CreateWindow(hwnd: 101, title: "Explorer A", processName: "explorer", processId: 1001, isForeground: false),
                CreateWindow(hwnd: 202, title: "Explorer B", processName: "explorer", processId: 1001, isForeground: true),
                CreateWindow(hwnd: 303, title: "Admin Console", processName: "powershell", processId: 2002, isForeground: false),
                CreateWindow(hwnd: 404, title: "Hidden Helper", processName: "notepad", processId: 3003, isForeground: false, isVisible: false),
            ]),
            approvalStore);

        ModelContextProtocol.Protocol.CallToolResult result = tools.ListApps();

        Assert.False(result.IsError);
        ComputerUseWinListAppsResult payload = JsonSerializer.Deserialize<ComputerUseWinListAppsResult>(
            result.StructuredContent!.Value.GetRawText(),
            ComputerUseWinToolResultFactory.PayloadJsonOptions)!;
        Assert.Equal(ComputerUseWinStatusValues.Ok, payload.Status);
        Assert.Equal(2, payload.Count);
        Assert.Equal(2, payload.Apps.Count);

        ComputerUseWinAppDescriptor explorer = payload.Apps[0];
        Assert.Equal("explorer", explorer.AppId);
        Assert.True(explorer.IsApproved);
        Assert.False(explorer.IsBlocked);
        Assert.Equal(2, explorer.Windows.Count);

        ComputerUseWinWindowDescriptor explorerWindowA = explorer.Windows.Single(window => window.Hwnd == 101);
        ComputerUseWinWindowDescriptor explorerWindowB = explorer.Windows.Single(window => window.Hwnd == 202);
        Assert.False(string.IsNullOrWhiteSpace(explorerWindowA.WindowId));
        Assert.False(string.IsNullOrWhiteSpace(explorerWindowB.WindowId));
        Assert.NotEqual(explorerWindowA.WindowId, explorerWindowB.WindowId);
        Assert.False(explorerWindowA.IsForeground);
        Assert.True(explorerWindowB.IsForeground);

        ComputerUseWinAppDescriptor blockedConsole = payload.Apps[1];
        Assert.Equal("powershell", blockedConsole.AppId);
        Assert.False(blockedConsole.IsApproved);
        Assert.True(blockedConsole.IsBlocked);
        Assert.Contains("powershell", blockedConsole.BlockReason, StringComparison.OrdinalIgnoreCase);
        ComputerUseWinWindowDescriptor consoleWindow = Assert.Single(blockedConsole.Windows);
        Assert.Equal(303, consoleWindow.Hwnd);
    }

    [Fact]
    public void ListAppsReusesWindowIdsForUnchangedDiscoverySnapshot()
    {
        using TempDirectoryScope temp = new();
        ComputerUseWinApprovalStore approvalStore = CreateApprovalStore(temp);
        ComputerUseWinTools tools = CreateComputerUseWinTools(
            new FakeListAppsWindowManager(
            [
                CreateWindow(hwnd: 101, title: "Explorer A", processName: "explorer", processId: 1001, isForeground: false),
                CreateWindow(hwnd: 202, title: "Explorer B", processName: "explorer", processId: 1001, isForeground: true),
            ]),
            approvalStore);

        ComputerUseWinListAppsResult firstPayload = JsonSerializer.Deserialize<ComputerUseWinListAppsResult>(
            tools.ListApps().StructuredContent!.Value.GetRawText(),
            ComputerUseWinToolResultFactory.PayloadJsonOptions)!;
        ComputerUseWinListAppsResult secondPayload = JsonSerializer.Deserialize<ComputerUseWinListAppsResult>(
            tools.ListApps().StructuredContent!.Value.GetRawText(),
            ComputerUseWinToolResultFactory.PayloadJsonOptions)!;

        string firstExplorerA = firstPayload.Apps.Single(app => app.AppId == "explorer").Windows.Single(window => window.Hwnd == 101).WindowId;
        string secondExplorerA = secondPayload.Apps.Single(app => app.AppId == "explorer").Windows.Single(window => window.Hwnd == 101).WindowId;
        string firstExplorerB = firstPayload.Apps.Single(app => app.AppId == "explorer").Windows.Single(window => window.Hwnd == 202).WindowId;
        string secondExplorerB = secondPayload.Apps.Single(app => app.AppId == "explorer").Windows.Single(window => window.Hwnd == 202).WindowId;

        Assert.Equal(firstExplorerA, secondExplorerA);
        Assert.Equal(firstExplorerB, secondExplorerB);
    }

    [Fact]
    public void GetAppStateTargetResolverCarriesExecutionTargetForWindowIdHwndAndAttachedFallback()
    {
        IReadOnlyList<WindowDescriptor> windows =
        [
            CreateWindow(hwnd: 101, title: "Explorer A", processName: "explorer", processId: 1001, isForeground: false),
            CreateWindow(hwnd: 202, title: "Explorer B", processName: "explorer", processId: 1001, isForeground: true),
        ];

        using TempDirectoryScope temp = new();
        ComputerUseWinApprovalStore approvalStore = new(
            new ComputerUseWinOptions(
                PluginRoot: temp.Root,
                AppInstructionsRoot: Path.Combine(temp.Root, "references", "AppInstructions"),
                ApprovalStorePath: Path.Combine(temp.Root, "AppApprovals.json")));
        ComputerUseWinExecutionTargetCatalog executionTargetCatalog = new();
        ComputerUseWinAppDiscoveryService discoveryService = new(new FakeListAppsWindowManager(windows), approvalStore, executionTargetCatalog);
        ComputerUseWinDiscoveredApp explorer = Assert.Single(discoveryService.ListVisibleApps());
        string firstWindowId = explorer.Windows.Single(window => window.Window.Hwnd == 101).PublicWindowId!;
        string secondWindowId = explorer.Windows.Single(window => window.Window.Hwnd == 202).PublicWindowId!;

        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-stage-4-target-resolution-tests"));
        ComputerUseWinGetAppStateTargetResolution firstResolution = ComputerUseWinGetAppStateTargetResolver.Resolve(
            windows,
            executionTargetCatalog,
            sessionManager,
            windowId: firstWindowId,
            hwnd: null);
        ComputerUseWinGetAppStateTargetResolution secondResolution = ComputerUseWinGetAppStateTargetResolver.Resolve(
            windows,
            executionTargetCatalog,
            sessionManager,
            windowId: secondWindowId,
            hwnd: null);
        ComputerUseWinGetAppStateTargetResolution explicitResolution = ComputerUseWinGetAppStateTargetResolver.Resolve(
            windows,
            executionTargetCatalog,
            sessionManager,
            windowId: null,
            hwnd: 202);
        sessionManager.Attach(CreateWindow(hwnd: 101, title: "Explorer A", processName: "explorer", processId: 1001, isForeground: false), "computer-use-win");
        ComputerUseWinGetAppStateTargetResolution attachedResolution = ComputerUseWinGetAppStateTargetResolver.Resolve(
            windows,
            executionTargetCatalog,
            sessionManager,
            windowId: null,
            hwnd: null);

        Assert.True(firstResolution.IsSuccess);
        Assert.NotNull(firstResolution.Target);
        Assert.Equal(101, firstResolution.Window!.Hwnd);
        Assert.Equal(firstWindowId, firstResolution.Target!.PublicWindowId);
        Assert.Equal("explorer", firstResolution.Target.ApprovalKey.Value);
        Assert.True(secondResolution.IsSuccess);
        Assert.NotNull(secondResolution.Target);
        Assert.Equal(202, secondResolution.Window!.Hwnd);
        Assert.Equal(secondWindowId, secondResolution.Target!.PublicWindowId);
        Assert.True(explicitResolution.IsSuccess);
        Assert.NotNull(explicitResolution.Target);
        Assert.Equal(202, explicitResolution.Target!.Window.Hwnd);
        Assert.Equal(secondWindowId, explicitResolution.Target.PublicWindowId);
        Assert.True(attachedResolution.IsSuccess);
        Assert.NotNull(attachedResolution.Target);
        Assert.Equal(101, attachedResolution.Target!.Window.Hwnd);
        Assert.Equal(firstWindowId, attachedResolution.Target.PublicWindowId);
    }

    [Fact]
    public void GetAppStateTargetResolverFailsClosedForDuplicateExplicitHwndMatches()
    {
        WindowDescriptor window = CreateWindow(hwnd: 101, title: "Explorer A", processName: "explorer", processId: 1001, isForeground: false);
        WindowDescriptor duplicateWindow = window with { };
        ComputerUseWinExecutionTargetCatalog executionTargetCatalog = new();
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-duplicate-explicit-hwnd-tests"));

        Exception? exception = Record.Exception(() =>
        {
            ComputerUseWinGetAppStateTargetResolution resolution = ComputerUseWinGetAppStateTargetResolver.Resolve(
                [window, duplicateWindow],
                executionTargetCatalog,
                sessionManager,
                windowId: null,
                hwnd: 101);

            Assert.False(resolution.IsSuccess);
            Assert.Equal(ComputerUseWinFailureCodeValues.AmbiguousTarget, resolution.FailureCode);
        });

        Assert.Null(exception);
    }

    [Fact]
    public void GetAppStateTargetResolverFailsClosedForDuplicateAttachedHwndMatches()
    {
        WindowDescriptor window = CreateWindow(hwnd: 101, title: "Explorer A", processName: "explorer", processId: 1001, isForeground: false);
        WindowDescriptor duplicateWindow = window with { };
        ComputerUseWinExecutionTargetCatalog executionTargetCatalog = new();
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-duplicate-attached-hwnd-tests"));
        sessionManager.Attach(window, "computer-use-win");

        Exception? exception = Record.Exception(() =>
        {
            ComputerUseWinGetAppStateTargetResolution resolution = ComputerUseWinGetAppStateTargetResolver.Resolve(
                [window, duplicateWindow],
                executionTargetCatalog,
                sessionManager,
                windowId: null,
                hwnd: null);

            Assert.False(resolution.IsSuccess);
            Assert.Equal(ComputerUseWinFailureCodeValues.AmbiguousTarget, resolution.FailureCode);
        });

        Assert.Null(exception);
    }

    [Fact]
    public async Task GetAppStateApprovalRequiredForExplicitHwndWithoutDiscoveryDoesNotPublishWindowId()
    {
        WindowDescriptor window = CreateWindow(hwnd: 101, title: "Explorer A", processName: "explorer", processId: 1001);
        ComputerUseWinExecutionTargetCatalog executionTargetCatalog = new(TimeProvider.System, TimeSpan.FromMinutes(2), maxEntries: 16);
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-explicit-hwnd-public-selector-tests"));
        using TempDirectoryScope temp = new();
        ComputerUseWinApprovalStore approvalStore = CreateApprovalStore(temp);
        ComputerUseWinGetAppStateHandler handler = CreateGetAppStateHandler(
            new FakeListAppsWindowManager([window]),
            sessionManager,
            approvalStore,
            executionTargetCatalog);
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinGetAppState,
            new { hwnd = 101 },
            sessionManager.GetSnapshot());

        ModelContextProtocol.Protocol.CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinGetAppStateRequest(Hwnd: 101),
            CancellationToken.None);

        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.ApprovalRequired, payload.GetProperty("status").GetString());
        AssertWindowIdNotPublished(payload.GetProperty("session"));
    }

    [Fact]
    public async Task GetAppStateApprovalRequiredForAttachedFallbackWithoutDiscoveryDoesNotPublishWindowId()
    {
        WindowDescriptor window = CreateWindow(hwnd: 101, title: "Explorer A", processName: "explorer", processId: 1001);
        ComputerUseWinExecutionTargetCatalog executionTargetCatalog = new(TimeProvider.System, TimeSpan.FromMinutes(2), maxEntries: 16);
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-attached-public-selector-tests"));
        sessionManager.Attach(window, "computer-use-win");
        using TempDirectoryScope temp = new();
        ComputerUseWinApprovalStore approvalStore = CreateApprovalStore(temp);
        ComputerUseWinGetAppStateHandler handler = CreateGetAppStateHandler(
            new FakeListAppsWindowManager([window]),
            sessionManager,
            approvalStore,
            executionTargetCatalog);
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinGetAppState,
            new { },
            sessionManager.GetSnapshot());

        ModelContextProtocol.Protocol.CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinGetAppStateRequest(),
            CancellationToken.None);

        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.ApprovalRequired, payload.GetProperty("status").GetString());
        AssertWindowIdNotPublished(payload.GetProperty("session"));
    }

    [Fact]
    public async Task GetAppStateOmitsPublishedWindowIdWhenActivationDriftsDiscoverySnapshot()
    {
        WindowDescriptor discoveredWindow = CreateWindow(
            hwnd: 101,
            title: "Explorer A",
            processName: "explorer",
            processId: 1001,
            windowState: WindowStateValues.Minimized,
            isForeground: false);
        WindowDescriptor activatedWindow = discoveredWindow with
        {
            WindowState = WindowStateValues.Normal,
            IsForeground = true,
        };
        ComputerUseWinExecutionTargetCatalog executionTargetCatalog = new(TimeProvider.System, TimeSpan.FromMinutes(2), maxEntries: 16);
        string windowId = Assert.Single(executionTargetCatalog.Materialize([discoveredWindow])).PublicWindowId!;
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-activation-selector-revalidation-tests"));
        ComputerUseWinStateStore stateStore = new();
        using TempDirectoryScope temp = new();
        ComputerUseWinApprovalStore approvalStore = CreateApprovalStore(temp);
        approvalStore.Approve("explorer");
        FakeUiAutomationService uiAutomationService = new((window, request, _) => Task.FromResult(
            new UiaSnapshotResult(
                Status: UiaSnapshotStatusValues.Done,
                Window: CreateObservedWindow(window),
                Root: new UiaElementSnapshot
                {
                    ElementId = "root",
                    ControlType = "window",
                    BoundingRectangle = window.Bounds,
                },
                RequestedDepth: request.Depth,
                RequestedMaxNodes: request.MaxNodes,
                CapturedAtUtc: DateTimeOffset.UtcNow)));
        ComputerUseWinGetAppStateHandler handler = new(
            new FakeListAppsWindowManager([discoveredWindow]),
            sessionManager,
            approvalStore,
            executionTargetCatalog,
            stateStore,
            new FakeWindowActivationService(_ => ActivateWindowResult.Done(activatedWindow, wasMinimized: true, isForeground: true)),
            new ComputerUseWinAppStateObserver(
                new SuccessfulComputerUseWinCaptureService(),
                uiAutomationService,
                new EmptyInstructionProvider()));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinGetAppState,
            new { windowId },
            sessionManager.GetSnapshot());

        ModelContextProtocol.Protocol.CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinGetAppStateRequest(WindowId: windowId),
            CancellationToken.None);

        JsonElement payload = result.StructuredContent!.Value;
        Assert.False(result.IsError);
        Assert.Equal(ComputerUseWinStatusValues.Ok, payload.GetProperty("status").GetString());
        AssertWindowIdNotPublished(payload.GetProperty("session"));
        string stateToken = payload.GetProperty("stateToken").GetString()!;
        Assert.True(stateStore.TryGet(stateToken, out ComputerUseWinStoredState? storedState));
        Assert.NotNull(storedState);
        Assert.Null(storedState!.Session.WindowId);
        Assert.Equal(WindowStateValues.Normal, storedState.Window.WindowState);
    }

    [Theory]
    [InlineData(ActivateWindowStatusValues.Ambiguous)]
    [InlineData(ActivateWindowStatusValues.Failed)]
    public async Task GetAppStateOmitsPublishedWindowIdWhenActivationWarningPathDriftsDiscoverySnapshot(string activationStatus)
    {
        WindowDescriptor discoveredWindow = CreateWindow(
            hwnd: 101,
            title: "Explorer A",
            processName: "explorer",
            processId: 1001,
            windowState: WindowStateValues.Minimized,
            isForeground: false);
        WindowDescriptor activatedWindow = discoveredWindow with
        {
            WindowState = WindowStateValues.Normal,
            IsForeground = false,
        };
        ComputerUseWinExecutionTargetCatalog executionTargetCatalog = new(TimeProvider.System, TimeSpan.FromMinutes(2), maxEntries: 16);
        string windowId = Assert.Single(executionTargetCatalog.Materialize([discoveredWindow])).PublicWindowId!;
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-activation-warning-selector-revalidation-tests"));
        ComputerUseWinStateStore stateStore = new();
        using TempDirectoryScope temp = new();
        ComputerUseWinApprovalStore approvalStore = CreateApprovalStore(temp);
        approvalStore.Approve("explorer");
        FakeUiAutomationService uiAutomationService = new((window, request, _) => Task.FromResult(
            new UiaSnapshotResult(
                Status: UiaSnapshotStatusValues.Done,
                Window: CreateObservedWindow(window),
                Root: new UiaElementSnapshot
                {
                    ElementId = "root",
                    ControlType = "window",
                    BoundingRectangle = window.Bounds,
                },
                RequestedDepth: request.Depth,
                RequestedMaxNodes: request.MaxNodes,
                CapturedAtUtc: DateTimeOffset.UtcNow)));
        ComputerUseWinGetAppStateHandler handler = new(
            new FakeListAppsWindowManager([discoveredWindow]),
            sessionManager,
            approvalStore,
            executionTargetCatalog,
            stateStore,
            new FakeWindowActivationService(_ => CreateWarningActivationResult(activationStatus, activatedWindow)),
            new ComputerUseWinAppStateObserver(
                new SuccessfulComputerUseWinCaptureService(),
                uiAutomationService,
                new EmptyInstructionProvider()));
        AuditLogOptions auditOptions = CreateAuditOptions(temp.Root, $"computer-use-win-activation-warning-{activationStatus}-tests");
        AuditLog auditLog = new(auditOptions, TimeProvider.System);
        using AuditInvocationScope invocation = auditLog.BeginInvocation(
            ToolNames.ComputerUseWinGetAppState,
            new { windowId },
            sessionManager.GetSnapshot());

        ModelContextProtocol.Protocol.CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinGetAppStateRequest(WindowId: windowId),
            CancellationToken.None);

        JsonElement payload = result.StructuredContent!.Value;
        Assert.False(result.IsError);
        Assert.Equal(ComputerUseWinStatusValues.Ok, payload.GetProperty("status").GetString());
        AssertWindowIdNotPublished(payload.GetProperty("session"));
        Assert.Contains(payload.GetProperty("warnings").EnumerateArray(), item => item.GetString() == "activation warning");
        string stateToken = payload.GetProperty("stateToken").GetString()!;
        Assert.True(stateStore.TryGet(stateToken, out ComputerUseWinStoredState? storedState));
        Assert.NotNull(storedState);
        Assert.Null(storedState!.Session.WindowId);
        Assert.Equal(WindowStateValues.Normal, storedState.Window.WindowState);
        string completedEvent = File.ReadLines(auditOptions.EventsPath)
            .Single(line => line.Contains("\"event_name\":\"tool.invocation.completed\"", StringComparison.Ordinal));
        Assert.DoesNotContain("\"window_id\"", completedEvent, StringComparison.Ordinal);
        Assert.Contains("\"execution_target_id\"", completedEvent, StringComparison.Ordinal);
    }

    [Fact]
    public void GetAppStateTargetResolverRejectsWindowIdWhenLiveWindowSnapshotDrifts()
    {
        WindowDescriptor originalWindow = CreateWindow(hwnd: 101, title: "Original", processName: "explorer", processId: 1001, isForeground: false);
        WindowDescriptor driftedWindow = CreateWindow(
            hwnd: 101,
            title: "Original (updated)",
            processName: "explorer",
            processId: 1001,
            isForeground: true,
            bounds: new Bounds(40, 50, 800, 620));
        ComputerUseWinExecutionTargetCatalog executionTargetCatalog = new(TimeProvider.System, TimeSpan.FromMinutes(2), maxEntries: 16);
        string windowId = Assert.Single(executionTargetCatalog.Materialize([originalWindow])).PublicWindowId!;
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-window-id-reuse-tests"));

        ComputerUseWinGetAppStateTargetResolution resolution = ComputerUseWinGetAppStateTargetResolver.Resolve(
            [driftedWindow],
            executionTargetCatalog,
            sessionManager,
            windowId,
            hwnd: null);

        Assert.False(resolution.IsSuccess);
        Assert.Equal(ComputerUseWinFailureCodeValues.IdentityProofUnavailable, resolution.FailureCode);
    }

    [Fact]
    public void GetAppStateTargetResolverAllowsAttachedFallbackWhenLiveWindowTitleAndBoundsDrift()
    {
        WindowDescriptor attachedWindow = CreateWindow(hwnd: 101, title: "Original", processName: "explorer", processId: 1001, isForeground: false);
        WindowDescriptor driftedWindow = CreateWindow(
            hwnd: 101,
            title: "Original (updated)",
            processName: "explorer",
            processId: 1001,
            isForeground: true,
            bounds: new Bounds(40, 50, 800, 620));
        ComputerUseWinExecutionTargetCatalog executionTargetCatalog = new(TimeProvider.System, TimeSpan.FromMinutes(2), maxEntries: 16);
        _ = executionTargetCatalog.Materialize([attachedWindow]);
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-attached-reuse-tests"));
        sessionManager.Attach(attachedWindow, "computer-use-win");

        ComputerUseWinGetAppStateTargetResolution resolution = ComputerUseWinGetAppStateTargetResolver.Resolve(
            [driftedWindow],
            executionTargetCatalog,
            sessionManager,
            windowId: null,
            hwnd: null);

        Assert.True(resolution.IsSuccess);
        Assert.NotNull(resolution.Target);
        Assert.Equal(101, resolution.Target!.Window.Hwnd);
    }

    [Fact]
    public void GetAppStateTargetResolverRejectsAttachedFallbackWhenStableIdentityDiffers()
    {
        WindowDescriptor attachedWindow = CreateWindow(hwnd: 101, title: "Original", processName: "explorer", processId: 1001, isForeground: false);
        WindowDescriptor replacementWindow = CreateWindow(
            hwnd: 101,
            title: "Original",
            processName: "explorer",
            processId: 1001,
            threadId: 9999,
            isForeground: true);
        ComputerUseWinExecutionTargetCatalog executionTargetCatalog = new(TimeProvider.System, TimeSpan.FromMinutes(2), maxEntries: 16);
        _ = executionTargetCatalog.Materialize([attachedWindow]);
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-attached-replacement-tests"));
        sessionManager.Attach(attachedWindow, "computer-use-win");

        ComputerUseWinGetAppStateTargetResolution resolution = ComputerUseWinGetAppStateTargetResolver.Resolve(
            [replacementWindow],
            executionTargetCatalog,
            sessionManager,
            windowId: null,
            hwnd: null);

        Assert.False(resolution.IsSuccess);
        Assert.Equal(ComputerUseWinFailureCodeValues.IdentityProofUnavailable, resolution.FailureCode);
    }

    [Fact]
    public void StoredStateResolverMaterializesObservedActionReadyStateForLiveStoredWindow()
    {
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateSafeStoredState());
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-stage-7-action-ready-tests"));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinClick,
            new { stateToken = token, elementIndex = 1 },
            sessionManager.GetSnapshot());
        ComputerUseWinStoredStateResolver resolver = new(
            stateStore,
            new FakeListAppsWindowManager([CreateWindow()]));

        bool success = resolver.TryResolve(
            token,
            invocation,
            ToolNames.ComputerUseWinClick,
            ComputerUseWinStoredStateValidationMode.SemanticElementAction,
            out ComputerUseWinActionReadyState? actionReadyState,
            out ModelContextProtocol.Protocol.CallToolResult? failureResult);

        Assert.True(success);
        Assert.Null(failureResult);
        Assert.NotNull(actionReadyState);
        Assert.Equal(ComputerUseWinRuntimeStateKind.Observed, actionReadyState!.RuntimeState.Kind);
        Assert.True(ComputerUseWinRuntimeStateModel.CanExecuteAction(actionReadyState.RuntimeState));
        Assert.Equal(101, actionReadyState.StoredState.Window.Hwnd);
    }

    [Fact]
    public void StoredStateResolverAllowsObservedWindowDriftWhenStableInstanceRemains()
    {
        WindowDescriptor originalWindow = CreateWindow(hwnd: 101, title: "Original", processName: "explorer", processId: 1001, isForeground: false);
        WindowDescriptor driftedWindow = CreateWindow(
            hwnd: 101,
            title: "Original (updated)",
            processName: "explorer",
            processId: 1001,
            isForeground: true,
            bounds: new Bounds(40, 50, 800, 620));
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateStoredState() with
        {
            Session = new ComputerUseWinAppSession("explorer", "cw_test_window", 101, originalWindow.Title, originalWindow.ProcessName, originalWindow.ProcessId),
            Window = originalWindow,
        });
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-stage-7-stale-window-tests"));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinClick,
            new { stateToken = token, elementIndex = 1 },
            sessionManager.GetSnapshot());
        ComputerUseWinStoredStateResolver resolver = new(
            stateStore,
            new FakeListAppsWindowManager([driftedWindow]));

        bool success = resolver.TryResolve(
            token,
            invocation,
            ToolNames.ComputerUseWinClick,
            ComputerUseWinStoredStateValidationMode.SemanticElementAction,
            out ComputerUseWinActionReadyState? actionReadyState,
            out ModelContextProtocol.Protocol.CallToolResult? failureResult);

        Assert.True(success);
        Assert.NotNull(actionReadyState);
        Assert.Null(failureResult);
        Assert.Equal("Original (updated)", actionReadyState!.StoredState.Window.Title);
    }

    [Fact]
    public void StoredStateResolverRejectsReplacementWindowWhenObservedStableIdentityDiffers()
    {
        WindowDescriptor originalWindow = CreateWindow(hwnd: 101, title: "Original", processName: "explorer", processId: 1001, isForeground: false);
        WindowDescriptor replacementWindow = CreateWindow(
            hwnd: 101,
            title: "Original",
            processName: "explorer",
            processId: 1001,
            threadId: 9999,
            isForeground: true);
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateStoredState() with
        {
            Session = new ComputerUseWinAppSession("explorer", "cw_test_window", 101, originalWindow.Title, originalWindow.ProcessName, originalWindow.ProcessId),
            Window = originalWindow,
        });
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-stage-7-replacement-window-tests"));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinClick,
            new { stateToken = token, elementIndex = 1 },
            sessionManager.GetSnapshot());
        ComputerUseWinStoredStateResolver resolver = new(
            stateStore,
            new FakeListAppsWindowManager([replacementWindow]));

        bool success = resolver.TryResolve(
            token,
            invocation,
            ToolNames.ComputerUseWinClick,
            ComputerUseWinStoredStateValidationMode.SemanticElementAction,
            out ComputerUseWinActionReadyState? actionReadyState,
            out ModelContextProtocol.Protocol.CallToolResult? failureResult);

        Assert.False(success);
        Assert.Null(actionReadyState);
        Assert.NotNull(failureResult);
        Assert.Equal(ComputerUseWinFailureCodeValues.StaleState, failureResult!.StructuredContent!.Value.GetProperty("failureCode").GetString());
    }

    [Fact]
    public void StoredStateResolverFailsClosedWhenLiveWindowResolutionHasDuplicateStrictMatches()
    {
        WindowDescriptor originalWindow = CreateWindow(hwnd: 101, title: "Original", processName: "explorer", processId: 1001, isForeground: false);
        WindowDescriptor duplicateWindow = originalWindow with { };
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateStoredState() with
        {
            Session = new ComputerUseWinAppSession("explorer", "cw_test_window", 101, originalWindow.Title, originalWindow.ProcessName, originalWindow.ProcessId),
            Window = originalWindow,
        });
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-stage-7-duplicate-live-window-tests"));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinClick,
            new { stateToken = token, elementIndex = 1 },
            sessionManager.GetSnapshot());
        ComputerUseWinStoredStateResolver resolver = new(
            stateStore,
            new FakeListAppsWindowManager([originalWindow, duplicateWindow]));

        Exception? exception = Record.Exception(() =>
        {
            bool success = resolver.TryResolve(
                token,
                invocation,
                ToolNames.ComputerUseWinClick,
                ComputerUseWinStoredStateValidationMode.SemanticElementAction,
                out ComputerUseWinActionReadyState? actionReadyState,
                out ModelContextProtocol.Protocol.CallToolResult? failureResult);

            Assert.False(success);
            Assert.Null(actionReadyState);
            Assert.NotNull(failureResult);
            Assert.Equal(ComputerUseWinFailureCodeValues.AmbiguousTarget, failureResult!.StructuredContent!.Value.GetProperty("failureCode").GetString());
        });

        Assert.Null(exception);
    }

    [Fact]
    public void StoredStateResolverRejectsCoordinateActionWhenObservedWindowGeometryDrifts()
    {
        WindowDescriptor originalWindow = CreateWindow(hwnd: 101, title: "Original", processName: "explorer", processId: 1001, isForeground: false);
        WindowDescriptor driftedWindow = CreateWindow(
            hwnd: 101,
            title: "Original (updated)",
            processName: "explorer",
            processId: 1001,
            isForeground: true,
            bounds: new Bounds(40, 50, 800, 620));
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateStoredState() with
        {
            Session = new ComputerUseWinAppSession("explorer", "cw_test_window", 101, originalWindow.Title, originalWindow.ProcessName, originalWindow.ProcessId),
            Window = originalWindow,
        });
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-stage-7-coordinate-window-tests"));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinClick,
            new { stateToken = token, point = new { x = 20, y = 30 }, confirm = true },
            sessionManager.GetSnapshot());
        ComputerUseWinStoredStateResolver resolver = new(
            stateStore,
            new FakeListAppsWindowManager([driftedWindow]));

        bool success = resolver.TryResolve(
            token,
            invocation,
            ToolNames.ComputerUseWinClick,
            ComputerUseWinStoredStateValidationMode.CoordinateCapturePixelsAction,
            out ComputerUseWinActionReadyState? actionReadyState,
            out ModelContextProtocol.Protocol.CallToolResult? failureResult);

        Assert.False(success);
        Assert.Null(actionReadyState);
        Assert.NotNull(failureResult);
        Assert.Equal(ComputerUseWinFailureCodeValues.StaleState, failureResult!.StructuredContent!.Value.GetProperty("failureCode").GetString());
    }

    [Theory]
    [InlineData(null, ComputerUseWinFailureCodeValues.StateRequired)]
    [InlineData("missing-token", ComputerUseWinFailureCodeValues.StaleState)]
    public async Task ClickHandlerRecordsObserveAfterRequestWhenStateResolutionFails(
        string? stateToken,
        string expectedFailureCode)
    {
        string root = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            AuditLogOptions options = CreateAuditOptions(root, "computer-use-win-click-observe-after-resolution-failure-tests");
            AuditLog auditLog = new(options, TimeProvider.System);
            ComputerUseWinStateStore stateStore = new();
            InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-click-observe-after-resolution-failure-tests"));
            using AuditInvocationScope invocation = auditLog.BeginInvocation(
                ToolNames.ComputerUseWinClick,
                new { stateToken, elementIndex = 1, observeAfter = true },
                sessionManager.GetSnapshot());
            FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
            FakeInputService inputService = new();
            ComputerUseWinClickHandler handler = new(
                new ComputerUseWinActionRequestExecutor(
                    new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow()]))),
                new ComputerUseWinClickExecutionCoordinator(
                    activationService,
                    new ComputerUseWinClickTargetResolver(new FakeUiAutomationService()),
                    inputService));

            CallToolResult result = await handler.ExecuteAsync(
                invocation,
                new ComputerUseWinClickRequest(StateToken: stateToken, ElementIndex: 1, ObserveAfter: true),
                CancellationToken.None);

            JsonElement payload = result.StructuredContent!.Value;
            Assert.Equal(ComputerUseWinStatusValues.Failed, payload.GetProperty("status").GetString());
            Assert.Equal(expectedFailureCode, payload.GetProperty("failureCode").GetString());
            Assert.Equal(0, inputService.Calls);

            string actionEvent = File.ReadLines(options.EventsPath)
                .Single(line => line.Contains("\"event_name\":\"computer_use_win.action.completed\"", StringComparison.Ordinal));
            Assert.Contains("\"observe_after_requested\":\"true\"", actionEvent, StringComparison.Ordinal);

            string actionArtifactPath = Directory
                .GetFiles(Path.Combine(options.RunDirectory, "computer-use-win"), "action-*.json", SearchOption.TopDirectoryOnly)
                .Single();
            using JsonDocument artifact = JsonDocument.Parse(File.ReadAllText(actionArtifactPath));
            Assert.True(artifact.RootElement.GetProperty("observe_after_requested").GetBoolean());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ClickHandlerReportsCaptureReferenceRequiredWhenLiveStateLacksCaptureProof()
    {
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateStoredStateWithoutCaptureReference());
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-capture-proof-taxonomy-tests"));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinClick,
            new { stateToken = token, point = new { x = 20, y = 30 }, confirm = true, observeAfter = true },
            sessionManager.GetSnapshot());
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeInputService inputService = new((_, _, _) => Task.FromResult(
            new InputResult(
                Status: InputStatusValues.Done,
                Decision: InputStatusValues.Done)));
        ComputerUseWinClickHandler handler = new(
            new ComputerUseWinActionRequestExecutor(
                new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow()]))),
            new ComputerUseWinClickExecutionCoordinator(
                activationService,
                new ComputerUseWinClickTargetResolver(new FakeUiAutomationService()),
                inputService));

        ModelContextProtocol.Protocol.CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinClickRequest(StateToken: token, Point: new InputPoint(20, 30), Confirm: true, ObserveAfter: true),
            CancellationToken.None);

        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.Equal(ComputerUseWinFailureCodeValues.CaptureReferenceRequired, payload.GetProperty("failureCode").GetString());
        Assert.True(payload.GetProperty("refreshStateRecommended").GetBoolean());
        Assert.False(payload.TryGetProperty("successorState", out _));
        Assert.False(payload.TryGetProperty("successorStateFailure", out _));
        Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        Assert.Null(activationService.LastHwnd);
        Assert.Equal(0, inputService.Calls);
    }

    [Fact]
    public async Task ClickHandlerEmbedsSuccessorStateAndImageWhenObserveAfterSucceeds()
    {
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateSafeStoredState());
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-click-observe-after-success-tests"));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinClick,
            new { stateToken = token, elementIndex = 1, observeAfter = true },
            sessionManager.GetSnapshot());
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeInputService inputService = new((request, _, _) => Task.FromResult(
            new InputResult(
                Status: InputStatusValues.VerifyNeeded,
                Decision: InputStatusValues.VerifyNeeded,
                ResultMode: InputResultModeValues.DispatchOnly,
                Reason: "Проверь результат клика по приложению вручную.",
                TargetHwnd: request.Hwnd,
                CompletedActionCount: 1)));
        FakeUiAutomationService uiAutomationService = new((window, request, _) => Task.FromResult(
            new UiaSnapshotResult(
                Status: UiaSnapshotStatusValues.Done,
                Window: CreateObservedWindow(window),
                Root: CreateClickSnapshotRoot(),
                RequestedDepth: request.Depth,
                RequestedMaxNodes: request.MaxNodes,
                CapturedAtUtc: DateTimeOffset.UtcNow)));
        ComputerUseWinClickHandler handler = new(
            new ComputerUseWinActionRequestExecutor(
                new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow()])),
                new ComputerUseWinAppStateObserver(
                    new SuccessfulComputerUseWinCaptureService(),
                    uiAutomationService,
                    new EmptyInstructionProvider()),
                stateStore,
                sessionManager),
            new ComputerUseWinClickExecutionCoordinator(
                activationService,
                new ComputerUseWinClickTargetResolver(uiAutomationService),
                inputService));

        CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinClickRequest(StateToken: token, ElementIndex: 1, Confirm: false, ObserveAfter: true),
            CancellationToken.None);

        Assert.False(result.IsError, result.StructuredContent!.Value.GetRawText());
        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.VerifyNeeded, payload.GetProperty("status").GetString());
        Assert.False(payload.GetProperty("refreshStateRecommended").GetBoolean());
        Assert.False(payload.TryGetProperty("successorStateFailure", out _));
        JsonElement successorState = payload.GetProperty("successorState");
        Assert.Equal(ComputerUseWinStatusValues.Ok, successorState.GetProperty("status").GetString());
        Assert.Equal(101, successorState.GetProperty("session").GetProperty("hwnd").GetInt64());
        string successorToken = successorState.GetProperty("stateToken").GetString()!;
        Assert.NotEqual(token, successorToken);
        Assert.True(stateStore.TryGet(successorToken, out ComputerUseWinStoredState? successorStoredState));
        Assert.NotNull(successorStoredState);
        Assert.IsType<TextContentBlock>(result.Content[0]);
        ImageContentBlock imageBlock = Assert.IsType<ImageContentBlock>(result.Content[1]);
        Assert.Equal("image/png", imageBlock.MimeType);
        Assert.Equal(1, inputService.Calls);
    }

    [Fact]
    public async Task ClickHandlerKeepsCommittedActionOutcomeWhenObserveAfterFails()
    {
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateSafeStoredState());
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-click-observe-after-failure-tests"));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinClick,
            new { stateToken = token, elementIndex = 1, observeAfter = true },
            sessionManager.GetSnapshot());
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeInputService inputService = new((request, _, _) => Task.FromResult(
            new InputResult(
                Status: InputStatusValues.VerifyNeeded,
                Decision: InputStatusValues.VerifyNeeded,
                ResultMode: InputResultModeValues.DispatchOnly,
                Reason: "Проверь результат клика по приложению вручную.",
                TargetHwnd: request.Hwnd,
                CompletedActionCount: 1)));
        FakeUiAutomationService uiAutomationService = new((window, request, _) => Task.FromResult(
            new UiaSnapshotResult(
                Status: UiaSnapshotStatusValues.Done,
                Window: CreateObservedWindow(window),
                Root: CreateClickSnapshotRoot(),
                RequestedDepth: request.Depth,
                RequestedMaxNodes: request.MaxNodes,
                CapturedAtUtc: DateTimeOffset.UtcNow)));
        ComputerUseWinClickHandler handler = new(
            new ComputerUseWinActionRequestExecutor(
                new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow()])),
                new ComputerUseWinAppStateObserver(
                    new ThrowingComputerUseWinCaptureService(new CaptureOperationException("post-action capture failed")),
                    uiAutomationService,
                    new EmptyInstructionProvider()),
                stateStore,
                sessionManager),
            new ComputerUseWinClickExecutionCoordinator(
                activationService,
                new ComputerUseWinClickTargetResolver(uiAutomationService),
                inputService));

        CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinClickRequest(StateToken: token, ElementIndex: 1, Confirm: false, ObserveAfter: true),
            CancellationToken.None);

        Assert.False(result.IsError, result.StructuredContent!.Value.GetRawText());
        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.VerifyNeeded, payload.GetProperty("status").GetString());
        Assert.True(payload.GetProperty("refreshStateRecommended").GetBoolean());
        Assert.False(payload.TryGetProperty("successorState", out _));
        JsonElement successorFailure = payload.GetProperty("successorStateFailure");
        Assert.Equal(ComputerUseWinFailureCodeValues.ObservationFailed, successorFailure.GetProperty("failureCode").GetString());
        Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        Assert.Equal(1, inputService.Calls);
    }

    [Fact]
    public async Task ClickHandlerSanitizesSuccessorObservationFailureReason()
    {
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateSafeStoredState());
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-click-observe-after-safe-failure-reason-tests"));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinClick,
            new { stateToken = token, elementIndex = 1, observeAfter = true },
            sessionManager.GetSnapshot());
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeInputService inputService = new((request, _, _) => Task.FromResult(
            new InputResult(
                Status: InputStatusValues.VerifyNeeded,
                Decision: InputStatusValues.VerifyNeeded,
                ResultMode: InputResultModeValues.DispatchOnly,
                Reason: "Проверь результат клика по приложению вручную.",
                TargetHwnd: request.Hwnd,
                CompletedActionCount: 1)));
        int snapshotCalls = 0;
        FakeUiAutomationService uiAutomationService = new((window, request, _) =>
        {
            snapshotCalls++;
            if (snapshotCalls == 1)
            {
                return Task.FromResult(
                    new UiaSnapshotResult(
                        Status: UiaSnapshotStatusValues.Done,
                        Window: CreateObservedWindow(window),
                        Root: CreateClickSnapshotRoot(),
                        RequestedDepth: request.Depth,
                        RequestedMaxNodes: request.MaxNodes,
                        CapturedAtUtc: DateTimeOffset.UtcNow));
            }

            return Task.FromResult(
                new UiaSnapshotResult(
                    Status: UiaSnapshotStatusValues.Failed,
                    Window: CreateObservedWindow(window),
                    Reason: "secret traversal failure",
                    RequestedDepth: request.Depth,
                    RequestedMaxNodes: request.MaxNodes,
                    CapturedAtUtc: DateTimeOffset.UtcNow));
        });
        ComputerUseWinClickHandler handler = new(
            new ComputerUseWinActionRequestExecutor(
                new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow()])),
                new ComputerUseWinAppStateObserver(
                    new SuccessfulComputerUseWinCaptureService(),
                    uiAutomationService,
                    new EmptyInstructionProvider()),
                stateStore,
                sessionManager),
            new ComputerUseWinClickExecutionCoordinator(
                activationService,
                new ComputerUseWinClickTargetResolver(uiAutomationService),
                inputService));

        CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinClickRequest(StateToken: token, ElementIndex: 1, Confirm: false, ObserveAfter: true),
            CancellationToken.None);

        Assert.False(result.IsError, result.StructuredContent!.Value.GetRawText());
        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.VerifyNeeded, payload.GetProperty("status").GetString());
        Assert.True(payload.GetProperty("refreshStateRecommended").GetBoolean());
        JsonElement successorFailure = payload.GetProperty("successorStateFailure");
        Assert.Equal(ComputerUseWinFailureCodeValues.ObservationFailed, successorFailure.GetProperty("failureCode").GetString());
        string reason = successorFailure.GetProperty("reason").GetString()!;
        Assert.DoesNotContain("secret traversal failure", reason, StringComparison.Ordinal);
        Assert.Contains("get_app_state", reason, StringComparison.Ordinal);
        Assert.Equal(2, snapshotCalls);
        Assert.Equal(1, inputService.Calls);
    }

    [Fact]
    public async Task ClickHandlerRecordsObserveAfterRequestWhenActionFailsBeforeDispatch()
    {
        string root = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            AuditLogOptions options = CreateAuditOptions(root, "computer-use-win-click-observe-after-failure-observability-tests");
            AuditLog auditLog = new(options, TimeProvider.System);
            ComputerUseWinStateStore stateStore = new();
            string token = stateStore.Create(CreateSafeStoredState());
            InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-click-observe-after-failure-observability-tests"));
            using AuditInvocationScope invocation = auditLog.BeginInvocation(
                ToolNames.ComputerUseWinClick,
                new { stateToken = token, elementIndex = 1, observeAfter = true },
                sessionManager.GetSnapshot());
            FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
            FakeUiAutomationService uiAutomationService = new((window, request, _) => Task.FromResult(
                new UiaSnapshotResult(
                    Status: UiaSnapshotStatusValues.Failed,
                    Window: CreateObservedWindow(window),
                    Reason: "secret observeAfter request failure",
                    RequestedDepth: request.Depth,
                    RequestedMaxNodes: request.MaxNodes,
                    CapturedAtUtc: DateTimeOffset.UtcNow)));
            FakeInputService inputService = new();
            ComputerUseWinClickHandler handler = new(
                new ComputerUseWinActionRequestExecutor(
                    new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow()]))),
                new ComputerUseWinClickExecutionCoordinator(
                    activationService,
                    new ComputerUseWinClickTargetResolver(uiAutomationService),
                    inputService));

            CallToolResult result = await handler.ExecuteAsync(
                invocation,
                new ComputerUseWinClickRequest(StateToken: token, ElementIndex: 1, Confirm: false, ObserveAfter: true),
                CancellationToken.None);

            JsonElement payload = result.StructuredContent!.Value;
            Assert.Equal(ComputerUseWinStatusValues.Failed, payload.GetProperty("status").GetString());
            Assert.Equal(ComputerUseWinFailureCodeValues.ObservationFailed, payload.GetProperty("failureCode").GetString());
            Assert.Equal(0, inputService.Calls);

            string actionEvent = File.ReadLines(options.EventsPath)
                .Single(line => line.Contains("\"event_name\":\"computer_use_win.action.completed\"", StringComparison.Ordinal));
            Assert.Contains("\"observe_after_requested\":\"true\"", actionEvent, StringComparison.Ordinal);

            string actionArtifactPath = Directory
                .GetFiles(Path.Combine(options.RunDirectory, "computer-use-win"), "action-*.json", SearchOption.TopDirectoryOnly)
                .Single();
            using JsonDocument artifact = JsonDocument.Parse(File.ReadAllText(actionArtifactPath));
            Assert.True(artifact.RootElement.GetProperty("observe_after_requested").GetBoolean());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ClickHandlerObservesSuccessorStateFromPostActionLiveWindow()
    {
        WindowDescriptor originalWindow = CreateWindow(title: "Original title");
        WindowDescriptor postActionWindow = CreateWindow(title: "Post action title");
        SequencedListAppsWindowManager windowManager = new([originalWindow], [postActionWindow]);
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateSafeStoredState() with
        {
            Session = new ComputerUseWinAppSession("explorer", "cw_test_window", originalWindow.Hwnd, originalWindow.Title, originalWindow.ProcessName, originalWindow.ProcessId),
            Window = originalWindow,
        });
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-click-observe-after-live-window-tests"));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinClick,
            new { stateToken = token, elementIndex = 1, observeAfter = true },
            sessionManager.GetSnapshot());
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeInputService inputService = new((request, _, _) => Task.FromResult(
            new InputResult(
                Status: InputStatusValues.VerifyNeeded,
                Decision: InputStatusValues.VerifyNeeded,
                ResultMode: InputResultModeValues.DispatchOnly,
                Reason: "Проверь результат клика по приложению вручную.",
                TargetHwnd: request.Hwnd,
                CompletedActionCount: 1)));
        FakeUiAutomationService uiAutomationService = new((window, request, _) => Task.FromResult(
            new UiaSnapshotResult(
                Status: UiaSnapshotStatusValues.Done,
                Window: CreateObservedWindow(window),
                Root: CreateClickSnapshotRoot(),
                RequestedDepth: request.Depth,
                RequestedMaxNodes: request.MaxNodes,
                CapturedAtUtc: DateTimeOffset.UtcNow)));
        ComputerUseWinClickHandler handler = new(
            new ComputerUseWinActionRequestExecutor(
                new ComputerUseWinStoredStateResolver(stateStore, windowManager),
                new ComputerUseWinAppStateObserver(
                    new SuccessfulComputerUseWinCaptureService(),
                    uiAutomationService,
                    new EmptyInstructionProvider()),
                stateStore,
                sessionManager),
            new ComputerUseWinClickExecutionCoordinator(
                activationService,
                new ComputerUseWinClickTargetResolver(uiAutomationService),
                inputService));

        CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinClickRequest(StateToken: token, ElementIndex: 1, Confirm: false, ObserveAfter: true),
            CancellationToken.None);

        Assert.False(result.IsError, result.StructuredContent!.Value.GetRawText());
        JsonElement payload = result.StructuredContent!.Value;
        Assert.False(payload.GetProperty("refreshStateRecommended").GetBoolean());
        JsonElement successorState = payload.GetProperty("successorState");
        JsonElement session = successorState.GetProperty("session");
        Assert.Equal(postActionWindow.Title, session.GetProperty("title").GetString());
        AssertWindowIdNotPublished(session);
        string successorToken = successorState.GetProperty("stateToken").GetString()!;
        Assert.True(stateStore.TryGet(successorToken, out ComputerUseWinStoredState? successorStoredState));
        Assert.NotNull(successorStoredState);
        Assert.Equal(postActionWindow.Title, successorStoredState!.Window.Title);
        Assert.Null(successorStoredState.Session.WindowId);
        Assert.True(windowManager.ListCalls >= 2);
    }

    [Fact]
    public async Task ClickHandlerKeepsCommittedActionOutcomeWhenSuccessorMaterializationThrows()
    {
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateSafeStoredState());
        ThrowingAttachSessionManager sessionManager = new("computer-use-win-click-observe-after-throw-tests");
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinClick,
            new { stateToken = token, elementIndex = 1, observeAfter = true },
            sessionManager.GetSnapshot());
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeInputService inputService = new((request, _, _) => Task.FromResult(
            new InputResult(
                Status: InputStatusValues.VerifyNeeded,
                Decision: InputStatusValues.VerifyNeeded,
                ResultMode: InputResultModeValues.DispatchOnly,
                Reason: "Проверь результат клика по приложению вручную.",
                TargetHwnd: request.Hwnd,
                CompletedActionCount: 1)));
        FakeUiAutomationService uiAutomationService = new((window, request, _) => Task.FromResult(
            new UiaSnapshotResult(
                Status: UiaSnapshotStatusValues.Done,
                Window: CreateObservedWindow(window),
                Root: CreateClickSnapshotRoot(),
                RequestedDepth: request.Depth,
                RequestedMaxNodes: request.MaxNodes,
                CapturedAtUtc: DateTimeOffset.UtcNow)));
        ComputerUseWinClickHandler handler = new(
            new ComputerUseWinActionRequestExecutor(
                new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow()])),
                new ComputerUseWinAppStateObserver(
                    new SuccessfulComputerUseWinCaptureService(),
                    uiAutomationService,
                    new EmptyInstructionProvider()),
                stateStore,
                sessionManager),
            new ComputerUseWinClickExecutionCoordinator(
                activationService,
                new ComputerUseWinClickTargetResolver(uiAutomationService),
                inputService));

        CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinClickRequest(StateToken: token, ElementIndex: 1, Confirm: false, ObserveAfter: true),
            CancellationToken.None);

        Assert.False(result.IsError, result.StructuredContent!.Value.GetRawText());
        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.VerifyNeeded, payload.GetProperty("status").GetString());
        Assert.True(payload.GetProperty("refreshStateRecommended").GetBoolean());
        Assert.False(payload.TryGetProperty("successorState", out _));
        JsonElement successorFailure = payload.GetProperty("successorStateFailure");
        Assert.Equal(ComputerUseWinFailureCodeValues.UnexpectedInternalFailure, successorFailure.GetProperty("failureCode").GetString());
        Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        Assert.Equal(1, inputService.Calls);
    }

    [Fact]
    public async Task PressKeyHandlerRequiresConfirmationForDangerousCombo()
    {
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateStoredState());
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-press-key-confirmation-tests"));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinPressKey,
            new { stateToken = token, key = "alt+f4", confirm = false },
            sessionManager.GetSnapshot());
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeInputService inputService = new((_, _, _) => Task.FromResult(
            new InputResult(
                Status: InputStatusValues.Done,
                Decision: InputStatusValues.Done)));
        ComputerUseWinPressKeyHandler handler = new(
            new ComputerUseWinActionRequestExecutor(
                new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow()]))),
            new ComputerUseWinPressKeyExecutionCoordinator(
                activationService,
                inputService));

        ModelContextProtocol.Protocol.CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinPressKeyRequest(StateToken: token, Key: "alt+f4", Repeat: 1, Confirm: false),
            CancellationToken.None);

        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.ApprovalRequired, payload.GetProperty("status").GetString());
        Assert.False(payload.GetProperty("refreshStateRecommended").GetBoolean());
        Assert.Equal(0, inputService.Calls);
        Assert.Null(activationService.LastHwnd);
    }

    [Fact]
    public async Task PressKeyHandlerDispatchesNormalizedShortcutThroughInputService()
    {
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateStoredState());
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-press-key-success-tests"));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinPressKey,
            new { stateToken = token, key = "CTRL+S", repeat = 2, confirm = true },
            sessionManager.GetSnapshot());
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeInputService inputService = new((request, _, _) => Task.FromResult(
            new InputResult(
                Status: InputStatusValues.VerifyNeeded,
                Decision: InputStatusValues.VerifyNeeded,
                ResultMode: InputResultModeValues.DispatchOnly,
                TargetHwnd: request.Hwnd,
                CompletedActionCount: 1)));
        ComputerUseWinPressKeyHandler handler = new(
            new ComputerUseWinActionRequestExecutor(
                new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow()]))),
            new ComputerUseWinPressKeyExecutionCoordinator(
                activationService,
                inputService));

        ModelContextProtocol.Protocol.CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinPressKeyRequest(StateToken: token, Key: "CTRL+S", Repeat: 2, Confirm: true),
            CancellationToken.None);

        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.VerifyNeeded, payload.GetProperty("status").GetString());
        Assert.NotNull(inputService.LastRequest);
        InputAction action = Assert.Single(inputService.LastRequest!.Actions);
        Assert.Equal(InputActionTypeValues.Keypress, action.Type);
        Assert.Equal("ctrl+s", action.Key);
        Assert.Equal(2, action.Repeat);
        Assert.Equal(1, inputService.Calls);
        Assert.Equal(101, activationService.LastHwnd);
    }

    [Fact]
    public async Task PressKeyHandlerReturnsStructuredFailureWhenRuntimeLosesForeground()
    {
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateStoredState());
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-press-key-foreground-failure-tests"));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinPressKey,
            new { stateToken = token, key = "CTRL+S", repeat = 1, confirm = true },
            sessionManager.GetSnapshot());
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeInputService inputService = new((request, _, _) => Task.FromResult(
            new InputResult(
                Status: InputStatusValues.Failed,
                Decision: InputStatusValues.Failed,
                FailureCode: InputFailureCodeValues.TargetNotForeground,
                Reason: "target not foreground",
                TargetHwnd: request.Hwnd,
                FailedActionIndex: 0)));
        ComputerUseWinPressKeyHandler handler = new(
            new ComputerUseWinActionRequestExecutor(
                new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow()]))),
            new ComputerUseWinPressKeyExecutionCoordinator(
                activationService,
                inputService));

        ModelContextProtocol.Protocol.CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinPressKeyRequest(StateToken: token, Key: "CTRL+S", Repeat: 1, Confirm: true),
            CancellationToken.None);

        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.Equal(ComputerUseWinFailureCodeValues.TargetNotForeground, payload.GetProperty("failureCode").GetString());
        Assert.True(payload.GetProperty("refreshStateRecommended").GetBoolean());
    }

    [Fact]
    public async Task PressKeyHandlerReturnsStructuredFailureWhenRuntimeDispatchFails()
    {
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateStoredState());
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-press-key-dispatch-failure-tests"));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinPressKey,
            new { stateToken = token, key = "CTRL+S", repeat = 1, confirm = true },
            sessionManager.GetSnapshot());
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeInputService inputService = new((request, _, _) => Task.FromResult(
            new InputResult(
                Status: InputStatusValues.Failed,
                Decision: InputStatusValues.Failed,
                FailureCode: InputFailureCodeValues.InputDispatchFailed,
                Reason: "keypress dispatch failed",
                TargetHwnd: request.Hwnd,
                FailedActionIndex: 0)));
        ComputerUseWinPressKeyHandler handler = new(
            new ComputerUseWinActionRequestExecutor(
                new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow()]))),
            new ComputerUseWinPressKeyExecutionCoordinator(
                activationService,
                inputService));

        ModelContextProtocol.Protocol.CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinPressKeyRequest(StateToken: token, Key: "CTRL+S", Repeat: 1, Confirm: true),
            CancellationToken.None);

        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.Equal(ComputerUseWinFailureCodeValues.InputDispatchFailed, payload.GetProperty("failureCode").GetString());
        Assert.True(payload.GetProperty("refreshStateRecommended").GetBoolean());
    }

    [Fact]
    public async Task SetValueHandlerReturnsUnsupportedActionForNonSettableStoredElement()
    {
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateNonActionableStoredState());
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-set-value-unsupported-tests"));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinSetValue,
            new { stateToken = token, elementIndex = 1, valueKind = "text", textValue = "value" },
            sessionManager.GetSnapshot());
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeUiAutomationService uiAutomationService = new();
        FakeUiAutomationSetValueService setValueService = new();
        ComputerUseWinSetValueHandler handler = new(
            new ComputerUseWinActionRequestExecutor(
                new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow()]))),
            new ComputerUseWinSetValueExecutionCoordinator(
                activationService,
                uiAutomationService,
                setValueService));

        ModelContextProtocol.Protocol.CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinSetValueRequest(StateToken: token, ElementIndex: 1, ValueKind: "text", TextValue: "value", NumberValue: null, Confirm: false),
            CancellationToken.None);

        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.Equal(ComputerUseWinFailureCodeValues.UnsupportedAction, payload.GetProperty("failureCode").GetString());
        Assert.Equal(0, uiAutomationService.Calls);
        Assert.Equal(0, setValueService.Calls);
    }

    [Fact]
    public async Task SetValueHandlerReturnsStaleStateWhenFreshElementCannotBeMatched()
    {
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateSettableStoredState());
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-set-value-stale-tests"));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinSetValue,
            new { stateToken = token, elementIndex = 1, valueKind = "text", textValue = "value" },
            sessionManager.GetSnapshot());
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeUiAutomationService uiAutomationService = new((window, request, _) =>
            Task.FromResult(
                new UiaSnapshotResult(
                    Status: UiaSnapshotStatusValues.Done,
                    Window: CreateObservedWindow(window),
                    Root: new UiaElementSnapshot
                    {
                        ElementId = "root",
                        ControlType = "window",
                        Children =
                        [
                            new UiaElementSnapshot
                            {
                                ElementId = "path:other",
                                ControlType = "edit",
                                Name = "Other input",
                                AutomationId = "OtherTextBox",
                                BoundingRectangle = new Bounds(10, 20, 50, 60),
                                IsEnabled = true,
                                IsOffscreen = false,
                                IsReadOnly = false,
                                Patterns = ["value"],
                            },
                        ],
                    },
                    RequestedDepth: request.Depth,
                    RequestedMaxNodes: request.MaxNodes,
                    CapturedAtUtc: DateTimeOffset.UtcNow)));
        FakeUiAutomationSetValueService setValueService = new();
        ComputerUseWinSetValueHandler handler = new(
            new ComputerUseWinActionRequestExecutor(
                new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow()]))),
            new ComputerUseWinSetValueExecutionCoordinator(
                activationService,
                uiAutomationService,
                setValueService));

        ModelContextProtocol.Protocol.CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinSetValueRequest(StateToken: token, ElementIndex: 1, ValueKind: "text", TextValue: "value", NumberValue: null, Confirm: false),
            CancellationToken.None);

        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.Equal(ComputerUseWinFailureCodeValues.StaleState, payload.GetProperty("failureCode").GetString());
        Assert.Equal(0, setValueService.Calls);
    }

    [Fact]
    public async Task SetValueHandlerAppliesTextValueViaSemanticService()
    {
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateSettableStoredState());
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-set-value-success-tests"));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinSetValue,
            new { stateToken = token, elementIndex = 1, valueKind = "text", textValue = "updated semantic text" },
            sessionManager.GetSnapshot());
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeUiAutomationService uiAutomationService = new((window, request, _) =>
            Task.FromResult(
                new UiaSnapshotResult(
                    Status: UiaSnapshotStatusValues.Done,
                    Window: CreateObservedWindow(window),
                    Root: new UiaElementSnapshot
                    {
                        ElementId = "root",
                        ControlType = "window",
                        Children =
                        [
                            new UiaElementSnapshot
                            {
                                ElementId = "path:0",
                                ControlType = "edit",
                                Name = "Smoke query input",
                                AutomationId = "SmokeQueryInputTextBox",
                                BoundingRectangle = new Bounds(10, 20, 50, 60),
                                IsEnabled = true,
                                IsOffscreen = false,
                                IsReadOnly = false,
                                Patterns = ["value"],
                            },
                        ],
                    },
                    RequestedDepth: request.Depth,
                    RequestedMaxNodes: request.MaxNodes,
                    CapturedAtUtc: DateTimeOffset.UtcNow)));
        FakeUiAutomationSetValueService setValueService = new((window, request, _) =>
            Task.FromResult(UiaSetValueResult.SuccessResult("value_pattern")));
        ComputerUseWinSetValueHandler handler = new(
            new ComputerUseWinActionRequestExecutor(
                new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow()]))),
            new ComputerUseWinSetValueExecutionCoordinator(
                activationService,
                uiAutomationService,
                setValueService));

        ModelContextProtocol.Protocol.CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinSetValueRequest(StateToken: token, ElementIndex: 1, ValueKind: "text", TextValue: "updated semantic text", NumberValue: null, Confirm: false),
            CancellationToken.None);

        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.Done, payload.GetProperty("status").GetString());
        Assert.NotNull(setValueService.LastRequest);
        Assert.Equal("text", setValueService.LastRequest!.ValueKind);
        Assert.Equal("updated semantic text", setValueService.LastRequest.TextValue);
        Assert.Equal(101, payload.GetProperty("targetHwnd").GetInt64());
        Assert.Equal(1, payload.GetProperty("elementIndex").GetInt32());
    }

    [Fact]
    public async Task SetValueHandlerReturnsInvalidRequestWhenSemanticServiceRejectsValueFormat()
    {
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateSettableStoredState());
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-set-value-invalid-value-tests"));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinSetValue,
            new { stateToken = token, elementIndex = 1, valueKind = "number", numberValue = 12.5 },
            sessionManager.GetSnapshot());
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeUiAutomationService uiAutomationService = new((window, request, _) =>
            Task.FromResult(
                new UiaSnapshotResult(
                    Status: UiaSnapshotStatusValues.Done,
                    Window: CreateObservedWindow(window),
                    Root: new UiaElementSnapshot
                    {
                        ElementId = "root",
                        ControlType = "window",
                        Children =
                        [
                            new UiaElementSnapshot
                            {
                                ElementId = "path:0",
                                ControlType = "edit",
                                Name = "Smoke range input",
                                AutomationId = "SmokeRangeInputUpDown",
                                BoundingRectangle = new Bounds(10, 20, 50, 60),
                                IsEnabled = true,
                                IsOffscreen = false,
                                IsReadOnly = false,
                                Patterns = ["value"],
                            },
                        ],
                    },
                    RequestedDepth: request.Depth,
                    RequestedMaxNodes: request.MaxNodes,
                    CapturedAtUtc: DateTimeOffset.UtcNow)));
        FakeUiAutomationSetValueService setValueService = new((window, request, _) =>
            Task.FromResult(UiaSetValueResult.FailureResult(UiaSetValueFailureKindValues.InvalidValue, "invalid numeric semantic value", "value_pattern")));
        ComputerUseWinSetValueHandler handler = new(
            new ComputerUseWinActionRequestExecutor(
                new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow()]))),
            new ComputerUseWinSetValueExecutionCoordinator(
                activationService,
                uiAutomationService,
                setValueService));

        ModelContextProtocol.Protocol.CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinSetValueRequest(StateToken: token, ElementIndex: 1, ValueKind: "number", TextValue: null, NumberValue: 12.5, Confirm: false),
            CancellationToken.None);

        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.Equal(ComputerUseWinFailureCodeValues.InvalidRequest, payload.GetProperty("failureCode").GetString());
        Assert.True(payload.GetProperty("refreshStateRecommended").GetBoolean());
    }

    [Fact]
    public async Task TypeTextHandlerReturnsUnsupportedActionWithoutFocusedEditableProof()
    {
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateEditableStoredStateWithoutFocus());
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-type-text-missing-focus-tests"));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinTypeText,
            new { stateToken = token, text = "typed text" },
            sessionManager.GetSnapshot());
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeUiAutomationService uiAutomationService = new();
        FakeInputService inputService = new();
        ComputerUseWinTypeTextHandler handler = new(
            new ComputerUseWinActionRequestExecutor(
                new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow()]))),
            new ComputerUseWinTypeTextExecutionCoordinator(
                activationService,
                uiAutomationService,
                inputService));

        ModelContextProtocol.Protocol.CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinTypeTextRequest(StateToken: token, ElementIndex: null, Text: "typed text", Confirm: false),
            CancellationToken.None);

        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.Equal(ComputerUseWinFailureCodeValues.UnsupportedAction, payload.GetProperty("failureCode").GetString());
        Assert.Equal(0, uiAutomationService.Calls);
        Assert.Equal(0, inputService.Calls);
    }

    [Fact]
    public async Task TypeTextHandlerReturnsUnsupportedActionWhenFocusedElementWasNotPublishedAsTypeTextTarget()
    {
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateFocusedEditableStoredStateWithoutTypeTextAction());
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-type-text-missing-affordance-tests"));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinTypeText,
            new { stateToken = token, elementIndex = 1, text = "typed text" },
            sessionManager.GetSnapshot());
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeUiAutomationService uiAutomationService = new();
        FakeInputService inputService = new();
        ComputerUseWinTypeTextHandler handler = new(
            new ComputerUseWinActionRequestExecutor(
                new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow()]))),
            new ComputerUseWinTypeTextExecutionCoordinator(
                activationService,
                uiAutomationService,
                inputService));

        ModelContextProtocol.Protocol.CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinTypeTextRequest(StateToken: token, ElementIndex: 1, Text: "typed text", Confirm: false),
            CancellationToken.None);

        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.Equal(ComputerUseWinFailureCodeValues.UnsupportedAction, payload.GetProperty("failureCode").GetString());
        Assert.Equal(0, uiAutomationService.Calls);
        Assert.Equal(0, inputService.Calls);
    }

    [Fact]
    public async Task TypeTextHandlerReturnsUnsupportedActionForNonEditableElementTarget()
    {
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateNonActionableStoredState());
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-type-text-unsupported-target-tests"));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinTypeText,
            new { stateToken = token, elementIndex = 1, text = "typed text" },
            sessionManager.GetSnapshot());
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeUiAutomationService uiAutomationService = new();
        FakeInputService inputService = new();
        ComputerUseWinTypeTextHandler handler = new(
            new ComputerUseWinActionRequestExecutor(
                new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow()]))),
            new ComputerUseWinTypeTextExecutionCoordinator(
                activationService,
                uiAutomationService,
                inputService));

        ModelContextProtocol.Protocol.CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinTypeTextRequest(StateToken: token, ElementIndex: 1, Text: "typed text", Confirm: false),
            CancellationToken.None);

        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.Equal(ComputerUseWinFailureCodeValues.UnsupportedAction, payload.GetProperty("failureCode").GetString());
        Assert.Equal(0, uiAutomationService.Calls);
        Assert.Equal(0, inputService.Calls);
    }

    [Fact]
    public async Task TypeTextHandlerReturnsStaleStateWhenFocusedEditableLosesFreshFocus()
    {
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateFocusedEditableStoredState());
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-type-text-stale-focus-tests"));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinTypeText,
            new { stateToken = token, elementIndex = 1, text = "typed text" },
            sessionManager.GetSnapshot());
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeUiAutomationService uiAutomationService = new((window, request, _) =>
            Task.FromResult(
                new UiaSnapshotResult(
                    Status: UiaSnapshotStatusValues.Done,
                    Window: CreateObservedWindow(window),
                    Root: new UiaElementSnapshot
                    {
                        ElementId = "root",
                        ControlType = "window",
                        Children =
                        [
                            new UiaElementSnapshot
                            {
                                ElementId = "path:0",
                                ControlType = "edit",
                                Name = "Smoke query input",
                                AutomationId = "SmokeQueryInputTextBox",
                                BoundingRectangle = new Bounds(10, 20, 50, 60),
                                IsEnabled = true,
                                IsOffscreen = false,
                                HasKeyboardFocus = false,
                                IsReadOnly = false,
                                Patterns = ["value"],
                            },
                        ],
                    },
                    RequestedDepth: request.Depth,
                    RequestedMaxNodes: request.MaxNodes,
                    CapturedAtUtc: DateTimeOffset.UtcNow)));
        FakeInputService inputService = new();
        ComputerUseWinTypeTextHandler handler = new(
            new ComputerUseWinActionRequestExecutor(
                new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow()]))),
            new ComputerUseWinTypeTextExecutionCoordinator(
                activationService,
                uiAutomationService,
                inputService));

        ModelContextProtocol.Protocol.CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinTypeTextRequest(StateToken: token, ElementIndex: 1, Text: "typed text", Confirm: false),
            CancellationToken.None);

        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.Equal(ComputerUseWinFailureCodeValues.StaleState, payload.GetProperty("failureCode").GetString());
        Assert.Equal(0, inputService.Calls);
    }

    [Fact]
    public async Task TypeTextHandlerDispatchesTextForFocusedEditableElement()
    {
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateFocusedEditableStoredState());
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-type-text-element-success-tests"));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinTypeText,
            new { stateToken = token, elementIndex = 1, text = "typed text" },
            sessionManager.GetSnapshot());
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeUiAutomationService uiAutomationService = new((window, request, _) =>
            Task.FromResult(
                new UiaSnapshotResult(
                    Status: UiaSnapshotStatusValues.Done,
                    Window: CreateObservedWindow(window),
                    Root: new UiaElementSnapshot
                    {
                        ElementId = "root",
                        ControlType = "window",
                        Children =
                        [
                            new UiaElementSnapshot
                            {
                                ElementId = "path:0",
                                ControlType = "edit",
                                Name = "Smoke query input",
                                AutomationId = "SmokeQueryInputTextBox",
                                BoundingRectangle = new Bounds(10, 20, 50, 60),
                                IsEnabled = true,
                                IsOffscreen = false,
                                HasKeyboardFocus = true,
                                IsReadOnly = false,
                                Patterns = ["value"],
                            },
                        ],
                    },
                    RequestedDepth: request.Depth,
                    RequestedMaxNodes: request.MaxNodes,
                    CapturedAtUtc: DateTimeOffset.UtcNow)));
        FakeInputService inputService = new((request, _, _) =>
            Task.FromResult(
                new InputResult(
                    Status: InputStatusValues.VerifyNeeded,
                    Decision: InputStatusValues.VerifyNeeded,
                    TargetHwnd: request.Hwnd,
                    CompletedActionCount: 1)));
        ComputerUseWinTypeTextHandler handler = new(
            new ComputerUseWinActionRequestExecutor(
                new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow()]))),
            new ComputerUseWinTypeTextExecutionCoordinator(
                activationService,
                uiAutomationService,
                inputService));

        ModelContextProtocol.Protocol.CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinTypeTextRequest(StateToken: token, ElementIndex: 1, Text: "typed text", Confirm: false),
            CancellationToken.None);

        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.VerifyNeeded, payload.GetProperty("status").GetString());
        Assert.NotNull(inputService.LastRequest);
        InputAction action = Assert.Single(inputService.LastRequest!.Actions);
        Assert.Equal(InputActionTypeValues.Type, action.Type);
        Assert.Equal("typed text", action.Text);
        Assert.Equal(1, payload.GetProperty("elementIndex").GetInt32());
        Assert.Equal(101, payload.GetProperty("targetHwnd").GetInt64());
    }

    [Fact]
    public async Task TypeTextHandlerUsesFocusedEditableFallbackWhenElementIndexIsOmitted()
    {
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateFocusedEditableStoredState());
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-type-text-focused-fallback-tests"));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinTypeText,
            new { stateToken = token, text = "typed text" },
            sessionManager.GetSnapshot());
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeUiAutomationService uiAutomationService = new((window, request, _) =>
            Task.FromResult(
                new UiaSnapshotResult(
                    Status: UiaSnapshotStatusValues.Done,
                    Window: CreateObservedWindow(window),
                    Root: new UiaElementSnapshot
                    {
                        ElementId = "root",
                        ControlType = "window",
                        Children =
                        [
                            new UiaElementSnapshot
                            {
                                ElementId = "path:0",
                                ControlType = "edit",
                                Name = "Smoke query input",
                                AutomationId = "SmokeQueryInputTextBox",
                                BoundingRectangle = new Bounds(10, 20, 50, 60),
                                IsEnabled = true,
                                IsOffscreen = false,
                                HasKeyboardFocus = true,
                                IsReadOnly = false,
                                Patterns = ["value"],
                            },
                        ],
                    },
                    RequestedDepth: request.Depth,
                    RequestedMaxNodes: request.MaxNodes,
                    CapturedAtUtc: DateTimeOffset.UtcNow)));
        FakeInputService inputService = new((request, _, _) =>
            Task.FromResult(
                new InputResult(
                    Status: InputStatusValues.VerifyNeeded,
                    Decision: InputStatusValues.VerifyNeeded,
                    TargetHwnd: request.Hwnd,
                    CompletedActionCount: 1)));
        ComputerUseWinTypeTextHandler handler = new(
            new ComputerUseWinActionRequestExecutor(
                new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow()]))),
            new ComputerUseWinTypeTextExecutionCoordinator(
                activationService,
                uiAutomationService,
                inputService));

        ModelContextProtocol.Protocol.CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinTypeTextRequest(StateToken: token, ElementIndex: null, Text: "typed text", Confirm: false),
            CancellationToken.None);

        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.VerifyNeeded, payload.GetProperty("status").GetString());
        Assert.NotNull(inputService.LastRequest);
        InputAction action = Assert.Single(inputService.LastRequest!.Actions);
        Assert.Equal(InputActionTypeValues.Type, action.Type);
        Assert.Equal("typed text", action.Text);
        Assert.False(payload.TryGetProperty("elementIndex", out JsonElement elementIndex) && elementIndex.ValueKind != JsonValueKind.Null);
    }

    [Fact]
    public async Task TypeTextHandlerUsesConfirmedFocusedFallbackForWeakFocusedElement()
    {
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateFocusedWeakStoredState());
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-type-text-focused-weak-fallback-tests"));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinTypeText,
            new { stateToken = token, text = "typed fallback text", allowFocusedFallback = true, confirm = true },
            sessionManager.GetSnapshot());
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeUiAutomationService uiAutomationService = new((window, request, _) =>
            Task.FromResult(
                new UiaSnapshotResult(
                    Status: UiaSnapshotStatusValues.Done,
                    Window: CreateObservedWindow(window),
                    Root: CreateFocusedWeakSnapshotRoot(hasKeyboardFocus: true),
                    RequestedDepth: request.Depth,
                    RequestedMaxNodes: request.MaxNodes,
                    CapturedAtUtc: DateTimeOffset.UtcNow)));
        FakeInputService inputService = new((request, _, _) =>
            Task.FromResult(
                new InputResult(
                    Status: InputStatusValues.VerifyNeeded,
                    Decision: InputStatusValues.VerifyNeeded,
                    TargetHwnd: request.Hwnd,
                    CompletedActionCount: 1)));
        ComputerUseWinTypeTextHandler handler = new(
            new ComputerUseWinActionRequestExecutor(
                new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow()]))),
            new ComputerUseWinTypeTextExecutionCoordinator(
                activationService,
                uiAutomationService,
                inputService));

        ModelContextProtocol.Protocol.CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinTypeTextRequest(
                StateToken: token,
                ElementIndex: null,
                Text: "typed fallback text",
                Confirm: true,
                AllowFocusedFallback: true),
            CancellationToken.None);

        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.VerifyNeeded, payload.GetProperty("status").GetString());
        Assert.NotNull(inputService.LastRequest);
        InputAction action = Assert.Single(inputService.LastRequest!.Actions);
        Assert.Equal(InputActionTypeValues.Type, action.Type);
        Assert.Equal("typed fallback text", action.Text);
        Assert.Equal(101, payload.GetProperty("targetHwnd").GetInt64());
        Assert.False(payload.TryGetProperty("elementIndex", out JsonElement elementIndex) && elementIndex.ValueKind != JsonValueKind.Null);
    }

    [Fact]
    public async Task TypeTextHandlerUsesElementScopedFocusedFallbackForWeakFocusedElement()
    {
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateFocusedWeakStoredState());
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-type-text-element-focused-fallback-tests"));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinTypeText,
            new { stateToken = token, elementIndex = 1, text = "typed fallback text", allowFocusedFallback = true, confirm = true },
            sessionManager.GetSnapshot());
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeUiAutomationService uiAutomationService = new((window, request, _) =>
            Task.FromResult(
                new UiaSnapshotResult(
                    Status: UiaSnapshotStatusValues.Done,
                    Window: CreateObservedWindow(window),
                    Root: CreateFocusedWeakSnapshotRoot(hasKeyboardFocus: true),
                    RequestedDepth: request.Depth,
                    RequestedMaxNodes: request.MaxNodes,
                    CapturedAtUtc: DateTimeOffset.UtcNow)));
        FakeInputService inputService = new((request, _, _) =>
            Task.FromResult(
                new InputResult(
                    Status: InputStatusValues.VerifyNeeded,
                    Decision: InputStatusValues.VerifyNeeded,
                    TargetHwnd: request.Hwnd,
                    CompletedActionCount: 1)));
        ComputerUseWinTypeTextHandler handler = new(
            new ComputerUseWinActionRequestExecutor(
                new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow()]))),
            new ComputerUseWinTypeTextExecutionCoordinator(
                activationService,
                uiAutomationService,
                inputService));

        ModelContextProtocol.Protocol.CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinTypeTextRequest(
                StateToken: token,
                ElementIndex: 1,
                Text: "typed fallback text",
                Confirm: true,
                AllowFocusedFallback: true),
            CancellationToken.None);

        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.VerifyNeeded, payload.GetProperty("status").GetString());
        Assert.Equal(1, payload.GetProperty("elementIndex").GetInt32());
        Assert.NotNull(inputService.LastRequest);
        InputAction action = Assert.Single(inputService.LastRequest!.Actions);
        Assert.Equal(InputActionTypeValues.Type, action.Type);
        Assert.Equal("typed fallback text", action.Text);
    }

    [Fact]
    public async Task TypeTextHandlerEmbedsSuccessorStateWhenFocusedFallbackObserveAfterSucceeds()
    {
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateFocusedWeakStoredState());
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-type-text-focused-fallback-observe-after-tests"));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinTypeText,
            new { stateToken = token, text = "typed fallback text", allowFocusedFallback = true, confirm = true, observeAfter = true },
            sessionManager.GetSnapshot());
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeUiAutomationService uiAutomationService = new((window, request, _) =>
            Task.FromResult(
                new UiaSnapshotResult(
                    Status: UiaSnapshotStatusValues.Done,
                    Window: CreateObservedWindow(window),
                    Root: CreateFocusedWeakSnapshotRoot(hasKeyboardFocus: true),
                    RequestedDepth: request.Depth,
                    RequestedMaxNodes: request.MaxNodes,
                    CapturedAtUtc: DateTimeOffset.UtcNow)));
        FakeInputService inputService = new((request, _, _) =>
            Task.FromResult(
                new InputResult(
                    Status: InputStatusValues.Done,
                    Decision: InputStatusValues.Done,
                    ResultMode: InputResultModeValues.PostconditionVerified,
                    TargetHwnd: request.Hwnd,
                    CompletedActionCount: 1)));
        ComputerUseWinTypeTextHandler handler = new(
            new ComputerUseWinActionRequestExecutor(
                new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow()])),
                new ComputerUseWinAppStateObserver(
                    new SuccessfulComputerUseWinCaptureService(),
                    uiAutomationService,
                    new EmptyInstructionProvider()),
                stateStore,
                sessionManager),
            new ComputerUseWinTypeTextExecutionCoordinator(
                activationService,
                uiAutomationService,
                inputService));

        CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinTypeTextRequest(
                StateToken: token,
                ElementIndex: null,
                Text: "typed fallback text",
                Confirm: true,
                AllowFocusedFallback: true,
                ObserveAfter: true),
            CancellationToken.None);

        Assert.False(result.IsError, result.StructuredContent!.Value.GetRawText());
        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.VerifyNeeded, payload.GetProperty("status").GetString());
        Assert.False(payload.GetProperty("refreshStateRecommended").GetBoolean());
        Assert.False(payload.TryGetProperty("successorStateFailure", out _));
        JsonElement successorState = payload.GetProperty("successorState");
        Assert.Equal(ComputerUseWinStatusValues.Ok, successorState.GetProperty("status").GetString());
        string successorToken = successorState.GetProperty("stateToken").GetString()!;
        Assert.NotEqual(token, successorToken);
        Assert.True(stateStore.TryGet(successorToken, out _));
        Assert.IsType<TextContentBlock>(result.Content[0]);
        Assert.IsType<ImageContentBlock>(result.Content[1]);
        Assert.Equal(1, inputService.Calls);
        Assert.Equal(2, uiAutomationService.Calls);
    }

    [Fact]
    public async Task TypeTextHandlerFocusedFallbackDoesNotPromoteDispatchOnlyDone()
    {
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateFocusedWeakStoredState());
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-type-text-focused-fallback-done-tests"));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinTypeText,
            new { stateToken = token, text = "typed fallback text", allowFocusedFallback = true, confirm = true },
            sessionManager.GetSnapshot());
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeUiAutomationService uiAutomationService = new((window, request, _) =>
            Task.FromResult(
                new UiaSnapshotResult(
                    Status: UiaSnapshotStatusValues.Done,
                    Window: CreateObservedWindow(window),
                    Root: CreateFocusedWeakSnapshotRoot(hasKeyboardFocus: true),
                    RequestedDepth: request.Depth,
                    RequestedMaxNodes: request.MaxNodes,
                    CapturedAtUtc: DateTimeOffset.UtcNow)));
        FakeInputService inputService = new((request, _, _) =>
            Task.FromResult(
                new InputResult(
                    Status: InputStatusValues.Done,
                    Decision: InputStatusValues.Done,
                    ResultMode: InputResultModeValues.PostconditionVerified,
                    TargetHwnd: request.Hwnd,
                    CompletedActionCount: 1)));
        ComputerUseWinTypeTextHandler handler = new(
            new ComputerUseWinActionRequestExecutor(
                new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow()]))),
            new ComputerUseWinTypeTextExecutionCoordinator(
                activationService,
                uiAutomationService,
                inputService));

        ModelContextProtocol.Protocol.CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinTypeTextRequest(
                StateToken: token,
                ElementIndex: null,
                Text: "typed fallback text",
                Confirm: true,
                AllowFocusedFallback: true),
            CancellationToken.None);

        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.VerifyNeeded, payload.GetProperty("status").GetString());
        Assert.NotNull(inputService.LastRequest);
    }

    [Fact]
    public async Task TypeTextHandlerUsesCoordinateConfirmedFallbackForTopLevelOnlyClassC()
    {
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateClassCCoordinateStoredState());
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-type-text-coordinate-confirmed-tests"));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinTypeText,
            new { stateToken = token, point = new { x = 30, y = 40 }, text = "coordinate typed text", allowFocusedFallback = true, confirm = true },
            sessionManager.GetSnapshot());
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeUiAutomationService uiAutomationService = new();
        FakeInputService inputService = new((request, _, _) =>
            Task.FromResult(
                new InputResult(
                    Status: InputStatusValues.Done,
                    Decision: InputStatusValues.Done,
                    ResultMode: InputResultModeValues.DispatchOnly,
                    TargetHwnd: request.Hwnd,
                    CompletedActionCount: 2)));
        ComputerUseWinTypeTextHandler handler = new(
            new ComputerUseWinActionRequestExecutor(
                new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow()]))),
            new ComputerUseWinTypeTextExecutionCoordinator(
                activationService,
                uiAutomationService,
                inputService));

        CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinTypeTextRequest(
                StateToken: token,
                ElementIndex: null,
                Point: new InputPoint(30, 40),
                CoordinateSpace: null,
                Text: "coordinate typed text",
                Confirm: true,
                AllowFocusedFallback: true),
            CancellationToken.None);

        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.VerifyNeeded, payload.GetProperty("status").GetString());
        Assert.Equal(0, uiAutomationService.Calls);
        Assert.NotNull(inputService.LastRequest);
        Assert.Equal(101, inputService.LastRequest!.Hwnd);
        Assert.Collection(
            inputService.LastRequest.Actions,
            clickAction =>
            {
                Assert.Equal(InputActionTypeValues.Click, clickAction.Type);
                Assert.Equal(InputCoordinateSpaceValues.CapturePixels, clickAction.CoordinateSpace);
                Assert.Equal(30, clickAction.Point!.X);
                Assert.Equal(40, clickAction.Point.Y);
                Assert.NotNull(clickAction.CaptureReference);
            },
            typeAction =>
            {
                Assert.Equal(InputActionTypeValues.Type, typeAction.Type);
                Assert.Equal("coordinate typed text", typeAction.Text);
            });
    }

    [Fact]
    public async Task TypeTextHandlerRejectsScreenCoordinateConfirmedFallbackBeforeDispatch()
    {
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateClassCCoordinateStoredState());
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-type-text-coordinate-screen-reject-tests"));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinTypeText,
            new { stateToken = token, point = new { x = 30, y = 40 }, coordinateSpace = "screen", text = "coordinate typed text", allowFocusedFallback = true, confirm = true },
            sessionManager.GetSnapshot());
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeUiAutomationService uiAutomationService = new();
        FakeInputService inputService = new();
        ComputerUseWinTypeTextHandler handler = new(
            new ComputerUseWinActionRequestExecutor(
                new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow()]))),
            new ComputerUseWinTypeTextExecutionCoordinator(
                activationService,
                uiAutomationService,
                inputService));

        CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinTypeTextRequest(
                StateToken: token,
                ElementIndex: null,
                Point: new InputPoint(30, 40),
                CoordinateSpace: InputCoordinateSpaceValues.Screen,
                Text: "coordinate typed text",
                Confirm: true,
                AllowFocusedFallback: true),
            CancellationToken.None);

        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.Equal(ComputerUseWinFailureCodeValues.InvalidRequest, payload.GetProperty("failureCode").GetString());
        Assert.Contains("capture_pixels", payload.GetProperty("reason").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, uiAutomationService.Calls);
        Assert.Equal(0, inputService.Calls);
        Assert.Null(activationService.LastHwnd);
    }

    [Fact]
    public async Task TypeTextHandlerRejectsCoordinateConfirmedFallbackWithoutCaptureReferenceBeforeDispatch()
    {
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateClassCCoordinateStoredState(useDefaultCaptureReference: false));
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-type-text-coordinate-missing-capture-tests"));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinTypeText,
            new { stateToken = token, point = new { x = 30, y = 40 }, text = "coordinate typed text", allowFocusedFallback = true, confirm = true },
            sessionManager.GetSnapshot());
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeUiAutomationService uiAutomationService = new();
        FakeInputService inputService = new();
        ComputerUseWinTypeTextHandler handler = new(
            new ComputerUseWinActionRequestExecutor(
                new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow()]))),
            new ComputerUseWinTypeTextExecutionCoordinator(
                activationService,
                uiAutomationService,
                inputService));

        CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinTypeTextRequest(
                StateToken: token,
                ElementIndex: null,
                Point: new InputPoint(30, 40),
                CoordinateSpace: InputCoordinateSpaceValues.CapturePixels,
                Text: "coordinate typed text",
                Confirm: true,
                AllowFocusedFallback: true),
            CancellationToken.None);

        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.Equal(ComputerUseWinFailureCodeValues.CaptureReferenceRequired, payload.GetProperty("failureCode").GetString());
        Assert.Equal(0, uiAutomationService.Calls);
        Assert.Equal(0, inputService.Calls);
    }

    [Fact]
    public async Task TypeTextHandlerRejectsCoordinateConfirmedFallbackWhenPointIsOutOfCaptureBounds()
    {
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateClassCCoordinateStoredState());
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-type-text-coordinate-out-of-bounds-tests"));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinTypeText,
            new { stateToken = token, point = new { x = 9999, y = 40 }, text = "coordinate typed text", allowFocusedFallback = true, confirm = true },
            sessionManager.GetSnapshot());
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeUiAutomationService uiAutomationService = new();
        FakeInputService inputService = new();
        ComputerUseWinTypeTextHandler handler = new(
            new ComputerUseWinActionRequestExecutor(
                new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow()]))),
            new ComputerUseWinTypeTextExecutionCoordinator(
                activationService,
                uiAutomationService,
                inputService));

        CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinTypeTextRequest(
                StateToken: token,
                ElementIndex: null,
                Point: new InputPoint(9999, 40),
                CoordinateSpace: InputCoordinateSpaceValues.CapturePixels,
                Text: "coordinate typed text",
                Confirm: true,
                AllowFocusedFallback: true),
            CancellationToken.None);

        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.Equal(ComputerUseWinFailureCodeValues.PointOutOfBounds, payload.GetProperty("failureCode").GetString());
        Assert.Equal(0, uiAutomationService.Calls);
        Assert.Equal(0, inputService.Calls);
    }

    [Fact]
    public async Task TypeTextHandlerDoesNotObserveAfterFailedCoordinateConfirmedDispatch()
    {
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateClassCCoordinateStoredState());
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-type-text-coordinate-failed-dispatch-tests"));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinTypeText,
            new { stateToken = token, point = new { x = 30, y = 40 }, text = "coordinate typed text", allowFocusedFallback = true, confirm = true, observeAfter = true },
            sessionManager.GetSnapshot());
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeUiAutomationService uiAutomationService = new((_, _, _) =>
            Task.FromResult(
                new UiaSnapshotResult(
                    Status: UiaSnapshotStatusValues.Done,
                    Window: CreateObservedWindow(CreateWindow()),
                    Root: CreateClickSnapshotRoot(),
                    RequestedDepth: UiaSnapshotDefaults.Depth,
                    RequestedMaxNodes: 768,
                    CapturedAtUtc: DateTimeOffset.UtcNow)));
        FakeInputService inputService = new((request, _, _) =>
            Task.FromResult(
                new InputResult(
                    Status: InputStatusValues.Failed,
                    Decision: InputStatusValues.Failed,
                    FailureCode: InputFailureCodeValues.InputDispatchFailed,
                    Reason: "dispatch failed",
                    TargetHwnd: request.Hwnd,
                    CompletedActionCount: 0)));
        ComputerUseWinTypeTextHandler handler = new(
            new ComputerUseWinActionRequestExecutor(
                new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow()])),
                new ComputerUseWinAppStateObserver(
                    new SuccessfulComputerUseWinCaptureService(),
                    uiAutomationService,
                    new EmptyInstructionProvider()),
                stateStore,
                sessionManager),
            new ComputerUseWinTypeTextExecutionCoordinator(
                activationService,
                uiAutomationService,
                inputService));

        CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinTypeTextRequest(
                StateToken: token,
                ElementIndex: null,
                Point: new InputPoint(30, 40),
                CoordinateSpace: InputCoordinateSpaceValues.CapturePixels,
                Text: "coordinate typed text",
                Confirm: true,
                AllowFocusedFallback: true,
                ObserveAfter: true),
            CancellationToken.None);

        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.False(payload.TryGetProperty("successorState", out _));
        Assert.False(payload.TryGetProperty("successorStateFailure", out _));
        Assert.Equal(0, uiAutomationService.Calls);
    }

    [Fact]
    public async Task TypeTextHandlerKeepsWeakFocusedElementUnavailableWithoutFocusedFallbackOptIn()
    {
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateFocusedWeakStoredState());
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-type-text-focused-weak-default-tests"));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinTypeText,
            new { stateToken = token, text = "typed fallback text" },
            sessionManager.GetSnapshot());
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeUiAutomationService uiAutomationService = new();
        FakeInputService inputService = new();
        ComputerUseWinTypeTextHandler handler = new(
            new ComputerUseWinActionRequestExecutor(
                new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow()]))),
            new ComputerUseWinTypeTextExecutionCoordinator(
                activationService,
                uiAutomationService,
                inputService));

        ModelContextProtocol.Protocol.CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinTypeTextRequest(StateToken: token, ElementIndex: null, Text: "typed fallback text", Confirm: false),
            CancellationToken.None);

        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.Equal(ComputerUseWinFailureCodeValues.UnsupportedAction, payload.GetProperty("failureCode").GetString());
        Assert.Equal(0, uiAutomationService.Calls);
        Assert.Equal(0, inputService.Calls);
    }

    [Theory]
    [InlineData("button", false)]
    [InlineData("button", true)]
    [InlineData("list", false)]
    [InlineData("check_box", false)]
    [InlineData("panel", false)]
    public async Task TypeTextHandlerRejectsFocusedFallbackForFocusedNonTextTarget(string controlType, bool useElementIndex)
    {
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateFocusedWeakStoredState(controlType: controlType));
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-type-text-focused-fallback-non-text-tests"));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinTypeText,
            new { stateToken = token, text = "typed fallback text", allowFocusedFallback = true, confirm = true },
            sessionManager.GetSnapshot());
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeUiAutomationService uiAutomationService = new((window, request, _) =>
            Task.FromResult(
                new UiaSnapshotResult(
                    Status: UiaSnapshotStatusValues.Done,
                    Window: CreateObservedWindow(window),
                    Root: CreateFocusedWeakSnapshotRoot(hasKeyboardFocus: true, controlType: controlType),
                    RequestedDepth: request.Depth,
                    RequestedMaxNodes: request.MaxNodes,
                    CapturedAtUtc: DateTimeOffset.UtcNow)));
        FakeInputService inputService = new();
        ComputerUseWinTypeTextHandler handler = new(
            new ComputerUseWinActionRequestExecutor(
                new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow()]))),
            new ComputerUseWinTypeTextExecutionCoordinator(
                activationService,
                uiAutomationService,
                inputService));

        CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinTypeTextRequest(
                StateToken: token,
                ElementIndex: useElementIndex ? 1 : null,
                Text: "typed fallback text",
                Confirm: true,
                AllowFocusedFallback: true),
            CancellationToken.None);

        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.Equal(ComputerUseWinFailureCodeValues.UnsupportedAction, payload.GetProperty("failureCode").GetString());
        Assert.Equal(0, inputService.Calls);
    }

    [Theory]
    [InlineData("document", "Context menu", "ContextMenu")]
    [InlineData("custom", "Research panel", "ResearchPanel")]
    [InlineData("document", "Fieldset host", "FieldsetHost")]
    [InlineData("custom", "EntryPoint canvas", "EntryPointCanvas")]
    public async Task TypeTextHandlerRejectsFocusedFallbackForNonTextTargetsWithTextMarkerSubstrings(
        string controlType,
        string name,
        string automationId)
    {
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateFocusedWeakStoredState(controlType: controlType, name: name, automationId: automationId));
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-type-text-focused-fallback-substring-tests"));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinTypeText,
            new { stateToken = token, text = "typed fallback text", allowFocusedFallback = true, confirm = true },
            sessionManager.GetSnapshot());
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeUiAutomationService uiAutomationService = new((window, request, _) =>
            Task.FromResult(
                new UiaSnapshotResult(
                    Status: UiaSnapshotStatusValues.Done,
                    Window: CreateObservedWindow(window),
                    Root: CreateFocusedWeakSnapshotRoot(
                        hasKeyboardFocus: true,
                        controlType: controlType,
                        name: name,
                        automationId: automationId),
                    RequestedDepth: request.Depth,
                    RequestedMaxNodes: request.MaxNodes,
                    CapturedAtUtc: DateTimeOffset.UtcNow)));
        FakeInputService inputService = new();
        ComputerUseWinTypeTextHandler handler = new(
            new ComputerUseWinActionRequestExecutor(
                new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow()]))),
            new ComputerUseWinTypeTextExecutionCoordinator(
                activationService,
                uiAutomationService,
                inputService));

        CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinTypeTextRequest(
                StateToken: token,
                ElementIndex: null,
                Text: "typed fallback text",
                Confirm: true,
                AllowFocusedFallback: true),
            CancellationToken.None);

        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.Equal(ComputerUseWinFailureCodeValues.UnsupportedAction, payload.GetProperty("failureCode").GetString());
        Assert.Equal(0, inputService.Calls);
    }

    [Fact]
    public async Task TypeTextHandlerRejectsFocusedFallbackForOversizedSemanticHints()
    {
        string oversizedName = "Text Box " + new string('x', 4096);
        string oversizedAutomationId = "TextBox_" + new string('x', 4096);
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateFocusedWeakStoredState(name: oversizedName, automationId: oversizedAutomationId));
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-type-text-focused-fallback-oversized-hint-tests"));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinTypeText,
            new { stateToken = token, text = "typed fallback text", allowFocusedFallback = true, confirm = true },
            sessionManager.GetSnapshot());
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeUiAutomationService uiAutomationService = new((window, request, _) =>
            Task.FromResult(
                new UiaSnapshotResult(
                    Status: UiaSnapshotStatusValues.Done,
                    Window: CreateObservedWindow(window),
                    Root: CreateFocusedWeakSnapshotRoot(
                        hasKeyboardFocus: true,
                        name: oversizedName,
                        automationId: oversizedAutomationId),
                    RequestedDepth: request.Depth,
                    RequestedMaxNodes: request.MaxNodes,
                    CapturedAtUtc: DateTimeOffset.UtcNow)));
        FakeInputService inputService = new();
        ComputerUseWinTypeTextHandler handler = new(
            new ComputerUseWinActionRequestExecutor(
                new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow()]))),
            new ComputerUseWinTypeTextExecutionCoordinator(
                activationService,
                uiAutomationService,
                inputService));

        CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinTypeTextRequest(
                StateToken: token,
                ElementIndex: null,
                Text: "typed fallback text",
                Confirm: true,
                AllowFocusedFallback: true),
            CancellationToken.None);

        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.Equal(ComputerUseWinFailureCodeValues.UnsupportedAction, payload.GetProperty("failureCode").GetString());
        Assert.Equal(0, inputService.Calls);
    }

    [Fact]
    public async Task TypeTextHandlerFocusedFallbackObservabilityAvoidsRawTextClipboardAndPaste()
    {
        string root = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            AuditLogOptions options = CreateAuditOptions(root, "computer-use-win-type-text-focused-fallback-observability-tests");
            AuditLog auditLog = new(options, TimeProvider.System);
            ComputerUseWinStateStore stateStore = new();
            string token = stateStore.Create(CreateFocusedWeakStoredState());
            InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-type-text-focused-fallback-observability-tests"));
            using AuditInvocationScope invocation = auditLog.BeginInvocation(
                ToolNames.ComputerUseWinTypeText,
                new { stateToken = token, text = "secret fallback text", allowFocusedFallback = true, confirm = true },
                sessionManager.GetSnapshot());
            FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
            FakeUiAutomationService uiAutomationService = new((window, request, _) =>
                Task.FromResult(
                    new UiaSnapshotResult(
                        Status: UiaSnapshotStatusValues.Done,
                        Window: CreateObservedWindow(window),
                        Root: CreateFocusedWeakSnapshotRoot(hasKeyboardFocus: true),
                        RequestedDepth: request.Depth,
                        RequestedMaxNodes: request.MaxNodes,
                        CapturedAtUtc: DateTimeOffset.UtcNow)));
            FakeInputService inputService = new((request, _, _) =>
                Task.FromResult(
                    new InputResult(
                        Status: InputStatusValues.VerifyNeeded,
                        Decision: InputStatusValues.VerifyNeeded,
                        TargetHwnd: request.Hwnd,
                        CompletedActionCount: 1)));
            ComputerUseWinTypeTextHandler handler = new(
                new ComputerUseWinActionRequestExecutor(
                    new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow()]))),
                new ComputerUseWinTypeTextExecutionCoordinator(
                    activationService,
                    uiAutomationService,
                    inputService));

            _ = await handler.ExecuteAsync(
                invocation,
                new ComputerUseWinTypeTextRequest(
                    StateToken: token,
                    ElementIndex: null,
                    Text: "secret fallback text",
                    Confirm: true,
                    AllowFocusedFallback: true),
                CancellationToken.None);

            string actionEvent = File.ReadLines(options.EventsPath)
                .Single(line => line.Contains("\"event_name\":\"computer_use_win.action.completed\"", StringComparison.Ordinal));
            Assert.Contains("\"fallback_used\":\"true\"", actionEvent, StringComparison.Ordinal);
            Assert.Contains("\"confirmation_required\":\"true\"", actionEvent, StringComparison.Ordinal);
            Assert.Contains("\"target_mode\":\"focused_fallback\"", actionEvent, StringComparison.Ordinal);
            Assert.Contains("\"risk_class\":\"focused_text_fallback\"", actionEvent, StringComparison.Ordinal);
            Assert.Contains("\"dispatch_path\":\"win32_sendinput_unicode\"", actionEvent, StringComparison.Ordinal);
            Assert.DoesNotContain("secret fallback text", actionEvent, StringComparison.Ordinal);
            Assert.DoesNotContain("clipboard", actionEvent, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("paste", actionEvent, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task TypeTextHandlerCoordinateConfirmedFallbackObservabilityAvoidsRawTextAndPoint()
    {
        string root = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            AuditLogOptions options = CreateAuditOptions(root, "computer-use-win-type-text-coordinate-fallback-observability-tests");
            AuditLog auditLog = new(options, TimeProvider.System);
            ComputerUseWinStateStore stateStore = new();
            string token = stateStore.Create(CreateClassCCoordinateStoredState());
            InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-type-text-coordinate-fallback-observability-tests"));
            using AuditInvocationScope invocation = auditLog.BeginInvocation(
                ToolNames.ComputerUseWinTypeText,
                new { stateToken = token, point = new { x = 30, y = 40 }, text = "secret coordinate text", allowFocusedFallback = true, confirm = true },
                sessionManager.GetSnapshot());
            FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
            FakeUiAutomationService uiAutomationService = new();
            FakeInputService inputService = new((request, _, _) =>
                Task.FromResult(
                    new InputResult(
                        Status: InputStatusValues.VerifyNeeded,
                        Decision: InputStatusValues.VerifyNeeded,
                        TargetHwnd: request.Hwnd,
                        CompletedActionCount: 2)));
            ComputerUseWinTypeTextHandler handler = new(
                new ComputerUseWinActionRequestExecutor(
                    new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow()]))),
                new ComputerUseWinTypeTextExecutionCoordinator(
                    activationService,
                    uiAutomationService,
                    inputService));

            _ = await handler.ExecuteAsync(
                invocation,
                new ComputerUseWinTypeTextRequest(
                    StateToken: token,
                    ElementIndex: null,
                    Point: new InputPoint(30, 40),
                    CoordinateSpace: InputCoordinateSpaceValues.CapturePixels,
                    Text: "secret coordinate text",
                    Confirm: true,
                    AllowFocusedFallback: true),
                CancellationToken.None);

            string actionEvent = File.ReadLines(options.EventsPath)
                .Single(line => line.Contains("\"event_name\":\"computer_use_win.action.completed\"", StringComparison.Ordinal));
            Assert.Contains("\"fallback_used\":\"true\"", actionEvent, StringComparison.Ordinal);
            Assert.Contains("\"target_mode\":\"coordinate_confirmed_fallback\"", actionEvent, StringComparison.Ordinal);
            Assert.Contains("\"risk_class\":\"coordinate_confirmed_text_fallback\"", actionEvent, StringComparison.Ordinal);
            Assert.Contains("\"dispatch_path\":\"capture_pixels_text_input\"", actionEvent, StringComparison.Ordinal);
            Assert.DoesNotContain("secret coordinate text", actionEvent, StringComparison.Ordinal);
            Assert.DoesNotContain("\"point\"", actionEvent, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("clipboard", actionEvent, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("paste", actionEvent, StringComparison.OrdinalIgnoreCase);

            string actionArtifactPath = Directory
                .GetFiles(Path.Combine(options.RunDirectory, "computer-use-win"), "action-*.json", SearchOption.TopDirectoryOnly)
                .Single();
            string actionArtifact = File.ReadAllText(actionArtifactPath);
            Assert.Contains("\"target_mode\": \"coordinate_confirmed_fallback\"", actionArtifact, StringComparison.Ordinal);
            Assert.DoesNotContain("secret coordinate text", actionArtifact, StringComparison.Ordinal);
            Assert.DoesNotContain("\"point\"", actionArtifact, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task TypeTextHandlerRejectsFocusedFallbackWhenFreshFocusProofIsMissing()
    {
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateFocusedWeakStoredState());
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-type-text-focused-fallback-proof-tests"));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinTypeText,
            new { stateToken = token, text = "typed fallback text", allowFocusedFallback = true, confirm = true },
            sessionManager.GetSnapshot());
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeUiAutomationService uiAutomationService = new((window, request, _) =>
            Task.FromResult(
                new UiaSnapshotResult(
                    Status: UiaSnapshotStatusValues.Done,
                    Window: CreateObservedWindow(window),
                    Root: CreateFocusedWeakSnapshotRoot(hasKeyboardFocus: false),
                    RequestedDepth: request.Depth,
                    RequestedMaxNodes: request.MaxNodes,
                    CapturedAtUtc: DateTimeOffset.UtcNow)));
        FakeInputService inputService = new();
        ComputerUseWinTypeTextHandler handler = new(
            new ComputerUseWinActionRequestExecutor(
                new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow()]))),
            new ComputerUseWinTypeTextExecutionCoordinator(
                activationService,
                uiAutomationService,
                inputService));

        ModelContextProtocol.Protocol.CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinTypeTextRequest(
                StateToken: token,
                ElementIndex: null,
                Text: "typed fallback text",
                Confirm: true,
                AllowFocusedFallback: true),
            CancellationToken.None);

        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.Equal(ComputerUseWinFailureCodeValues.StaleState, payload.GetProperty("failureCode").GetString());
        Assert.Equal(0, inputService.Calls);
    }

    [Fact]
    public async Task ScrollHandlerReturnsUnsupportedActionForNonScrollableStoredElement()
    {
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateNonActionableStoredState());
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-scroll-unsupported-tests"));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinScroll,
            new { stateToken = token, elementIndex = 1, direction = "down", pages = 1 },
            sessionManager.GetSnapshot());
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeUiAutomationService uiAutomationService = new();
        FakeUiAutomationScrollService scrollService = new();
        FakeInputService inputService = new();
        ComputerUseWinScrollHandler handler = new(
            new ComputerUseWinActionRequestExecutor(
                new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow()]))),
            new ComputerUseWinScrollExecutionCoordinator(
                activationService,
                uiAutomationService,
                scrollService,
                inputService));

        ModelContextProtocol.Protocol.CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinScrollRequest(StateToken: token, ElementIndex: 1, Direction: "down", Pages: 1, Confirm: false),
            CancellationToken.None);

        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.Equal(ComputerUseWinFailureCodeValues.UnsupportedAction, payload.GetProperty("failureCode").GetString());
        Assert.Equal(0, scrollService.Calls);
        Assert.Equal(0, inputService.Calls);
    }

    [Fact]
    public async Task ScrollHandlerAppliesSemanticScrollForScrollableElement()
    {
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateScrollableStoredState());
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-scroll-semantic-tests"));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinScroll,
            new { stateToken = token, elementIndex = 1, direction = "down", pages = 1 },
            sessionManager.GetSnapshot());
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeUiAutomationService uiAutomationService = new((window, request, _) =>
            Task.FromResult(
                new UiaSnapshotResult(
                    Status: UiaSnapshotStatusValues.Done,
                    Window: CreateObservedWindow(window),
                    Root: new UiaElementSnapshot
                    {
                        ElementId = "root",
                        ControlType = "window",
                        Children =
                        [
                            new UiaElementSnapshot
                            {
                                ElementId = "path:0",
                                ControlType = "list",
                                Name = "Smoke scroll list",
                                AutomationId = "SmokeScrollListBox",
                                BoundingRectangle = new Bounds(10, 20, 220, 180),
                                IsEnabled = true,
                                IsOffscreen = false,
                                Patterns = ["scroll"],
                            },
                        ],
                    },
                    RequestedDepth: request.Depth,
                    RequestedMaxNodes: request.MaxNodes,
                    CapturedAtUtc: DateTimeOffset.UtcNow)));
        FakeUiAutomationScrollService scrollService = new((window, request, _) =>
            Task.FromResult(UiaScrollResult.SuccessResult("scroll_pattern", movementObserved: true)));
        FakeInputService inputService = new();
        ComputerUseWinScrollHandler handler = new(
            new ComputerUseWinActionRequestExecutor(
                new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow()]))),
            new ComputerUseWinScrollExecutionCoordinator(
                activationService,
                uiAutomationService,
                scrollService,
                inputService));

        ModelContextProtocol.Protocol.CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinScrollRequest(StateToken: token, ElementIndex: 1, Direction: "down", Pages: 1, Confirm: false),
            CancellationToken.None);

        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.Done, payload.GetProperty("status").GetString());
        Assert.NotNull(scrollService.LastRequest);
        Assert.Equal("down", scrollService.LastRequest!.Direction);
        Assert.Equal(1, scrollService.LastRequest.Pages);
        Assert.Equal(0, inputService.Calls);
    }

    [Fact]
    public async Task ScrollHandlerReturnsFailedWhenSemanticScrollReportsNoMovement()
    {
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateScrollableStoredState());
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-scroll-no-movement-tests"));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinScroll,
            new { stateToken = token, elementIndex = 1, direction = "down", pages = 1 },
            sessionManager.GetSnapshot());
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeUiAutomationService uiAutomationService = new((window, request, _) =>
            Task.FromResult(
                new UiaSnapshotResult(
                    Status: UiaSnapshotStatusValues.Done,
                    Window: CreateObservedWindow(window),
                    Root: new UiaElementSnapshot
                    {
                        ElementId = "root",
                        ControlType = "window",
                        Children =
                        [
                            new UiaElementSnapshot
                            {
                                ElementId = "path:0",
                                ControlType = "list",
                                Name = "Smoke scroll list",
                                AutomationId = "SmokeScrollListBox",
                                BoundingRectangle = new Bounds(10, 20, 220, 180),
                                IsEnabled = true,
                                IsOffscreen = false,
                                Patterns = ["scroll"],
                            },
                        ],
                    },
                    RequestedDepth: request.Depth,
                    RequestedMaxNodes: request.MaxNodes,
                    CapturedAtUtc: DateTimeOffset.UtcNow)));
        FakeUiAutomationScrollService scrollService = new((window, request, _) =>
            Task.FromResult(UiaScrollResult.FailureResult(UiaScrollFailureKindValues.NoMovement, "semantic scroll did not move", "scroll_pattern")));
        FakeInputService inputService = new();
        ComputerUseWinScrollHandler handler = new(
            new ComputerUseWinActionRequestExecutor(
                new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow()]))),
            new ComputerUseWinScrollExecutionCoordinator(
                activationService,
                uiAutomationService,
                scrollService,
                inputService));

        ModelContextProtocol.Protocol.CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinScrollRequest(StateToken: token, ElementIndex: 1, Direction: "down", Pages: 1, Confirm: false),
            CancellationToken.None);

        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.Equal(ComputerUseWinFailureCodeValues.InputDispatchFailed, payload.GetProperty("failureCode").GetString());
        Assert.Equal(0, inputService.Calls);
    }

    [Fact]
    public async Task ScrollHandlerUsesCoordinateFallbackOnlyWithExplicitConfirmation()
    {
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateScrollableStoredState());
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-scroll-point-confirm-tests"));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinScroll,
            new { stateToken = token, point = new { x = 20, y = 30 }, direction = "down", pages = 1 },
            sessionManager.GetSnapshot());
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeUiAutomationService uiAutomationService = new();
        FakeUiAutomationScrollService scrollService = new();
        FakeInputService inputService = new();
        ComputerUseWinScrollHandler handler = new(
            new ComputerUseWinActionRequestExecutor(
                new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow()]))),
            new ComputerUseWinScrollExecutionCoordinator(
                activationService,
                uiAutomationService,
                scrollService,
                inputService));

        ModelContextProtocol.Protocol.CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinScrollRequest(StateToken: token, Point: new InputPoint(20, 30), CoordinateSpace: InputCoordinateSpaceValues.CapturePixels, Direction: "down", Pages: 1, Confirm: false),
            CancellationToken.None);

        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.ApprovalRequired, payload.GetProperty("status").GetString());
        Assert.Null(activationService.LastHwnd);
        Assert.Equal(0, inputService.Calls);
        Assert.Equal(0, scrollService.Calls);
    }

    [Fact]
    public async Task ScrollHandlerTreatsTrimmedScreenCoordinateSpaceAsScreenPath()
    {
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateStoredStateWithoutCaptureReference());
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-scroll-trimmed-screen-tests"));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinScroll,
            new { stateToken = token, point = new { x = 20, y = 30 }, coordinateSpace = " screen ", direction = "down", pages = 1, confirm = true },
            sessionManager.GetSnapshot());
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeUiAutomationService uiAutomationService = new();
        FakeUiAutomationScrollService scrollService = new();
        FakeInputService inputService = new((request, _, _) =>
            Task.FromResult(
                new InputResult(
                    Status: InputStatusValues.VerifyNeeded,
                    Decision: InputStatusValues.VerifyNeeded,
                    TargetHwnd: request.Hwnd,
                    CompletedActionCount: 1)));
        ComputerUseWinScrollHandler handler = new(
            new ComputerUseWinActionRequestExecutor(
                new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow()]))),
            new ComputerUseWinScrollExecutionCoordinator(
                activationService,
                uiAutomationService,
                scrollService,
                inputService));

        ModelContextProtocol.Protocol.CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinScrollRequest(StateToken: token, Point: new InputPoint(20, 30), CoordinateSpace: " screen ", Direction: "down", Pages: 1, Confirm: true),
            CancellationToken.None);

        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.VerifyNeeded, payload.GetProperty("status").GetString());
        Assert.NotNull(inputService.LastRequest);
        InputAction action = Assert.Single(inputService.LastRequest!.Actions);
        Assert.Equal(InputCoordinateSpaceValues.Screen, action.CoordinateSpace);
        Assert.Null(action.CaptureReference);
        Assert.Equal(0, scrollService.Calls);
    }

    [Fact]
    public async Task ScrollHandlerRejectsMissingCaptureProofBeforeActivation()
    {
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateStoredStateWithoutCaptureReference());
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-scroll-missing-capture-tests"));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinScroll,
            new { stateToken = token, point = new { x = 20, y = 30 }, coordinateSpace = "capture_pixels", direction = "down", pages = 1, confirm = true },
            sessionManager.GetSnapshot());
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeUiAutomationService uiAutomationService = new();
        FakeUiAutomationScrollService scrollService = new();
        FakeInputService inputService = new();
        ComputerUseWinScrollHandler handler = new(
            new ComputerUseWinActionRequestExecutor(
                new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow()]))),
            new ComputerUseWinScrollExecutionCoordinator(
                activationService,
                uiAutomationService,
                scrollService,
                inputService));

        ModelContextProtocol.Protocol.CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinScrollRequest(StateToken: token, Point: new InputPoint(20, 30), CoordinateSpace: InputCoordinateSpaceValues.CapturePixels, Direction: "down", Pages: 1, Confirm: true),
            CancellationToken.None);

        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.Equal(ComputerUseWinFailureCodeValues.CaptureReferenceRequired, payload.GetProperty("failureCode").GetString());
        Assert.Null(activationService.LastHwnd);
        Assert.Equal(0, inputService.Calls);
        Assert.Equal(0, scrollService.Calls);
    }

    [Fact]
    public async Task ScrollHandlerRejectsMalformedPointBeforeActivation()
    {
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateScrollableStoredState());
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-scroll-invalid-point-tests"));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinScroll,
            new { stateToken = token, point = new { x = 20, y = "oops" }, direction = "down", pages = 1, confirm = true },
            sessionManager.GetSnapshot());
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeUiAutomationService uiAutomationService = new();
        FakeUiAutomationScrollService scrollService = new();
        FakeInputService inputService = new();
        InputPoint invalidPoint = JsonSerializer.Deserialize<InputPoint>("""{"x":20,"y":"oops"}""")!;
        ComputerUseWinScrollHandler handler = new(
            new ComputerUseWinActionRequestExecutor(
                new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow()]))),
            new ComputerUseWinScrollExecutionCoordinator(
                activationService,
                uiAutomationService,
                scrollService,
                inputService));

        ModelContextProtocol.Protocol.CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinScrollRequest(StateToken: token, Point: invalidPoint, CoordinateSpace: InputCoordinateSpaceValues.CapturePixels, Direction: "down", Pages: 1, Confirm: true),
            CancellationToken.None);

        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.Equal(ComputerUseWinFailureCodeValues.InvalidRequest, payload.GetProperty("failureCode").GetString());
        Assert.Null(activationService.LastHwnd);
        Assert.Equal(0, inputService.Calls);
        Assert.Equal(0, scrollService.Calls);
    }

    [Fact]
    public async Task ScrollHandlerResolvesSemanticTargetAfterActivation()
    {
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateScrollableStoredState());
        WindowDescriptor activatedWindow = CreateWindow(hwnd: 202, title: "Activated scroll window");
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-scroll-post-activation-fresh-proof-tests"));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinScroll,
            new { stateToken = token, elementIndex = 1, direction = "down", pages = 1, confirm = false },
            sessionManager.GetSnapshot());
        FakeWindowActivationService activationService = new(_ => ActivateWindowResult.Done(activatedWindow, wasMinimized: false, isForeground: true));
        FakeUiAutomationService uiAutomationService = new((window, request, _) =>
            Task.FromResult(
                new UiaSnapshotResult(
                    Status: UiaSnapshotStatusValues.Done,
                    Window: CreateObservedWindow(window),
                    Root: new UiaElementSnapshot
                    {
                        ElementId = "root",
                        ControlType = "window",
                        Children =
                        [
                            new UiaElementSnapshot
                            {
                                ElementId = "path:0",
                                ControlType = "list",
                                Name = "Smoke scroll list",
                                AutomationId = "SmokeScrollListBox",
                                BoundingRectangle = new Bounds(10, 20, 220, 180),
                                IsEnabled = true,
                                IsOffscreen = false,
                                Patterns = ["scroll"],
                            },
                        ],
                    },
                    RequestedDepth: request.Depth,
                    RequestedMaxNodes: request.MaxNodes,
                    CapturedAtUtc: DateTimeOffset.UtcNow)));
        FakeUiAutomationScrollService scrollService = new((window, _, _) =>
            Task.FromResult(UiaScrollResult.SuccessResult("scroll_pattern", movementObserved: true)));
        FakeInputService inputService = new();
        ComputerUseWinScrollHandler handler = new(
            new ComputerUseWinActionRequestExecutor(
                new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow(), activatedWindow]))),
            new ComputerUseWinScrollExecutionCoordinator(
                activationService,
                uiAutomationService,
                scrollService,
                inputService));

        ModelContextProtocol.Protocol.CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinScrollRequest(StateToken: token, ElementIndex: 1, Direction: "down", Pages: 1, Confirm: false),
            CancellationToken.None);

        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.Done, payload.GetProperty("status").GetString());
        Assert.Equal(activatedWindow.Hwnd, uiAutomationService.LastWindow!.Hwnd);
        Assert.Equal(activatedWindow.Hwnd, scrollService.LastWindow!.Hwnd);
        Assert.Equal(0, inputService.Calls);
    }

    [Fact]
    public async Task ScrollHandlerUsesCoordinateFallbackWithConfirmationAndReturnsVerifyNeeded()
    {
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateScrollableStoredState());
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-scroll-point-success-tests"));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinScroll,
            new { stateToken = token, point = new { x = 20, y = 30 }, direction = "down", pages = 2, confirm = true },
            sessionManager.GetSnapshot());
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeUiAutomationService uiAutomationService = new();
        FakeUiAutomationScrollService scrollService = new();
        FakeInputService inputService = new((request, _, _) =>
            Task.FromResult(
                new InputResult(
                    Status: InputStatusValues.VerifyNeeded,
                    Decision: InputStatusValues.VerifyNeeded,
                    TargetHwnd: request.Hwnd,
                    CompletedActionCount: 1)));
        ComputerUseWinScrollHandler handler = new(
            new ComputerUseWinActionRequestExecutor(
                new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow()]))),
            new ComputerUseWinScrollExecutionCoordinator(
                activationService,
                uiAutomationService,
                scrollService,
                inputService));

        ModelContextProtocol.Protocol.CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinScrollRequest(StateToken: token, Point: new InputPoint(20, 30), CoordinateSpace: InputCoordinateSpaceValues.CapturePixels, Direction: "down", Pages: 2, Confirm: true),
            CancellationToken.None);

        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.VerifyNeeded, payload.GetProperty("status").GetString());
        Assert.NotNull(inputService.LastRequest);
        InputAction action = Assert.Single(inputService.LastRequest!.Actions);
        Assert.Equal(InputActionTypeValues.Scroll, action.Type);
        Assert.Equal(-240, action.Delta);
        Assert.Equal(InputCoordinateSpaceValues.CapturePixels, action.CoordinateSpace);
        Assert.Equal(0, scrollService.Calls);
    }

    [Fact]
    public async Task ScrollHandlerReturnsStructuredFailureWhenSemanticScrollServiceThrowsArgumentException()
    {
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateScrollableStoredState());
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-scroll-argument-exception-tests"));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinScroll,
            new { stateToken = token, elementIndex = 1, direction = "down", pages = 1 },
            sessionManager.GetSnapshot());
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeUiAutomationService uiAutomationService = new((window, request, _) =>
            Task.FromResult(
                new UiaSnapshotResult(
                    Status: UiaSnapshotStatusValues.Done,
                    Window: CreateObservedWindow(window),
                    Root: new UiaElementSnapshot
                    {
                        ElementId = "root",
                        ControlType = "window",
                        Children =
                        [
                            new UiaElementSnapshot
                            {
                                ElementId = "path:0",
                                ControlType = "list",
                                Name = "Smoke scroll list",
                                AutomationId = "SmokeScrollListBox",
                                BoundingRectangle = new Bounds(10, 20, 220, 180),
                                IsEnabled = true,
                                IsOffscreen = false,
                                Patterns = ["scroll"],
                            },
                        ],
                    },
                    RequestedDepth: request.Depth,
                    RequestedMaxNodes: request.MaxNodes,
                    CapturedAtUtc: DateTimeOffset.UtcNow)));
        FakeUiAutomationScrollService scrollService = new((_, _, _) => throw new ArgumentException("provider rejected scroll"));
        FakeInputService inputService = new();
        ComputerUseWinScrollHandler handler = new(
            new ComputerUseWinActionRequestExecutor(
                new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow()]))),
            new ComputerUseWinScrollExecutionCoordinator(
                activationService,
                uiAutomationService,
                scrollService,
                inputService));

        ModelContextProtocol.Protocol.CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinScrollRequest(StateToken: token, ElementIndex: 1, Direction: "down", Pages: 1, Confirm: false),
            CancellationToken.None);

        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.Equal(ComputerUseWinFailureCodeValues.InputDispatchFailed, payload.GetProperty("failureCode").GetString());
        Assert.False(string.IsNullOrWhiteSpace(payload.GetProperty("reason").GetString()));
        Assert.Equal(0, inputService.Calls);
    }

    [Fact]
    public async Task DragHandlerRequiresConfirmationForCoordinatePath()
    {
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateDragStoredState());
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-drag-point-confirm-tests"));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinDrag,
            new { stateToken = token, fromPoint = new { x = 20, y = 30 }, toPoint = new { x = 40, y = 60 }, confirm = false },
            sessionManager.GetSnapshot());
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeUiAutomationService uiAutomationService = new();
        FakeInputService inputService = new();
        ComputerUseWinDragHandler handler = new(
            new ComputerUseWinActionRequestExecutor(
                new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow()]))),
            new ComputerUseWinDragExecutionCoordinator(
                activationService,
                new ComputerUseWinDragTargetResolver(uiAutomationService),
                inputService));

        ModelContextProtocol.Protocol.CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinDragRequest(StateToken: token, FromPoint: new InputPoint(20, 30), ToPoint: new InputPoint(40, 60), CoordinateSpace: InputCoordinateSpaceValues.CapturePixels, Confirm: false),
            CancellationToken.None);

        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.ApprovalRequired, payload.GetProperty("status").GetString());
        Assert.Equal(ComputerUseWinFailureCodeValues.ApprovalRequired, payload.GetProperty("failureCode").GetString());
        Assert.Null(activationService.LastHwnd);
        Assert.Equal(0, uiAutomationService.Calls);
        Assert.Equal(0, inputService.Calls);
    }

    [Fact]
    public async Task DragHandlerRejectsMalformedPointBeforeActivation()
    {
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateDragStoredState());
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-drag-invalid-point-tests"));
        InputPoint invalidPoint = CreatePointWithAdditionalProperties(20, 30, ["unexpected"]);
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinDrag,
            new { stateToken = token, fromPoint = new { x = 20, y = 30, unexpected = 1 }, toPoint = new { x = 40, y = 60 }, confirm = true },
            sessionManager.GetSnapshot());
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeUiAutomationService uiAutomationService = new();
        FakeInputService inputService = new();
        ComputerUseWinDragHandler handler = new(
            new ComputerUseWinActionRequestExecutor(
                new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow()]))),
            new ComputerUseWinDragExecutionCoordinator(
                activationService,
                new ComputerUseWinDragTargetResolver(uiAutomationService),
                inputService));

        ModelContextProtocol.Protocol.CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinDragRequest(StateToken: token, FromPoint: invalidPoint, ToPoint: new InputPoint(40, 60), CoordinateSpace: InputCoordinateSpaceValues.CapturePixels, Confirm: true),
            CancellationToken.None);

        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.Equal(ComputerUseWinFailureCodeValues.InvalidRequest, payload.GetProperty("failureCode").GetString());
        Assert.Contains("fromPoint", payload.GetProperty("reason").GetString(), StringComparison.Ordinal);
        Assert.Equal(0, inputService.Calls);
    }

    [Fact]
    public async Task DragHandlerDispatchesElementToElementPathThroughInputService()
    {
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateDragStoredState());
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-drag-element-to-element-tests"));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinDrag,
            new { stateToken = token, fromElementIndex = 1, toElementIndex = 2, confirm = true },
            sessionManager.GetSnapshot());
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeUiAutomationService uiAutomationService = new((window, request, _) => Task.FromResult(
            new UiaSnapshotResult(
                Status: UiaSnapshotStatusValues.Done,
                Window: CreateObservedWindow(window),
                Root: CreateDragSnapshotRoot(),
                RequestedDepth: request.Depth,
                RequestedMaxNodes: request.MaxNodes,
                CapturedAtUtc: DateTimeOffset.UtcNow)));
        FakeInputService inputService = new((request, _, _) => Task.FromResult(
            new InputResult(
                Status: InputStatusValues.VerifyNeeded,
                Decision: InputStatusValues.VerifyNeeded,
                ResultMode: InputResultModeValues.DispatchOnly,
                TargetHwnd: CreateWindow().Hwnd,
                CompletedActionCount: 1,
                Actions:
                [
                    new InputActionResult(
                        Type: InputActionTypeValues.Drag,
                        Status: InputStatusValues.VerifyNeeded,
                        ResultMode: InputResultModeValues.DispatchOnly,
                        CoordinateSpace: InputCoordinateSpaceValues.Screen,
                        ResolvedScreenPoint: new InputPoint(270, 90)),
                ])));
        ComputerUseWinDragHandler handler = new(
            new ComputerUseWinActionRequestExecutor(
                new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow()]))),
            new ComputerUseWinDragExecutionCoordinator(
                activationService,
                new ComputerUseWinDragTargetResolver(uiAutomationService),
                inputService));

        ModelContextProtocol.Protocol.CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinDragRequest(StateToken: token, FromElementIndex: 1, ToElementIndex: 2, Confirm: true),
            CancellationToken.None);

        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.VerifyNeeded, payload.GetProperty("status").GetString());
        Assert.True(payload.GetProperty("refreshStateRecommended").GetBoolean());
        Assert.NotNull(inputService.LastRequest);
        InputAction action = Assert.Single(inputService.LastRequest!.Actions);
        Assert.Equal(InputActionTypeValues.Drag, action.Type);
        Assert.Equal(InputCoordinateSpaceValues.Screen, action.CoordinateSpace);
        Assert.Equal([new InputPoint(50, 50), new InputPoint(270, 90)], action.Path!.ToArray());
    }

    [Fact]
    public async Task DragHandlerReturnsStaleStateWhenFreshSourceCannotBeMatched()
    {
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateDragStoredState());
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-drag-stale-source-tests"));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinDrag,
            new { stateToken = token, fromElementIndex = 1, toElementIndex = 2, confirm = true },
            sessionManager.GetSnapshot());
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeUiAutomationService uiAutomationService = new((window, request, _) => Task.FromResult(
            new UiaSnapshotResult(
                Status: UiaSnapshotStatusValues.Done,
                Window: CreateObservedWindow(window),
                Root: CreateDestinationOnlyDragSnapshotRoot(),
                RequestedDepth: request.Depth,
                RequestedMaxNodes: request.MaxNodes,
                CapturedAtUtc: DateTimeOffset.UtcNow)));
        FakeInputService inputService = new();
        ComputerUseWinDragHandler handler = new(
            new ComputerUseWinActionRequestExecutor(
                new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow()]))),
            new ComputerUseWinDragExecutionCoordinator(
                activationService,
                new ComputerUseWinDragTargetResolver(uiAutomationService),
                inputService));

        ModelContextProtocol.Protocol.CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinDragRequest(StateToken: token, FromElementIndex: 1, ToElementIndex: 2, Confirm: true),
            CancellationToken.None);

        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.Equal(ComputerUseWinFailureCodeValues.StaleState, payload.GetProperty("failureCode").GetString());
        Assert.Contains("source", payload.GetProperty("reason").GetString(), StringComparison.Ordinal);
        Assert.Equal(0, inputService.Calls);
    }

    [Fact]
    public async Task DragHandlerReturnsStaleStateWhenFreshDestinationCannotBeMatched()
    {
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateDragStoredState());
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-drag-stale-destination-tests"));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinDrag,
            new { stateToken = token, fromElementIndex = 1, toElementIndex = 2, confirm = true },
            sessionManager.GetSnapshot());
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeUiAutomationService uiAutomationService = new((window, request, _) => Task.FromResult(
            new UiaSnapshotResult(
                Status: UiaSnapshotStatusValues.Done,
                Window: CreateObservedWindow(window),
                Root: CreateSourceOnlyDragSnapshotRoot(),
                RequestedDepth: request.Depth,
                RequestedMaxNodes: request.MaxNodes,
                CapturedAtUtc: DateTimeOffset.UtcNow)));
        FakeInputService inputService = new();
        ComputerUseWinDragHandler handler = new(
            new ComputerUseWinActionRequestExecutor(
                new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow()]))),
            new ComputerUseWinDragExecutionCoordinator(
                activationService,
                new ComputerUseWinDragTargetResolver(uiAutomationService),
                inputService));

        ModelContextProtocol.Protocol.CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinDragRequest(StateToken: token, FromElementIndex: 1, ToElementIndex: 2, Confirm: true),
            CancellationToken.None);

        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.Equal(ComputerUseWinFailureCodeValues.StaleState, payload.GetProperty("failureCode").GetString());
        Assert.Contains("destination", payload.GetProperty("reason").GetString(), StringComparison.Ordinal);
        Assert.Equal(0, inputService.Calls);
    }

    [Fact]
    public async Task PerformSecondaryActionHandlerReturnsUnsupportedActionForNonSecondaryStoredElement()
    {
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateNonActionableStoredState());
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-secondary-unsupported-tests"));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinPerformSecondaryAction,
            new { stateToken = token, elementIndex = 1 },
            sessionManager.GetSnapshot());
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeUiAutomationService uiAutomationService = new();
        FakeUiAutomationSecondaryActionService secondaryActionService = new();
        ComputerUseWinPerformSecondaryActionHandler handler = new(
            new ComputerUseWinActionRequestExecutor(
                new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow()]))),
            new ComputerUseWinPerformSecondaryActionExecutionCoordinator(
                activationService,
                uiAutomationService,
                secondaryActionService));

        ModelContextProtocol.Protocol.CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinPerformSecondaryActionRequest(StateToken: token, ElementIndex: 1, Confirm: false),
            CancellationToken.None);

        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.Equal(ComputerUseWinFailureCodeValues.UnsupportedAction, payload.GetProperty("failureCode").GetString());
        Assert.Equal(0, secondaryActionService.Calls);
        Assert.Null(activationService.LastHwnd);
    }

    [Fact]
    public async Task PerformSecondaryActionHandlerReturnsApprovalRequiredForRiskySemanticTarget()
    {
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateSecondaryStoredState(name: "Delete archived item"));
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-secondary-approval-tests"));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinPerformSecondaryAction,
            new { stateToken = token, elementIndex = 1 },
            sessionManager.GetSnapshot());
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeUiAutomationService uiAutomationService = new((window, request, _) =>
            Task.FromResult(
                new UiaSnapshotResult(
                    Status: UiaSnapshotStatusValues.Done,
                    Window: CreateObservedWindow(window),
                    Root: new UiaElementSnapshot
                    {
                        ElementId = "root",
                        ControlType = "window",
                        Children =
                        [
                            new UiaElementSnapshot
                            {
                                ElementId = "path:toggle",
                                ControlType = "check_box",
                                Name = "Delete archived item",
                                AutomationId = "RememberSemanticSelectionCheckBox",
                                BoundingRectangle = new Bounds(24, 104, 244, 128),
                                IsEnabled = true,
                                IsOffscreen = false,
                                Patterns = ["toggle"],
                            },
                        ],
                    },
                    RequestedDepth: request.Depth,
                    RequestedMaxNodes: request.MaxNodes,
                    CapturedAtUtc: DateTimeOffset.UtcNow)));
        FakeUiAutomationSecondaryActionService secondaryActionService = new();
        ComputerUseWinPerformSecondaryActionHandler handler = new(
            new ComputerUseWinActionRequestExecutor(
                new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow()]))),
            new ComputerUseWinPerformSecondaryActionExecutionCoordinator(
                activationService,
                uiAutomationService,
                secondaryActionService));

        ModelContextProtocol.Protocol.CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinPerformSecondaryActionRequest(StateToken: token, ElementIndex: 1, Confirm: false),
            CancellationToken.None);

        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.ApprovalRequired, payload.GetProperty("status").GetString());
        Assert.Equal(ComputerUseWinFailureCodeValues.ApprovalRequired, payload.GetProperty("failureCode").GetString());
        Assert.Equal(0, secondaryActionService.Calls);
        Assert.Null(activationService.LastHwnd);
    }

    [Fact]
    public async Task PerformSecondaryActionHandlerReturnsStaleStateWhenFreshTargetCannotBeMatched()
    {
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateSecondaryStoredState());
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-secondary-stale-tests"));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinPerformSecondaryAction,
            new { stateToken = token, elementIndex = 1, confirm = true },
            sessionManager.GetSnapshot());
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeUiAutomationService uiAutomationService = new((window, request, _) =>
            Task.FromResult(
                new UiaSnapshotResult(
                    Status: UiaSnapshotStatusValues.Done,
                    Window: CreateObservedWindow(window),
                    Root: new UiaElementSnapshot
                    {
                        ElementId = "root",
                        ControlType = "window",
                        Children =
                        [
                            new UiaElementSnapshot
                            {
                                ElementId = "path:different",
                                ControlType = "check_box",
                                Name = "Different toggle",
                                AutomationId = "OtherToggle",
                                BoundingRectangle = new Bounds(24, 104, 244, 128),
                                IsEnabled = true,
                                IsOffscreen = false,
                                Patterns = ["toggle"],
                            },
                        ],
                    },
                    RequestedDepth: request.Depth,
                    RequestedMaxNodes: request.MaxNodes,
                    CapturedAtUtc: DateTimeOffset.UtcNow)));
        FakeUiAutomationSecondaryActionService secondaryActionService = new();
        ComputerUseWinPerformSecondaryActionHandler handler = new(
            new ComputerUseWinActionRequestExecutor(
                new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow()]))),
            new ComputerUseWinPerformSecondaryActionExecutionCoordinator(
                activationService,
                uiAutomationService,
                secondaryActionService));

        ModelContextProtocol.Protocol.CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinPerformSecondaryActionRequest(StateToken: token, ElementIndex: 1, Confirm: true),
            CancellationToken.None);

        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.Equal(ComputerUseWinFailureCodeValues.StaleState, payload.GetProperty("failureCode").GetString());
        Assert.Equal(0, secondaryActionService.Calls);
    }

    [Fact]
    public async Task PerformSecondaryActionHandlerReturnsUnsupportedActionWhenFreshTargetLosesSecondarySignal()
    {
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateSecondaryStoredState());
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-secondary-fresh-unsupported-tests"));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinPerformSecondaryAction,
            new { stateToken = token, elementIndex = 1, confirm = true },
            sessionManager.GetSnapshot());
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeUiAutomationService uiAutomationService = new((window, request, _) =>
            Task.FromResult(
                new UiaSnapshotResult(
                    Status: UiaSnapshotStatusValues.Done,
                    Window: CreateObservedWindow(window),
                    Root: new UiaElementSnapshot
                    {
                        ElementId = "root",
                        ControlType = "window",
                        Children =
                        [
                            new UiaElementSnapshot
                            {
                                ElementId = "path:toggle",
                                ControlType = "check_box",
                                Name = "Remember semantic selection: on",
                                AutomationId = "RememberSemanticSelectionCheckBox",
                                BoundingRectangle = new Bounds(24, 104, 244, 128),
                                IsEnabled = true,
                                IsOffscreen = false,
                                Patterns = [],
                            },
                        ],
                    },
                    RequestedDepth: request.Depth,
                    RequestedMaxNodes: request.MaxNodes,
                    CapturedAtUtc: DateTimeOffset.UtcNow)));
        FakeUiAutomationSecondaryActionService secondaryActionService = new();
        ComputerUseWinPerformSecondaryActionHandler handler = new(
            new ComputerUseWinActionRequestExecutor(
                new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow()]))),
            new ComputerUseWinPerformSecondaryActionExecutionCoordinator(
                activationService,
                uiAutomationService,
                secondaryActionService));

        ModelContextProtocol.Protocol.CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinPerformSecondaryActionRequest(StateToken: token, ElementIndex: 1, Confirm: true),
            CancellationToken.None);

        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.Equal(ComputerUseWinFailureCodeValues.UnsupportedAction, payload.GetProperty("failureCode").GetString());
        Assert.Equal(0, secondaryActionService.Calls);
    }

    [Fact]
    public async Task PerformSecondaryActionHandlerAppliesToggleViaSemanticService()
    {
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateSecondaryStoredState());
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-secondary-toggle-tests"));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinPerformSecondaryAction,
            new { stateToken = token, elementIndex = 1, confirm = true },
            sessionManager.GetSnapshot());
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeUiAutomationService uiAutomationService = new((window, request, _) =>
            Task.FromResult(
                new UiaSnapshotResult(
                    Status: UiaSnapshotStatusValues.Done,
                    Window: CreateObservedWindow(window),
                    Root: new UiaElementSnapshot
                    {
                        ElementId = "root",
                        ControlType = "window",
                        Children =
                        [
                            new UiaElementSnapshot
                            {
                                ElementId = "path:toggle",
                                ControlType = "check_box",
                                Name = "Remember semantic selection: on",
                                AutomationId = "RememberSemanticSelectionCheckBox",
                                BoundingRectangle = new Bounds(24, 104, 244, 128),
                                IsEnabled = true,
                                IsOffscreen = false,
                                Patterns = ["toggle"],
                            },
                        ],
                    },
                    RequestedDepth: request.Depth,
                    RequestedMaxNodes: request.MaxNodes,
                    CapturedAtUtc: DateTimeOffset.UtcNow)));
        FakeUiAutomationSecondaryActionService secondaryActionService = new((window, request, _) =>
            Task.FromResult(UiaSecondaryActionResult.SuccessResult(UiaSecondaryActionKindValues.Toggle, "toggle_pattern")));
        ComputerUseWinPerformSecondaryActionHandler handler = new(
            new ComputerUseWinActionRequestExecutor(
                new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow()]))),
            new ComputerUseWinPerformSecondaryActionExecutionCoordinator(
                activationService,
                uiAutomationService,
                secondaryActionService));

        ModelContextProtocol.Protocol.CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinPerformSecondaryActionRequest(StateToken: token, ElementIndex: 1, Confirm: true),
            CancellationToken.None);

        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.Done, payload.GetProperty("status").GetString());
        Assert.NotNull(secondaryActionService.LastRequest);
        Assert.Equal(UiaSecondaryActionKindValues.Toggle, secondaryActionService.LastRequest!.ActionKind);
    }

    [Fact]
    public async Task PerformSecondaryActionHandlerResolvesFreshTargetAfterActivationAndKeepsRiskMarkerWhenConfirmed()
    {
        string root = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        AuditLogOptions options = CreateAuditOptions(root, "computer-use-win-secondary-risky-confirmed-tests");
        AuditLog auditLog = new(options, TimeProvider.System);
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateSecondaryStoredState(name: "Delete archived item"));
        WindowDescriptor activatedWindow = CreateWindow(hwnd: 202, title: "Activated secondary window");
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-secondary-risky-confirmed-tests"));
        using AuditInvocationScope invocation = auditLog.BeginInvocation(
            ToolNames.ComputerUseWinPerformSecondaryAction,
            new { stateToken = token, elementIndex = 1, confirm = true },
            sessionManager.GetSnapshot());
        FakeWindowActivationService activationService = new(_ => ActivateWindowResult.Done(activatedWindow, wasMinimized: false, isForeground: true));
        FakeUiAutomationService uiAutomationService = new((window, request, _) =>
            Task.FromResult(
                new UiaSnapshotResult(
                    Status: UiaSnapshotStatusValues.Done,
                    Window: CreateObservedWindow(window),
                    Root: new UiaElementSnapshot
                    {
                        ElementId = "root",
                        ControlType = "window",
                        Children =
                        [
                            new UiaElementSnapshot
                            {
                                ElementId = "path:toggle",
                                ControlType = "check_box",
                                Name = "Delete archived item",
                                AutomationId = "RememberSemanticSelectionCheckBox",
                                BoundingRectangle = new Bounds(24, 104, 244, 128),
                                IsEnabled = true,
                                IsOffscreen = false,
                                Patterns = ["toggle"],
                            },
                        ],
                    },
                    RequestedDepth: request.Depth,
                    RequestedMaxNodes: request.MaxNodes,
                    CapturedAtUtc: DateTimeOffset.UtcNow)));
        FakeUiAutomationSecondaryActionService secondaryActionService = new((window, request, _) =>
            Task.FromResult(UiaSecondaryActionResult.SuccessResult(UiaSecondaryActionKindValues.Toggle, "toggle_pattern")));
        ComputerUseWinPerformSecondaryActionHandler handler = new(
            new ComputerUseWinActionRequestExecutor(
                new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow(), activatedWindow]))),
            new ComputerUseWinPerformSecondaryActionExecutionCoordinator(
                activationService,
                uiAutomationService,
                secondaryActionService));

        ModelContextProtocol.Protocol.CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinPerformSecondaryActionRequest(StateToken: token, ElementIndex: 1, Confirm: true),
            CancellationToken.None);

        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.Done, payload.GetProperty("status").GetString());
        Assert.Equal(activatedWindow.Hwnd, uiAutomationService.LastWindow!.Hwnd);
        Assert.Equal(activatedWindow.Hwnd, secondaryActionService.LastWindow!.Hwnd);

        string actionEvent = File.ReadAllLines(options.EventsPath)
            .Single(line => line.Contains("\"event_name\":\"computer_use_win.action.completed\"", StringComparison.Ordinal));
        Assert.Contains("\"risk_class\":\"secondary_semantic_risky\"", actionEvent, StringComparison.Ordinal);
        Assert.Contains("\"confirmation_required\":\"true\"", actionEvent, StringComparison.Ordinal);
        Assert.Contains("\"confirmed\":\"true\"", actionEvent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PerformSecondaryActionHandlerReturnsStructuredFailureWhenSemanticServiceThrowsArgumentException()
    {
        ComputerUseWinStateStore stateStore = new();
        string token = stateStore.Create(CreateSecondaryStoredState());
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-secondary-argument-exception-tests"));
        using AuditInvocationScope invocation = CreateAuditLog().BeginInvocation(
            ToolNames.ComputerUseWinPerformSecondaryAction,
            new { stateToken = token, elementIndex = 1, confirm = true },
            sessionManager.GetSnapshot());
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeUiAutomationService uiAutomationService = new((window, request, _) =>
            Task.FromResult(
                new UiaSnapshotResult(
                    Status: UiaSnapshotStatusValues.Done,
                    Window: CreateObservedWindow(window),
                    Root: new UiaElementSnapshot
                    {
                        ElementId = "root",
                        ControlType = "window",
                        Children =
                        [
                            new UiaElementSnapshot
                            {
                                ElementId = "path:toggle",
                                ControlType = "check_box",
                                Name = "Remember semantic selection: on",
                                AutomationId = "RememberSemanticSelectionCheckBox",
                                BoundingRectangle = new Bounds(24, 104, 244, 128),
                                IsEnabled = true,
                                IsOffscreen = false,
                                Patterns = ["toggle"],
                            },
                        ],
                    },
                    RequestedDepth: request.Depth,
                    RequestedMaxNodes: request.MaxNodes,
                    CapturedAtUtc: DateTimeOffset.UtcNow)));
        FakeUiAutomationSecondaryActionService secondaryActionService = new((_, _, _) => throw new ArgumentException("provider rejected secondary action"));
        ComputerUseWinPerformSecondaryActionHandler handler = new(
            new ComputerUseWinActionRequestExecutor(
                new ComputerUseWinStoredStateResolver(stateStore, new FakeListAppsWindowManager([CreateWindow()]))),
            new ComputerUseWinPerformSecondaryActionExecutionCoordinator(
                activationService,
                uiAutomationService,
                secondaryActionService));

        ModelContextProtocol.Protocol.CallToolResult result = await handler.ExecuteAsync(
            invocation,
            new ComputerUseWinPerformSecondaryActionRequest(StateToken: token, ElementIndex: 1, Confirm: true),
            CancellationToken.None);

        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(ComputerUseWinStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.Equal(ComputerUseWinFailureCodeValues.InputDispatchFailed, payload.GetProperty("failureCode").GetString());
    }

    [Fact]
    public void ExecutionTargetCatalogKeepsCurrentBatchResolvableWhenVisibleWindowsExceedCapacity()
    {
        IReadOnlyList<WindowDescriptor> windows =
        [
            CreateWindow(hwnd: 101, title: "Window A", processName: "explorer", processId: 1001, isForeground: false),
            CreateWindow(hwnd: 202, title: "Window B", processName: "explorer", processId: 1001, isForeground: true),
            CreateWindow(hwnd: 303, title: "Window C", processName: "notepad", processId: 2002, threadId: 3003, className: "OtherWindow", isForeground: false),
        ];
        ComputerUseWinExecutionTargetCatalog executionTargetCatalog = new(TimeProvider.System, TimeSpan.FromMinutes(2), maxEntries: 2);

        IReadOnlyList<ComputerUseWinExecutionTarget> targets = executionTargetCatalog.Materialize(windows);

        Assert.Equal(3, targets.Count);
        foreach (ComputerUseWinExecutionTarget target in targets)
        {
            bool resolved = executionTargetCatalog.TryResolveWindowId(
                target.PublicWindowId!,
                windows,
                out ComputerUseWinExecutionTarget? resolvedTarget,
                out WindowDescriptor? _,
                out bool continuityFailed);

            Assert.True(resolved);
            Assert.NotNull(resolvedTarget);
            Assert.False(continuityFailed);
        }
    }

    [Fact]
    public void ExecutionTargetCatalogReusesWindowIdAcrossStrictDiscoveryMatch()
    {
        IReadOnlyList<WindowDescriptor> discoveryBatch =
        [
            CreateWindow(hwnd: 101, title: "Window A", processName: "explorer", processId: 1001, isForeground: false),
            CreateWindow(hwnd: 202, title: "Window B", processName: "explorer", processId: 1001, isForeground: true),
        ];
        ComputerUseWinExecutionTargetCatalog executionTargetCatalog = new(TimeProvider.System, TimeSpan.FromMinutes(2), maxEntries: 16);

        IReadOnlyList<ComputerUseWinExecutionTarget> firstTargets = executionTargetCatalog.Materialize(discoveryBatch);
        IReadOnlyList<ComputerUseWinExecutionTarget> secondTargets = executionTargetCatalog.Materialize(discoveryBatch);

        Assert.Equal(
            firstTargets.Single(target => target.Window.Hwnd == 101).PublicWindowId,
            secondTargets.Single(target => target.Window.Hwnd == 101).PublicWindowId);
        Assert.Equal(
            firstTargets.Single(target => target.Window.Hwnd == 202).PublicWindowId,
            secondTargets.Single(target => target.Window.Hwnd == 202).PublicWindowId);
    }

    [Fact]
    public void ExecutionTargetCatalogIssuesNewWindowIdWhenDiscoverySnapshotDrifts()
    {
        WindowDescriptor originalWindow = CreateWindow(hwnd: 101, title: "Window A", processName: "explorer", processId: 1001, isForeground: false);
        WindowDescriptor driftedWindow = originalWindow with
        {
            Title = "Window A - updated",
        };
        ComputerUseWinExecutionTargetCatalog executionTargetCatalog = new(TimeProvider.System, TimeSpan.FromMinutes(2), maxEntries: 16);

        ComputerUseWinExecutionTarget originalTarget = Assert.Single(executionTargetCatalog.Materialize([originalWindow]));
        ComputerUseWinExecutionTarget driftedTarget = Assert.Single(executionTargetCatalog.Materialize([driftedWindow]));

        Assert.NotEqual(originalTarget.PublicWindowId, driftedTarget.PublicWindowId);
        Assert.False(executionTargetCatalog.TryResolveWindowId(
            originalTarget.PublicWindowId!,
            [driftedWindow],
            out ComputerUseWinExecutionTarget? _,
            out WindowDescriptor? _,
            out bool continuityFailed));
        Assert.False(continuityFailed);
    }

    [Fact]
    public void ExecutionTargetCatalogDoesNotReuseWindowIdForReusedHwndReplacement()
    {
        WindowDescriptor originalWindow = CreateWindow(hwnd: 101, title: "Window A", processName: "explorer", processId: 1001, threadId: 2002, isForeground: false);
        WindowDescriptor replacementWindow = originalWindow with
        {
            ThreadId = 9999,
        };
        ComputerUseWinExecutionTargetCatalog executionTargetCatalog = new(TimeProvider.System, TimeSpan.FromMinutes(2), maxEntries: 16);

        ComputerUseWinExecutionTarget originalTarget = Assert.Single(executionTargetCatalog.Materialize([originalWindow]));
        ComputerUseWinExecutionTarget replacementTarget = Assert.Single(executionTargetCatalog.Materialize([replacementWindow]));

        Assert.NotEqual(originalTarget.PublicWindowId, replacementTarget.PublicWindowId);
        Assert.False(executionTargetCatalog.TryResolveWindowId(
            originalTarget.PublicWindowId!,
            [replacementWindow],
            out ComputerUseWinExecutionTarget? _,
            out WindowDescriptor? _,
            out bool continuityFailed));
        Assert.False(continuityFailed);
    }

    [Fact]
    public void ExecutionTargetCatalogDoesNotReuseWindowIdWhenCurrentDiscoveryBatchHasDuplicateStrictMatches()
    {
        WindowDescriptor originalWindow = CreateWindow(hwnd: 101, title: "Window A", processName: "explorer", processId: 1001, isForeground: false);
        WindowDescriptor duplicateWindow = originalWindow with { };
        ComputerUseWinExecutionTargetCatalog executionTargetCatalog = new(TimeProvider.System, TimeSpan.FromMinutes(2), maxEntries: 16);

        ComputerUseWinExecutionTarget originalTarget = Assert.Single(executionTargetCatalog.Materialize([originalWindow]));
        IReadOnlyList<ComputerUseWinExecutionTarget> duplicateTargets = executionTargetCatalog.Materialize([originalWindow, duplicateWindow]);

        Assert.Equal(2, duplicateTargets.Count);
        Assert.All(duplicateTargets, target => Assert.NotEqual(originalTarget.PublicWindowId, target.PublicWindowId));
        Assert.NotEqual(duplicateTargets[0].PublicWindowId, duplicateTargets[1].PublicWindowId);
    }

    [Fact]
    public void ExecutionTargetCatalogFailsClosedWhenResolvingWindowIdAgainstDuplicateStrictLiveMatches()
    {
        WindowDescriptor originalWindow = CreateWindow(hwnd: 101, title: "Window A", processName: "explorer", processId: 1001, isForeground: false);
        WindowDescriptor duplicateWindow = originalWindow with { };
        ComputerUseWinExecutionTargetCatalog executionTargetCatalog = new(TimeProvider.System, TimeSpan.FromMinutes(2), maxEntries: 16);

        ComputerUseWinExecutionTarget originalTarget = Assert.Single(executionTargetCatalog.Materialize([originalWindow]));

        Exception? exception = Record.Exception(() =>
        {
            bool resolved = executionTargetCatalog.TryResolveWindowId(
                originalTarget.PublicWindowId!,
                [originalWindow, duplicateWindow],
                out ComputerUseWinExecutionTarget? resolvedTarget,
                out WindowDescriptor? failureWindow,
                out bool continuityFailed);

            Assert.False(resolved);
            Assert.Null(resolvedTarget);
            Assert.NotNull(failureWindow);
            Assert.True(continuityFailed);
        });

        Assert.Null(exception);
    }

    [Fact]
    public void ExecutionTargetCatalogPreservesLatestBatchWhenOverflowEvictsOlderGeneration()
    {
        IReadOnlyList<WindowDescriptor> firstBatch =
        [
            CreateWindow(hwnd: 101, title: "Window A", processName: "explorer", processId: 1001, isForeground: false),
            CreateWindow(hwnd: 202, title: "Window B", processName: "explorer", processId: 1001, isForeground: true),
        ];
        IReadOnlyList<WindowDescriptor> secondBatch =
        [
            CreateWindow(hwnd: 303, title: "Window C", processName: "notepad", processId: 2002, threadId: 3003, className: "OtherWindow", isForeground: false),
            CreateWindow(hwnd: 404, title: "Window D", processName: "calc", processId: 4004, threadId: 5005, className: "CalcWindow", isForeground: true),
        ];
        ComputerUseWinExecutionTargetCatalog executionTargetCatalog = new(TimeProvider.System, TimeSpan.FromMinutes(2), maxEntries: 2);

        IReadOnlyList<ComputerUseWinExecutionTarget> firstTargets = executionTargetCatalog.Materialize(firstBatch);
        IReadOnlyList<ComputerUseWinExecutionTarget> secondTargets = executionTargetCatalog.Materialize(secondBatch);

        Assert.All(secondTargets, target =>
        {
            bool resolved = executionTargetCatalog.TryResolveWindowId(
                target.PublicWindowId!,
                secondBatch,
                out ComputerUseWinExecutionTarget? _,
                out WindowDescriptor? _,
                out bool continuityFailed);

            Assert.True(resolved);
            Assert.False(continuityFailed);
        });

        Assert.Contains(firstTargets, target =>
            !executionTargetCatalog.TryResolveWindowId(
                target.PublicWindowId!,
                firstBatch,
                out ComputerUseWinExecutionTarget? _,
                out WindowDescriptor? _,
                out bool _));
    }

    [Fact]
    public void ExecutionTargetCatalogInvalidatesPreviousDiscoveryBatchWhenNewSnapshotIsPublished()
    {
        IReadOnlyList<WindowDescriptor> firstBatch =
        [
            CreateWindow(hwnd: 101, title: "Window A", processName: "explorer", processId: 1001, isForeground: false),
        ];
        IReadOnlyList<WindowDescriptor> secondBatch =
        [
            CreateWindow(hwnd: 202, title: "Window B", processName: "notepad", processId: 2002, threadId: 3003, className: "OtherWindow", isForeground: true),
        ];
        ComputerUseWinExecutionTargetCatalog executionTargetCatalog = new(TimeProvider.System, TimeSpan.FromMinutes(2), maxEntries: 16);

        ComputerUseWinExecutionTarget firstTarget = Assert.Single(executionTargetCatalog.Materialize(firstBatch));
        ComputerUseWinExecutionTarget secondTarget = Assert.Single(executionTargetCatalog.Materialize(secondBatch));

        Assert.True(executionTargetCatalog.TryResolveWindowId(
            secondTarget.PublicWindowId!,
            secondBatch,
            out ComputerUseWinExecutionTarget? resolvedSecondTarget,
            out WindowDescriptor? _,
            out bool secondContinuityFailed));
        Assert.NotNull(resolvedSecondTarget);
        Assert.False(secondContinuityFailed);
        Assert.False(executionTargetCatalog.TryResolveWindowId(
            firstTarget.PublicWindowId!,
            firstBatch,
            out ComputerUseWinExecutionTarget? _,
            out WindowDescriptor? _,
            out bool firstContinuityFailed));
        Assert.False(firstContinuityFailed);
    }

    [Fact]
    public void ExecutionTargetCatalogPreservesLatestDiscoveryBatchAcrossFollowUpIssuance()
    {
        IReadOnlyList<WindowDescriptor> discoveryBatch =
        [
            CreateWindow(hwnd: 101, title: "Window A", processName: "explorer", processId: 1001, isForeground: false),
            CreateWindow(hwnd: 202, title: "Window B", processName: "explorer", processId: 1001, isForeground: true),
        ];
        WindowDescriptor followUpWindow = CreateWindow(
            hwnd: 303,
            title: "Window C",
            processName: "notepad",
            processId: 2002,
            threadId: 3003,
            className: "OtherWindow",
            isForeground: false);
        ComputerUseWinExecutionTargetCatalog executionTargetCatalog = new(TimeProvider.System, TimeSpan.FromMinutes(2), maxEntries: 2);

        IReadOnlyList<ComputerUseWinExecutionTarget> discoveryTargets = executionTargetCatalog.Materialize(discoveryBatch);
        bool followUpIssued = executionTargetCatalog.TryIssue(followUpWindow, out ComputerUseWinExecutionTarget? followUpTarget);

        Assert.True(followUpIssued);
        Assert.NotNull(followUpTarget);
        Assert.All(discoveryTargets, target =>
        {
            bool resolved = executionTargetCatalog.TryResolveWindowId(
                target.PublicWindowId!,
                discoveryBatch,
                out ComputerUseWinExecutionTarget? _,
                out WindowDescriptor? _,
                out bool continuityFailed);

            Assert.True(resolved);
            Assert.False(continuityFailed);
        });
    }

    [Fact]
    public void ExecutionTargetCatalogInvalidatesPreviousDiscoveryBatchWhenNextPublicationIsEmpty()
    {
        IReadOnlyList<WindowDescriptor> discoveryBatch =
        [
            CreateWindow(hwnd: 101, title: "Window A", processName: "explorer", processId: 1001, isForeground: false),
            CreateWindow(hwnd: 202, title: "Window B", processName: "explorer", processId: 1001, isForeground: true),
        ];
        ComputerUseWinExecutionTargetCatalog executionTargetCatalog = new(TimeProvider.System, TimeSpan.FromMinutes(2), maxEntries: 16);

        IReadOnlyList<ComputerUseWinExecutionTarget> discoveryTargets = executionTargetCatalog.Materialize(discoveryBatch);
        IReadOnlyList<ComputerUseWinExecutionTarget> emptyPublication = executionTargetCatalog.Materialize([]);

        Assert.Empty(emptyPublication);
        Assert.All(discoveryTargets, target =>
            Assert.False(
                executionTargetCatalog.TryResolveWindowId(
                    target.PublicWindowId!,
                    discoveryBatch,
                    out ComputerUseWinExecutionTarget? _,
                    out WindowDescriptor? _,
                    out bool continuityFailed)
                || continuityFailed));
    }

    [Fact]
    public void ExecutionTargetCatalogClearsPublishedDiscoveryProtectionWhenNextPublicationIsEmpty()
    {
        IReadOnlyList<WindowDescriptor> discoveryBatch =
        [
            CreateWindow(hwnd: 101, title: "Window A", processName: "explorer", processId: 1001, isForeground: false),
            CreateWindow(hwnd: 202, title: "Window B", processName: "explorer", processId: 1001, isForeground: true),
        ];
        WindowDescriptor followUpWindowA = CreateWindow(
            hwnd: 303,
            title: "Window C",
            processName: "notepad",
            processId: 2002,
            threadId: 3003,
            className: "OtherWindow",
            isForeground: false);
        WindowDescriptor followUpWindowB = CreateWindow(
            hwnd: 404,
            title: "Window D",
            processName: "calc",
            processId: 4004,
            threadId: 5005,
            className: "CalcWindow",
            isForeground: true);
        ComputerUseWinExecutionTargetCatalog executionTargetCatalog = new(TimeProvider.System, TimeSpan.FromMinutes(2), maxEntries: 2);

        IReadOnlyList<ComputerUseWinExecutionTarget> discoveryTargets = executionTargetCatalog.Materialize(discoveryBatch);
        IReadOnlyList<ComputerUseWinExecutionTarget> emptyPublication = executionTargetCatalog.Materialize([]);
        bool firstIssued = executionTargetCatalog.TryIssue(followUpWindowA, out ComputerUseWinExecutionTarget? _);
        bool secondIssued = executionTargetCatalog.TryIssue(followUpWindowB, out ComputerUseWinExecutionTarget? _);

        Assert.Empty(emptyPublication);
        Assert.True(firstIssued);
        Assert.True(secondIssued);
        Assert.All(discoveryTargets, target =>
            Assert.False(
                executionTargetCatalog.TryResolveWindowId(
                    target.PublicWindowId!,
                    discoveryBatch,
                    out ComputerUseWinExecutionTarget? _,
                    out WindowDescriptor? _,
                    out bool _)));
    }

    [Fact]
    public void AccessibilityProjectorCarriesKeyboardFocusIntoStoredElements()
    {
        UiaElementSnapshot root = new()
        {
            ElementId = "root",
            ControlType = "window",
            Children =
            [
                new UiaElementSnapshot
                {
                    ElementId = "child",
                    ControlType = "edit",
                    Name = "Focused input",
                    BoundingRectangle = new Bounds(10, 20, 110, 40),
                    IsEnabled = true,
                    IsOffscreen = false,
                    HasKeyboardFocus = true,
                },
            ],
        };

        IReadOnlyDictionary<int, ComputerUseWinStoredElement> elements = ComputerUseWinAccessibilityProjector.Flatten(root);

        Assert.True(elements[2].HasKeyboardFocus);
        Assert.Contains(ToolNames.ComputerUseWinClick, elements[2].Actions);
    }

    [Fact]
    public async Task ClickTargetResolverRequiresConfirmationForCoordinateTargets()
    {
        ComputerUseWinClickTargetResolver resolver = new(new FakeUiAutomationService());

        ComputerUseWinClickTargetResolution resolution = await resolver.ResolveAsync(
            CreateStoredState(),
            new ComputerUseWinClickRequest(Point: new InputPoint(20, 30)),
            CancellationToken.None);

        Assert.True(resolution.IsSuccess);
        Assert.True(resolution.RequiresConfirmation);
        Assert.NotNull(resolution.Action);
        Assert.Equal(InputCoordinateSpaceValues.CapturePixels, resolution.Action!.CoordinateSpace);
    }

    [Fact]
    public async Task ClickTargetResolverReresolvesElementAgainstFreshSnapshotBeforeDispatch()
    {
        FakeUiAutomationService uiAutomationService = new((window, request, _) => Task.FromResult(
            new UiaSnapshotResult(
                Status: UiaSnapshotStatusValues.Done,
                Window: CreateObservedWindow(window),
                Root: new UiaElementSnapshot
                {
                    ElementId = "root",
                    ControlType = "window",
                    Children =
                    [
                        new UiaElementSnapshot
                        {
                            ElementId = "path:0",
                            ControlType = "button",
                            Name = "Delete item",
                            BoundingRectangle = new Bounds(100, 120, 180, 160),
                            IsEnabled = true,
                            IsOffscreen = false,
                        },
                    ],
                },
                RequestedDepth: request.Depth,
                RequestedMaxNodes: request.MaxNodes,
                CapturedAtUtc: DateTimeOffset.UtcNow)));
        ComputerUseWinClickTargetResolver resolver = new(uiAutomationService);

        ComputerUseWinClickTargetResolution resolution = await resolver.ResolveAsync(
            CreateStoredState(),
            new ComputerUseWinClickRequest(ElementIndex: 1),
            CancellationToken.None);

        Assert.True(resolution.IsSuccess);
        Assert.Equal(768, uiAutomationService.LastRequest?.MaxNodes);
        Assert.NotNull(resolution.Action);
        Assert.Equal(InputCoordinateSpaceValues.Screen, resolution.Action!.CoordinateSpace);
        Assert.Equal(new InputPoint(140, 140), resolution.Action.Point);
        Assert.NotNull(resolution.EffectiveElement);
        Assert.True(resolution.RequiresConfirmation);
    }

    [Fact]
    public async Task ClickTargetResolverReturnsStaleStateWhenFreshSnapshotCannotFindStoredElement()
    {
        FakeUiAutomationService uiAutomationService = new((window, request, _) => Task.FromResult(
            new UiaSnapshotResult(
                Status: UiaSnapshotStatusValues.Done,
                Window: CreateObservedWindow(window),
                Root: new UiaElementSnapshot
                {
                    ElementId = "root",
                    ControlType = "window",
                    Children =
                    [
                        new UiaElementSnapshot
                        {
                            ElementId = "other",
                            ControlType = "button",
                            Name = "Different",
                            BoundingRectangle = new Bounds(100, 120, 180, 160),
                            IsEnabled = true,
                            IsOffscreen = false,
                        },
                    ],
                },
                RequestedDepth: request.Depth,
                RequestedMaxNodes: request.MaxNodes,
                CapturedAtUtc: DateTimeOffset.UtcNow)));
        ComputerUseWinClickTargetResolver resolver = new(uiAutomationService);

        ComputerUseWinClickTargetResolution resolution = await resolver.ResolveAsync(
            CreateStoredState(),
            new ComputerUseWinClickRequest(ElementIndex: 1),
            CancellationToken.None);

        Assert.False(resolution.IsSuccess);
        Assert.Equal(ComputerUseWinFailureCodeValues.StaleState, resolution.FailureCode);
    }

    [Fact]
    public async Task ClickTargetResolverReturnsObservationFailedWhenFreshSnapshotDoesNotComplete()
    {
        FakeUiAutomationService uiAutomationService = new((window, request, _) => Task.FromResult(
            new UiaSnapshotResult(
                Status: UiaSnapshotStatusValues.Failed,
                Reason: "UIA worker did not complete.",
                Window: CreateObservedWindow(window),
                RequestedDepth: request.Depth,
                RequestedMaxNodes: request.MaxNodes,
                CapturedAtUtc: DateTimeOffset.UtcNow)));
        ComputerUseWinClickTargetResolver resolver = new(uiAutomationService);

        ComputerUseWinClickTargetResolution resolution = await resolver.ResolveAsync(
            CreateStoredState(),
            new ComputerUseWinClickRequest(ElementIndex: 1),
            CancellationToken.None);

        Assert.False(resolution.IsSuccess);
        Assert.Equal(ComputerUseWinFailureCodeValues.ObservationFailed, resolution.FailureCode);
        Assert.Contains("UIA worker", resolution.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClickTargetResolverMaterializesUnexpectedSnapshotExceptionsAsStructuredFailure()
    {
        FakeUiAutomationService uiAutomationService = new((_, _, _) => throw new InvalidOperationException("secret uia failure"));
        ComputerUseWinClickTargetResolver resolver = new(uiAutomationService);

        ComputerUseWinClickTargetResolution resolution = await resolver.ResolveAsync(
            CreateStoredState(),
            new ComputerUseWinClickRequest(ElementIndex: 1),
            CancellationToken.None);

        Assert.False(resolution.IsSuccess);
        Assert.Equal(ComputerUseWinFailureCodeValues.ObservationFailed, resolution.FailureCode);
        Assert.DoesNotContain("secret", resolution.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(resolution.FailureDetails);
        Assert.IsType<InvalidOperationException>(resolution.FailureDetails!.AuditException);
    }

    [Fact]
    public async Task ClickExecutionCoordinatorRejectsCoordinateClickWithoutActivationWhenConfirmMissing()
    {
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeInputService inputService = new((_, _, _) => Task.FromResult(
            new InputResult(
                Status: InputStatusValues.Done,
                Decision: InputStatusValues.Done)));
        ComputerUseWinClickExecutionCoordinator coordinator = new(
            activationService,
            new ComputerUseWinClickTargetResolver(new FakeUiAutomationService()),
            inputService);

        ComputerUseWinActionExecutionOutcome outcome = await coordinator.ExecuteAsync(
            CreateStoredState(),
            new ComputerUseWinClickRequest(Point: new InputPoint(20, 30), Confirm: false),
            CancellationToken.None);

        Assert.True(outcome.IsApprovalRequired);
        Assert.Equal(ComputerUseWinActionLifecyclePhase.BeforeActivation, outcome.Phase);
        Assert.Null(activationService.LastHwnd);
        Assert.Equal(0, inputService.Calls);
    }

    [Fact]
    public async Task ClickExecutionCoordinatorRejectsMalformedRequestWithoutActivation()
    {
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeInputService inputService = new((_, _, _) => Task.FromResult(
            new InputResult(
                Status: InputStatusValues.Done,
                Decision: InputStatusValues.Done)));
        ComputerUseWinClickExecutionCoordinator coordinator = new(
            activationService,
            new ComputerUseWinClickTargetResolver(new FakeUiAutomationService()),
            inputService);

        ComputerUseWinActionExecutionOutcome outcome = await coordinator.ExecuteAsync(
            CreateStoredState(),
            new ComputerUseWinClickRequest(Confirm: true),
            CancellationToken.None);

        Assert.False(outcome.IsSuccess);
        Assert.False(outcome.IsApprovalRequired);
        Assert.Equal(ComputerUseWinFailureCodeValues.InvalidRequest, outcome.FailureDetails?.FailureCode);
        Assert.Null(activationService.LastHwnd);
        Assert.Equal(0, inputService.Calls);
    }

    [Fact]
    public async Task ClickExecutionCoordinatorRejectsRiskyElementWithoutActivationWhenConfirmMissing()
    {
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeInputService inputService = new((_, _, _) => Task.FromResult(
            new InputResult(
                Status: InputStatusValues.Done,
                Decision: InputStatusValues.Done)));
        ComputerUseWinClickExecutionCoordinator coordinator = new(
            activationService,
            new ComputerUseWinClickTargetResolver(new FakeUiAutomationService()),
            inputService);

        ComputerUseWinActionExecutionOutcome outcome = await coordinator.ExecuteAsync(
            CreateStoredState(),
            new ComputerUseWinClickRequest(ElementIndex: 1, Confirm: false),
            CancellationToken.None);

        Assert.True(outcome.IsApprovalRequired);
        Assert.Null(activationService.LastHwnd);
        Assert.Equal(0, inputService.Calls);
    }

    [Fact]
    public async Task ClickExecutionCoordinatorRejectsStoredElementWithoutClickAffordanceBeforeActivation()
    {
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeInputService inputService = new((_, _, _) => Task.FromResult(
            new InputResult(
                Status: InputStatusValues.Done,
                Decision: InputStatusValues.Done)));
        ComputerUseWinClickExecutionCoordinator coordinator = new(
            activationService,
            new ComputerUseWinClickTargetResolver(new FakeUiAutomationService()),
            inputService);

        ComputerUseWinActionExecutionOutcome outcome = await coordinator.ExecuteAsync(
            CreateNonActionableStoredState(),
            new ComputerUseWinClickRequest(ElementIndex: 1, Confirm: true),
            CancellationToken.None);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(ComputerUseWinFailureCodeValues.InvalidRequest, outcome.FailureDetails?.FailureCode);
        Assert.Null(activationService.LastHwnd);
        Assert.Equal(0, inputService.Calls);
    }

    [Fact]
    public async Task ClickExecutionCoordinatorRejectsCapturePixelsWithoutStoredCaptureReferenceBeforeActivation()
    {
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeInputService inputService = new((_, _, _) => Task.FromResult(
            new InputResult(
                Status: InputStatusValues.Done,
                Decision: InputStatusValues.Done)));
        ComputerUseWinClickExecutionCoordinator coordinator = new(
            activationService,
            new ComputerUseWinClickTargetResolver(new FakeUiAutomationService()),
            inputService);

        ComputerUseWinActionExecutionOutcome outcome = await coordinator.ExecuteAsync(
            CreateStoredStateWithoutCaptureReference(),
            new ComputerUseWinClickRequest(Point: new InputPoint(20, 30), Confirm: true),
            CancellationToken.None);

        Assert.False(outcome.IsSuccess);
        Assert.False(outcome.IsApprovalRequired);
        Assert.Equal(ComputerUseWinFailureCodeValues.CaptureReferenceRequired, outcome.FailureDetails?.FailureCode);
        Assert.Equal(ComputerUseWinActionLifecyclePhase.BeforeActivation, outcome.Phase);
        Assert.Null(activationService.LastHwnd);
        Assert.Equal(0, inputService.Calls);
    }

    [Fact]
    public async Task ClickExecutionCoordinatorRejectsCapturePixelsPointOutsideStoredRasterBeforeActivation()
    {
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeInputService inputService = new((_, _, _) => Task.FromResult(
            new InputResult(
                Status: InputStatusValues.Done,
                Decision: InputStatusValues.Done)));
        ComputerUseWinClickExecutionCoordinator coordinator = new(
            activationService,
            new ComputerUseWinClickTargetResolver(new FakeUiAutomationService()),
            inputService);

        ComputerUseWinActionExecutionOutcome outcome = await coordinator.ExecuteAsync(
            CreateStoredState(),
            new ComputerUseWinClickRequest(Point: new InputPoint(9999, 30), Confirm: true),
            CancellationToken.None);

        Assert.False(outcome.IsSuccess);
        Assert.False(outcome.IsApprovalRequired);
        Assert.Equal(ComputerUseWinFailureCodeValues.PointOutOfBounds, outcome.FailureDetails?.FailureCode);
        Assert.Equal(ComputerUseWinActionLifecyclePhase.BeforeActivation, outcome.Phase);
        Assert.Null(activationService.LastHwnd);
        Assert.Equal(0, inputService.Calls);
    }

    [Fact]
    public async Task ClickExecutionCoordinatorMapsMissingActivationTargetWithoutPolicyBlock()
    {
        FakeWindowActivationService activationService = new(static _ => ActivateWindowResult.Failed(
            "Окно для активации больше не найдено.",
            wasMinimized: false,
            failureKind: ActivationFailureKindValues.MissingTarget));
        FakeInputService inputService = new((_, _, _) => Task.FromResult(
            new InputResult(
                Status: InputStatusValues.Done,
                Decision: InputStatusValues.Done)));
        ComputerUseWinClickExecutionCoordinator coordinator = new(
            activationService,
            new ComputerUseWinClickTargetResolver(new FakeUiAutomationService()),
            inputService);

        ComputerUseWinActionExecutionOutcome outcome = await coordinator.ExecuteAsync(
            CreateStoredState(),
            new ComputerUseWinClickRequest(Point: new InputPoint(20, 30), Confirm: true),
            CancellationToken.None);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(ComputerUseWinFailureCodeValues.MissingTarget, outcome.FailureDetails?.FailureCode);
        Assert.NotEqual(ComputerUseWinFailureCodeValues.BlockedTarget, outcome.FailureDetails?.FailureCode);
        Assert.Equal(ComputerUseWinActionLifecyclePhase.AfterActivationBeforeDispatch, outcome.Phase);
        Assert.Equal(101, activationService.LastHwnd);
        Assert.Equal(0, inputService.Calls);
    }

    [Fact]
    public async Task ClickExecutionCoordinatorMapsMinimizedActivationFailureWithoutPolicyBlock()
    {
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Ambiguous(
            "Окно снова оказалось свернутым до завершения активации.",
            window with { WindowState = WindowStateValues.Minimized },
            wasMinimized: true,
            isForeground: false,
            failureKind: ActivationFailureKindValues.RestoreFailedStillMinimized));
        FakeInputService inputService = new((_, _, _) => Task.FromResult(
            new InputResult(
                Status: InputStatusValues.Done,
                Decision: InputStatusValues.Done)));
        ComputerUseWinClickExecutionCoordinator coordinator = new(
            activationService,
            new ComputerUseWinClickTargetResolver(new FakeUiAutomationService()),
            inputService);

        ComputerUseWinActionExecutionOutcome outcome = await coordinator.ExecuteAsync(
            CreateStoredState(),
            new ComputerUseWinClickRequest(Point: new InputPoint(20, 30), Confirm: true),
            CancellationToken.None);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(ComputerUseWinFailureCodeValues.TargetMinimized, outcome.FailureDetails?.FailureCode);
        Assert.NotEqual(ComputerUseWinFailureCodeValues.BlockedTarget, outcome.FailureDetails?.FailureCode);
        Assert.Equal(ComputerUseWinActionLifecyclePhase.AfterActivationBeforeDispatch, outcome.Phase);
        Assert.Equal(101, activationService.LastHwnd);
        Assert.Equal(0, inputService.Calls);
    }

    [Fact]
    public async Task ClickExecutionCoordinatorDoesNotTreatMinimizedIdentityLossAsMinimizedFailure()
    {
        FakeWindowActivationService activationService = new(static _ => ActivateWindowResult.Failed(
            "Окно для активации больше не найдено или больше не совпадает с исходной identity.",
            wasMinimized: true,
            failureKind: ActivationFailureKindValues.MissingTarget));
        FakeInputService inputService = new((_, _, _) => Task.FromResult(
            new InputResult(
                Status: InputStatusValues.Done,
                Decision: InputStatusValues.Done)));
        ComputerUseWinClickExecutionCoordinator coordinator = new(
            activationService,
            new ComputerUseWinClickTargetResolver(new FakeUiAutomationService()),
            inputService);

        ComputerUseWinActionExecutionOutcome outcome = await coordinator.ExecuteAsync(
            CreateStoredState(),
            new ComputerUseWinClickRequest(Point: new InputPoint(20, 30), Confirm: true),
            CancellationToken.None);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(ComputerUseWinFailureCodeValues.MissingTarget, outcome.FailureDetails?.FailureCode);
        Assert.NotEqual(ComputerUseWinFailureCodeValues.TargetMinimized, outcome.FailureDetails?.FailureCode);
        Assert.NotEqual(ComputerUseWinFailureCodeValues.BlockedTarget, outcome.FailureDetails?.FailureCode);
        Assert.Equal(ComputerUseWinActionLifecyclePhase.AfterActivationBeforeDispatch, outcome.Phase);
        Assert.Equal(101, activationService.LastHwnd);
        Assert.Equal(0, inputService.Calls);
    }

    [Fact]
    public async Task ClickExecutionCoordinatorMapsTypedActivationIdentityChangedAsStaleState()
    {
        FakeWindowActivationService activationService = new(static _ => ActivateWindowResult.Failed(
            "Окно для активации больше не совпадает с исходной identity в финальном activation snapshot.",
            wasMinimized: true,
            failureKind: ActivationFailureKindValues.IdentityChanged));
        FakeInputService inputService = new((_, _, _) => Task.FromResult(
            new InputResult(
                Status: InputStatusValues.Done,
                Decision: InputStatusValues.Done)));
        ComputerUseWinClickExecutionCoordinator coordinator = new(
            activationService,
            new ComputerUseWinClickTargetResolver(new FakeUiAutomationService()),
            inputService);

        ComputerUseWinActionExecutionOutcome outcome = await coordinator.ExecuteAsync(
            CreateStoredState(),
            new ComputerUseWinClickRequest(Point: new InputPoint(20, 30), Confirm: true),
            CancellationToken.None);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(ComputerUseWinFailureCodeValues.StaleState, outcome.FailureDetails?.FailureCode);
        Assert.NotEqual(ComputerUseWinFailureCodeValues.TargetMinimized, outcome.FailureDetails?.FailureCode);
        Assert.NotEqual(ComputerUseWinFailureCodeValues.BlockedTarget, outcome.FailureDetails?.FailureCode);
        Assert.Equal(ComputerUseWinActionLifecyclePhase.AfterActivationBeforeDispatch, outcome.Phase);
        Assert.Equal(101, activationService.LastHwnd);
        Assert.Equal(0, inputService.Calls);
    }

    [Fact]
    public async Task ClickExecutionCoordinatorMapsForegroundActivationFailureWithoutPolicyBlock()
    {
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Failed(
            "Windows отказалась перевести окно в foreground.",
            window,
            wasMinimized: false,
            isForeground: false,
            failureKind: ActivationFailureKindValues.ForegroundNotConfirmed));
        FakeInputService inputService = new((_, _, _) => Task.FromResult(
            new InputResult(
                Status: InputStatusValues.Done,
                Decision: InputStatusValues.Done)));
        ComputerUseWinClickExecutionCoordinator coordinator = new(
            activationService,
            new ComputerUseWinClickTargetResolver(new FakeUiAutomationService()),
            inputService);

        ComputerUseWinActionExecutionOutcome outcome = await coordinator.ExecuteAsync(
            CreateStoredState(),
            new ComputerUseWinClickRequest(Point: new InputPoint(20, 30), Confirm: true),
            CancellationToken.None);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(ComputerUseWinFailureCodeValues.TargetNotForeground, outcome.FailureDetails?.FailureCode);
        Assert.NotEqual(ComputerUseWinFailureCodeValues.BlockedTarget, outcome.FailureDetails?.FailureCode);
        Assert.Equal(ComputerUseWinActionLifecyclePhase.AfterActivationBeforeDispatch, outcome.Phase);
        Assert.Equal(101, activationService.LastHwnd);
        Assert.Equal(0, inputService.Calls);
    }

    [Fact]
    public async Task ClickTargetResolverReturnsStaleStateWhenFreshElementLosesClickAffordance()
    {
        FakeUiAutomationService uiAutomationService = new((window, request, _) => Task.FromResult(
            new UiaSnapshotResult(
                Status: UiaSnapshotStatusValues.Done,
                Window: CreateObservedWindow(window),
                Root: new UiaElementSnapshot
                {
                    ElementId = "root",
                    ControlType = "window",
                    Children =
                    [
                        new UiaElementSnapshot
                        {
                            ElementId = "path:0",
                            ControlType = "button",
                            Name = "Delete item",
                            AutomationId = "DeleteButton",
                            BoundingRectangle = new Bounds(100, 120, 180, 160),
                            IsEnabled = false,
                            IsOffscreen = false,
                        },
                    ],
                },
                RequestedDepth: request.Depth,
                RequestedMaxNodes: request.MaxNodes,
                CapturedAtUtc: DateTimeOffset.UtcNow)));
        ComputerUseWinClickTargetResolver resolver = new(uiAutomationService);

        ComputerUseWinClickTargetResolution resolution = await resolver.ResolveAsync(
            CreateStoredState(),
            new ComputerUseWinClickRequest(ElementIndex: 1),
            CancellationToken.None);

        Assert.False(resolution.IsSuccess);
        Assert.Equal(ComputerUseWinFailureCodeValues.StaleState, resolution.FailureCode);
    }

    [Fact]
    public async Task ClickTargetResolverDoesNotFallbackOnControlTypeOnlyMatch()
    {
        FakeUiAutomationService uiAutomationService = new((window, request, _) => Task.FromResult(
            new UiaSnapshotResult(
                Status: UiaSnapshotStatusValues.Done,
                Window: CreateObservedWindow(window),
                Root: new UiaElementSnapshot
                {
                    ElementId = "root",
                    ControlType = "window",
                    Children =
                    [
                        new UiaElementSnapshot
                        {
                            ElementId = "other",
                            ControlType = "button",
                            BoundingRectangle = new Bounds(100, 120, 180, 160),
                            IsEnabled = true,
                            IsOffscreen = false,
                        },
                    ],
                },
                RequestedDepth: request.Depth,
                RequestedMaxNodes: request.MaxNodes,
                CapturedAtUtc: DateTimeOffset.UtcNow)));
        ComputerUseWinClickTargetResolver resolver = new(uiAutomationService);

        ComputerUseWinClickTargetResolution resolution = await resolver.ResolveAsync(
            CreateUnnamedStoredState(),
            new ComputerUseWinClickRequest(ElementIndex: 1),
            CancellationToken.None);

        Assert.False(resolution.IsSuccess);
        Assert.Equal(ComputerUseWinFailureCodeValues.StaleState, resolution.FailureCode);
    }

    [Fact]
    public async Task ClickTargetResolverDoesNotFallbackOnLabelOnlyMatch()
    {
        FakeUiAutomationService uiAutomationService = new((window, request, _) => Task.FromResult(
            new UiaSnapshotResult(
                Status: UiaSnapshotStatusValues.Done,
                Window: CreateObservedWindow(window),
                Root: new UiaElementSnapshot
                {
                    ElementId = "root",
                    ControlType = "window",
                    Children =
                    [
                        new UiaElementSnapshot
                        {
                            ElementId = "other",
                            ControlType = "button",
                            Name = "Delete item",
                            BoundingRectangle = new Bounds(100, 120, 180, 160),
                            IsEnabled = true,
                            IsOffscreen = false,
                        },
                    ],
                },
                RequestedDepth: request.Depth,
                RequestedMaxNodes: request.MaxNodes,
                CapturedAtUtc: DateTimeOffset.UtcNow)));
        ComputerUseWinClickTargetResolver resolver = new(uiAutomationService);

        ComputerUseWinClickTargetResolution resolution = await resolver.ResolveAsync(
            CreateLabelOnlyStoredState(),
            new ComputerUseWinClickRequest(ElementIndex: 1),
            CancellationToken.None);

        Assert.False(resolution.IsSuccess);
        Assert.Equal(ComputerUseWinFailureCodeValues.StaleState, resolution.FailureCode);
    }

    [Fact]
    public async Task ClickExecutionCoordinatorReresolvesElementTargetAfterActivationRetry()
    {
        int snapshotCall = 0;
        FakeUiAutomationService uiAutomationService = new((window, request, _) =>
        {
            snapshotCall++;
            Bounds bounds = snapshotCall == 1
                ? new Bounds(100, 100, 140, 140)
                : new Bounds(200, 200, 240, 240);
            return Task.FromResult(
                new UiaSnapshotResult(
                    Status: UiaSnapshotStatusValues.Done,
                    Window: CreateObservedWindow(window),
                    Root: new UiaElementSnapshot
                    {
                        ElementId = "root",
                        ControlType = "window",
                        Children =
                        [
                            new UiaElementSnapshot
                            {
                                ElementId = "path:0",
                                ControlType = "button",
                                Name = "Delete item",
                                AutomationId = "DeleteButton",
                                BoundingRectangle = bounds,
                                IsEnabled = true,
                                IsOffscreen = false,
                            },
                        ],
                    },
                    RequestedDepth: request.Depth,
                    RequestedMaxNodes: request.MaxNodes,
                    CapturedAtUtc: DateTimeOffset.UtcNow));
        });
        int activationCalls = 0;
        FakeWindowActivationService activationService = new(window =>
        {
            activationCalls++;
            return ActivateWindowResult.Done(
                window with { Bounds = new Bounds(50, 50, 500, 500) },
                wasMinimized: false,
                isForeground: true);
        });
        int inputCalls = 0;
        FakeInputService inputService = new((request, _, _) =>
        {
            inputCalls++;
            return Task.FromResult(
                inputCalls == 1
                    ? new InputResult(
                        Status: InputStatusValues.Failed,
                        Decision: InputStatusValues.Failed,
                        FailureCode: InputFailureCodeValues.TargetNotForeground,
                        Reason: "target not foreground",
                        TargetHwnd: 101)
                    : new InputResult(
                        Status: InputStatusValues.Done,
                        Decision: InputStatusValues.Done,
                        TargetHwnd: 101));
        });
        ComputerUseWinClickExecutionCoordinator coordinator = new(
            activationService,
            new ComputerUseWinClickTargetResolver(uiAutomationService),
            inputService);

        ComputerUseWinActionExecutionOutcome outcome = await coordinator.ExecuteAsync(
            CreateStoredState(),
            new ComputerUseWinClickRequest(ElementIndex: 1, Confirm: true),
            CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(2, inputService.Calls);
        Assert.Equal(2, activationCalls);
        Assert.Equal(2, snapshotCall);
        Assert.Equal(new InputPoint(220, 220), inputService.LastRequest!.Actions[0].Point);
    }

    [Fact]
    public async Task ClickExecutionCoordinatorReappliesConfirmationAfterRetryReresolution()
    {
        int snapshotCall = 0;
        FakeUiAutomationService uiAutomationService = new((window, request, _) =>
        {
            snapshotCall++;
            UiaElementSnapshot element = snapshotCall == 1
                ? new UiaElementSnapshot
                {
                    ElementId = "path:0",
                    ControlType = "button",
                    Name = "Continue",
                    AutomationId = "ContinueButton",
                    BoundingRectangle = new Bounds(100, 100, 140, 140),
                    IsEnabled = true,
                    IsOffscreen = false,
                }
                : new UiaElementSnapshot
                {
                    ElementId = "path:0",
                    ControlType = "button",
                    Name = "Delete item",
                    AutomationId = "DeleteButton",
                    BoundingRectangle = new Bounds(200, 200, 240, 240),
                    IsEnabled = true,
                    IsOffscreen = false,
                };

            return Task.FromResult(
                new UiaSnapshotResult(
                    Status: UiaSnapshotStatusValues.Done,
                    Window: CreateObservedWindow(window),
                    Root: new UiaElementSnapshot
                    {
                        ElementId = "root",
                        ControlType = "window",
                        Children = [element],
                    },
                    RequestedDepth: request.Depth,
                    RequestedMaxNodes: request.MaxNodes,
                    CapturedAtUtc: DateTimeOffset.UtcNow));
        });
        int activationCalls = 0;
        FakeWindowActivationService activationService = new(window =>
        {
            activationCalls++;
            return ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true);
        });
        FakeInputService inputService = new((request, _, _) =>
            Task.FromResult(
                new InputResult(
                    Status: InputStatusValues.Failed,
                    Decision: InputStatusValues.Failed,
                    FailureCode: InputFailureCodeValues.TargetNotForeground,
                    Reason: "target not foreground",
                    TargetHwnd: 101)));
        ComputerUseWinClickExecutionCoordinator coordinator = new(
            activationService,
            new ComputerUseWinClickTargetResolver(uiAutomationService),
            inputService);

        ComputerUseWinActionExecutionOutcome outcome = await coordinator.ExecuteAsync(
            CreateSafeStoredState(),
            new ComputerUseWinClickRequest(ElementIndex: 1, Confirm: false),
            CancellationToken.None);

        Assert.True(outcome.IsApprovalRequired);
        Assert.Equal(ComputerUseWinActionLifecyclePhase.AfterRevalidationBeforeDispatch, outcome.Phase);
        Assert.Equal(1, inputService.Calls);
        Assert.Equal(2, activationCalls);
        Assert.Equal(2, snapshotCall);
    }

    private static ComputerUseWinTools CreateComputerUseWinTools(
        IWindowManager windowManager,
        ComputerUseWinApprovalStore approvalStore)
    {
        ComputerUseWinExecutionTargetCatalog executionTargetCatalog = new();
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-stage-2-test"));
        ComputerUseWinStateStore stateStore = new();
        FakeUiAutomationService uiAutomationService = new();
        FakeUiAutomationSetValueService setValueService = new();
        FakeWindowActivationService activationService = new();
        FakeInputService inputService = new();
        ComputerUseWinAppStateObserver appStateObserver = new(
            new NoopCaptureService(),
            uiAutomationService,
            new EmptyInstructionProvider());
        ComputerUseWinClickExecutionCoordinator clickExecutionCoordinator = new(
            activationService,
            new ComputerUseWinClickTargetResolver(uiAutomationService),
            inputService);
        ComputerUseWinDragExecutionCoordinator dragExecutionCoordinator = new(
            activationService,
            new ComputerUseWinDragTargetResolver(uiAutomationService),
            inputService);
        ComputerUseWinPressKeyExecutionCoordinator pressKeyExecutionCoordinator = new(
            activationService,
            inputService);
        ComputerUseWinSetValueExecutionCoordinator setValueExecutionCoordinator = new(
            activationService,
            uiAutomationService,
            setValueService);
        ComputerUseWinTypeTextExecutionCoordinator typeTextExecutionCoordinator = new(
            activationService,
            uiAutomationService,
            inputService);
        FakeUiAutomationScrollService scrollService = new();
        ComputerUseWinScrollExecutionCoordinator scrollExecutionCoordinator = new(
            activationService,
            uiAutomationService,
            scrollService,
            inputService);
        FakeUiAutomationSecondaryActionService secondaryActionService = new();
        ComputerUseWinPerformSecondaryActionExecutionCoordinator performSecondaryActionExecutionCoordinator = new(
            activationService,
            uiAutomationService,
            secondaryActionService);
        ComputerUseWinActionRequestExecutor actionRequestExecutor = new(
            new ComputerUseWinStoredStateResolver(stateStore, windowManager),
            appStateObserver,
            stateStore,
            sessionManager);

        return new(
            CreateAuditLog(),
            sessionManager,
            new ComputerUseWinListAppsHandler(new ComputerUseWinAppDiscoveryService(windowManager, approvalStore, executionTargetCatalog)),
            new ComputerUseWinGetAppStateHandler(
                windowManager,
                sessionManager,
                approvalStore,
                executionTargetCatalog,
                stateStore,
                activationService,
                appStateObserver),
            new ComputerUseWinClickHandler(
                actionRequestExecutor,
                clickExecutionCoordinator),
            new ComputerUseWinDragHandler(
                actionRequestExecutor,
                dragExecutionCoordinator),
            new ComputerUseWinPerformSecondaryActionHandler(
                actionRequestExecutor,
                performSecondaryActionExecutionCoordinator),
            new ComputerUseWinPressKeyHandler(
                actionRequestExecutor,
                pressKeyExecutionCoordinator),
            new ComputerUseWinScrollHandler(
                actionRequestExecutor,
                scrollExecutionCoordinator),
            new ComputerUseWinSetValueHandler(
                actionRequestExecutor,
                setValueExecutionCoordinator),
            new ComputerUseWinTypeTextHandler(
                actionRequestExecutor,
                typeTextExecutionCoordinator));
    }

    private static ComputerUseWinGetAppStateHandler CreateGetAppStateHandler(
        IWindowManager windowManager,
        ISessionManager sessionManager,
        ComputerUseWinApprovalStore approvalStore,
        ComputerUseWinExecutionTargetCatalog executionTargetCatalog) =>
        new(
            windowManager,
            sessionManager,
            approvalStore,
            executionTargetCatalog,
            new ComputerUseWinStateStore(),
            new FakeWindowActivationService(),
            new ComputerUseWinAppStateObserver(
                new NoopCaptureService(),
                new FakeUiAutomationService(),
                new EmptyInstructionProvider()));

    private static ComputerUseWinApprovalStore CreateApprovalStore(TempDirectoryScope temp) =>
        new(
            new ComputerUseWinOptions(
                PluginRoot: temp.Root,
                AppInstructionsRoot: Path.Combine(temp.Root, "references", "AppInstructions"),
                ApprovalStorePath: Path.Combine(temp.Root, "AppApprovals.json")));

    private static AuditLog CreateAuditLog()
    {
        string root = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return new AuditLog(CreateAuditOptions(root, "computer-use-win-stage-2-test"), TimeProvider.System);
    }

    private static AuditLogOptions CreateAuditOptions(string root, string runId) =>
        new(
            ContentRootPath: root,
            EnvironmentName: "tests",
            RunId: runId,
            DiagnosticsRoot: Path.Combine(root, "artifacts", "diagnostics"),
            RunDirectory: Path.Combine(root, "artifacts", "diagnostics", runId),
            EventsPath: Path.Combine(root, "artifacts", "diagnostics", runId, "events.jsonl"),
            SummaryPath: Path.Combine(root, "artifacts", "diagnostics", runId, "summary.md"));

    private static ComputerUseWinAppSession CreateSession() =>
        new("explorer", "cw_explorer_101", 101, "Explorer", "explorer", 1001);

    private static ActivateWindowResult CreateWarningActivationResult(string activationStatus, WindowDescriptor window) =>
        activationStatus switch
        {
            ActivateWindowStatusValues.Ambiguous => ActivateWindowResult.Ambiguous(
                "activation warning",
                window,
                wasMinimized: true,
                isForeground: false,
                failureKind: ActivationFailureKindValues.ForegroundNotConfirmed),
            ActivateWindowStatusValues.Failed => ActivateWindowResult.Failed(
                "activation warning",
                window,
                wasMinimized: true,
                isForeground: false,
                failureKind: ActivationFailureKindValues.ForegroundNotConfirmed),
            _ => throw new ArgumentOutOfRangeException(nameof(activationStatus), activationStatus, "Неизвестный activation status для warning-path теста."),
        };

    private static void AssertWindowIdNotPublished(JsonElement session)
    {
        if (!session.TryGetProperty("windowId", out JsonElement windowId))
        {
            return;
        }

        Assert.Equal(JsonValueKind.Null, windowId.ValueKind);
    }

    private static ComputerUseWinStoredState CreateStoredState() =>
        new(
            CreateSession(),
            CreateWindow(),
            CaptureReference: CreateCaptureReference(),
            Elements: new Dictionary<int, ComputerUseWinStoredElement>
            {
                [1] = new(
                    Index: 1,
                    ElementId: "path:0",
                    Name: "Delete item",
                    AutomationId: "DeleteButton",
                    ControlType: "button",
                    Bounds: new Bounds(10, 20, 50, 60),
                    HasKeyboardFocus: false,
                    Actions: [ToolNames.ComputerUseWinClick]),
            },
            Observation: new ComputerUseWinObservationEnvelope(UiaSnapshotDefaults.Depth, 768),
            CapturedAtUtc: DateTimeOffset.UtcNow);

    private static ComputerUseWinStoredState CreateSafeStoredState() =>
        new(
            CreateSession(),
            CreateWindow(),
            CaptureReference: CreateCaptureReference(),
            Elements: new Dictionary<int, ComputerUseWinStoredElement>
            {
                [1] = new(
                    Index: 1,
                    ElementId: "path:0",
                    Name: "Continue",
                    AutomationId: "ContinueButton",
                    ControlType: "button",
                    Bounds: new Bounds(10, 20, 50, 60),
                    HasKeyboardFocus: false,
                    Actions: [ToolNames.ComputerUseWinClick]),
            },
            Observation: new ComputerUseWinObservationEnvelope(UiaSnapshotDefaults.Depth, 768),
            CapturedAtUtc: DateTimeOffset.UtcNow);

    private static ComputerUseWinStoredState CreateStoredStateWithoutCaptureReference() =>
        new(
            CreateSession(),
            CreateWindow(),
            CaptureReference: null,
            Elements: new Dictionary<int, ComputerUseWinStoredElement>(),
            Observation: new ComputerUseWinObservationEnvelope(UiaSnapshotDefaults.Depth, 768),
            CapturedAtUtc: DateTimeOffset.UtcNow);

    private static ComputerUseWinStoredState CreateSettableStoredState() =>
        new(
            CreateSession(),
            CreateWindow(),
            CaptureReference: CreateCaptureReference(),
            Elements: new Dictionary<int, ComputerUseWinStoredElement>
            {
                [1] = new(
                    Index: 1,
                    ElementId: "path:0",
                    Name: "Smoke query input",
                    AutomationId: "SmokeQueryInputTextBox",
                    ControlType: "edit",
                    Bounds: new Bounds(10, 20, 50, 60),
                    HasKeyboardFocus: true,
                    Actions: [ToolNames.ComputerUseWinSetValue]),
            },
            Observation: new ComputerUseWinObservationEnvelope(UiaSnapshotDefaults.Depth, 768),
            CapturedAtUtc: DateTimeOffset.UtcNow);

    private static ComputerUseWinStoredState CreateFocusedEditableStoredState() =>
        new(
            CreateSession(),
            CreateWindow(),
            CaptureReference: CreateCaptureReference(),
            Elements: new Dictionary<int, ComputerUseWinStoredElement>
            {
                [1] = new(
                    Index: 1,
                    ElementId: "path:0",
                    Name: "Smoke query input",
                    AutomationId: "SmokeQueryInputTextBox",
                    ControlType: "edit",
                    Bounds: new Bounds(10, 20, 50, 60),
                    HasKeyboardFocus: true,
                    Actions: [ToolNames.ComputerUseWinClick, ToolNames.ComputerUseWinSetValue, ToolNames.ComputerUseWinTypeText],
                    Patterns: ["value"]),
            },
            Observation: new ComputerUseWinObservationEnvelope(UiaSnapshotDefaults.Depth, 768),
            CapturedAtUtc: DateTimeOffset.UtcNow);

    private static ComputerUseWinStoredState CreateEditableStoredStateWithoutFocus() =>
        new(
            CreateSession(),
            CreateWindow(),
            CaptureReference: CreateCaptureReference(),
            Elements: new Dictionary<int, ComputerUseWinStoredElement>
            {
                [1] = new(
                    Index: 1,
                    ElementId: "path:0",
                    Name: "Smoke query input",
                    AutomationId: "SmokeQueryInputTextBox",
                    ControlType: "edit",
                    Bounds: new Bounds(10, 20, 50, 60),
                    HasKeyboardFocus: false,
                    Actions: [ToolNames.ComputerUseWinClick, ToolNames.ComputerUseWinSetValue],
                    Patterns: ["value"]),
            },
            Observation: new ComputerUseWinObservationEnvelope(UiaSnapshotDefaults.Depth, 768),
            CapturedAtUtc: DateTimeOffset.UtcNow);

    private static ComputerUseWinStoredState CreateFocusedEditableStoredStateWithoutTypeTextAction() =>
        new(
            CreateSession(),
            CreateWindow(),
            CaptureReference: CreateCaptureReference(),
            Elements: new Dictionary<int, ComputerUseWinStoredElement>
            {
                [1] = new(
                    Index: 1,
                    ElementId: "path:0",
                    Name: "Smoke query input",
                    AutomationId: "SmokeQueryInputTextBox",
                    ControlType: "edit",
                    Bounds: new Bounds(10, 20, 50, 60),
                    HasKeyboardFocus: true,
                    Actions: [ToolNames.ComputerUseWinClick, ToolNames.ComputerUseWinSetValue],
                    Patterns: ["value"]),
            },
            Observation: new ComputerUseWinObservationEnvelope(UiaSnapshotDefaults.Depth, 768),
            CapturedAtUtc: DateTimeOffset.UtcNow);

    private static ComputerUseWinStoredState CreateFocusedWeakStoredState(
        string controlType = "document",
        string name = "Custom canvas text target",
        string automationId = "CustomCanvasTextTarget") =>
        new(
            CreateSession(),
            CreateWindow(),
            CaptureReference: CreateCaptureReference(),
            Elements: new Dictionary<int, ComputerUseWinStoredElement>
            {
                [1] = new(
                    Index: 1,
                    ElementId: "path:weak-focused",
                    Name: name,
                    AutomationId: automationId,
                    ControlType: controlType,
                    Bounds: new Bounds(10, 20, 180, 60),
                    HasKeyboardFocus: true,
                    Actions: [ToolNames.ComputerUseWinClick]),
            },
            Observation: new ComputerUseWinObservationEnvelope(UiaSnapshotDefaults.Depth, 768),
            CapturedAtUtc: DateTimeOffset.UtcNow);

    private static ComputerUseWinStoredState CreateClassCCoordinateStoredState(
        InputCaptureReference? captureReference = null,
        bool useDefaultCaptureReference = true) =>
        new(
            CreateSession(),
            CreateWindow(),
            CaptureReference: useDefaultCaptureReference ? captureReference ?? CreateCaptureReference() : captureReference,
            Elements: new Dictionary<int, ComputerUseWinStoredElement>
            {
                [1] = new(
                    Index: 1,
                    ElementId: "path:top-level-window",
                    Name: "Telegram",
                    AutomationId: null,
                    ControlType: "window",
                    Bounds: new Bounds(0, 0, 260, 180),
                    HasKeyboardFocus: true,
                    Actions: [ToolNames.ComputerUseWinClick, ToolNames.ComputerUseWinDrag, ToolNames.ComputerUseWinSetValue]),
                [2] = new(
                    Index: 2,
                    ElementId: "path:generic-content",
                    Name: null,
                    AutomationId: null,
                    ControlType: "group",
                    Bounds: new Bounds(0, 40, 260, 180),
                    HasKeyboardFocus: false,
                    Actions: [ToolNames.ComputerUseWinClick, ToolNames.ComputerUseWinDrag, ToolNames.ComputerUseWinSetValue]),
            },
            Observation: new ComputerUseWinObservationEnvelope(UiaSnapshotDefaults.Depth, 768),
            CapturedAtUtc: DateTimeOffset.UtcNow);

    private static ComputerUseWinStoredState CreateScrollableStoredState() =>
        new(
            CreateSession(),
            CreateWindow(),
            CaptureReference: CreateCaptureReference(),
            Elements: new Dictionary<int, ComputerUseWinStoredElement>
            {
                [1] = new(
                    Index: 1,
                    ElementId: "path:0",
                    Name: "Smoke scroll list",
                    AutomationId: "SmokeScrollListBox",
                    ControlType: "list",
                    Bounds: new Bounds(10, 20, 220, 180),
                    HasKeyboardFocus: false,
                    Actions: [ToolNames.ComputerUseWinScroll],
                    Patterns: ["scroll"]),
            },
            Observation: new ComputerUseWinObservationEnvelope(UiaSnapshotDefaults.Depth, 768),
            CapturedAtUtc: DateTimeOffset.UtcNow);

    private static ComputerUseWinStoredState CreateSecondaryStoredState(string name = "Remember semantic selection: on") =>
        new(
            CreateSession(),
            CreateWindow(),
            CaptureReference: CreateCaptureReference(),
            Elements: new Dictionary<int, ComputerUseWinStoredElement>
            {
                [1] = new(
                    Index: 1,
                    ElementId: "path:toggle",
                    Name: name,
                    AutomationId: "RememberSemanticSelectionCheckBox",
                    ControlType: "check_box",
                    Bounds: new Bounds(24, 104, 244, 128),
                    HasKeyboardFocus: false,
                    Actions: [ToolNames.ComputerUseWinPerformSecondaryAction],
                    Patterns: ["toggle"]),
            },
            Observation: new ComputerUseWinObservationEnvelope(UiaSnapshotDefaults.Depth, 768),
            CapturedAtUtc: DateTimeOffset.UtcNow);

    private static ComputerUseWinStoredState CreateDragStoredState() =>
        new(
            CreateSession(),
            CreateWindow(),
            CaptureReference: CreateCaptureReference(),
            Elements: new Dictionary<int, ComputerUseWinStoredElement>
            {
                [1] = new(
                    Index: 1,
                    ElementId: "path:drag-source",
                    Name: "Drag source token",
                    AutomationId: "DragSourceToken",
                    ControlType: "button",
                    Bounds: new Bounds(24, 24, 76, 76),
                    HasKeyboardFocus: false,
                    Actions: [ToolNames.ComputerUseWinClick, ToolNames.ComputerUseWinDrag]),
                [2] = new(
                    Index: 2,
                    ElementId: "path:drag-target",
                    Name: "Drag destination target",
                    AutomationId: "DragDestinationTarget",
                    ControlType: "panel",
                    Bounds: new Bounds(220, 40, 320, 140),
                    HasKeyboardFocus: false,
                    Actions: [ToolNames.ComputerUseWinClick, ToolNames.ComputerUseWinDrag]),
            },
            Observation: new ComputerUseWinObservationEnvelope(UiaSnapshotDefaults.Depth, 768),
            CapturedAtUtc: DateTimeOffset.UtcNow);

    private static UiaElementSnapshot CreateClickSnapshotRoot() =>
        new()
        {
            ElementId = "root",
            ControlType = "window",
            Children =
            [
                new UiaElementSnapshot
                {
                    ElementId = "path:0",
                    Name = "Continue",
                    AutomationId = "ContinueButton",
                    ControlType = "button",
                    BoundingRectangle = new Bounds(10, 20, 50, 60),
                    IsEnabled = true,
                    IsOffscreen = false,
                },
            ],
        };

    private static UiaElementSnapshot CreateDragSnapshotRoot() =>
        new()
        {
            ElementId = "root",
            ControlType = "window",
            Children =
            [
                new UiaElementSnapshot
                {
                    ElementId = "path:drag-source",
                    Name = "Drag source token",
                    AutomationId = "DragSourceToken",
                    ControlType = "button",
                    BoundingRectangle = new Bounds(24, 24, 76, 76),
                    IsEnabled = true,
                    IsOffscreen = false,
                },
                new UiaElementSnapshot
                {
                    ElementId = "path:drag-target",
                    Name = "Drag destination target",
                    AutomationId = "DragDestinationTarget",
                    ControlType = "panel",
                    BoundingRectangle = new Bounds(220, 40, 320, 140),
                    IsEnabled = true,
                    IsOffscreen = false,
                },
            ],
        };

    private static UiaElementSnapshot CreateFocusedWeakSnapshotRoot(
        bool hasKeyboardFocus,
        string controlType = "document",
        string name = "Custom canvas text target",
        string automationId = "CustomCanvasTextTarget") =>
        new()
        {
            ElementId = "root",
            ControlType = "window",
            Children =
            [
                new UiaElementSnapshot
                {
                    ElementId = "path:weak-focused",
                    ControlType = controlType,
                    Name = name,
                    AutomationId = automationId,
                    BoundingRectangle = new Bounds(10, 20, 180, 60),
                    IsEnabled = true,
                    IsOffscreen = false,
                    HasKeyboardFocus = hasKeyboardFocus,
                },
            ],
        };

    private static UiaElementSnapshot CreateDestinationOnlyDragSnapshotRoot() =>
        new()
        {
            ElementId = "root",
            ControlType = "window",
            Children =
            [
                new UiaElementSnapshot
                {
                    ElementId = "path:drag-target",
                    Name = "Drag destination target",
                    AutomationId = "DragDestinationTarget",
                    ControlType = "panel",
                    BoundingRectangle = new Bounds(220, 40, 320, 140),
                    IsEnabled = true,
                    IsOffscreen = false,
                },
            ],
        };

    private static UiaElementSnapshot CreateSourceOnlyDragSnapshotRoot() =>
        new()
        {
            ElementId = "root",
            ControlType = "window",
            Children =
            [
                new UiaElementSnapshot
                {
                    ElementId = "path:drag-source",
                    Name = "Drag source token",
                    AutomationId = "DragSourceToken",
                    ControlType = "button",
                    BoundingRectangle = new Bounds(24, 24, 76, 76),
                    IsEnabled = true,
                    IsOffscreen = false,
                },
            ],
        };

    private static InputPoint CreatePointWithAdditionalProperties(int x, int y, IReadOnlyList<string> additionalPropertyNames)
    {
        Dictionary<string, JsonElement> additionalProperties = new(StringComparer.Ordinal);
        foreach (string propertyName in additionalPropertyNames)
        {
            using JsonDocument document = JsonDocument.Parse("1");
            additionalProperties[propertyName] = document.RootElement.Clone();
        }

        return new InputPoint(x, y)
        {
            AdditionalProperties = additionalProperties,
        };
    }

    private static ComputerUseWinStoredState CreateNonActionableStoredState() =>
        new(
            CreateSession(),
            CreateWindow(),
            CaptureReference: CreateCaptureReference(),
            Elements: new Dictionary<int, ComputerUseWinStoredElement>
            {
                [1] = new(
                    Index: 1,
                    ElementId: "path:0",
                    Name: "Status only",
                    AutomationId: "StatusLabel",
                    ControlType: "button",
                    Bounds: new Bounds(10, 20, 50, 60),
                    HasKeyboardFocus: false,
                    Actions: []),
            },
            Observation: new ComputerUseWinObservationEnvelope(UiaSnapshotDefaults.Depth, 768),
            CapturedAtUtc: DateTimeOffset.UtcNow);

    private static ComputerUseWinStoredState CreateUnnamedStoredState() =>
        new(
            CreateSession(),
            CreateWindow(),
            CaptureReference: CreateCaptureReference(),
            Elements: new Dictionary<int, ComputerUseWinStoredElement>
            {
                [1] = new(
                    Index: 1,
                    ElementId: "path:0",
                    Name: null,
                    AutomationId: null,
                    ControlType: "button",
                    Bounds: new Bounds(10, 20, 50, 60),
                    HasKeyboardFocus: false,
                    Actions: [ToolNames.ComputerUseWinClick]),
            },
            Observation: new ComputerUseWinObservationEnvelope(UiaSnapshotDefaults.Depth, 768),
            CapturedAtUtc: DateTimeOffset.UtcNow);

    private static ComputerUseWinStoredState CreateLabelOnlyStoredState() =>
        new(
            CreateSession(),
            CreateWindow(),
            CaptureReference: CreateCaptureReference(),
            Elements: new Dictionary<int, ComputerUseWinStoredElement>
            {
                [1] = new(
                    Index: 1,
                    ElementId: "path:0",
                    Name: "Delete item",
                    AutomationId: null,
                    ControlType: "button",
                    Bounds: new Bounds(10, 20, 50, 60),
                    HasKeyboardFocus: false,
                    Actions: [ToolNames.ComputerUseWinClick]),
            },
            Observation: new ComputerUseWinObservationEnvelope(UiaSnapshotDefaults.Depth, 768),
            CapturedAtUtc: DateTimeOffset.UtcNow);

    private static WindowDescriptor CreateWindow(
        long hwnd = 101,
        string title = "Test window",
        string processName = "explorer",
        int processId = 1001,
        int threadId = 2002,
        string className = "TestWindow",
        Bounds? bounds = null,
        string windowState = WindowStateValues.Normal,
        string? monitorId = "display-source:0000000100000000:1",
        string? monitorFriendlyName = "Primary monitor",
        bool isForeground = true,
        bool isVisible = true) =>
        new(
            Hwnd: hwnd,
            Title: title,
            ProcessName: processName,
            ProcessId: processId,
            ThreadId: threadId,
            ClassName: className,
            Bounds: bounds ?? new Bounds(0, 0, 640, 480),
            IsForeground: isForeground,
            IsVisible: isVisible,
            WindowState: windowState,
            MonitorId: monitorId,
            MonitorFriendlyName: monitorFriendlyName);

    private static ObservedWindowDescriptor CreateObservedWindow(WindowDescriptor window) =>
        new(
            Hwnd: window.Hwnd,
            Title: window.Title,
            ProcessName: window.ProcessName,
            ProcessId: window.ProcessId,
            ThreadId: window.ThreadId,
            ClassName: window.ClassName,
            Bounds: window.Bounds,
            IsForeground: window.IsForeground,
            IsVisible: window.IsVisible,
            EffectiveDpi: window.EffectiveDpi,
            DpiScale: window.DpiScale,
            WindowState: window.WindowState,
            MonitorId: window.MonitorId,
            MonitorFriendlyName: window.MonitorFriendlyName);

    private static InputCaptureReference CreateCaptureReference() =>
        new(
            bounds: new InputBounds(0, 0, 640, 480),
            pixelWidth: 640,
            pixelHeight: 480,
            effectiveDpi: 96,
            capturedAtUtc: DateTimeOffset.UtcNow,
            frameBounds: new InputBounds(0, 0, 640, 480),
            targetIdentity: new InputTargetIdentity(101, 1001, 2002, "TestWindow"));

    private sealed class FakeListAppsWindowManager(IReadOnlyList<WindowDescriptor> windows) : IWindowManager
    {
        public IReadOnlyList<WindowDescriptor> ListWindows(bool includeInvisible = false) =>
            includeInvisible ? windows : windows.Where(static window => window.IsVisible).ToArray();

        public WindowDescriptor? FindWindow(WindowSelector selector) =>
            throw new NotSupportedException("FindWindow не должен вызываться в list_apps characterization test.");

        public bool TryFocus(long hwnd) =>
            throw new NotSupportedException("TryFocus не должен вызываться в list_apps characterization test.");
    }

    private sealed class SequencedListAppsWindowManager(params IReadOnlyList<WindowDescriptor>[] snapshots) : IWindowManager
    {
        private int nextSnapshot;

        public int ListCalls { get; private set; }

        public IReadOnlyList<WindowDescriptor> ListWindows(bool includeInvisible = false)
        {
            ListCalls++;
            int snapshotIndex = Math.Min(nextSnapshot, snapshots.Length - 1);
            nextSnapshot++;
            IReadOnlyList<WindowDescriptor> windows = snapshots[snapshotIndex];
            return includeInvisible ? windows : windows.Where(static window => window.IsVisible).ToArray();
        }

        public WindowDescriptor? FindWindow(WindowSelector selector) =>
            throw new NotSupportedException("FindWindow не должен вызываться в sequenced list_apps characterization test.");

        public bool TryFocus(long hwnd) =>
            throw new NotSupportedException("TryFocus не должен вызываться в sequenced list_apps characterization test.");
    }

    private sealed class NoopCaptureService : ICaptureService
    {
        public Task<CaptureResult> CaptureAsync(CaptureTarget target, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Capture не должен вызываться в list_apps characterization test.");
    }

    private sealed class SuccessfulComputerUseWinCaptureService : ICaptureService
    {
        public Task<CaptureResult> CaptureAsync(CaptureTarget target, CancellationToken cancellationToken)
        {
            WindowDescriptor window = target.Window
                ?? throw new InvalidOperationException("Тест computer-use-win capture ожидает window target.");
            return Task.FromResult(
                new CaptureResult(
                    new CaptureMetadata(
                        Scope: "window",
                        TargetKind: "window",
                        Hwnd: window.Hwnd,
                        Title: window.Title,
                        ProcessName: window.ProcessName,
                        Bounds: window.Bounds,
                        CoordinateSpace: "physical_pixels",
                        PixelWidth: window.Bounds.Width,
                        PixelHeight: window.Bounds.Height,
                        CapturedAtUtc: DateTimeOffset.UtcNow,
                        ArtifactPath: Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png"),
                        MimeType: "image/png",
                        ByteSize: 3,
                        SessionRunId: "tests",
                        EffectiveDpi: window.EffectiveDpi,
                        DpiScale: window.DpiScale,
                        MonitorId: window.MonitorId,
                        MonitorFriendlyName: window.MonitorFriendlyName,
                        CaptureReference: null),
                    [1, 2, 3]));
        }
    }

    private sealed class ThrowingComputerUseWinCaptureService(CaptureOperationException exception) : ICaptureService
    {
        public Task<CaptureResult> CaptureAsync(CaptureTarget target, CancellationToken cancellationToken) =>
            Task.FromException<CaptureResult>(exception);
    }

    private sealed class EmptyInstructionProvider : IComputerUseWinInstructionProvider
    {
        public IReadOnlyList<string> GetInstructions(string? processName) => [];
    }

    private sealed class ThrowingAttachSessionManager(string runId) : ISessionManager
    {
        private readonly SessionSnapshot snapshot = SessionSnapshot.CreateInitial(runId, DateTimeOffset.UtcNow);

        public SessionSnapshot GetSnapshot() => snapshot;

        public AttachedWindow? GetAttachedWindow() => null;

        public SessionMutation Attach(WindowDescriptor window, string matchStrategy) =>
            throw new InvalidOperationException("Тестовый session attach должен остаться advisory для observeAfter.");
    }

    private sealed class TempDirectoryScope : IDisposable
    {
        public TempDirectoryScope()
        {
            Root = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
