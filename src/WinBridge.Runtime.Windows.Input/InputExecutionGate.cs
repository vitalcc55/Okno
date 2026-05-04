// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Runtime.Windows.Input;

internal static class InputExecutionGate
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static async ValueTask<IAsyncDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Releaser();
    }

    private sealed class Releaser : IAsyncDisposable
    {
        private bool _released;

        public ValueTask DisposeAsync()
        {
            if (!_released)
            {
                Gate.Release();
                _released = true;
            }

            return ValueTask.CompletedTask;
        }
    }
}
