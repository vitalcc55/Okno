using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using WinBridge.Runtime;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Guards;
using WinBridge.Runtime.Session;
using WinBridge.Runtime.Tooling;
using WinBridge.Runtime.Waiting;
using WinBridge.Runtime.Windows.Capture;
using WinBridge.Runtime.Windows.Display;
using WinBridge.Runtime.Windows.Input;
using WinBridge.Runtime.Windows.Launch;
using WinBridge.Runtime.Windows.Shell;
using WinBridge.Runtime.Windows.UIA;
using WinBridge.Server.ComputerUse;
using WinBridge.Server.Tools;

string toolSurfaceProfile = global::WinBridge.Server.ToolSurfaceProfileResolver.Resolve(args);
if (TryRunExportMode(args, toolSurfaceProfile))
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
IReadOnlyList<McpServerTool> computerUseWinTools = ComputerUseWinToolRegistration.Create(
    () => hostServices?.GetRequiredService<ComputerUseWinTools>()
        ?? throw new InvalidOperationException("ComputerUseWinTools service is not available yet."));
builder.Logging.ClearProviders();
builder.Services.AddWinBridgeRuntime(builder.Environment.ContentRootPath, builder.Environment.EnvironmentName);
builder.Services.AddWinBridgeRuntimeWindowsUia(builder.Environment.ContentRootPath, builder.Environment.EnvironmentName);
builder.Services.AddSingleton(static services =>
    ComputerUseWinOptions.Resolve(services.GetRequiredService<IHostEnvironment>().ContentRootPath));
builder.Services.AddSingleton<ComputerUseWinApprovalStore>();
builder.Services.AddSingleton<ComputerUseWinStateStore>();
builder.Services.AddSingleton<ComputerUseWinPlaybookProvider>();
builder.Services.AddSingleton<AdminTools>();
builder.Services.AddSingleton(static services => new WindowTools(
    services.GetRequiredService<AuditLog>(),
    services.GetRequiredService<ISessionManager>(),
    services.GetRequiredService<IWindowManager>(),
    services.GetRequiredService<ICaptureService>(),
    services.GetRequiredService<IMonitorManager>(),
    services.GetRequiredService<IWindowActivationService>(),
    services.GetRequiredService<IWindowTargetResolver>(),
    services.GetRequiredService<IUiAutomationService>(),
    services.GetRequiredService<IWaitService>(),
    services.GetRequiredService<WaitResultMaterializer>(),
    services.GetRequiredService<IToolExecutionGate>(),
    services.GetRequiredService<IInputService>(),
    services.GetRequiredService<IProcessLaunchService>(),
    services.GetRequiredService<IOpenTargetService>()));
builder.Services.AddSingleton<ComputerUseWinTools>();

IMcpServerBuilder serverBuilder = builder.Services
    .AddMcpServer()
    .WithStdioServerTransport();

if (string.Equals(toolSurfaceProfile, ToolSurfaceProfileValues.ComputerUseWin, StringComparison.Ordinal))
{
    serverBuilder.WithTools(computerUseWinTools);
}
else
{
    serverBuilder
        .WithTools([launchProcessTool, openTargetTool, inputTool])
        .WithToolsFromAssembly(typeof(Program).Assembly);
}

using IHost host = builder.Build();
hostServices = host.Services;
await host.RunAsync();

static bool TryRunExportMode(string[] args, string toolSurfaceProfile)
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
        ToolContractExporter.ExportJson(Path.GetFullPath(jsonPath), toolSurfaceProfile);
    }

    if (markdownPath is not null)
    {
        ToolContractExporter.ExportMarkdown(Path.GetFullPath(markdownPath), toolSurfaceProfile);
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
