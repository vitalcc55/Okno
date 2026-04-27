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
    ComputerUseWinWindowInstanceIdentity WindowId,
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
        if (!TryCreatePendingTarget(window, out PendingTarget? pendingTarget) || pendingTarget is null)
        {
            return false;
        }

        target = CommitBatch([pendingTarget], protectAsPublishedDiscoverySnapshot: false)[0];
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
        WindowDescriptor? liveWindow = liveWindows.SingleOrDefault(item =>
            ComputerUseWinWindowContinuityProof.MatchesDiscoverySelector(item, discoveredWindow));
        if (liveWindow is null)
        {
            failureWindow = liveWindows.SingleOrDefault(item => item.Hwnd == discoveredWindow.Hwnd);
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

        target = new ComputerUseWinExecutionTarget(entry.ApprovalKey, entry.WindowId, liveWindow);
        return true;
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
            CreateWindowId(),
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

        ComputerUseWinExecutionTarget[] issuedTargets = pendingTargets
            .Select(static pending => new ComputerUseWinExecutionTarget(
                pending.ApprovalKey,
                pending.WindowId,
                pending.Window))
            .ToArray();
        lock (gate)
        {
            EvictExpired_NoLock();
            long generation = nextGeneration++;
            DateTimeOffset issuedAtUtc = timeProvider.GetUtcNow();
            foreach (ComputerUseWinExecutionTarget issuedTarget in issuedTargets)
            {
                entries[issuedTarget.WindowId.Value] = new CatalogEntry(
                    issuedTarget.ApprovalKey,
                    issuedTarget.WindowId,
                    issuedTarget.Window,
                    generation,
                    issuedAtUtc);
            }

            if (protectAsPublishedDiscoverySnapshot)
            {
                latestPublishedDiscoveryGeneration = generation;
            }

            EvictOverflowPreservingLifetimes_NoLock(generation);
        }

        return issuedTargets;
    }

    private static ComputerUseWinWindowInstanceIdentity CreateWindowId() =>
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
        ComputerUseWinWindowInstanceIdentity WindowId,
        WindowDescriptor Window);

    private sealed record CatalogEntry(
        ComputerUseWinApprovalKey ApprovalKey,
        ComputerUseWinWindowInstanceIdentity WindowId,
        WindowDescriptor Window,
        long Generation,
        DateTimeOffset IssuedAtUtc);
}
