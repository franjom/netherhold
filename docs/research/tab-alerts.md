# Bottom-panel tab and button flashing

Sources: DKII.EXE v1.70 through pyghidra-mcp, 2026-10-23 (claude); the shipped
`*Triggers.kld` corpus through `dk2tools kwd triggers <file> 214 9`. Function verdicts
live in their records; this page states the contract the presentation layer must meet.

## 1. There is one alert, and only a level script raises it — CONFIRMED

Every flashing bottom-panel tab, panel item and HUD icon reads the same **global
FlashButton descriptor** at world `+0x7291` (layout and lifetime:
[hud-resources.md](hud-resources.md#scripted-resource-flashing--confirmed)). Its only
setter, [0x00510260](functions/00510260.md), has exactly one caller, trigger action 9 in
[0x00518AD0](functions/00518AD0.md) (`0x005191C5`). The clear
[0x00509710](functions/00509710.md) is reached only through world vtable slot `+0x300`.

Research completion, manufacture and "now available" events **do not** flash anything:
no other code sets a target (action 9's own clear arm, `GameAction_LoadLevel`,
`World_Construct` and `0x005099A0` only reset it to category `0xFF`), and the tab and slot draw bodies
([0x00412260](functions/00412260.md), [0x00412FC0](functions/00412FC0.md), read whole)
have no other flash branch. A tab or button flashes only when a level script's
`FLASH_BUTTON` action names it. There is no per-tab alert field; the descriptor is the
whole state, so at most one target flashes at a time. It has no player gate.

### Target encoding

Byte 0 is the category, byte 1 the button (our `HudFlashTarget`).

| category | button | flashes | cleared by (besides duration / script) |
|---|---|---|---|
| 0 | `0xFB` / `0xFA` / `0xF9` / `0xF8` | the Rooms / Spells / Workshop / Creatures tab, even while its panel is open | `0xF8` only: a panel relayout with the Creatures panel (menu 4) visible ([0x00416FD0](functions/00416FD0.md)). FB/FA/F9 have no clearer. |
| 0 | `0xF2` / `0xF3` | mana / gold numbers (built) | none |
| 0 | any other | the icon button whose widget `+0x74` equals it ([0x004129E0](functions/004129E0.md)) | `0xFF` (Sell): the tool mode becoming 4 |
| 1 | room id | the room slot; the Rooms tab while the Rooms panel is closed | room tool (mode 3) with that room selected (`+0x13F4`) |
| 5 | spell id | the spell slot; the Spells tab while closed | spell tool (mode 8) with that spell (`+0x13E8`) |
| 3 | door id | the door slot; the Workshop tab while closed | door tool (mode 6), same id |
| 4 | trap id | the trap slot; the Workshop tab while closed | trap tool (mode 7), same id |
| 2 | creature id | the Creatures tab while closed; no slot flash found | none found |

The button ids are the widgets' own command ids: the tab click
[0x00416C20](functions/00416C20.md) receives the same `0xF8..0xFB`. The tab test
`0x00412442..0x004124B1` and the slot test `0x00413A2A..0x00413A43` are the mapping's
evidence; "panel closed" is `Gui_FindMenu(category)+0x48 == 0`.

Clearing on use is per frame: [0x00426F30](functions/00426F30.md) runs first in
[Hud_UpdateControls](functions/00407260.md) and drops the descriptor as soon as the
current tool equals the flashed item. The slot click handlers
([0x00410860](functions/00410860.md), [0x00411140](functions/00411140.md),
[0x00410BC0](functions/00410BC0.md), [0x004163F0](functions/004163F0.md)) also clear it at
once on a match. Opening a tab never clears an item flash; the tab simply stops
flashing while its panel is open and the item inside flashes instead (only when it is on
the visible page).

### What the shipped levels use

78 action-9 records in 16 levels: level1-4, 6a/6b, 10, 16, 17 and the seven My Pet
Dungeon maps. Categories used: 0 (`0xF3` gold 40 s in level1, `0xF2` mana 30 s in level2,
`0xF8` Creatures tab in level4), 1 (rooms; level1 flashes Lair 2 and Hatchery 4), 3 (door
1, level3), 4 (trap 3, level3) and 5 (spells). The room value is the one
[0x00410AE0](functions/00410AE0.md) returns and the room tool stores at `+0x13F4`. Reading it
as the room type id fits level1's tutorial (Lair, then Hatchery) but is LIKELY: that record
describes the iterator as resolving a display-order byte. No shipped level flashes the Rooms,
Spells or Workshop tab directly (FB/FA/F9): those arms are inert in the corpus, and the
tabs flash only as the route to a flashed item. Most durations are 0 (until used or
cleared); My Pet Dungeon uses 3-5 s.

## 2. How a flash is drawn — CONFIRMED (blend mode STRONGLY INFERRED)

Shared by the tab (0x00412260) and the slot (0x00412FC0):

- **Phase:** `(Clock_GetMilliseconds() / 500) & 1` from the real clock `0x005B5E90`.
  Odd phases tint the widget's normal art `0xFF323232` (dark); even phases draw it
  normally. That is a 1 s period at 50 % duty. It keeps running while Options pauses
  the game.
- **Sound:** when the phase becomes 0 (bright) and the shared latch interface
  `+0x4F0D` was 1, and the game is not paused (`+0x284 == 0`), sound id `0x33A` is played
  through the sound service `[0x0075B468]` slot `+0x24`. The bank argument is
  `[0x006AC8C4 + widget(+0x3C) * 0x30]` (the table the tab click's `0x138` sound uses). So:
  one chime per second, at the start of each bright half.
- **Sparkles:** four sprites with render flag 8 at layer 1, in front of everything else
  on the widget. Flag 8 is taken to be additive (STRONGLY INFERRED: the flag's blend is
  unread). Sprite `i` is catalogue `0xF3 + frame[i]`: 0xF3..0x102 are `GSparkle0`..`GSparkle15`
  (catalogue `0x006A7DF0`, 69-byte records; `GSparkle0` at `0x006ABF63`). The sixteen 32×32
  textures are one twinkle that grows and fades symmetrically: frame `n` and frame `15 − n`
  decode to the same brightness, and `GSparkle0` is faint but not blank. Each sprite is a
  square with side 64 reference units (88 when widget `+0x3C == 6`), scaled by
  `Gui_ReferencePointToScreen` 0x0052C5A0 from the 2560×1920 reference space, placed at
  `(left + bx % width − s, top + by % height − s)`, where `s` is the half side (32 or 44,
  scaled; the binary uses the scaled half's second component for both axes). The modulo
  is taken in screen pixels, so where the sparkles land depends on the resolution.
- **Sparkle clock:** [0x004033F0](functions/004033F0.md) (interface reset) starts each
  sparkle at `+0x4F11+i = Random_Next(0xE)` (frame 0..13), `+0x4F15+i = Random_Next(0xAC1)`,
  `+0x4F19+i = Random_Next(0x9E9)`, all byte-truncated. [0x00419DF0](functions/00419DF0.md)
  then advances the frames every HUD frame, not every game turn or millisecond: each of
  the four bytes is increased by 1. Past 15 it becomes 0 and that sparkle gets fresh
  byte-truncated positions: `+0x4F15+i = Random_Next(0xB7F)`,
  `+0x4F19+i = Random_Next(0xB23)`. Every draw comes from the interface's own seed `+0xF9`,
  not the simulation's stream. The staggered start means the four sparkles restart one
  at a time on a 16-frame cycle and jump to new random places in the widget. Their speed
  depends on the frame rate. The step runs only while interface `+0xF5` is non-zero, the
  same flag that encloses all ordinary HUD drawing (0x00419DF0), so sparkles never step
  unseen. The reset sets it to 0 when the global `0x00759AA0` is 1, else 1 (that global is
  unread).
- **Layering:** the tab's normal art draws at layer 6 with the tint; the selected overlay
  (layer 4) and hover (layer 2) still draw over a flashing tab. A slot's normal art is at
  layer 7.

The generic icon button (0x004129E0, category 0 with a widget `+0x74` match) only applies
the odd-phase `0x323232` tint: it has no sparkles and no sound.

## 3. Tab locking: Shift-click pins one panel beside the current one — CONFIRMED

Read whole 2026-10-23: the tab click [0x00416C20](functions/00416C20.md), the modifier mask
[0x005B2A50](functions/005B2A50.md), the relayout [0x00416FD0](functions/00416FD0.md), the
placement [0x00417310](functions/00417310.md) and the grid [0x00417470](functions/00417470.md).
It is not an alert and does not flash.

**State.** Interface bytes `+0x5F..+0x63` are one array indexed by category (1 Rooms, 2 Spells,
3 Workshop, 4 Creatures; tab ids FB/FA/F9/F8): `[0]` (`+0x5F`) is the pin count, `[c]` the
pin rank of category `c` (0 = not pinned). `+0x65 + 4c` (`+0x69..+0x75`) remembers which
category menus are open, so a collapse/expand restores them. A non-zero `[c]` makes
0x00412260 draw catalogue 8, `gui\icons\tab-lock`, over the tab at layer 3.

**The key is Shift alone.** 0x005B2A50 returns bit 1 for scan code 0x2A or 0x36 (left/right
Shift), 2 for 0x1D/0x9D (Ctrl), 4 for 0x38/0xB8 (Alt); the click tests `== 1`, so
Shift+Ctrl or Shift+Alt is an ordinary click.

**A click on tab `c`**, in order:
1. if the click has a widget id, the common selection-cleanup request `0x00422600(0, id)`;
2. if the panel is collapsed (`+0x109 == 0`): expand it through 0x004167D0(0,0), which
   restores the remembered menus, and play `0x138` from bank `[0x006AC8C4 + 0x30n]`
   (n = 1/2/3/4 for FB/FA/F9/F8). An expanded panel plays nothing here;
3. **ordinary click:** close (menu `+0x48 = 0`, remembered flag 0) every category 1..4 that is
   not pinned, then open `c`. A pinned panel therefore stays open next to whatever is
   clicked; clicking the pinned tab itself leaves it alone on the strip;
4. **Shift-click on an unpinned `c` with no pin:** count = 1, `[c]` = 1, open `c` (the rank
   bump of other pinned categories finds none). Other open panels are **not** closed, so the
   panel that was open stays beside it until the next ordinary click;
5. **Shift-click on an unpinned `c` while another tab is pinned:** nothing but the relayout.
   The count is only ever set to 1 (step 4) or decremented, so **at most one tab is pinned**;
   the rank arithmetic for several pins is dead code;
6. **Shift-click on the pinned `c` (unpin):** count − 1; ranks above `c`'s drop by one; if more
   than one category menu is open, `c`'s panel is closed, else it stays open, unpinned; `[c]` = 0;
7. every path ends in the relayout 0x00416FD0.

**Nothing else writes the pins.** StoreAt/InsGrep over `+0x5F..+0x63` find only 0x00416C20
(the other hits are sub-objects at `+0xC`/`+0x4D05`, constructor 0x00402AF0). The level
reset [0x004033F0](functions/004033F0.md) does not touch them and the interface is the
static 0x006CEA10, so a pin LIKELY survives a level change (the menus' own visibility is
rebuilt; whether the lock icon then shows over a closed panel is unobserved).

**Layout of several open panels** (0x00417310, relayout runs only while expanded). With `n`
category menus open, each gets width `W = 1800 / n` (integer), y 1600, height 300, hit
rect `(0, 0, W, 432)`. An unpinned panel sits at x 472; the pinned panel (rank 1) at
`472 + W` when an unpinned one is open, else 472. Creatures (4) places menu 5 (the
overview) instead of menu 4. In practice `n ≤ 2`: the current panel left
(472, 900 wide), the pinned one right (1372, 900 wide).

**Grid per panel** (0x00417470, expanded branch `+0x109 == 1`; the 0x198/0xC4 branch for
other values is unreachable because the toggle only cycles 0/1). Usable width is `W − 64`
(the page arrows), height 300. With `count` entries (0x00416980):
- 264-unit cells if `count ≤ 300·(W−64) / 73984 − 1` (integer division), else 128-unit cells;
- columns `(W−64) / cell`, rows `300 / cell`, page size columns × rows (`+0x7D + 4c`);
- cell `(col, row)` at local `x = 76 + (cell+8)·col`, `y = 12 + (cell+8)·row`;
- each slot's `+0x3C` becomes 6 for 264-unit cells, 5 for 128-unit (the sparkle size and
  the chime's bank index, section 2).

So one panel (W 1800) keeps 264-unit cells up to 6 entries, 6 per page; 7 or more switch
to 128-unit cells, 13 × 2 = 26 per page. Two panels (W 900): 264-unit cells only up to 2
entries (3 per page), else 128-unit cells, 6 × 2 = 12 per page. Creatures' overview grid
uses 116-unit pitch, `(W − 432) / 116` columns, one row.

**Page arrows.** The decompile of 0x00417470 keeps each arrow widget's own x (+0x14) and width
(+0x1C) and sets y 12 and 132 + 12 = 144, height 132, for menus 1..3 only; they are menu-local, so
each open panel carries its pair at its own left edge. Menu 5 (the overview) gets no arrows here.

**Built (2026-10-23).** `BottomPanelTabs` (Presentation) is the click and pin state machine above,
with the remembered open set kept across a collapse; `BottomPanelOpenSet` is the set of visible
menus. `BottomPanelLayout.PanelOf` is 0x00417310, `Grid` and `BottomPanelGrid` (`Slot`,
`PageSize`, `WidgetBank`) are 0x00417470 including the single-panel 128-unit switch at 7+ entries,
`PreviousPage`/`NextPage` the arrows and `OverviewColumns` menu 5's column count.
`ClassicBottomPanel` reads Shift without Ctrl/Alt from the release event, draws up to two panels
(26 slots each), each with its own arrows and page, the selected overlay on every open tab and
`gui\icons\tab-lock` over the pinned one (between the overlay and the hover);
`ClassicCreatureOverview` moves with its panel and narrows to `(W − 432) / 116` columns. Ours, not
read: each category keeps its own page, a panel's page resets when it opens, and the wheel pages
the panel under the pointer. A lone overview keeps the wider screen's extra width (as before);
the item grids keep the 1800-unit reference span. Not wired: sound `0x138` on expanding. Tests:
`BottomPanelTabsTests`.

## 4. Ours

- Simulation: `TriggerExecutor` emits action 9 as `ScriptedViewKind.FlashButton`;
  `ScriptedViewDirector` keeps the descriptor on the real clock with native expiry and
  exposes `ActiveFlashTarget` and `ClearFlash`. The mana/gold flash and the bottom-panel
  tab and slot flash consume it (section 5).

## 5. Built (2026-10-23)

`BottomPanelFlash` (Presentation) resolves the active FLASH_BUTTON target against the open
panel to a tab or slot and the real-clock dark half, and says when using the item clears it
(`0x00426F30`); `DungeonRunner.DrawHudControls` clears through `ScriptedViewDirector.ClearFlash`.
`ClassicBottomPanel` tints the flashing tab or slot `0x32` on the dark half and draws four
`GSparkle0..15` at the read positions. `BottomPanelSparkles` seeds them as 0x004033F0 does and
advances them once per rendered frame, with its own `Dk2Lcg`, never the simulation's stream.
The interface seed's value at reset is unread, so it is a constructor parameter. UI Toolkit
has no additive images, so each sparkle keeps its colour with its brightness as alpha.
Deviation: the modulo is taken in virtual units, not screen pixels. The sparkle is 88 units on
264-unit slots (`+0x3C == 6`) and 64 on tabs and 128-unit slots (`BottomPanelSparkles.SizeFor`,
section 3). `BottomPanelFlash` takes the set of open panels, so with a pinned tab both panels
count as open. Not built: the chime `0x33A` (no sound system yet) and generic icon buttons by
`+0x74` id other than the resource numbers. Tests: `BottomPanelFlashTests`.

## Open

- Which SFX sound `0x33A` is in its bank. Widget `+0x3C` is the record's sound slot `+0x4C`
  (0x00528300): tabs carry 1..4 (FB 1, FA 2, F9 3, F8 4; 64-unit sparkle), and 0x00417470
  sets category slots to 6 for 264-unit cells (88-unit sparkle) and 5 for 128-unit (2026-10-23,
  section 3). The sparkle sizes are built; the sound bank meaning of `+0x3C` stays open.
- Which icon buttons carry which `+0x74` ids beyond Sell `0xFF`. They come from static
  widget tables that have not been read.
- Category 2 (creature): no slot flash was found in 0x00412FC0, whose creature arms 5/6
  set no kind. The creature-panel draw paths in creature-overview.md were not checked.
- The DK2 frame rate the sparkle cycle was tuned for. It is frame-bound, so only play
  shows its speed.
- **Seen by the owner (2026-10-23, seven phone shots of Level 1's Lair slot):** the slot
  blinks between its normal and dark art, four-point stars twinkle at independent places
  (some over the icon, some past its left edge over the page arrows, as `X % width − half`
  allows), and it keeps going until the Lair is clicked. This confirms the dark phase, the
  staggered sparkles and the clear on use. The owner first read it as "a room becomes
  available"; Level 1's script flashes the Lair and then the Hatchery, and the binary has no
  other producer, so no non-scripted flash was observed. Still open from play: the chime
  `0x33A` once a second.
