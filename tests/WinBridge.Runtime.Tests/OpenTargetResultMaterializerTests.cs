// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Windows.Launch;

namespace WinBridge.Runtime.Tests;

public sealed class OpenTargetResultMaterializerTests
{
    [Fact]
    public void MaterializeWritesLaunchFamilyArtifactAndRuntimeEventForFactualLiveResult()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateOptions(root, "run-open-target-materialize");
        AuditLog auditLog = new(options, TimeProvider.System);
        FakeTimeProvider timeProvider = new(new DateTimeOffset(2026, 4, 8, 13, 0, 0, TimeSpan.Zero));
        OpenTargetResultMaterializer materializer = new(auditLog, options, timeProvider);

        OpenTargetResult result = materializer.Materialize(
            new OpenTargetResult(
                Status: OpenTargetStatusValues.Done,
                Decision: OpenTargetStatusValues.Done,
                ResultMode: OpenTargetResultModeValues.HandlerProcessObserved,
                TargetKind: OpenTargetKindValues.Document,
                TargetIdentity: "report.pdf",
                AcceptedAtUtc: new DateTimeOffset(2026, 4, 8, 12, 59, 0, TimeSpan.Zero),
                HandlerProcessId: 4242,
                ArtifactPath: null));

        Assert.NotNull(result.ArtifactPath);
        Assert.Contains($"{Path.DirectorySeparatorChar}launch{Path.DirectorySeparatorChar}open-target-", result.ArtifactPath, StringComparison.Ordinal);

        string[] eventLines = File.ReadAllLines(options.EventsPath);
        Assert.Single(eventLines);
        Assert.Contains("\"event_name\":\"open_target.runtime.completed\"", eventLines[0], StringComparison.Ordinal);
        Assert.Contains("\"tool_name\":\"windows.open_target\"", eventLines[0], StringComparison.Ordinal);
        Assert.Contains("\"handler_process_id\":\"4242\"", eventLines[0], StringComparison.Ordinal);

        string summary = File.ReadAllText(options.SummaryPath);
        Assert.Contains("target_kind=document", summary, StringComparison.Ordinal);
        Assert.Contains("target_identity=report.pdf", summary, StringComparison.Ordinal);
        Assert.Contains("handler_process_id=4242", summary, StringComparison.Ordinal);
        Assert.DoesNotContain(@"C:\Docs\report.pdf", summary, StringComparison.Ordinal);

        using JsonDocument artifact = JsonDocument.Parse(File.ReadAllText(result.ArtifactPath));
        JsonElement payload = artifact.RootElement;
        Assert.Equal(OpenTargetStatusValues.Done, payload.GetProperty("result").GetProperty("status").GetString());
        Assert.Equal(OpenTargetKindValues.Document, payload.GetProperty("result").GetProperty("target_kind").GetString());
        Assert.Equal("report.pdf", payload.GetProperty("result").GetProperty("target_identity").GetString());
    }

    [Fact]
    public void MaterializeReturnsFactualResultWhenArtifactWriteFails()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateOptions(root, "run-open-target-artifact-failure");
        AuditLog auditLog = new(options, TimeProvider.System);
        FakeTimeProvider timeProvider = new(new DateTimeOffset(2026, 4, 8, 13, 5, 0, TimeSpan.Zero));
        string blockedRunDirectory = Path.Combine(root, "blocked-parent", "artifact.json");
        Directory.CreateDirectory(Path.GetDirectoryName(blockedRunDirectory)!);
        File.WriteAllText(blockedRunDirectory, "not-a-directory");
        OpenTargetArtifactWriter writer = new(new AuditLogOptions(
            options.ContentRootPath,
            options.EnvironmentName,
            options.RunId,
            options.DiagnosticsRoot,
            blockedRunDirectory,
            options.EventsPath,
            options.SummaryPath));
        OpenTargetResultMaterializer materializer = new(writer, auditLog, timeProvider);

        OpenTargetResult result = materializer.Materialize(
            new OpenTargetResult(
                Status: OpenTargetStatusValues.Failed,
                Decision: OpenTargetStatusValues.Failed,
                FailureCode: OpenTargetFailureCodeValues.TargetNotFound,
                Reason: "Shell-open target не найден.",
                TargetKind: OpenTargetKindValues.Document,
                TargetIdentity: "report.pdf",
                ArtifactPath: null));

        Assert.Null(result.ArtifactPath);

        string[] eventLines = File.ReadAllLines(options.EventsPath);
        Assert.Single(eventLines);
        Assert.Contains("\"event_name\":\"open_target.runtime.completed\"", eventLines[0], StringComparison.Ordinal);
        Assert.Contains("\"failure_stage\":\"artifact_write\"", eventLines[0], StringComparison.Ordinal);
        Assert.Contains("\"exception_type\":", eventLines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void MaterializePreservesFactualResultWhenRuntimeEventWriteFails()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateOptions(root, "run-open-target-audit-failure");
        AuditLog auditLog = new(options, TimeProvider.System);
        FakeTimeProvider timeProvider = new(new DateTimeOffset(2026, 4, 8, 13, 10, 0, TimeSpan.Zero));
        OpenTargetResultMaterializer materializer = new(auditLog, options, timeProvider);

        File.Delete(options.EventsPath);
        Directory.CreateDirectory(options.EventsPath);

        OpenTargetResult result = materializer.Materialize(
            new OpenTargetResult(
                Status: OpenTargetStatusValues.Done,
                Decision: OpenTargetStatusValues.Done,
                ResultMode: OpenTargetResultModeValues.TargetOpenRequested,
                TargetKind: OpenTargetKindValues.Url,
                UriScheme: "https",
                AcceptedAtUtc: new DateTimeOffset(2026, 4, 8, 13, 9, 0, TimeSpan.Zero),
                ArtifactPath: null));

        Assert.Equal(OpenTargetStatusValues.Done, result.Status);
        Assert.Equal(OpenTargetResultModeValues.TargetOpenRequested, result.ResultMode);
        Assert.NotNull(result.ArtifactPath);
        Assert.True(File.Exists(result.ArtifactPath));
        Assert.True(Directory.Exists(options.EventsPath));
    }

    private static AuditLogOptions CreateOptions(string root, string runId) =>
        new(
            ContentRootPath: root,
            EnvironmentName: "Tests",
            RunId: runId,
            DiagnosticsRoot: Path.Combine(root, "artifacts", "diagnostics"),
            RunDirectory: Path.Combine(root, "artifacts", "diagnostics", runId),
            EventsPath: Path.Combine(root, "artifacts", "diagnostics", runId, "events.jsonl"),
            SummaryPath: Path.Combine(root, "artifacts", "diagnostics", runId, "summary.md"));

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FakeTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
