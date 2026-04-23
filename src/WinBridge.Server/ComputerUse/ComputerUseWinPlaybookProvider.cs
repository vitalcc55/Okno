namespace WinBridge.Server.ComputerUse;

internal sealed class ComputerUseWinPlaybookProvider : IComputerUseWinInstructionProvider
{
    private static readonly Dictionary<string, string> PlaybookMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ComputerUseWinAppIdentity.NormalizeProcessIdentity("notepad.exe")!] = "Notepad.md",
            [ComputerUseWinAppIdentity.NormalizeProcessIdentity("explorer.exe")!] = "FileExplorer.md",
            [ComputerUseWinAppIdentity.NormalizeProcessIdentity("systemsettings.exe")!] = "Settings.md",
            [ComputerUseWinAppIdentity.NormalizeProcessIdentity("msedge.exe")!] = "Edge.md",
            [ComputerUseWinAppIdentity.NormalizeProcessIdentity("code.exe")!] = "VSCode.md",
        };

    private readonly string appInstructionsRoot;

    public ComputerUseWinPlaybookProvider(ComputerUseWinOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        appInstructionsRoot = options.AppInstructionsRoot;
    }

    public IReadOnlyList<string> GetInstructions(string? processName)
    {
        string? canonicalProcessName = ComputerUseWinAppIdentity.NormalizeProcessIdentity(processName);
        if (string.IsNullOrWhiteSpace(canonicalProcessName)
            || !PlaybookMap.TryGetValue(canonicalProcessName, out string? playbookFileName))
        {
            return Array.Empty<string>();
        }

        string path = Path.Combine(appInstructionsRoot, playbookFileName);
        if (!File.Exists(path))
        {
            return Array.Empty<string>();
        }

        return File.ReadAllLines(path)
            .Select(static line => line.Trim())
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
    }
}
