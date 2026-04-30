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
    public void RiskPolicyRequiresConfirmationForDangerousPressKeyCombos()
    {
        Assert.True(ComputerUseWinTargetPolicy.RequiresRiskConfirmation(null, ToolNames.ComputerUseWinPressKey, "alt+f4"));
        Assert.True(ComputerUseWinTargetPolicy.RequiresRiskConfirmation(null, ToolNames.ComputerUseWinPressKey, "shift+ctrl+w"));
        Assert.False(ComputerUseWinTargetPolicy.RequiresRiskConfirmation(null, ToolNames.ComputerUseWinPressKey, "tab"));
    }

    [Fact]
    public void PressKeyValidatorRejectsPrintableKeyWithoutModifier()
    {
        string? failure = ComputerUseWinRequestContractValidator.Validate(
            new ComputerUseWinPressKeyRequest(
                StateToken: "token-1",
                Key: "s",
                Repeat: 1,
                Confirm: false));

        Assert.False(string.IsNullOrWhiteSpace(failure));
    }

    [Fact]
    public void PressKeyValidatorRejectsRepeatBeyondBoundedMaximum()
    {
        string? failure = ComputerUseWinRequestContractValidator.Validate(
            new ComputerUseWinPressKeyRequest(
                StateToken: "token-1",
                Key: "ctrl+s",
                Repeat: InputActionScalarConstraints.MaximumKeypressRepeat + 1,
                Confirm: true));

        Assert.False(string.IsNullOrWhiteSpace(failure));
        Assert.Contains("repeat", failure, StringComparison.OrdinalIgnoreCase);
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
    public void AffordanceResolverPublishesTypeTextOnlyForFocusedEditableTargets()
    {
        UiaElementSnapshot unfocusedNode = new()
        {
            ElementId = "path:0",
            ControlType = "edit",
            BoundingRectangle = new Bounds(10, 20, 110, 40),
            IsEnabled = true,
            IsOffscreen = false,
            IsReadOnly = false,
            Patterns = ["value"],
        };
        UiaElementSnapshot focusedNode = unfocusedNode with
        {
            HasKeyboardFocus = true,
        };

        IReadOnlyList<string> unfocusedActions = ComputerUseWinAffordanceResolver.Resolve(unfocusedNode);
        IReadOnlyList<string> focusedActions = ComputerUseWinAffordanceResolver.Resolve(focusedNode);
        IReadOnlyList<string> documentActions = ComputerUseWinAffordanceResolver.Resolve(
            new UiaElementSnapshot
            {
                ElementId = "path:document",
                ControlType = "document",
                BoundingRectangle = new Bounds(20, 30, 180, 120),
                IsEnabled = true,
                IsOffscreen = false,
                HasKeyboardFocus = true,
            });
        IReadOnlyList<string> missingWritableProofActions = ComputerUseWinAffordanceResolver.Resolve(
            focusedNode with
            {
                IsReadOnly = null,
            });
        IReadOnlyList<string> readOnlyActions = ComputerUseWinAffordanceResolver.Resolve(
            focusedNode with
            {
                IsReadOnly = true,
            });

        Assert.Contains(ToolNames.ComputerUseWinClick, unfocusedActions);
        Assert.Contains(ToolNames.ComputerUseWinSetValue, unfocusedActions);
        Assert.DoesNotContain(ToolNames.ComputerUseWinTypeText, unfocusedActions);
        Assert.Contains(ToolNames.ComputerUseWinTypeText, focusedActions);
        Assert.Contains(
            ToolNames.ComputerUseWinScroll,
            ComputerUseWinAffordanceResolver.Resolve(
                new UiaElementSnapshot
                {
                    ElementId = "path:scroll",
                    ControlType = "list",
                    BoundingRectangle = new Bounds(12, 22, 140, 120),
                    IsEnabled = true,
                    IsOffscreen = false,
                    Patterns = ["scroll"],
                }));
        Assert.Contains(
            ToolNames.ComputerUseWinPerformSecondaryAction,
            ComputerUseWinAffordanceResolver.Resolve(
                new UiaElementSnapshot
                {
                    ElementId = "path:toggle",
                    ControlType = "check_box",
                    BoundingRectangle = new Bounds(24, 104, 244, 128),
                    IsEnabled = true,
                    IsOffscreen = false,
                    Patterns = ["toggle"],
                }));
        Assert.DoesNotContain(
            ToolNames.ComputerUseWinPerformSecondaryAction,
            ComputerUseWinAffordanceResolver.Resolve(
                new UiaElementSnapshot
                {
                    ElementId = "path:leaf-like",
                    ControlType = "tree_item",
                    BoundingRectangle = new Bounds(24, 252, 120, 276),
                    IsEnabled = true,
                    IsOffscreen = false,
                    Patterns = ["expand_collapse"],
                }));
        Assert.DoesNotContain(ToolNames.ComputerUseWinTypeText, documentActions);
        Assert.DoesNotContain(ToolNames.ComputerUseWinSetValue, missingWritableProofActions);
        Assert.DoesNotContain(ToolNames.ComputerUseWinTypeText, missingWritableProofActions);
        Assert.DoesNotContain(ToolNames.ComputerUseWinSetValue, readOnlyActions);
        Assert.DoesNotContain(ToolNames.ComputerUseWinTypeText, readOnlyActions);
    }

    [Fact]
    public void SetValueValidatorRejectsMissingElementIndex()
    {
        string? failure = ComputerUseWinRequestContractValidator.Validate(
            new ComputerUseWinSetValueRequest(
                StateToken: "token-1",
                ElementIndex: null,
                ValueKind: "text",
                TextValue: "value",
                NumberValue: null,
                Confirm: false));

        Assert.False(string.IsNullOrWhiteSpace(failure));
        Assert.Contains("elementIndex", failure, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SetValueValidatorRejectsMismatchedValueKindPayload()
    {
        string? failure = ComputerUseWinRequestContractValidator.Validate(
            new ComputerUseWinSetValueRequest(
                StateToken: "token-1",
                ElementIndex: 1,
                ValueKind: "number",
                TextValue: "value",
                NumberValue: null,
                Confirm: false));

        Assert.False(string.IsNullOrWhiteSpace(failure));
        Assert.Contains("valueKind", failure, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TypeTextValidatorAllowsWhitespaceTextWithoutElementIndex()
    {
        string? failure = ComputerUseWinRequestContractValidator.Validate(
            new ComputerUseWinTypeTextRequest(
                StateToken: "token-1",
                ElementIndex: null,
                Text: "   ",
                Confirm: false));

        Assert.Null(failure);
    }

    [Fact]
    public void TypeTextValidatorRejectsEmptyText()
    {
        string? failure = ComputerUseWinRequestContractValidator.Validate(
            new ComputerUseWinTypeTextRequest(
                StateToken: "token-1",
                ElementIndex: 1,
                Text: string.Empty,
                Confirm: false));

        Assert.False(string.IsNullOrWhiteSpace(failure));
        Assert.Contains("text", failure, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TypeTextValidatorRequiresConfirmForFocusedFallbackOptIn()
    {
        using JsonDocument document = JsonDocument.Parse("""{"stateToken":"token-1","text":"typed text","allowFocusedFallback":true,"confirm":false}""");
        Dictionary<string, JsonElement> arguments = new(StringComparer.Ordinal)
        {
            ["stateToken"] = document.RootElement.GetProperty("stateToken").Clone(),
            ["text"] = document.RootElement.GetProperty("text").Clone(),
            ["allowFocusedFallback"] = document.RootElement.GetProperty("allowFocusedFallback").Clone(),
            ["confirm"] = document.RootElement.GetProperty("confirm").Clone(),
        };

        bool success = ToolRequestBinder.TryBind(
            arguments,
            fallbackRequest: new ComputerUseWinTypeTextRequest(),
            out ComputerUseWinTypeTextRequest request,
            out string? reason,
            static value => ComputerUseWinRequestContractValidator.Validate(value));

        Assert.False(success);
        Assert.Equal(new ComputerUseWinTypeTextRequest(), request);
        Assert.Contains("confirm", reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("unmapped", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ScrollValidatorRejectsSelectorlessRequest()
    {
        string? failure = ComputerUseWinRequestContractValidator.Validate(
            new ComputerUseWinScrollRequest(
                StateToken: "token-1",
                ElementIndex: null,
                Point: null,
                CoordinateSpace: null,
                Direction: "down",
                Pages: 1,
                Confirm: false));

        Assert.False(string.IsNullOrWhiteSpace(failure));
        Assert.Contains("elementIndex", failure, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ScrollValidatorRejectsConflictingElementAndPointSelectors()
    {
        string? failure = ComputerUseWinRequestContractValidator.Validate(
            new ComputerUseWinScrollRequest(
                StateToken: "token-1",
                ElementIndex: 1,
                Point: new InputPoint(10, 20),
                CoordinateSpace: InputCoordinateSpaceValues.Screen,
                Direction: "down",
                Pages: 1,
                Confirm: true));

        Assert.False(string.IsNullOrWhiteSpace(failure));
        Assert.Contains("point", failure, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ScrollValidatorRejectsPagesAboveMaximum()
    {
        string? failure = ComputerUseWinRequestContractValidator.Validate(
            new ComputerUseWinScrollRequest(
                StateToken: "token-1",
                ElementIndex: 1,
                Direction: "down",
                Pages: 11,
                Confirm: false));

        Assert.False(string.IsNullOrWhiteSpace(failure));
        Assert.Contains("pages", failure, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("10", failure, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PerformSecondaryActionValidatorRejectsMissingElementIndex()
    {
        string? failure = ComputerUseWinRequestContractValidator.Validate(
            new ComputerUseWinPerformSecondaryActionRequest(
                StateToken: "token-1",
                ElementIndex: null,
                Confirm: false));

        Assert.False(string.IsNullOrWhiteSpace(failure));
        Assert.Contains("elementIndex", failure, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ComputerUseWinProfilePublishesImplementedDragAlongsideShippedOperatorTools()
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
            ["CreateClickTool", "CreateDragTool", "CreateGetAppStateTool", "CreateListAppsTool", "CreatePerformSecondaryActionTool", "CreatePressKeyTool", "CreateScrollTool", "CreateSetValueTool", "CreateTypeTextTool"],
            factoryMethodNames);
    }

    [Fact]
    public void ComputerUseWinDeferredWaveIsEmptyAfterDragPromotion()
    {
        var tools = ComputerUseWinToolRegistration.Create(static () => null!);
        ToolContractProfile profile = ToolContractManifest.GetProfile(ToolSurfaceProfileValues.ComputerUseWin);

        string[] deferredNames = profile.Deferred
            .Select(static descriptor => descriptor.Name)
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray();
        string[] publishedToolNames = tools
            .Select(tool => tool.ProtocolTool.Name)
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(deferredNames);
        Assert.Contains("drag", publishedToolNames);
    }

    [Fact]
    public void ComputerUseWinListAppsMetadataReflectsStatefulSelectorIssuance()
    {
        var tools = ComputerUseWinToolRegistration.Create(static () => null!);
        ToolContractProfile profile = ToolContractManifest.GetProfile(ToolSurfaceProfileValues.ComputerUseWin);
        JsonElement listAppsDescriptor = JsonSerializer.SerializeToElement(
            ToolContractExporter.CreateDocument(ToolSurfaceProfileValues.ComputerUseWin)
                .Tools
                .Implemented
                .Single(tool => tool.Name == ToolNames.ComputerUseWinListApps));
        ToolDescriptor listAppsContract = profile.Implemented.Single(tool => tool.Name == ToolNames.ComputerUseWinListApps);
        var listAppsTool = tools.Single(tool => tool.ProtocolTool.Name == ToolNames.ComputerUseWinListApps);

        Assert.Equal(ToolSafetyClass.SessionMutation, listAppsContract.SafetyClass);
        Assert.False(listAppsTool.ProtocolTool.Annotations!.ReadOnlyHint!.Value);
        Assert.True(listAppsTool.ProtocolTool.Annotations.DestructiveHint!.Value);
        Assert.False(listAppsTool.ProtocolTool.Annotations.IdempotentHint!.Value);
        Assert.Equal("session_mutation", listAppsDescriptor.GetProperty("safety_class").GetString());
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
            ["Click", "Drag", "GetAppState", "ListApps", "PerformSecondaryAction", "PressKey", "Scroll", "SetValue", "TypeText"],
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
        services.AddSingleton<IUiAutomationSetValueService>(new FakeUiAutomationSetValueService());
        services.AddSingleton<IInputService>(new FakeInputService());
        services.AddSingleton(new ComputerUseWinOptions(
            PluginRoot: temp.Root,
            AppInstructionsRoot: Path.Combine(temp.Root, "references", "AppInstructions"),
            ApprovalStorePath: Path.Combine(temp.Root, "AppApprovals.json")));
        services.AddSingleton<ComputerUseWinApprovalStore>();
        services.AddSingleton<ComputerUseWinExecutionTargetCatalog>();
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
        services.AddSingleton(static provider => new ComputerUseWinDragExecutionCoordinator(
            provider.GetRequiredService<IWindowActivationService>(),
            new ComputerUseWinDragTargetResolver(provider.GetRequiredService<IUiAutomationService>()),
            provider.GetRequiredService<IInputService>()));
        services.AddSingleton(static provider => new ComputerUseWinPressKeyExecutionCoordinator(
            provider.GetRequiredService<IWindowActivationService>(),
            provider.GetRequiredService<IInputService>()));
        services.AddSingleton(static provider => new ComputerUseWinSetValueExecutionCoordinator(
            provider.GetRequiredService<IWindowActivationService>(),
            provider.GetRequiredService<IUiAutomationService>(),
            provider.GetRequiredService<IUiAutomationSetValueService>()));
        services.AddSingleton(static provider => new ComputerUseWinTypeTextExecutionCoordinator(
            provider.GetRequiredService<IWindowActivationService>(),
            provider.GetRequiredService<IUiAutomationService>(),
            provider.GetRequiredService<IInputService>()));
        services.AddSingleton<IUiAutomationScrollService>(new FakeUiAutomationScrollService());
        services.AddSingleton(static provider => new ComputerUseWinScrollExecutionCoordinator(
            provider.GetRequiredService<IWindowActivationService>(),
            provider.GetRequiredService<IUiAutomationService>(),
            provider.GetRequiredService<IUiAutomationScrollService>(),
            provider.GetRequiredService<IInputService>()));
        services.AddSingleton<IUiAutomationSecondaryActionService>(new FakeUiAutomationSecondaryActionService());
        services.AddSingleton(static provider => new ComputerUseWinPerformSecondaryActionExecutionCoordinator(
            provider.GetRequiredService<IWindowActivationService>(),
            provider.GetRequiredService<IUiAutomationService>(),
            provider.GetRequiredService<IUiAutomationSecondaryActionService>()));
        services.AddSingleton<ComputerUseWinStateStore>();
        services.AddSingleton<ComputerUseWinStoredStateResolver>();
        services.AddSingleton<ComputerUseWinActionRequestExecutor>();
        services.AddSingleton<ComputerUseWinListAppsHandler>();
        services.AddSingleton<ComputerUseWinGetAppStateHandler>();
        services.AddSingleton<ComputerUseWinClickHandler>();
        services.AddSingleton<ComputerUseWinDragHandler>();
        services.AddSingleton<ComputerUseWinPerformSecondaryActionHandler>();
        services.AddSingleton<ComputerUseWinPressKeyHandler>();
        services.AddSingleton<ComputerUseWinScrollHandler>();
        services.AddSingleton<ComputerUseWinSetValueHandler>();
        services.AddSingleton<ComputerUseWinTypeTextHandler>();
        services.AddSingleton<ComputerUseWinTools>();

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.IsType<ComputerUseWinListAppsHandler>(provider.GetRequiredService<ComputerUseWinListAppsHandler>());
        Assert.IsType<ComputerUseWinGetAppStateHandler>(provider.GetRequiredService<ComputerUseWinGetAppStateHandler>());
        Assert.IsType<ComputerUseWinClickHandler>(provider.GetRequiredService<ComputerUseWinClickHandler>());
        Assert.IsType<ComputerUseWinDragHandler>(provider.GetRequiredService<ComputerUseWinDragHandler>());
        Assert.IsType<ComputerUseWinPerformSecondaryActionHandler>(provider.GetRequiredService<ComputerUseWinPerformSecondaryActionHandler>());
        Assert.IsType<ComputerUseWinPressKeyHandler>(provider.GetRequiredService<ComputerUseWinPressKeyHandler>());
        Assert.IsType<ComputerUseWinScrollHandler>(provider.GetRequiredService<ComputerUseWinScrollHandler>());
        Assert.IsType<ComputerUseWinSetValueHandler>(provider.GetRequiredService<ComputerUseWinSetValueHandler>());
        Assert.IsType<ComputerUseWinTypeTextHandler>(provider.GetRequiredService<ComputerUseWinTypeTextHandler>());
        Assert.IsType<ComputerUseWinActionRequestExecutor>(provider.GetRequiredService<ComputerUseWinActionRequestExecutor>());
        Assert.IsType<ComputerUseWinExecutionTargetCatalog>(provider.GetRequiredService<ComputerUseWinExecutionTargetCatalog>());
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
    public void ToolRequestBinderRejectsNestedAdditionalPropertiesForComputerUseScrollPoint()
    {
        using JsonDocument document = JsonDocument.Parse("""{"stateToken":"token-1","point":{"x":10,"y":20,"extra":true},"direction":"down","confirm":true}""");
        Dictionary<string, JsonElement> arguments = new(StringComparer.Ordinal)
        {
            ["stateToken"] = document.RootElement.GetProperty("stateToken").Clone(),
            ["point"] = document.RootElement.GetProperty("point").Clone(),
            ["direction"] = document.RootElement.GetProperty("direction").Clone(),
            ["confirm"] = document.RootElement.GetProperty("confirm").Clone(),
        };

        bool success = ToolRequestBinder.TryBind(
            arguments,
            fallbackRequest: new ComputerUseWinScrollRequest(),
            out ComputerUseWinScrollRequest request,
            out string? reason,
            static value => ComputerUseWinRequestContractValidator.Validate(value));

        Assert.False(success);
        Assert.Equal(new ComputerUseWinScrollRequest(), request);
        Assert.Contains("point", reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("extra", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToolRequestBinderRejectsOutOfRangePagesForComputerUseScroll()
    {
        using JsonDocument document = JsonDocument.Parse("""{"stateToken":"token-1","elementIndex":1,"direction":"down","pages":11}""");
        Dictionary<string, JsonElement> arguments = new(StringComparer.Ordinal)
        {
            ["stateToken"] = document.RootElement.GetProperty("stateToken").Clone(),
            ["elementIndex"] = document.RootElement.GetProperty("elementIndex").Clone(),
            ["direction"] = document.RootElement.GetProperty("direction").Clone(),
            ["pages"] = document.RootElement.GetProperty("pages").Clone(),
        };

        bool success = ToolRequestBinder.TryBind(
            arguments,
            fallbackRequest: new ComputerUseWinScrollRequest(),
            out ComputerUseWinScrollRequest request,
            out string? reason,
            static value => ComputerUseWinRequestContractValidator.Validate(value));

        Assert.False(success);
        Assert.Equal(new ComputerUseWinScrollRequest(), request);
        Assert.Contains("pages", reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("10", reason, StringComparison.OrdinalIgnoreCase);
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
    public void ToolRequestBinderRejectsWhitespaceCoordinateSpaceForComputerUseScrollPoint()
    {
        using JsonDocument document = JsonDocument.Parse("""{"stateToken":"token-1","point":{"x":10,"y":20},"coordinateSpace":"   ","direction":"down","confirm":true}""");
        Dictionary<string, JsonElement> arguments = new(StringComparer.Ordinal)
        {
            ["stateToken"] = document.RootElement.GetProperty("stateToken").Clone(),
            ["point"] = document.RootElement.GetProperty("point").Clone(),
            ["coordinateSpace"] = document.RootElement.GetProperty("coordinateSpace").Clone(),
            ["direction"] = document.RootElement.GetProperty("direction").Clone(),
            ["confirm"] = document.RootElement.GetProperty("confirm").Clone(),
        };

        bool success = ToolRequestBinder.TryBind(
            arguments,
            fallbackRequest: new ComputerUseWinScrollRequest(),
            out ComputerUseWinScrollRequest request,
            out string? reason,
            static value => ComputerUseWinRequestContractValidator.Validate(value));

        Assert.False(success);
        Assert.Equal(new ComputerUseWinScrollRequest(), request);
        Assert.Contains("coordinateSpace", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToolRequestBinderRejectsWhitespaceCoordinateSpaceForComputerUseDragPointPath()
    {
        using JsonDocument document = JsonDocument.Parse("""{"stateToken":"token-1","fromPoint":{"x":10,"y":20},"toPoint":{"x":30,"y":40},"coordinateSpace":"   ","confirm":true}""");
        Dictionary<string, JsonElement> arguments = new(StringComparer.Ordinal)
        {
            ["stateToken"] = document.RootElement.GetProperty("stateToken").Clone(),
            ["fromPoint"] = document.RootElement.GetProperty("fromPoint").Clone(),
            ["toPoint"] = document.RootElement.GetProperty("toPoint").Clone(),
            ["coordinateSpace"] = document.RootElement.GetProperty("coordinateSpace").Clone(),
            ["confirm"] = document.RootElement.GetProperty("confirm").Clone(),
        };

        bool success = ToolRequestBinder.TryBind(
            arguments,
            fallbackRequest: new ComputerUseWinDragRequest(),
            out ComputerUseWinDragRequest request,
            out string? reason,
            static value => ComputerUseWinRequestContractValidator.Validate(value));

        Assert.False(success);
        Assert.Equal(new ComputerUseWinDragRequest(), request);
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
    public void RuntimeStateModelRejectsActionFromStaleState()
    {
        ComputerUseWinRuntimeState state = ComputerUseWinRuntimeStateModel.Stale();

        Assert.Equal(ComputerUseWinRuntimeStateKind.Stale, state.Kind);
        Assert.False(ComputerUseWinRuntimeStateModel.CanExecuteAction(state));
    }

    [Fact]
    public void RuntimeStateModelDoesNotTreatApprovalAsFreshObservationWithoutLiveProof()
    {
        ComputerUseWinRuntimeState state = ComputerUseWinRuntimeStateModel.Approved();

        Assert.Equal(ComputerUseWinRuntimeStateKind.Approved, state.Kind);
        Assert.False(ComputerUseWinRuntimeStateModel.CanPromoteToObserved(state, hasFreshObservation: false));
        Assert.True(ComputerUseWinRuntimeStateModel.CanPromoteToObserved(state, hasFreshObservation: true));
    }

    [Fact]
    public void RuntimeStateModelDoesNotPromoteBlockedStateWithoutNewLiveProof()
    {
        ComputerUseWinRuntimeState state = ComputerUseWinRuntimeStateModel.Blocked();

        Assert.Equal(ComputerUseWinRuntimeStateKind.Blocked, state.Kind);
        Assert.False(ComputerUseWinRuntimeStateModel.CanExecuteAction(state));
        Assert.False(ComputerUseWinRuntimeStateModel.CanPromoteToObserved(state, hasFreshObservation: false));
        Assert.False(ComputerUseWinRuntimeStateModel.CanPromoteToObserved(state, hasFreshObservation: true));
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
            new ComputerUseWinExecutionTargetCatalog(),
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
    public void ComputerUseWinTypeTextToolSchemaExposesFocusedFallbackOptIn()
    {
        var tools = ComputerUseWinToolRegistration.Create(static () => null!);
        var typeTextTool = tools.Single(tool => string.Equals(tool.ProtocolTool.Name, ToolNames.ComputerUseWinTypeText, StringComparison.Ordinal));
        JsonElement inputSchema = typeTextTool.ProtocolTool.InputSchema;
        JsonElement properties = inputSchema.GetProperty("properties");

        string[] required = inputSchema.GetProperty("required").EnumerateArray()
            .Select(item => item.GetString())
            .Where(static item => item is not null)
            .Cast<string>()
            .ToArray();

        Assert.Equal(["stateToken", "text"], required);
        Assert.Equal("boolean", properties.GetProperty("allowFocusedFallback").GetProperty("type").GetString());
        Assert.Equal("boolean", properties.GetProperty("confirm").GetProperty("type").GetString());
    }

    [Fact]
    public void ComputerUseWinScrollToolSchemaBoundsPagesAndRequiresNonNullSelectorBranches()
    {
        var tools = ComputerUseWinToolRegistration.Create(static () => null!);
        var scrollTool = tools.Single(tool => string.Equals(tool.ProtocolTool.Name, ToolNames.ComputerUseWinScroll, StringComparison.Ordinal));
        JsonElement inputSchema = scrollTool.ProtocolTool.InputSchema;
        JsonElement properties = inputSchema.GetProperty("properties");

        Assert.Equal(10, properties.GetProperty("pages").GetProperty("maximum").GetInt32());

        JsonElement[] selectorModes = [.. inputSchema.GetProperty("oneOf").EnumerateArray()];
        JsonElement elementBranch = selectorModes.Single(mode => mode.GetProperty("required").EnumerateArray().Any(item => item.GetString() == "elementIndex"));
        JsonElement pointBranch = selectorModes.Single(mode => mode.GetProperty("required").EnumerateArray().Any(item => item.GetString() == "point"));

        Assert.Equal("integer", elementBranch.GetProperty("properties").GetProperty("elementIndex").GetProperty("type").GetString());
        Assert.Equal("object", pointBranch.GetProperty("properties").GetProperty("point").GetProperty("type").GetString());
    }

    [Fact]
    public void ComputerUseWinDragToolSchemaRequiresStateTokenAndSeparateSourceDestinationModes()
    {
        var tools = ComputerUseWinToolRegistration.Create(static () => null!);
        var dragTool = tools.Single(tool => string.Equals(tool.ProtocolTool.Name, ToolNames.ComputerUseWinDrag, StringComparison.Ordinal));
        JsonElement inputSchema = dragTool.ProtocolTool.InputSchema;
        JsonElement properties = inputSchema.GetProperty("properties");

        string[] required = inputSchema.GetProperty("required").EnumerateArray()
            .Select(item => item.GetString())
            .Where(static item => item is not null)
            .Cast<string>()
            .ToArray();

        Assert.Equal(["stateToken"], required);
        Assert.True(inputSchema.TryGetProperty("allOf", out JsonElement allOf));
        JsonElement[] selectorModes = [.. allOf.EnumerateArray()];
        Assert.Equal(2, selectorModes.Length);
        Assert.All(
            selectorModes,
            mode => Assert.True(mode.TryGetProperty("oneOf", out _)));

        JsonElement sourceBranch = selectorModes[0].GetProperty("oneOf").EnumerateArray()
            .Single(mode => mode.GetProperty("required").EnumerateArray().Any(item => item.GetString() == "fromElementIndex"));
        JsonElement sourcePointBranch = selectorModes[0].GetProperty("oneOf").EnumerateArray()
            .Single(mode => mode.GetProperty("required").EnumerateArray().Any(item => item.GetString() == "fromPoint"));
        JsonElement destinationBranch = selectorModes[1].GetProperty("oneOf").EnumerateArray()
            .Single(mode => mode.GetProperty("required").EnumerateArray().Any(item => item.GetString() == "toElementIndex"));
        JsonElement destinationPointBranch = selectorModes[1].GetProperty("oneOf").EnumerateArray()
            .Single(mode => mode.GetProperty("required").EnumerateArray().Any(item => item.GetString() == "toPoint"));

        Assert.Equal("integer", sourceBranch.GetProperty("properties").GetProperty("fromElementIndex").GetProperty("type").GetString());
        Assert.Equal("object", sourcePointBranch.GetProperty("properties").GetProperty("fromPoint").GetProperty("type").GetString());
        Assert.Equal("integer", destinationBranch.GetProperty("properties").GetProperty("toElementIndex").GetProperty("type").GetString());
        Assert.Equal("object", destinationPointBranch.GetProperty("properties").GetProperty("toPoint").GetProperty("type").GetString());
        Assert.Equal(@".*\S.*", properties.GetProperty("stateToken").GetProperty("pattern").GetString());
    }

    [Fact]
    public void ComputerUseWinSecondaryActionToolSchemaRequiresStateTokenAndElementIndex()
    {
        var tools = ComputerUseWinToolRegistration.Create(static () => null!);
        var secondaryActionTool = tools.Single(tool => string.Equals(tool.ProtocolTool.Name, ToolNames.ComputerUseWinPerformSecondaryAction, StringComparison.Ordinal));
        JsonElement inputSchema = secondaryActionTool.ProtocolTool.InputSchema;
        JsonElement properties = inputSchema.GetProperty("properties");

        string[] required = inputSchema.GetProperty("required").EnumerateArray()
            .Select(item => item.GetString())
            .Where(static item => item is not null)
            .Cast<string>()
            .ToArray();

        Assert.Equal(["stateToken", "elementIndex"], required);
        Assert.Equal(1, properties.GetProperty("elementIndex").GetProperty("minimum").GetInt32());
        Assert.False(properties.TryGetProperty("point", out _));
    }

    [Fact]
    public void SecondaryActionKindDerivationAcceptsPrePatternAndResolvedPatternDispatchPaths()
    {
        Assert.Equal(
            UiaSecondaryActionKindValues.Toggle,
            ComputerUseWinPerformSecondaryActionHandler.ResolveSemanticActionKind("uia_toggle"));
        Assert.Equal(
            UiaSecondaryActionKindValues.Toggle,
            ComputerUseWinPerformSecondaryActionHandler.ResolveSemanticActionKind("uia_toggle_pattern"));
        Assert.Null(ComputerUseWinPerformSecondaryActionHandler.ResolveSemanticActionKind(null));
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
