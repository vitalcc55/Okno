using WinBridge.Runtime.Tooling;

namespace WinBridge.Runtime.Tests;

public sealed class WindowsInputPackageDSourceOfTruthTests
{
    [Fact]
    public void PackageDPromotionIsConsistentAcrossInteropAndManifestSources()
    {
        string repoRoot = GetRepositoryRoot();
        string windowsInputPlan = File.ReadAllText(Path.Combine(repoRoot, "docs", "exec-plans", "active", "windows-input.md"));
        string interopPlan = File.ReadAllText(Path.Combine(repoRoot, "docs", "exec-plans", "active", "openai-computer-use-interop.md"));
        string interopArchitecture = File.ReadAllText(Path.Combine(repoRoot, "docs", "architecture", "openai-computer-use-interop.md"));
        ToolContractExportDocument exportDocument = ToolContractExporter.CreateDocument();

        Assert.Contains("Package D implemented", windowsInputPlan, StringComparison.Ordinal);
        Assert.Contains("artifacts/diagnostics/<run_id>/input/input-*.json", exportDocument.Artifacts);
        Assert.Contains("artifacts/events/materializer уже закрыты Package D", ToolContractManifest.ContractNotes, StringComparison.Ordinal);
        Assert.Contains("Package D observability уже landed", interopPlan, StringComparison.Ordinal);
        Assert.Contains("Package E smoke/fresh-host acceptance", interopPlan, StringComparison.Ordinal);
        Assert.Contains("input.runtime.completed", interopArchitecture, StringComparison.Ordinal);
        Assert.Contains("Package D observability", interopArchitecture, StringComparison.Ordinal);

        Assert.DoesNotContain("Package D/E для `windows.input` ещё остаются открыты", interopPlan, StringComparison.Ordinal);
        Assert.DoesNotContain("runtime artifacts/events/materializer, smoke proof и fresh-host acceptance не считаются закрытыми", interopPlan, StringComparison.Ordinal);
        Assert.DoesNotContain("Package D/E proof для input observability", interopPlan, StringComparison.Ordinal);
        Assert.DoesNotContain("artifacts/events/materializer rollout остаются отдельным follow-up", ToolContractManifest.ContractNotes, StringComparison.Ordinal);
    }

    private static string GetRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "WinBridge.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Не удалось найти repo root от test base directory.");
    }
}
