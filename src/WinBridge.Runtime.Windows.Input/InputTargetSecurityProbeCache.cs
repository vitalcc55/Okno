// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Shell;

namespace WinBridge.Runtime.Windows.Input;

internal sealed class InputTargetSecurityProbeCache(IInputPlatform platform)
{
    private readonly Dictionary<int, InputTargetSecurityInfo> securityInfoByProcessId = [];

    public InputTargetSecurityInfo Probe(WindowDescriptor liveTargetWindow)
    {
        ArgumentNullException.ThrowIfNull(liveTargetWindow);

        if (liveTargetWindow.ProcessId is int processId
            && processId > 0
            && securityInfoByProcessId.TryGetValue(processId, out InputTargetSecurityInfo? cachedSecurityInfo))
        {
            return cachedSecurityInfo;
        }

        InputTargetSecurityInfo securityInfo = platform.ProbeTargetSecurity(liveTargetWindow.Hwnd, liveTargetWindow.ProcessId);
        if (securityInfo.ProcessId is int resolvedProcessId && resolvedProcessId > 0)
        {
            securityInfoByProcessId[resolvedProcessId] = securityInfo;
        }

        return securityInfo;
    }
}
