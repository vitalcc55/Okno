using System.Diagnostics;

namespace WinBridge.Runtime.Windows.Launch;

internal sealed class SystemProcessHandle(Process process) : IStartedProcessHandle
{
    private readonly Process _process = process;

    public int Id => _process.Id;

    public bool HasExited => _process.HasExited;

    public int ExitCode => _process.ExitCode;

    public long MainWindowHandle => _process.MainWindowHandle.ToInt64();

    public bool WaitForInputIdle(int milliseconds)
    {
        return _process.WaitForInputIdle(milliseconds);
    }

    public void Refresh()
    {
        _process.Refresh();
    }

    public void Dispose()
    {
        _process.Dispose();
    }
}
