using LibreHardwareMonitor.Hardware;

var computer = new Computer
{
    IsCpuEnabled = true,
    IsMemoryEnabled = false,
    IsGpuEnabled = false,
    IsMotherboardEnabled = true,
    IsStorageEnabled = false,
    IsNetworkEnabled = false,
    IsControllerEnabled = false,
};
computer.Open();

foreach (var hw in computer.Hardware)
{
    hw.Update();
    Console.WriteLine($"\n[HW] {hw.HardwareType} — {hw.Name}");
    foreach (var sub in hw.SubHardware)
    {
        sub.Update();
        Console.WriteLine($"  [SUB] {sub.HardwareType} — {sub.Name}");
        foreach (var s in sub.Sensors)
            Console.WriteLine($"    {s.SensorType,-16} {s.Name,-40} = {s.Value}");
    }
    foreach (var s in hw.Sensors)
        Console.WriteLine($"  {s.SensorType,-16} {s.Name,-40} = {s.Value}");
}

computer.Close();
Console.WriteLine("\nDone. Press any key.");
Console.ReadKey();
