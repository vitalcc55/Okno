// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;

namespace WinBridge.InstallSurface.AcceptanceTests;

public sealed partial class ComputerUseWinInstallSurfaceTests
{
    private const string BootstrapInstallerTestVersion = "0.2.3";
    private const string RuntimeIdentifier = "win-x64";
    private const string SetupAppExecutableName = "Okno Setup.exe";
    private const string SetupAppWindowTitle = "Okno Setup";
    private static readonly TimeSpan SetupAppLaunchTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SetupAppLaunchPollInterval = TimeSpan.FromMilliseconds(100);
    private static readonly Lazy<BootstrapInstallerArtifacts> SharedBootstrapInstallerArtifacts = new(CreateBootstrapInstallerArtifacts);

    [Fact]
    public void PackageComputerUseWinSetupCliPayloadProducesVersionedArchive()
    {
        BootstrapInstallerArtifacts artifacts = SharedBootstrapInstallerArtifacts.Value;
        string checksumPath = Path.Combine(artifacts.Root, $"okno-setup-cli-payload-{BootstrapInstallerTestVersion}-SHA256SUMS.txt");
        Assert.True(File.Exists(artifacts.SetupCliPayloadArchivePath));
        Assert.True(File.Exists(checksumPath));

        HashSet<string> entries = ReadBootstrapArchiveEntryPaths(artifacts.SetupCliPayloadArchivePath);
        Assert.Contains("WinBridge.Setup.Cli.exe", entries, StringComparer.Ordinal);
        Assert.Contains("WinBridge.Setup.Cli.dll", entries, StringComparer.Ordinal);
        Assert.Contains("runtime-release.json", entries, StringComparer.Ordinal);
        AssertBootstrapArchiveRuntimeDescriptorMatches(artifacts.SetupCliPayloadArchivePath, artifacts.RuntimePackage.DescriptorPath);
    }

    [Fact]
    public void PackageOknoSetupAppReleaseProducesArchiveWithOknoSetupExecutable()
    {
        BootstrapInstallerArtifacts artifacts = SharedBootstrapInstallerArtifacts.Value;
        Assert.True(File.Exists(artifacts.SetupAppReleaseArchivePath));

        HashSet<string> entries = ReadBootstrapArchiveEntryPaths(artifacts.SetupAppReleaseArchivePath);
        Assert.Contains(SetupAppExecutableName, entries, StringComparer.Ordinal);
        Assert.Contains("WinBridge.Setup.App.dll", entries, StringComparer.Ordinal);
        Assert.Contains("runtime-release.json", entries, StringComparer.Ordinal);
        Assert.DoesNotContain("WinBridge.Setup.App.exe", entries, StringComparer.Ordinal);
        AssertBootstrapArchiveRuntimeDescriptorMatches(artifacts.SetupAppReleaseArchivePath, artifacts.RuntimePackage.DescriptorPath);
    }

    [Fact]
    public void PackagedOknoSetupAppLaunchesFromOwnAndExternalWorkingDirectories()
    {
        string repoRoot = GetRepositoryRoot();
        string outputRoot = CreateBootstrapTestOutputRoot(repoRoot, "setup-app-launch");
        string extractRoot = Path.Combine(outputRoot, "extract");

        try
        {
            BootstrapInstallerArtifacts artifacts = SharedBootstrapInstallerArtifacts.Value;
            ZipFile.ExtractToDirectory(artifacts.SetupAppReleaseArchivePath, extractRoot);

            string executablePath = Path.Combine(extractRoot, SetupAppExecutableName);
            Assert.True(File.Exists(executablePath), "Packaged setup executable is missing.");

            AssertSetupAppLaunches(executablePath, extractRoot, "own package directory");
            AssertSetupAppLaunches(executablePath, repoRoot, "external working directory");
        }
        finally { DeleteDirectoryIfExists(outputRoot); }
    }

