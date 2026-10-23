# Bottom-panel slot indicators: research, manufacture and spell recharge

Sources: DKII.EXE v1.70 through pyghidra-mcp, 2026-10-23 (claude). The slot draw
[0x00412FC0](functions/00412FC0.md) was read whole by decompile, and its sector arms by
disassembly (0x00413E4F..0x00414079). Function verdicts live in their records; this page states
the contract the presentation layer must meet. Parent page:
[bottom-panel-controls.md](bottom-panel-controls.md).

## 1. One draw, three progress sectors — CONFIRMED

Every Rooms, Spells and Workshop slot widget has one draw callback, 0x00412FC0. The static
widget records hold the update callback at `+0x34` and the draw at `+0x38` (static-record
offsets; the live draw reads `[EBX+0x24]` and the state `[EBX+0x61]`, so the live widget is LIKELY
the static record minus 0x10, and live offsets below are written that way): rooms
[0x00410990](functions/00410990.md), spells [0x004113B0](functions/004113B0.md), Workshop
[0x00410DE0](functions/00410DE0.md), each paired with 0x00412FC0 (static data at
0x0068FCB8 and the records after it). No other callback draws on a slot, so the indicators
below are all there is. The slot draws **no digits**: no percentage, no timer and no crate
count. The only digit family on the panel, `Gui\spells\rt-%d` (interface `+0x12CA`), is read
only by its loader [0x004152B0](functions/004152B0.md), its free 0x00419C00 and the Reaper
countdown 0x00424840 (a literal search for `0x12ca` finds no other reader).

All three progress indicators use [Hud_DrawTexturedRadialSector](functions/0041EF70.md),
the routine behind the Heart and payday rings ([hud-resources.md](hud-resources.md)).
Each draws a **pie wedge cut from a square texture**. The wedge covers the slot's square, its
top-left at the slot rectangle's top-left and its diameter the rectangle's width in screen
pixels. It runs from `3π − 2π·p/100` to `3π`, where p is a percent byte. With
x = sin θ, y = cos θ and screen y down, the wedge starts at twelve o'clock and grows clockwise,
as the Heart ring does. At p = 0 and p = 100 it has no area.

| Indicator | Slot condition | p | Wedge texture | Wedge colour | Wedge depth | Base icon tint |
|---|---|---|---|---|---|---|
| Spell recharge | spell slot, state 1, p ≠ 100 | recharge percent | the slot's icon | (1, 1, 1) | 0.7 | `0x666666`, layer 8 |
| Manufacture | the door or trap at the head of the order ring, state 1, not pressed | manufacture percent | the slot's icon | (1, 1, 1) | 0.6 | `0x780078`, layer 7 |
| Research, new item | the research target's slot, state 2 | research percent | the slot's icon (the "tba" art at availability 2, the real icon at 4) | (1, 0.3, 0.3) | 0.4 | unchanged |
| Research, known item | the research target's slot, state 1 | research percent | catalogue 261 `gui\icons\Gold_Frame` | (1, 1, 1) | 0.4 | unchanged |

Slot state is widget `+0x61`, written by the update callbacks: 0 hidden, 1 normal,
2 for availability 2 or 4. Only availability 2 replaces the icon with the "tba" art (catalogue
118 `gui\rooms\tba`, 119 `gui\traps\w-tba`, 120 `gui\spells\s-tba`); availability 4 keeps
the real icon. The research and manufacture wedges need a non-zero p. The recharge wedge needs
p ≠ 100 and is drawn at p = 0 too (the branch is `angle != 3π || p == 0`), with no area. Colour triples are the float RGB block passed by pointer.
They are read in address order as R, G, B (LIKELY; the order inside the triangle submit is
unread). Base tints are the packed 24-bit tint the sprite submit takes, written as in
[the slot record](functions/00412FC0.md) (`0x646464` is the existing unaffordable tint).

