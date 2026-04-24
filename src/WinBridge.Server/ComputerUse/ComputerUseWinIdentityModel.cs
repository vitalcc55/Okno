using System.Security.Cryptography;
using System.Text;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Shell;

namespace WinBridge.Server.ComputerUse;

internal readonly record struct ComputerUseWinApprovalKey(string Value)
{
    public override string ToString() => Value;
}

internal readonly record struct ComputerUseWinWindowInstanceIdentity(string Value)
{
    public override string ToString() => Value;
}

internal sealed record ComputerUseWinExecutionTarget(
    ComputerUseWinApprovalKey ApprovalKey,
    ComputerUseWinWindowInstanceIdentity WindowId,
    WindowDescriptor Window);

internal sealed record ComputerUseWinDiscoveredApp(
    ComputerUseWinApprovalKey ApprovalKey,
    IReadOnlyList<ComputerUseWinExecutionTarget> Windows,
    bool IsApproved,
    bool IsBlocked,
    string? BlockReason);

internal static class ComputerUseWinExecutionTargetCatalog
{
    public static IReadOnlyList<ComputerUseWinExecutionTarget> Materialize(IReadOnlyList<WindowDescriptor> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);

        return windows
            .Where(static window => window.IsVisible)
            .Select(static window => TryCreate(window, out ComputerUseWinExecutionTarget? target) ? target : null)
            .Where(static target => target is not null)
            .Cast<ComputerUseWinExecutionTarget>()
            .ToArray();
    }

    public static bool TryCreate(WindowDescriptor window, out ComputerUseWinExecutionTarget? target)
    {
        ArgumentNullException.ThrowIfNull(window);

        target = null;
        if (!window.IsVisible)
        {
            return false;
        }

        if (!ComputerUseWinAppIdentity.TryCreateStableAppId(window, out string? appId))
        {
            return false;
        }

        if (!WindowIdentityValidator.TryValidateStableIdentity(window, out _))
        {
            return false;
        }

        ComputerUseWinApprovalKey approvalKey = new(appId!);
        target = new(
            approvalKey,
            CreateWindowId(approvalKey, window),
            window);
        return true;
    }

    public static ComputerUseWinWindowInstanceIdentity CreateWindowId(ComputerUseWinApprovalKey approvalKey, WindowDescriptor window)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(approvalKey.Value);
        ArgumentNullException.ThrowIfNull(window);

        string source = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"cw1|{approvalKey.Value}|{window.Hwnd}|{window.ProcessId}|{window.ThreadId}|{window.ClassName}");
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return new ComputerUseWinWindowInstanceIdentity($"cw_{Convert.ToHexString(hash[..12]).ToLowerInvariant()}");
    }
}
