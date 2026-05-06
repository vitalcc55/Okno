// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace WinBridge.Server.IntegrationTests;

public sealed partial class ComputerUseWinInstallSurfaceTests
{
    private const string BootstrapInstallerTestVersion = "0.1.0";
    private const string RuntimeIdentifier = "win-x64";
    private const string SetupAppExecutableName = "Okno Setup.exe";
    private const string SetupAppWindowTitle = "Okno Setup";
    private static readonly TimeSpan SetupAppLaunchTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SetupAppLaunchPollInterval = TimeSpan.FromMilliseconds(100);

    [Fact]
    public void PackageComputerUseWinSetupCliPayloadProducesVersionedArchive()
    {
        string repoRoot = GetRepositoryRoot();
        string outputRoot = CreateBootstrapTestOutputRoot(repoRoot, "setup-cli-payload");

        try
        {
            string archivePath = PackageSetupCliPayload(repoRoot, outputRoot);
            string checksumPath = Path.Combine(outputRoot, $"okno-setup-cli-payload-{BootstrapInstallerTestVersion}-SHA256SUMS.txt");
            Assert.True(File.Exists(archivePath));
            Assert.True(File.Exists(checksumPath));

            HashSet<string> entries = ReadBootstrapArchiveEntryPaths(archivePath);
            Assert.Contains("WinBridge.Setup.Cli.exe", entries, StringComparer.Ordinal);
            Assert.Contains("WinBridge.Setup.Cli.dll", entries, StringComparer.Ordinal);
            Assert.Contains("runtime-release.json", entries, StringComparer.Ordinal);
        }
        finally { DeleteDirectoryIfExists(outputRoot); }
    }

    [Fact]
    public void PackageOknoSetupAppReleaseProducesArchiveWithOknoSetupExecutable()
    {
        string repoRoot = GetRepositoryRoot();
        string outputRoot = CreateBootstrapTestOutputRoot(repoRoot, "setup-app-release");

        try
        {
            string archivePath = PackageSetupAppRelease(repoRoot, outputRoot);
            Assert.True(File.Exists(archivePath));

            HashSet<string> entries = ReadBootstrapArchiveEntryPaths(archivePath);
            Assert.Contains(SetupAppExecutableName, entries, StringComparer.Ordinal);
            Assert.Contains("WinBridge.Setup.App.dll", entries, StringComparer.Ordinal);
            Assert.Contains("runtime-release.json", entries, StringComparer.Ordinal);
            Assert.DoesNotContain("WinBridge.Setup.App.exe", entries, StringComparer.Ordinal);
        }
        finally { DeleteDirectoryIfExists(outputRoot); }
    }

    [Fact]
    public void PackagedOknoSetupAppLaunchesFromOwnAndExternalWorkingDirectories()
    {
        string repoRoot = GetRepositoryRoot();
        string outputRoot = CreateBootstrapTestOutputRoot(repoRoot, "setup-app-launch");
        string extractRoot = Path.Combine(outputRoot, "extract");

        try
        {
            string archivePath = PackageSetupAppRelease(repoRoot, outputRoot);
            ZipFile.ExtractToDirectory(archivePath, extractRoot);

            string executablePath = Path.Combine(extractRoot, SetupAppExecutableName);
            Assert.True(File.Exists(executablePath), "Packaged setup executable is missing.");

            AssertSetupAppLaunches(executablePath, extractRoot, "own package directory");
            AssertSetupAppLaunches(executablePath, repoRoot, "external working directory");
        }
        finally { DeleteDirectoryIfExists(outputRoot); }
    }

    [Fact]
    public void BootstrapInstallerInstallsRuntimeOnlyFromLocalPayloadArchive()
    {
        string repoRoot = GetRepositoryRoot();
        string outputRoot = CreateBootstrapTestOutputRoot(repoRoot, "bootstrap-runtime-only");
        string userProfile = CreateBootstrapTestUserProfileRoot(repoRoot, "bootstrap-runtime-only");
        string codexHome = GetBootstrapCodexHome(userProfile);

        try
        {
            string descriptorPath = CreateLocalRuntimeReleaseDescriptor(repoRoot, outputRoot);
            string payloadArchivePath = PackageSetupCliPayload(repoRoot, outputRoot);
            ScriptInvocationResult result = InvokeBootstrapInstaller(repoRoot, "runtime-only", payloadArchivePath, descriptorPath, userProfile);

            AssertBootstrapInstallerSucceeded(result, "runtime-only");
            Assert.True(File.Exists(GetExpectedRuntimeOnlyReceiptPath(codexHome)));
            Assert.False(File.Exists(GetExpectedPersonalMarketplacePath(userProfile)));
        }
        finally { DeleteBootstrapDirectoriesIfExists(outputRoot, codexHome, userProfile); }
    }

