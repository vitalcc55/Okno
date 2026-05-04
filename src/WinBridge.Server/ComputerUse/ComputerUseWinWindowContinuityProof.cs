// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Shell;

namespace WinBridge.Server.ComputerUse;

internal enum ComputerUseWinStoredStateValidationMode
{
    SemanticElementAction,
    CoordinateScreenAction,
    CoordinateCapturePixelsAction,
}

internal static class ComputerUseWinWindowContinuityProof
{
    // Windows does not expose a non-reusable public instance id for top-level HWNDs here.
    // The product therefore uses different proof strengths for different paths:
    // - discovery selector (`windowId`) is strict and discovery-scoped;
    // - attached session refresh tolerates ordinary post-action UI drift;
    // - observed state revalidation is path-specific: semantic actions can rely on
    //   fresh UIA revalidation, while coordinate actions require stable live geometry.
    public static bool MatchesDiscoverySelector(WindowDescriptor liveWindow, WindowDescriptor discoveredWindow)
    {
        ArgumentNullException.ThrowIfNull(liveWindow);
        ArgumentNullException.ThrowIfNull(discoveredWindow);

        return MatchesInstanceContinuity(liveWindow, discoveredWindow)
            && string.Equals(liveWindow.ProcessName, discoveredWindow.ProcessName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(liveWindow.Title, discoveredWindow.Title, StringComparison.Ordinal)
            && Equals(liveWindow.Bounds, discoveredWindow.Bounds)
            && liveWindow.IsVisible == discoveredWindow.IsVisible
            && liveWindow.EffectiveDpi == discoveredWindow.EffectiveDpi
            && liveWindow.DpiScale.Equals(discoveredWindow.DpiScale)
            && string.Equals(liveWindow.WindowState, discoveredWindow.WindowState, StringComparison.Ordinal)
            && string.Equals(liveWindow.MonitorId, discoveredWindow.MonitorId, StringComparison.Ordinal)
            && string.Equals(liveWindow.MonitorFriendlyName, discoveredWindow.MonitorFriendlyName, StringComparison.Ordinal);
    }

    public static bool MatchesAttachedSession(WindowDescriptor liveWindow, WindowDescriptor attachedWindow)
    {
        ArgumentNullException.ThrowIfNull(liveWindow);
        ArgumentNullException.ThrowIfNull(attachedWindow);

        return MatchesInstanceContinuity(liveWindow, attachedWindow);
    }

    public static bool MatchesObservedState(
        WindowDescriptor liveWindow,
        ComputerUseWinStoredState observedState,
        ComputerUseWinStoredStateValidationMode validationMode)
    {
        ArgumentNullException.ThrowIfNull(liveWindow);
        ArgumentNullException.ThrowIfNull(observedState);

        return validationMode switch
        {
            ComputerUseWinStoredStateValidationMode.SemanticElementAction =>
                MatchesInstanceContinuity(liveWindow, observedState.Window),
            ComputerUseWinStoredStateValidationMode.CoordinateScreenAction =>
                MatchesObservedCoordinateAction(liveWindow, observedState.Window),
            ComputerUseWinStoredStateValidationMode.CoordinateCapturePixelsAction =>
                MatchesObservedCoordinateAction(liveWindow, observedState.Window)
                && (observedState.CaptureReference is null
                    || CaptureReferenceGeometryPolicy.MatchesCaptureReferenceWindowProof(observedState.CaptureReference, liveWindow)),
            _ => throw new ArgumentOutOfRangeException(nameof(validationMode), validationMode, "Неизвестный validation mode для observed state."),
        };
    }

    private static bool MatchesInstanceContinuity(WindowDescriptor liveWindow, WindowDescriptor expectedWindow) =>
        liveWindow.Hwnd == expectedWindow.Hwnd
        && WindowIdentityValidator.MatchesStableIdentity(liveWindow, expectedWindow)
        && string.Equals(liveWindow.ProcessName, expectedWindow.ProcessName, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesObservedCoordinateAction(WindowDescriptor liveWindow, WindowDescriptor observedWindow) =>
        MatchesInstanceContinuity(liveWindow, observedWindow)
        && CaptureReferenceGeometryPolicy.BoundsMatchWithinDrift(observedWindow.Bounds, liveWindow.Bounds)
        && liveWindow.EffectiveDpi == observedWindow.EffectiveDpi
        && string.Equals(liveWindow.MonitorId, observedWindow.MonitorId, StringComparison.Ordinal);
}
