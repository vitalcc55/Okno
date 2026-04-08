using System.Text.Json;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Tooling;

namespace WinBridge.Runtime.Tests;

public sealed class OpenTargetContractFreezeTests
{
    [Fact]
    public void OpenTargetRequestUsesExpectedDefaults()
    {
        OpenTargetRequest request = new()
        {
            TargetKind = OpenTargetKindValues.Document,
            Target = @"C:\Docs\readme.txt",
        };

        Assert.Equal(OpenTargetKindValues.Document, request.TargetKind);
        Assert.Equal(@"C:\Docs\readme.txt", request.Target);
        Assert.False(request.DryRun);
        Assert.False(request.Confirm);
        Assert.Null(request.AdditionalProperties);
    }

    [Fact]
    public void OpenTargetRequestCanonicalizesTargetKindAndTarget()
    {
        OpenTargetRequest request = new()
        {
            TargetKind = "  document  ",
            Target = "  C:\\Docs\\readme.txt  ",
        };

        Assert.Equal("document", request.TargetKind);
        Assert.Equal(@"C:\Docs\readme.txt", request.Target);
    }

    [Fact]
    public void OpenTargetRequestDeserializesCanonicalCamelCaseTransportFields()
    {
        OpenTargetRequest request = DeserializeTransportRequest(
            """
            {
              "targetKind": "folder",
              "target": " C:\\Temp\\Workspace ",
              "dryRun": true,
              "confirm": true
            }
            """);

        Assert.Equal(OpenTargetKindValues.Folder, request.TargetKind);
        Assert.Equal(@"C:\Temp\Workspace", request.Target);
        Assert.True(request.DryRun);
        Assert.True(request.Confirm);
        Assert.Null(request.AdditionalProperties);
    }

    [Fact]
    public void OpenTargetResultUsesExpectedDefaults()
    {
        OpenTargetResult result = new(
            Status: OpenTargetStatusValues.Failed,
            Decision: OpenTargetStatusValues.Failed);

        Assert.Equal(OpenTargetStatusValues.Failed, result.Status);
        Assert.Equal(OpenTargetStatusValues.Failed, result.Decision);
        Assert.Null(result.ResultMode);
        Assert.Null(result.FailureCode);
        Assert.Null(result.Reason);
        Assert.Null(result.TargetKind);
        Assert.Null(result.TargetIdentity);
        Assert.Null(result.UriScheme);
        Assert.Null(result.AcceptedAtUtc);
        Assert.Null(result.HandlerProcessId);
        Assert.Null(result.ArtifactPath);
        Assert.Null(result.Preview);
        Assert.Null(result.RiskLevel);
        Assert.Null(result.GuardCapability);
        Assert.False(result.RequiresConfirmation);
        Assert.False(result.DryRunSupported);
        Assert.Null(result.Reasons);
    }

    [Fact]
    public void OpenTargetPreviewUsesExpectedShape()
    {
        OpenTargetPreview preview = new(
            TargetKind: OpenTargetKindValues.Url,
            TargetIdentity: null,
            UriScheme: "https");

        Assert.Equal(OpenTargetKindValues.Url, preview.TargetKind);
        Assert.Null(preview.TargetIdentity);
        Assert.Equal("https", preview.UriScheme);
    }

    [Fact]
    public void OpenTargetKindValuesExposeExpectedLiterals()
    {
        Assert.Equal("document", OpenTargetKindValues.Document);
        Assert.Equal("folder", OpenTargetKindValues.Folder);
        Assert.Equal("url", OpenTargetKindValues.Url);
    }

    [Fact]
    public void OpenTargetStatusValuesExposeExpectedLiterals()
    {
        Assert.Equal("done", OpenTargetStatusValues.Done);
        Assert.Equal("failed", OpenTargetStatusValues.Failed);
        Assert.Equal("blocked", OpenTargetStatusValues.Blocked);
        Assert.Equal("needs_confirmation", OpenTargetStatusValues.NeedsConfirmation);
        Assert.Equal("dry_run_only", OpenTargetStatusValues.DryRunOnly);
    }

    [Fact]
    public void OpenTargetResultModeValuesExposeExpectedLiterals()
    {
        Assert.Equal("target_open_requested", OpenTargetResultModeValues.TargetOpenRequested);
        Assert.Equal("handler_process_observed", OpenTargetResultModeValues.HandlerProcessObserved);
    }

