using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using WinBridge.Runtime;
using WinBridge.Runtime.Tooling;
using WinBridge.Runtime.Windows.UIA;
using WinBridge.Server.Tools;

if (TryRunExportMode(args))
{
    return;
}

global::WinBridge.Server.DpiAwarenessBootstrap.EnsurePerMonitorAware();

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
IServiceProvider? hostServices = null;
McpServerTool launchProcessTool = WindowsLaunchProcessToolRegistration.Create(
    () => hostServices?.GetRequiredService<WindowTools>()
        ?? throw new InvalidOperationException("WindowTools service is not available yet."));
McpServerTool openTargetTool = WindowsOpenTargetToolRegistration.Create(
    () => hostServices?.GetRequiredService<WindowTools>()
        ?? throw new InvalidOperationException("WindowTools service is not available yet."));
McpServerTool inputTool = WindowsInputToolRegistration.Create(
    () => hostServices?.GetRequiredService<WindowTools>()
        ?? throw new InvalidOperationException("WindowTools service is not available yet."));
builder.Logging.ClearProviders();
builder.Services.AddWinBridgeRuntime(builder.Environment.ContentRootPath, builder.Environment.EnvironmentName);
builder.Services.AddWinBridgeRuntimeWindowsUia(builder.Environment.ContentRootPath, builder.Environment.EnvironmentName);
builder.Services.AddSingleton<AdminTools>();
builder.Services.AddSingleton<WindowTools>();
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools([launchProcessTool, openTargetTool, inputTool])
    .WithToolsFromAssembly(typeof(Program).Assembly);

using IHost host = builder.Build();
hostServices = host.Services;
await host.RunAsync();

static bool TryRunExportMode(string[] args)
{
    string? jsonPath = null;
    string? markdownPath = null;

    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--export-tool-contract-json":
                jsonPath = ReadValue(args, ref i, "--export-tool-contract-json");
                break;
            case "--export-tool-contract-markdown":
                markdownPath = ReadValue(args, ref i, "--export-tool-contract-markdown");
                break;
        }
    }

    if (jsonPath is null && markdownPath is null)
    {
        return false;
    }

    if (jsonPath is not null)
    {
        ToolContractExporter.ExportJson(Path.GetFullPath(jsonPath));
    }

    if (markdownPath is not null)
    {
        ToolContractExporter.ExportMarkdown(Path.GetFullPath(markdownPath));
    }

    return true;
}

static string ReadValue(string[] args, ref int index, string optionName)
{
    if (index + 1 >= args.Length)
    {
        throw new ArgumentException($"Для аргумента '{optionName}' требуется путь назначения.");
    }

    index++;
    return args[index];
}
