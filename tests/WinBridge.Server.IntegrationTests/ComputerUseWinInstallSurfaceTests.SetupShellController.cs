// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Setup.Core;

namespace WinBridge.Server.IntegrationTests;

public sealed partial class ComputerUseWinInstallSurfaceTests
{
    private const string SetupCodexRoot = @"C:\Users\user\.codex";
    private const string SetupAppRoot = @"C:\Users\user\AppData\Local\Okno\computer-use-win";
    private const string SetupShellRoot = @"C:\Users\user\AppData\Local\Okno\setup-shell\current";
    private const string SetupStartMenuShortcutPath = @"C:\Users\user\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Okno Setup.lnk";
    private const string SetupUninstallRegistryKeyPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\Okno";
    private const string SetupRuntimeVersion = "0.2.1";
    private const string SetupRuntimeRid = "win-x64";
    private const string SetupRuntimeRoot = $@"{SetupAppRoot}\runtimes\{SetupRuntimeRid}\{SetupRuntimeVersion}";
    private const string SetupStatePath = $@"{SetupAppRoot}\state\current-runtime.json";
    private const string SetupRuntimeSnippet = "{ \"mcpServers\": {} }";

    [Fact]
    public async Task SetupShellControllerMapsRuntimeOnlyInstallToSnippetSummaryAsync()
    {
        List<string> operations = [];
        SetupShellController controller = new(
            () => CreateSetupStatus(SetupShellInstalledState.None, runtimeAvailable: false, runtimeFailureReason: "current_state_missing"),
            operation =>
            {
                operations.Add(operation.OperationKind + ":" + operation.Mode);
                Assert.Equal(SetupShellOperationKind.Install, operation.OperationKind);
                Assert.Equal(ComputerUseWinInstallMode.RuntimeOnly, operation.Mode);
                return CreateSetupResult(
                    action: "install",
                    installModeName: "runtime_only",
                    pluginSourceRoot: null,
                    marketplacePath: null,
                    marketplaceEntryId: null,
                    relativePluginPath: null,
                    restartRequired: false,
                    snippet: SetupRuntimeSnippet,
                    receiptFileName: "runtimeonly.json");
            },
            CreateNoOpRegistrationService());

        SetupShellOperationSummary summary = await controller.ExecutePrimaryActionAsync(ComputerUseWinInstallMode.RuntimeOnly);

        Assert.Equal("Runtime-only install completed.", summary.Title);
        Assert.NotNull(summary.Snippet);
        Assert.False(summary.RestartRequired);
        Assert.Null(summary.PluginSourceRoot);
        Assert.Equal(["Install:RuntimeOnly"], operations);
    }

    [Fact]
    public async Task SetupShellControllerMapsCodexInstallToRestartSummaryAsync()
    {
        List<string> operations = [];
        SetupShellController controller = new(
            () => CreateSetupStatus(SetupShellInstalledState.RuntimeOnly, runtimeAvailable: true, runtimeFailureReason: null),
            operation =>
            {
                operations.Add(operation.OperationKind + ":" + operation.Mode);
                Assert.Equal(SetupShellOperationKind.Install, operation.OperationKind);
                Assert.Equal(ComputerUseWinInstallMode.Codex, operation.Mode);
                return CreateSetupResult(
                    action: "install",
                    installModeName: "codex",
                    pluginSourceRoot: $@"{SetupCodexRoot}\plugins\computer-use-win",
                    marketplacePath: @"C:\Users\user\.agents\plugins\marketplace.json",
                    marketplaceEntryId: "okno-local-installed",
                    relativePluginPath: "./.codex/plugins/computer-use-win",
                    restartRequired: true,
                    snippet: null,
                    receiptFileName: "codex.json");
            },
            CreateNoOpRegistrationService());

        SetupShellOperationSummary summary = await controller.ExecutePrimaryActionAsync(ComputerUseWinInstallMode.Codex);

        Assert.Equal("Install for Codex completed.", summary.Title);
        Assert.True(summary.RestartRequired);
        Assert.NotNull(summary.PluginSourceRoot);
        Assert.NotNull(summary.MarketplacePath);
        Assert.Null(summary.Snippet);
        Assert.Equal(["Install:Codex"], operations);
    }

