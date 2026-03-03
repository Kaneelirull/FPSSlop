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

                        // Average per-core loads — CPU Total is unreliable on Ryzen
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

            // Both temp and clock via WMI — LHM returns 0/NaN for Ryzen 7000
            float cpuTemp  = TryWmiCpuTemp();
            float cpuClock = TryWmiCpuClock();

            lock (_lock)
            {
                _cpuUsage    = cpuCoreCount > 0 ? cpuUsageSum / cpuCoreCount : 0;
                _cpuTempC    = cpuTemp;
                _cpuClockMhz = cpuClock;
                _ramUsedGb   = ramUsed;
                _ramTotalGb  = ramUsed + ramAvailable;
            }
        }

        private static float TryWmiCpuTemp()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    @"root\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
                foreach (ManagementObject obj in searcher.Get())
                {
                    float celsius = (float)((Convert.ToDouble(obj["CurrentTemperature"]) - 2732) / 10.0);
                    if (celsius > 0 && celsius < 120) return celsius;
                }
            }
            catch { }
            return 0;
        }

        private static float TryWmiCpuClock()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT CurrentClockSpeed FROM Win32_Processor");
                foreach (ManagementObject obj in searcher.Get())
                {
                    float val = Convert.ToSingle(obj["CurrentClockSpeed"]);
                    if (val > 0) return val;
                }
            }
            catch { }
            return 0;
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
            _computer.Close();
        }
    }
}
