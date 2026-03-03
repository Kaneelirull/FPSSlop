using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using FPSSlop.Config;
using FPSSlop.Core;

namespace FPSSlop.UI
{
    public partial class OverlayWindow : Window
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll")] private static extern int GetWindowLong(nint hwnd, int index);
        [DllImport("user32.dll")] private static extern int SetWindowLong(nint hwnd, int index, int newStyle);

        private AppSettings _settings;
        private bool _dragging;
        private Point _dragStart;

        // Named text blocks built in code so rows can be reordered
        private readonly Dictionary<string, TextBlock[]> _rowBlocks = new();
        private readonly Dictionary<string, StackPanel> _rowPanels = new();

        public OverlayWindow(AppSettings settings)
        {
            _settings = settings;
            InitializeComponent();
            Loaded += OnLoaded;

            MouseLeftButtonDown += (_, e) =>
            {
                if (_settings.ClickThrough || _settings.PositionLocked) return;
                _dragging = true;
                _dragStart = e.GetPosition(this);
                CaptureMouse();
            };
            MouseLeftButtonUp += (_, _) => { _dragging = false; ReleaseMouseCapture(); };
            MouseMove += (_, e) =>
            {
                if (!_dragging) return;
                var pos = e.GetPosition(null);
                Left = Left + pos.X - _dragStart.X;
                Top  = Top  + pos.Y - _dragStart.Y;
                _settings.OverlayX = Left;
                _settings.OverlayY = Top;
            };
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            BuildRows();
            ApplySettings(_settings);

            var hwnd = new WindowInteropHelper(this).Handle;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TOOLWINDOW);
        }

        // ── Row construction ──────────────────────────────────────────────────

        private TextBlock MakeLabel(string text) => new()
        {
            Text = text,
            Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Margin = new Thickness(0, 0, 5, 0)
        };

        private TextBlock MakeValue(bool accent = false) => new()
        {
            Foreground = accent
                ? (SolidColorBrush)new BrushConverter().ConvertFrom(_settings.AccentColorHex)!
                : new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Margin = new Thickness(0, 0, 12, 0)
        };

        private void BuildRows()
        {
            _rowBlocks.Clear();
            _rowPanels.Clear();
            MetricsPanel.Children.Clear();

            // FPS row blocks: [fps, 1L, 0.1L, FT]
            var fpsRow = new StackPanel { Orientation = Orientation.Horizontal };
            var fpsBlocks = new[]
            {
                MakeValue(accent: true),  // FPS value
                MakeValue(),              // 1L value
                MakeValue(),              // 0.1L value
                MakeValue()               // FT value
            };
            fpsRow.Children.Add(MakeLabel("FPS"));  fpsRow.Children.Add(fpsBlocks[0]);
            fpsRow.Children.Add(MakeLabel("1L"));   fpsRow.Children.Add(fpsBlocks[1]);
            fpsRow.Children.Add(MakeLabel("0.1L")); fpsRow.Children.Add(fpsBlocks[2]);
            fpsRow.Children.Add(MakeLabel("FT"));   fpsRow.Children.Add(fpsBlocks[3]);
            _rowBlocks["fps"] = fpsBlocks;
            _rowPanels["fps"] = fpsRow;

            // GPU row blocks: [usage, temp, core, mem, vram, power]
            var gpuRow = new StackPanel { Orientation = Orientation.Horizontal };
            var gpuBlocks = new[]
            {
                MakeValue(), MakeValue(), MakeValue(),
                MakeValue(), MakeValue(), MakeValue()
            };
            gpuRow.Children.Add(MakeLabel("GPU"));  gpuRow.Children.Add(gpuBlocks[0]);
            gpuRow.Children.Add(MakeLabel("Temp")); gpuRow.Children.Add(gpuBlocks[1]);
            gpuRow.Children.Add(MakeLabel("Core")); gpuRow.Children.Add(gpuBlocks[2]);
            gpuRow.Children.Add(MakeLabel("Mem"));  gpuRow.Children.Add(gpuBlocks[3]);
            gpuRow.Children.Add(MakeLabel("VRAM")); gpuRow.Children.Add(gpuBlocks[4]);
            gpuRow.Children.Add(MakeLabel("W"));    gpuRow.Children.Add(gpuBlocks[5]);
            _rowBlocks["gpu"] = gpuBlocks;
            _rowPanels["gpu"] = gpuRow;

            // CPU row blocks: [usage, temp, clock, ram]
            var cpuRow = new StackPanel { Orientation = Orientation.Horizontal };
            var cpuBlocks = new[]
            {
                MakeValue(), MakeValue(), MakeValue(), MakeValue()
            };
            cpuRow.Children.Add(MakeLabel("CPU"));  cpuRow.Children.Add(cpuBlocks[0]);
            cpuRow.Children.Add(MakeLabel("Temp")); cpuRow.Children.Add(cpuBlocks[1]);
            cpuRow.Children.Add(MakeLabel("Clk"));  cpuRow.Children.Add(cpuBlocks[2]);
            cpuRow.Children.Add(MakeLabel("RAM"));  cpuRow.Children.Add(cpuBlocks[3]);
            _rowBlocks["cpu"] = cpuBlocks;
            _rowPanels["cpu"] = cpuRow;

            // Add rows in configured order
            RebuildRowOrder(_settings.RowOrder);
        }

        private void RebuildRowOrder(List<string> order)
        {
            MetricsPanel.Children.Clear();
            bool first = true;
            foreach (var key in order)
            {
                if (!_rowPanels.ContainsKey(key)) continue;
                var panel = _rowPanels[key];
                panel.Margin = first ? new Thickness(0) : new Thickness(0, 6, 0, 0);
                MetricsPanel.Children.Add(panel);
                first = false;
            }
        }

        // ── Settings application ──────────────────────────────────────────────

        public void ApplySettings(AppSettings s)
        {
            _settings = s;
            Opacity = s.Opacity;
            Left = s.OverlayX;
            Top  = s.OverlayY;
            MetricsPanel.LayoutTransform = new ScaleTransform(s.ScaleFactor, s.ScaleFactor);

            // Update accent colour on FPS value block
            if (_rowBlocks.TryGetValue("fps", out var fb) &&
                ColorConverter.ConvertFromString(s.AccentColorHex) is Color c)
                fb[0].Foreground = new SolidColorBrush(c);

            if (IsLoaded)
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                int style = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, s.ClickThrough
                    ? style | WS_EX_TRANSPARENT | WS_EX_LAYERED
                    : style & ~WS_EX_TRANSPARENT);

                RebuildRowOrder(s.RowOrder);
            }

            RefreshVisibility(s);
        }

        private static Visibility V(bool show) => show ? Visibility.Visible : Visibility.Collapsed;

        private void RefreshVisibility(AppSettings s)
        {
            if (!_rowBlocks.ContainsKey("fps")) return;

            var fb = _rowBlocks["fps"];
            SetPairVisibility(_rowPanels["fps"], 0, s.ShowFps);
            SetPairVisibility(_rowPanels["fps"], 2, s.Show1PercentLow);
            SetPairVisibility(_rowPanels["fps"], 4, s.Show01PercentLow);
            SetPairVisibility(_rowPanels["fps"], 6, s.ShowFrameTime);
            _rowPanels["fps"].Visibility = V(s.ShowFps || s.Show1PercentLow || s.Show01PercentLow || s.ShowFrameTime);

            SetPairVisibility(_rowPanels["gpu"], 0, s.ShowGpuUsage);
            SetPairVisibility(_rowPanels["gpu"], 2, s.ShowGpuTemp);
            SetPairVisibility(_rowPanels["gpu"], 4, s.ShowGpuCoreClock);
            SetPairVisibility(_rowPanels["gpu"], 6, s.ShowGpuMemClock);
            SetPairVisibility(_rowPanels["gpu"], 8, s.ShowVram);
            SetPairVisibility(_rowPanels["gpu"], 10, s.ShowPowerDraw);
            _rowPanels["gpu"].Visibility = V(s.ShowGpuUsage || s.ShowGpuTemp || s.ShowGpuCoreClock ||
                                              s.ShowGpuMemClock || s.ShowVram || s.ShowPowerDraw);

            SetPairVisibility(_rowPanels["cpu"], 0, s.ShowCpuUsage);
            SetPairVisibility(_rowPanels["cpu"], 2, s.ShowCpuTemp);
            SetPairVisibility(_rowPanels["cpu"], 4, s.ShowCpuClock);
            SetPairVisibility(_rowPanels["cpu"], 6, s.ShowRam);
            _rowPanels["cpu"].Visibility = V(s.ShowCpuUsage || s.ShowCpuTemp || s.ShowCpuClock || s.ShowRam);
        }

        // Hide/show a label+value pair at the given child index in a StackPanel
        private static void SetPairVisibility(StackPanel panel, int labelIndex, bool visible)
        {
            var v = visible ? Visibility.Visible : Visibility.Collapsed;
            if (labelIndex < panel.Children.Count)     panel.Children[labelIndex].Visibility = v;
            if (labelIndex + 1 < panel.Children.Count) panel.Children[labelIndex + 1].Visibility = v;
        }

        // ── Metric updates ────────────────────────────────────────────────────

        public void UpdateMetrics(MetricsSnapshot snap, AppSettings s)
        {
            if (!_rowBlocks.ContainsKey("fps")) return;

            var fb = _rowBlocks["fps"];
            fb[0].Text = $"{snap.Fps:F0}";
            fb[1].Text = $"{snap.Low1Percent:F0}";
            fb[2].Text = $"{snap.Low01Percent:F0}";
            fb[3].Text = $"{snap.FrameTimeMs:F1}ms";

            var gb = _rowBlocks["gpu"];
            gb[0].Text = $"{snap.GpuUsage:F0}%";
            gb[1].Text = snap.GpuTempC > 0 ? $"{snap.GpuTempC:F0}°C" : "--";
            gb[2].Text = $"{snap.GpuCoreMhz:F0}MHz";
            gb[3].Text = $"{snap.GpuMemMhz:F0}MHz";
            gb[4].Text = snap.GpuVramTotalGb > 0
                ? $"{snap.GpuVramUsedGb:F1}/{snap.GpuVramTotalGb:F0}G"
                : $"{snap.GpuVramUsedGb:F1}G";
            gb[5].Text = $"{snap.GpuPowerW:F0}W";

            var cb = _rowBlocks["cpu"];
            cb[0].Text = $"{snap.CpuUsage:F0}%";
            cb[1].Text = snap.CpuTempC > 0 ? $"{snap.CpuTempC:F0}°C" : "--";
            cb[2].Text = snap.CpuClockMhz > 0 ? $"{snap.CpuClockMhz:F0}MHz" : "--";
            cb[3].Text = snap.RamTotalGb > 0
                ? $"{snap.RamUsedGb:F1}/{snap.RamTotalGb:F0}G"
                : $"{snap.RamUsedGb:F1}G";

            RefreshVisibility(s);
        }
    }
}
