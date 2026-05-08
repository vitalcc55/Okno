// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Tooling;

namespace WinBridge.Server.IntegrationTests;

public sealed partial class ComputerUseWinInstallSurfaceTests
{
    private const string InstallSurfaceRuntimeRid = "win-x64";
    private const string InstallSurfaceRuntimeManifestFileName = "okno-runtime-bundle-manifest.json";
    private const string InstallSurfaceRuntimeReleaseDescriptorFileName = "runtime-release.json";
    private const string InstallSurfaceServerExecutableFileName = "Okno.Server.exe";
    private const string InstallSurfaceHostFxrFileName = "hostfxr.dll";
    private const string InstallSurfaceTestRuntimeReleaseVersion = "0.2.1-test";
    private const string InstallSurfaceEmptyRuntimeManifestJson = """{"formatVersion":1,"files":[]}""";

    private static readonly Lazy<string> InstallSurfaceCachedRepositoryRoot = new(FindInstallSurfaceRepositoryRoot);

    private static readonly string[] InstallSurfaceExpectedPublicToolNamesSorted =
    [
        ToolNames.ComputerUseWinClick,
        ToolNames.ComputerUseWinDrag,
        ToolNames.ComputerUseWinGetAppState,
        ToolNames.ComputerUseWinListApps,
        ToolNames.ComputerUseWinPerformSecondaryAction,
        ToolNames.ComputerUseWinPressKey,
        ToolNames.ComputerUseWinScroll,
        ToolNames.ComputerUseWinSetValue,
        ToolNames.ComputerUseWinTypeText,
    ];

    private static readonly string[] InstallSurfaceToolNamesWithObserveAfter =
    [
        ToolNames.ComputerUseWinClick,
        ToolNames.ComputerUseWinDrag,
        ToolNames.ComputerUseWinPressKey,
        ToolNames.ComputerUseWinScroll,
    ];

    private static readonly string[] InstallSurfaceRequiredReadmeMarkers =
    [
        "`list_apps`",
        "`get_app_state`",
        "`click`",
        "`press_key`",
        "`set_value`",
        "`type_text`",
        "`scroll`",
        "`perform_secondary_action`",
        "`drag`",
        "allowFocusedFallback=true",
        "confirm=true",
    ];

    private static readonly string[] InstallSurfaceObsoleteReadmeMarkers =
    [
        "следующий глобальный action wave",
        "type_text` без editable UIA proof по-прежнему fail-close",
    ];

    [Fact]
    public void PublishComputerUseWinPluginCreatesSelfContainedRuntimeBundle()
    {
        string repoRoot = GetRepositoryRoot();
        string scriptPath = GetPublishScriptPath(repoRoot);
        string runtimeRoot = GetInstallSurfaceComputerUseWinRuntimeRoot(repoRoot);

        DeleteDirectoryIfExists(runtimeRoot);

        ScriptInvocationResult result = InvokePowerShellScript(
            scriptPath,
            repoRoot,
            _ => { });

        using JsonDocument payload = ParseJsonStdoutOrThrow(result, "Publish script");
        Assert.True(File.Exists(GetInstallSurfaceRuntimeFilePath(runtimeRoot, InstallSurfaceServerExecutableFileName)));
        Assert.Equal(runtimeRoot, payload.RootElement.GetProperty("runtimeRoot").GetString());
    }

