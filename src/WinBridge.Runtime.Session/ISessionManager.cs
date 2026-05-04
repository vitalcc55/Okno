// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Session;

public interface ISessionManager
{
    SessionSnapshot GetSnapshot();

    AttachedWindow? GetAttachedWindow();

    SessionMutation Attach(WindowDescriptor window, string matchStrategy);
}
