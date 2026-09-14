# The hole in a portal, and the heart on its platform

Source(s):
- The owner, 2026-09-07, answering Q88
- The owner, 2026-09-13, distinguishing portal disposal from Imp disposal
- `DKII.EXE` v1.70 (GOG), read with Ghidra 11.0.1 on 2026-09-07
- The reference install's `Objects.kwd`, via `DK2.Data`

Original version: DK2 v1.70 (GOG)

Questions: **Q88 (closed)**, **Q209 (new)**, Q137 (a clause confirmed), Q52
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
the Dungeon Heart for Imps (owner, 2026-09-13). The current implementation only
special-cases the portal and therefore gets both sides of the Imp rule wrong.

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

## 5. The original's mechanism is NOT this, and is UNREAD (Q209)

**DK2 has no room-level walkability override.** It has an **obstacle** system
driven by the object table, and the object table says so out loud: object
**100 is named "Dungeon Heart Obstacle"**. What was established:

| Fact | Where |
|---|---|
| `objDef + 0xf4` is the runtime **flags** word | `FUN_004D12B0` reads it |
| `objDef + 0xf8` is a **size in 4096 fixed point**, used as `size >> 1` — a radius | same |
| that size's **file offset is `0x32C`** | the loader `FUN_0049DD60` stores `objDef+0xf8` from frame `EBP+0x3AC`; flags come from `EBP+0x3C4` against a known file offset of `0x344`, so the frame-to-record delta is `0x80` |
| the values at `0x32C` are diameters: Dungeon Heart **4096** (one tile), most beds **2048**, Work Bench **3276** | `kwd objecthex 32C 2 -w` |
| **15 objects carry `SolidObstacle`** (bit 25) and every one is room furniture | training dummy, anvil, work bench, book case, four tombstones, roulette wheel and table, bar table, barrel, steel bucket, guard post table, Dungeon Heart Obstacle — `kwd objects` |
| **58 objects carry `Obstacle`** (bit 14) | including every creature bed and every gold pile — `kwd objects` |
| `FirstPersonObstacle` (bit 24) counts only when a global is set | `DAT_006ECA78` |

The flag bits are `DK2.Data.Model.ObjectFlags`; `dk2tools kwd objects`
reproduces both counts and the per-object flag lists.

**And that is why the dig stopped rather than finished.** `FUN_004D12B0` is not
tile pathing. It walks a tile rectangle, enumerates obstacles to a **callback**
as `{x, y, size, flags}`, gates on `Obstacle` — which 58 objects carry, including
things creatures demonstrably stand on — and consults `FirstPersonObstacle`
behind a mode flag. It is a **collision enumerator**, plausibly for first-person
movement and boulders.

**Two candidate rules were considered and both are refuted by the shipped data:**

- `Obstacle && size > 0` would block **every creature bed** — which the owner
  confirms on 2026-09-11 is what DK2 does: a creature **walks around** a bed, a
  bookcase and an anvil. So this rule is **no longer refuted**, and the
  assumption that sank it (that a creature paths onto its own bed) was ours.
- `SolidObstacle` alone would **not** block a portal's centre: the **Portal
  Crystal (object 99) carries `Obstacle` without `SolidObstacle`**.

So the honest position is: **DK2 blocks these tiles with objects, the object
flags are mapped, and the predicate that turns an object into an impassable tile
has not been found.** Q209.

### 5.1 Pathfinder-side timebox (2026-09-07)

**MOVED, not closed.** Starting from the navigation system found a second object
enumerator at `0x004D1170`, distinct from the already-mapped collision enumerator
at `0x004D12B0`. Navigation initialisation registers four world callbacks:

- `0x004D1170` walks the tiles overlapped by a query radius, follows each tile's
  thing list, and forwards **every object** (thing type 0) to a supplied callback.
  It performs no object-definition flag test itself.
- `0x004D12B0` remains the flags-and-radius collision enumerator already ruled
  out as the tile predicate.
- `0x004D15E0` walks the same object lists and sums one signed field from each
  object instance.
- `0x004D16B0` returns the navigation byte for one world tile, or 8 outside the
  map.

