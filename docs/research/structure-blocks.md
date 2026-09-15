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

What is assumed rather than seen, all of it Q312: **which** of the heart's nine
platform tiles DK2 accepts (all nine are taken here), whether an imp's **gold** goes
with it as a portal's victim's does, and whether the exclusivity is real or whether
a heart also swallows a goblin.

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
| shipped diameters include Heart Obstacle 4096, most beds 2048, Work Bench 3276 | `kwd objecthex 32C 2 -w` |
| 15 objects carry `SolidObstacle`; all are room furniture | `kwd objects` |
| 58 objects carry `Obstacle`, including every creature bed and gold pile | `kwd objects` |
| `FirstPersonObstacle` counts only while mode global `0x006ECA78` is set | `0x004D12B0` |

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

CONFIRMED — both tiles unwalkable; non-Imps, including converted goodly creatures,
leave with their gold through a portal; Imps are refused there and discarded via
the Heart (owner, 2026-09-07 and 2026-09-13; ADR-0084).
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