    [Fact]
    public void OpenTargetFailureCodeValuesExposeExpectedLiterals()
    {
        Assert.Equal("invalid_request", OpenTargetFailureCodeValues.InvalidRequest);
        Assert.Equal("unsupported_target_kind", OpenTargetFailureCodeValues.UnsupportedTargetKind);
        Assert.Equal("unsupported_uri_scheme", OpenTargetFailureCodeValues.UnsupportedUriScheme);
        Assert.Equal("target_not_found", OpenTargetFailureCodeValues.TargetNotFound);
        Assert.Equal("target_access_denied", OpenTargetFailureCodeValues.TargetAccessDenied);
        Assert.Equal("no_association", OpenTargetFailureCodeValues.NoAssociation);
        Assert.Equal("shell_rejected_target", OpenTargetFailureCodeValues.ShellRejectedTarget);
    }

    [Fact]
    public void ToolDescriptionsExposeOpenTargetFreezeStrings()
    {
        Assert.False(string.IsNullOrWhiteSpace(ToolDescriptions.WindowsOpenTargetTool));
        Assert.False(string.IsNullOrWhiteSpace(ToolDescriptions.OpenTargetKindParameter));
        Assert.False(string.IsNullOrWhiteSpace(ToolDescriptions.OpenTargetTargetParameter));
        Assert.False(string.IsNullOrWhiteSpace(ToolDescriptions.OpenTargetDryRunParameter));
        Assert.False(string.IsNullOrWhiteSpace(ToolDescriptions.OpenTargetConfirmParameter));
    }

    [Theory]
    [InlineData("document", @"C:\Docs\readme.txt")]
    [InlineData("folder", @"C:\Docs")]
    [InlineData("url", "https://example.test/docs?q=hidden#fragment")]
    public void OpenTargetRequestValidatorAcceptsSupportedTargetForms(string targetKind, string target)
    {
        OpenTargetRequest request = new()
        {
            TargetKind = targetKind,
            Target = target,
        };

        bool isValid = OpenTargetRequestValidator.TryValidate(request, out string? failureCode, out string? reason);

        Assert.True(isValid);
        Assert.Null(failureCode);
        Assert.Null(reason);
    }

