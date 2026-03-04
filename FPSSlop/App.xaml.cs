using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace FPSSlop
{
    public partial class App : Application
    {
        private TrayController? _tray;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Catch all unhandled exceptions and log them instead of silently dying
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            _tray = new TrayController();
            _tray.Initialize();
        }

        private static void LogCrash(string source, Exception ex)
        {
            try
            {
                string logDir  = Path.Combine(Path.GetTempPath(), "FPSSlop");
                Directory.CreateDirectory(logDir);
                string logFile = Path.Combine(logDir, "crash.log");
                File.AppendAllText(logFile,
                    $"\n[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}\n{ex}\n");
            }
            catch { }
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LogCrash("DispatcherUnhandledException", e.Exception);
            e.Handled = true; // prevent shutdown
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
                LogCrash("AppDomain.UnhandledException", ex);
        }

        private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            LogCrash("UnobservedTaskException", e.Exception);
            e.SetObserved(); // prevent shutdown
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _tray?.Dispose();
            base.OnExit(e);
        }
    }
}
