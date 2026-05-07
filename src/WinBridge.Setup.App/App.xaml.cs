using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using System.IO;
using Windows.Graphics;
using WinBridge.Setup.App.Views;

namespace WinBridge.Setup.App;

public partial class App : Application
{
    private static readonly SizeInt32 StartupWindowSize = new(1060, 780);
    private static readonly SizeInt32 MinimumWindowSize = new(760, 640);
    private Window? mainWindow;
    private readonly SetupAppLaunchOptions launchOptions;

    public App(SetupAppLaunchOptions launchOptions)
    {
        this.launchOptions = launchOptions;
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            MainPage mainPage = new(launchOptions);

            mainWindow = new Window
            {
                Title = "Okno Setup",
                Content = mainPage,
            };

            mainPage.AttachWindow(mainWindow);
            ConfigureWindowSizing(mainWindow);
            TryApplyMica(mainWindow);
            mainWindow.Activate();
        }
        catch (Exception ex)
        {
            WriteStartupLog("OnLaunched", ex);
            throw;
        }
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        WriteStartupLog("UnhandledException", e.Exception);
    }

    private static void ConfigureWindowSizing(Window window)
    {
        try
        {
            if (window.AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.PreferredMinimumWidth = MinimumWindowSize.Width;
                presenter.PreferredMinimumHeight = MinimumWindowSize.Height;
            }

            window.AppWindow.Resize(StartupWindowSize);
        }
        catch
        {
        }
    }

    private static void TryApplyMica(Window window)
    {
        try
        {
            window.SystemBackdrop = new MicaBackdrop();
        }
        catch
        {
        }
    }

    private static void WriteStartupLog(string stage, Exception exception)
    {
        string path = Path.Combine(Path.GetTempPath(), "okno-setup-startup.log");
        File.AppendAllText(
            path,
            $"[{DateTimeOffset.Now:O}] {stage}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
    }
}
