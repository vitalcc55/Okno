// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace WinBridge.Server.IntegrationTests;

public sealed class RuntimeBundleResolverTests
{
    private const string DebugConfiguration = "debug";
    private const string ReleaseConfiguration = "release";
    private const string ServerProjectName = "WinBridge.Server";
    private const string HelperProjectName = "WinBridge.SmokeWindowHost";
    private const string IntegrationTestsProjectName = "WinBridge.Server.IntegrationTests";
    private const string ServerDllFileName = "Okno.Server.dll";
    private const string ServerExeFileName = "Okno.Server.exe";
    private const string HelperExeFileName = "WinBridge.SmokeWindowHost.exe";
    private const string IntegrationTestsDllFileName = "WinBridge.Server.IntegrationTests.dll";
    private const string FallbackSourceContextName = "fallback_build_cache";
    private const string ArtifactsSourceContextName = "artifacts_root";

    private static readonly string RepositoryRoot = GetRepositoryRoot();
    private static readonly string ResolveBundleScriptPath = ScriptPath("codex", "resolve-okno-test-bundle.ps1");
    private static readonly string PrepareBundleScriptPath = ScriptPath("codex", "prepare-okno-test-bundle.ps1");
    private static readonly string ResolveLaunchTargetScriptPath = ScriptPath("codex", "resolve-okno-server-launch-target.ps1");
    private static readonly string CommonScriptPath = ScriptPath("common.ps1");

    [Fact]
    public void ResolveOknoTestBundleRunIdOverrideDoesNotMixAmbientArtifactsState()
    {
        const string explicitRunId = "resolver-explicit-run";
        const string ambientRunId = "resolver-ambient-run";
        using TemporaryDirectories cleanup = new();

        string explicitRunRoot = cleanup.Add(RunRootFor(explicitRunId));
        string fallbackRelativePath = UniqueName("resolver-fallback-marker");
        string fallbackMarker = cleanup.Add(RepoPath("src", ServerProjectName, "bin", fallbackRelativePath));
        string fallbackHelperMarker = cleanup.Add(RepoPath("tests", HelperProjectName, "bin", fallbackRelativePath));

        CreateMarkerFile(Path.Combine(fallbackMarker, ServerDllFileName));
        CreateMarkerFile(Path.Combine(fallbackHelperMarker, HelperExeFileName));

        JsonElement payload = InvokeBundleResolver(
            startInfo =>
            {
                AddArguments(startInfo, "-RunId", explicitRunId);
                startInfo.Environment["WINBRIDGE_RUN_ID"] = ambientRunId;
                startInfo.Environment["WINBRIDGE_RUN_ROOT"] = RunRootFor(ambientRunId);
                startInfo.Environment["WINBRIDGE_ARTIFACTS_ROOT"] = ArtifactsRootFor(ambientRunId);
            });

        Assert.Equal(explicitRunId, JsonString(payload, "runId"));
        Assert.Equal(explicitRunRoot, JsonString(payload, "runRoot"));
        Assert.True(string.IsNullOrEmpty(JsonNullableString(payload, "artifactsRoot")));
        Assert.Equal(FallbackSourceContextName, JsonString(payload, "preferredSourceContext"));
    }

