using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using FPSSlop.Config;
using FPSSlop.Core;
using FPSSlop.UI;
using Application = System.Windows.Application;

namespace FPSSlop
{
    /// <summary>
    /// Owns the system tray icon, overlay window, and metrics collector lifecycle.
    /// </summary>
    public sealed class TrayController : IDisposable
    {
        private NotifyIcon? _trayIcon;
        private OverlayWindow? _overlay;
        private SettingsWindow? _settingsWindow;
        private MetricsCollector? _collector;
        private AppSettings _settings;
        private bool _disposed;

        public TrayController()
        {
            _settings = SettingsManager.Load();
        }

        public void Initialize()
        {
            BuildTrayIcon();
            BuildOverlay();
            StartCollector();
        }

        // ── Tray icon ─────────────────────────────────────────────────────────
        private void BuildTrayIcon()
        {
            _trayIcon = new NotifyIcon
            {
                Text = "FPSSlop",
                Icon = LoadTrayIcon(),
                Visible = true,
                ContextMenuStrip = BuildContextMenu()
            };
        }

        private static Icon LoadTrayIcon()
        {
            // Try to load bundled icon; fall back to a generated one
            try
            {
                var stream = System.Windows.Application.GetResourceStream(
                    new Uri("pack://application:,,,/Assets/Icons/tray.ico"))?.Stream;
                if (stream != null) return new Icon(stream);
            }
            catch { }

            // Fallback: draw a tiny cyan square
            var bmp = new Bitmap(16, 16);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.FromArgb(0, 255, 204));
            return Icon.FromHandle(bmp.GetHicon());
        }

        private ContextMenuStrip BuildContextMenu()
        {
            var menu = new ContextMenuStrip();

            var itemToggle = new ToolStripMenuItem("Hide overlay");
            itemToggle.Click += (_, _) =>
            {
                if (_overlay == null) return;
                bool visible = _overlay.Visibility == Visibility.Visible;
                _overlay.Visibility = visible ? Visibility.Hidden : Visibility.Visible;
                itemToggle.Text = visible ? "Show overlay" : "Hide overlay";
                _settings.OverlayVisible = !visible;
            };

            var itemSettings = new ToolStripMenuItem("Settings…");
            itemSettings.Click += (_, _) => OpenSettings();

            var itemClickThrough = new ToolStripMenuItem("Click-through")
            {
                Checked = _settings.ClickThrough,
                CheckOnClick = true
            };
            itemClickThrough.CheckedChanged += (_, _) =>
            {
                _settings.ClickThrough = itemClickThrough.Checked;
                Application.Current.Dispatcher.Invoke(
                    () => _overlay?.ApplySettings(_settings));
            };

            var itemRestart = new ToolStripMenuItem("Restart overlay");
            itemRestart.Click += (_, _) => RestartOverlay();

            var itemExit = new ToolStripMenuItem("Exit");
            itemExit.Click += (_, _) =>
            {
                SettingsManager.Save(_settings);
                Application.Current.Shutdown();
            };

            menu.Items.AddRange(new ToolStripItem[]
            {
                itemToggle,
                new ToolStripSeparator(),
                itemSettings,
                itemClickThrough,
                itemRestart,
                new ToolStripSeparator(),
                itemExit
            });
            return menu;
        }

        // ── Overlay ───────────────────────────────────────────────────────────
        private void BuildOverlay()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                _overlay = new OverlayWindow(_settings);
                PositionOverlayOnMonitor();
                _overlay.Show();
                if (!_settings.OverlayVisible)
                    _overlay.Visibility = Visibility.Hidden;
            });
        }

        private void PositionOverlayOnMonitor()
        {
            if (_overlay == null) return;
            var screens = Screen.AllScreens;
            int idx = Math.Clamp(_settings.OverlayMonitorIndex, 0, screens.Length - 1);
            var screen = screens[idx];
            _overlay.Left = screen.WorkingArea.Left + _settings.OverlayX;
            _overlay.Top  = screen.WorkingArea.Top  + _settings.OverlayY;
        }

        // ── Metrics collector ─────────────────────────────────────────────────
        private void StartCollector()
        {
            _collector = new MetricsCollector(
                _settings,
                OnMetricsUpdated,
                Application.Current.Dispatcher.Invoke);
            _collector.Start();
        }

        private void OnMetricsUpdated(MetricsSnapshot snap)
        {
            _overlay?.UpdateMetrics(snap, _settings);
        }

        // ── Settings ──────────────────────────────────────────────────────────
        private void OpenSettings()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (_settingsWindow?.IsVisible == true)
                {
                    _settingsWindow.Activate();
                    return;
                }
                _settingsWindow = new SettingsWindow(_settings, _collector?.FpsService);
                _settingsWindow.SettingsApplied += OnSettingsApplied;
                _settingsWindow.Show();
            });
        }

        private void OnSettingsApplied(AppSettings updated)
        {
            _settings = updated;
            if (_collector != null) _collector.Settings = updated;
            PositionOverlayOnMonitor();
            _overlay?.ApplySettings(updated);
        }

        private void RestartOverlay()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                _overlay?.Close();
                BuildOverlay();
            });
        }

        // ── Disposal ──────────────────────────────────────────────────────────
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _collector?.Dispose();
            _trayIcon?.Dispose();
        }
    }
}
