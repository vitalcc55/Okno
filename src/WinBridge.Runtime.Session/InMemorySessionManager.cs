// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Session;

public sealed class InMemorySessionManager : ISessionManager
{
    private readonly TimeProvider _timeProvider;
    private readonly object _sync = new();
    private SessionSnapshot _snapshot;

    public InMemorySessionManager(TimeProvider timeProvider, SessionContext sessionContext)
    {
        _timeProvider = timeProvider;
        _snapshot = SessionSnapshot.CreateInitial(sessionContext.RunId, _timeProvider.GetUtcNow());
    }

    public SessionSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            return _snapshot;
        }
    }

    public AttachedWindow? GetAttachedWindow()
    {
        lock (_sync)
        {
            return _snapshot.AttachedWindow;
        }
    }

    public SessionMutation Attach(WindowDescriptor window, string matchStrategy)
    {
        lock (_sync)
        {
            SessionSnapshot before = _snapshot;
            SessionSnapshot after = new(
                Mode: "window",
                AttachedWindow: new AttachedWindow(window, matchStrategy),
                UpdatedAtUtc: _timeProvider.GetUtcNow(),
                RunId: _snapshot.RunId);

            bool changed = before.AttachedWindow?.Window.Hwnd != window.Hwnd;
            _snapshot = after;

            return new SessionMutation(before, after, changed);
        }
    }
}
