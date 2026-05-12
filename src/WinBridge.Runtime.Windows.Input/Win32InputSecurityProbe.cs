// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Diagnostics;
using System.Runtime.InteropServices;
using Native = WinBridge.Runtime.Windows.Input.Win32InputNativeMethods;

namespace WinBridge.Runtime.Windows.Input;

internal static class Win32InputSecurityProbe
{
    public static InputProcessSecurityContext ProbeCurrentProcessSecurity()
    {
        using Process process = Process.GetCurrentProcess();
        bool sessionResolved = Native.ProcessIdToSessionId((uint)process.Id, out uint sessionId);

        if (!Native.OpenProcessToken(process.Handle, Native.TokenQuery, out IntPtr tokenHandle))
        {
            return new(
                SessionId: sessionResolved ? checked((int)sessionId) : null,
                SessionResolved: sessionResolved,
                IntegrityLevel: null,
                IntegrityResolved: false,
                HasUiAccess: false,
                UiAccessResolved: false,
                Reason: "Runtime не смог открыть token текущего процесса для input preflight.");
        }

        try
        {
            bool integrityResolved = TryQueryIntegrity(tokenHandle, out InputIntegrityLevel? integrityLevel);
            bool uiAccessResolved = TryQueryUInt32(tokenHandle, Native.TokenInformationClass.TokenUIAccess, out uint tokenUiAccess);

            return new(
                SessionId: sessionResolved ? checked((int)sessionId) : null,
                SessionResolved: sessionResolved,
                IntegrityLevel: integrityLevel,
                IntegrityResolved: integrityResolved,
                HasUiAccess: uiAccessResolved && tokenUiAccess != 0,
                UiAccessResolved: uiAccessResolved,
                Reason: BuildCurrentProcessProbeReason(sessionResolved, integrityResolved, uiAccessResolved));
        }
        finally
        {
            _ = Native.CloseHandle(tokenHandle);
        }
    }

    public static InputTargetSecurityInfo ProbeTargetSecurity(long hwnd, int? processIdHint)
    {
        int? processId = processIdHint;
        if (processId is null or <= 0)
        {
            _ = Native.GetWindowThreadProcessId(new IntPtr(hwnd), out uint liveProcessId);
            if (liveProcessId > 0)
            {
                processId = checked((int)liveProcessId);
            }
        }

        if (processId is null or <= 0)
        {
            return new(
                ProcessId: null,
                SessionId: null,
                SessionResolved: false,
                IntegrityLevel: null,
                IntegrityResolved: false,
                Reason: "Runtime не смог определить process id окна-цели для input preflight.");
        }

        bool sessionResolved = Native.ProcessIdToSessionId((uint)processId.Value, out uint sessionId);
        IntPtr processHandle = Native.OpenProcess(Native.ProcessQueryLimitedInformation, false, (uint)processId.Value);
        if (processHandle == IntPtr.Zero)
        {
            return new(
                ProcessId: processId,
                SessionId: sessionResolved ? checked((int)sessionId) : null,
                SessionResolved: sessionResolved,
                IntegrityLevel: null,
                IntegrityResolved: false,
                Reason: "Runtime не смог открыть target process для input preflight.");
        }

        try
        {
            if (!Native.OpenProcessToken(processHandle, Native.TokenQuery, out IntPtr tokenHandle))
            {
                return new(
                    ProcessId: processId,
                    SessionId: sessionResolved ? checked((int)sessionId) : null,
                    SessionResolved: sessionResolved,
                    IntegrityLevel: null,
                    IntegrityResolved: false,
                    Reason: "Runtime не смог открыть target token для input preflight.");
            }

            try
            {
                bool integrityResolved = TryQueryIntegrity(tokenHandle, out InputIntegrityLevel? integrityLevel);
                return new(
                    ProcessId: processId,
                    SessionId: sessionResolved ? checked((int)sessionId) : null,
                    SessionResolved: sessionResolved,
                    IntegrityLevel: integrityLevel,
                    IntegrityResolved: integrityResolved,
                    Reason: integrityResolved
                        ? null
                        : "Runtime не смог определить integrity окна-цели для input preflight.");
            }
            finally
            {
                _ = Native.CloseHandle(tokenHandle);
            }
        }
        finally
        {
            _ = Native.CloseHandle(processHandle);
        }
    }

    private static string? BuildCurrentProcessProbeReason(bool sessionResolved, bool integrityResolved, bool uiAccessResolved)
    {
        if (!sessionResolved)
        {
            return "Runtime не смог определить session текущего процесса для input preflight.";
        }

        if (!integrityResolved)
        {
            return "Runtime не смог определить integrity текущего процесса для input preflight.";
        }

        if (!uiAccessResolved)
        {
            return "Runtime не смог определить uiAccess flag текущего процесса для input preflight.";
        }

        return null;
    }

    private static bool TryQueryIntegrity(IntPtr tokenHandle, out InputIntegrityLevel? integrityLevel)
    {
        integrityLevel = null;
        if (!TryQueryBuffer(tokenHandle, Native.TokenInformationClass.TokenIntegrityLevel, out IntPtr buffer))
        {
            return false;
        }

        try
        {
            Native.TOKEN_MANDATORY_LABEL label = Marshal.PtrToStructure<Native.TOKEN_MANDATORY_LABEL>(buffer);
            if (label.Label.Sid == IntPtr.Zero || !Native.IsValidSid(label.Label.Sid))
            {
                return false;
            }

            IntPtr subAuthorityCountPointer = Native.GetSidSubAuthorityCount(label.Label.Sid);
            if (subAuthorityCountPointer == IntPtr.Zero)
            {
                return false;
            }

            byte subAuthorityCount = Marshal.ReadByte(subAuthorityCountPointer);
            if (subAuthorityCount == 0)
            {
                return false;
            }

            IntPtr ridPointer = Native.GetSidSubAuthority(label.Label.Sid, (uint)(subAuthorityCount - 1));
            if (ridPointer == IntPtr.Zero)
            {
                return false;
            }

            int rid = Marshal.ReadInt32(ridPointer);
            integrityLevel = MapIntegrityLevel(rid);
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static InputIntegrityLevel MapIntegrityLevel(int rid)
    {
        if (rid < Native.SecurityMandatoryMediumRid)
        {
            return InputIntegrityLevel.Low;
        }

        if (rid < Native.SecurityMandatoryHighRid)
        {
            return InputIntegrityLevel.Medium;
        }

        if (rid < Native.SecurityMandatorySystemRid)
        {
            return InputIntegrityLevel.High;
        }

        return InputIntegrityLevel.SystemOrAbove;
    }

    private static bool TryQueryUInt32(
        IntPtr tokenHandle,
        Native.TokenInformationClass informationClass,
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
        Native.TokenInformationClass informationClass,
        out IntPtr buffer)
    {
        buffer = IntPtr.Zero;
        _ = Native.GetTokenInformation(tokenHandle, informationClass, IntPtr.Zero, 0, out int requiredLength);
        if (requiredLength <= 0 || Marshal.GetLastWin32Error() != Native.ErrorInsufficientBuffer)
        {
            return false;
        }

        buffer = Marshal.AllocHGlobal(requiredLength);
        if (!Native.GetTokenInformation(tokenHandle, informationClass, buffer, requiredLength, out _))
        {
            Marshal.FreeHGlobal(buffer);
            buffer = IntPtr.Zero;
            return false;
        }

        return true;
    }
}
