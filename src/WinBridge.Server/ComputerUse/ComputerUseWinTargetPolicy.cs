using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Tooling;

namespace WinBridge.Server.ComputerUse;

internal static class ComputerUseWinTargetPolicy
{
    private static readonly HashSet<string> BlockedProcesses =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ComputerUseWinAppIdentity.NormalizeProcessIdentity("codex.exe")!,
            ComputerUseWinAppIdentity.NormalizeProcessIdentity("codex")!,
            ComputerUseWinAppIdentity.NormalizeProcessIdentity("cmd.exe")!,
            ComputerUseWinAppIdentity.NormalizeProcessIdentity("powershell.exe")!,
            ComputerUseWinAppIdentity.NormalizeProcessIdentity("pwsh.exe")!,
            ComputerUseWinAppIdentity.NormalizeProcessIdentity("bash.exe")!,
            ComputerUseWinAppIdentity.NormalizeProcessIdentity("wt.exe")!,
            ComputerUseWinAppIdentity.NormalizeProcessIdentity("windowsterminal.exe")!,
            ComputerUseWinAppIdentity.NormalizeProcessIdentity("conhost.exe")!,
            ComputerUseWinAppIdentity.NormalizeProcessIdentity("openconsole.exe")!,
        };

    private static readonly string[] RiskyElementKeywords =
    [
        "send",
        "submit",
        "delete",
        "remove",
        "buy",
        "purchase",
        "pay",
        "confirm",
        "отправ",
        "удал",
        "оплат",
        "подтверд",
        "куп",
    ];

    public static bool TryGetBlockedReason(WindowDescriptor window, out string? reason)
    {
        ArgumentNullException.ThrowIfNull(window);

        string? canonicalProcessName = ComputerUseWinAppIdentity.NormalizeProcessIdentity(window.ProcessName);
        if (!string.IsNullOrWhiteSpace(canonicalProcessName)
            && BlockedProcesses.Contains(canonicalProcessName))
        {
            reason = $"Computer Use for Windows не автоматизирует процесс '{window.ProcessName}' по умолчанию.";
            return true;
        }

        reason = null;
        return false;
    }

    public static bool RequiresRiskConfirmation(ComputerUseWinStoredElement? element, string actionName, string? key = null)
    {
        if (string.Equals(actionName, ToolNames.ComputerUseWinPressKey, StringComparison.Ordinal))
        {
            return key is not null
                && (key.Contains("enter", StringComparison.OrdinalIgnoreCase)
                    || key.Contains("delete", StringComparison.OrdinalIgnoreCase)
                    || key.Contains("return", StringComparison.OrdinalIgnoreCase));
        }

        if (element is null)
        {
            return false;
        }

        string haystack = string.Join(
            " ",
            new[]
            {
                element.Name,
                element.AutomationId,
                element.ControlType,
            }.Where(static item => !string.IsNullOrWhiteSpace(item)));

        return RiskyElementKeywords.Any(keyword => haystack.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }
}
