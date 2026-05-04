// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;

namespace WinBridge.Runtime.Windows.Display;

public sealed class Win32MonitorManager(AuditLog auditLog) : IMonitorManager
{
    private const int ErrorAccessDenied = 5;
    private const int ErrorInsufficientBuffer = 122;
    private const int ErrorNotSupported = 50;
    private const int ErrorSuccess = 0;
    private const int MonitorDefaultToNearest = 2;
    private const uint DisplayDeviceAttachedToDesktop = 0x00000001;
    private const uint DisplayDeviceMirroringDriver = 0x00000008;
    private const uint MonitorInfoPrimary = 1;
    private const uint QdcOnlyActivePaths = 0x00000002;

    public DisplayTopologySnapshot GetTopologySnapshot()
    {
        DisplayConfigQueryOutcome displayConfigOutcome = QueryDisplayConfigMap();
        bool hasDisplayConfig = displayConfigOutcome.DisplayConfigMap.Count > 0;
        HashSet<string> desktopGdiDevices = ListDesktopGdiDevices();
        Dictionary<string, MonitorAccumulator> monitors = new(StringComparer.OrdinalIgnoreCase);
        DisplayIdentityFailureInfo? topologyFailure = null;
        bool usedFallbackMonitorIdentity = false;

        _ = EnumDisplayMonitors(
            IntPtr.Zero,
            IntPtr.Zero,
            (monitor, _, _, _) =>
            {
                MONITORINFOEX info = new() { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
                if (!GetMonitorInfo(monitor, ref info))
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    topologyFailure = DisplayIdentityFailureAggregator.SelectMoreSignificant(
                        topologyFailure,
                        DisplayIdentityFailureInfoFactory.Create(
                            DisplayIdentityFailureStageValues.GetMonitorInfo,
                            errorCode,
                            "Не удалось получить monitor metadata через GetMonitorInfo; topology snapshot неполный и runtime не может считать strong identity fully intact."));
                    usedFallbackMonitorIdentity = true;
                    return true;
                }

                string gdiDeviceName = info.szDevice;
                string normalizedGdiDeviceName = NormalizeGdiDeviceName(gdiDeviceName);
                if (desktopGdiDevices.Count > 0 && !desktopGdiDevices.Contains(normalizedGdiDeviceName))
                {
                    return true;
                }

                DisplayConfigSourceIdentity? displayIdentity = null;
                if (hasDisplayConfig)
                {
                    _ = displayConfigOutcome.DisplayConfigMap.TryGetValue(normalizedGdiDeviceName, out displayIdentity);
                }

                if (displayIdentity is null)
                {
                    usedFallbackMonitorIdentity = true;
                }

                string monitorId = displayIdentity is not null
                    ? BuildDisplaySourceMonitorId(displayIdentity!)
                    : BuildFallbackMonitorId(gdiDeviceName);

                string friendlyName = displayIdentity is not null
                    ? displayIdentity!.GetPublicFriendlyName(gdiDeviceName)
                    : gdiDeviceName;

                MonitorDescriptor descriptor = new(
                    MonitorId: monitorId,
                    FriendlyName: friendlyName,
                    GdiDeviceName: gdiDeviceName,
                    Bounds: new Bounds(info.rcMonitor.Left, info.rcMonitor.Top, info.rcMonitor.Right, info.rcMonitor.Bottom),
                    WorkArea: new Bounds(info.rcWork.Left, info.rcWork.Top, info.rcWork.Right, info.rcWork.Bottom),
                    IsPrimary: (info.dwFlags & MonitorInfoPrimary) != 0);
                long handle = monitor.ToInt64();

                if (!monitors.TryGetValue(monitorId, out MonitorAccumulator? accumulator))
                {
                    accumulator = new MonitorAccumulator(descriptor, handle);
                    monitors[monitorId] = accumulator;
                    return true;
                }

                accumulator.AddHandle(handle);
                if (descriptor.IsPrimary && !accumulator.Descriptor.IsPrimary)
                {
                    accumulator.ReplaceRepresentative(descriptor, handle);
                }

                return true;
            },
            IntPtr.Zero);

        MonitorInfo[] materializedMonitors = monitors.Values
            .Select(accumulator => accumulator.Build())
            .OrderByDescending(item => item.Descriptor.IsPrimary)
            .ThenBy(item => item.Descriptor.MonitorId, StringComparer.Ordinal)
            .ToArray();
        DisplayIdentityFailureInfo? combinedFailure = DisplayIdentityFailureAggregator.SelectMoreSignificant(
            topologyFailure,
            displayConfigOutcome.FailedStage is null
                ? null
                : new DisplayIdentityFailureInfo(
                    displayConfigOutcome.FailedStage,
                    displayConfigOutcome.ErrorCode ?? 0,
                    displayConfigOutcome.ErrorName ?? "WIN32_UNKNOWN",
                    displayConfigOutcome.MessageHuman ?? "Display identity деградировала."));
        DisplayIdentityDiagnostics diagnostics = DisplayIdentityDiagnosticsBuilder.Build(
            new DisplayConfigQueryDiagnostics(
                combinedFailure?.FailedStage,
                combinedFailure?.ErrorCode,
                combinedFailure?.ErrorName,
                combinedFailure?.MessageHuman),
            usedFallbackMonitorIdentity,
            materializedMonitors.Length,
            DateTimeOffset.UtcNow);
        auditLog.RecordDisplayIdentityStateChange(diagnostics, materializedMonitors.Length);
        return new(materializedMonitors, diagnostics);
    }

