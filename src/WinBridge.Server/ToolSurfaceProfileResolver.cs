using WinBridge.Runtime.Tooling;

namespace WinBridge.Server;

internal static class ToolSurfaceProfileResolver
{
    public static string Resolve(string[] args)
    {
        ToolSurfaceProfileSelection selection = ReadSelection(args);
        if (!selection.IsExplicit)
        {
            return ToolSurfaceProfileValues.WindowsEngine;
        }

        if (string.IsNullOrWhiteSpace(selection.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(args), selection.Value, "Явно переданный tool surface profile не может быть пустым.");
        }

        return ToolContractManifest.GetProfile(selection.Value).Name;
    }

    private static ToolSurfaceProfileSelection ReadSelection(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--tool-surface-profile", StringComparison.Ordinal))
            {
                return new(true, ReadValue(args, ref i, "--tool-surface-profile"));
            }
        }

        return new(false, null);
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

internal readonly record struct ToolSurfaceProfileSelection(bool IsExplicit, string? Value);
