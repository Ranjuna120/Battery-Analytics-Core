# Battery-Analytics-Core

Low-level + modern UI battery analytics toolkit for Windows laptops. Collects detailed pack telemetry (design / full charge capacity, health %, cycle count, temperature, chemistry, live power, voltage, state) using the Windows Battery Class Driver (`DeviceIoControl` + `IOCTL_BATTERY_*`) with layered fallbacks (legacy device paths, WinRT aggregate, WMI) and a WPF dashboard (gradient theme, dark mode, tray icon, history sparkline, toast alerts).

## Key Features

Core (Console):
* Native Battery Class Driver interop (IOCTL_BATTERY_QUERY_*)
* Multi‑battery enumeration + legacy fallback (`\\.\BatteryN`)
* Static info: manufacturer, serial, name, chemistry, manufacture date, cycle count
* Health % (FullChargedCapacity / DesignedCapacity) & temperature (if supported)
* Live dynamic status: remaining capacity, rate (mW), voltage, flags, ETA to empty
* CSV logging & Prometheus metrics exporter
* Alerts (temperature / health) + graceful degradation when data missing
* Re‑enumeration option to cope with docking / hot‑swap

UI (WPF Desktop):
* Modern gradient glass style + accent palette
* Dark mode toggle (custom switch) & semi‑transparent cards
* Real‑time sparkline (last 5 min charge %)
* Tray icon (minimize to tray) & context menu (Start / Stop / Refresh / Exit)
* Balloon (toast style) alerts (high temperature / low health)
* Auto percentage + charging/discharging tooltip
* Theming resources isolated under `Themes/Colors.xaml` for easy customization

Fallback Providers:
* WinRT `Windows.Devices.Power.Battery.AggregateBattery`
* WMI `Win32_Battery` (charge %, design voltage, runtime estimation)

## Project Structure

```
src/
	BatteryMonitor/            # Core console & exporters (CSV, Prometheus, alerts)
	BatteryMonitor.UI/         # WPF UI (gradient, tray, history, theming)
		Themes/Colors.xaml       # Palette + styles (buttons, datagrid, switch)
```

## Quick Start (Console)

```powershell
cd src/BatteryMonitor
dotnet run
```

Output example:

```
Device: \\?\BAT#DEV_...#...
	Manufacturer : OEMCorp
	Device Name  : Smart Battery
	Serial       : 1234ABC
	Chemistry    : LION
	Designed Cap : 60000 mWh
	Full Chg Cap : 49820 mWh
	Cycle Count  : 312
	Mfg Date     : 2023-04-17
	Health       : 83.0%
	Temperature  : 37.2 °C

[14:22:11] \\?\BAT#DEV_...#...
	Remaining : 42155 mWh
	Rate      : -12200 mW
	Voltage   : 11850 mV
	State     : BATTERY_DISCHARGING
	Est Empty : 03:27:00
```

Press Ctrl+C to stop polling.

## Quick Start (UI)

```powershell
cd src/BatteryMonitor.UI
dotnet run
```

Usage:
* Set interval -> Start to begin polling; Stop halts.
* Toggle dark mode switch for dark/light.
* Minimize window -> sent to tray (double‑click icon to restore).
* Right‑click tray icon for menu.
* History card updates each tick (5‑minute sliding window).

## CLI Options (Console)

```text
--interval <sec>       Polling interval seconds (default 5)
--csv <path>           Append CSV logging of each sample
--http <port>          Expose Prometheus metrics at /metrics
--once                 Output static info only and exit
--high-temp <C>        Temperature alert threshold (default 50)
--low-health <pct>     Health alert threshold (default 80)
--reenum <n>           Re-enumerate battery devices every n cycles (0=never)
--help                 Show help
```

Examples:

```powershell
# Static info only
dotnet run -- --once

# Poll every 10s, log to CSV, serve metrics
dotnet run -- --interval 10 --csv log.csv --http 9900

# Aggressive monitoring with alerts
dotnet run -- --interval 5 --high-temp 55 --low-health 75

# Re-enumerate every 60 cycles (~5 min if interval=5)
dotnet run -- --reenum 60
```

## CSV Schema

CSV columns written (append mode):

```
TimestampUtc,Device,Remaining_mWh,Rate_mW,Voltage_mV,Temperature_C,Health_pct,Designed_mWh,Full_mWh,CycleCount
```

Notes:
* Temperature / Health blank if unknown.
* Multiple batteries produce one row per device per poll.

## Prometheus Metrics

Metric names (labels: device):