    public MonitorInfo? FindMonitorById(string monitorId, DisplayTopologySnapshot? snapshot = null)
    {
        if (string.IsNullOrWhiteSpace(monitorId))
        {
            return null;
        }

        IReadOnlyList<MonitorInfo> monitors = (snapshot ?? GetTopologySnapshot()).Monitors;
        return monitors.FirstOrDefault(
            monitor => MonitorAddressMatcher.Matches(monitorId, monitor.Descriptor));
    }

    public MonitorInfo? FindMonitorByHandle(long handle, DisplayTopologySnapshot? snapshot = null)
    {
        IReadOnlyList<MonitorInfo> source = (snapshot ?? GetTopologySnapshot()).Monitors;
        return source.FirstOrDefault(item => item.Handles.Contains(handle));
    }

    public long? GetMonitorHandleForWindow(long hwnd)
    {
        IntPtr monitor = MonitorFromWindow(new IntPtr(hwnd), MonitorDefaultToNearest);
        return monitor == IntPtr.Zero ? null : monitor.ToInt64();
    }

    public MonitorInfo? FindMonitorForWindow(long hwnd, DisplayTopologySnapshot? snapshot = null)
    {
        long? handle = GetMonitorHandleForWindow(hwnd);
        if (handle is null)
        {
            return null;
        }

        return FindMonitorByHandle(handle.Value, snapshot);
    }

    public MonitorInfo? GetPrimaryMonitor(DisplayTopologySnapshot? snapshot = null) =>
        (snapshot ?? GetTopologySnapshot()).Monitors.FirstOrDefault(item => item.Descriptor.IsPrimary);

