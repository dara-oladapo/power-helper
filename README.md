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
- **Looks like Windows** — the settings window follows your light/dark mode and accent colour, and so does the tray menu.
- **Update checker** — checks GitHub Releases shortly after startup and notifies you if a newer version is out; "Check for Updates..." in the tray menu triggers it manually anytime. Purely informational — it never downloads or installs anything on its own.

Every automatic feature above is opt-in except the dGPU toggle (on by default) — nothing else changes your system until you turn it on.

## Settings window

Left-click the tray icon (or right-click → Settings…) for a window with live battery status and every setting, alongside the tray's own menu — the two are views of the same state and stay in sync.

It's built to look like part of Windows rather than like a third-party utility: it follows your light/dark app mode and your accent colour, uses the system UI font and real WinUI controls, and lays settings out as a single column of grouped rows the way the Windows Settings app does. Closing the window leaves the app running in the tray; **Exit** in the tray menu is what quits it, and it's the only path that restores everything it changed.

A setting for hardware your machine doesn't have is disabled and says so, rather than silently doing nothing.

All settings persist across restarts to `%AppData%\PowerHelper\settings.json`.

## Platform support

| Capability | Windows | macOS | Linux |
|---|---|---|---|
| Battery status and time remaining | ✅ | ✅ | [#4](../../issues/4) |
| Start at login | ✅ | ✅ | [#5](../../issues/5) |
| Low battery warning | ✅ | ✅ | [#4](../../issues/4) |
| Disable the discrete GPU on battery | ✅ | ❌ no API | [#5](../../issues/5) |
| Power profile matched to power source | ✅ | ❌ needs root | [#5](../../issues/5) |
| Brightness locked on battery | ✅ | ❌ no public API | [#5](../../issues/5) |
| Refresh rate dropped on battery | ✅ | ❌ no public API | [#5](../../issues/5) |
| Lives in the tray / menu bar | ✅ | ❌ needs AppKit | [#3](../../issues/3) |

**macOS is a much smaller app, and honestly so.** Four of the seven capabilities have no
API Apple exposes to a third-party app — Apple Silicon has no discrete GPU to switch, Low
Power Mode needs root, and neither brightness nor refresh rate is reachable from Mac
Catalyst. On top of that, a Catalyst app can't create a menu-bar item (`NSStatusItem` needs
AppKit), so on macOS this is an ordinary windowed battery utility rather than something that
lives in the background. Every unavailable control is disabled and says why.

Every capability is a per-OS question, and on most operating systems the answer to at least
one of them is "there is no API for that" — so support is something each platform
implementation *reports* (`CapabilitySupport` in `PowerHelper.Core/Abstractions`), not something
inferred from what you're running on. A control for a capability the machine can't provide is
disabled and says why, rather than silently doing nothing.

.NET MAUI has no Linux target at all, so Linux isn't a build that can simply be added; the
options are laid out in [#3](../../issues/3), with the per-feature work in
[#4](../../issues/4) and [#5](../../issues/5).

## Requirements

- Windows 10/11
- Administrator rights (required to enable/disable the GPU device and manage the startup task — the app always runs elevated)
- NVIDIA Optimus (Intel/AMD integrated + NVIDIA discrete graphics) only for the dGPU toggle; every other feature works on any Windows laptop, and the dGPU card just disables itself if no discrete GPU is found

## Installation

Grab the latest release from the [Releases](../../releases) page — two options, both self-contained (no separate .NET or Windows App SDK install needed):

- **`PowerHelper-Setup-X.Y.Z.exe`** (recommended) — installs to Program Files, adds a Start Menu entry and optional desktop shortcut, and gives you a proper uninstaller (also cleans up the startup scheduled task if you had it enabled).
- **`PowerHelper-X.Y.Z-win-x64.zip`** — the portable build. Unzip anywhere and run `PowerHelper.exe`.

Both require administrator rights to run (see [Requirements](#requirements)).

> Earlier versions shipped a single portable `.exe`. The UI now runs on .NET MAUI / WinUI 3, which doesn't support single-file publishing, so the portable option is a folder in a `.zip` instead. Nothing else about running it changed.

## Building from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and the MAUI Windows workload:

```
dotnet workload install maui-windows
dotnet build
```

The built app is at `src/PowerHelper.App/bin/Debug/net10.0-windows10.0.19041.0/win-x64/PowerHelper.exe`. Since it needs to enable/disable a PCI device, run it as Administrator.

The solution is two projects:

- **`src/PowerHelper.Core`** — the services that talk to hardware, the engine that owns settings and policy, and the notification-area icon.
- **`src/PowerHelper.App`** — the .NET MAUI settings window.

See [DESIGN.md](DESIGN.md) for why the split falls there, and [docs/UX-AUDIT.md](docs/UX-AUDIT.md) for the UX audit the current UI came out of.

### Building the installer locally

Requires [Inno Setup 6](https://jrsoftware.org/isinfo.php) (`choco install innosetup`). Publish a self-contained build first, then compile the installer against the output directory:

```
dotnet publish src/PowerHelper.App/PowerHelper.App.csproj -c Release -f net10.0-windows10.0.19041.0 -r win-x64 --self-contained true -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -p:Version=0.2.0 -o publish
iscc /DAppVersion=0.2.0 /DSourceDir="%cd%\publish" installer\setup.iss
```

Output lands in `installer-output\`. CI (`.github/workflows/release.yml`) runs the same two steps automatically on every version tag.

## License

[MIT](LICENSE)
