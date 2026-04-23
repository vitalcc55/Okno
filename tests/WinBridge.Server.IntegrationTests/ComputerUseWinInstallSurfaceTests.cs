using System.Diagnostics;
using System.Text;
using System.Text.Json;
using WinBridge.Runtime.Tooling;

namespace WinBridge.Server.IntegrationTests;

public sealed class ComputerUseWinInstallSurfaceTests
{
    [Fact]
    public void PublishComputerUseWinPluginCreatesSelfContainedRuntimeBundle()
    {
        string repoRoot = GetRepositoryRoot();
        string scriptPath = Path.Combine(repoRoot, "scripts", "codex", "publish-computer-use-win-plugin.ps1");
        string runtimeRoot = Path.Combine(repoRoot, "plugins", "computer-use-win", "runtime", "win-x64");

        DeleteDirectoryIfExists(runtimeRoot);

        ScriptInvocationResult result = InvokePowerShellScript(
            scriptPath,
            repoRoot,
            _ => { });

        Assert.True(
            result.ExitCode == 0,
            $"Publish script failed. ExitCode={result.ExitCode}. stderr='{result.Stderr.Trim()}', stdout='{result.Stdout.Trim()}'.");
        Assert.True(File.Exists(Path.Combine(runtimeRoot, "Okno.Server.exe")));
    }

    [Fact]
    public void PublishComputerUseWinPluginRestoresPreviousRuntimeWhenPromoteFails()
    {
        string repoRoot = GetRepositoryRoot();
        string scriptPath = Path.Combine(repoRoot, "scripts", "codex", "publish-computer-use-win-plugin.ps1");
        string runtimeRoot = Path.Combine(repoRoot, "plugins", "computer-use-win", "runtime", "win-x64");
        string backupRoot = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "computer-use-win-runtime-backup", Guid.NewGuid().ToString("N"));
        string sentinelPath = Path.Combine(runtimeRoot, "keep.txt");

