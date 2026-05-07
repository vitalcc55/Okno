// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.IO;

namespace WinBridge.Setup.Core;

public sealed class SetupShellController
{
    private readonly Func<ComputerUseWinInstallerStatus> statusProvider;
    private readonly Func<ComputerUseWinInstallerOperation, ComputerUseWinInstallerResult> operationRunner;
    private readonly OknoSetupShellRegistrationService shellRegistrationService;

    public SetupShellController()
        : this(
            () => new ComputerUseWinInstallerService().GetStatus(),
            operation =>
            {
                ComputerUseWinInstallerService installer = new();
                return operation switch
                {
                    { OperationKind: SetupShellOperationKind.Install, Mode: ComputerUseWinInstallMode.Codex } => installer.InstallCodex(),
                    { OperationKind: SetupShellOperationKind.Install, Mode: ComputerUseWinInstallMode.RuntimeOnly } => installer.InstallRuntimeOnly(),
                    { OperationKind: SetupShellOperationKind.Reinstall, Mode: ComputerUseWinInstallMode.Codex } => installer.UpdateCodex(),
                    { OperationKind: SetupShellOperationKind.Reinstall, Mode: ComputerUseWinInstallMode.RuntimeOnly } => installer.UpdateRuntimeOnly(),
                    { OperationKind: SetupShellOperationKind.Repair, Mode: ComputerUseWinInstallMode.Codex } => installer.RepairCodex(),
                    { OperationKind: SetupShellOperationKind.Repair, Mode: ComputerUseWinInstallMode.RuntimeOnly } => installer.RepairRuntimeOnly(),
                    { OperationKind: SetupShellOperationKind.RemoveAll } => installer.UninstallAll(),
                    _ => throw new InvalidOperationException($"Unsupported lifecycle operation '{operation.OperationKind}' for mode '{operation.Mode}'."),
                };
            },
            new OknoSetupShellRegistrationService())
    {
    }

    public SetupShellController(
        Func<ComputerUseWinInstallerStatus> statusProvider,
        Func<ComputerUseWinInstallerOperation, ComputerUseWinInstallerResult> operationRunner,
        OknoSetupShellRegistrationService shellRegistrationService)
    {
        this.statusProvider = statusProvider;
        this.operationRunner = operationRunner;
        this.shellRegistrationService = shellRegistrationService;
    }

