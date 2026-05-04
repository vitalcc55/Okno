// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Tooling;

namespace WinBridge.Runtime.Tests;

public sealed class LaunchProcessContractFreezeTests
{
    [Fact]
    public void LaunchProcessRequestUsesExpectedDefaults()
    {
        LaunchProcessRequest request = new()
        {
            Executable = "notepad.exe",
        };

        Assert.Equal("notepad.exe", request.Executable);
        Assert.Empty(request.Args);
        Assert.Null(request.WorkingDirectory);
        Assert.False(request.WaitForWindow);
        Assert.Null(request.TimeoutMs);
        Assert.False(request.DryRun);
        Assert.False(request.Confirm);
    }

    [Fact]
    public void LaunchProcessRequestCanonicalizesExecutableAndWorkingDirectory()
    {
        LaunchProcessRequest request = new()
        {
            Executable = "  notepad.exe  ",
            WorkingDirectory = "  C:\\Tools  ",
        };

        Assert.Equal("notepad.exe", request.Executable);
        Assert.Equal(@"C:\Tools", request.WorkingDirectory);
    }

    [Fact]
    public void LaunchProcessRequestDeserializesCanonicalCamelCaseTransportFields()
    {
        LaunchProcessRequest request = DeserializeTransportRequest(
            """
            {
              "executable": "notepad.exe",
              "waitForWindow": true,
              "timeoutMs": 5000,
              "dryRun": true,
              "confirm": true,
              "workingDirectory": " C:\\Tools ",
              "args": ["--flag"]
            }
            """);

        Assert.Equal("notepad.exe", request.Executable);
        Assert.True(request.WaitForWindow);
        Assert.Equal(5000, request.TimeoutMs);
        Assert.True(request.DryRun);
        Assert.True(request.Confirm);
        Assert.Equal(@"C:\Tools", request.WorkingDirectory);
        Assert.Equal(["--flag"], request.Args);
        Assert.Null(request.AdditionalProperties);
    }

    [Fact]
    public void LaunchProcessResultUsesExpectedDefaults()
    {
        LaunchProcessResult result = new(
            Status: LaunchProcessStatusValues.Failed,
            Decision: LaunchProcessStatusValues.Failed);

        Assert.Equal(LaunchProcessStatusValues.Failed, result.Status);
        Assert.Equal(LaunchProcessStatusValues.Failed, result.Decision);
        Assert.Null(result.ResultMode);
        Assert.Null(result.FailureCode);
        Assert.Null(result.Reason);
        Assert.Null(result.ExecutableIdentity);
        Assert.Null(result.ProcessId);
        Assert.Null(result.StartedAtUtc);
        Assert.Null(result.HasExited);
        Assert.Null(result.ExitCode);
        Assert.False(result.MainWindowObserved);
        Assert.Null(result.MainWindowHandle);
        Assert.Null(result.MainWindowObservationStatus);
        Assert.Null(result.ArtifactPath);
        Assert.Null(result.Preview);
        Assert.Null(result.RiskLevel);
        Assert.Null(result.GuardCapability);
        Assert.False(result.RequiresConfirmation);
        Assert.False(result.DryRunSupported);
        Assert.Null(result.Reasons);
    }

    [Fact]
    public void LaunchProcessPreviewUsesExpectedShape()
    {
        LaunchProcessPreview preview = new(
            ExecutableIdentity: "notepad.exe",
            ResolutionMode: LaunchProcessPreviewResolutionModeValues.PathLookup,
            ArgumentCount: 2,
            WorkingDirectoryProvided: true,
            WaitForWindow: true,
            TimeoutMs: 7500);

        Assert.Equal("notepad.exe", preview.ExecutableIdentity);
        Assert.Equal(LaunchProcessPreviewResolutionModeValues.PathLookup, preview.ResolutionMode);
        Assert.Equal(2, preview.ArgumentCount);
        Assert.True(preview.WorkingDirectoryProvided);
        Assert.True(preview.WaitForWindow);
        Assert.Equal(7500, preview.TimeoutMs);
    }

    [Fact]
    public void LaunchProcessStatusValuesExposeExpectedLiterals()
    {
        Assert.Equal("done", LaunchProcessStatusValues.Done);
        Assert.Equal("failed", LaunchProcessStatusValues.Failed);
        Assert.Equal("blocked", LaunchProcessStatusValues.Blocked);
        Assert.Equal("needs_confirmation", LaunchProcessStatusValues.NeedsConfirmation);
        Assert.Equal("dry_run_only", LaunchProcessStatusValues.DryRunOnly);
    }

