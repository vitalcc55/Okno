// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Shell;

public sealed class WindowTargetResolver(IWindowManager windowManager) : IWindowTargetResolver
{
    public WindowDescriptor? ResolveExplicitOrAttachedWindow(long? explicitHwnd, WindowDescriptor? attachedWindow)
    {
        IReadOnlyList<WindowDescriptor> liveWindows = windowManager.ListWindows(includeInvisible: true);
        return ResolveExplicitOrAttachedWindowCore(explicitHwnd, attachedWindow, liveWindows);
    }

    public LiveWindowIdentityResolution ResolveLiveWindowByIdentity(WindowDescriptor expectedWindow)
    {
        if (!WindowIdentityValidator.TryValidateStableIdentity(expectedWindow, out string? expectedIdentityReason))
        {
            return LiveWindowIdentityResolution.IdentityProofUnavailable(expectedIdentityReason!);
        }

        IReadOnlyList<WindowDescriptor> liveWindows = windowManager.ListWindows(includeInvisible: true);
        WindowDescriptor? liveCandidate = liveWindows.FirstOrDefault(candidate => candidate.Hwnd == expectedWindow.Hwnd);
        if (liveCandidate is null)
        {
            return LiveWindowIdentityResolution.MissingTarget("Окно больше не найдено среди live top-level windows.");
        }

        if (!WindowIdentityValidator.TryValidateStableIdentity(liveCandidate, out string? liveIdentityReason))
        {
            return LiveWindowIdentityResolution.IdentityProofUnavailable(liveIdentityReason!);
        }

        return WindowIdentityValidator.MatchesStableIdentity(liveCandidate, expectedWindow)
            ? LiveWindowIdentityResolution.Resolved(liveCandidate)
            : LiveWindowIdentityResolution.IdentityChanged("Live HWND больше не совпадает с expected stable identity.");
    }

    public UiaSnapshotTargetResolution ResolveUiaSnapshotTarget(long? explicitHwnd, WindowDescriptor? attachedWindow)
    {
        IReadOnlyList<WindowDescriptor> liveWindows = windowManager.ListWindows(includeInvisible: true);
        TargetResolutionCore resolution = ResolveCapabilityTargetCore(explicitHwnd, attachedWindow, liveWindows);
        return new(
            resolution.Window,
            MapUiaSnapshotTargetSource(resolution.Source),
            MapUiaSnapshotTargetFailureCode(resolution.FailureCode));
    }

    public WaitTargetResolution ResolveWaitTarget(long? explicitHwnd, WindowDescriptor? attachedWindow)
    {
        IReadOnlyList<WindowDescriptor> liveWindows = windowManager.ListWindows(includeInvisible: true);
        TargetResolutionCore resolution = ResolveCapabilityTargetCore(explicitHwnd, attachedWindow, liveWindows);
        if (resolution.Window is WindowDescriptor candidate
            && !WindowIdentityValidator.TryValidateStableIdentity(candidate, out _))
        {
            string? failureCode = resolution.Source switch
            {
                TargetResolutionSource.Explicit => WaitTargetFailureValues.StaleExplicitTarget,
                TargetResolutionSource.Attached => WaitTargetFailureValues.StaleAttachedTarget,
                TargetResolutionSource.Active => WaitTargetFailureValues.MissingTarget,
                _ => MapWaitTargetFailureCode(resolution.FailureCode),
            };

            return new(FailureCode: failureCode);
        }

        return new(
            resolution.Window,
            MapWaitTargetSource(resolution.Source),
            MapWaitTargetFailureCode(resolution.FailureCode));
    }

    public InputTargetResolution ResolveInputTarget(long? explicitHwnd, WindowDescriptor? attachedWindow)
    {
        IReadOnlyList<WindowDescriptor> liveWindows = windowManager.ListWindows(includeInvisible: true);
        if (explicitHwnd is long hwnd)
        {
            if (hwnd <= 0)
            {
                return new(FailureCode: InputTargetFailureValues.StaleExplicitTarget);
            }

            WindowDescriptor? explicitWindow = liveWindows.FirstOrDefault(candidate => candidate.Hwnd == hwnd);
            if (explicitWindow is null || !WindowIdentityValidator.TryValidateStableIdentity(explicitWindow, out _))
            {
                return new(FailureCode: InputTargetFailureValues.StaleExplicitTarget);
            }

            if (attachedWindow is not null
                && attachedWindow.Hwnd == hwnd
                && WindowIdentityValidator.TryValidateStableIdentity(attachedWindow, out _)
                && !WindowIdentityValidator.MatchesStableIdentity(explicitWindow, attachedWindow))
            {
                return new(FailureCode: InputTargetFailureValues.StaleExplicitTarget);
            }

            return explicitWindow is null
                ? new(FailureCode: InputTargetFailureValues.StaleExplicitTarget)
                : new(explicitWindow, InputTargetSourceValues.Explicit);
        }

        if (attachedWindow is not null)
        {
            WindowDescriptor? resolvedAttachedWindow = ResolveExplicitOrAttachedWindowCore(explicitHwnd: null, attachedWindow, liveWindows);
            return resolvedAttachedWindow is null
                ? new(FailureCode: InputTargetFailureValues.StaleAttachedTarget)
                : new(resolvedAttachedWindow, InputTargetSourceValues.Attached);
        }

        return new(FailureCode: InputTargetFailureValues.MissingTarget);
    }

