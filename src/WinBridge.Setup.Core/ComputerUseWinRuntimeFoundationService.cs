// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Globalization;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace WinBridge.Setup.Core;

public sealed class ComputerUseWinRuntimeFoundationService
{
    private const int StateFormatVersion = 1;
    private const string DefaultServerExeRelativePath = "Okno.Server.exe";
    private const string DefaultBundleManifestName = "okno-runtime-bundle-manifest.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private static readonly HttpClient HttpClient = new();

    private readonly ComputerUseWinRuntimeStorePaths storePaths;

    public ComputerUseWinRuntimeFoundationService()
        : this(new ComputerUseWinRuntimeStorePaths(ResolveCodexHome(), ResolveLocalAppDataRoot()))
    {
    }

    public ComputerUseWinRuntimeFoundationService(ComputerUseWinRuntimeStorePaths storePaths)
    {
        this.storePaths = storePaths;
    }

    public ComputerUseWinRuntimeStorePaths StorePaths => storePaths;

    public ComputerUseWinRuntimeInstallResult InstallRuntime(string? descriptorPathOverride = null)
    {
        ComputerUseWinRuntimeReleaseDescriptor descriptor = LoadRuntimeReleaseDescriptor(descriptorPathOverride);
        using FileStream lockStream = AcquireLock(storePaths.GetRidLockPath(descriptor.Rid));
        string runtimeRoot = EnsureInstalledRuntimeFromDescriptor(descriptor);
        ComputerUseWinInstalledRuntimeState state = WriteCurrentState(descriptor, runtimeRoot);
        return ToInstallResult("install", state);
    }

    public ComputerUseWinRuntimeStatus GetRuntimeStatus(string? descriptorPathOverride = null)
    {
        ComputerUseWinRuntimeReleaseDescriptor? descriptor = TryResolveDescriptor(descriptorPathOverride);
        return BuildStatus(descriptor);
    }

    public ComputerUseWinRuntimeStatus VerifyRuntime(string? descriptorPathOverride = null)
    {
        ComputerUseWinRuntimeReleaseDescriptor? descriptor = TryResolveDescriptor(descriptorPathOverride);
        return BuildStatus(descriptor);
    }

    public ComputerUseWinRuntimeInstallResult RepairRuntime(string? descriptorPathOverride = null)
    {
        ComputerUseWinRuntimeReleaseDescriptor descriptor = LoadRuntimeReleaseDescriptor(descriptorPathOverride);
        using FileStream lockStream = AcquireLock(storePaths.GetRidLockPath(descriptor.Rid));
        string runtimeRoot = ForceReinstallRuntimeFromDescriptor(descriptor);
        ComputerUseWinInstalledRuntimeState state = WriteCurrentState(descriptor, runtimeRoot);
        return ToInstallResult("repair", state);
    }

