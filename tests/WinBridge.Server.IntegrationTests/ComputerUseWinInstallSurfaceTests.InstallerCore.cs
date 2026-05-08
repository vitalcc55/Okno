// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;
using WinBridge.Setup.Core;

namespace WinBridge.Server.IntegrationTests;

public sealed partial class ComputerUseWinInstallSurfaceTests
{
    private const string SetupCliReleaseVersion = "0.2.1";
    private const string WindowsRuntimeIdentifier = "win-x64";

    private static readonly object ReleasePackagingGate = new();
    private static readonly Lazy<PublishedRuntimeBundle> SharedPublishedRuntimeBundle = new(CreateSharedPublishedRuntimeBundle);
    private static readonly Lazy<SetupCliReleaseArtifacts> SharedRuntimeOnlyRelease = new(
        () => CreateSetupCliReleaseArtifacts("setup-cli-runtime-only-release", packagePluginRelease: false));
    private static readonly Lazy<SetupCliReleaseArtifacts> SharedCodexRelease = new(
        () => CreateSetupCliReleaseArtifacts("setup-cli-codex-release", packagePluginRelease: true));

    private const string MarketplaceWithOtherPluginEntry = """
        {
          "name": "okno-local-installed",
          "interface": { "displayName": "Okno: Installed plugins" },
          "plugins": [
            { "name": "other-plugin", "source": { "source": "local", "path": "./.codex/plugins/other-plugin" }, "policy": { "installation": "AVAILABLE", "authentication": "ON_INSTALL" }, "category": "Productivity" },
            { "name": "computer-use-win", "source": { "source": "local", "path": "./.codex/plugins/computer-use-win" }, "policy": { "installation": "AVAILABLE", "authentication": "ON_INSTALL" }, "category": "Productivity" }
          ]
        }
        """;

    [Fact]
    public void SetupCliInstallRuntimeOnlyDoesNotTouchMarketplace()
    {
        using SetupCliTestHarness test = CreateRuntimeOnlySetupCliTestHarness("install-runtime-only");

        ScriptInvocationResult result = test.RunSetupCliJsonWithRuntimeDescriptor("install", "runtime-only");

        AssertSetupCliSucceeded(result, "runtime-only install");
        Assert.False(File.Exists(GetExpectedPersonalMarketplacePath(test.UserProfile)));
        Assert.True(File.Exists(GetExpectedRuntimeOnlyReceiptPath(test.CodexHome)));
        Assert.False(File.Exists(GetExpectedCodexReceiptPath(test.CodexHome)));
    }

    [Fact]
    public void SetupCliInstallCodexCreatesPluginSourceAndMarketplaceEntry()
    {
        using SetupCliTestHarness test = CreateCodexSetupCliTestHarness("install-codex");

        ScriptInvocationResult result = test.RunSetupCliJsonWithRuntimeDescriptor("install", "codex");

        AssertSetupCliSucceeded(result, "codex install");
        using JsonDocument payload = JsonDocument.Parse(result.Stdout);
        Assert.True(payload.RootElement.GetProperty("restartRequired").GetBoolean());

        string expectedMarketplacePath = GetExpectedPersonalMarketplacePath(test.UserProfile);
        Assert.True(File.Exists(GetExpectedInstalledPluginDescriptorPath(test.CodexHome)));
        Assert.True(File.Exists(expectedMarketplacePath));
        Assert.True(File.Exists(GetExpectedCodexReceiptPath(test.CodexHome)));

        string marketplaceText = File.ReadAllText(expectedMarketplacePath);
        Assert.Contains("computer-use-win", marketplaceText, StringComparison.Ordinal);
        Assert.Contains("./.codex/plugins/computer-use-win", marketplaceText, StringComparison.Ordinal);
    }

