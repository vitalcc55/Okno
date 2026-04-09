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
}
