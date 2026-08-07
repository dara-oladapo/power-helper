# Power Helper

A lightweight Windows tray utility for laptops with a discrete + integrated GPU (NVIDIA Optimus). Built for a Lenovo Legion, but the power-plan and refresh-rate features work on any Windows laptop.

## Features

- **Disable the discrete GPU on battery** — hard-disables the dGPU at the device level (not just a driver preference) whenever you unplug, so nothing can wake it back up until you're on AC again. A manual override is one click away if you need it while still on battery.
- **Live battery status** — real percentage and time remaining/until-full, shown directly on the tray icon (no hover required) and refreshed every 30 seconds. Falls back gracefully when the hardware doesn't report a reliable charge rate.
- **Power plan matched to power source** — Power saver on battery, Balanced on AC. Deliberately does *not* force High performance, since that pins CPU clock speed and ramps the fan regardless of actual heat.
- **Refresh rate throttling on battery** — drops to 60Hz to save power, restores your native rate on AC or when the app exits.
- **Low battery warning** — a toast notification at a threshold you set.
- **Starts with Windows** — registers an elevated logon task, so it doesn't repeatedly prompt for UAC.

All settings persist across restarts and are controllable from the tray menu or the settings window (double-click the tray icon).

## Requirements

- Windows 10/11
- A laptop with NVIDIA Optimus (Intel/AMD integrated + NVIDIA discrete graphics)
- Administrator rights (required to enable/disable the GPU device and manage the startup task)

## Installation

Download the latest release from the [Releases](../../releases) page and run the `.exe`. It's self-contained — no separate .NET installation needed.

## Building from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```
dotnet build
```

The built app is at `src/PowerHelper/bin/Debug/net10.0-windows/PowerHelper.exe`. Since it needs to enable/disable a PCI device, run it as Administrator.

## License

[MIT](LICENSE)