    [Fact]
    public void LaunchProcessResultModeValuesExposeExpectedLiterals()
    {
        Assert.Equal("process_started", LaunchProcessResultModeValues.ProcessStarted);
        Assert.Equal("process_started_and_exited", LaunchProcessResultModeValues.ProcessStartedAndExited);
        Assert.Equal("window_observed", LaunchProcessResultModeValues.WindowObserved);
    }

    [Fact]
    public void LaunchProcessFailureCodeValuesExposeExpectedLiterals()
    {
        Assert.Equal("invalid_request", LaunchProcessFailureCodeValues.InvalidRequest);
        Assert.Equal("unsupported_target_kind", LaunchProcessFailureCodeValues.UnsupportedTargetKind);
        Assert.Equal("unsupported_environment_overrides", LaunchProcessFailureCodeValues.UnsupportedEnvironmentOverrides);
        Assert.Equal("executable_not_found", LaunchProcessFailureCodeValues.ExecutableNotFound);
        Assert.Equal("working_directory_not_found", LaunchProcessFailureCodeValues.WorkingDirectoryNotFound);
        Assert.Equal("start_failed", LaunchProcessFailureCodeValues.StartFailed);
        Assert.Equal("process_object_unavailable", LaunchProcessFailureCodeValues.ProcessObjectUnavailable);
        Assert.Equal("process_exited_before_window", LaunchProcessFailureCodeValues.ProcessExitedBeforeWindow);
        Assert.Equal("main_window_timeout", LaunchProcessFailureCodeValues.MainWindowTimeout);
        Assert.Equal("main_window_not_observed", LaunchProcessFailureCodeValues.MainWindowNotObserved);
        Assert.Equal("main_window_observation_not_supported", LaunchProcessFailureCodeValues.MainWindowObservationNotSupported);
    }

    [Fact]
    public void LaunchMainWindowObservationStatusValuesExposeExpectedLiterals()
    {
        Assert.Equal("not_requested", LaunchMainWindowObservationStatusValues.NotRequested);
        Assert.Equal("observed", LaunchMainWindowObservationStatusValues.Observed);
        Assert.Equal("timed_out", LaunchMainWindowObservationStatusValues.TimedOut);
        Assert.Equal("not_observed", LaunchMainWindowObservationStatusValues.NotObserved);
        Assert.Equal("not_supported", LaunchMainWindowObservationStatusValues.NotSupported);
        Assert.Equal("process_exited", LaunchMainWindowObservationStatusValues.ProcessExited);
    }

    [Fact]
    public void LaunchProcessPreviewResolutionModeValuesExposeExpectedLiterals()
    {
        Assert.Equal("absolute_path", LaunchProcessPreviewResolutionModeValues.AbsolutePath);
        Assert.Equal("path_lookup", LaunchProcessPreviewResolutionModeValues.PathLookup);
    }

    [Fact]
    public void ToolDescriptionsExposeLaunchProcessFreezeStrings()
    {
        Assert.False(string.IsNullOrWhiteSpace(ToolDescriptions.WindowsLaunchProcessTool));
        Assert.False(string.IsNullOrWhiteSpace(ToolDescriptions.LaunchProcessExecutableParameter));
        Assert.False(string.IsNullOrWhiteSpace(ToolDescriptions.LaunchProcessArgsParameter));
        Assert.False(string.IsNullOrWhiteSpace(ToolDescriptions.LaunchProcessWorkingDirectoryParameter));
        Assert.False(string.IsNullOrWhiteSpace(ToolDescriptions.LaunchProcessWaitForWindowParameter));
        Assert.False(string.IsNullOrWhiteSpace(ToolDescriptions.LaunchProcessTimeoutMsParameter));
        Assert.False(string.IsNullOrWhiteSpace(ToolDescriptions.LaunchProcessDryRunParameter));
        Assert.False(string.IsNullOrWhiteSpace(ToolDescriptions.LaunchProcessConfirmParameter));
    }

    [Theory]
    [InlineData("notepad.exe")]
    [InlineData(@"C:\Tools\app.exe")]
    public void LaunchProcessRequestValidatorAcceptsSupportedExecutableForms(string executable)
    {
        LaunchProcessRequest request = new()
        {
            Executable = executable,
        };

        bool isValid = LaunchProcessRequestValidator.TryValidate(request, out string? failureCode, out string? reason);

        Assert.True(isValid);
        Assert.Null(failureCode);
        Assert.Null(reason);
    }

