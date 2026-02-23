# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

```bash
# Build
dotnet build CncController/CncController.csproj

# Run
dotnet run --project CncController/CncController.csproj
```

Target framework: `net10.0-windows`. Requires .NET 10 SDK and Windows (WPF).

There are no automated tests in this project.

## Architecture

This is a WPF desktop app that serves as a GUI frontend for a LinuxCNC-based CNC machine controller. The backend runs at `http://192.168.0.137:5000` (hardcoded in `MachineControlService`).

**MVVM with CommunityToolkit.Mvvm:**
- ViewModels inherit `ObservableObject` and use `[ObservableProperty]` / `[RelayCommand]` source generators.
- `MainViewModel` is the root VM, owns child VMs (`SettingsViewModel`, `MonitorViewModel`, `HistoryViewModel`), manages page navigation, and drives a `DispatcherTimer` for periodic status polling (~500ms).
- Views bind to VMs via `DataContext`; code-behind is kept minimal.

**Services (manual singleton via `Instance` property, not DI container):**
- `MachineControlService` — HTTP client for all machine commands and status polling (`GET /v2/status`, `POST /api/...`). Caches last status in `_lastCachedStatus` for local guard checks.
- `ConfigurationService` — Loads/saves `MachineConfig.json` locally and generates LinuxCNC config files (INI, HAL, XML) uploaded to the backend.
- `HardwareScanService` — EtherCAT slave discovery; has a simulation fallback for offline development.
- `AlarmService` — Centralized log with severity levels; feeds the marquee alert UI.
- `AuthService` — Role-based access (`Operator`, `Engineer`, `Admin`, `Developer`); gates the Settings page.
- `LocalizationService` — Traditional Chinese (zh-TW) resource dictionary support.

**Key data flow:**
```
DispatcherTimer → MachineControlService.GetStatusAsync()
    → updates MainViewModel.Status (MachineStatus : ObservableObject)
    → UI bindings auto-refresh
```

**Settings workflow:** Hardware scan → assign slaves to axes (AxisMappingViewModel) → configure I/O → save → ConfigurationService generates LinuxCNC files → upload → backend restart.

## Project-Specific Rules

- Display `DiscoveredSlave` `Index`, `VendorId`, and `ProductCode` as raw integers — **no hex conversion**. The UI shows these values directly without formatting them as `0x...`.

## Key Files

| File | Purpose |
|------|---------|
| `Services/MachineControlService.cs` | All HTTP communication; server URL configured here |
| `Services/ConfigurationService.cs` | Machine config load/save and LinuxCNC file generation |
| `ViewModels/MainViewModel.cs` | Root VM, polling timer, navigation, power/estop commands |
| `ViewModels/SettingsViewModel.cs` | Settings page orchestration |
| `Models/MachineModels.cs` | `MachineConfig`, `AxisSetting`, `HardwareMapping`, `DiscoveredSlave` |
| `Models/MachineStatus.cs` | Observable machine state bound to the main UI |
| `VersionConfig.cs` | Version string — update this when tagging milestones |
| `Resources/Languages/Lang.zh-TW.xaml` | All UI strings (Traditional Chinese) |
| `Resources/Themes/Theme.Dark.xaml` | Color palette and brush definitions |

## NuGet Packages

- `CommunityToolkit.Mvvm` 8.4.0 — MVVM base classes and source generators
- `HelixToolkit.Wpf` 3.1.2 — 3D visualization
- `Microsoft.Xaml.Behaviors.Wpf` 1.1.135 — XAML behavior/trigger support
