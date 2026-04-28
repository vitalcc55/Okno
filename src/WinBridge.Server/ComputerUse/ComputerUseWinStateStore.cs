using WinBridge.Runtime.Contracts;

namespace WinBridge.Server.ComputerUse;

internal sealed class ComputerUseWinStateStore
{
    private readonly TimeProvider timeProvider;
    private readonly TimeSpan stateTtl;
    private readonly int maxEntries;
    private readonly object gate = new();
    private readonly Dictionary<string, ComputerUseWinStoredState> states = new(StringComparer.Ordinal);

    public ComputerUseWinStateStore()
        : this(TimeProvider.System, TimeSpan.FromSeconds(30), maxEntries: 16)
    {
    }

    internal ComputerUseWinStateStore(TimeProvider timeProvider, TimeSpan stateTtl, int maxEntries)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxEntries, 0);

        this.timeProvider = timeProvider;
        this.stateTtl = stateTtl;
        this.maxEntries = maxEntries;
    }

    public string Create(ComputerUseWinStoredState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        string token = CreateToken();
        Commit(token, state);
        return token;
    }

    public static string CreateToken() => Guid.NewGuid().ToString("N");

    public void Commit(string token, ComputerUseWinStoredState state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        ArgumentNullException.ThrowIfNull(state);

        lock (gate)
        {
            EvictExpired_NoLock();
            states[token] = state with
            {
                IssuedAtUtc = timeProvider.GetUtcNow(),
            };
            EvictOverflow_NoLock();
        }
    }

    public bool TryGet(string stateToken, out ComputerUseWinStoredState? state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateToken);

        lock (gate)
        {
            EvictExpired_NoLock();
            if (states.TryGetValue(stateToken, out ComputerUseWinStoredState? existing))
            {
                state = existing;
                return true;
            }
        }

        state = null;
        return false;
    }

    private void EvictExpired_NoLock()
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        string[] expiredTokens = states
            .Where(entry => now - entry.Value.IssuedAtUtc > stateTtl)
            .Select(static entry => entry.Key)
            .ToArray();

        foreach (string token in expiredTokens)
        {
            states.Remove(token);
        }
    }

    private void EvictOverflow_NoLock()
    {
        if (states.Count <= maxEntries)
        {
            return;
        }

        string[] tokensToRemove = states
            .OrderBy(static entry => entry.Value.IssuedAtUtc)
            .ThenBy(static entry => entry.Key, StringComparer.Ordinal)
            .Take(states.Count - maxEntries)
            .Select(static entry => entry.Key)
            .ToArray();

        foreach (string token in tokensToRemove)
        {
            states.Remove(token);
        }
    }
}

internal sealed record ComputerUseWinStoredState(
    ComputerUseWinAppSession Session,
    WindowDescriptor Window,
    InputCaptureReference? CaptureReference,
    IReadOnlyDictionary<int, ComputerUseWinStoredElement> Elements,
    ComputerUseWinObservationEnvelope Observation,
    DateTimeOffset CapturedAtUtc,
    DateTimeOffset IssuedAtUtc = default);

internal sealed record ComputerUseWinObservationEnvelope(
    int RequestedDepth,
    int RequestedMaxNodes);

internal sealed record ComputerUseWinStoredElement(
    int Index,
    string ElementId,
    string? Name,
    string? AutomationId,
    string ControlType,
    Bounds? Bounds,
    bool HasKeyboardFocus,
    IReadOnlyList<string> Actions,
    IReadOnlyList<string>? Patterns = null);
