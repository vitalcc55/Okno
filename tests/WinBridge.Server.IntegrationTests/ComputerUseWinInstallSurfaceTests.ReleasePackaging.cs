// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WinBridge.Runtime.Tooling;

namespace WinBridge.Server.IntegrationTests;

public sealed partial class ComputerUseWinInstallSurfaceTests
{
    private const string ReleasePackagingPluginVersion = "0.1.0";
    private const string ReleasePackagingRuntimeVersion = "0.1.0-test";
    private const string ReleasePackagingRuntimeRid = "win-x64";
    private const string ReleasePackagingRuntimeServerExeName = "Okno.Server.exe";
    private const string ReleasePackagingRuntimeBundleManifestName = "okno-runtime-bundle-manifest.json";
    private const string ReleasePackagingRuntimeWorkerExeName = "WinBridge.Runtime.Windows.UIA.Worker.exe";

    private static readonly Lazy<ReleasePackagingPackage> SharedReleasePackagingPluginPackage = new(CreateReleasePackagingPluginPackage);
    private static readonly Lazy<ReleasePackagingPackage> SharedReleasePackagingRuntimePackage = new(CreateReleasePackagingRuntimePackage);
    private static readonly byte[] ReleasePackagingDirectoryDigestSeparator = [0];

    [Fact]
    public void PackageComputerUseWinPluginReleaseCreatesVersionedZipAndChecksumWithoutMutatingPluginSource()
    {
        ReleasePackagingPackage package = SharedReleasePackagingPluginPackage.Value;

        Assert.Equal($"okno-computer-use-win-plugin-{ReleasePackagingPluginVersion}.zip", package.AssetName);
        AssertReleasePackagingFilesExist(package.ArchivePath, package.ChecksumPath);
        Assert.Equal(package.SourceDigestBefore, package.SourceDigestAfter);
    }

    [Fact]
    public void PackageComputerUseWinPluginReleaseIncludesPluginContractFilesAndExcludesRuntimeDirectory()
    {
        using ZipArchive archive = ZipFile.OpenRead(SharedReleasePackagingPluginPackage.Value.ArchivePath);

        AssertReleasePackagingArchiveContains(
            archive,
            ".mcp.json",
            ".codex-plugin/plugin.json",
            "run-computer-use-win-mcp.ps1",
            "runtime-release.json",
            "skills/computer-use-win/SKILL.md",
            "okno-plugin-bundle-manifest.json");
        AssertReleasePackagingArchiveDoesNotContainPrefix(archive, "runtime/");
    }

    [Fact]
    public void PackageComputerUseWinPluginReleaseEmbedsProvidedRuntimeDescriptor()
    {
        ReleasePackagingPackage package = SharedReleasePackagingPluginPackage.Value;
        Assert.NotNull(package.RuntimeDescriptorPath);

        using ZipArchive archive = ZipFile.OpenRead(package.ArchivePath);
        using JsonDocument actual = ReadReleasePackagingJsonArchiveEntry(archive, "runtime-release.json");
        using JsonDocument expected = JsonDocument.Parse(File.ReadAllText(package.RuntimeDescriptorPath!));

        Assert.True(JsonElement.DeepEquals(expected.RootElement, actual.RootElement));
    }