This narrows the missing predicate to the **consumer callback downstream of
`0x004D1170`**, not the world scan and not the navigation grid itself. The
pathfinder receives raw object instances and decides later which ones affect a
route. The timebox ended while tracing that indirect callback through the
36-byte path-following object; no comparison against object id, flags, room
role, size, or state was established. The next probe is precise: recover the
callback passed through the first registered navigation slot and enumerate its
object-definition and object-instance field reads.

### 5.3 The four slots, walked (2026-09-10)

§5.1's probe was "recover the callback passed through the first registered
navigation slot". Done, and it moves the question rather than answering it.

**Navigation registers four callbacks, not three.** One function stores each
enumerator into a global with a context pointer beside it:

| slot | enumerator | context | consumers |
|---|---|---|---|
| `0x006ED3FC` | `0x004D1170` | `0x006ED40C` | 2 |
| `0x006ED400` | `0x004D12B0` | `0x006ED410` | 4 |
| `0x006ED404` | `0x004D15E0` | `0x006ED414` | 2 |
| `0x006ED408` | **`0x004D16B0`** | `0x006ED418` | 13 |

The fourth was not recorded before. It is much the busiest and has not been read.

**Both consumers of the first slot are ruled out.** Neither tests a tile:

- `0x004D1AB0` is a **ray cast**: it sums the two things' `+0x200` radii, runs a
  segment test on their `+0x1A8` positions and keeps the nearest hit in a global
  seeded to `0x7FFFFFFF`. Its caller sets the `FirstPersonObstacle` global
  `0x006ECA78` around the call and clears it after, so this is the first-person
  path the flag bit was always about.
- `0x004D2B80` is **thing-versus-thing separation**: `+0x84` positions, `+0xDC`
  radii summed, a `+0x10E` bit-7 skip, and a divide against the summed radius.

**The obstacle enumerator's answer is a distance, not a flag.** Of its four
consumers, `0x004D27F0` is the one that answers a yes/no: the caller seeds
`0x006ED37C`, asks over a 1.2-tile radius, and reads the byte back. The callback
takes `(thing, context)`, deltas the thing's `+0x84` against the query point,
approximates the length as `max + min/2`, compares it with a threshold at
`context+0x8`, and sets the byte when it is within.

**So the shape of the mechanism is against a per-tile predicate.** Every object
field any of these four consumers reads is an **instance** field — `+0x84`,
`+0xDC`, `+0x1A8`, `+0x200`, `+0x10E`. Not one reads the object *definition*, so
neither `objDef+0xF4` flags nor `objDef+0xF8` size reaches a consumer: the flag
gate is inside the enumerator and the geometry is a **continuous radius**.

That would explain why both candidate predicates in §5 are refuted by the data.
A creature is pushed out of a circle rather than refused a tile, so the Dungeon
Heart Obstacle's 4096 diameter covers its tile and a bed's 2048 does not cover
its own — no flag combination has to separate them, and none does.

**HYPOTHESIS, not a finding.** It is consistent with everything read here and
with the shipped diameters, and it is not established: the pathfinder's own tile
test has still not been located, and a grid router needs one. The next probe is
the fourth slot `0x004D16B0` and its thirteen consumers, which is where a router
would sit if one of these four is it (Q209).

### 5.4 The tile predicate exists, and it reads no object (2026-09-11)

§5.3's next probe was the fourth slot. Done — and it **refutes §5.3's
hypothesis**. There is a per-tile walkability test, it is a family of world
virtual methods, and not one of them consults an object.

**Slot 4 is a grid lookup, not an enumerator.** `0x004D16B0` takes `(x, y,
world)`, asks the world's own bounds test through vtable slot `+0x1FC`, and on
success returns one **byte** out of a grid: descriptor at `world+0x9F2`, row
stride at `desc+0x8`, base at `desc+0x10`, one byte per tile. **Outside the map
it returns 8.** Sixteen call sites go through the slot, not thirteen —
§5.3 counted references, and one of the seventeen is the registration store.

**The world's vtable carries a family of predicates over that byte.** None is
ever called directly: each appears only as an `.rdata` vtable entry.

