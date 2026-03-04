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

        // ── 16-bit color palette ──────────────────────────────────────────────
        // FPS row  — classic green
        private static readonly Color ColFpsVal   = Color.FromRgb(0x00, 0xFF, 0x44);
        // GPU row  — hot orange
        private static readonly Color ColGpuVal   = Color.FromRgb(0xFF, 0x88, 0x00);
        // CPU row  — electric cyan
        private static readonly Color ColCpuVal   = Color.FromRgb(0x00, 0xCC, 0xFF);
        // RAM      — magenta/purple
        private static readonly Color ColRamVal   = Color.FromRgb(0xDD, 0x44, 0xFF);
        // Labels   — dim blue-grey
        private static readonly Color ColLabel    = Color.FromRgb(0x55, 0x77, 0x88);

        private static readonly FontFamily PixelFont =
            new FontFamily(new Uri("pack://application:,,,/"), "/Assets/Fonts/#Press Start 2P");

        private TextBlock MakeLabel(string text) => new()
        {
            Text       = text,
            Foreground = new SolidColorBrush(ColLabel),
            FontFamily = PixelFont,
            FontSize   = _settings.FontSize * 0.7,
            Margin     = new Thickness(0, 0, 5, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        private TextBlock MakeValue(Color color) => new()
        {
            Foreground = new SolidColorBrush(color),
            FontFamily = PixelFont,
            FontSize   = _settings.FontSize * 0.7,
            Margin     = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        private void BuildRows()
        {
            _rowBlocks.Clear();
            _rowPanels.Clear();
            MetricsPanel.Children.Clear();

            // FPS row — all green
            var fpsRow = new StackPanel { Orientation = Orientation.Horizontal };
            var fpsBlocks = new[]
            {
                MakeValue(ColFpsVal),  // FPS
                MakeValue(ColFpsVal),  // 1L
                MakeValue(ColFpsVal),  // 0.1L
                MakeValue(ColFpsVal),  // FT
            };
            fpsRow.Children.Add(MakeLabel("FPS"));  fpsRow.Children.Add(fpsBlocks[0]);
            fpsRow.Children.Add(MakeLabel("1L"));   fpsRow.Children.Add(fpsBlocks[1]);
            fpsRow.Children.Add(MakeLabel("0.1L")); fpsRow.Children.Add(fpsBlocks[2]);
            fpsRow.Children.Add(MakeLabel("FT"));   fpsRow.Children.Add(fpsBlocks[3]);
            _rowBlocks["fps"] = fpsBlocks;
            _rowPanels["fps"] = fpsRow;

            // GPU row — orange
            var gpuRow = new StackPanel { Orientation = Orientation.Horizontal };
            var gpuBlocks = new[]
            {
                MakeValue(ColGpuVal), MakeValue(ColGpuVal), MakeValue(ColGpuVal),
                MakeValue(ColGpuVal), MakeValue(ColGpuVal), MakeValue(ColGpuVal),
            };
            gpuRow.Children.Add(MakeLabel("GPU"));  gpuRow.Children.Add(gpuBlocks[0]);
            gpuRow.Children.Add(MakeLabel("TEMP")); gpuRow.Children.Add(gpuBlocks[1]);
            gpuRow.Children.Add(MakeLabel("CORE")); gpuRow.Children.Add(gpuBlocks[2]);
            gpuRow.Children.Add(MakeLabel("MEM"));  gpuRow.Children.Add(gpuBlocks[3]);
            gpuRow.Children.Add(MakeLabel("VRAM")); gpuRow.Children.Add(gpuBlocks[4]);
            gpuRow.Children.Add(MakeLabel("W"));    gpuRow.Children.Add(gpuBlocks[5]);
            _rowBlocks["gpu"] = gpuBlocks;
            _rowPanels["gpu"] = gpuRow;

            // CPU row — cyan, RAM value magenta
            var cpuRow = new StackPanel { Orientation = Orientation.Horizontal };
            var cpuBlocks = new[]
            {
                MakeValue(ColCpuVal), MakeValue(ColCpuVal), MakeValue(ColCpuVal),
                MakeValue(ColRamVal), // RAM gets its own color
            };
            cpuRow.Children.Add(MakeLabel("CPU"));  cpuRow.Children.Add(cpuBlocks[0]);
            cpuRow.Children.Add(MakeLabel("TEMP")); cpuRow.Children.Add(cpuBlocks[1]);
            cpuRow.Children.Add(MakeLabel("CLK"));  cpuRow.Children.Add(cpuBlocks[2]);
            cpuRow.Children.Add(MakeLabel("RAM"));  cpuRow.Children.Add(cpuBlocks[3]);
            _rowBlocks["cpu"] = cpuBlocks;
            _rowPanels["cpu"] = cpuRow;

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

            if (IsLoaded)
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                int style = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, s.ClickThrough
                    ? style | WS_EX_TRANSPARENT | WS_EX_LAYERED
                    : style & ~WS_EX_TRANSPARENT);

                BuildRows();
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
