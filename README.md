# Power Helper

A lightweight Windows tray utility for laptop power management. The discrete-GPU toggle needs NVIDIA Optimus (Intel/AMD integrated + NVIDIA discrete graphics — this is what it was built for, on a Lenovo Legion); everything else (power plan, refresh rate, brightness, battery warning) is plain Windows/WMI and works on any Windows laptop.

## Features

- **Disable the discrete GPU on battery** — hard-disables the dGPU at the device level (not just a driver preference) whenever you unplug, so nothing can wake it back up until you're on AC again. A manual "enable/disable now" override is one click away if you need the dGPU while still on battery.
- **Live battery status** — real percentage and time remaining/until-full, shown directly on the tray icon (no hover required) and refreshed every 30 seconds. Falls back to self-estimating the charge rate when the hardware's own reporting is unreliable, and calls out when the battery is genuinely losing charge despite being plugged in (system drawing more than the charger supplies).
- **Power plan matched to power source** — Power saver on battery, Balanced on AC. Deliberately does *not* force High performance, since that pins CPU clock speed and ramps the fan regardless of actual heat; Performance stays a manual choice (Fn+Q or Windows Settings).
- **Refresh rate throttling on battery** — drops to 60Hz to save power, restores your native rate on AC or when the app exits.
- **Brightness locked on battery** — caps the panel to a level you set whenever unplugged, and restores exactly what you had (not a guessed default) when you plug back in or exit the app.
- **Low battery warning** — a toast notification at a threshold you set, with hysteresis so it doesn't repeat every poll.
- **Starts with Windows** — registers an elevated logon task, so it doesn't repeatedly prompt for UAC.
- **Update checker** — checks GitHub Releases shortly after startup and notifies you if a newer version is out; "Check for Updates..." in the tray menu triggers it manually anytime. Purely informational — it never downloads or installs anything on its own.

Every automatic feature above is opt-in except the dGPU toggle (on by default) — nothing else changes your system until you turn it on.

## Settings window

Double-click the tray icon (or right-click → Settings…) for a resizable window with live status and every toggle, alongside the tray's own context menu — the two stay in sync. The layout is responsive: cards reflow from a single column on a narrow window up to a capped, centered multi-column grid on wide or ultrawide displays, so nothing requires scrolling on a reasonably sized window.

All settings persist across restarts to `%AppData%\PowerHelper\settings.json`.

## Requirements

- Windows 10/11
- Administrator rights (required to enable/disable the GPU device and manage the startup task — the app always runs elevated)
- NVIDIA Optimus (Intel/AMD integrated + NVIDIA discrete graphics) only for the dGPU toggle; every other feature works on any Windows laptop, and the dGPU card just disables itself if no discrete GPU is found

## Installation

Grab the latest release from the [Releases](../../releases) page — two options, both self-contained (no separate .NET install needed):

- **`PowerHelper-Setup-X.Y.Z.exe`** (recommended) — installs to Program Files, adds a Start Menu entry and optional desktop shortcut, and gives you a proper uninstaller (also cleans up the startup scheduled task if you had it enabled).
- **`PowerHelper-X.Y.Z-win-x64.exe`** — the portable single-file exe, if you'd rather not install anything.

Both require administrator rights to run (see [Requirements](#requirements)).

## Building from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```
dotnet build
```

The built app is at `src/PowerHelper/bin/Debug/net10.0-windows/PowerHelper.exe`. Since it needs to enable/disable a PCI device, run it as Administrator.

### Building the installer locally

Requires [Inno Setup 6](https://jrsoftware.org/isinfo.php) (`choco install innosetup`). Publish a self-contained build first, then compile the installer against it:

```
dotnet publish src/PowerHelper/PowerHelper.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:Version=0.1.0 -o publish
iscc /DAppVersion=0.1.0 /DSourceExe="%cd%\publish\PowerHelper.exe" installer\setup.iss
```

Output lands in `installer-output\`. CI (`.github/workflows/release.yml`) runs the same two steps automatically on every version tag.

## License

[MIT](LICENSE)
