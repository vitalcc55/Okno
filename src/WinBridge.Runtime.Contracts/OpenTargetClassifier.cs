namespace WinBridge.Runtime.Contracts;

internal static class OpenTargetClassifier
{
    private static readonly HashSet<string> LauncherLikeDocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".application",
        ".appref-ms",
        ".bat",
        ".cpl",
        ".cmd",
        ".com",
        ".exe",
        ".hta",
        ".js",
        ".jse",
        ".lnk",
        ".msc",
        ".msi",
        ".ps1",
        ".psd1",
        ".psm1",
        ".reg",
        ".scr",
        ".url",
        ".vb",
        ".vbe",
        ".vbs",
        ".ws",
        ".wsf",
        ".wsh",
    };

    internal static bool TryClassify(
        OpenTargetRequest request,
        out OpenTargetClassification classification,
        out string? failureCode,
        out string? reason)
    {
        ArgumentNullException.ThrowIfNull(request);

        string targetKind = request.TargetKind;
        string target = request.Target;

        if (string.Equals(targetKind, OpenTargetKindValues.Document, StringComparison.Ordinal))
        {
            return TryClassifyDocument(target, out classification, out failureCode, out reason);
        }

        if (string.Equals(targetKind, OpenTargetKindValues.Folder, StringComparison.Ordinal))
        {
            return TryClassifyFolder(target, out classification, out failureCode, out reason);
        }

        if (string.Equals(targetKind, OpenTargetKindValues.Url, StringComparison.Ordinal))
        {
            return TryClassifyUrl(target, out classification, out failureCode, out reason);
        }

        classification = default;
        failureCode = OpenTargetFailureCodeValues.UnsupportedTargetKind;
        reason = $"Параметр targetKind для open_target должен быть одним из literal-set: {OpenTargetKindValues.Document}, {OpenTargetKindValues.Folder}, {OpenTargetKindValues.Url}.";
        return false;
    }

    private static bool TryClassifyDocument(
        string target,
        out OpenTargetClassification classification,
        out string? failureCode,
        out string? reason)
    {
        if (!TryClassifyPathTarget(target, out OpenTargetPathClassification pathClassification, out failureCode, out reason))
        {
            classification = default;
            return false;
        }

        if (pathClassification.IsLauncherLikeDocumentTarget)
        {
            classification = default;
            failureCode = OpenTargetFailureCodeValues.UnsupportedTargetKind;
            reason = "V1 open_target с targetKind=document не принимает executable, script или launcher-like targets.";
            return false;
        }

        classification = pathClassification.ToClassification(OpenTargetKindValues.Document);
        failureCode = null;
        reason = null;
        return true;
    }

    private static bool TryClassifyFolder(
        string target,
        out OpenTargetClassification classification,
        out string? failureCode,
        out string? reason)
    {
        if (!TryClassifyPathTarget(target, out OpenTargetPathClassification pathClassification, out failureCode, out reason))
        {
            classification = default;
            return false;
        }

        classification = pathClassification.ToClassification(OpenTargetKindValues.Folder);
        failureCode = null;
        reason = null;
        return true;
    }

    private static bool TryClassifyUrl(
        string target,
        out OpenTargetClassification classification,
        out string? failureCode,
        out string? reason)
    {
        if (!Uri.TryCreate(target, UriKind.Absolute, out Uri? uri) || !uri.IsAbsoluteUri)
        {
            classification = default;
            failureCode = OpenTargetFailureCodeValues.InvalidRequest;
            reason = "Параметр target для open_target с targetKind=url должен быть absolute http/https URL.";
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            classification = default;
            failureCode = OpenTargetFailureCodeValues.UnsupportedUriScheme;
            reason = "V1 open_target поддерживает только http/https URL и не принимает mailto/file/custom schemes.";
            return false;
        }

        classification = new(
            TargetKind: OpenTargetKindValues.Url,
            TargetIdentity: null,
            UriScheme: uri.Scheme.ToLowerInvariant());
        failureCode = null;
        reason = null;
        return true;
    }

    private static bool TryValidateAbsolutePathTarget(
        string target,
        out string? failureCode,
        out string? reason)
    {
        if (Path.IsPathRooted(target) && !Path.IsPathFullyQualified(target))
        {
            failureCode = OpenTargetFailureCodeValues.InvalidRequest;
            reason = "V1 open_target принимает только absolute path; drive-relative target не поддерживается.";
            return false;
        }

        if (Path.IsPathFullyQualified(target))
        {
            failureCode = null;
            reason = null;
            return true;
        }

        if (IsAbsoluteUri(target))
        {
            failureCode = OpenTargetFailureCodeValues.InvalidRequest;
            reason = "V1 open_target принимает absolute path для document/folder и не принимает URI в этих targetKind.";
            return false;
        }

        failureCode = OpenTargetFailureCodeValues.InvalidRequest;
        reason = "V1 open_target принимает только absolute path для document/folder; relative target не поддерживается.";
        return false;
    }

    private static bool TryClassifyPathTarget(
        string target,
        out OpenTargetPathClassification pathClassification,
        out string? failureCode,
        out string? reason)
    {
        if (!TryValidateAbsolutePathTarget(target, out failureCode, out reason))
        {
            pathClassification = default;
            return false;
        }

        pathClassification = new OpenTargetPathClassification(
            TargetIdentity: TryResolveSafePathIdentity(target),
            IsLauncherLikeDocumentTarget: IsLauncherLikeDocumentTarget(target));
        failureCode = null;
        reason = null;
        return true;
    }

    private static bool IsAbsoluteUri(string value) =>
        !Path.IsPathFullyQualified(value)
        && Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
        && uri.IsAbsoluteUri;

    private static bool IsLauncherLikeDocumentTarget(string target)
    {
        string extension = Path.GetExtension(target);
        return !string.IsNullOrWhiteSpace(extension) && LauncherLikeDocumentExtensions.Contains(extension);
    }

    private static string? TryResolveSafePathIdentity(string path)
    {
        string normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        string trimmed = normalized.TrimEnd(Path.DirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return null;
        }

        string fileName = Path.GetFileName(trimmed);
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            return fileName;
        }

        string[] segments = trimmed
            .Split([Path.DirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Length == 0 ? null : segments[^1];
    }
}

internal readonly record struct OpenTargetPathClassification(
    string? TargetIdentity,
    bool IsLauncherLikeDocumentTarget)
{
    internal OpenTargetClassification ToClassification(string targetKind) =>
        new(
            TargetKind: targetKind,
            TargetIdentity: TargetIdentity,
            UriScheme: null);
}

internal readonly record struct OpenTargetClassification(
    string TargetKind,
    string? TargetIdentity,
    string? UriScheme)
{
    internal OpenTargetPreview ToPreview() =>
        new(
            TargetKind: TargetKind,
            TargetIdentity: TargetIdentity,
            UriScheme: UriScheme);
}
