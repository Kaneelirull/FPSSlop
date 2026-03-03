using System.Windows;

namespace FPSSlop
{
    public partial class App : Application
    {
        private TrayController? _tray;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _tray = new TrayController();
            _tray.Initialize();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _tray?.Dispose();
            base.OnExit(e);
        }
    }
}
