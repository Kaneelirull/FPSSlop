using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Media;
using FPSSlop.Config;

namespace FPSSlop.UI
{
    public partial class SettingsWindow : Window
    {
        private AppSettings _settings;
        private List<string> _rowOrder = new();
        public event Action<AppSettings>? SettingsApplied;

        private static readonly Dictionary<string, string> RowLabels = new()
        {
            { "fps", "FPS / Frame time" },
            { "gpu", "GPU" },
            { "cpu", "CPU / RAM" }
        };

        public SettingsWindow(AppSettings settings)
        {
            _settings = settings;
            InitializeComponent();
            Loaded += (_, _) => LoadToUi();
        }

        private void LoadToUi()
        {
            ChkStartWithWindows.IsChecked = _settings.StartWithWindows;
            SliderPoll.Value    = _settings.PollIntervalMs;
            SliderOpacity.Value = _settings.Opacity;
            SliderScale.Value   = _settings.ScaleFactor;

            var screens = Screen.AllScreens;
            CboFpsMonitor.Items.Clear();
            CboOverlayMonitor.Items.Clear();
            for (int i = 0; i < screens.Length; i++)
            {
                string label = $"Monitor {i + 1} ({screens[i].Bounds.Width}x{screens[i].Bounds.Height})";
                CboFpsMonitor.Items.Add(label);
                CboOverlayMonitor.Items.Add(label);
            }
            CboFpsMonitor.SelectedIndex    = Math.Clamp(_settings.FpsSourceMonitorIndex, 0, screens.Length - 1);
            CboOverlayMonitor.SelectedIndex = Math.Clamp(_settings.OverlayMonitorIndex,   0, screens.Length - 1);

            PopulateFpsTargetList(_settings.FpsTargetProcess);

            ChkFps.IsChecked       = _settings.ShowFps;
            ChkFrameTime.IsChecked = _settings.ShowFrameTime;
            ChkLow1.IsChecked      = _settings.Show1PercentLow;
            ChkLow01.IsChecked     = _settings.Show01PercentLow;
            ChkGpuUsage.IsChecked  = _settings.ShowGpuUsage;
            ChkGpuTemp.IsChecked   = _settings.ShowGpuTemp;
            ChkGpuCore.IsChecked   = _settings.ShowGpuCoreClock;
            ChkGpuMem.IsChecked    = _settings.ShowGpuMemClock;
            ChkVram.IsChecked      = _settings.ShowVram;
            ChkPower.IsChecked     = _settings.ShowPowerDraw;
            ChkCpuUsage.IsChecked  = _settings.ShowCpuUsage;
            ChkCpuTemp.IsChecked   = _settings.ShowCpuTemp;
            ChkCpuClock.IsChecked  = _settings.ShowCpuClock;
            ChkRam.IsChecked       = _settings.ShowRam;

            TxtAccentColor.Text  = _settings.AccentColorHex;
            SliderFontSize.Value = _settings.FontSize;
            ChkCompact.IsChecked = _settings.CompactMode;
            ChkPositionLocked.IsChecked = _settings.PositionLocked;

            _rowOrder = new List<string>(_settings.RowOrder);
            BuildRowOrderUi();
        }

        // ── Row order UI ──────────────────────────────────────────────────────

        private void BuildRowOrderUi()
        {
            RowOrderPanel.Children.Clear();
            foreach (var key in _rowOrder)
            {
                var key_ = key; // capture
                var row = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(0, 2, 0, 2),
                    Padding = new Thickness(8, 4, 8, 4)
                };

                var sp = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
                sp.Children.Add(new TextBlock
                {
                    Text = RowLabels.TryGetValue(key, out var lbl) ? lbl : key,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)),
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    Width = 180
                });

                var upBtn = new System.Windows.Controls.Button { Content = "▲", Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(4, 0, 2, 0) };
                var dnBtn = new System.Windows.Controls.Button { Content = "▼", Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(2, 0, 0, 0) };

                upBtn.Click += (_, _) => MoveRow(key_, -1);
                dnBtn.Click += (_, _) => MoveRow(key_,  1);

