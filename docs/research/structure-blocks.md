# The hole in a portal, and the heart on its platform

Source(s):
- The owner, 2026-09-07, answering Q88
- The owner, 2026-09-13, distinguishing portal disposal from Imp disposal
- `DKII.EXE` v1.70 (GOG), read with Ghidra 11.0.1 on 2026-09-07
- The reference install's `Objects.kwd`, via `DK2.Data`

Original version: DK2 v1.70 (GOG)

Questions: **Q88 and Q209 (closed)**, Q137 (a clause confirmed), Q52
(reused as evidence).

---

## 1. What the owner said, and it was three answers

Asked whether a portal's centre tile is walkable (owner, 2026-09-07):

> *"no it is not walkable, dungeon heart's center is also not walkable. portal's
> center is reserved for creature's 'arriving' animation and for hand of evil to
> throw creatures in, removing them from dungeon"*

1. A portal's centre is not walkable.
2. A dungeon heart's centre is not walkable either — which nobody had asked.
3. **Dropping a non-Imp creature into a portal's centre removes it from the
   dungeon.** Imps are refused there and discarded through the Heart instead.

None of the three was reachable from the shipped data.

## 2. The two shapes, and why they are different sizes

| Room | Blocked | Why that many |
|---|---|---|
| **Portal** | the **single** centre tile | It is the tile `PortalRules.EntranceTileIndex` already spawns arrivals on (ADR-0042). The owner's answer names that as the arriving animation, so the tile was already special. |
| **Dungeon Heart** | the centre **3x3** | Q52, and the evidence has nothing to do with walking. |

**The heart's nine is borrowed evidence.** A keeper with no treasury tops out at
**16000** gold — CONFIRMED by the owner off the game's own counter — against a
`MaxGoldPerDungeonHeartTile` of **1000** (`game-database-dump.md`). So **16** of
a heart's 25 tiles store gold and the other **9** do not, because the heart's
structure is standing on them. A tile that cannot hold gold because there is a
heart on it is not a tile a creature can stand on either.

`RoomInstance.IsCentrePlatform` is the same predicate the gold rules use
(`GoldStore`), so "16 storable tiles" and "16 walkable tiles" are one fact and
cannot drift apart.

### 2.1 A room with no middle has no structure

Both cases refuse a room narrower than 3 in either direction. For the heart that
is `IsCentrePlatform`'s own guard, and it keeps a half-destroyed heart from
trapping the creatures standing in it. For the portal it was added after a test
failed: **the first version of this change walled off a deserter**, because the
anger tests paint a **1x1** portal as a convenience and blocking its only tile
left nothing to walk to. A portal below 3x3 does not exist in DK2 — so the guard
is right on its own terms and the test failure was the messenger.

## 3. Arriving on an unwalkable tile works, and by luck

A creature is spawned **on** the hole and has to walk off it. That needs the tile
to be a legal place to stand and an illegal place to walk *through*, which
sounds like it needs a special case and does not:

- **`GridPathfinder` tests the destination and every neighbour it expands, and
  never the start.** So a creature standing on the hole routes off it while
  nothing can route onto it.
- **`CreatureMovementSystem.KeepOffTheCorner`** already nudges a creature that
  finds itself somewhere unwalkable toward its next waypoint.

Both were written for other reasons — the second for diagonal corner-cutting —
so the behaviour is asserted by a test rather than left to that coincidence.

## 4. The Hand had to be asked in the right order

`HandSystem` refuses to drop a creature on an unwalkable tile, so making the
hole unwalkable made it undroppable — the opposite of what the tile is for.

**There are two unwalkable disposal targets:** the portal hole for non-Imps and
the Dungeon Heart for Imps (owner, 2026-09-13). **Built 2026-09-15 as ADR-0148**,
in `DisposalRules`, asked before the walkability refusal. The two are **exclusive**:
a worker is refused the hole, and anything else is refused the platform. Neither
bypasses ownership, so a rival's heart is refused the way a rival's portal was.

**All of it seen, 2026-09-15** (owner, two screenshots; Q312 closed). It is the
heart's **single centre tile** that accepts an imp — not the raised 3x3, whose other
eight refuse a drop like any unwalkable tile. The imp's **gold vanishes with it**.
A **goblin is refused** the heart outright, so the two tiles are exclusive both ways.
And a dungeon heart is **always 5x5**, so the size guard is defensive, not a case.

### 4.1 The gold goes with it, and the owner already said so

Q137 was answered on 2026-09-07 too: *"a creature leaving (being thrown) through
portal does not drop its gold"*. This applies to ordinary creatures, including
converted goodly creatures. A creature posted through the hole takes its gold, and removal reuses
the same `creature_left` event a deserter gets, with `via=thrown_in` instead of
`via=portal`. Same thing happening for a different reason.

