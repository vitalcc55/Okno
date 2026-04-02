using System.Diagnostics;

namespace WinBridge.Runtime.Windows.Launch;

internal sealed class SystemProcessLaunchPlatform : IProcessLaunchPlatform
{
    public IStartedProcessHandle? Start(ProcessStartInfo startInfo)
    {
        Process? process = Process.Start(startInfo);
        return process is null ? null : new SystemProcessHandle(process);
    }
}
