// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;

namespace WinBridge.Server.IntegrationTests;

public sealed partial class ComputerUseWinInstallSurfaceTests
{
    private const string SetupCliReleaseVersion = "0.1.0";
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

        ScriptInvocationResult uninstallResult = test.RunSetupCliJson("uninstall", "codex");

        AssertSetupCliSucceeded(uninstallResult, "codex uninstall");
        Assert.False(Directory.Exists(GetExpectedInstalledPluginRoot(test.CodexHome)));
        Assert.False(File.Exists(GetExpectedCodexReceiptPath(test.CodexHome)));
        Assert.True(Directory.Exists(GetExpectedSharedRuntimeStoreRoot(test.CodexHome)));

        string marketplaceText = File.ReadAllText(GetExpectedPersonalMarketplacePath(test.UserProfile));
        Assert.DoesNotContain("\"name\": \"computer-use-win\"", marketplaceText, StringComparison.Ordinal);
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

    private static SetupCliTestHarness CreateRuntimeOnlySetupCliTestHarness(string scenarioName) => CreateSetupCliTestHarness(SharedRuntimeOnlyRelease.Value, scenarioName);

    private static SetupCliTestHarness CreateCodexSetupCliTestHarness(string scenarioName) => CreateSetupCliTestHarness(SharedCodexRelease.Value, scenarioName);

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
                string runtimeArchivePath = PackageRuntimeRelease(
                    runtimeBundle.RepoRoot,
                    GetRuntimePackageScriptPath(runtimeBundle.RepoRoot),
                    runtimeBundle.RuntimeRoot,
                    outputRoot,
                    SetupCliReleaseVersion);

                runtimeDescriptorPath = CreateRuntimeReleaseDescriptor(
                    outputRoot,
                    SetupCliReleaseVersion,
                    runtimeArchivePath,
                    WindowsRuntimeIdentifier);

                if (packagePluginRelease)
                {
                    PackagePluginRelease(
                        runtimeBundle.RepoRoot,
                        GetPluginPackageScriptPath(runtimeBundle.RepoRoot),
                        outputRoot,
                        SetupCliReleaseVersion);
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
