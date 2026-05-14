// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.InstallSurface.AcceptanceTests;

public sealed partial class ComputerUseWinInstallSurfaceTests
{
    [Fact]
    public void ReleaseVerifyFailsClosedWhenRequestedVersionDoesNotMatchRepoSourceOfTruth()
    {
        string repoRoot = GetRepositoryRoot();

        ScriptInvocationResult result = InvokePowerShellScript(
            Path.Combine(repoRoot, "scripts", "release-verify.ps1"),
            repoRoot,
            startInfo => AddProcessArguments(startInfo, "-Version", "0.0.0"));

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("does not match repo source-of-truth version", result.Stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VersionInvariantFailsClosedWhenAnySourceOfTruthVersionIsBlank()
    {
        string tempRepoRoot = CreateControlPlaneTempRepoRoot("blank-version-source");
        string probeScriptPath = Path.Combine(tempRepoRoot, "probe-version-invariant.ps1");

        try
        {
            WriteMinimalVersionStateRepo(tempRepoRoot, pluginVersion: "");
            File.WriteAllText(
                probeScriptPath,
                $$"""
                . "{{Path.Combine(GetRepositoryRoot(), "scripts", "common.ps1")}}"
                Assert-WinBridgeComputerUseWinVersionState -RepoRoot "{{tempRepoRoot}}" | Out-Null
                """);

            ScriptInvocationResult result = InvokePowerShellScript(
                probeScriptPath,
                tempRepoRoot,
                _ => { });

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("source-of-truth version is missing", result.Stderr, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectoryIfExists(tempRepoRoot);
        }
    }

    [Fact]
    public void ReleaseWorkflowPassesResolvedVersionIntoReleaseVerifyGate()
    {
        string workflowPath = Path.Combine(GetRepositoryRoot(), ".github", "workflows", "release-computer-use-win-runtime.yml");
        string workflow = File.ReadAllText(workflowPath);

        Assert.Contains(
            "scripts/release-verify.ps1 -Version \"${{ steps.version.outputs.version }}\"",
            workflow,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseWorkflowUsesCanonicalTagParsingInsteadOfTrimStartNormalization()
    {
        string workflowPath = Path.Combine(GetRepositoryRoot(), ".github", "workflows", "release-computer-use-win-runtime.yml");
        string workflow = File.ReadAllText(workflowPath);

        Assert.DoesNotContain("TrimStart('v')", workflow, StringComparison.Ordinal);
        Assert.Contains("does not match the canonical 'v<semver>' format", workflow, StringComparison.Ordinal);
        Assert.Contains("[System.Text.RegularExpressions.Regex]::Match", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void MaterializeCacheCopyRejectsSourcePluginRootAsCacheDestination()
    {
        string repoRoot = GetRepositoryRoot();
        string sourcePluginRoot = GetInstallSurfaceComputerUseWinPluginRoot(repoRoot);

        ScriptInvocationResult result = InvokePowerShellScript(
            GetInstallSurfaceCodexScriptPath(repoRoot, "materialize-computer-use-win-cache-copy.ps1"),
            repoRoot,
            startInfo => AddProcessArguments(startInfo, "-SourcePluginRoot", sourcePluginRoot, "-CachePluginRoot", sourcePluginRoot));

        Assert.NotEqual(0, result.ExitCode);
        Assert.True(
            result.Stderr.Contains("unsafe cache copy path combination", StringComparison.OrdinalIgnoreCase)
            || result.Stderr.Contains("must stay under the codex cache root", StringComparison.OrdinalIgnoreCase),
            $"Unexpected stderr='{result.Stderr}'.");
    }

    [Fact]
    public void MaterializeCacheCopyRejectsDestinationOutsideCodexCacheRoot()
    {
        string repoRoot = GetRepositoryRoot();
        string sourcePluginRoot = GetInstallSurfaceComputerUseWinPluginRoot(repoRoot);
        string invalidCacheRoot = Path.Combine(repoRoot, ".tmp", ".codex", "tests", "materialize-cache-copy-invalid-root", Guid.NewGuid().ToString("N"));

        ScriptInvocationResult result = InvokePowerShellScript(
            GetInstallSurfaceCodexScriptPath(repoRoot, "materialize-computer-use-win-cache-copy.ps1"),
            repoRoot,
            startInfo => AddProcessArguments(startInfo, "-SourcePluginRoot", sourcePluginRoot, "-CachePluginRoot", invalidCacheRoot));

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("must stay under the codex cache root", result.Stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MaterializeCacheCopyRejectsDifferentPluginCacheLeafWithinCodexCache()
    {
        string repoRoot = GetRepositoryRoot();
        string sourcePluginRoot = GetInstallSurfaceComputerUseWinPluginRoot(repoRoot);
        string cacheBaseRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "plugins", "cache");
        string differentPluginCacheLeaf = Path.Combine(cacheBaseRoot, "other-plugin-local", "other-plugin", "0.2.3");

        ScriptInvocationResult result = InvokePowerShellScript(
            GetInstallSurfaceCodexScriptPath(repoRoot, "materialize-computer-use-win-cache-copy.ps1"),
            repoRoot,
            startInfo => AddProcessArguments(startInfo, "-SourcePluginRoot", sourcePluginRoot, "-CachePluginRoot", differentPluginCacheLeaf));

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("must resolve to the owned computer-use-win cache root", result.Stderr, StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateControlPlaneTempRepoRoot(string scenarioName)
    {
        return Path.Combine(GetRepositoryRoot(), ".tmp", ".codex", "tests", "control-plane", scenarioName, Guid.NewGuid().ToString("N"));
    }

    private static void WriteMinimalVersionStateRepo(string tempRepoRoot, string buildVersion = "0.2.3", string pluginVersion = "0.2.3", string runtimeReleaseVersion = "0.2.3")
    {
        Directory.CreateDirectory(tempRepoRoot);
        File.WriteAllText(
            Path.Combine(tempRepoRoot, "Directory.Build.props"),
            $$"""
            <Project>
              <PropertyGroup>
                <Version>{{buildVersion}}</Version>
              </PropertyGroup>
            </Project>
            """);

        string pluginRoot = Path.Combine(tempRepoRoot, "plugins", "computer-use-win");
        Directory.CreateDirectory(Path.Combine(pluginRoot, ".codex-plugin"));
        File.WriteAllText(
            Path.Combine(pluginRoot, ".codex-plugin", "plugin.json"),
            $$"""
            {
              "name": "computer-use-win",
              "version": "{{pluginVersion}}"
            }
            """);
        File.WriteAllText(
            Path.Combine(pluginRoot, "runtime-release.json"),
            $$"""
            {
              "formatVersion": 1,
              "version": "{{runtimeReleaseVersion}}",
              "rid": "win-x64",
              "tag": "v{{runtimeReleaseVersion}}",
              "assetName": "okno-computer-use-win-runtime-{{runtimeReleaseVersion}}-win-x64.zip",
              "downloadUrl": "https://example.invalid/okno-computer-use-win-runtime-{{runtimeReleaseVersion}}-win-x64.zip",
              "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
              "serverExeRelativePath": "Okno.Server.exe",
              "bundleManifestName": "okno-runtime-bundle-manifest.json"
            }
            """);
    }
}
