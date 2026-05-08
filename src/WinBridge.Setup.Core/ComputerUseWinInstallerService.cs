// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.IO.Compression;
using System.Text.Json;

namespace WinBridge.Setup.Core;

public sealed class ComputerUseWinInstallerService
{
    private const int StateFormatVersion = 1;
    private const string CodexReceiptMode = "codex";
    private const string RuntimeOnlyReceiptMode = "runtime_only";
    private const string PluginId = "computer-use-win";
    private const string DefaultMarketplaceName = "okno-local-installed";
    private const string DefaultMarketplaceDisplayName = "Okno: Installed plugins";
    private const string PluginBundleManifestName = "okno-plugin-bundle-manifest.json";
    private const string RuntimeOnlyLauncherCommand = "powershell.exe";
    private static readonly string[][] OwnedCodexConfigSectionPaths =
    [
        ["plugins", "computer-use-win@okno-local-installed"],
        ["mcp_servers", "computer_use_win"],
        ["mcp_servers", "computer-use-win"],
    ];
    private static readonly HttpClient HttpClient = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly ComputerUseWinRuntimeFoundationService runtimeFoundation;
    private readonly ComputerUseWinRuntimeStorePaths storePaths;
    private readonly string userProfileRoot;
    private readonly string marketplacePath;
    private readonly string pluginSourceRoot;

    public ComputerUseWinInstallerService()
        : this(new ComputerUseWinRuntimeFoundationService())
    {
    }

    public ComputerUseWinInstallerService(ComputerUseWinRuntimeFoundationService runtimeFoundation)
        : this(runtimeFoundation, ResolveUserProfileRoot())
    {
    }

    public ComputerUseWinInstallerService(ComputerUseWinRuntimeFoundationService runtimeFoundation, string userProfileRoot)
    {
        this.runtimeFoundation = runtimeFoundation;
        storePaths = runtimeFoundation.StorePaths;
        this.userProfileRoot = Path.GetFullPath(userProfileRoot);
        marketplacePath = Path.Combine(this.userProfileRoot, ".agents", "plugins", "marketplace.json");
        pluginSourceRoot = Path.Combine(storePaths.CodexHome, "plugins", PluginId);
    }

    public ComputerUseWinInstallerStatus GetStatus(string? descriptorPathOverride = null)
    {
        ComputerUseWinRuntimeStatus runtimeStatus = runtimeFoundation.GetRuntimeStatus(descriptorPathOverride);
        return new ComputerUseWinInstallerStatus(
            StateFormatVersion,
            storePaths.CodexHome,
            storePaths.RuntimeStoreRoot,
            runtimeStatus,
            TryReadReceipt(ComputerUseWinInstallMode.RuntimeOnly),
            TryReadReceipt(ComputerUseWinInstallMode.Codex));
    }

    public ComputerUseWinInstallerResult InstallRuntimeOnly(string? descriptorPathOverride = null)
    {
        ComputerUseWinRuntimeInstallResult runtimeResult = runtimeFoundation.InstallRuntime(descriptorPathOverride);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ComputerUseWinInstallReceipt receipt = WriteReceipt(
            ComputerUseWinInstallMode.RuntimeOnly,
            runtimeResult.Version,
            runtimeResult.Rid,
            runtimeResult.RuntimeRoot,
            pluginVersion: null,
            pluginSourceRoot: null,
            marketplacePath: null,
            marketplaceName: null,
            marketplaceSourcePath: null,
            restartRequired: false,
            installedAtUtcOverride: now,
            updatedAtUtc: now);

        return new ComputerUseWinInstallerResult(
            StateFormatVersion,
            "install",
            RuntimeOnlyReceiptMode,
            storePaths.CodexHome,
            storePaths.RuntimeStoreRoot,
            runtimeResult.RuntimeRoot,
            runtimeResult.Version,
            runtimeResult.Rid,
            null,
            null,
            null,
            null,
            false,
            BuildRuntimeOnlySnippet(),
            storePaths.GetReceiptPath(ComputerUseWinInstallMode.RuntimeOnly),
            receipt.InstalledAtUtc,
            receipt.UpdatedAtUtc);
    }