| Metric | Description |
|--------|-------------|
| `battery_remaining_mwh` | Remaining capacity (mWh) |
| `battery_rate_mw` | Current rate (+ charge / - discharge) |
| `battery_voltage_mv` | Pack voltage (mV) |
| `battery_temperature_c` | Temperature (°C, if reported) |
| `battery_health_pct` | Health percentage (if computable) |

Endpoint: `http://localhost:<port>/metrics` when `--http <port>` is supplied.

## Data Interpretation

| Field | Meaning | Notes |
|-------|---------|-------|
| DesignedCapacity | Factory design capacity (mWh) | May be 0 on some firmware |
| FullChargedCapacity | Current learned full capacity | Declines with aging |
| Health % | FullCharged / Designed * 100 | Only if both non-zero |
| CycleCount | Reported by pack | Often missing or 0 |
| Rate | mW (+ charging, − discharging) | Sign convention varies but negative usually discharge |
| Voltage | mV | Raw pack voltage |
| Temperature | °C | Converted from 0.1 Kelvin; may be unsupported |
| PowerState flags | Charging/Discharging/OnLine/Critical | Bitwise flags |

Temperature conversion: `C = (value / 10) - 273.15`

Estimated time to empty (if discharging): `RemainingCapacity (mWh) / |Rate| (mW) * 3600` seconds.

## Architecture Overview

`BatteryInterop` performs:

1. Enumeration via `SetupDiGetClassDevs` + `SetupDiEnumDeviceInterfaces` using `GUID_DEVINTERFACE_BATTERY`.
2. Per device: open handle (`CreateFile`).
3. Query tag (`IOCTL_BATTERY_QUERY_TAG`).
4. Static queries using `IOCTL_BATTERY_QUERY_INFORMATION` with various `BATTERY_QUERY_INFO_LEVEL` values.
5. Live status via `IOCTL_BATTERY_QUERY_STATUS` (rate, remaining capacity, voltage, power state).
6. Helpers convert raw bytes to strongly typed records; errors are localized to each query.

## UI Theming

Edit `Themes/Colors.xaml` to customize:
* Accent & gradient colors (`AccentColor`, `GradientStartColor`, `GradientEndColor`)
* Light/Dark brush mappings (`BgColor`, `FgColor`, `PanelBgLight/Dark`, etc.)
* Button styles: `PrimaryButton`, `SecondaryButton`, `GhostButton`, `SwitchToggle`

Minimal example (change accent):
```xml
<Color x:Key="AccentColor">#FF00C2A8</Color>
```
Rebuild or just save — WPF resource updates at runtime for many elements.

## Limitations

* Some firmware blocks particular info levels (returns ERROR_INVALID_FUNCTION / NOT_SUPPORTED).
* Cycle count or temperature may be absent or zero.
* Values can transiently fail if battery not ready (Tag becomes 0); re-enumerate if needed.
* Health % meaningless if design or full charge capacity is 0.

## Status & Roadmap

Implemented:
* Native + legacy enumeration
* WinRT + WMI fallbacks
* Health / temperature / cycle count extraction
* CSV logging & Prometheus export
* Alerts (temp / health) & tray notifications
* WPF UI (history sparkline, tray, dark mode, gradient theme, modern buttons)

Possible Next Enhancements:
| Area | Idea |
|------|------|
| Charts | Add capacity decay trend (SQLite + retention) |
| UI | Circular gauge + battery icon set per state |
| Export | InfluxDB / OpenTelemetry exporter |
| Packaging | MSIX or standalone single-file publish |
| Analytics | Degradation rate projection / anomaly detection |
| Calibration | Suggest recalibration cycles & track results |
| Plugin | Additional sensors (EC SMBus, ACPI evaluation) |

## Troubleshooting

| Symptom | Possible Cause | Action |
|---------|----------------|--------|
| No batteries found | Desktop / enumeration blocked | Run on a laptop; check Device Manager batteries category |
| Tag query fails | Driver transient | Retry after short delay |
| Temperature always n/a | Firmware not exposing | Expected on many models |
| Cycle count 0 | Not reported | Can't infer reliably |
| Access denied | Rare permission issue | Try elevated (Run as Administrator) |

## Solution File (Optional)

To generate a solution for IDE convenience:
```powershell
dotnet new sln -n BatteryAnalytics
dotnet sln add .\src\BatteryMonitor\BatteryAnalyticsCore.csproj
dotnet sln add .\src\BatteryMonitor.UI\BatteryMonitor.UI.csproj
```

## License

MIT (add a LICENSE file if distributing). Contributions welcome.

---
Generated initial core + UI by automation. Extend responsibly.
