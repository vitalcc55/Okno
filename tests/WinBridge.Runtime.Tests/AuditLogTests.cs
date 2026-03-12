using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;

namespace WinBridge.Runtime.Tests;

public sealed class AuditLogTests
{
    [Fact]
    public void BeginInvocationWritesStartedAndCompletedEvents()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = new(
            ContentRootPath: root,
            EnvironmentName: "Tests",
            RunId: "run-001",
            DiagnosticsRoot: Path.Combine(root, "artifacts", "diagnostics"),
            RunDirectory: Path.Combine(root, "artifacts", "diagnostics", "run-001"),
            EventsPath: Path.Combine(root, "artifacts", "diagnostics", "run-001", "events.jsonl"),
            SummaryPath: Path.Combine(root, "artifacts", "diagnostics", "run-001", "summary.md"));
        AuditLog auditLog = new(options, TimeProvider.System);
        SessionSnapshot snapshot = SessionSnapshot.CreateInitial("run-001", DateTimeOffset.UtcNow);

        using (AuditInvocationScope invocation = auditLog.BeginInvocation("okno.health", new { probe = "test" }, snapshot))
        {
            invocation.Complete("done", "Тестовый вызов завершён.");
        }

        string[] lines = File.ReadAllLines(options.EventsPath);
        Assert.Equal(2, lines.Length);
        Assert.Contains("\"event_name\":\"tool.invocation.started\"", lines[0], StringComparison.Ordinal);
        Assert.Contains("\"event_name\":\"tool.invocation.completed\"", lines[1], StringComparison.Ordinal);
        Assert.Contains("\"schema_version\":\"1.0.0\"", lines[0], StringComparison.Ordinal);

        string summary = File.ReadAllText(options.SummaryPath);
        Assert.Contains("run-001", summary, StringComparison.Ordinal);
        Assert.Contains("Тестовый вызов завершён.", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void RecordSessionAttachedDeduplicatesIdenticalStateTransitions()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = new(
            ContentRootPath: root,
            EnvironmentName: "Tests",
            RunId: "run-002",
            DiagnosticsRoot: Path.Combine(root, "artifacts", "diagnostics"),
            RunDirectory: Path.Combine(root, "artifacts", "diagnostics", "run-002"),
            EventsPath: Path.Combine(root, "artifacts", "diagnostics", "run-002", "events.jsonl"),
            SummaryPath: Path.Combine(root, "artifacts", "diagnostics", "run-002", "summary.md"));
        AuditLog auditLog = new(options, TimeProvider.System);
        WindowDescriptor window = new(
            Hwnd: 42,
            Title: "Calculator",
            ProcessName: "CalculatorApp",
            Bounds: new Bounds(0, 0, 800, 600),
            IsForeground: true,
            IsVisible: true);
        SessionSnapshot before = SessionSnapshot.CreateInitial("run-002", DateTimeOffset.UtcNow);
        SessionSnapshot after = new("window", new AttachedWindow(window, "hwnd"), DateTimeOffset.UtcNow, "run-002");

        auditLog.RecordSessionAttached(before, after);
        auditLog.RecordSessionAttached(after, after);

        string[] lines = File.ReadAllLines(options.EventsPath);
        Assert.Single(lines);

        Assert.Contains("\"event_name\":\"session.attached\"", lines[0], StringComparison.Ordinal);
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
