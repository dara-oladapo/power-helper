<!--
Keep this short. Delete any section that doesn't apply rather than writing "N/A" in it.
The diff says what changed; this should say why, and what you actually checked.
-->

## What and why

<!-- What this changes, and the problem it solves. Link the issue if there is one: Fixes #123 -->

## How it was verified

<!--
Be specific about what you ran, not what you intended to run. "It builds" and "I watched it
work on a real laptop" are different claims — say which one is true. Almost nothing in this
app can be proven by a build: it drives PnP devices, ACPI battery data and display modes,
none of which CI has.
-->

- [ ] `dotnet build -c Release`
- [ ] Ran the app elevated and exercised the change by hand
- [ ] Checked it in both light and dark mode (any UI change)
- [ ] Checked both surfaces stay in step — the tray menu and the settings window are two
      views of one settings object

Machine tested on: <!-- e.g. Lenovo Legion 5 Pro, RTX 3070 Ti + Radeon 680M / not run — CI only -->

## Does this leave anything changed after the app exits?

<!--
Delete this section unless the change touches the GPU, the power plan, the refresh rate, or
brightness.

Power Helper's bargain is that every hardware change it makes is temporary and gets put back
— on AC, and on exit. A change that outlives the process is the most damaging class of bug
here, because it strands the user in a state they didn't choose and can't see the cause of.
If this PR touches any of that, say what you did to convince yourself the restore path still
holds, including the messy exits: closing the window, sleep, and a forced kill.
-->

## UI changes

<!--
Delete this section if there are none. Otherwise:
- Does it follow DESIGN.md — WinUI tokens rather than literals, no hardcoded accent, no
  hardcoded light/dark colour?
- Does it still look right in the other theme, and does it follow a theme switch made while
  the window is open?
- Is a control that can't work on this hardware disabled *and* saying why?
- Does anything slow run off the UI thread? pnputil, powercfg, schtasks and WMI are all slow
  enough to freeze a window.
- Screenshots of both themes are worth more than a description.
-->

## Notes for the reviewer

<!-- Anything you're unsure about, deliberately left out, or want a second opinion on. -->
