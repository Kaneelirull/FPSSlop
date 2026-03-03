# FPSSlop

PC gaming monitor overlay for Windows. Displays GPU, CPU, RAM and FPS metrics in a minimal dark pixel-art overlay.

## Stack

- **Language**: C# 12 / .NET 8
- **UI**: WPF (hardware accelerated, transparent layered window)
- **GPU sensors**: LibreHardwareMonitor (Nvidia via NVAPI internally)
- **CPU/RAM sensors**: LibreHardwareMonitor
- **FPS tracking**: Windows ETW / DXGI present timing (PresentMon-compatible approach)
- **Tray**: System.Windows.Forms.NotifyIcon

## Project structure

```
FPSSlop/
├── Core/
│   ├── MetricsSnapshot.cs      # Data model
│   ├── SensorService.cs        # CPU + RAM via LHM
│   ├── NvidiaService.cs        # GPU via LHM/NVAPI
│   ├── FpsService.cs           # FPS + frame time
│   └── MetricsCollector.cs     # Polling orchestrator
├── UI/
│   ├── OverlayWindow.xaml(.cs) # Always-on-top transparent overlay
│   └── SettingsWindow.xaml(.cs)# Settings panel
├── Config/
│   └── SettingsManager.cs      # JSON persistence (~AppData/FPSSlop/settings.json)
├── TrayController.cs           # System tray, lifecycle glue
├── App.xaml(.cs)               # Entry point
└── Assets/
    ├── Fonts/
    └── Icons/tray.ico
```

## Build

Requirements: .NET 8 SDK, Windows 10+, Nvidia GPU (for GPU metrics).

```
dotnet restore
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained false
```

## Notes

- Run as Administrator for full ETW/PresentMon FPS capture in exclusive fullscreen games.
- GPU metrics require an Nvidia GPU; other metrics work on any hardware.
- Settings persist to `%APPDATA%\FPSSlop\settings.json`.
- Overlay is draggable (click and drag when click-through is off).

## Roadmap

- [ ] Direct NVAPI integration (replace LHM GPU path)
- [ ] PresentMon ETL session for exclusive fullscreen FPS
- [ ] AMD GPU support (plug into existing NvidiaService interface)
- [ ] Per-game profiles
- [ ] Docker Hub publish (kaneelir0ll/fpsslop) — N/A, Windows desktop app
