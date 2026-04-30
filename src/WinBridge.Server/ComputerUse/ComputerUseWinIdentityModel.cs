using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Shell;

namespace WinBridge.Server.ComputerUse;

internal readonly record struct ComputerUseWinApprovalKey(string Value)
{
    public override string ToString() => Value;
}

internal readonly record struct ComputerUseWinWindowInstanceIdentity(string Value)
{
    public override string ToString() => Value;
}

internal sealed record ComputerUseWinExecutionTarget(
    ComputerUseWinApprovalKey ApprovalKey,
    ComputerUseWinWindowInstanceIdentity ExecutionTargetId,
    string? PublicWindowId,
    WindowDescriptor Window);

internal sealed record ComputerUseWinDiscoveredApp(
    ComputerUseWinApprovalKey ApprovalKey,
    IReadOnlyList<ComputerUseWinExecutionTarget> Windows,
    bool IsApproved,
    bool IsBlocked,
    string? BlockReason);

internal sealed class ComputerUseWinExecutionTargetCatalog
{
    private readonly TimeProvider timeProvider;
    private readonly TimeSpan entryTtl;
    private readonly int maxEntries;
    private readonly object gate = new();
    private long nextGeneration = 1;
    private long? latestPublishedDiscoveryGeneration;
    private readonly Dictionary<string, CatalogEntry> entries = new(StringComparer.Ordinal);

    public ComputerUseWinExecutionTargetCatalog()
        : this(TimeProvider.System, TimeSpan.FromMinutes(2), maxEntries: 128)
    {
    }