    [Fact]
    public void OpenTargetRequestValidatorRejectsBlankTargetKind()
    {
        OpenTargetRequest request = new()
        {
            TargetKind = " ",
            Target = @"C:\Docs\readme.txt",
        };

        bool isValid = OpenTargetRequestValidator.TryValidate(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(OpenTargetFailureCodeValues.InvalidRequest, failureCode);
        Assert.Contains("targetKind", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OpenTargetRequestValidatorRejectsBlankTarget()
    {
        OpenTargetRequest request = new()
        {
            TargetKind = OpenTargetKindValues.Document,
            Target = " ",
        };

        bool isValid = OpenTargetRequestValidator.TryValidate(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(OpenTargetFailureCodeValues.InvalidRequest, failureCode);
        Assert.Contains("target", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("file")]
    [InlineData("process")]
    [InlineData("Document")]
    public void OpenTargetRequestValidatorRejectsUnsupportedTargetKind(string targetKind)
    {
        OpenTargetRequest request = new()
        {
            TargetKind = targetKind,
            Target = @"C:\Docs\readme.txt",
        };

        bool isValid = OpenTargetRequestValidator.TryValidate(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(OpenTargetFailureCodeValues.UnsupportedTargetKind, failureCode);
        Assert.Contains("targetKind", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("document", @".\docs\readme.txt")]
    [InlineData("document", @"docs\readme.txt")]
    [InlineData("folder", @".\workspace")]
    [InlineData("folder", @"workspace")]
    public void OpenTargetRequestValidatorRejectsRelativePaths(string targetKind, string target)
    {
        OpenTargetRequest request = new()
        {
            TargetKind = targetKind,
            Target = target,
        };

        bool isValid = OpenTargetRequestValidator.TryValidate(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(OpenTargetFailureCodeValues.InvalidRequest, failureCode);
        Assert.Contains("absolute", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("document", "C:readme.txt")]
    [InlineData("folder", "C:workspace")]
    public void OpenTargetRequestValidatorRejectsDriveRelativePaths(string targetKind, string target)
    {
        OpenTargetRequest request = new()
        {
            TargetKind = targetKind,
            Target = target,
        };

        bool isValid = OpenTargetRequestValidator.TryValidate(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(OpenTargetFailureCodeValues.InvalidRequest, failureCode);
        Assert.Contains("absolute", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(@"C:\Tools\setup.exe")]
    [InlineData(@"C:\Tools\deploy.application")]
    [InlineData(@"C:\Tools\launch.appref-ms")]
    [InlineData(@"C:\Tools\runner.com")]
    [InlineData(@"C:\Tools\script.ps1")]
    [InlineData(@"C:\Tools\launch.bat")]
    [InlineData(@"C:\Tools\shortcut.lnk")]
    [InlineData(@"C:\Tools\web.url")]
    [InlineData(@"C:\Tools\control.cpl")]
    [InlineData(@"C:\Tools\automation.wsf")]
    [InlineData(@"C:\Tools\script.jse")]
    [InlineData(@"C:\Tools\profile.psd1")]
    [InlineData(@"C:\Tools\module.psm1")]
    public void OpenTargetRequestValidatorRejectsExecutableAndLauncherDocumentTargets(string target)
    {
        OpenTargetRequest request = new()
        {
            TargetKind = OpenTargetKindValues.Document,
            Target = target,
        };

        bool isValid = OpenTargetRequestValidator.TryValidate(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(OpenTargetFailureCodeValues.UnsupportedTargetKind, failureCode);
        Assert.Contains("document", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("mailto:user@example.test")]
    [InlineData("file:///C:/Docs/readme.txt")]
    [InlineData("ms-settings:display")]
    [InlineData("slack://channel")]
    public void OpenTargetRequestValidatorRejectsUnsupportedUriSchemes(string target)
    {
        OpenTargetRequest request = new()
        {
            TargetKind = OpenTargetKindValues.Url,
            Target = target,
        };

        bool isValid = OpenTargetRequestValidator.TryValidate(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(OpenTargetFailureCodeValues.UnsupportedUriScheme, failureCode);
        Assert.Contains("scheme", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("workingDirectory", "\"C:\\\\Docs\"")]
    [InlineData("verb", "\"openas\"")]
    [InlineData("waitForWindow", "true")]
    [InlineData("timeoutMs", "5000")]
    [InlineData("environment", "{}")]
    public void OpenTargetRequestValidatorRejectsUnsupportedAdditionalFields(string propertyName, string rawJsonValue)
    {
        OpenTargetRequest request = DeserializeTransportRequest(
            $$"""
            {
              "targetKind": "document",
              "target": "C:\\Docs\\readme.txt",
              "{{propertyName}}": {{rawJsonValue}}
            }
            """);

        bool isValid = OpenTargetRequestValidator.TryValidate(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(OpenTargetFailureCodeValues.InvalidRequest, failureCode);
        Assert.Contains(propertyName, reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OpenTargetClassifierBuildsSafePreviewIdentityForPathTarget()
    {
        OpenTargetClassification classification = AssertClassified(
            new OpenTargetRequest
            {
                TargetKind = OpenTargetKindValues.Document,
                Target = @"C:\Docs\Quarterly\report.pdf",
            });

        Assert.Equal(OpenTargetKindValues.Document, classification.TargetKind);
        Assert.Equal("report.pdf", classification.TargetIdentity);
        Assert.Null(classification.UriScheme);
    }

    [Fact]
    public void OpenTargetClassifierBuildsSafePreviewIdentityForUrlTarget()
    {
        OpenTargetClassification classification = AssertClassified(
            new OpenTargetRequest
            {
                TargetKind = OpenTargetKindValues.Url,
                Target = "https://example.test/docs?q=secret#fragment",
            });

        Assert.Equal(OpenTargetKindValues.Url, classification.TargetKind);
        Assert.Null(classification.TargetIdentity);
        Assert.Equal("https", classification.UriScheme);
    }

    private static OpenTargetClassification AssertClassified(OpenTargetRequest request)
    {
        bool isValid = OpenTargetRequestValidator.TryValidate(
            request,
            out OpenTargetClassification classification,
            out string? failureCode,
            out string? reason);

        Assert.True(isValid);
        Assert.Null(failureCode);
        Assert.Null(reason);
        return classification;
    }

    private static OpenTargetRequest DeserializeTransportRequest(string json) =>
        JsonSerializer.Deserialize<OpenTargetRequest>(json)
        ?? throw new InvalidOperationException("Transport JSON did not deserialize to OpenTargetRequest.");

}
