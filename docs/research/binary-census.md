# Census: what DKII.EXE holds that the function records do not

Source(s):
- `DKII.EXE` v1.70 (GOG), the canonical Ghidra project, read 2026-10-14. Addresses
  and offsets are facts about the binary in our own words (ADR-0046)
- The function records in `functions/`, as of commit `4b42565f`

Original version: DK2 v1.70 (GOG)

Question: **Q595** (whether DK2's per-turn order, section 2, moves an outcome).

The census asked where unread code could hide a mechanism that no question had
pointed at. Its outputs and scripts are local, under the ignored
`reverse-engineering/census/2026-10-14/`. Sections 2 to 9 read the leads it ranked
first; none of them is a simulation mechanism we lack.

## 1. The numbers

| Part of the binary | Functions | With a record |
|---|---|---|
| Ghidra functions, plus pseudo-functions for code it never made into one | 11,596 | 1,505 |
| C runtime (`0x00634000`–`0x00652FFF`) and exception-unwind stubs | about 3,600 | none needed |
| `0x00550000` and up: Bullfrog's library, the Direct3D engine, libpng, zlib, SoundFont | about 2,800 | few |
| The simulation core, `0x00401000`–`0x0051FFFF` | 3,497 | 1,353 |
| Core functions reachable from the turn, the action table or the level loader, unread | 941 | 0 |

About 108,000 instructions lie outside any Ghidra function, and 148 records
point into that code. Reachability counts direct calls, function pointers and
the tables a function references, so it over-reaches through vtables.

Two census rankings were artefacts. The most-called unread function,
`0x00634DB0`, sits in the C runtime and is used like `sprintf` (not read). A
global read by many records and written by one unread function is often a
static pointer set once (section 8).

## 2. The per-turn frame

`World_UpdateTurn` (`0x0050A450`) makes these calls after its triggers, in
this order:

| Site | Callee | `this` | What it does |
|---|---|---|---|
| `0x0050A7D7` | `0x004B7830` | world `+0x8` | every thing's update |
| `0x0050A7E2` | `0x0044E2A0` | world `+0x9DA` | the effect grid around the listener (section 5) |
| `0x0050A7ED` | `0x004EC450` | world `+0x9BE` | every room's tick (section 6) |
| `0x0050A7F5` | `0x004C2310` | world `+0x7A` | every player's frame |
| `0x0050A800` | `0x004C41E0` | world `+0x2E54` | the message sweep |
| `0x0050A807` | `0x0050F8C0` | world | the generated-special placer (section 4) |
| `0x0050A80E` | `0x00510440` | world | world Tremor |
| `0x0050A815` | `0x00504F00` | world | the battle records (section 3) |

So DK2 ticks rooms after the thing pass and before the player frame. Ours runs
the room lifecycle before creatures and room health inside the player frame
(`SimulationSystems.cs`), and sweeps messages first by choice (ADR-0245).

## 3. Battles are presentation

The world holds eight battle records of `0x26` bytes at `+0x9C50`. A record
keeps a member list at `+0x04` (creatures linked through `+0x240`/`+0x23E`),
the members' mean position at `+0x06`/`+0x0A`/`+0x0E`, a member count at
`+0x1E` and an age in turns at `+0x22`.

- `0x00504F00` runs `0x00505310` on each record, then offers every creature
  of every player to `0x00504FB0`. A creature qualifies in category 22 (combat),
  in neither state 202 field (`+0x25E`, `+0x32D`), with `+0x3E6` bit `0x8000`
  clear and its battle flag `+0x37A` clear.
- `0x00504FB0` needs the thing named by the creature's `+0x502` to exist (byte
  table `0x006ED540`) and to be a creature (`+0x12` zero). It joins the nearest
  record whose centre is within 5 tiles, else the first empty one. It sets
  `+0x37A`, recomputes the centre, and sets `+0x37E` to four seconds ahead.
  When the creature is the local player's and the record held none of that
  player's, it calls slot `+0x4C` of an object reached through world `+0x4`.
- `0x00505310` sends a member whose timer `+0x37E` has passed to `0x00505500`
  (not read). It sums the threat of fighting members not owned by the local
  player: definition `+0x698`, scaled above level 1 by the level percentage at
  `[0x006E4154] + 0x105` (`computer-keeper-threat.md`). The sum picks a music
  cue through the effect service: `0x101` from `MusicLevelTwoThreatThreshold`,
  `0x102` from Three, `0x103` from Four (variables 215 to 217).
- World slot `+0x2B8` (`0x00505250`) returns the next record holding a local
  player's creature. Only the interface calls it (`0x00426974`, `0x004269DB`).

A text scan for those offsets finds only these, the creature constructor,
death and collapse bookkeeping (`0x005051F0`), the creature debug dump,
presentation code, and one library function (`0x0056DFD0`, not read) that
loads words there. So the records feed music, an alert and a camera control,
and no simulation code reads them. Ours has no battle records.

## 4. The generated-special placer is inert

