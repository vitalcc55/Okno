using System.Diagnostics;
using System.Text;
using System.Text.Json;
using WinBridge.Runtime.Tooling;

namespace WinBridge.Server.IntegrationTests;

public sealed class McpProtocolSmokeTests
{
    private static readonly string[] AttachSuccessStates = { "done", "already_attached" };
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ProcessExitTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task InitializeAndValidateCoreOknoToolsThroughStdio()
    {
        using Process process = StartServer();

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

            using JsonDocument healthResponse = await CallToolAsync(reader, writer, 3, ToolNames.OknoHealth, new { });
            string healthText = healthResponse.RootElement
                .GetProperty("result")
                .GetProperty("content")[0]
                .GetProperty("text")
                .GetString()!;
            Assert.Contains("\"service\":\"Okno\"", healthText, StringComparison.Ordinal);

            using JsonDocument windowsResponse = await CallToolAsync(reader, writer, 4, ToolNames.WindowsListWindows, new { includeInvisible = false });
            string windowsText = windowsResponse.RootElement
                .GetProperty("result")
                .GetProperty("content")[0]
                .GetProperty("text")
                .GetString()!;
            using JsonDocument windowsPayload = JsonDocument.Parse(windowsText);
            JsonElement windowsRoot = windowsPayload.RootElement;
            int count = windowsRoot.GetProperty("count").GetInt32();
            Assert.True(count > 0, "Smoke contract requires at least one visible window.");

            long hwnd = windowsRoot.GetProperty("windows")[0].GetProperty("hwnd").GetInt64();

            using JsonDocument attachResponse = await CallToolAsync(reader, writer, 5, ToolNames.WindowsAttachWindow, new { hwnd });
            string attachText = attachResponse.RootElement
                .GetProperty("result")
                .GetProperty("content")[0]
                .GetProperty("text")
                .GetString()!;
            using JsonDocument attachPayload = JsonDocument.Parse(attachText);
            JsonElement attachRoot = attachPayload.RootElement;
            string attachStatus = attachRoot.GetProperty("status").GetString()!;
            Assert.Contains(attachStatus, AttachSuccessStates);
            Assert.Equal(hwnd, attachRoot.GetProperty("attachedWindow").GetProperty("window").GetProperty("hwnd").GetInt64());

            using JsonDocument sessionResponse = await CallToolAsync(reader, writer, 6, ToolNames.OknoSessionState, new { });
            string sessionText = sessionResponse.RootElement
                .GetProperty("result")
                .GetProperty("content")[0]
                .GetProperty("text")
                .GetString()!;
            using JsonDocument sessionPayload = JsonDocument.Parse(sessionText);
            JsonElement sessionRoot = sessionPayload.RootElement;
            Assert.Equal("window", sessionRoot.GetProperty("mode").GetString());
            Assert.Equal(hwnd, sessionRoot.GetProperty("attachedWindow").GetProperty("window").GetProperty("hwnd").GetInt64());

            process.StandardInput.Close();
            await WaitForExitAsync(process);
        }
        finally
        {
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

    private static string GetServerDllPath() =>
        Path.Combine(
            GetRepositoryRoot(),
            "src",
            "WinBridge.Server",
            "bin",
            "Debug",
            "net8.0-windows10.0.19041.0",
            "Okno.Server.dll");

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
}
