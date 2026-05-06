// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Setup.Core;

namespace WinBridge.Server.IntegrationTests;

public sealed partial class ComputerUseWinInstallSurfaceTests
{
    [Fact]
    public async Task SetupShellControllerMapsRuntimeOnlyInstallToSnippetSummaryAsync()
    {
        SetupShellController controller = new(
            () => new ComputerUseWinInstallerStatus(
                1,
                @"C:\Users\user\.codex",
                @"C:\Users\user\AppData\Local\Okno\computer-use-win",
                new ComputerUseWinRuntimeStatus(
                    1,
                    @"C:\Users\user\.codex",
                    @"C:\Users\user\AppData\Local\Okno\computer-use-win",
                    @"C:\Users\user\AppData\Local\Okno\computer-use-win\state\current-runtime.json",
                    false,
                    false,
                    false,
                    null,
                    "current_state_missing",
                    null),
                null,
                null),
            mode =>
            {
                Assert.Equal(ComputerUseWinInstallMode.RuntimeOnly, mode);
                return new ComputerUseWinInstallerResult(
                    1,
                    "install",
                    "runtime_only",
                    @"C:\Users\user\.codex",
                    @"C:\Users\user\AppData\Local\Okno\computer-use-win",
                    @"C:\Users\user\AppData\Local\Okno\computer-use-win\runtimes\win-x64\0.1.0",
                    "0.1.0",
                    "win-x64",
                    null,
                    null,
                    null,
                    null,
                    false,
                    "{ \"mcpServers\": {} }",
                    @"C:\Users\user\AppData\Local\Okno\computer-use-win\receipts\runtimeonly.json",
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow);
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
            () => new ComputerUseWinInstallerStatus(
                1,
                @"C:\Users\user\.codex",
                @"C:\Users\user\AppData\Local\Okno\computer-use-win",
                new ComputerUseWinRuntimeStatus(
                    1,
                    @"C:\Users\user\.codex",
                    @"C:\Users\user\AppData\Local\Okno\computer-use-win",
                    @"C:\Users\user\AppData\Local\Okno\computer-use-win\state\current-runtime.json",
                    true,
                    true,
                    true,
                    @"C:\Users\user\AppData\Local\Okno\computer-use-win\runtimes\win-x64\0.1.0",
                    null,
                    null),
                null,
                null),
            mode =>
            {
                Assert.Equal(ComputerUseWinInstallMode.Codex, mode);
                return new ComputerUseWinInstallerResult(
                    1,
                    "install",
                    "codex",
                    @"C:\Users\user\.codex",
                    @"C:\Users\user\AppData\Local\Okno\computer-use-win",
                    @"C:\Users\user\AppData\Local\Okno\computer-use-win\runtimes\win-x64\0.1.0",
                    "0.1.0",
                    "win-x64",
                    @"C:\Users\user\.codex\plugins\computer-use-win",
                    @"C:\Users\user\.agents\plugins\marketplace.json",
                    "okno-local-installed",
                    "./.codex/plugins/computer-use-win",
                    true,
                    null,
                    @"C:\Users\user\AppData\Local\Okno\computer-use-win\receipts\codex.json",
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow);
            });

        SetupShellInstallSummary summary = await controller.InstallAsync(ComputerUseWinInstallMode.Codex);
        Assert.Equal("Install for Codex completed.", summary.Title);
        Assert.True(summary.RestartRequired);
        Assert.NotNull(summary.PluginSourceRoot);
        Assert.NotNull(summary.MarketplacePath);
        Assert.Null(summary.Snippet);
    }
}
