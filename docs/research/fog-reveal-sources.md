# Fog reveal sources: every writer of the two discovery bits

Source(s):
- Retail v1.70 `DKII.EXE`, read in Ghidra 12.1.4, 2026-09-25 (Q429, Q430)
- The owner's TrapTime save series (`fow_1..3.SAV`, 2026-09-06), re-read
- The shipped `*Things.kld` action points, all 66 levels
- OpenKeeper `fb3c350a` (`game/fogofwar/`), as leads only: it cites an
  unpublished design document and gives no addresses

Original version: DK2 v1.70 (GOG)

**The headline: DK2 keeps two discovery bits per tile, not one.** `fog-of-war.md`
§11 found one, from the save. There is a second one in the same save record, and
the save reader never saw it because its grid starts 8 bytes late (§2). The bit
§11 found is the smaller layer. Most of what OpenKeeper describes as "reveal"
writes the other bit.

Everything below comes from reading the binary and is **STRONGLY INFERRED**,
unless it says **CONFIRMED**. Those facts were measured from the owner's saves
or the shipped data.

## 1. The two bits

A tile is 16 bytes. The map object has the tile array at `+0x14`, the width at
`+0xC` and the height at `+0x10`. The world also holds the array, at
`world+0x9EE` (width `+0x9E6`), and the global base is at `0x006DA49C`. Terrain
id is `+0xA`, and the owner is the low 12 bits of the word at `+8`.

| Bit | Our name | What sets it | What it shows |
|---|---|---|---|
| `+0xB` bit `0x40` | **scanned** | one writer only: a creature's `PerceptionRange` circle, with no occlusion (§3.1) | the 3D view draws a scanned, unexplored tile as terrain 38 **Claim Scan**. The minimap shows it only for `RevealThroughFogOfWar` terrain |
| `+8` word bit `0x2000` (`+9` bit `0x20`) | **explored** | every source in §3.1–§3.9 | the minimap's known/unknown test. `0x00451C90` returns the unknown colour unless this bit is set, or the scanned bit is set on terrain with flag `0x01000000` (`RevealThroughFogOfWar`) |

The explored bit is **Q309's bit `0x2000`**, the one action 17 writes. Q309 said
its only reader was the overhead-map painter. That painter is the minimap's
known/unknown test, and 49 byte tests of the bit
(`TEST byte ptr [..+9],0x20`) sit in 30 functions, most of them in the
renderer at `0x0043C310`–`0x00448xxx`. So bit `0x2000` is the explored layer and is not
cosmetic. `trigger-dispatch-tables.md`'s "touches nothing simulated" was true
of the simulation proper, but the bit is saved and restored (§2).

`0x00452DC0`, the tile look-up used for lighting, confirms the split. A tile
with no room, explored clear, scanned set, and no `RevealThroughFogOfWar`
reports terrain `0x26` (38, *Claim Scan*) instead of its own.

A third bit, `+8` word bit `0x4000` (`+9` bit `0x40`), is cleared by
`0x004DAE40` and `0x004EF1F0` and set by `0x004EF1F0`. It is the movement
gate of `structure-blocks.md` §5.4, not fog.

CONFIRMED (whole reads, 2026-10-14): 0x0043E320 substitutes terrain 27 when
unscanned, or 38 when scanned and lacking flag 0x01000000. Scanned flagged
terrain retains its real definition. Room pieces are still suppressed unless
explored, Sight-visible or possession-visible. Helper 0x0044DB70 is the
possession camera bypass: true only for camera modes 2, 10 and 11.

## 2. The save carries both bits, and our reader is 8 bytes late — CONFIRMED

The map's save routine `0x0044F3F0` writes an 8-byte header (`100`, then
`height + width<<16`). It then calls `0x00452B90` once per tile, with ECX set to
tile base + 16·i. That routine writes **the tile's bytes `+2..+0xB`**, 10 bytes.
The loader `0x00452BD0` reads the same 10 bytes back. It re-applies the terrain
through `0x0044FC40`, then restores the explored bit through its own setter or
clearer. Nothing in the loader clears the scanned bit.

