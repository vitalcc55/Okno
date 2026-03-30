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
    private const int SwMinimize = 6;

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
                    protocolVersion = "2025-06-18",
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
    public async Task OknoContractPublishesDeferredExecutionPolicyWithSnakeCaseDescriptorFields()
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
                    protocolVersion = "2025-06-18",
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
                .GetProperty("deferredTools")
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
            Assert.Equal("roadmap stage 5", inputDescriptor.GetProperty("planned_phase").GetString());
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
                    protocolVersion = "2025-06-18",
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
                    protocolVersion = "2025-06-18",
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
            JsonElement smokeCheckbox = AssertUiaNodeExists(uiaRoot, "check_box", "Remember semantic selection");
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

    private static Process StartServer()
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "dotnet",
            Arguments = $"\"{GetServerDllPath()}\"",
            WorkingDirectory = GetRepositoryRoot(),
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        Process process = new() { StartInfo = startInfo };
        process.Start();
        return process;
    }

    private static Process StartHelperWindow()
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = GetHelperWindowExePath(),
            Arguments = "--title \"Okno Smoke Helper\"",
            WorkingDirectory = GetRepositoryRoot(),
            UseShellExecute = false,
        };

        Process process = new() { StartInfo = startInfo };
        process.Start();
        return process;
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
                && TryFindUiaNode(root, "check_box", "Remember semantic selection", out _)
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

    private static string GetServerDllPath() =>
        Path.Combine(
            GetRepositoryRoot(),
            "src",
            "WinBridge.Server",
            "bin",
            "Debug",
            "net8.0-windows10.0.19041.0",
            "Okno.Server.dll");

    private static string GetHelperWindowExePath() =>
        Path.Combine(
            GetRepositoryRoot(),
            "tests",
            "WinBridge.SmokeWindowHost",
            "bin",
            "Debug",
            "net8.0-windows10.0.19041.0",
            "WinBridge.SmokeWindowHost.exe");

    private static string GetRepositoryRoot()
    {
        string? root = Environment.GetEnvironmentVariable("WINBRIDGE_REPO_ROOT");
        if (!string.IsNullOrWhiteSpace(root))
        {
            return root;
        }

        string current = AppContext.BaseDirectory;
        for (int i = 0; i < 6; i++)
        {
            current = Directory.GetParent(current)?.FullName
                ?? throw new InvalidOperationException("Не удалось вычислить корень репозитория.");
        }

        return current;
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr hwnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hwnd);

    [DllImport("shcore.dll", EntryPoint = "GetProcessDpiAwareness")]
    private static extern int GetProcessDpiAwarenessNative(IntPtr processHandle, out int awareness);
}
