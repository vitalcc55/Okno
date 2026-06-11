// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

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
    private const string NonBlankJsonStringPattern = @".*\S.*";

    private static readonly Lazy<ToolContractProfile> ComputerUseWinProfile = new(
        static () => ToolContractManifest.GetProfile(ToolSurfaceProfileValues.ComputerUseWin));

    private static readonly Lazy<string[]> PublishedComputerUseWinToolNames = new(
        static () => ComputerUseWinToolRegistration.Create(static () => null!)
            .Select(static tool => tool.ProtocolTool.Name)
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray());

    private static readonly Lazy<IReadOnlyDictionary<string, JsonElement>> ComputerUseWinInputSchemasByToolName = new(
        static () => ComputerUseWinToolRegistration.Create(static () => null!)
            .ToDictionary(
                static tool => tool.ProtocolTool.Name,
                static tool => tool.ProtocolTool.InputSchema,
                StringComparer.Ordinal));

    private static readonly Lazy<string[]> ComputerUseWinToolFactoryMethodNames = new(
        static () => typeof(ComputerUseWinToolRegistration)
            .GetMethods(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly)
            .Where(method => method.Name.StartsWith("Create", StringComparison.Ordinal)
                && method.Name.EndsWith("Tool", StringComparison.Ordinal)
                && method.GetParameters().Length == 1
                && method.GetParameters()[0].ParameterType == typeof(Func<ComputerUseWinTools>))
            .Select(static method => method.Name)
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray());

    [Fact]
    public void BlockPolicyUsesCanonicalProcessIdentityReturnedByRuntime()
    {
        WindowDescriptor window = CreateWindow(processName: "powershell");

        bool isBlocked = ComputerUseWinTargetPolicy.TryGetBlockedReason(window, out string? reason);

        Assert.True(isBlocked);
        AssertFailureReasonContains(reason, "powershell");
    }

    [Theory]
    [InlineData("conhost.exe")]
    [InlineData("OpenConsole.exe")]
    public void BlockPolicyCoversConsoleHostFamilies(string processName)
    {
        WindowDescriptor window = CreateWindow(processName: processName);

        bool isBlocked = ComputerUseWinTargetPolicy.TryGetBlockedReason(window, out string? reason);

        Assert.True(isBlocked);
        AssertFailureReasonContains(reason, processName);
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

        AssertValidationFailure(failure);
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

        AssertValidationFailure(failure, "repeat");
    }

    [Fact]
    public void PlaybookProviderMatchesBareRuntimeProcessName()
    {
        using TempDirectoryScope temp = new();
        string instructionsRoot = CreateAppInstructionsRoot(temp.Root);
        File.WriteAllLines(
            Path.Combine(instructionsRoot, "FileExplorer.md"),
            ["Открой нужную папку.", "", "Используй левую панель только после get_app_state."]);
        ComputerUseWinPlaybookProvider provider = CreatePlaybookProvider(temp.Root, instructionsRoot);

        IReadOnlyList<string> instructions = provider.GetInstructions("explorer");

        Assert.Equal(
            ["Открой нужную папку.", "Используй левую панель только после get_app_state."],
            instructions);
    }

    [Fact]
    public void PlaybookProviderRaisesUnavailableWhenPlaybookPathIsUnreadable()
    {
        using TempDirectoryScope temp = new();
        string instructionsRoot = CreateAppInstructionsRoot(temp.Root);
        Directory.CreateDirectory(Path.Combine(instructionsRoot, "FileExplorer.md"));
        ComputerUseWinPlaybookProvider provider = CreatePlaybookProvider(temp.Root, instructionsRoot);

        Assert.Throws<ComputerUseWinInstructionUnavailableException>(() => provider.GetInstructions("explorer"));
    }

    [Fact]
    public void PlaybookProviderRaisesUnavailableWhenMappedPlaybookAssetIsMissing()
    {
        using TempDirectoryScope temp = new();
        string instructionsRoot = CreateAppInstructionsRoot(temp.Root);
        ComputerUseWinPlaybookProvider provider = CreatePlaybookProvider(temp.Root, instructionsRoot);

        Assert.Throws<ComputerUseWinInstructionUnavailableException>(() => provider.GetInstructions("explorer"));
    }

    [Fact]
    public void AffordanceResolverPublishesTypeTextOnlyForFocusedEditableTargets()
    {
        UiaElementSnapshot unfocusedNode = CreateEnabledUiaElement(
            elementId: "path:0",
            controlType: "edit",
            bounds: new Bounds(10, 20, 110, 40),
            isReadOnly: false,
            patterns: ["value"]);
        UiaElementSnapshot focusedNode = unfocusedNode with { HasKeyboardFocus = true };

        IReadOnlyList<string> unfocusedActions = ResolveAffordances(unfocusedNode);
        IReadOnlyList<string> focusedActions = ResolveAffordances(focusedNode);
        IReadOnlyList<string> documentActions = ResolveAffordances(CreateEnabledUiaElement(
            elementId: "path:document",
            controlType: "document",
            bounds: new Bounds(20, 30, 180, 120),
            hasKeyboardFocus: true));
        IReadOnlyList<string> missingWritableProofActions = ResolveAffordances(focusedNode with { IsReadOnly = null });
        IReadOnlyList<string> readOnlyActions = ResolveAffordances(focusedNode with { IsReadOnly = true });

        Assert.Contains(ToolNames.ComputerUseWinClick, unfocusedActions);
        Assert.Contains(ToolNames.ComputerUseWinSetValue, unfocusedActions);
        Assert.DoesNotContain(ToolNames.ComputerUseWinTypeText, unfocusedActions);
        Assert.Contains(ToolNames.ComputerUseWinTypeText, focusedActions);
        Assert.Contains(
            ToolNames.ComputerUseWinScroll,
            ResolveAffordances(CreateEnabledUiaElement("path:scroll", "list", new Bounds(12, 22, 140, 120), patterns: ["scroll"])));
        Assert.Contains(
            ToolNames.ComputerUseWinPerformSecondaryAction,
            ResolveAffordances(CreateEnabledUiaElement("path:toggle", "check_box", new Bounds(24, 104, 244, 128), patterns: ["toggle"])));
        Assert.DoesNotContain(
            ToolNames.ComputerUseWinPerformSecondaryAction,
            ResolveAffordances(CreateEnabledUiaElement("path:leaf-like", "tree_item", new Bounds(24, 252, 120, 276), patterns: ["expand_collapse"])));
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

        AssertValidationFailure(failure, "elementIndex");
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

        AssertValidationFailure(failure, "valueKind");
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

        AssertValidationFailure(failure, "text");
    }

    [Fact]
    public void TypeTextValidatorRequiresConfirmForFocusedFallbackOptIn()
    {
        Dictionary<string, JsonElement> arguments = CreateToolArguments("""{"stateToken":"token-1","text":"typed text","allowFocusedFallback":true,"confirm":false}""");

        string? reason = BindInvalidRequestAndAssertFallback(
            arguments,
            new ComputerUseWinTypeTextRequest(),
            static value => ComputerUseWinRequestContractValidator.Validate(value));
        AssertFailureReasonContains(reason, "confirm");
        AssertFailureReasonDoesNotContain(reason, "unmapped");
    }

    [Fact]
    public void TypeTextValidatorAllowsCoordinateConfirmedFallbackWithExplicitPoint()
    {
        string? failure = ComputerUseWinRequestContractValidator.Validate(
            new ComputerUseWinTypeTextRequest(
                StateToken: "token-1",
                ElementIndex: null,
                Point: new InputPoint(10, 20),
                CoordinateSpace: InputCoordinateSpaceValues.CapturePixels,
                Text: "typed text",
                Confirm: true,
                AllowFocusedFallback: true));

        Assert.Null(failure);
    }

    [Fact]
    public void TypeTextContractDefaultsCoordinateConfirmedFallbackToCapturePixels()
    {
        bool parsed = ComputerUseWinTypeTextContract.TryParse(
            new ComputerUseWinTypeTextRequest(
                StateToken: "token-1",
                ElementIndex: null,
                Point: new InputPoint(10, 20),
                CoordinateSpace: null,
                Text: "typed text",
                Confirm: true,
                AllowFocusedFallback: true),
            out ComputerUseWinTypeTextPayload? payload,
            out string? failure);

        Assert.True(parsed, failure);
        Assert.Equal(InputCoordinateSpaceValues.CapturePixels, payload!.CoordinateSpace);
    }

    [Fact]
    public void TypeTextValidatorRejectsScreenCoordinateSpaceForCoordinateConfirmedFallback()
    {
        string? failure = ComputerUseWinRequestContractValidator.Validate(
            new ComputerUseWinTypeTextRequest(
                StateToken: "token-1",
                ElementIndex: null,
                Point: new InputPoint(10, 20),
                CoordinateSpace: InputCoordinateSpaceValues.Screen,
                Text: "typed text",
                Confirm: true,
                AllowFocusedFallback: true));

        AssertValidationFailure(failure, "capture_pixels");
    }

    [Fact]
    public void TypeTextValidatorRejectsCoordinatePointWithoutFocusedFallbackOptIn()
    {
        string? failure = ComputerUseWinRequestContractValidator.Validate(
            new ComputerUseWinTypeTextRequest(
                StateToken: "token-1",
                ElementIndex: null,
                Point: new InputPoint(10, 20),
                CoordinateSpace: InputCoordinateSpaceValues.CapturePixels,
                Text: "typed text",
                Confirm: true,
                AllowFocusedFallback: false));

        AssertValidationFailure(failure, "allowFocusedFallback");
    }

    [Fact]
    public void TypeTextValidatorRejectsCoordinatePointWithoutConfirm()
    {
        string? failure = ComputerUseWinRequestContractValidator.Validate(
            new ComputerUseWinTypeTextRequest(
                StateToken: "token-1",
                ElementIndex: null,
                Point: new InputPoint(10, 20),
                CoordinateSpace: InputCoordinateSpaceValues.CapturePixels,
                Text: "typed text",
                Confirm: false,
                AllowFocusedFallback: true));

        AssertValidationFailure(failure, "confirm");
    }

    [Fact]
    public void TypeTextValidatorRejectsConflictingElementAndPointSelectors()
    {
        string? failure = ComputerUseWinRequestContractValidator.Validate(
            new ComputerUseWinTypeTextRequest(
                StateToken: "token-1",
                ElementIndex: 1,
                Point: new InputPoint(10, 20),
                CoordinateSpace: InputCoordinateSpaceValues.CapturePixels,
                Text: "typed text",
                Confirm: true,
                AllowFocusedFallback: true));

        AssertValidationFailure(failure, "point");
    }

    [Fact]
    public void TypeTextValidatorRejectsCoordinateSpaceWithoutPoint()
    {
        string? failure = ComputerUseWinRequestContractValidator.Validate(
            new ComputerUseWinTypeTextRequest(
                StateToken: "token-1",
                ElementIndex: null,
                Point: null,
                CoordinateSpace: InputCoordinateSpaceValues.CapturePixels,
                Text: "typed text",
                Confirm: true,
                AllowFocusedFallback: true));

        AssertValidationFailure(failure, "coordinateSpace");
    }

    [Fact]
    public void TypeTextValidatorRejectsUnsupportedCoordinateSpace()
    {
        string? failure = ComputerUseWinRequestContractValidator.Validate(
            new ComputerUseWinTypeTextRequest(
                StateToken: "token-1",
                ElementIndex: null,
                Point: new InputPoint(10, 20),
                CoordinateSpace: "bogus",
                Text: "typed text",
                Confirm: true,
                AllowFocusedFallback: true));

        AssertValidationFailure(failure, "coordinateSpace");
    }

    [Fact]
    public void TypeTextBinderRejectsMalformedCoordinatePointBeforeDispatch()
    {
        Dictionary<string, JsonElement> arguments = CreateToolArguments("""{"stateToken":"token-1","point":{"x":10,"y":20,"extra":true},"text":"typed text","allowFocusedFallback":true,"confirm":true}""");

        string? reason = BindInvalidRequestAndAssertFallback(
            arguments,
            new ComputerUseWinTypeTextRequest(),
            static value => ComputerUseWinRequestContractValidator.Validate(value));
        AssertFailureReasonContains(reason, "point");
        AssertFailureReasonDoesNotContain(reason, "unmapped");
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

        AssertValidationFailure(failure, "elementIndex");
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

        AssertValidationFailure(failure, "point");
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

        AssertValidationFailure(failure, "pages", "10");
    }

    [Fact]
    public void PerformSecondaryActionValidatorRejectsMissingElementIndex()
    {
        string? failure = ComputerUseWinRequestContractValidator.Validate(
            new ComputerUseWinPerformSecondaryActionRequest(
                StateToken: "token-1",
                ElementIndex: null,
                Confirm: false));

        AssertValidationFailure(failure, "elementIndex");
    }

    [Fact]
    public void ComputerUseWinProfilePublishesImplementedDragAlongsideShippedOperatorTools()
    {
        ToolContractProfile profile = GetComputerUseWinProfile();
        string[] publishedToolNames = GetPublishedComputerUseWinToolNames();
        string[] factoryMethodNames = GetComputerUseWinToolFactoryMethodNames();

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
        ToolContractProfile profile = GetComputerUseWinProfile();
        string[] deferredNames = profile.Deferred
            .Select(static descriptor => descriptor.Name)
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(deferredNames);
        Assert.Contains("drag", GetPublishedComputerUseWinToolNames());
    }

    [Fact]
    public void ComputerUseWinListAppsMetadataReflectsStatefulSelectorIssuance()
    {
        var tools = ComputerUseWinToolRegistration.Create(static () => null!);
        ToolContractProfile profile = GetComputerUseWinProfile();
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
        using ServiceProvider provider = BuildComputerUseWinServiceProviderForResolutionTest(temp.Root);

        AssertServiceResolves<ComputerUseWinListAppsHandler>(provider);
        AssertServiceResolves<ComputerUseWinGetAppStateHandler>(provider);
        AssertServiceResolves<ComputerUseWinClickHandler>(provider);
        AssertServiceResolves<ComputerUseWinDragHandler>(provider);
        AssertServiceResolves<ComputerUseWinPerformSecondaryActionHandler>(provider);
        AssertServiceResolves<ComputerUseWinPressKeyHandler>(provider);
        AssertServiceResolves<ComputerUseWinScrollHandler>(provider);
        AssertServiceResolves<ComputerUseWinSetValueHandler>(provider);
        AssertServiceResolves<ComputerUseWinTypeTextHandler>(provider);
        AssertServiceResolves<ComputerUseWinActionRequestExecutor>(provider);
        AssertServiceResolves<ComputerUseWinExecutionTargetCatalog>(provider);
        AssertServiceResolves<ComputerUseWinTools>(provider);
    }

    [Fact]
    public void ComputerUseWinManualSchemasRelyOnJsonSchema202012DefaultWithoutExplicitSchemaKeyword()
    {
        JsonElement getAppStateSchema = GetComputerUseWinInputSchema(ToolNames.ComputerUseWinGetAppState);
        JsonElement clickSchema = GetComputerUseWinInputSchema(ToolNames.ComputerUseWinClick);

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
        Dictionary<string, JsonElement> arguments = CreateToolArguments("""{"unexpected":true}""");

        bool success = ToolRequestBinder.TryBind(
            arguments,
            fallbackRequest: new ComputerUseWinGetAppStateRequest(),
            out ComputerUseWinGetAppStateRequest _,
            out string? reason);

        Assert.False(success);
        AssertFailureReasonContains(reason, "unexpected");
    }

    [Fact]
    public void ToolRequestBinderRejectsSchemaInvalidRangeForComputerUseRequests()
    {
        Dictionary<string, JsonElement> arguments = CreateToolArguments("""{"maxNodes":2048}""");

        string? reason = BindInvalidRequestAndAssertFallback(
            arguments,
            new ComputerUseWinGetAppStateRequest(),
            static value => ComputerUseWinRequestContractValidator.Validate(value));
        AssertFailureReasonContains(reason, "1024");
    }

    [Fact]
    public void ToolRequestBinderRejectsNestedAdditionalPropertiesForComputerUseClickPoint()
    {
        Dictionary<string, JsonElement> arguments = CreateToolArguments("""{"stateToken":"token-1","point":{"x":10,"y":20,"extra":true}}""");

        string? reason = BindInvalidRequestAndAssertFallback(
            arguments,
            new ComputerUseWinClickRequest(),
            static value => ComputerUseWinRequestContractValidator.Validate(value));
        AssertFailureReasonContains(reason, "point", "extra");
    }

    [Fact]
    public void ToolRequestBinderRejectsExplicitNullPointForComputerUseClick()
    {
        Dictionary<string, JsonElement> arguments = CreateToolArguments("""{"stateToken":"token-1","point":null,"confirm":true}""");

        string? reason = BindInvalidRequestAndAssertFallback(
            arguments,
            new ComputerUseWinClickRequest(),
            ComputerUseWinRequestContractValidator.Validate);
        AssertFailureReasonContains(reason, "point", "JSON object");
    }

    [Fact]
    public void ToolRequestBinderRejectsExplicitNullPointForComputerUseTypeText()
    {
        Dictionary<string, JsonElement> arguments = CreateToolArguments("""{"stateToken":"token-1","point":null,"text":"typed text","allowFocusedFallback":true,"confirm":true}""");

        string? reason = BindInvalidRequestAndAssertFallback(
            arguments,
            new ComputerUseWinTypeTextRequest(),
            ComputerUseWinRequestContractValidator.Validate);
        AssertFailureReasonContains(reason, "point", "JSON object");
    }

    [Fact]
    public void ToolRequestBinderRejectsNestedAdditionalPropertiesForComputerUseScrollPoint()
    {
        Dictionary<string, JsonElement> arguments = CreateToolArguments("""{"stateToken":"token-1","point":{"x":10,"y":20,"extra":true},"direction":"down","confirm":true}""");

        string? reason = BindInvalidRequestAndAssertFallback(
            arguments,
            new ComputerUseWinScrollRequest(),
            static value => ComputerUseWinRequestContractValidator.Validate(value));
        AssertFailureReasonContains(reason, "point", "extra");
    }

    [Fact]
    public void ToolRequestBinderRejectsExplicitNullPointForComputerUseScroll()
    {
        Dictionary<string, JsonElement> arguments = CreateToolArguments("""{"stateToken":"token-1","point":null,"direction":"down","confirm":true}""");

        string? reason = BindInvalidRequestAndAssertFallback(
            arguments,
            new ComputerUseWinScrollRequest(),
            ComputerUseWinRequestContractValidator.Validate);
        AssertFailureReasonContains(reason, "point", "JSON object");
    }

    [Fact]
    public void ToolRequestBinderRejectsOutOfRangePagesForComputerUseScroll()
    {
        Dictionary<string, JsonElement> arguments = CreateToolArguments("""{"stateToken":"token-1","elementIndex":1,"direction":"down","pages":11}""");

        string? reason = BindInvalidRequestAndAssertFallback(
            arguments,
            new ComputerUseWinScrollRequest(),
            static value => ComputerUseWinRequestContractValidator.Validate(value));
        AssertFailureReasonContains(reason, "pages", "10");
    }

    [Fact]
    public void ToolRequestBinderRejectsUnsupportedCoordinateSpaceForComputerUseClick()
    {
        Dictionary<string, JsonElement> arguments = CreateToolArguments("""{"stateToken":"token-1","point":{"x":10,"y":20},"coordinateSpace":"bogus","confirm":true}""");

        string? reason = BindInvalidRequestAndAssertFallback(
            arguments,
            new ComputerUseWinClickRequest(),
            static value => ComputerUseWinRequestContractValidator.Validate(value));
        AssertFailureReasonContains(reason, "coordinateSpace");
    }

    [Fact]
    public void ToolRequestBinderRejectsWhitespaceCoordinateSpaceForComputerUseClick()
    {
        Dictionary<string, JsonElement> arguments = CreateToolArguments("""{"stateToken":"token-1","point":{"x":10,"y":20},"coordinateSpace":"   ","confirm":true}""");

        string? reason = BindInvalidRequestAndAssertFallback(
            arguments,
            new ComputerUseWinClickRequest(),
            static value => ComputerUseWinRequestContractValidator.Validate(value));
        AssertFailureReasonContains(reason, "coordinateSpace");
    }

    [Fact]
    public void ToolRequestBinderRejectsWhitespaceCoordinateSpaceForComputerUseScrollPoint()
    {
        Dictionary<string, JsonElement> arguments = CreateToolArguments("""{"stateToken":"token-1","point":{"x":10,"y":20},"coordinateSpace":"   ","direction":"down","confirm":true}""");

        string? reason = BindInvalidRequestAndAssertFallback(
            arguments,
            new ComputerUseWinScrollRequest(),
            static value => ComputerUseWinRequestContractValidator.Validate(value));
        AssertFailureReasonContains(reason, "coordinateSpace");
    }

    [Fact]
    public void ToolRequestBinderRejectsWhitespaceCoordinateSpaceForComputerUseDragPointPath()
    {
        Dictionary<string, JsonElement> arguments = CreateToolArguments("""{"stateToken":"token-1","fromPoint":{"x":10,"y":20},"toPoint":{"x":30,"y":40},"coordinateSpace":"   ","confirm":true}""");

        string? reason = BindInvalidRequestAndAssertFallback(
            arguments,
            new ComputerUseWinDragRequest(),
            static value => ComputerUseWinRequestContractValidator.Validate(value));
        AssertFailureReasonContains(reason, "coordinateSpace");
    }

    [Fact]
    public void ToolRequestBinderRejectsExplicitNullPointsForComputerUseDrag()
    {
        Dictionary<string, JsonElement> arguments = CreateToolArguments("""{"stateToken":"token-1","fromPoint":null,"toPoint":null,"confirm":true}""");

        string? reason = BindInvalidRequestAndAssertFallback(
            arguments,
            new ComputerUseWinDragRequest(),
            ComputerUseWinRequestContractValidator.Validate);
        AssertFailureReasonContains(reason, "fromPoint", "JSON object");
    }

    [Fact]
    public void ToolRequestBinderRejectsUnsupportedButtonForComputerUseClick()
    {
        Dictionary<string, JsonElement> arguments = CreateToolArguments("""{"stateToken":"token-1","elementIndex":1,"button":"middle","confirm":true}""");

        string? reason = BindInvalidRequestAndAssertFallback(
            arguments,
            new ComputerUseWinClickRequest(),
            static value => ComputerUseWinRequestContractValidator.Validate(value));
        AssertFailureReasonContains(reason, "button");
    }

    [Fact]
    public void ToolRequestBinderRejectsWhitespaceButtonForComputerUseClick()
    {
        Dictionary<string, JsonElement> arguments = CreateToolArguments("""{"stateToken":"token-1","elementIndex":1,"button":"   ","confirm":true}""");

        string? reason = BindInvalidRequestAndAssertFallback(
            arguments,
            new ComputerUseWinClickRequest(),
            static value => ComputerUseWinRequestContractValidator.Validate(value));
        AssertFailureReasonContains(reason, "button");
    }

    [Fact]
    public void ToolRequestBinderRejectsMissingStateTokenForComputerUseClick()
    {
        Dictionary<string, JsonElement> arguments = CreateToolArguments("""{"elementIndex":1,"confirm":true}""");

        string? reason = BindInvalidRequestAndAssertFallback(
            arguments,
            new ComputerUseWinClickRequest(),
            static value => ComputerUseWinRequestContractValidator.Validate(value));
        AssertFailureReasonContains(reason, "stateToken");
    }

    [Fact]
    public void ToolRequestBinderRejectsMissingSelectorForComputerUseClick()
    {
        Dictionary<string, JsonElement> arguments = CreateToolArguments("""{"stateToken":"token-1","confirm":true}""");

        string? reason = BindInvalidRequestAndAssertFallback(
            arguments,
            new ComputerUseWinClickRequest(),
            static value => ComputerUseWinRequestContractValidator.Validate(value));
        AssertFailureReasonContains(reason, "elementIndex", "selector", "point");
    }

    [Fact]
    public void ToolRequestBinderRejectsWhitespaceStateTokenForComputerUseClick()
    {
        Dictionary<string, JsonElement> arguments = CreateToolArguments("""{"stateToken":"   ","elementIndex":1,"confirm":true}""");

        string? reason = BindInvalidRequestAndAssertFallback(
            arguments,
            new ComputerUseWinClickRequest(),
            static value => ComputerUseWinRequestContractValidator.Validate(value));
        AssertFailureReasonContains(reason, "stateToken");
    }

    [Fact]
    public void ToolRequestBinderRejectsConflictingSelectorsForGetAppState()
    {
        Dictionary<string, JsonElement> arguments = CreateToolArguments("""{"windowId":"cw_explorer_123","hwnd":123}""");

        string? reason = BindInvalidRequestAndAssertFallback(
            arguments,
            new ComputerUseWinGetAppStateRequest(),
            static value => ComputerUseWinRequestContractValidator.Validate(value));
        AssertFailureReasonContains(reason, "windowId", "hwnd");
    }

    [Fact]
    public void ToolRequestBinderRejectsWhitespaceWindowIdWhenHwndIsPresent()
    {
        Dictionary<string, JsonElement> arguments = CreateToolArguments("""{"windowId":"   ","hwnd":123}""");

        string? reason = BindInvalidRequestAndAssertFallback(
            arguments,
            new ComputerUseWinGetAppStateRequest(),
            static value => ComputerUseWinRequestContractValidator.Validate(value));
        AssertFailureReasonContains(reason, "windowId");
    }

    [Fact]
    public void ToolRequestBinderRejectsConflictingSelectorsForComputerUseClick()
    {
        Dictionary<string, JsonElement> arguments = CreateToolArguments("""{"stateToken":"token-1","elementIndex":1,"point":{"x":10,"y":20},"confirm":true}""");

        string? reason = BindInvalidRequestAndAssertFallback(
            arguments,
            new ComputerUseWinClickRequest(),
            static value => ComputerUseWinRequestContractValidator.Validate(value));
        AssertFailureReasonContains(reason, "elementIndex", "selector", "point");
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
        JsonElement properties = GetComputerUseWinInputSchemaProperties(ToolNames.ComputerUseWinClick);

        AssertNullableStringEnum(properties.GetProperty("button"), [InputButtonValues.Left, InputButtonValues.Right]);
        AssertNullableStringEnum(properties.GetProperty("coordinateSpace"), [InputCoordinateSpaceValues.Screen, InputCoordinateSpaceValues.CapturePixels]);
        AssertSchemaPropertyType(properties, "selector", "object");
        AssertSchemaPropertyType(properties, "point", "object");
    }

    [Fact]
    public void ComputerUseWinClickToolSchemaRequiresStateTokenAndExactlyOneSelector()
    {
        JsonElement inputSchema = GetComputerUseWinInputSchema(ToolNames.ComputerUseWinClick);
        JsonElement[] selectorModes = GetSchemaBranches(inputSchema, "oneOf");

        AssertRequiredProperties(inputSchema, ["stateToken"]);
        Assert.Equal(3, selectorModes.Length);
        Assert.Contains(selectorModes, mode => RequiresSchemaProperty(mode, "elementIndex"));
        Assert.Contains(selectorModes, mode => RequiresSchemaProperty(mode, "selector"));
        Assert.Contains(selectorModes, mode => RequiresSchemaProperty(mode, "point"));
    }

    [Fact]
    public void ComputerUseWinClickToolSchemaRejectsWhitespaceOnlyStateToken()
    {
        JsonElement properties = GetComputerUseWinInputSchemaProperties(ToolNames.ComputerUseWinClick);

        AssertSchemaPropertyPattern(properties, "stateToken", NonBlankJsonStringPattern);
    }

    [Fact]
    public void ComputerUseWinTypeTextToolSchemaExposesFocusedFallbackOptIn()
    {
        JsonElement inputSchema = GetComputerUseWinInputSchema(ToolNames.ComputerUseWinTypeText);
        JsonElement properties = inputSchema.GetProperty("properties");

        AssertRequiredProperties(inputSchema, ["stateToken", "text"]);
        AssertSchemaPropertyType(properties, "allowFocusedFallback", "boolean");
        AssertSchemaPropertyType(properties, "confirm", "boolean");
        AssertSchemaPropertyType(properties, "selector", "object");
        AssertSchemaPropertyType(properties, "point", "object");
        Assert.Equal(
            [InputCoordinateSpaceValues.CapturePixels],
            ReadJsonStringArray(properties.GetProperty("coordinateSpace").GetProperty("enum")));
    }

    [Fact]
    public void ComputerUseWinSelectedActionSchemasExposeObserveAfterOptIn()
    {
        string[] observeAfterTools =
        [
            ToolNames.ComputerUseWinClick,
            ToolNames.ComputerUseWinDrag,
            ToolNames.ComputerUseWinPressKey,
            ToolNames.ComputerUseWinScroll,
            ToolNames.ComputerUseWinTypeText,
        ];

        foreach (string toolName in observeAfterTools)
        {
            AssertSchemaPropertyType(GetComputerUseWinInputSchemaProperties(toolName), "observeAfter", "boolean");
        }

        Assert.False(GetComputerUseWinInputSchemaProperties(ToolNames.ComputerUseWinSetValue).TryGetProperty("observeAfter", out _));
        Assert.False(GetComputerUseWinInputSchemaProperties(ToolNames.ComputerUseWinPerformSecondaryAction).TryGetProperty("observeAfter", out _));
    }

    [Fact]
    public void ComputerUseWinScrollToolSchemaBoundsPagesAndRequiresNonNullSelectorBranches()
    {
        JsonElement inputSchema = GetComputerUseWinInputSchema(ToolNames.ComputerUseWinScroll);
        JsonElement properties = inputSchema.GetProperty("properties");
        JsonElement[] selectorModes = GetSchemaBranches(inputSchema, "oneOf");
        JsonElement elementBranch = selectorModes.Single(mode => RequiresSchemaProperty(mode, "elementIndex"));
        JsonElement selectorBranch = selectorModes.Single(mode => RequiresSchemaProperty(mode, "selector"));
        JsonElement pointBranch = selectorModes.Single(mode => RequiresSchemaProperty(mode, "point"));

        Assert.Equal(10, properties.GetProperty("pages").GetProperty("maximum").GetInt32());
        AssertSchemaPropertyType(properties, "point", "object");
        AssertBranchPropertyType(elementBranch, "elementIndex", "integer");
        AssertBranchPropertyType(selectorBranch, "selector", "object");
        AssertBranchPropertyType(pointBranch, "point", "object");
    }

    [Fact]
    public void ComputerUseWinDragToolSchemaRequiresStateTokenAndSeparateSourceDestinationModes()
    {
        JsonElement inputSchema = GetComputerUseWinInputSchema(ToolNames.ComputerUseWinDrag);
        JsonElement properties = inputSchema.GetProperty("properties");

        AssertRequiredProperties(inputSchema, ["stateToken"]);
        Assert.True(inputSchema.TryGetProperty("allOf", out JsonElement allOf));
        JsonElement[] selectorModes = [.. allOf.EnumerateArray()];
        Assert.Equal(2, selectorModes.Length);
        Assert.All(
            selectorModes,
            mode => Assert.True(mode.TryGetProperty("oneOf", out _)));

        JsonElement sourceBranch = GetSchemaBranchRequiring(selectorModes[0].GetProperty("oneOf"), "fromElementIndex");
        JsonElement sourceSelectorBranch = GetSchemaBranchRequiring(selectorModes[0].GetProperty("oneOf"), "fromSelector");
        JsonElement sourcePointBranch = GetSchemaBranchRequiring(selectorModes[0].GetProperty("oneOf"), "fromPoint");
        JsonElement destinationBranch = GetSchemaBranchRequiring(selectorModes[1].GetProperty("oneOf"), "toElementIndex");
        JsonElement destinationSelectorBranch = GetSchemaBranchRequiring(selectorModes[1].GetProperty("oneOf"), "toSelector");
        JsonElement destinationPointBranch = GetSchemaBranchRequiring(selectorModes[1].GetProperty("oneOf"), "toPoint");

        AssertBranchPropertyType(sourceBranch, "fromElementIndex", "integer");
        AssertBranchPropertyType(sourceSelectorBranch, "fromSelector", "object");
        AssertBranchPropertyType(sourcePointBranch, "fromPoint", "object");
        AssertBranchPropertyType(destinationBranch, "toElementIndex", "integer");
        AssertBranchPropertyType(destinationSelectorBranch, "toSelector", "object");
        AssertBranchPropertyType(destinationPointBranch, "toPoint", "object");
        AssertSchemaPropertyType(properties, "fromSelector", "object");
        AssertSchemaPropertyType(properties, "fromPoint", "object");
        AssertSchemaPropertyType(properties, "toSelector", "object");
        AssertSchemaPropertyType(properties, "toPoint", "object");
        AssertSchemaPropertyPattern(properties, "stateToken", NonBlankJsonStringPattern);
    }

    [Fact]
    public void ComputerUseWinSecondaryActionToolSchemaRequiresStateTokenAndSemanticTarget()
    {
        JsonElement inputSchema = GetComputerUseWinInputSchema(ToolNames.ComputerUseWinPerformSecondaryAction);
        JsonElement properties = inputSchema.GetProperty("properties");
        JsonElement[] selectorModes = GetSchemaBranches(inputSchema, "oneOf");

        AssertRequiredProperties(inputSchema, ["stateToken"]);
        Assert.Contains(selectorModes, mode => RequiresSchemaProperty(mode, "elementIndex"));
        Assert.Contains(selectorModes, mode => RequiresSchemaProperty(mode, "selector"));
        Assert.Equal(1, properties.GetProperty("elementIndex").GetProperty("minimum").GetInt32());
        AssertSchemaPropertyType(properties, "selector", "object");
        Assert.False(properties.TryGetProperty("point", out _));
    }

    [Theory]
    [InlineData(ToolNames.ComputerUseWinSetValue)]
    [InlineData(ToolNames.ComputerUseWinPerformSecondaryAction)]
    [InlineData(ToolNames.ComputerUseWinScroll)]
    public void ComputerUseWinSemanticActionSchemasExposeBoundedSemanticSelector(string toolName)
    {
        JsonElement properties = GetComputerUseWinInputSchemaProperties(toolName);

        Assert.True(properties.TryGetProperty("selector", out JsonElement selector), $"{toolName} должен публиковать bounded semantic selector.");
        Assert.Equal("object", selector.GetProperty("type").GetString());
        JsonElement selectorProperties = selector.GetProperty("properties");
        Assert.True(selectorProperties.TryGetProperty("automationId", out _), "selector должен поддерживать AutomationId.");
        Assert.True(selectorProperties.TryGetProperty("controlType", out _), "selector должен поддерживать ControlType.");
        Assert.True(selectorProperties.TryGetProperty("name", out _), "selector должен поддерживать optional Name.");
    }

    [Theory]
    [InlineData(ToolNames.ComputerUseWinClick, "selector")]
    [InlineData(ToolNames.ComputerUseWinSetValue, "selector")]
    [InlineData(ToolNames.ComputerUseWinTypeText, "selector")]
    [InlineData(ToolNames.ComputerUseWinScroll, "selector")]
    [InlineData(ToolNames.ComputerUseWinPerformSecondaryAction, "selector")]
    [InlineData(ToolNames.ComputerUseWinDrag, "fromSelector")]
    [InlineData(ToolNames.ComputerUseWinDrag, "toSelector")]
    public void ComputerUseWinSelectorSchemasDoNotAdvertiseNullCriteria(string toolName, string selectorPropertyName)
    {
        JsonElement selector = GetComputerUseWinInputSchemaProperties(toolName).GetProperty(selectorPropertyName);
        JsonElement selectorProperties = selector.GetProperty("properties");

        foreach (string criterionName in new[] { "name", "automationId", "controlType" })
        {
            JsonElement criterion = selectorProperties.GetProperty(criterionName);
            Assert.Equal("string", criterion.GetProperty("type").GetString());
            Assert.Equal(NonBlankJsonStringPattern, criterion.GetProperty("pattern").GetString());
        }
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
        JsonElement inputSchema = GetComputerUseWinInputSchema(ToolNames.ComputerUseWinGetAppState);

        Assert.Equal(["windowId", "hwnd"], ReadJsonStringArray(inputSchema.GetProperty("not").GetProperty("required")));
    }

    [Fact]
    public void ComputerUseWinGetAppStateToolSchemaRejectsWhitespaceOnlyWindowId()
    {
        JsonElement properties = GetComputerUseWinInputSchemaProperties(ToolNames.ComputerUseWinGetAppState);

        AssertSchemaPropertyPattern(properties, "windowId", NonBlankJsonStringPattern);
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
        Dictionary<string, JsonElement> arguments = CreateToolArguments("""{"actions":[],"unexpected":true}""");

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
        using TempDirectoryScope temp = new();
        string storePath = Path.Combine(temp.Root, "AppApprovals.json");
        File.WriteAllText(storePath, "{not valid json");
        ComputerUseWinApprovalStore store = new(CreateComputerUseWinOptions(temp.Root, approvalStorePath: storePath));

        Assert.False(store.IsApproved("explorer"));

        store.Approve("explorer");

        string json = File.ReadAllText(storePath);
        string[] values = JsonSerializer.Deserialize<string[]>(json)!;
        Assert.Contains("explorer", values, StringComparer.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFiles(temp.Root, "*.tmp", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public void ApprovalStoreDoesNotThrowWhenPersistPathCannotBeReplaced()
    {
        using TempDirectoryScope temp = new();
        string unwritableStorePath = Path.Combine(temp.Root, "approval-store-as-directory");
        Directory.CreateDirectory(unwritableStorePath);
        ComputerUseWinApprovalStore store = new(CreateComputerUseWinOptions(temp.Root, approvalStorePath: unwritableStorePath));

        store.Approve("explorer");

        Assert.True(store.IsApproved("explorer"));
        Assert.Empty(Directory.EnumerateFiles(temp.Root, "*.tmp", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public void ReadmeUsesPublishFlowInsteadOfRemovedComputerUseWinRepoRootHint()
    {
        string readme = File.ReadAllText(ResolveRepoPath("README.md"));

        Assert.DoesNotContain("write-computer-use-win-plugin-repo-root-hint.ps1", readme, StringComparison.Ordinal);
        Assert.Contains("publish-computer-use-win-plugin.ps1", readme, StringComparison.Ordinal);
    }

    private static UiaElementSnapshot CreateEnabledUiaElement(
        string elementId,
        string controlType,
        Bounds bounds,
        bool hasKeyboardFocus = false,
        bool? isReadOnly = null,
        string[]? patterns = null)
    {
        return new UiaElementSnapshot
        {
            ElementId = elementId,
            ControlType = controlType,
            BoundingRectangle = bounds,
            IsEnabled = true,
            IsOffscreen = false,
            HasKeyboardFocus = hasKeyboardFocus,
            IsReadOnly = isReadOnly,
            Patterns = patterns ?? [],
        };
    }

    private static IReadOnlyList<string> ResolveAffordances(UiaElementSnapshot element) =>
        ComputerUseWinAffordanceResolver.Resolve(element);

    private static ToolContractProfile GetComputerUseWinProfile() =>
        ComputerUseWinProfile.Value;

    private static string[] GetPublishedComputerUseWinToolNames() =>
        PublishedComputerUseWinToolNames.Value;

    private static string[] GetComputerUseWinToolFactoryMethodNames() =>
        ComputerUseWinToolFactoryMethodNames.Value;

    private static void AssertValidationFailure(string? failure, params string[] expectedFragments)
    {
        Assert.False(string.IsNullOrWhiteSpace(failure));

        foreach (string expectedFragment in expectedFragments)
        {
            Assert.Contains(expectedFragment, failure, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static void AssertFailureReasonContains(string? reason, params string[] expectedFragments)
    {
        Assert.False(string.IsNullOrWhiteSpace(reason));

        foreach (string expectedFragment in expectedFragments)
        {
            Assert.Contains(expectedFragment, reason, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static void AssertFailureReasonDoesNotContain(string? reason, string unexpectedFragment)
    {
        Assert.False(string.IsNullOrWhiteSpace(reason));
        Assert.DoesNotContain(unexpectedFragment, reason, StringComparison.OrdinalIgnoreCase);
    }

    private static string? BindInvalidRequestAndAssertFallback<TRequest>(
        Dictionary<string, JsonElement> arguments,
        TRequest fallbackRequest,
        Func<TRequest, string?>? validate = null)
    {
        TRequest request;
        string? reason;
        bool success = validate is null
            ? ToolRequestBinder.TryBind(arguments, fallbackRequest, out request, out reason)
            : ToolRequestBinder.TryBind(arguments, fallbackRequest, out request, out reason, validate);

        Assert.False(success);
        Assert.Equal(fallbackRequest, request);

        return reason;
    }

    private static JsonElement GetComputerUseWinInputSchema(string toolName) =>
        ComputerUseWinInputSchemasByToolName.Value[toolName];

    private static JsonElement GetComputerUseWinInputSchemaProperties(string toolName) =>
        GetComputerUseWinInputSchema(toolName).GetProperty("properties");

    private static void AssertNullableStringEnum(JsonElement schemaProperty, string[] expectedValues)
    {
        JsonElement type = schemaProperty.GetProperty("type");

        Assert.Equal("string", type[0].GetString());
        Assert.Equal("null", type[1].GetString());
        Assert.Equal(expectedValues, ReadJsonStringArray(schemaProperty.GetProperty("enum")));
    }

    private static void AssertRequiredProperties(JsonElement inputSchema, string[] expectedProperties) =>
        Assert.Equal(expectedProperties, ReadJsonStringArray(inputSchema.GetProperty("required")));

    private static void AssertSchemaPropertyPattern(JsonElement properties, string propertyName, string expectedPattern) =>
        Assert.Equal(expectedPattern, properties.GetProperty(propertyName).GetProperty("pattern").GetString());

    private static void AssertSchemaPropertyType(JsonElement properties, string propertyName, string expectedType) =>
        Assert.Equal(expectedType, properties.GetProperty(propertyName).GetProperty("type").GetString());

    private static void AssertBranchPropertyType(JsonElement branch, string propertyName, string expectedType) =>
        AssertSchemaPropertyType(branch.GetProperty("properties"), propertyName, expectedType);

    private static JsonElement[] GetSchemaBranches(JsonElement schema, string compositionKeyword) =>
        [.. schema.GetProperty(compositionKeyword).EnumerateArray()];

    private static JsonElement GetSchemaBranchRequiring(JsonElement branches, string requiredPropertyName) =>
        branches.EnumerateArray().Single(mode => RequiresSchemaProperty(mode, requiredPropertyName));

    private static bool RequiresSchemaProperty(JsonElement schema, string propertyName) =>
        schema.GetProperty("required").EnumerateArray().Any(item => item.GetString() == propertyName);

    private static string[] ReadJsonStringArray(JsonElement array) =>
        array.EnumerateArray()
            .Select(static item => item.GetString())
            .Where(static item => item is not null)
            .Cast<string>()
            .ToArray();


    private static ServiceProvider BuildComputerUseWinServiceProviderForResolutionTest(string root)
    {
        ServiceCollection services = new();

        services.AddSingleton(CreateAuditLog(root));
        services.AddSingleton<ISessionManager>(new InMemorySessionManager(TimeProvider.System, new SessionContext("computer-use-win-stage-2-service-graph")));
        services.AddSingleton<IWindowManager>(new ServiceGraphWindowManager());
        services.AddSingleton<IWindowActivationService>(new FakeWindowActivationService(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true)));
        services.AddSingleton<ICaptureService>(new NoopCaptureService());
        services.AddSingleton<IUiAutomationService>(new FakeUiAutomationService());
        services.AddSingleton<IUiAutomationSetValueService>(new FakeUiAutomationSetValueService());
        services.AddSingleton<IInputService>(new FakeInputService());
        services.AddSingleton(CreateComputerUseWinOptions(root));
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
            provider.GetRequiredService<IUiAutomationSemanticLookupService>(),
            provider.GetRequiredService<IUiAutomationSetValueService>()));
        services.AddSingleton(static provider => new ComputerUseWinTypeTextExecutionCoordinator(
            provider.GetRequiredService<IWindowActivationService>(),
            provider.GetRequiredService<IUiAutomationService>(),
            provider.GetRequiredService<IInputService>()));
        services.AddSingleton<IUiAutomationScrollService>(new FakeUiAutomationScrollService());
        services.AddSingleton<IUiAutomationSemanticLookupService>(new FakeUiAutomationSemanticLookupService());
        services.AddSingleton(static provider => new ComputerUseWinScrollExecutionCoordinator(
            provider.GetRequiredService<IWindowActivationService>(),
            provider.GetRequiredService<IUiAutomationService>(),
            provider.GetRequiredService<IUiAutomationSemanticLookupService>(),
            provider.GetRequiredService<IUiAutomationScrollService>(),
            provider.GetRequiredService<IInputService>()));
        services.AddSingleton<IUiAutomationSecondaryActionService>(new FakeUiAutomationSecondaryActionService());
        services.AddSingleton(static provider => new ComputerUseWinPerformSecondaryActionExecutionCoordinator(
            provider.GetRequiredService<IWindowActivationService>(),
            provider.GetRequiredService<IUiAutomationService>(),
            provider.GetRequiredService<IUiAutomationSemanticLookupService>(),
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

        return services.BuildServiceProvider();
    }

    private static void AssertServiceResolves<TService>(IServiceProvider provider)
        where TService : notnull =>
        Assert.IsType<TService>(provider.GetRequiredService<TService>());

    private static string CreateAppInstructionsRoot(string root)
    {
        string instructionsRoot = Path.Combine(root, "references", "AppInstructions");
        Directory.CreateDirectory(instructionsRoot);
        return instructionsRoot;
    }

    private static ComputerUseWinPlaybookProvider CreatePlaybookProvider(string root, string instructionsRoot) =>
        new(CreateComputerUseWinOptions(root, appInstructionsRoot: instructionsRoot));

    private static ComputerUseWinOptions CreateComputerUseWinOptions(
        string root,
        string? appInstructionsRoot = null,
        string? approvalStorePath = null) =>
        new(
            PluginRoot: root,
            AppInstructionsRoot: appInstructionsRoot ?? Path.Combine(root, "references", "AppInstructions"),
            ApprovalStorePath: approvalStorePath ?? Path.Combine(root, "AppApprovals.json"));

    private static Dictionary<string, JsonElement> CreateToolArguments(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        Dictionary<string, JsonElement> arguments = new(StringComparer.Ordinal);

        foreach (JsonProperty property in document.RootElement.EnumerateObject())
        {
            arguments[property.Name] = property.Value.Clone();
        }

        return arguments;
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