    [Fact]
    public void PackageComputerUseWinPluginReleaseRejectsRuntimeDescriptorWithInvalidLaunchMetadata()
    {
        string repoRoot = GetRepositoryRoot();
        string runtimeRoot = GetReleasePackagingRuntimeRoot(repoRoot);
        string outputRoot = CreateSharedReleasePackagingTestOutputRoot(repoRoot, "computer-use-win-plugin-release-invalid-descriptor");
        string runtimeOutputRoot = CreateSharedReleasePackagingTestOutputRoot(repoRoot, "computer-use-win-plugin-release-invalid-descriptor-runtime");

        EnsurePublishedRuntimeBundle(repoRoot, GetPublishScriptPath(repoRoot), runtimeRoot);

        try
        {
            RuntimeReleasePackageResult runtimePackage = PackageRuntimeRelease(
                repoRoot,
                GetReleasePackagingCodexScriptPath(repoRoot, "package-computer-use-win-runtime-release.ps1"),
                runtimeRoot,
                runtimeOutputRoot,
                ReleasePackagingPluginVersion);
            string runtimeDescriptorPath = CreateRuntimeReleaseDescriptor(
                runtimeOutputRoot,
                ReleasePackagingPluginVersion,
                runtimePackage.ArchivePath,
                ReleasePackagingRuntimeRid,
                serverExeRelativePathOverride: "Wrong.Server.exe");
            string runtimeResultPath = CreateModifiedRuntimePackagingResult(runtimeOutputRoot, runtimePackage.ResultPath, descriptorPathOverride: runtimeDescriptorPath);

            ScriptInvocationResult result = InvokePowerShellScript(
                GetReleasePackagingCodexScriptPath(repoRoot, "package-computer-use-win-plugin-release.ps1"),
                repoRoot,
                startInfo =>
                {
                    startInfo.ArgumentList.Add("-Version");
                    startInfo.ArgumentList.Add(ReleasePackagingPluginVersion);
                    startInfo.ArgumentList.Add("-RuntimePackagingResultPath");
                    startInfo.ArgumentList.Add(runtimeResultPath);
                    startInfo.ArgumentList.Add("-OutputRoot");
                    startInfo.ArgumentList.Add(outputRoot);
                });

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("serverExeRelativePath", result.Stderr, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectoryIfExists(outputRoot);
            DeleteDirectoryIfExists(runtimeOutputRoot);
        }
    }

    [Fact]
    public void PackageComputerUseWinPluginReleaseRejectsUnsupportedRidFromRuntimePackagingResult()
    {
        string repoRoot = GetRepositoryRoot();
        string runtimeRoot = GetReleasePackagingRuntimeRoot(repoRoot);
        string outputRoot = CreateSharedReleasePackagingTestOutputRoot(repoRoot, "computer-use-win-plugin-release-unsupported-rid");
        string runtimeOutputRoot = CreateSharedReleasePackagingTestOutputRoot(repoRoot, "computer-use-win-plugin-release-unsupported-rid-runtime");

        EnsurePublishedRuntimeBundle(repoRoot, GetPublishScriptPath(repoRoot), runtimeRoot);

        try
        {
            RuntimeReleasePackageResult runtimePackage = PackageRuntimeRelease(
                repoRoot,
                GetReleasePackagingCodexScriptPath(repoRoot, "package-computer-use-win-runtime-release.ps1"),
                runtimeRoot,
                runtimeOutputRoot,
                ReleasePackagingPluginVersion);
            string mismatchedDescriptorPath = CreateModifiedRuntimeDescriptor(
                runtimeOutputRoot,
                runtimePackage.DescriptorPath,
                ridOverride: "win-arm64",
                assetNameOverride: $"okno-computer-use-win-runtime-{ReleasePackagingPluginVersion}-win-arm64.zip");
            string mismatchedResultPath = CreateModifiedRuntimePackagingResult(
                runtimeOutputRoot,
                runtimePackage.ResultPath,
                descriptorPathOverride: mismatchedDescriptorPath,
                ridOverride: "win-arm64");

            ScriptInvocationResult result = InvokePowerShellScript(
                GetReleasePackagingCodexScriptPath(repoRoot, "package-computer-use-win-plugin-release.ps1"),
                repoRoot,
                startInfo =>
                {
                    startInfo.ArgumentList.Add("-Version");
                    startInfo.ArgumentList.Add(ReleasePackagingPluginVersion);
                    startInfo.ArgumentList.Add("-RuntimePackagingResultPath");
                    startInfo.ArgumentList.Add(mismatchedResultPath);
                    startInfo.ArgumentList.Add("-OutputRoot");
                    startInfo.ArgumentList.Add(outputRoot);
                });

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("RID", result.Stderr, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectoryIfExists(outputRoot);
            DeleteDirectoryIfExists(runtimeOutputRoot);
        }
    }

    [Fact]
    public void PackageComputerUseWinPluginReleaseBundleManifestCarriesPluginAndRuntimeCompatibilityMetadata()
    {
        using ZipArchive archive = ZipFile.OpenRead(SharedReleasePackagingPluginPackage.Value.ArchivePath);
        using JsonDocument manifest = ReadReleasePackagingJsonArchiveEntry(archive, "okno-plugin-bundle-manifest.json");
        JsonElement root = manifest.RootElement;

        Assert.Equal(1, root.GetProperty("formatVersion").GetInt32());
        AssertReleasePackagingJsonString(root, "pluginId", "computer-use-win");
        AssertReleasePackagingJsonString(root, "pluginVersion", ReleasePackagingPluginVersion);
        AssertReleasePackagingJsonString(root, "runtimeVersion", ReleasePackagingPluginVersion);
        AssertReleasePackagingJsonString(root, "runtimeRid", ReleasePackagingRuntimeRid);
        AssertReleasePackagingJsonString(root, "runtimeTag", $"v{ReleasePackagingPluginVersion}");
        AssertReleasePackagingJsonString(root, "runtimeAssetName", $"okno-computer-use-win-runtime-{ReleasePackagingPluginVersion}-{ReleasePackagingRuntimeRid}.zip");
    }

    [Fact]
    public void PackageComputerUseWinRuntimeReleaseCreatesVersionedZipAndChecksumWithoutMutatingRuntimeBundle()
    {
        ReleasePackagingPackage package = SharedReleasePackagingRuntimePackage.Value;

        Assert.Equal($"okno-computer-use-win-runtime-{ReleasePackagingRuntimeVersion}-{ReleasePackagingRuntimeRid}.zip", package.AssetName);
        AssertReleasePackagingFilesExist(package.ArchivePath, package.ChecksumPath);
        Assert.Equal(package.SourceDigestBefore, package.SourceDigestAfter);
    }

    [Fact]
    public void PackageComputerUseWinRuntimeReleasePreservesRuntimeManifestInsideArchive()
    {
        using ZipArchive archive = ZipFile.OpenRead(SharedReleasePackagingRuntimePackage.Value.ArchivePath);

        AssertReleasePackagingArchiveContains(archive, ReleasePackagingRuntimeServerExeName, ReleasePackagingRuntimeBundleManifestName, ReleasePackagingRuntimeWorkerExeName);
    }

    [Fact]
    public async Task ComputerUseWinLauncherBootstrapsRuntimeFromPinnedReleaseDescriptorWhenRuntimeBundleIsMissing()
    {
        string repoRoot = GetRepositoryRoot();
        string sourcePluginRoot = GetReleasePackagingPluginRoot(repoRoot);
        string descriptorRoot = CreateReleasePackagingTestOutputRoot(repoRoot, "computer-use-win-release-bootstrap-descriptor");
        string tempPluginRoot = CreateReleasePackagingTestOutputRoot(repoRoot, "computer-use-win-release-backed-plugin");
        string codexHome = CreateReleasePackagingTestOutputRoot(repoRoot, "codex-home-release-bootstrap");

        try
        {
            string descriptorPath = CreateRuntimeReleaseDescriptor(
                descriptorRoot,
                ReleasePackagingRuntimeVersion,
                SharedReleasePackagingRuntimePackage.Value.ArchivePath,
                ReleasePackagingRuntimeRid);

            CopyReleasePackagingPluginWithoutLocalRuntime(sourcePluginRoot, tempPluginRoot);

            await using PluginLauncherSession launcher = StartPluginLauncherSession(tempPluginRoot, descriptorPath, codexHome);
            PluginMcpSession session = launcher.CreateMcpSession();

            await InitializeReleasePackagingMcpSessionAsync(session);
            string[] toolNames = await ListReleasePackagingMcpToolNamesAsync(session);

            Assert.Contains(ToolNames.ComputerUseWinListApps, toolNames);
            Assert.Contains(ToolNames.ComputerUseWinGetAppState, toolNames);
            Assert.Contains(ToolNames.ComputerUseWinClick, toolNames);
            AssertReleasePackagingFilesExistUnder(
                GetExpectedSharedRuntimeRoot(codexHome, ReleasePackagingRuntimeRid, ReleasePackagingRuntimeVersion),
                ReleasePackagingRuntimeServerExeName,
                ReleasePackagingRuntimeBundleManifestName);
        }
        finally
        {
            DeleteDirectoryIfExists(descriptorRoot);
            DeleteDirectoryIfExists(tempPluginRoot);
            DeleteDirectoryIfExists(codexHome);
        }
    }

    [Fact]
    public void ComputerUseWinLauncherFailsClosedWhenPinnedReleaseChecksumDoesNotMatch()
    {
        string repoRoot = GetRepositoryRoot();
        string sourcePluginRoot = GetReleasePackagingPluginRoot(repoRoot);
        string descriptorRoot = CreateReleasePackagingTestOutputRoot(repoRoot, "computer-use-win-release-bootstrap-fail-descriptor");
        string tempPluginRoot = CreateReleasePackagingTestOutputRoot(repoRoot, "computer-use-win-release-backed-plugin-fail");
        string codexHome = CreateReleasePackagingTestOutputRoot(repoRoot, "codex-home-release-bootstrap-fail");

        try
        {
            string descriptorPath = CreateRuntimeReleaseDescriptor(
                descriptorRoot,
                ReleasePackagingRuntimeVersion,
                SharedReleasePackagingRuntimePackage.Value.ArchivePath,
                ReleasePackagingRuntimeRid,
                sha256Override: new string('0', 64));

            CopyReleasePackagingPluginWithoutLocalRuntime(sourcePluginRoot, tempPluginRoot);

            ScriptInvocationResult result = InvokePluginLauncher(tempPluginRoot, descriptorPath, codexHome);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("sha256", result.Stderr, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectoryIfExists(descriptorRoot);
            DeleteDirectoryIfExists(tempPluginRoot);
            DeleteDirectoryIfExists(codexHome);
        }
    }

    [Fact]
    public void ComputerUseWinRuntimeReleaseDescriptorMatchesPinnedContractShape()
    {
        using JsonDocument descriptor = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(GetReleasePackagingPluginRoot(GetRepositoryRoot()), "runtime-release.json")));
        JsonElement root = descriptor.RootElement;

        Assert.Equal(1, root.GetProperty("formatVersion").GetInt32());
        AssertReleasePackagingJsonString(root, "version", "0.1.0");
        AssertReleasePackagingJsonString(root, "rid", ReleasePackagingRuntimeRid);
        AssertReleasePackagingJsonString(root, "tag", "v0.1.0");
        AssertReleasePackagingJsonString(root, "assetName", "okno-computer-use-win-runtime-0.1.0-win-x64.zip");
        Assert.Contains("/releases/download/v0.1.0/", GetRequiredReleasePackagingJsonString(root, "downloadUrl"), StringComparison.Ordinal);
        Assert.Matches("^[0-9a-f]{64}$", GetRequiredReleasePackagingJsonString(root, "sha256"));
        AssertReleasePackagingJsonString(root, "serverExeRelativePath", ReleasePackagingRuntimeServerExeName);
        AssertReleasePackagingJsonString(root, "bundleManifestName", ReleasePackagingRuntimeBundleManifestName);
    }

    [Fact]
    public void ComputerUseWinInstallRunbookSeparatesCodexGenericAndDeveloperPaths()
    {
        string runbook = File.ReadAllText(Path.Combine(GetRepositoryRoot(), "docs", "runbooks", "computer-use-win-install.md"));

        AssertReleasePackagingTextContainsAll(
            runbook,
            "## 1. Installer-first Codex install",
            "## 2. Installer-first runtime-only install",
            "## 3. Generic MCP STDIO runtime zip",
            "## 4. Developer from source",
            "Okno Setup.exe",
            "install-computer-use-win.ps1",
            ReleasePackagingRuntimeServerExeName,
            "publish-computer-use-win-plugin.ps1");
    }

    [Fact]
    public void CacheInstallProofTracksRuntimeReleaseDescriptorMetadata()
    {
        string proofScript = File.ReadAllText(Path.Combine(GetRepositoryRoot(), "scripts", "codex", "prove-computer-use-win-cache-install.ps1"));

        AssertReleasePackagingTextContainsAll(
            proofScript,
            "runtime-release.json",
            "runtimeReleaseVersion",
            "runtimeReleaseAssetName",
            "runtimeReleaseDownloadUrl");
    }

    private static ReleasePackagingPackage CreateReleasePackagingPluginPackage()
    {
        string repoRoot = GetRepositoryRoot();
        string runtimeRoot = GetReleasePackagingRuntimeRoot(repoRoot);
        string outputRoot = CreateSharedReleasePackagingTestOutputRoot(repoRoot, "computer-use-win-plugin-release-package");
        string runtimeOutputRoot = CreateSharedReleasePackagingTestOutputRoot(repoRoot, "computer-use-win-plugin-runtime-descriptor");

        EnsurePublishedRuntimeBundle(repoRoot, GetPublishScriptPath(repoRoot), runtimeRoot);
        RuntimeReleasePackageResult runtimePackage = PackageRuntimeRelease(
            repoRoot,
            GetReleasePackagingCodexScriptPath(repoRoot, "package-computer-use-win-runtime-release.ps1"),
            runtimeRoot,
            runtimeOutputRoot,
            ReleasePackagingPluginVersion);
        string runtimeDescriptorPath = runtimePackage.DescriptorPath;

        return InvokeReleasePackagingPackageScript(
            repoRoot,
            GetReleasePackagingCodexScriptPath(repoRoot, "package-computer-use-win-plugin-release.ps1"),
            GetReleasePackagingPluginRoot(repoRoot),
            "Plugin release packaging script",
            startInfo =>
            {
                startInfo.ArgumentList.Add("-Version");
                startInfo.ArgumentList.Add(ReleasePackagingPluginVersion);
                startInfo.ArgumentList.Add("-RuntimePackagingResultPath");
                startInfo.ArgumentList.Add(runtimePackage.ResultPath);
                startInfo.ArgumentList.Add("-OutputRoot");
                startInfo.ArgumentList.Add(outputRoot);
            },
            runtimeDescriptorPath);
    }

    private static ReleasePackagingPackage CreateReleasePackagingRuntimePackage()
    {
        string repoRoot = GetRepositoryRoot();
        string runtimeRoot = GetReleasePackagingRuntimeRoot(repoRoot);
        string outputRoot = CreateSharedReleasePackagingTestOutputRoot(repoRoot, "computer-use-win-runtime-release-package");

        EnsurePublishedRuntimeBundle(repoRoot, GetPublishScriptPath(repoRoot), runtimeRoot);

        return InvokeReleasePackagingPackageScript(
            repoRoot,
            GetReleasePackagingCodexScriptPath(repoRoot, "package-computer-use-win-runtime-release.ps1"),
            runtimeRoot,
            "Release packaging script",
            startInfo => AddReleasePackagingRuntimePackageArguments(startInfo, ReleasePackagingRuntimeVersion, runtimeRoot, outputRoot));
    }

    private static ReleasePackagingPackage InvokeReleasePackagingPackageScript(
        string repoRoot,
        string packageScriptPath,
        string sourceRoot,
        string failureContext,
        Action<ProcessStartInfo> configureStartInfo,
        string? runtimeDescriptorPath = null)
    {
        string sourceDigestBefore = ComputeDirectoryDigest(sourceRoot);
        ScriptInvocationResult result = InvokePowerShellScript(packageScriptPath, repoRoot, configureStartInfo);

        AssertReleasePackagingScriptSucceeded(result, failureContext);

        using JsonDocument payload = ParseJsonStdoutOrThrow(result, failureContext);
        return new ReleasePackagingPackage(
            GetRequiredReleasePackagingJsonString(payload.RootElement, "archivePath"),
            GetRequiredReleasePackagingJsonString(payload.RootElement, "checksumPath"),
            GetRequiredReleasePackagingJsonString(payload.RootElement, "assetName"),
            GetOptionalReleasePackagingJsonString(payload.RootElement, "descriptorPath") ?? runtimeDescriptorPath,
            sourceDigestBefore,
            ComputeDirectoryDigest(sourceRoot));
    }

    private static void AddReleasePackagingRuntimePackageArguments(ProcessStartInfo startInfo, string version, string runtimeRoot, string outputRoot)
    {
        startInfo.ArgumentList.Add("-Version");
        startInfo.ArgumentList.Add(version);
        startInfo.ArgumentList.Add("-Rid");
        startInfo.ArgumentList.Add(ReleasePackagingRuntimeRid);
        startInfo.ArgumentList.Add("-PublishSourceRoot");
        startInfo.ArgumentList.Add(runtimeRoot);
        startInfo.ArgumentList.Add("-OutputRoot");
        startInfo.ArgumentList.Add(outputRoot);
    }

    private static string CreateRuntimeReleaseDescriptor(
        string outputRoot,
        string version,
        string archivePath,
        string rid,
        string? sha256Override = null,
        string? serverExeRelativePathOverride = null,
        string? bundleManifestNameOverride = null)
    {
        Directory.CreateDirectory(outputRoot);
        string sha256 = sha256Override ?? ComputeFileSha256(archivePath);
        string normalizedVersion = version.Replace('.', '_').Replace('-', '_').Replace('+', '_');
        string descriptorPath = Path.Combine(outputRoot, $"runtime-release.override.{normalizedVersion}.{rid}.json");
        var descriptor = new
        {
            formatVersion = 1,
            version,
            rid,
            tag = $"v{version}",
            assetName = Path.GetFileName(archivePath),
            downloadUrl = new Uri(archivePath).AbsoluteUri,
            sha256,
            serverExeRelativePath = serverExeRelativePathOverride ?? ReleasePackagingRuntimeServerExeName,
            bundleManifestName = bundleManifestNameOverride ?? ReleasePackagingRuntimeBundleManifestName,
        };

        File.WriteAllText(descriptorPath, JsonSerializer.Serialize(descriptor));
        return descriptorPath;
    }

    private static async Task InitializeReleasePackagingMcpSessionAsync(PluginMcpSession session)
    {
        using JsonDocument _ = await session.SendRequestAsync(
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
    }

    private static async Task<string[]> ListReleasePackagingMcpToolNamesAsync(PluginMcpSession session)
    {
        using JsonDocument toolsResponse = await session.SendRequestAsync("tools/list", new { }, "tools/list");

        return toolsResponse.RootElement
            .GetProperty("result")
            .GetProperty("tools")
            .EnumerateArray()
            .Select(tool => tool.GetProperty("name").GetString() ?? string.Empty)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
    }

    private static void CopyReleasePackagingPluginWithoutLocalRuntime(string sourcePluginRoot, string tempPluginRoot)
    {
        CopyDirectory(sourcePluginRoot, tempPluginRoot, IncludeStablePluginPath);
        DeleteDirectoryIfExists(Path.Combine(tempPluginRoot, "runtime", ReleasePackagingRuntimeRid));
    }

    private static void AssertReleasePackagingScriptSucceeded(ScriptInvocationResult result, string failureContext)
    {
        Assert.True(
            result.ExitCode == 0,
            $"{failureContext} failed. ExitCode={result.ExitCode}. stderr='{result.Stderr.Trim()}', stdout='{result.Stdout.Trim()}'.");
    }

    private static void AssertReleasePackagingArchiveContains(ZipArchive archive, params string[] expectedPaths)
    {
        HashSet<string> entries = GetNormalizedReleasePackagingArchiveEntryPaths(archive);

        foreach (string expectedPath in expectedPaths)
        {
            Assert.True(entries.Contains(expectedPath), $"Archive is missing '{expectedPath}'.");
        }
    }

    private static void AssertReleasePackagingArchiveDoesNotContainPrefix(ZipArchive archive, string pathPrefix)
    {
        Assert.DoesNotContain(
            GetNormalizedReleasePackagingArchiveEntryPaths(archive),
            entry => entry.StartsWith(pathPrefix, StringComparison.Ordinal));
    }

    private static JsonDocument ReadReleasePackagingJsonArchiveEntry(ZipArchive archive, string entryPath)
    {
        using Stream stream = GetRequiredReleasePackagingArchiveEntry(archive, entryPath).Open();
        return JsonDocument.Parse(stream);
    }

    private static ZipArchiveEntry GetRequiredReleasePackagingArchiveEntry(ZipArchive archive, string entryPath) =>
        Assert.Single(archive.Entries.Where(
            entry => string.Equals(NormalizeArchiveEntryPath(entry.FullName), entryPath, StringComparison.Ordinal)));

    private static HashSet<string> GetNormalizedReleasePackagingArchiveEntryPaths(ZipArchive archive) =>
        archive.Entries.Select(entry => NormalizeArchiveEntryPath(entry.FullName)).ToHashSet(StringComparer.Ordinal);

    private static void AssertReleasePackagingJsonString(JsonElement json, string propertyName, string expected) =>
        Assert.Equal(expected, GetRequiredReleasePackagingJsonString(json, propertyName));

    private static string GetRequiredReleasePackagingJsonString(JsonElement json, string propertyName) =>
        json.GetProperty(propertyName).GetString() ?? throw new InvalidOperationException($"{propertyName} missing.");

    private static string? GetOptionalReleasePackagingJsonString(JsonElement json, string propertyName)
    {
        if (!json.TryGetProperty(propertyName, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.GetString();
    }

    private static void AssertReleasePackagingTextContainsAll(string text, params string[] expectedSnippets)
    {
        foreach (string expectedSnippet in expectedSnippets)
        {
            Assert.Contains(expectedSnippet, text, StringComparison.Ordinal);
        }
    }

    private static void AssertReleasePackagingFilesExist(params string[] paths)
    {
        foreach (string path in paths)
        {
            Assert.True(File.Exists(path), $"Expected file does not exist: {path}");
        }
    }

    private static void AssertReleasePackagingFilesExistUnder(string rootPath, params string[] relativePaths)
    {
        foreach (string relativePath in relativePaths)
        {
            AssertReleasePackagingFilesExist(Path.Combine(rootPath, relativePath));
        }
    }

    private static string ComputeDirectoryDigest(string rootPath)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        foreach (string path in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories)
            .OrderBy(static value => value, StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(NormalizeArchiveEntryPath(Path.GetRelativePath(rootPath, path))));
            hash.AppendData(ReleasePackagingDirectoryDigestSeparator);
            hash.AppendData(ComputeFileSha256Bytes(path));
            hash.AppendData(ReleasePackagingDirectoryDigestSeparator);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static string ComputeFileSha256(string path) =>
        Convert.ToHexString(ComputeFileSha256Bytes(path)).ToLowerInvariant();

    private static byte[] ComputeFileSha256Bytes(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return SHA256.HashData(stream);
    }

    private static string NormalizeArchiveEntryPath(string path) => path.Replace('\\', '/');

    private static string GetReleasePackagingCodexScriptPath(string repoRoot, string scriptName) =>
        Path.Combine(repoRoot, "scripts", "codex", scriptName);

    private static string GetReleasePackagingPluginRoot(string repoRoot) =>
        Path.Combine(repoRoot, "plugins", "computer-use-win");

    private static string GetReleasePackagingRuntimeRoot(string repoRoot) =>
        Path.Combine(GetReleasePackagingPluginRoot(repoRoot), "runtime", ReleasePackagingRuntimeRid);

    private static string CreateReleasePackagingTestOutputRoot(string repoRoot, string scenarioName) =>
        Path.Combine(repoRoot, ".tmp", ".codex", "tests", scenarioName, Guid.NewGuid().ToString("N"));

    private static string CreateSharedReleasePackagingTestOutputRoot(string repoRoot, string scenarioName)
    {
        string outputRoot = CreateReleasePackagingTestOutputRoot(repoRoot, scenarioName);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => TryDeleteReleasePackagingDirectoryIfExists(outputRoot);
        return outputRoot;
    }

    private static void TryDeleteReleasePackagingDirectoryIfExists(string path)
    {
        try
        {
            DeleteDirectoryIfExists(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static bool IncludeStablePluginPath(string relativePath)
    {
        string normalized = relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        string localRuntimeRoot = $"runtime{Path.DirectorySeparatorChar}{ReleasePackagingRuntimeRid}";

        return !normalized.Equals(localRuntimeRoot, StringComparison.OrdinalIgnoreCase)
            && !normalized.StartsWith($"{localRuntimeRoot}{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            && !normalized.StartsWith($"{localRuntimeRoot}.", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ReleasePackagingPackage(
        string ArchivePath,
        string ChecksumPath,
        string AssetName,
        string? RuntimeDescriptorPath,
        string SourceDigestBefore,
        string SourceDigestAfter);
}
