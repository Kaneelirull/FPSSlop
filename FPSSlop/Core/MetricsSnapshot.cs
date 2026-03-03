namespace FPSSlop.Core
{
    /// <summary>Snapshot of all monitored metrics at a given point in time.</summary>
    public class MetricsSnapshot
    {
        // FPS
        public float Fps { get; set; }
        public float FrameTimeMs { get; set; }
        public float Low1Percent { get; set; }
        public float Low01Percent { get; set; }

        // GPU (Nvidia)
        public float GpuUsage { get; set; }
        public float GpuTempC { get; set; }
        public float GpuCoreMhz { get; set; }
        public float GpuMemMhz { get; set; }
        public float GpuVramUsedGb { get; set; }
        public float GpuVramTotalGb { get; set; }
        public float GpuPowerW { get; set; }

        // CPU
        public float CpuUsage { get; set; }
        public float CpuTempC { get; set; }
        public float CpuClockMhz { get; set; }

        // RAM
        public float RamUsedGb { get; set; }
        public float RamTotalGb { get; set; }
    }
}
