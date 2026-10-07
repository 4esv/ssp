# Spike #292: can a new user find hide, restore, tabs and move panels

Measured 2026-10-05 on the published site `https://ssp.aesv.io/editor`, headless Chromium, fresh page per row, key help closed.
Script: `docs/spikes/292/run.js` (Playwright, `node run.js`). Screenshots: `docs/spikes/292/`.

## What this measures, and what it does not

- **Time** is page load to the control being ready (about 2.9 s, from `goto` to the first interactive render plus 1.5 s settle) plus the script's action time. A person is slower. Treat it as a floor.
- **Clicks** is pointer gestures from load. A drag counts as one.
- **Found without help** is my judgment from what is on screen without the help text open: is there a visible control or cue that names the action. A script cannot be a new user. The owner's column is the real test and is not filled.

## Table

| Action | 1280 px time | clicks | found without help | 390 px time | clicks | found without help | Owner on phone |
|---|---|---|---|---|---|---|---|
| Hide a panel | 3.0 s | 1 | yes: `–` beside each tab (tooltip `Hide Netlist`, no text) | 4.1 s | 2 | yes, by toggle: tap a tab to open, tap again to hide; nothing says so | owner to fill |
| Restore it | 3.0 s | 2 (hide, then `+ Netlist`) | yes: `+ Netlist` button appears in the bar | 3.5 s | 3 (open, hide, open) | yes, by toggle: same tab | owner to fill |
| Two panels as tabs of one group | 4.1 s | 1 drag | no: tabs show no drag cue; drop zones appear only during a drag; only a tooltip on the tab names it | not possible | 0 | no: one panel shows at a time, no groups | owner to fill |
| Move a panel to an edge | 4.1 s | 1 drag | no: same cue gap; the result is correct (Results moved from 910x135 at the bottom to 455x637 at the left) | not possible | 0 | no: drop zones and splitters are `display:none` under 48rem (`app.css:227`) | owner to fill |
| Find the key help | 3.0 s | 1 | yes: `?` button under the title | not possible | 0 | no: the dock bar, with `?` and `Reset layout`, is not rendered on a phone | owner to fill |

Notes:

- At 1280 px the drags work (the layout changes as expected) once a drag is started. The gap is in finding that a drag is possible.
- The 390 px hide and restore row works only because the tab bar buttons toggle (`aria-pressed`). On load all phone panels are hidden, so "hide" means "open, then close", and nothing tells the user the second tap closes.
- At 390 px every `Hide`, `Restore`, `Reset layout` and `Layout help` control is hidden (zero visible controls counted by the script).

## Proposals (one per "no"; the owner picks which to build)

1. **Tabs, 1280:** give each tab a visible grip or a `⠿` handle and a `grab` cursor, so a tab reads as draggable.
2. **Move to edge, 1280:** show the drop zones faintly (an outline) from the moment a drag starts, with edge labels, and add a `Dock ▾` menu on each tab with `Left`, `Right`, `Top`, `Bottom` entries for people who do not drag.
3. **Tabs, 390:** accept that a phone has no groups: drop this action from the phone scope, and say so in the docs.
4. **Move to edge, 390:** same as 3: drop it from the phone scope.
5. **Key help, 390:** the keyboard help has no use on a phone; show a one-line `Tap a tab again to hide` hint on the tab bar instead.
6. **Hide/restore signifier, 390 (not a "no", but unsignalled):** mark the open tab (a `×` on the active tab) so the second tap reads as "close".

## Not done

- The owner's phone pass (five actions, time each). This needs a person.
- 390 px viewport emulation is headless Chromium without touch events. Real touch is the owner's column.