    public ComputerUseWinInstallerResult UpdateRuntimeOnly(string? descriptorPathOverride = null)
    {
        ComputerUseWinRuntimeInstallResult runtimeResult = runtimeFoundation.InstallRuntime(descriptorPathOverride);
        ComputerUseWinInstallReceipt? existingReceipt = TryReadReceipt(ComputerUseWinInstallMode.RuntimeOnly);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ComputerUseWinInstallReceipt receipt = WriteReceipt(
            ComputerUseWinInstallMode.RuntimeOnly,
            runtimeResult.Version,
            runtimeResult.Rid,
            runtimeResult.RuntimeRoot,
            pluginVersion: null,
            pluginSourceRoot: null,
            marketplacePath: null,
            marketplaceName: null,
            marketplaceSourcePath: null,
            restartRequired: false,
            installedAtUtcOverride: existingReceipt?.InstalledAtUtc ?? now,
            updatedAtUtc: now);

        return new ComputerUseWinInstallerResult(
            StateFormatVersion,
            "update",
            RuntimeOnlyReceiptMode,
            storePaths.CodexHome,
            storePaths.RuntimeStoreRoot,
            runtimeResult.RuntimeRoot,
            runtimeResult.Version,
            runtimeResult.Rid,
            null,
            null,
            null,
            null,
            false,
            BuildRuntimeOnlySnippet(),
            storePaths.GetReceiptPath(ComputerUseWinInstallMode.RuntimeOnly),
            receipt.InstalledAtUtc,
            receipt.UpdatedAtUtc);
    }

    public ComputerUseWinInstallerResult RepairRuntimeOnly(string? descriptorPathOverride = null)
    {
        ComputerUseWinRuntimeInstallResult runtimeResult = runtimeFoundation.RepairRuntime(descriptorPathOverride);
        ComputerUseWinInstallReceipt? existingReceipt = TryReadReceipt(ComputerUseWinInstallMode.RuntimeOnly);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ComputerUseWinInstallReceipt receipt = WriteReceipt(
            ComputerUseWinInstallMode.RuntimeOnly,
            runtimeResult.Version,
            runtimeResult.Rid,
            runtimeResult.RuntimeRoot,
            pluginVersion: null,
            pluginSourceRoot: null,
            marketplacePath: null,
            marketplaceName: null,
            marketplaceSourcePath: null,
            restartRequired: false,
            installedAtUtcOverride: existingReceipt?.InstalledAtUtc ?? now,
            updatedAtUtc: now);

        return new ComputerUseWinInstallerResult(
            StateFormatVersion,
            "repair",
            RuntimeOnlyReceiptMode,
            storePaths.CodexHome,
            storePaths.RuntimeStoreRoot,
            runtimeResult.RuntimeRoot,
            runtimeResult.Version,
            runtimeResult.Rid,
            null,
            null,
            null,
            null,
            false,
            BuildRuntimeOnlySnippet(),
            storePaths.GetReceiptPath(ComputerUseWinInstallMode.RuntimeOnly),
            receipt.InstalledAtUtc,
            receipt.UpdatedAtUtc);
    }

    public ComputerUseWinInstallerResult UninstallRuntimeOnly()
    {
        ComputerUseWinInstallReceipt? receipt = TryReadReceipt(ComputerUseWinInstallMode.RuntimeOnly)
            ?? throw new InvalidOperationException("Runtime-only install receipt is missing.");

        DeleteReceipt(ComputerUseWinInstallMode.RuntimeOnly);
        if (TryReadReceipt(ComputerUseWinInstallMode.Codex) is null)
        {
            DeleteDirectoryIfExists(storePaths.RuntimeStoreRoot);
        }

        return new ComputerUseWinInstallerResult(
            StateFormatVersion,
            "uninstall",
            RuntimeOnlyReceiptMode,
            storePaths.CodexHome,
            storePaths.RuntimeStoreRoot,
            receipt.RuntimeRoot,
            receipt.RuntimeVersion,
            receipt.RuntimeRid,
            null,
            null,
            null,
            null,
            false,
            null,
            storePaths.GetReceiptPath(ComputerUseWinInstallMode.RuntimeOnly),
            receipt.InstalledAtUtc,
            DateTimeOffset.UtcNow);
    }

