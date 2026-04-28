using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Tooling;

namespace WinBridge.Server.IntegrationTests;

public sealed class McpProtocolSmokeTests
{
    private static readonly string[] AttachSuccessStates = { "done", "already_attached" };
    private static readonly string[] ExpectedHealthDomains =
    [
        ReadinessDomainValues.DesktopSession,
        ReadinessDomainValues.SessionAlignment,
        ReadinessDomainValues.Integrity,
        ReadinessDomainValues.UiAccess,
    ];

    private static readonly string[] ExpectedHealthCapabilities =
    [
        CapabilitySummaryValues.Capture,
        CapabilitySummaryValues.Uia,
        CapabilitySummaryValues.Wait,
        CapabilitySummaryValues.Input,
        CapabilitySummaryValues.Clipboard,
        CapabilitySummaryValues.Launch,
    ];

    private static readonly HashSet<string> AllowedGuardStatuses =
    [
        GuardStatusValues.Ready,
        GuardStatusValues.Degraded,
        GuardStatusValues.Blocked,
        GuardStatusValues.Unknown,
    ];
    private static readonly HashSet<string> AllowedDisplayIdentityModes =
    [
        DisplayIdentityModeValues.DisplayConfigStrong,
        DisplayIdentityModeValues.GdiFallback,
    ];

    private static readonly HashSet<string> AllowedDisplayIdentityFailureStages =
    [
        DisplayIdentityFailureStageValues.CoverageGap,
        DisplayIdentityFailureStageValues.GetMonitorInfo,
        DisplayIdentityFailureStageValues.GetBufferSizes,
        DisplayIdentityFailureStageValues.QueryDisplayConfig,
        DisplayIdentityFailureStageValues.GetSourceName,
        DisplayIdentityFailureStageValues.GetTargetName,
    ];
    private const int ProcessPerMonitorDpiAware = 2;
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ProcessExitTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan HelperWindowTimeout = TimeSpan.FromSeconds(10);
    private static readonly Lazy<RuntimeBundlePaths> RuntimeBundle = new(ResolveRuntimeBundlePaths, LazyThreadSafetyMode.ExecutionAndPublication);
    private const int SwMinimize = 6;

    [Fact]
    public async Task InitializeNegotiatesMcp20251125ProtocolVersion()
    {
        using Process process = StartServer();

        await using StreamWriter writer = process.StandardInput;
        using StreamReader reader = process.StandardOutput;
        McpRequestSession session = new(reader, writer);

        try
        {
            using JsonDocument initializeResponse = await session.SendRequestAsync(
                "initialize",
                new
                {
                    protocolVersion = "2025-11-25",
                    capabilities = new { },
                    clientInfo = new
                    {
                        name = "Okno.IntegrationTests",
                        version = "0.1.0",
                    },
                },
                "initialize");

            JsonElement initializeResult = initializeResponse.RootElement.GetProperty("result");
            Assert.Equal("2025-11-25", initializeResult.GetProperty("protocolVersion").GetString());
            Assert.Equal("Okno.Server", initializeResult.GetProperty("serverInfo").GetProperty("name").GetString());
        }
        finally
        {
            process.StandardInput.Close();
            await WaitForExitAsync(process);
        }
    }

    [Fact]
    public async Task InitializePublishesMinimalServerInfoWithoutDescription()
    {
        using Process process = StartServer();

        await using StreamWriter writer = process.StandardInput;
        using StreamReader reader = process.StandardOutput;
        McpRequestSession session = new(reader, writer);

        try
        {
            using JsonDocument initializeResponse = await session.SendRequestAsync(
                "initialize",
                new
                {
                    protocolVersion = "2025-11-25",
                    capabilities = new { },
                    clientInfo = new
                    {
                        name = "Okno.IntegrationTests",
                        version = "0.1.0",
                    },
                },
                "initialize");

            JsonElement serverInfo = initializeResponse.RootElement.GetProperty("result").GetProperty("serverInfo");
            Assert.Equal("Okno.Server", serverInfo.GetProperty("name").GetString());
            Assert.False(string.IsNullOrWhiteSpace(serverInfo.GetProperty("version").GetString()));
            Assert.False(serverInfo.TryGetProperty("description", out _));
        }
        finally
        {
            process.StandardInput.Close();
            await WaitForExitAsync(process);
        }
    }

