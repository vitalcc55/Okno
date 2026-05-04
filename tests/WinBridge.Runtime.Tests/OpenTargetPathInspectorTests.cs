// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Windows.Launch;

namespace WinBridge.Runtime.Tests;

public sealed class OpenTargetPathInspectorTests
{
    [Fact]
    public void InspectReturnsUnresolvedWithoutTouchingAttributesForUncPath()
    {
        int attributesCalls = 0;
        FileSystemOpenTargetPathInspector inspector = new(
            getDriveType: _ => throw new InvalidOperationException("drive type should not be queried for UNC"),
            getAttributes: _ =>
            {
                attributesCalls++;
                throw new InvalidOperationException("attributes should not be queried for UNC");
            });

        OpenTargetResolvedPathKind result = inspector.Inspect(@"\\server\share\folder");

        Assert.Equal(OpenTargetResolvedPathKind.Unresolved, result);
        Assert.Equal(0, attributesCalls);
    }

    [Fact]
    public void InspectReturnsUnresolvedWithoutTouchingAttributesForNetworkDrive()
    {
        int attributesCalls = 0;
        FileSystemOpenTargetPathInspector inspector = new(
            getDriveType: _ => FileSystemOpenTargetPathInspector.DriveRemote,
            getAttributes: _ =>
            {
                attributesCalls++;
                throw new InvalidOperationException("attributes should not be queried for network drive");
            });

        OpenTargetResolvedPathKind result = inspector.Inspect(@"Z:\Shared\folder");

        Assert.Equal(OpenTargetResolvedPathKind.Unresolved, result);
        Assert.Equal(0, attributesCalls);
    }

    [Fact]
    public void InspectReturnsExistingDirectoryForLocalFixedDrive()
    {
        FileSystemOpenTargetPathInspector inspector = new(
            getDriveType: _ => FileSystemOpenTargetPathInspector.DriveFixed,
            getAttributes: _ => FileAttributes.Directory);

        OpenTargetResolvedPathKind result = inspector.Inspect(@"C:\Docs");

        Assert.Equal(OpenTargetResolvedPathKind.ExistingDirectory, result);
    }

    [Fact]
    public void InspectReturnsExistingFileForLocalFixedDrive()
    {
        FileSystemOpenTargetPathInspector inspector = new(
            getDriveType: _ => FileSystemOpenTargetPathInspector.DriveFixed,
            getAttributes: _ => FileAttributes.Normal);

        OpenTargetResolvedPathKind result = inspector.Inspect(@"C:\Docs\report.pdf");

        Assert.Equal(OpenTargetResolvedPathKind.ExistingFile, result);
    }

    [Fact]
    public void InspectReturnsExistingFileForLocalRemovableDrive()
    {
        FileSystemOpenTargetPathInspector inspector = new(
            getDriveType: _ => FileSystemOpenTargetPathInspector.DriveRemovable,
            getAttributes: _ => FileAttributes.Normal);

        OpenTargetResolvedPathKind result = inspector.Inspect(@"E:\Docs\report.pdf");

        Assert.Equal(OpenTargetResolvedPathKind.ExistingFile, result);
    }

    [Fact]
    public void InspectReturnsExistingDirectoryForLocalCdRom()
    {
        FileSystemOpenTargetPathInspector inspector = new(
            getDriveType: _ => FileSystemOpenTargetPathInspector.DriveCdRom,
            getAttributes: _ => FileAttributes.Directory);

        OpenTargetResolvedPathKind result = inspector.Inspect(@"D:\Docs");

        Assert.Equal(OpenTargetResolvedPathKind.ExistingDirectory, result);
    }
}
