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

**One torch per floor tile, and the first wall processed wins it.** The rule
itself gives no floor tile a direction priority. Which wall wins a tile that
faces two walls depends on the order in which the walls were refreshed. At level
start that order is row-major over the walls (§6, Level start), so each floor
takes its torch from the wall on its N side first, then W, then E, then S.

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
| Each floor tile tries N, W, S, E | **No.** Each *wall* tries N, E, S, W; the first wall refreshed takes the floor tile. At level start that works out as N, W, E, S per floor (§6) |
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
to add 255 to red, green and blue. `RoomFlags` now names them `TorchRedHigh`, `TorchGreenHigh`
and `TorchBlueHigh`, and `RoomTorch` folds them in (ADR-0391).

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

The **light** is centred along the shared edge, 512 (1/8 tile) out from the wall
face, at the torch height. Yaw is `((d + 127) << 9) & 0x7FF`, stored at
`+0xE2`. The facing vector points from the torch into the wall.

**Mesh mount: CONFIRMED (binary recheck, 2026-10-11).** `0x00439B90` writes that
vector to light `+0x77/+0x79/+0x7B`, the RenderInfo position offsets. The light
drawer `0x00599960` keeps its illumination at `+0xD6/+0xDA/+0xDE`, then passes
the RenderInfo at `+0x26` to `0x0059BB80`. That helper adds signed offsets
`+0x51/+0x53/+0x55` before drawing the mesh. Its type-5 branch rotates by
`-yaw * pi / 1024`, without the ordinary mesh's half-turn; under our frame,
Unity yaw is positive. Thus the mesh anchor lies on the wall, while the light
stays off it. The classic adapter separates these positions.

**Mesh bend: CONFIRMED (2026-10-11).** The type-5 branch passes the mesh to
`0x00585F90`, which constructs its instance through `0x0057AD20`. That path
transforms each vertex and then samples `0x0058F030`, as for the wall pieces.
Moving only the anchor onto the straight tile edge still leaves a gap against
a bent wall. Classic torches now share the wall lattice for every vertex,
retain rotated unbent normals, and refresh their private meshes after terrain
changes. The illumination position stays separate.

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
file. The art flag is set by the loader for any non-empty art name; the file's art flags are
not read (`0x004EBE58..0x004EBEE4`, re-read 2026-10-06).

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
  only (Q436). The save rebuild calls the level initialiser `0x0050B5B0`
  (`0x0050B48D`), whose wall sweep rebuilds the torches (§6, Level start).
- **Network — INFERRED not sent.** Every client's simulation posts the bridge
  commands locally (§6).
- **Simulation readers — none found.** The only torch code on the simulation
  side is the room light-list bookkeeping (`0x004E67D0`, `0x004E6840`, and
  `0x004E39C0` on room removal). No fog or visibility reader was found. That is
  a negative search: it shows nothing reads torch light, not that light has no
  effect anywhere.

