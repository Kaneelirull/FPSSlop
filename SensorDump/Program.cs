using System;
using LibreHardwareMonitor.Hardware;

var computer = new Computer
{
    IsCpuEnabled = true,
    IsMotherboardEnabled = true
};
computer.Open();

foreach (var hw in computer.Hardware)
{
    hw.Update();
    Console.WriteLine($"\nHardware: {hw.Name} [{hw.HardwareType}]");

    foreach (var sub in hw.SubHardware)
    {
        sub.Update();
        Console.WriteLine($"  SubHardware: {sub.Name} [{sub.HardwareType}]");
        foreach (var s in sub.Sensors)
            Console.WriteLine($"    [{s.SensorType,-12}] \"{s.Name}\" = {s.Value}");
    }

    foreach (var s in hw.Sensors)
        Console.WriteLine($"  [{s.SensorType,-12}] \"{s.Name}\" = {s.Value}");
}

computer.Close();
Console.WriteLine("\nDone. Press any key.");
Console.ReadKey();
