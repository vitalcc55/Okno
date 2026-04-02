namespace WinBridge.Runtime.Contracts;

internal enum LaunchProcessExecutableTargetKind
{
    BareName,
    AbsolutePath,
    RelativePath,
    DriveRelativePath,
    AbsoluteUri,
    Directory,
    UnsupportedFileType,
}

internal static class LaunchProcessExecutableTarget
{
    private static readonly HashSet<string> DirectExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".com",
        ".exe",
    };

    internal static LaunchProcessExecutableTargetKind Classify(string executable)
    {
        if (IsAbsoluteUri(executable))
        {
            return LaunchProcessExecutableTargetKind.AbsoluteUri;
        }

        if (Path.IsPathRooted(executable) && !Path.IsPathFullyQualified(executable))
        {
            return LaunchProcessExecutableTargetKind.DriveRelativePath;
        }

        if (Path.IsPathFullyQualified(executable) && Path.EndsInDirectorySeparator(executable))
        {
            return LaunchProcessExecutableTargetKind.Directory;
        }

        if (HasDirectorySeparators(executable) && !Path.IsPathFullyQualified(executable))
        {
            return LaunchProcessExecutableTargetKind.RelativePath;
        }

        if (Path.IsPathFullyQualified(executable))
        {
            return HasSupportedDirectExecutableExtension(executable)
                ? LaunchProcessExecutableTargetKind.AbsolutePath
                : LaunchProcessExecutableTargetKind.UnsupportedFileType;
        }

        if (HasUnsupportedBareExecutableExtension(executable))
        {
            return LaunchProcessExecutableTargetKind.UnsupportedFileType;
        }

        return LaunchProcessExecutableTargetKind.BareName;
    }

    internal static string? TryResolveSafeExecutableIdentity(string executable)
    {
        if (string.IsNullOrWhiteSpace(executable))
        {
            return null;
        }

        return Classify(executable) == LaunchProcessExecutableTargetKind.AbsoluteUri
            ? TryResolveUriExecutableIdentity(executable)
            : TryResolvePathExecutableIdentity(executable);
    }

    private static string? TryResolveUriExecutableIdentity(string executable)
    {
        if (!Uri.TryCreate(executable, UriKind.Absolute, out Uri? uri) || !uri.IsAbsoluteUri)
        {
            return null;
        }

        string absolutePath = uri.AbsolutePath;
        return string.IsNullOrWhiteSpace(absolutePath)
            ? null
            : TryResolvePathExecutableIdentity(absolutePath);
    }

    private static string? TryResolvePathExecutableIdentity(string executable)
    {
        string normalized = executable.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        string trimmed = normalized.TrimEnd(Path.DirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return null;
        }

        string executableName = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(executableName) ? null : executableName;
    }

    private static bool HasDirectorySeparators(string value) =>
        value.Contains(Path.DirectorySeparatorChar) || value.Contains(Path.AltDirectorySeparatorChar);

    private static bool IsAbsoluteUri(string value) =>
        !Path.IsPathFullyQualified(value)
        && Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
        && uri.IsAbsoluteUri;

    private static bool HasSupportedDirectExecutableExtension(string executable)
    {
        string extension = Path.GetExtension(executable);
        return !string.IsNullOrWhiteSpace(extension) && DirectExecutableExtensions.Contains(extension);
    }

    private static bool HasUnsupportedBareExecutableExtension(string executable)
    {
        string extension = Path.GetExtension(executable);
        return !string.IsNullOrWhiteSpace(extension) && !DirectExecutableExtensions.Contains(extension);
    }
}
