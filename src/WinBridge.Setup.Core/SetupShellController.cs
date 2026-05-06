namespace WinBridge.Setup.Core;

public sealed class SetupShellController
{
    private readonly Func<ComputerUseWinInstallerStatus> statusProvider;
    private readonly Func<ComputerUseWinInstallMode, ComputerUseWinInstallerResult> installRunner;

    public SetupShellController()
        : this(
            () => new ComputerUseWinInstallerService().GetStatus(),
            mode =>
            {
                ComputerUseWinInstallerService installer = new();
                return mode switch
                {
                    ComputerUseWinInstallMode.Codex => installer.InstallCodex(),
                    ComputerUseWinInstallMode.RuntimeOnly => installer.InstallRuntimeOnly(),
                    _ => throw new InvalidOperationException($"Unsupported install mode '{mode}'."),
                };
            })
    {
    }

    public SetupShellController(
        Func<ComputerUseWinInstallerStatus> statusProvider,
        Func<ComputerUseWinInstallMode, ComputerUseWinInstallerResult> installRunner)
    {
        this.statusProvider = statusProvider;
        this.installRunner = installRunner;
    }

    public SetupShellStatusSnapshot GetStatusSnapshot()
    {
        ComputerUseWinInstallerStatus status = statusProvider();
        string headline;
        string detail;

        if (status.CodexInstall is not null)
        {
            headline = "Codex plugin is already installed.";
            detail = $"Plugin root: {status.CodexInstall.PluginSourceRoot}";
        }
        else if (status.RuntimeStatus.IsInstalled && status.RuntimeStatus.IsUsable && status.RuntimeStatus.IsCompatible)
        {
            headline = "Shared runtime is ready.";
            detail = $"Runtime root: {status.RuntimeStatus.EffectiveRuntimeRoot}";
        }
        else
        {
            headline = "Ready to install Okno.";
            detail = "Choose a mode and start the installer.";
        }

        return new SetupShellStatusSnapshot(
            status.CodexHome,
            status.RuntimeStoreRoot,
            status.RuntimeStatus.IsInstalled && status.RuntimeStatus.IsUsable && status.RuntimeStatus.IsCompatible,
            status.CodexInstall is not null,
            headline,
            detail);
    }

    public Task<SetupShellInstallSummary> InstallAsync(ComputerUseWinInstallMode mode, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            ComputerUseWinInstallerResult result = installRunner(mode);
            return mode switch
            {
                ComputerUseWinInstallMode.Codex => new SetupShellInstallSummary(
                    "Install for Codex completed.",
                    "Restart Codex to load the installed plugin from your personal marketplace.",
                    result.RuntimeRoot,
                    result.PluginSourceRoot,
                    result.MarketplacePath,
                    null,
                    result.RestartRequired),
                ComputerUseWinInstallMode.RuntimeOnly => new SetupShellInstallSummary(
                    "Runtime-only install completed.",
                    "Use the generated MCP snippet in your client configuration.",
                    result.RuntimeRoot,
                    null,
                    null,
                    result.Snippet,
                    false),
                _ => throw new InvalidOperationException($"Unsupported install mode '{mode}'."),
            };
        }, cancellationToken);
    }
}

public sealed record SetupShellStatusSnapshot(
    string CodexHome,
    string RuntimeStoreRoot,
    bool RuntimeReady,
    bool CodexInstalled,
    string Headline,
    string Detail);

public sealed record SetupShellInstallSummary(
    string Title,
    string Message,
    string RuntimeRoot,
    string? PluginSourceRoot,
    string? MarketplacePath,
    string? Snippet,
    bool RestartRequired);
