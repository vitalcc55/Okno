// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;
using WinBridge.Server.Tools;

namespace WinBridge.Server.IntegrationTests;

public sealed class BootstrapRuntimeDependencyTests
{
    [Fact]
    public void ServerRuntimeConfigRequiresWindowsDesktopAndStagesUiaWorkerAfterPublicRollout()
    {
        string serverAssemblyPath = typeof(WindowTools).Assembly.Location;
        string runtimeConfigPath = Path.ChangeExtension(serverAssemblyPath, ".runtimeconfig.json")!;
        string outputDirectory = Path.GetDirectoryName(serverAssemblyPath)!;

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

        Assert.Contains(
            frameworkNames,
            frameworkName => string.Equals(frameworkName, "Microsoft.WindowsDesktop.App", StringComparison.Ordinal));
        Assert.True(File.Exists(Path.Combine(outputDirectory, "WinBridge.Runtime.Windows.UIA.Worker.exe")));
        Assert.True(File.Exists(Path.Combine(outputDirectory, "WinBridge.Runtime.Windows.UIA.Worker.dll")));
        Assert.True(File.Exists(Path.Combine(outputDirectory, "WinBridge.Runtime.Windows.UIA.Worker.runtimeconfig.json")));
        Assert.True(File.Exists(Path.Combine(outputDirectory, "WinBridge.Runtime.Windows.UIA.Worker.deps.json")));
    }
}
