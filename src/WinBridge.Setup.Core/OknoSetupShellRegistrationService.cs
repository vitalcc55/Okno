// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Diagnostics;
using System.Reflection;
using Microsoft.Win32;

namespace WinBridge.Setup.Core;

public sealed class OknoSetupShellRegistrationService
{
    private const string CurrentShellDirectoryName = "current";
    private const string ShellExecutableName = "Okno Setup.exe";
    private const string DefaultDisplayName = "Okno";
    private const string DefaultPublisher = "Vlasov Vitaly";

    private readonly OknoSetupShellRegistrationOptions options;

    public OknoSetupShellRegistrationService()
        : this(CreateDefaultOptions())
    {
    }

    public OknoSetupShellRegistrationService(OknoSetupShellRegistrationOptions options)
    {
        this.options = options;
    }

    public string ShellRoot => options.ShellRoot;

    public string CurrentShellRoot => Path.GetDirectoryName(options.ShellExecutablePath)
        ?? throw new InvalidOperationException($"Shell executable path '{options.ShellExecutablePath}' does not have a parent directory.");

    public string ShellExecutablePath => options.ShellExecutablePath;

    public string ShortcutPath => options.ShortcutPath;

    public string UninstallRegistryKeyPath => options.UninstallRegistryKeyPath;

    public void RegisterShell(string sourceRoot, string displayVersion, string? currentExecutablePathOverride = null)
    {
        string resolvedSourceRoot = Path.GetFullPath(sourceRoot);
        string currentExecutablePath = string.IsNullOrWhiteSpace(currentExecutablePathOverride)
            ? Environment.ProcessPath ?? Path.Combine(resolvedSourceRoot, ShellExecutableName)
            : Path.GetFullPath(currentExecutablePathOverride);
        string currentSourceExecutable = Path.Combine(resolvedSourceRoot, ShellExecutableName);
        if (!File.Exists(currentSourceExecutable))
        {
            throw new InvalidOperationException($"Setup shell source root '{resolvedSourceRoot}' does not contain '{ShellExecutableName}'.");
        }

        Directory.CreateDirectory(options.ShellRoot);
        if (!PathsEqual(resolvedSourceRoot, CurrentShellRoot))
        {
            string stagingRoot = Path.Combine(options.ShellRoot, "staging-" + Guid.NewGuid().ToString("N"));
            string backupRoot = Path.Combine(options.ShellRoot, "backup-" + Guid.NewGuid().ToString("N"));
            try
            {
                options.CopyDirectoryContents(resolvedSourceRoot, stagingRoot);
                if (!File.Exists(Path.Combine(stagingRoot, ShellExecutableName)))
                {
                    throw new InvalidOperationException($"Staged setup shell '{stagingRoot}' does not contain '{ShellExecutableName}'.");
                }

                if (Directory.Exists(CurrentShellRoot))
                {
                    Directory.Move(CurrentShellRoot, backupRoot);
                }

                Directory.Move(stagingRoot, CurrentShellRoot);
                options.DeleteDirectory(backupRoot);
            }
            catch
            {
                if (!Directory.Exists(CurrentShellRoot) && Directory.Exists(backupRoot))
                {
                    Directory.Move(backupRoot, CurrentShellRoot);
                }

                throw;
            }
            finally
            {
                options.DeleteDirectory(stagingRoot);
                options.DeleteDirectory(backupRoot);
            }
        }

        options.CreateShortcut(options.ShortcutPath, options.ShellExecutablePath);
        options.WriteRegistryValues(options.UninstallRegistryKeyPath, BuildRegistryValues(displayVersion, currentExecutablePath));
    }

    public void UnregisterShell()
    {
        options.DeleteRegistryKey(options.UninstallRegistryKeyPath);
        if (File.Exists(options.ShortcutPath))
        {
            File.Delete(options.ShortcutPath);
        }
    }