**Which is in front — INFERRED.** The sprite submit (`[renderer]+0x5C`) takes an integer layer:
the normal icon is 7, the recharge icon 8, selection 5, hover 4 and the slash overlay 3.
Overlays use lower numbers, so a lower layer is in front, as the Heart's layers suggest
(hud-resources.md). The sector routine takes a float depth instead. Reading depth d as layer
10·d puts each wedge one step or more in front of its base: 0.7 over 8, 0.6 over 7, 0.4 over 7.
That is the only arrangement in which the bright wedge is visible over a dimmed icon. The
depth-to-layer mapping itself is unread.

**Pressed slots — CONFIRMED.** Widget `+0x41`/`+0x45` non-zero (meaning unread, taken as
pressed) draws the base icon inset one pixel, `w − 2`. The recharge wedge and the state-2
research wedge use `w` when not pressed and `w − 2` when pressed (0x00413909..0x00413967,
0x00413EDD..0x00413F80). The state-1 research wedge does the reverse, `w − 2` when not pressed
(0x00413FCB..0x00414070). The manufacture wedge and its purple tint are skipped while pressed.

## 2. Spell recharge — CONFIRMED except the layering

Drawn in the spell arm of 0x00412FC0 (0x00413422 reads p, 0x0041388D..0x004139CB draws) for ordinary spells
only; library specials, the Reaper (spell 5) and the one-shot Reaper (14) are not on the grid.

- **p** is [PlayerSpells_RechargePercent](functions/004BE430.md): 100 when the spell's row has
  no last-cast turn (row `+5` = 0) or `turn − lastCast ≥ cooldown`. Otherwise it is
  `floor(floor(100·4096 / cooldown) · elapsed / 4096)`, with elapsed and cooldown in game turns.
  The cooldown is the definition's runtime `+0x3E`, KEEPERSPELLS file `0xCC`, loaded as
  `(raw × 4) >> 12` ([keeper-spell-thunderbolt.md](keeper-spell-thunderbolt.md)).
- **While p ≠ 100:** the wedge of the full-colour icon covers p% clockwise from twelve
  o'clock, and the whole icon is drawn tinted `0x666666` at layer 8. The branch then jumps to the
  shared tail (`goto 0x00413E4F`). The unaffordable `0x646464` tint and the FlashButton flash are
  **not** drawn on a recharging slot. Research, hover and selection overlays still are.
- Spells with a cooldown in the shipped data include Inferno and Turn to Chicken, 60 s each
  ([keeper-spell-inferno-chicken.md](keeper-spell-inferno-chicken.md)). Call to Arms has none
  ([call-to-arms.md](call-to-arms.md)).

**Simulation values (exist):** `AvailabilityState.LastCastTick` (0 = never, as DK2's row
`+5`) and `KeeperSpellDef.CooldownRaw`. The cooldown in ticks is the expression
`KeeperSpellRules.CanCast` already uses,
`(uint)(CooldownRaw × OriginalTicksPerGameSecond) >> 12`. Built (§8).

## 3. Manufacture — CONFIRMED, one simulation divergence (Q618)

Drawn in the Workshop arm (0x0041349D..0x00413723 marks the slot, 0x00413C4B draws).

- **Which slot:** [ManufactureQueue_PeekHead](functions/00506930.md) on the ring at player
  `+0x219` copies the head order. The slot is marked when the order's kind byte (`+2`: 0 door,
  1 trap) and type byte (`+3`) match the slot's door or trap. Only the head order's item shows a
  wedge. Queued orders behind it show nothing, and neither do crates already made.
- **p** is [Player_ManufactureProgressPercent](functions/004C31B0.md) →
  [ManufactureQueue_ProgressPercent](functions/00506B70.md): manufacture points (`+0x217`) ×
  100 / the cost word at **ring slot 0**, not at the head. The pay test
  [0x00506B50](functions/00506B50.md) compares with the same slot-0 word, so the two always
  agree with each other. The wedge reaches the cost the game pays at.
- **Draw:** with p ≠ 0 and the slot not pressed, the wedge of the full-colour icon (depth 0.6),
  then the icon tinted `0x780078` (dark magenta, 120/0/120). The no-Workshop `0x808080` tint is
  applied after it, so it would replace the magenta if both held.

