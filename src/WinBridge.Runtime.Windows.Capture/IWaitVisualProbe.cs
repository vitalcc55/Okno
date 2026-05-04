// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Capture;

public interface IWaitVisualProbe
{
    Task<WaitVisualSample> CaptureVisualSampleAsync(
        WindowDescriptor targetWindow,
        CancellationToken cancellationToken);

    Task WriteVisualEvidenceAsync(
        WaitVisualEvidenceFrame frame,
        string path,
        CancellationToken cancellationToken);
}