In all three TrapTime saves the header `64 00 00 00 50 00 6E 00` sits at
**`0x14870`**. Tile 0 therefore starts at `0x14878`, and `Dk2SaveReader`'s grid
offset `0x14880` is 8 bytes into it. So the reader's lanes 0–1 are the tile's
own `+0xA`/`+0xB` (terrain and the scanned byte), and its lanes 2–9 are the
**next** tile's `+2..+9`. `LaneOwner` (8) is the east neighbour's owner. That
explains why the owner agreed with the design map only 72% of the time while
terrain agreed 99.7% (Q436).

Re-read at the true alignment (live `+2..+0xB`):

| save | scanned tiles | explored tiles | player 3's tiles, explored / scanned | any other keeper's |
|---|---:|---:|---|---:|
| 1 | 213 | 49 | 49 of 49 / 49 of 49 | 0% / 0% |
| 2 | 249 | 49 | 49 / 49 | 0% |
| 3 | 555 | 146 | 81 of 81 / 81 of 81 | 0% |

- **Scanned reproduces §11 exactly.** Save 1→2 set 36 tiles, and save 2→3 set
  306 in a 26×13 band. None cleared.
- **Explored is a subset of scanned in every save.** No tile is explored but
  unscanned. Save 2→3 set 97 tiles in a 25×8 band, narrower than the scanned
  band. None cleared.
- **Both bits cover 100% of the local player's tiles and 0% of the other three
  keepers'.** §11.2's conclusion (one viewpoint) holds for both bits.

## 3. Every writer

A scan for masked gameplay stores found **one setter of the scanned bit
(`0x00454CB3` in `0x00454C90`) and no gameplay clearer**. Tile construction
`0x00452B30` clears scanned with the compound mask0xA0 and clears explored
with the whole-word initialization at+8. The gameplay scan covered all 743k
instructions of both code blocks (`.text` and `cseg`, `0x00401000`–`0x0066BE19`)
in four forms: a `+0xB` byte with `0x40`/`0xBF`, a `+0xA` word with
`0x4000`/`0xBFFF`, a `+8` dword with `0x40000000`/`0xBFFFFFFF`, and a
register OR/AND within 10 instructions of a store. A second scan took any
displacement in the 131 functions that index the tile array. It found no
further writer of `0x40`, but it did find the pre-offset form
`[slab+8]+1` in `0x00451EF0`. The only whole-byte stores to `+0xB` in
tile-indexing code merge bit `0x20` (`0x0050B79A`, `0x0050B867`). The one
immediate `MOV byte [..+0xB],0x40` (`0x005D4058`) is in a pixel-format converter.

The explored bit's primitives are these. Each one writes the bit, redraws the
tile's minimap cell (`0x00451C60`) and posts a tile notification (`0x00517480`).

| Routine | Does |
|---|---|
| `0x00454B80` | set, **unless the tile belongs to a room whose `TileConstruction` (`room def+0x452`) is 3, 4 or 8** (3×3, 3×3 rotated, 5×5 rotated; values from the KWD format enum). This applies to any room, the viewer's or not. For those it hands over to `0x004E8B80`, which sets the bit along the room's tile chain through `0x00454D40`, so such a room is explored as a whole |
| `0x00454E00` | clear. Its room-chain twin is `0x004E8BF0` → `0x00454F10` |

### 3.1 Creature sight — `0x0048CC90`

The routine returns at once if the creature has a `PrisonOwner` (`+0x3CB`) or
is `CreatureDying` (`+0x3E6` bit 21). Otherwise it makes two passes.
Before either pass, a closed door at the source tile ends the update too
(door open byte +0x10D; checked after choosing the first pass's radius).

1. **Explored: an occluded square.** For a creature the local player owns, the
   radius is the definition's `DistanceCanSee` in whole tiles (`def+0x77B`,
   rounded by the loader), clamped to 2..7. The shipped value is 4 for every
   creature. `0x0044E920` first fills a 15×15 visible grid (`map+0xACF`, centre
   `+0xB3F`) around the creature's tile, and the pass explores every tile of the
   square that the grid marks visible; the grid's size is why the clamp stops at 7.
   The fill is angular shadowcasting over a table the map builds at load
   (`0x0044E9E0`): 225 11-byte entries at `map+0x124` (Flame's `CMapEntry`; `+1`/`+2`
   first and last slot, `+3`/`+7` dx, dy), sorted on squared tile distance, of which
   `map+0xBB4` = 78 are used. It opens all 256 slots of `map+0x24` and runs
   `0x0044E7F0` on entries 1..77 in order: an off-map cell closes its slot range;
   otherwise the cell is visible if any slot in its range (inclusive, wrapping at 256)
   is still open, and then, visible or not, `0x004511A0(x, y, 0, 1)` closes the range
   if the cell blocks. The generator and sort disassembly (2026-10-15) establish
   initial order dy=7..-7, dx=-7..7; bearings use `(2dx,-2dy)` and the four corners
   `(2dx±1,-2dy±1)`, with the angular extremes around the centre as endpoints.
   `Bearing.Of` reproduces the native angle function; endpoints reduce by `>>3`.
   For end=224..1, predecessors end-1..0 exchange with end only when its squared
   distance is smaller. This is not a stable sort. Only the first 78 sorted
   entries participate, including the centre; some radius-five boundary cells
   and all four (±4,±4) corners are therefore absent even without blockers.
   The selected boundary still needs an original capture. For a
   creature the local player does not own, the radius is 1, but only while
   `+0x475` counts down and `+0x476` names the local player (§3.7). Otherwise
   there is no pass.
