using System.ComponentModel;
using System.Runtime.InteropServices;
using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Display;

public sealed class Win32MonitorManager : IMonitorManager
{
    private const int ErrorInsufficientBuffer = 122;
    private const int ErrorSuccess = 0;
    private const int MonitorDefaultToNearest = 2;
    private const uint MonitorInfoPrimary = 1;
    private const uint QdcOnlyActivePaths = 0x00000002;
    private readonly object _aliasSync = new();
    private readonly Dictionary<string, string> _monitorAliases = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<MonitorInfo> ListMonitors()
    {
        IReadOnlyDictionary<string, DisplayConfigMonitorIdentity> displayConfigMap = QueryDisplayConfigMap();
        List<MonitorInfo> monitors = new();

        _ = EnumDisplayMonitors(
            IntPtr.Zero,
            IntPtr.Zero,
            (monitor, _, _, _) =>
            {
                MONITORINFOEX info = new() { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
                if (!GetMonitorInfo(monitor, ref info))
                {
                    return true;
                }

                string gdiDeviceName = info.szDevice;
                string normalizedGdiDeviceName = NormalizeGdiDeviceName(gdiDeviceName);
                _ = displayConfigMap.TryGetValue(normalizedGdiDeviceName, out DisplayConfigMonitorIdentity? displayIdentity);

                string monitorId = displayIdentity is null
                    ? BuildFallbackMonitorId(gdiDeviceName)
                    : BuildDisplayConfigMonitorId(displayIdentity.AdapterId, displayIdentity.TargetId);
                string friendlyName = !string.IsNullOrWhiteSpace(displayIdentity?.FriendlyName)
                    ? displayIdentity.FriendlyName!
                    : gdiDeviceName;

                MonitorDescriptor descriptor = new(
                    MonitorId: monitorId,
                    FriendlyName: friendlyName,
                    GdiDeviceName: gdiDeviceName,
                    Bounds: new Bounds(info.rcMonitor.Left, info.rcMonitor.Top, info.rcMonitor.Right, info.rcMonitor.Bottom),
                    WorkArea: new Bounds(info.rcWork.Left, info.rcWork.Top, info.rcWork.Right, info.rcWork.Bottom),
                    DpiScale: GetMonitorDpiScale(monitor),
                    IsPrimary: (info.dwFlags & MonitorInfoPrimary) != 0);
                monitors.Add(new MonitorInfo(descriptor, monitor.ToInt64()));

                return true;
            },
            IntPtr.Zero);

        lock (_aliasSync)
        {
            foreach (MonitorInfo monitor in monitors)
            {
                string fallbackId = BuildFallbackMonitorId(monitor.Descriptor.GdiDeviceName);
                _monitorAliases[fallbackId] = fallbackId;
                _monitorAliases[monitor.Descriptor.MonitorId] = fallbackId;
            }
        }

        return monitors
            .OrderByDescending(item => item.Descriptor.IsPrimary)
            .ThenBy(item => item.Descriptor.MonitorId, StringComparer.Ordinal)
            .ToArray();
    }

    public MonitorInfo? FindMonitorById(string monitorId)
    {
        if (string.IsNullOrWhiteSpace(monitorId))
        {
            return null;
        }

        IReadOnlyList<MonitorInfo> monitors = ListMonitors();
        MonitorInfo? exactMatch = monitors.FirstOrDefault(
            monitor => string.Equals(
                monitor.Descriptor.MonitorId,
                monitorId,
                StringComparison.OrdinalIgnoreCase));
        if (exactMatch is not null)
        {
            return exactMatch;
        }

        string? fallbackAlias = ResolveFallbackAlias(monitorId);
        if (fallbackAlias is null)
        {
            return null;
        }

        return monitors.FirstOrDefault(
            monitor => string.Equals(
                BuildFallbackMonitorId(monitor.Descriptor.GdiDeviceName),
                fallbackAlias,
                StringComparison.OrdinalIgnoreCase));
    }

    public MonitorInfo? FindMonitorByHandle(long handle, IReadOnlyList<MonitorInfo>? monitors = null)
    {
        IReadOnlyList<MonitorInfo> source = monitors ?? ListMonitors();
        return source.FirstOrDefault(item => item.Handle == handle);
    }

    public long? GetMonitorHandleForWindow(long hwnd)
    {
        IntPtr monitor = MonitorFromWindow(new IntPtr(hwnd), MonitorDefaultToNearest);
        return monitor == IntPtr.Zero ? null : monitor.ToInt64();
    }

    public MonitorInfo? FindMonitorForWindow(long hwnd)
    {
        long? handle = GetMonitorHandleForWindow(hwnd);
        if (handle is null)
        {
            return null;
        }

        return FindMonitorByHandle(handle.Value);
    }

    public MonitorInfo? GetPrimaryMonitor() =>
        ListMonitors().FirstOrDefault(item => item.Descriptor.IsPrimary);

    private static Dictionary<string, DisplayConfigMonitorIdentity> QueryDisplayConfigMap()
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            int bufferResult = GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out uint pathCount, out uint modeCount);
            if (bufferResult != ErrorSuccess)
            {
                return EmptyDisplayConfigMap();
            }