    private static DisplayConfigQueryOutcome QueryDisplayConfigMap()
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            int bufferResult = GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out uint pathCount, out uint modeCount);
            if (bufferResult != ErrorSuccess)
            {
                return DisplayConfigQueryOutcome.Failed(
                    DisplayIdentityFailureStageValues.GetBufferSizes,
                    bufferResult,
                    "Не удалось получить размер буфера display topology; runtime использует `gdi:` fallback для monitor identity.");
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
                return DisplayConfigQueryOutcome.Failed(
                    DisplayIdentityFailureStageValues.QueryDisplayConfig,
                    queryResult,
                    "Не удалось получить active display topology через QueryDisplayConfig; runtime использует `gdi:` fallback для monitor identity.");
            }

            Dictionary<string, DisplayConfigSourceIdentity> map = new(StringComparer.OrdinalIgnoreCase);
            DisplayIdentityFailureInfo? firstFailure = null;
            for (int index = 0; index < pathCount; index++)
            {
                DISPLAYCONFIG_PATH_INFO path = paths[index];
                DeviceInfoQueryResult sourceNameResult = TryGetSourceGdiDeviceName(path.sourceInfo.adapterId, path.sourceInfo.id);
                string? gdiDeviceName = sourceNameResult.Value;
                if (string.IsNullOrWhiteSpace(gdiDeviceName))
                {
                    firstFailure = DisplayIdentityFailureAggregator.SelectMoreSignificant(
                        firstFailure,
                        DisplayIdentityFailureInfoFactory.Create(
                        DisplayIdentityFailureStageValues.GetSourceName,
                        sourceNameResult.ResultCode,
                        "Не удалось получить GDI имя display source; runtime использует `gdi:` fallback для monitor identity."));
                    continue;
                }

                string normalized = NormalizeGdiDeviceName(gdiDeviceName);
                DeviceInfoQueryResult friendlyNameResult = TryGetTargetFriendlyName(path.targetInfo.adapterId, path.targetInfo.id);
                string? friendlyName = friendlyNameResult.Value;
                if (friendlyNameResult.ResultCode != ErrorSuccess)
                {
                    firstFailure = DisplayIdentityFailureAggregator.SelectMoreSignificant(
                        firstFailure,
                        DisplayIdentityFailureInfoFactory.Create(
                        DisplayIdentityFailureStageValues.GetTargetName,
                        friendlyNameResult.ResultCode,
                        "Не удалось получить friendly name display target; strong monitor identity сохранена, но human-readable monitor name деградирует."));
                }

                if (!map.TryGetValue(normalized, out DisplayConfigSourceIdentity? identity))
                {
                    identity = new DisplayConfigSourceIdentity(path.sourceInfo.adapterId, path.sourceInfo.id);
                    map[normalized] = identity;
                }

                identity.AddFriendlyName(friendlyName);
            }

            return new DisplayConfigQueryOutcome(
                map,
                firstFailure?.FailedStage,
                firstFailure?.ErrorCode,
                firstFailure?.ErrorName,
                firstFailure?.MessageHuman);
        }

        return DisplayConfigQueryOutcome.Failed(
            DisplayIdentityFailureStageValues.QueryDisplayConfig,
            ErrorInsufficientBuffer,
            "Не удалось стабильно получить display topology после повтора QueryDisplayConfig; runtime использует `gdi:` fallback для monitor identity.");
    }

    private static DeviceInfoQueryResult TryGetSourceGdiDeviceName(Luid adapterId, uint sourceId)
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

        int resultCode = DisplayConfigGetDeviceInfo(ref request);
        return new(resultCode, resultCode == ErrorSuccess ? request.viewGdiDeviceName : null);
    }

    private static DeviceInfoQueryResult TryGetTargetFriendlyName(Luid adapterId, uint targetId)
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

        int resultCode = DisplayConfigGetDeviceInfo(ref request);
        return new(resultCode, resultCode == ErrorSuccess ? request.monitorFriendlyDeviceName : null);
    }

    private static string BuildDisplaySourceMonitorId(DisplayConfigSourceIdentity sourceIdentity)
        => MonitorIdFormatter.FromDisplaySource(sourceIdentity.AdapterId.HighPart, sourceIdentity.AdapterId.LowPart, sourceIdentity.SourceId);

    private static string BuildFallbackMonitorId(string gdiDeviceName)
        => MonitorIdFormatter.FromGdiDeviceName(gdiDeviceName);

    private static string NormalizeGdiDeviceName(string gdiDeviceName) =>
        (gdiDeviceName ?? string.Empty).Trim().ToUpperInvariant();

    private static string? GetWin32ErrorName(int? errorCode) =>
        errorCode switch
        {
            null => null,
            ErrorSuccess => "ERROR_SUCCESS",
            ErrorAccessDenied => "ERROR_ACCESS_DENIED",
            ErrorNotSupported => "ERROR_NOT_SUPPORTED",
            ErrorInsufficientBuffer => "ERROR_INSUFFICIENT_BUFFER",
            _ => "WIN32_" + errorCode.Value.ToString(CultureInfo.InvariantCulture),
        };

    private static HashSet<string> ListDesktopGdiDevices()
    {
        HashSet<string> devices = new(StringComparer.OrdinalIgnoreCase);

        for (uint index = 0; ; index++)
        {
            DISPLAY_DEVICE displayDevice = new() { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (!EnumDisplayDevices(null, index, ref displayDevice, 0))
            {
                break;
            }

            if ((displayDevice.StateFlags & DisplayDeviceAttachedToDesktop) == 0)
            {
                continue;
            }

            if ((displayDevice.StateFlags & DisplayDeviceMirroringDriver) != 0)
            {
                continue;
            }

            string normalized = NormalizeGdiDeviceName(displayDevice.DeviceName);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                devices.Add(normalized);
            }
        }

        return devices;
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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;

        public uint StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(
        string? lpDevice,
        uint iDevNum,
        ref DISPLAY_DEVICE lpDisplayDevice,
        uint dwFlags);

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

    private sealed class DisplayConfigSourceIdentity(Luid adapterId, uint sourceId)
    {
        private readonly HashSet<string> _friendlyNames = new(StringComparer.OrdinalIgnoreCase);

        public Luid AdapterId { get; } = adapterId;

        public uint SourceId { get; } = sourceId;

        public void AddFriendlyName(string? friendlyName)
        {
            if (!string.IsNullOrWhiteSpace(friendlyName))
            {
                _friendlyNames.Add(friendlyName);
            }
        }

        public string GetPublicFriendlyName(string gdiDeviceName)
        {
            return _friendlyNames.Count == 1
                ? _friendlyNames.First()
                : gdiDeviceName;
        }
    }

    private sealed class MonitorAccumulator(MonitorDescriptor descriptor, long captureHandle)
    {
        private readonly HashSet<long> _handles = [captureHandle];

        public MonitorDescriptor Descriptor { get; private set; } = descriptor;

        public long CaptureHandle { get; private set; } = captureHandle;

        public void AddHandle(long handle) =>
            _handles.Add(handle);

        public void ReplaceRepresentative(MonitorDescriptor descriptor, long captureHandle)
        {
            Descriptor = descriptor;
            CaptureHandle = captureHandle;
            _handles.Add(captureHandle);
        }

        public MonitorInfo Build() =>
            new(
                Descriptor,
                CaptureHandle,
                _handles.OrderBy(handle => handle).ToArray());
    }

    private readonly record struct DeviceInfoQueryResult(
        int ResultCode,
        string? Value);

    private sealed record DisplayConfigQueryOutcome(
        Dictionary<string, DisplayConfigSourceIdentity> DisplayConfigMap,
        string? FailedStage,
        int? ErrorCode,
        string? ErrorName,
        string? MessageHuman)
    {
        public static DisplayConfigQueryOutcome Failed(string failedStage, int errorCode, string messageHuman) =>
            new(
                new Dictionary<string, DisplayConfigSourceIdentity>(StringComparer.OrdinalIgnoreCase),
                failedStage,
                errorCode,
                GetWin32ErrorName(errorCode),
                $"{messageHuman} Win32 error: {GetWin32ErrorName(errorCode)} ({errorCode}).");
    }

    private static class DisplayIdentityFailureInfoFactory
    {
        public static DisplayIdentityFailureInfo Create(string failedStage, int errorCode, string messageHuman)
        {
            string errorName = GetWin32ErrorName(errorCode) ?? "WIN32_UNKNOWN";
            return new(
                failedStage,
                errorCode,
                errorName,
                $"{messageHuman} Win32 error: {errorName} ({errorCode}).");
        }
    }
}
