namespace WinBridge.Server.ComputerUse;

internal sealed record ComputerUseWinOptions(
    string PluginRoot,
    string AppInstructionsRoot,
    string ApprovalStorePath)
{
    private const string PluginRootEnvironmentVariable = "COMPUTER_USE_WIN_PLUGIN_ROOT";
    private const string ApprovalStoreEnvironmentVariable = "COMPUTER_USE_WIN_APPROVAL_STORE";

    public static ComputerUseWinOptions Resolve(string contentRootPath)
    {
        string pluginRoot = ResolvePluginRoot(contentRootPath);
        string appInstructionsRoot = Path.Combine(pluginRoot, "references", "AppInstructions");
        string approvalStorePath = ResolveApprovalStorePath();
        return new(pluginRoot, appInstructionsRoot, approvalStorePath);
    }

    private static string ResolvePluginRoot(string contentRootPath)
    {
        string? explicitPluginRoot = Environment.GetEnvironmentVariable(PluginRootEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(explicitPluginRoot))
        {
            return Path.GetFullPath(explicitPluginRoot);
        }

        return Path.GetFullPath(Path.Combine(contentRootPath, "plugins", "computer-use-win"));
    }

    private static string ResolveApprovalStorePath()
    {
        string? explicitStorePath = Environment.GetEnvironmentVariable(ApprovalStoreEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(explicitStorePath))
        {
            return Path.GetFullPath(explicitStorePath);
        }

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "Okno", "ComputerUseWin", "AppApprovals.json");
    }
}
