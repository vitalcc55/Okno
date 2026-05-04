// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

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
    public void RedactLaunchEventDataKeepsExecutableBasenameButNotFullPath()
    {
        AuditPayloadRedactor redactor = new();

        AuditRedactionResult result = redactor.Redact(
            new AuditPayloadRedactionContext(
                ToolName: "windows.launch_process",
                EventName: "launch.runtime.completed",
                PayloadKind: AuditPayloadKind.EventData,
                RedactionClass: ToolExecutionRedactionClass.LaunchPayload),
            new Dictionary<string, string?>
            {
                ["executable"] = @"C:\tools\demo.exe",
                ["arguments"] = "--token=super-secret",
            });

        Assert.True(result.RedactionApplied);
        Assert.Contains("executable", result.RedactedFields);
        Assert.Contains("arguments", result.RedactedFields);
        Assert.Equal("demo.exe", result.SanitizedData["executable"]);
        Assert.False(result.SanitizedData.ContainsKey("arguments"));
    }

    [Fact]
    public void RedactLaunchRuntimeEventDataKeepsSafeFieldsAndSuppressesSensitiveLaunchValues()
    {
        AuditPayloadRedactor redactor = new();

        AuditRedactionResult result = redactor.Redact(
            new AuditPayloadRedactionContext(
                ToolName: "windows.launch_process",
                EventName: "launch.runtime.completed",
                PayloadKind: AuditPayloadKind.EventData,
                RedactionClass: ToolExecutionRedactionClass.LaunchPayload),
            new Dictionary<string, string?>
            {
                ["status"] = "done",
                ["executable_identity"] = @"C:\tools\demo.exe",
                ["working_directory"] = @"C:\Users\alice\private",
                ["arguments"] = "--token=super-secret",
                ["artifact_path"] = @"C:\artifacts\launch\launch-20260406T140000000-demo.json",
            });

        Assert.True(result.RedactionApplied);
        Assert.Contains("executable_identity", result.RedactedFields);
        Assert.Contains("working_directory", result.RedactedFields);
        Assert.Contains("arguments", result.RedactedFields);
        Assert.Equal("done", result.SanitizedData["status"]);
        Assert.Equal("demo.exe", result.SanitizedData["executable_identity"]);
        Assert.Equal(@"C:\artifacts\launch\launch-20260406T140000000-demo.json", result.SanitizedData["artifact_path"]);
        Assert.False(result.SanitizedData.ContainsKey("working_directory"));
        Assert.False(result.SanitizedData.ContainsKey("arguments"));
    }

    [Fact]
    public void RedactLaunchEventDataSuppressesExecutableWhenBasenameCannotBeResolved()
    {
        AuditPayloadRedactor redactor = new();

        AuditRedactionResult result = redactor.Redact(
            new AuditPayloadRedactionContext(
                ToolName: "windows.launch_process",
                EventName: "launch.runtime.completed",
                PayloadKind: AuditPayloadKind.EventData,
                RedactionClass: ToolExecutionRedactionClass.LaunchPayload),
            new Dictionary<string, string?>
            {
                ["executable"] = @"C:\tools\folder\",
            });

        Assert.True(result.RedactionApplied);
        Assert.Contains("executable", result.RedactedFields);
        Assert.False(result.SanitizedData.ContainsKey("executable"));
    }

    [Fact]
    public void RedactOpenTargetRuntimeEventDataKeepsSafePathIdentityAndSuppressesRawPathAndUrl()
    {
        AuditPayloadRedactor redactor = new();

        AuditRedactionResult result = redactor.Redact(
            new AuditPayloadRedactionContext(
                ToolName: "windows.open_target",
                EventName: "open_target.runtime.completed",
                PayloadKind: AuditPayloadKind.EventData,
                RedactionClass: ToolExecutionRedactionClass.LaunchPayload),
            new Dictionary<string, string?>
            {
                ["status"] = "done",
                ["target_kind"] = "document",
                ["target_identity"] = @"C:\Docs\Quarterly\report.pdf",
                ["target"] = @"C:\Docs\Quarterly\report.pdf",
                ["url"] = "https://example.test/docs?q=hidden#fragment",
                ["artifact_path"] = @"C:\artifacts\diagnostics\launch\open-target-20260408T130000000-demo.json",
            });

        Assert.True(result.RedactionApplied);
        Assert.Contains("target_identity", result.RedactedFields);
        Assert.Contains("target", result.RedactedFields);
        Assert.Contains("url", result.RedactedFields);
        Assert.Equal("done", result.SanitizedData["status"]);
        Assert.Equal("document", result.SanitizedData["target_kind"]);
        Assert.Equal("report.pdf", result.SanitizedData["target_identity"]);
        Assert.Equal(@"C:\artifacts\diagnostics\launch\open-target-20260408T130000000-demo.json", result.SanitizedData["artifact_path"]);
        Assert.False(result.SanitizedData.ContainsKey("target"));
        Assert.False(result.SanitizedData.ContainsKey("url"));
    }

    [Fact]
    public void RedactOpenTargetRuntimeEventDataSuppressesUrlLikeTargetIdentityWhenSafeIdentityCannotBeDerived()
    {
        AuditPayloadRedactor redactor = new();

        AuditRedactionResult result = redactor.Redact(
            new AuditPayloadRedactionContext(
                ToolName: "windows.open_target",
                EventName: "open_target.runtime.completed",
                PayloadKind: AuditPayloadKind.EventData,
                RedactionClass: ToolExecutionRedactionClass.LaunchPayload),
            new Dictionary<string, string?>
            {
                ["target_kind"] = "url",
                ["target_identity"] = "https://example.test/docs?q=hidden#fragment",
                ["uri_scheme"] = "https",
            });

        Assert.True(result.RedactionApplied);
        Assert.Contains("target_identity", result.RedactedFields);
        Assert.Equal("url", result.SanitizedData["target_kind"]);
        Assert.Equal("https", result.SanitizedData["uri_scheme"]);
        Assert.False(result.SanitizedData.ContainsKey("target_identity"));
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