`0x0050F8C0` places DK2's random specials. It needs the dword at `0x0075AE7B`
non-zero, world `+0x729F` (CurrentNoOfGeneratedSpecialsOnLevel) below the
byte at `0x006F25BC`, and the turn at or past world `+0x729B`
(TimeToPlaceNextGeneratedSpecial). It then draws a random tile whose terrain
`+0x17B` has bits `0x81` and `0x100` clear. It creates object `117 + random(12)`,
one of the twelve specials from Increase Level to Heal All, for the neutral
player at the tile's centre. On success it sets object `+0x188`, increments
world `+0x729F` and every player's `+0x184`, and sets the next time to the
dword at `0x006F25BD` times TicksPerSecond.

Nothing gives the cap a value. `World_InitializeRuntimeStorage` zeroes the
variables block `0x006F2550`–`0x006F2A01`, cap and interval included
(`0x00509A54`). No instruction stores to either address, none of the 170 loads
of the block pointer `0x006E4154` is used at `+0x6C` or `+0x6D`, and no data
holds a pointer to them. The variable map has no id at either address
(`balance-variables-in-the-binary.md`, between ids 28 and 29). The switch
`0x0075AE7B`, field `+0x134` of the session object at `0x0075AD47`, has no
store either, direct or in the 32 methods called on that object. So the count
test always fails and nothing is placed. These are negative scans, so the
verdict is LIKELY.

## 5. The effect grid around the listener

`0x0044E2A0` runs each turn while the effect service (`[0x0075B468]`) slot
`+0x74` answers non-zero. It takes a listener point from a record reached
through world `+0x4` (`+0x10`, slot `+0xD0`, field `+0xD74`), or in mode 7 (slot
`+0xCC`) from the cursor mapped onto the screen. It rebuilds when that point or
three orientation words differ from the stored ones, in mode 7, when its own
flag `+0xBBA` asks, or while the local player's `+0x134` is non-zero. The
rebuild fills an 11-by-11 grid of words around the listener. A cell is zero
unless the tile's cell byte `+9` has bit `0x20`. It then holds a word of the
terrain definition (`0x006DA4A8` by cell byte `+0xA`): `+0x195`, or `+0x199` on
a Temple tile (room type 13) whose room `+0x61` is zero, or `+0x19D` on an arm
the caller selects in `EBX`. The grid goes to service slots `+0x3C` and
`+0x48`, and the point to `+0x54`. It writes nothing but `+0xBBA`. That it
drives terrain ambience sound is an inference.

## 6. The room manager

The manager at world `+0x9BE` owns a fixed pool of 672 room records of `0xA9`
bytes each.

- `0x004EC000` allocates the pool (element constructor `0x004E3790`, destructor
  `0x004E37F0`), stores it at `+0x0A` and pushes every record on the free list
  at `+0x10` by its id (`+0x04`). It caches its argument in `0x006ED484` and
  itself in `0x006ED488`. Many room functions read `0x006ED484`.
- `0x004EC190` frees every room in use through `Room_Free`, clears both globals
  and deletes the pool.
- `0x004EC450` walks the in-use list (`+0x0E`, link `+0x21`) and runs
  `Room_Tick` on each room, taking the next link first.

Ours has no room cap; no level comes near 672 rooms.

## 7. The keeper-spell chunk handler

`0x0051FCD0` is handler 5 in the record-kind tables that `0x0051E0A0` (the
level loader, `0x0051E20F`) and `0x00541AA0` (`0x00541C8A`) build. It loads one
keeper-spell record into the world's 122-byte table at `+0x1604`
(`research-tree.md`) through the loader `0x004F7CE0`.

- While world `+0xA3C7` is clear, it inserts the record sorted by spell id
  (`+0x75`): later entries whose id is not lower move up one. It adds the
  entry's `+0x36` to world `+0x1610` and increments the count at `+0x160C`.
  A record past the capacity at `+0x1608` is dropped.
- While `+0xA3C7` is set, it calls world slot `+0x110` with the spell id and
  then the loader. That reads as reloading an existing entry in place. The
  same flag gates `CreatureDef_BuildFromRecord`'s checksum (`kwd-creatures.md`).

## 8. Smaller leads

- The effect service pointer `0x0075B468` is set once, by `0x00567130`, to the
  static object at `0x0075B880`.
- `0x004DFAA0`, called only from `World_InitializeRuntimeStorage`, requests one
  resource per campaign level: `3dmap_level` names with the 6a/6b, 11a–11c and
  15a/15b variants, five secret levels, then seven `MPD_map` names. They land at
  world `+0x9EAC` and `+0xA24E`. It stops at the first failed request. The
  level names are file names, not rules.
- The steering globals `0x006ED3FC`–`0x006ED418` are the four callbacks and
  contexts `Steering_Init` installs through `0x004D2770`–`0x004D27D0`
  (`steering-behaviours.md` §1).

## 9. What remains

The 941 unread core functions are ranked in the local reading list. Every lead
read so far turned out to be presentation, set-up or loader plumbing. The one
outcome-shaped difference is the turn order in section 2 (Q595).
