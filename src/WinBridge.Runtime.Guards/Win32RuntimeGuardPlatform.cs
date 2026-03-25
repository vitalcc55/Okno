using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WinBridge.Runtime.Guards;

internal sealed class Win32RuntimeGuardPlatform : IRuntimeGuardPlatform
{
    private const int WtsCurrentSession = -1;
    private const uint DesktopReadObjects = 0x0001;
    private const uint TokenQuery = 0x0008;
    private const int ErrorInsufficientBuffer = 122;
    private const int SecurityMandatoryLowRid = 0x00001000;
    private const int SecurityMandatoryMediumRid = 0x00002000;
    private const int SecurityMandatoryHighRid = 0x00003000;
    private const int SecurityMandatorySystemRid = 0x00004000;

    public RuntimeGuardRawFacts Probe() =>
        new(
            DesktopSession: ProbeDesktopSession(),
            SessionAlignment: ProbeSessionAlignment(),
            Token: ProbeToken());

    private static DesktopSessionProbeResult ProbeDesktopSession()
    {
        IntPtr desktop = OpenInputDesktop(0, false, DesktopReadObjects);
        if (desktop == IntPtr.Zero)
        {
            return new DesktopSessionProbeResult(InputDesktopAvailable: false, ErrorCode: Marshal.GetLastWin32Error());
        }

        try
        {
            return new DesktopSessionProbeResult(InputDesktopAvailable: true, ErrorCode: null);
        }
        finally
        {
            _ = CloseDesktop(desktop);
        }
    }

    private static SessionAlignmentProbeResult ProbeSessionAlignment()
    {
        bool processSessionResolved = ProcessIdToSessionId((uint)Environment.ProcessId, out uint processSessionId);
        SessionConnectState? connectState = TryQueryConnectState();
        ushort? clientProtocolType = TryQueryClientProtocolType();

        return new SessionAlignmentProbeResult(
            ProcessSessionResolved: processSessionResolved,
            ProcessSessionId: processSessionResolved ? processSessionId : null,
            ActiveConsoleSessionId: WTSGetActiveConsoleSessionId(),
            ConnectState: connectState,
            ClientProtocolType: clientProtocolType);
    }

    private static TokenProbeResult ProbeToken()
    {
        using Process process = Process.GetCurrentProcess();
        if (!OpenProcessToken(process.Handle, TokenQuery, out IntPtr tokenHandle))
        {
            return new TokenProbeResult(
                IntegrityResolved: false,
                IntegrityLevel: null,
                IntegrityRid: null,
                ElevationResolved: false,
                IsElevated: false,
                ElevationType: null,
                UiAccessResolved: false,
                UiAccess: false);
        }

        try
        {
            bool integrityResolved = TryQueryIntegrity(tokenHandle, out RuntimeIntegrityLevel? integrityLevel, out int? integrityRid);
            bool elevationResolved = TryQueryUInt32(tokenHandle, TokenInformationClass.TokenElevation, out uint tokenElevation);
            bool elevationTypeResolved = TryQueryUInt32(tokenHandle, TokenInformationClass.TokenElevationType, out uint tokenElevationType);
            bool uiAccessResolved = TryQueryUInt32(tokenHandle, TokenInformationClass.TokenUIAccess, out uint tokenUiAccess);

            return new TokenProbeResult(
                IntegrityResolved: integrityResolved,
                IntegrityLevel: integrityLevel,
                IntegrityRid: integrityRid,
                ElevationResolved: elevationResolved && elevationTypeResolved,
                IsElevated: elevationResolved && tokenElevation != 0,
                ElevationType: elevationTypeResolved ? (TokenElevationTypeValue)tokenElevationType : null,
                UiAccessResolved: uiAccessResolved,
                UiAccess: uiAccessResolved && tokenUiAccess != 0);
        }
        finally
        {
            _ = CloseHandle(tokenHandle);
        }
    }

