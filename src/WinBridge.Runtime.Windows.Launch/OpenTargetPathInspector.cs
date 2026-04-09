using System.Runtime.InteropServices;

namespace WinBridge.Runtime.Windows.Launch;

internal interface IOpenTargetPathInspector
{
    OpenTargetResolvedPathKind Inspect(string target);
}

internal enum OpenTargetResolvedPathKind
{
    Unresolved,
    ExistingFile,
    ExistingDirectory,
}

internal sealed class FileSystemOpenTargetPathInspector : IOpenTargetPathInspector
{
    internal const uint DriveFixed = 3;
    internal const uint DriveRemote = 4;
    internal const uint DriveRamDisk = 6;

    private readonly Func<string, uint> _getDriveType;
    private readonly Func<string, FileAttributes> _getAttributes;

    public FileSystemOpenTargetPathInspector()
        : this(GetDriveType, File.GetAttributes)
    {
    }

    internal FileSystemOpenTargetPathInspector(
        Func<string, uint> getDriveType,
        Func<string, FileAttributes> getAttributes)
    {
        _getDriveType = getDriveType;
        _getAttributes = getAttributes;
    }

    public OpenTargetResolvedPathKind Inspect(string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);

        if (!ShouldInspectLocally(target))
        {
            return OpenTargetResolvedPathKind.Unresolved;
        }

        try
        {
            FileAttributes attributes = _getAttributes(target);
            return attributes.HasFlag(FileAttributes.Directory)
                ? OpenTargetResolvedPathKind.ExistingDirectory
                : OpenTargetResolvedPathKind.ExistingFile;
        }
        catch (Exception exception) when (exception is FileNotFoundException
                                          or DirectoryNotFoundException
                                          or UnauthorizedAccessException
                                          or IOException
                                          or NotSupportedException
                                          or ArgumentException)
        {
            return OpenTargetResolvedPathKind.Unresolved;
        }
    }

    private bool ShouldInspectLocally(string target)
    {
        // Live path refinement must stay fast and local-only. UNC, device-style and remote-drive
        // targets degrade to Unresolved so cancellation/latency semantics do not depend on a network probe.
        if (target.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return false;
        }

        if (!Path.IsPathFullyQualified(target))
        {
            return false;
        }

        string? root = Path.GetPathRoot(target);
        if (string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        uint driveType = _getDriveType(root);
        return driveType is DriveFixed or DriveRamDisk;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetDriveType(string lpRootPathName);
}