    [Fact]
    public void SetupShellControllerMapsInstalledStatesToLifecycleSnapshot()
    {
        SetupShellController controller = new(
            () => CreateSetupStatus(SetupShellInstalledState.CodexAndRuntimeOnly, runtimeAvailable: true, runtimeFailureReason: null),
            _ => throw new Xunit.Sdk.XunitException("No lifecycle action should run while reading status."),
            CreateNoOpRegistrationService());

        SetupShellStatusSnapshot snapshot = controller.GetStatusSnapshot();

        Assert.Equal(SetupShellInstalledState.CodexAndRuntimeOnly, snapshot.InstalledState);
        Assert.True(snapshot.HasCodexInstall);
        Assert.True(snapshot.HasRuntimeOnlyInstall);
        Assert.True(snapshot.RuntimeReady);
    }

    [Fact]
    public async Task SetupShellControllerMapsSelectedInstalledModeToReinstallActionAsync()
    {
        List<string> operations = [];
        SetupShellController controller = new(
            () => CreateSetupStatus(SetupShellInstalledState.Codex, runtimeAvailable: true, runtimeFailureReason: null),
            operation =>
            {
                operations.Add(operation.OperationKind + ":" + operation.Mode);
                Assert.Equal(SetupShellOperationKind.Reinstall, operation.OperationKind);
                return CreateSetupResult(
                    action: "update",
                    installModeName: "codex",
                    pluginSourceRoot: $@"{SetupCodexRoot}\plugins\computer-use-win",
                    marketplacePath: @"C:\Users\user\.agents\plugins\marketplace.json",
                    marketplaceEntryId: "okno-local-installed",
                    relativePluginPath: "./.codex/plugins/computer-use-win",
                    restartRequired: true,
                    snippet: null,
                    receiptFileName: "codex.json");
            },
            CreateNoOpRegistrationService());

        SetupShellOperationSummary summary = await controller.ExecutePrimaryActionAsync(ComputerUseWinInstallMode.Codex);

        Assert.Equal(SetupShellOperationKind.Reinstall, summary.OperationKind);
        Assert.Equal(["Reinstall:Codex"], operations);
    }

    [Fact]
    public async Task SetupShellControllerMapsRepairActionAsync()
    {
        List<string> operations = [];
        SetupShellController controller = new(
            () => CreateSetupStatus(SetupShellInstalledState.RuntimeOnly, runtimeAvailable: true, runtimeFailureReason: null),
            operation =>
            {
                operations.Add(operation.OperationKind + ":" + operation.Mode);
                Assert.Equal(SetupShellOperationKind.Repair, operation.OperationKind);
                return CreateSetupResult(
                    action: "repair",
                    installModeName: "runtime_only",
                    pluginSourceRoot: null,
                    marketplacePath: null,
                    marketplaceEntryId: null,
                    relativePluginPath: null,
                    restartRequired: false,
                    snippet: SetupRuntimeSnippet,
                    receiptFileName: "runtimeonly.json");
            },
            CreateNoOpRegistrationService());

        SetupShellOperationSummary summary = await controller.RepairAsync(ComputerUseWinInstallMode.RuntimeOnly);

        Assert.Equal(SetupShellOperationKind.Repair, summary.OperationKind);
        Assert.Equal(["Repair:RuntimeOnly"], operations);
    }

    [Fact]
    public async Task SetupShellControllerMapsFullRemoveActionAsync()
    {
        List<string> operations = [];
        SetupShellController controller = new(
            () => CreateSetupStatus(SetupShellInstalledState.CodexAndRuntimeOnly, runtimeAvailable: true, runtimeFailureReason: null),
            operation =>
            {
                operations.Add(operation.OperationKind + ":" + operation.Mode);
                Assert.Equal(SetupShellOperationKind.RemoveAll, operation.OperationKind);
                return CreateSetupResult(
                    action: "remove-all",
                    installModeName: "all",
                    pluginSourceRoot: null,
                    marketplacePath: null,
                    marketplaceEntryId: null,
                    relativePluginPath: null,
                    restartRequired: false,
                    snippet: null,
                    receiptFileName: "all.json");
            },
            CreateNoOpRegistrationService());

        SetupShellOperationSummary summary = await controller.RemoveAllAsync();

        Assert.Equal(SetupShellOperationKind.RemoveAll, summary.OperationKind);
        Assert.Equal(["RemoveAll:"], operations);
    }

