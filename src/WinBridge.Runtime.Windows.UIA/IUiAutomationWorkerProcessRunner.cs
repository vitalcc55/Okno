namespace WinBridge.Runtime.Windows.UIA;

internal interface IUiAutomationWorkerProcessRunner
{
    Task<UiAutomationWorkerProcessResult> ExecuteAsync(
        object invocation,
        long? windowHwnd,
        TimeSpan? timeout,
        CancellationToken cancellationToken);
}
