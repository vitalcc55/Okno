// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;
using WinBridge.Runtime.Tooling;

namespace WinBridge.InstallSurface.AcceptanceTests;

public sealed partial class ComputerUseWinInstallSurfaceTests
{
    private const string SharedRuntimeFoundationRid = "win-x64";
    private const string SharedRuntimeFoundationVersion = "0.3.0";
    private const string SharedRuntimeFoundationMcpProtocolVersion = "2025-11-25";
    private const string SharedRuntimeFoundationTestClientName = "ComputerUseWin.SharedRuntimeFoundationTests";

    private static readonly object s_sharedRuntimeFoundationCacheLock = new();
    private static SharedRuntimeRelease? s_cachedSharedRuntimeRelease;
    private static string? s_publishedSharedRuntimeRoot;
    private static bool s_cachedSharedRuntimeReleaseCleanupRegistered;

    [Fact]
    public void SetupCliRuntimeInstallCreatesCanonicalSharedRuntimeStoreUnderCodexHome()
    {
        using SharedRuntimeScenario scenario = CreateSharedRuntimeScenario("setup-cli-runtime-install");

        ScriptInvocationResult result = InvokeDefaultRuntimeCommand(scenario, "install");

        AssertCliSucceeded(result, "Setup CLI runtime install");
        using JsonDocument payload = JsonDocument.Parse(result.Stdout);

        string expectedRuntimeRoot = GetExpectedDefaultSharedRuntimeRoot(scenario);
        string expectedStatePath = GetExpectedSharedRuntimeStatePath(scenario.CodexHome);

        Assert.Equal(expectedRuntimeRoot, payload.RootElement.GetProperty("runtimeRoot").GetString());
        Assert.Equal(expectedStatePath, payload.RootElement.GetProperty("currentStatePath").GetString());
        Assert.True(Directory.Exists(expectedRuntimeRoot));
        Assert.True(File.Exists(Path.Combine(expectedRuntimeRoot, "Okno.Server.exe")));
        Assert.True(File.Exists(Path.Combine(expectedRuntimeRoot, "okno-runtime-bundle-manifest.json")));
        Assert.True(File.Exists(expectedStatePath));

        using JsonDocument state = JsonDocument.Parse(File.ReadAllText(expectedStatePath));
        Assert.Equal(1, state.RootElement.GetProperty("formatVersion").GetInt32());
        Assert.Equal(SharedRuntimeFoundationRid, state.RootElement.GetProperty("rid").GetString());
        Assert.Equal(scenario.DefaultRelease.Version, state.RootElement.GetProperty("version").GetString());
        Assert.Equal(expectedRuntimeRoot, state.RootElement.GetProperty("runtimeRoot").GetString());
    }

    [Fact]
    public void SetupCliRuntimeStatusReturnsCurrentSharedRuntimeMetadata()
    {
        using SharedRuntimeScenario scenario = CreateSharedRuntimeScenario("setup-cli-runtime-status");
        InstallDefaultRuntimeOrFail(scenario);

        ScriptInvocationResult statusResult = InvokeDefaultRuntimeCommand(scenario, "status");

        AssertCliSucceeded(statusResult, "Setup CLI status");
        using JsonDocument payload = JsonDocument.Parse(statusResult.Stdout);
        Assert.True(payload.RootElement.GetProperty("isInstalled").GetBoolean());
        Assert.True(payload.RootElement.GetProperty("isUsable").GetBoolean());
        Assert.True(payload.RootElement.GetProperty("isCompatible").GetBoolean());
        Assert.Equal(scenario.DefaultRelease.Version, payload.RootElement.GetProperty("currentRuntime").GetProperty("version").GetString());
    }