    [Fact]
    public void ResolveOknoTestBundleAssemblyBaseDirectoryWinsOverAmbientDllEnvironment()
    {
        TestRunContext context = CreateRunContext("resolver-assembly-run");
        using TemporaryDirectories cleanup = new(context.RunRoot, context.ArtifactsRoot);

        string assemblyBaseDirectory = ArtifactOutputDirectory(
            context.ArtifactsRoot,
            IntegrationTestsProjectName,
            DebugConfiguration);
        string ambientRoot = cleanup.Add(CodexPath("resolver-ambient-dll", Guid.NewGuid().ToString("N")));
        string ambientServerDll = Path.Combine(ambientRoot, "server", ServerDllFileName);
        string ambientHelperExe = Path.Combine(ambientRoot, "helper", HelperExeFileName);

        CreateServerArtifact(context.ArtifactsRoot, DebugConfiguration);
        CreateHelperArtifact(context.ArtifactsRoot, DebugConfiguration);
        CreateMarkerFile(Path.Combine(assemblyBaseDirectory, IntegrationTestsDllFileName));
        CreateMarkerFile(ambientServerDll);
        CreateMarkerFile(ambientHelperExe);

        JsonElement payload = InvokeBundleResolver(
            startInfo =>
            {
                AddArguments(startInfo, "-AssemblyBaseDirectory", assemblyBaseDirectory);
                startInfo.Environment["WINBRIDGE_SERVER_DLL"] = ambientServerDll;
                startInfo.Environment["WINBRIDGE_SMOKE_HELPER_EXE"] = ambientHelperExe;
            });

        Assert.Equal(context.RunId, JsonString(payload, "runId"));
        Assert.Equal(context.RunRoot, JsonString(payload, "runRoot"));
        Assert.Equal(context.ArtifactsRoot, JsonString(payload, "artifactsRoot"));
        Assert.Equal(ArtifactsSourceContextName, JsonString(payload, "preferredSourceContext"));
        Assert.StartsWith(context.RunRoot, JsonString(payload, "serverDll"), StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(context.RunRoot, JsonString(payload, "helperExe"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PrepareOknoTestBundleSelectsOneCoherentRelativeOutputContext()
    {
        TestRunContext context = CreateRunContext("resolver-coherent-pair");
        using TemporaryDirectories cleanup = new(context.RunRoot, context.ArtifactsRoot);

        DateTime utcNow = DateTime.UtcNow;
        CreateServerArtifact(context.ArtifactsRoot, DebugConfiguration, utcNow.AddMinutes(10));
        CreateHelperArtifact(context.ArtifactsRoot, DebugConfiguration, utcNow);
        CreateServerArtifact(context.ArtifactsRoot, ReleaseConfiguration, utcNow.AddMinutes(5));
        CreateHelperArtifact(context.ArtifactsRoot, ReleaseConfiguration, utcNow.AddMinutes(5));

        using JsonDocument payload = InvokePrepareBundle(
            startInfo => AddArguments(startInfo, "-RunId", context.RunId, "-ArtifactsRoot", context.ArtifactsRoot));
        BundleManifestSources manifestSources = ReadBundleManifestSources(payload.RootElement);

        Assert.EndsWith(
            Path.Combine(ServerProjectName, ReleaseConfiguration),
            manifestSources.ServerSourceDirectory,
            StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(
            Path.Combine(HelperProjectName, ReleaseConfiguration),
            manifestSources.HelperSourceDirectory,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveOknoTestBundleAssemblyBaseDirectoryPinsSameRelativeOutputContext()
    {
        TestRunContext context = CreateRunContext("resolver-assembly-relative-context");
        using TemporaryDirectories cleanup = new(context.RunRoot, context.ArtifactsRoot);

        string releaseAssemblyBaseDirectory = ArtifactOutputDirectory(
            context.ArtifactsRoot,
            IntegrationTestsProjectName,
            ReleaseConfiguration);
        DateTime utcNow = DateTime.UtcNow;
        CreateServerArtifact(context.ArtifactsRoot, DebugConfiguration, utcNow.AddMinutes(10));
        CreateHelperArtifact(context.ArtifactsRoot, DebugConfiguration, utcNow.AddMinutes(10));
        CreateServerArtifact(context.ArtifactsRoot, ReleaseConfiguration, utcNow.AddMinutes(5));
        CreateHelperArtifact(context.ArtifactsRoot, ReleaseConfiguration, utcNow.AddMinutes(5));
        CreateMarkerFile(Path.Combine(releaseAssemblyBaseDirectory, IntegrationTestsDllFileName), utcNow.AddMinutes(6));

        JsonElement payload = InvokeBundleResolver(
            startInfo => AddArguments(startInfo, "-AssemblyBaseDirectory", releaseAssemblyBaseDirectory));
        BundleManifestSources manifestSources = ReadBundleManifestSources(payload);

        Assert.EndsWith(
            Path.Combine(ServerProjectName, ReleaseConfiguration),
            manifestSources.ServerSourceDirectory,
            StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(
            Path.Combine(HelperProjectName, ReleaseConfiguration),
            manifestSources.HelperSourceDirectory,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveOknoTestBundleFallbackAssemblyBaseDirectoryPinsSameRelativeOutputContext()
    {
        TestRunContext context = CreateRunContext("resolver-fallback-assembly-relative-context");
        using TemporaryDirectories cleanup = new(context.RunRoot);

        string marker = UniqueName("resolver-fallback-relative");
        string serverBinRoot = cleanup.Add(RepoPath("src", ServerProjectName, "bin", marker));
        string helperBinRoot = cleanup.Add(RepoPath("tests", HelperProjectName, "bin", marker));
        string testBinRoot = cleanup.Add(RepoPath("tests", IntegrationTestsProjectName, "bin", marker));
        string releaseAssemblyBaseDirectory = Path.Combine(testBinRoot, ReleaseConfiguration);

        DateTime utcNow = DateTime.UtcNow;
        CreateMarkerFile(Path.Combine(serverBinRoot, DebugConfiguration, ServerDllFileName), utcNow.AddMinutes(10));
        CreateMarkerFile(Path.Combine(helperBinRoot, DebugConfiguration, HelperExeFileName), utcNow.AddMinutes(10));
        CreateMarkerFile(Path.Combine(serverBinRoot, ReleaseConfiguration, ServerDllFileName), utcNow.AddMinutes(5));
        CreateMarkerFile(Path.Combine(helperBinRoot, ReleaseConfiguration, HelperExeFileName), utcNow.AddMinutes(5));
        CreateMarkerFile(Path.Combine(releaseAssemblyBaseDirectory, IntegrationTestsDllFileName), utcNow.AddMinutes(6));

        JsonElement payload = InvokeBundleResolver(
            startInfo => AddArguments(
                startInfo,
                "-RunId",
                context.RunId,
                "-AssemblyBaseDirectory",
                releaseAssemblyBaseDirectory));
        BundleManifestSources manifestSources = ReadBundleManifestSources(payload);

        Assert.Equal(
            Path.Combine(serverBinRoot, ReleaseConfiguration),
            manifestSources.ServerSourceDirectory,
            StringComparer.OrdinalIgnoreCase);
        Assert.Equal(
            Path.Combine(helperBinRoot, ReleaseConfiguration),
            manifestSources.HelperSourceDirectory,
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveWinBridgeVerificationContextPinsBundleSourceToExecutedConfiguration()
    {
        TestRunContext context = CreateRunContext("resolver-verification-context");
        using TemporaryDirectories cleanup = new(context.RunRoot, context.ArtifactsRoot);

        string scriptRoot = cleanup.Add(CodexPath("resolver-tests", Guid.NewGuid().ToString("N")));
        string scriptPath = Path.Combine(scriptRoot, "resolve-verification-context.ps1");

        DateTime utcNow = DateTime.UtcNow;
        CreateIntegrationTestAssemblyArtifact(context.ArtifactsRoot, DebugConfiguration, utcNow);
        CreateIntegrationTestAssemblyArtifact(context.ArtifactsRoot, ReleaseConfiguration, utcNow.AddMinutes(10));
        WriteVerificationContextProbeScript(scriptRoot, scriptPath, context.ArtifactsRoot);

        using JsonDocument payload = InvokeJsonScript(scriptPath, _ => { });
        JsonElement root = payload.RootElement;
        string[] dotnetTestArguments = JsonStringArray(root, "dotnetTestArguments");

        Assert.Equal(DebugConfiguration, JsonString(root, "bundleSourceRelativePath"), ignoreCase: true);
        Assert.Contains("--configuration", dotnetTestArguments);
        Assert.Contains("Debug", dotnetTestArguments);
        Assert.EndsWith(
            Path.Combine(IntegrationTestsProjectName, DebugConfiguration, IntegrationTestsDllFileName),
            JsonString(root, "integrationTestAssembly"),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveWinBridgeVerificationContextFailsClosedWhenArtifactsRootDoesNotContainStagedIntegrationAssembly()
    {
        TestRunContext context = CreateRunContext("resolver-verification-context-missing-artifacts");
        using TemporaryDirectories cleanup = new(context.RunRoot, context.ArtifactsRoot);

        string scriptRoot = cleanup.Add(CodexPath("resolver-tests", Guid.NewGuid().ToString("N")));
        string scriptPath = Path.Combine(scriptRoot, "resolve-verification-context-missing-artifacts.ps1");
        WriteVerificationContextProbeScript(scriptRoot, scriptPath, context.ArtifactsRoot);

        ScriptInvocationResult result = InvokePowerShellScript(scriptPath, _ => { });

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("expected staged test artifacts", result.Stderr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(IntegrationTestsDllFileName, result.Stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveOknoTestBundleRejectsConflictingExplicitExecutionContext()
    {
        ScriptInvocationResult result = InvokeBundleResolverRaw(
            startInfo => AddArguments(
                startInfo,
                "-RunRoot",
                RunRootFor("local"),
                "-ArtifactsRoot",
                ArtifactsRootFor("ci-proof")));

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Explicit execution context is internally inconsistent", result.Stderr, StringComparison.Ordinal);
        Assert.Contains("RunRoot=", result.Stderr, StringComparison.Ordinal);
        Assert.Contains("ArtifactsRoot=", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void PrepareOknoTestBundlePreferredSourceContextDoesNotResetAmbientRunContext()
    {
        TestRunContext context = CreateRunContext("resolver-preferred-source-run");
        using TemporaryDirectories cleanup = new(context.RunRoot, context.ArtifactsRoot);

        string fallbackRelativePath = UniqueName("resolver-preferred-source");
        string fallbackMarker = cleanup.Add(RepoPath("src", ServerProjectName, "bin", fallbackRelativePath));
        string fallbackHelperMarker = cleanup.Add(RepoPath("tests", HelperProjectName, "bin", fallbackRelativePath));

        CreateMarkerFile(Path.Combine(fallbackMarker, ServerDllFileName));
        CreateMarkerFile(Path.Combine(fallbackHelperMarker, HelperExeFileName));

        using JsonDocument payload = InvokePrepareBundle(
            startInfo =>
            {
                AddArguments(startInfo, "-PreferredSourceContextName", FallbackSourceContextName);
                startInfo.Environment["WINBRIDGE_RUN_ID"] = context.RunId;
                startInfo.Environment["WINBRIDGE_RUN_ROOT"] = context.RunRoot;
                startInfo.Environment["WINBRIDGE_ARTIFACTS_ROOT"] = context.ArtifactsRoot;
            });

        JsonElement root = payload.RootElement;
        Assert.Equal(context.RunId, JsonString(root, "runId"));
        Assert.Equal(context.ArtifactsRoot, JsonString(root, "artifactsRoot"));
        Assert.Equal(context.BundleManifestPath, JsonString(root, "manifestPath"));
        Assert.Equal(FallbackSourceContextName, JsonString(root, "sourceContextName"));
        Assert.StartsWith(context.RunRoot, JsonString(root, "serverDll"), StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(context.RunRoot, JsonString(root, "helperExe"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PrepareOknoTestBundleAllowsNonCanonicalRunRootWithExplicitRunId()
    {
        const string runId = "resolver-custom-run-root";
        using TemporaryDirectories cleanup = new();

        string customRunRoot = cleanup.Add(CodexPath("custom-run-root", Guid.NewGuid().ToString("N")));
        string fallbackRelativePath = UniqueName("resolver-custom-run-root");
        string fallbackMarker = cleanup.Add(RepoPath("src", ServerProjectName, "bin", fallbackRelativePath));
        string fallbackHelperMarker = cleanup.Add(RepoPath("tests", HelperProjectName, "bin", fallbackRelativePath));

        CreateMarkerFile(Path.Combine(fallbackMarker, ServerDllFileName));
        CreateMarkerFile(Path.Combine(fallbackHelperMarker, HelperExeFileName));

        using JsonDocument payload = InvokePrepareBundle(
            startInfo => AddArguments(startInfo, "-RunId", runId, "-RunRoot", customRunRoot));

        JsonElement root = payload.RootElement;
        string customBundleDirectory = BundleDirectory(customRunRoot);
        Assert.Equal(runId, JsonString(root, "runId"));
        Assert.Equal(Path.Combine(customBundleDirectory, "okno-test-bundle.json"), JsonString(root, "manifestPath"));
        Assert.StartsWith(customBundleDirectory, JsonString(root, "serverDll"), StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(customBundleDirectory, JsonString(root, "helperExe"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveOknoTestBundleRejectsManifestPathCombinedWithRunId()
    {
        ScriptInvocationResult result = InvokeBundleResolverRaw(
            startInfo => AddArguments(
                startInfo,
                "-ManifestPath",
                Path.Combine(RunRootFor("local"), "test-bundle", "okno-test-bundle.json"),
                "-RunId",
                "ci-proof"));

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Explicit manifest request is incompatible", result.Stderr, StringComparison.Ordinal);
        Assert.Contains("RunId", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveOknoServerLaunchTargetPrefersAppHostWhenBundleContainsExecutable()
    {
        using JsonDocument payload = InvokeLaunchTargetResolver(
            startInfo => AddArguments(
                startInfo,
                "-ArtifactsRoot",
                ArtifactsRootFor("local"),
                "-PreferredSourceContextName",
                ArtifactsSourceContextName,
                "-ForcePrepare"));

        JsonElement root = payload.RootElement;
        Assert.Equal("apphost", JsonString(root, "launchMode"));
        Assert.EndsWith(ServerExeFileName, JsonString(root, "launchTarget"), StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(ServerDllFileName, JsonString(root, "serverDll"), StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(ServerExeFileName, JsonString(root, "serverExe"), StringComparison.OrdinalIgnoreCase);
    }

    private static JsonElement InvokeBundleResolver(Action<ProcessStartInfo> configure)
    {
        using JsonDocument payload = InvokeJsonScript(ResolveBundleScriptPath, WithRepoRootArgument(configure));
        return payload.RootElement.Clone();
    }

    private static ScriptInvocationResult InvokeBundleResolverRaw(Action<ProcessStartInfo> configure) =>
        InvokePowerShellScript(ResolveBundleScriptPath, WithRepoRootArgument(configure));

    private static JsonDocument InvokePrepareBundle(Action<ProcessStartInfo> configure) =>
        InvokeJsonScript(PrepareBundleScriptPath, WithRepoRootArgument(configure));

    private static JsonDocument InvokeLaunchTargetResolver(Action<ProcessStartInfo> configure) =>
        InvokeJsonScript(ResolveLaunchTargetScriptPath, WithRepoRootArgument(configure));

    private static Action<ProcessStartInfo> WithRepoRootArgument(Action<ProcessStartInfo> configure) =>
        startInfo =>
        {
            AddArguments(startInfo, "-RepoRoot", RepositoryRoot);
            configure(startInfo);
        };

    private static JsonDocument InvokeJsonScript(string scriptPath, Action<ProcessStartInfo> configure)
    {
        ScriptInvocationResult result = InvokePowerShellScript(scriptPath, configure);
        Assert.True(
            result.ExitCode == 0,
            $"Resolver failed. ExitCode={result.ExitCode}. stderr='{result.Stderr.Trim()}', stdout='{result.Stdout.Trim()}'.");
        Assert.False(string.IsNullOrWhiteSpace(result.Stdout));
        return JsonDocument.Parse(result.Stdout);
    }

    private static ScriptInvocationResult InvokePowerShellScript(string scriptPath, Action<ProcessStartInfo> configure)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "powershell",
            WorkingDirectory = RepositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        AddArguments(
            startInfo,
            "-NoLogo",
            "-NoProfile",
            "-NonInteractive",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            scriptPath);
        configure(startInfo);

        using Process process = new() { StartInfo = startInfo };
        process.Start();

        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();

        return new ScriptInvocationResult(process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
    }

    private static void WriteVerificationContextProbeScript(string scriptRoot, string scriptPath, string artifactsRoot)
    {
        Directory.CreateDirectory(scriptRoot);
        File.WriteAllText(
            scriptPath,
            string.Join(
                Environment.NewLine,
                "$ErrorActionPreference = 'Stop'",
                $". '{PowerShellSingleQuote(CommonScriptPath)}'",
                $"$env:WINBRIDGE_ARTIFACTS_ROOT = '{PowerShellSingleQuote(artifactsRoot)}'",
                $"$context = Resolve-WinBridgeVerificationContext -RepoRoot '{PowerShellSingleQuote(RepositoryRoot)}'",
                "[pscustomobject]@{",
                "    bundleSourceRelativePath = $context.BundleSourceRelativePath",
                "    dotnetTestArguments = $context.DotnetTestArguments",
                "    integrationTestAssembly = $context.IntegrationTestAssembly",
                "} | ConvertTo-Json -Depth 4 -Compress"),
            Encoding.UTF8);
    }

    private static BundleManifestSources ReadBundleManifestSources(JsonElement payload)
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(JsonString(payload, "manifestPath")));
        return new BundleManifestSources(
            JsonString(manifest.RootElement, "serverSourceDirectory"),
            JsonString(manifest.RootElement, "helperSourceDirectory"));
    }

    private static void CreateServerArtifact(string artifactsRoot, string configuration, DateTime? lastWriteTimeUtc = null) =>
        CreateMarkerFile(Path.Combine(ArtifactOutputDirectory(artifactsRoot, ServerProjectName, configuration), ServerDllFileName), lastWriteTimeUtc);

    private static void CreateHelperArtifact(string artifactsRoot, string configuration, DateTime? lastWriteTimeUtc = null) =>
        CreateMarkerFile(Path.Combine(ArtifactOutputDirectory(artifactsRoot, HelperProjectName, configuration), HelperExeFileName), lastWriteTimeUtc);

    private static void CreateIntegrationTestAssemblyArtifact(string artifactsRoot, string configuration, DateTime? lastWriteTimeUtc = null) =>
        CreateMarkerFile(
            Path.Combine(ArtifactOutputDirectory(artifactsRoot, IntegrationTestsProjectName, configuration), IntegrationTestsDllFileName),
            lastWriteTimeUtc);

    private static void CreateMarkerFile(string path, DateTime? lastWriteTimeUtc = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "marker", Encoding.UTF8);
        File.SetLastWriteTimeUtc(path, lastWriteTimeUtc ?? DateTime.UtcNow);
    }

    private static void AddArguments(ProcessStartInfo startInfo, params string[] arguments)
    {
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
    }

    private static string JsonString(JsonElement element, string propertyName) =>
        element.GetProperty(propertyName).GetString()
        ?? throw new InvalidOperationException($"JSON property '{propertyName}' was not returned.");

    private static string? JsonNullableString(JsonElement element, string propertyName) =>
        element.GetProperty(propertyName).GetString();

    private static string[] JsonStringArray(JsonElement element, string propertyName) =>
        element.GetProperty(propertyName)
            .EnumerateArray()
            .Select(value => value.GetString() ?? string.Empty)
            .ToArray();

    private static TestRunContext CreateRunContext(string runId) =>
        new(runId, RunRootFor(runId), ArtifactsRootFor(runId));

    private static string RunRootFor(string runId) => CodexPath("runs", runId);

    private static string ArtifactsRootFor(string runId) => CodexPath("artifacts", runId);

    private static string BundleDirectory(string runRoot) => Path.Combine(runRoot, "test-bundle");

    private static string ArtifactOutputDirectory(string artifactsRoot, string projectName, string configuration) =>
        Path.Combine(artifactsRoot, "bin", projectName, configuration);

    private static string UniqueName(string prefix) => prefix + "-" + Guid.NewGuid().ToString("N");

    private static string ScriptPath(params string[] parts) => Path.Combine([RepositoryRoot, "scripts", .. parts]);

    private static string CodexPath(params string[] parts) => Path.Combine([RepositoryRoot, ".tmp", ".codex", .. parts]);

    private static string RepoPath(params string[] parts) => Path.Combine([RepositoryRoot, .. parts]);

    private static string PowerShellSingleQuote(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
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

    private sealed class TemporaryDirectories : IDisposable
    {
        private readonly List<string> paths = [];

        public TemporaryDirectories(params string[] initialPaths)
        {
            foreach (string path in initialPaths)
            {
                Add(path);
            }
        }

        public string Add(string path)
        {
            paths.Add(path);
            return path;
        }

        public void Dispose()
        {
            for (int i = paths.Count - 1; i >= 0; --i)
            {
                DeleteDirectoryIfExists(paths[i]);
            }
        }
    }

    private sealed record TestRunContext(string RunId, string RunRoot, string ArtifactsRoot)
    {
        public string BundleManifestPath => Path.Combine(BundleDirectory(RunRoot), "okno-test-bundle.json");
    }

    private sealed record BundleManifestSources(string ServerSourceDirectory, string HelperSourceDirectory);

    private sealed record ScriptInvocationResult(int ExitCode, string Stdout, string Stderr);
}
