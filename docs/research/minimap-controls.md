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

Corner tooltips come from the widget records' text id `+0x30` (read 2026-10-19): Query 2149,
Sell 2151, magnifier 2145 ("Resize map"), Options 2147. The status line is the switch
[0x0042DB60](functions/0042DB60.md): the next string, except Query 2149 gives 2795. The popup
name over them follows the owner's report; its native producer is unread (`HudWidgetText`).

## Options toggle (CONFIRMED control flow; menu identity strongly inferred)

`0x00428710` requires player `+0x19A == 0` and first resets ordinary tool state.
Opening hides menu ids 22..35, shows menu 22, sets `+0x2722=1`, plays sound
`0x139`, saves menu 40's visibility at `+0x272E` and hides that menu if needed.
Closing hides ids 22..35, clears `+0x2722`, plays `0x13A`, optionally restores
menu 33 when `+0x2726==1`, then restores menu 40's saved visibility. Other
active panels at `+0x272A/+0x1C1F` are closed through their existing callbacks.
In non-mode-3 games an additional session request uses outer field `+0x284`;
its pause/network semantics are not established by this reading.

Static menu 22 at `0x00696BE8` uses widgets at `0x006945C8`.
`ClassicOptionsLayout` preserves its shipped text ids and reference positions;
`ClassicOptionsPanel` loads Text.str and BF4 through its asset abstraction.
The rows are Level Objective, Game Options, Load, Save, End Game, Restart Level
and Continue Game. Unsupported host actions are disabled; no save browser is
invented. Save/load callbacks prepare a `.sav` file list (`0x0042A760`).
`0x00428A30` draws enabled option text with grey 200 and hover white 255;
`0x00428C30` restricts the objective row to game modes 1/4. Restart opens menu35
and seeds its confirmation state (`0x00420570`), rather than restarting directly.
For game modes other than3, opening submits action92 with value1 and closing
with0. `0x005137E0` forwards this to `0x00527270`, which saves/restores the
session turn and toggles container `+0x284`. `0x0050A450` continues dispatching
packet actions while that flag suppresses level time, triggers, paydays,
entities, rooms, players, messages and battles. `0x00526590` suppresses turn
increment and render-time interpolation while paused. Campaign Options
therefore pauses simulation and blocks world-hand pointer dispatch; menu
input remains live. Mode3 changes only menu visibility in this callback.
The adapter binds Escape using the original shipped default key list
([controls](controls.md)); a key consumed by path skipping does not also open
Options. While Options owns input, ordinary map/GUI/Heart hotkeys are blocked.

## Heart navigation and full-screen map

**CONFIRMED:** `0x00416680` reads the viewing player's stored Heart tile,
requires world slot `+0x1FC` to accept it, then submits session command 4 with
that tile pair and zero height. It uses the same camera-target/glide consumer
as a minimap navigation click, not a heading reset. In `0x00405010`, binding
`0x27` invokes this callback. The shipped default H mapping is recorded in
[controls.md](controls.md#keeper-mode--everything-else).

**CONFIRMED:** binding `0x29` enters full-screen map by submitting
session type `0xF` with mode 7, setting world slot `+0x1F0` to 1, and refreshing
the complete map rectangle. While view slot `+0xCC` returns 7, only that binding
reaches the key handler's exit: mode 3, clear `+0x1F0`, refresh. Normal key
handling is bypassed. Default M is in controls.md. Minimap secondary callback
`0x0041A7D0` enters the same map mode.

Read 2026-10-18: `0x00403FB0` centres the terrain in a 256-square backing
region, skips class1 and uses the native palette for remaining classes.
The displayed map preserves aspect inside pixel bounds
`[W*41/640,H*33/480,W*425/640,H*417/480]` with integer endpoint division;
fit dimensions truncate and unused space is centred with integer division.
`0x00404E50` performs the aspect fit; `0x00404EB0` applies the inverse pointer
transform. `0x00404DE0` only copies a rectangle.

The primary full-map arm in `0x004075E0` transforms raw pointer coordinates,
truncates each tile coordinate toward zero, submits session type131 with two
separate tile integers, then exits to mode3. It applies no minimap-style bounds
check. Type131's view table entry is `0x00441F50`: it sets tile-centre X/Y with
the camera's existing height and calls the guarded, clamped immediate focus
setter. This is distinct from the minimap's command4 timed glide.
Secondary `0x00408EE0` exits to mode3 without navigating. `ClassicFullMapLayout`
mirrors that display/pointer geometry; `ClassicFullMap` draws cached class and
palette textures without consulting a running world.

The full map also has a discovered-room hover symbol, discovered creature
squares and a right-hand player/creature legend. These are not equivalent to
the minimap's dot/trap/door/reporting overlays. Their complete layout, strings
and refresh contract remain outside the implemented terrain/input subset.
The adapter therefore does not repurpose minimap markers for this view.
Normal-HUD toggle producers and remaining Q611 boundaries remain in
[hud-visibility.md](hud-visibility.md#normal-gui-toggle-producers).

## Query selection (confirmed callback chain, partial presentation)

In ordinary tool mode10, `0x004075E0` accepts a pressed creature hit (kind2)
only when camera `+0xE02` is zero. It stores the id at interface `+0x13D7`,
submits session type17 and invokes `0x00426EF0(1,2,id)`. That helper shows
menu40 and records its entity kind/id at `+0x4B17/+0x4B1B`; type17's view
consumer `0x004427D0` sets camera `+0xE02` and requests yaw quantised to the
current 64-unit remainder. Query is a creature camera/detail selection,
not possession or a sale. Creature slot `+0x50` resolves through vtable `0x0066D524` to `0x0048D340`.
Its ordinary owned-creature template is Text.str `0x969`, substituting personal
name, creature type, blood group, current wage and gold held. Hero `0x96C`,
neutral `0x96F` and Reaper `0xAE9` have distinct shorter templates; hostile
keeper creatures produce empty text. Personal name index `+0x4ED` and blood-group index `+0x4EF` now have
simulation fields and original allocation rules; see creature-identity.md.
`ClassicCreatureQuery` captures original text plus the current creature position
while the worker is joined, retains unchanged text, and exposes the native
64-unit yaw remainder through `FollowHeading`. The adapter loads its names and
blood groups from `CreatureNamePool`; no replacement identities are generated.

Menu40 at `0x00693B08` is a generic entity/objective text panel, virtual
`(1020,200,1200,440)`, with text widget local `(20,20,1096,288)`. `0x00417170` moves it above
the expanded bottom panel to virtual y980 (reference392); the text begins at
reference `(416,400)`. A separate `+0x10C2` branch uses a larger gap and the
collapsed panel has its own y. Static y200 is not the default live position.
`0x004242F0` suppresses it under interface `+0x1C1F/+0x272A/+0x2722/+0x2726`;
valid entity kind2/3 obtains text through virtual `+0x50`. The type7 scrollbar is
`0x00414400`; catalogue0..3 are up/down arrows, track and thumb, not a frame. The neighbouring menu41 is the Reaper button, not a creature
portrait. Its spell-specific evidence belongs in keeper-spell-reaper.md.

A pressed secondary input in an ordinary nonzero tool mode invokes
`0x004094C0`. Query clears the selected id, submits session17 with id0, hides
menu40 and returns to tool0. Build/sell modes3/4 first clear an active drag
without leaving the tool; the next secondary press returns to tool0. Ordinary
spell and other listed tool arms reset immediately. `0x00407580`, used by the
Options callback, only resets mode1 outright and cancels drags in modes3/4.
