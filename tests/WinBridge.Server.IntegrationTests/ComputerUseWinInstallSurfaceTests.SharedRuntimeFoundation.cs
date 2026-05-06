// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;
using WinBridge.Runtime.Tooling;

namespace WinBridge.Server.IntegrationTests;

public sealed partial class ComputerUseWinInstallSurfaceTests
{
    [Fact]
    public void SetupCliRuntimeInstallCreatesCanonicalSharedRuntimeStoreUnderCodexHome()
    {
        string repoRoot = GetRepositoryRoot();
        string publishScriptPath = GetPublishScriptPath(repoRoot);
        string packageScriptPath = Path.Combine(repoRoot, "scripts", "codex", "package-computer-use-win-runtime-release.ps1");
        string sourcePluginRoot = Path.Combine(repoRoot, "plugins", "computer-use-win");
        string runtimeRoot = Path.Combine(sourcePluginRoot, "runtime", "win-x64");
        string outputRoot = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "setup-cli-runtime-install", Guid.NewGuid().ToString("N"));
        string codexHome = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "codex-home-runtime-install", Guid.NewGuid().ToString("N"));
        const string version = "0.1.0";

        EnsurePublishedRuntimeBundle(repoRoot, publishScriptPath, runtimeRoot);

        try
        {
            string archivePath = PackageRuntimeRelease(repoRoot, packageScriptPath, runtimeRoot, outputRoot, version);
            string descriptorPath = CreateRuntimeReleaseDescriptor(outputRoot, version, archivePath, "win-x64");

            ScriptInvocationResult result = InvokeSetupCli(
                repoRoot,
                ["runtime", "install", "--descriptor-path", descriptorPath, "--json"],
                codexHome);

            Assert.True(result.ExitCode == 0, $"Setup CLI runtime install failed. stderr='{result.Stderr}', stdout='{result.Stdout}'.");
            using JsonDocument payload = JsonDocument.Parse(result.Stdout);

            string expectedRuntimeRoot = GetExpectedSharedRuntimeRoot(codexHome, "win-x64", version);
            string expectedStatePath = GetExpectedSharedRuntimeStatePath(codexHome);

            Assert.Equal(expectedRuntimeRoot, payload.RootElement.GetProperty("runtimeRoot").GetString());
            Assert.Equal(expectedStatePath, payload.RootElement.GetProperty("currentStatePath").GetString());
            Assert.True(Directory.Exists(expectedRuntimeRoot));
            Assert.True(File.Exists(Path.Combine(expectedRuntimeRoot, "Okno.Server.exe")));
            Assert.True(File.Exists(Path.Combine(expectedRuntimeRoot, "okno-runtime-bundle-manifest.json")));
            Assert.True(File.Exists(expectedStatePath));

            using JsonDocument state = JsonDocument.Parse(File.ReadAllText(expectedStatePath));
            Assert.Equal(1, state.RootElement.GetProperty("formatVersion").GetInt32());
            Assert.Equal("win-x64", state.RootElement.GetProperty("rid").GetString());
            Assert.Equal(version, state.RootElement.GetProperty("version").GetString());
            Assert.Equal(expectedRuntimeRoot, state.RootElement.GetProperty("runtimeRoot").GetString());
        }
        finally
        {
            DeleteDirectoryIfExists(outputRoot);
            DeleteDirectoryIfExists(codexHome);
        }
    }

    [Fact]
    public void SetupCliRuntimeStatusReturnsCurrentSharedRuntimeMetadata()
    {
        string repoRoot = GetRepositoryRoot();
        string publishScriptPath = GetPublishScriptPath(repoRoot);
        string packageScriptPath = Path.Combine(repoRoot, "scripts", "codex", "package-computer-use-win-runtime-release.ps1");
        string sourcePluginRoot = Path.Combine(repoRoot, "plugins", "computer-use-win");
        string runtimeRoot = Path.Combine(sourcePluginRoot, "runtime", "win-x64");
        string outputRoot = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "setup-cli-runtime-status", Guid.NewGuid().ToString("N"));
        string codexHome = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "codex-home-runtime-status", Guid.NewGuid().ToString("N"));
        const string version = "0.1.0";

        EnsurePublishedRuntimeBundle(repoRoot, publishScriptPath, runtimeRoot);

        try
        {
            string archivePath = PackageRuntimeRelease(repoRoot, packageScriptPath, runtimeRoot, outputRoot, version);
            string descriptorPath = CreateRuntimeReleaseDescriptor(outputRoot, version, archivePath, "win-x64");

            ScriptInvocationResult installResult = InvokeSetupCli(
                repoRoot,
                ["runtime", "install", "--descriptor-path", descriptorPath, "--json"],
                codexHome);
            Assert.True(installResult.ExitCode == 0, $"Setup CLI install failed. stderr='{installResult.Stderr}'");

            ScriptInvocationResult statusResult = InvokeSetupCli(
                repoRoot,
                ["runtime", "status", "--descriptor-path", descriptorPath, "--json"],
                codexHome);
            Assert.True(statusResult.ExitCode == 0, $"Setup CLI status failed. stderr='{statusResult.Stderr}', stdout='{statusResult.Stdout}'.");

            using JsonDocument payload = JsonDocument.Parse(statusResult.Stdout);
            Assert.True(payload.RootElement.GetProperty("isInstalled").GetBoolean());
            Assert.True(payload.RootElement.GetProperty("isUsable").GetBoolean());
            Assert.True(payload.RootElement.GetProperty("isCompatible").GetBoolean());
            Assert.Equal(version, payload.RootElement.GetProperty("currentRuntime").GetProperty("version").GetString());
        }
        finally
        {
            DeleteDirectoryIfExists(outputRoot);
            DeleteDirectoryIfExists(codexHome);
        }
    }

    [Fact]
    public void SetupCliRuntimeVerifyFailsClosedWhenSharedRuntimeBundleDrifts()
    {
        string repoRoot = GetRepositoryRoot();
        string publishScriptPath = GetPublishScriptPath(repoRoot);
        string packageScriptPath = Path.Combine(repoRoot, "scripts", "codex", "package-computer-use-win-runtime-release.ps1");
        string sourcePluginRoot = Path.Combine(repoRoot, "plugins", "computer-use-win");
        string runtimeRoot = Path.Combine(sourcePluginRoot, "runtime", "win-x64");
        string outputRoot = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "setup-cli-runtime-verify", Guid.NewGuid().ToString("N"));
        string codexHome = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "codex-home-runtime-verify", Guid.NewGuid().ToString("N"));
        const string version = "0.1.0";

        EnsurePublishedRuntimeBundle(repoRoot, publishScriptPath, runtimeRoot);

        try
        {
            string archivePath = PackageRuntimeRelease(repoRoot, packageScriptPath, runtimeRoot, outputRoot, version);
            string descriptorPath = CreateRuntimeReleaseDescriptor(outputRoot, version, archivePath, "win-x64");

            ScriptInvocationResult installResult = InvokeSetupCli(
                repoRoot,
                ["runtime", "install", "--descriptor-path", descriptorPath, "--json"],
                codexHome);
            Assert.True(installResult.ExitCode == 0, $"Setup CLI install failed. stderr='{installResult.Stderr}'");

            string installedRuntimeRoot = GetExpectedSharedRuntimeRoot(codexHome, "win-x64", version);
            File.Delete(Path.Combine(installedRuntimeRoot, "hostfxr.dll"));

            ScriptInvocationResult verifyResult = InvokeSetupCli(
                repoRoot,
                ["runtime", "verify", "--descriptor-path", descriptorPath, "--json"],
                codexHome);

            Assert.NotEqual(0, verifyResult.ExitCode);
            Assert.Contains("isUsable", verifyResult.Stdout, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectoryIfExists(outputRoot);
            DeleteDirectoryIfExists(codexHome);
        }
    }

    [Fact]
    public void SetupCliRuntimeRepairRestoresDriftedCurrentRuntimeFromDescriptor()
    {
        string repoRoot = GetRepositoryRoot();
        string publishScriptPath = GetPublishScriptPath(repoRoot);
        string packageScriptPath = Path.Combine(repoRoot, "scripts", "codex", "package-computer-use-win-runtime-release.ps1");
        string sourcePluginRoot = Path.Combine(repoRoot, "plugins", "computer-use-win");
        string runtimeRoot = Path.Combine(sourcePluginRoot, "runtime", "win-x64");
        string outputRoot = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "setup-cli-runtime-repair", Guid.NewGuid().ToString("N"));
        string codexHome = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "codex-home-runtime-repair", Guid.NewGuid().ToString("N"));
        const string version = "0.1.0";

        EnsurePublishedRuntimeBundle(repoRoot, publishScriptPath, runtimeRoot);

        try
        {
            string archivePath = PackageRuntimeRelease(repoRoot, packageScriptPath, runtimeRoot, outputRoot, version);
            string descriptorPath = CreateRuntimeReleaseDescriptor(outputRoot, version, archivePath, "win-x64");

            ScriptInvocationResult installResult = InvokeSetupCli(
                repoRoot,
                ["runtime", "install", "--descriptor-path", descriptorPath, "--json"],
                codexHome);
            Assert.True(installResult.ExitCode == 0, $"Setup CLI install failed. stderr='{installResult.Stderr}'");

            string installedRuntimeRoot = GetExpectedSharedRuntimeRoot(codexHome, "win-x64", version);
            File.Delete(Path.Combine(installedRuntimeRoot, "hostfxr.dll"));

            ScriptInvocationResult repairResult = InvokeSetupCli(
                repoRoot,
                ["runtime", "repair", "--descriptor-path", descriptorPath, "--json"],
                codexHome);

            Assert.True(repairResult.ExitCode == 0, $"Setup CLI repair failed. stderr='{repairResult.Stderr}', stdout='{repairResult.Stdout}'.");
            Assert.True(File.Exists(Path.Combine(installedRuntimeRoot, "hostfxr.dll")));
            AssertRuntimeBundleMatchesManifest(installedRuntimeRoot);
        }
        finally
        {
            DeleteDirectoryIfExists(outputRoot);
            DeleteDirectoryIfExists(codexHome);
        }
    }

    [Fact]
    public async Task ComputerUseWinLauncherUsesSharedInstalledRuntimeWhenPluginLocalRuntimeIsInvalid()
    {
        string repoRoot = GetRepositoryRoot();
        string publishScriptPath = GetPublishScriptPath(repoRoot);
        string packageScriptPath = Path.Combine(repoRoot, "scripts", "codex", "package-computer-use-win-runtime-release.ps1");
        string sourcePluginRoot = Path.Combine(repoRoot, "plugins", "computer-use-win");
        string pluginRuntimeRoot = Path.Combine(sourcePluginRoot, "runtime", "win-x64");
        string outputRoot = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "launcher-shared-runtime-preferred", Guid.NewGuid().ToString("N"));
        string tempPluginRoot = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "launcher-shared-runtime-plugin-copy", Guid.NewGuid().ToString("N"));
        string codexHome = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "codex-home-launcher-shared", Guid.NewGuid().ToString("N"));
        const string version = "0.1.0";

        EnsurePublishedRuntimeBundle(repoRoot, publishScriptPath, pluginRuntimeRoot);

        try
        {
            string archivePath = PackageRuntimeRelease(repoRoot, packageScriptPath, pluginRuntimeRoot, outputRoot, version);
            string descriptorPath = CreateRuntimeReleaseDescriptor(outputRoot, version, archivePath, "win-x64");

            ScriptInvocationResult installResult = InvokeSetupCli(
                repoRoot,
                ["runtime", "install", "--descriptor-path", descriptorPath, "--json"],
                codexHome);
            Assert.True(installResult.ExitCode == 0, $"Setup CLI install failed. stderr='{installResult.Stderr}'");

            CopyDirectory(sourcePluginRoot, tempPluginRoot, _ => true);
            File.Delete(Path.Combine(tempPluginRoot, "runtime", "win-x64", "Okno.Server.exe"));

            await using PluginLauncherSession launcher = StartPluginLauncherSession(tempPluginRoot, descriptorPath, codexHome);
            PluginMcpSession session = launcher.CreateMcpSession();

            using JsonDocument initializeResponse = await session.SendRequestAsync(
                "initialize",
                new
                {
                    protocolVersion = "2025-11-25",
                    capabilities = new { },
                    clientInfo = new
                    {
                        name = "ComputerUseWin.SharedRuntimeFoundationTests",
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
                .ToArray();

            Assert.Contains(ToolNames.ComputerUseWinListApps, toolNames);
        }
        finally
        {
            DeleteDirectoryIfExists(outputRoot);
            DeleteDirectoryIfExists(tempPluginRoot);
            DeleteDirectoryIfExists(codexHome);
        }
    }

    [Fact]
    public async Task ComputerUseWinLauncherFallsBackToPluginLocalRuntimeWhenSharedRuntimeStateIsIncompatible()
    {
        string repoRoot = GetRepositoryRoot();
        string publishScriptPath = GetPublishScriptPath(repoRoot);
        string packageScriptPath = Path.Combine(repoRoot, "scripts", "codex", "package-computer-use-win-runtime-release.ps1");
        string sourcePluginRoot = Path.Combine(repoRoot, "plugins", "computer-use-win");
        string pluginRuntimeRoot = Path.Combine(sourcePluginRoot, "runtime", "win-x64");
        string outputRoot = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "launcher-plugin-local-fallback", Guid.NewGuid().ToString("N"));
        string tempPluginRoot = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "launcher-plugin-local-fallback-plugin-copy", Guid.NewGuid().ToString("N"));
        string codexHome = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "codex-home-plugin-local-fallback", Guid.NewGuid().ToString("N"));
        const string validVersion = "0.1.0";
        const string mismatchedVersion = "0.1.0-test";

        EnsurePublishedRuntimeBundle(repoRoot, publishScriptPath, pluginRuntimeRoot);

        try
        {
            string mismatchedArchivePath = PackageRuntimeRelease(repoRoot, packageScriptPath, pluginRuntimeRoot, outputRoot, mismatchedVersion);
            string mismatchedDescriptorPath = CreateRuntimeReleaseDescriptor(outputRoot, mismatchedVersion, mismatchedArchivePath, "win-x64");
            string validArchivePath = PackageRuntimeRelease(repoRoot, packageScriptPath, pluginRuntimeRoot, outputRoot, validVersion);
            string validDescriptorPath = CreateRuntimeReleaseDescriptor(outputRoot, validVersion, validArchivePath, "win-x64");

            ScriptInvocationResult installResult = InvokeSetupCli(
                repoRoot,
                ["runtime", "install", "--descriptor-path", mismatchedDescriptorPath, "--json"],
                codexHome);
            Assert.True(installResult.ExitCode == 0, $"Setup CLI mismatched install failed. stderr='{installResult.Stderr}'");

            CopyDirectory(sourcePluginRoot, tempPluginRoot, _ => true);

            await using PluginLauncherSession launcher = StartPluginLauncherSession(tempPluginRoot, validDescriptorPath, codexHome);
            PluginMcpSession session = launcher.CreateMcpSession();

            using JsonDocument initializeResponse = await session.SendRequestAsync(
                "initialize",
                new
                {
                    protocolVersion = "2025-11-25",
                    capabilities = new { },
                    clientInfo = new
                    {
                        name = "ComputerUseWin.SharedRuntimeFoundationTests",
                        version = "0.1.0",
                    },
                },
                "initialize");

            await session.SendNotificationAsync("notifications/initialized");

            using JsonDocument state = JsonDocument.Parse(File.ReadAllText(GetExpectedSharedRuntimeStatePath(codexHome)));
            Assert.Equal(mismatchedVersion, state.RootElement.GetProperty("version").GetString());
        }
        finally
        {
            DeleteDirectoryIfExists(outputRoot);
            DeleteDirectoryIfExists(tempPluginRoot);
            DeleteDirectoryIfExists(codexHome);
        }
    }

    [Fact]
    public async Task ComputerUseWinLauncherRehydratesSharedRuntimeStoreWhenNoUsableRuntimeExists()
    {
        string repoRoot = GetRepositoryRoot();
        string publishScriptPath = GetPublishScriptPath(repoRoot);
        string packageScriptPath = Path.Combine(repoRoot, "scripts", "codex", "package-computer-use-win-runtime-release.ps1");
        string sourcePluginRoot = Path.Combine(repoRoot, "plugins", "computer-use-win");
        string pluginRuntimeRoot = Path.Combine(sourcePluginRoot, "runtime", "win-x64");
        string outputRoot = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "launcher-rehydrate-shared-runtime", Guid.NewGuid().ToString("N"));
        string tempPluginRoot = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "launcher-rehydrate-shared-runtime-plugin-copy", Guid.NewGuid().ToString("N"));
        string codexHome = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "codex-home-rehydrate-shared-runtime", Guid.NewGuid().ToString("N"));
        const string version = "0.1.0";

        EnsurePublishedRuntimeBundle(repoRoot, publishScriptPath, pluginRuntimeRoot);

        try
        {
            string archivePath = PackageRuntimeRelease(repoRoot, packageScriptPath, pluginRuntimeRoot, outputRoot, version);
            string descriptorPath = CreateRuntimeReleaseDescriptor(outputRoot, version, archivePath, "win-x64");

            CopyDirectory(sourcePluginRoot, tempPluginRoot, _ => true);
            DeleteDirectoryIfExists(Path.Combine(tempPluginRoot, "runtime", "win-x64"));

            await using PluginLauncherSession launcher = StartPluginLauncherSession(tempPluginRoot, descriptorPath, codexHome);
            PluginMcpSession session = launcher.CreateMcpSession();

            using JsonDocument initializeResponse = await session.SendRequestAsync(
                "initialize",
                new
                {
                    protocolVersion = "2025-11-25",
                    capabilities = new { },
                    clientInfo = new
                    {
                        name = "ComputerUseWin.SharedRuntimeFoundationTests",
                        version = "0.1.0",
                    },
                },
                "initialize");

            await session.SendNotificationAsync("notifications/initialized");

            string sharedRuntimeRoot = GetExpectedSharedRuntimeRoot(codexHome, "win-x64", version);
            string statePath = GetExpectedSharedRuntimeStatePath(codexHome);
            Assert.True(File.Exists(Path.Combine(sharedRuntimeRoot, "Okno.Server.exe")));
            Assert.True(File.Exists(statePath));

            using JsonDocument state = JsonDocument.Parse(File.ReadAllText(statePath));
            Assert.Equal(version, state.RootElement.GetProperty("version").GetString());
            Assert.Equal(sharedRuntimeRoot, state.RootElement.GetProperty("runtimeRoot").GetString());
        }
        finally
        {
            DeleteDirectoryIfExists(outputRoot);
            DeleteDirectoryIfExists(tempPluginRoot);
            DeleteDirectoryIfExists(codexHome);
        }
    }
}
