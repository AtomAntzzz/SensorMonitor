# SysPulse

> 🌐 **[中文说明 →](README.zh-CN.md)**

![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D6)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)
![PowerToys](https://img.shields.io/badge/PowerToys-Command%20Palette-blue)

A **PowerToys Command Palette dock extension** that shows live hardware sensor readings — CPU clock and CPU / GPU / motherboard temperature — right on the Command Palette dock strip at the edge of your screen.

Sensor data comes from the open-source [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) (MPL-2.0), served by an elevated background process that the extension launches on demand.

## Features

- **Four preset dock slots** — CPU clock, CPU temperature, GPU temperature, motherboard temperature — each its own control, so nothing gets truncated.
- **Right-click a slot** to rotate to the previous / next metric within its category; **click a slot** to open a picker page listing the candidates for that category.
- **Settings page** — refresh interval (1 / 2 / 5 s) and temperature unit (°C / °F).
- **Persistent selection** — your slot choices survive restarts.
- **Silent auto-start** — after install the host runs via a scheduled task, so there are no UAC prompts at runtime.

## Requirements

- Windows 10 / 11 (x64 or ARM64)
- PowerToys with **Command Palette** (CmdPal Extensions SDK ≥ 0.9.260303001)
- **PawnIO driver** for CPU & motherboard temperatures (installed separately). GPU temperature works without it.

## Install

Distribution is a **signed Inno installer** that bundles a self-contained host, registers the scheduled task, and provisions the extension MSIX machine-wide: one UAC prompt at install, zero at runtime, and a single uninstall entry that cleans everything up.

> ⏳ Prebuilt **Release / WinGet** packages are still pending (tracked as R4b). Until then, build the installer from source (below).

## Build from source

```powershell
# Host unit tests
dotnet test tests/SysPulse.Host.Tests

# Build the signed installer → installer/Output/SysPulseSetup_x64.exe
powershell -ExecutionPolicy Bypass -File installer/build.ps1
```

The CmdPal extension builds via `dotnet build -p:Platform=x64`, but deploying it during development goes through Visual Studio (**Deploy**, then **Reload** inside Command Palette). See [docs/references/installer.md](docs/references/installer.md) and [docs/references/msix-packaging.md](docs/references/msix-packaging.md) for details.

## How it works

Two processes:

- **`SysPulse.Host`** — elevated; reads sensors via LibreHardwareMonitorLib and serves snapshots over a named pipe.
- **`SysPulseExtension`** — the CmdPal MSIX extension; its dock slot controls poll a shared snapshot cache every second, and if the host isn't running a scheduled task silently starts it.

Why two processes (elevation + driver access) is unavoidable: see [docs/architecture.md](docs/architecture.md) (D1).

## Documentation

| Topic | Doc |
|-------|-----|
| Architecture & design decisions | [docs/architecture.md](docs/architecture.md) |
| Roadmap / current status | [docs/plans/2026-07-18-verification-and-next-phase.md](docs/plans/2026-07-18-verification-and-next-phase.md) |
| Extension & dock notes | [docs/references/cmdpal-extension.md](docs/references/cmdpal-extension.md) |
| Sensors & drivers | [docs/references/sensor-sources.md](docs/references/sensor-sources.md) |

## Credits

- Sensor backend: [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) (MPL-2.0).
- Author: **AtomAntzzz**

## Support

If you find SysPulse useful, consider supporting the project. See [`.github/FUNDING.yml`](.github/FUNDING.yml) for available channels — just uncomment your preferred option and fill in your ID.
