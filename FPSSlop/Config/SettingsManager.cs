using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FPSSlop.Config
{
    public class AppSettings
    {
        // General
        public bool StartWithWindows { get; set; } = false;
        public int PollIntervalMs { get; set; } = 500;
        public double Opacity { get; set; } = 0.92;
        public double ScaleFactor { get; set; } = 1.0;

        // Display
        public int FpsSourceMonitorIndex { get; set; } = 0;
        public int OverlayMonitorIndex { get; set; } = 1;
        public double OverlayX { get; set; } = 10;
        public double OverlayY { get; set; } = 10;

        // FPS target process — empty string means Auto (foreground)
        public string FpsTargetProcess { get; set; } = "";

        // Metrics visibility
        public bool ShowFps { get; set; } = true;
        public bool Show1PercentLow { get; set; } = true;
        public bool Show01PercentLow { get; set; } = true;
        public bool ShowFrameTime { get; set; } = true;
        public bool ShowGpuUsage { get; set; } = true;
        public bool ShowGpuTemp { get; set; } = true;
        public bool ShowGpuCoreClock { get; set; } = false;
        public bool ShowGpuMemClock { get; set; } = false;
        public bool ShowVram { get; set; } = true;
        public bool ShowPowerDraw { get; set; } = false;
        public bool ShowCpuUsage { get; set; } = true;
        public bool ShowCpuTemp { get; set; } = true;
        public bool ShowCpuClock { get; set; } = false;
        public bool ShowRam { get; set; } = true;

        // Metric row order — each string is a row group key
        // Valid keys: "fps", "gpu", "cpu"
        public List<string> RowOrder { get; set; } = new() { "fps", "gpu", "cpu" };

        // Theme
        public string AccentColorHex { get; set; } = "#00FFCC";
        public int FontSize { get; set; } = 13;
        public bool CompactMode { get; set; } = false;
        public bool ClickThrough { get; set; } = false;
        public bool OverlayVisible { get; set; } = true;
        public bool PositionLocked { get; set; } = false;
    }

    public static class SettingsManager
    {
        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FPSSlop", "settings.json");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    var s = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
                    // Ensure RowOrder always has all three rows
                    foreach (var key in new[] { "fps", "gpu", "cpu" })
                        if (!s.RowOrder.Contains(key)) s.RowOrder.Add(key);
                    return s;
                }
            }
            catch { }
            return new AppSettings();
        }

        public static void Save(AppSettings settings)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
                File.WriteAllText(ConfigPath, JsonSerializer.Serialize(settings, JsonOptions));
            }
            catch { }
        }
    }
}
