# Mechanic: the Battle tab — crossed blades, battle records and the zoom to a fight

Source(s):
- `DKII.EXE` v1.70 (GOG) through pyghidra-mcp, 2026-10-27 (claude). Facts in our own
  words; no decompiler output enters the repository (ADR-0046). Function verdicts live in
  their records under `functions/`.
- The shipped `Text.str`, `LEVEL1.str` and `Speech.str` (decoded as `Dk2StringTable`
  does), the GUI catalogue at `0x006A7DF0` (69-byte entries; entry *n*'s name at
  `0x006A7DF4 + 69n`), and `kwd triggers Level1Triggers.kld`.
- OpenKeeper PR #743 (open, 2026-10-27) as a lead only; section 10 says where it agrees.

Original version: DK2 v1.70 (GOG)

The battle records themselves were first read for the census
([binary-census.md §3](binary-census.md#3-battles-are-presentation)); this page is the home of
the whole mechanism, from the records to the HUD. Nothing here is built.

## 1. Where the tab lives — CONFIRMED

The tab is widget 1 of **menu 43** (`0x2B`; page record `0x00693BE0`, widget array
`0x00692AF8`), a 480 x 140 menu at (2080, 1472) in the 2560 x 1920 reference screen, right of
the message tabs (menu 39). [0x00417170](functions/00417170.md) moves it with the panel when
the panel collapses. Its three static widget records (0x50 bytes, layout in
[front-end-menus.md §3.1](front-end-menus.md#31-how-menus-are-defined-hard-coded-tables-in-the-exe)):

| id | local x | size | art (catalogue) | right click | left click | frame callback | text |
|---|---|---|---|---|---|---|---|
| 1 | 0 | 128 x 128 | 225..228 `gui\icons\a-fight-00..03` | `0x00426960` | none | `0x004269D0` | 2165 "Zoom to Battle" |
| 2 | 136 | 128 x 128 | 223/224 `gui\spells\call_to_arms-kill(-2)` | `0x00426830` | `0x004268A0` | `0x00426920` | none |
| 3 | 264 | 128 x 128 | 219..222 `gui\icons\a-alarm-00..03` | `0x00426A00` | none | `0x00426B90` | none |

All three draw through [Hud_DrawIconButton](functions/004129E0.md). The loader
[0x00528300](functions/00528300.md) copies the record's dword at `+1` (the id) to the live
widget's `+0x74`, the right-click callback to `+0x51` and the frame callback to `+0x24`, and
starts the state `+0x61` at 1. Widgets 2 and 3 (Call to Arms disband, LIKELY from the art; and
an alarm icon) are not read here. The status line for a hovered widget is LIKELY text
2166 "Battle Tab: right-click to zoom, click again to cycle.", by the tabs' id + 1 rule
(bottom-panel-controls.md). The positions are static, so a hidden neighbour leaves its gap
(STRONGLY INFERRED: no relayout of menu 43 was found).

## 2. Battle records — CONFIRMED

The world holds **eight** records of `0x26` bytes at `+0x9C50`; world slot `+0x2B4`
([0x00509540](functions/00509540.md)) returns record *i*.

| offset | field |
|---|---|
| `+0x04` | u16 first member; members link through creature `+0x240` (next) and `+0x23E` (previous) |
| `+0x06` / `+0x0A` / `+0x0E` | mean member position, x / y / z |
| `+0x12` / `+0x16` / `+0x1A` | dwords zeroed with the record; no other reader found |
| `+0x1E` | member count; non-zero means live |
| `+0x22` | age in turns, +1 per turn while the record has members |

A creature carries `+0x37A` (in a battle) and `+0x37E` (turn before which it may not leave).

**Who fights.** Once per turn, last in [World_UpdateTurn](functions/0050A450.md),
[Battles_Update](functions/00504F00.md) updates all eight records, then offers every creature
of **every player** (heroes and neutral included) that is fighting and not yet in a battle.
Fighting means: resolved category 22 (states 199/200 engaging, 201..203 fleeing, 251/252
responding to a threat, 259/260 celebrating; a walk counts by the state it resumes), neither
state field `+0x25E` nor `+0x32D` equal to 202, and not a Combat Pit fighter (`+0x3E6` bit
`0x8000`).

**Joining** ([Battle_JoinNearest](functions/00504FB0.md)). Only a creature whose target
`+0x502` exists and is a creature joins, so a fight with a door or the Heart makes no battle.
It joins the live record whose centre is nearest in x/y, if within **5 tiles** (squared
distance in tiles at most 25, inclusive); else the first empty record; with all eight live, the
nearest at any distance. Records are therefore grouped by place, never merged, and one record
can hold both sides and any number of players. Joining sets `+0x37A`, re-averages the centre
and sets `+0x37E` to now + 4 x TicksPerSecond.

**Leaving.** [Battle_Update](functions/00505310.md) releases a member that is no longer
fighting once `+0x37E` has passed ([Battle_RemoveMember](functions/00505500.md)); a member
still fighting stays however long. So a creature stays at least 4 s (4 × TicksPerSecond turns)
after joining and leaves the first turn after that on which it is not fighting. Death
bookkeeping ([0x00487C70](functions/00487C70.md)) and the collapse's ordinary branch
([0x0048B770](functions/0048B770.md); types 0x15/0x1C/0x1D and `+0x3EA` bit 11 take a vtable
path instead, presumably reaching the death bookkeeping, unread) remove it at once through [Battles_RemoveCreature](functions/005051F0.md). A record whose count
reaches zero is cleared whole, age included; there is no other expiry.

## 3. When the tab shows — CONFIRMED

The frame callback [Hud_BattleTabUpdate](functions/004269D0.md) runs every frame and sets the
widget state to 1 (shown) when [World_NextLocalBattle](functions/00505250.md) from index -1
finds any live record holding a creature of the **local player** (world slot `+0x264`), else
0 (hidden). There is no hysteresis beyond the 4-second membership: the tab goes out the frame
after the last local member leaves its record. A battle between two other players never shows
it. In multiplayer each machine presumably shows its own player's battles, the records being
shared world state and the local player word `+0x7243` per machine (no multiplayer path read).

## 4. Clicks and the F key — CONFIRMED

- **Right click** ([Hud_ZoomToNextBattle](functions/00426960.md)): asks
  World_NextLocalBattle for the next local battle after the index kept at interface `+0x130`,
  stores the answer there, and if there is one glides the camera to its centre's tile through
  [Hud_SubmitWorldPosition](functions/00415B30.md) (game action 4, yaw 0). Each further right
  click moves on to the next local battle, wrapping through the eight records. The HUD set-up
  0x00419180 sets `+0x130` to -1, and a click finding no local battle stores -1 back, so the
  first click goes to the lowest-numbered local battle.
- **Left click:** the record has none (`+6` is 0); the widget manager tests `+0x4D` on the
  left-click path in 0x00528610 and skips it. Nothing happens.
- **Key:** binding `0x25` in [Input_HandleGameKey](functions/00405010.md) (`0x00405EEF`) calls
  the same function. [InputBindings_SeedDefaults](functions/005604C0.md) gives it DIK `0x21`,
  **F**, no modifier: the manual's "Zoom To Next Fight", Text.str 126.
- No sound, speech or packet besides the camera action.

## 5. The fly-in notice — trigger, latch and sounds CONFIRMED; its look UNKNOWN

When a local player's creature joins a record that held none of that player's creatures,
Battle_JoinNearest calls interface slot `+0x4C` ([Hud_StartBattleFlyIn](functions/0042C0D0.md),
interface `[[world+4]+0xC]`, vtable `0x0066C4A4`). It acts only when graphics detail
`0x00759AEC` is at least 3 and no fly-in is already running (`+0x4D75` zero). It plays sound
`0x32B`, sets `+0x4D75`, and starts the overlay object at `+0x4D05` from reference point
(1840, 240) toward the centre of menu 43's widget 1 ([0x00426D80](functions/00426D80.md)), with
float `0.15` at `+0x4D1D`, a real-clock stamp now + 2000 ms at `+0x4D58` and the value 2441 at
`+0x4D54`. The interface frame update ([0x0042C390](functions/0042C390.md)) steps it while the
camera is not letterboxed; when interface `+0xF5` is set and the object reports done (`+0x4D50`)
it plays sound `0x32C` and clears `+0x4D75` (with `+0xF5` zero the latch stays and no later
fly-in plays). That update skips camera modes 2, 7 and `0x12` and a paused game.
What the object draws, and what 0.15, 2000 ms and 2441 mean, are unread (`0x0042F040`).
This is the "box" of the census; the owner's account of it is in section 8.

## 6. Drawing — CONFIRMED

Hud_DrawIconButton's menu-43 arm (`0x00412AF3..0x00412BA7`) runs for a widget in state 1:

- phase = real-clock milliseconds / **150**, kept in `0x006D3948`;
- widget 1 (the blades) and widget 3 cycle four frames, art + (phase & 3): a-fight-00..03,
  each **150 ms**, a 600 ms loop; widget 2 alternates two frames every 300 ms;
- widget 1 only: on every draw while the phase is odd, sound id `0x30F` is requested with
  bank argument `[0x006ACB94]` and handle 0 through `[0x0075B468]` slot `+0x24`.
  Whether the service restarts or ignores a sound already playing is unread.

The art is the whole flash: there is no tint, sparkle or FlashButton involvement
([tab-alerts.md](tab-alerts.md) is a separate, script-only alert).

**Minimap.** [Minimap_DrawThings](functions/0041CC50.md) (`0x0041D860..0x0041D9B9`) draws, for
each live record holding a creature of the viewing player ([0x005055D0](functions/005055D0.md))
and younger than **128 turns**, an outline circle of colour bytes (0, 255, 0) (green) at the
record's centre, radius = age & 31 raster units, so it expands and restarts every 32 turns
(8 s and 32 s at the shipped 4 turns a second).

## 7. Speech — CONFIRMED for the negative scope stated

The battle code plays no speech. Level 1's line `LEVEL1.str` 21 ("...move your gaze to see
them now by right-clicking on the flashing blades...") is script: trigger 78 (flag 80 = 1)
waits on condition 72, then speaks SpeechId 22 and sets flag 67. Flag 80 is set by trigger 80
(player event 56, dungeon breached), triggers 81 and 201 (creature event 6, attacked) and
triggers 305/306 (condition 36). `Speech.str` 20..22 ("Another fight has broken out!", "Fights
are occurring in your Dungeon!", "There is mass fighting throughout the Dungeon!") have no
encoded reference (`-(10000 + id)`) anywhere in the file and no direct enqueue site among the
ten `call 0x00568390` sites (Q663). `message-table.md` row 6 (CombatStarted, speech none)
agrees.

## 8. Owner's observations

2026-10-14 (binary-census.md): fights make the music intensify, and a box appears that moves
the camera to the fight when right-clicked. The right-click matches section 4; whether the
"box" is the tab or the fly-in is not settled (Q664).

## 9. Link to the music work

[Battle_Update](functions/00505310.md) is where battle threat is summed and handed on. Per
record and per turn, it sums the threat of members that are still fighting and **not** owned
by the local player (definition `+0x698`, scaled above level 1 by the level table
`[0x006E4154] + 0x105`), in every record, including records with no local creature. It then
calls effect service `[0x0075B468]` slot `+0x4C` (`0x00567BC0`) with `0x103`, `0x102` or `0x101`
when the sum reaches the globals `0x006F29BD`, `0x006F29B9` or `0x006F29B5`
(MusicLevelFour/Three/TwoThreatThreshold, variables 217..215). What the sound manager does
with it is [music.md §4](music.md#4-the-battle-threat-battle_update-0x00505310).

## 10. OpenKeeper PR #743

It cites the same addresses and agrees on: eight world records, the 5-tile join, the
all-full case, 4-second minimum stay, removal on death, visibility through 0x004269D0, the
right-click cycle, key `0x25`, no left click, texts 2165/2166, and the fly-in with sounds 811
and 812 (`0x32B`/`0x32C`; their names GUI_BUTTON_TABSLAB / GUI_BUTTON_SPINNING are theirs, not
checked). It differs: (a) it calls 0x0048B770 "most likely the pick up"; it is the collapse
(Creature_Collapse); (b) its "fighting" is one FIGHT state with a creature target, where DK2
uses category 22 minus state 202 and Pit fighters; (c) it flashes by pulsing two images and
plays no `0x30F`; DK2 cycles four frames at 150 ms; (d) it omits the graphics-detail gate,
the single-flight latch and the minimap ring.

## 11. The contract in pseudocode

```
each turn (after entities and players):
  for r in records: release members (not fighting and now >= leaveTurn); recentre;
                    feed music (section 9); if r.count > 0: r.age += 1
  for every creature of every player:
    if fighting(c) and not c.inBattle and c.target is a creature:
      r = nearest live record within 5 tiles ?? first empty ?? nearest live
      if c.owner == local and r has no creature of local: startFlyIn()
      add c to r; recentre; c.leaveTurn = now + 4 s

each frame (HUD):
  bladesVisible = any live record has a local creature
  if bladesVisible: frame = a-fight-((ms / 150) & 3); request sound 0x30F on odd phases
on right click or F: idx = next local record after idx (wrapping); if any: glide camera to it
```

## UNKNOWN

- The fly-in object's art and motion: `0x0042F040`, `+0x4D1D` 0.15, `+0x4D54` 2441 (Q664).
- Which samples sounds `0x30F`, `0x32B` and `0x32C` name in their banks.
- Widgets 2 and 3 of menu 43: their frame callbacks `0x00426920`/`0x00426B90` and clicks.
- Creature `+0x32D` (the second state field tested for 202) and record `+0x00`, `+0x12..+0x1A`.
- Whether `Speech.str` 20..22 are ever spoken (Q663).
