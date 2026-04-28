namespace WinBridge.Server.ComputerUse;

internal static class ComputerUseWinFreshElementResolver
{
    public static bool TryResolve(
        IReadOnlyDictionary<int, ComputerUseWinStoredElement> freshElements,
        ComputerUseWinStoredElement storedElement,
        out ComputerUseWinStoredElement? effectiveElement)
    {
        ArgumentNullException.ThrowIfNull(freshElements);
        ArgumentNullException.ThrowIfNull(storedElement);

        effectiveElement = freshElements.Values.FirstOrDefault(item =>
            string.Equals(item.ElementId, storedElement.ElementId, StringComparison.Ordinal));
        if (effectiveElement is not null)
        {
            return true;
        }

        if (!ComputerUseWinActionability.HasSemanticFallbackSignal(storedElement))
        {
            effectiveElement = null;
            return false;
        }

        ComputerUseWinStoredElement[] fallbackMatches = freshElements.Values
            .Where(item =>
                string.Equals(item.ControlType, storedElement.ControlType, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.Name, storedElement.Name, StringComparison.Ordinal)
                && string.Equals(item.AutomationId, storedElement.AutomationId, StringComparison.Ordinal))
            .ToArray();

        if (fallbackMatches.Length == 1)
        {
            effectiveElement = fallbackMatches[0];
            return true;
        }

        effectiveElement = null;
        return false;
    }
}
