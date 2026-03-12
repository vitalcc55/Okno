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
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