### 4.2 A captive is refused the tile

An **enemy prisoner cannot be sent through a portal** (owner, 2026-09-13).
`HandSystem.Drop` already refuses it, leaving the creature in the hand and still
imprisoned. This is CONFIRMED, not the former conservative UNKNOWN fallback.

## 5. Objects collide continuously; the navigation cache is separate (Q209)

DK2 has no room-level walkability override and does not turn furniture into a
blocked tile. Its object table instead drives a continuous obstacle system:

| Fact | Evidence |
|---|---|
| `objDef+0xF4` is the runtime flags word | `0x004D12B0` reads it |
| `objDef+0xF8` is a 4096-fixed-point size used as `size >> 1` | same function; file offset `0x32C` from loader `0x0049DD60` |
| shipped diameters include Dungeon Heart Obstacle 12288, Dungeon Heart 4096, most beds 2048, Work Bench 3276 | `kwd objecthex 32C 2 -w` |
| 15 objects carry `SolidObstacle`; all are room furniture | `kwd objects` |
| 58 objects carry `Obstacle`, including every creature bed and gold pile | `kwd objects` |
| `FirstPersonObstacle` counts only while mode global `0x006ECA78` is set | `0x004D12B0` |
| object 100 is skipped by the per-tile walk and forwarded once through the heart-tile path | `0x004D13EA`, `0x004D1503`–`0x004D15AD` |

`0x004D12B0` walks the objects overlapping a tile rectangle, gates them on
`Obstacle`, and forwards position, size and flags to a callback. Its consumers
operate on continuous geometry: `0x004D1AB0` performs a ray cast,
`0x004D2B80` separates two things using their summed radii, and
`0x004D27F0` tests approximate distance against a query threshold.
`0x004D1170` is a separate ungated object enumerator.

This explains the owner's 2026-09-11 observation that a creature routes around
a bed, bookcase and anvil. `Obstacle && size > 0` is not an impassable-tile
predicate; it is the gate and geometry for object collision. Portal and Dungeon
Heart tile blocking is separate room/terrain geometry (§5.4).

### 5.3 The heart's obstacle is forwarded by the room, not found on its tile (Q355)

