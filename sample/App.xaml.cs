using System;
using System.Windows;

namespace HttpsSample
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
        }

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            RootVisual = new MainPage();
        }

        private void Application_UnhandledException(object sender, ApplicationUnhandledExceptionEventArgs e)
        {
            // Keep running and show what went wrong rather than letting the
            // host quit to the dashboard.
            e.Handled = true;
            MainPage page = RootVisual as MainPage;
            if (page != null)
            {
                page.ShowError(e.ExceptionObject);
            }
        }
    }
}