    [Fact]
    public void BootstrapInstallerInstallsCodexFromLocalPayloadArchive()
    {
        string repoRoot = GetRepositoryRoot();
        string outputRoot = CreateBootstrapTestOutputRoot(repoRoot, "bootstrap-codex");
        string userProfile = CreateBootstrapTestUserProfileRoot(repoRoot, "bootstrap-codex");
        string codexHome = GetBootstrapCodexHome(userProfile);

        try
        {
            string descriptorPath = CreateLocalRuntimeReleaseDescriptor(repoRoot, outputRoot);
            PackageLocalPluginRelease(repoRoot, outputRoot);
            string payloadArchivePath = PackageSetupCliPayload(repoRoot, outputRoot);
            ScriptInvocationResult result = InvokeBootstrapInstaller(repoRoot, "codex", payloadArchivePath, descriptorPath, userProfile);

            AssertBootstrapInstallerSucceeded(result, "codex");
            Assert.True(Directory.Exists(GetExpectedInstalledPluginRoot(codexHome)));
            Assert.True(File.Exists(GetExpectedPersonalMarketplacePath(userProfile)));
            Assert.True(File.Exists(GetExpectedCodexReceiptPath(codexHome)));
        }
        finally { DeleteBootstrapDirectoriesIfExists(outputRoot, codexHome, userProfile); }
    }

    private static string PackageSetupCliPayload(string repoRoot, string outputRoot) =>
        PackageSetupCliPayload(repoRoot, GetBootstrapCodexScriptPath(repoRoot, "package-computer-use-win-setup-cli-payload.ps1"), outputRoot, BootstrapInstallerTestVersion);

    private static string PackageSetupAppRelease(string repoRoot, string outputRoot) =>
        PackageSetupAppRelease(repoRoot, GetBootstrapCodexScriptPath(repoRoot, "package-okno-setup-app-release.ps1"), outputRoot, BootstrapInstallerTestVersion);

    private static string PackageSetupCliPayload(string repoRoot, string packageScriptPath, string outputRoot, string version) =>
        PackageBootstrapVersionedArchive(repoRoot, packageScriptPath, outputRoot, version, "Setup CLI payload packaging script");

    private static string PackageSetupAppRelease(string repoRoot, string packageScriptPath, string outputRoot, string version) =>
        PackageBootstrapVersionedArchive(repoRoot, packageScriptPath, outputRoot, version, "Setup app release packaging script");

    private static string PackageBootstrapVersionedArchive(string repoRoot, string packageScriptPath, string outputRoot, string version, string scriptDisplayName)
    {
        ScriptInvocationResult result = InvokePowerShellScript(
            packageScriptPath,
            repoRoot,
            startInfo => AddProcessArguments(startInfo, "-Version", version, "-OutputRoot", outputRoot));

        if (result.ExitCode != 0)
        {
            throw new Xunit.Sdk.XunitException($"{scriptDisplayName} failed. stderr='{result.Stderr}', stdout='{result.Stdout}'.");
        }

        using JsonDocument payload = ParseJsonStdoutOrThrow(result, scriptDisplayName);
        return payload.RootElement.GetProperty("archivePath").GetString()
            ?? throw new InvalidOperationException("archivePath missing.");
    }

    private static string CreateLocalRuntimeReleaseDescriptor(string repoRoot, string outputRoot)
    {
        string runtimeRoot = Path.Combine(repoRoot, "plugins", "computer-use-win", "runtime", RuntimeIdentifier);
        EnsurePublishedRuntimeBundle(repoRoot, GetPublishScriptPath(repoRoot), runtimeRoot);

        string runtimeArchivePath = PackageRuntimeRelease(
            repoRoot,
            GetBootstrapCodexScriptPath(repoRoot, "package-computer-use-win-runtime-release.ps1"),
            runtimeRoot,
            outputRoot,
            BootstrapInstallerTestVersion);

        return CreateRuntimeReleaseDescriptor(outputRoot, BootstrapInstallerTestVersion, runtimeArchivePath, RuntimeIdentifier);
    }

