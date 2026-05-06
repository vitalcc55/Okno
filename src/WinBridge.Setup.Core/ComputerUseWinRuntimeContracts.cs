// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json.Serialization;

namespace WinBridge.Setup.Core;

public sealed record ComputerUseWinRuntimeReleaseDescriptor(
    [property: JsonPropertyName("formatVersion")] int FormatVersion,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("rid")] string Rid,
    [property: JsonPropertyName("tag")] string Tag,
    [property: JsonPropertyName("assetName")] string AssetName,
    [property: JsonPropertyName("downloadUrl")] string DownloadUrl,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("serverExeRelativePath")] string ServerExeRelativePath,
    [property: JsonPropertyName("bundleManifestName")] string BundleManifestName);

public sealed record ComputerUseWinRuntimeBundleManifest(
    [property: JsonPropertyName("formatVersion")] int FormatVersion,
    [property: JsonPropertyName("files")] IReadOnlyList<ComputerUseWinRuntimeBundleManifestFile> Files);

public sealed record ComputerUseWinRuntimeBundleManifestFile(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("size")] long Size);

public sealed record ComputerUseWinPluginBundleManifest(
    [property: JsonPropertyName("formatVersion")] int FormatVersion,
    [property: JsonPropertyName("pluginId")] string PluginId,
    [property: JsonPropertyName("pluginVersion")] string PluginVersion,
    [property: JsonPropertyName("runtimeVersion")] string RuntimeVersion,
    [property: JsonPropertyName("runtimeRid")] string RuntimeRid,
    [property: JsonPropertyName("runtimeTag")] string RuntimeTag,
    [property: JsonPropertyName("runtimeAssetName")] string RuntimeAssetName,
    [property: JsonPropertyName("files")] IReadOnlyList<ComputerUseWinRuntimeBundleManifestFile> Files);

public enum ComputerUseWinInstallMode
{
    RuntimeOnly,
    Codex,
}

public sealed record ComputerUseWinInstalledRuntimeState(
    [property: JsonPropertyName("formatVersion")] int FormatVersion,
    [property: JsonPropertyName("rid")] string Rid,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("runtimeRoot")] string RuntimeRoot,
    [property: JsonPropertyName("runtimeAssetName")] string RuntimeAssetName,
    [property: JsonPropertyName("runtimeTag")] string RuntimeTag,
    [property: JsonPropertyName("runtimeSha256")] string RuntimeSha256,
    [property: JsonPropertyName("installedAtUtc")] DateTimeOffset InstalledAtUtc);

public sealed record ComputerUseWinInstallReceipt(
    [property: JsonPropertyName("formatVersion")] int FormatVersion,
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("pluginId")] string PluginId,
    [property: JsonPropertyName("pluginVersion")] string? PluginVersion,
    [property: JsonPropertyName("runtimeVersion")] string RuntimeVersion,
    [property: JsonPropertyName("runtimeRid")] string RuntimeRid,
    [property: JsonPropertyName("runtimeRoot")] string RuntimeRoot,
    [property: JsonPropertyName("pluginSourceRoot")] string? PluginSourceRoot,
    [property: JsonPropertyName("marketplacePath")] string? MarketplacePath,
    [property: JsonPropertyName("marketplaceName")] string? MarketplaceName,
    [property: JsonPropertyName("marketplaceSourcePath")] string? MarketplaceSourcePath,
    [property: JsonPropertyName("restartRequired")] bool RestartRequired,
    [property: JsonPropertyName("installedAtUtc")] DateTimeOffset InstalledAtUtc,
    [property: JsonPropertyName("updatedAtUtc")] DateTimeOffset UpdatedAtUtc);

public sealed record ComputerUseWinRuntimeStatus(
    [property: JsonPropertyName("formatVersion")] int FormatVersion,
    [property: JsonPropertyName("codexHome")] string CodexHome,
    [property: JsonPropertyName("runtimeStoreRoot")] string RuntimeStoreRoot,
    [property: JsonPropertyName("currentStatePath")] string CurrentStatePath,
    [property: JsonPropertyName("isInstalled")] bool IsInstalled,
    [property: JsonPropertyName("isUsable")] bool IsUsable,
    [property: JsonPropertyName("isCompatible")] bool IsCompatible,
    [property: JsonPropertyName("effectiveRuntimeRoot")] string? EffectiveRuntimeRoot,
    [property: JsonPropertyName("failureReason")] string? FailureReason,
    [property: JsonPropertyName("currentRuntime")] ComputerUseWinInstalledRuntimeState? CurrentRuntime);

public sealed record ComputerUseWinRuntimeInstallResult(
    [property: JsonPropertyName("formatVersion")] int FormatVersion,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("codexHome")] string CodexHome,
    [property: JsonPropertyName("runtimeStoreRoot")] string RuntimeStoreRoot,
    [property: JsonPropertyName("currentStatePath")] string CurrentStatePath,
    [property: JsonPropertyName("runtimeRoot")] string RuntimeRoot,
    [property: JsonPropertyName("rid")] string Rid,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("runtimeAssetName")] string RuntimeAssetName,
    [property: JsonPropertyName("runtimeTag")] string RuntimeTag,
    [property: JsonPropertyName("runtimeSha256")] string RuntimeSha256,
    [property: JsonPropertyName("installedAtUtc")] DateTimeOffset InstalledAtUtc);

public sealed record ComputerUseWinInstallerResult(
    [property: JsonPropertyName("formatVersion")] int FormatVersion,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("codexHome")] string CodexHome,
    [property: JsonPropertyName("runtimeStoreRoot")] string RuntimeStoreRoot,
    [property: JsonPropertyName("runtimeRoot")] string RuntimeRoot,
    [property: JsonPropertyName("runtimeVersion")] string RuntimeVersion,
    [property: JsonPropertyName("runtimeRid")] string RuntimeRid,
    [property: JsonPropertyName("pluginSourceRoot")] string? PluginSourceRoot,
    [property: JsonPropertyName("marketplacePath")] string? MarketplacePath,
    [property: JsonPropertyName("marketplaceName")] string? MarketplaceName,
    [property: JsonPropertyName("marketplaceSourcePath")] string? MarketplaceSourcePath,
    [property: JsonPropertyName("restartRequired")] bool RestartRequired,
    [property: JsonPropertyName("snippet")] string? Snippet,
    [property: JsonPropertyName("receiptPath")] string ReceiptPath,
    [property: JsonPropertyName("installedAtUtc")] DateTimeOffset InstalledAtUtc,
    [property: JsonPropertyName("updatedAtUtc")] DateTimeOffset UpdatedAtUtc);

public sealed record ComputerUseWinInstallerStatus(
    [property: JsonPropertyName("formatVersion")] int FormatVersion,
    [property: JsonPropertyName("codexHome")] string CodexHome,
    [property: JsonPropertyName("runtimeStoreRoot")] string RuntimeStoreRoot,
    [property: JsonPropertyName("runtimeStatus")] ComputerUseWinRuntimeStatus RuntimeStatus,
    [property: JsonPropertyName("runtimeOnlyInstall")] ComputerUseWinInstallReceipt? RuntimeOnlyInstall,
    [property: JsonPropertyName("codexInstall")] ComputerUseWinInstallReceipt? CodexInstall);
