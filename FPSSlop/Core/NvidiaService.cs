using System.Runtime.InteropServices;

namespace FPSSlop.Core
{
    /// <summary>
    /// Nvidia GPU metrics via NVAPI (nvapi64.dll).
    /// Falls back gracefully if no Nvidia GPU detected.
    /// </summary>
    public sealed class NvidiaService : IDisposable
    {
        // ── NVAPI status ──────────────────────────────────────────────────────────
        private const int NVAPI_OK = 0;
        private const uint GPU_THERMAL_TARGET_GPU = 1;

        private bool _available;
        private bool _disposed;
        private readonly object _lock = new();

        private float _gpuUsage, _gpuTempC, _gpuCoreMhz, _gpuMemMhz, _gpuVramUsedGb, _gpuVramTotalGb, _gpuPowerW;

        // ── P/Invoke shims ────────────────────────────────────────────────────────
        [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
        private static extern nint QueryInterface(uint functionOffset);

        // We call NVAPI through the QueryInterface function pointer table.
        // For simplicity, we use LibreHardwareMonitor's NvidiaGpu sensor instead
        // for the initial implementation, which wraps NVAPI internally.
        // A full direct NVAPI implementation can be swapped in here.
        // For now, this service reads from LHM's GPU sensors via a companion approach.

        // ── LibreHardwareMonitor GPU fallback ────────────────────────────────────
        private LibreHardwareMonitor.Hardware.Computer? _lhmComputer;

        public NvidiaService()
        {
            try
            {
                _lhmComputer = new LibreHardwareMonitor.Hardware.Computer
                {
                    IsGpuEnabled = true
                };
                _lhmComputer.Open();
                _available = true;
            }
            catch
            {
                _available = false;
            }
        }

        public bool IsAvailable => _available;

        public void Poll()
        {
            if (!_available || _lhmComputer == null) return;

            float usage = 0, temp = 0, core = 0, mem = 0, vramUsed = 0, vramTotal = 0, power = 0;

            try
            {
                foreach (var hw in _lhmComputer.Hardware)
                {
                    if (hw.HardwareType != LibreHardwareMonitor.Hardware.HardwareType.GpuNvidia) continue;
                    hw.Update();

                    foreach (var s in hw.Sensors)
                    {
                        var st = s.SensorType;
                        var sn = s.Name;
                        var sv = s.Value ?? 0f;
                        if (st == LibreHardwareMonitor.Hardware.SensorType.Load && sn.Contains("GPU Core"))
                            usage = sv;
                        else if (st == LibreHardwareMonitor.Hardware.SensorType.Temperature && sv > 0 && sv < 120)
                            temp = sv;   // clamp bogus sensor values (255 = unpopulated sensor)
                        else if (st == LibreHardwareMonitor.Hardware.SensorType.Clock && sn.Contains("GPU Core"))
                            core = sv;
                        else if (st == LibreHardwareMonitor.Hardware.SensorType.Clock && sn.Contains("GPU Memory"))
                            mem = sv;
                        else if (st == LibreHardwareMonitor.Hardware.SensorType.SmallData && sn.Contains("GPU Memory Used"))
                            vramUsed = sv / 1024f;
                        else if (st == LibreHardwareMonitor.Hardware.SensorType.SmallData && sn.Contains("GPU Memory Total"))
                            vramTotal = sv / 1024f;
                        else if (st == LibreHardwareMonitor.Hardware.SensorType.Power)
                            power = sv;
                    }
                }
            }
            catch { /* GPU disappeared / driver issue */ }

            lock (_lock)
            {
                _gpuUsage = usage; _gpuTempC = temp;
                _gpuCoreMhz = core; _gpuMemMhz = mem;
                _gpuVramUsedGb = vramUsed; _gpuVramTotalGb = vramTotal; _gpuPowerW = power;
            }
        }

        public (float usage, float tempC, float coreMhz, float memMhz, float vramGb, float vramTotalGb, float powerW) GetGpu()
        {
            lock (_lock)
                return (_gpuUsage, _gpuTempC, _gpuCoreMhz, _gpuMemMhz, _gpuVramUsedGb, _gpuVramTotalGb, _gpuPowerW);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _lhmComputer?.Close();
        }
    }
}
