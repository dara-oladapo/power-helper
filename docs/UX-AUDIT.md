# UX audit

An audit of Power Helper's two surfaces — the tray menu and the settings window — as they
stood at v0.1.2, and what was done about each finding.

The short version: the app was well-built underneath and dressed as something it isn't. It
presented a hand-drawn amber-on-near-black instrument panel with custom controls, on a
desktop where every other window follows the user's chosen theme and accent. That costume
cost real usability — no theme following, no focus indicators, controls a screen reader
couldn't name — and it bought nothing a tray utility needs. The fix was not to redecorate
but to stop decorating: adopt the platform's own controls, tokens and layout conventions,
and spend the remaining effort on the things Windows can't do for you.

Findings are ordered by severity. Each says what shipped in response.

---

## Severity 1 — the window ignored Windows

### 1.1 Hardcoded dark theme

`Background="#1A1A18"` on the window, with every colour a literal hex in the same file.
On a machine in light mode the settings window was a black rectangle among white ones. There
was no code path that read the user's app mode at all.

**Fixed.** The window now follows the OS by default. Every colour is an `AppThemeBinding`
over a token pair in `Resources/Styles/Colors.xaml`, and `UserAppTheme` is left at
`Unspecified` unless the user says otherwise — so a desktop that switches at sunset takes the
window with it, rather than sampling the theme once at launch and freezing.