    public ComputerUseWinInstallerResult InstallCodex(string? descriptorPathOverride = null)
    {
        return InstallOrUpdateCodex("install", descriptorPathOverride);
    }

    public ComputerUseWinInstallerResult UpdateCodex(string? descriptorPathOverride = null)
    {
        return InstallOrUpdateCodex("update", descriptorPathOverride);
    }

    public ComputerUseWinInstallerResult RepairCodex(string? descriptorPathOverride = null)
    {
        return InstallOrUpdateCodex("repair", descriptorPathOverride);
    }

    public ComputerUseWinInstallerResult UninstallCodex()
    {
        ComputerUseWinInstallReceipt receipt = TryReadReceipt(ComputerUseWinInstallMode.Codex)
            ?? throw new InvalidOperationException("Codex install receipt is missing.");

        if (!File.Exists(marketplacePath))
        {
            throw new InvalidOperationException($"Personal marketplace file '{marketplacePath}' is missing.");
        }

        PersonalMarketplace marketplace = PrepareMarketplace(removingPluginEntry: true, marketplaceNameOverride: receipt.MarketplaceName, marketplaceSourcePath: null);
        WriteMarketplace(marketplace);
        DeleteDirectoryIfExists(receipt.PluginSourceRoot ?? pluginSourceRoot);
        RemoveOwnedCodexConfigSections();
        DeleteReceipt(ComputerUseWinInstallMode.Codex);

        if (TryReadReceipt(ComputerUseWinInstallMode.RuntimeOnly) is null)
        {
            DeleteDirectoryIfExists(storePaths.RuntimeStoreRoot);
        }

        return new ComputerUseWinInstallerResult(
            StateFormatVersion,
            "uninstall",
            CodexReceiptMode,
            storePaths.CodexHome,
            storePaths.RuntimeStoreRoot,
            receipt.RuntimeRoot,
            receipt.RuntimeVersion,
            receipt.RuntimeRid,
            receipt.PluginSourceRoot,
            receipt.MarketplacePath,
            receipt.MarketplaceName,
            receipt.MarketplaceSourcePath,
            false,
            null,
            storePaths.GetReceiptPath(ComputerUseWinInstallMode.Codex),
            receipt.InstalledAtUtc,
            DateTimeOffset.UtcNow);
    }

