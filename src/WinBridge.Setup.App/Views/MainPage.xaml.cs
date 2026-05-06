using System.IO;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI;
using WinBridge.Setup.Core;

namespace WinBridge.Setup.App.Views;

public partial class MainPage : Page
{
    private readonly SetupShellController controller = new();
    private readonly SetupLocalizationService localization = new();
    private Window? window;
    private SetupShellInstallSummary? lastInstallSummary;
    private bool showingFailureResult;

    public MainPage()
    {
        try
        {
            InitializeComponent();
            InitializeLanguageSelector();
            CodexModeRadioButton.Checked += OnInstallModeChanged;
            RuntimeOnlyModeRadioButton.Checked += OnInstallModeChanged;
            Loaded += OnLoaded;
            ActualThemeChanged += OnActualThemeChanged;

            ApplyLocalizedStrings();
            ApplyStatusSnapshot(controller.GetStatusSnapshot());
            UpdateModePresentation();
        }
        catch (Exception ex)
        {
            WriteStartupLog("MainPage.ctor", ex);
            throw;
        }
    }

    public void AttachWindow(Window ownerWindow)
    {
        window = ownerWindow;
        ApplyLocalizedStrings();
        ApplyWindowTheme();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyWindowTheme();
    }

    private void InitializeLanguageSelector()
    {
        LanguageComboBox.DisplayMemberPath = nameof(SetupLanguageOption.DisplayName);
        LanguageComboBox.SelectedValuePath = nameof(SetupLanguageOption.Tag);
        LanguageComboBox.ItemsSource = SetupLocalizationService.SupportedLanguages;
        LanguageComboBox.SelectedValue = localization.CurrentLanguageTag;
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        ApplyWindowTheme();
    }

    private async void OnInstallClicked(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        ClearResult();
        try
        {
            SetupShellInstallSummary summary = await controller.InstallAsync(GetSelectedMode());
            lastInstallSummary = summary;
            showingFailureResult = false;
            ApplyResult(summary);
            ApplyStatusSnapshot(controller.GetStatusSnapshot());
            UpdateModePresentation();
        }
        catch (Exception ex)
        {
            ResultPanel.Visibility = Visibility.Visible;
            lastInstallSummary = null;
            showingFailureResult = true;
            ResultTitleTextBlock.Text = L("ResultFailureTitle");
            ResultMessageTextBlock.Text = ex.Message;
            RuntimePathTextBlock.Text = string.Empty;
            PluginPathTextBlock.Text = string.Empty;
            MarketplacePathTextBlock.Text = string.Empty;
            PluginPathTextBlock.Visibility = Visibility.Collapsed;
            MarketplacePathTextBlock.Visibility = Visibility.Collapsed;
            SnippetPanel.Visibility = Visibility.Collapsed;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OnCopySnippetClicked(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SnippetTextBox.Text))
        {
            return;
        }

        DataPackage package = new();
        package.SetText(SnippetTextBox.Text);
        Clipboard.SetContent(package);
        ResultMessageTextBlock.Text = L("SnippetCopiedMessage");
    }

    private void OnInstallModeChanged(object sender, RoutedEventArgs e)
    {
        UpdateModePresentation();
    }