    [Fact]
    public void SetupAppSourceManifestPinsAsInvokerExecutionLevel()
    {
        string manifestPath = Path.Combine(GetRepositoryRoot(), "src", "WinBridge.Setup.App", "app.manifest");
        Assert.True(File.Exists(manifestPath), $"Setup app manifest is missing: {manifestPath}");

        XDocument manifest = XDocument.Load(manifestPath);
        XNamespace asmV3 = "urn:schemas-microsoft-com:asm.v3";
        XElement requestedExecutionLevel = Assert.Single(
            manifest
                .Descendants(asmV3 + "requestedExecutionLevel"));

        Assert.Equal("asInvoker", requestedExecutionLevel.Attribute("level")?.Value);
        Assert.Equal("false", requestedExecutionLevel.Attribute("uiAccess")?.Value);
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
            BootstrapInstallerArtifacts artifacts = SharedBootstrapInstallerArtifacts.Value;
            ScriptInvocationResult result = InvokeBootstrapInstaller(repoRoot, "runtime-only", artifacts.SetupCliPayloadArchivePath, artifacts.RuntimePackage.DescriptorPath, userProfile);

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
            BootstrapInstallerArtifacts artifacts = SharedBootstrapInstallerArtifacts.Value;
            ScriptInvocationResult result = InvokeBootstrapInstaller(repoRoot, "codex", artifacts.SetupCliPayloadArchivePath, artifacts.RuntimePackage.DescriptorPath, userProfile);

            AssertBootstrapInstallerSucceeded(result, "codex");
            Assert.True(Directory.Exists(GetExpectedInstalledPluginRoot(codexHome)));
            Assert.True(File.Exists(GetExpectedPersonalMarketplacePath(userProfile)));
            Assert.True(File.Exists(GetExpectedCodexReceiptPath(codexHome)));
        }
        finally { DeleteBootstrapDirectoriesIfExists(outputRoot, codexHome, userProfile); }
    }

    [Fact]
    public void BootstrapInstallerRejectsTamperedLocalPayloadArchiveWithoutUnsafeBypass()
    {
        string repoRoot = GetRepositoryRoot();
        string outputRoot = CreateBootstrapTestOutputRoot(repoRoot, "bootstrap-tampered-payload");
        string userProfile = CreateBootstrapTestUserProfileRoot(repoRoot, "bootstrap-tampered-payload");

        try
        {
            BootstrapInstallerArtifacts artifacts = SharedBootstrapInstallerArtifacts.Value;
            string payloadArchivePath = Path.Combine(outputRoot, Path.GetFileName(artifacts.SetupCliPayloadArchivePath));
            Directory.CreateDirectory(outputRoot);
            File.Copy(artifacts.SetupCliPayloadArchivePath, payloadArchivePath, overwrite: true);
            using FileStream stream = new(payloadArchivePath, FileMode.Append, FileAccess.Write, FileShare.None);
            byte[] tamperBytes = [0x13, 0x37, 0x42];
            stream.Write(tamperBytes, 0, tamperBytes.Length);

            ScriptInvocationResult result = InvokeBootstrapInstaller(repoRoot, "runtime-only", payloadArchivePath, artifacts.RuntimePackage.DescriptorPath, userProfile);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("SHA256", result.Stderr, StringComparison.OrdinalIgnoreCase);
        }
        finally { DeleteBootstrapDirectoriesIfExists(outputRoot, GetBootstrapCodexHome(userProfile), userProfile); }
    }

    [Fact]
    public void PackageSetupCliPayloadRejectsRuntimeDescriptorVersionMismatch()
    {
        string repoRoot = GetRepositoryRoot();
        string outputRoot = CreateBootstrapTestOutputRoot(repoRoot, "setup-cli-payload-version-mismatch");

        try
        {
            BootstrapInstallerArtifacts artifacts = SharedBootstrapInstallerArtifacts.Value;
            string mismatchedDescriptorPath = CreateModifiedRuntimeDescriptor(outputRoot, artifacts.RuntimePackage.DescriptorPath, versionOverride: "0.3.0");
            string mismatchedResultPath = CreateModifiedRuntimePackagingResult(outputRoot, artifacts.RuntimePackage.ResultPath, descriptorPathOverride: mismatchedDescriptorPath);

            ScriptInvocationResult result = InvokePowerShellScript(
                GetBootstrapCodexScriptPath(repoRoot, "package-computer-use-win-setup-cli-payload.ps1"),
                repoRoot,
                startInfo => AddProcessArguments(startInfo, "-Version", BootstrapInstallerTestVersion, "-RuntimePackagingResultPath", mismatchedResultPath, "-OutputRoot", outputRoot));

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("version", result.Stderr, StringComparison.OrdinalIgnoreCase);
        }
        finally { DeleteDirectoryIfExists(outputRoot); }
    }

    [Fact]
    public void PackageSetupAppReleaseRejectsRuntimeDescriptorRidMismatch()
    {
        string repoRoot = GetRepositoryRoot();
        string outputRoot = CreateBootstrapTestOutputRoot(repoRoot, "setup-app-release-rid-mismatch");

        try
        {
            BootstrapInstallerArtifacts artifacts = SharedBootstrapInstallerArtifacts.Value;
            string mismatchedDescriptorPath = CreateModifiedRuntimeDescriptor(outputRoot, artifacts.RuntimePackage.DescriptorPath, ridOverride: "win-arm64");
            string mismatchedResultPath = CreateModifiedRuntimePackagingResult(outputRoot, artifacts.RuntimePackage.ResultPath, descriptorPathOverride: mismatchedDescriptorPath);

            ScriptInvocationResult result = InvokePowerShellScript(
                GetBootstrapCodexScriptPath(repoRoot, "package-okno-setup-app-release.ps1"),
                repoRoot,
                startInfo => AddProcessArguments(startInfo, "-Version", BootstrapInstallerTestVersion, "-Rid", RuntimeIdentifier, "-RuntimePackagingResultPath", mismatchedResultPath, "-OutputRoot", outputRoot));

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("RID", result.Stderr, StringComparison.OrdinalIgnoreCase);
        }
        finally { DeleteDirectoryIfExists(outputRoot); }
    }

    [Fact]
    public void PackageSetupCliPayloadRejectsRuntimeDescriptorDownloadUrlMismatchAgainstPackagingResult()
    {
        string repoRoot = GetRepositoryRoot();
        string outputRoot = CreateBootstrapTestOutputRoot(repoRoot, "setup-cli-payload-origin-mismatch");

        try
        {
            BootstrapInstallerArtifacts artifacts = SharedBootstrapInstallerArtifacts.Value;
            string mismatchedDescriptorPath = CreateModifiedRuntimeDescriptor(
                outputRoot,
                artifacts.RuntimePackage.DescriptorPath,
                downloadUrlOverride: "https://example.invalid/other-runtime.zip");
            string mismatchedResultPath = CreateModifiedRuntimePackagingResult(outputRoot, artifacts.RuntimePackage.ResultPath, descriptorPathOverride: mismatchedDescriptorPath);

            ScriptInvocationResult result = InvokePowerShellScript(
                GetBootstrapCodexScriptPath(repoRoot, "package-computer-use-win-setup-cli-payload.ps1"),
                repoRoot,
                startInfo => AddProcessArguments(startInfo, "-Version", BootstrapInstallerTestVersion, "-RuntimePackagingResultPath", mismatchedResultPath, "-OutputRoot", outputRoot));

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("downloadUrl", result.Stderr, StringComparison.OrdinalIgnoreCase);
        }
        finally { DeleteDirectoryIfExists(outputRoot); }
    }

    [Fact]
    public void PackageSetupCliPayloadRejectsSelfConsistentPackagingResultWithTamperedSha256()
    {
        string repoRoot = GetRepositoryRoot();
        string outputRoot = CreateBootstrapTestOutputRoot(repoRoot, "setup-cli-payload-self-consistent-tampered-sha");

        try
        {
            BootstrapInstallerArtifacts artifacts = SharedBootstrapInstallerArtifacts.Value;
            string fakeSha256 = new string('a', 64);
            string tamperedDescriptorPath = CreateModifiedRuntimeDescriptor(
                outputRoot,
                artifacts.RuntimePackage.DescriptorPath,
                sha256Override: fakeSha256);
            string tamperedResultPath = CreateModifiedRuntimePackagingResult(
                outputRoot,
                artifacts.RuntimePackage.ResultPath,
                descriptorPathOverride: tamperedDescriptorPath,
                sha256Override: fakeSha256);

            ScriptInvocationResult result = InvokePowerShellScript(
                GetBootstrapCodexScriptPath(repoRoot, "package-computer-use-win-setup-cli-payload.ps1"),
                repoRoot,
                startInfo => AddProcessArguments(startInfo, "-Version", BootstrapInstallerTestVersion, "-RuntimePackagingResultPath", tamperedResultPath, "-OutputRoot", outputRoot));

            Assert.NotEqual(0, result.ExitCode);
            Assert.True(
                result.Stderr.Contains("Runtime packaging result", StringComparison.OrdinalIgnoreCase)
                || result.Stderr.Contains("proof", StringComparison.OrdinalIgnoreCase)
                || result.Stderr.Contains("sha256", StringComparison.OrdinalIgnoreCase)
                || result.Stderr.Contains("checksum", StringComparison.OrdinalIgnoreCase),
                $"Expected artifact proof failure, got stderr='{result.Stderr}'.");
        }
        finally { DeleteDirectoryIfExists(outputRoot); }
    }

    [Fact]
    public void PackageSetupCliPayloadRejectsMissingRuntimeArchiveInPackagingResult()
    {
        string repoRoot = GetRepositoryRoot();
        string outputRoot = CreateBootstrapTestOutputRoot(repoRoot, "setup-cli-payload-missing-runtime-archive");

        try
        {
            BootstrapInstallerArtifacts artifacts = SharedBootstrapInstallerArtifacts.Value;
            string missingArchivePath = Path.Combine(outputRoot, "missing-runtime.zip");
            string tamperedResultPath = CreateModifiedRuntimePackagingResult(
                outputRoot,
                artifacts.RuntimePackage.ResultPath,
                archivePathOverride: missingArchivePath);

            ScriptInvocationResult result = InvokePowerShellScript(
                GetBootstrapCodexScriptPath(repoRoot, "package-computer-use-win-setup-cli-payload.ps1"),
                repoRoot,
                startInfo => AddProcessArguments(startInfo, "-Version", BootstrapInstallerTestVersion, "-RuntimePackagingResultPath", tamperedResultPath, "-OutputRoot", outputRoot));

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("missing archive", result.Stderr, StringComparison.OrdinalIgnoreCase);
        }
        finally { DeleteDirectoryIfExists(outputRoot); }
    }

    private static string PackageSetupCliPayload(string repoRoot, string outputRoot, RuntimeReleasePackageResult runtimePackage) =>
        PackageSetupCliPayload(repoRoot, GetBootstrapCodexScriptPath(repoRoot, "package-computer-use-win-setup-cli-payload.ps1"), outputRoot, BootstrapInstallerTestVersion, runtimePackage);

    private static string PackageSetupAppRelease(string repoRoot, string outputRoot, RuntimeReleasePackageResult runtimePackage) =>
        PackageSetupAppRelease(repoRoot, GetBootstrapCodexScriptPath(repoRoot, "package-okno-setup-app-release.ps1"), outputRoot, BootstrapInstallerTestVersion, runtimePackage);

    private static string PackageSetupCliPayload(string repoRoot, string packageScriptPath, string outputRoot, string version, RuntimeReleasePackageResult runtimePackage) =>
        PackageBootstrapVersionedArchive(repoRoot, packageScriptPath, outputRoot, version, runtimePackage, "Setup CLI payload packaging script");

    private static string PackageSetupAppRelease(string repoRoot, string packageScriptPath, string outputRoot, string version, RuntimeReleasePackageResult runtimePackage) =>
        PackageBootstrapVersionedArchive(repoRoot, packageScriptPath, outputRoot, version, runtimePackage, "Setup app release packaging script");

    private static string PackageBootstrapVersionedArchive(string repoRoot, string packageScriptPath, string outputRoot, string version, RuntimeReleasePackageResult runtimePackage, string scriptDisplayName)
    {
        ScriptInvocationResult result = InvokePowerShellScript(
            packageScriptPath,
            repoRoot,
            startInfo => AddProcessArguments(startInfo, "-Version", version, "-RuntimePackagingResultPath", runtimePackage.ResultPath, "-OutputRoot", outputRoot));

        if (result.ExitCode != 0)
        {
            throw new Xunit.Sdk.XunitException($"{scriptDisplayName} failed. stderr='{result.Stderr}', stdout='{result.Stdout}'.");
        }

        using JsonDocument payload = ParseJsonStdoutOrThrow(result, scriptDisplayName);
        return payload.RootElement.GetProperty("archivePath").GetString()
            ?? throw new InvalidOperationException("archivePath missing.");
    }

    private static BootstrapInstallerArtifacts CreateBootstrapInstallerArtifacts()
    {
        string repoRoot = GetRepositoryRoot();
        string outputRoot = CreateBootstrapTemporaryRoot(repoRoot, "shared-bootstrap-installer-artifacts");
        string runtimeRoot = Path.Combine(repoRoot, "plugins", "computer-use-win", "runtime", RuntimeIdentifier);
        EnsurePublishedRuntimeBundle(repoRoot, GetPublishScriptPath(repoRoot), runtimeRoot);

        RuntimeReleasePackageResult runtimePackage = PackageRuntimeRelease(
            repoRoot,
            GetBootstrapCodexScriptPath(repoRoot, "package-computer-use-win-runtime-release.ps1"),
            runtimeRoot,
            outputRoot,
            BootstrapInstallerTestVersion);
        PackageLocalPluginRelease(repoRoot, outputRoot, runtimePackage);
        string setupCliPayloadArchivePath = PackageSetupCliPayload(repoRoot, outputRoot, runtimePackage);
        string setupAppReleaseArchivePath = PackageSetupAppRelease(repoRoot, outputRoot, runtimePackage);
        RegisterProcessExitCleanup(outputRoot);

        return new BootstrapInstallerArtifacts(outputRoot, runtimePackage, setupCliPayloadArchivePath, setupAppReleaseArchivePath);
    }

    private static void PackageLocalPluginRelease(string repoRoot, string outputRoot, RuntimeReleasePackageResult runtimePackage) =>
        PackagePluginRelease(repoRoot, GetBootstrapCodexScriptPath(repoRoot, "package-computer-use-win-plugin-release.ps1"), outputRoot, BootstrapInstallerTestVersion, runtimePackage.ResultPath);

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

    private static void AssertBootstrapArchiveRuntimeDescriptorMatches(string archivePath, string runtimeDescriptorPath)
    {
        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        using JsonDocument actual = ReadBootstrapArchiveJsonEntry(archive, "runtime-release.json");
        using JsonDocument expected = JsonDocument.Parse(File.ReadAllText(runtimeDescriptorPath));
        Assert.True(JsonElement.DeepEquals(expected.RootElement, actual.RootElement), "Packaged runtime-release.json does not match the provided runtime descriptor.");
    }

    private static JsonDocument ReadBootstrapArchiveJsonEntry(ZipArchive archive, string entryPath)
    {
        ZipArchiveEntry entry = Assert.Single(archive.Entries.Where(
            archiveEntry => string.Equals(NormalizeArchiveEntryPath(archiveEntry.FullName), entryPath, StringComparison.Ordinal)));
        using Stream stream = entry.Open();
        return JsonDocument.Parse(stream);
    }

    private static string CreateModifiedRuntimeDescriptor(
        string outputRoot,
        string originalDescriptorPath,
        string? versionOverride = null,
        string? ridOverride = null,
        string? assetNameOverride = null,
        string? downloadUrlOverride = null,
        string? sha256Override = null)
    {
        using JsonDocument original = JsonDocument.Parse(File.ReadAllText(originalDescriptorPath));
        JsonElement root = original.RootElement;
        string version = versionOverride ?? root.GetProperty("version").GetString() ?? throw new InvalidOperationException("version missing.");
        string rid = ridOverride ?? root.GetProperty("rid").GetString() ?? throw new InvalidOperationException("rid missing.");
        string assetName = assetNameOverride ?? root.GetProperty("assetName").GetString() ?? throw new InvalidOperationException("assetName missing.");

        Directory.CreateDirectory(outputRoot);
        string descriptorPath = Path.Combine(outputRoot, Guid.NewGuid().ToString("N") + ".runtime-release.json");
        File.WriteAllText(
            descriptorPath,
            JsonSerializer.Serialize(new
            {
                formatVersion = root.GetProperty("formatVersion").GetInt32(),
                version,
                rid,
                tag = root.GetProperty("tag").GetString(),
                assetName,
                downloadUrl = downloadUrlOverride ?? root.GetProperty("downloadUrl").GetString(),
                sha256 = sha256Override ?? root.GetProperty("sha256").GetString(),
                serverExeRelativePath = root.GetProperty("serverExeRelativePath").GetString(),
                bundleManifestName = root.GetProperty("bundleManifestName").GetString(),
            }));
        return descriptorPath;
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

    private sealed record BootstrapInstallerArtifacts(
        string Root,
        RuntimeReleasePackageResult RuntimePackage,
        string SetupCliPayloadArchivePath,
        string SetupAppReleaseArchivePath);
}

