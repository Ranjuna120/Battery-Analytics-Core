# Battery-Analytics-Core

Low-level Windows laptop battery analytics toolkit. Provides detailed metrics (design/full charge capacity, health %, cycle count, temperature, chemistry, live rate/voltage, multi‑battery enumeration) using the Windows Battery Class Driver (`DeviceIoControl` + `IOCTL_BATTERY_*`).

## Features

* Enumerate one or more battery packs (detachable / tablet scenarios supported)
* Static info: Manufacturer, Serial, Device Name, Chemistry, Manufacture Date (if exposed)
* Capacity data: Designed vs FullCharged -> Health %
* Cycle Count (if firmware reports)
* Temperature (0.1 K -> °C) when supported
* Live status polling: RemainingCapacity, Rate (mW, sign indicates direction), Voltage, PowerState flags
* Graceful handling of unsupported queries (best‑effort)

## Quick Start

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

## CLI Options

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

## Limitations

* Some firmware blocks particular info levels (returns ERROR_INVALID_FUNCTION / NOT_SUPPORTED).
* Cycle count or temperature may be absent or zero.
* Values can transiently fail if battery not ready (Tag becomes 0); re-enumerate if needed.
* Health % meaningless if design or full charge capacity is 0.

## Roadmap / Extension Ideas

| Area | Idea |
|------|------|
| Logging | Periodic snapshots -> CSV / SQLite for trend & health decay graph |
| Alerts | High temperature, health threshold, sudden capacity drop notifications (Toast) |
| UI | WPF/WinUI dashboard with charts (LiveCharts / ScottPlot) & tray icon |
| Export | Minimal Web API / Prometheus metrics endpoint |
| Aggregation | Combine multiple batteries (sum mWh; compute overall health) |
| Cache | Persist first seen DesignedCapacity to mitigate disappearing OEM data |
| Calibration | Detect anomalies, suggest full discharge/charge cycle |
| Plugin | Abstraction for alternative data sources (WinRT, WMI fallback) |

## Troubleshooting

| Symptom | Possible Cause | Action |
|---------|----------------|--------|
| No batteries found | Desktop / enumeration blocked | Run on a laptop; check Device Manager batteries category |
| Tag query fails | Driver transient | Retry after short delay |
| Temperature always n/a | Firmware not exposing | Expected on many models |
| Cycle count 0 | Not reported | Can't infer reliably |
| Access denied | Rare permission issue | Try elevated (Run as Administrator) |

## License

MIT (add a LICENSE file if distributing). Contributions welcome.

---
Generated initial core by automation. Extend responsibly.
