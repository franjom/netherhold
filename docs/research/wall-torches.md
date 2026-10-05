# Wall torches: where DK2 puts them, and which layer owns them

Source(s):
- Retail v1.70 `DKII.EXE`, read in Ghidra 12.1.4, 2026-09-28. The addresses
  were re-read in a second, independent pass the same day (§8)
- The shipped `Data/editor/Rooms.kwd` and `Terrain.kwd`, read directly
- OpenKeeper `07535b58` (`view/map/MapViewController.java`, `handleTorch`), as a
  lead only: its rule comes from watching the game (its issue #128), not the
  binary
- Flame's `CBridge` field names (`torch1Idx`, `c_world_entry_torch1`), cited as
  Flame's

Original version: DK2 v1.70 (GOG)

**The headline: torch placement is a rendering job.** The whole rule is one
function, `0x00439B90`, slot `+0xB8` of the `CBridge` vtable at `0x0066CC94`.
Its allocation string names `D:\Dev\DK2\Projects\source\game\BridgeLighting.cpp`.
The bridge is the object that hands the game world to the 3D engine. A torch
is an engine light, not a game thing, and nothing in the game rules reads it
(§5). In this project it belongs in **`DK2.Presentation`**. The simulation only
has to report terrain changes, which it already does.

Everything read from the binary is **STRONGLY INFERRED** unless it says
**CONFIRMED**. Facts marked CONFIRMED were measured from the shipped data.

## 1. The rule, for one wall tile

`0x00439B90(x, y, unused)` ends in `RET 0xC` and refreshes the torches that
belong to the wall tile at (x, y).

1. **A solid tile loses its own torches.** If the world says (x, y) is solid
   (`CWorld` vt`+0x208`), every torch light in the tile's own list is
   destroyed (`0x00439BE4..0x00439C39`). A torch light is an engine object
   with type `+0xF4 == 2` and `+0x134` bits `0x10` and `0x4000` set.
2. **The wall must qualify.** `0x00452D80` requires both of these:
   - the terrain has the `Torch` flag (runtime `+0x17B` bit `0x40`, file
     `0x190` bit 6);
   - `CWorld` vt`+0x21C(x, y, 0)` returns 0, meaning the tile is not a door.

   If the wall does not qualify, the torch on each of its four faces is
   destroyed, found by its exact position (`0x004432C0`), and the function
   returns.
3. **A terrain-art gate.** If runtime terrain byte `+0x1CB` bit 1 is set and
   the terrain is not Impenetrable, there are no torches
   (`0x00439D04..0x00439D25`). Bit 1 means the terrain has both side art and
   top art. **It is inert with the shipped data**: Impenetrable Rock and Edge of
   Map are Impenetrable, and Reinforced Wall has no top art.
4. **Faces are tried N, E, S, W.** The direction table at `0x006BC350` is
   (0,−1) (1,0) (0,1) (−1,0). For each face, the floor tile F in front of it
   must pass all of these:
   - F is inside the bridge grid (`0x00440F20`) and is not solid;
   - the parity test on F's own coordinates passes (§2);
   - F is not a door (vt`+0x21C`);
   - F holds **no torch light at all** yet (`0x00443240`, which matches on type
     and flag only, not on position).
5. **Light and mesh are created** through vt`+0xA4` (`0x004397E0`) with flags
   `0x4052` (§4). A torch in a room is linked into the room's light list at
   `room+0x12` (`0x004E67D0`).

**One torch per floor tile, and the first wall processed wins it.** No floor
tile has a direction priority. Which wall wins a tile that faces two walls
depends on the order in which the walls were refreshed (Q446).

## 2. Spacing: parity, and two rooms that follow their furniture

| Face | Test | Helper |
|---|---|---|
| N and S | `(F.x + phase)` is odd | `0x00452F20` |
| E and W | `(F.y + phase)` is even | `0x00452EC0` |

`phase` is 0 unless F lies in a room whose runtime definition has `+0x482`
set. Then it is the room's wall-furniture phase: `room+0x92` for N and S,
`room+0x96` for E and W (`auto-placed-furniture.md`). A negative phase means no
torch on that face.

The room loader sets `+0x482` to 1 only for **RoomId 12 (Torture Chamber) and
15 (Casino)** (`0x004EBD6C..0x004EBD86`). In those two rooms torches alternate
with the wall furniture. Everywhere else, other rooms included, the plain
parity applies.

## 3. What was wrong or missing in OpenKeeper's rule

| OpenKeeper | DK2 |
|---|---|
| At most one torch per non-solid tile, on a `Torch`-flagged neighbour | **Same** |
| x odd for N/S walls, y even for E/W walls | **Same**, tested on the floor tile |
| Each floor tile tries N, W, S, E | **No.** Each *wall* tries N, E, S, W; the first wall refreshed takes the floor tile |
| A room with no torch art gets no torch | **No.** It gets the light without a mesh (§4) |
| — | Door tiles are excluded, as wall and as floor |
| — | Torture Chamber and Casino follow their furniture phase |

`AllowRoomWalls`, ownership and `PlayerColouredWall` are never read.

## 4. Light, mesh and position

**Parameters.** If F has a room (tile word `+2 & 0xFFF`), the room definition
supplies them. Otherwise they come from the balance defaults.

| | Room definition (file offset) | Default |
|---|---|---|
| Colour R, G, B | bytes `0x397`/`0x398`/`0x399`, each **+255** when room flag `0x200`/`0x400`/`0x800` is set | variables 52/53/54 = 300/200/100 |
| Intensity | `0x36E` (u16 in the file, stored as one byte) | variable 219 = 180 |
| Radius | u32 `0x3C3` | variable 220 = 6144 (1.5 tiles) |
| Height | u16 `0x380` | variable 221 = 6144 (1.5 tiles) |

- **Radius clamp.** The light setter `0x0043A1E0` clamps the radius to
  `0x2780` (10,112). The shipped 10,240 of Guard Room and Hero Gate Final/Tile
  is therefore cut slightly.
- **Loader gates.** The loader copies each torch field only when a loader-side
  dword at intermediate `+0x64..+0x78` is non-zero (`0x004EBCC2..0x004EBD5E`).
  What sets those dwords is **UNKNOWN**. The +255 additions are applied either
  way.
- **Terrain light height is not read.** File `0x18C` does not reach torches;
  that field belongs to the tile's own light (`TerrainLight`, bit 28), a
  separate mechanism.

**Room flags 9–11 are torch-colour high bits.** `RoomFlags` in
`src/DK2.Data.Model/RoomDef.cs` names bit 9 `Attackable` (ufdada's bit order)
and bits 10–11 `Unk10`/`Unk11`. The loader (`0x004EBCD5..0x004EBD2A`) uses them
to add 255 to red, green and blue. Nothing in `src/` reads them.

**Mesh.**
- **F has no room:** the bridge's default `Torch1` mesh at `CBridge+0x382`,
  loaded with sound `GLOBAL\OBJECT_TORCH`.
- **F has a room whose torch-art flag bit 0 (definition `+0x472`) is set:** the
  room's torch art at `+0x467` (`0x0043A0C0`).
- **Otherwise:** a light with no mesh (object `+0xD5` bit 0 cleared).

**Position, in Fp32 (4096 = one tile), relative to floor tile F.** The torch
sits at `F·4096 + (fx, fy)`, from table `0x0066CBF0`:

| Face | (fx, fy) | Yaw (2048 = full turn) | Facing vector (`0x006D3980`) |
|---|---|---|---|
| N (wall south of F) | (0x800, 0xE00) | 1536 | (0, +511, 0) |
| E | (0x200, 0x800) | 0 | (−512, 0, 0) |
| S | (0x800, 0x200) | 512 | (0, −512, 0) |
| W | (0xE00, 0x800) | 1024 | (+511, 0, 0) |

The torch is centred along the shared edge, 512 (1/8 tile) out from the wall
face, at the torch height. Yaw is `((d + 127) << 9) & 0x7FF`, stored at
`+0xE2`. The facing vector points from the torch into the wall.

### Shipped room values (CONFIRMED, `Rooms.kwd`)

Colours include the +255 high bits. A blank cell is height 6144, radius 6144 or
art `Torch1`.

| RoomId: room | RGB | Int | Height | Radius | Art |
|---|---|---|---|---|---|
| 1 Treasury | 349,200,50 | 180 | | | |
| 2 Lair, 11 Prison | 299,200,60 | 160 | | | |
| 3 Portal | 200,67,10 | 128 | | 8192 | |
| 4 Hatchery | 299,200,100 | 180 | | | |
| 5 Dungeon Heart | 499,200,60 | 180 | | | |
| 6 Library | 250,250,150 | 180 | | | |
| 7 Training Room | 299,220,115 | 180 | | | |
| 8 Wooden Bridge | 80,100,120 | 128 | | | |
| 9 Guard Room | 193,165,103 | 128 | | 10240 | |
| 10 Work Shop, 12 Torture Chamber, 15 Casino, 16 Combat Pit, 20 Hero Gate 2x2 | 299,200,60 | 180 | | | |
| 13 Temple | 50,150,160 | 70 | | 7372 | *(none)* |
| 14 Graveyard, 26 Crypt | 90,132,146 | 150 | | 7372 | |
| 17 Stone Bridge | 100,95,90 | 128 | | | |
| 18 Hero Gate Final, 19 Hero Gate Tile | 299,299,299 | 128 | 4096 | 10240 | *(none)* |
| 21 Hero Gate FrontEnd, 23 Hero Gate 3x1, 24 Mercenary Gate | 299,200,60 | 180 | | | *(none)* |
| 22 Hero Stone Bridge | 0,0,0 | 0 | 0 | 0 | |
| 25 Hero Portal | 200,67,10 | 128 | | 8192 | *(none)* |

Hero Stone Bridge has a mesh but a zero light. The art names were read from the
file. The art flag byte was read by the first pass only.

## 5. The layer: an engine object, not a game thing

- **Allocation.** The light is allocated from the bridge pool at
  `CBridge+0x132F`, size `0x146` (`0x004397F1`), which is Flame's `CLight`
  size.
- **Registration.** It is registered in the engine object table `0x006D6458`
  (`0x004468E0`), not the thing table `0x006EE540`.
- **Links.** It is linked into the bridge's light list (`CBridge+0x33A`) and
  per-tile grid (`CBridge+0x326`).
- **Randomness.** Its one random draw (`0x005558D0`, the flicker phase) is
  seeded from the bridge. `0x00439B90` never touches the synced seed
  `0x006F3FA8`, so torches cannot desync lockstep.
- **Save — INFERRED not saved.** The map saver `0x0044F3F0` writes tile bytes
  only (Q436). The save loader `0x00452BD0` re-applies terrain through
  `0x0044FC40`, which rebuilds the torches.
- **Network — INFERRED not sent.** Every client's simulation posts the bridge
  commands locally (§6).
- **Simulation readers — none found.** The only torch code on the simulation
  side is the room light-list bookkeeping (`0x004E67D0`, `0x004E6840`, and
  `0x004E39C0` on room removal). No fog or visibility reader was found. That is
  a negative search: it shows nothing reads torch light, not that light has no
  effect anywhere.

**For this project:** `WorldView` works out torches from terrain, rooms and
Q446's refresh order, and emits them alongside `TileView`. ADR-0035 already
keeps this kind of decision out of the renderers. `RoomDef` will need the five
torch fields and the art flag, none of which `KwdTerrainRooms` parses today.

## 6. When the refresh runs

The simulation posts bridge commands through `0x00517480`, the only caller of
the enqueue `0x00527150`, from 62 call sites. The command id is the record's
word at `+0xC`. **Dispatch is synchronous — CONFIRMED (2026-10-05):** CBridge's
vtable slot `+0x28` (`0x0066CCBC`) is `0x0043B1E0`, which calls the handler
from the table at `CBridge+0x2CA` (ids below `0x17`) at once and then forwards
the record to the next sink's slot `+0x28` (`0x0043B20F`). That the session's
sink is the CBridge is STRONGLY INFERRED. The command table at `0x0066D0D8`
(copied by `0x0043AAD0`) holds these torch and gate commands:

| Id | Handler | What it does |
|---|---|---|
| 2 | `0x00447B20` | Destroys every bridge light and clears the gate `CBridge+0x12E3`. Its only poster is the map teardown `0x0044F280` (`0x0044F33C`) |
| 3 | `0x00447C70` → `0x004484A0` | Refreshes the tile's bridge state, then calls vt`+0xB8`. Runs only when the gate is set **and** record `+4` is non-zero |
| 4 | `0x00447CA0` | Calls vt`+0xB8` directly, ungated |
| 7 | `0x00447D50` | Sets the gate (`0x00447D99`), clears bit 13 of every bridge grid word, makes three global lights and one effect, then sweeps every tile for terrain `+0x17B` bit 0 work through world vt`+0x1F8`. **It places no torches**: no call to vt`+0xB8`, `0x004484A0` or `0x00439B90` before its `RET 4` (`0x004482A1`) |
| 16 | `0x00448710` | Recolours the torch lights on a tile: room definition `+0x456/+0x458/+0x45A`, or variables 52–54 when the tile has no room |
| 17 | `0x004488A0` | Re-runs `0x004484A0` over the 3×3 around a door |
| 19 | `0x00448800` | Posted per room tile with command 16's posters (below); not read |

- **Terrain setter `0x0044FC40`.** When the new terrain is not Solid, it posts
  command 4 for each of the four neighbours, N, E, S, W from `0x006BC350`, that
  passes `0x00452D80` (Torch flag; "not a door" is inferred from world
  vt`+0x21C`), then for the qualifying tiles two steps away (`0x00450108`,
  `0x0045019D`). When its argument 5 is set, it posts command 3 with `+4 = 1`
  for the tile itself (`0x00450288`).
- **Command 16 — CONFIRMED (2026-10-05, Q445).** Its only posters are the two
  room-change closers, `0x004F04F0` (after a build: the player's wave
  `0x004C7380`, the computer keeper `0x004C5B40`, trigger dispatch
  `0x005195F0`) and `0x004F09B0` (after a sell or destruction: the sell wave
  `0x004C7160`, trigger dispatch `0x005194FC`/`0x00519637`, action 11 at
  `0x00497EF2`/`0x004980A9`, and the unread `0x005128D4`). Each sets EBP to
  `0x10` before its loop (`0x004F053D`, `0x004F0A30`), which hid the id from a
  pre-call window, and posts 16 for every in-bounds tile of the changed
  rectangle grown by one tile, y outer, x inner (`0x004F058D`, `0x004F0A83`).
  Then, for each Buildable room touching the rectangle, it refreshes the room's
  tiles through `0x004522A0` (command 3), calls `0x004EF1F0` and, when the
  room's definition `+0x482` is non-zero (the flag of §2), posts command 19
  per room tile (`0x004F07CA`, `0x004F0CCB`) and calls `0x004F0E30`. That a torch therefore takes a new
  colour when a room is built, sold or destroyed beside it is STRONGLY
  INFERRED. The other 60 sites post other ids, the five register-loaded ones
  included (`0x0048A66B` 8, `0x0048F33B` `0x12`, `0x004A6604` `0x11`,
  `0x004AEDD1` and `0x004AF8BC` `0x0A`).
- **Callers of the setter:** the KWD map loader `0x0051EC60` (×3), the save
  loader `0x00452BD0`, the health applier `0x00452F60` (dig, claim, reinforce),
  the room split `0x004ED7B0`, heart and defeat (`0x004DA4E0`, `0x004DAA10`,
  `0x004DAE40`), and the unread `0x00450770`, `0x00455030` (×5) and
  `0x00518AD0` (×4).
- **Room path.** `0x004F0E30` calls vt`+0xB8` directly on every `Torch` wall
  next to a room slab, then posts command 3. Its callers are the wall-phase
  setter `0x004ECDB0`, `0x004F04F0`, `0x004F09B0` and `0x004D6762` (in
  `0x004D6750`).
- **Other command-3 posters:** `0x004522A0`, `0x00454B80`, `0x00454C90`,
  `0x00454D40`, `0x00454E00`, `0x00454F10`, `0x004A5070`, `0x004A5290` and
  `0x004D5FA0`.
- **Command-17 posters:** `0x00467460`, `0x00467869`, `0x004A5290`,
  `0x004A6604`, `0x004C3670`, `0x0050B5B0` and `0x00520F40`.

### Level start (Q446)

**CONFIRMED:** GameAction_LoadLevel `0x00513870` calls the teardown
`0x0044F280` (`0x00513923`, command 2: gate closed), later the level initialiser
(`0x00513C42`), then posts command 7 (`0x00513C94`). GameSession_Run
`0x00525370` posts 7 at `0x0052574D` after the KWD load `0x0050E620`
(`0x0052565A`), and the save rebuild `0x0050B3C0` at `0x0050B51B`. The KWD
per-tile callback `0x0051EC60` calls the setter with argument 5 = 1 on all three
of its paths. The CMap tile constructor `0x00452B30` zeroes each tile (terrain
byte `+0xA` = 0) before the map is read.

**STRONGLY INFERRED:** the map is read between `0x00513923` and `0x00513C42`,
row by row with x inside y (`room-slab-chain.md` §6), through `0x0051EC60`.
`0x0051E0A0` installs that callback only when `[session+0x7239]` bit `0x4` is
set (otherwise `0x0051EB50`/`0x0051EE60`), and which path a new-level load
takes is unchecked. On that reading every command 3 posted during the load is
dropped at the closed gate, and command 4 runs during it.

**HYPOTHESIS:** level-start torches come only from those command-4 refreshes.
Not excluded: other vt`+0xB8` callers, command 3 posted after the gate opens,
the `0x004D6762` room path, and handler 6. How an unloaded tile (terrain 0)
reads to `0x00439B90` was not read, so the winner at an inside corner is open.

## 7. What is still open

| | What | How to close it |
|---|---|---|
| Q446 | Which wall wins a floor tile it shares with another at level start | The gate and its posters are read (§6, Level start). Read how terrain 0 reads to `0x00439B90`, which callback a new-level load installs, and whether `0x004ED1A0` reaches `0x004F0E30`. Oracle: the owner describes an inside corner and a long straight wall on a fresh level |
| — | Command 19's handler `0x00448800` | Read it |
| — | Why the terrain setter also refreshes tiles two steps away | Probably so a torch that loses its wall can move to the next eligible wall; read the setter's loop |
| — | The meaning of light flag bits `0x2` and `0x40` in `0x4052` | Engine-side; matters only for matching the lighting |
| — | The loader-side gate dwords at intermediate `+0x64..+0x78` (§4) | Read the room loader's caller; a level override is the likely reading |
| — | `0x004559B0`, called three times in the refresh | Not read; probably the tile's room lookup |

## 8. How this was checked

A second pass, in its own Ghidra runs, re-read the following:
- the vtable slot and the source string;
- all six balance-variable readers, which are `0x00439B90`, plus `0x00448710`
  for colour;
- the direction, sub-tile, facing and command tables;
- `0x00452D80` and both parity helpers, instruction by instruction;
- the room loader span `0x004EBCC2..0x004EBD90`;
- the solid-tile removal loop and the terrain-art gate;
- the `0x146` allocation, the `0x2780` clamp, the yaw shift and mask;
- every call inside `0x00439B90` (no synced seed);
- the command-3 and command-4 handlers, all 62 post sites and their ids;
- the callers of `0x0044FC40` and `0x004F0E30`.

The shipped room values were read from the file.

**Taken from the first pass only:**
- the solid bit behind `CWorld` vt`+0x208`;
- the door nibble behind vt`+0x21C` (`0x004D1A20`);
- the writer of terrain `+0x1CB`;
- the room art flag byte;
- the bridge seed `CBridge+0x72`;
- the body of `0x004F0E30`.