**For this project (built, ADR-0391):** `TorchLayout` in `DK2.Presentation`
keeps the torches, since the winner of a shared floor depends on refresh order;
`ScenePresenter` pushes them through `IRenderTarget`'s torch methods, and
`RoomDef.Torch` carries the five fields and the art switch.

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
| 4 | `0x00447CA0` | Calls vt`+0xB8` for the tile, **gated like 3** (`0x00447CA0..0x00447CA8`, CONFIRMED 2026-10-21; Ghidra's listing starts at `0x00447CAA` and hides the test) |
| 6 | `0x00447CE0` | Gated; calls `0x004484A0` over a rectangle, y outer, x inner |
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

**Answered (binary, 2026-10-21).** Level-start torches come from **one sweep
after the whole map is loaded**. During the load itself no torch is placed.

1. **The gate is closed during the load. CONFIRMED.** The bridge initialiser
   `0x0043AAD0` stores 0 in the gate. The map loader `0x0044E9E0` first calls
   the teardown `0x0044F280`, which posts command 2 when a map already exists.
   Command 2 destroys every light and clears the gate. GameAction_LoadLevel
   `0x00513870` also calls the teardown at `0x00513923`.
2. **Unloaded tiles are Impenetrable Rock. CONFIRMED.** `0x0044F370` sets every
   tile to terrain 1 with nav byte 8 (solid) before any tile record is read.
   Terrain 0 never reaches the terrain table, whose entry 0 is null
   (`0x00455D40`).
3. **The map is read row by row, x inside y. CONFIRMED** (`0x0056D2D0`). Each
   tile goes through `0x0051EC60` and then the setter. The setter's command 4
   and command 3 both hit the closed gate. So does command 6, which
   `0x00451E50` posts for the whole map. `0x0051E0A0` takes its world
   `+0x7239` bit `0x4` branch for any level file; that bit picks the callback
   set.
4. **The sweep. CONFIRMED.** The level initialiser `0x0050B5B0` is called by
   the KWD load (`0x0050E89B`), by GameAction_LoadLevel (`0x00513C42`) and by
   the save rebuild (`0x0050B48D`). Near its end (`0x0050C4B5..0x0050C643`) it
   walks every tile, y outer and x inner. For each tile that passes
   `0x00452D80` (terrain `+0x17B` bit `0x40`, Torch, and world vt`+0x21C(x, y, 0)`
   returning 0; that the slot is the door test is LIKELY, `004528E0`), it calls
   bridge vt`+0xB8` directly (`0x0050C4DC`). That call is not gated. The same loop
   then does other per-tile work (`0x0050C4E2..0x0050C60E`, terrain `0x23`), unread.
   The load's room build does not place torches: `0x0051EC60` →
   `Player_BuildRoomTile` (`0x004C5C30`) → `0x004ED1A0`, whose only bridge posts are
   command 8 (`0x004ED221`), and none of `0x004F0E30`'s four callers is reached
   from it (read 2026-10-21).
5. **Command 7 is posted afterwards** (`0x00513C94`, `0x0052574D`,
   `0x0050B51B`) and places nothing.

**The rule.** Each Torch wall, in row-major order, lights each free face in the
order N, E, S, W. Seen from a floor tile, the first claim comes from the wall to
its N (an earlier row), then W, then E (the same row), then S (the next row).

**Room walls. STRONGLY INFERRED harmless.** `0x004F0E30` also calls vt`+0xB8`
directly, on the Torch walls beside a room's slabs. Its callers are the build
and remove closers, `0x004ECDB0` and Casino_RoomUpdate (`0x004D6762`). Whether
the load's room build (`0x0051EC60`'s call to Player_BuildRoomTile) reaches it
was not traced. During a load the
floor beyond row y is still solid rock, so it could only win tiles that are
already loaded.

**Level 1, the Lord's hall: predicted, then checked against the owner's DK2
frames (2026-10-21).** Floor ← wall (the wall's side of the floor):

| Floor | DK2 | Ours before | Why |
|---|---|---|---|
| (15,6) | (15,5) N | same | |
| (16,6) | (17,6) E | same | |
| (17,8) | (17,7) N | same | (17,7) in row 7 beats (18,8) in row 8 |
| (14,8) | (13,8) W | same | (14,7) fails parity (x even) |
| (14,10) | (13,10) W | same | |
| (17,10) | **(18,10) E** | (17,11) S | (18,10) in row 10 beats (17,11) in row 11 |
| (15,11) | none | none | E/W fail parity (y odd); (15,12) is Rock (terrain 2, flags `0x00840009`), which has no Torch flag; Impenetrable Rock (terrain 1) has it |

Ours took (17,10) from (17,11) because `TorchLayout` reached floor (17,9)
first, and its two-step refresh south lit (17,11) before any wall in row 10.
**BUILT 2026-10-21:** `TorchLayout`'s level start is now this row-major wall sweep
(`TorchLayoutTests.TheLordsHallTorchesMatchDk2`); the setter's refreshes remain for
terrain changes during play.
The owner's frames show (17,8), (17,10), (14,10) and (15,11) as in the DK2
column ((15,6) with medium confidence).

## 7. What is still open

| | What | How to close it |
|---|---|---|
| Q446 | Answered (§6, Level start): a row-major sweep of the walls in `0x0050B5B0`; the load's room build places none | Built in `TorchLayout` (2026-10-21) |
| — | Command 19's handler `0x00448800` | Read it |
| — | Why the terrain setter also refreshes tiles two steps away | Read 2026-10-06: the loop is (±2, 0), (0, ±2), each gated by world vt`+0x1FC` (unread, probably a bounds test) and `0x00452D80` (`0x00450125..0x004501B3`); why is still a guess |
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