    [Fact]
    public void SetupShellControllerBuildsModePresentationFromState()
    {
        SetupShellController controller = new(
            () => CreateSetupStatus(SetupShellInstalledState.RuntimeOnly, runtimeAvailable: true, runtimeFailureReason: null),
            _ => throw new Xunit.Sdk.XunitException("No lifecycle action should run while building mode presentation."),
            CreateNoOpRegistrationService());

        SetupShellModePresentation runtimeOnly = controller.GetModePresentation(ComputerUseWinInstallMode.RuntimeOnly);
        SetupShellModePresentation codex = controller.GetModePresentation(ComputerUseWinInstallMode.Codex);

        Assert.Equal("Reinstall runtime only", runtimeOnly.PrimaryActionLabel);
        Assert.Equal("Install for Codex", codex.PrimaryActionLabel);
        Assert.True(runtimeOnly.CanRepair);
        Assert.True(runtimeOnly.CanRemove);
    }

    [Fact]
    public void ShellRegistrationServiceRegistersInstalledAppEntry()
    {
        using SetupShellRegistrationScenario scenario = new("register");
        OknoSetupShellRegistrationService service = scenario.CreateService();

        service.RegisterShell(
            sourceRoot: scenario.SourceRoot,
            displayVersion: "0.2.1",
            currentExecutablePathOverride: Path.Combine(scenario.SourceRoot, "Okno Setup.exe"));

        Assert.True(File.Exists(Path.Combine(scenario.CurrentShellRoot, "Okno Setup.exe")));
        Assert.True(File.Exists(scenario.ShortcutPath));
        Assert.Equal("Okno", scenario.ReadRegistryValue("DisplayName"));
        Assert.Equal("Vlasov Vitaly", scenario.ReadRegistryValue("Publisher"));
        Assert.Contains("--operation remove-all", scenario.ReadRegistryValue("UninstallString"), StringComparison.Ordinal);
        Assert.Contains("--quiet", scenario.ReadRegistryValue("QuietUninstallString"), StringComparison.Ordinal);
    }

    [Fact]
    public void ShellRegistrationServiceRefreshesStableShellFromExternalSource()
    {
        using SetupShellRegistrationScenario scenario = new("refresh");
        File.WriteAllText(Path.Combine(scenario.SourceRoot, "marker.txt"), "v2");
        File.WriteAllText(Path.Combine(scenario.CurrentShellRoot, "marker.txt"), "v1");
        OknoSetupShellRegistrationService service = scenario.CreateService();

        service.RegisterShell(
            sourceRoot: scenario.SourceRoot,
            displayVersion: "0.2.1",
            currentExecutablePathOverride: Path.Combine(scenario.SourceRoot, "Okno Setup.exe"));

        Assert.Equal("v2", File.ReadAllText(Path.Combine(scenario.CurrentShellRoot, "marker.txt")));
        Assert.Equal("0.2.1", scenario.ReadRegistryValue("DisplayVersion"));
    }