    public static string ResolveCodexHome()
    {
        string? explicitCodexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (!string.IsNullOrWhiteSpace(explicitCodexHome))
        {
            return Path.GetFullPath(explicitCodexHome);
        }

        string? userProfile = Environment.GetEnvironmentVariable("USERPROFILE");
        if (string.IsNullOrWhiteSpace(userProfile))
        {
            userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        if (string.IsNullOrWhiteSpace(userProfile))
        {
            throw new InvalidOperationException("Neither CODEX_HOME nor USERPROFILE is available for runtime store resolution.");
        }

        return Path.Combine(userProfile, ".codex");
    }

    public static string ResolveLocalAppDataRoot()
    {
        string? explicitLocalAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        if (!string.IsNullOrWhiteSpace(explicitLocalAppData))
        {
            return Path.GetFullPath(explicitLocalAppData);
        }

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            return Path.GetFullPath(localAppData);
        }

        string? userProfile = Environment.GetEnvironmentVariable("USERPROFILE");
        if (string.IsNullOrWhiteSpace(userProfile))
        {
            userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        if (string.IsNullOrWhiteSpace(userProfile))
        {
            throw new InvalidOperationException("Neither LOCALAPPDATA nor USERPROFILE is available for runtime store resolution.");
        }

        return Path.Combine(Path.GetFullPath(userProfile), "AppData", "Local");
    }

    private ComputerUseWinRuntimeStatus BuildStatus(ComputerUseWinRuntimeReleaseDescriptor? descriptor)
    {
        if (!File.Exists(storePaths.CurrentStatePath))
        {
            return new ComputerUseWinRuntimeStatus(
                StateFormatVersion,
                storePaths.CodexHome,
                storePaths.RuntimeStoreRoot,
                storePaths.CurrentStatePath,
                false,
                false,
                false,
                null,
                "current_state_missing",
                null);
        }

        if (!TryReadCurrentState(out ComputerUseWinInstalledRuntimeState? state, out string? stateFailureReason))
        {
            return new ComputerUseWinRuntimeStatus(
                StateFormatVersion,
                storePaths.CodexHome,
                storePaths.RuntimeStoreRoot,
                storePaths.CurrentStatePath,
                true,
                false,
                false,
                null,
                stateFailureReason,
                null);
        }

        ComputerUseWinInstalledRuntimeState currentState = state!;
        if (!IsCanonicalRuntimeRoot(currentState))
        {
            return new ComputerUseWinRuntimeStatus(
                StateFormatVersion,
                storePaths.CodexHome,
                storePaths.RuntimeStoreRoot,
                storePaths.CurrentStatePath,
                true,
                false,
                false,
                null,
                "runtime_root_noncanonical",
                currentState);
        }

        if (!TryValidateRuntimeRoot(
                currentState.RuntimeRoot,
                descriptor?.ServerExeRelativePath ?? DefaultServerExeRelativePath,
                descriptor?.BundleManifestName ?? DefaultBundleManifestName,
                out string validationFailureReason))
        {
            return new ComputerUseWinRuntimeStatus(
                StateFormatVersion,
                storePaths.CodexHome,
                storePaths.RuntimeStoreRoot,
                storePaths.CurrentStatePath,
                true,
                false,
                false,
                null,
                validationFailureReason,
                currentState);
        }

        string? compatibilityFailureReason = null;
        bool isCompatible = descriptor is null || MatchesDescriptor(currentState, descriptor, out compatibilityFailureReason);
        return new ComputerUseWinRuntimeStatus(
            StateFormatVersion,
            storePaths.CodexHome,
            storePaths.RuntimeStoreRoot,
            storePaths.CurrentStatePath,
            true,
            true,
            isCompatible,
            isCompatible ? currentState.RuntimeRoot : null,
            isCompatible ? null : compatibilityFailureReason,
            currentState);
    }

    private ComputerUseWinRuntimeInstallResult ToInstallResult(string action, ComputerUseWinInstalledRuntimeState state)
    {
        return new ComputerUseWinRuntimeInstallResult(
            StateFormatVersion,
            action,
            storePaths.CodexHome,
            storePaths.RuntimeStoreRoot,
            storePaths.CurrentStatePath,
            state.RuntimeRoot,
            state.Rid,
            state.Version,
            state.RuntimeAssetName,
            state.RuntimeTag,
            state.RuntimeSha256,
            state.InstalledAtUtc);
    }

    public static ComputerUseWinRuntimeReleaseDescriptor LoadRuntimeReleaseDescriptor(string? descriptorPathOverride = null)
    {
        return TryResolveDescriptor(descriptorPathOverride)
            ?? throw new InvalidOperationException("Unable to resolve runtime release descriptor.");
    }

    private static ComputerUseWinRuntimeReleaseDescriptor? TryResolveDescriptor(string? descriptorPathOverride)
    {
        string? path = ResolveDescriptorPath(descriptorPathOverride);
        if (path is null)
        {
            return null;
        }

        return ReadDescriptor(path);
    }

    private static string? ResolveDescriptorPath(string? descriptorPathOverride)
    {
        string? explicitOverride = descriptorPathOverride;
        if (string.IsNullOrWhiteSpace(explicitOverride))
        {
            explicitOverride = Environment.GetEnvironmentVariable("COMPUTER_USE_WIN_RUNTIME_RELEASE_DESCRIPTOR_OVERRIDE");
        }

        if (!string.IsNullOrWhiteSpace(explicitOverride))
        {
            string fullPath = Path.GetFullPath(explicitOverride);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException($"Runtime release descriptor not found: {fullPath}", fullPath);
            }

            return fullPath;
        }

        string baseDirectory = AppContext.BaseDirectory;
        string localDescriptorPath = Path.Combine(baseDirectory, "runtime-release.json");
        if (File.Exists(localDescriptorPath))
        {
            return localDescriptorPath;
        }

        DirectoryInfo? current = new(baseDirectory);
        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, "plugins", "computer-use-win", "runtime-release.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return null;
    }