    internal ComputerUseWinExecutionTargetCatalog(TimeProvider timeProvider, TimeSpan entryTtl, int maxEntries)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxEntries, 0);

        this.timeProvider = timeProvider;
        this.entryTtl = entryTtl;
        this.maxEntries = maxEntries;
    }

    public IReadOnlyList<ComputerUseWinExecutionTarget> Materialize(IReadOnlyList<WindowDescriptor> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);

        PendingTarget[] pendingTargets = windows
            .Where(static window => window.IsVisible)
            .Select(static window => TryCreatePendingTarget(window, out PendingTarget? target) ? target : null)
            .Where(static target => target is not null)
            .Cast<PendingTarget>()
            .ToArray();
        return CommitBatch(pendingTargets, protectAsPublishedDiscoverySnapshot: true);
    }

    public bool TryIssue(WindowDescriptor window, out ComputerUseWinExecutionTarget? target)
    {
        ArgumentNullException.ThrowIfNull(window);

        target = null;
        if (TryResolveCurrentPublishedWindow_NoMutation(window, out target))
        {
            return true;
        }

        if (!TryCreatePendingTarget(window, out PendingTarget? pendingTarget) || pendingTarget is null)
        {
            return false;
        }

        target = new ComputerUseWinExecutionTarget(
            pendingTarget.ApprovalKey,
            pendingTarget.ExecutionTargetId,
            PublicWindowId: null,
            pendingTarget.Window);
        return true;
    }

    public bool TryResolveWindowId(
        string windowId,
        IReadOnlyList<WindowDescriptor> liveWindows,
        out ComputerUseWinExecutionTarget? target,
        out WindowDescriptor? failureWindow,
        out bool continuityFailed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(windowId);
        ArgumentNullException.ThrowIfNull(liveWindows);

        target = null;
        failureWindow = null;
        continuityFailed = false;

        CatalogEntry? entry;
        lock (gate)
        {
            EvictExpired_NoLock();
            if (!entries.TryGetValue(windowId, out entry))
            {
                return false;
            }

            if (!IsCurrentPublishedDiscoveryEntry_NoLock(entry))
            {
                return false;
            }
        }

        WindowDescriptor discoveredWindow = entry!.Window;
        WindowDescriptor? liveWindow = null;
        foreach (WindowDescriptor item in liveWindows)
        {
            if (!ComputerUseWinWindowContinuityProof.MatchesDiscoverySelector(item, discoveredWindow))
            {
                continue;
            }

            if (liveWindow is not null)
            {
                failureWindow = liveWindow;
                continuityFailed = true;
                return false;
            }

            liveWindow = item;
        }

        if (liveWindow is null)
        {
            failureWindow = liveWindows.FirstOrDefault(item => item.Hwnd == discoveredWindow.Hwnd);
            continuityFailed = failureWindow is not null;
            return false;
        }

        lock (gate)
        {
            if (!entries.TryGetValue(windowId, out CatalogEntry? currentEntry)
                || currentEntry.Generation != entry.Generation
                || !IsCurrentPublishedDiscoveryEntry_NoLock(currentEntry))
            {
                return false;
            }
        }

        target = new ComputerUseWinExecutionTarget(
            entry.ApprovalKey,
            entry.ExecutionTargetId,
            entry.WindowId,
            liveWindow);
        return true;
    }

    public ComputerUseWinExecutionTarget RevalidatePublicSelectorAfterSideEffect(
        ComputerUseWinExecutionTarget target,
        WindowDescriptor liveWindow)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(liveWindow);

        if (string.IsNullOrWhiteSpace(target.PublicWindowId))
        {
            return target with { Window = liveWindow };
        }

        lock (gate)
        {
            EvictExpired_NoLock();
            if (!entries.TryGetValue(target.PublicWindowId, out CatalogEntry? entry)
                || !IsCurrentPublishedDiscoveryEntry_NoLock(entry)
                || !ComputerUseWinWindowContinuityProof.MatchesDiscoverySelector(liveWindow, entry.Window))
            {
                return target with
                {
                    PublicWindowId = null,
                    Window = liveWindow,
                };
            }

            return new ComputerUseWinExecutionTarget(
                entry.ApprovalKey,
                entry.ExecutionTargetId,
                entry.WindowId,
                liveWindow);
        }
    }

    private static bool TryCreatePendingTarget(WindowDescriptor window, out PendingTarget? target)
    {
        ArgumentNullException.ThrowIfNull(window);

        target = null;
        if (!window.IsVisible)
        {
            return false;
        }

        if (!ComputerUseWinAppIdentity.TryCreateStableAppId(window, out string? appId))
        {
            return false;
        }

        if (!WindowIdentityValidator.TryValidateStableIdentity(window, out _))
        {
            return false;
        }

        target = new PendingTarget(
            new ComputerUseWinApprovalKey(appId!),
            CreateExecutionTargetId(),
            window);
        return true;
    }

    private ComputerUseWinExecutionTarget[] CommitBatch(
        IReadOnlyList<PendingTarget> pendingTargets,
        bool protectAsPublishedDiscoverySnapshot)
    {
        if (pendingTargets.Count == 0)
        {
            if (protectAsPublishedDiscoverySnapshot)
            {
                lock (gate)
                {
                    EvictExpired_NoLock();
                    latestPublishedDiscoveryGeneration = null;
                    EvictOverflowPreservingLifetimes_NoLock(currentGeneration: null);
                }
            }

            return [];
        }

        lock (gate)
        {
            EvictExpired_NoLock();
            long generation = nextGeneration++;
            DateTimeOffset issuedAtUtc = timeProvider.GetUtcNow();
            HashSet<string> ambiguousReusableWindowIds = FindAmbiguousReusableWindowIds_NoLock(
                pendingTargets,
                protectAsPublishedDiscoverySnapshot);
            ComputerUseWinExecutionTarget[] issuedTargets = pendingTargets
                .Select(pending => CreatePublishedTarget_NoLock(
                    pending,
                    protectAsPublishedDiscoverySnapshot,
                    ambiguousReusableWindowIds))
                .ToArray();
            foreach (ComputerUseWinExecutionTarget issuedTarget in issuedTargets)
            {
                string windowId = issuedTarget.PublicWindowId
                    ?? throw new InvalidOperationException("Published discovery targets must carry a public windowId.");
                entries[windowId] = new CatalogEntry(
                    issuedTarget.ApprovalKey,
                    issuedTarget.ExecutionTargetId,
                    windowId,
                    issuedTarget.Window,
                    generation,
                    issuedAtUtc);
            }

            if (protectAsPublishedDiscoverySnapshot)
            {
                latestPublishedDiscoveryGeneration = generation;
            }

            EvictOverflowPreservingLifetimes_NoLock(generation);
            return issuedTargets;
        }
    }

    private ComputerUseWinExecutionTarget CreatePublishedTarget_NoLock(
        PendingTarget pendingTarget,
        bool allowPublishedReuse,
        HashSet<string> ambiguousReusableWindowIds)
    {
        if (allowPublishedReuse && TryFindReusablePublishedEntry_NoLock(pendingTarget.Window, out CatalogEntry? entry))
        {
            if (ambiguousReusableWindowIds.Contains(entry.WindowId))
            {
                return CreateNewPublishedTarget(pendingTarget);
            }

            return new ComputerUseWinExecutionTarget(
                entry.ApprovalKey,
                entry.ExecutionTargetId,
                entry.WindowId,
                pendingTarget.Window);
        }

        return CreateNewPublishedTarget(pendingTarget);
    }

    private static ComputerUseWinExecutionTarget CreateNewPublishedTarget(PendingTarget pendingTarget) =>
        new(
            pendingTarget.ApprovalKey,
            pendingTarget.ExecutionTargetId,
            pendingTarget.ExecutionTargetId.Value,
            pendingTarget.Window);

    private HashSet<string> FindAmbiguousReusableWindowIds_NoLock(
        IReadOnlyList<PendingTarget> pendingTargets,
        bool allowPublishedReuse)
    {
        if (!allowPublishedReuse)
        {
            return [];
        }

        Dictionary<string, int> matchCounts = new(StringComparer.Ordinal);
        foreach (PendingTarget pendingTarget in pendingTargets)
        {
            if (TryFindReusablePublishedEntry_NoLock(pendingTarget.Window, out CatalogEntry? entry))
            {
                matchCounts[entry.WindowId] = matchCounts.GetValueOrDefault(entry.WindowId) + 1;
            }
        }

        return matchCounts
            .Where(static entry => entry.Value > 1)
            .Select(static entry => entry.Key)
            .ToHashSet(StringComparer.Ordinal);
    }

    private bool TryResolveCurrentPublishedWindow_NoMutation(
        WindowDescriptor liveWindow,
        out ComputerUseWinExecutionTarget? target)
    {
        target = null;
        lock (gate)
        {
            EvictExpired_NoLock();
            if (latestPublishedDiscoveryGeneration is not long latestPublished)
            {
                return false;
            }

            CatalogEntry[] matches = entries.Values
                .Where(entry => entry.Generation == latestPublished)
                .Where(entry => ComputerUseWinWindowContinuityProof.MatchesDiscoverySelector(liveWindow, entry.Window))
                .Take(2)
                .ToArray();
            if (matches.Length != 1)
            {
                return false;
            }

            CatalogEntry entry = matches[0];
            target = new ComputerUseWinExecutionTarget(
                entry.ApprovalKey,
                entry.ExecutionTargetId,
                entry.WindowId,
                liveWindow);
            return true;
        }
    }

    private bool TryFindReusablePublishedEntry_NoLock(WindowDescriptor liveWindow, out CatalogEntry entry)
    {
        entry = null!;
        if (latestPublishedDiscoveryGeneration is not long latestPublished)
        {
            return false;
        }

        CatalogEntry[] matches = entries.Values
            .Where(candidate => candidate.Generation == latestPublished)
            .Where(candidate => ComputerUseWinWindowContinuityProof.MatchesDiscoverySelector(liveWindow, candidate.Window))
            .Take(2)
            .ToArray();
        if (matches.Length != 1)
        {
            return false;
        }

        entry = matches[0];
        return true;
    }

    private static ComputerUseWinWindowInstanceIdentity CreateExecutionTargetId() =>
        new($"cw_{Guid.NewGuid():N}");

    private void EvictExpired_NoLock()
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        string[] expiredWindowIds = entries
            .Where(entry => now - entry.Value.IssuedAtUtc > entryTtl)
            .Select(static entry => entry.Key)
            .ToArray();

        foreach (string windowId in expiredWindowIds)
        {
            entries.Remove(windowId);
        }
    }

    private void EvictOverflowPreservingLifetimes_NoLock(long? currentGeneration)
    {
        if (entries.Count <= maxEntries)
        {
            return;
        }

        int protectedCount = entries.Count(entry =>
            (currentGeneration is long generation && entry.Value.Generation == generation)
            || (latestPublishedDiscoveryGeneration is long latestPublished && entry.Value.Generation == latestPublished));
        int allowedCount = Math.Max(maxEntries, protectedCount);
        int removeCount = entries.Count - allowedCount;
        if (removeCount <= 0)
        {
            return;
        }

        string[] windowIdsToRemove = entries
            .Where(entry =>
                (currentGeneration is not long generation || entry.Value.Generation != generation)
                && (latestPublishedDiscoveryGeneration is not long latestPublished || entry.Value.Generation != latestPublished))
            .OrderBy(static entry => entry.Value.Generation)
            .ThenBy(static entry => entry.Value.IssuedAtUtc)
            .ThenBy(static entry => entry.Key, StringComparer.Ordinal)
            .Take(removeCount)
            .Select(static entry => entry.Key)
            .ToArray();

        foreach (string windowId in windowIdsToRemove)
        {
            entries.Remove(windowId);
        }
    }

    private bool IsCurrentPublishedDiscoveryEntry_NoLock(CatalogEntry entry) =>
        latestPublishedDiscoveryGeneration is long latestPublished
        && entry.Generation == latestPublished;

    private sealed record PendingTarget(
        ComputerUseWinApprovalKey ApprovalKey,
        ComputerUseWinWindowInstanceIdentity ExecutionTargetId,
        WindowDescriptor Window);

    private sealed record CatalogEntry(
        ComputerUseWinApprovalKey ApprovalKey,
        ComputerUseWinWindowInstanceIdentity ExecutionTargetId,
        string WindowId,
        WindowDescriptor Window,
        long Generation,
        DateTimeOffset IssuedAtUtc);
}
