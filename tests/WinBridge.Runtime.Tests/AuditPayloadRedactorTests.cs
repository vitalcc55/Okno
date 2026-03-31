using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Tooling;

namespace WinBridge.Runtime.Tests;

public sealed class AuditPayloadRedactorTests
{
    private static readonly string[] LaunchArguments =
    [
        "--token=super-secret",
        "--user=alice",
    ];

    [Fact]
    public void RedactRequestSuppressesRawTextPayloadValues()
    {
        AuditPayloadRedactor redactor = new();

        AuditRedactionResult result = redactor.Redact(
            new AuditPayloadRedactionContext(
                ToolName: "windows.wait",
                PayloadKind: AuditPayloadKind.Request,
                RedactionClass: ToolExecutionRedactionClass.TextPayload),
            new
            {
                condition = "text_appears",
                expectedText = "super secret text",
                selector = new
                {
                    name = "Sensitive field",
                    automationId = "SearchBox",
                    controlType = "edit",
                },
                timeoutMs = 500,
            });

        Assert.True(result.RedactionApplied);
        Assert.Contains("expectedText", result.RedactedFields);
        Assert.Contains("selector.name", result.RedactedFields);
        Assert.NotNull(result.Summary);
        Assert.DoesNotContain("super secret text", result.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("Sensitive field", result.Summary, StringComparison.Ordinal);
        Assert.Contains("SearchBox", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactEventDataSuppressesExceptionMessageAndKeepsArtifactHints()
    {
        AuditPayloadRedactor redactor = new();

        AuditRedactionResult result = redactor.Redact(
            new AuditPayloadRedactionContext(
                ToolName: "windows.wait",
                EventName: "wait.runtime.completed",
                PayloadKind: AuditPayloadKind.EventData,
                RedactionClass: ToolExecutionRedactionClass.TextPayload),
            new Dictionary<string, string?>
            {
                ["failure_stage"] = "runtime_unhandled",
                ["exception_type"] = typeof(InvalidOperationException).FullName,
                ["exception_message"] = "secret probe failure",
                ["artifact_path"] = @"C:\artifacts\wait.json",
            });

        Assert.True(result.RedactionApplied);
        Assert.Contains("exception_message", result.RedactedFields);
        Assert.Equal(typeof(InvalidOperationException).FullName, result.SanitizedData["exception_type"]);
        Assert.Equal(@"C:\artifacts\wait.json", result.SanitizedData["artifact_path"]);
        Assert.False(result.SanitizedData.ContainsKey("exception_message"));
    }

    [Fact]
    public void RedactLaunchPayloadKeepsExecutableIdentityButNotArguments()
    {
        AuditPayloadRedactor redactor = new();

        AuditRedactionResult result = redactor.Redact(
            new AuditPayloadRedactionContext(
                ToolName: "windows.launch_process",
                PayloadKind: AuditPayloadKind.Request,
                RedactionClass: ToolExecutionRedactionClass.LaunchPayload),
            new
            {
                executable = @"C:\tools\demo.exe",
                args = LaunchArguments,
                workingDirectory = @"C:\Users\alice\private",
            });

        Assert.True(result.RedactionApplied);
        Assert.Contains("args", result.RedactedFields);
        Assert.Contains("workingDirectory", result.RedactedFields);
        Assert.NotNull(result.Summary);
        Assert.Contains("demo.exe", result.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret", result.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain(@"C:\Users\alice\private", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactNonePreservesSafeSummary()
    {
        AuditPayloadRedactor redactor = new();

        AuditRedactionResult result = redactor.Redact(
            new AuditPayloadRedactionContext(
                ToolName: "windows.capture",
                PayloadKind: AuditPayloadKind.Request,
                RedactionClass: ToolExecutionRedactionClass.None),
            new
            {
                scope = "window",
                hwnd = 42,
            });

        Assert.False(result.RedactionApplied);
        Assert.Equal("{\"scope\":\"window\",\"hwnd\":42}", result.Summary);
    }

    [Fact]
    public void RedactFailsClosedWhenSerializationThrows()
    {
        AuditPayloadRedactor redactor = new();
        SelfReferencingPayload payload = new();
        payload.Self = payload;

        AuditRedactionResult result = redactor.Redact(
            new AuditPayloadRedactionContext(
                ToolName: "windows.wait",
                PayloadKind: AuditPayloadKind.Request,
                RedactionClass: ToolExecutionRedactionClass.TextPayload),
            payload);

        Assert.True(result.SummarySuppressed);
        Assert.Null(result.Summary);
    }

    private sealed class SelfReferencingPayload
    {
        public SelfReferencingPayload? Self { get; set; }
    }
}
