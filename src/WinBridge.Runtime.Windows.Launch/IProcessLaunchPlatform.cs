// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Diagnostics;

namespace WinBridge.Runtime.Windows.Launch;

internal interface IProcessLaunchPlatform
{
    IStartedProcessHandle? Start(ProcessStartInfo startInfo);
}
