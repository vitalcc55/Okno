using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.UIA;

public interface IUiAutomationWaitProbe
{
    Task<UiAutomationWaitProbeExecutionResult> ProbeAsync(
        WindowDescriptor targetWindow,
        WaitRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}
