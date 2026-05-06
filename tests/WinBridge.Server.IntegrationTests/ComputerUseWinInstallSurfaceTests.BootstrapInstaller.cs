// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.IO.Compression;
using System.Text.Json;

namespace WinBridge.Server.IntegrationTests;

public sealed partial class ComputerUseWinInstallSurfaceTests
{
    [Fact]
    public void PackageComputerUseWinSetupCliPayloadProducesVersionedArchive()
    {
        string repoRoot = GetRepositoryRoot();
        string packageScriptPath = Path.Combine(repoRoot, "scripts", "codex", "package-computer-use-win-setup-cli-payload.ps1");
        string outputRoot = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "setup-cli-payload", Guid.NewGuid().ToString("N"));
        const string version = "0.1.0";

        try
        {
            string archivePath = PackageSetupCliPayload(repoRoot, packageScriptPath, outputRoot, version);
            string checksumPath = Path.Combine(outputRoot, $"okno-setup-cli-payload-{version}-SHA256SUMS.txt");
            Assert.True(File.Exists(archivePath));
            Assert.True(File.Exists(checksumPath));

            using ZipArchive archive = ZipFile.OpenRead(archivePath);
            string[] entries = archive.Entries.Select(static entry => NormalizeArchiveEntryPath(entry.FullName)).ToArray();
            Assert.Contains("WinBridge.Setup.Cli.exe", entries, StringComparer.Ordinal);
            Assert.Contains("WinBridge.Setup.Cli.dll", entries, StringComparer.Ordinal);
            Assert.Contains("runtime-release.json", entries, StringComparer.Ordinal);
        }
        finally
        {
            DeleteDirectoryIfExists(outputRoot);
        }
    }

    [Fact]
    public void PackageOknoSetupAppReleaseProducesArchiveWithOknoSetupExecutable()
    {
        string repoRoot = GetRepositoryRoot();
        string packageScriptPath = Path.Combine(repoRoot, "scripts", "codex", "package-okno-setup-app-release.ps1");
        string outputRoot = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "setup-app-release", Guid.NewGuid().ToString("N"));
        const string version = "0.1.0";

        try
        {
            string archivePath = PackageSetupAppRelease(repoRoot, packageScriptPath, outputRoot, version);
            Assert.True(File.Exists(archivePath));

            using ZipArchive archive = ZipFile.OpenRead(archivePath);
            string[] entries = archive.Entries.Select(static entry => NormalizeArchiveEntryPath(entry.FullName)).ToArray();
            Assert.Contains("Okno Setup.exe", entries, StringComparer.Ordinal);
            Assert.Contains("WinBridge.Setup.App.dll", entries, StringComparer.Ordinal);
            Assert.Contains("runtime-release.json", entries, StringComparer.Ordinal);
            Assert.DoesNotContain("WinBridge.Setup.App.exe", entries, StringComparer.Ordinal);
        }
        finally
        {
            DeleteDirectoryIfExists(outputRoot);
        }
    }

    [Fact]
    public void BootstrapInstallerInstallsRuntimeOnlyFromLocalPayloadArchive()
    {
        string repoRoot = GetRepositoryRoot();
        string publishScriptPath = GetPublishScriptPath(repoRoot);
        string runtimePackageScriptPath = Path.Combine(repoRoot, "scripts", "codex", "package-computer-use-win-runtime-release.ps1");
        string payloadPackageScriptPath = Path.Combine(repoRoot, "scripts", "codex", "package-computer-use-win-setup-cli-payload.ps1");
        string bootstrapScriptPath = Path.Combine(repoRoot, "scripts", "codex", "install-computer-use-win.ps1");
        string sourcePluginRoot = Path.Combine(repoRoot, "plugins", "computer-use-win");
        string runtimeRoot = Path.Combine(sourcePluginRoot, "runtime", "win-x64");
        string outputRoot = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "bootstrap-runtime-only", Guid.NewGuid().ToString("N"));
        string userProfile = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "user-profile-bootstrap-runtime-only", Guid.NewGuid().ToString("N"));
        string codexHome = Path.Combine(userProfile, ".codex");
        const string version = "0.1.0";

        EnsurePublishedRuntimeBundle(repoRoot, publishScriptPath, runtimeRoot);

        try
        {
            string runtimeArchivePath = PackageRuntimeRelease(repoRoot, runtimePackageScriptPath, runtimeRoot, outputRoot, version);
            string descriptorPath = CreateRuntimeReleaseDescriptor(outputRoot, version, runtimeArchivePath, "win-x64");
            string payloadArchivePath = PackageSetupCliPayload(repoRoot, payloadPackageScriptPath, outputRoot, version);

            ScriptInvocationResult result = InvokePowerShellScript(
                bootstrapScriptPath,
                repoRoot,
                startInfo =>
                {
                    startInfo.ArgumentList.Add("-Mode");
                    startInfo.ArgumentList.Add("runtime-only");
                    startInfo.ArgumentList.Add("-PayloadArchivePath");
                    startInfo.ArgumentList.Add(payloadArchivePath);
                    startInfo.ArgumentList.Add("-DescriptorPath");
                    startInfo.ArgumentList.Add(descriptorPath);
                    startInfo.ArgumentList.Add("-Json");
                    startInfo.Environment["CODEX_HOME"] = codexHome;
                    startInfo.Environment["USERPROFILE"] = userProfile;
                });

            Assert.True(result.ExitCode == 0, $"Bootstrap runtime-only install failed. stderr='{result.Stderr}', stdout='{result.Stdout}'.");
            Assert.True(File.Exists(GetExpectedRuntimeOnlyReceiptPath(codexHome)));
            Assert.False(File.Exists(GetExpectedPersonalMarketplacePath(userProfile)));
        }
        finally
        {
            DeleteDirectoryIfExists(outputRoot);
            DeleteDirectoryIfExists(codexHome);
            DeleteDirectoryIfExists(userProfile);
        }
    }

    [Fact]
    public void BootstrapInstallerInstallsCodexFromLocalPayloadArchive()
    {
        string repoRoot = GetRepositoryRoot();
        string publishScriptPath = GetPublishScriptPath(repoRoot);
        string runtimePackageScriptPath = Path.Combine(repoRoot, "scripts", "codex", "package-computer-use-win-runtime-release.ps1");
        string pluginPackageScriptPath = Path.Combine(repoRoot, "scripts", "codex", "package-computer-use-win-plugin-release.ps1");
        string payloadPackageScriptPath = Path.Combine(repoRoot, "scripts", "codex", "package-computer-use-win-setup-cli-payload.ps1");
        string bootstrapScriptPath = Path.Combine(repoRoot, "scripts", "codex", "install-computer-use-win.ps1");
        string sourcePluginRoot = Path.Combine(repoRoot, "plugins", "computer-use-win");
        string runtimeRoot = Path.Combine(sourcePluginRoot, "runtime", "win-x64");
        string outputRoot = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "bootstrap-codex", Guid.NewGuid().ToString("N"));
        string userProfile = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "user-profile-bootstrap-codex", Guid.NewGuid().ToString("N"));
        string codexHome = Path.Combine(userProfile, ".codex");
        const string version = "0.1.0";

        EnsurePublishedRuntimeBundle(repoRoot, publishScriptPath, runtimeRoot);

        try
        {
            string runtimeArchivePath = PackageRuntimeRelease(repoRoot, runtimePackageScriptPath, runtimeRoot, outputRoot, version);
            string descriptorPath = CreateRuntimeReleaseDescriptor(outputRoot, version, runtimeArchivePath, "win-x64");
            PackagePluginRelease(repoRoot, pluginPackageScriptPath, outputRoot, version);
            string payloadArchivePath = PackageSetupCliPayload(repoRoot, payloadPackageScriptPath, outputRoot, version);

            ScriptInvocationResult result = InvokePowerShellScript(
                bootstrapScriptPath,
                repoRoot,
                startInfo =>
                {
                    startInfo.ArgumentList.Add("-Mode");
                    startInfo.ArgumentList.Add("codex");
                    startInfo.ArgumentList.Add("-PayloadArchivePath");
                    startInfo.ArgumentList.Add(payloadArchivePath);
                    startInfo.ArgumentList.Add("-DescriptorPath");
                    startInfo.ArgumentList.Add(descriptorPath);
                    startInfo.ArgumentList.Add("-Json");
                    startInfo.Environment["CODEX_HOME"] = codexHome;
                    startInfo.Environment["USERPROFILE"] = userProfile;
                });

            Assert.True(result.ExitCode == 0, $"Bootstrap codex install failed. stderr='{result.Stderr}', stdout='{result.Stdout}'.");
            Assert.True(Directory.Exists(GetExpectedInstalledPluginRoot(codexHome)));
            Assert.True(File.Exists(GetExpectedPersonalMarketplacePath(userProfile)));
            Assert.True(File.Exists(GetExpectedCodexReceiptPath(codexHome)));
        }
        finally
        {
            DeleteDirectoryIfExists(outputRoot);
            DeleteDirectoryIfExists(codexHome);
            DeleteDirectoryIfExists(userProfile);
        }
    }

    private static string PackageSetupCliPayload(string repoRoot, string packageScriptPath, string outputRoot, string version)
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
            throw new Xunit.Sdk.XunitException($"Setup CLI payload packaging script failed. stderr='{result.Stderr}', stdout='{result.Stdout}'.");
        }

        using JsonDocument payload = JsonDocument.Parse(result.Stdout);
        return payload.RootElement.GetProperty("archivePath").GetString()
            ?? throw new InvalidOperationException("archivePath missing.");
    }

    private static string PackageSetupAppRelease(string repoRoot, string packageScriptPath, string outputRoot, string version)
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
            throw new Xunit.Sdk.XunitException($"Setup app release packaging script failed. stderr='{result.Stderr}', stdout='{result.Stdout}'.");
        }

        using JsonDocument payload = JsonDocument.Parse(result.Stdout);
        return payload.RootElement.GetProperty("archivePath").GetString()
            ?? throw new InvalidOperationException("archivePath missing.");
    }
}
