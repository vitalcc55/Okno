using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace WinBridge.Server.IntegrationTests;

public sealed class RuntimeBundleResolverTests
{
    [Fact]
    public void ResolveOknoTestBundleRunIdOverrideDoesNotMixAmbientArtifactsState()
    {
        string repoRoot = GetRepositoryRoot();
        string explicitRunId = "resolver-explicit-run";
        string ambientRunId = "resolver-ambient-run";
        string fallbackMarker = Path.Combine(repoRoot, "src", "WinBridge.Server", "bin", "resolver-fallback-marker-" + Guid.NewGuid().ToString("N"));
        string fallbackHelperMarker = Path.Combine(repoRoot, "tests", "WinBridge.SmokeWindowHost", "bin", "resolver-fallback-marker-" + Guid.NewGuid().ToString("N"));
        string ambientArtifactsRoot = Path.Combine(repoRoot, ".tmp", ".codex", "artifacts", ambientRunId);
        string ambientRunRoot = Path.Combine(repoRoot, ".tmp", ".codex", "runs", ambientRunId);

        try
        {
            CreateMarkerFile(Path.Combine(fallbackMarker, "Okno.Server.dll"));
            CreateMarkerFile(Path.Combine(fallbackHelperMarker, "WinBridge.SmokeWindowHost.exe"));

            JsonElement payload = InvokeBundleResolver(
                repoRoot,
                startInfo =>
                {
                    startInfo.ArgumentList.Add("-RunId");
                    startInfo.ArgumentList.Add(explicitRunId);
                    startInfo.Environment["WINBRIDGE_RUN_ID"] = ambientRunId;
                    startInfo.Environment["WINBRIDGE_RUN_ROOT"] = ambientRunRoot;
                    startInfo.Environment["WINBRIDGE_ARTIFACTS_ROOT"] = ambientArtifactsRoot;
                });

            Assert.Equal(explicitRunId, payload.GetProperty("runId").GetString());
            Assert.Equal(
                Path.Combine(repoRoot, ".tmp", ".codex", "runs", explicitRunId),
                payload.GetProperty("runRoot").GetString());
            Assert.True(string.IsNullOrEmpty(payload.GetProperty("artifactsRoot").GetString()));
            Assert.Equal("fallback_build_cache", payload.GetProperty("preferredSourceContext").GetString());
        }
        finally
        {
            DeleteDirectoryIfExists(fallbackMarker);
            DeleteDirectoryIfExists(fallbackHelperMarker);
            DeleteDirectoryIfExists(Path.Combine(repoRoot, ".tmp", ".codex", "runs", explicitRunId));
        }
    }

