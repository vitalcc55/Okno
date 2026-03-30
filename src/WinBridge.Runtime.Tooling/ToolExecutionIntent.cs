namespace WinBridge.Runtime.Guards;

public readonly record struct ToolExecutionIntent(
    bool IsDryRunRequested,
    bool ConfirmationGranted,
    bool PreviewAvailable)
{
    public static ToolExecutionIntent Default => default;
}
