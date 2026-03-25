using System.Text.Json;
using WinBridge.Runtime.Tooling;

namespace WinBridge.Runtime.Tests;

public sealed class ToolContractExporterTests
{
    [Fact]
    public void CommittedProjectInterfacesStayInSyncWithExporter()
    {
        string root = CreateTempDirectory();
        string jsonPath = Path.Combine(root, "project-interfaces.json");
        string markdownPath = Path.Combine(root, "project-interfaces.md");

        ToolContractExporter.ExportJson(jsonPath);
        ToolContractExporter.ExportMarkdown(markdownPath);

        string repoRoot = GetRepositoryRoot();
        string committedJsonPath = Path.Combine(repoRoot, "docs", "generated", "project-interfaces.json");
        string committedMarkdownPath = Path.Combine(repoRoot, "docs", "generated", "project-interfaces.md");

        Assert.Equal(
            NormalizeLineEndings(File.ReadAllText(jsonPath)),
            NormalizeLineEndings(File.ReadAllText(committedJsonPath)));
        Assert.Equal(
            NormalizeLineEndings(File.ReadAllText(markdownPath)),
            NormalizeLineEndings(File.ReadAllText(committedMarkdownPath)));
    }

    [Fact]
    public void ExportJsonContainsAllManifestTools()
    {
        string root = CreateTempDirectory();
        string jsonPath = Path.Combine(root, "project-interfaces.json");

        ToolContractExporter.ExportJson(jsonPath);

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(jsonPath));
        JsonElement tools = document.RootElement.GetProperty("tools").GetProperty("implemented_names");

        string[] exportedNames = tools.EnumerateArray()
            .Select(item => item.GetString())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .ToArray();

        Assert.Equal(ToolContractManifest.ImplementedNames, exportedNames);
    }

    [Fact]
    public void ExportMarkdownContainsImplementedAndDeferredSections()
    {
        ToolContractExportDocument document = ToolContractExporter.CreateDocument();
        string markdown = ToolContractExporter.RenderMarkdown(document);

        Assert.Contains("### Implemented now", markdown, StringComparison.Ordinal);
        Assert.Contains("### Deferred but declared", markdown, StringComparison.Ordinal);
        Assert.Contains(ToolNames.OknoHealth, markdown, StringComparison.Ordinal);
        Assert.Contains(ToolNames.WindowsCapture, markdown, StringComparison.Ordinal);
        Assert.Contains("artifacts/diagnostics/<run_id>/captures/<capture_id>.png", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportJsonIncludesCaptureArtifactPath()
    {
        ToolContractExportDocument document = ToolContractExporter.CreateDocument();

        Assert.Contains(
            "artifacts/diagnostics/<run_id>/captures/<capture_id>.png",
            document.Artifacts);
        Assert.Contains(
            "artifacts/diagnostics/<run_id>/uia/<snapshot_id>.json",
            document.Artifacts);
        Assert.Contains(
            "artifacts/diagnostics/<run_id>/wait/<wait_id>.json",
            document.Artifacts);
        Assert.Contains(
            "artifacts/diagnostics/<run_id>/wait/visual/<visual_wait_artifact>.png",
            document.Artifacts);
    }

    [Fact]
    public void ExportJsonUsesCanonicalSnakeCaseContractLiterals()
    {
        string root = CreateTempDirectory();
        string jsonPath = Path.Combine(root, "project-interfaces.json");

        ToolContractExporter.ExportJson(jsonPath);

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(jsonPath));
        JsonElement attachTool = document.RootElement
            .GetProperty("tools")
            .GetProperty("implemented")
            .EnumerateArray()
            .Single(tool => tool.GetProperty("name").GetString() == ToolNames.WindowsAttachWindow);

        Assert.Equal("implemented", attachTool.GetProperty("lifecycle").GetString());
        Assert.Equal("session_mutation", attachTool.GetProperty("safety_class").GetString());
    }

    [Fact]
    public void ExportJsonDoesNotContainRunSpecificGeneratedAtField()
    {
        string root = CreateTempDirectory();
        string jsonPath = Path.Combine(root, "project-interfaces.json");

        ToolContractExporter.ExportJson(jsonPath);

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(jsonPath));

        Assert.False(document.RootElement.TryGetProperty("generated_at_utc", out _));
    }

    [Fact]
    public void ExportJsonPublishesImplementedWaitAsOsSideEffect()
    {
        string root = CreateTempDirectory();
        string jsonPath = Path.Combine(root, "project-interfaces.json");

        ToolContractExporter.ExportJson(jsonPath);

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(jsonPath));
        JsonElement waitTool = document.RootElement
            .GetProperty("tools")
            .GetProperty("implemented")
            .EnumerateArray()
            .Single(tool => tool.GetProperty("name").GetString() == ToolNames.WindowsWait);

        Assert.Equal("implemented", waitTool.GetProperty("lifecycle").GetString());
        Assert.Equal("os_side_effect", waitTool.GetProperty("safety_class").GetString());
        Assert.True(waitTool.GetProperty("smoke_required").GetBoolean());
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string GetRepositoryRoot()
    {
        string? root = Environment.GetEnvironmentVariable("WINBRIDGE_REPO_ROOT");
        if (!string.IsNullOrWhiteSpace(root))
        {
            return root;
        }

        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "WinBridge.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Не удалось вычислить корень репозитория.");
    }

    private static string NormalizeLineEndings(string content) =>
        content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
}
