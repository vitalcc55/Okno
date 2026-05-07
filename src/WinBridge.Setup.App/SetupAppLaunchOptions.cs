namespace WinBridge.Setup.App;

public enum SetupAppShellOperation
{
    RemoveAll,
}

public sealed record SetupAppLaunchOptions(
    SetupAppShellOperation? Operation,
    bool Quiet)
{
    public static SetupAppLaunchOptions Parse(string[] args)
    {
        SetupAppShellOperation? operation = null;
        bool quiet = false;

        for (int index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--operation":
                    if (index + 1 >= args.Length)
                    {
                        throw new InvalidOperationException("The '--operation' option requires a value.");
                    }

                    operation = args[++index] switch
                    {
                        "remove-all" => SetupAppShellOperation.RemoveAll,
                        _ => throw new InvalidOperationException($"Unsupported shell operation '{args[index]}'."),
                    };
                    break;
                case "--quiet":
                    quiet = true;
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported setup shell argument '{args[index]}'.");
            }
        }

        return new SetupAppLaunchOptions(operation, quiet);
    }
}