**System / Light / Dark** is also offered under *General*, defaulting to System and persisted
in `settings.json`. See [DESIGN.md](../DESIGN.md#the-theme-preference) for why the default is
the interesting part and the override is the cheap part.

### 1.2 A light title bar bolted onto a dark window

Even setting the theme aside, the non-client area was whatever Windows drew by default.
The most recognisable "this app doesn't belong here" signal there is.

**Fixed.** `DwmWindowStyle.SetTitleBarTheme` sets `DWMWA_USE_IMMERSIVE_DARK_MODE`, with the
build-19 fallback for Windows 10 1809–1909, and re-applies on a live theme change with a
`SWP_FRAMECHANGED` nudge so the frame actually repaints.

### 1.3 The accent colour was ours, not the user's

A fixed amber (`#FFB020`) stood in for the accent everywhere. Picking an accent on the
user's behalf is a thing an app on Windows does not get to do.

**Fixed.** Every interactive control is now a real WinUI control, which paints itself with
the system accent for free. The one place the app draws its own accent fill — the charge
meter — reads `AccentPalette` out of the registry, taking the Light2 shade on dark and the
Dark1 shade on light exactly as Windows itself does, and falls back to Windows' default blue
if the value isn't the shape expected rather than rendering a colour from misread bytes.

### 1.4 The tray menu was always light

`ContextMenuStrip` has no dark mode. On a dark desktop the menu was a white slab. This was
the more visible half of the problem, not the lesser one — most sessions never open the
window at all.

**Fixed.** A `ProfessionalColorTable` and renderer built from the same token values as the
window, with the submenu arrow and the checkmark redrawn in the palette's text colour
(the base renderer draws both in a near-black system colour that vanishes on dark), plus
`DWMWA_WINDOW_CORNER_PREFERENCE` so it isn't the one square-cornered menu on the desktop.

---

## Severity 2 — accessibility

### 2.1 The toggles were unnameable

The switch was a templated `CheckBox` with **no content** and no `AutomationProperties.Name`.
A screen reader reached it and had nothing to announce but "checkbox". Six of them, in a row,
all identical.

**Fixed.** Real `Switch` controls, each with a `SemanticProperties.Description` matching its
visible label.

### 2.2 No focus indicators anywhere

Every control had the default focus visual suppressed or templated away. Tabbing through the
window moved focus invisibly.

**Fixed.** WinUI's own focus rectangle, inherited with the platform controls.

### 2.3 Text was upper-cased in code

`BatteryStatusText.Text = (...).ToUpperInvariant()` and labels like `"P O W E R   H E L P E R"`
with literal spaces between letters. Screen readers spell letter-spaced text out
character by character, and the upper-casing destroyed the real string.

**Fixed.** Sentence case throughout. The letter-spaced wordmark is gone.

### 2.4 Disabled meant "35% opacity"

The only signal that a control was unavailable was `Opacity="0.35"` — no text, no reason.
A user on a laptop with no discrete GPU saw a faded switch and no explanation.

**Fixed.** Every capability-gated control is disabled *and* its description is rewritten to
say why ("No discrete GPU was detected, so there's nothing to switch off").

---

## Severity 3 — the UI blocked on hardware

Every device call ran on the UI thread.

| Call | Where it ran | Cost |
|---|---|---|
| `pnputil /disable-device` | UI thread, on switch toggle | seconds |
| `powercfg /setactive` | UI thread, on switch toggle | ~100 ms |
| `schtasks /Query` | UI thread, **every 3 seconds** while the window was open | a process launch |
| WMI battery + GPU state | UI thread, every 3 s (window) / 30 s (tray) | tens of ms, cross-process |

### 3.1 Toggling a switch froze the window

`OnSettingsChangedExternally` → `ApplyDesiredState` → `pnputil` synchronously.

**Fixed.** `PowerHelperEngine.ApplyDesiredStateAsync` moves the work to a background thread
behind a `SemaphoreSlim`, so overlapping requests can't race the device.

### 3.2 The window shelled out to `schtasks` every three seconds

`LoadFromSettings` called `StartupService.IsRegistered()` on a 3-second timer, purely to
redraw a switch that hadn't moved. A process launch every three seconds for as long as the
window stayed open.

**Fixed.** The answer is cached on the engine and re-queried only when the switch is actually
used.

### 3.3 The periodic reload fought the user

The same 3-second tick reassigned `Slider.Value` from settings. Dragging the brightness
slider across a tick boundary snapped it back under your cursor.

**Fixed.** The window no longer polls settings at all. The engine raises `SettingsChanged`
when something actually changes, and the two surfaces render from it.

### 3.4 Sliders wrote to disk on every pixel of movement

`ValueChanged` → `OnSettingsChangedExternally` → `settings.json` written *and* the brightness
re-applied to the panel, on every intermediate value of a drag.

**Fixed.** `ValueChanged` updates the label only; `DragCompleted` persists and applies.

---

## Severity 4 — failure was invisible

The app had no way to tell the user anything had gone wrong. Every service returned a `bool`
and every caller discarded it.

- `_gpuService.Enable(...)` failing → nothing. The switch stayed where you put it and the
  GPU didn't move.
- `StartupService.Register()` failing → the switch silently snapped back, with no
  explanation, which reads as a UI bug rather than a refused operation.

**Fixed.** A message strip in the window, using WinUI's InfoBar caution/critical tokens,
carrying a specific message ("Windows refused the device change — this usually means another
process is holding the adapter, or the app isn't running elevated") and dismissible. The
startup switch reports a refused registration instead of just reverting.

---

## Severity 5 — layout and windowing

### 5.1 A four-column grid of setting rows

The window reflowed cards into up to four columns on a wide display, via ~45 lines of
code-behind that reparented every card into freshly-built row containers on each resize.

No Windows settings surface multi-columns its own setting rows, and for good reason: at four
columns the eye has to re-find the control position on every row. The reflow solved wasted
width, which is not a problem a settings page has.

**Changed** — and this is the one finding that removes a deliberate feature, so it is the one
most worth arguing with. Rows are now a single full-width column, grouped under headings, the
way the Settings app does it. The reparenting code-behind is gone entirely.

### 5.2 The window opened taller than the screen

`Height="820"` fixed, with `WindowStartupLocation="CenterScreen"`. On a 768-tall laptop panel
it opened taller than the work area, centred on the *screen* rather than the work area, so it
sat under the taskbar.

**Fixed.** 560×780 with a 420×420 floor, and closing the window hides it rather than
destroying it, so it reopens where you left it.

### 5.3 Closing the window was ambiguous

Nothing distinguished "close this window" from "quit the app" — closing left the app running
with no acknowledgement, which is right, but there was no cue.

**Fixed.** The close button hides the window and the app stays in the tray. Exit is an
explicit menu choice, and it is the only path that restores the hardware.

### 5.4 Left-clicking the tray icon did nothing

Only double-click opened the window. Every other tray icon on the desktop — network, volume,
OneDrive — opens on a single left click.

**Fixed.** Single left click opens it; double-click still works.

### 5.5 The tray menu was a flat list of eleven items

Status lines, four automatic policies, two independent toggles and three commands, separated
only by rules.

**Fixed.** The four "what should happen when I unplug?" policies are now one
**Automatically on battery** submenu, which shortens the top level to something scannable.
The two status lines are `ToolStripLabel`s in the secondary text colour rather than disabled
menu items — a disabled item is greyed to the point of failing contrast, which is a poor way
to render text whose only job is to be read.

---

## Deliberately not done

- **Mica / acrylic backdrop.** Available to WinUI 3, and it would look right. Left out of
  this pass because it interacts with how the window is hidden and re-shown from the tray,
  and that path is the riskiest new thing here — worth landing on its own once the tray
  lifecycle has been exercised on real hardware.
- **A first-run explanation.** `AutoDisableDgpuOnBattery` defaults to **on**, so the app
  starts changing hardware state the first time you unplug, before you've opened anything.
  That deserves a first-run moment. It's a product decision rather than a UI fix, so it is
  called out here and left for its own change.
- **Restoring hardware on forced termination.** Task Manager kill and some shutdown paths
  skip the restore. Genuinely hard to fix properly; the stranded-hardware issue template now
  captures the recovery steps.

---

## On the tooling

The repository enables the `frontend-design` plugin in `.claude/settings.json`. Its skills
weren't loaded in the session this audit was produced in, so the design-critique pass here
was carried out by hand against the same criteria: platform conventions, colour and
contrast, focus and keyboard reachability, semantics for assistive technology, feedback and
error surfaces, and layout rhythm. Worth re-running through the plugin once it's available
to see what a second pass turns up.