    private static WindowDescriptor? ResolveExplicitOrAttachedWindowCore(
        long? explicitHwnd,
        WindowDescriptor? attachedWindow,
        IReadOnlyList<WindowDescriptor> liveWindows)
    {
        if (explicitHwnd is long hwnd)
        {
            return liveWindows.FirstOrDefault(candidate => candidate.Hwnd == hwnd);
        }

        if (attachedWindow is null)
        {
            return null;
        }

        if (!WindowIdentityValidator.TryValidateStableIdentity(attachedWindow, out _))
        {
            return null;
        }

        WindowDescriptor? liveCandidate = liveWindows.FirstOrDefault(candidate => candidate.Hwnd == attachedWindow.Hwnd);
        if (liveCandidate is null)
        {
            return null;
        }

        return WindowIdentityValidator.MatchesStableIdentity(liveCandidate, attachedWindow) ? liveCandidate : null;
    }

    private static TargetResolutionCore ResolveCapabilityTargetCore(
        long? explicitHwnd,
        WindowDescriptor? attachedWindow,
        IReadOnlyList<WindowDescriptor> liveWindows)
    {
        if (explicitHwnd is long hwnd)
        {
            if (hwnd <= 0)
            {
                return new(FailureCode: TargetResolutionFailureCode.StaleExplicitTarget);
            }

            WindowDescriptor? explicitWindow = ResolveExplicitOrAttachedWindowCore(hwnd, attachedWindow: null, liveWindows);
            return explicitWindow is null
                ? new(FailureCode: TargetResolutionFailureCode.StaleExplicitTarget)
                : new(explicitWindow, TargetResolutionSource.Explicit);
        }

        if (attachedWindow is not null)
        {
            WindowDescriptor? resolvedAttachedWindow = ResolveExplicitOrAttachedWindowCore(explicitHwnd: null, attachedWindow, liveWindows);
            return resolvedAttachedWindow is null
                ? new(FailureCode: TargetResolutionFailureCode.StaleAttachedTarget)
                : new(resolvedAttachedWindow, TargetResolutionSource.Attached);
        }

        WindowDescriptor[] activeCandidates = liveWindows
            .Where(candidate => candidate.IsForeground)
            .GroupBy(candidate => candidate.Hwnd)
            .Select(group => group.First())
            .Take(2)
            .ToArray();

        return activeCandidates.Length switch
        {
            0 => new(FailureCode: TargetResolutionFailureCode.MissingTarget),
            1 => new(activeCandidates[0], TargetResolutionSource.Active),
            _ => new(FailureCode: TargetResolutionFailureCode.AmbiguousActiveTarget),
        };
    }

    private static string? MapUiaSnapshotTargetSource(TargetResolutionSource? source) =>
        source switch
        {
            TargetResolutionSource.Explicit => UiaSnapshotTargetSourceValues.Explicit,
            TargetResolutionSource.Attached => UiaSnapshotTargetSourceValues.Attached,
            TargetResolutionSource.Active => UiaSnapshotTargetSourceValues.Active,
            _ => null,
        };

    private static string? MapUiaSnapshotTargetFailureCode(TargetResolutionFailureCode? failureCode) =>
        failureCode switch
        {
            TargetResolutionFailureCode.MissingTarget => UiaSnapshotTargetFailureValues.MissingTarget,
            TargetResolutionFailureCode.StaleExplicitTarget => UiaSnapshotTargetFailureValues.StaleExplicitTarget,
            TargetResolutionFailureCode.StaleAttachedTarget => UiaSnapshotTargetFailureValues.StaleAttachedTarget,
            TargetResolutionFailureCode.AmbiguousActiveTarget => UiaSnapshotTargetFailureValues.AmbiguousActiveTarget,
            _ => null,
        };

    private static string? MapWaitTargetSource(TargetResolutionSource? source) =>
        source switch
        {
            TargetResolutionSource.Explicit => WaitTargetSourceValues.Explicit,
            TargetResolutionSource.Attached => WaitTargetSourceValues.Attached,
            TargetResolutionSource.Active => WaitTargetSourceValues.Active,
            _ => null,
        };

    private static string? MapWaitTargetFailureCode(TargetResolutionFailureCode? failureCode) =>
        failureCode switch
        {
            TargetResolutionFailureCode.MissingTarget => WaitTargetFailureValues.MissingTarget,
            TargetResolutionFailureCode.StaleExplicitTarget => WaitTargetFailureValues.StaleExplicitTarget,
            TargetResolutionFailureCode.StaleAttachedTarget => WaitTargetFailureValues.StaleAttachedTarget,
            TargetResolutionFailureCode.AmbiguousActiveTarget => WaitTargetFailureValues.AmbiguousActiveTarget,
            _ => null,
        };

    private readonly record struct TargetResolutionCore(
        WindowDescriptor? Window = null,
        TargetResolutionSource? Source = null,
        TargetResolutionFailureCode? FailureCode = null);

    private enum TargetResolutionSource
    {
        Explicit,
        Attached,
        Active,
    }

    private enum TargetResolutionFailureCode
    {
        MissingTarget,
        StaleExplicitTarget,
        StaleAttachedTarget,
        AmbiguousActiveTarget,
    }
}