    [Fact]
    public void PublishComputerUseWinPluginCreatesRunnableSelfContainedUiaWorker()
    {
        string repoRoot = GetRepositoryRoot();
        string scriptPath = GetPublishScriptPath(repoRoot);
        string runtimeRoot = GetInstallSurfaceComputerUseWinRuntimeRoot(repoRoot);
        string workerExecutablePath = GetInstallSurfaceRuntimeFilePath(runtimeRoot, "WinBridge.Runtime.Windows.UIA.Worker.exe");
        string workerRuntimeConfigPath = GetInstallSurfaceRuntimeFilePath(runtimeRoot, "WinBridge.Runtime.Windows.UIA.Worker.runtimeconfig.json");

        EnsurePublishedRuntimeBundle(repoRoot, scriptPath, runtimeRoot);
        AssertInstallSurfaceWorkerRuntimeConfigIsSelfContained(workerRuntimeConfigPath);

        WorkerProbeResult workerProbe = InvokeUiaWorkerSnapshotAgainstMissingWindow(workerExecutablePath);
        Assert.Equal(0, workerProbe.ExitCode);
        Assert.DoesNotContain("You must install or update .NET", workerProbe.Stderr, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Could not load file or assembly", workerProbe.Stderr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"success\":false", workerProbe.Stdout, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PublishComputerUseWinPluginRestoresPreviousRuntimeWhenPromoteFails()
    {
        using InstallSurfaceRuntimePublishBackupScenario scenario = CreateInstallSurfaceRuntimePublishBackupScenario("computer-use-win-runtime-backup");
        scenario.EnsurePublishedBaseline();
        scenario.CopyRuntimeToBackup();

        ScriptInvocationResult result = scenario.InvokeWithPreparedPublishSource("-FailAfterBackup");

        Assert.NotEqual(0, result.ExitCode);
        AssertRuntimeBundleMatchesManifest(scenario.RuntimeRoot);
    }

    [Fact]
    public void PublishComputerUseWinPluginPreservesBackupWhenRestoreFails()
    {
        using InstallSurfaceRuntimePublishBackupScenario scenario = CreateInstallSurfaceRuntimePublishBackupScenario(
            "computer-use-win-runtime-backup-restore",
            cleanupBackupWorkspaces: true);
        scenario.EnsurePublishedBaseline();
        scenario.ReplaceRuntimeWithBackupCopy();

        ScriptInvocationResult result = scenario.InvokeWithPreparedPublishSource("-FailAfterBackup", "-FailRestore");

        Assert.NotEqual(0, result.ExitCode);
        AssertInstallSurfaceCanonicalRuntimeBundleFilesExist(scenario.RuntimeRoot);
    }

    [Fact]
    public void PublishComputerUseWinPluginKeepsCanonicalRuntimeRunnableWhenRepairCopyFails()
    {
        using InstallSurfaceRuntimePublishBackupScenario scenario = CreateInstallSurfaceRuntimePublishBackupScenario(
            "computer-use-win-runtime-backup-repair",
            cleanupBackupWorkspaces: true,
            cleanupRepairWorkspaces: true);
        scenario.EnsurePublishedBaseline();
        scenario.ReplaceRuntimeWithBackupCopy();

        ScriptInvocationResult result = scenario.InvokeWithPreparedPublishSource(
            "-FailAfterBackup",
            "-FailRestore",
            "-FailRepairCopyAfterServer");

        Assert.NotEqual(0, result.ExitCode);
        AssertRuntimeBundleMatchesManifest(scenario.RuntimeRoot);
    }

    [Fact]
    public void PublishComputerUseWinPluginRejectsIncompleteBackupRuntimeBundleDuringRepair()
    {
        using InstallSurfaceRuntimePublishBackupScenario scenario = CreateInstallSurfaceRuntimePublishBackupScenario(
            "computer-use-win-runtime-backup-corrupt",
            cleanupBackupWorkspaces: true,
            cleanupRepairWorkspaces: true);
        scenario.EnsurePublishedBaseline();
        scenario.CopyRuntimeToBackup();
        scenario.DeleteRuntimeFile(InstallSurfaceHostFxrFileName);

        ScriptInvocationResult result = scenario.InvokeWithPreparedPublishSource("-FailAfterBackup", "-FailRestore");

        Assert.NotEqual(0, result.ExitCode);
        Assert.False(Directory.Exists(scenario.RuntimeRoot));
        AssertInstallSurfaceBackupWorkspaceExists(scenario.RuntimeParent);
    }

    [Fact]
    public void PublishComputerUseWinPluginDoesNotConsumeInvalidBackupBeforeRestoreValidation()
    {
        using InstallSurfaceRuntimePublishBackupScenario scenario = CreateInstallSurfaceRuntimePublishBackupScenario(
            "computer-use-win-runtime-backup-invalid-restore",
            cleanupBackupWorkspaces: true,
            cleanupRepairWorkspaces: true);
        scenario.EnsurePublishedBaseline();
        scenario.CopyRuntimeToBackup();
        scenario.DeleteRuntimeFile(InstallSurfaceHostFxrFileName);

        ScriptInvocationResult result = scenario.InvokeWithPreparedPublishSource("-FailAfterBackup");

        Assert.NotEqual(0, result.ExitCode);
        Assert.False(Directory.Exists(scenario.RuntimeRoot));
        AssertInstallSurfaceBackupWorkspaceExists(scenario.RuntimeParent);
    }

    [Fact]
    public void PublishComputerUseWinPluginRejectsPreManifestRuntimeWithoutManifestProofWhenPromoteFails()
    {
        using InstallSurfaceRuntimePublishBackupScenario scenario = CreateInstallSurfaceRuntimePublishBackupScenario(
            "computer-use-win-runtime-backup-legacy",
            cleanupBackupWorkspaces: true,
            cleanupRepairWorkspaces: true);
        scenario.EnsurePublishedBaseline();
        scenario.CopyRuntimeToBackup();
        scenario.DeleteRuntimeFile(InstallSurfaceRuntimeManifestFileName);

        ScriptInvocationResult result = scenario.InvokeWithPreparedPublishSource("-FailAfterBackup");

        Assert.NotEqual(0, result.ExitCode);
        Assert.False(Directory.Exists(scenario.RuntimeRoot));
        AssertInstallSurfaceBackupWorkspaceExists(scenario.RuntimeParent);
    }

    [Fact]
    public void PublishComputerUseWinPluginRejectsPreManifestBackupMissingManagedDependency()
    {
        using InstallSurfaceRuntimePublishBackupScenario scenario = CreateInstallSurfaceRuntimePublishBackupScenario(
            "computer-use-win-runtime-backup-legacy-missing-dependency",
            cleanupBackupWorkspaces: true,
            cleanupRepairWorkspaces: true);
        scenario.EnsurePublishedBaseline();
        scenario.CopyRuntimeToBackup();
        scenario.DeleteRuntimeFile(InstallSurfaceRuntimeManifestFileName);
        scenario.DeleteRuntimeFile("Microsoft.Extensions.Hosting.dll");

        ScriptInvocationResult result = scenario.InvokeWithPreparedPublishSource("-FailAfterBackup");

        Assert.NotEqual(0, result.ExitCode);
        Assert.False(Directory.Exists(scenario.RuntimeRoot));
        AssertInstallSurfaceBackupWorkspaceExists(scenario.RuntimeParent);
    }

    [Fact]
    public void PublishComputerUseWinPluginKeepsCanonicalRuntimeRunnableWhenRepairHandoffFails()
    {
        using InstallSurfaceRuntimePublishBackupScenario scenario = CreateInstallSurfaceRuntimePublishBackupScenario(
            "computer-use-win-runtime-backup-handoff",
            cleanupBackupWorkspaces: true,
            cleanupRepairWorkspaces: true);
        scenario.EnsurePublishedBaseline();
        scenario.CopyRuntimeToBackup();

        ScriptInvocationResult result = scenario.InvokeWithPreparedPublishSource(
            "-FailAfterBackup",
            "-FailRestore",
            "-FailRepairHandoff");

        Assert.NotEqual(0, result.ExitCode);
        AssertInstallSurfaceCanonicalRuntimeBundleFilesExist(scenario.RuntimeRoot);
    }

    [Fact]
    public void PublishComputerUseWinPluginDoesNotUseCanonicalRuntimeAsFallbackRepairWorkspace()
    {
        using InstallSurfaceRuntimePublishBackupScenario scenario = CreateInstallSurfaceRuntimePublishBackupScenario(
            "computer-use-win-runtime-backup-fallback-handoff",
            cleanupBackupWorkspaces: true,
            cleanupRepairWorkspaces: true);
        scenario.EnsurePublishedBaseline();
        scenario.CopyRuntimeToBackup();

        ScriptInvocationResult result = scenario.InvokeWithPreparedPublishSource(
            "-FailAfterBackup",
            "-FailRestore",
            "-FailRepairHandoff",
            "-FailRepairFallbackHandoff");

        Assert.NotEqual(0, result.ExitCode);
        Assert.False(Directory.Exists(scenario.RuntimeRoot));
        AssertInstallSurfaceBackupWorkspaceExists(scenario.RuntimeParent);
    }

    [Fact]
    public void PublishComputerUseWinPluginTreatsBackupCleanupFailureAsBestEffortAfterSuccessfulPromote()
    {
        string repoRoot = GetRepositoryRoot();
        string scriptPath = GetPublishTestScriptPath(repoRoot);
        string runtimeRoot = GetInstallSurfaceComputerUseWinRuntimeRoot(repoRoot);
        string runtimeParent = Path.GetDirectoryName(runtimeRoot)!;

        try
        {
            EnsurePublishedRuntimeBundle(repoRoot, scriptPath, runtimeRoot);

            ScriptInvocationResult result = InvokeInstallSurfacePublishScriptWithPreparedSource(
                scriptPath,
                repoRoot,
                runtimeRoot,
                "-FailBackupCleanup");

            Assert.Equal(0, result.ExitCode);
            using JsonDocument payload = JsonDocument.Parse(result.Stdout);
            Assert.True(File.Exists(GetInstallSurfaceRuntimeFilePath(runtimeRoot, InstallSurfaceServerExecutableFileName)));
            Assert.Equal(runtimeRoot, payload.RootElement.GetProperty("runtimeRoot").GetString());
            AssertInstallSurfaceBackupWorkspaceExists(runtimeParent);
        }
        finally
        {
            DeleteInstallSurfaceRuntimeWorkspaceCandidates(runtimeParent, InstallSurfaceRuntimeWorkspaceKind.Backup);
        }
    }

    [Fact]
    public void PublishComputerUseWinPluginDoesNotReadInheritedTestEnvironmentOverrides()
    {
        string repoRoot = GetRepositoryRoot();
        string scriptPath = GetPublishScriptPath(repoRoot);
        string coreScriptPath = GetInstallSurfaceCodexScriptPath(repoRoot, "publish-computer-use-win-plugin-core.ps1");

        Assert.DoesNotContain("COMPUTER_USE_WIN_TEST_", File.ReadAllText(scriptPath), StringComparison.Ordinal);
        Assert.DoesNotContain("COMPUTER_USE_WIN_TEST_", File.ReadAllText(coreScriptPath), StringComparison.Ordinal);
    }

    [Fact]
    public void ComputerUseWinLauncherFailsClosedWhenRuntimeIsMissingAndDescriptorIsMissing()
    {
        string repoRoot = GetRepositoryRoot();
        string sourcePluginRoot = GetInstallSurfaceComputerUseWinPluginRoot(repoRoot);
        string tempPluginRoot = GetInstallSurfaceTempRoot(repoRoot, "computer-use-win-missing-runtime");

        try
        {
            CopyDirectory(sourcePluginRoot, tempPluginRoot, ExcludeInstallSurfacePluginRuntimeFiles);
            DeleteInstallSurfaceRuntimeReleaseDescriptor(tempPluginRoot);

            ScriptInvocationResult result = InvokePluginLauncher(tempPluginRoot);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("runtime release descriptor not found", result.Stderr, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(InstallSurfaceRuntimeReleaseDescriptorFileName, result.Stderr, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectoryIfExists(tempPluginRoot);
        }
    }

    [Fact]
    public void ComputerUseWinLauncherFailsClosedWhenRuntimeBundleIsIncompleteAndDescriptorIsMissing()
    {
        string repoRoot = GetRepositoryRoot();
        string sourcePluginRoot = GetInstallSurfaceComputerUseWinPluginRoot(repoRoot);
        string tempPluginRoot = GetInstallSurfaceTempRoot(repoRoot, "computer-use-win-partial-runtime");

        try
        {
            CopyDirectory(sourcePluginRoot, tempPluginRoot, ExcludeInstallSurfaceRuntimeWorkspaceFiles);
            string serverDllPath = Path.Combine(tempPluginRoot, "runtime", InstallSurfaceRuntimeRid, "Okno.Server.dll");
            if (File.Exists(serverDllPath))
            {
                File.Delete(serverDllPath);
            }

            DeleteInstallSurfaceRuntimeReleaseDescriptor(tempPluginRoot);

            ScriptInvocationResult result = InvokePluginLauncher(tempPluginRoot);

            Assert.NotEqual(0, result.ExitCode);
            Assert.False(string.IsNullOrWhiteSpace(result.Stderr));
            Assert.Contains("runtime release descriptor not found", result.Stderr, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectoryIfExists(tempPluginRoot);
        }
    }

    [Fact]
    public async Task ComputerUseWinLauncherRehydratesFromPinnedReleaseWhenRuntimeDependencyFileIsMissing()
    {
        string repoRoot = GetRepositoryRoot();
        string publishScriptPath = GetPublishScriptPath(repoRoot);
        string packageScriptPath = GetInstallSurfaceCodexScriptPath(repoRoot, "package-computer-use-win-runtime-release.ps1");
        string sourcePluginRoot = GetInstallSurfaceComputerUseWinPluginRoot(repoRoot);
        string sourceRuntimeRoot = GetInstallSurfaceComputerUseWinRuntimeRoot(repoRoot);
        string tempPluginRoot = GetInstallSurfaceTempRoot(repoRoot, "computer-use-win-missing-dependency");
        string outputRoot = GetInstallSurfaceTempRoot(repoRoot, "computer-use-win-missing-dependency-release");
        string codexHome = GetInstallSurfaceTempRoot(repoRoot, "codex-home-missing-dependency");

        EnsurePublishedRuntimeBundle(repoRoot, publishScriptPath, sourceRuntimeRoot);

        try
        {
            RuntimeReleasePackageResult runtimePackage = PackageRuntimeRelease(
                repoRoot,
                packageScriptPath,
                sourceRuntimeRoot,
                outputRoot,
                InstallSurfaceTestRuntimeReleaseVersion);
            string descriptorPath = runtimePackage.DescriptorPath;
            CopyDirectory(sourcePluginRoot, tempPluginRoot, IncludeAllInstallSurfaceFiles);
            DeleteInstallSurfacePluginRuntimeFile(tempPluginRoot, InstallSurfaceHostFxrFileName);

            await using PluginLauncherSession launcher = StartPluginLauncherSession(tempPluginRoot, descriptorPath, codexHome);
            PluginMcpSession session = launcher.CreateMcpSession();

            await InitializeInstallSurfaceMcpSessionAsync(session);

            using JsonDocument toolsResponse = await RequestInstallSurfaceToolsListAsync(session);
            string[] toolNames = GetInstallSurfaceSortedToolNames(GetInstallSurfaceToolsElement(toolsResponse));

            Assert.Contains(ToolNames.ComputerUseWinListApps, toolNames);
            Assert.Contains(ToolNames.ComputerUseWinTypeText, toolNames);
            Assert.True(File.Exists(GetInstallSurfaceExpectedSharedRuntimeFilePath(codexHome, InstallSurfaceRuntimeRid, InstallSurfaceTestRuntimeReleaseVersion, InstallSurfaceHostFxrFileName)));
        }
        finally
        {
            DeleteDirectoryIfExists(tempPluginRoot);
            DeleteDirectoryIfExists(outputRoot);
            DeleteDirectoryIfExists(codexHome);
        }
    }

    [Fact]
    public async Task ComputerUseWinLauncherFromTempPluginCopyPublishesPublicSurfaceWithoutRepoHints()
    {
        string repoRoot = GetRepositoryRoot();
        string publishScriptPath = GetPublishScriptPath(repoRoot);
        string sourcePluginRoot = GetInstallSurfaceComputerUseWinPluginRoot(repoRoot);
        string tempPluginRoot = GetInstallSurfaceTempRoot(repoRoot, "computer-use-win-installed-copy");

        EnsurePublishedRuntimeBundle(repoRoot, publishScriptPath, GetInstallSurfaceComputerUseWinRuntimeRoot(repoRoot));

        try
        {
            CopyDirectory(sourcePluginRoot, tempPluginRoot, IncludeAllInstallSurfaceFiles);

            await using PluginLauncherSession launcher = StartPluginLauncherSession(tempPluginRoot);
            PluginMcpSession session = launcher.CreateMcpSession();

            await InitializeInstallSurfaceMcpSessionAsync(session);

            using JsonDocument toolsResponse = await RequestInstallSurfaceToolsListAsync(session);
            JsonElement tools = GetInstallSurfaceToolsElement(toolsResponse);
            Assert.Equal(InstallSurfaceExpectedPublicToolNamesSorted, GetInstallSurfaceSortedToolNames(tools));

            AssertInstallSurfaceToolSchemaHasProperties(tools, ToolNames.ComputerUseWinGetAppState, "windowId", "hwnd");
            AssertInstallSurfaceToolSchemaDoesNotHaveProperties(tools, ToolNames.ComputerUseWinGetAppState, "appId");

            AssertInstallSurfaceToolSchemaHasProperties(
                tools,
                ToolNames.ComputerUseWinTypeText,
                "allowFocusedFallback",
                "observeAfter",
                "point",
                "coordinateSpace");
            AssertInstallSurfaceToolCoordinateSpaceEnumEquals(
                tools,
                ToolNames.ComputerUseWinTypeText,
                [InputCoordinateSpaceValues.CapturePixels]);

            foreach (string toolName in InstallSurfaceToolNamesWithObserveAfter)
            {
                AssertInstallSurfaceToolSchemaHasProperties(tools, toolName, "observeAfter");
            }

            AssertInstallSurfaceToolSchemaDoesNotHaveProperties(tools, ToolNames.ComputerUseWinSetValue, "observeAfter");
            AssertInstallSurfaceToolSchemaDoesNotHaveProperties(tools, ToolNames.ComputerUseWinPerformSecondaryAction, "observeAfter");

            using JsonDocument listAppsResponse = await CallInstallSurfaceToolAsync(session, ToolNames.ComputerUseWinListApps, new { });
            AssertInstallSurfaceListAppsResponseContainsWindowArrays(listAppsResponse);
        }
        finally
        {
            DeleteDirectoryIfExists(tempPluginRoot);
        }
    }

    private static JsonElement GetToolDescriptor(JsonElement tools, string toolName) =>
        tools.EnumerateArray()
            .Single(tool => string.Equals(tool.GetProperty("name").GetString(), toolName, StringComparison.Ordinal));

    [Fact]
    public void ComputerUseWinPluginReadmeDocumentsCurrentShippedToolSurface()
    {
        string repoRoot = GetRepositoryRoot();
        string readmePath = Path.Combine(GetInstallSurfaceComputerUseWinPluginRoot(repoRoot), "README.md");
        string readme = File.ReadAllText(readmePath);

        foreach (string marker in InstallSurfaceRequiredReadmeMarkers)
        {
            Assert.Contains(marker, readme, StringComparison.Ordinal);
        }

        foreach (string marker in InstallSurfaceObsoleteReadmeMarkers)
        {
            Assert.DoesNotContain(marker, readme, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ComputerUseWinPluginManifestDocumentsCurrentShippedToolSurface()
    {
        string repoRoot = GetRepositoryRoot();
        string manifestPath = Path.Combine(GetInstallSurfaceComputerUseWinPluginRoot(repoRoot), ".codex-plugin", "plugin.json");
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));

        string longDescription = manifest.RootElement
            .GetProperty("interface")
            .GetProperty("longDescription")
            .GetString() ?? string.Empty;

        foreach (string toolName in InstallSurfaceExpectedPublicToolNamesSorted)
        {
            Assert.Contains(toolName, longDescription, StringComparison.Ordinal);
        }

        JsonElement defaultPrompt = manifest.RootElement
            .GetProperty("interface")
            .GetProperty("defaultPrompt");
        foreach (JsonElement prompt in defaultPrompt.EnumerateArray())
        {
            Assert.True(
                prompt.GetString()?.Length <= 128,
                $"Plugin defaultPrompt entry exceeds Codex 128-character limit: '{prompt.GetString()}'.");
        }
    }

    [Theory]
    [InlineData("Directory.Build.props")]
    [InlineData("Directory.Packages.props")]
    [InlineData("Directory.Build.rsp")]
    [InlineData("global.json")]
    [InlineData("WinBridge.sln")]
    [InlineData("NuGet.Config")]
    [InlineData(".editorconfig")]
    [InlineData(".globalconfig")]
    [InlineData("Repo.Custom.globalconfig")]
    [InlineData("Repo.Custom.props")]
    [InlineData("Repo.Custom.targets")]
    public void PublishedRuntimeBundleIsFreshReturnsFalseWhenRepoLevelBuildInputIsNewerThanManifest(string repoLevelInputName)
    {
        string root = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        string runtimeRoot = Path.Combine(root, "plugins", "computer-use-win", "runtime", InstallSurfaceRuntimeRid);
        string manifestPath = GetInstallSurfaceRuntimeFilePath(runtimeRoot, InstallSurfaceRuntimeManifestFileName);
        string repoLevelInputPath = Path.Combine(root, repoLevelInputName);

        try
        {
            Directory.CreateDirectory(runtimeRoot);
            File.WriteAllText(manifestPath, InstallSurfaceEmptyRuntimeManifestJson);
            File.WriteAllText(repoLevelInputPath, "<Project />");

            DateTime manifestWriteUtc = DateTime.UtcNow.AddMinutes(-2);
            DateTime inputWriteUtc = DateTime.UtcNow.AddMinutes(-1);
            File.SetLastWriteTimeUtc(manifestPath, manifestWriteUtc);
            File.SetLastWriteTimeUtc(repoLevelInputPath, inputWriteUtc);

            Assert.False(PublishedRuntimeBundleIsFresh(root, runtimeRoot));
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Theory]
    [InlineData("Directory.Build.props")]
    [InlineData("Directory.Packages.props")]
    [InlineData("Directory.Build.rsp")]
    [InlineData("global.json")]
    [InlineData("WinBridge.sln")]
    [InlineData("NuGet.Config")]
    [InlineData(".editorconfig")]
    [InlineData(".globalconfig")]
    [InlineData("*.globalconfig")]
    [InlineData("*.props")]
    [InlineData("*.targets")]
    public void CacheInstallProofTracksRepoLevelRuntimePublicationInputs(string inputMarker)
    {
        string repoRoot = GetRepositoryRoot();
        string proofScriptPath = GetInstallSurfaceCodexScriptPath(repoRoot, "prove-computer-use-win-cache-install.ps1");
        string proofScript = File.ReadAllText(proofScriptPath);

        Assert.Contains(inputMarker, proofScript, StringComparison.Ordinal);
    }

    [Fact]
    public void CacheInstallProofUsesRuntimeManifestAsFreshnessAnchor()
    {
        string repoRoot = GetRepositoryRoot();
        string proofScriptPath = GetInstallSurfaceCodexScriptPath(repoRoot, "prove-computer-use-win-cache-install.ps1");
        string proofScript = File.ReadAllText(proofScriptPath);

        Assert.Contains(InstallSurfaceRuntimeManifestFileName, proofScript, StringComparison.Ordinal);
        Assert.Contains("Assert-RuntimeBundleMatchesManifest", proofScript, StringComparison.Ordinal);
        Assert.Contains("runtimeBundleManifestWriteTimeUtc", proofScript, StringComparison.Ordinal);
        Assert.Contains("runtimeBundleFreshForPublicationInputs", proofScript, StringComparison.Ordinal);
    }

    private static InstallSurfaceRuntimePublishBackupScenario CreateInstallSurfaceRuntimePublishBackupScenario(
        string backupRootName,
        bool cleanupBackupWorkspaces = false,
        bool cleanupRepairWorkspaces = false)
    {
        string repoRoot = GetRepositoryRoot();
        string runtimeRoot = GetInstallSurfaceComputerUseWinRuntimeRoot(repoRoot);
        return new InstallSurfaceRuntimePublishBackupScenario(
            repoRoot,
            GetPublishTestScriptPath(repoRoot),
            runtimeRoot,
            GetInstallSurfaceTempRoot(repoRoot, backupRootName),
            cleanupBackupWorkspaces,
            cleanupRepairWorkspaces);
    }

    private static void AssertInstallSurfaceWorkerRuntimeConfigIsSelfContained(string workerRuntimeConfigPath)
    {
        using JsonDocument runtimeConfig = JsonDocument.Parse(File.ReadAllText(workerRuntimeConfigPath));
        JsonElement runtimeOptions = runtimeConfig.RootElement.GetProperty("runtimeOptions");
        Assert.True(
            runtimeOptions.TryGetProperty("includedFrameworks", out JsonElement includedFrameworks),
            "The cache-installed computer-use-win plugin launches the UIA worker from the plugin-local runtime bundle; the worker must be self-contained and cannot depend on a machine-wide .NET runtime.");

        string[] frameworkNames = includedFrameworks
            .EnumerateArray()
            .Select(framework => framework.GetProperty("name").GetString() ?? string.Empty)
            .ToArray();
        Assert.Contains("Microsoft.NETCore.App", frameworkNames);
        Assert.Contains("Microsoft.WindowsDesktop.App", frameworkNames);
    }

    private static void AssertInstallSurfaceCanonicalRuntimeBundleFilesExist(string runtimeRoot)
    {
        Assert.True(File.Exists(GetInstallSurfaceRuntimeFilePath(runtimeRoot, InstallSurfaceServerExecutableFileName)));
        Assert.True(File.Exists(GetInstallSurfaceRuntimeFilePath(runtimeRoot, InstallSurfaceHostFxrFileName)));
        Assert.True(File.Exists(GetInstallSurfaceRuntimeFilePath(runtimeRoot, InstallSurfaceRuntimeManifestFileName)));
    }

    private static void AssertInstallSurfaceBackupWorkspaceExists(string runtimeParent)
    {
        Assert.NotEmpty(GetInstallSurfaceRuntimeWorkspaceCandidates(runtimeParent, InstallSurfaceRuntimeWorkspaceKind.Backup));
    }

    private static async Task InitializeInstallSurfaceMcpSessionAsync(PluginMcpSession session)
    {
        using (await session.SendRequestAsync(
            "initialize",
            new
            {
                protocolVersion = "2025-11-25",
                capabilities = new { },
                clientInfo = new
                {
                    name = "ComputerUseWin.InstallSurfaceTests",
                    version = "0.2.1",
                },
            },
            "initialize"))
        {
        }

        await session.SendNotificationAsync("notifications/initialized");
    }

    private static Task<JsonDocument> RequestInstallSurfaceToolsListAsync(PluginMcpSession session) =>
        session.SendRequestAsync("tools/list", new { }, "tools/list");

    private static Task<JsonDocument> CallInstallSurfaceToolAsync(PluginMcpSession session, string toolName, object arguments) =>
        session.SendRequestAsync(
            "tools/call",
            new
            {
                name = toolName,
                arguments,
            },
            "tools/call:" + toolName);

    private static JsonElement GetInstallSurfaceToolsElement(JsonDocument toolsResponse) =>
        toolsResponse.RootElement
            .GetProperty("result")
            .GetProperty("tools");

    private static string[] GetInstallSurfaceSortedToolNames(JsonElement tools) =>
        tools.EnumerateArray()
            .Select(tool => tool.GetProperty("name").GetString() ?? string.Empty)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();

    private static void AssertInstallSurfaceToolSchemaHasProperties(JsonElement tools, string toolName, params string[] propertyNames)
    {
        JsonElement properties = GetInstallSurfaceToolSchemaProperties(tools, toolName);
        foreach (string propertyName in propertyNames)
        {
            Assert.True(properties.TryGetProperty(propertyName, out _));
        }
    }

    private static void AssertInstallSurfaceToolSchemaDoesNotHaveProperties(JsonElement tools, string toolName, params string[] propertyNames)
    {
        JsonElement properties = GetInstallSurfaceToolSchemaProperties(tools, toolName);
        foreach (string propertyName in propertyNames)
        {
            Assert.False(properties.TryGetProperty(propertyName, out _));
        }
    }

    private static JsonElement GetInstallSurfaceToolSchemaProperties(JsonElement tools, string toolName) =>
        GetToolDescriptor(tools, toolName)
            .GetProperty("inputSchema")
            .GetProperty("properties");

    private static void AssertInstallSurfaceToolCoordinateSpaceEnumEquals(JsonElement tools, string toolName, string[] expectedValues)
    {
        JsonElement coordinateSpace = GetInstallSurfaceToolSchemaProperties(tools, toolName).GetProperty("coordinateSpace");
        string[] actualValues = coordinateSpace
            .GetProperty("enum")
            .EnumerateArray()
            .Select(item => item.GetString())
            .Where(static item => item is not null)
            .Cast<string>()
            .ToArray();

        Assert.Equal(expectedValues, actualValues);
    }

    private static void AssertInstallSurfaceListAppsResponseContainsWindowArrays(JsonDocument listAppsResponse)
    {
        JsonElement listAppsStructured = listAppsResponse.RootElement
            .GetProperty("result")
            .GetProperty("structuredContent");
        Assert.Equal(ComputerUseWinStatusValues.Ok, listAppsStructured.GetProperty("status").GetString());
        JsonElement apps = listAppsStructured.GetProperty("apps");
        Assert.Equal(JsonValueKind.Array, apps.ValueKind);
        foreach (JsonElement app in apps.EnumerateArray())
        {
            Assert.True(app.TryGetProperty("windows", out JsonElement windows));
            Assert.Equal(JsonValueKind.Array, windows.ValueKind);
        }
    }

    private static PluginLauncherSession StartPluginLauncherSession(string pluginRoot, string? runtimeReleaseDescriptorOverridePath = null, string? codexHomeOverridePath = null)
    {
        PluginLauncherStartContext context = CreatePluginLauncherStartContext(pluginRoot, runtimeReleaseDescriptorOverridePath, codexHomeOverridePath);
        ProcessStartInfo startInfo = CreateInstallSurfacePowerShellProcessStartInfo(pluginRoot, redirectStandardInput: true);
        ConfigurePluginLauncherProcessStartInfo(startInfo, context);

        Process process = new() { StartInfo = startInfo };
        process.Start();
        return new PluginLauncherSession(
            context,
            process,
            process.StandardInput,
            process.StandardOutput,
            process.StandardError.ReadToEndAsync());
    }

    private static ScriptInvocationResult InvokePluginLauncher(string pluginRoot, string? runtimeReleaseDescriptorOverridePath = null, string? codexHomeOverridePath = null)
    {
        PluginLauncherStartContext context = CreatePluginLauncherStartContext(pluginRoot, runtimeReleaseDescriptorOverridePath, codexHomeOverridePath);
        ProcessStartInfo startInfo = CreateInstallSurfacePowerShellProcessStartInfo(pluginRoot);
        ConfigurePluginLauncherProcessStartInfo(startInfo, context);

        using Process process = new() { StartInfo = startInfo };
        process.Start();

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        try
        {
            if (!process.WaitForExit(TimeSpan.FromSeconds(30)))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
                Task.WaitAll(stdoutTask, stderrTask);
                return new ScriptInvocationResult(
                    -1,
                    stdoutTask.Result,
                    $"Plugin launcher timed out.{Environment.NewLine}{BuildPluginLauncherFailureContext(context, process, stderrTask.Result)}");
            }

            Task.WaitAll(stdoutTask, stderrTask);
            return new ScriptInvocationResult(process.ExitCode, stdoutTask.Result, stderrTask.Result);
        }
        finally
        {
            DeleteInstallSurfacePluginLauncherContextDirectories(context);
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
        if (!Directory.Exists(path))
        {
            return;
        }

        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                foreach (string filePath in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(filePath, FileAttributes.Normal);
                }

                Directory.Delete(path, recursive: true);
                return;
            }
            catch (DirectoryNotFoundException)
            {
                return;
            }
            catch (UnauthorizedAccessException) when (attempt < 4)
            {
                Thread.Sleep(200);
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(200);
            }
        }
    }

    private static PluginLauncherStartContext CreatePluginLauncherStartContext(string pluginRoot, string? runtimeReleaseDescriptorOverridePath, string? codexHomeOverridePath)
    {
        string runId = "computer-use-win-test-" + Guid.NewGuid().ToString("N");
        string repoRoot = GetRepositoryRoot();
        string runRoot = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "launcher-runs", runId);
        string artifactsRoot = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "launcher-artifacts", runId);
        string codexHomePath = string.IsNullOrWhiteSpace(codexHomeOverridePath)
            ? Path.Combine(repoRoot, ".tmp", ".codex", "tests", "launcher-codex-home", runId)
            : codexHomeOverridePath;
        Directory.CreateDirectory(runRoot);
        Directory.CreateDirectory(artifactsRoot);
        Directory.CreateDirectory(codexHomePath);

        return new PluginLauncherStartContext(
            pluginRoot,
            runId,
            runRoot,
            artifactsRoot,
            runtimeReleaseDescriptorOverridePath,
            codexHomePath);
    }

    private static void ConfigurePluginLauncherProcessStartInfo(ProcessStartInfo startInfo, PluginLauncherStartContext context)
    {
        AddInstallSurfacePowerShellScriptArguments(startInfo, Path.Combine(context.PluginRoot, "run-computer-use-win-mcp.ps1"));
        startInfo.Environment["COMPUTER_USE_WIN_REPO_ROOT"] = string.Empty;
        startInfo.Environment["WINBRIDGE_RUN_ID"] = context.RunId;
        startInfo.Environment["WINBRIDGE_RUN_ROOT"] = context.RunRoot;
        startInfo.Environment["WINBRIDGE_ARTIFACTS_ROOT"] = context.ArtifactsRoot;
        if (!string.IsNullOrWhiteSpace(context.CodexHomeOverridePath))
        {
            startInfo.Environment["CODEX_HOME"] = context.CodexHomeOverridePath;
            startInfo.Environment["USERPROFILE"] = GetExpectedUserProfileRootFromCodexHome(context.CodexHomeOverridePath);
            startInfo.Environment["LOCALAPPDATA"] = GetExpectedLocalAppDataRootFromCodexHome(context.CodexHomeOverridePath);
        }
        else
        {
            startInfo.Environment.Remove("CODEX_HOME");
            startInfo.Environment.Remove("USERPROFILE");
            startInfo.Environment.Remove("LOCALAPPDATA");
        }

        if (!string.IsNullOrWhiteSpace(context.RuntimeReleaseDescriptorOverridePath))
        {
            startInfo.Environment["COMPUTER_USE_WIN_RUNTIME_RELEASE_DESCRIPTOR_OVERRIDE"] = context.RuntimeReleaseDescriptorOverridePath;
        }
        else
        {
            startInfo.Environment.Remove("COMPUTER_USE_WIN_RUNTIME_RELEASE_DESCRIPTOR_OVERRIDE");
        }
    }

    private static void DeleteInstallSurfacePluginLauncherContextDirectories(PluginLauncherStartContext context)
    {
        DeleteDirectoryIfExists(context.RunRoot);
        DeleteDirectoryIfExists(context.ArtifactsRoot);
        if (!string.IsNullOrWhiteSpace(context.CodexHomeOverridePath))
        {
            DeleteDirectoryIfExists(context.CodexHomeOverridePath);
        }
    }

    private static void EnsurePublishedRuntimeBundle(string repoRoot, string scriptPath, string runtimeRoot)
    {
        StopRepoOwnedTestBundleServers(repoRoot);

        if (Directory.Exists(runtimeRoot)
            && RuntimeBundleMatchesManifest(runtimeRoot)
            && PublishedRuntimeBundleIsFresh(repoRoot, runtimeRoot))
        {
            return;
        }

        ScriptInvocationResult result = InvokePowerShellScript(
            scriptPath,
            repoRoot,
            _ => { });
        Assert.True(
            result.ExitCode == 0,
            $"Publish script failed while preparing runtime baseline. ExitCode={result.ExitCode}. stderr='{result.Stderr.Trim()}', stdout='{result.Stdout.Trim()}'.");
        AssertRuntimeBundleMatchesManifest(runtimeRoot);
    }

    private static void StopRepoOwnedTestBundleServers(string repoRoot)
    {
        string ownedPrefix = Path.GetFullPath(Path.Combine(repoRoot, ".tmp", ".codex", "runs", "local", "test-bundle"))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        foreach (Process process in Process.GetProcessesByName("Okno.Server"))
        {
            try
            {
                string? executablePath = process.MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(executablePath))
                {
                    continue;
                }

                string normalizedPath = Path.GetFullPath(executablePath);
                if (!normalizedPath.StartsWith(ownedPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
            catch (Exception)
            {
                // Best effort cleanup for repo-owned stale test-bundle servers.
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static void AssertRuntimeBundleMatchesManifest(string runtimeRoot)
    {
        Assert.True(RuntimeBundleMatchesManifest(runtimeRoot), $"Runtime bundle '{runtimeRoot}' does not match its manifest.");
    }

    private static bool PublishedRuntimeBundleIsFresh(string repoRoot, string runtimeRoot)
    {
        string manifestPath = GetInstallSurfaceRuntimeFilePath(runtimeRoot, InstallSurfaceRuntimeManifestFileName);
        if (!File.Exists(manifestPath))
        {
            return false;
        }

        DateTime manifestWriteUtc = File.GetLastWriteTimeUtc(manifestPath);
        DateTime latestSourceWriteUtc = EnumeratePublishInputs(repoRoot)
            .Select(File.GetLastWriteTimeUtc)
            .DefaultIfEmpty(DateTime.MinValue)
            .Max();
        return manifestWriteUtc >= latestSourceWriteUtc;
    }

    private static IEnumerable<string> EnumeratePublishInputs(string repoRoot)
    {
        HashSet<string> yielded = new(StringComparer.OrdinalIgnoreCase);

        foreach (string path in EnumerateRepoRootBuildInputs(repoRoot))
        {
            if (yielded.Add(Path.GetFullPath(path)))
            {
                yield return path;
            }
        }

        string srcRoot = Path.Combine(repoRoot, "src");
        if (Directory.Exists(srcRoot))
        {
            foreach (string path in EnumerateFilesRecursively(srcRoot))
            {
                if (yielded.Add(Path.GetFullPath(path)))
                {
                    yield return path;
                }
            }
        }

        string pluginRoot = GetInstallSurfaceComputerUseWinPluginRoot(repoRoot);
        string generatedRuntimeRoot = Path.Combine(pluginRoot, "runtime") + Path.DirectorySeparatorChar;
        if (Directory.Exists(pluginRoot))
        {
            foreach (string path in EnumerateFilesRecursively(pluginRoot))
            {
                string normalizedPath = Path.GetFullPath(path);
                if (normalizedPath.StartsWith(generatedRuntimeRoot, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (yielded.Add(normalizedPath))
                {
                    yield return path;
                }
            }
        }

        string scriptsRoot = Path.Combine(repoRoot, "scripts", "codex");
        if (Directory.Exists(scriptsRoot))
        {
            foreach (string path in EnumerateFilesRecursively(scriptsRoot))
            {
                if (yielded.Add(Path.GetFullPath(path)))
                {
                    yield return path;
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateRepoRootBuildInputs(string repoRoot)
    {
        string[] canonicalFiles =
        [
            ".editorconfig",
            ".globalconfig",
            "Directory.Build.rsp",
            "global.json",
            "Directory.Build.props",
            "Directory.Packages.props",
            "WinBridge.sln",
            "NuGet.Config",
            "nuget.config",
        ];

        foreach (string fileName in canonicalFiles)
        {
            string candidate = Path.Combine(repoRoot, fileName);
            if (File.Exists(candidate))
            {
                yield return candidate;
            }
        }

        foreach (string path in Directory.EnumerateFiles(repoRoot, "*.props", SearchOption.TopDirectoryOnly))
        {
            yield return path;
        }

        foreach (string path in Directory.EnumerateFiles(repoRoot, "*.targets", SearchOption.TopDirectoryOnly))
        {
            yield return path;
        }

        foreach (string path in Directory.EnumerateFiles(repoRoot, "*.globalconfig", SearchOption.TopDirectoryOnly))
        {
            yield return path;
        }
    }

    private static IEnumerable<string> EnumerateFilesRecursively(string root)
    {
        foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return path;
        }
    }

    private static bool RuntimeBundleMatchesManifest(string runtimeRoot)
    {
        if (!Directory.Exists(runtimeRoot))
        {
            return false;
        }

        string manifestPath = GetInstallSurfaceRuntimeFilePath(runtimeRoot, InstallSurfaceRuntimeManifestFileName);
        if (!File.Exists(manifestPath))
        {
            return false;
        }

        using JsonDocument manifestDocument = JsonDocument.Parse(File.ReadAllText(manifestPath));
        if (!manifestDocument.RootElement.TryGetProperty("formatVersion", out JsonElement formatVersionElement)
            || formatVersionElement.GetInt32() != 1)
        {
            return false;
        }

        Dictionary<string, long> expectedFiles = manifestDocument.RootElement
            .GetProperty("files")
            .EnumerateArray()
            .ToDictionary(
                static entry => entry.GetProperty("path").GetString() ?? string.Empty,
                static entry => entry.GetProperty("size").GetInt64(),
                StringComparer.Ordinal);

        foreach (string filePath in Directory.EnumerateFiles(runtimeRoot, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(runtimeRoot, filePath);
            if (string.Equals(relativePath, InstallSurfaceRuntimeManifestFileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!expectedFiles.Remove(relativePath, out long expectedSize))
            {
                return false;
            }

            long actualSize = new FileInfo(filePath).Length;
            if (actualSize != expectedSize)
            {
                return false;
            }
        }

        return expectedFiles.Count == 0;
    }

    private static void UsePreparedPublishSource(ProcessStartInfo startInfo, string sourceRoot)
    {
        startInfo.ArgumentList.Add("-PublishSourceRoot");
        startInfo.ArgumentList.Add(sourceRoot);
    }

    private static void AddScriptSwitch(ProcessStartInfo startInfo, string switchName)
    {
        startInfo.ArgumentList.Add(switchName);
    }

    private static ScriptInvocationResult InvokeInstallSurfacePublishScriptWithPreparedSource(
        string scriptPath,
        string repoRoot,
        string sourceRoot,
        params string[] switchNames)
    {
        return InvokePowerShellScript(
            scriptPath,
            repoRoot,
            startInfo =>
            {
                UsePreparedPublishSource(startInfo, sourceRoot);
                AddInstallSurfaceScriptSwitches(startInfo, switchNames);
            });
    }

    private static void AddInstallSurfaceScriptSwitches(ProcessStartInfo startInfo, IEnumerable<string> switchNames)
    {
        foreach (string switchName in switchNames)
        {
            AddScriptSwitch(startInfo, switchName);
        }
    }

    private static string GetPublishScriptPath(string repoRoot)
    {
        return GetInstallSurfaceCodexScriptPath(repoRoot, "publish-computer-use-win-plugin.ps1");
    }

    private static string GetPublishTestScriptPath(string repoRoot)
    {
        return GetInstallSurfaceCodexScriptPath(repoRoot, "test-publish-computer-use-win-plugin.ps1");
    }

    private static string GetInstallSurfaceCodexScriptPath(string repoRoot, string scriptFileName)
    {
        return Path.Combine(repoRoot, "scripts", "codex", scriptFileName);
    }

    private static string GetInstallSurfaceComputerUseWinPluginRoot(string repoRoot)
    {
        return Path.Combine(repoRoot, "plugins", "computer-use-win");
    }

    private static string GetInstallSurfaceComputerUseWinRuntimeRoot(string repoRoot)
    {
        return Path.Combine(GetInstallSurfaceComputerUseWinPluginRoot(repoRoot), "runtime", InstallSurfaceRuntimeRid);
    }

    private static string GetInstallSurfaceRuntimeFilePath(string runtimeRoot, string fileName)
    {
        return Path.Combine(runtimeRoot, fileName);
    }

    private static string GetInstallSurfaceTempRoot(string repoRoot, string name)
    {
        return Path.Combine(repoRoot, ".tmp", ".codex", "tests", name, Guid.NewGuid().ToString("N"));
    }

    private static string GetSetupCliAssemblyPath(string repoRoot)
    {
        XDocument document = XDocument.Load(Path.Combine(repoRoot, "Directory.Build.props"));
        string targetFramework = document.Root?
            .Elements("PropertyGroup")
            .Elements("TargetFramework")
            .FirstOrDefault()?
            .Value
            .Trim()
            ?? throw new InvalidOperationException("Directory.Build.props does not define TargetFramework.");
        return Path.Combine(repoRoot, "src", "WinBridge.Setup.Cli", "bin", "Debug", targetFramework, "WinBridge.Setup.Cli.dll");
    }

    private static string GetExpectedSharedRuntimeStoreRoot(string codexHome)
    {
        return Path.Combine(GetExpectedLocalAppDataRootFromCodexHome(codexHome), "Okno", "computer-use-win");
    }

    private static string GetExpectedUserProfileRootFromCodexHome(string codexHome)
    {
        string normalizedCodexHome = Path.GetFullPath(codexHome);
        if (string.Equals(Path.GetFileName(normalizedCodexHome), ".codex", StringComparison.OrdinalIgnoreCase))
        {
            string? parent = Directory.GetParent(normalizedCodexHome)?.FullName;
            if (string.IsNullOrWhiteSpace(parent))
            {
                throw new InvalidOperationException($"Unable to derive user profile root from CODEX_HOME '{codexHome}'.");
            }

            return parent;
        }

        return normalizedCodexHome;
    }

    private static string GetExpectedLocalAppDataRootFromCodexHome(string codexHome)
    {
        return Path.Combine(GetExpectedUserProfileRootFromCodexHome(codexHome), "AppData", "Local");
    }

    private static string GetExpectedSharedRuntimeRoot(string codexHome, string rid, string version)
    {
        return Path.Combine(GetExpectedSharedRuntimeStoreRoot(codexHome), "runtimes", rid, version);
    }

    private static string GetInstallSurfaceExpectedSharedRuntimeFilePath(string codexHome, string rid, string version, string fileName)
    {
        return Path.Combine(GetExpectedSharedRuntimeRoot(codexHome, rid, version), fileName);
    }

    private static string GetExpectedSharedRuntimeStatePath(string codexHome)
    {
        return Path.Combine(GetExpectedSharedRuntimeStoreRoot(codexHome), "state", "current-runtime.json");
    }

    private static string GetExpectedSharedRuntimeLauncherScriptPath(string codexHome)
    {
        return Path.Combine(GetExpectedSharedRuntimeStoreRoot(codexHome), "run-computer-use-win-runtime.ps1");
    }

    private static string GetExpectedRuntimeOnlyReceiptPath(string codexHome)
    {
        return Path.Combine(GetExpectedSharedRuntimeStoreRoot(codexHome), "receipts", "runtimeonly.json");
    }

    private static string GetExpectedCodexReceiptPath(string codexHome)
    {
        return Path.Combine(GetExpectedSharedRuntimeStoreRoot(codexHome), "receipts", "codex.json");
    }

    private static string GetExpectedInstalledPluginRoot(string codexHome)
    {
        return Path.Combine(codexHome, "plugins", "computer-use-win");
    }

    private static string GetExpectedPersonalMarketplacePath(string userProfile)
    {
        return Path.Combine(userProfile, ".agents", "plugins", "marketplace.json");
    }

    private static ScriptInvocationResult InvokeSetupCli(string repoRoot, IReadOnlyList<string> arguments, string codexHome, string? userProfileOverride = null)
    {
        string assemblyPath = GetSetupCliAssemblyPath(repoRoot);
        ProcessStartInfo startInfo = new()
        {
            FileName = "dotnet",
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        startInfo.ArgumentList.Add(assemblyPath);
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["CODEX_HOME"] = codexHome;
        startInfo.Environment["USERPROFILE"] = string.IsNullOrWhiteSpace(userProfileOverride)
            ? GetExpectedUserProfileRootFromCodexHome(codexHome)
            : userProfileOverride;
        startInfo.Environment["LOCALAPPDATA"] = string.IsNullOrWhiteSpace(userProfileOverride)
            ? GetExpectedLocalAppDataRootFromCodexHome(codexHome)
            : Path.Combine(userProfileOverride, "AppData", "Local");
        if (!string.IsNullOrWhiteSpace(userProfileOverride))
        {
            startInfo.Environment["USERPROFILE"] = userProfileOverride;
        }

        using Process process = new() { StartInfo = startInfo };
        process.Start();

        return ReadInstallSurfaceRedirectedProcessToEnd(
            process,
            process.StandardOutput.ReadToEndAsync(),
            process.StandardError.ReadToEndAsync(),
            TimeSpan.FromMinutes(5),
            stderr => $"Setup CLI timed out. {stderr}");
    }

    private static ScriptInvocationResult InvokePowerShellScript(string scriptPath, string workingDirectory, Action<ProcessStartInfo> configure)
    {
        TimeSpan timeout = TimeSpan.FromMinutes(5);
        ProcessStartInfo startInfo = CreateInstallSurfacePowerShellProcessStartInfo(workingDirectory);
        AddInstallSurfacePowerShellScriptArguments(startInfo, scriptPath);

        configure(startInfo);

        using Process process = new() { StartInfo = startInfo };
        process.Start();

        return ReadInstallSurfaceRedirectedProcessToEnd(
            process,
            process.StandardOutput.ReadToEndAsync(),
            process.StandardError.ReadToEndAsync(),
            timeout,
            stderr => $"PowerShell script timed out after {timeout}. {stderr}");
    }

    private static ScriptInvocationResult ReadInstallSurfaceRedirectedProcessToEnd(
        Process process,
        Task<string> stdoutTask,
        Task<string> stderrTask,
        TimeSpan timeout,
        Func<string, string> buildTimeoutStderr)
    {
        if (!process.WaitForExit(timeout))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            Task.WaitAll(stdoutTask, stderrTask);
            return new ScriptInvocationResult(-1, stdoutTask.Result, buildTimeoutStderr(stderrTask.Result));
        }

        Task.WaitAll(stdoutTask, stderrTask);
        return new ScriptInvocationResult(process.ExitCode, stdoutTask.Result, stderrTask.Result);
    }

    private static ProcessStartInfo CreateInstallSurfacePowerShellProcessStartInfo(string workingDirectory, bool redirectStandardInput = false)
    {
        return new ProcessStartInfo
        {
            FileName = "powershell",
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = redirectStandardInput,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
    }

    private static void AddInstallSurfacePowerShellScriptArguments(ProcessStartInfo startInfo, string scriptPath)
    {
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
    }

    private static JsonDocument ParseJsonStdoutOrThrow(ScriptInvocationResult result, string description)
    {
        Assert.True(
            result.ExitCode == 0,
            $"{description} failed. ExitCode={result.ExitCode}. stderr='{result.Stderr.Trim()}', stdout='{result.Stdout.Trim()}'.");
        Assert.False(
            string.IsNullOrWhiteSpace(result.Stdout),
            $"{description} returned empty stdout. stderr='{result.Stderr.Trim()}'.");
        return JsonDocument.Parse(result.Stdout);
    }

    private static RuntimeReleasePackageResult PackageRuntimeRelease(
        string repoRoot,
        string packageScriptPath,
        string runtimeRoot,
        string outputRoot,
        string version)
    {
        ScriptInvocationResult result = InvokePowerShellScript(
            packageScriptPath,
            repoRoot,
            startInfo =>
            {
                startInfo.ArgumentList.Add("-Version");
                startInfo.ArgumentList.Add(version);
                startInfo.ArgumentList.Add("-Rid");
                startInfo.ArgumentList.Add(InstallSurfaceRuntimeRid);
                startInfo.ArgumentList.Add("-PublishSourceRoot");
                startInfo.ArgumentList.Add(runtimeRoot);
                startInfo.ArgumentList.Add("-OutputRoot");
                startInfo.ArgumentList.Add(outputRoot);
            });

        using JsonDocument payload = ParseJsonStdoutOrThrow(result, "Release packaging script");
        return new RuntimeReleasePackageResult(
            payload.RootElement.GetProperty("archivePath").GetString()
                ?? throw new InvalidOperationException("archivePath missing."),
            payload.RootElement.GetProperty("descriptorPath").GetString()
                ?? throw new InvalidOperationException("descriptorPath missing."),
            payload.RootElement.GetProperty("resultPath").GetString()
                ?? throw new InvalidOperationException("resultPath missing."));
    }

    private static string CreateModifiedRuntimePackagingResult(
        string outputRoot,
        string originalResultPath,
        string? descriptorPathOverride = null,
        string? downloadUrlOverride = null,
        string? sha256Override = null,
        string? ridOverride = null,
        string? archivePathOverride = null,
        string? checksumPathOverride = null)
    {
        using JsonDocument original = JsonDocument.Parse(File.ReadAllText(originalResultPath));
        JsonElement root = original.RootElement;
        string modifiedResultPath = Path.Combine(outputRoot, Guid.NewGuid().ToString("N") + ".runtime-packaging-result.json");
        File.WriteAllText(
            modifiedResultPath,
            JsonSerializer.Serialize(new
            {
                version = root.GetProperty("version").GetString(),
                rid = ridOverride ?? root.GetProperty("rid").GetString(),
                tag = root.GetProperty("tag").GetString(),
                assetName = root.GetProperty("assetName").GetString(),
                archivePath = archivePathOverride ?? root.GetProperty("archivePath").GetString(),
                checksumPath = checksumPathOverride ?? root.GetProperty("checksumPath").GetString(),
                descriptorPath = descriptorPathOverride ?? root.GetProperty("descriptorPath").GetString(),
                resultPath = modifiedResultPath,
                downloadUrl = downloadUrlOverride ?? root.GetProperty("downloadUrl").GetString(),
                sha256 = sha256Override ?? root.GetProperty("sha256").GetString(),
            }));
        return modifiedResultPath;
    }

    private static string GetRepositoryRoot()
    {
        return InstallSurfaceCachedRepositoryRoot.Value;
    }

    private static string FindInstallSurfaceRepositoryRoot()
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

    private static WorkerProbeResult InvokeUiaWorkerSnapshotAgainstMissingWindow(string workerExecutablePath)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = workerExecutablePath,
            WorkingDirectory = Path.GetDirectoryName(workerExecutablePath)!,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using Process process = new() { StartInfo = startInfo };
        process.Start();
        string payload = JsonSerializer.Serialize(new
        {
            operation = "snapshot",
            targetWindow = new
            {
                hwnd = 1,
                title = "Missing window",
                processName = "missing",
                processId = (int?)null,
                threadId = (int?)null,
                className = string.Empty,
                bounds = new
                {
                    left = 0,
                    top = 0,
                    right = 10,
                    bottom = 10,
                },
                isForeground = true,
                isVisible = true,
            },
            snapshotRequest = new
            {
                depth = 1,
                maxNodes = 5,
            },
        });

        process.StandardInput.Write(payload);
        process.StandardInput.Close();
        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromSeconds(15)))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            Task.WaitAll(stdoutTask, stderrTask);
            return new WorkerProbeResult(-1, stdoutTask.Result, stderrTask.Result);
        }

        Task.WaitAll(stdoutTask, stderrTask);
        return new WorkerProbeResult(process.ExitCode, stdoutTask.Result, stderrTask.Result);
    }

    private static string BuildPluginLauncherFailureContext(PluginLauncherStartContext context, Process process, string stderr)
    {
        StringBuilder builder = new();
        builder.AppendLine("Launcher failure context:");
        builder.AppendLine("- pluginRoot: " + context.PluginRoot);
        builder.AppendLine("- runId: " + context.RunId);
        builder.AppendLine("- runRoot: " + context.RunRoot);
        builder.AppendLine("- artifactsRoot: " + context.ArtifactsRoot);
        builder.AppendLine("- descriptorOverride: " + (context.RuntimeReleaseDescriptorOverridePath ?? "<none>"));
        builder.AppendLine("- hasExited: " + process.HasExited);
        if (process.HasExited)
        {
            builder.AppendLine("- exitCode: " + process.ExitCode);
        }

        if (!string.IsNullOrWhiteSpace(stderr))
        {
            builder.AppendLine("- stderr:");
            builder.AppendLine(stderr.Trim());
        }

        return builder.ToString().TrimEnd();
    }

    private static bool IncludeAllInstallSurfaceFiles(string _) => true;

    private static bool ExcludeInstallSurfacePluginRuntimeFiles(string relativePath)
    {
        return !relativePath.StartsWith($"runtime{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ExcludeInstallSurfaceRuntimeWorkspaceFiles(string relativePath)
    {
        return !relativePath.StartsWith($"runtime{Path.DirectorySeparatorChar}{InstallSurfaceRuntimeRid}.", StringComparison.OrdinalIgnoreCase);
    }

    private static void DeleteInstallSurfaceRuntimeReleaseDescriptor(string pluginRoot)
    {
        File.Delete(Path.Combine(pluginRoot, InstallSurfaceRuntimeReleaseDescriptorFileName));
    }

    private static void DeleteInstallSurfacePluginRuntimeFile(string pluginRoot, string fileName)
    {
        string filePath = Path.Combine(pluginRoot, "runtime", InstallSurfaceRuntimeRid, fileName);
        Assert.True(File.Exists(filePath));
        File.Delete(filePath);
    }

    private static string[] GetInstallSurfaceRuntimeWorkspaceCandidates(string runtimeParent, InstallSurfaceRuntimeWorkspaceKind workspaceKind)
    {
        if (!Directory.Exists(runtimeParent))
        {
            return [];
        }

        string suffix = workspaceKind switch
        {
            InstallSurfaceRuntimeWorkspaceKind.Backup => "backup",
            InstallSurfaceRuntimeWorkspaceKind.Repair => "repair",
            _ => throw new ArgumentOutOfRangeException(nameof(workspaceKind), workspaceKind, null),
        };

        return Directory.GetDirectories(runtimeParent, $"{InstallSurfaceRuntimeRid}.{suffix}-*", SearchOption.TopDirectoryOnly);
    }

    private static void DeleteInstallSurfaceRuntimeWorkspaceCandidates(string runtimeParent, InstallSurfaceRuntimeWorkspaceKind workspaceKind)
    {
        foreach (string candidate in GetInstallSurfaceRuntimeWorkspaceCandidates(runtimeParent, workspaceKind))
        {
            DeleteDirectoryIfExists(candidate);
        }
    }

    private sealed class InstallSurfaceRuntimePublishBackupScenario(
        string repoRoot,
        string scriptPath,
        string runtimeRoot,
        string backupRoot,
        bool cleanupBackupWorkspaces,
        bool cleanupRepairWorkspaces) : IDisposable
    {
        public string RepoRoot { get; } = repoRoot;
        public string ScriptPath { get; } = scriptPath;
        public string RuntimeRoot { get; } = runtimeRoot;
        public string RuntimeParent { get; } = Path.GetDirectoryName(runtimeRoot)!;
        public string BackupRoot { get; } = backupRoot;

        public void EnsurePublishedBaseline()
        {
            EnsurePublishedRuntimeBundle(RepoRoot, ScriptPath, RuntimeRoot);
        }

        public void CopyRuntimeToBackup()
        {
            Assert.True(Directory.Exists(RuntimeRoot), $"Runtime root '{RuntimeRoot}' must exist before it can be backed up for the scenario.");
            CopyDirectory(RuntimeRoot, BackupRoot, IncludeAllInstallSurfaceFiles);
        }

        public void ReplaceRuntimeWithBackupCopy()
        {
            CopyRuntimeToBackup();
            DeleteDirectoryIfExists(RuntimeRoot);
            CopyDirectory(BackupRoot, RuntimeRoot, IncludeAllInstallSurfaceFiles);
        }

        public void DeleteRuntimeFile(string fileName)
        {
            string filePath = GetInstallSurfaceRuntimeFilePath(RuntimeRoot, fileName);
            Assert.True(File.Exists(filePath));
            File.Delete(filePath);
        }

        public ScriptInvocationResult InvokeWithPreparedPublishSource(params string[] switchNames)
        {
            return InvokeInstallSurfacePublishScriptWithPreparedSource(ScriptPath, RepoRoot, BackupRoot, switchNames);
        }

        public void Dispose()
        {
            DeleteDirectoryIfExists(RuntimeRoot);
            if (Directory.Exists(BackupRoot))
            {
                CopyDirectory(BackupRoot, RuntimeRoot, IncludeAllInstallSurfaceFiles);
            }

            DeleteDirectoryIfExists(BackupRoot);
            if (cleanupBackupWorkspaces)
            {
                DeleteInstallSurfaceRuntimeWorkspaceCandidates(RuntimeParent, InstallSurfaceRuntimeWorkspaceKind.Backup);
            }

            if (cleanupRepairWorkspaces)
            {
                DeleteInstallSurfaceRuntimeWorkspaceCandidates(RuntimeParent, InstallSurfaceRuntimeWorkspaceKind.Repair);
            }
        }
    }

    private enum InstallSurfaceRuntimeWorkspaceKind
    {
        Backup,
        Repair,
    }

    private sealed class PluginMcpSession(StreamReader reader, StreamWriter writer, Func<string> failureContextFactory)
    {
        private static readonly TimeSpan DefaultResponseTimeout = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan InitializeResponseTimeout = TimeSpan.FromSeconds(45);
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
            TimeSpan responseTimeout = string.Equals(requestName, "initialize", StringComparison.OrdinalIgnoreCase)
                ? InitializeResponseTimeout
                : DefaultResponseTimeout;

            while (true)
            {
                string? line;
                try
                {
                    line = await reader.ReadLineAsync().WaitAsync(responseTimeout);
                }
                catch (TimeoutException)
                {
                    throw new Xunit.Sdk.XunitException($"Timed out waiting for '{requestName}' response after {responseTimeout}.{Environment.NewLine}{failureContextFactory()}");
                }

                if (line is null)
                {
                    throw new Xunit.Sdk.XunitException($"Plugin process exited before '{requestName}' response.{Environment.NewLine}{failureContextFactory()}");
                }

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

    private sealed record PluginLauncherStartContext(
        string PluginRoot,
        string RunId,
        string RunRoot,
        string ArtifactsRoot,
        string? RuntimeReleaseDescriptorOverridePath,
        string? CodexHomeOverridePath);

    private sealed class PluginLauncherSession(
        PluginLauncherStartContext context,
        Process process,
        StreamWriter writer,
        StreamReader reader,
        Task<string> stderrTask) : IAsyncDisposable
    {
        public PluginMcpSession CreateMcpSession() => new(reader, writer, GetFailureContext);

        private string GetFailureContext()
        {
            string stderr = stderrTask.IsCompletedSuccessfully ? stderrTask.Result : string.Empty;
            return BuildPluginLauncherFailureContext(context, process, stderr);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (!process.HasExited)
                {
                    try
                    {
                        writer.Close();
                    }
                    catch
                    {
                    }

                    Task waitTask = process.WaitForExitAsync();
                    Task completedTask = await Task.WhenAny(waitTask, Task.Delay(TimeSpan.FromSeconds(5)));
                    if (!ReferenceEquals(completedTask, waitTask) && !process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync();
                    }
                }

                await stderrTask;
            }
            finally
            {
                writer.Dispose();
                reader.Dispose();
                process.Dispose();
                DeleteInstallSurfacePluginLauncherContextDirectories(context);
            }
        }
    }

    private sealed record ScriptInvocationResult(int ExitCode, string Stdout, string Stderr);

    private sealed record RuntimeReleasePackageResult(string ArchivePath, string DescriptorPath, string ResultPath);

    private sealed record WorkerProbeResult(int ExitCode, string Stdout, string Stderr);
}
