# Minimap corner controls and related view navigation

Read 2026-10-18 from DKII.EXE callbacks, key dispatch and static menu records.
Original corner artwork, active variants and widget geometry remain canonical
in [minimap-rendering.md](minimap-rendering.md#widget-art-and-callback-bindings-partially-read).
The corners do not provide Home or compass-reset actions.

## Corner meanings

| Corner | Callback | CONFIRMED action |
|---|---|---|
| Upper left | `0x0041A2B0` | Select interface tool mode 10 and submit the common selection-cleanup request. |
| Upper right | `0x004163F0` | Select tool mode 4, clear drag state and conditionally clear queued selection. |
| Lower right | `0x00416480` / `0x00416580` | Zoom in/out; existing implementation and exact contract remain in minimap-rendering.md. |
| Lower left | `0x00428710` | Toggle the Options menu family through interface `+0x2722`. |

Mode 10 displays cursor icon `gui/cursors/I-Query`; mode 4 displays
`gui/cursors/I-Sell` (the icon path is `0x0040CAE0`, with ordinary mode arms
`0x0040BB60` and `0x0040B600`). Naming the controls Query and Sell is
**STRONGLY INFERRED** from those shipped asset bindings and their consumers;
their callbacks alone do not implement the eventual world-click action.
Neither changes camera heading or navigates to the Heart.

`0x00422600` is the shared selection-cleanup submission: action `0x72` with
its two callback arguments and viewing player. Keep it separate from a tool
selection's actual sale/query action. The queued-selection cleanup in the
Sell callback uses the same descriptor/channel match as the zoom callbacks.

Lower-left hit bounds are virtual `[20,1820,100,1900)`, derived from menu 20,
the bottom-anchored draw origin and the widget's local `[0,48,80,80]` hit box.
Under the height-scaled 1024×768 HUD these are `[8,728,40,760)`.
Upper-corner/magnifier hit bounds and widget ordering remain in the minimap
navigation section of minimap-rendering.md.

## Options toggle (CONFIRMED control flow; menu identity strongly inferred)

`0x00428710` requires player `+0x19A == 0` and first resets ordinary tool state.
Opening hides menu ids 22..35, shows menu 22, sets `+0x2722=1`, plays sound
`0x139`, saves menu 40's visibility at `+0x272E` and hides that menu if needed.
Closing hides ids 22..35, clears `+0x2722`, plays `0x13A`, optionally restores
menu 33 when `+0x2726==1`, then restores menu 40's saved visibility. Other
active panels at `+0x272A/+0x1C1F` are closed through their existing callbacks.
In non-mode-3 games an additional session request uses outer field `+0x284`;
its pause/network semantics are not established by this reading.

Static menu 22 at `0x00696BE8` uses widgets at `0x006945C8`. Their text ids
resolve in shipped Text.str to Options, Level Objective, Game Options, Load,
Save, End Game, Restart Level and Continue Game. This corrects the earlier
tentative "message panel" interpretation. Menu/pause actions, availability,
save/load frontend and per-game-mode restoration require further research.

## Heart navigation and full-screen map

**CONFIRMED:** `0x00416680` reads the viewing player's stored Heart tile,
requires world slot `+0x1FC` to accept it, then submits session command 4 with
that tile pair and zero height. It uses the same camera-target/glide consumer
as a minimap navigation click, not a heading reset. In `0x00405010`, binding
`0x27` invokes this callback. The shipped default H mapping is recorded in
[controls.md](controls.md#keeper-mode--everything-else).

**CONFIRMED subset:** binding `0x29` enters full-screen map by submitting
session type `0xF` with mode 7, setting world slot `+0x1F0` to 1, and refreshing
the complete map rectangle. While view slot `+0xCC` returns 7, only a press
matching that binding reaches its early exit branch: it submits mode 3,
clears slot `+0x1F0` and refreshes the map rectangle. Normal key handling is
bypassed by that early map branch. The shipped default M mapping is in
controls.md. Session mode-consumer details and full-map pointer input remain
unclassified; do not reuse ordinary minimap clicks without tracing that path.

The full-map branch of `0x00403FB0` bypasses the normal HUD, obtains map
dimensions and centres the map inside a 256×256 backing region. Complete
palette/fog/marker/layout and pointer contracts were not read here.
Normal-HUD toggle producers and remaining Q611 boundaries are maintained in
[hud-visibility.md](hud-visibility.md#normal-gui-toggle-producers).
