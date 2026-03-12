using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;

namespace WinBridge.Runtime.Tests;

public sealed class ToolExecutionTests
{
    [Fact]
    public void RunLogsFailedOutcomeWhenCallbackThrows()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = new(
            ContentRootPath: root,
            EnvironmentName: "Tests",
            RunId: "run-004",
            DiagnosticsRoot: Path.Combine(root, "artifacts", "diagnostics"),
            RunDirectory: Path.Combine(root, "artifacts", "diagnostics", "run-004"),
            EventsPath: Path.Combine(root, "artifacts", "diagnostics", "run-004", "events.jsonl"),
            SummaryPath: Path.Combine(root, "artifacts", "diagnostics", "run-004", "summary.md"));
        AuditLog auditLog = new(options, TimeProvider.System);
        SessionSnapshot snapshot = SessionSnapshot.CreateInitial("run-004", DateTimeOffset.UtcNow);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => ToolExecution.Run<int>(
                auditLog,
                snapshot,
                "okno.health",
                null,
                _ => throw new InvalidOperationException("boom")));

        Assert.Equal("boom", exception.Message);

        string[] lines = File.ReadAllLines(options.EventsPath);
        Assert.Equal(2, lines.Length);
        Assert.Contains("\"outcome\":\"failed\"", lines[1], StringComparison.Ordinal);
        Assert.Contains("\"exception_type\":\"System.InvalidOperationException\"", lines[1], StringComparison.Ordinal);
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
