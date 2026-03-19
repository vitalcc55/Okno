namespace WinBridge.Runtime.Windows.UIA;

internal static class UiAutomationMtaRunner
{
    public static Task<T> RunAsync<T>(
        Func<CancellationToken, T> callback,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(callback);
        cancellationToken.ThrowIfCancellationRequested();

        CancellationTokenSource linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        TaskCompletionSource<T> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread worker = new(() =>
        {
            try
            {
                linkedCancellation.Token.ThrowIfCancellationRequested();
                T result = callback(linkedCancellation.Token);
                completion.TrySetResult(result);
            }
            catch (OperationCanceledException exception) when (exception.CancellationToken == linkedCancellation.Token || linkedCancellation.IsCancellationRequested)
            {
                completion.TrySetCanceled(linkedCancellation.Token);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        })
        {
            IsBackground = true,
            Name = "Okno.UIA.Snapshot",
        };
        worker.SetApartmentState(ApartmentState.MTA);
        worker.Start();

        completion.Task.ContinueWith(
            static (_, state) => ((CancellationTokenSource)state!).Dispose(),
            linkedCancellation,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        return completion.Task;
    }
}
