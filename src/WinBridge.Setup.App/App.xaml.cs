using Microsoft.UI.Xaml.Navigation;
using WinBridge.Setup.App.Views;

namespace WinBridge.Setup.App
{
    public partial class App : Application
    {
        private Window window = Window.Current;

        public App()
        {
            this.InitializeComponent();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            window ??= new Window();
            window.Title = "Okno Setup";

            if (window.Content is not Frame rootFrame)
            {
                rootFrame = new Frame();
                rootFrame.NavigationFailed += OnNavigationFailed;
                window.Content = rootFrame;
            }

            _ = rootFrame.Navigate(typeof(MainPage), args.Arguments);
            window.Activate();
        }

        void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new InvalidOperationException("Failed to load page " + e.SourcePageType.FullName);
        }
    }
}