    public bool RemoveShellArtifacts(string? currentBaseDirectory = null, int? currentProcessId = null)
    {
        UnregisterShell();

        if (!Directory.Exists(options.ShellRoot))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(currentBaseDirectory)
            && currentProcessId is int processId
            && IsCurrentShellPath(currentBaseDirectory))
        {
            ScheduleDeferredCleanup(processId, options.ShellRoot);
            return true;
        }

        options.DeleteDirectory(options.ShellRoot);
        return false;
    }

    public bool IsCurrentShellPath(string baseDirectory)
    {
        return PathsEqual(baseDirectory, CurrentShellRoot);
    }

    public bool TryScheduleSelfCleanup(string currentBaseDirectory, int processId)
    {
        if (!IsCurrentShellPath(currentBaseDirectory))
        {
            return false;
        }

        ScheduleDeferredCleanup(processId, options.ShellRoot);
        return true;
    }

    public void ScheduleDeferredCleanup(int processId, string directoryPath)
    {
        Action<string>? cleanupAction = options.DeferredCleanupProcessFactory(processId);
        if (cleanupAction is null)
        {
            return;
        }

        cleanupAction(Path.GetFullPath(directoryPath));
    }

    public bool RegistryEntryExists() => options.RegistryKeyExists(options.UninstallRegistryKeyPath);

    public static Action<string> StartPowerShellCleanupHelper(int processId)
    {
        return directoryPath =>
        {
            string helperScriptPath = Path.Combine(Path.GetTempPath(), "okno-setup-cleanup-" + Guid.NewGuid().ToString("N") + ".ps1");
            string escapedDirectoryPath = EscapePowerShellSingleQuotedString(Path.GetFullPath(directoryPath));
            string script = $$"""
            $targetPid = {{processId}}
            $targetDirectory = '{{escapedDirectoryPath}}'
            try {
                $process = Get-Process -Id $targetPid -ErrorAction SilentlyContinue
                if ($null -ne $process) {
                    Wait-Process -Id $targetPid -Timeout 30 -ErrorAction SilentlyContinue
                }
            }
            catch {
            }
            Start-Sleep -Milliseconds 400
            if (Test-Path $targetDirectory -PathType Container) {
                Remove-Item -LiteralPath $targetDirectory -Recurse -Force -ErrorAction SilentlyContinue
            }
            Remove-Item -LiteralPath $MyInvocation.MyCommand.Path -Force -ErrorAction SilentlyContinue
            """;
            File.WriteAllText(helperScriptPath, script);

            ProcessStartInfo startInfo = new()
            {
                FileName = "powershell",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            startInfo.ArgumentList.Add("-NoLogo");
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(helperScriptPath);
            _ = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start deferred Okno setup cleanup helper.");
        };
    }

    private Dictionary<string, object> BuildRegistryValues(string displayVersion, string currentExecutablePath)
    {
        string installLocation = CurrentShellRoot;
        string executablePath = options.ShellExecutablePath;
        string uninstallCommand = QuoteForRegistry(executablePath) + " --operation remove-all";
        string quietUninstallCommand = uninstallCommand + " --quiet";

        return new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["DisplayName"] = options.DisplayName,
            ["Publisher"] = options.Publisher,
            ["DisplayVersion"] = displayVersion,
            ["InstallLocation"] = installLocation,
            ["DisplayIcon"] = executablePath,
            ["UninstallString"] = uninstallCommand,
            ["QuietUninstallString"] = quietUninstallCommand,
            ["NoModify"] = 1,
            ["NoRepair"] = 1,
        };
    }

    private static OknoSetupShellRegistrationOptions CreateDefaultOptions()
    {
        string localAppDataRoot = ComputerUseWinRuntimeFoundationService.ResolveLocalAppDataRoot();
        string appDataRoot = ResolveAppDataRoot();
        string shellRoot = Path.Combine(localAppDataRoot, "Okno", "setup-shell");
        string currentShellRoot = Path.Combine(shellRoot, CurrentShellDirectoryName);
        string shellExecutablePath = Path.Combine(currentShellRoot, ShellExecutableName);
        string shortcutPath = Path.Combine(appDataRoot, "Microsoft", "Windows", "Start Menu", "Programs", "Okno Setup.lnk");
        return new OknoSetupShellRegistrationOptions(
            shellRoot,
            shellExecutablePath,
            shortcutPath,
            @"HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\Okno",
            DefaultDisplayName,
            DefaultPublisher,
            StartPowerShellCleanupHelper,
            DefaultCopyDirectoryContents,
            DefaultCreateShortcut,
            DefaultWriteRegistryValues,
            DefaultDeleteRegistryKey,
            DefaultDeleteDirectory,
            DefaultRegistryKeyExists);
    }

    private static void DefaultCopyDirectoryContents(string sourceRoot, string destinationRoot)
    {
        string resolvedSourceRoot = Path.GetFullPath(sourceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        Directory.CreateDirectory(destinationRoot);
        foreach (string directory in Directory.EnumerateDirectories(resolvedSourceRoot, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(resolvedSourceRoot, directory);
            Directory.CreateDirectory(Path.Combine(destinationRoot, relative));
        }

        foreach (string filePath in Directory.EnumerateFiles(resolvedSourceRoot, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(resolvedSourceRoot, filePath);
            string destinationPath = Path.Combine(destinationRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(filePath, destinationPath, overwrite: true);
        }
    }

    private static void DefaultCreateShortcut(string shortcutPath, string targetPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);
        Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null)
        {
            throw new InvalidOperationException("WScript.Shell is not available for shortcut creation.");
        }

        dynamic shell = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("Failed to create WScript.Shell instance.");
        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = targetPath;
        shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath);
        shortcut.IconLocation = targetPath;
        shortcut.Save();
    }

    private static void DefaultWriteRegistryValues(string keyPath, IReadOnlyDictionary<string, object> values)
    {
        using RegistryKey key = OpenRegistryKeyForWrite(keyPath);
        foreach ((string name, object value) in values)
        {
            key.SetValue(name, value);
        }
    }

    private static void DefaultDeleteRegistryKey(string keyPath)
    {
        (RegistryKey root, string relativePath) = ResolveRegistryPath(keyPath);
        root.DeleteSubKeyTree(relativePath, throwOnMissingSubKey: false);
    }

    private static bool DefaultRegistryKeyExists(string keyPath)
    {
        (RegistryKey root, string relativePath) = ResolveRegistryPath(keyPath);
        using RegistryKey? key = root.OpenSubKey(relativePath);
        return key is not null;
    }

    private static RegistryKey OpenRegistryKeyForWrite(string keyPath)
    {
        (RegistryKey root, string relativePath) = ResolveRegistryPath(keyPath);
        return root.CreateSubKey(relativePath)
            ?? throw new InvalidOperationException($"Failed to create registry key '{keyPath}'.");
    }

    private static (RegistryKey Root, string RelativePath) ResolveRegistryPath(string keyPath)
    {
        if (keyPath.StartsWith(@"HKCU\", StringComparison.OrdinalIgnoreCase))
        {
            return (Registry.CurrentUser, keyPath[@"HKCU\".Length..]);
        }

        throw new InvalidOperationException($"Unsupported registry root in '{keyPath}'.");
    }

    private static void DefaultDeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static string QuoteForRegistry(string path)
    {
        return "\"" + path + "\"";
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveAppDataRoot()
    {
        string? explicitAppData = Environment.GetEnvironmentVariable("APPDATA");
        if (!string.IsNullOrWhiteSpace(explicitAppData))
        {
            return Path.GetFullPath(explicitAppData);
        }

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrWhiteSpace(appData))
        {
            return Path.GetFullPath(appData);
        }

        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfile))
        {
            throw new InvalidOperationException("Unable to resolve APPDATA for Okno setup shell registration.");
        }

        return Path.Combine(Path.GetFullPath(userProfile), "AppData", "Roaming");
    }

    private static string EscapePowerShellSingleQuotedString(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }
}
