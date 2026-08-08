# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

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

[0.1.2]: https://github.com/dara-oladapo/power-helper/compare/v0.1.1...v0.1.2
[0.1.1]: https://github.com/dara-oladapo/power-helper/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/dara-oladapo/power-helper/releases/tag/v0.1.0
