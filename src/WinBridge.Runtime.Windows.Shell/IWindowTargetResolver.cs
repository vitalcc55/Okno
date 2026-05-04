// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Shell;

public interface IWindowTargetResolver
{
    WindowDescriptor? ResolveExplicitOrAttachedWindow(long? explicitHwnd, WindowDescriptor? attachedWindow);

    LiveWindowIdentityResolution ResolveLiveWindowByIdentity(WindowDescriptor expectedWindow);

    UiaSnapshotTargetResolution ResolveUiaSnapshotTarget(long? explicitHwnd, WindowDescriptor? attachedWindow);

    InputTargetResolution ResolveInputTarget(long? explicitHwnd, WindowDescriptor? attachedWindow);

    WaitTargetResolution ResolveWaitTarget(long? explicitHwnd, WindowDescriptor? attachedWindow);
}
