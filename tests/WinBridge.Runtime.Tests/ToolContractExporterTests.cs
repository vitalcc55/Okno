using System.Text.Json;
using WinBridge.Runtime.Tooling;

namespace WinBridge.Runtime.Tests;

public sealed class ToolContractExporterTests
{
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
}