    [Fact]
    public void SetupCliRuntimeVerifyFailsClosedWhenSharedRuntimeBundleDrifts()
    {
        using SharedRuntimeScenario scenario = CreateSharedRuntimeScenario("setup-cli-runtime-verify");
        InstallDefaultRuntimeOrFail(scenario);

        string installedRuntimeRoot = GetExpectedDefaultSharedRuntimeRoot(scenario);
        File.Delete(Path.Combine(installedRuntimeRoot, "hostfxr.dll"));

        ScriptInvocationResult verifyResult = InvokeDefaultRuntimeCommand(scenario, "verify");

        Assert.NotEqual(0, verifyResult.ExitCode);
        Assert.Contains("isUsable", verifyResult.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void SetupCliRuntimeRepairRestoresDriftedCurrentRuntimeFromDescriptor()
    {
        using SharedRuntimeScenario scenario = CreateSharedRuntimeScenario("setup-cli-runtime-repair");
        InstallDefaultRuntimeOrFail(scenario);

        string installedRuntimeRoot = GetExpectedDefaultSharedRuntimeRoot(scenario);
        File.Delete(Path.Combine(installedRuntimeRoot, "hostfxr.dll"));

        ScriptInvocationResult repairResult = InvokeDefaultRuntimeCommand(scenario, "repair");

        AssertCliSucceeded(repairResult, "Setup CLI repair");
        Assert.True(File.Exists(Path.Combine(installedRuntimeRoot, "hostfxr.dll")));
        AssertRuntimeBundleMatchesManifest(installedRuntimeRoot);
    }

    [Fact]
    public async Task ComputerUseWinLauncherUsesSharedInstalledRuntimeWhenPluginLocalRuntimeIsInvalid()
    {
        using SharedRuntimeScenario scenario = CreateSharedRuntimeScenario("launcher-shared-runtime-preferred");
        InstallDefaultRuntimeOrFail(scenario);

        string tempPluginRoot = CopySourcePluginToTempRoot(scenario);
        File.Delete(Path.Combine(tempPluginRoot, "runtime", SharedRuntimeFoundationRid, "Okno.Server.exe"));

        await using PluginLauncherSession launcher = StartPluginLauncherSession(tempPluginRoot, scenario.DefaultRelease.DescriptorPath, scenario.CodexHome);
        PluginMcpSession session = launcher.CreateMcpSession();

        await InitializeComputerUseWinMcpSessionAsync(session);
        string[] toolNames = await ReadComputerUseWinToolNamesAsync(session);

        Assert.Contains(ToolNames.ComputerUseWinListApps, toolNames);
    }

    [Fact]
    public async Task ComputerUseWinLauncherFallsBackToPluginLocalRuntimeWhenSharedRuntimeStateIsIncompatible()
    {
        const string mismatchedVersion = "0.3.0-test";

        using SharedRuntimeScenario scenario = CreateSharedRuntimeScenario("launcher-plugin-local-fallback");
        SharedRuntimeRelease mismatchedRelease = CreateRuntimeRelease(scenario, mismatchedVersion);
        ScriptInvocationResult installResult = InvokeRuntimeCommand(scenario, "install", mismatchedRelease.DescriptorPath);
        AssertCliSucceeded(installResult, "Setup CLI mismatched install");

        string tempPluginRoot = CopySourcePluginToTempRoot(scenario);

        await using PluginLauncherSession launcher = StartPluginLauncherSession(tempPluginRoot, scenario.DefaultRelease.DescriptorPath, scenario.CodexHome);
        PluginMcpSession session = launcher.CreateMcpSession();

        await InitializeComputerUseWinMcpSessionAsync(session);

        using JsonDocument state = JsonDocument.Parse(File.ReadAllText(GetExpectedSharedRuntimeStatePath(scenario.CodexHome)));
        Assert.Equal(mismatchedVersion, state.RootElement.GetProperty("version").GetString());
    }

    [Fact]
    public async Task ComputerUseWinLauncherRehydratesSharedRuntimeStoreWhenNoUsableRuntimeExists()
    {
        using SharedRuntimeScenario scenario = CreateSharedRuntimeScenario("launcher-rehydrate-shared-runtime");

        string tempPluginRoot = CopySourcePluginToTempRoot(scenario);
        DeleteDirectoryIfExists(Path.Combine(tempPluginRoot, "runtime", SharedRuntimeFoundationRid));

        await using PluginLauncherSession launcher = StartPluginLauncherSession(tempPluginRoot, scenario.DefaultRelease.DescriptorPath, scenario.CodexHome);
        PluginMcpSession session = launcher.CreateMcpSession();

        await InitializeComputerUseWinMcpSessionAsync(session);

        string sharedRuntimeRoot = GetExpectedDefaultSharedRuntimeRoot(scenario);
        string statePath = GetExpectedSharedRuntimeStatePath(scenario.CodexHome);
        Assert.True(File.Exists(Path.Combine(sharedRuntimeRoot, "Okno.Server.exe")));
        Assert.True(File.Exists(statePath));

        using JsonDocument state = JsonDocument.Parse(File.ReadAllText(statePath));
        Assert.Equal(scenario.DefaultRelease.Version, state.RootElement.GetProperty("version").GetString());
        Assert.Equal(sharedRuntimeRoot, state.RootElement.GetProperty("runtimeRoot").GetString());
    }

    private static SharedRuntimeScenario CreateSharedRuntimeScenario(string scenarioName)
    {
        SharedRuntimeTestPaths paths = CreateSharedRuntimeTestPaths();
        EnsureSharedRuntimeBundlePublishedOnce(paths);

        return new SharedRuntimeScenario(paths, scenarioName, GetCachedDefaultSharedRuntimeRelease(paths), DeleteDirectoryIfExists);
    }

    private static SharedRuntimeTestPaths CreateSharedRuntimeTestPaths()
    {
        string repoRoot = GetRepositoryRoot();
        string sourcePluginRoot = Path.Combine(repoRoot, "plugins", "computer-use-win");

        return new SharedRuntimeTestPaths(
            repoRoot,
            GetPublishScriptPath(repoRoot),
            Path.Combine(repoRoot, "scripts", "codex", "package-computer-use-win-runtime-release.ps1"),
            sourcePluginRoot,
            Path.Combine(sourcePluginRoot, "runtime", SharedRuntimeFoundationRid),
            Path.Combine(repoRoot, ".tmp", ".codex", "tests"));
    }

    private static void EnsureSharedRuntimeBundlePublishedOnce(SharedRuntimeTestPaths paths)
    {
        lock (s_sharedRuntimeFoundationCacheLock)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(s_publishedSharedRuntimeRoot, paths.PluginRuntimeRoot))
            {
                return;
            }

            EnsurePublishedRuntimeBundle(paths.RepoRoot, paths.PublishScriptPath, paths.PluginRuntimeRoot);
            s_publishedSharedRuntimeRoot = paths.PluginRuntimeRoot;
        }
    }