**Simulation values:** `PlayerState.ManufacturePoints` and `PlayerState.ManufactureQueue`
(`ManufactureOrder.Kind`, `TypeId`, `Cost`; `ManufactureRules.DoorKind` 0, `TrapKind` 1). The
head is `ManufactureQueue[0]`. **Divergence (Q618):** DK2's ring starts head and tail at slot 0
([reset](functions/00506690.md)) and moves the head on each pop
([0x00506970](functions/00506970.md)) without shifting. After the first crate, DK2 pays for
later orders at the cost left in slot 0 until something rewrites it: an enqueue wrapping onto
slot 0, or a door or trap sell ([0x005069E0](functions/005069E0.md)) that empties the ring
(zeroed, head and tail reset to 0) or compacts later entries down over slot 0. Ours pays each
order at its own cost. A faithful indicator needs that slot-0 cost: either the simulation keeps
the ring, or the view shows our head cost and inherits the divergence.

## 4. Research — CONFIRMED except the colour order

Drawn in the shared tail of 0x00412FC0 (0x00413E4F..0x00414079) for any slot kind.

- **Which slot:** [Player_CurrentResearchEntry](functions/004C3190.md) returns the 12-byte entry
  of the 1-based target `+0x213` ([ResearchList_Entry](functions/005070A0.md)). The slot matches
  when the entry's kind dword (1 room, 2 door, 3 trap, 4 spell) and type dword equal the slot's.
  Only the current target shows a wedge. With no target (`+0x213` = 0) nothing is drawn.
- **p** is [Player_ResearchProgressPercent](functions/004C31D0.md) →
  [ResearchList_ProgressPercent](functions/00507060.md): 100 when points (`+0x20F`) ≥ cost
  (unsigned, so a zero cost is 100), else points × 100 / cost, truncated to a byte.
- **Draw:** for a state-2 slot (an item not yet available), the wedge of the slot's own icon in
  colour (1, 0.3, 0.3): at availability 2 that is the "tba" art, a red sweep over the grey
  question mark; at availability 4 the real icon. For a state-1
  slot (an item already available, which is a spell being researched for its upgrade,
  research-tree.md), the wedge of catalogue 261 `gui\icons\Gold_Frame`, untinted: a gold frame
  drawn around the icon clockwise. Both at depth 0.4. The base icon keeps its normal tint.

**Simulation values (exist):** `PlayerState.ResearchPoints`, `PlayerState.ResearchTarget` (1-based,
0 = none) and `GlobalVariables.Research[target − 1]` (`ResearchEntry.Kind`, `TypeId`, `Cost`;
`ResearchTreeRules.KindRoom/KindDoor/KindTrap/KindSpell` = 1/2/3/4). Built (§8).

## 5. Other per-slot overlays in the same draw — CONFIRMED

| Overlay | Condition | Art (catalogue index, name) | Layer | Built |
|---|---|---|---|---|
| Unknown icon | availability 2 (not 4) | 118 `gui\rooms\tba`, 119 `gui\traps\w-tba`, 120 `gui\spells\s-tba` | 7 | yes |
| Upgraded spell icon | spell row value 1 (Player_GetKeeperSpellRowValue) | the spell's icon entry + 1 | 7 | no |
| Unaffordable | state 1, not affordable, not a library special | tint `0x646464` | 7 | yes |
| No Workshop | door or trap, no Workshop, not pressed | tint `0x808080` | 7 | yes |
| Flash | FlashButton names the slot | tint `0x323232` + 4 GSparkle | 7 / 1 | yes ([tab-alerts.md](tab-alerts.md)) |
| Hover | state 1, widget `+0x49` ≠ 0 | 121 `gui\icons\hilight` with render flag 8, or 262 `gui\icons\hilight-2` without it for an upgraded spell | 4 | no |
| Selected | the slot is the current tool | widget art `+0x30` (+1 for a trap): 174 room, 175 spell, 176 door, 177 trap | 5 | yes |
| Slash | room availability 4 | 235 `gui\icons\slash` | 3 | no |