    private void OnLanguageSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LanguageComboBox.SelectedItem is not SetupLanguageOption option)
        {
            return;
        }

        localization.SetLanguage(option.Tag);
        ApplyLocalizedStrings();
        ApplyStatusSnapshot(controller.GetStatusSnapshot());
        UpdateModePresentation();

        if (lastInstallSummary is not null)
        {
            ApplyResult(lastInstallSummary);
        }
        else if (showingFailureResult)
        {
            ResultTitleTextBlock.Text = L("ResultFailureTitle");
        }
    }

    private ComputerUseWinInstallMode GetSelectedMode()
    {
        return RuntimeOnlyModeRadioButton.IsChecked == true
            ? ComputerUseWinInstallMode.RuntimeOnly
            : ComputerUseWinInstallMode.Codex;
    }

    private void ApplyStatusSnapshot(SetupShellStatusSnapshot snapshot)
    {
        CodexHomeTextBlock.Text = snapshot.CodexHome;
        RuntimeStoreTextBlock.Text = snapshot.RuntimeStoreRoot;
        PluginRootTextBlock.Text = snapshot.PluginSourceRoot;
        MarketplaceTextBlock.Text = snapshot.MarketplacePath;

        if (snapshot.CodexInstalled)
        {
            StatusTitleTextBlock.Text = L("StatusCodexInstalledTitle");
            StatusDetailTextBlock.Text = L("StatusCodexInstalledDetail");
        }
        else if (snapshot.RuntimeReady)
        {
            StatusTitleTextBlock.Text = L("StatusRuntimeReadyTitle");
            StatusDetailTextBlock.Text = L("StatusRuntimeReadyDetail");
        }
        else
        {
            StatusTitleTextBlock.Text = L("StatusRecommendedTitle");
            StatusDetailTextBlock.Text = L("StatusRecommendedDetail");
        }
    }

    private void ApplyResult(SetupShellInstallSummary summary)
    {
        ResultPanel.Visibility = Visibility.Visible;
        bool isRuntimeOnly = !string.IsNullOrWhiteSpace(summary.Snippet);
        ResultTitleTextBlock.Text = isRuntimeOnly ? L("ResultRuntimeTitle") : L("ResultCodexTitle");
        ResultMessageTextBlock.Text = isRuntimeOnly ? L("ResultRuntimeMessage") : L("ResultCodexMessage");
        RuntimePathTextBlock.Text = $"{L("RuntimeRootLabel")}: {summary.RuntimeRoot}";

        if (string.IsNullOrWhiteSpace(summary.PluginSourceRoot))
        {
            PluginPathTextBlock.Visibility = Visibility.Collapsed;
        }
        else
        {
            PluginPathTextBlock.Visibility = Visibility.Visible;
            PluginPathTextBlock.Text = $"{L("PluginRootLabel")}: {summary.PluginSourceRoot}";
        }

        if (string.IsNullOrWhiteSpace(summary.MarketplacePath))
        {
            MarketplacePathTextBlock.Visibility = Visibility.Collapsed;
        }
        else
        {
            MarketplacePathTextBlock.Visibility = Visibility.Visible;
            MarketplacePathTextBlock.Text = $"{L("MarketplaceFileLabel")}: {summary.MarketplacePath}";
        }

        if (string.IsNullOrWhiteSpace(summary.Snippet))
        {
            SnippetPanel.Visibility = Visibility.Collapsed;
            SnippetTextBox.Text = string.Empty;
        }
        else
        {
            SnippetPanel.Visibility = Visibility.Visible;
            SnippetTextBox.Text = summary.Snippet;
        }
    }

    private void ClearResult()
    {
        ResultPanel.Visibility = Visibility.Collapsed;
        ResultTitleTextBlock.Text = string.Empty;
        ResultMessageTextBlock.Text = string.Empty;
        RuntimePathTextBlock.Text = string.Empty;
        PluginPathTextBlock.Text = string.Empty;
        MarketplacePathTextBlock.Text = string.Empty;
        SnippetTextBox.Text = string.Empty;
        PluginPathTextBlock.Visibility = Visibility.Collapsed;
        MarketplacePathTextBlock.Visibility = Visibility.Collapsed;
        SnippetPanel.Visibility = Visibility.Collapsed;
        showingFailureResult = false;
    }

    private void UpdateModePresentation()
    {
        bool isCodexMode = GetSelectedMode() == ComputerUseWinInstallMode.Codex;

        ModeSummaryTitleTextBlock.Text = isCodexMode
            ? L("ModeSummaryCodexTitle")
            : L("ModeSummaryRuntimeTitle");
        ModeSummaryDetailTextBlock.Text = isCodexMode
            ? L("ModeSummaryCodexDetail")
            : L("ModeSummaryRuntimeDetail");

        Visibility codexOnlyVisibility = isCodexMode ? Visibility.Visible : Visibility.Collapsed;
        CodexHomeLabelTextBlock.Visibility = codexOnlyVisibility;
        CodexHomeTextBlock.Visibility = codexOnlyVisibility;
        PluginRootLabelTextBlock.Visibility = codexOnlyVisibility;
        PluginRootTextBlock.Visibility = codexOnlyVisibility;
        MarketplaceLabelTextBlock.Visibility = codexOnlyVisibility;
        MarketplaceTextBlock.Visibility = codexOnlyVisibility;

        FooterHintTextBlock.Text = isCodexMode
            ? L("FooterCodexHint")
            : L("FooterRuntimeHint");
        InstallButton.Content = isCodexMode ? L("InstallCodexButton") : L("InstallRuntimeOnlyButton");
    }

    private void SetBusy(bool isBusy)
    {
        InstallButton.IsEnabled = !isBusy;
        CodexModeRadioButton.IsEnabled = !isBusy;
        RuntimeOnlyModeRadioButton.IsEnabled = !isBusy;
        InstallProgressRing.IsActive = isBusy;
        InstallProgressRing.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyWindowTheme()
    {
        if (window is null || !AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

        ElementTheme actualTheme = ActualTheme;
        AppWindowTitleBar nativeTitleBar = window.AppWindow.TitleBar;
        nativeTitleBar.ForegroundColor = ResolveColor(
            "WindowCaptionForeground",
            actualTheme == ElementTheme.Dark ? Colors.White : Colors.Black);
        nativeTitleBar.InactiveForegroundColor = ResolveColor(
            "WindowCaptionForegroundDisabled",
            actualTheme == ElementTheme.Dark
                ? ColorHelper.FromArgb(0xFF, 0xAA, 0xAA, 0xAA)
                : ColorHelper.FromArgb(0xFF, 0x7A, 0x7A, 0x7A));
        nativeTitleBar.BackgroundColor = ResolveColor(
            "LayerFillColorDefault",
            actualTheme == ElementTheme.Dark
                ? ColorHelper.FromArgb(0xFF, 0x20, 0x20, 0x20)
                : ColorHelper.FromArgb(0xFF, 0xF3, 0xF3, 0xF3));
        nativeTitleBar.InactiveBackgroundColor = nativeTitleBar.BackgroundColor;
        nativeTitleBar.ButtonBackgroundColor = nativeTitleBar.BackgroundColor;
        nativeTitleBar.ButtonInactiveBackgroundColor = nativeTitleBar.BackgroundColor;
        nativeTitleBar.ButtonForegroundColor = nativeTitleBar.ForegroundColor;
        nativeTitleBar.ButtonInactiveForegroundColor = nativeTitleBar.InactiveForegroundColor;
        nativeTitleBar.ButtonHoverBackgroundColor = ResolveColor(
            "WindowCaptionButtonBackgroundPointerOver",
            actualTheme == ElementTheme.Dark
                ? ColorHelper.FromArgb(0x33, 0xFF, 0xFF, 0xFF)
                : ColorHelper.FromArgb(0x14, 0x00, 0x00, 0x00));
        nativeTitleBar.ButtonPressedBackgroundColor = ResolveColor(
            "WindowCaptionButtonBackgroundPressed",
            actualTheme == ElementTheme.Dark
                ? ColorHelper.FromArgb(0x24, 0xFF, 0xFF, 0xFF)
                : ColorHelper.FromArgb(0x24, 0x00, 0x00, 0x00));
        nativeTitleBar.ButtonHoverForegroundColor = nativeTitleBar.ForegroundColor;
        nativeTitleBar.ButtonPressedForegroundColor = nativeTitleBar.ForegroundColor;
    }

    private void ApplyLocalizedStrings()
    {
        if (window is not null)
        {
            window.Title = L("WindowTitle");
        }

        MainTitleTextBlock.Text = L("MainTitle");
        MainSubtitleTextBlock.Text = L("MainSubtitle");
        LanguageLabelTextBlock.Text = L("LanguageLabel");
        InstallModeSectionTitleTextBlock.Text = L("InstallModeSectionTitle");
        InstallModeSectionDescriptionTextBlock.Text = L("InstallModeSectionDescription");
        CodexModeRadioButton.Content = L("CodexModeTitle");
        CodexModeDescriptionTextBlock.Text = L("CodexModeDescription");
        RuntimeOnlyModeRadioButton.Content = L("RuntimeOnlyModeTitle");
        RuntimeOnlyModeDescriptionTextBlock.Text = L("RuntimeOnlyModeDescription");
        ModeSummarySectionTitleTextBlock.Text = L("ModeSummarySectionTitle");
        CodexHomeLabelTextBlock.Text = L("CodexHomeLabel");
        RuntimeStoreLabelTextBlock.Text = L("RuntimeStoreLabel");
        PluginRootLabelTextBlock.Text = L("PluginRootLabel");
        MarketplaceLabelTextBlock.Text = L("MarketplaceFileLabel");
        SnippetTitleTextBlock.Text = L("SnippetTitle");
        CopySnippetButton.Content = L("CopySnippetButton");

        LanguageComboBox.Header = null;
        LanguageComboBox.PlaceholderText = L("LanguageSelectorPlaceholder");
        LanguageComboBox.SetValue(Microsoft.UI.Xaml.Automation.AutomationProperties.NameProperty, L("LanguageSelectorAutomationName"));
        InstallButton.SetValue(Microsoft.UI.Xaml.Automation.AutomationProperties.NameProperty, L("InstallButtonAutomationName"));
        CodexModeRadioButton.SetValue(Microsoft.UI.Xaml.Automation.AutomationProperties.NameProperty, L("CodexModeTitle"));
        RuntimeOnlyModeRadioButton.SetValue(Microsoft.UI.Xaml.Automation.AutomationProperties.NameProperty, L("RuntimeOnlyModeTitle"));
        CopySnippetButton.SetValue(Microsoft.UI.Xaml.Automation.AutomationProperties.NameProperty, L("CopySnippetButton"));
    }

    private string L(string resourceKey) => localization.GetString(resourceKey);

    private static Color ResolveColor(string resourceKey, Color fallback)
    {
        if (Application.Current.Resources.TryGetValue(resourceKey, out object value)
            && value is SolidColorBrush brush)
        {
            return brush.Color;
        }

        return fallback;
    }

    private static void WriteStartupLog(string stage, Exception exception)
    {
        string path = Path.Combine(Path.GetTempPath(), "okno-setup-startup.log");
        File.AppendAllText(
            path,
            $"[{DateTimeOffset.Now:O}] {stage}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
    }
}