The gated enumerator walks the query rectangle's tiles and remembers the last
one whose slab type byte `+0xA` is 14, Dungeon Heart terrain. Its per-tile walk
forwards every `Obstacle` object **except type 100**. After the walk, if a heart
tile was seen, it takes the room's stored centre (`room+0xB43`, set by
`0x004C5F60`), lists that tile's things and forwards the `Dungeon Heart
Obstacle` standing there: its own position, size `width >> 1` = 1.5 tiles, flag
bits 0 **and 1** (`OR AL,3`). So `Repel_Obstacle` records one slide contact per
query whenever the query touches heart terrain, wherever the body stands.
The heart's construction code spawns object 100 at the centre tile
(`0x004DAC87`, `0x004DB154`: position `(tile << 12) + 0x800`).

**Measured (Level 7 capture, 2026-09-17, 119,445 creature samples):** the
nearest-approach histogram peaks in the 1.375–1.625 band around both hearts, and
imp 427 rode that ring for nine ticks before turning in to a job on the platform
(arrival exemption, steering-behaviours.md §2). Imp 428 jittered eight ticks at
the heart's wall, then walked straight across the platform at full speed,
passing 0.64 tiles from the centre: **escape mode does switch the obstacle off
in DK2**, as ours does. **The Level 10 object dump (2026-09-18)** shows what
holds the last half tile: each heart's centre tile carries three objects —
type 100 (flags `0x03014000`, width 12288), the visible `Dungeon Heart` 13
(`0x00234000`, width 4096) and `Arches` 86 (`0x00014000`, width 2457). The
latter two pass the enumerator's per-tile walk as plain shoves of reach 0.5
and 0.3 tiles plus the body's radius. Over 564,231 Level 10 samples the ring
peaks in the same 1.375–1.625 band and the closest approaches are 0.64 and
0.95 tiles; Level 7's were 0.50–0.60.

### 5.4 The navigation cache and all known writers (Q209)

The tile predicates are a family of world virtual methods. They read the
one-byte grid whose descriptor is at `world+0x9F2` (stride `+0x8`, base
`+0x10`) and, in three variants, slab word `+0x8` bit 14. The grid byte is
an enumeration: whole-byte comparisons use 4 and compare neighbouring values,
while bit 3 is the blocked test. Off-map lookup returns 8.

The binary has two central byte setters:

- `0x004D1930` stores only on change and then invalidates the changed one-tile
  rectangle through `0x004CE770`.
- `0x004D1970` performs the same conditional store without notification; it
  is used during bulk map work.

Their caller graph accounts for these producers:

| producer | value written |
|---|---|
| map initialization `0x0044F370` | 8 on every tile |
| terrain update `0x0044FC40` | terrain flags `+0x17B`: bit 0 → 8, else bit 7 → 1, else bit 8 → 3, else 0 |
| door update `0x004A66F0` | owner number in the high nibble, plus low bits 1 and 2 from map/door state; removal clears it to 0 |
| door state change `0x004A6800` | toggles bit 2 in the existing byte |
| room-side wrapper `0x00454FD0` | explicit 3 or 4 from three room geometry/construction paths |

The terrain writer selects the silent setter while world flag
`+0x7239 & 0x40` is set and the notifying setter otherwise. The door paths
always notify. None of these writers reads an object definition, object flags,
or object size, and the door overlays never set bit 3.

**STRONGLY INFERRED:** furniture is not turned into an impassable *tile*.
Q209's premise was wrong. The nav cache holds terrain, door, and a few
room-geometry categories; furniture remains continuous collision geometry.
The object enumerator at `0x004D12B0` gates on `Obstacle`, supplies object
position and radius to its consumers, and the movement-side separation path
works in that continuous space. This accounts for the observed routing around
beds, bookcases and anvils without a furniture write to the tile cache.

The slab word's separate bit 14 path is room/terrain geometry: its low 12 bits
are the room id, four sites clear bit 14, and no independent setter exists.
It is therefore written with the room id and punched out for structures
(STRONGLY INFERRED); this is not an object predicate either.

### 5.2 What this corrects in Q88

The portal centre and Heart platform belong to room/terrain navigation
geometry. Furniture belongs to continuous object collision. A faithful
replacement therefore needs both mechanisms; projecting furniture into the
tile path grid would merge two distinct DK2 behaviours.

## 6. What is built

`StructureBlocks`, consulted by `WorldPathGrid.IsWalkable`, plus
`PortalRules.IsHoleTile` and one branch in `HandSystem.Drop` (ADR-0084). The
missing Heart disposal exception and portal's missing Imp refusal are known gaps.

**The router does not use the block** (2026-09-17). DK2's own navigation
cache, captured from a running Level 7, holds plain floor on every one of a
heart's 25 tiles and byte 4 on a portal's centre, and the cost function
crosses a bit-2 boundary only for a state that passed option bit 2 (§5.4,
navigation-mesh-and-search.md §4). `WorldSteering.ByteOf` follows the cache:
the router walks the heart's platform and enters the hole only for a leaving
creature, while the path grid and the Hand keep the owner's blocks. What keeps
creatures off the platform in play is §5.3's forwarded obstacle — record 100,
size 1.5 tiles, flag bits 0 and 1 — with the visible heart and the arches as
shoves inside it, three obstacles on the centre tile
(`WorldSteering.CollectObstacles`). Off the heart's axis a walker rides
the ring and arrives; aimed at the centre it stalls, and escape mode carries it
across, as Level 7's imp 428 did. The owner's screenshot of 2026-09-17 marks the
last half tile creatures never enter: the visible heart's shove (§5.3).

The mask costs one byte compare per tile plus a flood fill over portal and heart
tiles only. `CreatureMovementSystem` builds a grid per creature per tick, so it
computes the mask once for the tick and passes it in.

**Verified on real geometry**, not only on the fixture: scenario
`creatures-a-portal-fills-a-dungeon-001`
(`scenarios/creatures/a_portal_fills_a_dungeon.yaml`) runs campaign level 1,
which has a genuine 5x5 heart and a genuine 3x3 portal, and imps dig, claim the
portal and four Goblins arrive through it with no route failure and no creature
stuck.

## Confidence

CONFIRMED, and BUILT — both tiles unwalkable; non-Imps, including converted goodly
creatures, leave with their gold through a portal; Imps are refused there and go
through the heart's single centre tile, gold and all, while a non-worker is refused
that tile (owner, 2026-09-07, 2026-09-13 and 2026-09-15; ADR-0084, ADR-0148).
STRONGLY INFERRED — Q209: furniture collision does not write the navigation
tile cache; the binary's writer graph and continuous obstacle consumers are §5.

## Implementation

`DK2.Simulation.StructureBlocks`, `WorldPathGrid.IsWalkable`,
`PortalRules.IsHoleTile`, `HandSystem.Drop`, `RoomInstance.IsCentrePlatform`.

## Tests

`DK2.Simulation.Tests.StructureBlocksTests`, plus the scenario above.
`StructureBlocks` carries `[Dk2Behavior(Confidence.Confirmed,
"docs/research/structure-blocks.md")]`, so the pointer is enforced from the code
side.
