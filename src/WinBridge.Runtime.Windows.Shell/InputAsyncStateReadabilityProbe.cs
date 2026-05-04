// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Runtime.InteropServices;

namespace WinBridge.Runtime.Windows.Shell;

internal enum InputAsyncStateReadabilityStatus
{
    Readable,
    Unreadable,
    Unknown,
}

internal enum InputAsyncStateReadabilityMode
{
    SameProcessForeground,
    CrossProcessForeground,
}

internal sealed record InputAsyncStateReadabilityProbeResult(
    InputAsyncStateReadabilityStatus Status,
    string? Reason = null);

internal static class InputAsyncStateReadabilityProbe
{
    internal const uint DesktopHookControlAccess = 0x0008;
    internal const uint DesktopJournalRecordAccess = 0x0010;
    private const int UoiIo = 6;
    private static readonly IInputAsyncStateReadabilityPlatform Win32Platform = new Win32InputAsyncStateReadabilityPlatform();

    public static InputAsyncStateReadabilityProbeResult ProbeForCurrentThread(InputAsyncStateReadabilityMode mode)
        => ProbeForCurrentThread(mode, Win32Platform);

    public static InputAsyncStateReadabilityProbeResult ProbeForCurrentThread(
        InputAsyncStateReadabilityMode mode,
        IInputAsyncStateReadabilityPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(platform);

        IntPtr threadDesktop = platform.GetThreadDesktop(platform.GetCurrentThreadId());
        if (threadDesktop == IntPtr.Zero)
        {
            return new(
                InputAsyncStateReadabilityStatus.Unknown,
                "Runtime не смог определить desktop текущего thread для async-state proof.");
        }

        if (!platform.TryQueryDesktopReceivesInput(threadDesktop, out bool receivesInput))
        {
            return new(
                InputAsyncStateReadabilityStatus.Unknown,
                "Runtime не смог подтвердить, что текущий thread работает на active input desktop.");
        }

        if (!receivesInput)
        {
            return new(
                InputAsyncStateReadabilityStatus.Unreadable,
                "Текущий thread не работает на active input desktop, поэтому async input state нельзя считать доказуемым.");
        }

        if (mode == InputAsyncStateReadabilityMode.SameProcessForeground)
        {
            return new(InputAsyncStateReadabilityStatus.Readable);
        }

        IntPtr inputDesktop = TryOpenInputDesktopForCrossProcessAsyncState(platform);
        if (inputDesktop == IntPtr.Zero)
        {
            return new(
                InputAsyncStateReadabilityStatus.Unreadable,
                "Runtime не смог открыть active input desktop с hook/journal access, который `GetAsyncKeyState` документирует как prerequisite для cross-process foreground path.");
        }

        try
        {
            return new(InputAsyncStateReadabilityStatus.Readable);
        }
        finally
        {
            platform.CloseDesktop(inputDesktop);
        }
    }

    private static IntPtr TryOpenInputDesktopForCrossProcessAsyncState(IInputAsyncStateReadabilityPlatform platform)
    {
        IntPtr hookControlDesktop = platform.OpenInputDesktop(DesktopHookControlAccess);
        if (hookControlDesktop != IntPtr.Zero)
        {
            return hookControlDesktop;
        }

        return platform.OpenInputDesktop(DesktopJournalRecordAccess);
    }

    private sealed class Win32InputAsyncStateReadabilityPlatform : IInputAsyncStateReadabilityPlatform
    {
        public uint GetCurrentThreadId() => GetCurrentThreadIdNative();

        public IntPtr GetThreadDesktop(uint threadId) => GetThreadDesktopNative(threadId);

        public bool TryQueryDesktopReceivesInput(IntPtr desktopHandle, out bool receivesInput)
        {
            IntPtr buffer = Marshal.AllocHGlobal(sizeof(int));
            try
            {
                if (!GetUserObjectInformation(desktopHandle, UoiIo, buffer, sizeof(int), out _))
                {
                    receivesInput = false;
                    return false;
                }

                receivesInput = Marshal.ReadInt32(buffer) != 0;
                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        public IntPtr OpenInputDesktop(uint desiredAccess) =>
            OpenInputDesktopNative(0, false, desiredAccess);

        public void CloseDesktop(IntPtr hDesktop)
        {
            _ = CloseDesktopNative(hDesktop);
        }
    }

    [DllImport("user32.dll", EntryPoint = "OpenInputDesktop", SetLastError = true)]
    private static extern IntPtr OpenInputDesktopNative(uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", EntryPoint = "CloseDesktop", SetLastError = true)]
    private static extern bool CloseDesktopNative(IntPtr hDesktop);

    [DllImport("user32.dll", EntryPoint = "GetThreadDesktop")]
    private static extern IntPtr GetThreadDesktopNative(uint dwThreadId);

    [DllImport("kernel32.dll", EntryPoint = "GetCurrentThreadId")]
    private static extern uint GetCurrentThreadIdNative();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetUserObjectInformation(
        IntPtr hObj,
        int nIndex,
        IntPtr pvInfo,
        int nLength,
        out int lpnLengthNeeded);
}
