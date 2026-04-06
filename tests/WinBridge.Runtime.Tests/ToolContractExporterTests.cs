using System.Text.Json;
using WinBridge.Runtime.Contracts;
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
        Assert.Contains("| Tool | Safety class | Policy | Notes |", markdown, StringComparison.Ordinal);
        Assert.Contains("| Tool | Current outcome | Planned phase | Policy |", markdown, StringComparison.Ordinal);
        Assert.Contains(ToolNames.OknoHealth, markdown, StringComparison.Ordinal);
        Assert.Contains(ToolNames.WindowsCapture, markdown, StringComparison.Ordinal);
        Assert.Contains(ToolNames.WindowsLaunchProcess, markdown, StringComparison.Ordinal);
        Assert.Contains("policy_group=launch; risk_level=high; guard_capability=launch; supports_dry_run=true; confirmation_mode=required; redaction_class=launch_payload", markdown, StringComparison.Ordinal);
        Assert.Contains("policy_group=clipboard; risk_level=medium; guard_capability=clipboard; supports_dry_run=false; confirmation_mode=required; redaction_class=clipboard_payload", markdown, StringComparison.Ordinal);
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

    [Fact]
    public void ExportJsonPublishesDeferredExecutionPolicyMetadata()
    {
        string root = CreateTempDirectory();
        string jsonPath = Path.Combine(root, "project-interfaces.json");

        ToolContractExporter.ExportJson(jsonPath);

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(jsonPath));
        JsonElement inputTool = document.RootElement
            .GetProperty("tools")
            .GetProperty("deferred")
            .EnumerateArray()
            .Single(tool => tool.GetProperty("name").GetString() == ToolNames.WindowsInput);

        JsonElement policy = inputTool.GetProperty("execution_policy");
        Assert.Equal("input", policy.GetProperty("policy_group").GetString());
        Assert.Equal("destructive", policy.GetProperty("risk_level").GetString());
        Assert.Equal("input", policy.GetProperty("guard_capability").GetString());
        Assert.False(policy.GetProperty("supports_dry_run").GetBoolean());
        Assert.Equal("required", policy.GetProperty("confirmation_mode").GetString());
        Assert.Equal("text_payload", policy.GetProperty("redaction_class").GetString());
    }

    [Fact]
    public void ExporterPublishesLaunchProcessAsImplementedPolicyBearingTool()
    {
        ToolContractExportDocument document = ToolContractExporter.CreateDocument();
        string markdown = ToolContractExporter.RenderMarkdown(document);

        ContractToolDescriptor descriptor = Assert.Single(
            document.Tools.Implemented,
            item => item.Name == ToolNames.WindowsLaunchProcess);
        Assert.Equal("implemented", descriptor.Lifecycle);
        Assert.Equal("os_side_effect", descriptor.SafetyClass);
        ContractToolExecutionPolicyDescriptor policy = Assert.IsType<ContractToolExecutionPolicyDescriptor>(descriptor.ExecutionPolicy);
        Assert.Equal("launch", policy.PolicyGroup);
        Assert.Equal("high", policy.RiskLevel);
        Assert.Equal("launch", policy.GuardCapability);
        Assert.True(policy.SupportsDryRun);
        Assert.Equal("required", policy.ConfirmationMode);
        Assert.Equal("launch_payload", policy.RedactionClass);
        Assert.Contains(document.Tools.ImplementedNames, toolName => toolName == ToolNames.WindowsLaunchProcess);
        Assert.DoesNotContain(document.Tools.Deferred, descriptor => descriptor.Name == ToolNames.WindowsLaunchProcess);
        Assert.DoesNotContain(document.Tools.DeferredPhaseMap.Keys, toolName => toolName == ToolNames.WindowsLaunchProcess);
        Assert.Contains(ToolNames.WindowsLaunchProcess, markdown, StringComparison.Ordinal);
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
