using System.Text.Json;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Tooling;
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
        using JsonDocument document = JsonDocument.Parse("""{"point":{"x":10,"y":20,"extra":true}}""");
        Dictionary<string, JsonElement> arguments = new(StringComparer.Ordinal)
        {
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
        using JsonDocument document = JsonDocument.Parse("""{"point":{"x":10,"y":20},"coordinateSpace":"bogus","confirm":true}""");
        Dictionary<string, JsonElement> arguments = new(StringComparer.Ordinal)
        {
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
        using JsonDocument document = JsonDocument.Parse("""{"elementIndex":1,"button":"middle","confirm":true}""");
        Dictionary<string, JsonElement> arguments = new(StringComparer.Ordinal)
        {
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

    private static ComputerUseWinStoredState CreateStoredState(DateTimeOffset capturedAtUtc) =>
        new(
            new ComputerUseWinAppSession("explorer", 101, "Explorer", "explorer", 1001),
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

    private static WindowDescriptor CreateWindow(string processName) =>
        new(
            Hwnd: 101,
            Title: "Test window",
            ProcessName: processName,
            ProcessId: 1001,
            ThreadId: 2002,
            ClassName: "TestWindow",
            Bounds: new Bounds(0, 0, 640, 480),
            IsForeground: true,
            IsVisible: true);

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;

        public override DateTimeOffset GetUtcNow() => current;

        public void Advance(TimeSpan delta) => current = current.Add(delta);
    }
}
