using System.Text.Json;
using WinBridge.Server.Tools;

namespace WinBridge.Server.IntegrationTests;

public sealed class BootstrapRuntimeDependencyTests
{
    [Fact]
    public void ServerRuntimeConfigDoesNotRequireWindowsDesktopBeforePublicUiaRollout()
    {
        string serverAssemblyPath = typeof(WindowTools).Assembly.Location;
        string runtimeConfigPath = Path.ChangeExtension(serverAssemblyPath, ".runtimeconfig.json")!;

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(runtimeConfigPath));
        JsonElement runtimeOptions = document.RootElement.GetProperty("runtimeOptions");
        List<string?> frameworkNames = [];
        if (runtimeOptions.TryGetProperty("frameworks", out JsonElement frameworks))
        {
            frameworkNames.AddRange(frameworks.EnumerateArray().Select(item => item.GetProperty("name").GetString()));
        }

        if (runtimeOptions.TryGetProperty("framework", out JsonElement framework))
        {
            frameworkNames.Add(framework.GetProperty("name").GetString());
        }

        Assert.DoesNotContain(
            frameworkNames,
            frameworkName => string.Equals(frameworkName, "Microsoft.WindowsDesktop.App", StringComparison.Ordinal));
    }
}