                sp.Children.Add(upBtn);
                sp.Children.Add(dnBtn);
                row.Child = sp;
                RowOrderPanel.Children.Add(row);
            }
        }

        private void MoveRow(string key, int delta)
        {
            int idx = _rowOrder.IndexOf(key);
            if (idx < 0) return;
            int newIdx = Math.Clamp(idx + delta, 0, _rowOrder.Count - 1);
            if (newIdx == idx) return;
            _rowOrder.RemoveAt(idx);
            _rowOrder.Insert(newIdx, key);
            BuildRowOrderUi();
        }

        // ── FPS Target Process ────────────────────────────────────────────────

        private void PopulateFpsTargetList(string currentTarget)
        {
            CboFpsTarget.Items.Clear();
            CboFpsTarget.Items.Add("Auto (foreground)");

            var procs = Process.GetProcesses()
                .Where(p => p.Id > 4 && !string.IsNullOrEmpty(p.ProcessName))
                .Select(p => p.ProcessName + ".exe")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n)
                .ToList();

            foreach (var name in procs)
                CboFpsTarget.Items.Add(name);

            // Select saved target
            if (string.IsNullOrEmpty(currentTarget))
            {
                CboFpsTarget.SelectedIndex = 0;
            }
            else
            {
                int idx = CboFpsTarget.Items.IndexOf(currentTarget);
                CboFpsTarget.SelectedIndex = idx >= 0 ? idx : 0;
            }
        }

        private void BtnRefreshProcesses_Click(object sender, RoutedEventArgs e)
        {
            string current = CboFpsTarget.SelectedIndex > 0
                ? CboFpsTarget.SelectedItem?.ToString() ?? ""
                : "";
            PopulateFpsTargetList(current);
        }

        // ── Apply ─────────────────────────────────────────────────────────────

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            _settings.StartWithWindows  = ChkStartWithWindows.IsChecked == true;
            _settings.PollIntervalMs    = (int)SliderPoll.Value;
            _settings.Opacity           = SliderOpacity.Value;
            _settings.ScaleFactor       = SliderScale.Value;
            _settings.FpsSourceMonitorIndex  = CboFpsMonitor.SelectedIndex;
            _settings.OverlayMonitorIndex    = CboOverlayMonitor.SelectedIndex;
            _settings.FpsTargetProcess       = CboFpsTarget.SelectedIndex <= 0
                ? "" : CboFpsTarget.SelectedItem?.ToString() ?? "";

            _settings.ShowFps          = ChkFps.IsChecked == true;
            _settings.ShowFrameTime    = ChkFrameTime.IsChecked == true;
            _settings.Show1PercentLow  = ChkLow1.IsChecked == true;
            _settings.Show01PercentLow = ChkLow01.IsChecked == true;
            _settings.ShowGpuUsage     = ChkGpuUsage.IsChecked == true;
            _settings.ShowGpuTemp      = ChkGpuTemp.IsChecked == true;
            _settings.ShowGpuCoreClock = ChkGpuCore.IsChecked == true;
            _settings.ShowGpuMemClock  = ChkGpuMem.IsChecked == true;
            _settings.ShowVram         = ChkVram.IsChecked == true;
            _settings.ShowPowerDraw    = ChkPower.IsChecked == true;
            _settings.ShowCpuUsage     = ChkCpuUsage.IsChecked == true;
            _settings.ShowCpuTemp      = ChkCpuTemp.IsChecked == true;
            _settings.ShowCpuClock     = ChkCpuClock.IsChecked == true;
            _settings.ShowRam          = ChkRam.IsChecked == true;

            _settings.AccentColorHex   = TxtAccentColor.Text.Trim();
            _settings.FontSize         = (int)SliderFontSize.Value;
            _settings.CompactMode      = ChkCompact.IsChecked == true;
            _settings.PositionLocked   = ChkPositionLocked.IsChecked == true;
            _settings.RowOrder         = new List<string>(_rowOrder);

            SettingsApplied?.Invoke(_settings);
            SettingsManager.Save(_settings);
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}
