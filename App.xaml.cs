using System.Windows;

namespace BatteryWattWidget
{
    public partial class App : Application
    {
        private MainWindow? _mainWindow;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Create the window but never Show() it: no HWND is created,
            // so nothing appears in Alt+Tab or the taskbar. The tray icon lives on its own.
            _mainWindow = new MainWindow();
        }
    }
}