    private static SessionConnectState? TryQueryConnectState()
    {
        if (!WTSQuerySessionInformation(IntPtr.Zero, WtsCurrentSession, WtsInfoClass.WTSConnectState, out IntPtr buffer, out int bytesReturned))
        {
            return null;
        }

        try
        {
            if (buffer == IntPtr.Zero || bytesReturned < sizeof(int))
            {
                return null;
            }

            return (SessionConnectState)Marshal.ReadInt32(buffer);
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    private static ushort? TryQueryClientProtocolType()
    {
        if (!WTSQuerySessionInformation(IntPtr.Zero, WtsCurrentSession, WtsInfoClass.WTSClientProtocolType, out IntPtr buffer, out int bytesReturned))
        {
            return null;
        }

        try
        {
            if (buffer == IntPtr.Zero || bytesReturned < sizeof(short))
            {
                return null;
            }

            return unchecked((ushort)Marshal.ReadInt16(buffer));
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    private static bool TryQueryIntegrity(
        IntPtr tokenHandle,
        out RuntimeIntegrityLevel? integrityLevel,
        out int? integrityRid)
    {
        integrityLevel = null;
        integrityRid = null;

        if (!TryQueryBuffer(tokenHandle, TokenInformationClass.TokenIntegrityLevel, out IntPtr buffer))
        {
            return false;
        }

        try
        {
            return TryParseIntegrityLabel(buffer, out integrityLevel, out integrityRid);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool TryParseIntegrityLabel(
        IntPtr buffer,
        out RuntimeIntegrityLevel? integrityLevel,
        out int? integrityRid)
    {
        integrityLevel = null;
        integrityRid = null;

        TOKEN_MANDATORY_LABEL label = Marshal.PtrToStructure<TOKEN_MANDATORY_LABEL>(buffer);
        if (label.Label.Sid == IntPtr.Zero || !IsValidSid(label.Label.Sid))
        {
            return false;
        }

        IntPtr subAuthorityCountPointer = GetSidSubAuthorityCount(label.Label.Sid);
        if (subAuthorityCountPointer == IntPtr.Zero)
        {
            return false;
        }

        byte subAuthorityCount = Marshal.ReadByte(subAuthorityCountPointer);
        if (subAuthorityCount == 0)
        {
            return false;
        }

        IntPtr ridPointer = GetSidSubAuthority(label.Label.Sid, (uint)(subAuthorityCount - 1));
        if (ridPointer == IntPtr.Zero)
        {
            return false;
        }

        int rid = Marshal.ReadInt32(ridPointer);
        integrityRid = rid;
        integrityLevel = MapIntegrityLevel(rid);
        return true;
    }

    private static bool TryQueryUInt32(
        IntPtr tokenHandle,
        TokenInformationClass informationClass,
        out uint value)
    {
        value = 0;
        if (!TryQueryBuffer(tokenHandle, informationClass, out IntPtr buffer))
        {
            return false;
        }

        try
        {
            value = unchecked((uint)Marshal.ReadInt32(buffer));
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool TryQueryBuffer(
        IntPtr tokenHandle,
        TokenInformationClass informationClass,
        out IntPtr buffer)
    {
        buffer = IntPtr.Zero;
        _ = GetTokenInformation(tokenHandle, informationClass, IntPtr.Zero, 0, out int requiredLength);
        if (requiredLength <= 0 || Marshal.GetLastWin32Error() != ErrorInsufficientBuffer)
        {
            return false;
        }

        buffer = Marshal.AllocHGlobal(requiredLength);
        if (!GetTokenInformation(tokenHandle, informationClass, buffer, requiredLength, out _))
        {
            Marshal.FreeHGlobal(buffer);
            buffer = IntPtr.Zero;
            return false;
        }

        return true;
    }

    private static RuntimeIntegrityLevel MapIntegrityLevel(int rid)
    {
        if (rid < SecurityMandatoryLowRid)
        {
            return RuntimeIntegrityLevel.Untrusted;
        }

        if (rid < SecurityMandatoryMediumRid)
        {
            return RuntimeIntegrityLevel.Low;
        }

        if (rid < SecurityMandatoryHighRid)
        {
            return RuntimeIntegrityLevel.Medium;
        }

        if (rid < SecurityMandatorySystemRid)
        {
            return RuntimeIntegrityLevel.High;
        }

        return RuntimeIntegrityLevel.SystemOrAbove;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseDesktop(IntPtr hDesktop);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ProcessIdToSessionId(uint dwProcessId, out uint pSessionId);

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool WTSQuerySessionInformation(
        IntPtr hServer,
        int sessionId,
        WtsInfoClass wtsInfoClass,
        out IntPtr ppBuffer,
        out int pBytesReturned);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr memory);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(
        IntPtr tokenHandle,
        TokenInformationClass tokenInformationClass,
        IntPtr tokenInformation,
        int tokenInformationLength,
        out int returnLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern IntPtr GetSidSubAuthority(IntPtr sid, uint subAuthority);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool IsValidSid(IntPtr sid);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    private enum TokenInformationClass
    {
        TokenElevationType = 18,
        TokenElevation = 20,
        TokenIntegrityLevel = 25,
        TokenUIAccess = 26,
    }

    private enum WtsInfoClass
    {
        WTSConnectState = 8,
        WTSClientProtocolType = 16,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_MANDATORY_LABEL
    {
        public SID_AND_ATTRIBUTES Label;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SID_AND_ATTRIBUTES
    {
        public IntPtr Sid;
        public uint Attributes;
    }
}
