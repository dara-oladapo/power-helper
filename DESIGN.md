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

.NET MAUI. One head per OS: `net10.0-windows10.0.19041.0` (unpackaged) and
`net10.0-maccatalyst`. No Linux head, because MAUI has no Linux target — see
[#3](https://github.com/dara-oladapo/power-helper/issues/3).

Unpackaged on Windows, but not for elevation anymore: the app itself runs `asInvoker`.
Enabling/disabling the discrete GPU device does need administrator rights, but that one
operation is delegated to `PowerHelper.GpuHelper` — a standalone helper exe run through a
pre-registered `RunLevel Highest` scheduled task, triggered with `schtasks /run` rather than
by the app requesting elevation for itself. Staying unpackaged is still required for the
self-contained deployment MAUI/WinUI needs on Windows: `PublishSingleFile` is unavailable
(WinUI 3 does not support it), so releases ship a self-contained folder rather than a single
portable `.exe`.

## Project shape

| Project | Target | Holds |
|---|---|---|
| `PowerHelper.Core` | `net10.0` | Abstractions, `PowerHelperEngine`, settings, update check |
| `PowerHelper.Windows` | `net10.0-windows` | Windows implementations + the tray icon |
| `PowerHelper.GpuHelper` | `net10.0-windows` | Standalone elevated helper — enables/disables the discrete GPU, nothing else |
| `PowerHelper.App` | per-OS MAUI heads | The settings window; macOS implementations |

Core has **no target platform**: no WinForms, no `System.Management`, no P/Invoke.
Everything that touches a device sits behind seven interfaces in `Abstractions/`. The target
framework is what enforces that, and it is worth keeping strict — the earlier shape of the
project imported WinForms, which is how a type called `FeatureSupport` ended up colliding
with `System.Windows.Forms.FeatureSupport`.

Windows gets a project of its own, while macOS lives in `PowerHelper.App/Platforms/MacCatalyst`
the way pc-cleaner does, for exactly one reason: the tray needs WinForms, and a project
carrying both MAUI's and WinForms' implicit usings has `Application`, `Button`, `Label`,
`Image` and `Color` ambiguous in every file.

## Capability, not platform

The rule that shapes everything above: **support is a value an implementation reports, not
something a caller infers from the platform it is running on.**

```csharp
CapabilitySupport.Unavailable(
    "Not available on macOS — Apple Silicon has no discrete GPU to switch, and macOS
     never exposed a public API for it on the Intel machines that did.")
```

That reason is written for a user, because it is rendered verbatim under the setting it
disables. Only the implementation knows whether the true answer is "no NVIDIA adapter in
this laptop", "this panel reports one refresh rate" or "Apple doesn't expose it" — a page
that guesses between those says something untrue on at least one platform.

It also means the same page serves every OS with no branching. When the macOS head landed,
`SettingsPage` needed no changes at all.

The one thing this *cannot* express is the shape of the app itself. On Windows this is a
tray-first utility: hidden window, close-to-hide, Exit in the notification-area menu. On
macOS a Catalyst app cannot create a menu-bar item (`NSStatusItem` needs AppKit), so it is
an ordinary window that quits when closed. That is a difference in what the app *is*, so
`App.xaml.cs` branches on `#if WINDOWS` for the shell — and that is the only `#if` in the
UI layer.

## Threading

Nothing slow runs on a UI thread. Device calls are process launches, WMI queries and
D-Bus round trips depending on the OS, and all of them used to run inline on the window's
thread.

Everything goes through `PowerHelperEngine` behind a single `SemaphoreSlim`, because the
implementations are slow enough to overlap and a battery reader keeping rolling samples for
a charge-rate estimate would have its arithmetic corrupted by two concurrent readers.

The engine raises its events on whichever thread finished the work, deliberately — it has
consumers on different message pumps and shouldn't guess. Each surface marshals for itself:
the page through `MainThread.BeginInvokeOnMainThread`, the tray through its own `Post`
helper onto its WinForms loop.

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

## The theme preference

**System / Light / Dark, defaulting to System, persisted in `settings.json` as
`AppSettings.Theme`.** The default is the part that matters: an app in this position should
look like the OS unless it is told not to, and *System* is a live subscription rather than a
reading taken at launch — a desktop that switches at sunset takes this window with it.

The override exists because pinning is a real preference and refusing it is a worse answer
than spending one row on it. But it is exactly one row, in *General*, as a drop-down with
those three entries — which is what Windows' own **Choose your mode** is — rather than a
theme picker with a preview.

The mechanism worth knowing about: the preference is expressed as MAUI's `UserAppTheme`, and
everything downstream reads the resolved answer from `Application.RequestedTheme` instead of
asking Windows. That distinction is load-bearing. `SystemThemeService.CurrentTheme` reports
the *OS app mode*, which is the wrong answer for a window pinned to Light on a dark desktop,
and three things outside the XAML tree would otherwise get it wrong:

| Surface | Why it can't use `AppThemeBinding` |
|---|---|
| Title bar | DWM, not XAML. Also has no "follow the system" state — it takes a resolved bool |
| WinUI content tree | `ElementTheme` on the root element, which is what a `Switch` or a drop-down paints from |
| Tray menu | A different UI framework on a different thread — `TrayHost.ApplyTheme` is told, not asked |

`App` owns all of that: it applies the preference before the first window exists, and
repaints those three whenever the preference changes or Windows raises a personalisation
event. Nothing else sets `UserAppTheme`, and the page that hosts the drop-down only saves the
value.

One consequence to keep in mind when adding anything: the accent has a light shade and a dark
shade, and which one is right follows the *app's* theme, not the OS's. `ApplyAccent` resolves
it from `RequestedTheme` for that reason.

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

## Extending this

Adding a setting: put it in the group it belongs to, reuse the row shape, take colours from
`Colors.xaml` and sizes from `Styles.xaml` rather than typing a value, use a real WinUI
control rather than templating one, give it a `SemanticProperties.Description`, disable it
with a reason if the hardware might not support it, and — if it changes hardware state — make
sure the restore path in `PowerHelperEngine.RestoreOnExit` puts it back.