2. **Scanned: a strict circle, no occlusion.** Local player's creatures only.
   The radius is `def+0x783`, which the loader copies unrounded from staging
   `+0x10F1`. That is file `0x0D01`, **`PerceptionRange`**, `0x6000` (6
   tiles). **§8.1 is confirmed on the radius and the shape, and corrected on
   two points.** The distance is measured from the creature's **exact**
   fixed-point position to each tile centre. Each squared term is shifted
   right 12 **before** the sum, which is then compared `<` against
   `r²>>12`. So the truncation can admit a tile that exact arithmetic would
   leave out.

**Callers (13 sites).**
- The creature update `0x00488930`: every update while the creature is
  **Possessed** (`+0x3E6` bit 7, at `0x00488A6E`), and again at `0x0048A817`
  under a condition not traced.
- Level start (§3.3).
- The hand drop `0x004BC710`.
- Imp dig and mine work (`0x00496E00`, `0x004971D0`), which opens new ground.
- Idle state 1 (`0x004773D0`).
- The breach check `0x00498BB0`.
- The shot processor `0x004AD7A0`, and `0x004AFE50` after a creature is
  created and levelled.
- An alliance walk (`0x004BF410`, under `0x004BE630`).
- A game-action handler at `0x00514816`, whenever a position update moves the
  creature onto a different tile.

So the walk is event-driven, not run every tick. What possession adds is the
per-update cadence and nothing else: no reveal of its own.

### 3.2 Terrain change — `0x0044FC40(x, y, terrain, owner, …)`

This is the terrain-change routine. It has 10 callers, among them the save
loader and the defeat routines. It does two things to fog.

- **Re-shroud.** If the *old* terrain carries flag `0x4000`
  (`UnexploreIfDugByAnotherPlayer`) and the new owner is not the local player,
  it **clears** the explored bit. The loader restores the saved bit itself
  afterwards (§2).
- **Ownership reveal.** If the new owner is the local player, it sets the
  explored bit. So every tile that becomes yours is explored, whatever
  creature was there to see it.

### 3.3 Level start — `0x0050B5B0` → `0x0050C800`

Skipped when the level comes from a save (world `+0x7239` bit `0x40`).

- **Mode-gated seed.** In game modes 2 and 3 only (`0x0075AD4B`: 1 is single
  player), `0x0050C800` calls one of two routines.
  - `0x00451EF0` when the switch `0x0075AE8B` is 0 (fog off): **every tile
    explored**.
  - `0x00451FD0` otherwise. It explores each tile the local player owns, and,
    when that tile's terrain is not `Solid`, its four orthogonal neighbours.
    Diagonals are not included. It also explores **every tile whose terrain is
    `AlwaysExplored` (`0x200`)**, a flag no shipped terrain carries. Last, it
    **clears the explored bit along all four map edges**, so the border starts
    hidden. OpenKeeper's code does the same; Q429's old summary of it said
    "revealed", and that was wrong.
- **Flagged action points, every mode.** For each action point whose runtime
  flag byte `+0x32` has bit `0x10`, the level start explores the point's
  inclusive rectangle through `0x00454B80` (`action-points.md`'s bit-4 reader
  `0x0050C083`). It then posts one rectangle notification. **85 of the 1,152
  shipped points carry the bit, in 30 levels — CONFIRMED** from the corpus.
  Those include campaign levels 2, 5, 7, 9, 15a, 15b and 18, the four Secret
  levels and all seven `mpd`. OpenKeeper's format enum names the editor bit
  `REVEAL_THROUGH_FOG_OF_WAR` (a format fact).
