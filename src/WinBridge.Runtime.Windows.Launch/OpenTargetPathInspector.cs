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
    public OpenTargetResolvedPathKind Inspect(string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);

        try
        {
            FileAttributes attributes = File.GetAttributes(target);
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
}
