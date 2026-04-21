using WinBridge.Runtime.Tooling;

namespace WinBridge.Server;

internal static class ToolSurfaceProfileResolver
{
    public static string Resolve(string[] args)
    {
        string? explicitProfile = TryReadExplicitProfile(args);
        if (string.IsNullOrWhiteSpace(explicitProfile))
        {
            return ToolSurfaceProfileValues.WindowsEngine;
        }

        return ToolContractManifest.GetProfile(explicitProfile).Name;
    }

    private static string? TryReadExplicitProfile(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--tool-surface-profile", StringComparison.Ordinal))
            {
                return ReadValue(args, ref i, "--tool-surface-profile");
            }
        }

        return null;
    }

    private static string ReadValue(string[] args, ref int index, string optionName)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"Для аргумента '{optionName}' требуется значение.");
        }

        index++;
        return args[index];
    }
}