    private static ComputerUseWinRuntimeReleaseDescriptor ReadDescriptor(string descriptorPath)
    {
        ComputerUseWinRuntimeReleaseDescriptor descriptor = JsonSerializer.Deserialize<ComputerUseWinRuntimeReleaseDescriptor>(
                File.ReadAllText(descriptorPath),
                JsonOptions)
            ?? throw new InvalidOperationException($"Runtime release descriptor '{descriptorPath}' is empty.");

        if (descriptor.FormatVersion != 1)
        {
            throw new InvalidOperationException($"Unsupported runtime release descriptor version '{descriptor.FormatVersion}'.");
        }

        EnsureRequired(descriptor.Version, descriptorPath, "version");
        EnsureRequired(descriptor.Rid, descriptorPath, "rid");
        EnsureRequired(descriptor.Tag, descriptorPath, "tag");
        EnsureRequired(descriptor.AssetName, descriptorPath, "assetName");
        EnsureRequired(descriptor.DownloadUrl, descriptorPath, "downloadUrl");
        EnsureRequired(descriptor.Sha256, descriptorPath, "sha256");
        EnsureRequired(descriptor.ServerExeRelativePath, descriptorPath, "serverExeRelativePath");
        EnsureRequired(descriptor.BundleManifestName, descriptorPath, "bundleManifestName");

        if (string.Equals(descriptor.Sha256, "REPLACE_ON_RELEASE", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Runtime release descriptor '{descriptorPath}' is not finalized yet.");
        }

        return descriptor;
    }

    private static void EnsureRequired(string value, string descriptorPath, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Runtime release descriptor '{descriptorPath}' is missing required field '{fieldName}'.");
        }
    }

    private bool TryReadCurrentState(out ComputerUseWinInstalledRuntimeState? state, out string? failureReason)
    {
        try
        {
            state = JsonSerializer.Deserialize<ComputerUseWinInstalledRuntimeState>(
                File.ReadAllText(storePaths.CurrentStatePath),
                JsonOptions);
            if (state is null)
            {
                failureReason = "current_state_invalid";
                return false;
            }

            if (state.FormatVersion != StateFormatVersion
                || string.IsNullOrWhiteSpace(state.Rid)
                || string.IsNullOrWhiteSpace(state.Version)
                || string.IsNullOrWhiteSpace(state.RuntimeRoot)
                || string.IsNullOrWhiteSpace(state.RuntimeAssetName)
                || string.IsNullOrWhiteSpace(state.RuntimeTag)
                || string.IsNullOrWhiteSpace(state.RuntimeSha256))
            {
                failureReason = "current_state_invalid";
                return false;
            }

            failureReason = null;
            return true;
        }
        catch (Exception)
        {
            state = null;
            failureReason = "current_state_invalid";
            return false;
        }
    }