Catalogue names are from the 69-byte catalogue records at 0x006A7DF0 (index × 69, name at `+4`;
minimap-rendering.md). The runtime array at interface `+0x12B6` uses 27-byte entries, with the
texture at `+0xF`, so `+0x1B96` is entry 261's texture.

## 6. The Reaper's `_timerImages` — CONFIRMED, already built

`ClassicBottomPanel._timerImages` are the Reaper one-shot countdown digits (`Gui\spells\rt-0..9`)
drawn by 0x00424840 in menu41 ([keeper-spell-reaper.md](keeper-spell-reaper.md)). They are
complete for that use, and no slot indicator uses them. The x centring is the one
owner-comparison item that page leaves.

## 7. Contract for the Unity adapter

1. Per visible slot, the view supplies: slot state (1 or 2), `Unknown` (already present), and
   optional `RechargePercent`, `ManufacturePercent` and `ResearchPercent` bytes computed as in
   §2–§4, null when the slot is not the subject.
2. Recharge (p ≠ 100): draw the icon tinted `0x666666`. Over it draw the icon masked to the
   clockwise sector from 12 o'clock covering p%. Skip the unaffordable tint and the flash.
3. Manufacture (p ≠ 0, not pressed, head order's item): icon tinted `0x780078`, with the
   full-colour sector over it.
4. Research (p ≠ 0, target's item): over the normal icon, a sector of the slot's icon tinted
   (1, 0.3, 0.3) when unknown, or a sector of `GUI\Icons\Gold_Frame` when known.
5. The sector is a square mask: the wedge reaches the square's corners. It is not a circle
   inscribed in it.

## 8. Built — 2026-10-23

- **Presentation.** `BottomPanelProgress` (`src/DK2.Presentation/BottomPanelIndicators.cs`) holds
  the three percent functions in DK2's integer arithmetic. `BottomPanelView.Capture` sets
  `BottomPanelChoice.Indicators`, a `BottomPanelSlotIndicators` of three nullable percents and a
  state-2 flag (availability 2 or 4; the wedge is cut from the slot's own icon). They are three fields, not one kind, because a spell can recharge while its upgrade
  is the research target. State 1 is `Selectable`; state 2 is a visible slot at availability 2 or 4.
  The recharge percent is set at p = 0 too, since the grey tint and the skipped flash apply there.
  Tests: `BottomPanelIndicatorsTests`.
- **Unity.** `ClassicBottomPanel.SlotSector` is a `generateVisualContent` fan of at most five
  triangles: centre, 12 o'clock, each square corner the sweep passes, the end point. It reallocates
  only when its texture, tint or percent changes. Draw order is icon, recharge/manufacture sector,
  selection, research sector (the inferred depth reading of §1). Base tints follow the slot draw's
  tint variable: recharge `0x666666` replaces flash and unaffordable, and a recharging slot shows
  no sparkles. Manufacture `0x780078` overwrites flash and unaffordable, and no-Workshop
  overwrites it (0x00413D3C..0x00413D72).
- **Deviations.** Manufacture divides by our head order's cost, not ring slot 0 (Q618). The pressed
  inset (`w − 2`, and the manufacture skip while pressed) is not mirrored: widget `+0x41`/`+0x45`
  are unread and our slot draws no pressed state. A zero manufacture cost (a DK2 divide fault)
  gives 0, and a zero cooldown with a negative elapsed gives 100.

## 9. Unknown

- The float-depth to integer-layer mapping (§1); the in-front reading is inferred.
- The colour triple's channel order inside the triangle submit (LIKELY R, G, B).
- Widget `+0x41`/`+0x45` (taken as pressed).
- 0x004C1740, which gates state 1 for spells (0x004113B0 leaves the state unchanged when it
  returns 0).
- Owner check (play-visible): does a researched room's "?" fill red clockwise, and does an
  upgrading spell get a gold frame drawn around it? Does a recharging Inferno slot go grey and
  refill clockwise, with no sparkles?
- Owner check: does a queued crate show a magenta tint and fill on the head order only, and does
  the second crate's fill seem to stop at the first's cost (Q618)? Does selling a queued door or
  trap change the Workshop fill?
