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
and Continue Game. Row/page evidence is in [options-menu.md](options-menu.md).
Unsupported host actions are disabled; no save browser is
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

### Preview and legend (CONFIRMED, binary 2026-10-29)

`0x00598880` tests camera mode 7 at `0x0059899C`; its dedicated render branch
(`0x005989BB..0x00598C92`) unprojects the live pointer through the fitted map,
uses that continuous map position as the eye X/Y, engine Z = -8.0, an identity
rotation and focal length 240 pixels. Its viewport endpoints are
`(W*466/640, H*33/480, W*594/640, H*225/480)`, each divided with integer
truncation: **128×192 at (466,33)** at 640×480. This is a live top-down dungeon
preview, not an OpenKeeper addition. It sets view distance 10.0. The branch
has no separate discovered-tile or pointer-inside-map test before camera setup;
normal terrain visibility still applies. Do not clamp the pointer to a tile centre.

`0x00403FB0` (`0x00404A17` onward) draws the legend rectangle with endpoints
`(W*466/640, H*266/480, W*572/640, H*417/480)`: **106×151 at (466,266)**.
Player rows begin at reference (474,274); the producer selects rows by player
`+0x10FC > 2` and draws names in player colours. The discovered-room hover
symbol and discovered creature squares belong to this full-map pass too.
These are separate from minimap overlays. The preview, legend and markers
remain unimplemented; completion plan Task E1 owns them.
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
valid entity kind2/3 obtains text through virtual `+0x50`. Its scrollbar is in the next
section. The neighbouring menu41 is the Reaper button, not a creature
portrait. Its spell-specific evidence belongs in keeper-spell-reaper.md.

A pressed secondary input in an ordinary nonzero tool mode invokes
`0x004094C0`. Query clears the selected id, submits session17 with id0, hides
menu40 and returns to tool0. Build/sell modes3/4 first clear an active drag
without leaving the tool; the next secondary press returns to tool0. Ordinary
spell and other listed tool arms reset immediately. `0x00407580`, used by the
Options callback, only resets mode1 outright and cancels drags in modes3/4.

## Menu40 scrollbar (read 2026-10-23)

**Creation — CONFIRMED.** A type-7 text widget whose record byte `+5` is not `0xFF` gets a
child widget in its init slot (`0x0052A0D0`): a type-2 scrollbar (vtable `0x0066ECBC`, 0xB0
bytes) copied from the parent's record with x = parent x + width (+ byte 5 when non-zero),
the parent's y and height, width and hit width 64, range [0, 1], no click callbacks, the
parent's sound slot, and draw callback = the parent record's `+0x3C`. Menu40's text widget
(record `0x00692008`: type 7, rect (20,20,1096,288), draw 0x004242F0, `+0x3C` =
[0x00414400](functions/00414400.md), byte 5 = 0, sound slot 0) therefore owns a bar at
menu-local **(1116, 20), 64 × 288**, right of the text, 20 inside the 1200-wide menu. It is
created disabled (`+0x61 = 0`). Sound slot 0 means its press/release sounds
(`0x0052CAD0`, table `0x006AC8C4`) are silent.

**When it shows — CONFIRMED.** [0x0052A330](functions/0052A330.md) measures the text in the
widget rectangle narrowed by 64 reference units (the bar's width), in screen pixels. If it is
taller than the widget, the bar gets min 0, max = text height − box height (pixels),
position 0, and is enabled; otherwise it stays disabled. Every call resets the position to
the top.

**Art and geometry — CONFIRMED** (0x00414400, only while enabled; catalogue `0x006A7DF0`
entries 0..3, loaded at interface `+0x12B6`, 27 bytes each). In reference units, bar
`(x, y, 64, h)`:

| part | art | rect | layer |
|---|---|---|---|
| up arrow | `gui\scroll bars\v-up_arrow` | (x, y, 64, 64) | 3 |
| down arrow | `gui\scroll bars\v-down_arrow` | (x, y + h − 64, 64, 64) | 3 |
| track | `gui\scroll bars\v-scroll_bar_bg` | (x, y + 64, 64, h − 128) stretched | 3 |
| thumb | `gui\scroll bars\v-scroll_bar` | (x + 20, top + 4, 28, len − 8) stretched | 2 |

The binary scales 64 and 8 with the x factor and 128, 28 and 4 with the y factor
(`0x0052C5A0` pairs); equal at 4:3. No pressed or hover variants are drawn.

**Thumb — CONFIRMED** (slots `+0x20` 0x00529160 and `+0x24` 0x0052A1F0). Track length
`T = h − 128` (160 for menu40), range `R = max − min` in pixels:
`len = T / (R + 1)`, at least 16 (or the record's fixed `+0xA8`, 0 here);
`top = y + 64 + (pos − min)·(T − len) / R` (`y + 64` when R = 0). With any real overflow the
thumb is 16 units, drawn 8 high.

**Input — CONFIRMED** (slot `+0x08` 0x00528B20, every GUI update while the button at input
`+0xC0` is held; states in `+0x41`):
- **arrows:** pressing the up/down square starts state 1/2, and each update while it stays
  held on the arrow moves the position by −1/+1 **pixel** (`0x00528FB0`, clamped to
  [min, max]): no delay, no repeat rate, frame-rate bound;
- **track above/below the thumb:** −1/+1 pixel **once** per press (state 4, then 5 until release);
- **thumb drag:** pressing the thumb stores the grab offset; while held on the track column the
  thumb follows the pointer, clamped to the track (0x00529050), and the position becomes
  `min + (pointerY − (y + 64))·(R + 1) / T` (0x005290F0); on release the thumb snaps back to
  the position's rect;
- **no mouse wheel:** the class reads only the pointer and the button, and DK2 has no wheel
  binding ([controls.md](controls.md)).

**Who scrolls — CONFIRMED** (0x004242F0). In **message mode** the text is set once by the
message click (0x00423F50) and re-rendered into the text cache (`+0x1409`, 0x0042CB60) at
y offset `−position` whenever the position changes. In **Query mode** the entity text is
regenerated and passed to 0x0052A330 on every draw, which puts the position back to 0, and
the cache is drawn with offset 0: the bar shows when the text overflows but never scrolls it.

**Ours (built 2026-10-23).** `TextScrollBar` draws the four textures and takes the arrow, track and
thumb input above; `ClassicMessageTabs.DrawPanel` scrolls the message panel by it, and the query panel
shows the bar inert, as DK2 does.
