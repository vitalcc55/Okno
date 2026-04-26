using System.Diagnostics;
using System.Text;
using System.Text.Json;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Tooling;

namespace WinBridge.Server.IntegrationTests;

public sealed class ComputerUseWinInstallSurfaceTests
{
    [Fact]
    public void PublishComputerUseWinPluginCreatesSelfContainedRuntimeBundle()
    {
        string repoRoot = GetRepositoryRoot();
        string scriptPath = GetPublishScriptPath(repoRoot);
        string runtimeRoot = Path.Combine(repoRoot, "plugins", "computer-use-win", "runtime", "win-x64");

        DeleteDirectoryIfExists(runtimeRoot);

        ScriptInvocationResult result = InvokePowerShellScript(
            scriptPath,
            repoRoot,
            _ => { });

        using JsonDocument payload = JsonDocument.Parse(result.Stdout);
        Assert.True(
            result.ExitCode == 0,
            $"Publish script failed. ExitCode={result.ExitCode}. stderr='{result.Stderr.Trim()}', stdout='{result.Stdout.Trim()}'.");
        Assert.True(File.Exists(Path.Combine(runtimeRoot, "Okno.Server.exe")));
        Assert.Equal(runtimeRoot, payload.RootElement.GetProperty("runtimeRoot").GetString());
    }

    [Fact]
    public void PublishComputerUseWinPluginRestoresPreviousRuntimeWhenPromoteFails()
    {
        string repoRoot = GetRepositoryRoot();
        string scriptPath = GetPublishTestScriptPath(repoRoot);
        string runtimeRoot = Path.Combine(repoRoot, "plugins", "computer-use-win", "runtime", "win-x64");
        string backupRoot = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "computer-use-win-runtime-backup", Guid.NewGuid().ToString("N"));