    private bool IsCanonicalRuntimeRoot(ComputerUseWinInstalledRuntimeState state)
    {
        string expectedRoot = Path.GetFullPath(storePaths.GetRuntimeVersionRoot(state.Rid, state.Version));
        string actualRoot = Path.GetFullPath(state.RuntimeRoot);
        return string.Equals(expectedRoot, actualRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesDescriptor(
        ComputerUseWinInstalledRuntimeState state,
        ComputerUseWinRuntimeReleaseDescriptor descriptor,
        out string? failureReason)
    {
        if (!string.Equals(state.Rid, descriptor.Rid, StringComparison.Ordinal))
        {
            failureReason = "descriptor_rid_mismatch";
            return false;
        }

        if (!string.Equals(state.Version, descriptor.Version, StringComparison.Ordinal))
        {
            failureReason = "descriptor_version_mismatch";
            return false;
        }

        if (!string.Equals(state.RuntimeTag, descriptor.Tag, StringComparison.Ordinal))
        {
            failureReason = "descriptor_tag_mismatch";
            return false;
        }

        if (!string.Equals(state.RuntimeAssetName, descriptor.AssetName, StringComparison.Ordinal))
        {
            failureReason = "descriptor_asset_name_mismatch";
            return false;
        }

        if (!string.Equals(state.RuntimeSha256, descriptor.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            failureReason = "descriptor_sha256_mismatch";
            return false;
        }

        failureReason = null;
        return true;
    }

    private string EnsureInstalledRuntimeFromDescriptor(ComputerUseWinRuntimeReleaseDescriptor descriptor)
    {
        string versionRoot = storePaths.GetRuntimeVersionRoot(descriptor.Rid, descriptor.Version);
        if (TryValidateRuntimeRoot(versionRoot, descriptor.ServerExeRelativePath, descriptor.BundleManifestName, out _))
        {
            return versionRoot;
        }

        return ForceReinstallRuntimeFromDescriptor(descriptor);
    }

    private string ForceReinstallRuntimeFromDescriptor(ComputerUseWinRuntimeReleaseDescriptor descriptor)
    {
        string versionRoot = storePaths.GetRuntimeVersionRoot(descriptor.Rid, descriptor.Version);
        string ridRoot = Path.Combine(storePaths.RuntimesRoot, descriptor.Rid);
        string assetExtension = Path.GetExtension(descriptor.AssetName);
        string downloadFileName = Path.GetFileNameWithoutExtension(descriptor.AssetName) + ".download" + assetExtension;
        string downloadPath = Path.Combine(storePaths.LocksRoot, downloadFileName);
        string stagingRoot = Path.Combine(ridRoot, descriptor.Version + ".install-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(storePaths.LocksRoot);
        Directory.CreateDirectory(ridRoot);

        if (!string.Equals(assetExtension, ".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Runtime release asset '{descriptor.AssetName}' must be a .zip archive.");
        }

        DeleteDirectoryIfExists(stagingRoot);
        DeleteFileIfExists(downloadPath);

        try
        {
            SaveRemoteAssetToPath(descriptor.DownloadUrl, downloadPath);
            string actualSha256 = ComputeFileSha256(downloadPath);
            if (!string.Equals(actualSha256, descriptor.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"SHA256 mismatch for runtime release asset '{descriptor.AssetName}'. Expected '{descriptor.Sha256}', actual '{actualSha256}'.");
            }

            Directory.CreateDirectory(stagingRoot);
            ZipFile.ExtractToDirectory(downloadPath, stagingRoot);
            if (!TryValidateRuntimeRoot(stagingRoot, descriptor.ServerExeRelativePath, descriptor.BundleManifestName, out string failureReason))
            {
                throw new InvalidOperationException($"Installed runtime bundle is invalid: {failureReason}.");
            }

            DeleteDirectoryIfExists(versionRoot);
            MoveDirectory(stagingRoot, versionRoot);
            return versionRoot;
        }
        finally
        {
            DeleteFileIfExists(downloadPath);
            DeleteDirectoryIfExists(stagingRoot);
        }
    }

    private ComputerUseWinInstalledRuntimeState WriteCurrentState(ComputerUseWinRuntimeReleaseDescriptor descriptor, string runtimeRoot)
    {
        Directory.CreateDirectory(storePaths.StateRoot);
        ComputerUseWinInstalledRuntimeState state = new(
            StateFormatVersion,
            descriptor.Rid,
            descriptor.Version,
            Path.GetFullPath(runtimeRoot),
            descriptor.AssetName,
            descriptor.Tag,
            descriptor.Sha256.ToLowerInvariant(),
            DateTimeOffset.UtcNow);

        string tempStatePath = storePaths.CurrentStatePath + ".tmp-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(tempStatePath, JsonSerializer.Serialize(state, JsonOptions));
        if (File.Exists(storePaths.CurrentStatePath))
        {
            File.Delete(storePaths.CurrentStatePath);
        }

        File.Move(tempStatePath, storePaths.CurrentStatePath);
        return state;
    }

    private static bool TryValidateRuntimeRoot(
        string runtimeRoot,
        string serverExeRelativePath,
        string bundleManifestName,
        out string failureReason)
    {
        if (!Directory.Exists(runtimeRoot))
        {
            failureReason = "runtime_root_missing";
            return false;
        }

        string serverExePath = Path.Combine(runtimeRoot, serverExeRelativePath);
        if (!File.Exists(serverExePath))
        {
            failureReason = "server_executable_missing";
            return false;
        }

        string manifestPath = Path.Combine(runtimeRoot, bundleManifestName);
        if (!File.Exists(manifestPath))
        {
            failureReason = "runtime_manifest_missing";
            return false;
        }

        ComputerUseWinRuntimeBundleManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<ComputerUseWinRuntimeBundleManifest>(File.ReadAllText(manifestPath), JsonOptions);
        }
        catch (Exception)
        {
            failureReason = "runtime_manifest_invalid";
            return false;
        }

        if (manifest is null || manifest.FormatVersion != 1)
        {
            failureReason = "runtime_manifest_invalid";
            return false;
        }

        Dictionary<string, long> expectedEntries = manifest.Files.ToDictionary(
            static entry => entry.Path,
            static entry => entry.Size,
            StringComparer.Ordinal);

        foreach (string filePath in Directory.EnumerateFiles(runtimeRoot, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(runtimeRoot, filePath);
            if (string.Equals(relativePath, bundleManifestName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!expectedEntries.Remove(relativePath, out long expectedSize))
            {
                failureReason = "runtime_manifest_unexpected_file";
                return false;
            }

            long actualSize = new FileInfo(filePath).Length;
            if (actualSize != expectedSize)
            {
                failureReason = "runtime_manifest_size_drift";
                return false;
            }
        }

        if (expectedEntries.Count > 0)
        {
            failureReason = "runtime_manifest_incomplete";
            return false;
        }

        failureReason = string.Empty;
        return true;
    }

    private static FileStream AcquireLock(string lockPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (true)
        {
            try
            {
                return File.Open(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (DateTimeOffset.UtcNow < deadline)
            {
                Thread.Sleep(250);
            }
        }
    }

    private static void SaveRemoteAssetToPath(string sourceUrl, string destinationPath)
    {
        Uri uri = new(sourceUrl);
        if (uri.IsFile)
        {
            File.Copy(uri.LocalPath, destinationPath, overwrite: true);
            return;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unsupported downloadUrl scheme '{uri.Scheme}'.");
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
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void DeleteFileIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
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

    private static void MoveDirectory(string sourceRoot, string destinationRoot)
    {
        if (Directory.Exists(destinationRoot))
        {
            DeleteDirectoryIfExists(destinationRoot);
        }

        Directory.Move(sourceRoot, destinationRoot);
    }
}
