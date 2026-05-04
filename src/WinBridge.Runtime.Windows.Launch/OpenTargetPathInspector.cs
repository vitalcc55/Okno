// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

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
    internal const uint DriveRemovable = 2;
    internal const uint DriveFixed = 3;
    internal const uint DriveRemote = 4;
    internal const uint DriveCdRom = 5;
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
        // Live path refinement must stay fast and local-only. UNC and network-backed roots degrade
        // to Unresolved so cancellation/latency semantics do not depend on a network probe.
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
        return IsLocalSafeDriveType(driveType);
    }

    private static bool IsLocalSafeDriveType(uint driveType) =>
        driveType is DriveRemovable or DriveFixed or DriveCdRom or DriveRamDisk;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetDriveType(string lpRootPathName);
}