        try
        {
            EnsurePublishedRuntimeBundle(repoRoot, scriptPath, runtimeRoot);
            CopyDirectory(runtimeRoot, backupRoot, _ => true);

            ScriptInvocationResult result = InvokePowerShellScript(
                scriptPath,
                repoRoot,
                startInfo =>
                {
                    UsePreparedPublishSource(startInfo, backupRoot);
                    AddScriptSwitch(startInfo, "-FailAfterBackup");
                });

            Assert.NotEqual(0, result.ExitCode);
            AssertRuntimeBundleMatchesManifest(runtimeRoot);
        }
        finally
        {
            DeleteDirectoryIfExists(runtimeRoot);
            if (Directory.Exists(backupRoot))
            {
                CopyDirectory(backupRoot, runtimeRoot, _ => true);
            }

            DeleteDirectoryIfExists(backupRoot);
        }
    }

    [Fact]
    public void PublishComputerUseWinPluginPreservesBackupWhenRestoreFails()
    {
        string repoRoot = GetRepositoryRoot();
        string scriptPath = GetPublishTestScriptPath(repoRoot);
        string runtimeRoot = Path.Combine(repoRoot, "plugins", "computer-use-win", "runtime", "win-x64");
        string runtimeParent = Path.GetDirectoryName(runtimeRoot)!;
        string backupRoot = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "computer-use-win-runtime-backup-restore", Guid.NewGuid().ToString("N"));

        try
        {
            EnsurePublishedRuntimeBundle(repoRoot, scriptPath, runtimeRoot);

            if (Directory.Exists(runtimeRoot))
            {
                CopyDirectory(runtimeRoot, backupRoot, _ => true);
                DeleteDirectoryIfExists(runtimeRoot);
            }

            CopyDirectory(backupRoot, runtimeRoot, _ => true);

            ScriptInvocationResult result = InvokePowerShellScript(
                scriptPath,
                repoRoot,
                startInfo =>
                {
                    UsePreparedPublishSource(startInfo, backupRoot);
                    AddScriptSwitch(startInfo, "-FailAfterBackup");
                    AddScriptSwitch(startInfo, "-FailRestore");
                });

            Assert.NotEqual(0, result.ExitCode);
            Assert.True(File.Exists(Path.Combine(runtimeRoot, "Okno.Server.exe")));
            Assert.True(File.Exists(Path.Combine(runtimeRoot, "hostfxr.dll")));
            Assert.True(File.Exists(Path.Combine(runtimeRoot, "okno-runtime-bundle-manifest.json")));

        }
        finally
        {
            DeleteDirectoryIfExists(runtimeRoot);
            if (Directory.Exists(backupRoot))
            {
                CopyDirectory(backupRoot, runtimeRoot, _ => true);
            }

            DeleteDirectoryIfExists(backupRoot);
            foreach (string backupCandidate in Directory.Exists(runtimeParent)
                         ? Directory.GetDirectories(runtimeParent, "win-x64.backup-*", SearchOption.TopDirectoryOnly)
                         : [])
            {
                DeleteDirectoryIfExists(backupCandidate);
            }
        }
    }

    [Fact]
    public void PublishComputerUseWinPluginKeepsCanonicalRuntimeRunnableWhenRepairCopyFails()
    {
        string repoRoot = GetRepositoryRoot();
        string scriptPath = GetPublishTestScriptPath(repoRoot);
        string runtimeRoot = Path.Combine(repoRoot, "plugins", "computer-use-win", "runtime", "win-x64");
        string runtimeParent = Path.GetDirectoryName(runtimeRoot)!;
        string backupRoot = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "computer-use-win-runtime-backup-repair", Guid.NewGuid().ToString("N"));

        try
        {
            EnsurePublishedRuntimeBundle(repoRoot, scriptPath, runtimeRoot);

            if (Directory.Exists(runtimeRoot))
            {
                CopyDirectory(runtimeRoot, backupRoot, _ => true);
                DeleteDirectoryIfExists(runtimeRoot);
            }

            CopyDirectory(backupRoot, runtimeRoot, _ => true);

            ScriptInvocationResult result = InvokePowerShellScript(
                scriptPath,
                repoRoot,
                startInfo =>
                {
                    UsePreparedPublishSource(startInfo, backupRoot);
                    AddScriptSwitch(startInfo, "-FailAfterBackup");
                    AddScriptSwitch(startInfo, "-FailRestore");
                    AddScriptSwitch(startInfo, "-FailRepairCopyAfterServer");
                });

            Assert.NotEqual(0, result.ExitCode);
            AssertRuntimeBundleMatchesManifest(runtimeRoot);
        }
        finally
        {
            DeleteDirectoryIfExists(runtimeRoot);
            if (Directory.Exists(backupRoot))
            {
                CopyDirectory(backupRoot, runtimeRoot, _ => true);
            }

            DeleteDirectoryIfExists(backupRoot);
            foreach (string backupCandidate in Directory.Exists(runtimeParent)
                         ? Directory.GetDirectories(runtimeParent, "win-x64.backup-*", SearchOption.TopDirectoryOnly)
                         : [])
            {
                DeleteDirectoryIfExists(backupCandidate);
            }

            foreach (string repairCandidate in Directory.Exists(runtimeParent)
                         ? Directory.GetDirectories(runtimeParent, "win-x64.repair-*", SearchOption.TopDirectoryOnly)
                         : [])
            {
                DeleteDirectoryIfExists(repairCandidate);
            }
        }
    }

    [Fact]
    public void PublishComputerUseWinPluginRejectsIncompleteBackupRuntimeBundleDuringRepair()
    {
        string repoRoot = GetRepositoryRoot();
        string scriptPath = GetPublishTestScriptPath(repoRoot);
        string runtimeRoot = Path.Combine(repoRoot, "plugins", "computer-use-win", "runtime", "win-x64");
        string runtimeParent = Path.GetDirectoryName(runtimeRoot)!;
        string backupRoot = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "computer-use-win-runtime-backup-corrupt", Guid.NewGuid().ToString("N"));

        try
        {
            EnsurePublishedRuntimeBundle(repoRoot, scriptPath, runtimeRoot);
            CopyDirectory(runtimeRoot, backupRoot, _ => true);
            string dependencyPath = Path.Combine(runtimeRoot, "hostfxr.dll");
            Assert.True(File.Exists(dependencyPath));
            File.Delete(dependencyPath);

            ScriptInvocationResult result = InvokePowerShellScript(
                scriptPath,
                repoRoot,
                startInfo =>
                {
                    UsePreparedPublishSource(startInfo, backupRoot);
                    AddScriptSwitch(startInfo, "-FailAfterBackup");
                    AddScriptSwitch(startInfo, "-FailRestore");
                });

            Assert.NotEqual(0, result.ExitCode);
            Assert.False(Directory.Exists(runtimeRoot));
            Assert.NotEmpty(Directory.GetDirectories(runtimeParent, "win-x64.backup-*", SearchOption.TopDirectoryOnly));
        }
        finally
        {
            DeleteDirectoryIfExists(runtimeRoot);
            if (Directory.Exists(backupRoot))
            {
                CopyDirectory(backupRoot, runtimeRoot, _ => true);
            }

            DeleteDirectoryIfExists(backupRoot);
            foreach (string backupCandidate in Directory.Exists(runtimeParent)
                         ? Directory.GetDirectories(runtimeParent, "win-x64.backup-*", SearchOption.TopDirectoryOnly)
                         : [])
            {
                DeleteDirectoryIfExists(backupCandidate);
            }

            foreach (string repairCandidate in Directory.Exists(runtimeParent)
                         ? Directory.GetDirectories(runtimeParent, "win-x64.repair-*", SearchOption.TopDirectoryOnly)
                         : [])
            {
                DeleteDirectoryIfExists(repairCandidate);
            }
        }
    }

    [Fact]
    public void PublishComputerUseWinPluginDoesNotConsumeInvalidBackupBeforeRestoreValidation()
    {
        string repoRoot = GetRepositoryRoot();
        string scriptPath = GetPublishTestScriptPath(repoRoot);
        string runtimeRoot = Path.Combine(repoRoot, "plugins", "computer-use-win", "runtime", "win-x64");
        string runtimeParent = Path.GetDirectoryName(runtimeRoot)!;
        string backupRoot = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "computer-use-win-runtime-backup-invalid-restore", Guid.NewGuid().ToString("N"));

        try
        {
            EnsurePublishedRuntimeBundle(repoRoot, scriptPath, runtimeRoot);
            CopyDirectory(runtimeRoot, backupRoot, _ => true);
            string dependencyPath = Path.Combine(runtimeRoot, "hostfxr.dll");
            Assert.True(File.Exists(dependencyPath));
            File.Delete(dependencyPath);

            ScriptInvocationResult result = InvokePowerShellScript(
                scriptPath,
                repoRoot,
                startInfo =>
                {
                    UsePreparedPublishSource(startInfo, backupRoot);
                    AddScriptSwitch(startInfo, "-FailAfterBackup");
                });

            Assert.NotEqual(0, result.ExitCode);
            Assert.False(Directory.Exists(runtimeRoot));
            Assert.NotEmpty(Directory.GetDirectories(runtimeParent, "win-x64.backup-*", SearchOption.TopDirectoryOnly));
        }
        finally
        {
            DeleteDirectoryIfExists(runtimeRoot);
            if (Directory.Exists(backupRoot))
            {
                CopyDirectory(backupRoot, runtimeRoot, _ => true);
            }

            DeleteDirectoryIfExists(backupRoot);
            foreach (string backupCandidate in Directory.Exists(runtimeParent)
                         ? Directory.GetDirectories(runtimeParent, "win-x64.backup-*", SearchOption.TopDirectoryOnly)
                         : [])
            {
                DeleteDirectoryIfExists(backupCandidate);
            }

            foreach (string repairCandidate in Directory.Exists(runtimeParent)
                         ? Directory.GetDirectories(runtimeParent, "win-x64.repair-*", SearchOption.TopDirectoryOnly)
                         : [])
            {
                DeleteDirectoryIfExists(repairCandidate);
            }
        }
    }

    [Fact]
    public void PublishComputerUseWinPluginRejectsPreManifestRuntimeWithoutManifestProofWhenPromoteFails()
    {
        string repoRoot = GetRepositoryRoot();
        string scriptPath = GetPublishTestScriptPath(repoRoot);
        string runtimeRoot = Path.Combine(repoRoot, "plugins", "computer-use-win", "runtime", "win-x64");
        string runtimeParent = Path.GetDirectoryName(runtimeRoot)!;
        string backupRoot = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "computer-use-win-runtime-backup-legacy", Guid.NewGuid().ToString("N"));

        try
        {
            EnsurePublishedRuntimeBundle(repoRoot, scriptPath, runtimeRoot);
            CopyDirectory(runtimeRoot, backupRoot, _ => true);
            string manifestPath = Path.Combine(runtimeRoot, "okno-runtime-bundle-manifest.json");
            Assert.True(File.Exists(manifestPath));
            File.Delete(manifestPath);

            ScriptInvocationResult result = InvokePowerShellScript(
                scriptPath,
                repoRoot,
                startInfo =>
                {
                    UsePreparedPublishSource(startInfo, backupRoot);
                    AddScriptSwitch(startInfo, "-FailAfterBackup");
                });

            Assert.NotEqual(0, result.ExitCode);
            Assert.False(Directory.Exists(runtimeRoot));
            Assert.NotEmpty(Directory.GetDirectories(runtimeParent, "win-x64.backup-*", SearchOption.TopDirectoryOnly));
        }
        finally
        {
            DeleteDirectoryIfExists(runtimeRoot);
            if (Directory.Exists(backupRoot))
            {
                CopyDirectory(backupRoot, runtimeRoot, _ => true);
            }

            DeleteDirectoryIfExists(backupRoot);
            foreach (string backupCandidate in Directory.Exists(runtimeParent)
                         ? Directory.GetDirectories(runtimeParent, "win-x64.backup-*", SearchOption.TopDirectoryOnly)
                         : [])
            {
                DeleteDirectoryIfExists(backupCandidate);
            }

            foreach (string repairCandidate in Directory.Exists(runtimeParent)
                         ? Directory.GetDirectories(runtimeParent, "win-x64.repair-*", SearchOption.TopDirectoryOnly)
                         : [])
            {
                DeleteDirectoryIfExists(repairCandidate);
            }
        }
    }

    [Fact]
    public void PublishComputerUseWinPluginRejectsPreManifestBackupMissingManagedDependency()
    {
        string repoRoot = GetRepositoryRoot();
        string scriptPath = GetPublishTestScriptPath(repoRoot);
        string runtimeRoot = Path.Combine(repoRoot, "plugins", "computer-use-win", "runtime", "win-x64");
        string runtimeParent = Path.GetDirectoryName(runtimeRoot)!;
        string backupRoot = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "computer-use-win-runtime-backup-legacy-missing-dependency", Guid.NewGuid().ToString("N"));

        try
        {
            EnsurePublishedRuntimeBundle(repoRoot, scriptPath, runtimeRoot);
            CopyDirectory(runtimeRoot, backupRoot, _ => true);
            File.Delete(Path.Combine(runtimeRoot, "okno-runtime-bundle-manifest.json"));
            string dependencyPath = Path.Combine(runtimeRoot, "Microsoft.Extensions.Hosting.dll");
            Assert.True(File.Exists(dependencyPath));
            File.Delete(dependencyPath);

            ScriptInvocationResult result = InvokePowerShellScript(
                scriptPath,
                repoRoot,
                startInfo =>
                {
                    UsePreparedPublishSource(startInfo, backupRoot);
                    AddScriptSwitch(startInfo, "-FailAfterBackup");
                });

            Assert.NotEqual(0, result.ExitCode);
            Assert.False(Directory.Exists(runtimeRoot));
            Assert.NotEmpty(Directory.GetDirectories(runtimeParent, "win-x64.backup-*", SearchOption.TopDirectoryOnly));
        }
        finally
        {
            DeleteDirectoryIfExists(runtimeRoot);
            if (Directory.Exists(backupRoot))
            {
                CopyDirectory(backupRoot, runtimeRoot, _ => true);
            }

            DeleteDirectoryIfExists(backupRoot);
            foreach (string backupCandidate in Directory.Exists(runtimeParent)
                         ? Directory.GetDirectories(runtimeParent, "win-x64.backup-*", SearchOption.TopDirectoryOnly)
                         : [])
            {
                DeleteDirectoryIfExists(backupCandidate);
            }

            foreach (string repairCandidate in Directory.Exists(runtimeParent)
                         ? Directory.GetDirectories(runtimeParent, "win-x64.repair-*", SearchOption.TopDirectoryOnly)
                         : [])
            {
                DeleteDirectoryIfExists(repairCandidate);
            }
        }
    }

    [Fact]
    public void PublishComputerUseWinPluginKeepsCanonicalRuntimeRunnableWhenRepairHandoffFails()
    {
        string repoRoot = GetRepositoryRoot();
        string scriptPath = GetPublishTestScriptPath(repoRoot);
        string runtimeRoot = Path.Combine(repoRoot, "plugins", "computer-use-win", "runtime", "win-x64");
        string runtimeParent = Path.GetDirectoryName(runtimeRoot)!;
        string backupRoot = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "computer-use-win-runtime-backup-handoff", Guid.NewGuid().ToString("N"));

        try
        {
            EnsurePublishedRuntimeBundle(repoRoot, scriptPath, runtimeRoot);
            CopyDirectory(runtimeRoot, backupRoot, _ => true);

            ScriptInvocationResult result = InvokePowerShellScript(
                scriptPath,
                repoRoot,
                startInfo =>
                {
                    UsePreparedPublishSource(startInfo, backupRoot);
                    AddScriptSwitch(startInfo, "-FailAfterBackup");
                    AddScriptSwitch(startInfo, "-FailRestore");
                    AddScriptSwitch(startInfo, "-FailRepairHandoff");
                });

            Assert.NotEqual(0, result.ExitCode);
            Assert.True(File.Exists(Path.Combine(runtimeRoot, "Okno.Server.exe")));
            Assert.True(File.Exists(Path.Combine(runtimeRoot, "hostfxr.dll")));
            Assert.True(File.Exists(Path.Combine(runtimeRoot, "okno-runtime-bundle-manifest.json")));
        }
        finally
        {
            DeleteDirectoryIfExists(runtimeRoot);
            if (Directory.Exists(backupRoot))
            {
                CopyDirectory(backupRoot, runtimeRoot, _ => true);
            }

            DeleteDirectoryIfExists(backupRoot);
            foreach (string backupCandidate in Directory.Exists(runtimeParent)
                         ? Directory.GetDirectories(runtimeParent, "win-x64.backup-*", SearchOption.TopDirectoryOnly)
                         : [])
            {
                DeleteDirectoryIfExists(backupCandidate);
            }

            foreach (string repairCandidate in Directory.Exists(runtimeParent)
                         ? Directory.GetDirectories(runtimeParent, "win-x64.repair-*", SearchOption.TopDirectoryOnly)
                         : [])
            {
                DeleteDirectoryIfExists(repairCandidate);
            }
        }
    }

    [Fact]
    public void PublishComputerUseWinPluginDoesNotUseCanonicalRuntimeAsFallbackRepairWorkspace()
    {
        string repoRoot = GetRepositoryRoot();
        string scriptPath = GetPublishTestScriptPath(repoRoot);
        string runtimeRoot = Path.Combine(repoRoot, "plugins", "computer-use-win", "runtime", "win-x64");
        string runtimeParent = Path.GetDirectoryName(runtimeRoot)!;
        string backupRoot = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "computer-use-win-runtime-backup-fallback-handoff", Guid.NewGuid().ToString("N"));

        try
        {
            EnsurePublishedRuntimeBundle(repoRoot, scriptPath, runtimeRoot);
            CopyDirectory(runtimeRoot, backupRoot, _ => true);

            ScriptInvocationResult result = InvokePowerShellScript(
                scriptPath,
                repoRoot,
                startInfo =>
                {
                    UsePreparedPublishSource(startInfo, backupRoot);
                    AddScriptSwitch(startInfo, "-FailAfterBackup");
                    AddScriptSwitch(startInfo, "-FailRestore");
                    AddScriptSwitch(startInfo, "-FailRepairHandoff");
                    AddScriptSwitch(startInfo, "-FailRepairFallbackHandoff");
                });

            Assert.NotEqual(0, result.ExitCode);
            Assert.False(Directory.Exists(runtimeRoot));
            Assert.NotEmpty(Directory.GetDirectories(runtimeParent, "win-x64.backup-*", SearchOption.TopDirectoryOnly));
        }
        finally
        {
            DeleteDirectoryIfExists(runtimeRoot);
            if (Directory.Exists(backupRoot))
            {
                CopyDirectory(backupRoot, runtimeRoot, _ => true);
            }

            DeleteDirectoryIfExists(backupRoot);
            foreach (string backupCandidate in Directory.Exists(runtimeParent)
                         ? Directory.GetDirectories(runtimeParent, "win-x64.backup-*", SearchOption.TopDirectoryOnly)
                         : [])
            {
                DeleteDirectoryIfExists(backupCandidate);
            }

            foreach (string repairCandidate in Directory.Exists(runtimeParent)
                         ? Directory.GetDirectories(runtimeParent, "win-x64.repair-*", SearchOption.TopDirectoryOnly)
                         : [])
            {
                DeleteDirectoryIfExists(repairCandidate);
            }
        }
    }

    [Fact]
    public void PublishComputerUseWinPluginTreatsBackupCleanupFailureAsBestEffortAfterSuccessfulPromote()
    {
        string repoRoot = GetRepositoryRoot();
        string scriptPath = GetPublishTestScriptPath(repoRoot);
        string runtimeRoot = Path.Combine(repoRoot, "plugins", "computer-use-win", "runtime", "win-x64");
        string runtimeParent = Path.GetDirectoryName(runtimeRoot)!;

        try
        {
            EnsurePublishedRuntimeBundle(repoRoot, scriptPath, runtimeRoot);

            ScriptInvocationResult result = InvokePowerShellScript(
                scriptPath,
                repoRoot,
                startInfo =>
                {
                    UsePreparedPublishSource(startInfo, runtimeRoot);
                    AddScriptSwitch(startInfo, "-FailBackupCleanup");
                });

            Assert.Equal(0, result.ExitCode);
            using JsonDocument payload = JsonDocument.Parse(result.Stdout);
            Assert.True(File.Exists(Path.Combine(runtimeRoot, "Okno.Server.exe")));
            Assert.Equal(runtimeRoot, payload.RootElement.GetProperty("runtimeRoot").GetString());
            Assert.NotEmpty(Directory.GetDirectories(runtimeParent, "win-x64.backup-*", SearchOption.TopDirectoryOnly));
        }
        finally
        {
            foreach (string backupCandidate in Directory.Exists(runtimeParent)
                         ? Directory.GetDirectories(runtimeParent, "win-x64.backup-*", SearchOption.TopDirectoryOnly)
                         : [])
            {
                DeleteDirectoryIfExists(backupCandidate);
            }
        }
    }

    [Fact]
    public void PublishComputerUseWinPluginDoesNotReadInheritedTestEnvironmentOverrides()
    {
        string repoRoot = GetRepositoryRoot();
        string scriptPath = Path.Combine(repoRoot, "scripts", "codex", "publish-computer-use-win-plugin.ps1");
        string coreScriptPath = Path.Combine(repoRoot, "scripts", "codex", "publish-computer-use-win-plugin-core.ps1");

        Assert.DoesNotContain("COMPUTER_USE_WIN_TEST_", File.ReadAllText(scriptPath), StringComparison.Ordinal);
        Assert.DoesNotContain("COMPUTER_USE_WIN_TEST_", File.ReadAllText(coreScriptPath), StringComparison.Ordinal);
    }

    [Fact]
    public void ComputerUseWinLauncherFailsFastWhenPluginLocalRuntimeIsMissing()
    {
        string repoRoot = GetRepositoryRoot();
        string sourcePluginRoot = Path.Combine(repoRoot, "plugins", "computer-use-win");
        string tempPluginRoot = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "computer-use-win-missing-runtime", Guid.NewGuid().ToString("N"));

        try
        {
            CopyDirectory(sourcePluginRoot, tempPluginRoot, relativePath =>
                !relativePath.StartsWith($"runtime{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

            ScriptInvocationResult result = InvokePowerShellScript(
                Path.Combine(tempPluginRoot, "run-computer-use-win-mcp.ps1"),
                tempPluginRoot,
                startInfo =>
                {
                    startInfo.Environment["COMPUTER_USE_WIN_REPO_ROOT"] = string.Empty;
                    startInfo.Environment["WINBRIDGE_RUN_ID"] = string.Empty;
                    startInfo.Environment["WINBRIDGE_RUN_ROOT"] = string.Empty;
                    startInfo.Environment["WINBRIDGE_ARTIFACTS_ROOT"] = string.Empty;
                });

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("publish-computer-use-win-plugin.ps1", result.Stderr, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(Path.Combine("runtime", "win-x64", "Okno.Server.exe"), result.Stderr, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectoryIfExists(tempPluginRoot);
        }
    }

    [Fact]
    public void ComputerUseWinLauncherFailsFastWhenPluginLocalRuntimeBundleIsIncomplete()
    {
        string repoRoot = GetRepositoryRoot();
        string sourcePluginRoot = Path.Combine(repoRoot, "plugins", "computer-use-win");
        string tempPluginRoot = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "computer-use-win-partial-runtime", Guid.NewGuid().ToString("N"));

        try
        {
            CopyDirectory(sourcePluginRoot, tempPluginRoot, _ => true);
            string runtimeRoot = Path.Combine(tempPluginRoot, "runtime", "win-x64");
            string serverDllPath = Path.Combine(runtimeRoot, "Okno.Server.dll");
            if (File.Exists(serverDllPath))
            {
                File.Delete(serverDllPath);
            }

            ScriptInvocationResult result = InvokePowerShellScript(
                Path.Combine(tempPluginRoot, "run-computer-use-win-mcp.ps1"),
                tempPluginRoot,
                startInfo =>
                {
                    startInfo.Environment["COMPUTER_USE_WIN_REPO_ROOT"] = string.Empty;
                    startInfo.Environment["WINBRIDGE_RUN_ID"] = string.Empty;
                    startInfo.Environment["WINBRIDGE_RUN_ROOT"] = string.Empty;
                    startInfo.Environment["WINBRIDGE_ARTIFACTS_ROOT"] = string.Empty;
                });

            Assert.NotEqual(0, result.ExitCode);
            Assert.False(string.IsNullOrWhiteSpace(result.Stderr));
            Assert.Contains("Okno.Server.dll", result.Stderr, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectoryIfExists(tempPluginRoot);
        }
    }

    [Fact]
    public void ComputerUseWinLauncherFailsFastWhenRuntimeDependencyFileIsMissing()
    {
        string repoRoot = GetRepositoryRoot();
        string publishScriptPath = Path.Combine(repoRoot, "scripts", "codex", "publish-computer-use-win-plugin.ps1");
        string sourcePluginRoot = Path.Combine(repoRoot, "plugins", "computer-use-win");
        string tempPluginRoot = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "computer-use-win-missing-dependency", Guid.NewGuid().ToString("N"));

        EnsurePublishedRuntimeBundle(repoRoot, publishScriptPath, Path.Combine(sourcePluginRoot, "runtime", "win-x64"));

        try
        {
            CopyDirectory(sourcePluginRoot, tempPluginRoot, _ => true);
            string dependencyPath = Path.Combine(tempPluginRoot, "runtime", "win-x64", "hostfxr.dll");
            Assert.True(File.Exists(dependencyPath));
            File.Delete(dependencyPath);

            ScriptInvocationResult result = InvokePowerShellScript(
                Path.Combine(tempPluginRoot, "run-computer-use-win-mcp.ps1"),
                tempPluginRoot,
                startInfo =>
                {
                    startInfo.Environment["COMPUTER_USE_WIN_REPO_ROOT"] = string.Empty;
                    startInfo.Environment["WINBRIDGE_RUN_ID"] = string.Empty;
                    startInfo.Environment["WINBRIDGE_RUN_ROOT"] = string.Empty;
                    startInfo.Environment["WINBRIDGE_ARTIFACTS_ROOT"] = string.Empty;
                });

            Assert.NotEqual(0, result.ExitCode);
            Assert.False(string.IsNullOrWhiteSpace(result.Stderr));
            Assert.Contains("hostfxr.dll", result.Stderr, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectoryIfExists(tempPluginRoot);
        }
    }

    [Fact]
    public async Task ComputerUseWinLauncherFromTempPluginCopyPublishesPublicSurfaceWithoutRepoHints()
    {
        string repoRoot = GetRepositoryRoot();
        string publishScriptPath = Path.Combine(repoRoot, "scripts", "codex", "publish-computer-use-win-plugin.ps1");
        string sourcePluginRoot = Path.Combine(repoRoot, "plugins", "computer-use-win");
        string tempPluginRoot = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "computer-use-win-installed-copy", Guid.NewGuid().ToString("N"));

        EnsurePublishedRuntimeBundle(repoRoot, publishScriptPath, Path.Combine(sourcePluginRoot, "runtime", "win-x64"));

        try
        {
            CopyDirectory(sourcePluginRoot, tempPluginRoot, _ => true);

            using Process process = StartPluginLauncher(tempPluginRoot);
            await using StreamWriter writer = process.StandardInput;
            using StreamReader reader = process.StandardOutput;
            PluginMcpSession session = new(reader, writer);

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
                            name = "ComputerUseWin.InstallSurfaceTests",
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

                Assert.Equal(
                    [ToolNames.ComputerUseWinClick, ToolNames.ComputerUseWinGetAppState, ToolNames.ComputerUseWinListApps],
                    toolNames);

                JsonElement getAppStateDescriptor = toolsResponse.RootElement
                    .GetProperty("result")
                    .GetProperty("tools")
                    .EnumerateArray()
                    .Single(tool => tool.GetProperty("name").GetString() == ToolNames.ComputerUseWinGetAppState);
                JsonElement properties = getAppStateDescriptor.GetProperty("inputSchema").GetProperty("properties");
                Assert.True(properties.TryGetProperty("windowId", out _));
                Assert.True(properties.TryGetProperty("hwnd", out _));
                Assert.False(properties.TryGetProperty("appId", out _));

                using JsonDocument listAppsResponse = await session.SendRequestAsync(
                    "tools/call",
                    new
                    {
                        name = ToolNames.ComputerUseWinListApps,
                        arguments = new { },
                    },
                    "tools/call:list_apps");
                JsonElement listAppsStructured = listAppsResponse.RootElement
                    .GetProperty("result")
                    .GetProperty("structuredContent");
                Assert.Equal(ComputerUseWinStatusValues.Ok, listAppsStructured.GetProperty("status").GetString());
                JsonElement apps = listAppsStructured.GetProperty("apps");
                Assert.Equal(JsonValueKind.Array, apps.ValueKind);
                foreach (JsonElement app in apps.EnumerateArray())
                {
                    Assert.True(app.TryGetProperty("windows", out JsonElement windows));
                    Assert.Equal(JsonValueKind.Array, windows.ValueKind);
                }
            }
            finally
            {
                writer.Close();
                await WaitForExitAsync(process);
            }
        }
        finally
        {
            DeleteDirectoryIfExists(tempPluginRoot);
        }
    }

    [Theory]
    [InlineData("Directory.Build.props")]
    [InlineData("Directory.Packages.props")]
    [InlineData("global.json")]
    [InlineData("WinBridge.sln")]
    [InlineData("NuGet.Config")]
    [InlineData("Repo.Custom.props")]
    [InlineData("Repo.Custom.targets")]
    public void PublishedRuntimeBundleIsFreshReturnsFalseWhenRepoLevelBuildInputIsNewerThanManifest(string repoLevelInputName)
    {
        string root = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        string runtimeRoot = Path.Combine(root, "plugins", "computer-use-win", "runtime", "win-x64");
        string manifestPath = Path.Combine(runtimeRoot, "okno-runtime-bundle-manifest.json");
        string repoLevelInputPath = Path.Combine(root, repoLevelInputName);

        try
        {
            Directory.CreateDirectory(runtimeRoot);
            File.WriteAllText(manifestPath, """{"formatVersion":1,"files":[]}""");
            File.WriteAllText(repoLevelInputPath, "<Project />");

            DateTime manifestWriteUtc = DateTime.UtcNow.AddMinutes(-2);
            DateTime inputWriteUtc = DateTime.UtcNow.AddMinutes(-1);
            File.SetLastWriteTimeUtc(manifestPath, manifestWriteUtc);
            File.SetLastWriteTimeUtc(repoLevelInputPath, inputWriteUtc);

            Assert.False(PublishedRuntimeBundleIsFresh(root, runtimeRoot));
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    private static Process StartPluginLauncher(string pluginRoot)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "powershell",
            WorkingDirectory = pluginRoot,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(Path.Combine(pluginRoot, "run-computer-use-win-mcp.ps1"));
        startInfo.Environment["COMPUTER_USE_WIN_REPO_ROOT"] = string.Empty;
        startInfo.Environment["WINBRIDGE_RUN_ID"] = string.Empty;
        startInfo.Environment["WINBRIDGE_RUN_ROOT"] = string.Empty;
        startInfo.Environment["WINBRIDGE_ARTIFACTS_ROOT"] = string.Empty;

        Process process = new() { StartInfo = startInfo };
        process.Start();
        return process;
    }

    private static async Task WaitForExitAsync(Process process)
    {
        if (process.HasExited)
        {
            return;
        }

        Task waitTask = process.WaitForExitAsync();
        Task completedTask = await Task.WhenAny(waitTask, Task.Delay(TimeSpan.FromSeconds(5)));
        if (!ReferenceEquals(completedTask, waitTask))
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
    }

    private static void CopyDirectory(string sourceRoot, string destinationRoot, Func<string, bool> includePredicate)
    {
        foreach (string sourcePath in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourceRoot, sourcePath);
            if (!includePredicate(relativePath))
            {
                continue;
            }

            string destinationPath = Path.Combine(destinationRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(sourcePath, destinationPath, overwrite: true);
        }
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static void EnsurePublishedRuntimeBundle(string repoRoot, string scriptPath, string runtimeRoot)
    {
        if (Directory.Exists(runtimeRoot)
            && RuntimeBundleMatchesManifest(runtimeRoot)
            && PublishedRuntimeBundleIsFresh(repoRoot, runtimeRoot))
        {
            return;
        }

        ScriptInvocationResult result = InvokePowerShellScript(
            scriptPath,
            repoRoot,
            _ => { });
        Assert.True(
            result.ExitCode == 0,
            $"Publish script failed while preparing runtime baseline. ExitCode={result.ExitCode}. stderr='{result.Stderr.Trim()}', stdout='{result.Stdout.Trim()}'.");
        AssertRuntimeBundleMatchesManifest(runtimeRoot);
    }

    private static void AssertRuntimeBundleMatchesManifest(string runtimeRoot)
    {
        Assert.True(RuntimeBundleMatchesManifest(runtimeRoot), $"Runtime bundle '{runtimeRoot}' does not match its manifest.");
    }

    private static bool PublishedRuntimeBundleIsFresh(string repoRoot, string runtimeRoot)
    {
        string manifestPath = Path.Combine(runtimeRoot, "okno-runtime-bundle-manifest.json");
        if (!File.Exists(manifestPath))
        {
            return false;
        }

        DateTime manifestWriteUtc = File.GetLastWriteTimeUtc(manifestPath);
        DateTime latestSourceWriteUtc = EnumeratePublishInputs(repoRoot)
            .Select(File.GetLastWriteTimeUtc)
            .DefaultIfEmpty(DateTime.MinValue)
            .Max();
        return manifestWriteUtc >= latestSourceWriteUtc;
    }

    private static IEnumerable<string> EnumeratePublishInputs(string repoRoot)
    {
        HashSet<string> yielded = new(StringComparer.OrdinalIgnoreCase);

        foreach (string path in EnumerateRepoRootBuildInputs(repoRoot))
        {
            if (yielded.Add(Path.GetFullPath(path)))
            {
                yield return path;
            }
        }

        string srcRoot = Path.Combine(repoRoot, "src");
        if (Directory.Exists(srcRoot))
        {
            foreach (string path in EnumerateFilesRecursively(srcRoot))
            {
                if (yielded.Add(Path.GetFullPath(path)))
                {
                    yield return path;
                }
            }
        }

        string pluginRoot = Path.Combine(repoRoot, "plugins", "computer-use-win");
        string generatedRuntimeRoot = Path.Combine(pluginRoot, "runtime") + Path.DirectorySeparatorChar;
        if (Directory.Exists(pluginRoot))
        {
            foreach (string path in EnumerateFilesRecursively(pluginRoot))
            {
                string normalizedPath = Path.GetFullPath(path);
                if (normalizedPath.StartsWith(generatedRuntimeRoot, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (yielded.Add(normalizedPath))
                {
                    yield return path;
                }
            }
        }

        string scriptsRoot = Path.Combine(repoRoot, "scripts", "codex");
        if (Directory.Exists(scriptsRoot))
        {
            foreach (string path in EnumerateFilesRecursively(scriptsRoot))
            {
                if (yielded.Add(Path.GetFullPath(path)))
                {
                    yield return path;
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateRepoRootBuildInputs(string repoRoot)
    {
        string[] canonicalFiles =
        [
            "global.json",
            "Directory.Build.props",
            "Directory.Packages.props",
            "WinBridge.sln",
            "NuGet.Config",
            "nuget.config",
        ];

        foreach (string fileName in canonicalFiles)
        {
            string candidate = Path.Combine(repoRoot, fileName);
            if (File.Exists(candidate))
            {
                yield return candidate;
            }
        }

        foreach (string path in Directory.EnumerateFiles(repoRoot, "*.props", SearchOption.TopDirectoryOnly))
        {
            yield return path;
        }

        foreach (string path in Directory.EnumerateFiles(repoRoot, "*.targets", SearchOption.TopDirectoryOnly))
        {
            yield return path;
        }
    }

    private static IEnumerable<string> EnumerateFilesRecursively(string root)
    {
        foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return path;
        }
    }

    private static bool RuntimeBundleMatchesManifest(string runtimeRoot)
    {
        if (!Directory.Exists(runtimeRoot))
        {
            return false;
        }

        string manifestPath = Path.Combine(runtimeRoot, "okno-runtime-bundle-manifest.json");
        if (!File.Exists(manifestPath))
        {
            return false;
        }

        using JsonDocument manifestDocument = JsonDocument.Parse(File.ReadAllText(manifestPath));
        if (!manifestDocument.RootElement.TryGetProperty("formatVersion", out JsonElement formatVersionElement)
            || formatVersionElement.GetInt32() != 1)
        {
            return false;
        }

        Dictionary<string, long> expectedFiles = manifestDocument.RootElement
            .GetProperty("files")
            .EnumerateArray()
            .ToDictionary(
                static entry => entry.GetProperty("path").GetString() ?? string.Empty,
                static entry => entry.GetProperty("size").GetInt64(),
                StringComparer.Ordinal);

        foreach (string filePath in Directory.EnumerateFiles(runtimeRoot, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(runtimeRoot, filePath);
            if (string.Equals(relativePath, "okno-runtime-bundle-manifest.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!expectedFiles.Remove(relativePath, out long expectedSize))
            {
                return false;
            }

            long actualSize = new FileInfo(filePath).Length;
            if (actualSize != expectedSize)
            {
                return false;
            }
        }

        return expectedFiles.Count == 0;
    }

    private static void UsePreparedPublishSource(ProcessStartInfo startInfo, string sourceRoot)
    {
        startInfo.ArgumentList.Add("-PublishSourceRoot");
        startInfo.ArgumentList.Add(sourceRoot);
    }

    private static void AddScriptSwitch(ProcessStartInfo startInfo, string switchName)
    {
        startInfo.ArgumentList.Add(switchName);
    }

    private static string GetPublishScriptPath(string repoRoot)
    {
        return Path.Combine(repoRoot, "scripts", "codex", "publish-computer-use-win-plugin.ps1");
    }

    private static string GetPublishTestScriptPath(string repoRoot)
    {
        return Path.Combine(repoRoot, "scripts", "codex", "test-publish-computer-use-win-plugin.ps1");
    }

    private static ScriptInvocationResult InvokePowerShellScript(string scriptPath, string workingDirectory, Action<ProcessStartInfo> configure)
    {
        TimeSpan timeout = TimeSpan.FromMinutes(15);
        ProcessStartInfo startInfo = new()
        {
            FileName = "powershell",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);

        configure(startInfo);

        using Process process = new() { StartInfo = startInfo };
        process.Start();

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeout))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            Task.WaitAll(stdoutTask, stderrTask);
            return new ScriptInvocationResult(
                -1,
                stdoutTask.Result,
                $"PowerShell script timed out after {timeout}. {stderrTask.Result}");
        }

        Task.WaitAll(stdoutTask, stderrTask);

        return new ScriptInvocationResult(process.ExitCode, stdoutTask.Result, stderrTask.Result);
    }

    private static string GetRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "WinBridge.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Не удалось определить корень репозитория WinBridge.");
    }

    private sealed class PluginMcpSession(StreamReader reader, StreamWriter writer)
    {
        private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(15);
        private int nextRequestId = 1;

        public async Task SendNotificationAsync(string method)
        {
            string json = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                method,
            });

            await writer.WriteLineAsync(json);
            await writer.FlushAsync();
        }

        public Task<JsonDocument> SendRequestAsync(string method, object parameters, string requestName)
        {
            int requestId = nextRequestId++;
            string json = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = requestId,
                method,
                @params = parameters,
            });

            return SendAndReadAsync(requestName, requestId, json);
        }

        private async Task<JsonDocument> SendAndReadAsync(string requestName, int expectedId, string json)
        {
            await writer.WriteLineAsync(json);
            await writer.FlushAsync();

            while (true)
            {
                string? line = await reader.ReadLineAsync().WaitAsync(ResponseTimeout);
                Assert.False(line is null, $"Plugin process exited before '{requestName}' response.");
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                JsonDocument document = JsonDocument.Parse(line);
                if (!document.RootElement.TryGetProperty("id", out JsonElement idElement))
                {
                    document.Dispose();
                    continue;
                }

                if (idElement.GetInt32() == expectedId)
                {
                    return document;
                }

                document.Dispose();
            }
        }
    }

    private sealed record ScriptInvocationResult(int ExitCode, string Stdout, string Stderr);
}
