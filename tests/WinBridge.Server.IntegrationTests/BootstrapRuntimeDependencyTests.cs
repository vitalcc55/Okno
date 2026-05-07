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
        string outputDirectory = Path.GetDirectoryName(serverAssemblyPath)!;
        string runtimeConfigPath = Path.ChangeExtension(serverAssemblyPath, ".runtimeconfig.json")!;

        using JsonDocument runtimeConfig = JsonDocument.Parse(File.ReadAllBytes(runtimeConfigPath));
        JsonElement runtimeOptions = runtimeConfig.RootElement.GetProperty("runtimeOptions");

        bool usesWindowsDesktop = (runtimeOptions.TryGetProperty("frameworks", out JsonElement frameworks)
            && frameworks.EnumerateArray().Any(IsWindowsDesktopFramework))
            || (runtimeOptions.TryGetProperty("framework", out JsonElement framework)
            && IsWindowsDesktopFramework(framework));

        Assert.True(usesWindowsDesktop);
        Assert.All<string>(
            [
                "WinBridge.Runtime.Windows.UIA.Worker.exe",
                "WinBridge.Runtime.Windows.UIA.Worker.dll",
                "WinBridge.Runtime.Windows.UIA.Worker.runtimeconfig.json",
                "WinBridge.Runtime.Windows.UIA.Worker.deps.json",
            ],
            fileName => Assert.True(File.Exists(Path.Combine(outputDirectory, fileName)), $"Missing file: {fileName}"));
        static bool IsWindowsDesktopFramework(JsonElement framework) =>
            string.Equals(framework.GetProperty("name").GetString(), "Microsoft.WindowsDesktop.App", StringComparison.Ordinal);
    }
}