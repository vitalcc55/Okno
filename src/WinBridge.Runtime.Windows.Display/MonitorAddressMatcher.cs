using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Display;

public static class MonitorAddressMatcher
{
    public static bool Matches(string requestedMonitorId, MonitorDescriptor descriptor)
    {
        if (string.IsNullOrWhiteSpace(requestedMonitorId))
        {
            return false;
        }

        if (string.Equals(descriptor.MonitorId, requestedMonitorId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (MonitorIdFormatter.IsGdiMonitorId(requestedMonitorId))
        {
            string currentGdiAddress = MonitorIdFormatter.FromGdiDeviceName(descriptor.GdiDeviceName);
            return string.Equals(currentGdiAddress, requestedMonitorId, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