        try
        {
            if (Directory.Exists(runtimeRoot))
            {
                CopyDirectory(runtimeRoot, backupRoot, _ => true);
                DeleteDirectoryIfExists(runtimeRoot);
            }

            Directory.CreateDirectory(runtimeRoot);
            File.WriteAllText(Path.Combine(runtimeRoot, "Okno.Server.exe"), "old-runtime");
            File.WriteAllText(sentinelPath, "keep");

            ScriptInvocationResult result = InvokePowerShellScript(
                scriptPath,
                repoRoot,
                startInfo => startInfo.Environment["COMPUTER_USE_WIN_TEST_FAIL_AFTER_BACKUP"] = "1");

            Assert.NotEqual(0, result.ExitCode);
            Assert.True(File.Exists(sentinelPath));
            Assert.Equal("keep", File.ReadAllText(sentinelPath));
        }
        finally
        {
            DeleteDirectoryIfExists(runtimeRoot);
            if (Directory.Exists(backupRoot))
            {
                CopyDirectory(backupRoot, runtimeRoot, _ => true);
            }

            DeleteDirectoryIfExists(backupRoot);
        }
    }

    [Fact]
    public void PublishComputerUseWinPluginPreservesBackupWhenRestoreFails()
    {
        string repoRoot = GetRepositoryRoot();
        string scriptPath = Path.Combine(repoRoot, "scripts", "codex", "publish-computer-use-win-plugin.ps1");
        string runtimeRoot = Path.Combine(repoRoot, "plugins", "computer-use-win", "runtime", "win-x64");
        string runtimeParent = Path.GetDirectoryName(runtimeRoot)!;
        string backupRoot = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "computer-use-win-runtime-backup-restore", Guid.NewGuid().ToString("N"));
        string sentinelPath = Path.Combine(runtimeRoot, "keep.txt");

        try
        {
            if (Directory.Exists(runtimeRoot))
            {
                CopyDirectory(runtimeRoot, backupRoot, _ => true);
                DeleteDirectoryIfExists(runtimeRoot);
            }

            Directory.CreateDirectory(runtimeRoot);
            File.WriteAllText(Path.Combine(runtimeRoot, "Okno.Server.exe"), "old-runtime");
            File.WriteAllText(sentinelPath, "keep");

            ScriptInvocationResult result = InvokePowerShellScript(
                scriptPath,
                repoRoot,
                startInfo =>
                {
                    startInfo.Environment["COMPUTER_USE_WIN_TEST_FAIL_AFTER_BACKUP"] = "1";
                    startInfo.Environment["COMPUTER_USE_WIN_TEST_FAIL_RESTORE"] = "1";
                });

            Assert.NotEqual(0, result.ExitCode);
            Assert.True(File.Exists(Path.Combine(runtimeRoot, "Okno.Server.exe")));
            Assert.True(File.Exists(sentinelPath));
            Assert.Equal("keep", File.ReadAllText(sentinelPath));

        }
        finally
        {
            DeleteDirectoryIfExists(runtimeRoot);
            if (Directory.Exists(backupRoot))
            {
                CopyDirectory(backupRoot, runtimeRoot, _ => true);
            }

            DeleteDirectoryIfExists(backupRoot);
            foreach (string backupCandidate in Directory.Exists(runtimeParent)
                         ? Directory.GetDirectories(runtimeParent, "win-x64.backup-*", SearchOption.TopDirectoryOnly)
                         : [])
            {
                DeleteDirectoryIfExists(backupCandidate);
            }
        }
    }

    [Fact]
    public void PublishComputerUseWinPluginTreatsBackupCleanupFailureAsBestEffortAfterSuccessfulPromote()
    {
        string repoRoot = GetRepositoryRoot();
        string scriptPath = Path.Combine(repoRoot, "scripts", "codex", "publish-computer-use-win-plugin.ps1");
        string runtimeRoot = Path.Combine(repoRoot, "plugins", "computer-use-win", "runtime", "win-x64");
        string runtimeParent = Path.GetDirectoryName(runtimeRoot)!;

        try
        {
            ScriptInvocationResult result = InvokePowerShellScript(
                scriptPath,
                repoRoot,
                startInfo => startInfo.Environment["COMPUTER_USE_WIN_TEST_FAIL_BACKUP_CLEANUP"] = "1");

            Assert.Equal(0, result.ExitCode);
            Assert.True(File.Exists(Path.Combine(runtimeRoot, "Okno.Server.exe")));
            Assert.NotEmpty(Directory.GetDirectories(runtimeParent, "win-x64.backup-*", SearchOption.TopDirectoryOnly));
        }
        finally
        {
            foreach (string backupCandidate in Directory.Exists(runtimeParent)
                         ? Directory.GetDirectories(runtimeParent, "win-x64.backup-*", SearchOption.TopDirectoryOnly)
                         : [])
            {
                DeleteDirectoryIfExists(backupCandidate);
            }
        }
    }

    [Fact]
    public void ComputerUseWinLauncherFailsFastWhenPluginLocalRuntimeIsMissing()
    {
        string repoRoot = GetRepositoryRoot();
        string sourcePluginRoot = Path.Combine(repoRoot, "plugins", "computer-use-win");
        string tempPluginRoot = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "computer-use-win-missing-runtime", Guid.NewGuid().ToString("N"));

        try
        {
            CopyDirectory(sourcePluginRoot, tempPluginRoot, relativePath =>
                !relativePath.StartsWith($"runtime{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

            ScriptInvocationResult result = InvokePowerShellScript(
                Path.Combine(tempPluginRoot, "run-computer-use-win-mcp.ps1"),
                tempPluginRoot,
                startInfo =>
                {
                    startInfo.Environment["COMPUTER_USE_WIN_REPO_ROOT"] = string.Empty;
                    startInfo.Environment["WINBRIDGE_RUN_ID"] = string.Empty;
                    startInfo.Environment["WINBRIDGE_RUN_ROOT"] = string.Empty;
                    startInfo.Environment["WINBRIDGE_ARTIFACTS_ROOT"] = string.Empty;
                });

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("publish-computer-use-win-plugin.ps1", result.Stderr, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(Path.Combine("runtime", "win-x64", "Okno.Server.exe"), result.Stderr, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectoryIfExists(tempPluginRoot);
        }
    }

    [Fact]
    public async Task ComputerUseWinLauncherFromTempPluginCopyPublishesPublicSurfaceWithoutRepoHints()
    {
        string repoRoot = GetRepositoryRoot();
        string publishScriptPath = Path.Combine(repoRoot, "scripts", "codex", "publish-computer-use-win-plugin.ps1");
        string sourcePluginRoot = Path.Combine(repoRoot, "plugins", "computer-use-win");
        string tempPluginRoot = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "computer-use-win-installed-copy", Guid.NewGuid().ToString("N"));

        ScriptInvocationResult publishResult = InvokePowerShellScript(
            publishScriptPath,
            repoRoot,
            _ => { });
        Assert.True(
            publishResult.ExitCode == 0,
            $"Publish script failed. ExitCode={publishResult.ExitCode}. stderr='{publishResult.Stderr.Trim()}', stdout='{publishResult.Stdout.Trim()}'.");

        try
        {
            CopyDirectory(sourcePluginRoot, tempPluginRoot, _ => true);

            using Process process = StartPluginLauncher(tempPluginRoot);
            await using StreamWriter writer = process.StandardInput;
            using StreamReader reader = process.StandardOutput;
            PluginMcpSession session = new(reader, writer);

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
                            name = "ComputerUseWin.InstallSurfaceTests",
                            version = "0.1.0",
                        },
                    },
                    "initialize");

                await session.SendNotificationAsync("notifications/initialized");

                using JsonDocument toolsResponse = await session.SendRequestAsync("tools/list", new { }, "tools/list");
                string[] toolNames = toolsResponse.RootElement
                    .GetProperty("result")
                    .GetProperty("tools")
                    .EnumerateArray()
                    .Select(tool => tool.GetProperty("name").GetString() ?? string.Empty)
                    .OrderBy(static value => value, StringComparer.Ordinal)
                    .ToArray();

                Assert.Equal(
                    [ToolNames.ComputerUseWinClick, ToolNames.ComputerUseWinGetAppState, ToolNames.ComputerUseWinListApps],
                    toolNames);
            }
            finally
            {
                writer.Close();
                await WaitForExitAsync(process);
            }
        }
        finally
        {
            DeleteDirectoryIfExists(tempPluginRoot);
        }
    }

    private static Process StartPluginLauncher(string pluginRoot)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "powershell",
            WorkingDirectory = pluginRoot,
            RedirectStandardInput = true,
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
        startInfo.ArgumentList.Add(Path.Combine(pluginRoot, "run-computer-use-win-mcp.ps1"));
        startInfo.Environment["COMPUTER_USE_WIN_REPO_ROOT"] = string.Empty;
        startInfo.Environment["WINBRIDGE_RUN_ID"] = string.Empty;
        startInfo.Environment["WINBRIDGE_RUN_ROOT"] = string.Empty;
        startInfo.Environment["WINBRIDGE_ARTIFACTS_ROOT"] = string.Empty;

        Process process = new() { StartInfo = startInfo };
        process.Start();
        return process;
    }

    private static async Task WaitForExitAsync(Process process)
    {
        if (process.HasExited)
        {
            return;
        }

        Task waitTask = process.WaitForExitAsync();
        Task completedTask = await Task.WhenAny(waitTask, Task.Delay(TimeSpan.FromSeconds(5)));
        if (!ReferenceEquals(completedTask, waitTask))
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
    }

    private static void CopyDirectory(string sourceRoot, string destinationRoot, Func<string, bool> includePredicate)
    {
        foreach (string sourcePath in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourceRoot, sourcePath);
            if (!includePredicate(relativePath))
            {
                continue;
            }

            string destinationPath = Path.Combine(destinationRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(sourcePath, destinationPath, overwrite: true);
        }
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static ScriptInvocationResult InvokePowerShellScript(string scriptPath, string workingDirectory, Action<ProcessStartInfo> configure)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "powershell",
            WorkingDirectory = workingDirectory,
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
        startInfo.ArgumentList.Add(scriptPath);

        configure(startInfo);

        using Process process = new() { StartInfo = startInfo };
        process.Start();

        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new ScriptInvocationResult(process.ExitCode, stdout, stderr);
    }

    private static string GetRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "WinBridge.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Не удалось определить корень репозитория WinBridge.");
    }

    private sealed class PluginMcpSession(StreamReader reader, StreamWriter writer)
    {
        private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(15);
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

        public Task<JsonDocument> SendRequestAsync(string method, object parameters, string requestName)
        {
            int requestId = nextRequestId++;
            string json = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = requestId,
                method,
                @params = parameters,
            });

            return SendAndReadAsync(requestName, requestId, json);
        }

        private async Task<JsonDocument> SendAndReadAsync(string requestName, int expectedId, string json)
        {
            await writer.WriteLineAsync(json);
            await writer.FlushAsync();

            while (true)
            {
                string? line = await reader.ReadLineAsync().WaitAsync(ResponseTimeout);
                Assert.False(line is null, $"Plugin process exited before '{requestName}' response.");
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                JsonDocument document = JsonDocument.Parse(line);
                if (!document.RootElement.TryGetProperty("id", out JsonElement idElement))
                {
                    document.Dispose();
                    continue;
                }

                if (idElement.GetInt32() == expectedId)
                {
                    return document;
                }

                document.Dispose();
            }
        }
    }

    private sealed record ScriptInvocationResult(int ExitCode, string Stdout, string Stderr);
}