            DISPLAYCONFIG_PATH_INFO[] paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            DISPLAYCONFIG_MODE_INFO[] modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
            int queryResult = QueryDisplayConfig(QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
            if (queryResult == ErrorInsufficientBuffer)
            {
                continue;
            }

            if (queryResult != ErrorSuccess)
            {
                return EmptyDisplayConfigMap();
            }

            Dictionary<string, DisplayConfigMonitorIdentity> map = new(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < pathCount; index++)
            {
                DISPLAYCONFIG_PATH_INFO path = paths[index];
                string? gdiDeviceName = TryGetSourceGdiDeviceName(path.sourceInfo.adapterId, path.sourceInfo.id);
                if (string.IsNullOrWhiteSpace(gdiDeviceName))
                {
                    continue;
                }

                string normalized = NormalizeGdiDeviceName(gdiDeviceName);
                string? friendlyName = TryGetTargetFriendlyName(path.targetInfo.adapterId, path.targetInfo.id);
                map[normalized] = new DisplayConfigMonitorIdentity(path.targetInfo.adapterId, path.targetInfo.id, friendlyName);
            }

            return map;
        }

        return EmptyDisplayConfigMap();
    }

    private static Dictionary<string, DisplayConfigMonitorIdentity> EmptyDisplayConfigMap() =>
        new Dictionary<string, DisplayConfigMonitorIdentity>(StringComparer.OrdinalIgnoreCase);

    private static string? TryGetSourceGdiDeviceName(Luid adapterId, uint sourceId)
    {
        DISPLAYCONFIG_SOURCE_DEVICE_NAME request = new()
        {
            header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                type = DISPLAYCONFIG_DEVICE_INFO_TYPE.GetSourceName,
                size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>(),
                adapterId = adapterId,
                id = sourceId,
            },
        };

        return DisplayConfigGetDeviceInfo(ref request) == ErrorSuccess
            ? request.viewGdiDeviceName
            : null;
    }

    private static string? TryGetTargetFriendlyName(Luid adapterId, uint targetId)
    {
        DISPLAYCONFIG_TARGET_DEVICE_NAME request = new()
        {
            header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                type = DISPLAYCONFIG_DEVICE_INFO_TYPE.GetTargetName,
                size = (uint)Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>(),
                adapterId = adapterId,
                id = targetId,
            },
        };

        return DisplayConfigGetDeviceInfo(ref request) == ErrorSuccess
            ? request.monitorFriendlyDeviceName
            : null;
    }

    private static double GetMonitorDpiScale(IntPtr monitor)
    {
        int hr = GetDpiForMonitor(monitor, MonitorDpiType.Effective, out uint dpiX, out _);
        if (hr < 0 || dpiX == 0)
        {
            return 1.0;
        }

        return dpiX / 96.0;
    }

    private static string BuildDisplayConfigMonitorId(Luid adapterId, uint targetId)
        => MonitorIdFormatter.FromDisplayConfig(adapterId.HighPart, adapterId.LowPart, targetId);

    private static string BuildFallbackMonitorId(string gdiDeviceName)
        => MonitorIdFormatter.FromGdiDeviceName(gdiDeviceName);

    private static string NormalizeGdiDeviceName(string gdiDeviceName) =>
        (gdiDeviceName ?? string.Empty).Trim().ToUpperInvariant();

    private string? ResolveFallbackAlias(string monitorId)
    {
        lock (_aliasSync)
        {
            return _monitorAliases.TryGetValue(monitorId, out string? fallbackAlias)
                ? fallbackAlias
                : null;
        }
    }

    private delegate bool MonitorEnumProc(
        IntPtr monitor,
        IntPtr hdcMonitor,
        IntPtr lprcMonitor,
        IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_RATIONAL
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINTL
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_2DREGION
    {
        public uint cx;
        public uint cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_SOURCE_INFO
    {
        public Luid adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public Luid adapterId;
        public uint id;
        public uint modeInfoIdx;
        public DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY outputTechnology;
        public DISPLAYCONFIG_ROTATION rotation;
        public DISPLAYCONFIG_SCALING scaling;
        public DISPLAYCONFIG_RATIONAL refreshRate;
        public DISPLAYCONFIG_SCANLINE_ORDERING scanLineOrdering;

        [MarshalAs(UnmanagedType.Bool)]
        public bool targetAvailable;

        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_INFO
    {
        public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
        public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_VIDEO_SIGNAL_INFO
    {
        public ulong pixelRate;
        public DISPLAYCONFIG_RATIONAL hSyncFreq;
        public DISPLAYCONFIG_RATIONAL vSyncFreq;
        public DISPLAYCONFIG_2DREGION activeSize;
        public DISPLAYCONFIG_2DREGION totalSize;
        public uint videoStandard;
        public DISPLAYCONFIG_SCANLINE_ORDERING scanLineOrdering;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_TARGET_MODE
    {
        public DISPLAYCONFIG_VIDEO_SIGNAL_INFO targetVideoSignalInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_SOURCE_MODE
    {
        public uint width;
        public uint height;
        public DISPLAYCONFIG_PIXELFORMAT pixelFormat;
        public POINTL position;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_DESKTOP_IMAGE_INFO
    {
        public POINTL PathSourceSize;
        public RECT DesktopImageRegion;
        public RECT DesktopImageClip;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct DISPLAYCONFIG_MODE_INFO_UNION
    {
        [FieldOffset(0)]
        public DISPLAYCONFIG_TARGET_MODE targetMode;

        [FieldOffset(0)]
        public DISPLAYCONFIG_SOURCE_MODE sourceMode;

        [FieldOffset(0)]
        public DISPLAYCONFIG_DESKTOP_IMAGE_INFO desktopImageInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_MODE_INFO
    {
        public DISPLAYCONFIG_MODE_INFO_TYPE infoType;
        public uint id;
        public Luid adapterId;
        public DISPLAYCONFIG_MODE_INFO_UNION modeInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_DEVICE_INFO_HEADER
    {
        public DISPLAYCONFIG_DEVICE_INFO_TYPE type;
        public uint size;
        public Luid adapterId;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string viewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAYCONFIG_TARGET_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint flags;
        public DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY outputTechnology;
        public ushort edidManufactureId;
        public ushort edidProductCodeId;
        public uint connectorInstance;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string monitorFriendlyDeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string monitorDevicePath;
    }

    private enum DISPLAYCONFIG_MODE_INFO_TYPE : uint
    {
        Source = 1,
        Target = 2,
        DesktopImage = 3,
    }

    private enum DISPLAYCONFIG_DEVICE_INFO_TYPE : uint
    {
        GetSourceName = 1,
        GetTargetName = 2,
    }

    private enum DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY : int
    {
        Other = -1,
    }

    private enum DISPLAYCONFIG_ROTATION : uint
    {
        Identity = 1,
    }

    private enum DISPLAYCONFIG_SCALING : uint
    {
        Identity = 1,
    }

    private enum DISPLAYCONFIG_SCANLINE_ORDERING : uint
    {
        Unspecified = 0,
    }

    private enum DISPLAYCONFIG_PIXELFORMAT : uint
    {
        Pixelformat8Bpp = 1,
        Pixelformat16Bpp = 2,
        Pixelformat24Bpp = 3,
        Pixelformat32Bpp = 4,
        PixelformatNongdi = 5,
    }

    private enum MonitorDpiType
    {
        Effective = 0,
    }

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(
        IntPtr hdc,
        IntPtr lprcClip,
        MonitorEnumProc lpfnEnum,
        IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX monitorInfo);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(
        uint flags,
        ref uint numPathArrayElements,
        [Out] DISPLAYCONFIG_PATH_INFO[] pathInfoArray,
        ref uint numModeInfoArrayElements,
        [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
        IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME requestPacket);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME requestPacket);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, MonitorDpiType dpiType, out uint dpiX, out uint dpiY);

    private sealed record DisplayConfigMonitorIdentity(Luid AdapterId, uint TargetId, string? FriendlyName);
}
