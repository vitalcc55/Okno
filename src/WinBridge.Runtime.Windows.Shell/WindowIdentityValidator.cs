using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Shell;

public static class WindowIdentityValidator
{
    public static bool TryValidateStableIdentity(WindowDescriptor window, out string? reason)
    {
        if (window.ProcessId is null)
        {
            reason = "Окно не содержит достаточных identity-сигналов для attach: отсутствует ProcessId.";
            return false;
        }

        if (window.ThreadId is null)
        {
            reason = "Окно не содержит достаточных identity-сигналов для attach: отсутствует ThreadId.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(window.ClassName))
        {
            reason = "Окно не содержит достаточных identity-сигналов для attach: отсутствует ClassName.";
            return false;
        }

        reason = null;
        return true;
    }

    public static bool MatchesStableIdentity(WindowDescriptor liveCandidate, WindowDescriptor expectedWindow)
    {
        if (!TryValidateStableIdentity(expectedWindow, out _))
        {
            return false;
        }

        return liveCandidate.ProcessId == expectedWindow.ProcessId
            && liveCandidate.ThreadId == expectedWindow.ThreadId
            && string.Equals(liveCandidate.ClassName, expectedWindow.ClassName, StringComparison.Ordinal);
    }
}
