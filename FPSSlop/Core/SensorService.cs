using LibreHardwareMonitor.Hardware;
using System.Diagnostics;
using System.Management;

namespace FPSSlop.Core
{
    public sealed class SensorService : IDisposable
    {
        private readonly Computer _computer;
        private readonly object _lock = new();
        private float _cpuUsage, _cpuTempC, _cpuClockMhz;
        private float _ramUsedGb, _ramTotalGb;
        private bool _disposed;

        private Thread? _perfThread;
        private volatile bool _running;
        private float _perfTemp, _perfClock;
        private readonly object _perfLock = new();

        private PerformanceCounter? _procPerfCounter;
        private int _maxClockMhz;

        public SensorService()
        {
            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsMemoryEnabled = true,
                IsGpuEnabled = false,
                IsMotherboardEnabled = false,
                IsStorageEnabled = false,
                IsNetworkEnabled = false,
                IsControllerEnabled = false,
                IsBatteryEnabled = false,
                IsPsuEnabled = false
            };
            _computer.Open();

            _running = true;
            _perfThread = new Thread(PerfLoop)
            {
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal,
                Name = "FPSSlop.PerfPoller"
            };
            _perfThread.Start();
        }

        private void PerfLoop()
        {
            try
            {
                _procPerfCounter = new PerformanceCounter(
                    "Processor Information", "% Processor Performance", "_Total");
                _procPerfCounter.NextValue(); // discard first reading

                using var s = new ManagementObjectSearcher("SELECT MaxClockSpeed FROM Win32_Processor");
                foreach (ManagementObject o in s.Get())
                {
                    _maxClockMhz = Convert.ToInt32(o["MaxClockSpeed"]);
                    break;
                }
            }
            catch { }

            while (_running && !_disposed)
            {
                float clock = QueryClock();
                float temp  = QueryTemp();

                lock (_perfLock)
                {
                    if (clock > 0) _perfClock = clock;
                    if (temp  > 0) _perfTemp  = temp;
                }

                Thread.Sleep(1000);
            }
        }

        private float QueryClock()
        {
            try
            {
                if (_procPerfCounter == null || _maxClockMhz <= 0) return 0;
                float pct = _procPerfCounter.NextValue();
                return _maxClockMhz * pct / 100f;
            }
            catch { return 0; }
        }

        private float QueryTemp()
        {
            // Try MSAcpi first (requires admin, may only return ambient on Ryzen 7000)
            try
            {
                using var s = new ManagementObjectSearcher(
                    @"root\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
                float best = 0;
                foreach (ManagementObject o in s.Get())
                {
                    float c = (float)((Convert.ToDouble(o["CurrentTemperature"]) - 2732) / 10.0);
                    if (c > best) best = c;
                }
                if (best > 20 && best < 120) return best;
            }
            catch { }

            // Fallback: perf counter thermal zone (HighPrecisionTemperature / 10)
            try
            {
                using var s = new ManagementObjectSearcher(
                    "SELECT HighPrecisionTemperature FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation");
                float best = 0;
                foreach (ManagementObject o in s.Get())
                {
                    float c = Convert.ToSingle(o["HighPrecisionTemperature"]) / 10f;
                    if (c > best) best = c;
                }
                if (best > 0 && best < 120) return best;
            }
            catch { }

            return 0;
        }

        public void Poll()
        {
            if (_disposed) return;

            float cpuUsage = 0, ramUsed = 0, ramAvailable = 0;

            foreach (var hw in _computer.Hardware)
            {
                hw.Update();

                if (hw.HardwareType == HardwareType.Cpu)
                {
                    foreach (var s in hw.Sensors)
                    {
                        if (s.SensorType == SensorType.Load && s.Name == "CPU Total")
                            cpuUsage = s.Value ?? 0;
                    }
                }
                else if (hw.HardwareType == HardwareType.Memory)
                {
                    foreach (var s in hw.Sensors)
                    {
                        if (s.SensorType == SensorType.Data)
                        {
                            if (s.Name == "Memory Used")           ramUsed      = s.Value ?? 0;
                            else if (s.Name == "Memory Available") ramAvailable = s.Value ?? 0;
                        }
                    }
                }
            }

            float perfTemp, perfClock;
            lock (_perfLock) { perfTemp = _perfTemp; perfClock = _perfClock; }

            lock (_lock)
            {
                _cpuUsage    = cpuUsage;
                _cpuTempC    = perfTemp;
                _cpuClockMhz = perfClock;
                _ramUsedGb   = ramUsed;
                _ramTotalGb  = ramUsed + ramAvailable;
            }
        }

        public (float usage, float tempC, float clockMhz) GetCpu()
        {
            lock (_lock) return (_cpuUsage, _cpuTempC, _cpuClockMhz);
        }

        public (float usedGb, float totalGb) GetRam()
        {
            lock (_lock) return (_ramUsedGb, _ramTotalGb);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _running = false;
            _perfThread?.Join(3000);
            _procPerfCounter?.Dispose();
            _computer.Close();
        }
    }
}
