---
systems: [rooms, creatures]
---
# Room pillar obstacle coverage (Q808)

Source: retail `DKII.EXE`, fresh PyGhidra reads and the shipped global
`Data/editor/Objects.kwd`, 2026-11-14. STRONGLY INFERRED from binary/data;
no new running-game observation. The remake census is against hash 515.

## Which generated pillars are obstacles?

Every pillar type selected by the automatic room-pillar pass has file flags
`0x00044000`: `Obstacle` (`0x4000`) and `Pillar` (`0x40000`). The raw file
bytes at record `+0x344` are `00 40 04 00` for all seven:

| Room | Object id |
|---|---:|
| Treasury | 76 |
| Hatchery | 78 |
| Combat Pit | 79 |
| Torture Chamber | 82 |
| Graveyard | 84 |
| Casino | 85 |
| Crypt | 141 |

This is the generated set in [furniture placement §5](furniture-placement.md#5-room-pillars-0x004ef1f0),
not every shipped object named Pillar. Other pillar definitions exist, but
that pass does not select them for Lair, Workshop, Prison or Temple.
Training likewise has no automatically placed room pillars.

The six non-Hatchery types therefore do not escape the native obstacle test
through missing flags. The current virtual predicate only reconstructs 78.
Objects explicitly present in `WorldState.Objects` already pass through the
ordinary flag check; the gap concerns generated pillars absent from that list.

## Caller census and modern coverage

A raw E8/E9 scan of both executable sections (`.text` and `cseg`) finds the
same eleven direct sites as Ghidra's cross-references to `Tile_FirstObstacle`
(`0x00453600`). No absolute occurrence of its address was found in the PE
section data. The table groups those eleven sites, not eleven independent
mechanics:

| Native consumer / call sites | Current coverage of generated room pillars |
|---|---|
| Room walk `0x004E4CE5` | `RoomWalkRules.ArrivalTile` adds virtual 78; other six missing |
| Chicken choice `0x00463647` | `EatingRules` adds virtual 78 for its Hatchery case |
| Owned chicken process 6 `0x004A2046` | `ChickenMovementRules` adds virtual 78 |
| Random plain room tile `0x004E4F53` | `CasinoStep` checks stored objects; generated Casino 85 missing |
| Wander point `0x00480B05`, `0x00480CE9`, `0x00480E3D` | `ImpTaskSystem.Wander` checks stored objects; no virtual room pillars |
| Loose movement process 10 `0x004A28D6` | Separate virtual Heart-pillar/store-gold checks exist, but none of these seven room pillars |
| Recuperation point `0x00461CF8`, `0x00461E20` | State 266 is unbuilt |
| Disturbed Training `0x00485A26` | State 109 is unbuilt; ordinary Training has no generated pillars |

The predicate gaps do not prove that every room or caller currently produces
a differing endpoint: prior floor-slot, wall-slot, navigation and room filters
can reject a candidate first. This census establishes missing coverage, not
measured outcome or RNG divergence for the six additional rooms.

## Previously unread consumers

`0x00461B50` is state **266, recuperating**. Its guard `0x0048F160` compares
current health for exact equality with the level-scaled maximum. At full
health the handler returns 0. Otherwise it calls `0x00461BB0` with radius 10
and, on success, requests travel resuming 266 with options `0x80`.
The point helper requires own land, a valid non-blocked tile, and no obstacle
at both of its acceptance sites. Its perimeter fallback quirks are not ported
by this research. A decompiler's apparent null output pointer there is a stack
artifact: the instructions reload the output argument at `[ESP+0x34]`.

Training state **109**, `0x004859E0`, first runs the existing intruder-watch
routine (which also charges AngerNoWork). When that finishes, it requests a
point from its current room with arguments `(1, 0)` and tests that point's
tile for an obstacle. Clear: travel resuming 108 with options 0. Obstructed:
set 108 immediately. There is no obstacle retry loop in this handler.

## What remains unbuilt

Q808's flags and caller questions are answered. General generated-room-pillar
coverage, real pillar lifetime/movement-bit writes, and the two unbuilt state
handlers remain implementation work. The Hatchery steering model and physical
collision geometry are separate from this tile-level endpoint predicate.
This change implements none of those gameplay extensions.

The reproducible flags/call census and input hashes are retained in
`F:/DK2Evidence/studies/q808-pillar-obstacles--2026-11-14--001`.
