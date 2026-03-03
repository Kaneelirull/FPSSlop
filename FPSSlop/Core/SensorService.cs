using LibreHardwareMonitor.Hardware;
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

        // WMI is slow — poll on a separate slower thread
        private Thread? _wmiThread;
        private volatile bool _running;
        private float _wmiTemp, _wmiClock;
        private readonly object _wmiLock = new();

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
            _wmiThread = new Thread(WmiLoop)
            {
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal,
                Name = "FPSSlop.WmiPoller"
            };
            _wmiThread.Start();
        }

        private void WmiLoop()
        {
            while (_running && !_disposed)
            {
                float temp  = QueryWmiTemp();
                float clock = QueryWmiClock();

                lock (_wmiLock)
                {
                    if (temp  > 0) _wmiTemp  = temp;
                    if (clock > 0) _wmiClock = clock;
                }

                Thread.Sleep(2000); // WMI every 2s — plenty for temp/clock display
            }
        }

        private static float QueryWmiTemp()
        {
            try
            {
                using var s = new ManagementObjectSearcher(
                    @"root\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
                foreach (ManagementObject o in s.Get())
                {
                    float c = (float)((Convert.ToDouble(o["CurrentTemperature"]) - 2732) / 10.0);
                    if (c > 0 && c < 120) return c;
                }
            }
            catch { }
            return 0;
        }

        private static float QueryWmiClock()
        {
            try
            {
                using var s = new ManagementObjectSearcher(
                    "SELECT CurrentClockSpeed FROM Win32_Processor");
                foreach (ManagementObject o in s.Get())
                {
                    float v = Convert.ToSingle(o["CurrentClockSpeed"]);
                    if (v > 0) return v;
                }
            }
            catch { }
            return 0;
        }

        public void Poll()
        {
            if (_disposed) return;

            float cpuUsageSum = 0, ramUsed = 0, ramAvailable = 0;
            int cpuCoreCount = 0;

            foreach (var hw in _computer.Hardware)
            {
                hw.Update();

                if (hw.HardwareType == HardwareType.Cpu)
                {
                    foreach (var s in hw.Sensors)
                    {
                        float val = s.Value ?? 0;
                        if (s.SensorType == SensorType.Load && s.Name.StartsWith("CPU Core #"))
                        {
                            cpuUsageSum += val;
                            cpuCoreCount++;
                        }
                    }
                }
                else if (hw.HardwareType == HardwareType.Memory)
                {
                    foreach (var s in hw.Sensors)
                    {
                        if (s.SensorType == SensorType.Data)
                        {
                            if (s.Name == "Memory Used") ramUsed = s.Value ?? 0;
                            else if (s.Name == "Memory Available") ramAvailable = s.Value ?? 0;
                        }
                    }
                }
            }

            float wmiTemp, wmiClock;
            lock (_wmiLock) { wmiTemp = _wmiTemp; wmiClock = _wmiClock; }

            lock (_lock)
            {
                _cpuUsage    = cpuCoreCount > 0 ? cpuUsageSum / cpuCoreCount : 0;
                _cpuTempC    = wmiTemp;
                _cpuClockMhz = wmiClock;
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
            _wmiThread?.Join(3000);
            _computer.Close();
        }
    }
}
