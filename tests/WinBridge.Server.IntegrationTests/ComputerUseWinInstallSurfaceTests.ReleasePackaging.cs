// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.IO.Compression;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using WinBridge.Runtime.Tooling;

namespace WinBridge.Server.IntegrationTests;

public sealed partial class ComputerUseWinInstallSurfaceTests
{
    [Fact]
    public void PackageComputerUseWinRuntimeReleaseCreatesVersionedZipAndChecksumWithoutMutatingRuntimeBundle()
    {
        string repoRoot = GetRepositoryRoot();
        string publishScriptPath = GetPublishScriptPath(repoRoot);
        string packageScriptPath = Path.Combine(repoRoot, "scripts", "codex", "package-computer-use-win-runtime-release.ps1");
        string runtimeRoot = Path.Combine(repoRoot, "plugins", "computer-use-win", "runtime", "win-x64");
        string outputRoot = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "computer-use-win-release-package", Guid.NewGuid().ToString("N"));
        const string version = "0.1.0-test";

        EnsurePublishedRuntimeBundle(repoRoot, publishScriptPath, runtimeRoot);
        string runtimeDigestBefore = ComputeDirectoryDigest(runtimeRoot);

        try
        {
            ScriptInvocationResult result = InvokePowerShellScript(
                packageScriptPath,
                repoRoot,
                startInfo =>
                {
                    startInfo.ArgumentList.Add("-Version");
                    startInfo.ArgumentList.Add(version);
                    startInfo.ArgumentList.Add("-Rid");
                    startInfo.ArgumentList.Add("win-x64");
                    startInfo.ArgumentList.Add("-PublishSourceRoot");
                    startInfo.ArgumentList.Add(runtimeRoot);
                    startInfo.ArgumentList.Add("-OutputRoot");
                    startInfo.ArgumentList.Add(outputRoot);
                });

            Assert.True(
                result.ExitCode == 0,
                $"Release packaging script failed. ExitCode={result.ExitCode}. stderr='{result.Stderr.Trim()}', stdout='{result.Stdout.Trim()}'.");
            using JsonDocument payload = JsonDocument.Parse(result.Stdout);

            string archivePath = payload.RootElement.GetProperty("archivePath").GetString()
                ?? throw new InvalidOperationException("archivePath missing.");
            string checksumPath = payload.RootElement.GetProperty("checksumPath").GetString()
                ?? throw new InvalidOperationException("checksumPath missing.");
            string assetName = payload.RootElement.GetProperty("assetName").GetString()
                ?? throw new InvalidOperationException("assetName missing.");

            Assert.Equal($"okno-computer-use-win-runtime-{version}-win-x64.zip", assetName);
            Assert.True(File.Exists(archivePath));
            Assert.True(File.Exists(checksumPath));
            Assert.Equal(runtimeDigestBefore, ComputeDirectoryDigest(runtimeRoot));
        }
        finally
        {
            DeleteDirectoryIfExists(outputRoot);
        }
    }

    [Fact]
    public void PackageComputerUseWinRuntimeReleasePreservesRuntimeManifestInsideArchive()
    {
        string repoRoot = GetRepositoryRoot();
        string publishScriptPath = GetPublishScriptPath(repoRoot);
        string packageScriptPath = Path.Combine(repoRoot, "scripts", "codex", "package-computer-use-win-runtime-release.ps1");
        string runtimeRoot = Path.Combine(repoRoot, "plugins", "computer-use-win", "runtime", "win-x64");
        string outputRoot = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "computer-use-win-release-package-archive", Guid.NewGuid().ToString("N"));
        const string version = "0.1.0-test";

        EnsurePublishedRuntimeBundle(repoRoot, publishScriptPath, runtimeRoot);

        try
        {
            ScriptInvocationResult result = InvokePowerShellScript(
                packageScriptPath,
                repoRoot,
                startInfo =>
                {
                    startInfo.ArgumentList.Add("-Version");
                    startInfo.ArgumentList.Add(version);
                    startInfo.ArgumentList.Add("-Rid");
                    startInfo.ArgumentList.Add("win-x64");
                    startInfo.ArgumentList.Add("-PublishSourceRoot");
                    startInfo.ArgumentList.Add(runtimeRoot);
                    startInfo.ArgumentList.Add("-OutputRoot");
                    startInfo.ArgumentList.Add(outputRoot);
                });

            Assert.True(result.ExitCode == 0, $"Release packaging script failed: {result.Stderr}");
            using JsonDocument payload = JsonDocument.Parse(result.Stdout);

            string archivePath = payload.RootElement.GetProperty("archivePath").GetString()
                ?? throw new InvalidOperationException("archivePath missing.");
            using ZipArchive archive = ZipFile.OpenRead(archivePath);
            Assert.Contains(archive.Entries, entry => string.Equals(entry.FullName, "Okno.Server.exe", StringComparison.Ordinal));
            Assert.Contains(archive.Entries, entry => string.Equals(entry.FullName, "okno-runtime-bundle-manifest.json", StringComparison.Ordinal));
            Assert.Contains(archive.Entries, entry => string.Equals(entry.FullName, "WinBridge.Runtime.Windows.UIA.Worker.exe", StringComparison.Ordinal));
        }
        finally
        {
            DeleteDirectoryIfExists(outputRoot);
        }
    }

    [Fact]
    public async Task ComputerUseWinLauncherBootstrapsRuntimeFromPinnedReleaseDescriptorWhenRuntimeBundleIsMissing()
    {
        string repoRoot = GetRepositoryRoot();
        string publishScriptPath = GetPublishScriptPath(repoRoot);
        string packageScriptPath = Path.Combine(repoRoot, "scripts", "codex", "package-computer-use-win-runtime-release.ps1");
        string sourcePluginRoot = Path.Combine(repoRoot, "plugins", "computer-use-win");
        string runtimeRoot = Path.Combine(sourcePluginRoot, "runtime", "win-x64");
        string outputRoot = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "computer-use-win-release-bootstrap", Guid.NewGuid().ToString("N"));
        string tempPluginRoot = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "computer-use-win-release-backed-plugin", Guid.NewGuid().ToString("N"));
        const string version = "0.1.0-test";

        EnsurePublishedRuntimeBundle(repoRoot, publishScriptPath, runtimeRoot);

        try
        {
            string archivePath = PackageRuntimeRelease(repoRoot, packageScriptPath, runtimeRoot, outputRoot, version);
            string descriptorPath = CreateRuntimeReleaseDescriptor(outputRoot, version, archivePath, "win-x64");

            CopyDirectory(sourcePluginRoot, tempPluginRoot, IncludeStablePluginPath);
            DeleteDirectoryIfExists(Path.Combine(tempPluginRoot, "runtime", "win-x64"));

            await using PluginLauncherSession launcher = StartPluginLauncherSession(tempPluginRoot, descriptorPath);
            PluginMcpSession session = launcher.CreateMcpSession();

            try
            {
                using JsonDocument initializeResponse = await session.SendRequestAsync(
                    "initialize",
                    new
                    {
                        protocolVersion = "2025-11-25",
                        capabilities = new { },
                        clientInfo = new
                        {
                            name = "ComputerUseWin.ReleasePackagingTests",
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
                    .OrderBy(static value => value, StringComparer.Ordinal)
                    .ToArray();

                Assert.Contains(ToolNames.ComputerUseWinListApps, toolNames);
                Assert.Contains(ToolNames.ComputerUseWinGetAppState, toolNames);
                Assert.Contains(ToolNames.ComputerUseWinClick, toolNames);
                Assert.True(File.Exists(Path.Combine(tempPluginRoot, "runtime", "win-x64", "Okno.Server.exe")));
                Assert.True(File.Exists(Path.Combine(tempPluginRoot, "runtime", "win-x64", "okno-runtime-bundle-manifest.json")));
            }
            finally
            {
            }
        }
        finally
        {
            DeleteDirectoryIfExists(outputRoot);
            DeleteDirectoryIfExists(tempPluginRoot);
        }
    }

    [Fact]
    public void ComputerUseWinLauncherFailsClosedWhenPinnedReleaseChecksumDoesNotMatch()
    {
        string repoRoot = GetRepositoryRoot();
        string publishScriptPath = GetPublishScriptPath(repoRoot);
        string packageScriptPath = Path.Combine(repoRoot, "scripts", "codex", "package-computer-use-win-runtime-release.ps1");
        string sourcePluginRoot = Path.Combine(repoRoot, "plugins", "computer-use-win");
        string runtimeRoot = Path.Combine(sourcePluginRoot, "runtime", "win-x64");
        string outputRoot = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "computer-use-win-release-bootstrap-fail", Guid.NewGuid().ToString("N"));
        string tempPluginRoot = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "computer-use-win-release-backed-plugin-fail", Guid.NewGuid().ToString("N"));
        const string version = "0.1.0-test";

        EnsurePublishedRuntimeBundle(repoRoot, publishScriptPath, runtimeRoot);

        try
        {
            string archivePath = PackageRuntimeRelease(repoRoot, packageScriptPath, runtimeRoot, outputRoot, version);
            string descriptorPath = CreateRuntimeReleaseDescriptor(
                outputRoot,
                version,
                archivePath,
                "win-x64",
                sha256Override: new string('0', 64));

            CopyDirectory(sourcePluginRoot, tempPluginRoot, IncludeStablePluginPath);
            DeleteDirectoryIfExists(Path.Combine(tempPluginRoot, "runtime", "win-x64"));

            ScriptInvocationResult result = InvokePluginLauncher(tempPluginRoot, descriptorPath);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("sha256", result.Stderr, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectoryIfExists(outputRoot);
            DeleteDirectoryIfExists(tempPluginRoot);
        }
    }

    [Fact]
    public void ComputerUseWinRuntimeReleaseDescriptorMatchesPinnedContractShape()
    {
        string repoRoot = GetRepositoryRoot();
        string descriptorPath = Path.Combine(repoRoot, "plugins", "computer-use-win", "runtime-release.json");
        using JsonDocument descriptor = JsonDocument.Parse(File.ReadAllText(descriptorPath));

        Assert.Equal(1, descriptor.RootElement.GetProperty("formatVersion").GetInt32());
        Assert.Equal("0.1.0", descriptor.RootElement.GetProperty("version").GetString());
        Assert.Equal("win-x64", descriptor.RootElement.GetProperty("rid").GetString());
        Assert.Equal("v0.1.0", descriptor.RootElement.GetProperty("tag").GetString());
        Assert.Equal("okno-computer-use-win-runtime-0.1.0-win-x64.zip", descriptor.RootElement.GetProperty("assetName").GetString());
        Assert.Contains("/releases/download/v0.1.0/", descriptor.RootElement.GetProperty("downloadUrl").GetString(), StringComparison.Ordinal);
        string sha256 = descriptor.RootElement.GetProperty("sha256").GetString()
            ?? throw new InvalidOperationException("sha256 missing.");
        Assert.Matches("^[0-9a-f]{64}$", sha256);
        Assert.Equal("Okno.Server.exe", descriptor.RootElement.GetProperty("serverExeRelativePath").GetString());
        Assert.Equal("okno-runtime-bundle-manifest.json", descriptor.RootElement.GetProperty("bundleManifestName").GetString());
    }

    [Fact]
    public void ComputerUseWinInstallRunbookSeparatesCodexGenericAndDeveloperPaths()
    {
        string repoRoot = GetRepositoryRoot();
        string runbookPath = Path.Combine(repoRoot, "docs", "runbooks", "computer-use-win-install.md");
        string runbook = File.ReadAllText(runbookPath);

        Assert.Contains("## 1. Codex plugin install", runbook, StringComparison.Ordinal);
        Assert.Contains("## 2. Generic MCP STDIO install", runbook, StringComparison.Ordinal);
        Assert.Contains("## 3. Developer from source", runbook, StringComparison.Ordinal);
        Assert.Contains("Okno.Server.exe", runbook, StringComparison.Ordinal);
        Assert.Contains("publish-computer-use-win-plugin.ps1", runbook, StringComparison.Ordinal);
    }

    [Fact]
    public void CacheInstallProofTracksRuntimeReleaseDescriptorMetadata()
    {
        string repoRoot = GetRepositoryRoot();
        string proofScriptPath = Path.Combine(repoRoot, "scripts", "codex", "prove-computer-use-win-cache-install.ps1");
        string proofScript = File.ReadAllText(proofScriptPath);

        Assert.Contains("runtime-release.json", proofScript, StringComparison.Ordinal);
        Assert.Contains("runtimeReleaseVersion", proofScript, StringComparison.Ordinal);
        Assert.Contains("runtimeReleaseAssetName", proofScript, StringComparison.Ordinal);
        Assert.Contains("runtimeReleaseDownloadUrl", proofScript, StringComparison.Ordinal);
    }

    private static string PackageRuntimeRelease(
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
                startInfo.ArgumentList.Add("win-x64");
                startInfo.ArgumentList.Add("-PublishSourceRoot");
                startInfo.ArgumentList.Add(runtimeRoot);
                startInfo.ArgumentList.Add("-OutputRoot");
                startInfo.ArgumentList.Add(outputRoot);
            });

        if (result.ExitCode != 0)
        {
            throw new Xunit.Sdk.XunitException($"Release packaging script failed. stderr='{result.Stderr}', stdout='{result.Stdout}'.");
        }

        using JsonDocument payload = JsonDocument.Parse(result.Stdout);
        return payload.RootElement.GetProperty("archivePath").GetString()
            ?? throw new InvalidOperationException("archivePath missing.");
    }

    private static string CreateRuntimeReleaseDescriptor(
        string outputRoot,
        string version,
        string archivePath,
        string rid,
        string? sha256Override = null)
    {
        Directory.CreateDirectory(outputRoot);
        string sha256 = sha256Override ?? ComputeFileSha256(archivePath);
        string descriptorPath = Path.Combine(outputRoot, "runtime-release.override.json");
        var descriptor = new
        {
            formatVersion = 1,
            version,
            rid,
            tag = $"v{version}",
            assetName = Path.GetFileName(archivePath),
            downloadUrl = new Uri(archivePath).AbsoluteUri,
            sha256,
            serverExeRelativePath = "Okno.Server.exe",
            bundleManifestName = "okno-runtime-bundle-manifest.json",
        };

        File.WriteAllText(descriptorPath, JsonSerializer.Serialize(descriptor));
        return descriptorPath;
    }

    private static string ComputeDirectoryDigest(string rootPath)
    {
        Dictionary<string, string> entries = Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToDictionary(
                path => Path.GetRelativePath(rootPath, path).Replace('\\', '/'),
                ComputeFileSha256,
                StringComparer.Ordinal);

        string serialized = JsonSerializer.Serialize(entries);
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(serialized);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static string ComputeFileSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static bool IncludeStablePluginPath(string relativePath)
    {
        string normalized = relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        return !normalized.StartsWith($"runtime{Path.DirectorySeparatorChar}win-x64.", StringComparison.OrdinalIgnoreCase);
    }
}
