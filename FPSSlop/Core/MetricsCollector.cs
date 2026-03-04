using FPSSlop.Config;

namespace FPSSlop.Core
{
    /// <summary>
    /// Drives all sensor polling on a background thread at the configured interval.
    /// Fires MetricsUpdated on the UI thread via the supplied dispatcher action.
    /// </summary>
    public sealed class MetricsCollector : IDisposable
    {
        private readonly SensorService _sensors;
        private readonly NvidiaService _nvidia;
        private readonly FpsService _fps;
        private readonly Action<MetricsSnapshot> _onUpdate;
        private readonly Action<Action> _dispatchToUi;

        private Thread? _thread;
        private volatile bool _running;
        private bool _disposed;

        public MetricsCollector(AppSettings settings,
                                Action<MetricsSnapshot> onUpdate,
                                Action<Action> dispatchToUi)
        {
            Settings = settings;
            _onUpdate = onUpdate;
            _dispatchToUi = dispatchToUi;

            _sensors = new SensorService();
            _nvidia = new NvidiaService();
            _fps = new FpsService();
        }

        public AppSettings Settings { get; set; }

        /// <summary>Exposes the FPS service so the UI can read CurrentApps.</summary>
        public FpsService FpsService => _fps;

        public void Start()
        {
            _fps.Start();
            _running = true;
            _thread = new Thread(PollLoop)
            {
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal,
                Name = "FPSSlop.MetricsCollector"
            };
            _thread.Start();
        }

        private void PollLoop()
        {
            while (_running && !_disposed)
            {
                // Sync FPS target from settings
                _fps.TargetProcessName = Settings.FpsTargetProcess ?? "";

                _sensors.Poll();
                _nvidia.Poll();

                var (cpuUsage, cpuTemp, cpuClock) = _sensors.GetCpu();
                var (ramUsed, ramTotal) = _sensors.GetRam();
                var (gpuUsage, gpuTemp, gpuCore, gpuMem, vram, vramTotal, power) = _nvidia.GetGpu();
                var (fps, ft, low1, low01) = _fps.GetStats();

                var snap = new MetricsSnapshot
                {
                    Fps = fps,
                    FrameTimeMs = ft,
                    Low1Percent = low1,
                    Low01Percent = low01,
                    GpuUsage = gpuUsage,
                    GpuTempC = gpuTemp,
                    GpuCoreMhz = gpuCore,
                    GpuMemMhz = gpuMem,
                    GpuVramUsedGb = vram,
                    GpuVramTotalGb = vramTotal,
                    GpuPowerW = power,
                    CpuUsage = cpuUsage,
                    CpuTempC = cpuTemp,
                    CpuClockMhz = cpuClock,
                    RamUsedGb = ramUsed,
                    RamTotalGb = ramTotal
                };

                _dispatchToUi(() => _onUpdate(snap));

                int interval = Settings.PollIntervalMs;
                Thread.Sleep(Math.Max(100, interval));
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _running = false;
            _thread?.Join(1000);
            _fps.Dispose();
            _nvidia.Dispose();
            _sensors.Dispose();
        }
    }
}
