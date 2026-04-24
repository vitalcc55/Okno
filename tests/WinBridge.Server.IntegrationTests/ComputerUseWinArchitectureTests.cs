using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Session;
using WinBridge.Runtime.Tooling;
using WinBridge.Runtime.Windows.Capture;
using WinBridge.Runtime.Windows.Input;
using WinBridge.Runtime.Windows.Shell;
using WinBridge.Runtime.Windows.UIA;
using WinBridge.Server.ComputerUse;

namespace WinBridge.Server.IntegrationTests;

public sealed class ComputerUseWinArchitectureTests
{
    [Fact]
    public void BlockPolicyUsesCanonicalProcessIdentityReturnedByRuntime()
    {
        WindowDescriptor window = CreateWindow(processName: "powershell");

        bool isBlocked = ComputerUseWinTargetPolicy.TryGetBlockedReason(window, out string? reason);

        Assert.True(isBlocked);
        Assert.Contains("powershell", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("conhost.exe")]
    [InlineData("OpenConsole.exe")]
    public void BlockPolicyCoversConsoleHostFamilies(string processName)
    {
        WindowDescriptor window = CreateWindow(processName: processName);

        bool isBlocked = ComputerUseWinTargetPolicy.TryGetBlockedReason(window, out string? reason);

        Assert.True(isBlocked);
        Assert.Contains(processName, reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RiskPolicyRequiresConfirmationForRussianDestructiveLabels()
    {
        ComputerUseWinStoredElement element = new(
            Index: 1,
            ElementId: "path:0",
            Name: "Удалить",
            AutomationId: "DangerButton",
            ControlType: "button",
            Bounds: new Bounds(10, 10, 110, 40),
            HasKeyboardFocus: false,
            Actions: [ToolNames.ComputerUseWinClick]);

        bool requiresConfirmation = ComputerUseWinTargetPolicy.RequiresRiskConfirmation(element, ToolNames.ComputerUseWinClick);

        Assert.True(requiresConfirmation);
    }

    [Fact]
    public void PlaybookProviderMatchesBareRuntimeProcessName()
    {
        string root = CreateTempDirectory();
        try
        {
            string instructionsRoot = Path.Combine(root, "references", "AppInstructions");
            Directory.CreateDirectory(instructionsRoot);
            File.WriteAllLines(
                Path.Combine(instructionsRoot, "FileExplorer.md"),
                ["Открой нужную папку.", "", "Используй левую панель только после get_app_state."]);

            ComputerUseWinPlaybookProvider provider = new(
                new ComputerUseWinOptions(
                    PluginRoot: root,
                    AppInstructionsRoot: instructionsRoot,
                    ApprovalStorePath: Path.Combine(root, "AppApprovals.json")));

            IReadOnlyList<string> instructions = provider.GetInstructions("explorer");

            Assert.Equal(
                ["Открой нужную папку.", "Используй левую панель только после get_app_state."],
                instructions);
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public void PlaybookProviderRaisesUnavailableWhenPlaybookPathIsUnreadable()
    {
        string root = CreateTempDirectory();
        try
        {
            string instructionsRoot = Path.Combine(root, "references", "AppInstructions");
            Directory.CreateDirectory(instructionsRoot);
            Directory.CreateDirectory(Path.Combine(instructionsRoot, "FileExplorer.md"));

            ComputerUseWinPlaybookProvider provider = new(
                new ComputerUseWinOptions(
                    PluginRoot: root,
                    AppInstructionsRoot: instructionsRoot,
                    ApprovalStorePath: Path.Combine(root, "AppApprovals.json")));

            Assert.Throws<ComputerUseWinInstructionUnavailableException>(() => provider.GetInstructions("explorer"));
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public void PlaybookProviderRaisesUnavailableWhenMappedPlaybookAssetIsMissing()
    {
        string root = CreateTempDirectory();
        try
        {
            string instructionsRoot = Path.Combine(root, "references", "AppInstructions");
            Directory.CreateDirectory(instructionsRoot);

            ComputerUseWinPlaybookProvider provider = new(
                new ComputerUseWinOptions(
                    PluginRoot: root,
                    AppInstructionsRoot: instructionsRoot,
                    ApprovalStorePath: Path.Combine(root, "AppApprovals.json")));

            Assert.Throws<ComputerUseWinInstructionUnavailableException>(() => provider.GetInstructions("explorer"));
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public void AffordanceResolverEmitsOnlyImplementedActionsForComputerUseProfile()
    {
        UiaElementSnapshot node = new()
        {
            ElementId = "path:0",
            ControlType = "edit",
            BoundingRectangle = new Bounds(10, 20, 110, 40),
            IsEnabled = true,
            IsOffscreen = false,
        };

        IReadOnlyList<string> actions = ComputerUseWinAffordanceResolver.Resolve(node);

        Assert.Contains(ToolNames.ComputerUseWinClick, actions);
        Assert.DoesNotContain(ToolNames.ComputerUseWinTypeText, actions);
    }

    [Fact]
    public void ComputerUseWinProfilePublishesOnlyThreeImplementedTools()
    {
        var tools = ComputerUseWinToolRegistration.Create(static () => null!);
        ToolContractProfile profile = ToolContractManifest.GetProfile(ToolSurfaceProfileValues.ComputerUseWin);

        string[] publishedToolNames = tools
            .Select(tool => tool.ProtocolTool.Name)
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray();
        string[] factoryMethodNames = typeof(ComputerUseWinToolRegistration)
            .GetMethods(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly)
            .Where(method => method.Name.StartsWith("Create", StringComparison.Ordinal)
                && method.Name.EndsWith("Tool", StringComparison.Ordinal)
                && method.GetParameters().Length == 1
                && method.GetParameters()[0].ParameterType == typeof(Func<ComputerUseWinTools>))
            .Select(method => method.Name)
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            profile.ImplementedNames.OrderBy(static item => item, StringComparer.Ordinal).ToArray(),
            publishedToolNames);
        Assert.Equal(
            ["CreateClickTool", "CreateGetAppStateTool", "CreateListAppsTool"],
            factoryMethodNames);
    }

    [Fact]
    public void ComputerUseWinToolsExposeOnlyCuratedOperatorEntryPoints()
    {
        string[] callableMethodNames = typeof(ComputerUseWinTools)
            .GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly)
            .Where(method => method.ReturnType == typeof(ModelContextProtocol.Protocol.CallToolResult)
                || method.ReturnType == typeof(Task<ModelContextProtocol.Protocol.CallToolResult>))
            .Select(method => method.Name)
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ["Click", "GetAppState", "ListApps"],
            callableMethodNames);
    }

    [Fact]
    public void ComputerUseWinHandlersResolveFromServiceCollection()
    {
        using TempDirectoryScope temp = new();
        ServiceCollection services = new();

        services.AddSingleton(CreateAuditLog(temp.Root));
        services.AddSingleton<ISessionManager>(new InMemorySessionManager(TimeProvider.System, new SessionContext("computer-use-win-stage-2-service-graph")));
        services.AddSingleton<IWindowManager>(new ServiceGraphWindowManager());
        services.AddSingleton<IWindowActivationService>(new FakeWindowActivationService(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true)));
        services.AddSingleton<ICaptureService>(new NoopCaptureService());
        services.AddSingleton<IUiAutomationService>(new FakeUiAutomationService());
        services.AddSingleton<IInputService>(new FakeInputService());
        services.AddSingleton(new ComputerUseWinOptions(
            PluginRoot: temp.Root,
            AppInstructionsRoot: Path.Combine(temp.Root, "references", "AppInstructions"),
            ApprovalStorePath: Path.Combine(temp.Root, "AppApprovals.json")));
        services.AddSingleton<ComputerUseWinApprovalStore>();
        services.AddSingleton<ComputerUseWinAppDiscoveryService>();
        services.AddSingleton<IComputerUseWinInstructionProvider, EmptyInstructionProvider>();
        services.AddSingleton(static provider => new ComputerUseWinAppStateObserver(
            provider.GetRequiredService<ICaptureService>(),
            provider.GetRequiredService<IUiAutomationService>(),
            provider.GetRequiredService<IComputerUseWinInstructionProvider>()));
        services.AddSingleton(static provider => new ComputerUseWinClickExecutionCoordinator(
            provider.GetRequiredService<IWindowActivationService>(),
            new ComputerUseWinClickTargetResolver(provider.GetRequiredService<IUiAutomationService>()),
            provider.GetRequiredService<IInputService>()));
        services.AddSingleton<ComputerUseWinStateStore>();
        services.AddSingleton<ComputerUseWinStoredStateResolver>();
        services.AddSingleton<ComputerUseWinListAppsHandler>();
        services.AddSingleton<ComputerUseWinGetAppStateHandler>();
        services.AddSingleton<ComputerUseWinClickHandler>();
        services.AddSingleton<ComputerUseWinTools>();

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.IsType<ComputerUseWinListAppsHandler>(provider.GetRequiredService<ComputerUseWinListAppsHandler>());
        Assert.IsType<ComputerUseWinGetAppStateHandler>(provider.GetRequiredService<ComputerUseWinGetAppStateHandler>());
        Assert.IsType<ComputerUseWinClickHandler>(provider.GetRequiredService<ComputerUseWinClickHandler>());
        Assert.IsType<ComputerUseWinTools>(provider.GetRequiredService<ComputerUseWinTools>());
    }

    [Fact]
    public void ComputerUseWinManualSchemasRelyOnJsonSchema202012DefaultWithoutExplicitSchemaKeyword()
    {
        var tools = ComputerUseWinToolRegistration.Create(static () => null!);

        JsonElement getAppStateSchema = tools.Single(tool => tool.ProtocolTool.Name == ToolNames.ComputerUseWinGetAppState).ProtocolTool.InputSchema;
        JsonElement clickSchema = tools.Single(tool => tool.ProtocolTool.Name == ToolNames.ComputerUseWinClick).ProtocolTool.InputSchema;

        Assert.False(getAppStateSchema.TryGetProperty("$schema", out _));
        Assert.False(clickSchema.TryGetProperty("$schema", out _));
    }

    [Fact]
    public void ProgramUsesTypedComputerUseWinToolCatalogInsteadOfHostServicesClosure()
    {
        string program = File.ReadAllText(ResolveRepoPath(@"src\WinBridge.Server\Program.cs"));

        Assert.DoesNotContain(
            "ComputerUseWinToolRegistration.Create(\r\n    () => hostServices?.GetRequiredService<ComputerUseWinTools>()",
            program,
            StringComparison.Ordinal);
        Assert.Contains(
            "ComputerUseWinRegisteredTools computerUseWinTools = new();",
            program,
            StringComparison.Ordinal);
        Assert.Contains(
            "computerUseWinTools.BindToolHost(host.Services.GetRequiredService<ComputerUseWinTools>());",
            program,
            StringComparison.Ordinal);
        Assert.Contains(
            "serverBuilder.WithTools<ComputerUseWinRegisteredTools>(computerUseWinTools);",
            program,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ToolRequestBinderTreatsOmittedArgumentsAsEmptyRequestObject()
    {
        bool success = ToolRequestBinder.TryBind(
            arguments: null,
            fallbackRequest: new ComputerUseWinGetAppStateRequest(),
            out ComputerUseWinGetAppStateRequest request,
            out string? reason);

        Assert.True(success);
        Assert.Null(reason);
        Assert.Equal(new ComputerUseWinGetAppStateRequest(), request);
    }

    [Fact]
    public void ToolRequestBinderRejectsUnknownPropertiesForComputerUseRequests()
    {
        using JsonDocument document = JsonDocument.Parse("""{"unexpected":true}""");
        Dictionary<string, JsonElement> arguments = new(StringComparer.Ordinal)
        {
            ["unexpected"] = document.RootElement.GetProperty("unexpected").Clone(),
        };

        bool success = ToolRequestBinder.TryBind(
            arguments,
            fallbackRequest: new ComputerUseWinGetAppStateRequest(),
            out ComputerUseWinGetAppStateRequest _,
            out string? reason);

        Assert.False(success);
        Assert.Contains("unexpected", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToolRequestBinderRejectsSchemaInvalidRangeForComputerUseRequests()
    {
        using JsonDocument document = JsonDocument.Parse("""{"maxNodes":2048}""");
        Dictionary<string, JsonElement> arguments = new(StringComparer.Ordinal)
        {
            ["maxNodes"] = document.RootElement.GetProperty("maxNodes").Clone(),
        };

        bool success = ToolRequestBinder.TryBind(
            arguments,
            fallbackRequest: new ComputerUseWinGetAppStateRequest(),
            out ComputerUseWinGetAppStateRequest request,
            out string? reason,
            static value => ComputerUseWinRequestContractValidator.Validate(value));

        Assert.False(success);
        Assert.Equal(new ComputerUseWinGetAppStateRequest(), request);
        Assert.Contains("1024", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolRequestBinderRejectsNestedAdditionalPropertiesForComputerUseClickPoint()
    {
        using JsonDocument document = JsonDocument.Parse("""{"stateToken":"token-1","point":{"x":10,"y":20,"extra":true}}""");
        Dictionary<string, JsonElement> arguments = new(StringComparer.Ordinal)
        {
            ["stateToken"] = document.RootElement.GetProperty("stateToken").Clone(),
            ["point"] = document.RootElement.GetProperty("point").Clone(),
        };

        bool success = ToolRequestBinder.TryBind(
            arguments,
            fallbackRequest: new ComputerUseWinClickRequest(),
            out ComputerUseWinClickRequest request,
            out string? reason,
            static value => ComputerUseWinRequestContractValidator.Validate(value));

        Assert.False(success);
        Assert.Equal(new ComputerUseWinClickRequest(), request);
        Assert.Contains("point", reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("extra", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToolRequestBinderRejectsUnsupportedCoordinateSpaceForComputerUseClick()
    {
        using JsonDocument document = JsonDocument.Parse("""{"stateToken":"token-1","point":{"x":10,"y":20},"coordinateSpace":"bogus","confirm":true}""");
        Dictionary<string, JsonElement> arguments = new(StringComparer.Ordinal)
        {
            ["stateToken"] = document.RootElement.GetProperty("stateToken").Clone(),
            ["point"] = document.RootElement.GetProperty("point").Clone(),
            ["coordinateSpace"] = document.RootElement.GetProperty("coordinateSpace").Clone(),
            ["confirm"] = document.RootElement.GetProperty("confirm").Clone(),
        };

        bool success = ToolRequestBinder.TryBind(
            arguments,
            fallbackRequest: new ComputerUseWinClickRequest(),
            out ComputerUseWinClickRequest request,
            out string? reason,
            static value => ComputerUseWinRequestContractValidator.Validate(value));

        Assert.False(success);
        Assert.Equal(new ComputerUseWinClickRequest(), request);
        Assert.Contains("coordinateSpace", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToolRequestBinderRejectsWhitespaceCoordinateSpaceForComputerUseClick()
    {
        using JsonDocument document = JsonDocument.Parse("""{"stateToken":"token-1","point":{"x":10,"y":20},"coordinateSpace":"   ","confirm":true}""");
        Dictionary<string, JsonElement> arguments = new(StringComparer.Ordinal)
        {
            ["stateToken"] = document.RootElement.GetProperty("stateToken").Clone(),
            ["point"] = document.RootElement.GetProperty("point").Clone(),
            ["coordinateSpace"] = document.RootElement.GetProperty("coordinateSpace").Clone(),
            ["confirm"] = document.RootElement.GetProperty("confirm").Clone(),
        };

        bool success = ToolRequestBinder.TryBind(
            arguments,
            fallbackRequest: new ComputerUseWinClickRequest(),
            out ComputerUseWinClickRequest request,
            out string? reason,
            static value => ComputerUseWinRequestContractValidator.Validate(value));

        Assert.False(success);
        Assert.Equal(new ComputerUseWinClickRequest(), request);
        Assert.Contains("coordinateSpace", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToolRequestBinderRejectsUnsupportedButtonForComputerUseClick()
    {
        using JsonDocument document = JsonDocument.Parse("""{"stateToken":"token-1","elementIndex":1,"button":"middle","confirm":true}""");
        Dictionary<string, JsonElement> arguments = new(StringComparer.Ordinal)
        {
            ["stateToken"] = document.RootElement.GetProperty("stateToken").Clone(),
            ["elementIndex"] = document.RootElement.GetProperty("elementIndex").Clone(),
            ["button"] = document.RootElement.GetProperty("button").Clone(),
            ["confirm"] = document.RootElement.GetProperty("confirm").Clone(),
        };

        bool success = ToolRequestBinder.TryBind(
            arguments,
            fallbackRequest: new ComputerUseWinClickRequest(),
            out ComputerUseWinClickRequest request,
            out string? reason,
            static value => ComputerUseWinRequestContractValidator.Validate(value));

        Assert.False(success);
        Assert.Equal(new ComputerUseWinClickRequest(), request);
        Assert.Contains("button", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToolRequestBinderRejectsWhitespaceButtonForComputerUseClick()
    {
        using JsonDocument document = JsonDocument.Parse("""{"stateToken":"token-1","elementIndex":1,"button":"   ","confirm":true}""");
        Dictionary<string, JsonElement> arguments = new(StringComparer.Ordinal)
        {
            ["stateToken"] = document.RootElement.GetProperty("stateToken").Clone(),
            ["elementIndex"] = document.RootElement.GetProperty("elementIndex").Clone(),
            ["button"] = document.RootElement.GetProperty("button").Clone(),
            ["confirm"] = document.RootElement.GetProperty("confirm").Clone(),
        };

        bool success = ToolRequestBinder.TryBind(
            arguments,
            fallbackRequest: new ComputerUseWinClickRequest(),
            out ComputerUseWinClickRequest request,
            out string? reason,
            static value => ComputerUseWinRequestContractValidator.Validate(value));

        Assert.False(success);
        Assert.Equal(new ComputerUseWinClickRequest(), request);
        Assert.Contains("button", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToolRequestBinderRejectsMissingStateTokenForComputerUseClick()
    {
        using JsonDocument document = JsonDocument.Parse("""{"elementIndex":1,"confirm":true}""");
        Dictionary<string, JsonElement> arguments = new(StringComparer.Ordinal)
        {
            ["elementIndex"] = document.RootElement.GetProperty("elementIndex").Clone(),
            ["confirm"] = document.RootElement.GetProperty("confirm").Clone(),
        };

        bool success = ToolRequestBinder.TryBind(
            arguments,
            fallbackRequest: new ComputerUseWinClickRequest(),
            out ComputerUseWinClickRequest request,
            out string? reason,
            static value => ComputerUseWinRequestContractValidator.Validate(value));

        Assert.False(success);
        Assert.Equal(new ComputerUseWinClickRequest(), request);
        Assert.Contains("stateToken", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToolRequestBinderRejectsMissingSelectorForComputerUseClick()
    {
        using JsonDocument document = JsonDocument.Parse("""{"stateToken":"token-1","confirm":true}""");
        Dictionary<string, JsonElement> arguments = new(StringComparer.Ordinal)
        {
            ["stateToken"] = document.RootElement.GetProperty("stateToken").Clone(),
            ["confirm"] = document.RootElement.GetProperty("confirm").Clone(),
        };

        bool success = ToolRequestBinder.TryBind(
            arguments,
            fallbackRequest: new ComputerUseWinClickRequest(),
            out ComputerUseWinClickRequest request,
            out string? reason,
            static value => ComputerUseWinRequestContractValidator.Validate(value));

        Assert.False(success);
        Assert.Equal(new ComputerUseWinClickRequest(), request);
        Assert.Contains("elementIndex", reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("point", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToolRequestBinderRejectsWhitespaceStateTokenForComputerUseClick()
    {
        using JsonDocument document = JsonDocument.Parse("""{"stateToken":"   ","elementIndex":1,"confirm":true}""");
        Dictionary<string, JsonElement> arguments = new(StringComparer.Ordinal)
        {
            ["stateToken"] = document.RootElement.GetProperty("stateToken").Clone(),
            ["elementIndex"] = document.RootElement.GetProperty("elementIndex").Clone(),
            ["confirm"] = document.RootElement.GetProperty("confirm").Clone(),
        };

        bool success = ToolRequestBinder.TryBind(
            arguments,
            fallbackRequest: new ComputerUseWinClickRequest(),
            out ComputerUseWinClickRequest request,
            out string? reason,
            static value => ComputerUseWinRequestContractValidator.Validate(value));

        Assert.False(success);
        Assert.Equal(new ComputerUseWinClickRequest(), request);
        Assert.Contains("stateToken", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToolRequestBinderRejectsConflictingSelectorsForGetAppState()
    {
        using JsonDocument document = JsonDocument.Parse("""{"windowId":"cw_explorer_123","hwnd":123}""");
        Dictionary<string, JsonElement> arguments = new(StringComparer.Ordinal)
        {
            ["windowId"] = document.RootElement.GetProperty("windowId").Clone(),
            ["hwnd"] = document.RootElement.GetProperty("hwnd").Clone(),
        };

        bool success = ToolRequestBinder.TryBind(
            arguments,
            fallbackRequest: new ComputerUseWinGetAppStateRequest(),
            out ComputerUseWinGetAppStateRequest request,
            out string? reason,
            static value => ComputerUseWinRequestContractValidator.Validate(value));

        Assert.False(success);
        Assert.Equal(new ComputerUseWinGetAppStateRequest(), request);
        Assert.Contains("windowId", reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hwnd", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToolRequestBinderRejectsWhitespaceWindowIdWhenHwndIsPresent()
    {
        using JsonDocument document = JsonDocument.Parse("""{"windowId":"   ","hwnd":123}""");
        Dictionary<string, JsonElement> arguments = new(StringComparer.Ordinal)
        {
            ["windowId"] = document.RootElement.GetProperty("windowId").Clone(),
            ["hwnd"] = document.RootElement.GetProperty("hwnd").Clone(),
        };

        bool success = ToolRequestBinder.TryBind(
            arguments,
            fallbackRequest: new ComputerUseWinGetAppStateRequest(),
            out ComputerUseWinGetAppStateRequest request,
            out string? reason,
            static value => ComputerUseWinRequestContractValidator.Validate(value));

        Assert.False(success);
        Assert.Equal(new ComputerUseWinGetAppStateRequest(), request);
        Assert.Contains("windowId", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToolRequestBinderRejectsConflictingSelectorsForComputerUseClick()
    {
        using JsonDocument document = JsonDocument.Parse("""{"stateToken":"token-1","elementIndex":1,"point":{"x":10,"y":20},"confirm":true}""");
        Dictionary<string, JsonElement> arguments = new(StringComparer.Ordinal)
        {
            ["stateToken"] = document.RootElement.GetProperty("stateToken").Clone(),
            ["elementIndex"] = document.RootElement.GetProperty("elementIndex").Clone(),
            ["point"] = document.RootElement.GetProperty("point").Clone(),
            ["confirm"] = document.RootElement.GetProperty("confirm").Clone(),
        };

        bool success = ToolRequestBinder.TryBind(
            arguments,
            fallbackRequest: new ComputerUseWinClickRequest(),
            out ComputerUseWinClickRequest request,
            out string? reason,
            static value => ComputerUseWinRequestContractValidator.Validate(value));

        Assert.False(success);
        Assert.Equal(new ComputerUseWinClickRequest(), request);
        Assert.Contains("elementIndex", reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("point", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StableAppIdentityRequiresCanonicalProcessName()
    {
        WindowDescriptor unstableWindow = CreateWindow(processName: null);

        bool success = ComputerUseWinAppIdentity.TryCreateStableAppId(unstableWindow, out string? appId);

        Assert.False(success);
        Assert.Null(appId);
    }

    [Fact]
    public void GetAppStateIdentityProofFailureUsesRetriableFailedPayload()
    {
        ComputerUseWinGetAppStateResult payload = ComputerUseWinGetAppStateFinalizer.CreateIdentityProofFailurePayload(
            "Computer Use for Windows не смог подтвердить стабильную process identity окна.");

        Assert.Equal(ComputerUseWinStatusValues.Failed, payload.Status);
        Assert.Equal(ComputerUseWinFailureCodeValues.IdentityProofUnavailable, payload.FailureCode);
        Assert.False(payload.ApprovalRequired);
    }

    [Fact]
    public void GetAppStateTargetResolverPreservesIdentityProofUnavailableForAttachedFallback()
    {
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-target-resolution-tests"));
        sessionManager.Attach(CreateWindow(processName: "explorer"), "computer-use-win");

        WindowDescriptor liveWindowWithoutStableIdentity = CreateWindow(
            processName: null,
            processId: null,
            threadId: null,
            className: null);

        ComputerUseWinGetAppStateTargetResolution resolution = ComputerUseWinGetAppStateTargetResolver.Resolve(
            [liveWindowWithoutStableIdentity],
            [],
            sessionManager,
            windowId: null,
            hwnd: null);

        Assert.False(resolution.IsSuccess);
        Assert.Equal(ComputerUseWinFailureCodeValues.IdentityProofUnavailable, resolution.FailureCode);
    }

    [Fact]
    public void ComputerUseWinClickToolSchemaPublishesOnlyAllowedButtonAndCoordinateSpaceValues()
    {
        var tools = ComputerUseWinToolRegistration.Create(static () => null!);
        var clickTool = tools.Single(tool => string.Equals(tool.ProtocolTool.Name, ToolNames.ComputerUseWinClick, StringComparison.Ordinal));
        JsonElement inputSchema = clickTool.ProtocolTool.InputSchema;
        JsonElement properties = inputSchema.GetProperty("properties");

        JsonElement button = properties.GetProperty("button");
        Assert.Equal("string", button.GetProperty("type")[0].GetString());
        Assert.Equal("null", button.GetProperty("type")[1].GetString());
        Assert.Equal(
            [InputButtonValues.Left, InputButtonValues.Right],
            button.GetProperty("enum").EnumerateArray().Select(item => item.GetString()).Where(static item => item is not null).Cast<string>().ToArray());

        JsonElement coordinateSpace = properties.GetProperty("coordinateSpace");
        Assert.Equal("string", coordinateSpace.GetProperty("type")[0].GetString());
        Assert.Equal("null", coordinateSpace.GetProperty("type")[1].GetString());
        Assert.Equal(
            [InputCoordinateSpaceValues.Screen, InputCoordinateSpaceValues.CapturePixels],
            coordinateSpace.GetProperty("enum").EnumerateArray().Select(item => item.GetString()).Where(static item => item is not null).Cast<string>().ToArray());
    }

    [Fact]
    public void ComputerUseWinClickToolSchemaRequiresStateTokenAndExactlyOneSelector()
    {
        var tools = ComputerUseWinToolRegistration.Create(static () => null!);
        var clickTool = tools.Single(tool => string.Equals(tool.ProtocolTool.Name, ToolNames.ComputerUseWinClick, StringComparison.Ordinal));
        JsonElement inputSchema = clickTool.ProtocolTool.InputSchema;

        string[] required = inputSchema.GetProperty("required").EnumerateArray()
            .Select(item => item.GetString())
            .Where(static item => item is not null)
            .Cast<string>()
            .ToArray();
        Assert.Equal(["stateToken"], required);

        JsonElement[] selectorModes = [.. inputSchema.GetProperty("oneOf").EnumerateArray()];
        Assert.Equal(2, selectorModes.Length);
        Assert.Contains(selectorModes, mode => mode.GetProperty("required").EnumerateArray().Any(item => item.GetString() == "elementIndex"));
        Assert.Contains(selectorModes, mode => mode.GetProperty("required").EnumerateArray().Any(item => item.GetString() == "point"));
    }

    [Fact]
    public void ComputerUseWinClickToolSchemaRejectsWhitespaceOnlyStateToken()
    {
        var tools = ComputerUseWinToolRegistration.Create(static () => null!);
        var clickTool = tools.Single(tool => string.Equals(tool.ProtocolTool.Name, ToolNames.ComputerUseWinClick, StringComparison.Ordinal));
        JsonElement inputSchema = clickTool.ProtocolTool.InputSchema;

        Assert.Equal(@".*\S.*", inputSchema.GetProperty("properties").GetProperty("stateToken").GetProperty("pattern").GetString());
    }

    [Fact]
    public void ComputerUseWinGetAppStateToolSchemaRejectsConflictingSelectors()
    {
        var tools = ComputerUseWinToolRegistration.Create(static () => null!);
        var getAppStateTool = tools.Single(tool => string.Equals(tool.ProtocolTool.Name, ToolNames.ComputerUseWinGetAppState, StringComparison.Ordinal));
        JsonElement inputSchema = getAppStateTool.ProtocolTool.InputSchema;

        string[] notRequired = inputSchema.GetProperty("not").GetProperty("required").EnumerateArray()
            .Select(item => item.GetString())
            .Where(static item => item is not null)
            .Cast<string>()
            .ToArray();
        Assert.Equal(["windowId", "hwnd"], notRequired);
    }

    [Fact]
    public void ComputerUseWinGetAppStateToolSchemaRejectsWhitespaceOnlyWindowId()
    {
        var tools = ComputerUseWinToolRegistration.Create(static () => null!);
        var getAppStateTool = tools.Single(tool => string.Equals(tool.ProtocolTool.Name, ToolNames.ComputerUseWinGetAppState, StringComparison.Ordinal));
        JsonElement inputSchema = getAppStateTool.ProtocolTool.InputSchema;

        Assert.Equal(@".*\S.*", inputSchema.GetProperty("properties").GetProperty("windowId").GetProperty("pattern").GetString());
    }

    [Fact]
    public void ToolRequestBinderTreatsOmittedArgumentsAsEmptyRequestForWindowInputDto()
    {
        bool success = ToolRequestBinder.TryBind(
            arguments: null,
            fallbackRequest: new InputRequest(),
            out InputRequest request,
            out string? reason);

        Assert.True(success);
        Assert.Null(reason);
        Assert.Empty(request.Actions);
    }

    [Fact]
    public void ToolRequestBinderPreservesJsonExtensionDataPatternForWindowInputDto()
    {
        using JsonDocument document = JsonDocument.Parse("""{"actions":[],"unexpected":true}""");
        Dictionary<string, JsonElement> arguments = new(StringComparer.Ordinal)
        {
            ["actions"] = document.RootElement.GetProperty("actions").Clone(),
            ["unexpected"] = document.RootElement.GetProperty("unexpected").Clone(),
        };

        bool success = ToolRequestBinder.TryBind(
            arguments,
            fallbackRequest: new InputRequest(),
            out InputRequest request,
            out string? reason);

        Assert.True(success);
        Assert.Null(reason);
        Assert.NotNull(request.AdditionalProperties);
        Assert.True(request.AdditionalProperties!.ContainsKey("unexpected"));
    }

    [Fact]
    public void ToolSurfaceProfileResolverRejectsUnknownExplicitProfile()
    {
        string[] args = ["--tool-surface-profile", "bogus-profile"];

        Assert.Throws<ArgumentOutOfRangeException>(() => ToolSurfaceProfileResolver.Resolve(args));
    }

    [Fact]
    public void ToolSurfaceProfileResolverReturnsDefaultWindowsEngineWhenProfileIsAbsent()
    {
        Assert.Equal(ToolSurfaceProfileValues.WindowsEngine, ToolSurfaceProfileResolver.Resolve([]));
    }

    [Fact]
    public void ToolSurfaceProfileResolverRejectsExplicitBlankProfile()
    {
        string[] args = ["--tool-surface-profile", "   "];

        Assert.Throws<ArgumentOutOfRangeException>(() => ToolSurfaceProfileResolver.Resolve(args));
    }

    [Fact]
    public void StateStoreEvictsExpiredAndOldestEntries()
    {
        MutableTimeProvider timeProvider = new(new DateTimeOffset(2026, 4, 21, 12, 0, 0, TimeSpan.Zero));
        ComputerUseWinStateStore store = new(timeProvider, TimeSpan.FromSeconds(10), maxEntries: 2);

        string firstToken = store.Create(CreateStoredState(timeProvider.GetUtcNow()));
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        string secondToken = store.Create(CreateStoredState(timeProvider.GetUtcNow()));
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        string thirdToken = store.Create(CreateStoredState(timeProvider.GetUtcNow()));

        Assert.False(store.TryGet(firstToken, out _));
        Assert.True(store.TryGet(secondToken, out _));
        Assert.True(store.TryGet(thirdToken, out _));

        timeProvider.Advance(TimeSpan.FromSeconds(11));

        Assert.False(store.TryGet(secondToken, out _));
        Assert.False(store.TryGet(thirdToken, out _));
    }

    [Fact]
    public void StateStoreUsesIssuedTimeInsteadOfCaptureTimeForTtl()
    {
        MutableTimeProvider timeProvider = new(new DateTimeOffset(2026, 4, 21, 12, 0, 0, TimeSpan.Zero));
        ComputerUseWinStateStore store = new(timeProvider, TimeSpan.FromSeconds(10), maxEntries: 2);

        string token = store.Create(CreateStoredState(timeProvider.GetUtcNow().AddMinutes(-5)));

        Assert.True(store.TryGet(token, out _));

        timeProvider.Advance(TimeSpan.FromSeconds(9));
        Assert.True(store.TryGet(token, out _));

        timeProvider.Advance(TimeSpan.FromSeconds(2));
        Assert.False(store.TryGet(token, out _));
    }

    [Fact]
    public void StateStoreOverflowUsesIssuedTimeInsteadOfCaptureTime()
    {
        MutableTimeProvider timeProvider = new(new DateTimeOffset(2026, 4, 21, 12, 0, 0, TimeSpan.Zero));
        ComputerUseWinStateStore store = new(timeProvider, TimeSpan.FromSeconds(30), maxEntries: 1);

        string firstToken = store.Create(CreateStoredState(timeProvider.GetUtcNow()));
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        string secondToken = store.Create(CreateStoredState(timeProvider.GetUtcNow().AddHours(-1)));

        Assert.False(store.TryGet(firstToken, out _));
        Assert.True(store.TryGet(secondToken, out _));
    }

    [Fact]
    public void ApprovalStoreRecoversFromCorruptJsonAndRewritesAtomically()
    {
        string root = CreateTempDirectory();
        try
        {
            string storePath = Path.Combine(root, "AppApprovals.json");
            File.WriteAllText(storePath, "{not valid json");
            ComputerUseWinApprovalStore store = new(
                new ComputerUseWinOptions(
                    PluginRoot: root,
                    AppInstructionsRoot: Path.Combine(root, "references", "AppInstructions"),
                    ApprovalStorePath: storePath));

            Assert.False(store.IsApproved("explorer"));

            store.Approve("explorer");

            string json = File.ReadAllText(storePath);
            string[] values = JsonSerializer.Deserialize<string[]>(json)!;
            Assert.Contains("explorer", values, StringComparer.OrdinalIgnoreCase);
            Assert.Empty(Directory.EnumerateFiles(root, "*.tmp", SearchOption.TopDirectoryOnly));
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public void ApprovalStoreDoesNotThrowWhenPersistPathCannotBeReplaced()
    {
        string root = CreateTempDirectory();
        string unwritableStorePath = Path.Combine(root, "approval-store-as-directory");
        Directory.CreateDirectory(unwritableStorePath);

        try
        {
            ComputerUseWinApprovalStore store = new(
                new ComputerUseWinOptions(
                    PluginRoot: root,
                    AppInstructionsRoot: Path.Combine(root, "references", "AppInstructions"),
                    ApprovalStorePath: unwritableStorePath));

            store.Approve("explorer");

            Assert.True(store.IsApproved("explorer"));
            Assert.Empty(Directory.EnumerateFiles(root, "*.tmp", SearchOption.TopDirectoryOnly));
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public void ReadmeUsesPublishFlowInsteadOfRemovedComputerUseWinRepoRootHint()
    {
        string readme = File.ReadAllText(ResolveRepoPath("README.md"));

        Assert.DoesNotContain("write-computer-use-win-plugin-repo-root-hint.ps1", readme, StringComparison.Ordinal);
        Assert.Contains("publish-computer-use-win-plugin.ps1", readme, StringComparison.Ordinal);
    }

    private static ComputerUseWinStoredState CreateStoredState(DateTimeOffset capturedAtUtc) =>
        new(
            new ComputerUseWinAppSession("explorer", "cw_explorer_101", 101, "Explorer", "explorer", 1001),
            CreateWindow(processName: "explorer"),
            CaptureReference: null,
            Elements: new Dictionary<int, ComputerUseWinStoredElement>
            {
                [1] = new(
                    Index: 1,
                    ElementId: "path:0",
                    Name: "Open",
                    AutomationId: "OpenButton",
                    ControlType: "button",
                    Bounds: new Bounds(10, 10, 110, 40),
                    HasKeyboardFocus: false,
                    Actions: [ToolNames.ComputerUseWinClick]),
            },
            Observation: new ComputerUseWinObservationEnvelope(UiaSnapshotDefaults.Depth, 128),
            CapturedAtUtc: capturedAtUtc);

    private static WindowDescriptor CreateWindow(
        string? processName,
        int? processId = 1001,
        int? threadId = 2002,
        string? className = "TestWindow") =>
        new(
            Hwnd: 101,
            Title: "Test window",
            ProcessName: processName,
            ProcessId: processId,
            ThreadId: threadId,
            ClassName: className,
            Bounds: new Bounds(0, 0, 640, 480),
            IsForeground: true,
            IsVisible: true);

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string ResolveRepoPath(string relativePath)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate) || Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException($"Не удалось найти '{relativePath}' от AppContext.BaseDirectory.");
    }

    private static AuditLog CreateAuditLog(string root)
    {
        string runDirectory = Path.Combine(root, "artifacts", "diagnostics", "computer-use-win-stage-2-service-graph");
        return new AuditLog(
            new AuditLogOptions(
                ContentRootPath: root,
                EnvironmentName: "tests",
                RunId: "computer-use-win-stage-2-service-graph",
                DiagnosticsRoot: Path.Combine(root, "artifacts", "diagnostics"),
                RunDirectory: runDirectory,
                EventsPath: Path.Combine(runDirectory, "events.jsonl"),
                SummaryPath: Path.Combine(runDirectory, "summary.md")),
            TimeProvider.System);
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed class ServiceGraphWindowManager : IWindowManager
    {
        public IReadOnlyList<WindowDescriptor> ListWindows(bool includeInvisible = false) => [];

        public WindowDescriptor? FindWindow(WindowSelector selector) => null;

        public bool TryFocus(long hwnd) => false;
    }

    private sealed class NoopCaptureService : ICaptureService
    {
        public Task<CaptureResult> CaptureAsync(CaptureTarget target, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Capture не должен вызываться в DI resolution test.");
    }

    private sealed class EmptyInstructionProvider : IComputerUseWinInstructionProvider
    {
        public IReadOnlyList<string> GetInstructions(string? processName) => [];
    }

    private sealed class TempDirectoryScope : IDisposable
    {
        public TempDirectoryScope()
        {
            Root = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            DeleteDirectoryIfExists(Root);
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;

        public override DateTimeOffset GetUtcNow() => current;

        public void Advance(TimeSpan delta) => current = current.Add(delta);
    }
}
