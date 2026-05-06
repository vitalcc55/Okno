using Windows.ApplicationModel.DataTransfer;
using WinBridge.Setup.Core;

namespace WinBridge.Setup.App.Views;

public partial class MainPage : Page
{
    private readonly SetupShellController controller = new();

    public MainPage()
    {
        InitializeComponent();
        ApplyStatusSnapshot(controller.GetStatusSnapshot());
    }

    private async void OnInstallClicked(object sender, RoutedEventArgs e)
    {
        ComputerUseWinInstallMode selectedMode = RuntimeOnlyModeRadioButton.IsChecked == true
            ? ComputerUseWinInstallMode.RuntimeOnly
            : ComputerUseWinInstallMode.Codex;

        SetBusy(true);
        ClearResult();
        try
        {
            SetupShellInstallSummary summary = await controller.InstallAsync(selectedMode);
            ApplyResult(summary);
            ApplyStatusSnapshot(controller.GetStatusSnapshot());
        }
        catch (Exception ex)
        {
            ResultBorder.Visibility = Visibility.Visible;
            ResultTitleTextBlock.Text = "Installation failed.";
            ResultMessageTextBlock.Text = ex.Message;
            RuntimePathTextBlock.Text = string.Empty;
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
        ResultMessageTextBlock.Text = "Snippet copied to clipboard.";
    }

    private void ApplyStatusSnapshot(SetupShellStatusSnapshot snapshot)
    {
        StatusHeadlineTextBlock.Text = snapshot.Headline;
        StatusDetailTextBlock.Text = snapshot.Detail;
        CodexHomeTextBlock.Text = $"Codex home: {snapshot.CodexHome}";
        RuntimeStoreTextBlock.Text = $"Runtime store: {snapshot.RuntimeStoreRoot}";
    }

    private void ApplyResult(SetupShellInstallSummary summary)
    {
        ResultBorder.Visibility = Visibility.Visible;
        ResultTitleTextBlock.Text = summary.Title;
        ResultMessageTextBlock.Text = summary.Message;
        RuntimePathTextBlock.Text = $"Runtime root: {summary.RuntimeRoot}";

        if (string.IsNullOrWhiteSpace(summary.PluginSourceRoot))
        {
            PluginPathTextBlock.Visibility = Visibility.Collapsed;
        }
        else
        {
            PluginPathTextBlock.Visibility = Visibility.Visible;
            PluginPathTextBlock.Text = $"Plugin root: {summary.PluginSourceRoot}";
        }

        if (string.IsNullOrWhiteSpace(summary.MarketplacePath))
        {
            MarketplacePathTextBlock.Visibility = Visibility.Collapsed;
        }
        else
        {
            MarketplacePathTextBlock.Visibility = Visibility.Visible;
            MarketplacePathTextBlock.Text = $"Marketplace: {summary.MarketplacePath}";
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
        ResultBorder.Visibility = Visibility.Collapsed;
        ResultTitleTextBlock.Text = string.Empty;
        ResultMessageTextBlock.Text = string.Empty;
        RuntimePathTextBlock.Text = string.Empty;
        PluginPathTextBlock.Text = string.Empty;
        MarketplacePathTextBlock.Text = string.Empty;
        SnippetTextBox.Text = string.Empty;
        PluginPathTextBlock.Visibility = Visibility.Collapsed;
        MarketplacePathTextBlock.Visibility = Visibility.Collapsed;
        SnippetPanel.Visibility = Visibility.Collapsed;
    }

    private void SetBusy(bool isBusy)
    {
        InstallButton.IsEnabled = !isBusy;
        CodexModeRadioButton.IsEnabled = !isBusy;
        RuntimeOnlyModeRadioButton.IsEnabled = !isBusy;
        InstallProgressRing.IsActive = isBusy;
        InstallProgressRing.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
    }
}
