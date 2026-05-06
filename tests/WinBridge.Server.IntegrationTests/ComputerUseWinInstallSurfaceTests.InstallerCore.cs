// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;

namespace WinBridge.Server.IntegrationTests;

public sealed partial class ComputerUseWinInstallSurfaceTests
{
    [Fact]
    public void SetupCliInstallRuntimeOnlyDoesNotTouchMarketplace()
    {
        string repoRoot = GetRepositoryRoot();
        string publishScriptPath = GetPublishScriptPath(repoRoot);
        string runtimePackageScriptPath = Path.Combine(repoRoot, "scripts", "codex", "package-computer-use-win-runtime-release.ps1");
        string sourcePluginRoot = Path.Combine(repoRoot, "plugins", "computer-use-win");
        string runtimeRoot = Path.Combine(sourcePluginRoot, "runtime", "win-x64");
        string outputRoot = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "setup-cli-install-runtime-only", Guid.NewGuid().ToString("N"));
        string userProfile = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "user-profile-install-runtime-only", Guid.NewGuid().ToString("N"));
        string codexHome = Path.Combine(userProfile, ".codex");
        const string version = "0.1.0";

        EnsurePublishedRuntimeBundle(repoRoot, publishScriptPath, runtimeRoot);

        try
        {
            string archivePath = PackageRuntimeRelease(repoRoot, runtimePackageScriptPath, runtimeRoot, outputRoot, version);
            string descriptorPath = CreateRuntimeReleaseDescriptor(outputRoot, version, archivePath, "win-x64");

            ScriptInvocationResult result = InvokeSetupCli(
                repoRoot,
                ["install", "runtime-only", "--descriptor-path", descriptorPath, "--json"],
                codexHome,
                userProfile);

            Assert.True(result.ExitCode == 0, $"Setup CLI runtime-only install failed. stderr='{result.Stderr}', stdout='{result.Stdout}'.");
            Assert.False(File.Exists(GetExpectedPersonalMarketplacePath(userProfile)));
            Assert.True(File.Exists(GetExpectedRuntimeOnlyReceiptPath(codexHome)));
            Assert.False(File.Exists(GetExpectedCodexReceiptPath(codexHome)));
        }
        finally
        {
            DeleteDirectoryIfExists(outputRoot);
            DeleteDirectoryIfExists(codexHome);
            DeleteDirectoryIfExists(userProfile);
        }
    }

    [Fact]
    public void SetupCliInstallCodexCreatesPluginSourceAndMarketplaceEntry()
    {
        string repoRoot = GetRepositoryRoot();
        string publishScriptPath = GetPublishScriptPath(repoRoot);
        string runtimePackageScriptPath = Path.Combine(repoRoot, "scripts", "codex", "package-computer-use-win-runtime-release.ps1");
        string pluginPackageScriptPath = Path.Combine(repoRoot, "scripts", "codex", "package-computer-use-win-plugin-release.ps1");
        string sourcePluginRoot = Path.Combine(repoRoot, "plugins", "computer-use-win");
        string runtimeRoot = Path.Combine(sourcePluginRoot, "runtime", "win-x64");
        string outputRoot = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "setup-cli-install-codex", Guid.NewGuid().ToString("N"));
        string userProfile = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "user-profile-install-codex", Guid.NewGuid().ToString("N"));
        string codexHome = Path.Combine(userProfile, ".codex");
        const string version = "0.1.0";

        EnsurePublishedRuntimeBundle(repoRoot, publishScriptPath, runtimeRoot);

        try
        {
            string runtimeArchivePath = PackageRuntimeRelease(repoRoot, runtimePackageScriptPath, runtimeRoot, outputRoot, version);
            string runtimeDescriptorPath = CreateRuntimeReleaseDescriptor(outputRoot, version, runtimeArchivePath, "win-x64");
            PackagePluginRelease(repoRoot, pluginPackageScriptPath, outputRoot, version);

            ScriptInvocationResult result = InvokeSetupCli(
                repoRoot,
                ["install", "codex", "--descriptor-path", runtimeDescriptorPath, "--json"],
                codexHome,
                userProfile);

            Assert.True(result.ExitCode == 0, $"Setup CLI codex install failed. stderr='{result.Stderr}', stdout='{result.Stdout}'.");
            using JsonDocument payload = JsonDocument.Parse(result.Stdout);
            Assert.True(payload.RootElement.GetProperty("restartRequired").GetBoolean());

            string expectedPluginRoot = GetExpectedInstalledPluginRoot(codexHome);
            string expectedMarketplacePath = GetExpectedPersonalMarketplacePath(userProfile);
            Assert.True(File.Exists(Path.Combine(expectedPluginRoot, ".codex-plugin", "plugin.json")));
            Assert.True(File.Exists(expectedMarketplacePath));
            Assert.True(File.Exists(GetExpectedCodexReceiptPath(codexHome)));

            string marketplaceText = File.ReadAllText(expectedMarketplacePath);
            Assert.Contains("computer-use-win", marketplaceText, StringComparison.Ordinal);
            Assert.Contains("./.codex/plugins/computer-use-win", marketplaceText, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectoryIfExists(outputRoot);
            DeleteDirectoryIfExists(codexHome);
            DeleteDirectoryIfExists(userProfile);
        }
    }

    [Fact]
    public void SetupCliUpdateCodexPreservesUnrelatedMarketplaceEntries()
    {
        string repoRoot = GetRepositoryRoot();
        string publishScriptPath = GetPublishScriptPath(repoRoot);
        string runtimePackageScriptPath = Path.Combine(repoRoot, "scripts", "codex", "package-computer-use-win-runtime-release.ps1");
        string pluginPackageScriptPath = Path.Combine(repoRoot, "scripts", "codex", "package-computer-use-win-plugin-release.ps1");
        string sourcePluginRoot = Path.Combine(repoRoot, "plugins", "computer-use-win");
        string runtimeRoot = Path.Combine(sourcePluginRoot, "runtime", "win-x64");
        string outputRoot = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "setup-cli-update-codex", Guid.NewGuid().ToString("N"));
        string userProfile = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "user-profile-update-codex", Guid.NewGuid().ToString("N"));
        string codexHome = Path.Combine(userProfile, ".codex");
        const string version = "0.1.0";

        EnsurePublishedRuntimeBundle(repoRoot, publishScriptPath, runtimeRoot);

        try
        {
            string runtimeArchivePath = PackageRuntimeRelease(repoRoot, runtimePackageScriptPath, runtimeRoot, outputRoot, version);
            string runtimeDescriptorPath = CreateRuntimeReleaseDescriptor(outputRoot, version, runtimeArchivePath, "win-x64");
            PackagePluginRelease(repoRoot, pluginPackageScriptPath, outputRoot, version);

            ScriptInvocationResult installResult = InvokeSetupCli(
                repoRoot,
                ["install", "codex", "--descriptor-path", runtimeDescriptorPath, "--json"],
                codexHome,
                userProfile);
            Assert.True(installResult.ExitCode == 0, $"Setup CLI codex install failed. stderr='{installResult.Stderr}'");

            string marketplacePath = GetExpectedPersonalMarketplacePath(userProfile);
            File.WriteAllText(
                marketplacePath,
                """
                {
                  "name": "okno-local-installed",
                  "interface": { "displayName": "Okno: Installed plugins" },
                  "plugins": [
                    {
                      "name": "other-plugin",
                      "source": { "source": "local", "path": "./.codex/plugins/other-plugin" },
                      "policy": { "installation": "AVAILABLE", "authentication": "ON_INSTALL" },
                      "category": "Productivity"
                    },
                    {
                      "name": "computer-use-win",
                      "source": { "source": "local", "path": "./.codex/plugins/computer-use-win" },
                      "policy": { "installation": "AVAILABLE", "authentication": "ON_INSTALL" },
                      "category": "Productivity"
                    }
                  ]
                }
                """);

            ScriptInvocationResult updateResult = InvokeSetupCli(
                repoRoot,
                ["update", "codex", "--descriptor-path", runtimeDescriptorPath, "--json"],
                codexHome,
                userProfile);

            Assert.True(updateResult.ExitCode == 0, $"Setup CLI codex update failed. stderr='{updateResult.Stderr}', stdout='{updateResult.Stdout}'.");
            string marketplaceText = File.ReadAllText(marketplacePath);
            Assert.Contains("other-plugin", marketplaceText, StringComparison.Ordinal);
            Assert.Contains("computer-use-win", marketplaceText, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectoryIfExists(outputRoot);
            DeleteDirectoryIfExists(codexHome);
            DeleteDirectoryIfExists(userProfile);
        }
    }

    [Fact]
    public void SetupCliUninstallCodexRemovesPluginEntryAndKeepsRuntimeWhenRuntimeOnlyReceiptExists()
    {
        string repoRoot = GetRepositoryRoot();
        string publishScriptPath = GetPublishScriptPath(repoRoot);
        string runtimePackageScriptPath = Path.Combine(repoRoot, "scripts", "codex", "package-computer-use-win-runtime-release.ps1");
        string pluginPackageScriptPath = Path.Combine(repoRoot, "scripts", "codex", "package-computer-use-win-plugin-release.ps1");
        string sourcePluginRoot = Path.Combine(repoRoot, "plugins", "computer-use-win");
        string runtimeRoot = Path.Combine(sourcePluginRoot, "runtime", "win-x64");
        string outputRoot = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "setup-cli-uninstall-codex", Guid.NewGuid().ToString("N"));
        string userProfile = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "user-profile-uninstall-codex", Guid.NewGuid().ToString("N"));
        string codexHome = Path.Combine(userProfile, ".codex");
        const string version = "0.1.0";

        EnsurePublishedRuntimeBundle(repoRoot, publishScriptPath, runtimeRoot);

        try
        {
            string runtimeArchivePath = PackageRuntimeRelease(repoRoot, runtimePackageScriptPath, runtimeRoot, outputRoot, version);
            string runtimeDescriptorPath = CreateRuntimeReleaseDescriptor(outputRoot, version, runtimeArchivePath, "win-x64");
            PackagePluginRelease(repoRoot, pluginPackageScriptPath, outputRoot, version);

            Assert.True(
                InvokeSetupCli(repoRoot, ["install", "runtime-only", "--descriptor-path", runtimeDescriptorPath, "--json"], codexHome, userProfile).ExitCode == 0);
            Assert.True(
                InvokeSetupCli(repoRoot, ["install", "codex", "--descriptor-path", runtimeDescriptorPath, "--json"], codexHome, userProfile).ExitCode == 0);

            ScriptInvocationResult uninstallResult = InvokeSetupCli(
                repoRoot,
                ["uninstall", "codex", "--json"],
                codexHome,
                userProfile);

            Assert.True(uninstallResult.ExitCode == 0, $"Setup CLI codex uninstall failed. stderr='{uninstallResult.Stderr}', stdout='{uninstallResult.Stdout}'.");
            Assert.False(Directory.Exists(GetExpectedInstalledPluginRoot(codexHome)));
            Assert.False(File.Exists(GetExpectedCodexReceiptPath(codexHome)));
            Assert.True(Directory.Exists(GetExpectedSharedRuntimeStoreRoot(codexHome)));
            string marketplaceText = File.ReadAllText(GetExpectedPersonalMarketplacePath(userProfile));
            Assert.DoesNotContain("\"name\": \"computer-use-win\"", marketplaceText, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectoryIfExists(outputRoot);
            DeleteDirectoryIfExists(codexHome);
            DeleteDirectoryIfExists(userProfile);
        }
    }

    [Fact]
    public void SetupCliRepairCodexRestoresMissingPluginSourceAndMarketplaceEntry()
    {
        string repoRoot = GetRepositoryRoot();
        string publishScriptPath = GetPublishScriptPath(repoRoot);
        string runtimePackageScriptPath = Path.Combine(repoRoot, "scripts", "codex", "package-computer-use-win-runtime-release.ps1");
        string pluginPackageScriptPath = Path.Combine(repoRoot, "scripts", "codex", "package-computer-use-win-plugin-release.ps1");
        string sourcePluginRoot = Path.Combine(repoRoot, "plugins", "computer-use-win");
        string runtimeRoot = Path.Combine(sourcePluginRoot, "runtime", "win-x64");
        string outputRoot = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "setup-cli-repair-codex", Guid.NewGuid().ToString("N"));
        string userProfile = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "user-profile-repair-codex", Guid.NewGuid().ToString("N"));
        string codexHome = Path.Combine(userProfile, ".codex");
        const string version = "0.1.0";

        EnsurePublishedRuntimeBundle(repoRoot, publishScriptPath, runtimeRoot);

        try
        {
            string runtimeArchivePath = PackageRuntimeRelease(repoRoot, runtimePackageScriptPath, runtimeRoot, outputRoot, version);
            string runtimeDescriptorPath = CreateRuntimeReleaseDescriptor(outputRoot, version, runtimeArchivePath, "win-x64");
            PackagePluginRelease(repoRoot, pluginPackageScriptPath, outputRoot, version);

            Assert.True(
                InvokeSetupCli(repoRoot, ["install", "codex", "--descriptor-path", runtimeDescriptorPath, "--json"], codexHome, userProfile).ExitCode == 0);

            DeleteDirectoryIfExists(GetExpectedInstalledPluginRoot(codexHome));
            File.Delete(GetExpectedPersonalMarketplacePath(userProfile));

            ScriptInvocationResult repairResult = InvokeSetupCli(
                repoRoot,
                ["repair", "codex", "--descriptor-path", runtimeDescriptorPath, "--json"],
                codexHome,
                userProfile);

            Assert.True(repairResult.ExitCode == 0, $"Setup CLI codex repair failed. stderr='{repairResult.Stderr}', stdout='{repairResult.Stdout}'.");
            Assert.True(File.Exists(Path.Combine(GetExpectedInstalledPluginRoot(codexHome), ".codex-plugin", "plugin.json")));
            Assert.True(File.Exists(GetExpectedPersonalMarketplacePath(userProfile)));
        }
        finally
        {
            DeleteDirectoryIfExists(outputRoot);
            DeleteDirectoryIfExists(codexHome);
            DeleteDirectoryIfExists(userProfile);
        }
    }

    private static string PackagePluginRelease(string repoRoot, string packageScriptPath, string outputRoot, string version)
    {
        ScriptInvocationResult result = InvokePowerShellScript(
            packageScriptPath,
            repoRoot,
            startInfo =>
            {
                startInfo.ArgumentList.Add("-Version");
                startInfo.ArgumentList.Add(version);
                startInfo.ArgumentList.Add("-OutputRoot");
                startInfo.ArgumentList.Add(outputRoot);
            });

        if (result.ExitCode != 0)
        {
            throw new Xunit.Sdk.XunitException($"Plugin release packaging script failed. stderr='{result.Stderr}', stdout='{result.Stdout}'.");
        }

        using JsonDocument payload = JsonDocument.Parse(result.Stdout);
        return payload.RootElement.GetProperty("archivePath").GetString()
            ?? throw new InvalidOperationException("archivePath missing.");
    }
}
