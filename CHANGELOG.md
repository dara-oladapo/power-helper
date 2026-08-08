# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [0.2.0] - 2026-08-08

A UX pass over both surfaces, and the settings window rebuilt on .NET MAUI. See
[docs/UX-AUDIT.md](docs/UX-AUDIT.md) for the findings this came out of, and
[DESIGN.md](DESIGN.md) for the design system.

### Changed

- **The settings window is now a .NET MAUI app** rather than WPF, and looks like part of Windows instead of a custom instrument panel. It follows your light/dark app mode and your accent colour, uses the system UI font and real WinUI controls, and tints its title bar to match.
- **The tray menu follows the app mode too**, with rounded corners on Windows 11. The four "what happens when I unplug" policies moved into one **Automatically on battery** submenu, which shortens the top level to something scannable.
- **Left-clicking the tray icon opens the window.** Previously only a double-click did, which is not how any other tray icon on the desktop behaves.
- **Settings are laid out as one column of grouped rows**, the way the Windows Settings app does it, replacing a grid that reflowed into up to four columns of cards.
- Closing the window now hides it and leaves the app in the tray. **Exit** in the tray menu quits, and is still the only path that restores the hardware.
- The portable release artifact is a `.zip` containing a folder rather than a single `.exe`. WinUI 3 doesn't support single-file publishing; nothing about running it changed.
- The project is now split into `PowerHelper.Core` (services, policy engine, tray) and `PowerHelper.App` (the MAUI window).

### Added

- A message strip in the settings window that reports failures. Previously a refused device change or a failed startup-task registration was completely silent — every service returned a result and every caller discarded it.
- Controls for hardware you don't have are disabled **and say why**, instead of just being faded.
- `SemanticProperties` on every switch and slider. The old custom toggles had no text and no automation name, so a screen reader had nothing to announce.

### Fixed

- Toggling a setting no longer freezes the window. `pnputil`, `powercfg`, `schtasks` and the WMI queries all ran on the UI thread; they now run behind a gate on a background thread.
- The window no longer launches `schtasks.exe` every three seconds while it's open, which it did purely to redraw a switch that hadn't moved.
- Dragging a slider no longer fights a periodic reload that snapped the value back under the cursor, and no longer writes `settings.json` and re-applies brightness on every intermediate value of the drag.
- The window no longer opens taller than the work area on a short display, or centred behind the taskbar.

## [0.1.2] - 2026-08-08

### Fixed

- Installer failed to auto-launch the app after install, with "CreateProcess failed; code 740. The requested operation requires elevation." `Setup.exe` runs elevated, and a directly-elevated process launching another exe that also demands elevation via its own manifest fails via plain `CreateProcess` — now routed through the shell instead, which handles it correctly.

## [0.1.1] - 2026-08-08

### Added

- Brightness locked to a configurable level on battery, restoring the exact prior level (not a guess) on AC or exit.
- App icon — an amber power glyph on a dark graphite badge, matching the settings window's design — used as both the exe icon and the tray's fallback icon.
- Windows installer (Inno Setup): Program Files install, Start Menu shortcuts, optional desktop shortcut, and a clean uninstaller (also removes the startup scheduled task if it was registered). Published alongside the portable exe.
- In-app update checker: polls GitHub Releases shortly after startup and notifies you if a newer version is out. "Check for Updates..." in the tray menu triggers it manually. Purely informational — never downloads or installs anything.

## [0.1.0] - 2026-08-07

Initial release.

### Added

- Disable the discrete GPU on battery, hard-disabling it at the device level, with a manual "enable/disable now" override.
- Live battery status shown directly on the tray icon (no hover required), with self-estimated charge rate as a fallback when the hardware's own reporting is unreliable.
- Power plan matched to power source — Power saver on battery, Balanced on AC (deliberately not High performance, which pins CPU clock speed regardless of actual heat).
- Refresh rate throttling on battery, restoring your native rate on AC or exit.
- Low battery warning notification at a configurable threshold.
- Start with Windows via an elevated logon task, avoiding repeated UAC prompts.
- Resizable, responsive settings window alongside the tray menu, staying in sync with it.

[0.2.0]: https://github.com/dara-oladapo/power-helper/compare/v0.1.2...v0.2.0
[0.1.2]: https://github.com/dara-oladapo/power-helper/compare/v0.1.1...v0.1.2
[0.1.1]: https://github.com/dara-oladapo/power-helper/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/dara-oladapo/power-helper/releases/tag/v0.1.0
