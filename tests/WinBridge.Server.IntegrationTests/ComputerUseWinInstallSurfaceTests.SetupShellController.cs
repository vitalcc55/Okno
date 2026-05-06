// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Setup.Core;

namespace WinBridge.Server.IntegrationTests;

public sealed partial class ComputerUseWinInstallSurfaceTests
{
    private const string SetupCodexRoot = @"C:\Users\user\.codex";
    private const string SetupAppRoot = @"C:\Users\user\AppData\Local\Okno\computer-use-win";
    private const string SetupRuntimeVersion = "0.1.0";
    private const string SetupRuntimeRid = "win-x64";
    private const string SetupRuntimeRoot = $@"{SetupAppRoot}\runtimes\{SetupRuntimeRid}\{SetupRuntimeVersion}";
    private const string SetupStatePath = $@"{SetupAppRoot}\state\current-runtime.json";
    private const string SetupRuntimeSnippet = "{ \"mcpServers\": {} }";

    [Fact]
    public async Task SetupShellControllerMapsRuntimeOnlyInstallToSnippetSummaryAsync()
    {
        SetupShellController controller = new(
            () => CreateSetupStatus(runtimeAvailable: false, runtimeFailureReason: "current_state_missing"),
            mode =>
            {
                Assert.Equal(ComputerUseWinInstallMode.RuntimeOnly, mode);
                return CreateSetupResult(
                    installModeName: "runtime_only",
                    pluginSourceRoot: null,
                    marketplacePath: null,
                    marketplaceEntryId: null,
                    relativePluginPath: null,
                    restartRequired: false,
                    snippet: SetupRuntimeSnippet,
                    receiptFileName: "runtimeonly.json");
            });

        SetupShellInstallSummary summary = await controller.InstallAsync(ComputerUseWinInstallMode.RuntimeOnly);

        Assert.Equal("Runtime-only install completed.", summary.Title);
        Assert.NotNull(summary.Snippet);
        Assert.False(summary.RestartRequired);
        Assert.Null(summary.PluginSourceRoot);
    }

    [Fact]
    public async Task SetupShellControllerMapsCodexInstallToRestartSummaryAsync()
    {
        SetupShellController controller = new(
            () => CreateSetupStatus(runtimeAvailable: true, runtimeFailureReason: null),
            mode =>
            {
                Assert.Equal(ComputerUseWinInstallMode.Codex, mode);
                return CreateSetupResult(
                    installModeName: "codex",
                    pluginSourceRoot: $@"{SetupCodexRoot}\plugins\computer-use-win",
                    marketplacePath: @"C:\Users\user\.agents\plugins\marketplace.json",
                    marketplaceEntryId: "okno-local-installed",
                    relativePluginPath: "./.codex/plugins/computer-use-win",
                    restartRequired: true,
                    snippet: null,
                    receiptFileName: "codex.json");
            });

        SetupShellInstallSummary summary = await controller.InstallAsync(ComputerUseWinInstallMode.Codex);

        Assert.Equal("Install for Codex completed.", summary.Title);
        Assert.True(summary.RestartRequired);
        Assert.NotNull(summary.PluginSourceRoot);
        Assert.NotNull(summary.MarketplacePath);
        Assert.Null(summary.Snippet);
    }

    private static ComputerUseWinInstallerStatus CreateSetupStatus(bool runtimeAvailable, string? runtimeFailureReason) =>
        new(
            1,
            SetupCodexRoot,
            SetupAppRoot,
            new ComputerUseWinRuntimeStatus(
                1, SetupCodexRoot, SetupAppRoot, SetupStatePath,
                runtimeAvailable, runtimeAvailable, runtimeAvailable,
                runtimeAvailable ? SetupRuntimeRoot : null,
                runtimeFailureReason,
                null),
            null,
            null);

    private static ComputerUseWinInstallerResult CreateSetupResult(
        string installModeName, string? pluginSourceRoot, string? marketplacePath, string? marketplaceEntryId,
        string? relativePluginPath, bool restartRequired, string? snippet, string receiptFileName)
    {
        DateTimeOffset completedAt = DateTimeOffset.UtcNow;

        return new(
            1, "install", installModeName, SetupCodexRoot, SetupAppRoot, SetupRuntimeRoot,
            SetupRuntimeVersion, SetupRuntimeRid, pluginSourceRoot, marketplacePath,
            marketplaceEntryId, relativePluginPath, restartRequired, snippet,
            $@"{SetupAppRoot}\receipts\{receiptFileName}", completedAt, completedAt);
    }
}