    private static SharedRuntimeRelease GetCachedDefaultSharedRuntimeRelease(SharedRuntimeTestPaths paths)
    {
        lock (s_sharedRuntimeFoundationCacheLock)
        {
            if (s_cachedSharedRuntimeRelease is not null)
            {
                return s_cachedSharedRuntimeRelease;
            }

            string outputRoot = Path.Combine(paths.TestRoot, "shared-runtime-foundation-release-cache", Guid.NewGuid().ToString("N"));
            RuntimeReleasePackageResult runtimePackage = PackageRuntimeRelease(paths.RepoRoot, paths.PackageScriptPath, paths.PluginRuntimeRoot, outputRoot, SharedRuntimeFoundationVersion);
            string descriptorPath = runtimePackage.DescriptorPath;

            s_cachedSharedRuntimeRelease = new SharedRuntimeRelease(SharedRuntimeFoundationVersion, descriptorPath, outputRoot);
            RegisterCachedSharedRuntimeReleaseCleanup(s_cachedSharedRuntimeRelease);

            return s_cachedSharedRuntimeRelease;
        }
    }

    private static void RegisterCachedSharedRuntimeReleaseCleanup(SharedRuntimeRelease release)
    {
        if (s_cachedSharedRuntimeReleaseCleanupRegistered)
        {
            return;
        }

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                if (Directory.Exists(release.OutputRoot))
                {
                    Directory.Delete(release.OutputRoot, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        };
        s_cachedSharedRuntimeReleaseCleanupRegistered = true;
    }

    private static SharedRuntimeRelease CreateRuntimeRelease(SharedRuntimeScenario scenario, string version)
    {
        RuntimeReleasePackageResult runtimePackage = PackageRuntimeRelease(scenario.Paths.RepoRoot, scenario.Paths.PackageScriptPath, scenario.Paths.PluginRuntimeRoot, scenario.OutputRoot, version);
        string descriptorPath = runtimePackage.DescriptorPath;

        return new SharedRuntimeRelease(version, descriptorPath, scenario.OutputRoot);
    }

    private static ScriptInvocationResult InvokeDefaultRuntimeCommand(SharedRuntimeScenario scenario, string command) =>
        InvokeRuntimeCommand(scenario, command, scenario.DefaultRelease.DescriptorPath);

    private static ScriptInvocationResult InvokeRuntimeCommand(SharedRuntimeScenario scenario, string command, string descriptorPath) =>
        InvokeSetupCli(scenario.Paths.RepoRoot, ["runtime", command, "--descriptor-path", descriptorPath, "--json"], scenario.CodexHome);

    private static void AssertCliSucceeded(ScriptInvocationResult result, string operationName)
    {
        Assert.True(result.ExitCode == 0, $"{operationName} failed. stderr='{result.Stderr}', stdout='{result.Stdout}'.");
    }

    private static void InstallDefaultRuntimeOrFail(SharedRuntimeScenario scenario) =>
        AssertCliSucceeded(InvokeDefaultRuntimeCommand(scenario, "install"), "Setup CLI install");

    private static string CopySourcePluginToTempRoot(SharedRuntimeScenario scenario)
    {
        string tempPluginRoot = scenario.AllocateTempPluginRoot();
        CopyDirectory(scenario.Paths.SourcePluginRoot, tempPluginRoot, _ => true);
        return tempPluginRoot;
    }

    private static string GetExpectedDefaultSharedRuntimeRoot(SharedRuntimeScenario scenario) =>
        GetExpectedSharedRuntimeRoot(scenario.CodexHome, SharedRuntimeFoundationRid, scenario.DefaultRelease.Version);

    private static async Task InitializeComputerUseWinMcpSessionAsync(PluginMcpSession session)
    {
        using JsonDocument _ = await session.SendRequestAsync(
            "initialize",
            new
            {
                protocolVersion = SharedRuntimeFoundationMcpProtocolVersion,
                capabilities = new { },
                clientInfo = new
                {
                    name = SharedRuntimeFoundationTestClientName,
                    version = SharedRuntimeFoundationVersion,
                },
            },
            "initialize");

        await session.SendNotificationAsync("notifications/initialized");
    }

    private static async Task<string[]> ReadComputerUseWinToolNamesAsync(PluginMcpSession session)
    {
        using JsonDocument toolsResponse = await session.SendRequestAsync("tools/list", new { }, "tools/list");

        return toolsResponse.RootElement
            .GetProperty("result")
            .GetProperty("tools")
            .EnumerateArray()
            .Select(tool => tool.GetProperty("name").GetString() ?? string.Empty)
            .ToArray();
    }

    private sealed record SharedRuntimeTestPaths(
        string RepoRoot,
        string PublishScriptPath,
        string PackageScriptPath,
        string SourcePluginRoot,
        string PluginRuntimeRoot,
        string TestRoot);

    private sealed record SharedRuntimeRelease(string Version, string DescriptorPath, string OutputRoot);

    private sealed class SharedRuntimeScenario(
        SharedRuntimeTestPaths paths,
        string scenarioName,
        SharedRuntimeRelease defaultRelease,
        Action<string> deleteDirectoryIfExists) : IDisposable
    {
        public SharedRuntimeTestPaths Paths { get; } = paths;

        public SharedRuntimeRelease DefaultRelease { get; } = defaultRelease;

        public string OutputRoot { get; } = Path.Combine(paths.TestRoot, scenarioName, Guid.NewGuid().ToString("N"));

        public string CodexHome { get; } = Path.Combine(paths.TestRoot, $"codex-home-{scenarioName}", Guid.NewGuid().ToString("N"));

        public string? TempPluginRoot { get; private set; }

        public string AllocateTempPluginRoot() =>
            TempPluginRoot ??= Path.Combine(Paths.TestRoot, $"{scenarioName}-plugin-copy", Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            deleteDirectoryIfExists(OutputRoot);

            if (TempPluginRoot is not null)
            {
                deleteDirectoryIfExists(TempPluginRoot);
            }

            deleteDirectoryIfExists(CodexHome);
        }
    }
}