    [Fact]
    public async Task ShellRegistrationServiceSchedulesDeferredCleanupAsync()
    {
        using SetupShellRegistrationScenario scenario = new("cleanup");
        OknoSetupShellRegistrationService service = scenario.CreateService();
        string cleanupRoot = Path.Combine(scenario.Root, "cleanup-target");
        Directory.CreateDirectory(cleanupRoot);
        File.WriteAllText(Path.Combine(cleanupRoot, "locked.txt"), "cleanup");

        using System.Diagnostics.Process process = new()
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }
        };
        process.StartInfo.ArgumentList.Add("-NoLogo");
        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-NonInteractive");
        process.StartInfo.ArgumentList.Add("-Command");
        process.StartInfo.ArgumentList.Add("Start-Sleep -Seconds 1");
        Assert.True(process.Start());

        service.ScheduleDeferredCleanup(process.Id, cleanupRoot);
        await process.WaitForExitAsync();

        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (Directory.Exists(cleanupRoot) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(200);
        }

        Assert.False(Directory.Exists(cleanupRoot));
    }

    [Fact]
    public void ShellRegistrationServiceUnregistersInstalledAppEntry()
    {
        using SetupShellRegistrationScenario scenario = new("unregister");
        OknoSetupShellRegistrationService service = scenario.CreateService();
        service.RegisterShell(
            sourceRoot: scenario.SourceRoot,
            displayVersion: "0.2.1",
            currentExecutablePathOverride: Path.Combine(scenario.SourceRoot, "Okno Setup.exe"));

        service.UnregisterShell();

        Assert.False(scenario.RegistryKeyExists());
        Assert.False(File.Exists(scenario.ShortcutPath));
    }

    [Fact]
    public void ShellRegistrationServiceRemovesStableShellImmediatelyWhenRunningFromExternalInstaller()
    {
        using SetupShellRegistrationScenario scenario = new("remove-shell-external");
        OknoSetupShellRegistrationService service = scenario.CreateService();
        service.RegisterShell(
            sourceRoot: scenario.SourceRoot,
            displayVersion: "0.2.1",
            currentExecutablePathOverride: Path.Combine(scenario.SourceRoot, "Okno Setup.exe"));

        bool cleanupScheduled = service.RemoveShellArtifacts(Path.Combine(scenario.Root, "external-installer"), currentProcessId: 4242);

        Assert.False(cleanupScheduled);
        Assert.False(Directory.Exists(Path.Combine(scenario.Root, "setup-shell")));
        Assert.False(scenario.RegistryKeyExists());
        Assert.False(File.Exists(scenario.ShortcutPath));
    }

    private static ComputerUseWinInstallerStatus CreateSetupStatus(SetupShellInstalledState installedState, bool runtimeAvailable, string? runtimeFailureReason) =>
        new(
            1,
            SetupCodexRoot,
            SetupAppRoot,
            new ComputerUseWinRuntimeStatus(
                1, SetupCodexRoot, SetupAppRoot, SetupStatePath,
                runtimeAvailable, runtimeAvailable, runtimeAvailable,
                runtimeAvailable ? SetupRuntimeRoot : null,
                runtimeFailureReason,
                null),
            installedState is SetupShellInstalledState.RuntimeOnly or SetupShellInstalledState.CodexAndRuntimeOnly
                ? CreateReceipt("runtime_only", null, null, null, "runtimeonly.json")
                : null,
            installedState is SetupShellInstalledState.Codex or SetupShellInstalledState.CodexAndRuntimeOnly
                ? CreateReceipt("codex", $@"{SetupCodexRoot}\plugins\computer-use-win", @"C:\Users\user\.agents\plugins\marketplace.json", "./.codex/plugins/computer-use-win", "codex.json")
                : null);

    private static ComputerUseWinInstallReceipt CreateReceipt(string modeName, string? pluginSourceRoot, string? marketplacePath, string? marketplaceSourcePath, string receiptFileName)
    {
        DateTimeOffset completedAt = DateTimeOffset.UtcNow;
        return new(
            1,
            modeName,
            "computer-use-win",
            pluginSourceRoot is null ? null : "0.2.1",
            SetupRuntimeVersion,
            SetupRuntimeRid,
            SetupRuntimeRoot,
            pluginSourceRoot,
            marketplacePath,
            pluginSourceRoot is null ? null : "okno-local-installed",
            marketplaceSourcePath,
            pluginSourceRoot is not null,
            completedAt,
            completedAt);
    }

    private static ComputerUseWinInstallerResult CreateSetupResult(
        string action, string installModeName, string? pluginSourceRoot, string? marketplacePath, string? marketplaceEntryId,
        string? relativePluginPath, bool restartRequired, string? snippet, string receiptFileName)
    {
        DateTimeOffset completedAt = DateTimeOffset.UtcNow;

        return new(
            1, action, installModeName, SetupCodexRoot, SetupAppRoot, SetupRuntimeRoot,
            SetupRuntimeVersion, SetupRuntimeRid, pluginSourceRoot, marketplacePath,
            marketplaceEntryId, relativePluginPath, restartRequired, snippet,
            $@"{SetupAppRoot}\receipts\{receiptFileName}", completedAt, completedAt);
    }

    private static OknoSetupShellRegistrationService CreateNoOpRegistrationService() =>
        new(new OknoSetupShellRegistrationOptions(
            SetupShellRoot,
            Path.Combine(SetupShellRoot, "Okno Setup.exe"),
            SetupStartMenuShortcutPath,
            SetupUninstallRegistryKeyPath,
            "Okno",
            "Vlasov Vitaly",
            processId => throw new Xunit.Sdk.XunitException($"Deferred cleanup is not expected in this scenario. pid={processId}"),
            (sourceRoot, destinationRoot) => { },
            (shortcutPath, targetPath) => { },
            (keyPath, values) => { },
            _ => { },
            _ => { },
            _ => false));

    private sealed class SetupShellRegistrationScenario : IDisposable
    {
        private readonly string registryKeyPath;
        private readonly string startMenuRoot;

        public SetupShellRegistrationScenario(string scenarioName)
        {
            Root = Path.Combine(GetRepositoryRoot(), ".tmp", ".codex", "tests", "setup-shell-registration", scenarioName, Guid.NewGuid().ToString("N"));
            SourceRoot = Path.Combine(Root, "source");
            CurrentShellRoot = Path.Combine(Root, "setup-shell", "current");
            startMenuRoot = Path.Combine(Root, "StartMenu");
            ShortcutPath = Path.Combine(startMenuRoot, "Okno Setup.lnk");
            registryKeyPath = $@"Software\CodexTests\OknoSetup\{scenarioName}\{Guid.NewGuid():N}";

            Directory.CreateDirectory(SourceRoot);
            Directory.CreateDirectory(CurrentShellRoot);
            Directory.CreateDirectory(startMenuRoot);
            File.WriteAllText(Path.Combine(SourceRoot, "Okno Setup.exe"), "exe");
        }

        public string Root { get; }

        public string SourceRoot { get; }

        public string CurrentShellRoot { get; }

        public string ShortcutPath { get; }

        public OknoSetupShellRegistrationService CreateService() =>
            new(new OknoSetupShellRegistrationOptions(
                Path.Combine(Root, "setup-shell"),
                Path.Combine(CurrentShellRoot, "Okno Setup.exe"),
                ShortcutPath,
                $@"HKCU\{registryKeyPath}",
                "Okno",
                "Vlasov Vitaly",
                processId => OknoSetupShellRegistrationService.StartPowerShellCleanupHelper(processId),
                CopyDirectoryContentsForShell,
                (shortcutPath, targetPath) =>
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);
                    File.WriteAllText(shortcutPath, targetPath);
                },
                WriteRegistryValues,
                DeleteRegistryKey,
                DeleteDirectoryIfExists,
                _ => RegistryKeyExists()));

        public string ReadRegistryValue(string name)
        {
            using Microsoft.Win32.RegistryKey? key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(registryKeyPath);
            return key?.GetValue(name)?.ToString()
                ?? throw new InvalidOperationException($"Registry value '{name}' is missing.");
        }

        public bool RegistryKeyExists()
        {
            using Microsoft.Win32.RegistryKey? key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(registryKeyPath);
            return key is not null;
        }

        public void Dispose()
        {
            DeleteRegistryKey($@"HKCU\{registryKeyPath}");
            DeleteDirectoryIfExists(Root);
        }

        private static void WriteRegistryValues(string keyPath, IReadOnlyDictionary<string, object> values)
        {
            if (!keyPath.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Unsupported test registry root '{keyPath}'.");
            }

            string relativePath = keyPath["HKCU\\".Length..];
            using Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(relativePath)
                ?? throw new InvalidOperationException($"Failed to create registry key '{keyPath}'.");
            foreach ((string name, object value) in values)
            {
                key.SetValue(name, value);
            }
        }

        private static void DeleteRegistryKey(string keyPath)
        {
            if (!keyPath.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string relativePath = keyPath["HKCU\\".Length..];
            try
            {
                Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(relativePath, throwOnMissingSubKey: false);
            }
            catch
            {
            }
        }

        private static void CopyDirectoryContentsForShell(string sourceRoot, string destinationRoot)
        {
            Directory.CreateDirectory(destinationRoot);
            foreach (string directory in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(sourceRoot, directory);
                Directory.CreateDirectory(Path.Combine(destinationRoot, relative));
            }

            foreach (string filePath in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(sourceRoot, filePath);
                string destinationPath = Path.Combine(destinationRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                File.Copy(filePath, destinationPath, overwrite: true);
            }
        }
    }
}