    [Fact]
    public void SetupCliUpdateCodexPreservesUnrelatedMarketplaceEntries()
    {
        using SetupCliTestHarness test = CreateCodexSetupCliTestHarness("update-codex");

        ScriptInvocationResult installResult = test.RunSetupCliJsonWithRuntimeDescriptor("install", "codex");
        AssertSetupCliSucceeded(installResult, "codex install");

        string marketplacePath = GetExpectedPersonalMarketplacePath(test.UserProfile);
        File.WriteAllText(marketplacePath, MarketplaceWithOtherPluginEntry);

        ScriptInvocationResult updateResult = test.RunSetupCliJsonWithRuntimeDescriptor("update", "codex");

        AssertSetupCliSucceeded(updateResult, "codex update");
        string marketplaceText = File.ReadAllText(marketplacePath);
        Assert.Contains("other-plugin", marketplaceText, StringComparison.Ordinal);
        Assert.Contains("computer-use-win", marketplaceText, StringComparison.Ordinal);
    }

    [Fact]
    public void SetupCliUninstallCodexRemovesPluginEntryAndKeepsRuntimeWhenRuntimeOnlyReceiptExists()
    {
        using SetupCliTestHarness test = CreateCodexSetupCliTestHarness("uninstall-codex");

        AssertSetupCliSucceeded(test.RunSetupCliJsonWithRuntimeDescriptor("install", "runtime-only"), "runtime-only install");
        AssertSetupCliSucceeded(test.RunSetupCliJsonWithRuntimeDescriptor("install", "codex"), "codex install");
        WriteCodexConfigWithComputerUseWinEntries(test.CodexHome);

        ScriptInvocationResult uninstallResult = test.RunSetupCliJson("uninstall", "codex");

        AssertSetupCliSucceeded(uninstallResult, "codex uninstall");
        Assert.False(Directory.Exists(GetExpectedInstalledPluginRoot(test.CodexHome)));
        Assert.False(File.Exists(GetExpectedCodexReceiptPath(test.CodexHome)));
        Assert.True(Directory.Exists(GetExpectedSharedRuntimeStoreRoot(test.CodexHome)));

        string marketplaceText = File.ReadAllText(GetExpectedPersonalMarketplacePath(test.UserProfile));
        Assert.DoesNotContain("\"name\": \"computer-use-win\"", marketplaceText, StringComparison.Ordinal);
        string configAfterCodexUninstall = File.ReadAllText(GetExpectedCodexConfigPath(test.CodexHome));
        Assert.DoesNotContain("computer-use-win@okno-local-installed", configAfterCodexUninstall, StringComparison.Ordinal);
        Assert.DoesNotContain("computer_use_win", configAfterCodexUninstall, StringComparison.Ordinal);
        Assert.DoesNotContain("computer-use-win\"] # Legacy Okno alias", configAfterCodexUninstall, StringComparison.Ordinal);
    }

    [Fact]
    public void SetupCliRepairCodexRestoresMissingPluginSourceAndMarketplaceEntry()
    {
        using SetupCliTestHarness test = CreateCodexSetupCliTestHarness("repair-codex");

        AssertSetupCliSucceeded(test.RunSetupCliJsonWithRuntimeDescriptor("install", "codex"), "codex install");

        DeleteDirectoryIfExists(GetExpectedInstalledPluginRoot(test.CodexHome));
        File.Delete(GetExpectedPersonalMarketplacePath(test.UserProfile));

        ScriptInvocationResult repairResult = test.RunSetupCliJsonWithRuntimeDescriptor("repair", "codex");

        AssertSetupCliSucceeded(repairResult, "codex repair");
        Assert.True(File.Exists(GetExpectedInstalledPluginDescriptorPath(test.CodexHome)));
        Assert.True(File.Exists(GetExpectedPersonalMarketplacePath(test.UserProfile)));
    }

    [Fact]
    public void SetupCliRuntimeOnlySnippetStaysStableAcrossRuntimeUpdate()
    {
        using SetupCliTestHarness test = CreateRuntimeOnlySetupCliTestHarness("runtime-only-stable-snippet");

        ScriptInvocationResult installResult = test.RunSetupCliJsonWithRuntimeDescriptor("install", "runtime-only");
        AssertSetupCliSucceeded(installResult, "runtime-only install");

        using JsonDocument installPayload = JsonDocument.Parse(installResult.Stdout);
        string installSnippet = installPayload.RootElement.GetProperty("snippet").GetString()
            ?? throw new InvalidOperationException("runtime-only install snippet missing.");

        string repoRoot = GetRepositoryRoot();
        string outputRoot = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "runtime-only-stable-snippet-update", Guid.NewGuid().ToString("N"));
        RuntimeReleasePackageResult updatedRuntimePackage = PackageRuntimeRelease(
            repoRoot,
            GetRuntimePackageScriptPath(repoRoot),
            SharedPublishedRuntimeBundle.Value.RuntimeRoot,
            outputRoot,
            "0.2.1-test");

