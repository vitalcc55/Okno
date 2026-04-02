namespace WinBridge.Runtime.Windows.Launch;

internal interface IStartedProcessHandle : IDisposable
{
    int Id { get; }

    bool HasExited { get; }

    int ExitCode { get; }

    long MainWindowHandle { get; }

    bool WaitForInputIdle(int milliseconds);

    void Refresh();
}