    [Fact]
    public void LaunchProcessRequestValidatorAcceptsAbsoluteWorkingDirectoryAndWindowWaitTimeout()
    {
        LaunchProcessRequest request = new()
        {
            Executable = @"C:\Tools\app.exe",
            WorkingDirectory = @"C:\Tools",
            WaitForWindow = true,
            TimeoutMs = 9000,
        };

        bool isValid = LaunchProcessRequestValidator.TryValidate(request, out string? failureCode, out string? reason);

        Assert.True(isValid);
        Assert.Null(failureCode);
        Assert.Null(reason);
    }

    [Fact]
    public void LaunchProcessRequestValidatorRejectsBlankExecutable()
    {
        LaunchProcessRequest request = new()
        {
            Executable = "   ",
        };

        bool isValid = LaunchProcessRequestValidator.TryValidate(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(LaunchProcessFailureCodeValues.InvalidRequest, failureCode);
        Assert.Contains("executable", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("https://example.test/app.exe")]
    [InlineData("file:///C:/Tools/app.exe")]
    public void LaunchProcessRequestValidatorRejectsUris(string executable)
    {
        LaunchProcessRequest request = new()
        {
            Executable = executable,
        };

        bool isValid = LaunchProcessRequestValidator.TryValidate(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(LaunchProcessFailureCodeValues.UnsupportedTargetKind, failureCode);
        Assert.Contains("uri", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(@"C:\Tools\")]
    [InlineData(@"\\server\share\folder\")]
    public void LaunchProcessRequestValidatorRejectsDirectoryTargets(string executable)
    {
        LaunchProcessRequest request = new()
        {
            Executable = executable,
        };

        bool isValid = LaunchProcessRequestValidator.TryValidate(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(LaunchProcessFailureCodeValues.UnsupportedTargetKind, failureCode);
        Assert.Contains("directory", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LaunchProcessRequestValidatorRejectsExistingRootedDirectoryWithoutTrailingSeparator()
    {
        string rootedDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        Assert.True(Directory.Exists(rootedDirectory), $"Expected existing Windows directory, got '{rootedDirectory}'.");

        LaunchProcessRequest request = new()
        {
            Executable = rootedDirectory,
        };

        bool isValid = LaunchProcessRequestValidator.TryValidate(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(LaunchProcessFailureCodeValues.UnsupportedTargetKind, failureCode);
        Assert.Contains("direct executable", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(@".\tools\app.exe")]
    [InlineData(@"tools\app.exe")]
    [InlineData(@"subdir/app.exe")]
    public void LaunchProcessRequestValidatorRejectsRelativeSubpaths(string executable)
    {
        LaunchProcessRequest request = new()
        {
            Executable = executable,
        };

        bool isValid = LaunchProcessRequestValidator.TryValidate(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(LaunchProcessFailureCodeValues.InvalidRequest, failureCode);
        Assert.Contains("relative", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LaunchProcessRequestValidatorRejectsDriveRelativeExecutable()
    {
        LaunchProcessRequest request = new()
        {
            Executable = "C:demo.exe",
        };

        bool isValid = LaunchProcessRequestValidator.TryValidate(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(LaunchProcessFailureCodeValues.InvalidRequest, failureCode);
        Assert.Contains("absolute path", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(@"C:\Temp\readme.txt")]
    [InlineData(@"C:\Temp\link.url")]
    [InlineData(@"C:\Temp\script.ps1")]
    public void LaunchProcessRequestValidatorRejectsObviousDocumentTargets(string executable)
    {
        LaunchProcessRequest request = new()
        {
            Executable = executable,
        };

        bool isValid = LaunchProcessRequestValidator.TryValidate(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(LaunchProcessFailureCodeValues.UnsupportedTargetKind, failureCode);
        Assert.Contains("launch_process", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(@"C:\Temp\photo.png")]
    [InlineData(@"C:\Temp\archive.zip")]
    [InlineData(@"C:\Temp\notes.yaml")]
    [InlineData(@"C:\Temp\report.log")]
    public void LaunchProcessRequestValidatorRejectsAdditionalDocumentLikeAbsoluteTargets(string executable)
    {
        LaunchProcessRequest request = new()
        {
            Executable = executable,
        };

        bool isValid = LaunchProcessRequestValidator.TryValidate(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(LaunchProcessFailureCodeValues.UnsupportedTargetKind, failureCode);
        Assert.Contains("launch_process", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData(@".\work")]
    public void LaunchProcessRequestValidatorRejectsInvalidWorkingDirectory(string workingDirectory)
    {
        LaunchProcessRequest request = new()
        {
            Executable = "notepad.exe",
            WorkingDirectory = workingDirectory,
        };

        bool isValid = LaunchProcessRequestValidator.TryValidate(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(LaunchProcessFailureCodeValues.InvalidRequest, failureCode);
        Assert.Contains("workingDirectory", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LaunchProcessRequestValidatorRejectsNonDefaultTimeoutWhenWindowWaitIsDisabled()
    {
        LaunchProcessRequest request = new()
        {
            Executable = "notepad.exe",
            WaitForWindow = false,
            TimeoutMs = LaunchProcessDefaults.TimeoutMs + 1,
        };

        bool isValid = LaunchProcessRequestValidator.TryValidate(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(LaunchProcessFailureCodeValues.InvalidRequest, failureCode);
        Assert.Contains("waitForWindow", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LaunchProcessRequestValidatorRejectsExplicitDefaultTimeoutWhenWindowWaitIsDisabled()
    {
        LaunchProcessRequest request = new()
        {
            Executable = "notepad.exe",
            WaitForWindow = false,
            TimeoutMs = LaunchProcessDefaults.TimeoutMs,
        };

        bool isValid = LaunchProcessRequestValidator.TryValidate(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(LaunchProcessFailureCodeValues.InvalidRequest, failureCode);
        Assert.Contains("waitForWindow", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LaunchProcessRequestValidatorRejectsNonPositiveTimeout()
    {
        LaunchProcessRequest request = new()
        {
            Executable = "notepad.exe",
            WaitForWindow = true,
            TimeoutMs = 0,
        };

        bool isValid = LaunchProcessRequestValidator.TryValidate(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(LaunchProcessFailureCodeValues.InvalidRequest, failureCode);
        Assert.Contains("timeoutMs", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LaunchProcessRequestValidatorRejectsEnvironmentOverridesCapturedFromJson()
    {
        LaunchProcessRequest request = DeserializeTransportRequest(
            """
            {
              "executable": "notepad.exe",
              "environment": {
                "FOO": "bar"
              }
            }
            """);

        bool isValid = LaunchProcessRequestValidator.TryValidate(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(LaunchProcessFailureCodeValues.UnsupportedEnvironmentOverrides, failureCode);
        Assert.Contains("environment", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("verb", "\"runas\"")]
    [InlineData("attachAfterLaunch", "true")]
    [InlineData("createNoWindow", "true")]
    [InlineData("userName", "\"Administrator\"")]
    public void LaunchProcessRequestValidatorRejectsUnsupportedAdditionalOverrides(string propertyName, string rawJsonValue)
    {
        LaunchProcessRequest request = DeserializeTransportRequest(
            $$"""
            {
              "executable": "notepad.exe",
              "{{propertyName}}": {{rawJsonValue}}
            }
            """);

        bool isValid = LaunchProcessRequestValidator.TryValidate(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(LaunchProcessFailureCodeValues.InvalidRequest, failureCode);
        Assert.Contains(propertyName, reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LaunchProcessRequestValidatorRejectsNullArgsElementsCapturedFromJson()
    {
        LaunchProcessRequest request = DeserializeTransportRequest(
            """
            {
              "executable": "notepad.exe",
              "args": [null, "--flag"]
            }
            """);

        bool isValid = LaunchProcessRequestValidator.TryValidate(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(LaunchProcessFailureCodeValues.InvalidRequest, failureCode);
        Assert.Contains("args", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LaunchProcessRequestValidatorPrioritizesEnvironmentFailureCodeWhenMixedExtraFieldsExist()
    {
        LaunchProcessRequest request = DeserializeTransportRequest(
            """
            {
              "executable": "notepad.exe",
              "attachAfterLaunch": true,
              "environment": {
                "FOO": "bar"
              }
            }
            """);

        bool isValid = LaunchProcessRequestValidator.TryValidate(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(LaunchProcessFailureCodeValues.UnsupportedEnvironmentOverrides, failureCode);
        Assert.Contains("environment", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(@"C:\Temp\tool.dll")]
    [InlineData("foo.cmd")]
    [InlineData("runner.bat")]
    public void LaunchProcessRequestValidatorRejectsUnsupportedDirectExecutableExtensions(string executable)
    {
        LaunchProcessRequest request = new()
        {
            Executable = executable,
        };

        bool isValid = LaunchProcessRequestValidator.TryValidate(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(LaunchProcessFailureCodeValues.UnsupportedTargetKind, failureCode);
        Assert.Contains("direct executable", reason, StringComparison.OrdinalIgnoreCase);
    }

    private static LaunchProcessRequest DeserializeTransportRequest(string json) =>
        JsonSerializer.Deserialize<LaunchProcessRequest>(json)
        ?? throw new InvalidOperationException("Transport JSON did not deserialize to LaunchProcessRequest.");
}