        try
        {
            ScriptInvocationResult updateResult = test.RunSetupCliJsonWithDescriptorPath(updatedRuntimePackage.DescriptorPath, "update", "runtime-only");
            AssertSetupCliSucceeded(updateResult, "runtime-only update");

            using JsonDocument updatePayload = JsonDocument.Parse(updateResult.Stdout);
            string updateSnippet = updatePayload.RootElement.GetProperty("snippet").GetString()
                ?? throw new InvalidOperationException("runtime-only update snippet missing.");
            Assert.Equal(installSnippet, updateSnippet);

            using JsonDocument snippetJson = JsonDocument.Parse(updateSnippet);
            JsonElement server = snippetJson.RootElement.GetProperty("mcpServers").GetProperty("computer-use-win");
            Assert.Equal("powershell.exe", server.GetProperty("command").GetString());

            string expectedLauncherPath = GetExpectedSharedRuntimeLauncherScriptPath(test.CodexHome);
            string[] args = server.GetProperty("args").EnumerateArray().Select(static value => value.GetString() ?? string.Empty).ToArray();
            Assert.Contains(expectedLauncherPath, args, StringComparer.Ordinal);
            Assert.DoesNotContain("0.2.1", updateSnippet, StringComparison.Ordinal);
            Assert.DoesNotContain("0.2.1-test", updateSnippet, StringComparison.Ordinal);
            Assert.True(File.Exists(expectedLauncherPath));

            string launcherScript = File.ReadAllText(expectedLauncherPath);
            Assert.Contains("current-runtime.json", launcherScript, StringComparison.Ordinal);
            Assert.DoesNotContain("0.2.1", launcherScript, StringComparison.Ordinal);
            Assert.DoesNotContain("0.2.1-test", launcherScript, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectoryIfExists(outputRoot);
        }
    }

