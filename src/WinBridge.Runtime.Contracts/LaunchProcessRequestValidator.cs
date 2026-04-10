using System.Text.Json;

namespace WinBridge.Runtime.Contracts;

public static class LaunchProcessRequestValidator
{
    public static bool TryValidate(
        LaunchProcessRequest request,
        out string? failureCode,
        out string? reason)
    {
        ArgumentNullException.ThrowIfNull(request);

        string executable = request.Executable;
        if (string.IsNullOrWhiteSpace(executable))
        {
            failureCode = LaunchProcessFailureCodeValues.InvalidRequest;
            reason = "Параметр executable для launch_process не должен быть пустым.";
            return false;
        }

        if (TryValidateAdditionalProperties(request.AdditionalProperties, out failureCode, out reason) is false)
        {
            return false;
        }

        if (request.Args.Any(static item => item is null))
        {
            failureCode = LaunchProcessFailureCodeValues.InvalidRequest;
            reason = "Параметр args для launch_process не должен содержать null-элементы.";
            return false;
        }

        switch (LaunchProcessExecutableTarget.Classify(executable))
        {
            case LaunchProcessExecutableTargetKind.AbsoluteUri:
                failureCode = LaunchProcessFailureCodeValues.UnsupportedTargetKind;
                reason = "Текущий launch_process contract не принимает URI target; передай executable path или bare executable name.";
                return false;
            case LaunchProcessExecutableTargetKind.RelativePath:
            case LaunchProcessExecutableTargetKind.DriveRelativePath:
                failureCode = LaunchProcessFailureCodeValues.InvalidRequest;
                reason = "Текущий launch_process contract принимает только absolute path или bare executable name; relative executable path не поддерживается.";
                return false;
            case LaunchProcessExecutableTargetKind.Directory:
                failureCode = LaunchProcessFailureCodeValues.UnsupportedTargetKind;
                reason = "Текущий launch_process contract не принимает directory target в поле executable.";
                return false;
            case LaunchProcessExecutableTargetKind.UnsupportedFileType:
                failureCode = LaunchProcessFailureCodeValues.UnsupportedTargetKind;
                reason = "Текущий launch_process contract принимает только direct executable target; document/shell-open file types должны идти через отдельный launch_process/open_target split.";
                return false;
        }

        if (request.WorkingDirectory is not null)
        {
            if (string.IsNullOrWhiteSpace(request.WorkingDirectory)
                || TryClassifyWorkingDirectoryAsUri(request.WorkingDirectory)
                || !Path.IsPathFullyQualified(request.WorkingDirectory))
            {
                failureCode = LaunchProcessFailureCodeValues.InvalidRequest;
                reason = "Параметр workingDirectory для launch_process должен быть absolute path.";
                return false;
            }
        }

        if (!request.WaitForWindow && request.TimeoutMs is not null)
        {
            failureCode = LaunchProcessFailureCodeValues.InvalidRequest;
            reason = "Параметр timeoutMs допустим только вместе с waitForWindow=true.";
            return false;
        }

        if (request.WaitForWindow && request.TimeoutMs is <= 0)
        {
            failureCode = LaunchProcessFailureCodeValues.InvalidRequest;
            reason = "Параметр timeoutMs для launch_process должен быть > 0.";
            return false;
        }

        failureCode = null;
        reason = null;
        return true;
    }

    private static bool TryValidateAdditionalProperties(
        IDictionary<string, JsonElement>? additionalProperties,
        out string? failureCode,
        out string? reason)
    {
        if (additionalProperties is null || additionalProperties.Count == 0)
        {
            failureCode = null;
            reason = null;
            return true;
        }

        if (additionalProperties.Keys.Any(static key => string.Equals(key, "environment", StringComparison.OrdinalIgnoreCase)))
        {
            failureCode = LaunchProcessFailureCodeValues.UnsupportedEnvironmentOverrides;
            reason = "Параметр environment для launch_process в текущем contract не поддерживается.";
            return false;
        }

        string key = additionalProperties.Keys
            .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
            .First();

        failureCode = LaunchProcessFailureCodeValues.InvalidRequest;
        reason = $"Дополнительное поле '{key}' не входит в текущий request surface launch_process.";
        return false;
    }

    private static bool TryClassifyWorkingDirectoryAsUri(string value) =>
        !Path.IsPathFullyQualified(value)
        && Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
        && uri.IsAbsoluteUri;
}
