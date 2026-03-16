using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using WinBridge.Runtime.Tooling;

namespace WinBridge.Server.IntegrationTests;

public sealed class McpProtocolSmokeTests
{
    private static readonly string[] AttachSuccessStates = { "done", "already_attached" };
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

        try
        {
            await SendAsync(
                writer,
                new
                {
                    jsonrpc = "2.0",
                    id = 1,
                    method = "initialize",
                    @params = new
                    {
                        protocolVersion = "2025-06-18",
                        capabilities = new { },
                        clientInfo = new
                        {
                            name = "Okno.IntegrationTests",
                            version = "0.1.0",
                        },
                    },
                });

            using JsonDocument initializeResponse = await ReadResponseAsync(reader, "initialize");
            JsonElement initializeRoot = initializeResponse.RootElement;
            Assert.Equal(1, initializeRoot.GetProperty("id").GetInt32());
            Assert.True(initializeRoot.TryGetProperty("result", out JsonElement initializeResult));
            Assert.True(initializeResult.TryGetProperty("serverInfo", out JsonElement serverInfo));
            Assert.Equal("Okno.Server", serverInfo.GetProperty("name").GetString());

            await SendAsync(
                writer,
                new
                {
                    jsonrpc = "2.0",
                    method = "notifications/initialized",
                });

            await SendAsync(
                writer,
                new
                {
                    jsonrpc = "2.0",
                    id = 2,
                    method = "tools/list",
                    @params = new { },
                });

            using JsonDocument toolsResponse = await ReadResponseAsync(reader, "tools/list");
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
            JsonElement captureAnnotations = captureDescriptor.GetProperty("annotations");
            Assert.False(captureAnnotations.GetProperty("readOnlyHint").GetBoolean());
            Assert.False(captureAnnotations.GetProperty("idempotentHint").GetBoolean());
            Assert.False(captureAnnotations.GetProperty("destructiveHint").GetBoolean());
            Assert.True(captureAnnotations.GetProperty("openWorldHint").GetBoolean());

            using JsonDocument healthResponse = await CallToolAsync(reader, writer, 3, ToolNames.OknoHealth, new { });
            string healthText = GetToolTextPayload(healthResponse);
            Assert.Contains("\"service\":\"Okno\"", healthText, StringComparison.Ordinal);

            using JsonDocument monitorsResponse = await CallToolAsync(reader, writer, 4, ToolNames.WindowsListMonitors, new { });
            using JsonDocument monitorsPayload = JsonDocument.Parse(GetToolTextPayload(monitorsResponse));
            JsonElement monitorsRoot = monitorsPayload.RootElement;
            int monitorCount = monitorsRoot.GetProperty("count").GetInt32();
            Assert.True(monitorCount > 0, "Smoke contract requires at least one active monitor.");
            string primaryMonitorId = monitorsRoot.GetProperty("monitors")[0].GetProperty("monitorId").GetString()!;
            long helperHwnd = await WaitForMainWindowAsync(helperProcess);

            using JsonDocument windowsResponse = await CallToolAsync(reader, writer, 5, ToolNames.WindowsListWindows, new { includeInvisible = false });
            using JsonDocument windowsPayload = JsonDocument.Parse(GetToolTextPayload(windowsResponse));
            JsonElement windowsRoot = windowsPayload.RootElement;
            int count = windowsRoot.GetProperty("count").GetInt32();
            Assert.True(count > 0, "Smoke contract requires at least one visible window.");
            Assert.Contains(
                windowsRoot.GetProperty("windows").EnumerateArray().Select(window => window.GetProperty("hwnd").GetInt64()),
                hwnd => hwnd == helperHwnd);

            using JsonDocument desktopCaptureResponse = await CallToolAsync(
                reader,
                writer,
                6,
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

            using JsonDocument attachResponse = await CallToolAsync(reader, writer, 7, ToolNames.WindowsAttachWindow, new { hwnd = helperHwnd });
            using JsonDocument attachPayload = JsonDocument.Parse(GetToolTextPayload(attachResponse));
            JsonElement attachRoot = attachPayload.RootElement;
            string attachStatus = attachRoot.GetProperty("status").GetString()!;
            Assert.Contains(attachStatus, AttachSuccessStates);

            using JsonDocument sessionResponse = await CallToolAsync(reader, writer, 8, ToolNames.OknoSessionState, new { });
            using JsonDocument sessionPayload = JsonDocument.Parse(GetToolTextPayload(sessionResponse));
            JsonElement sessionRoot = sessionPayload.RootElement;
            Assert.Equal("window", sessionRoot.GetProperty("mode").GetString());
            Assert.Equal(helperHwnd, sessionRoot.GetProperty("attachedWindow").GetProperty("window").GetProperty("hwnd").GetInt64());

            using JsonDocument captureResponse = await CallToolAsync(reader, writer, 9, ToolNames.WindowsCapture, new { scope = "window" });
            JsonElement captureResult = captureResponse.RootElement.GetProperty("result");
            Assert.False(captureResult.GetProperty("isError").GetBoolean());

            JsonElement structuredContent = captureResult.GetProperty("structuredContent");
            Assert.Equal("window", structuredContent.GetProperty("scope").GetString());
            Assert.Equal(helperHwnd, structuredContent.GetProperty("hwnd").GetInt64());
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

            using JsonDocument activateResponse = await CallToolAsync(reader, writer, 20, ToolNames.WindowsActivateWindow, new { });
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

            using JsonDocument helperCaptureResponse = await CallToolAsync(
                reader,
                writer,
                21,
                ToolNames.WindowsCapture,
                new
                {
                    scope = "window",
                    hwnd = helperHwnd,
                });
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

    private static async Task SendAsync(StreamWriter writer, object message)
    {
        string json = JsonSerializer.Serialize(message);
        await writer.WriteLineAsync(json);
        await writer.FlushAsync();
    }

    private static async Task<JsonDocument> CallToolAsync(
        StreamReader reader,
        StreamWriter writer,
        int id,
        string toolName,
        object arguments)
    {
        await SendAsync(
            writer,
            new
            {
                jsonrpc = "2.0",
                id,
                method = "tools/call",
                @params = new
                {
                    name = toolName,
                    arguments,
                },
            });

        return await ReadResponseAsync(reader, toolName);
    }

    private static string GetToolTextPayload(JsonDocument response) =>
        response.RootElement
            .GetProperty("result")
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString()!;

    private static async Task<JsonDocument> ReadResponseAsync(StreamReader reader, string requestName)
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
            if (root.TryGetProperty("id", out _))
            {
                return document;
            }

            document.Dispose();
        }
    }

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
}