    [Fact]
    public void SetupCliRepairCodexFailsClosedWhenMarketplaceIsMalformed()
    {
        using SetupCliTestHarness test = CreateCodexSetupCliTestHarness("repair-codex-malformed-marketplace");

        AssertSetupCliSucceeded(test.RunSetupCliJsonWithRuntimeDescriptor("install", "codex"), "codex install");

        string marketplacePath = GetExpectedPersonalMarketplacePath(test.UserProfile);
        const string malformedMarketplace = "{ invalid json";
        File.WriteAllText(marketplacePath, malformedMarketplace);
        DeleteDirectoryIfExists(GetExpectedInstalledPluginRoot(test.CodexHome));

        ScriptInvocationResult repairResult = test.RunSetupCliJsonWithRuntimeDescriptor("repair", "codex");

        Assert.NotEqual(0, repairResult.ExitCode);
        using JsonDocument payload = JsonDocument.Parse(repairResult.Stdout);
        Assert.Contains("malformed", payload.RootElement.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(malformedMarketplace, File.ReadAllText(marketplacePath));
        Assert.False(Directory.Exists(GetExpectedInstalledPluginRoot(test.CodexHome)));
    }

    [Fact]
    public void SetupCliUninstallAllRemovesRuntimeOnlyInstall()
    {
        using SetupCliTestHarness test = CreateRuntimeOnlySetupCliTestHarness("uninstall-all-runtime-only");

        AssertSetupCliSucceeded(test.RunSetupCliJsonWithRuntimeDescriptor("install", "runtime-only"), "runtime-only install");
        WriteCodexConfigWithComputerUseWinEntries(test.CodexHome);

        ComputerUseWinInstallerService installer = new(
            new ComputerUseWinRuntimeFoundationService(
                new ComputerUseWinRuntimeStorePaths(test.CodexHome, Path.Combine(test.UserProfile, "AppData", "Local"))),
            test.UserProfile);
        ComputerUseWinInstallerResult result = installer.UninstallAll();

        Assert.Equal("remove-all", result.Action);
        Assert.False(File.Exists(GetExpectedRuntimeOnlyReceiptPath(test.CodexHome)));
        Assert.False(Directory.Exists(GetExpectedSharedRuntimeStoreRoot(test.CodexHome)));
        string configText = File.ReadAllText(GetExpectedCodexConfigPath(test.CodexHome));
        Assert.Contains("[plugins.\"computer-use-win@okno-local-installed\"] # Okno plugin", configText, StringComparison.Ordinal);
        Assert.Contains("[mcp_servers.\"computer_use_win\"] # Okno launcher", configText, StringComparison.Ordinal);
        Assert.Contains("[mcp_servers.\"computer-use-win\"] # Legacy Okno alias", configText, StringComparison.Ordinal);
    }

    [Fact]
    public void SetupCliUninstallAllRemovesCodexInstall()
    {
        using SetupCliTestHarness test = CreateCodexSetupCliTestHarness("uninstall-all-codex");

        AssertSetupCliSucceeded(test.RunSetupCliJsonWithRuntimeDescriptor("install", "codex"), "codex install");

        ComputerUseWinInstallerService installer = new(
            new ComputerUseWinRuntimeFoundationService(
                new ComputerUseWinRuntimeStorePaths(test.CodexHome, Path.Combine(test.UserProfile, "AppData", "Local"))),
            test.UserProfile);
        ComputerUseWinInstallerResult result = installer.UninstallAll();

        Assert.Equal("remove-all", result.Action);
        Assert.False(File.Exists(GetExpectedCodexReceiptPath(test.CodexHome)));
        Assert.False(Directory.Exists(GetExpectedInstalledPluginRoot(test.CodexHome)));
        string marketplacePath = GetExpectedPersonalMarketplacePath(test.UserProfile);
        if (File.Exists(marketplacePath))
        {
            Assert.DoesNotContain("computer-use-win", File.ReadAllText(marketplacePath), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void SetupCliUninstallAllRemovesBothInstallModes()
    {
        using SetupCliTestHarness test = CreateCodexSetupCliTestHarness("uninstall-all-both");

        AssertSetupCliSucceeded(test.RunSetupCliJsonWithRuntimeDescriptor("install", "runtime-only"), "runtime-only install");
        AssertSetupCliSucceeded(test.RunSetupCliJsonWithRuntimeDescriptor("install", "codex"), "codex install");
        WriteCodexConfigWithComputerUseWinEntries(test.CodexHome);

        ComputerUseWinInstallerService installer = new(
            new ComputerUseWinRuntimeFoundationService(
                new ComputerUseWinRuntimeStorePaths(test.CodexHome, Path.Combine(test.UserProfile, "AppData", "Local"))),
            test.UserProfile);
        installer.UninstallAll();

        Assert.False(File.Exists(GetExpectedRuntimeOnlyReceiptPath(test.CodexHome)));
        Assert.False(File.Exists(GetExpectedCodexReceiptPath(test.CodexHome)));
        Assert.False(Directory.Exists(GetExpectedInstalledPluginRoot(test.CodexHome)));
        Assert.False(Directory.Exists(GetExpectedSharedRuntimeStoreRoot(test.CodexHome)));
        string configText = File.ReadAllText(GetExpectedCodexConfigPath(test.CodexHome));
        Assert.DoesNotContain("computer-use-win@okno-local-installed", configText, StringComparison.Ordinal);
        Assert.DoesNotContain("computer_use_win", configText, StringComparison.Ordinal);
        Assert.DoesNotContain("computer-use-win\"] # Legacy Okno alias", configText, StringComparison.Ordinal);
        Assert.Contains("[plugins.\"other-plugin@local\"]", configText, StringComparison.Ordinal);
    }

    [Fact]
    public void SetupCliUninstallAllIgnoresMalformedMarketplaceAndStillRemovesOwnedState()
    {
        using SetupCliTestHarness test = CreateCodexSetupCliTestHarness("uninstall-all-malformed-marketplace");

        AssertSetupCliSucceeded(test.RunSetupCliJsonWithRuntimeDescriptor("install", "codex"), "codex install");

        string marketplacePath = GetExpectedPersonalMarketplacePath(test.UserProfile);
        const string malformedMarketplace = "{ malformed";
        File.WriteAllText(marketplacePath, malformedMarketplace);

        ComputerUseWinInstallerService installer = new(
            new ComputerUseWinRuntimeFoundationService(
                new ComputerUseWinRuntimeStorePaths(test.CodexHome, Path.Combine(test.UserProfile, "AppData", "Local"))),
            test.UserProfile);
        ComputerUseWinInstallerResult result = installer.UninstallAll();

        Assert.Equal("remove-all", result.Action);
        Assert.Equal(malformedMarketplace, File.ReadAllText(marketplacePath));
        Assert.False(File.Exists(GetExpectedCodexReceiptPath(test.CodexHome)));
        Assert.False(Directory.Exists(GetExpectedInstalledPluginRoot(test.CodexHome)));
        Assert.False(Directory.Exists(GetExpectedSharedRuntimeStoreRoot(test.CodexHome)));
    }

    private static SetupCliTestHarness CreateRuntimeOnlySetupCliTestHarness(string scenarioName) => CreateSetupCliTestHarness(SharedRuntimeOnlyRelease.Value, scenarioName);

    private static SetupCliTestHarness CreateCodexSetupCliTestHarness(string scenarioName) => CreateSetupCliTestHarness(SharedCodexRelease.Value, scenarioName);

    private static string GetExpectedCodexConfigPath(string codexHome) => Path.Combine(codexHome, "config.toml");

    private static void WriteCodexConfigWithComputerUseWinEntries(string codexHome)
    {
        Directory.CreateDirectory(codexHome);
        File.WriteAllText(
            GetExpectedCodexConfigPath(codexHome),
            """
            [plugins."other-plugin@local"]
            enabled = true

            [plugins."computer-use-win@okno-local-installed"] # Okno plugin
            enabled = true

            [mcp_servers."computer_use_win"] # Okno launcher
            command = 'powershell'
            args = ['-File', 'C:\Users\test\.codex\plugins\computer-use-win\run-computer-use-win-mcp.ps1']
            enabled = true

            [mcp_servers."computer-use-win"] # Legacy Okno alias
            command = 'powershell'
            args = ['-File', 'C:\Users\test\.codex\plugins\computer-use-win\run-computer-use-win-mcp.ps1']
            enabled = true

            [mcp_servers.other]
            command = 'other'
            """);
    }

    private static SetupCliTestHarness CreateSetupCliTestHarness(SetupCliReleaseArtifacts release, string scenarioName)
    {
        string userProfile = Path.Combine(
            release.RepoRoot,
            ".tmp",
            ".codex",
            "tests",
            $"user-profile-{scenarioName}",
            Guid.NewGuid().ToString("N"));

        return new SetupCliTestHarness(release, userProfile);
    }

    private static PublishedRuntimeBundle CreateSharedPublishedRuntimeBundle()
    {
        string repoRoot = GetRepositoryRoot();
        string runtimeRoot = Path.Combine(repoRoot, "plugins", "computer-use-win", "runtime", WindowsRuntimeIdentifier);

        EnsurePublishedRuntimeBundle(repoRoot, GetPublishScriptPath(repoRoot), runtimeRoot);
        return new PublishedRuntimeBundle(repoRoot, runtimeRoot);
    }

    private static SetupCliReleaseArtifacts CreateSetupCliReleaseArtifacts(string outputDirectoryName, bool packagePluginRelease)
    {
        PublishedRuntimeBundle runtimeBundle = SharedPublishedRuntimeBundle.Value;
        string outputRoot = Path.Combine(
            runtimeBundle.RepoRoot,
            ".tmp",
            ".codex",
            "tests",
            outputDirectoryName,
            Guid.NewGuid().ToString("N"));

        try
        {
            string runtimeDescriptorPath;
            lock (ReleasePackagingGate)
            {
                RuntimeReleasePackageResult runtimePackage = PackageRuntimeRelease(
                    runtimeBundle.RepoRoot,
                    GetRuntimePackageScriptPath(runtimeBundle.RepoRoot),
                    runtimeBundle.RuntimeRoot,
                    outputRoot,
                    SetupCliReleaseVersion);

                runtimeDescriptorPath = runtimePackage.DescriptorPath;

                if (packagePluginRelease)
                {
                    PackagePluginRelease(
                        runtimeBundle.RepoRoot,
                        GetPluginPackageScriptPath(runtimeBundle.RepoRoot),
                        outputRoot,
                        SetupCliReleaseVersion,
                        runtimePackage.ResultPath);
                }
            }

            RegisterProcessExitCleanup(outputRoot);
            return new SetupCliReleaseArtifacts(runtimeBundle.RepoRoot, runtimeDescriptorPath);
        }
        catch
        {
            DeleteDirectoryIfExists(outputRoot);
            throw;
        }
    }

    private static string GetRuntimePackageScriptPath(string repoRoot) =>
        Path.Combine(repoRoot, "scripts", "codex", "package-computer-use-win-runtime-release.ps1");

    private static string GetPluginPackageScriptPath(string repoRoot) =>
        Path.Combine(repoRoot, "scripts", "codex", "package-computer-use-win-plugin-release.ps1");

    private static string GetExpectedInstalledPluginDescriptorPath(string codexHome) =>
        Path.Combine(GetExpectedInstalledPluginRoot(codexHome), ".codex-plugin", "plugin.json");

    private static void AssertSetupCliSucceeded(ScriptInvocationResult result, string operation) =>
        Assert.True(result.ExitCode == 0, $"Setup CLI {operation} failed. stderr='{result.Stderr}', stdout='{result.Stdout}'.");

    private static void RegisterProcessExitCleanup(string directory)
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { DeleteDirectoryIfExists(directory); }
            catch { }
        };
    }

    private static string PackagePluginRelease(string repoRoot, string packageScriptPath, string outputRoot, string version, string runtimePackagingResultPath)
    {
        ScriptInvocationResult result = InvokePowerShellScript(
            packageScriptPath,
            repoRoot,
            startInfo =>
            {
                startInfo.ArgumentList.Add("-Version");
                startInfo.ArgumentList.Add(version);
                startInfo.ArgumentList.Add("-RuntimePackagingResultPath");
                startInfo.ArgumentList.Add(runtimePackagingResultPath);
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

    private sealed record PublishedRuntimeBundle(string RepoRoot, string RuntimeRoot);
    private sealed record SetupCliReleaseArtifacts(string RepoRoot, string RuntimeDescriptorPath);

    private sealed class SetupCliTestHarness : IDisposable
    {
        private readonly SetupCliReleaseArtifacts _release;

        public SetupCliTestHarness(SetupCliReleaseArtifacts release, string userProfile)
        {
            _release = release;
            UserProfile = userProfile;
            CodexHome = Path.Combine(userProfile, ".codex");
        }

        public string UserProfile { get; }

        public string CodexHome { get; }

        public ScriptInvocationResult RunSetupCliJsonWithRuntimeDescriptor(params string[] command)
        {
            string[] args = [.. command, "--descriptor-path", _release.RuntimeDescriptorPath, "--json"];
            return RunSetupCli(args);
        }

        public ScriptInvocationResult RunSetupCliJsonWithDescriptorPath(string descriptorPath, params string[] command)
        {
            string[] args = [.. command, "--descriptor-path", descriptorPath, "--json"];
            return RunSetupCli(args);
        }

        public ScriptInvocationResult RunSetupCliJson(params string[] command)
        {
            string[] args = [.. command, "--json"];
            return RunSetupCli(args);
        }

        public void Dispose()
        {
            DeleteDirectoryIfExists(CodexHome);
            DeleteDirectoryIfExists(UserProfile);
        }

        private ScriptInvocationResult RunSetupCli(string[] args) =>
            InvokeSetupCli(_release.RepoRoot, args, CodexHome, UserProfile);
    }
}
