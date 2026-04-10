namespace WinBridge.Runtime.Contracts;

internal enum OpenTargetPathCategory
{
    Relative,
    DriveRelative,
    DosLocalAbsolute,
    UncAbsolute,
    DeviceStyle,
    AbsoluteUri,
}

internal static class OpenTargetPathAdmissionPolicy
{
    internal static OpenTargetPathCategory Classify(string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);

        if (IsDeviceStylePath(target))
        {
            return OpenTargetPathCategory.DeviceStyle;
        }

        if (Path.IsPathRooted(target) && !Path.IsPathFullyQualified(target))
        {
            return OpenTargetPathCategory.DriveRelative;
        }

        if (Path.IsPathFullyQualified(target))
        {
            return target.StartsWith(@"\\", StringComparison.Ordinal)
                ? OpenTargetPathCategory.UncAbsolute
                : OpenTargetPathCategory.DosLocalAbsolute;
        }

        if (Uri.TryCreate(target, UriKind.Absolute, out Uri? uri) && uri.IsAbsoluteUri)
        {
            return OpenTargetPathCategory.AbsoluteUri;
        }

        return OpenTargetPathCategory.Relative;
    }

    internal static bool IsSupportedDocumentOrFolderPath(string target, out string? failureCode, out string? reason)
    {
        OpenTargetPathCategory category = Classify(target);
        switch (category)
        {
            case OpenTargetPathCategory.DosLocalAbsolute:
            case OpenTargetPathCategory.UncAbsolute:
                failureCode = null;
                reason = null;
                return true;
            case OpenTargetPathCategory.DriveRelative:
                failureCode = OpenTargetFailureCodeValues.InvalidRequest;
                reason = "Текущий open_target contract принимает только absolute local/UNC path; drive-relative target не поддерживается.";
                return false;
            case OpenTargetPathCategory.DeviceStyle:
                failureCode = OpenTargetFailureCodeValues.InvalidRequest;
                reason = "Текущий open_target contract принимает только absolute local/UNC path и не принимает device-style path forms.";
                return false;
            case OpenTargetPathCategory.AbsoluteUri:
                failureCode = OpenTargetFailureCodeValues.InvalidRequest;
                reason = "Текущий open_target contract принимает absolute local/UNC path для document/folder и не принимает URI в этих targetKind.";
                return false;
            default:
                failureCode = OpenTargetFailureCodeValues.InvalidRequest;
                reason = "Текущий open_target contract принимает только absolute local/UNC path для document/folder; relative target не поддерживается.";
                return false;
        }
    }

    private static bool IsDeviceStylePath(string target) =>
        target.StartsWith(@"\\?\", StringComparison.Ordinal)
        || target.StartsWith(@"\\.\", StringComparison.Ordinal);
}
