namespace WinBridge.Runtime.Windows.Shell;

internal static class InputAsyncStateReadabilityModeSelector
{
    public static InputAsyncStateReadabilityMode Select(int? foregroundOwnerProcessId, int currentProcessId) =>
        foregroundOwnerProcessId is int processId && processId == currentProcessId
            ? InputAsyncStateReadabilityMode.SameProcessForeground
            : InputAsyncStateReadabilityMode.CrossProcessForeground;
}