| address | takes | answers |
|---|---|---|
| `0x00509200` | tile `x, y` | `(nav >> 3) & 1` |
| `0x005090C0` | a fixed-point position | the same bit, `SAR 0xC` first |
| `0x00509160` | tile `x, y` | `nav & 8` **or** slab word `+0x8` bit 14 |
| `0x00509100` | a fixed-point position | the same pair |
| `0x005091C0` | a slab pointer | bit 0 of `+0x17B` in the slab's **terrain** record **or** that same bit 14 |

**So the slab's flag word at `+0x8` is `id:12 | flags:4`.** The placement scan at
`0x004FE677` masks it with `0xFFF` and compares that against a field of the thing
being placed, and three of the predicates test **bit 14** of the same word.
Q309's `0x2000` is bit 13 — one bit below a bit that gates movement. **Which
id the low 12 bits hold is not established**: the slab's *terrain* id is the
separate byte at `+0x0A` (`runtime-map.md`), and `+0x02`'s low 12 bits index a
third table at `0x006EE540`.

**The nav byte is an enumeration, not a bitfield.** `0x004B228F` compares the
whole byte against **4**; `0x00459944` reads **two** tiles' bytes and compares
them for equality; `0x0048ECC5` hands a pair of them to one function. A plain
blocked/clear flag would need none of that. Bit 3 is the blocked bit inside it,
and off-map answering 8 is consistent with that reading.

**And no object reaches any of it.** These predicates read the nav grid and the
slab; nothing here touches an object instance or an object definition. So the
link Q209 asks about cannot be a query-time test — it has to be a **write**:
placing an object updates the nav byte or the slab bit. The grid is maintained
through the descriptor rather than through the world, because **all 16
references to `world+0x9F2` in both code blocks are reads** — `StoreAt`
finds no store and a mask scan agrees. The next probe is that descriptor's own
class, and whether object placement calls into it.

### 5.5 The link is a cached bit, written when a thing is placed (2026-09-11)

§5.4 left the question "who writes it". The bit-14 side answers, and it
answers in a way that dissolves §5.3's puzzle.

**A mask scan for a word-sized clear of bit 14 returns five sites in the whole
executable, and four of them target `+0x8`** — the slab flag word. There is
**no** matching set: no instruction anywhere ORs an immediate `0x4000` into a
word. So the bit is cleared by bit-ops and set by a computed or whole-word
write, which a constant scan cannot see. **Which way round the bit runs is
therefore still UNKNOWN**, and nothing below depends on it.

| function | what it does |
|---|---|
| `FUN_004DAE40` | asserts from **`DungeonHeart.cpp`** — its `__FILE__` is inside the function |
| `FUN_004EF1F0` | takes a **room**, reads its tile at `+0x2D`/`+0x2E`, **saves the old bit**, clears it, then probes two neighbouring slabs |
| `FUN_004F09B0` | walks a **rectangle** of slabs through the world's slab-at-`(x,y)` virtual `+0x1D8`, clearing as it goes, then makes a border pass one tile out |
| `FUN_004F0F90` | takes a **room**, same `+0x2D`/`+0x2E` tile, clears the bit, then notifies through `world+0x8` |

All four sit in the room-file block — `DungeonHeart.cpp` around `0x004DAxxx`,
below `Workshop.cpp` at `0x004F4F9F` (`source-file-map.md`). `FUN_004EF1F0` has
five callers, one of them `FUN_0050B5B0` at `0x0050BC95` — the level loader
(`World.cpp`), which is where ADR-0102 read the trigger timer scaling.

**Two of the four take a room, not an object** — corrected the same day it was
first written the other way. The gate both use, `FUN_004E4300`, reads the
record's **type byte `+0x18`** and admits some of 24 categories, and
`trigger-semantics.md` already names that byte as a **room's type** in the global
thing table `0x006EE540`. Q301 goes further and settles it: the **low 12 bits of
the slab word at `+0x8` are the room on the tile**, looked up in that table.

**So bit 14 shares its word with a room id, which is exactly why §5.6 finds no
setter** — the bit is written when the room id is, in one store.