- **Sight.** Each creature the local player owns runs §3.1 once. The one
  exception is a creature with `+0x46F` bit `0x40` set whose tile holds a thing
  with type byte `+0x18` equal to 11, 12 or 16. What that thing is was not
  identified.
- **Nothing seeds the scanned bit.** It comes from sight alone.

### 3.4 The Dungeon Heart disc — Q430

The heart's per-turn update `0x004D9A60` keeps a start stamp at `+0x5B`, equal
to `TimeBeforeDungeonHeartConstructionBegins × var19 + 1`. Its opening
sequence runs while `start ≤ now ≤ start + 9 × var19`. Inside it, a switch on
the elapsed ticks has cases 56–62. Each case calls `0x0049D150(r)` with
`r = DungeonHeartClaimScanRadiusTiles` (variable 180, its only reader, at
`0x004D9BCE`) scaled by a literal: 9/64, 37/128, 27/64, 73/128, 91/128,
110/128 and 1. That gives OpenKeeper's 18/37/54/73/91/110/128.

- **They are a table in code, not a formula.** The nearest-integer rounding of
  128·k/7 gives 55 at k=3, and DK2 has 54.
- **The disc is explored-only.** `0x0049D150` requires the heart's owner to be
  the local player. It then explores every tile strictly inside the circle
  whose explored bit is clear **and whose terrain is `RevealThroughFogOfWar`**.
  It is not a claim scan: nothing in it claims. The variable's name describes
  the look (terrain 38).
- **With shipped data the cases cannot be reached.** Variable 19 is 4 in all
  66 levels, so the window closes at elapsed tick 36, and the switch needs 56.
  **The save agrees — CONFIRMED.** In `fow_1.SAV`, 72 Gold tiles lie within 15
  tiles of player 3's heart, and none is explored. 18 of them are scanned.
- **Nothing in play raises variable 19 (Q434, 2026-10-05).** Its two writers run
  only while a level file loads: the `Variables_SetMisc` stub `0x0051CDCA` (id 19,
  dispatched only by the file-record handler `0x00521790` through world `+0x1F24`,
  reached from `World_LoadLevelKwd` and `GameAction_LoadLevel`) and
  `0x0051E708`, which copies the LEVEL record's `TicksPerSecond` (`+0x12B6`, file
  offset `0x1182`). No trigger or per-turn path reaches either, no shipped file
  carries id 19, and every level ships 4: the disc needs an edited level with
  `TicksPerSecond` of 7 or more.

### 3.5 Room ownership change — `0x004E5650`

When a room changes owner, every tile of its chain that the local player owns
is explored, and its minimap cell is redrawn. Its callers include trigger
dispatch `0x00518AD0` and the defeat routines `0x004DAE40` and `0x004DAA10`.

### 3.6 Trigger action 17 — `0x00518AD0`

`trigger-dispatch-tables.md`'s arm: body byte 1 = 0 **explores** the action
point's rectangle and non-zero **un-explores** it, through `0x00454B80` and
`0x00454E00`. There is no player test, so it applies to whichever player is
local. 268 of the 291 shipped records reveal.
Frosty's Lair's startup use is mapped in [portal-visibility.md](portal-visibility.md#scripted-startup-reveal-q197-2026-10-28).

### 3.7 The Torture Chamber's information — `0x004F2370`

Reward 11 either explores the first of the victim owner's rooms (the chain from the
player's `+0x36`) whose first tile is not yet explored, every tile of it, or tags one
of that player's creatures that stands on unexplored ground: `+0x475 = 16`,
`+0x476 = the chamber's owner`. The tag feeds §3.1's radius-1 square. The heart
branch has no local-player test in the lines read.

### 3.8 Game action `0x79` — `0x00514240`

If the issuing object's player is the local player, `0x004BFF60(p)` explores
every tile player `p` owns. `game-actions.md` lists the id; what issues it is
unread.

### 3.9 An ability effect — `0x00474F80`

This is the four-neighbour trap/door effect in `doors-and-traps.md`. Its tail
explores one tile. It is the campaign finale's walk state 234 (creature state
`0x00474F80`), see `campaign-win-sequence.md`.

