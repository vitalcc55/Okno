using Microsoft.UI.Xaml;
using System.Reflection;

namespace WinBridge.Setup.App;

public static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        string windowsAppRuntimeBaseDirectory = ResolveWindowsAppRuntimeBaseDirectory();
        EnsureUnpackagedRuntimeAliases(windowsAppRuntimeBaseDirectory);
        Environment.SetEnvironmentVariable(
            "MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY",
            windowsAppRuntimeBaseDirectory);

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(init =>
        {
            Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext context = new(
                Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            App app = new();
            _ = app;
        });
    }

    private static string ResolveWindowsAppRuntimeBaseDirectory()
    {
        try
        {
            foreach (string candidate in EnumerateRuntimeDirectoryCandidates())
            {
                if (IsWindowsAppRuntimeBaseDirectory(candidate))
                {
                    return candidate;
                }
            }
        }
        catch
        {
            // Fall back to the executable directory when probing metadata is incomplete.
        }

        return AppContext.BaseDirectory;
    }

    private static bool IsWindowsAppRuntimeBaseDirectory(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        try
        {
            if (!Directory.Exists(candidate))
            {
                return false;
            }

            return File.Exists(Path.Combine(candidate, "Microsoft.WindowsAppRuntime.dll"))
                || File.Exists(Path.Combine(candidate, "Microsoft.UI.Xaml.dll"))
                || File.Exists(Path.Combine(candidate, "WinBridge.Setup.App.pri"));
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerable<string> EnumerateRuntimeDirectoryCandidates()
    {
        yield return AppContext.BaseDirectory;

        string assemblyLocation = Assembly.GetExecutingAssembly().Location;
        if (!string.IsNullOrWhiteSpace(assemblyLocation))
        {
            string? assemblyDirectory = Path.GetDirectoryName(assemblyLocation);
            if (!string.IsNullOrWhiteSpace(assemblyDirectory))
            {
                yield return assemblyDirectory;
            }
        }

        if (AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") is string nativeSearchDirectories
            && !string.IsNullOrWhiteSpace(nativeSearchDirectories))
        {
            foreach (string entry in nativeSearchDirectories.Split(
                         Path.PathSeparator,
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                yield return entry;
            }
        }

        if (AppContext.GetData("APP_CONTEXT_DEPS_FILES") is string depsFiles
            && !string.IsNullOrWhiteSpace(depsFiles))
        {
            foreach (string entry in depsFiles.Split(
                         Path.PathSeparator,
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string? directory = Path.GetDirectoryName(entry);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    yield return directory;
                }
            }
        }
    }

    private static void EnsureUnpackagedRuntimeAliases(string runtimeBaseDirectory)
    {
        try
        {
            string? processPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(processPath))
            {
                return;
            }

            string? processFileName = Path.GetFileName(processPath);
            string? processBaseName = Path.GetFileNameWithoutExtension(processPath);
            if (string.IsNullOrWhiteSpace(processFileName) || string.IsNullOrWhiteSpace(processBaseName))
            {
                return;
            }

            HashSet<string> targetDirectories = new(StringComparer.OrdinalIgnoreCase)
            {
                runtimeBaseDirectory,
                AppContext.BaseDirectory,
            };

            foreach (string targetDirectory in targetDirectories)
            {
                if (!Directory.Exists(targetDirectory))
                {
                    continue;
                }

                EnsureAlias(targetDirectory, "app.manifest", $"{processFileName}.manifest");
                EnsureAlias(targetDirectory, "WinBridge.Setup.App.pri", $"{processBaseName}.pri");
                EnsureAlias(targetDirectory, "WinBridge.Setup.App.pri", "resources.pri");
            }
        }
        catch
        {
            // Rename-friendly aliases are best-effort startup compatibility helpers.
        }
    }

    private static void EnsureAlias(string directory, string sourceFileName, string aliasFileName)
    {
        try
        {
            string sourcePath = Path.Combine(directory, sourceFileName);
            if (!File.Exists(sourcePath))
            {
                return;
            }

            string aliasPath = Path.Combine(directory, aliasFileName);
            if (File.Exists(aliasPath))
            {
                return;
            }

            File.Copy(sourcePath, aliasPath);
        }
        catch
        {
            // Missing write access to the app directory should not block startup.
        }
    }
}