    private static void PackageLocalPluginRelease(string repoRoot, string outputRoot) =>
        PackagePluginRelease(repoRoot, GetBootstrapCodexScriptPath(repoRoot, "package-computer-use-win-plugin-release.ps1"), outputRoot, BootstrapInstallerTestVersion);

    private static ScriptInvocationResult InvokeBootstrapInstaller(string repoRoot, string mode, string payloadArchivePath, string descriptorPath, string userProfile)
    {
        string codexHome = GetBootstrapCodexHome(userProfile);
        return InvokePowerShellScript(
            GetBootstrapCodexScriptPath(repoRoot, "install-computer-use-win.ps1"),
            repoRoot,
            startInfo =>
            {
                AddProcessArguments(startInfo, "-Mode", mode, "-PayloadArchivePath", payloadArchivePath, "-DescriptorPath", descriptorPath, "-Json");
                startInfo.Environment["CODEX_HOME"] = codexHome;
                startInfo.Environment["USERPROFILE"] = userProfile;
                startInfo.Environment["LOCALAPPDATA"] = Path.Combine(userProfile, "AppData", "Local");
            });
    }

    private static void AssertBootstrapInstallerSucceeded(ScriptInvocationResult result, string mode) =>
        Assert.True(result.ExitCode == 0, $"Bootstrap {mode} install failed. stderr='{result.Stderr}', stdout='{result.Stdout}'.");

    private static HashSet<string> ReadBootstrapArchiveEntryPaths(string archivePath)
    {
        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        return archive.Entries.Select(static entry => NormalizeArchiveEntryPath(entry.FullName)).ToHashSet(StringComparer.Ordinal);
    }

    private static string GetBootstrapCodexScriptPath(string repoRoot, string scriptName) =>
        Path.Combine(repoRoot, "scripts", "codex", scriptName);

    private static string CreateBootstrapTestOutputRoot(string repoRoot, string scenarioName) =>
        CreateBootstrapTemporaryRoot(repoRoot, scenarioName);

    private static string CreateBootstrapTestUserProfileRoot(string repoRoot, string scenarioName) =>
        CreateBootstrapTemporaryRoot(repoRoot, $"user-profile-{scenarioName}");

    private static string CreateBootstrapTemporaryRoot(string repoRoot, string scenarioName) =>
        Path.Combine(repoRoot, ".tmp", ".codex", "tests", scenarioName, Guid.NewGuid().ToString("N"));

    private static string GetBootstrapCodexHome(string userProfile) =>
        Path.Combine(userProfile, ".codex");

    private static void AddProcessArguments(ProcessStartInfo startInfo, params string[] arguments)
    {
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
    }

    private static void DeleteBootstrapDirectoriesIfExists(params string[] directoryPaths)
    {
        foreach (string directoryPath in directoryPaths)
        {
            DeleteDirectoryIfExists(directoryPath);
        }
    }

    private static void AssertSetupAppLaunches(string executablePath, string workingDirectory, string scenarioName)
    {
        using Process process = new();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
        };

        Assert.True(process.Start(), $"Failed to start packaged setup app for scenario '{scenarioName}'.");
        try
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < SetupAppLaunchTimeout)
            {
                process.Refresh();
                if (process.HasExited)
                {
                    break;
                }

                if (process.MainWindowHandle != IntPtr.Zero
                    && string.Equals(process.MainWindowTitle, SetupAppWindowTitle, StringComparison.Ordinal))
                {
                    return;
                }

                Thread.Sleep(SetupAppLaunchPollInterval);
            }

            process.Refresh();
            throw new Xunit.Sdk.XunitException(
                $"Packaged setup app did not open a visible window for scenario '{scenarioName}'. " +
                $"hasExited={process.HasExited}, mainWindowTitle='{process.MainWindowTitle}', mainWindowHandle={process.MainWindowHandle}.");
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
    }
}