**And Q209's own question stays open, better posed.** This machinery is
room-keyed, so nothing here says an anvil or a bookcase blocks a tile, and §5's
`Obstacle`/`SolidObstacle` rules gain no support. What it does suggest is that
the portal hole and the heart's 3×3 — `StructureBlocks`' only two cases —
are **room** geometry, and may never have been an object rule at all.

**Corroboration, free.** `FUN_004F0F90` reaches the slab a second way, off
`world+0x9DA`: `+0xC` is the map width and `+0x14` the slab base — the same
`world+0x9E6` and `world+0x9EE` §5.4 read elsewhere, from an independent path.

**Next probe**, in order: the whole-word write to slab `+0x8`, which is where the
set lives; then `FUN_004F0390` and `FUN_004F0840`, the two callers of the
thing-clear, for which things qualify.

### 5.6 Nothing sets bit 14 on its own, which says what it is (2026-09-11)

§5.5's next probe was the set. It does not exist as a bit operation, and the
sweep that establishes that is worth recording because it is exhaustive.

**What was searched.** All of `.text` and `cseg` — `0x00401000` to
`0x004D0000`, `0x004D0000` to `0x004F6000`, `0x004F6000` to `0x00653000`,
disassembled whole and filtered, so this is not a range-limited claim:

| shape | sites | on a slab |
|---|---|---|
| any **word-sized** op with immediate `0x4000` | 0 | — |
| word-sized clear of bit 14 (`AND …,0xBFFF`) | 5 | **4**, all at `+0x8` (§5.5) |
| `OR word ptr […], reg` | 64 | 0 |
| `MOV word ptr [… + reg*1 + 0x8], reg` | 5 | 0 |
| `MOV word ptr [reg + 0x8], reg` | 96 | none of those checked |

The last row is the honest limit: 96 is too many to read, so the ones inside
functions that also read the slab base (`world+0x9EE`) were checked first. Every
one of those writes a **definition** record out of the table at `0x006EE540`,
whose own `+0x8` is a different field — a trap worth naming, because the
offset matches and the meaning does not.

**What that constrains.** The word at `+0x8` is `roomId:12 | flags:4` (§5.5),
so a whole-word write sets the room id and the flags together. Bit 14 having
**no** independent setter and four independent clears means it is **written when
a room claims the slab and punched out afterwards**, not toggled as a state.

**So, STRONGLY INFERRED and not confirmed:** bit 14 is set from the terrain a
slab holds and cleared where a structure opens that slab, which makes it read as
**blocked**, matching the nav byte's bit 3 that the predicates OR it with.

**What would settle it in one observation**, and it is cheap: the memory reader
used for the tick-rate amendment (ADR-0102) can read the word at slab `+0x8` on
a known tile. Compare a rock wall, a plain floor, a claimed room and a Dungeon
Heart centre. If bit 14 is set on the wall and clear on the floor the reading
above is confirmed; if it is the other way round this section is wrong and
§5.5 still stands. `observation-checklist.md` is where it belongs.

### 5.2 Which corrects what Q88 used to say

The old note read: *"Fixing it means teaching the path grid about rooms, which
is a bigger change than the fact justifies today."* Wrong on the mechanism.
When the real predicate is found, the path grid should learn about **objects**,
`StructureBlocks` should be deleted, and the two tiles should come out of the
object table like the anvil and the bookcase do.

The fifteen `SolidObstacle` objects are the workshop's anvil, the library's
bookcases, the casino's tables and the graveyard's tombstones, and every one of
those rooms is coming. So what is built here covers two tiles and no more,
rather than guessing a rule that would silently decide the other thirteen.

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
UNKNOWN — the predicate the original uses (Q209, §5).

## Implementation

`DK2.Simulation.StructureBlocks`, `WorldPathGrid.IsWalkable`,
`PortalRules.IsHoleTile`, `HandSystem.Drop`, `RoomInstance.IsCentrePlatform`.

## Tests

`DK2.Simulation.Tests.StructureBlocksTests`, plus the scenario above.
`StructureBlocks` carries `[Dk2Behavior(Confidence.Confirmed,
"docs/research/structure-blocks.md")]`, so the pointer is enforced from the code
side.
