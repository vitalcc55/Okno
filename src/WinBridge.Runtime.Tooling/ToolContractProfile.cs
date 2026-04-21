namespace WinBridge.Runtime.Tooling;

public sealed record ToolContractProfile(
    string Name,
    IReadOnlyList<ToolDescriptor> Implemented,
    IReadOnlyList<ToolDescriptor> Deferred,
    IReadOnlyList<string> ImplementedNames,
    IReadOnlyList<string> SmokeRequiredToolNames,
    IReadOnlyDictionary<string, string> DeferredPhaseMap,
    string Notes);

public static class ToolSurfaceProfileValues
{
    public const string WindowsEngine = "windows-engine";
    public const string ComputerUseWin = "computer-use-win";

    public static IReadOnlySet<string> All { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            WindowsEngine,
            ComputerUseWin,
        };
}
