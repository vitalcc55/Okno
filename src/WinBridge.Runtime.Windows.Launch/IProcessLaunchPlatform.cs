using System.Diagnostics;

namespace WinBridge.Runtime.Windows.Launch;

internal interface IProcessLaunchPlatform
{
    IStartedProcessHandle? Start(ProcessStartInfo startInfo);
}
