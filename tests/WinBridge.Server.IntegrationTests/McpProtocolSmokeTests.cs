using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using WinBridge.Runtime.Tooling;

namespace WinBridge.Server.IntegrationTests;

public sealed class McpProtocolSmokeTests
{
    private static readonly string[] AttachSuccessStates = { "done", "already_attached" };
    private const int ProcessPerMonitorDpiAware = 2;
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ProcessExitTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan HelperWindowTimeout = TimeSpan.FromSeconds(10);
    private const int SwMinimize = 6;

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

            using JsonDocument healthResponse = await session.CallToolAsync(ToolNames.OknoHealth, new { });
            using JsonDocument healthPayload = JsonDocument.Parse(GetToolTextPayload(healthResponse));
            JsonElement healthRoot = healthPayload.RootElement;
            Assert.Equal("Okno", healthRoot.GetProperty("service").GetString());
            Assert.True(healthRoot.GetProperty("activeMonitorCount").GetInt32() > 0);
            Assert.False(string.IsNullOrWhiteSpace(healthRoot.GetProperty("displayIdentity").GetProperty("identityMode").GetString()));

            using JsonDocument monitorsResponse = await session.CallToolAsync(ToolNames.WindowsListMonitors, new { });
            using JsonDocument monitorsPayload = JsonDocument.Parse(GetToolTextPayload(monitorsResponse));
            JsonElement monitorsRoot = monitorsPayload.RootElement;
            int monitorCount = monitorsRoot.GetProperty("count").GetInt32();
            Assert.True(monitorCount > 0, "Smoke contract requires at least one active monitor.");
            Assert.False(string.IsNullOrWhiteSpace(monitorsRoot.GetProperty("diagnostics").GetProperty("identityMode").GetString()));
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
