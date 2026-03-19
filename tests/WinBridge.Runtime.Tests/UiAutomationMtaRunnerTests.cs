using WinBridge.Runtime.Windows.UIA;

namespace WinBridge.Runtime.Tests;

public sealed class UiAutomationMtaRunnerTests
{
    [Fact]
    public async Task RunAsyncCompletesWhenCallbackReturnsResult()
    {
        Task<int> task = UiAutomationMtaRunner.RunAsync(
            cancellationToken =>
            {
                return 42;
            },
            cancellationToken: CancellationToken.None);

        Assert.Equal(42, await task);
    }

    [Fact]
    public async Task RunAsyncBridgesExternalCancellationToCallerFacingTask()
    {
        using CancellationTokenSource cancellation = new();
        Task<int> task = UiAutomationMtaRunner.RunAsync(
            cancellationToken =>
            {
                cancellation.CancelAfter(TimeSpan.FromMilliseconds(50));
                cancellationToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(5));
                cancellationToken.ThrowIfCancellationRequested();
                return 42;
            },
            cancellationToken: cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
    }
}
