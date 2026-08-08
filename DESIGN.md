# Design system

Power Helper's visual identity, in one place, so new screens stay consistent with the rest
of the app.

## Why this look

Most design documents in a repo like this open by arguing for a distinctive identity. This
one argues the opposite, and the reasoning matters more than any token below.

Power Helper is a **tray utility for Windows' own power hardware**. It has no daily
workspace, no content of its own, and no session: you click a tray icon, flip one switch,
and close it. It sits beside the Windows Settings "Power & battery" page — often literally,
since that is where you go next. An app in that position should be **indistinguishable from
the operating system**, because every pixel spent announcing itself is a pixel spent making
the user re-learn a control they already know.

That is the exact opposite of the bet [pc-cleaner](https://github.com/dara-oladapo/pc-cleaner)
makes, and deliberately so. pc-cleaner is a full-window app you sit inside and work in; it
earns a palette, a typeface and a signature readout of its own. Two apps, two jobs, two
answers. Don't port tokens between them.

The previous design went the other way — a hand-drawn amber-on-near-black instrument panel
with custom rocker switches. It was coherent and it was a costume. It hardcoded dark, ignored
the user's accent, reimplemented Windows controls with no focus visuals and nothing for a
screen reader to announce, and looked wrong beside every other window on the desktop. See
[docs/UX-AUDIT.md](docs/UX-AUDIT.md) for the full accounting.

**The rule that follows from all of this: prefer the platform's answer.** If WinUI has a
control, use it rather than templating one. If Windows has a token, take it rather than
picking a value. Spend the effort saved on the things the platform can't do for you — which,
in this app, is honest hardware state and never stranding the user in a setting they didn't
choose.

## The stack

.NET MAUI, Windows head only (`net10.0-windows10.0.19041.0`), unpackaged.

Unpackaged is not a preference. The app enables and disables a PCI device, which needs
administrator rights, and **an MSIX-packaged app cannot request elevation** — its
`requestedExecutionLevel` is ignored and it always runs at medium integrity. Unpackaged WinUI 3
is the only shape of MAUI app that can hold the privileges this one needs. The consequence
is that `PublishSingleFile` is unavailable (WinUI 3 does not support it), so releases ship a
self-contained folder — as a `.zip` and inside the Inno Setup installer — rather than the
single portable `.exe` earlier versions had.

## Project shape

| Project | Target | Holds |
|---|---|---|
| `PowerHelper.Core` | `net10.0-windows` | Services, `PowerHelperEngine`, and the tray icon |
| `PowerHelper.App` | `net10.0-windows10.0.19041.0` (MAUI) | The settings window |

The tray lives in Core rather than in the MAUI app, for a specific reason: `NotifyIcon` and
`ContextMenuStrip` need WinForms, and a project with both MAUI's and WinForms' implicit
usings has `Application`, `Button`, `Label`, `Image`, `Color` and a dozen more names
ambiguous in every file. Confining WinForms to Core means the MAUI app never sees a second UI
framework's type names.

`PowerHelperEngine` is the single owner of settings, hardware policy and status polling. Both
surfaces render from the events it raises and neither touches a device directly. Every device
call and every status read passes through one `SemaphoreSlim`, because `pnputil`, `powercfg`
and the WMI queries are all slow enough to overlap, and `BatteryStatusService` keeps rolling
samples for its own charge-rate estimate that two concurrent readers would corrupt.

## Colour tokens

Defined in `Resources/Styles/Colors.xaml`. Values track **WinUI 3's Fluent tokens** rather
than being invented.

| Token | Light | Dark | Use |
|---|---|---|---|
| `Layer` / `LayerDark` | `#F3F3F3` | `#202020` | Page background |
| `Card` / `CardDark` | `#FFFFFF` | `#2B2B2B` | Setting-row cards, the status card |
| `CardStroke` / `CardStrokeDark` | `#E5E5E5` | `#303030` | Card borders |
| `SubtleFill` / `SubtleFillDark` | `#E9E9E9` | `#383838` | Meter track, chips |
| `Divider` / `DividerDark` | `#EAEAEA` | `#303030` | Hairline between rows in one card |
| `TextPrimary` / `TextPrimaryDark` | `#1B1B1B` | `#FFFFFF` | Primary text |
| `TextSecondary` / `TextSecondaryDark` | `#5D5D5D` | `#C5C5C5` | Descriptions, captions, icons |
| `CautionFill` / `Stroke` / `Text` | `#FFF4CE` / `#EDD9A0` / `#9D5D00` | `#433519` / `#5C4A22` / `#FCE100` | The message strip, and a charge meter below the warning threshold |
| `CriticalFill` / `Stroke` / `Text` | `#FDE7E9` / `#F0C7CB` / `#C42B1C` | `#442726` / `#5C3634` / `#FF99A4` | The message strip when an operation actually failed |

Dark is an intentional palette, not the light set inverted — `#202020`/`#2B2B2B` are the
layer and card fills Windows itself uses, and a true black would sit wrong beside every other
window.

**There is deliberately no accent token.** The accent belongs to the user. WinUI's `Switch`,
`Slider` and `Button` paint themselves with the system accent for free, and the single place
the app draws its own accent fill — the charge meter — reads it out of the registry at
runtime (`SystemThemeService.CurrentAccent`). A hardcoded accent in `Colors.xaml` would be a
value that quietly stops matching the rest of the desktop.

Rule: colour carries information or it isn't used. On this page exactly one thing is
coloured on purpose — the charge meter turns `CautionText` below the user's own warning
threshold. `Caution` and `Critical` otherwise appear only on the message strip, and never
to mean "important".

## Type

No `FontFamily` is set anywhere. That is the design decision, not an omission: the window
inherits the platform UI face — **Segoe UI Variable** on Windows 11, **Segoe UI** on
Windows 10 — because a settings surface that ships its own typeface announces itself as
third-party on a screen where every neighbouring window uses the system font.

The scale is WinUI's, in `Resources/Styles/Styles.xaml`:

| Role | Size | Where |
|---|---|---|
| Hero | 32 semibold | The battery percentage, and nothing else |
| Group header | 14 semibold | "Graphics", "Power", "Display", … |
| Row title | 14 | The thing a switch controls |
| Row description / caption | 12 secondary | Why you'd want it, and what it costs |
| Readout | 13, right-aligned | Slider values, so digits stay in one column |

## Spacing and shape

4px base. Card padding `16,14`; page padding `24,20`; card radius 8; control radius 4
(WinUI's `OverlayCornerRadius` and `ControlCornerRadius`). Indented sub-rows sit at 50px so
they line up under the row title rather than under its icon.

## Icons

Vector `PathGeometry` on a 20×20 grid in `Resources/Styles/Icons.xaml`, rendered through
`Microsoft.Maui.Controls.Shapes.Path`.

Geometry rather than glyphs from Segoe Fluent Icons: that font ships on Windows 11 but not on
every Windows 10 install, and a missing glyph renders as a hollow box. A `Path` takes its
`Stroke` from a binding, so one definition covers both themes; a `MauiImage` SVG is baked to
a PNG at build time and can't be tinted. No icon font, no NuGet package.

Single-weight strokes, flat terminals, round caps and joins, geometry over illustration —
the same family as the app mark. Draw new ones on the same grid.

## Layout

**One column of full-width rows, grouped under headings.** This is the Windows Settings app's
own structure, and it replaced a grid that reflowed into up to four columns of cards. No
Windows settings surface multi-columns its own setting rows: at four columns the eye has to
re-find the control position on every row, and the reflow was solving wasted width, which a
settings page does not have.

A row is `icon · title + description · control`. Rows that belong together share one card and
are separated by a hairline. A control whose value needs a second control (brightness level,
warning threshold) gets an indented sub-row underneath that enables and dims with its switch,
so the relationship is visible rather than implied by adjacency.

## The signature element: the charge meter

The status card opens with the battery percentage at 32px and a horizontal meter of the real
charge underneath. The meter is two star-width `ColumnDefinition`s, not a pixel figure, so it
stays a true proportion at any window width, and it turns the caution colour below the user's
own warning threshold.

It's the one piece of drawing the app does for itself, and it earns that by being real
information — the same reasoning behind pc-cleaner's capacity readout, arrived at
independently for a different quantity.

## States that aren't the happy path

Three of this app's five features depend on hardware that not every laptop has. A control for
something this machine can't do is **disabled and says why** — "No discrete GPU was detected,
so there's nothing to switch off" — never just greyed out. A faded switch with no explanation
is the single most common way a capability-gated UI wastes someone's afternoon.

Failure gets the message strip: WinUI's InfoBar caution/critical tokens, a specific message
naming the likely cause, and a dismiss button. Before it existed the app had no way to tell
you anything had gone wrong at all — every service returned a `bool` and every caller
discarded it.

## Threading

Nothing slow runs on a UI thread. `pnputil`, `powercfg`, `schtasks` and WMI are all slow
enough to freeze a window, and all four used to run inline on it.

The engine raises its events on whichever thread finished the work, deliberately — it has two
consumers on two different message pumps and shouldn't guess. Each surface marshals for
itself: the page through `MainThread.BeginInvokeOnMainThread`, the tray through its own
`Post` helper onto its WinForms loop.

## Extending this

Adding a setting: put it in the group it belongs to, reuse the row shape, take colours from
`Colors.xaml` and sizes from `Styles.xaml` rather than typing a value, use a real WinUI
control rather than templating one, give it a `SemanticProperties.Description`, disable it
with a reason if the hardware might not support it, and — if it changes hardware state — make
sure the restore path in `PowerHelperEngine.RestoreOnExit` puts it back.
