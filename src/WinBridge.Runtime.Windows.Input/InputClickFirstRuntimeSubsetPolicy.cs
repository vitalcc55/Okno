using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Input;

internal static class InputClickFirstRuntimeSubsetPolicy
{
    public static bool TryValidateRequest(
        InputRequest request,
        out string? failureCode,
        out string? reason) =>
        InputClickFirstSubsetContract.TryValidateRequest(request, out failureCode, out reason);
}