    [Fact]
    public async Task ToolsListPublishesWindowsWaitWithFinalSchemaAndAnnotations()
    {
        using Process process = StartServer();

        await using StreamWriter writer = process.StandardInput;
        using StreamReader reader = process.StandardOutput;
        McpRequestSession session = new(reader, writer);

        try
        {
            using JsonDocument initializeResponse = await session.SendRequestAsync(
                "initialize",
                new
                {
                    protocolVersion = "2025-11-25",
                    capabilities = new { },
                    clientInfo = new
                    {
                        name = "Okno.IntegrationTests",
                        version = "0.1.0",
                    },
                },
                "initialize");

            await session.SendNotificationAsync("notifications/initialized");

            using JsonDocument toolsResponse = await session.SendRequestAsync(
                "tools/list",
                new { },
                "tools/list");
            JsonElement waitDescriptor = toolsResponse.RootElement
                .GetProperty("result")
                .GetProperty("tools")
                .EnumerateArray()
                .Single(tool => tool.GetProperty("name").GetString() == ToolNames.WindowsWait);

            Assert.False(string.IsNullOrWhiteSpace(waitDescriptor.GetProperty("description").GetString()));
            JsonElement annotations = waitDescriptor.GetProperty("annotations");
            Assert.False(annotations.GetProperty("readOnlyHint").GetBoolean());
            Assert.False(annotations.GetProperty("destructiveHint").GetBoolean());
            Assert.False(annotations.GetProperty("idempotentHint").GetBoolean());
            Assert.True(annotations.GetProperty("openWorldHint").GetBoolean());

            JsonElement properties = waitDescriptor.GetProperty("inputSchema").GetProperty("properties");
            Assert.False(properties.TryGetProperty("until", out _));
            Assert.False(string.IsNullOrWhiteSpace(properties.GetProperty("condition").GetProperty("description").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(properties.GetProperty("selector").GetProperty("description").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(properties.GetProperty("expectedText").GetProperty("description").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(properties.GetProperty("hwnd").GetProperty("description").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(properties.GetProperty("timeoutMs").GetProperty("description").GetString()));
            JsonElement selectorType = properties.GetProperty("selector").GetProperty("type");
            Assert.Contains("object", selectorType.EnumerateArray().Select(item => item.GetString()));
            JsonElement selectorProperties = properties.GetProperty("selector").GetProperty("properties");
            Assert.True(selectorProperties.TryGetProperty("name", out _));
            Assert.True(selectorProperties.TryGetProperty("automationId", out _));
            Assert.True(selectorProperties.TryGetProperty("controlType", out _));
        }
        finally
        {
            process.StandardInput.Close();
            await WaitForExitAsync(process);
        }
    }

    [Fact]
    public async Task ToolsListPublishesComputerUseWinProfileWithOnlyCuratedOperatorTools()
    {
        using Process process = StartServer(ToolSurfaceProfileValues.ComputerUseWin);

        await using StreamWriter writer = process.StandardInput;
        using StreamReader reader = process.StandardOutput;
        McpRequestSession session = new(reader, writer);

        try
        {
            using JsonDocument initializeResponse = await session.SendRequestAsync(
                "initialize",
                new
                {
                    protocolVersion = "2025-11-25",
                    capabilities = new { },
                    clientInfo = new
                    {
                        name = "Okno.IntegrationTests",
                        version = "0.1.0",
                    },
                },
                "initialize");

            await session.SendNotificationAsync("notifications/initialized");

            using JsonDocument toolsResponse = await session.SendRequestAsync(
                "tools/list",
                new { },
                "tools/list");

            string[] toolNames = toolsResponse.RootElement
                .GetProperty("result")
                .GetProperty("tools")
                .EnumerateArray()
                .Select(tool => tool.GetProperty("name").GetString()!)
                .OrderBy(static item => item, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(
                [
                    ToolNames.ComputerUseWinClick,
                    ToolNames.ComputerUseWinDrag,
                    ToolNames.ComputerUseWinGetAppState,
                    ToolNames.ComputerUseWinListApps,
                    ToolNames.ComputerUseWinPerformSecondaryAction,
                    ToolNames.ComputerUseWinPressKey,
                    ToolNames.ComputerUseWinScroll,
                    ToolNames.ComputerUseWinSetValue,
                    ToolNames.ComputerUseWinTypeText,
                ],
                toolNames);

            JsonElement clickDescriptor = toolsResponse.RootElement
                .GetProperty("result")
                .GetProperty("tools")
                .EnumerateArray()
                .Single(tool => tool.GetProperty("name").GetString() == ToolNames.ComputerUseWinClick);
            JsonElement pressKeyDescriptor = toolsResponse.RootElement
                .GetProperty("result")
                .GetProperty("tools")
                .EnumerateArray()
                .Single(tool => tool.GetProperty("name").GetString() == ToolNames.ComputerUseWinPressKey);
            JsonElement dragDescriptor = toolsResponse.RootElement
                .GetProperty("result")
                .GetProperty("tools")
                .EnumerateArray()
                .Single(tool => tool.GetProperty("name").GetString() == ToolNames.ComputerUseWinDrag);
            JsonElement secondaryActionDescriptor = toolsResponse.RootElement
                .GetProperty("result")
                .GetProperty("tools")
                .EnumerateArray()
                .Single(tool => tool.GetProperty("name").GetString() == ToolNames.ComputerUseWinPerformSecondaryAction);
            JsonElement scrollDescriptor = toolsResponse.RootElement
                .GetProperty("result")
                .GetProperty("tools")
                .EnumerateArray()
                .Single(tool => tool.GetProperty("name").GetString() == ToolNames.ComputerUseWinScroll);
            JsonElement setValueDescriptor = toolsResponse.RootElement
                .GetProperty("result")
                .GetProperty("tools")
                .EnumerateArray()
                .Single(tool => tool.GetProperty("name").GetString() == ToolNames.ComputerUseWinSetValue);
            JsonElement typeTextDescriptor = toolsResponse.RootElement
                .GetProperty("result")
                .GetProperty("tools")
                .EnumerateArray()
                .Single(tool => tool.GetProperty("name").GetString() == ToolNames.ComputerUseWinTypeText);
            JsonElement listAppsDescriptor = toolsResponse.RootElement
                .GetProperty("result")
                .GetProperty("tools")
                .EnumerateArray()
                .Single(tool => tool.GetProperty("name").GetString() == ToolNames.ComputerUseWinListApps);
            JsonElement getAppStateDescriptor = toolsResponse.RootElement
                .GetProperty("result")
                .GetProperty("tools")
                .EnumerateArray()
                .Single(tool => tool.GetProperty("name").GetString() == ToolNames.ComputerUseWinGetAppState);
            JsonElement clickProperties = clickDescriptor.GetProperty("inputSchema").GetProperty("properties");
            JsonElement pressKeyProperties = pressKeyDescriptor.GetProperty("inputSchema").GetProperty("properties");
            JsonElement dragSchema = dragDescriptor.GetProperty("inputSchema");
            JsonElement dragProperties = dragSchema.GetProperty("properties");
            JsonElement secondaryActionSchema = secondaryActionDescriptor.GetProperty("inputSchema");
            JsonElement secondaryActionProperties = secondaryActionSchema.GetProperty("properties");
            JsonElement scrollSchema = scrollDescriptor.GetProperty("inputSchema");
            JsonElement scrollProperties = scrollSchema.GetProperty("properties");
            JsonElement setValueSchema = setValueDescriptor.GetProperty("inputSchema");
            JsonElement setValueProperties = setValueSchema.GetProperty("properties");
            JsonElement typeTextSchema = typeTextDescriptor.GetProperty("inputSchema");
            JsonElement typeTextProperties = typeTextSchema.GetProperty("properties");
            JsonElement getAppStateSchema = getAppStateDescriptor.GetProperty("inputSchema");
            JsonElement getAppStateProperties = getAppStateSchema.GetProperty("properties");
            AssertSchemaRequiredContains(clickDescriptor.GetProperty("inputSchema"), "stateToken");
            AssertSchemaRequiredContains(dragSchema, "stateToken");
            AssertSchemaRequiredContains(pressKeyDescriptor.GetProperty("inputSchema"), "stateToken");
            AssertSchemaRequiredContains(pressKeyDescriptor.GetProperty("inputSchema"), "key");
            AssertSchemaRequiredContains(secondaryActionSchema, "stateToken");
            AssertSchemaRequiredContains(secondaryActionSchema, "elementIndex");
            AssertSchemaRequiredContains(scrollSchema, "stateToken");
            AssertSchemaRequiredContains(scrollSchema, "direction");
            AssertSchemaRequiredContains(setValueSchema, "stateToken");
            AssertSchemaRequiredContains(setValueSchema, "elementIndex");
            AssertSchemaRequiredContains(setValueSchema, "valueKind");
            AssertSchemaRequiredContains(typeTextSchema, "stateToken");
            AssertSchemaRequiredContains(typeTextSchema, "text");

            JsonElement getAppStateAnnotations = getAppStateDescriptor.GetProperty("annotations");
            JsonElement listAppsAnnotations = listAppsDescriptor.GetProperty("annotations");
            Assert.False(listAppsAnnotations.GetProperty("readOnlyHint").GetBoolean());
            Assert.True(listAppsAnnotations.GetProperty("destructiveHint").GetBoolean());
            Assert.False(listAppsAnnotations.GetProperty("idempotentHint").GetBoolean());
            Assert.True(listAppsAnnotations.GetProperty("openWorldHint").GetBoolean());
            Assert.False(getAppStateAnnotations.GetProperty("readOnlyHint").GetBoolean());
            Assert.False(getAppStateAnnotations.GetProperty("destructiveHint").GetBoolean());
            Assert.False(getAppStateAnnotations.GetProperty("idempotentHint").GetBoolean());
            Assert.True(getAppStateAnnotations.GetProperty("openWorldHint").GetBoolean());
            Assert.True(getAppStateProperties.TryGetProperty("windowId", out JsonElement windowIdProperty));
            Assert.True(getAppStateProperties.TryGetProperty("hwnd", out JsonElement getAppStateHwndProperty));
            Assert.False(getAppStateProperties.TryGetProperty("appId", out _));
            Assert.Contains("string", windowIdProperty.GetProperty("type").EnumerateArray().Select(item => item.GetString()));
            Assert.Contains("null", windowIdProperty.GetProperty("type").EnumerateArray().Select(item => item.GetString()));
            Assert.Equal(@".*\S.*", windowIdProperty.GetProperty("pattern").GetString());
            Assert.Contains("integer", getAppStateHwndProperty.GetProperty("type").EnumerateArray().Select(item => item.GetString()));
            string[] conflictingSelectors = getAppStateSchema.GetProperty("not").GetProperty("required").EnumerateArray()
                .Select(item => item.GetString())
                .Where(static item => item is not null)
                .Cast<string>()
                .ToArray();
            Assert.Equal(["windowId", "hwnd"], conflictingSelectors);

            Assert.Equal(
                [InputCoordinateSpaceValues.Screen, InputCoordinateSpaceValues.CapturePixels],
                clickProperties.GetProperty("coordinateSpace").GetProperty("enum").EnumerateArray().Select(item => item.GetString()).Where(static item => item is not null).Cast<string>().ToArray());
            Assert.Equal(
                [InputButtonValues.Left, InputButtonValues.Right],
                clickProperties.GetProperty("button").GetProperty("enum").EnumerateArray().Select(item => item.GetString()).Where(static item => item is not null).Cast<string>().ToArray());
            Assert.Equal("string", pressKeyProperties.GetProperty("key").GetProperty("type").GetString());
            Assert.Equal(1, pressKeyProperties.GetProperty("repeat").GetProperty("minimum").GetInt32());
            Assert.Equal(InputActionScalarConstraints.MaximumKeypressRepeat, pressKeyProperties.GetProperty("repeat").GetProperty("maximum").GetInt32());
            Assert.Equal(1, dragProperties.GetProperty("fromElementIndex").GetProperty("minimum").GetInt32());
            Assert.Equal(1, dragProperties.GetProperty("toElementIndex").GetProperty("minimum").GetInt32());
            Assert.Equal(
                [InputCoordinateSpaceValues.Screen, InputCoordinateSpaceValues.CapturePixels],
                dragProperties.GetProperty("coordinateSpace").GetProperty("enum").EnumerateArray().Select(item => item.GetString()).Where(static item => item is not null).Cast<string>().ToArray());
            JsonElement[] dragSelectorSets = [.. dragSchema.GetProperty("allOf").EnumerateArray()];
            Assert.Equal(2, dragSelectorSets.Length);
            Assert.Contains(dragSelectorSets[0].GetProperty("oneOf").EnumerateArray(), mode => mode.GetProperty("required").EnumerateArray().Any(item => item.GetString() == "fromElementIndex"));
            Assert.Contains(dragSelectorSets[0].GetProperty("oneOf").EnumerateArray(), mode => mode.GetProperty("required").EnumerateArray().Any(item => item.GetString() == "fromPoint"));
            Assert.Contains(dragSelectorSets[1].GetProperty("oneOf").EnumerateArray(), mode => mode.GetProperty("required").EnumerateArray().Any(item => item.GetString() == "toElementIndex"));
            Assert.Contains(dragSelectorSets[1].GetProperty("oneOf").EnumerateArray(), mode => mode.GetProperty("required").EnumerateArray().Any(item => item.GetString() == "toPoint"));
            Assert.Equal(1, secondaryActionProperties.GetProperty("elementIndex").GetProperty("minimum").GetInt32());
            Assert.False(secondaryActionProperties.TryGetProperty("point", out _));
            Assert.Equal(1, scrollProperties.GetProperty("elementIndex").GetProperty("minimum").GetInt32());
            Assert.Equal(
                ["up", "down", "left", "right"],
                scrollProperties.GetProperty("direction").GetProperty("enum").EnumerateArray().Select(item => item.GetString()).Where(static item => item is not null).Cast<string>().ToArray());
            Assert.Equal(1, scrollProperties.GetProperty("pages").GetProperty("minimum").GetInt32());
            Assert.Equal(1, setValueProperties.GetProperty("elementIndex").GetProperty("minimum").GetInt32());
            Assert.Equal(
                ["text", "number"],
                setValueProperties.GetProperty("valueKind").GetProperty("enum").EnumerateArray().Select(item => item.GetString()).Where(static item => item is not null).Cast<string>().ToArray());
            Assert.Equal("string", setValueProperties.GetProperty("textValue").GetProperty("type").GetString());
            Assert.Equal("number", setValueProperties.GetProperty("numberValue").GetProperty("type").GetString());
            Assert.Equal(1, typeTextProperties.GetProperty("elementIndex").GetProperty("minimum").GetInt32());
            Assert.Equal("string", typeTextProperties.GetProperty("text").GetProperty("type").GetString());
            Assert.False(typeTextProperties.TryGetProperty("key", out _));
            Assert.False(typeTextProperties.TryGetProperty("valueKind", out _));

            JsonElement[] selectorModes = [.. clickDescriptor.GetProperty("inputSchema").GetProperty("oneOf").EnumerateArray()];
            Assert.Equal(2, selectorModes.Length);
            Assert.Contains(selectorModes, mode => mode.GetProperty("required").EnumerateArray().Any(item => item.GetString() == "elementIndex"));
            Assert.Contains(selectorModes, mode => mode.GetProperty("required").EnumerateArray().Any(item => item.GetString() == "point"));

            Assert.False(clickDescriptor.TryGetProperty("icons", out _));
            Assert.False(getAppStateDescriptor.TryGetProperty("icons", out _));
            Assert.Equal("optional", clickDescriptor.GetProperty("execution").GetProperty("taskSupport").GetString());
            Assert.Equal("optional", getAppStateDescriptor.GetProperty("execution").GetProperty("taskSupport").GetString());
        }
        finally
        {
            process.StandardInput.Close();
            await WaitForExitAsync(process);
        }
    }

    [Fact]
    public async Task ComputerUseWinGetAppStateRequiresApprovalBeforeReturningState()
    {
        using Process helper = StartHelperWindow(
            title: $"Okno Smoke Helper Approval {Guid.NewGuid():N}",
            lifetimeMs: 20000);
        long helperHwnd = await WaitForMainWindowAsync(helper);
        await Task.Delay(750);
        using Process process = StartServer(ToolSurfaceProfileValues.ComputerUseWin);

        await using StreamWriter writer = process.StandardInput;
        using StreamReader reader = process.StandardOutput;
        McpRequestSession session = new(reader, writer);

        try
        {
            using JsonDocument initializeResponse = await session.SendRequestAsync(
                "initialize",
                new
                {
                    protocolVersion = "2025-11-25",
                    capabilities = new { },
                    clientInfo = new
                    {
                        name = "Okno.IntegrationTests",
                        version = "0.1.0",
                    },
                },
                "initialize");

            await session.SendNotificationAsync("notifications/initialized");

            using JsonDocument response = await session.CallToolAsync(
                ToolNames.ComputerUseWinGetAppState,
                new
                {
                    hwnd = helperHwnd,
                });

            JsonElement payload = response.RootElement.GetProperty("result").GetProperty("structuredContent");
            Assert.Equal(ComputerUseWinStatusValues.ApprovalRequired, payload.GetProperty("status").GetString());
            Assert.True(payload.GetProperty("approvalRequired").GetBoolean());
            Assert.Equal(ComputerUseWinFailureCodeValues.ApprovalRequired, payload.GetProperty("failureCode").GetString());
        }
        finally
        {
            process.StandardInput.Close();
            await WaitForExitAsync(process);
            await StopHelperWindowAsync(helper);
        }
    }

    [Fact]
    public async Task ComputerUseWinClickUsesStateTokenAndElementIndexAfterApprovedAppState()
    {
        using Process helper = StartHelperWindow(
            title: $"Okno Smoke Helper Click {Guid.NewGuid():N}",
            lifetimeMs: 20000);
        long helperHwnd = await WaitForMainWindowAsync(helper);
        await Task.Delay(750);
        using Process process = StartServer(ToolSurfaceProfileValues.ComputerUseWin);

        await using StreamWriter writer = process.StandardInput;
        using StreamReader reader = process.StandardOutput;
        McpRequestSession session = new(reader, writer);

        try
        {
            using JsonDocument initializeResponse = await session.SendRequestAsync(
                "initialize",
                new
                {
                    protocolVersion = "2025-11-25",
                    capabilities = new { },
                    clientInfo = new
                    {
                        name = "Okno.IntegrationTests",
                        version = "0.1.0",
                    },
                },
                "initialize");

            await session.SendNotificationAsync("notifications/initialized");

            using JsonDocument stateResponse = await session.CallToolAsync(
                ToolNames.ComputerUseWinGetAppState,
                new
                {
                    hwnd = helperHwnd,
                    confirm = true,
                });

            JsonElement statePayload = stateResponse.RootElement.GetProperty("result").GetProperty("structuredContent");
            Assert.Equal(ComputerUseWinStatusValues.Ok, statePayload.GetProperty("status").GetString());
            Assert.Contains(
                stateResponse.RootElement.GetProperty("result").GetProperty("content").EnumerateArray(),
                block => block.GetProperty("type").GetString() == "image");

            string stateToken = statePayload.GetProperty("stateToken").GetString()!;
            JsonElement targetElement = statePayload
                .GetProperty("accessibilityTree")
                .EnumerateArray()
                .First(element =>
                    string.Equals(element.GetProperty("name").GetString(), "Smoke query input", StringComparison.Ordinal));
            int elementIndex = targetElement.GetProperty("index").GetInt32();

            using JsonDocument clickResponse = await session.CallToolAsync(
                ToolNames.ComputerUseWinClick,
                new
                {
                    stateToken,
                    elementIndex,
                });

            JsonElement clickPayload = clickResponse.RootElement.GetProperty("result").GetProperty("structuredContent");
            Assert.Equal(ComputerUseWinStatusValues.VerifyNeeded, clickPayload.GetProperty("status").GetString());
            Assert.Equal(helperHwnd, clickPayload.GetProperty("targetHwnd").GetInt64());
            Assert.Equal(elementIndex, clickPayload.GetProperty("elementIndex").GetInt32());
        }
        finally
        {
            process.StandardInput.Close();
            await WaitForExitAsync(process);
            await StopHelperWindowAsync(helper);
        }
    }

    [Fact]
    public async Task ComputerUseWinPressKeyMovesKeyboardFocusThroughApprovedAppState()
    {
        using Process helper = StartHelperWindow(
            title: $"Okno Smoke Helper PressKey {Guid.NewGuid():N}",
            lifetimeMs: 20000);
        long helperHwnd = await WaitForMainWindowAsync(helper);
        await Task.Delay(750);
        using Process process = StartServer(ToolSurfaceProfileValues.ComputerUseWin);

        await using StreamWriter writer = process.StandardInput;
        using StreamReader reader = process.StandardOutput;
        McpRequestSession session = new(reader, writer);

        try
        {
            using JsonDocument initializeResponse = await session.SendRequestAsync(
                "initialize",
                new
                {
                    protocolVersion = "2025-11-25",
                    capabilities = new { },
                    clientInfo = new
                    {
                        name = "Okno.IntegrationTests",
                        version = "0.1.0",
                    },
                },
                "initialize");

            await session.SendNotificationAsync("notifications/initialized");

            using JsonDocument firstStateResponse = await session.CallToolAsync(
                ToolNames.ComputerUseWinGetAppState,
                new
                {
                    hwnd = helperHwnd,
                    confirm = true,
                });

            JsonElement firstStatePayload = firstStateResponse.RootElement.GetProperty("result").GetProperty("structuredContent");
            Assert.Equal(ComputerUseWinStatusValues.Ok, firstStatePayload.GetProperty("status").GetString());
            string stateToken = firstStatePayload.GetProperty("stateToken").GetString()!;
            string focusedNameBefore = firstStatePayload
                .GetProperty("accessibilityTree")
                .EnumerateArray()
                .Single(element => element.GetProperty("hasKeyboardFocus").GetBoolean())
                .GetProperty("name")
                .GetString()!;
            Assert.Equal("Run semantic smoke", focusedNameBefore);

            using JsonDocument pressKeyResponse = await session.CallToolAsync(
                ToolNames.ComputerUseWinPressKey,
                new
                {
                    stateToken,
                    key = "tab",
                });

            JsonElement pressKeyPayload = pressKeyResponse.RootElement.GetProperty("result").GetProperty("structuredContent");
            Assert.Equal(ComputerUseWinStatusValues.VerifyNeeded, pressKeyPayload.GetProperty("status").GetString());
            Assert.Equal(helperHwnd, pressKeyPayload.GetProperty("targetHwnd").GetInt64());

            using JsonDocument secondStateResponse = await session.CallToolAsync(
                ToolNames.ComputerUseWinGetAppState,
                new
                {
                    hwnd = helperHwnd,
                    confirm = true,
                });

            JsonElement secondStatePayload = secondStateResponse.RootElement.GetProperty("result").GetProperty("structuredContent");
            Assert.Equal(ComputerUseWinStatusValues.Ok, secondStatePayload.GetProperty("status").GetString());
            string focusedNameAfter = secondStatePayload
                .GetProperty("accessibilityTree")
                .EnumerateArray()
                .Single(element => element.GetProperty("hasKeyboardFocus").GetBoolean())
                .GetProperty("name")
                .GetString()!;
            Assert.NotEqual(focusedNameBefore, focusedNameAfter);
            Assert.Equal("Transient wait target", focusedNameAfter);
        }
        finally
        {
            process.StandardInput.Close();
            await WaitForExitAsync(process);
            await StopHelperWindowAsync(helper);
        }
    }

    [Fact]
    public async Task ComputerUseWinSetValueUpdatesSemanticMirrorThroughApprovedAppState()
    {
        using Process helper = StartHelperWindow(
            title: $"Okno Smoke Helper SetValue {Guid.NewGuid():N}",
            lifetimeMs: 20000);
        long helperHwnd = await WaitForMainWindowAsync(helper);
        await Task.Delay(750);
        using Process process = StartServer(ToolSurfaceProfileValues.ComputerUseWin);

        await using StreamWriter writer = process.StandardInput;
        using StreamReader reader = process.StandardOutput;
        McpRequestSession session = new(reader, writer);

        try
        {
            using JsonDocument initializeResponse = await session.SendRequestAsync(
                "initialize",
                new
                {
                    protocolVersion = "2025-11-25",
                    capabilities = new { },
                    clientInfo = new
                    {
                        name = "Okno.IntegrationTests",
                        version = "0.1.0",
                    },
                },
                "initialize");

            await session.SendNotificationAsync("notifications/initialized");

            using JsonDocument firstStateResponse = await session.CallToolAsync(
                ToolNames.ComputerUseWinGetAppState,
                new
                {
                    hwnd = helperHwnd,
                    confirm = true,
                });

            JsonElement firstStatePayload = firstStateResponse.RootElement.GetProperty("result").GetProperty("structuredContent");
            Assert.Equal(ComputerUseWinStatusValues.Ok, firstStatePayload.GetProperty("status").GetString());
            string stateToken = firstStatePayload.GetProperty("stateToken").GetString()!;
            JsonElement targetElement = firstStatePayload
                .GetProperty("accessibilityTree")
                .EnumerateArray()
                .First(element =>
                    string.Equals(element.GetProperty("name").GetString(), "Smoke query input", StringComparison.Ordinal));
            int elementIndex = targetElement.GetProperty("index").GetInt32();

            using JsonDocument setValueResponse = await session.CallToolAsync(
                ToolNames.ComputerUseWinSetValue,
                new
                {
                    stateToken,
                    elementIndex,
                    valueKind = "text",
                    textValue = "stage four semantic value",
                });

            JsonElement setValuePayload = setValueResponse.RootElement.GetProperty("result").GetProperty("structuredContent");
            Assert.Equal(ComputerUseWinStatusValues.Done, setValuePayload.GetProperty("status").GetString());
            Assert.Equal(helperHwnd, setValuePayload.GetProperty("targetHwnd").GetInt64());

            using JsonDocument secondStateResponse = await session.CallToolAsync(
                ToolNames.ComputerUseWinGetAppState,
                new
                {
                    hwnd = helperHwnd,
                    confirm = true,
                });

            JsonElement secondStatePayload = secondStateResponse.RootElement.GetProperty("result").GetProperty("structuredContent");
            Assert.Equal(ComputerUseWinStatusValues.Ok, secondStatePayload.GetProperty("status").GetString());
            Assert.Contains(
                secondStatePayload.GetProperty("accessibilityTree").EnumerateArray(),
                element => string.Equals(element.GetProperty("name").GetString(), "Query mirror: stage four semantic value", StringComparison.Ordinal));
        }
        finally
        {
            process.StandardInput.Close();
            await WaitForExitAsync(process);
            await StopHelperWindowAsync(helper);
        }
    }

    [Fact]
    public async Task ComputerUseWinSetValueUpdatesRangeMirrorThroughApprovedAppState()
    {
        using Process helper = StartHelperWindow(
            title: $"Okno Smoke Helper SetValue Range {Guid.NewGuid():N}",
            lifetimeMs: 20000);
        long helperHwnd = await WaitForMainWindowAsync(helper);
        await Task.Delay(750);
        using Process process = StartServer(ToolSurfaceProfileValues.ComputerUseWin);

        await using StreamWriter writer = process.StandardInput;
        using StreamReader reader = process.StandardOutput;
        McpRequestSession session = new(reader, writer);

        try
        {
            using JsonDocument initializeResponse = await session.SendRequestAsync(
                "initialize",
                new
                {
                    protocolVersion = "2025-11-25",
                    capabilities = new { },
                    clientInfo = new
                    {
                        name = "Okno.IntegrationTests",
                        version = "0.1.0",
                    },
                },
                "initialize");

            await session.SendNotificationAsync("notifications/initialized");

            using JsonDocument firstStateResponse = await session.CallToolAsync(
                ToolNames.ComputerUseWinGetAppState,
                new
                {
                    hwnd = helperHwnd,
                    confirm = true,
                });

            JsonElement firstStatePayload = firstStateResponse.RootElement.GetProperty("result").GetProperty("structuredContent");
            Assert.Equal(ComputerUseWinStatusValues.Ok, firstStatePayload.GetProperty("status").GetString());
            string stateToken = firstStatePayload.GetProperty("stateToken").GetString()!;
            JsonElement targetElement = firstStatePayload
                .GetProperty("accessibilityTree")
                .EnumerateArray()
                .First(element =>
                    string.Equals(element.GetProperty("name").GetString(), "Smoke range input", StringComparison.Ordinal)
                    && string.Equals(element.GetProperty("controlType").GetString(), "edit", StringComparison.Ordinal));
            int elementIndex = targetElement.GetProperty("index").GetInt32();

            using JsonDocument setValueResponse = await session.CallToolAsync(
                ToolNames.ComputerUseWinSetValue,
                new
                {
                    stateToken,
                    elementIndex,
                    valueKind = "number",
                    numberValue = 9,
                });

            JsonElement setValuePayload = setValueResponse.RootElement.GetProperty("result").GetProperty("structuredContent");
            Assert.Equal(ComputerUseWinStatusValues.Done, setValuePayload.GetProperty("status").GetString());
            Assert.Equal(helperHwnd, setValuePayload.GetProperty("targetHwnd").GetInt64());

            using JsonDocument secondStateResponse = await session.CallToolAsync(
                ToolNames.ComputerUseWinGetAppState,
                new
                {
                    hwnd = helperHwnd,
                    confirm = true,
                });

            JsonElement secondStatePayload = secondStateResponse.RootElement.GetProperty("result").GetProperty("structuredContent");
            Assert.Equal(ComputerUseWinStatusValues.Ok, secondStatePayload.GetProperty("status").GetString());
            Assert.Contains(
                secondStatePayload.GetProperty("accessibilityTree").EnumerateArray(),
                element => string.Equals(element.GetProperty("name").GetString(), "Range mirror: 9", StringComparison.Ordinal));
        }
        finally
        {
            process.StandardInput.Close();
            await WaitForExitAsync(process);
            await StopHelperWindowAsync(helper);
        }
    }

    [Fact]
    public async Task ComputerUseWinTypeTextUpdatesQueryMirrorAfterExplicitFocusProof()
    {
        using Process helper = StartHelperWindow(
            title: $"Okno Smoke Helper TypeText {Guid.NewGuid():N}",
            lifetimeMs: 20000);
        long helperHwnd = await WaitForMainWindowAsync(helper);
        await Task.Delay(750);
        using Process process = StartServer(ToolSurfaceProfileValues.ComputerUseWin);

        await using StreamWriter writer = process.StandardInput;
        using StreamReader reader = process.StandardOutput;
        McpRequestSession session = new(reader, writer);

        try
        {
            using JsonDocument initializeResponse = await session.SendRequestAsync(
                "initialize",
                new
                {
                    protocolVersion = "2025-11-25",
                    capabilities = new { },
                    clientInfo = new
                    {
                        name = "Okno.IntegrationTests",
                        version = "0.1.0",
                    },
                },
                "initialize");

            await session.SendNotificationAsync("notifications/initialized");

            using JsonDocument firstStateResponse = await session.CallToolAsync(
                ToolNames.ComputerUseWinGetAppState,
                new
                {
                    hwnd = helperHwnd,
                    confirm = true,
                });

            JsonElement firstStatePayload = firstStateResponse.RootElement.GetProperty("result").GetProperty("structuredContent");
            Assert.Equal(ComputerUseWinStatusValues.Ok, firstStatePayload.GetProperty("status").GetString());
            string firstStateToken = firstStatePayload.GetProperty("stateToken").GetString()!;
            JsonElement queryInputElement = firstStatePayload
                .GetProperty("accessibilityTree")
                .EnumerateArray()
                .First(element =>
                    string.Equals(element.GetProperty("name").GetString(), "Smoke query input", StringComparison.Ordinal));
            int queryInputIndex = queryInputElement.GetProperty("index").GetInt32();

            using JsonDocument clickResponse = await session.CallToolAsync(
                ToolNames.ComputerUseWinClick,
                new
                {
                    stateToken = firstStateToken,
                    elementIndex = queryInputIndex,
                    confirm = false,
                });

            JsonElement clickPayload = clickResponse.RootElement.GetProperty("result").GetProperty("structuredContent");
            Assert.Equal(ComputerUseWinStatusValues.VerifyNeeded, clickPayload.GetProperty("status").GetString());

            using JsonDocument focusedStateResponse = await session.CallToolAsync(
                ToolNames.ComputerUseWinGetAppState,
                new
                {
                    hwnd = helperHwnd,
                    confirm = true,
                });

            JsonElement focusedStatePayload = focusedStateResponse.RootElement.GetProperty("result").GetProperty("structuredContent");
            Assert.Equal(ComputerUseWinStatusValues.Ok, focusedStatePayload.GetProperty("status").GetString());
            string focusedStateToken = focusedStatePayload.GetProperty("stateToken").GetString()!;
            JsonElement focusedElement = focusedStatePayload
                .GetProperty("accessibilityTree")
                .EnumerateArray()
                .Single(element => element.GetProperty("hasKeyboardFocus").GetBoolean());
            Assert.Equal("Smoke query input", focusedElement.GetProperty("name").GetString());

            using JsonDocument typeTextResponse = await session.CallToolAsync(
                ToolNames.ComputerUseWinTypeText,
                new
                {
                    stateToken = focusedStateToken,
                    text = "stage five typed text",
                });

            JsonElement typeTextPayload = typeTextResponse.RootElement.GetProperty("result").GetProperty("structuredContent");
            Assert.Equal(ComputerUseWinStatusValues.VerifyNeeded, typeTextPayload.GetProperty("status").GetString());
            Assert.Equal(helperHwnd, typeTextPayload.GetProperty("targetHwnd").GetInt64());

            using JsonDocument finalStateResponse = await session.CallToolAsync(
                ToolNames.ComputerUseWinGetAppState,
                new
                {
                    hwnd = helperHwnd,
                    confirm = true,
                });

            JsonElement finalStatePayload = finalStateResponse.RootElement.GetProperty("result").GetProperty("structuredContent");
            Assert.Equal(ComputerUseWinStatusValues.Ok, finalStatePayload.GetProperty("status").GetString());
            Assert.Contains(
                finalStatePayload.GetProperty("accessibilityTree").EnumerateArray(),
                element => string.Equals(element.GetProperty("name").GetString(), "Query mirror: stage five typed text", StringComparison.Ordinal));
        }
        finally
        {
            process.StandardInput.Close();
            await WaitForExitAsync(process);
            await StopHelperWindowAsync(helper);
        }
    }

    [Fact]
    public async Task ComputerUseWinScrollUpdatesScrollMirrorThroughApprovedAppState()
    {
        using Process helper = StartHelperWindow(
            title: $"Okno Smoke Helper Scroll {Guid.NewGuid():N}",
            lifetimeMs: 20000);
        long helperHwnd = await WaitForMainWindowAsync(helper);
        await Task.Delay(750);
        using Process process = StartServer(ToolSurfaceProfileValues.ComputerUseWin);

        await using StreamWriter writer = process.StandardInput;
        using StreamReader reader = process.StandardOutput;
        McpRequestSession session = new(reader, writer);

        try
        {
            using JsonDocument initializeResponse = await session.SendRequestAsync(
                "initialize",
                new
                {
                    protocolVersion = "2025-11-25",
                    capabilities = new { },
                    clientInfo = new
                    {
                        name = "Okno.IntegrationTests",
                        version = "0.1.0",
                    },
                },
                "initialize");

            await session.SendNotificationAsync("notifications/initialized");

            using JsonDocument firstStateResponse = await session.CallToolAsync(
                ToolNames.ComputerUseWinGetAppState,
                new
                {
                    hwnd = helperHwnd,
                    confirm = true,
                });

            JsonElement firstStatePayload = firstStateResponse.RootElement.GetProperty("result").GetProperty("structuredContent");
            Assert.Equal(ComputerUseWinStatusValues.Ok, firstStatePayload.GetProperty("status").GetString());
            string stateToken = firstStatePayload.GetProperty("stateToken").GetString()!;
            string initialMirror = firstStatePayload
                .GetProperty("accessibilityTree")
                .EnumerateArray()
                .First(element => element.GetProperty("name").GetString()!.StartsWith("Scroll mirror:", StringComparison.Ordinal))
                .GetProperty("name")
                .GetString()!;
            JsonElement scrollTarget = firstStatePayload
                .GetProperty("accessibilityTree")
                .EnumerateArray()
                .First(element =>
                    string.Equals(element.GetProperty("name").GetString(), "Smoke scroll list", StringComparison.Ordinal)
                    && string.Equals(element.GetProperty("controlType").GetString(), "list", StringComparison.Ordinal));
            int elementIndex = scrollTarget.GetProperty("index").GetInt32();

            using JsonDocument scrollResponse = await session.CallToolAsync(
                ToolNames.ComputerUseWinScroll,
                new
                {
                    stateToken,
                    elementIndex,
                    direction = "down",
                    pages = 1,
                });

            JsonElement scrollPayload = scrollResponse.RootElement.GetProperty("result").GetProperty("structuredContent");
            Assert.Equal(ComputerUseWinStatusValues.Done, scrollPayload.GetProperty("status").GetString());
            Assert.Equal(helperHwnd, scrollPayload.GetProperty("targetHwnd").GetInt64());

            using JsonDocument secondStateResponse = await session.CallToolAsync(
                ToolNames.ComputerUseWinGetAppState,
                new
                {
                    hwnd = helperHwnd,
                    confirm = true,
                });

            JsonElement secondStatePayload = secondStateResponse.RootElement.GetProperty("result").GetProperty("structuredContent");
            Assert.Equal(ComputerUseWinStatusValues.Ok, secondStatePayload.GetProperty("status").GetString());
            string updatedMirror = secondStatePayload
                .GetProperty("accessibilityTree")
                .EnumerateArray()
                .First(element => element.GetProperty("name").GetString()!.StartsWith("Scroll mirror:", StringComparison.Ordinal))
                .GetProperty("name")
                .GetString()!;
            Assert.NotEqual(initialMirror, updatedMirror);
        }
        finally
        {
            process.StandardInput.Close();
            await WaitForExitAsync(process);
            await StopHelperWindowAsync(helper);
        }
    }

    [Fact]
    public async Task ComputerUseWinPerformSecondaryActionTogglesCheckboxStateThroughApprovedAppState()
    {
        using Process helper = StartHelperWindow(
            title: $"Okno Smoke Helper Secondary {Guid.NewGuid():N}",
            lifetimeMs: 20000);
        long helperHwnd = await WaitForMainWindowAsync(helper);
        await Task.Delay(750);
        using Process process = StartServer(ToolSurfaceProfileValues.ComputerUseWin);

        await using StreamWriter writer = process.StandardInput;
        using StreamReader reader = process.StandardOutput;
        McpRequestSession session = new(reader, writer);

        try
        {
            using JsonDocument initializeResponse = await session.SendRequestAsync(
                "initialize",
                new
                {
                    protocolVersion = "2025-11-25",
                    capabilities = new { },
                    clientInfo = new
                    {
                        name = "Okno.IntegrationTests",
                        version = "0.1.0",
                    },
                },
                "initialize");

            await session.SendNotificationAsync("notifications/initialized");

            using JsonDocument firstStateResponse = await session.CallToolAsync(
                ToolNames.ComputerUseWinGetAppState,
                new
                {
                    hwnd = helperHwnd,
                    confirm = true,
                });

            JsonElement firstStatePayload = firstStateResponse.RootElement.GetProperty("result").GetProperty("structuredContent");
            Assert.Equal(ComputerUseWinStatusValues.Ok, firstStatePayload.GetProperty("status").GetString());
            string stateToken = firstStatePayload.GetProperty("stateToken").GetString()!;
            JsonElement toggleTarget = firstStatePayload
                .GetProperty("accessibilityTree")
                .EnumerateArray()
                .First(element =>
                    string.Equals(element.GetProperty("controlType").GetString(), "check_box", StringComparison.Ordinal)
                    && element.GetProperty("name").GetString()!.StartsWith("Remember semantic selection:", StringComparison.Ordinal));
            string initialName = toggleTarget.GetProperty("name").GetString()!;
            int elementIndex = toggleTarget.GetProperty("index").GetInt32();

            using JsonDocument secondaryActionResponse = await session.CallToolAsync(
                ToolNames.ComputerUseWinPerformSecondaryAction,
                new
                {
                    stateToken,
                    elementIndex,
                    confirm = true,
                });

            JsonElement secondaryActionPayload = secondaryActionResponse.RootElement.GetProperty("result").GetProperty("structuredContent");
            Assert.Equal(ComputerUseWinStatusValues.Done, secondaryActionPayload.GetProperty("status").GetString());
            Assert.Equal(helperHwnd, secondaryActionPayload.GetProperty("targetHwnd").GetInt64());

            using JsonDocument secondStateResponse = await session.CallToolAsync(
                ToolNames.ComputerUseWinGetAppState,
                new
                {
                    hwnd = helperHwnd,
                    confirm = true,
                });

            JsonElement secondStatePayload = secondStateResponse.RootElement.GetProperty("result").GetProperty("structuredContent");
            Assert.Equal(ComputerUseWinStatusValues.Ok, secondStatePayload.GetProperty("status").GetString());
            string updatedName = secondStatePayload
                .GetProperty("accessibilityTree")
                .EnumerateArray()
                .First(element =>
                    string.Equals(element.GetProperty("controlType").GetString(), "check_box", StringComparison.Ordinal)
                    && element.GetProperty("name").GetString()!.StartsWith("Remember semantic selection:", StringComparison.Ordinal))
                .GetProperty("name")
                .GetString()!;
            Assert.NotEqual(initialName, updatedName);
        }
        finally
        {
            process.StandardInput.Close();
            await WaitForExitAsync(process);
            await StopHelperWindowAsync(helper);
        }
    }

    [Fact]
    public async Task ComputerUseWinDragUpdatesDragMirrorThroughApprovedAppState()
    {
        using Process helper = StartHelperWindow(
            title: $"Okno Smoke Helper Drag {Guid.NewGuid():N}",
            lifetimeMs: 20000);
        long helperHwnd = await WaitForMainWindowAsync(helper);
        await Task.Delay(750);
        using Process process = StartServer(ToolSurfaceProfileValues.ComputerUseWin);

        await using StreamWriter writer = process.StandardInput;
        using StreamReader reader = process.StandardOutput;
        McpRequestSession session = new(reader, writer);

        try
        {
            using JsonDocument initializeResponse = await session.SendRequestAsync(
                "initialize",
                new
                {
                    protocolVersion = "2025-11-25",
                    capabilities = new { },
                    clientInfo = new
                    {
                        name = "Okno.IntegrationTests",
                        version = "0.1.0",
                    },
                },
                "initialize");

            await session.SendNotificationAsync("notifications/initialized");

            using JsonDocument firstStateResponse = await session.CallToolAsync(
                ToolNames.ComputerUseWinGetAppState,
                new
                {
                    hwnd = helperHwnd,
                    confirm = true,
                });

            JsonElement firstStatePayload = firstStateResponse.RootElement.GetProperty("result").GetProperty("structuredContent");
            Assert.Equal(ComputerUseWinStatusValues.Ok, firstStatePayload.GetProperty("status").GetString());
            string stateToken = firstStatePayload.GetProperty("stateToken").GetString()!;
            JsonElement sourceTarget = firstStatePayload
                .GetProperty("accessibilityTree")
                .EnumerateArray()
                .First(element => string.Equals(element.GetProperty("name").GetString(), "Drag source token", StringComparison.Ordinal));
            JsonElement destinationTarget = firstStatePayload
                .GetProperty("accessibilityTree")
                .EnumerateArray()
                .First(element => element.GetProperty("name").GetString()!.StartsWith("Drag destination target:", StringComparison.Ordinal));
            string initialMirror = firstStatePayload
                .GetProperty("accessibilityTree")
                .EnumerateArray()
                .First(element => element.GetProperty("name").GetString()!.StartsWith("Drag mirror:", StringComparison.Ordinal))
                .GetProperty("name")
                .GetString()!;
            Assert.Contains(
                ToolNames.ComputerUseWinDrag,
                sourceTarget.GetProperty("actions").EnumerateArray().Select(item => item.GetString()));
            Assert.Contains(
                ToolNames.ComputerUseWinDrag,
                destinationTarget.GetProperty("actions").EnumerateArray().Select(item => item.GetString()));

            using JsonDocument dragResponse = await session.CallToolAsync(
                ToolNames.ComputerUseWinDrag,
                new
                {
                    stateToken,
                    fromElementIndex = sourceTarget.GetProperty("index").GetInt32(),
                    toElementIndex = destinationTarget.GetProperty("index").GetInt32(),
                    confirm = true,
                });

            JsonElement dragPayload = dragResponse.RootElement.GetProperty("result").GetProperty("structuredContent");
            Assert.Equal(ComputerUseWinStatusValues.VerifyNeeded, dragPayload.GetProperty("status").GetString());
            Assert.Equal(helperHwnd, dragPayload.GetProperty("targetHwnd").GetInt64());
            Assert.True(dragPayload.GetProperty("refreshStateRecommended").GetBoolean());

            using JsonDocument secondStateResponse = await session.CallToolAsync(
                ToolNames.ComputerUseWinGetAppState,
                new
                {
                    hwnd = helperHwnd,
                    confirm = true,
                });

            JsonElement secondStatePayload = secondStateResponse.RootElement.GetProperty("result").GetProperty("structuredContent");
            Assert.Equal(ComputerUseWinStatusValues.Ok, secondStatePayload.GetProperty("status").GetString());
            string updatedMirror = secondStatePayload
                .GetProperty("accessibilityTree")
                .EnumerateArray()
                .First(element => element.GetProperty("name").GetString()!.StartsWith("Drag mirror:", StringComparison.Ordinal))
                .GetProperty("name")
                .GetString()!;
            Assert.NotEqual(initialMirror, updatedMirror);
            Assert.Equal("Drag mirror: dropped", updatedMirror);
        }
        finally
        {
            process.StandardInput.Close();
            await WaitForExitAsync(process);
            await StopHelperWindowAsync(helper);
        }
    }

    [Fact]
    public async Task ComputerUseWinGetAppStateDoesNotAttachWindowWhenRequestIsInvalid()
    {
        using Process helper = StartHelperWindow(
            title: $"Okno Smoke Helper Failed Observation {Guid.NewGuid():N}",
            lifetimeMs: 20000);
        long helperHwnd = await WaitForMainWindowAsync(helper);
        await Task.Delay(750);
        using Process process = StartServer(ToolSurfaceProfileValues.ComputerUseWin);

        await using StreamWriter writer = process.StandardInput;
        using StreamReader reader = process.StandardOutput;
        McpRequestSession session = new(reader, writer);

        try
        {
            using JsonDocument initializeResponse = await session.SendRequestAsync(
                "initialize",
                new
                {
                    protocolVersion = "2025-11-25",
                    capabilities = new { },
                    clientInfo = new
                    {
                        name = "Okno.IntegrationTests",
                        version = "0.1.0",
                    },
                },
                "initialize");

            await session.SendNotificationAsync("notifications/initialized");

            using JsonDocument failedStateResponse = await session.CallToolAsync(
                ToolNames.ComputerUseWinGetAppState,
                new
                {
                    hwnd = helperHwnd,
                    confirm = true,
                    maxNodes = UiaSnapshotRequestValidator.MaxNodesCeiling + 1,
                });

            JsonElement failedResult = failedStateResponse.RootElement.GetProperty("result");
            Assert.True(failedResult.GetProperty("isError").GetBoolean());
            JsonElement failedPayload = failedResult.GetProperty("structuredContent");
            Assert.Equal(ComputerUseWinStatusValues.Failed, failedPayload.GetProperty("status").GetString());
            Assert.Equal(ComputerUseWinFailureCodeValues.InvalidRequest, failedPayload.GetProperty("failureCode").GetString());
            Assert.Contains("maxNodes", failedPayload.GetProperty("reason").GetString(), StringComparison.OrdinalIgnoreCase);

            using JsonDocument noArgsResponse = await session.CallToolAsync(
                ToolNames.ComputerUseWinGetAppState,
                new { });

            JsonElement noArgsPayload = noArgsResponse.RootElement.GetProperty("result").GetProperty("structuredContent");
            Assert.Equal(ComputerUseWinStatusValues.Failed, noArgsPayload.GetProperty("status").GetString());
            Assert.Equal(ComputerUseWinFailureCodeValues.MissingTarget, noArgsPayload.GetProperty("failureCode").GetString());
        }
        finally
        {
            process.StandardInput.Close();
            await WaitForExitAsync(process);
            await StopHelperWindowAsync(helper);
        }
    }

    [Fact]
    public async Task ComputerUseWinGetAppStateRejectsInvalidMaxNodesBeforeObservation()
    {
        using Process helper = StartHelperWindow(
            title: $"Okno Smoke Helper Snapshot Failure {Guid.NewGuid():N}",
            lifetimeMs: 20000);
        long helperHwnd = await WaitForMainWindowAsync(helper);
        await Task.Delay(750);
        using Process process = StartServer(ToolSurfaceProfileValues.ComputerUseWin);

        await using StreamWriter writer = process.StandardInput;
        using StreamReader reader = process.StandardOutput;
        McpRequestSession session = new(reader, writer);

        try
        {
            using JsonDocument initializeResponse = await session.SendRequestAsync(
                "initialize",
                new
                {
                    protocolVersion = "2025-11-25",
                    capabilities = new { },
                    clientInfo = new
                    {
                        name = "Okno.IntegrationTests",
                        version = "0.1.0",
                    },
                },
                "initialize");

            await session.SendNotificationAsync("notifications/initialized");

            using JsonDocument stateResponse = await session.CallToolAsync(
                ToolNames.ComputerUseWinGetAppState,
                new
                {
                    hwnd = helperHwnd,
                    confirm = true,
                    maxNodes = UiaSnapshotRequestValidator.MaxNodesCeiling + 1,
                });

            JsonElement result = stateResponse.RootElement.GetProperty("result");
            Assert.True(result.GetProperty("isError").GetBoolean());
            JsonElement payload = result.GetProperty("structuredContent");
            Assert.Equal(ComputerUseWinStatusValues.Failed, payload.GetProperty("status").GetString());
            Assert.Equal(ComputerUseWinFailureCodeValues.InvalidRequest, payload.GetProperty("failureCode").GetString());
            Assert.Contains("maxNodes", payload.GetProperty("reason").GetString(), StringComparison.OrdinalIgnoreCase);
            Assert.False(payload.TryGetProperty("stateToken", out _));
            Assert.False(payload.TryGetProperty("accessibilityTree", out _));
        }
        finally
        {
            process.StandardInput.Close();
            await WaitForExitAsync(process);
            await StopHelperWindowAsync(helper);
        }
    }

    [Fact]
    public async Task ComputerUseWinGetAppStatePublishesObservedKeyboardFocus()
    {
        using Process helper = StartHelperWindow(
            title: $"Okno Smoke Helper Focus {Guid.NewGuid():N}",
            lifetimeMs: 20000);
        long helperHwnd = await WaitForMainWindowAsync(helper);
        await Task.Delay(750);
        using Process process = StartServer(ToolSurfaceProfileValues.ComputerUseWin);

        await using StreamWriter writer = process.StandardInput;
        using StreamReader reader = process.StandardOutput;
        McpRequestSession session = new(reader, writer);

        try
        {
            using JsonDocument initializeResponse = await session.SendRequestAsync(
                "initialize",
                new
                {
                    protocolVersion = "2025-11-25",
                    capabilities = new { },
                    clientInfo = new
                    {
                        name = "Okno.IntegrationTests",
                        version = "0.1.0",
                    },
                },
                "initialize");

            await session.SendNotificationAsync("notifications/initialized");

            using JsonDocument stateResponse = await session.CallToolAsync(
                ToolNames.ComputerUseWinGetAppState,
                new
                {
                    hwnd = helperHwnd,
                    confirm = true,
                });

            JsonElement payload = stateResponse.RootElement.GetProperty("result").GetProperty("structuredContent");
            Assert.Equal(ComputerUseWinStatusValues.Ok, payload.GetProperty("status").GetString());
            Assert.Contains(
                payload.GetProperty("accessibilityTree").EnumerateArray(),
                element => element.GetProperty("hasKeyboardFocus").GetBoolean());
        }
        finally
        {
            process.StandardInput.Close();
            await WaitForExitAsync(process);
            await StopHelperWindowAsync(helper);
        }
    }

    [Fact]
    public async Task ToolsListPublishesWindowsLaunchProcessWithSchemaAndAnnotations()
    {
        using Process process = StartServer();

        await using StreamWriter writer = process.StandardInput;
        using StreamReader reader = process.StandardOutput;
        McpRequestSession session = new(reader, writer);

        try
        {
            using JsonDocument initializeResponse = await session.SendRequestAsync(
                "initialize",
                new
                {
                    protocolVersion = "2025-11-25",
                    capabilities = new { },
                    clientInfo = new
                    {
                        name = "Okno.IntegrationTests",
                        version = "0.1.0",
                    },
                },
                "initialize");

            await session.SendNotificationAsync("notifications/initialized");

            using JsonDocument toolsResponse = await session.SendRequestAsync(
                "tools/list",
                new { },
                "tools/list");
            JsonElement launchDescriptor = toolsResponse.RootElement
                .GetProperty("result")
                .GetProperty("tools")
                .EnumerateArray()
                .Single(tool => tool.GetProperty("name").GetString() == ToolNames.WindowsLaunchProcess);

            Assert.False(string.IsNullOrWhiteSpace(launchDescriptor.GetProperty("description").GetString()));
            JsonElement annotations = launchDescriptor.GetProperty("annotations");
            Assert.False(annotations.GetProperty("readOnlyHint").GetBoolean());
            Assert.False(annotations.GetProperty("destructiveHint").GetBoolean());
            Assert.False(annotations.GetProperty("idempotentHint").GetBoolean());
            Assert.True(annotations.GetProperty("openWorldHint").GetBoolean());

            JsonElement properties = launchDescriptor.GetProperty("inputSchema").GetProperty("properties");
            Assert.False(string.IsNullOrWhiteSpace(properties.GetProperty("executable").GetProperty("description").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(properties.GetProperty("args").GetProperty("description").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(properties.GetProperty("workingDirectory").GetProperty("description").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(properties.GetProperty("waitForWindow").GetProperty("description").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(properties.GetProperty("timeoutMs").GetProperty("description").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(properties.GetProperty("dryRun").GetProperty("description").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(properties.GetProperty("confirm").GetProperty("description").GetString()));
            AssertSchemaTypeContains(properties.GetProperty("args").GetProperty("type"), "array");
            AssertSchemaTypeContains(properties.GetProperty("executable").GetProperty("type"), "string");
        }
        finally
        {
            process.StandardInput.Close();
            await WaitForExitAsync(process);
        }
    }

    [Fact]
    public async Task ToolsListPublishesWindowsOpenTargetWithSchemaAndAnnotations()
    {
        using Process process = StartServer();

        await using StreamWriter writer = process.StandardInput;
        using StreamReader reader = process.StandardOutput;
        McpRequestSession session = new(reader, writer);

        try
        {
            using JsonDocument initializeResponse = await session.SendRequestAsync(
                "initialize",
                new
                {
                    protocolVersion = "2025-11-25",
                    capabilities = new { },
                    clientInfo = new
                    {
                        name = "Okno.IntegrationTests",
                        version = "0.1.0",
                    },
                },
                "initialize");

            await session.SendNotificationAsync("notifications/initialized");

            using JsonDocument toolsResponse = await session.SendRequestAsync(
                "tools/list",
                new { },
                "tools/list");
            JsonElement openTargetDescriptor = toolsResponse.RootElement
                .GetProperty("result")
                .GetProperty("tools")
                .EnumerateArray()
                .Single(tool => tool.GetProperty("name").GetString() == ToolNames.WindowsOpenTarget);

            Assert.False(string.IsNullOrWhiteSpace(openTargetDescriptor.GetProperty("description").GetString()));
            JsonElement annotations = openTargetDescriptor.GetProperty("annotations");
            Assert.False(annotations.GetProperty("readOnlyHint").GetBoolean());
            Assert.False(annotations.GetProperty("destructiveHint").GetBoolean());
            Assert.False(annotations.GetProperty("idempotentHint").GetBoolean());
            Assert.True(annotations.GetProperty("openWorldHint").GetBoolean());

            JsonElement properties = openTargetDescriptor.GetProperty("inputSchema").GetProperty("properties");
            Assert.False(string.IsNullOrWhiteSpace(properties.GetProperty("targetKind").GetProperty("description").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(properties.GetProperty("target").GetProperty("description").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(properties.GetProperty("dryRun").GetProperty("description").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(properties.GetProperty("confirm").GetProperty("description").GetString()));
            AssertSchemaTypeContains(properties.GetProperty("targetKind").GetProperty("type"), "string");
            AssertSchemaTypeContains(properties.GetProperty("target").GetProperty("type"), "string");
        }
        finally
        {
            process.StandardInput.Close();
            await WaitForExitAsync(process);
        }
    }

    [Fact]
    public async Task ToolsListPublishesImplementedWindowsInputWithClickFirstRequestSchema()
    {
        using Process process = StartServer();

        await using StreamWriter writer = process.StandardInput;
        using StreamReader reader = process.StandardOutput;
        McpRequestSession session = new(reader, writer);

        try
        {
            using JsonDocument initializeResponse = await session.SendRequestAsync(
                "initialize",
                new
                {
                    protocolVersion = "2025-11-25",
                    capabilities = new { },
                    clientInfo = new
                    {
                        name = "Okno.IntegrationTests",
                        version = "0.1.0",
                    },
                },
                "initialize");

            await session.SendNotificationAsync("notifications/initialized");

            using JsonDocument toolsResponse = await session.SendRequestAsync(
                "tools/list",
                new { },
                "tools/list");
            JsonElement inputDescriptor = toolsResponse.RootElement
                .GetProperty("result")
                .GetProperty("tools")
                .EnumerateArray()
                .Single(tool => tool.GetProperty("name").GetString() == ToolNames.WindowsInput);

            Assert.False(string.IsNullOrWhiteSpace(inputDescriptor.GetProperty("description").GetString()));
            JsonElement properties = inputDescriptor.GetProperty("inputSchema").GetProperty("properties");
            Assert.True(properties.TryGetProperty("actions", out JsonElement actionsProperty));
            Assert.True(properties.TryGetProperty("hwnd", out JsonElement hwndProperty));
            Assert.True(properties.TryGetProperty("confirm", out JsonElement confirmProperty));
            Assert.False(properties.TryGetProperty("actionsJson", out _));
            Assert.False(string.IsNullOrWhiteSpace(actionsProperty.GetProperty("description").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(hwndProperty.GetProperty("description").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(confirmProperty.GetProperty("description").GetString()));
            AssertSchemaTypeContains(actionsProperty.GetProperty("type"), "array");
            JsonElement actionItemProperties = actionsProperty.GetProperty("items").GetProperty("properties");
            Assert.True(actionItemProperties.TryGetProperty("type", out _));
            Assert.True(actionItemProperties.TryGetProperty("point", out _));
            Assert.True(actionItemProperties.TryGetProperty("coordinateSpace", out _));
            Assert.True(actionItemProperties.TryGetProperty("button", out _));
            Assert.True(actionItemProperties.TryGetProperty("captureReference", out _));
            Assert.False(actionItemProperties.TryGetProperty("path", out _));
            Assert.False(actionItemProperties.TryGetProperty("keys", out _));
            Assert.False(actionItemProperties.TryGetProperty("text", out _));
            Assert.False(actionItemProperties.TryGetProperty("key", out _));
            Assert.False(actionItemProperties.TryGetProperty("repeat", out _));
            Assert.False(actionItemProperties.TryGetProperty("delta", out _));
            Assert.False(actionItemProperties.TryGetProperty("direction", out _));

            JsonElement actionBranches = actionsProperty.GetProperty("items").GetProperty("oneOf");
            Assert.Equal(3, actionBranches.GetArrayLength());

            JsonElement clickBranch = FindActionSchemaBranch(actionBranches, InputActionTypeValues.Click);
            AssertSchemaRequiredContains(clickBranch, "type", "point", "coordinateSpace");
            AssertSchemaPropertyDoesNotAllowNull(clickBranch, "point");
            AssertSchemaPropertyDoesNotAllowNull(clickBranch, "coordinateSpace");
            Assert.True(clickBranch.GetProperty("properties").TryGetProperty("button", out _));
            AssertSchemaPropertyDoesNotAllowNull(clickBranch, "button");
            Assert.True(clickBranch.GetProperty("properties").TryGetProperty("captureReference", out _));
            AssertSchemaPropertyDoesNotAllowNull(clickBranch, "captureReference");
            Assert.False(clickBranch.GetProperty("properties").TryGetProperty("text", out _));

            JsonElement moveBranch = FindActionSchemaBranch(actionBranches, InputActionTypeValues.Move);
            AssertSchemaRequiredContains(moveBranch, "type", "point", "coordinateSpace");
            Assert.False(moveBranch.GetProperty("properties").TryGetProperty("button", out _));

            JsonElement doubleClickBranch = FindActionSchemaBranch(actionBranches, InputActionTypeValues.DoubleClick);
            AssertSchemaRequiredContains(doubleClickBranch, "type", "point", "coordinateSpace");
            Assert.False(doubleClickBranch.GetProperty("properties").TryGetProperty("button", out _));

            JsonElement pointSchema = actionItemProperties.GetProperty("point");
            AssertSchemaRequiredContains(pointSchema, "x", "y");
            AssertSchemaPropertyDoesNotAllowNull(pointSchema, "x");
            AssertSchemaPropertyDoesNotAllowNull(pointSchema, "y");

            JsonElement captureReferenceSchema = actionItemProperties.GetProperty("captureReference");
            AssertSchemaRequiredContains(captureReferenceSchema, "bounds", "pixelWidth", "pixelHeight");
            AssertSchemaPropertyDoesNotAllowNull(captureReferenceSchema, "bounds");
            AssertSchemaPropertyDoesNotAllowNull(captureReferenceSchema, "pixelWidth");
            AssertSchemaPropertyDoesNotAllowNull(captureReferenceSchema, "pixelHeight");
            AssertSchemaIntegerPropertyHasMinimum(captureReferenceSchema, "pixelWidth", 1);
            AssertSchemaIntegerPropertyHasMinimum(captureReferenceSchema, "pixelHeight", 1);
            AssertSchemaPropertyAllowsNull(captureReferenceSchema, "effectiveDpi");
            AssertSchemaPropertyAllowsNull(captureReferenceSchema, "capturedAtUtc");
            Assert.True(captureReferenceSchema.GetProperty("properties").TryGetProperty("frameBounds", out JsonElement frameBoundsSchema));
            Assert.True(captureReferenceSchema.GetProperty("properties").TryGetProperty("targetIdentity", out JsonElement targetIdentitySchema));
            AssertSchemaPropertyDoesNotAllowNull(captureReferenceSchema, "frameBounds");
            AssertSchemaPropertyDoesNotAllowNull(captureReferenceSchema, "targetIdentity");
            AssertSchemaRequiredContains(
                captureReferenceSchema.GetProperty("properties").GetProperty("bounds"),
                "left",
                "top",
                "right",
                "bottom");
            JsonElement boundsSchema = captureReferenceSchema.GetProperty("properties").GetProperty("bounds");
            AssertSchemaPropertyDoesNotAllowNull(boundsSchema, "left");
            AssertSchemaPropertyDoesNotAllowNull(boundsSchema, "top");
            AssertSchemaPropertyDoesNotAllowNull(boundsSchema, "right");
            AssertSchemaPropertyDoesNotAllowNull(boundsSchema, "bottom");
            AssertSchemaRequiredContains(frameBoundsSchema, "left", "top", "right", "bottom");
            AssertSchemaPropertyDoesNotAllowNull(frameBoundsSchema, "left");
            AssertSchemaPropertyDoesNotAllowNull(frameBoundsSchema, "top");
            AssertSchemaPropertyDoesNotAllowNull(frameBoundsSchema, "right");
            AssertSchemaPropertyDoesNotAllowNull(frameBoundsSchema, "bottom");
            AssertSchemaRequiredContains(targetIdentitySchema, "hwnd", "processId", "threadId", "className");
            AssertSchemaPropertyDoesNotAllowNull(targetIdentitySchema, "hwnd");
            AssertSchemaPropertyDoesNotAllowNull(targetIdentitySchema, "processId");
            AssertSchemaPropertyDoesNotAllowNull(targetIdentitySchema, "threadId");
            AssertSchemaPropertyDoesNotAllowNull(targetIdentitySchema, "className");
        }
        finally
        {
            process.StandardInput.Close();
            await WaitForExitAsync(process);
        }
    }

    [Fact]
    public async Task OpenTargetRejectsUnsupportedExtraFieldAsToolLevelFailedResult()
    {
        using Process process = StartServer();

        await using StreamWriter writer = process.StandardInput;
        using StreamReader reader = process.StandardOutput;
        McpRequestSession session = new(reader, writer);

        try
        {
            using JsonDocument initializeResponse = await session.SendRequestAsync(
                "initialize",
                new
                {
                    protocolVersion = "2025-11-25",
                    capabilities = new { },
                    clientInfo = new
                    {
                        name = "Okno.IntegrationTests",
                        version = "0.1.0",
                    },
                },
                "initialize");

            await session.SendNotificationAsync("notifications/initialized");

            using JsonDocument response = await CallToolAsync(
                session,
                ToolNames.WindowsOpenTarget,
                new
                {
                    targetKind = "document",
                    target = @"C:\Docs\report.pdf",
                    workingDirectory = @"C:\Docs",
                });

            JsonElement result = response.RootElement.GetProperty("result");
            Assert.True(result.GetProperty("isError").GetBoolean());
            JsonElement payload = result.GetProperty("structuredContent");
            Assert.Equal(OpenTargetStatusValues.Failed, payload.GetProperty("status").GetString());
            Assert.Equal(OpenTargetStatusValues.Failed, payload.GetProperty("decision").GetString());
            Assert.Equal(OpenTargetFailureCodeValues.InvalidRequest, payload.GetProperty("failureCode").GetString());
        }
        finally
        {
            process.StandardInput.Close();
            await WaitForExitAsync(process);
        }
    }

    [Fact]
    public async Task LaunchProcessRejectsEnvironmentOverridesAsToolLevelFailedResult()
    {
        using Process process = StartServer();

        await using StreamWriter writer = process.StandardInput;
        using StreamReader reader = process.StandardOutput;
        McpRequestSession session = new(reader, writer);

        try
        {
            using JsonDocument initializeResponse = await session.SendRequestAsync(
                "initialize",
                new
                {
                    protocolVersion = "2025-11-25",
                    capabilities = new { },
                    clientInfo = new
                    {
                        name = "Okno.IntegrationTests",
                        version = "0.1.0",
                    },
                },
                "initialize");

            await session.SendNotificationAsync("notifications/initialized");

            using JsonDocument response = await CallToolAsync(
                session,
                ToolNames.WindowsLaunchProcess,
                new
                {
                    executable = "notepad.exe",
                    environment = new
                    {
                        FOO = "bar",
                    },
                });

            JsonElement result = response.RootElement.GetProperty("result");
            Assert.True(result.GetProperty("isError").GetBoolean());
            JsonElement payload = result.GetProperty("structuredContent");
            Assert.Equal(LaunchProcessStatusValues.Failed, payload.GetProperty("status").GetString());
            Assert.Equal(LaunchProcessStatusValues.Failed, payload.GetProperty("decision").GetString());
            Assert.Equal(LaunchProcessFailureCodeValues.UnsupportedEnvironmentOverrides, payload.GetProperty("failureCode").GetString());
        }
        finally
        {
            process.StandardInput.Close();
            await WaitForExitAsync(process);
        }
    }

    [Fact]
    public async Task LaunchProcessRejectsUnsupportedExtraFieldAsToolLevelFailedResult()
    {
        using Process process = StartServer();

        await using StreamWriter writer = process.StandardInput;
        using StreamReader reader = process.StandardOutput;
        McpRequestSession session = new(reader, writer);

        try
        {
            using JsonDocument initializeResponse = await session.SendRequestAsync(
                "initialize",
                new
                {
                    protocolVersion = "2025-11-25",
                    capabilities = new { },
                    clientInfo = new
                    {
                        name = "Okno.IntegrationTests",
                        version = "0.1.0",
                    },
                },
                "initialize");

            await session.SendNotificationAsync("notifications/initialized");

            using JsonDocument response = await CallToolAsync(
                session,
                ToolNames.WindowsLaunchProcess,
                new
                {
                    executable = "notepad.exe",
                    extraField = "unexpected",
                });

            JsonElement result = response.RootElement.GetProperty("result");
            Assert.True(result.GetProperty("isError").GetBoolean());
            JsonElement payload = result.GetProperty("structuredContent");
            Assert.Equal(LaunchProcessStatusValues.Failed, payload.GetProperty("status").GetString());
            Assert.Equal(LaunchProcessStatusValues.Failed, payload.GetProperty("decision").GetString());
            Assert.Equal(LaunchProcessFailureCodeValues.InvalidRequest, payload.GetProperty("failureCode").GetString());
        }
        finally
        {
            process.StandardInput.Close();
            await WaitForExitAsync(process);
        }
    }

    [Fact]
    public async Task LaunchProcessRejectsMissingExecutableAsToolLevelFailedResult()
    {
        using Process process = StartServer();

        await using StreamWriter writer = process.StandardInput;
        using StreamReader reader = process.StandardOutput;
        McpRequestSession session = new(reader, writer);

        try
        {
            using JsonDocument initializeResponse = await session.SendRequestAsync(
                "initialize",
                new
                {
                    protocolVersion = "2025-11-25",
                    capabilities = new { },
                    clientInfo = new
                    {
                        name = "Okno.IntegrationTests",
                        version = "0.1.0",
                    },
                },
                "initialize");

            await session.SendNotificationAsync("notifications/initialized");

            using JsonDocument response = await CallToolAsync(
                session,
                ToolNames.WindowsLaunchProcess,
                new { });

            JsonElement result = response.RootElement.GetProperty("result");
            Assert.True(result.GetProperty("isError").GetBoolean());
            JsonElement payload = result.GetProperty("structuredContent");
            Assert.Equal(LaunchProcessStatusValues.Failed, payload.GetProperty("status").GetString());
            Assert.Equal(LaunchProcessStatusValues.Failed, payload.GetProperty("decision").GetString());
            Assert.Equal(LaunchProcessFailureCodeValues.InvalidRequest, payload.GetProperty("failureCode").GetString());
        }
        finally
        {
            process.StandardInput.Close();
            await WaitForExitAsync(process);
        }
    }

    [Fact]
    public async Task LaunchProcessRejectsWrongTypeArgsAsToolLevelFailedResult()
    {
        using Process process = StartServer();

        await using StreamWriter writer = process.StandardInput;
        using StreamReader reader = process.StandardOutput;
        McpRequestSession session = new(reader, writer);

        try
        {
            using JsonDocument initializeResponse = await session.SendRequestAsync(
                "initialize",
                new
                {
                    protocolVersion = "2025-11-25",
                    capabilities = new { },
                    clientInfo = new
                    {
                        name = "Okno.IntegrationTests",
                        version = "0.1.0",
                    },
                },
                "initialize");

            await session.SendNotificationAsync("notifications/initialized");

            using JsonDocument response = await CallToolAsync(
                session,
                ToolNames.WindowsLaunchProcess,
                new
                {
                    executable = "notepad.exe",
                    args = "--flag",
                });

            JsonElement result = response.RootElement.GetProperty("result");
            Assert.True(result.GetProperty("isError").GetBoolean());
            JsonElement payload = result.GetProperty("structuredContent");
            Assert.Equal(LaunchProcessStatusValues.Failed, payload.GetProperty("status").GetString());
            Assert.Equal(LaunchProcessStatusValues.Failed, payload.GetProperty("decision").GetString());
            Assert.Equal(LaunchProcessFailureCodeValues.InvalidRequest, payload.GetProperty("failureCode").GetString());
        }
        finally
        {
            process.StandardInput.Close();
            await WaitForExitAsync(process);
        }
    }

    [Fact]
    public async Task OknoContractPublishesImplementedInputExecutionPolicyWithSnakeCaseDescriptorFields()
    {
        using Process process = StartServer();

        await using StreamWriter writer = process.StandardInput;
        using StreamReader reader = process.StandardOutput;
        McpRequestSession session = new(reader, writer);

        try
        {
            using JsonDocument initializeResponse = await session.SendRequestAsync(
                "initialize",
                new
                {
                    protocolVersion = "2025-11-25",
                    capabilities = new { },
                    clientInfo = new
                    {
                        name = "Okno.IntegrationTests",
                        version = "0.1.0",
                    },
                },
                "initialize");

            await session.SendNotificationAsync("notifications/initialized");

            using JsonDocument contractResponse = await session.CallToolAsync(ToolNames.OknoContract, new { });
            using JsonDocument contractPayload = JsonDocument.Parse(GetToolTextPayload(contractResponse));
            JsonElement implementedDescriptor = contractPayload.RootElement
                .GetProperty("implementedTools")
                .EnumerateArray()
                .Single(item => item.GetProperty("name").GetString() == ToolNames.OknoContract);
            JsonElement inputDescriptor = contractPayload.RootElement
                .GetProperty("implementedTools")
                .EnumerateArray()
                .Single(item => item.GetProperty("name").GetString() == ToolNames.WindowsInput);

            Assert.True(implementedDescriptor.TryGetProperty("planned_phase", out JsonElement implementedPlannedPhase));
            Assert.Equal(JsonValueKind.Null, implementedPlannedPhase.ValueKind);
            Assert.True(implementedDescriptor.TryGetProperty("suggested_alternative", out JsonElement implementedSuggestedAlternative));
            Assert.Equal(JsonValueKind.Null, implementedSuggestedAlternative.ValueKind);
            Assert.True(implementedDescriptor.TryGetProperty("execution_policy", out JsonElement implementedExecutionPolicy));
            Assert.Equal(JsonValueKind.Null, implementedExecutionPolicy.ValueKind);
            Assert.False(implementedDescriptor.TryGetProperty("plannedPhase", out _));
            Assert.False(implementedDescriptor.TryGetProperty("suggestedAlternative", out _));
            Assert.False(implementedDescriptor.TryGetProperty("executionPolicy", out _));
            Assert.Equal(JsonValueKind.Null, inputDescriptor.GetProperty("planned_phase").ValueKind);
            Assert.False(inputDescriptor.TryGetProperty("plannedPhase", out _));
            Assert.Equal("os_side_effect", inputDescriptor.GetProperty("safety_class").GetString());
            Assert.False(inputDescriptor.TryGetProperty("safetyClass", out _));
            Assert.True(inputDescriptor.TryGetProperty("execution_policy", out JsonElement executionPolicy));
            Assert.False(inputDescriptor.TryGetProperty("executionPolicy", out _));
            Assert.Equal("input", executionPolicy.GetProperty("policy_group").GetString());
            Assert.False(executionPolicy.TryGetProperty("policyGroup", out _));
            Assert.Equal("destructive", executionPolicy.GetProperty("risk_level").GetString());
            Assert.False(executionPolicy.TryGetProperty("riskLevel", out _));
            Assert.False(executionPolicy.GetProperty("supports_dry_run").GetBoolean());
            Assert.False(executionPolicy.TryGetProperty("supportsDryRun", out _));
        }
        finally
        {
            process.StandardInput.Close();
            await WaitForExitAsync(process);
        }
    }

    [Fact]
    public async Task DeferredActionToolsReturnStructuredUnsupportedPayloadWithoutTransportError()
    {
        using Process process = StartServer();

        await using StreamWriter writer = process.StandardInput;
        using StreamReader reader = process.StandardOutput;
        McpRequestSession session = new(reader, writer);

        string[] expectedDeferredTools =
        [
            ToolNames.WindowsClipboardGet,
            ToolNames.WindowsClipboardSet,
            ToolNames.WindowsUiaAction,
        ];

        try
        {
            using JsonDocument initializeResponse = await session.SendRequestAsync(
                "initialize",
                new
                {
                    protocolVersion = "2025-11-25",
                    capabilities = new { },
                    clientInfo = new
                    {
                        name = "Okno.IntegrationTests",
                        version = "0.1.0",
                    },
                },
                "initialize");

            await session.SendNotificationAsync("notifications/initialized");

            Assert.Equal(
                expectedDeferredTools,
                ToolContractManifest.Deferred.Select(item => item.Name).ToArray());

            foreach (string toolName in expectedDeferredTools)
            {
                using JsonDocument response = await session.CallToolAsync(
                    toolName,
                    CreateDeferredInvocationArguments(toolName));

                Assert.False(
                    response.RootElement.TryGetProperty("error", out _),
                    $"Deferred tool '{toolName}' must not fail through transport-level error.");

                JsonElement result = response.RootElement.GetProperty("result");
                Assert.True(result.TryGetProperty("content", out JsonElement content));
                Assert.Equal(JsonValueKind.Array, content.ValueKind);
                Assert.True(content.GetArrayLength() > 0);

                using JsonDocument payload = JsonDocument.Parse(GetToolTextPayload(response));
                JsonElement root = payload.RootElement;
                ToolDescriptor descriptor = ToolContractManifest.Deferred.Single(item => item.Name == toolName);

                Assert.Equal(toolName, root.GetProperty("toolName").GetString());
                Assert.Equal("unsupported", root.GetProperty("status").GetString());
                Assert.Equal(descriptor.PlannedPhase, root.GetProperty("plannedPhase").GetString());
                Assert.Equal(descriptor.SuggestedAlternative, root.GetProperty("suggestedAlternative").GetString());
                Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("reason").GetString()));
            }
        }
        finally
        {
            process.StandardInput.Close();
            await WaitForExitAsync(process);
        }
    }

    [Fact]
    public async Task WindowsInputCallMaterializesMalformedActionElementAsToolLevelFailedResult()
    {
        using Process process = StartServer();

        await using StreamWriter writer = process.StandardInput;
        using StreamReader reader = process.StandardOutput;
        McpRequestSession session = new(reader, writer);

        try
        {
            using JsonDocument initializeResponse = await session.SendRequestAsync(
                "initialize",
                new
                {
                    protocolVersion = "2025-11-25",
                    capabilities = new { },
                    clientInfo = new
                    {
                        name = "Okno.IntegrationTests",
                        version = "0.1.0",
                    },
                },
                "initialize");

            await session.SendNotificationAsync("notifications/initialized");

            using JsonDocument response = await session.CallToolAsync(
                ToolNames.WindowsInput,
                new
                {
                    actions = new object[]
                    {
                        "bad",
                    },
                });

            Assert.False(
                response.RootElement.TryGetProperty("error", out _),
                "Implemented windows.input must not fail through transport-level error for malformed action element.");

            JsonElement result = response.RootElement.GetProperty("result");
            Assert.True(result.GetProperty("isError").GetBoolean());
            JsonElement root = result.GetProperty("structuredContent");
            Assert.Equal(InputStatusValues.Failed, root.GetProperty("status").GetString());
            Assert.Equal(InputStatusValues.Failed, root.GetProperty("decision").GetString());
            Assert.Equal(InputFailureCodeValues.InvalidRequest, root.GetProperty("failureCode").GetString());
        }
        finally
        {
            process.StandardInput.Close();
            await WaitForExitAsync(process);
        }
    }

    [Fact]
    public async Task WindowsWaitRejectsExpectedTextForNonTextAppearsThroughStdio()
    {
        using Process process = StartServer();

        await using StreamWriter writer = process.StandardInput;
        using StreamReader reader = process.StandardOutput;
        McpRequestSession session = new(reader, writer);

        try
        {
            using JsonDocument initializeResponse = await session.SendRequestAsync(
                "initialize",
                new
                {
                    protocolVersion = "2025-11-25",
                    capabilities = new { },
                    clientInfo = new
                    {
                        name = "Okno.IntegrationTests",
                        version = "0.1.0",
                    },
                },
                "initialize");

            await session.SendNotificationAsync("notifications/initialized");

            using JsonDocument waitResponse = await session.CallToolAsync(
                ToolNames.WindowsWait,
                new
                {
                    condition = WaitConditionValues.FocusIs,
                    selector = new
                    {
                        automationId = "SearchBox",
                    },
                    expectedText = "Ready",
                    timeoutMs = 500,
                });

            JsonElement result = waitResponse.RootElement.GetProperty("result");
            Assert.True(result.GetProperty("isError").GetBoolean());
            JsonElement payload = result.GetProperty("structuredContent");
            Assert.Equal(WaitStatusValues.Failed, payload.GetProperty("status").GetString());
            Assert.Contains("expectedText", payload.GetProperty("reason").GetString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            process.StandardInput.Close();
            await WaitForExitAsync(process);
        }
    }

    [Fact]
    public async Task InitializeAndValidateCoreOknoToolsThroughStdio()
    {
        using Process process = StartServer();
        using Process helperProcess = StartHelperWindow();

        await using StreamWriter writer = process.StandardInput;
        using StreamReader reader = process.StandardOutput;
        McpRequestSession session = new(reader, writer);

        try
        {
            Assert.True(
                await WaitUntilAsync(() => TryGetProcessDpiAwareness(process, out int awareness) && awareness == ProcessPerMonitorDpiAware),
                "Okno.Server did not reach per-monitor DPI awareness during startup.");

            using JsonDocument initializeResponse = await session.SendRequestAsync(
                "initialize",
                new
                {
                    protocolVersion = "2025-11-25",
                    capabilities = new { },
                    clientInfo = new
                    {
                        name = "Okno.IntegrationTests",
                        version = "0.1.0",
                    },
                },
                "initialize");
            JsonElement initializeRoot = initializeResponse.RootElement;
            Assert.True(initializeRoot.TryGetProperty("result", out JsonElement initializeResult));
            Assert.True(initializeResult.TryGetProperty("serverInfo", out JsonElement serverInfo));
            Assert.Equal("Okno.Server", serverInfo.GetProperty("name").GetString());

            await session.SendNotificationAsync("notifications/initialized");

            using JsonDocument toolsResponse = await session.SendRequestAsync(
                "tools/list",
                new { },
                "tools/list");
            JsonElement tools = toolsResponse.RootElement
                .GetProperty("result")
                .GetProperty("tools");

            string[] toolNames = tools.EnumerateArray()
                .Select(tool => tool.GetProperty("name").GetString())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Cast<string>()
                .ToArray();

            foreach (string requiredTool in ToolContractManifest.SmokeRequiredToolNames)
            {
                Assert.Contains(requiredTool, toolNames);
            }

            JsonElement captureDescriptor = tools.EnumerateArray()
                .Single(tool => tool.GetProperty("name").GetString() == ToolNames.WindowsCapture);
            Assert.False(string.IsNullOrWhiteSpace(captureDescriptor.GetProperty("description").GetString()));
            Assert.Contains("explicit hwnd", captureDescriptor.GetProperty("description").GetString(), StringComparison.OrdinalIgnoreCase);
            JsonElement captureAnnotations = captureDescriptor.GetProperty("annotations");
            Assert.False(captureAnnotations.GetProperty("readOnlyHint").GetBoolean());
            Assert.False(captureAnnotations.GetProperty("idempotentHint").GetBoolean());
            Assert.False(captureAnnotations.GetProperty("destructiveHint").GetBoolean());
            Assert.True(captureAnnotations.GetProperty("openWorldHint").GetBoolean());
            JsonElement captureProperties = captureDescriptor.GetProperty("inputSchema").GetProperty("properties");
            Assert.False(string.IsNullOrWhiteSpace(captureProperties.GetProperty("scope").GetProperty("description").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(captureProperties.GetProperty("monitorId").GetProperty("description").GetString()));
            Assert.Contains("desktop", captureProperties.GetProperty("hwnd").GetProperty("description").GetString(), StringComparison.OrdinalIgnoreCase);

            JsonElement listMonitorsDescriptor = tools.EnumerateArray()
                .Single(tool => tool.GetProperty("name").GetString() == ToolNames.WindowsListMonitors);
            Assert.False(string.IsNullOrWhiteSpace(listMonitorsDescriptor.GetProperty("description").GetString()));
            JsonElement listMonitorsAnnotations = listMonitorsDescriptor.GetProperty("annotations");
            Assert.True(listMonitorsAnnotations.GetProperty("readOnlyHint").GetBoolean());
            Assert.False(listMonitorsAnnotations.GetProperty("destructiveHint").GetBoolean());

            JsonElement activateDescriptor = tools.EnumerateArray()
                .Single(tool => tool.GetProperty("name").GetString() == ToolNames.WindowsActivateWindow);
            Assert.False(string.IsNullOrWhiteSpace(activateDescriptor.GetProperty("description").GetString()));
            JsonElement activateAnnotations = activateDescriptor.GetProperty("annotations");
            Assert.False(activateAnnotations.GetProperty("readOnlyHint").GetBoolean());
            Assert.False(activateAnnotations.GetProperty("destructiveHint").GetBoolean());

            JsonElement uiaSnapshotDescriptor = tools.EnumerateArray()
                .Single(tool => tool.GetProperty("name").GetString() == ToolNames.WindowsUiaSnapshot);
            Assert.False(string.IsNullOrWhiteSpace(uiaSnapshotDescriptor.GetProperty("description").GetString()));
            JsonElement uiaSnapshotAnnotations = uiaSnapshotDescriptor.GetProperty("annotations");
            Assert.True(uiaSnapshotAnnotations.GetProperty("readOnlyHint").GetBoolean());
            Assert.True(uiaSnapshotAnnotations.GetProperty("idempotentHint").GetBoolean());
            Assert.False(uiaSnapshotAnnotations.GetProperty("destructiveHint").GetBoolean());
            Assert.True(uiaSnapshotAnnotations.GetProperty("openWorldHint").GetBoolean());
            JsonElement uiaSnapshotProperties = uiaSnapshotDescriptor.GetProperty("inputSchema").GetProperty("properties");
            Assert.False(string.IsNullOrWhiteSpace(uiaSnapshotProperties.GetProperty("hwnd").GetProperty("description").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(uiaSnapshotProperties.GetProperty("depth").GetProperty("description").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(uiaSnapshotProperties.GetProperty("maxNodes").GetProperty("description").GetString()));

            JsonElement healthDescriptor = tools.EnumerateArray()
                .Single(tool => tool.GetProperty("name").GetString() == ToolNames.OknoHealth);
            string healthDescription = healthDescriptor.GetProperty("description").GetString()!;
            Assert.Equal(ToolDescriptions.OknoHealthTool, healthDescription);

            using JsonDocument healthResponse = await session.CallToolAsync(ToolNames.OknoHealth, new { });
            JsonElement healthResult = healthResponse.RootElement.GetProperty("result");
            Assert.False(healthResult.TryGetProperty("isError", out JsonElement healthIsError) && healthIsError.GetBoolean());
            using JsonDocument healthPayload = JsonDocument.Parse(GetToolTextPayload(healthResponse));
            JsonElement healthRoot = healthPayload.RootElement;
            Assert.Equal("Okno", healthRoot.GetProperty("service").GetString());
            AssertHealthTopLevelContract(healthRoot);
            AssertHealthReadinessShape(healthRoot);

            using JsonDocument monitorsResponse = await session.CallToolAsync(ToolNames.WindowsListMonitors, new { });
            using JsonDocument monitorsPayload = JsonDocument.Parse(GetToolTextPayload(monitorsResponse));
            JsonElement monitorsRoot = monitorsPayload.RootElement;
            int monitorCount = monitorsRoot.GetProperty("count").GetInt32();
            Assert.True(monitorCount > 0, "Smoke contract requires at least one active monitor.");
            Assert.False(string.IsNullOrWhiteSpace(monitorsRoot.GetProperty("diagnostics").GetProperty("identityMode").GetString()));
            Assert.Equal(monitorCount, healthRoot.GetProperty("activeMonitorCount").GetInt32());
            Assert.Equal(
                monitorsRoot.GetProperty("diagnostics").GetProperty("identityMode").GetString(),
                healthRoot.GetProperty("displayIdentity").GetProperty("identityMode").GetString());
            string primaryMonitorId = monitorsRoot.GetProperty("monitors")[0].GetProperty("monitorId").GetString()!;
            long helperHwnd = await WaitForMainWindowAsync(helperProcess);

            using JsonDocument windowsPayload = await WaitForVisibleHelperWindowAsync(session, helperHwnd);
            JsonElement windowsRoot = windowsPayload.RootElement;
            int count = windowsRoot.GetProperty("count").GetInt32();
            Assert.True(count > 0, "Smoke contract requires at least one visible window.");
            Assert.Contains(
                windowsRoot.GetProperty("windows").EnumerateArray().Select(window => window.GetProperty("hwnd").GetInt64()),
                hwnd => hwnd == helperHwnd);

            using JsonDocument desktopCaptureResponse = await session.CallToolAsync(
                ToolNames.WindowsCapture,
                new
                {
                    scope = "desktop",
                    monitorId = primaryMonitorId,
                });
            JsonElement desktopCaptureResult = desktopCaptureResponse.RootElement.GetProperty("result");
            Assert.False(desktopCaptureResult.GetProperty("isError").GetBoolean());
            JsonElement desktopStructuredContent = desktopCaptureResult.GetProperty("structuredContent");
            Assert.Equal("desktop", desktopStructuredContent.GetProperty("scope").GetString());
            Assert.Equal(primaryMonitorId, desktopStructuredContent.GetProperty("monitorId").GetString());
            Assert.Equal("physical_pixels", desktopStructuredContent.GetProperty("coordinateSpace").GetString());

            using JsonDocument attachResponse = await session.CallToolAsync(ToolNames.WindowsAttachWindow, new { hwnd = helperHwnd });
            using JsonDocument attachPayload = JsonDocument.Parse(GetToolTextPayload(attachResponse));
            JsonElement attachRoot = attachPayload.RootElement;
            string attachStatus = attachRoot.GetProperty("status").GetString()!;
            Assert.Contains(attachStatus, AttachSuccessStates);

            using JsonDocument sessionResponse = await session.CallToolAsync(ToolNames.OknoSessionState, new { });
            using JsonDocument sessionPayload = JsonDocument.Parse(GetToolTextPayload(sessionResponse));
            JsonElement sessionRoot = sessionPayload.RootElement;
            Assert.Equal("window", sessionRoot.GetProperty("mode").GetString());
            Assert.Equal(helperHwnd, sessionRoot.GetProperty("attachedWindow").GetProperty("window").GetProperty("hwnd").GetInt64());

            using JsonDocument uiaSnapshotResponse = await WaitForSemanticUiaSnapshotAsync(session);
            JsonElement uiaSnapshotResult = uiaSnapshotResponse.RootElement.GetProperty("result");
            Assert.False(uiaSnapshotResult.GetProperty("isError").GetBoolean());
            JsonElement uiaSnapshotStructured = uiaSnapshotResult.GetProperty("structuredContent");
            Assert.Equal(UiaSnapshotStatusValues.Done, uiaSnapshotStructured.GetProperty("status").GetString());
            Assert.Equal(UiaSnapshotTargetSourceValues.Attached, uiaSnapshotStructured.GetProperty("targetSource").GetString());
            Assert.Equal(helperHwnd, uiaSnapshotStructured.GetProperty("window").GetProperty("hwnd").GetInt64());
            Assert.Equal("control", uiaSnapshotStructured.GetProperty("view").GetString());
            Assert.Equal(5, uiaSnapshotStructured.GetProperty("requestedDepth").GetInt32());
            Assert.Equal(128, uiaSnapshotStructured.GetProperty("requestedMaxNodes").GetInt32());
            string uiaArtifactPath = uiaSnapshotStructured.GetProperty("artifactPath").GetString()!;
            Assert.True(File.Exists(uiaArtifactPath), $"UIA snapshot artifact '{uiaArtifactPath}' was not created.");

            JsonElement uiaContent = uiaSnapshotResult.GetProperty("content");
            Assert.Equal(1, uiaContent.GetArrayLength());
            Assert.Equal("text", uiaContent[0].GetProperty("type").GetString());
            Assert.Contains("\"targetSource\":\"attached\"", uiaContent[0].GetProperty("text").GetString(), StringComparison.Ordinal);

            JsonElement uiaRoot = uiaSnapshotStructured.GetProperty("root");
            JsonElement smokeButton = AssertUiaNodeExists(uiaRoot, "button", "Run semantic smoke");
            Assert.Contains("invoke", smokeButton.GetProperty("patterns").EnumerateArray().Select(item => item.GetString()));
            JsonElement smokeCheckbox = AssertUiaNodeExists(uiaRoot, "check_box", "Remember semantic selection: on");
            Assert.Contains("toggle", smokeCheckbox.GetProperty("patterns").EnumerateArray().Select(item => item.GetString()));
            JsonElement smokeEdit = AssertUiaNodeExists(uiaRoot, "edit", "Smoke query input");
            string[] editPatterns = smokeEdit.GetProperty("patterns").EnumerateArray().Select(item => item.GetString()!).ToArray();
            Assert.True(editPatterns.Contains("value", StringComparer.Ordinal) || editPatterns.Contains("text", StringComparer.Ordinal));
            AssertUiaNodeExists(uiaRoot, "tree", "Smoke navigation tree");
            AssertUiaNodeExists(uiaRoot, "tree_item", "Workspace");
            AssertUiaNodeExists(uiaRoot, "tree_item", "Inbox");

            using JsonDocument captureResponse = await session.CallToolAsync(ToolNames.WindowsCapture, new { scope = "window" });
            JsonElement captureResult = captureResponse.RootElement.GetProperty("result");
            Assert.False(captureResult.GetProperty("isError").GetBoolean());

            JsonElement structuredContent = captureResult.GetProperty("structuredContent");
            Assert.Equal("window", structuredContent.GetProperty("scope").GetString());
            Assert.Equal(helperHwnd, structuredContent.GetProperty("hwnd").GetInt64());
            Assert.Equal("physical_pixels", structuredContent.GetProperty("coordinateSpace").GetString());
            Assert.True(structuredContent.GetProperty("effectiveDpi").GetInt32() >= 96);
            Assert.True(structuredContent.GetProperty("pixelWidth").GetInt32() > 0);
            Assert.True(structuredContent.GetProperty("pixelHeight").GetInt32() > 0);

            JsonElement content = captureResult.GetProperty("content");
            Assert.Equal(2, content.GetArrayLength());
            Assert.Equal("text", content[0].GetProperty("type").GetString());
            Assert.Equal("image", content[1].GetProperty("type").GetString());
            Assert.Equal("image/png", content[1].GetProperty("mimeType").GetString());
            Assert.False(string.IsNullOrWhiteSpace(content[1].GetProperty("data").GetString()));

            string artifactPath = structuredContent.GetProperty("artifactPath").GetString()!;
            Assert.True(File.Exists(artifactPath), $"Capture artifact '{artifactPath}' was not created.");

            Assert.True(MinimizeWindow(helperHwnd), "Smoke helper window did not accept minimize request.");
            Assert.True(await WaitUntilAsync(() => IsIconic(new IntPtr(helperHwnd))), "Smoke helper window did not become minimized in time.");

            using JsonDocument minimizedCaptureResponse = await session.CallToolAsync(ToolNames.WindowsCapture, new { scope = "window" });
            JsonElement minimizedCaptureResult = minimizedCaptureResponse.RootElement.GetProperty("result");
            Assert.True(minimizedCaptureResult.GetProperty("isError").GetBoolean());
            JsonElement minimizedPayload = minimizedCaptureResult.GetProperty("structuredContent");
            Assert.Contains("Свернутое окно", minimizedPayload.GetProperty("reason").GetString(), StringComparison.Ordinal);

            using JsonDocument activateResponse = await session.CallToolAsync(ToolNames.WindowsActivateWindow, new { });
            JsonElement activateResult = activateResponse.RootElement
                .GetProperty("result");
            JsonElement activateRoot = activateResult
                .GetProperty("structuredContent");
            string activateStatus = activateRoot.GetProperty("status").GetString()!;
            Assert.Contains(activateStatus, ["done", "ambiguous"]);
            Assert.Equal(activateStatus == "ambiguous", activateResult.GetProperty("isError").GetBoolean());
            Assert.True(activateRoot.GetProperty("wasMinimized").GetBoolean());
            Assert.Equal(helperHwnd, activateRoot.GetProperty("window").GetProperty("hwnd").GetInt64());
            Assert.Equal(activateStatus == "done", activateRoot.GetProperty("isForeground").GetBoolean());

            using JsonDocument helperCaptureResponse = await WaitForSuccessfulWindowCaptureAsync(session);
            JsonElement helperCaptureResult = helperCaptureResponse.RootElement.GetProperty("result");
            Assert.False(helperCaptureResult.GetProperty("isError").GetBoolean());
            JsonElement helperStructured = helperCaptureResult.GetProperty("structuredContent");
            Assert.Equal(helperHwnd, helperStructured.GetProperty("hwnd").GetInt64());

            process.StandardInput.Close();
            await WaitForExitAsync(process);
        }
        finally
        {
            if (!helperProcess.HasExited)
            {
                helperProcess.Kill(entireProcessTree: true);
                await helperProcess.WaitForExitAsync().WaitAsync(ProcessExitTimeout);
            }

            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(ProcessExitTimeout);
            }
        }
    }

    private static string GetToolTextPayload(JsonDocument response) =>
        response.RootElement
            .GetProperty("result")
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString()!;

    private static object CreateDeferredInvocationArguments(string toolName) =>
        toolName switch
        {
            ToolNames.WindowsClipboardGet => new { },
            ToolNames.WindowsClipboardSet => new { value = "smoke" },
            ToolNames.WindowsUiaAction => new { elementId = "root", action = "invoke" },
            _ => throw new InvalidOperationException($"Неизвестный deferred tool '{toolName}' для smoke invocation contract."),
        };

    private static async Task WaitForExitAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync().WaitAsync(ProcessExitTimeout);
        }
        catch (TimeoutException exception)
        {
            throw new TimeoutException(
                $"MCP integration smoke timed out while waiting for server process {process.Id} to exit.",
                exception);
        }
    }

    private static bool TryGetProcessDpiAwareness(Process process, out int awareness)
    {
        awareness = -1;
        if (process.HasExited)
        {
            return false;
        }

        int result = GetProcessDpiAwarenessNative(process.Handle, out int processAwareness);
        awareness = processAwareness;
        return result == 0;
    }

    private sealed class McpRequestSession(StreamReader reader, StreamWriter writer)
    {
        private int nextRequestId = 1;

        public async Task SendNotificationAsync(string method)
        {
            string json = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                method,
            });

            await writer.WriteLineAsync(json);
            await writer.FlushAsync();
        }

        public Task<JsonDocument> CallToolAsync(string toolName, object arguments) =>
            SendRequestAsync(
                "tools/call",
                new
                {
                    name = toolName,
                    arguments,
                },
                toolName);

        public async Task<JsonDocument> SendRequestAsync(string method, object parameters, string requestName)
        {
            int requestId = nextRequestId++;
            string json = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = requestId,
                method,
                @params = parameters,
            });

            await writer.WriteLineAsync(json);
            await writer.FlushAsync();
            return await ReadResponseAsync(requestName, requestId);
        }

        private async Task<JsonDocument> ReadResponseAsync(string requestName, int expectedId)
        {
            while (true)
            {
                string? line;
                try
                {
                    line = await reader.ReadLineAsync().WaitAsync(ResponseTimeout);
                }
                catch (TimeoutException exception)
                {
                    throw new TimeoutException(
                        $"MCP integration smoke timed out while waiting for response to '{requestName}'.",
                        exception);
                }

                Assert.False(line is null, "Сервер завершился до получения ответа.");

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                JsonDocument document = JsonDocument.Parse(line);
                JsonElement root = document.RootElement;
                if (!root.TryGetProperty("id", out JsonElement idElement))
                {
                    document.Dispose();
                    continue;
                }

                int actualId = idElement.GetInt32();
                if (actualId != expectedId)
                {
                    document.Dispose();
                    throw new InvalidDataException(
                        $"MCP integration smoke received response id '{actualId}' while waiting for '{requestName}' response id '{expectedId}'.");
                }

                return document;
            }
        }
    }

    private static Process StartServer(string? toolSurfaceProfile = null)
    {
        string arguments = $"\"{GetServerDllPath()}\"";
        if (!string.IsNullOrWhiteSpace(toolSurfaceProfile))
        {
            arguments += $" --tool-surface-profile {toolSurfaceProfile}";
        }

        ProcessStartInfo startInfo = new()
        {
            FileName = "dotnet",
            Arguments = arguments,
            WorkingDirectory = GetRepositoryRoot(),
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        if (string.Equals(toolSurfaceProfile, ToolSurfaceProfileValues.ComputerUseWin, StringComparison.Ordinal))
        {
            string repoRoot = GetRepositoryRoot();
            string uniqueName = Guid.NewGuid().ToString("N");
            startInfo.Environment["COMPUTER_USE_WIN_PLUGIN_ROOT"] = Path.Combine(repoRoot, "plugins", "computer-use-win");
            startInfo.Environment["COMPUTER_USE_WIN_APPROVAL_STORE"] = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "computer-use-win-approvals", $"{uniqueName}.json");
        }

        Process process = new() { StartInfo = startInfo };
        process.Start();
        return process;
    }

    private static Process StartHelperWindow(string? title = null, int? lifetimeMs = null)
    {
        string effectiveTitle = string.IsNullOrWhiteSpace(title) ? "Okno Smoke Helper" : title;
        List<string> arguments =
        [
            "--title",
            effectiveTitle,
        ];
        if (lifetimeMs is int helperLifetimeMs and > 0)
        {
            arguments.Add("--lifetime-ms");
            arguments.Add(helperLifetimeMs.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        ProcessStartInfo startInfo = new()
        {
            FileName = GetHelperWindowExePath(),
            Arguments = string.Join(
                " ",
                arguments.Select(static item => item.Contains(' ', StringComparison.Ordinal) ? $"\"{item}\"" : item)),
            WorkingDirectory = GetRepositoryRoot(),
            UseShellExecute = false,
        };

        Process process = new() { StartInfo = startInfo };
        process.Start();
        return process;
    }

    private static async Task StopHelperWindowAsync(Process helper)
    {
        if (helper.HasExited)
        {
            return;
        }

        helper.Kill(entireProcessTree: true);
        await helper.WaitForExitAsync().WaitAsync(ProcessExitTimeout);
    }

    private static async Task<long> WaitForMainWindowAsync(Process process)
    {
        try
        {
            _ = process.WaitForInputIdle((int)HelperWindowTimeout.TotalMilliseconds);
        }
        catch (InvalidOperationException)
        {
        }

        DateTime deadlineUtc = DateTime.UtcNow + HelperWindowTimeout;
        while (DateTime.UtcNow < deadlineUtc)
        {
            process.Refresh();
            if (process.MainWindowHandle != IntPtr.Zero)
            {
                return process.MainWindowHandle.ToInt64();
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("Smoke helper window did not expose a main window handle in time.");
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> predicate)
    {
        DateTime deadlineUtc = DateTime.UtcNow + HelperWindowTimeout;
        while (DateTime.UtcNow < deadlineUtc)
        {
            if (predicate())
            {
                return true;
            }

            await Task.Delay(100);
        }

        return predicate();
    }

    private static async Task<JsonDocument> WaitForVisibleHelperWindowAsync(
        McpRequestSession session,
        long helperHwnd)
    {
        DateTime deadlineUtc = DateTime.UtcNow + HelperWindowTimeout;
        while (DateTime.UtcNow < deadlineUtc)
        {
            using JsonDocument windowsResponse = await CallToolAsync(
                session,
                ToolNames.WindowsListWindows,
                new { includeInvisible = false });
            using JsonDocument windowsPayload = JsonDocument.Parse(GetToolTextPayload(windowsResponse));
            JsonElement root = windowsPayload.RootElement;
            if (root.GetProperty("windows").EnumerateArray().Any(window => window.GetProperty("hwnd").GetInt64() == helperHwnd))
            {
                return JsonDocument.Parse(root.GetRawText());
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("Smoke helper window did not appear in visible windows.list_windows inventory in time.");
    }

    private static async Task<JsonDocument> WaitForSuccessfulWindowCaptureAsync(
        McpRequestSession session)
    {
        DateTime deadlineUtc = DateTime.UtcNow + HelperWindowTimeout;
        string? lastReason = null;

        while (DateTime.UtcNow < deadlineUtc)
        {
            using JsonDocument captureResponse = await CallToolAsync(
                session,
                ToolNames.WindowsCapture,
                new { scope = "window" });
            JsonElement result = captureResponse.RootElement.GetProperty("result");
            if (!result.GetProperty("isError").GetBoolean())
            {
                return JsonDocument.Parse(captureResponse.RootElement.GetRawText());
            }

            if (result.TryGetProperty("structuredContent", out JsonElement structuredContent)
                && structuredContent.TryGetProperty("reason", out JsonElement reasonElement))
            {
                lastReason = reasonElement.GetString();
            }

            await Task.Delay(100);
        }

        if (string.IsNullOrWhiteSpace(lastReason))
        {
            throw new TimeoutException("Helper window did not become capturable after activation in time.");
        }

        throw new TimeoutException($"Helper window did not become capturable after activation in time. Last capture reason: {lastReason}");
    }

    private static async Task<JsonDocument> WaitForSemanticUiaSnapshotAsync(McpRequestSession session)
    {
        DateTime deadlineUtc = DateTime.UtcNow + HelperWindowTimeout;
        string? lastStatus = null;
        string? lastArtifactPath = null;

        while (DateTime.UtcNow < deadlineUtc)
        {
            using JsonDocument snapshotResponse = await CallToolAsync(
                session,
                ToolNames.WindowsUiaSnapshot,
                new
                {
                    depth = 5,
                    maxNodes = 128,
                });
            JsonElement result = snapshotResponse.RootElement.GetProperty("result");
            JsonElement structured = result.GetProperty("structuredContent");

            if (!result.GetProperty("isError").GetBoolean()
                && structured.TryGetProperty("root", out JsonElement root)
                && TryFindUiaNode(root, "button", "Run semantic smoke", out _)
                && TryFindUiaNode(root, "check_box", "Remember semantic selection: on", out _)
                && TryFindUiaNode(root, "edit", "Smoke query input", out _)
                && TryFindUiaNode(root, "tree_item", "Inbox", out _))
            {
                return JsonDocument.Parse(snapshotResponse.RootElement.GetRawText());
            }

            if (structured.TryGetProperty("status", out JsonElement statusElement))
            {
                lastStatus = statusElement.GetString();
            }

            if (structured.TryGetProperty("artifactPath", out JsonElement artifactElement))
            {
                lastArtifactPath = artifactElement.GetString();
            }

            await Task.Delay(100);
        }

        throw new TimeoutException(
            $"UIA semantic subtree did not materialize in time. Last status: {lastStatus ?? "<unknown>"}. Last artifact: {lastArtifactPath ?? "<none>"}");
    }

    private static JsonElement AssertUiaNodeExists(JsonElement root, string controlType, string name)
    {
        Assert.True(
            TryFindUiaNode(root, controlType, name, out JsonElement match),
            $"UIA snapshot did not contain expected node '{controlType}:{name}'.");
        return match;
    }

    private static bool TryFindUiaNode(JsonElement node, string controlType, string name, out JsonElement match)
    {
        if (node.ValueKind == JsonValueKind.Object
            && node.TryGetProperty("controlType", out JsonElement controlTypeElement)
            && string.Equals(controlTypeElement.GetString(), controlType, StringComparison.Ordinal)
            && node.TryGetProperty("name", out JsonElement nameElement)
            && string.Equals(nameElement.GetString(), name, StringComparison.Ordinal))
        {
            match = node;
            return true;
        }

        if (node.ValueKind == JsonValueKind.Object
            && node.TryGetProperty("children", out JsonElement children)
            && children.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement child in children.EnumerateArray())
            {
                if (TryFindUiaNode(child, controlType, name, out match))
                {
                    return true;
                }
            }
        }

        match = default;
        return false;
    }

    private static void AssertHealthReadinessShape(JsonElement healthRoot)
    {
        JsonElement readiness = healthRoot.GetProperty("readiness");
        Assert.Equal(ExpectedHealthDomains, readiness.GetProperty("domains").EnumerateArray().Select(item => item.GetProperty("domain").GetString()).ToArray());
        Assert.Equal(ExpectedHealthCapabilities, readiness.GetProperty("capabilities").EnumerateArray().Select(item => item.GetProperty("capability").GetString()).ToArray());
        Assert.False(string.IsNullOrWhiteSpace(readiness.GetProperty("capturedAtUtc").GetString()));

        foreach (JsonElement domain in readiness.GetProperty("domains").EnumerateArray())
        {
            string? status = domain.GetProperty("status").GetString();
            Assert.NotNull(status);
            Assert.Contains(status, AllowedGuardStatuses);
            AssertReasonList(domain.GetProperty("reasons"), domain.GetProperty("domain").GetString()!, ExpectedHealthDomains);
        }

        foreach (JsonElement capability in readiness.GetProperty("capabilities").EnumerateArray())
        {
            string? status = capability.GetProperty("status").GetString();
            Assert.NotNull(status);
            Assert.Contains(status, AllowedGuardStatuses);
            AssertReasonList(capability.GetProperty("reasons"), capability.GetProperty("capability").GetString()!, ExpectedHealthCapabilities);
        }

        foreach (JsonElement blockedCapability in healthRoot.GetProperty("blockedCapabilities").EnumerateArray())
        {
            Assert.Equal(GuardStatusValues.Blocked, blockedCapability.GetProperty("status").GetString());
            string capabilityName = blockedCapability.GetProperty("capability").GetString()!;
            Assert.Contains(capabilityName, ExpectedHealthCapabilities);
            AssertReasonList(blockedCapability.GetProperty("reasons"), capabilityName, ExpectedHealthCapabilities);
        }

        AssertBlockedCapabilityProjection(readiness, healthRoot.GetProperty("blockedCapabilities"));
        AssertWarningProjection(readiness, healthRoot.GetProperty("warnings"));
    }

    private static void AssertHealthTopLevelContract(JsonElement healthRoot)
    {
        Assert.False(string.IsNullOrWhiteSpace(healthRoot.GetProperty("version").GetString()));
        Assert.Equal("stdio", healthRoot.GetProperty("transport").GetString());
        Assert.Equal(AuditConstants.SchemaVersion, healthRoot.GetProperty("auditSchemaVersion").GetString());
        Assert.False(string.IsNullOrWhiteSpace(healthRoot.GetProperty("runId").GetString()));

        string artifactsDirectory = healthRoot.GetProperty("artifactsDirectory").GetString()!;
        Assert.False(string.IsNullOrWhiteSpace(artifactsDirectory));
        Assert.True(Directory.Exists(artifactsDirectory), $"Health artifacts directory '{artifactsDirectory}' was not created.");

        Assert.True(healthRoot.GetProperty("activeMonitorCount").GetInt32() > 0);
        AssertDisplayIdentityContract(healthRoot.GetProperty("displayIdentity"));
        Assert.False(healthRoot.TryGetProperty("artifactPath", out _));

        Assert.Equal(
            ToolContractManifest.ImplementedNames,
            healthRoot.GetProperty("implementedTools").EnumerateArray().Select(item => item.GetString()!).ToArray());

        Dictionary<string, string> deferredTools = healthRoot.GetProperty("deferredTools")
            .EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.GetString()!, StringComparer.Ordinal);
        Assert.Equal(ToolContractManifest.DeferredPhaseMap.Count, deferredTools.Count);
        foreach ((string toolName, string plannedPhase) in ToolContractManifest.DeferredPhaseMap)
        {
            Assert.True(deferredTools.TryGetValue(toolName, out string? actualPhase), $"Health deferredTools is missing '{toolName}'.");
            Assert.Equal(plannedPhase, actualPhase);
        }
    }

    private static void AssertBlockedCapabilityProjection(JsonElement readiness, JsonElement blockedCapabilities)
    {
        JsonElement[] expected = [.. readiness.GetProperty("capabilities").EnumerateArray()
            .Where(item => string.Equals(item.GetProperty("status").GetString(), GuardStatusValues.Blocked, StringComparison.Ordinal))];
        JsonElement[] actual = [.. blockedCapabilities.EnumerateArray()];

        Assert.Equal(
            expected.Select(item => item.GetProperty("capability").GetString()).ToArray(),
            actual.Select(item => item.GetProperty("capability").GetString()).ToArray());

        foreach (JsonElement expectedCapability in expected)
        {
            string capabilityName = expectedCapability.GetProperty("capability").GetString()!;
            JsonElement actualCapability = actual.Single(item => item.GetProperty("capability").GetString() == capabilityName);
            Assert.Equal(GuardStatusValues.Blocked, actualCapability.GetProperty("status").GetString());
            Assert.Equal(
                GetReasonSignatures(expectedCapability.GetProperty("reasons")),
                GetReasonSignatures(actualCapability.GetProperty("reasons")));
        }
    }

    private static void AssertWarningProjection(JsonElement readiness, JsonElement warnings)
    {
        JsonElement[] expected = [..
            readiness.GetProperty("domains").EnumerateArray().SelectMany(item => GetWarningReasons(item.GetProperty("reasons"))),
            .. readiness.GetProperty("capabilities").EnumerateArray()
                .Where(item => !string.Equals(item.GetProperty("status").GetString(), GuardStatusValues.Blocked, StringComparison.Ordinal))
                .SelectMany(item => GetWarningReasons(item.GetProperty("reasons")))];

        JsonElement[] actual = [.. warnings.EnumerateArray()];
        Assert.Equal(GetReasonSignatures(expected), GetReasonSignatures(actual));
    }

    private static void AssertDisplayIdentityContract(JsonElement displayIdentity)
    {
        string identityMode = displayIdentity.GetProperty("identityMode").GetString()!;
        Assert.Contains(identityMode, AllowedDisplayIdentityModes);
        Assert.False(string.IsNullOrWhiteSpace(displayIdentity.GetProperty("messageHuman").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(displayIdentity.GetProperty("capturedAtUtc").GetString()));

        bool hasFailedStage = displayIdentity.TryGetProperty("failedStage", out JsonElement failedStageElement)
            && failedStageElement.ValueKind != JsonValueKind.Null;
        bool hasErrorCode = displayIdentity.TryGetProperty("errorCode", out JsonElement errorCodeElement)
            && errorCodeElement.ValueKind != JsonValueKind.Null;
        bool hasErrorName = displayIdentity.TryGetProperty("errorName", out JsonElement errorNameElement)
            && errorNameElement.ValueKind != JsonValueKind.Null;

        if (!hasFailedStage)
        {
            Assert.False(hasErrorCode);
            Assert.False(hasErrorName);
            return;
        }

        string failedStage = failedStageElement.GetString()!;
        Assert.Contains(failedStage, AllowedDisplayIdentityFailureStages);
        if (string.Equals(failedStage, DisplayIdentityFailureStageValues.CoverageGap, StringComparison.Ordinal))
        {
            Assert.False(hasErrorCode);
            Assert.False(hasErrorName);
            return;
        }

        Assert.True(hasErrorCode, $"Display identity stage '{failedStage}' must publish errorCode.");
        Assert.True(hasErrorName, $"Display identity stage '{failedStage}' must publish errorName.");
        if (hasErrorName)
        {
            Assert.False(string.IsNullOrWhiteSpace(errorNameElement.GetString()));
        }
    }

    private static string[] GetReasonSignatures(JsonElement reasons) =>
        [.. reasons.EnumerateArray().Select(GetReasonSignature)];

    private static string[] GetReasonSignatures(IEnumerable<JsonElement> reasons) =>
        [.. reasons.Select(GetReasonSignature)];

    private static IEnumerable<JsonElement> GetWarningReasons(JsonElement reasons) =>
        reasons.EnumerateArray()
            .Where(item => string.Equals(item.GetProperty("severity").GetString(), GuardSeverityValues.Warning, StringComparison.Ordinal));

    private static void AssertSchemaTypeContains(JsonElement typeElement, string expectedType)
    {
        if (typeElement.ValueKind == JsonValueKind.String)
        {
            Assert.Equal(expectedType, typeElement.GetString());
            return;
        }

        Assert.Contains(expectedType, typeElement.EnumerateArray().Select(item => item.GetString()));
    }

    private static JsonElement FindActionSchemaBranch(JsonElement branches, string actionType)
    {
        foreach (JsonElement branch in branches.EnumerateArray())
        {
            JsonElement typeSchema = branch.GetProperty("properties").GetProperty("type");
            if (typeSchema.GetProperty("enum").EnumerateArray().Any(item => string.Equals(item.GetString(), actionType, StringComparison.Ordinal)))
            {
                return branch;
            }
        }

        throw new InvalidOperationException($"Schema branch for action type '{actionType}' was not found.");
    }

    private static void AssertSchemaRequiredContains(JsonElement schema, params string[] requiredFields)
    {
        string[] actual = schema.GetProperty("required")
            .EnumerateArray()
            .Select(item => item.GetString()!)
            .ToArray();

        foreach (string requiredField in requiredFields)
        {
            Assert.Contains(requiredField, actual);
        }
    }

    private static void AssertSchemaPropertyDoesNotAllowNull(JsonElement schema, string propertyName)
    {
        JsonElement propertySchema = schema.GetProperty("properties").GetProperty(propertyName);
        Assert.False(SchemaTypeAllowsNull(propertySchema.GetProperty("type")), $"Schema property '{propertyName}' must not allow null.");
        if (propertySchema.TryGetProperty("enum", out JsonElement enumValues))
        {
            Assert.DoesNotContain(enumValues.EnumerateArray(), item => item.ValueKind == JsonValueKind.Null);
        }
    }

    private static void AssertSchemaPropertyAllowsNull(JsonElement schema, string propertyName)
    {
        JsonElement propertySchema = schema.GetProperty("properties").GetProperty(propertyName);
        Assert.True(SchemaTypeAllowsNull(propertySchema.GetProperty("type")), $"Schema property '{propertyName}' must allow null.");
    }

    private static void AssertSchemaIntegerPropertyHasMinimum(JsonElement schema, string propertyName, int minimum)
    {
        JsonElement propertySchema = schema.GetProperty("properties").GetProperty(propertyName);
        Assert.Equal(minimum, propertySchema.GetProperty("minimum").GetInt32());
    }

    private static void AssertSchemaIntegerPropertyRejectsConst(JsonElement schema, string propertyName, int constValue)
    {
        JsonElement propertySchema = schema.GetProperty("properties").GetProperty(propertyName);
        Assert.Equal(constValue, propertySchema.GetProperty("not").GetProperty("const").GetInt32());
    }

    private static void AssertSchemaStringPropertyRejectsWhitespaceOnly(JsonElement schema, string propertyName)
    {
        JsonElement propertySchema = schema.GetProperty("properties").GetProperty(propertyName);
        Assert.Equal(1, propertySchema.GetProperty("minLength").GetInt32());
        Assert.Equal(@"\S", propertySchema.GetProperty("pattern").GetString());
    }

    private static bool SchemaTypeAllowsNull(JsonElement typeElement)
    {
        if (typeElement.ValueKind == JsonValueKind.String)
        {
            return string.Equals(typeElement.GetString(), "null", StringComparison.Ordinal);
        }

        return typeElement.EnumerateArray().Any(item => string.Equals(item.GetString(), "null", StringComparison.Ordinal));
    }

    private static string GetReasonSignature(JsonElement reason) =>
        string.Join(
            "\u001F",
            reason.GetProperty("source").GetString(),
            reason.GetProperty("code").GetString(),
            reason.GetProperty("severity").GetString(),
            reason.GetProperty("messageHuman").GetString());

    private static void AssertReasonList(JsonElement reasons, string expectedSource, IEnumerable<string> allowedSources)
    {
        JsonElement[] items = [.. reasons.EnumerateArray()];
        Assert.NotEmpty(items);

        foreach (JsonElement reason in items)
        {
            Assert.False(string.IsNullOrWhiteSpace(reason.GetProperty("code").GetString()));
            Assert.Contains(reason.GetProperty("severity").GetString(), new[]
            {
                GuardSeverityValues.Info,
                GuardSeverityValues.Warning,
                GuardSeverityValues.Blocked,
            });
            Assert.Equal(expectedSource, reason.GetProperty("source").GetString());
            Assert.Contains(reason.GetProperty("source").GetString(), allowedSources);
            Assert.False(string.IsNullOrWhiteSpace(reason.GetProperty("messageHuman").GetString()));
        }
    }

    private static Task<JsonDocument> CallToolAsync(
        McpRequestSession session,
        string toolName,
        object arguments) =>
        session.CallToolAsync(toolName, arguments);

    private static bool MinimizeWindow(long hwnd) =>
        ShowWindowAsync(new IntPtr(hwnd), SwMinimize);

    private static string GetServerDllPath() => RuntimeBundle.Value.ServerDll;

    private static string GetHelperWindowExePath() => RuntimeBundle.Value.HelperExe;

    private static RuntimeBundlePaths ResolveRuntimeBundlePaths()
    {
        string repoRoot = GetRepositoryRoot();
        string resolverScriptPath = Path.Combine(repoRoot, "scripts", "codex", "resolve-okno-test-bundle.ps1");

        ProcessStartInfo startInfo = new()
        {
            FileName = "powershell",
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(resolverScriptPath);
        startInfo.ArgumentList.Add("-RepoRoot");
        startInfo.ArgumentList.Add(repoRoot);
        startInfo.ArgumentList.Add("-AssemblyBaseDirectory");
        startInfo.ArgumentList.Add(AppContext.BaseDirectory);

        using Process process = new() { StartInfo = startInfo };
        process.Start();

        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Не удалось resolve staged test bundle через '{resolverScriptPath}'. ExitCode={process.ExitCode}. stderr='{stderr.Trim()}', stdout='{stdout.Trim()}'.");
        }

        if (string.IsNullOrWhiteSpace(stdout))
        {
            throw new InvalidOperationException(
                $"Resolver script '{resolverScriptPath}' completed without payload.");
        }

        using JsonDocument resolution = JsonDocument.Parse(stdout);
        JsonElement root = resolution.RootElement;
        string? manifestPath = null;
        if (root.TryGetProperty("manifestPath", out JsonElement manifestPathElement) &&
            manifestPathElement.ValueKind == JsonValueKind.String)
        {
            manifestPath = manifestPathElement.GetString();
        }

        string serverDll = RequireResolverPath(root, "serverDll", resolverScriptPath);
        string helperExe = RequireResolverPath(root, "helperExe", resolverScriptPath);

        return new RuntimeBundlePaths(
            manifestPath,
            serverDll,
            helperExe);
    }

    private static string RequireResolverPath(JsonElement payload, string propertyName, string resolverScriptPath)
    {
        if (!payload.TryGetProperty(propertyName, out JsonElement propertyElement) ||
            propertyElement.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException(
                $"Resolver script '{resolverScriptPath}' did not provide '{propertyName}'.");
        }

        string? path = propertyElement.GetString();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new InvalidOperationException(
                $"Resolved path '{path ?? "<empty>"}' for '{propertyName}' from '{resolverScriptPath}' was not found.");
        }

        return Path.GetFullPath(path);
    }

    private static string GetRepositoryRoot()
    {
        string? root = Environment.GetEnvironmentVariable("WINBRIDGE_REPO_ROOT");
        if (!string.IsNullOrWhiteSpace(root))
        {
            return root;
        }

        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "WinBridge.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Не удалось вычислить корень репозитория.");
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr hwnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hwnd);

    [DllImport("shcore.dll", EntryPoint = "GetProcessDpiAwareness")]
    private static extern int GetProcessDpiAwarenessNative(IntPtr processHandle, out int awareness);

    private sealed record RuntimeBundlePaths(
        string? ManifestPath,
        string ServerDll,
        string HelperExe);
}