    [Fact]
    public void ResolveOknoTestBundleAssemblyBaseDirectoryWinsOverAmbientDllEnvironment()
    {
        string repoRoot = GetRepositoryRoot();
        string runId = "resolver-assembly-run";
        string artifactsRoot = Path.Combine(repoRoot, ".tmp", ".codex", "artifacts", runId);
        string runRoot = Path.Combine(repoRoot, ".tmp", ".codex", "runs", runId);
        string assemblyBaseDirectory = Path.Combine(artifactsRoot, "bin", "WinBridge.Server.IntegrationTests", "debug");
        string ambientRoot = Path.Combine(repoRoot, ".tmp", ".codex", "resolver-ambient-dll", Guid.NewGuid().ToString("N"));
        string ambientServerDll = Path.Combine(ambientRoot, "server", "Okno.Server.dll");
        string ambientHelperExe = Path.Combine(ambientRoot, "helper", "WinBridge.SmokeWindowHost.exe");

        try
        {
            CreateMarkerFile(Path.Combine(artifactsRoot, "bin", "WinBridge.Server", "debug", "Okno.Server.dll"));
            CreateMarkerFile(Path.Combine(artifactsRoot, "bin", "WinBridge.SmokeWindowHost", "debug", "WinBridge.SmokeWindowHost.exe"));
            CreateMarkerFile(Path.Combine(assemblyBaseDirectory, "WinBridge.Server.IntegrationTests.dll"));
            CreateMarkerFile(ambientServerDll);
            CreateMarkerFile(ambientHelperExe);

            JsonElement payload = InvokeBundleResolver(
                repoRoot,
                startInfo =>
                {
                    startInfo.ArgumentList.Add("-AssemblyBaseDirectory");
                    startInfo.ArgumentList.Add(assemblyBaseDirectory);
                    startInfo.Environment["WINBRIDGE_SERVER_DLL"] = ambientServerDll;
                    startInfo.Environment["WINBRIDGE_SMOKE_HELPER_EXE"] = ambientHelperExe;
                });

            Assert.Equal(runId, payload.GetProperty("runId").GetString());
            Assert.Equal(runRoot, payload.GetProperty("runRoot").GetString());
            Assert.Equal(artifactsRoot, payload.GetProperty("artifactsRoot").GetString());
            Assert.Equal("artifacts_root", payload.GetProperty("preferredSourceContext").GetString());
            Assert.StartsWith(runRoot, payload.GetProperty("serverDll").GetString()!, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith(runRoot, payload.GetProperty("helperExe").GetString()!, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectoryIfExists(runRoot);
            DeleteDirectoryIfExists(artifactsRoot);
            DeleteDirectoryIfExists(ambientRoot);
        }
    }

    [Fact]
    public void PrepareOknoTestBundleSelectsOneCoherentRelativeOutputContext()
    {
        string repoRoot = GetRepositoryRoot();
        string runId = "resolver-coherent-pair";
        string artifactsRoot = Path.Combine(repoRoot, ".tmp", ".codex", "artifacts", runId);
        string runRoot = Path.Combine(repoRoot, ".tmp", ".codex", "runs", runId);

        try
        {
            DateTime utcNow = DateTime.UtcNow;
            CreateMarkerFile(Path.Combine(artifactsRoot, "bin", "WinBridge.Server", "debug", "Okno.Server.dll"), utcNow.AddMinutes(10));
            CreateMarkerFile(Path.Combine(artifactsRoot, "bin", "WinBridge.SmokeWindowHost", "debug", "WinBridge.SmokeWindowHost.exe"), utcNow);
            CreateMarkerFile(Path.Combine(artifactsRoot, "bin", "WinBridge.Server", "release", "Okno.Server.dll"), utcNow.AddMinutes(5));
            CreateMarkerFile(Path.Combine(artifactsRoot, "bin", "WinBridge.SmokeWindowHost", "release", "WinBridge.SmokeWindowHost.exe"), utcNow.AddMinutes(5));

            using JsonDocument payload = InvokeJsonScript(
                Path.Combine(repoRoot, "scripts", "codex", "prepare-okno-test-bundle.ps1"),
                repoRoot,
                startInfo =>
                {
                    startInfo.ArgumentList.Add("-RepoRoot");
                    startInfo.ArgumentList.Add(repoRoot);
                    startInfo.ArgumentList.Add("-RunId");
                    startInfo.ArgumentList.Add(runId);
                    startInfo.ArgumentList.Add("-ArtifactsRoot");
                    startInfo.ArgumentList.Add(artifactsRoot);
                });

            string manifestPath = payload.RootElement.GetProperty("manifestPath").GetString()
                ?? throw new InvalidOperationException("Bundle manifest path was not returned.");
            using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
            string serverSourceDirectory = manifest.RootElement.GetProperty("serverSourceDirectory").GetString()
                ?? throw new InvalidOperationException("Bundle manifest did not contain serverSourceDirectory.");
            string helperSourceDirectory = manifest.RootElement.GetProperty("helperSourceDirectory").GetString()
                ?? throw new InvalidOperationException("Bundle manifest did not contain helperSourceDirectory.");

            Assert.EndsWith(Path.Combine("WinBridge.Server", "release"), serverSourceDirectory, StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith(Path.Combine("WinBridge.SmokeWindowHost", "release"), helperSourceDirectory, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectoryIfExists(runRoot);
            DeleteDirectoryIfExists(artifactsRoot);
        }
    }

    [Fact]
    public void ResolveOknoTestBundleAssemblyBaseDirectoryPinsSameRelativeOutputContext()
    {
        string repoRoot = GetRepositoryRoot();
        string runId = "resolver-assembly-relative-context";
        string artifactsRoot = Path.Combine(repoRoot, ".tmp", ".codex", "artifacts", runId);
        string runRoot = Path.Combine(repoRoot, ".tmp", ".codex", "runs", runId);
        string releaseAssemblyBaseDirectory = Path.Combine(artifactsRoot, "bin", "WinBridge.Server.IntegrationTests", "release");

        try
        {
            DateTime utcNow = DateTime.UtcNow;
            CreateMarkerFile(Path.Combine(artifactsRoot, "bin", "WinBridge.Server", "debug", "Okno.Server.dll"), utcNow.AddMinutes(10));
            CreateMarkerFile(Path.Combine(artifactsRoot, "bin", "WinBridge.SmokeWindowHost", "debug", "WinBridge.SmokeWindowHost.exe"), utcNow.AddMinutes(10));
            CreateMarkerFile(Path.Combine(artifactsRoot, "bin", "WinBridge.Server", "release", "Okno.Server.dll"), utcNow.AddMinutes(5));
            CreateMarkerFile(Path.Combine(artifactsRoot, "bin", "WinBridge.SmokeWindowHost", "release", "WinBridge.SmokeWindowHost.exe"), utcNow.AddMinutes(5));
            CreateMarkerFile(Path.Combine(releaseAssemblyBaseDirectory, "WinBridge.Server.IntegrationTests.dll"), utcNow.AddMinutes(6));

            JsonElement payload = InvokeBundleResolver(
                repoRoot,
                startInfo =>
                {
                    startInfo.ArgumentList.Add("-AssemblyBaseDirectory");
                    startInfo.ArgumentList.Add(releaseAssemblyBaseDirectory);
                });

            string manifestPath = payload.GetProperty("manifestPath").GetString()
                ?? throw new InvalidOperationException("Bundle manifest path was not returned.");
            using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
            string serverSourceDirectory = manifest.RootElement.GetProperty("serverSourceDirectory").GetString()
                ?? throw new InvalidOperationException("Bundle manifest did not contain serverSourceDirectory.");
            string helperSourceDirectory = manifest.RootElement.GetProperty("helperSourceDirectory").GetString()
                ?? throw new InvalidOperationException("Bundle manifest did not contain helperSourceDirectory.");

            Assert.EndsWith(Path.Combine("WinBridge.Server", "release"), serverSourceDirectory, StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith(Path.Combine("WinBridge.SmokeWindowHost", "release"), helperSourceDirectory, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectoryIfExists(runRoot);
            DeleteDirectoryIfExists(artifactsRoot);
        }
    }

    [Fact]
    public void ResolveOknoTestBundleFallbackAssemblyBaseDirectoryPinsSameRelativeOutputContext()
    {
        string repoRoot = GetRepositoryRoot();
        string runId = "resolver-fallback-assembly-relative-context";
        string runRoot = Path.Combine(repoRoot, ".tmp", ".codex", "runs", runId);
        string marker = "resolver-fallback-relative-" + Guid.NewGuid().ToString("N");
        string serverBinRoot = Path.Combine(repoRoot, "src", "WinBridge.Server", "bin", marker);
        string helperBinRoot = Path.Combine(repoRoot, "tests", "WinBridge.SmokeWindowHost", "bin", marker);
        string testBinRoot = Path.Combine(repoRoot, "tests", "WinBridge.Server.IntegrationTests", "bin", marker);
        string releaseAssemblyBaseDirectory = Path.Combine(testBinRoot, "release");

        try
        {
            DateTime utcNow = DateTime.UtcNow;
            CreateMarkerFile(Path.Combine(serverBinRoot, "debug", "Okno.Server.dll"), utcNow.AddMinutes(10));
            CreateMarkerFile(Path.Combine(helperBinRoot, "debug", "WinBridge.SmokeWindowHost.exe"), utcNow.AddMinutes(10));
            CreateMarkerFile(Path.Combine(serverBinRoot, "release", "Okno.Server.dll"), utcNow.AddMinutes(5));
            CreateMarkerFile(Path.Combine(helperBinRoot, "release", "WinBridge.SmokeWindowHost.exe"), utcNow.AddMinutes(5));
            CreateMarkerFile(Path.Combine(releaseAssemblyBaseDirectory, "WinBridge.Server.IntegrationTests.dll"), utcNow.AddMinutes(6));

            JsonElement payload = InvokeBundleResolver(
                repoRoot,
                startInfo =>
                {
                    startInfo.ArgumentList.Add("-RunId");
                    startInfo.ArgumentList.Add(runId);
                    startInfo.ArgumentList.Add("-AssemblyBaseDirectory");
                    startInfo.ArgumentList.Add(releaseAssemblyBaseDirectory);
                });

            string manifestPath = payload.GetProperty("manifestPath").GetString()
                ?? throw new InvalidOperationException("Bundle manifest path was not returned.");
            using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
            string serverSourceDirectory = manifest.RootElement.GetProperty("serverSourceDirectory").GetString()
                ?? throw new InvalidOperationException("Bundle manifest did not contain serverSourceDirectory.");
            string helperSourceDirectory = manifest.RootElement.GetProperty("helperSourceDirectory").GetString()
                ?? throw new InvalidOperationException("Bundle manifest did not contain helperSourceDirectory.");

            Assert.Equal(Path.Combine(serverBinRoot, "release"), serverSourceDirectory, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(Path.Combine(helperBinRoot, "release"), helperSourceDirectory, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectoryIfExists(runRoot);
            DeleteDirectoryIfExists(serverBinRoot);
            DeleteDirectoryIfExists(helperBinRoot);
            DeleteDirectoryIfExists(testBinRoot);
        }
    }

    [Fact]
    public void ResolveWinBridgeVerificationContextPinsBundleSourceToExecutedConfiguration()
    {
        string repoRoot = GetRepositoryRoot();
        string runId = "resolver-verification-context";
        string artifactsRoot = Path.Combine(repoRoot, ".tmp", ".codex", "artifacts", runId);
        string runRoot = Path.Combine(repoRoot, ".tmp", ".codex", "runs", runId);
        string scriptRoot = Path.Combine(repoRoot, ".tmp", ".codex", "resolver-tests", Guid.NewGuid().ToString("N"));
        string scriptPath = Path.Combine(scriptRoot, "resolve-verification-context.ps1");

        try
        {
            DateTime utcNow = DateTime.UtcNow;
            CreateMarkerFile(
                Path.Combine(artifactsRoot, "bin", "WinBridge.Server.IntegrationTests", "debug", "WinBridge.Server.IntegrationTests.dll"),
                utcNow);
            CreateMarkerFile(
                Path.Combine(artifactsRoot, "bin", "WinBridge.Server.IntegrationTests", "release", "WinBridge.Server.IntegrationTests.dll"),
                utcNow.AddMinutes(10));

            Directory.CreateDirectory(scriptRoot);
            File.WriteAllText(
                scriptPath,
                string.Join(
                    Environment.NewLine,
                    "$ErrorActionPreference = 'Stop'",
                    $". '{PowerShellSingleQuote(Path.Combine(repoRoot, "scripts", "common.ps1"))}'",
                    $"$env:WINBRIDGE_ARTIFACTS_ROOT = '{PowerShellSingleQuote(artifactsRoot)}'",
                    $"$context = Resolve-WinBridgeVerificationContext -RepoRoot '{PowerShellSingleQuote(repoRoot)}'",
                    "[pscustomobject]@{",
                    "    bundleSourceRelativePath = $context.BundleSourceRelativePath",
                    "    dotnetTestArguments = $context.DotnetTestArguments",
                    "    integrationTestAssembly = $context.IntegrationTestAssembly",
                    "} | ConvertTo-Json -Depth 4 -Compress"),
                Encoding.UTF8);

            using JsonDocument payload = InvokeJsonScript(scriptPath, repoRoot, _ => { });
            JsonElement root = payload.RootElement;
            string? bundleSourceRelativePath = root.GetProperty("bundleSourceRelativePath").GetString();
            string[] dotnetTestArguments = root.GetProperty("dotnetTestArguments")
                .EnumerateArray()
                .Select(value => value.GetString() ?? string.Empty)
                .ToArray();
            string? integrationTestAssembly = root.GetProperty("integrationTestAssembly").GetString();

            Assert.Equal("debug", bundleSourceRelativePath, ignoreCase: true);
            Assert.Contains("--configuration", dotnetTestArguments);
            Assert.Contains("Debug", dotnetTestArguments);
            Assert.EndsWith(
                Path.Combine("WinBridge.Server.IntegrationTests", "debug", "WinBridge.Server.IntegrationTests.dll"),
                integrationTestAssembly,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectoryIfExists(runRoot);
            DeleteDirectoryIfExists(artifactsRoot);
            DeleteDirectoryIfExists(scriptRoot);
        }
    }

    [Fact]
    public void ResolveOknoTestBundleRejectsConflictingExplicitExecutionContext()
    {
        string repoRoot = GetRepositoryRoot();
        ScriptInvocationResult result = InvokePowerShellScript(
            Path.Combine(repoRoot, "scripts", "codex", "resolve-okno-test-bundle.ps1"),
            repoRoot,
            startInfo =>
            {
                startInfo.ArgumentList.Add("-RepoRoot");
                startInfo.ArgumentList.Add(repoRoot);
                startInfo.ArgumentList.Add("-RunRoot");
                startInfo.ArgumentList.Add(Path.Combine(repoRoot, ".tmp", ".codex", "runs", "local"));
                startInfo.ArgumentList.Add("-ArtifactsRoot");
                startInfo.ArgumentList.Add(Path.Combine(repoRoot, ".tmp", ".codex", "artifacts", "ci-proof"));
            });

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Explicit execution context is internally inconsistent", result.Stderr, StringComparison.Ordinal);
        Assert.Contains("RunRoot=", result.Stderr, StringComparison.Ordinal);
        Assert.Contains("ArtifactsRoot=", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void PrepareOknoTestBundlePreferredSourceContextDoesNotResetAmbientRunContext()
    {
        string repoRoot = GetRepositoryRoot();
        string runId = "resolver-preferred-source-run";
        string runRoot = Path.Combine(repoRoot, ".tmp", ".codex", "runs", runId);
        string artifactsRoot = Path.Combine(repoRoot, ".tmp", ".codex", "artifacts", runId);

        try
        {
            using JsonDocument payload = InvokeJsonScript(
                Path.Combine(repoRoot, "scripts", "codex", "prepare-okno-test-bundle.ps1"),
                repoRoot,
                startInfo =>
                {
                    startInfo.ArgumentList.Add("-RepoRoot");
                    startInfo.ArgumentList.Add(repoRoot);
                    startInfo.ArgumentList.Add("-PreferredSourceContextName");
                    startInfo.ArgumentList.Add("fallback_build_cache");
                    startInfo.Environment["WINBRIDGE_RUN_ID"] = runId;
                    startInfo.Environment["WINBRIDGE_RUN_ROOT"] = runRoot;
                    startInfo.Environment["WINBRIDGE_ARTIFACTS_ROOT"] = artifactsRoot;
                });

            JsonElement root = payload.RootElement;
            Assert.Equal(runId, root.GetProperty("runId").GetString());
            Assert.Equal(artifactsRoot, root.GetProperty("artifactsRoot").GetString());
            Assert.Equal(
                Path.Combine(runRoot, "test-bundle", "okno-test-bundle.json"),
                root.GetProperty("manifestPath").GetString());
            Assert.Equal("fallback_build_cache", root.GetProperty("sourceContextName").GetString());
            Assert.StartsWith(runRoot, root.GetProperty("serverDll").GetString()!, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith(runRoot, root.GetProperty("helperExe").GetString()!, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectoryIfExists(runRoot);
            DeleteDirectoryIfExists(artifactsRoot);
        }
    }

    [Fact]
    public void PrepareOknoTestBundleAllowsNonCanonicalRunRootWithExplicitRunId()
    {
        string repoRoot = GetRepositoryRoot();
        string runId = "resolver-custom-run-root";
        string customRunRoot = Path.Combine(repoRoot, ".tmp", ".codex", "custom-run-root", Guid.NewGuid().ToString("N"));
        string fallbackMarker = Path.Combine(repoRoot, "src", "WinBridge.Server", "bin", "resolver-custom-run-root-" + Guid.NewGuid().ToString("N"));
        string fallbackHelperMarker = Path.Combine(repoRoot, "tests", "WinBridge.SmokeWindowHost", "bin", "resolver-custom-run-root-" + Guid.NewGuid().ToString("N"));

        try
        {
            CreateMarkerFile(Path.Combine(fallbackMarker, "Okno.Server.dll"));
            CreateMarkerFile(Path.Combine(fallbackHelperMarker, "WinBridge.SmokeWindowHost.exe"));

            using JsonDocument payload = InvokeJsonScript(
                Path.Combine(repoRoot, "scripts", "codex", "prepare-okno-test-bundle.ps1"),
                repoRoot,
                startInfo =>
                {
                    startInfo.ArgumentList.Add("-RepoRoot");
                    startInfo.ArgumentList.Add(repoRoot);
                    startInfo.ArgumentList.Add("-RunId");
                    startInfo.ArgumentList.Add(runId);
                    startInfo.ArgumentList.Add("-RunRoot");
                    startInfo.ArgumentList.Add(customRunRoot);
                });

            JsonElement root = payload.RootElement;
            Assert.Equal(runId, root.GetProperty("runId").GetString());
            Assert.Equal(Path.Combine(customRunRoot, "test-bundle", "okno-test-bundle.json"), root.GetProperty("manifestPath").GetString());
            Assert.StartsWith(Path.Combine(customRunRoot, "test-bundle"), root.GetProperty("serverDll").GetString()!, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith(Path.Combine(customRunRoot, "test-bundle"), root.GetProperty("helperExe").GetString()!, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectoryIfExists(customRunRoot);
            DeleteDirectoryIfExists(fallbackMarker);
            DeleteDirectoryIfExists(fallbackHelperMarker);
        }
    }

    [Fact]
    public void ResolveOknoTestBundleRejectsManifestPathCombinedWithRunId()
    {
        string repoRoot = GetRepositoryRoot();
        string manifestPath = Path.Combine(repoRoot, ".tmp", ".codex", "runs", "local", "test-bundle", "okno-test-bundle.json");
        ScriptInvocationResult result = InvokePowerShellScript(
            Path.Combine(repoRoot, "scripts", "codex", "resolve-okno-test-bundle.ps1"),
            repoRoot,
            startInfo =>
            {
                startInfo.ArgumentList.Add("-RepoRoot");
                startInfo.ArgumentList.Add(repoRoot);
                startInfo.ArgumentList.Add("-ManifestPath");
                startInfo.ArgumentList.Add(manifestPath);
                startInfo.ArgumentList.Add("-RunId");
                startInfo.ArgumentList.Add("ci-proof");
            });

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Explicit manifest request is incompatible", result.Stderr, StringComparison.Ordinal);
        Assert.Contains("RunId", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveOknoServerLaunchTargetPrefersAppHostWhenBundleContainsExecutable()
    {
        string repoRoot = GetRepositoryRoot();
        string artifactsRoot = Path.Combine(repoRoot, ".tmp", ".codex", "artifacts", "local");

        using JsonDocument payload = InvokeJsonScript(
            Path.Combine(repoRoot, "scripts", "codex", "resolve-okno-server-launch-target.ps1"),
            repoRoot,
            startInfo =>
            {
                startInfo.ArgumentList.Add("-RepoRoot");
                startInfo.ArgumentList.Add(repoRoot);
                startInfo.ArgumentList.Add("-ArtifactsRoot");
                startInfo.ArgumentList.Add(artifactsRoot);
                startInfo.ArgumentList.Add("-PreferredSourceContextName");
                startInfo.ArgumentList.Add("artifacts_root");
                startInfo.ArgumentList.Add("-ForcePrepare");
            });

        JsonElement root = payload.RootElement;
        Assert.Equal("apphost", root.GetProperty("launchMode").GetString());
        Assert.EndsWith("Okno.Server.exe", root.GetProperty("launchTarget").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("Okno.Server.dll", root.GetProperty("serverDll").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("Okno.Server.exe", root.GetProperty("serverExe").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    private static JsonElement InvokeBundleResolver(string repoRoot, Action<ProcessStartInfo> configure)
    {
        using JsonDocument payload = InvokeJsonScript(
            Path.Combine(repoRoot, "scripts", "codex", "resolve-okno-test-bundle.ps1"),
            repoRoot,
            startInfo =>
            {
                startInfo.ArgumentList.Add("-RepoRoot");
                startInfo.ArgumentList.Add(repoRoot);
                configure(startInfo);
            });
        return payload.RootElement.Clone();
    }

    private static JsonDocument InvokeJsonScript(string scriptPath, string workingDirectory, Action<ProcessStartInfo> configure)
    {
        ScriptInvocationResult result = InvokePowerShellScript(scriptPath, workingDirectory, configure);
        Assert.True(
            result.ExitCode == 0,
            $"Resolver failed. ExitCode={result.ExitCode}. stderr='{result.Stderr.Trim()}', stdout='{result.Stdout.Trim()}'.");
        Assert.False(string.IsNullOrWhiteSpace(result.Stdout));
        return JsonDocument.Parse(result.Stdout);
    }

    private static ScriptInvocationResult InvokePowerShellScript(string scriptPath, string workingDirectory, Action<ProcessStartInfo> configure)
    {
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

        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new ScriptInvocationResult(process.ExitCode, stdout, stderr);
    }

    private static void CreateMarkerFile(string path, DateTime? lastWriteTimeUtc = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "marker", Encoding.UTF8);
        File.SetLastWriteTimeUtc(path, lastWriteTimeUtc ?? DateTime.UtcNow);
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static string PowerShellSingleQuote(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private sealed record ScriptInvocationResult(int ExitCode, string Stdout, string Stderr);

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
}
