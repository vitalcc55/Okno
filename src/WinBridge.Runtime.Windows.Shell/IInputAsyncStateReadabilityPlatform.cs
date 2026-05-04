// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Runtime.Windows.Shell;

internal interface IInputAsyncStateReadabilityPlatform
{
    uint GetCurrentThreadId();

    IntPtr GetThreadDesktop(uint threadId);

    bool TryQueryDesktopReceivesInput(IntPtr desktopHandle, out bool receivesInput);

    IntPtr OpenInputDesktop(uint desiredAccess);

    void CloseDesktop(IntPtr hDesktop);
}