    public SetupShellStatusSnapshot GetStatusSnapshot()
    {
        ComputerUseWinInstallerStatus status = statusProvider();
        SetupShellInstalledState installedState = ResolveInstalledState(status);

        string headline;
        string detail;

        switch (installedState)
        {
            case SetupShellInstalledState.CodexAndRuntimeOnly:
                headline = "Codex and runtime-only installs are already present.";
                detail = "You can reinstall the selected mode, repair it, or remove Okno completely.";
                break;
            case SetupShellInstalledState.Codex:
                headline = "Codex plugin is already installed.";
                detail = $"Plugin root: {status.CodexInstall?.PluginSourceRoot}";
                break;
            case SetupShellInstalledState.RuntimeOnly:
                headline = "Shared runtime is already installed.";
                detail = $"Runtime root: {status.RuntimeStatus.EffectiveRuntimeRoot ?? status.RuntimeOnlyInstall?.RuntimeRoot}";
                break;
            default:
                headline = "Ready to install Okno.";
                detail = "Choose a mode and start the installer.";
                break;
        }

        return new SetupShellStatusSnapshot(
            status.CodexHome,
            status.RuntimeStoreRoot,
            status.CodexInstall?.PluginSourceRoot ?? Path.Combine(status.CodexHome, "plugins", "computer-use-win"),
            status.CodexInstall?.MarketplacePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".agents", "plugins", "marketplace.json"),
            installedState,
            status.RuntimeOnlyInstall is not null,
            status.CodexInstall is not null,
            status.RuntimeStatus.IsInstalled && status.RuntimeStatus.IsUsable && status.RuntimeStatus.IsCompatible,
            status.RuntimeStatus.FailureReason,
            headline,
            detail);
    }

    public SetupShellModePresentation GetModePresentation(ComputerUseWinInstallMode mode)
    {
        SetupShellStatusSnapshot snapshot = GetStatusSnapshot();
        bool selectedModeInstalled = mode switch
        {
            ComputerUseWinInstallMode.Codex => snapshot.HasCodexInstall,
            ComputerUseWinInstallMode.RuntimeOnly => snapshot.HasRuntimeOnlyInstall,
            _ => false,
        };

        SetupShellOperationKind primaryActionKind = selectedModeInstalled
            ? SetupShellOperationKind.Reinstall
            : SetupShellOperationKind.Install;

        string primaryActionLabel = (mode, selectedModeInstalled) switch
        {
            (ComputerUseWinInstallMode.Codex, true) => "Reinstall for Codex",
            (ComputerUseWinInstallMode.Codex, false) => "Install for Codex",
            (ComputerUseWinInstallMode.RuntimeOnly, true) => "Reinstall runtime only",
            _ => "Install runtime only",
        };

        string summaryTitle = mode switch
        {
            ComputerUseWinInstallMode.Codex => "Codex mode installs the plugin and the shared runtime.",
            _ => "Runtime-only mode installs only the shared runtime.",
        };
        string summaryDetail = mode switch
        {
            ComputerUseWinInstallMode.Codex => snapshot.HasCodexInstall
                ? "Use this path to refresh the Codex plugin and the shared runtime together."
                : "Use this path when you want Okno to appear inside Codex through your personal marketplace.",
            _ => snapshot.HasRuntimeOnlyInstall
                ? "Use this path to refresh the shared runtime and keep the MCP launcher command stable."
                : "Use this path when you only need the shared Okno runtime and an MCP command snippet for another client.",
        };
        string footerHint = mode switch
        {
            ComputerUseWinInstallMode.Codex => "After installation or reinstallation, restart Codex and start a new thread so the local plugin is reloaded.",
            _ => "After installation or reinstallation, copy the generated snippet into your MCP client configuration.",
        };

        return new SetupShellModePresentation(
            snapshot.InstalledState,
            primaryActionKind,
            primaryActionLabel,
            summaryTitle,
            summaryDetail,
            footerHint,
            mode == ComputerUseWinInstallMode.Codex,
            selectedModeInstalled,
            snapshot.InstalledState is not SetupShellInstalledState.None);
    }

    public Task<SetupShellOperationSummary> ExecutePrimaryActionAsync(ComputerUseWinInstallMode mode, CancellationToken cancellationToken = default)
    {
        SetupShellModePresentation presentation = GetModePresentation(mode);
        return RunOperationAsync(new ComputerUseWinInstallerOperation(presentation.PrimaryActionKind, mode), cancellationToken);
    }

    public Task<SetupShellOperationSummary> RepairAsync(ComputerUseWinInstallMode mode, CancellationToken cancellationToken = default)
    {
        return RunOperationAsync(new ComputerUseWinInstallerOperation(SetupShellOperationKind.Repair, mode), cancellationToken);
    }

    public Task<SetupShellOperationSummary> RemoveAllAsync(
        string? currentBaseDirectory = null,
        int? currentProcessId = null,
        CancellationToken cancellationToken = default)
    {
        return RunOperationAsync(
            new ComputerUseWinInstallerOperation(SetupShellOperationKind.RemoveAll, null),
            cancellationToken,
            currentBaseDirectory,
            currentProcessId);
    }

    private Task<SetupShellOperationSummary> RunOperationAsync(
        ComputerUseWinInstallerOperation operation,
        CancellationToken cancellationToken,
        string? currentBaseDirectory = null,
        int? currentProcessId = null)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            ComputerUseWinInstallerResult result = operationRunner(operation);
            bool cleanupScheduled = false;

            if (operation.OperationKind is SetupShellOperationKind.Install or SetupShellOperationKind.Reinstall or SetupShellOperationKind.Repair)
            {
                string sourceRoot = string.IsNullOrWhiteSpace(currentBaseDirectory)
                    ? AppContext.BaseDirectory
                    : currentBaseDirectory;
                if (File.Exists(Path.Combine(sourceRoot, "Okno Setup.exe")))
                {
                    shellRegistrationService.RegisterShell(sourceRoot, result.RuntimeVersion);
                }
            }
            else if (operation.OperationKind == SetupShellOperationKind.RemoveAll)
            {
                cleanupScheduled = shellRegistrationService.RemoveShellArtifacts(currentBaseDirectory, currentProcessId);
            }

            return CreateSummary(operation, result, cleanupScheduled);
        }, cancellationToken);
    }

    private static SetupShellOperationSummary CreateSummary(
        ComputerUseWinInstallerOperation operation,
        ComputerUseWinInstallerResult result,
        bool cleanupScheduled)
    {
        return operation switch
        {
            { OperationKind: SetupShellOperationKind.RemoveAll } => new SetupShellOperationSummary(
                SetupShellOperationKind.RemoveAll,
                "Okno removed",
                cleanupScheduled
                    ? "Okno was removed. The maintenance shell will clean itself up after this window closes."
                    : "Okno was removed.",
                null,
                null,
                null,
                null,
                false,
                cleanupScheduled),
            { Mode: ComputerUseWinInstallMode.Codex } => new SetupShellOperationSummary(
                operation.OperationKind,
                operation.OperationKind == SetupShellOperationKind.Repair ? "Repair for Codex completed." : "Install for Codex completed.",
                "Restart Codex to load the installed plugin from your personal marketplace.",
                result.RuntimeRoot,
                result.PluginSourceRoot,
                result.MarketplacePath,
                null,
                result.RestartRequired,
                false),
            { Mode: ComputerUseWinInstallMode.RuntimeOnly } => new SetupShellOperationSummary(
                operation.OperationKind,
                operation.OperationKind == SetupShellOperationKind.Repair ? "Runtime-only repair completed." : "Runtime-only install completed.",
                "Use the generated MCP snippet in your client configuration.",
                result.RuntimeRoot,
                null,
                null,
                result.Snippet,
                false,
                false),
            _ => throw new InvalidOperationException($"Unsupported lifecycle operation '{operation.OperationKind}' with mode '{operation.Mode}'."),
        };
    }

    private static SetupShellInstalledState ResolveInstalledState(ComputerUseWinInstallerStatus status)
    {
        bool hasRuntimeOnly = status.RuntimeOnlyInstall is not null;
        bool hasCodex = status.CodexInstall is not null;

        return (hasRuntimeOnly, hasCodex) switch
        {
            (true, true) => SetupShellInstalledState.CodexAndRuntimeOnly,
            (true, false) => SetupShellInstalledState.RuntimeOnly,
            (false, true) => SetupShellInstalledState.Codex,
            _ => SetupShellInstalledState.None,
        };
    }
}