    public ComputerUseWinInstallerResult UninstallAll()
    {
        ComputerUseWinInstallReceipt? runtimeOnlyReceipt = TryReadReceipt(ComputerUseWinInstallMode.RuntimeOnly);
        ComputerUseWinInstallReceipt? codexReceipt = TryReadReceipt(ComputerUseWinInstallMode.Codex);
        ComputerUseWinInstallReceipt? referenceReceipt = codexReceipt ?? runtimeOnlyReceipt;
        string runtimeRoot = referenceReceipt?.RuntimeRoot ?? storePaths.RuntimeStoreRoot;

        if (codexReceipt is not null)
        {
            if (File.Exists(marketplacePath))
            {
                TryRemovePluginEntryFromMarketplace(codexReceipt.MarketplaceName);
            }

            DeleteDirectoryIfExists(codexReceipt.PluginSourceRoot ?? pluginSourceRoot);
            DeleteReceipt(ComputerUseWinInstallMode.Codex);
            RemoveOwnedCodexConfigSections();
        }

        if (runtimeOnlyReceipt is not null)
        {
            DeleteReceipt(ComputerUseWinInstallMode.RuntimeOnly);
        }

        DeleteDirectoryIfExists(storePaths.RuntimeStoreRoot);

        return new ComputerUseWinInstallerResult(
            StateFormatVersion,
            "remove-all",
            "all",
            storePaths.CodexHome,
            storePaths.RuntimeStoreRoot,
            runtimeRoot,
            referenceReceipt?.RuntimeVersion ?? string.Empty,
            referenceReceipt?.RuntimeRid ?? string.Empty,
            null,
            null,
            null,
            null,
            false,
            null,
            Path.Combine(storePaths.RuntimeStoreRoot, "receipts"),
            referenceReceipt?.InstalledAtUtc ?? DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
    }

    private ComputerUseWinInstallerResult InstallOrUpdateCodex(string action, string? descriptorPathOverride)
    {
        ComputerUseWinRuntimeInstallResult runtimeResult = action == "repair"
            ? runtimeFoundation.RepairRuntime(descriptorPathOverride)
            : runtimeFoundation.InstallRuntime(descriptorPathOverride);

        ComputerUseWinRuntimeReleaseDescriptor runtimeDescriptor = ComputerUseWinRuntimeFoundationService.LoadRuntimeReleaseDescriptor(descriptorPathOverride);
        ComputerUseWinPluginReleaseAsset pluginRelease = ResolvePluginRelease(runtimeDescriptor);
        string marketplaceSourcePath = BuildMarketplaceSourcePath(pluginSourceRoot);
        PersonalMarketplace marketplace = PrepareMarketplace(removingPluginEntry: false, marketplaceNameOverride: null, marketplaceSourcePath: marketplaceSourcePath);
        string installedPluginRoot = InstallPluginBundle(pluginRelease);
        string marketplaceName = WriteMarketplace(marketplace);
        ComputerUseWinInstallReceipt? existingReceipt = TryReadReceipt(ComputerUseWinInstallMode.Codex);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ComputerUseWinInstallReceipt receipt = WriteReceipt(
            ComputerUseWinInstallMode.Codex,
            runtimeResult.Version,
            runtimeResult.Rid,
            runtimeResult.RuntimeRoot,
            pluginRelease.BundleManifest.PluginVersion,
            installedPluginRoot,
            marketplacePath,
            marketplaceName,
            marketplaceSourcePath,
            restartRequired: true,
            installedAtUtcOverride: existingReceipt?.InstalledAtUtc ?? now,
            updatedAtUtc: now);

        return new ComputerUseWinInstallerResult(
            StateFormatVersion,
            action,
            CodexReceiptMode,
            storePaths.CodexHome,
            storePaths.RuntimeStoreRoot,
            runtimeResult.RuntimeRoot,
            runtimeResult.Version,
            runtimeResult.Rid,
            installedPluginRoot,
            marketplacePath,
            marketplaceName,
            marketplaceSourcePath,
            true,
            null,
            storePaths.GetReceiptPath(ComputerUseWinInstallMode.Codex),
            receipt.InstalledAtUtc,
            receipt.UpdatedAtUtc);
    }

    private ComputerUseWinPluginReleaseAsset ResolvePluginRelease(ComputerUseWinRuntimeReleaseDescriptor runtimeDescriptor)
    {
        string pluginAssetName = $"okno-computer-use-win-plugin-{runtimeDescriptor.Version}.zip";
        string checksumFileName = $"okno-computer-use-win-plugin-{runtimeDescriptor.Version}-SHA256SUMS.txt";
        Uri runtimeUri = new(runtimeDescriptor.DownloadUrl);
        Uri pluginArchiveUri;
        Uri checksumUri;

        if (runtimeUri.IsFile)
        {
            string directory = Path.GetDirectoryName(runtimeUri.LocalPath)
                ?? throw new InvalidOperationException("Runtime release descriptor download path does not have a parent directory.");
            pluginArchiveUri = new Uri(Path.Combine(directory, pluginAssetName));
            checksumUri = new Uri(Path.Combine(directory, checksumFileName));
        }
        else
        {
            Uri baseUri = new(new Uri(runtimeDescriptor.DownloadUrl), ".");
            pluginArchiveUri = new(baseUri, pluginAssetName);
            checksumUri = new(baseUri, checksumFileName);
        }

        string pluginArchiveSha256 = ReadSha256FromChecksumFile(checksumUri, pluginAssetName);
        return new ComputerUseWinPluginReleaseAsset(
            pluginAssetName,
            pluginArchiveUri,
            checksumUri,
            pluginArchiveSha256,
            ReadPluginBundleManifest(pluginArchiveUri, pluginArchiveSha256, runtimeDescriptor));
    }

    private ComputerUseWinPluginBundleManifest ReadPluginBundleManifest(Uri pluginArchiveUri, string expectedSha256, ComputerUseWinRuntimeReleaseDescriptor runtimeDescriptor)
    {
        string tempDirectory = Path.Combine(storePaths.LocksRoot, "plugin-manifest-" + Guid.NewGuid().ToString("N"));
        string archivePath = Path.Combine(tempDirectory, runtimeDescriptor.Version + ".zip");
        string extractRoot = Path.Combine(tempDirectory, "extract");

        Directory.CreateDirectory(tempDirectory);
        try
        {
            SaveUriToPath(pluginArchiveUri, archivePath);
            string actualSha256 = ComputeFileSha256(archivePath);
            if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"SHA256 mismatch for plugin bundle asset '{pluginArchiveUri}'. Expected '{expectedSha256}', actual '{actualSha256}'.");
            }

            ZipFile.ExtractToDirectory(archivePath, extractRoot);
            string manifestPath = Path.Combine(extractRoot, PluginBundleManifestName);
            if (!File.Exists(manifestPath))
            {
                throw new InvalidOperationException($"Plugin bundle manifest '{PluginBundleManifestName}' is missing from plugin release asset.");
            }

            ComputerUseWinPluginBundleManifest manifest = JsonSerializer.Deserialize<ComputerUseWinPluginBundleManifest>(
                    File.ReadAllText(manifestPath),
                    JsonOptions)
                ?? throw new InvalidOperationException("Plugin bundle manifest is empty.");

            if (manifest.FormatVersion != 1)
            {
                throw new InvalidOperationException($"Unsupported plugin bundle manifest version '{manifest.FormatVersion}'.");
            }

            if (!string.Equals(manifest.PluginId, PluginId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Plugin bundle manifest plugin id '{manifest.PluginId}' does not match expected '{PluginId}'.");
            }

            if (!string.Equals(manifest.RuntimeVersion, runtimeDescriptor.Version, StringComparison.Ordinal)
                || !string.Equals(manifest.RuntimeRid, runtimeDescriptor.Rid, StringComparison.Ordinal)
                || !string.Equals(manifest.RuntimeTag, runtimeDescriptor.Tag, StringComparison.Ordinal)
                || !string.Equals(manifest.RuntimeAssetName, runtimeDescriptor.AssetName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Plugin bundle manifest is incompatible with the runtime release descriptor.");
            }

            return manifest;
        }
        finally
        {
            DeleteDirectoryIfExists(tempDirectory);
        }
    }

    private string InstallPluginBundle(ComputerUseWinPluginReleaseAsset pluginRelease)
    {
        string tempDirectory = Path.Combine(storePaths.LocksRoot, "plugin-install-" + Guid.NewGuid().ToString("N"));
        string archivePath = Path.Combine(tempDirectory, pluginRelease.AssetName);
        string extractRoot = Path.Combine(tempDirectory, "extract");
        string pluginSourceParent = Path.GetDirectoryName(pluginSourceRoot)
            ?? throw new InvalidOperationException($"Plugin source root '{pluginSourceRoot}' does not have a parent directory.");
        string deploymentRoot = Path.Combine(pluginSourceParent, ".computer-use-win-deploy-" + Guid.NewGuid().ToString("N"));
        string stagingRoot = Path.Combine(deploymentRoot, "staging");
        string backupRoot = Path.Combine(deploymentRoot, "backup");

        Directory.CreateDirectory(tempDirectory);
        Directory.CreateDirectory(pluginSourceParent);
        try
        {
            SaveUriToPath(pluginRelease.ArchiveUri, archivePath);
            string actualSha256 = ComputeFileSha256(archivePath);
            if (!string.Equals(actualSha256, pluginRelease.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"SHA256 mismatch for plugin bundle asset '{pluginRelease.AssetName}'. Expected '{pluginRelease.Sha256}', actual '{actualSha256}'.");
            }

            ZipFile.ExtractToDirectory(archivePath, extractRoot);
            ValidateExtractedPluginBundle(extractRoot, pluginRelease.BundleManifest);

            Directory.CreateDirectory(deploymentRoot);
            CopyDirectory(extractRoot, stagingRoot);
            if (Directory.Exists(pluginSourceRoot))
            {
                Directory.Move(pluginSourceRoot, backupRoot);
            }

            Directory.Move(stagingRoot, pluginSourceRoot);
            DeleteDirectoryIfExists(backupRoot);
            return pluginSourceRoot;
        }
        catch
        {
            if (!Directory.Exists(pluginSourceRoot) && Directory.Exists(backupRoot))
            {
                Directory.Move(backupRoot, pluginSourceRoot);
            }

            throw;
        }
        finally
        {
            DeleteDirectoryIfExists(tempDirectory);
            DeleteDirectoryIfExists(deploymentRoot);
        }
    }

    private static void ValidateExtractedPluginBundle(string extractRoot, ComputerUseWinPluginBundleManifest manifest)
    {
        if (Directory.Exists(Path.Combine(extractRoot, "runtime")))
        {
            throw new InvalidOperationException("Plugin bundle unexpectedly contains embedded runtime directory.");
        }

        string pluginManifestPath = Path.Combine(extractRoot, ".codex-plugin", "plugin.json");
        string mcpManifestPath = Path.Combine(extractRoot, ".mcp.json");
        string launcherPath = Path.Combine(extractRoot, "run-computer-use-win-mcp.ps1");
        string runtimeDescriptorPath = Path.Combine(extractRoot, "runtime-release.json");
        if (!File.Exists(pluginManifestPath) || !File.Exists(mcpManifestPath) || !File.Exists(launcherPath) || !File.Exists(runtimeDescriptorPath))
        {
            throw new InvalidOperationException("Extracted plugin bundle is missing required plugin contract files.");
        }

        Dictionary<string, long> expectedEntries = manifest.Files.ToDictionary(static entry => entry.Path, static entry => entry.Size, StringComparer.Ordinal);
        foreach (string filePath in Directory.EnumerateFiles(extractRoot, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(extractRoot, filePath);
            if (string.Equals(relativePath, PluginBundleManifestName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!expectedEntries.Remove(relativePath, out long expectedSize))
            {
                throw new InvalidOperationException($"Plugin bundle contains unexpected file '{relativePath}'.");
            }

            if (new FileInfo(filePath).Length != expectedSize)
            {
                throw new InvalidOperationException($"Plugin bundle file '{relativePath}' has size drift.");
            }
        }

        if (expectedEntries.Count > 0)
        {
            throw new InvalidOperationException($"Plugin bundle is incomplete. Missing: {string.Join(", ", expectedEntries.Keys)}.");
        }
    }

    private PersonalMarketplace PrepareMarketplace(bool removingPluginEntry, string? marketplaceNameOverride, string? marketplaceSourcePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(marketplacePath)!);
        PersonalMarketplace marketplace = LoadMarketplace(marketplaceNameOverride);

        marketplace.Plugins.RemoveAll(entry => string.Equals(entry.Name, PluginId, StringComparison.Ordinal));
        if (!removingPluginEntry)
        {
            if (string.IsNullOrWhiteSpace(marketplaceSourcePath))
            {
                throw new InvalidOperationException("Marketplace source path is required when adding the computer-use-win entry.");
            }

            marketplace.Plugins.Add(new PersonalMarketplacePluginEntry(
                PluginId,
                new PersonalMarketplacePluginSource("local", marketplaceSourcePath),
                new PersonalMarketplacePluginPolicy("AVAILABLE", "ON_INSTALL"),
                "Productivity"));
        }

        return marketplace;
    }

    private string WriteMarketplace(PersonalMarketplace marketplace)
    {
        string tempPath = marketplacePath + ".tmp-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(tempPath, JsonSerializer.Serialize(marketplace, JsonOptions));
        if (File.Exists(marketplacePath))
        {
            File.Delete(marketplacePath);
        }

        File.Move(tempPath, marketplacePath);
        return marketplace.Name;
    }

    private void TryRemovePluginEntryFromMarketplace(string? marketplaceNameOverride)
    {
        try
        {
            PersonalMarketplace marketplace = PrepareMarketplace(removingPluginEntry: true, marketplaceNameOverride: marketplaceNameOverride, marketplaceSourcePath: null);
            WriteMarketplace(marketplace);
        }
        catch (InvalidOperationException)
        {
            // Full removal must not be blocked by unrelated malformed user-owned marketplace state.
        }
    }

    private PersonalMarketplace LoadMarketplace(string? marketplaceNameOverride)
    {
        if (!File.Exists(marketplacePath))
        {
            return CreateDefaultMarketplace(marketplaceNameOverride);
        }

        try
        {
            PersonalMarketplace? marketplace = JsonSerializer.Deserialize<PersonalMarketplace>(
                File.ReadAllText(marketplacePath),
                JsonOptions);
            if (marketplace is null || marketplace.Plugins is null)
            {
                throw new InvalidOperationException("Marketplace file is invalid.");
            }

            return marketplace;
        }
        catch (Exception)
        {
            throw new InvalidOperationException($"Marketplace file '{marketplacePath}' is malformed. Installer will not overwrite unrelated personal marketplace entries automatically.");
        }
    }

    private static PersonalMarketplace CreateDefaultMarketplace(string? marketplaceNameOverride)
    {
        return new PersonalMarketplace(
            marketplaceNameOverride ?? DefaultMarketplaceName,
            new PersonalMarketplaceInterface(DefaultMarketplaceDisplayName),
            []);
    }

    private string BuildMarketplaceSourcePath(string pluginRoot)
    {
        string normalizedUserProfile = Path.GetFullPath(userProfileRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string normalizedPluginRoot = Path.GetFullPath(pluginRoot);
        string prefix = normalizedUserProfile + Path.DirectorySeparatorChar;
        if (normalizedPluginRoot.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            string relativePath = normalizedPluginRoot[prefix.Length..].Replace('\\', '/');
            return "./" + relativePath;
        }

        return normalizedPluginRoot.Replace('\\', '/');
    }

    private ComputerUseWinInstallReceipt WriteReceipt(
        ComputerUseWinInstallMode mode,
        string runtimeVersion,
        string runtimeRid,
        string runtimeRoot,
        string? pluginVersion,
        string? pluginSourceRoot,
        string? marketplacePath,
        string? marketplaceName,
        string? marketplaceSourcePath,
        bool restartRequired,
        DateTimeOffset installedAtUtcOverride,
        DateTimeOffset updatedAtUtc)
    {
        Directory.CreateDirectory(storePaths.ReceiptsRoot);
        ComputerUseWinInstallReceipt receipt = new(
            StateFormatVersion,
            mode == ComputerUseWinInstallMode.Codex ? CodexReceiptMode : RuntimeOnlyReceiptMode,
            PluginId,
            pluginVersion,
            runtimeVersion,
            runtimeRid,
            runtimeRoot,
            pluginSourceRoot,
            marketplacePath,
            marketplaceName,
            marketplaceSourcePath,
            restartRequired,
            installedAtUtcOverride,
            updatedAtUtc);

        string receiptPath = storePaths.GetReceiptPath(mode);
        string tempPath = receiptPath + ".tmp-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(tempPath, JsonSerializer.Serialize(receipt, JsonOptions));
        if (File.Exists(receiptPath))
        {
            File.Delete(receiptPath);
        }

        File.Move(tempPath, receiptPath);
        return receipt;
    }

    private ComputerUseWinInstallReceipt? TryReadReceipt(ComputerUseWinInstallMode mode)
    {
        string receiptPath = storePaths.GetReceiptPath(mode);
        if (!File.Exists(receiptPath))
        {
            return null;
        }

        return JsonSerializer.Deserialize<ComputerUseWinInstallReceipt>(File.ReadAllText(receiptPath), JsonOptions);
    }

    private void DeleteReceipt(ComputerUseWinInstallMode mode)
    {
        string receiptPath = storePaths.GetReceiptPath(mode);
        if (File.Exists(receiptPath))
        {
            File.Delete(receiptPath);
        }
    }

    private void RemoveOwnedCodexConfigSections()
    {
        string configPath = Path.Combine(storePaths.CodexHome, "config.toml");
        if (!File.Exists(configPath))
        {
            return;
        }

        string configText = File.ReadAllText(configPath);
        if (CodexConfigTomlSectionRewriter.TryRemoveOwnedSections(
            configText,
            OwnedCodexConfigSectionPaths,
            out string rewrittenText))
        {
            File.WriteAllText(configPath, rewrittenText);
        }
    }

    private string BuildRuntimeOnlySnippet()
    {
        string escapedLauncherPath = storePaths.RuntimeLauncherScriptPath.Replace("\\", "\\\\");
        return $$"""
{
  "mcpServers": {
    "computer-use-win": {
      "command": "{{RuntimeOnlyLauncherCommand}}",
      "args": ["-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", "{{escapedLauncherPath}}", "--tool-surface-profile", "computer-use-win"]
    }
  }
}
""";
    }

    private static string ResolveUserProfileRoot()
    {
        string? userProfile = Environment.GetEnvironmentVariable("USERPROFILE");
        if (string.IsNullOrWhiteSpace(userProfile))
        {
            userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        if (string.IsNullOrWhiteSpace(userProfile))
        {
            throw new InvalidOperationException("USERPROFILE is not available for marketplace resolution.");
        }

        return Path.GetFullPath(userProfile);
    }

    private static string ReadSha256FromChecksumFile(Uri checksumUri, string assetName)
    {
        string content = ReadAllTextFromUri(checksumUri);
        foreach (string rawLine in content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            if (line.EndsWith("*" + assetName, StringComparison.Ordinal))
            {
                return line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)[0].Trim();
            }
        }

        throw new InvalidOperationException($"Checksum file '{checksumUri}' does not contain an entry for '{assetName}'.");
    }

    private static string ReadAllTextFromUri(Uri uri)
    {
        if (uri.IsFile)
        {
            return File.ReadAllText(uri.LocalPath);
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unsupported URI scheme '{uri.Scheme}'.");
        }

        using Stream responseStream = HttpClient.GetStreamAsync(uri).GetAwaiter().GetResult();
        using StreamReader reader = new(responseStream);
        return reader.ReadToEnd();
    }

    private static void SaveUriToPath(Uri uri, string destinationPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        if (uri.IsFile)
        {
            File.Copy(uri.LocalPath, destinationPath, overwrite: true);
            return;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unsupported URI scheme '{uri.Scheme}'.");
        }

        using HttpResponseMessage response = HttpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
        using Stream responseStream = response.Content.ReadAsStream();
        using FileStream fileStream = File.Create(destinationPath);
        responseStream.CopyTo(fileStream);
    }

    private static string ComputeFileSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void CopyDirectory(string sourceRoot, string destinationRoot)
    {
        foreach (string sourcePath in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourceRoot, sourcePath);
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

    private sealed record PersonalMarketplace(
        string Name,
        PersonalMarketplaceInterface Interface,
        List<PersonalMarketplacePluginEntry> Plugins);

    private sealed record PersonalMarketplaceInterface(string DisplayName);

    private sealed record PersonalMarketplacePluginEntry(
        string Name,
        PersonalMarketplacePluginSource Source,
        PersonalMarketplacePluginPolicy Policy,
        string Category);

    private sealed record PersonalMarketplacePluginSource(string Source, string Path);

    private sealed record PersonalMarketplacePluginPolicy(string Installation, string Authentication);

    private sealed record ComputerUseWinPluginReleaseAsset(
        string AssetName,
        Uri ArchiveUri,
        Uri ChecksumUri,
        string Sha256,
        ComputerUseWinPluginBundleManifest BundleManifest);
}
