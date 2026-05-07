// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Setup.Core;

public sealed class ComputerUseWinRuntimeStorePaths
{
    public ComputerUseWinRuntimeStorePaths(string codexHome, string localAppDataRoot)
    {
        CodexHome = Path.GetFullPath(codexHome);
        LocalAppDataRoot = Path.GetFullPath(localAppDataRoot);
        RuntimeStoreRoot = Path.Combine(LocalAppDataRoot, "Okno", "computer-use-win");
        RuntimesRoot = Path.Combine(RuntimeStoreRoot, "runtimes");
        StateRoot = Path.Combine(RuntimeStoreRoot, "state");
        ReceiptsRoot = Path.Combine(RuntimeStoreRoot, "receipts");
        LocksRoot = Path.Combine(RuntimeStoreRoot, "locks");
        CurrentStatePath = Path.Combine(StateRoot, "current-runtime.json");
        RuntimeLauncherScriptPath = Path.Combine(RuntimeStoreRoot, "run-computer-use-win-runtime.ps1");
    }

    public string CodexHome { get; }

    public string LocalAppDataRoot { get; }

    public string RuntimeStoreRoot { get; }

    public string RuntimesRoot { get; }

    public string StateRoot { get; }

    public string ReceiptsRoot { get; }

    public string LocksRoot { get; }

    public string CurrentStatePath { get; }

    public string RuntimeLauncherScriptPath { get; }

    public string GetRuntimeVersionRoot(string rid, string version)
    {
        return Path.Combine(RuntimesRoot, rid, version);
    }

    public string GetRidLockPath(string rid)
    {
        return Path.Combine(LocksRoot, $"{rid}.install.lock");
    }

    public string GetInstallModeLockPath(ComputerUseWinInstallMode mode)
    {
        return Path.Combine(LocksRoot, $"{mode.ToString().ToLowerInvariant()}.install.lock");
    }

    public string GetReceiptPath(ComputerUseWinInstallMode mode)
    {
        return Path.Combine(ReceiptsRoot, $"{mode.ToString().ToLowerInvariant()}.json");
    }
}
