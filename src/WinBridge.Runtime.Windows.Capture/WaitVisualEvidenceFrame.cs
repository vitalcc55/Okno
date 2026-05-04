// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Capture;

public abstract class WaitVisualEvidenceFrame : IDisposable
{
    private int _disposeState;

    protected WaitVisualEvidenceFrame(
        WindowDescriptor window,
        int pixelWidth,
        int pixelHeight)
    {
        Window = window ?? throw new ArgumentNullException(nameof(window));
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
    }

    public WindowDescriptor Window { get; }
    public int PixelWidth { get; }
    public int PixelHeight { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        DisposeCore();
        GC.SuppressFinalize(this);
    }

    protected abstract void DisposeCore();
}