### 3.10 Negative results

Each negative gives the range it covers.

- **No gameplay clearer of scanned was found.** New tiles clear it through
  `0x00452B30`; see §3 for the masked-store scan's scope.
- **No spell writes either bit directly.** The writer set of the explored
  bit is closed over the direct and helper forms scanned. Its callers are §3.1
  and §3.2–§3.9, and none of them is a keeper-spell handler. Sight's process25
  and local presentation region have now been read; see `keeper-spell-sight.md`.
  Reveal Map, a special, explores every tile through `0x00451EF0` (`specials.md` §4).
- **Terrain flags.** A scan for tests of the runtime flag dword `+0x17B`
  covered direct tests, `+0x17C` byte tests, and a load followed within 6
  instructions by a test of the register or its high byte. It found
  `AlwaysExplored` read once (`0x00451FD0`) and `UnexploreIfDugByAnotherPlayer`
  once (`0x0044FC40`). The positive control found 13 readers of `0x01000000`.
- **Re-shroud and Re-fog.** The explored bit is cleared by: action 17 with
  byte 1 ≠ 0; `0x0044FC40` on `0x4000` terrain taken by another player; the
  seed's border pass; and the loader's restore. No path above consults a
  setting other than the fog switch `0x0075AE8B`. Where Q192's labels are read
  is still unread.

## 4. Whose viewpoint each writer serves

Every per-player writer tests **the local player** (world vtable `+0x264`):
sight, the heart disc, the terrain-change reveal, room ownership, the seed and
action `0x79`. The rest name no player, and therefore write the local
machine's view: flagged action points, action 17, `AlwaysExplored`, fog-off,
and the border clear. A computer keeper's creatures do run §3.1, because most
callers do not filter by owner, but both of its passes exit for a non-local
owner. **No writer keeps state for a computer keeper.** This agrees with §11
and with Q190's "no storage for AI fog".

## 5. Build plan against our code

`TileState.DiscoveredBy` stores scanned state; `ExploredBy` stores explored
state, both as ADR-0076's per-viewer masks. Merged hash/save schema260 carries both.
Each DK2 local-player test becomes a viewer-specific mask operation; an
unconditional writer updates every represented viewer. `WorldView` selects
terrain 27/38/real terrain from those separate layers; rooms and entities
require exploration. No distance fog or brightness approximation is applied.

| Source | Hook | State we lack |
|---|---|---|
| Split the layers | Built: independent masks, hash/snapshot, terrain selection and entity gates | none |
| §3.1 explored square | Built: `DiscoverySight` reproduces the native table and angular grid; `FogOfWarSystem` applies the clamped `DistanceCanSee` square and `DiscoveryRules` the whole-room cases | the `+0x475`/`+0x476` torture tag |
| §3.1 scanned circle | Built with exact position, per-term `>>12`, prisoner exclusion and source-tile closed-door guard (2026-10-14). Per-tick cadence and `InHand` exclusion stay ours | none |
| §3.1 cadence | Tile-change and event-driven in DK2, every tick in ours; this can reveal earlier or re-explore after a clear | native request cadence |
| §3.2 terrain change | Built in `TileWork.Convert` and `RoomRules.Place`/`Unmake`: ownership reveal and old-terrain re-shroud | remaining direct defeat/script terrain writes |
| §3.3 seed | `WorldState.FromMap` applies terrain ownership reveal only; the separate mode-gated seed remains unbuilt | game mode, neighbour/flag/border seed |
| §3.3 action points | Built: `ThingProjection`, flag `0x10`, inclusive rectangle for every viewer | none |
| §3.4 heart disc | do not build while var 19 is 4 (Q434) | — |
| §3.5 room owner | Built: `RoomHealthRules.TransferOwnership` explores each slab for its new owner | remaining direct defeat/script ownership writes |
| §3.6 action 17 | Built: `TriggerExecutor` explores or un-explores the rectangle for every viewer | none |
| §3.7 torture | `TortureRules`, when reward 11 is built | the reward table |
| §3.8, §3.9 | wait for their issuers | the game action, the ability |

`WorldState.FromMap` applies the terrain loader's ownership reveal, without
inventing the separate game-mode seed. Remaining source limitations are listed
in the table; temporary Sight presentation is separate (`keeper-spell-sight.md`).
