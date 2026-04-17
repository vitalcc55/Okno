using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Tooling;

public static class ToolContractExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    public static ToolContractExportDocument CreateDocument()
        => new(
            Transport: new ToolTransportDescriptor("stdio", "2025-06-18", "src/WinBridge.Server/Program.cs", "product-ready target"),
            FutureTransports:
            [
                new FutureTransportDescriptor("http", "not implemented", "after stable stdio only"),
            ],
            Tools: new ToolContractToolSection(
                Implemented: ToolContractManifest.Implemented.Select(ContractToolDescriptorFactory.FromToolDescriptor).ToArray(),
                Deferred: ToolContractManifest.Deferred.Select(ContractToolDescriptorFactory.FromToolDescriptor).ToArray(),
                ImplementedNames: ToolContractManifest.ImplementedNames,
                SmokeRequiredNames: ToolContractManifest.SmokeRequiredToolNames,
                DeferredPhaseMap: ToolContractManifest.DeferredPhaseMap),
            Scripts:
            [
                "scripts/bootstrap.ps1",
                "scripts/build.ps1",
                "scripts/test.ps1",
                "scripts/smoke.ps1",
                "scripts/ci.ps1",
                "scripts/investigate.ps1",
                "scripts/refresh-generated-docs.ps1",
                "scripts/codex/bootstrap.ps1",
                "scripts/codex/verify.ps1",
            ],
            Artifacts:
            [
                "artifacts/diagnostics/<run_id>/events.jsonl",
                "artifacts/diagnostics/<run_id>/summary.md",
                "artifacts/diagnostics/<run_id>/captures/<capture_id>.png",
                "artifacts/diagnostics/<run_id>/launch/<launch_id>.json",
                "artifacts/diagnostics/<run_id>/uia/<snapshot_id>.json",
                "artifacts/diagnostics/<run_id>/wait/<wait_id>.json",
                "artifacts/diagnostics/<run_id>/input/input-*.json",
                "artifacts/diagnostics/<run_id>/wait/visual/<visual_wait_artifact>.png",
                "artifacts/smoke/<run_id>/report.json",
                "artifacts/smoke/<run_id>/summary.md",
            ]);

    public static void ExportJson(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(CreateDocument(), JsonOptions), new UTF8Encoding(false));
    }

    public static void ExportMarkdown(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, RenderMarkdown(CreateDocument()), new UTF8Encoding(false));
    }

    public static string RenderMarkdown(ToolContractExportDocument document)
    {
        StringBuilder builder = new();
        builder.AppendLine("# Project Interfaces Map");
        builder.AppendLine();
        builder.AppendLine("## Runtime interfaces");
        builder.AppendLine();
        builder.AppendLine("### MCP over STDIO");
        builder.AppendLine();
        builder.AppendLine("- transport: `" + document.Transport.Kind.ToUpperInvariant() + "`");
        builder.AppendLine("- protocol baseline: MCP `" + document.Transport.ProtocolVersion + "`");
        builder.AppendLine("- server entry point: `" + document.Transport.ServerEntry + "`");
        builder.AppendLine("- delivery status: `" + document.Transport.DeliveryStatus + "`");
        builder.AppendLine("- smoke-validated methods:");
        builder.AppendLine("  - `initialize`");
        builder.AppendLine("  - `tools/list`");
        builder.AppendLine("  - `tools/call`");
        builder.AppendLine();
        builder.AppendLine("### HTTP / URL server");
        builder.AppendLine();
        builder.AppendLine("- status: `" + document.FutureTransports[0].Status + "`");
        builder.AppendLine("- policy: не входит в текущий delivery baseline;");
        builder.AppendLine("- activation point: только после готового и стабилизированного `STDIO`.");
        builder.AppendLine();
        builder.AppendLine("## Tool surface");
        builder.AppendLine();
        builder.AppendLine("### Implemented now");
        builder.AppendLine();
        builder.AppendLine("| Tool | Safety class | Policy | Notes |");
        builder.AppendLine("| --- | --- | --- | --- |");

        foreach (ContractToolDescriptor descriptor in document.Tools.Implemented)
        {
            builder.AppendLine("| `" + descriptor.Name + "` | `" + descriptor.SafetyClass + "` | " + FormatExecutionPolicy(descriptor.ExecutionPolicy) + " | " + descriptor.Summary + " |");
        }

        builder.AppendLine();
        builder.AppendLine("### Deferred but declared");
        builder.AppendLine();
        builder.AppendLine("| Tool | Current outcome | Planned phase | Policy |");
        builder.AppendLine("| --- | --- | --- | --- |");

        foreach (ContractToolDescriptor descriptor in document.Tools.Deferred)
        {
            builder.AppendLine("| `" + descriptor.Name + "` | `unsupported` | " + descriptor.PlannedPhase + " | " + FormatExecutionPolicy(descriptor.ExecutionPolicy) + " |");
        }

        builder.AppendLine();
        builder.AppendLine("## Script interfaces");
        builder.AppendLine();

        foreach (string script in document.Scripts)
        {
            builder.AppendLine("- `" + script + "`");
        }

        builder.AppendLine();
        builder.AppendLine("## Artifact interfaces");
        builder.AppendLine();

        foreach (string artifact in document.Artifacts)
        {
            builder.AppendLine("- `" + artifact + "`");
        }

        return builder.ToString();
    }

    private static string FormatExecutionPolicy(ContractToolExecutionPolicyDescriptor? descriptor) =>
        descriptor is null
            ? "—"
            : "`policy_group=" + descriptor.PolicyGroup
              + "; risk_level=" + descriptor.RiskLevel
              + "; guard_capability=" + descriptor.GuardCapability
              + "; supports_dry_run=" + descriptor.SupportsDryRun.ToString().ToLowerInvariant()
              + "; confirmation_mode=" + descriptor.ConfirmationMode
              + "; redaction_class=" + descriptor.RedactionClass
              + "`";
}
