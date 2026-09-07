# The hole in a portal, and the heart on its platform

Source(s):
- The owner, 2026-09-07, answering Q88
- `DKII.EXE` v1.70 (GOG), read with Ghidra 11.0.1 on 2026-09-07
- The reference install's `Objects.kwd`, via `DK2.Data`

Original version: DK2 v1.70 (GOG)

Questions: **Q88 (closed)**, **Q209 (new)**, Q137 (a clause confirmed), Q52
(reused as evidence).

---

## 1. What the owner said, and it was two answers

Asked whether a portal's centre tile is walkable:

> *"no it is not walkable, dungeon heart's center is also not walkable. portal's
> center is reserved for creature's 'arriving' animation and for hand of evil to
> throw creatures in, removing them from dungeon"*

**Three facts in one sentence, and only the first was the question.**

1. A portal's centre is not walkable.
2. A dungeon heart's centre is not walkable either — which nobody had asked.
3. **Dropping a creature into a portal's centre removes it from the dungeon** —
   a keeper power this project did not have at all.

The second and third are the kind of thing that only comes from having played
the game. Neither was reachable from the data.

## 2. The two shapes, and why they are different sizes

| Room | Blocked | Why that many |
|---|---|---|
| **Portal** | the **single** centre tile | It is the tile `PortalRules.EntranceTileIndex` already spawns arrivals on (ADR-0042). The owner's answer names that as the arriving animation, so the tile was already special. |
| **Dungeon Heart** | the centre **3x3** | Q52, and the evidence has nothing to do with walking. |

**The heart's nine is borrowed evidence and that is what makes it solid.** A
keeper with no treasury tops out at **16000** gold — CONFIRMED by the owner off
the game's own counter — against a `MaxGoldPerDungeonHeartTile` of **1000**. So
**16** of a heart's 25 tiles store gold and the other **9** do not, because the
heart's structure is standing on them. A tile that cannot hold gold because
there is a heart on it is not a tile a creature can stand on either.

`RoomInstance.IsCentrePlatform` is the same predicate the gold rules use, so
"16 storable tiles" and "16 walkable tiles" are one fact and cannot drift apart.

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
and both happen to be exactly what this needs. Asserted by a test rather than
left to luck.

## 4. The Hand had to be asked in the right order

The two halves of the owner's sentence contradict each other if taken in the
wrong order, and they did for about a minute: `HandSystem` refuses to drop a
creature on an unwalkable tile, so making the hole unwalkable made it
undroppable — the exact opposite of what the tile is for.

**So the hole is the one unwalkable tile a keeper may drop onto**, tested before
the walkability refusal rather than after it. Not an exception bolted on: it is
what the tile does.

### 4.1 The gold goes with it, and the owner already said so

Q137 was answered on 2026-09-07 too: *"a creature leaving (being thrown) through
portal does not drop its gold"*. **The parenthesis is this mechanic.** So a
creature posted through the hole takes its gold with it, and the removal reuses
the same `creature_left` event a deserter gets, with `via=thrown_in` instead of
`via=portal`. Same thing happening for a different reason.

### 4.2 A captive is refused the tile

Whether DK2 lets a keeper post an **enemy prisoner** home through its own portal
is **UNKNOWN**. Three options, and two of them invent something:

| Option | Invents |
|---|---|
| eject it | that DK2 allows it |
| free it into the dungeon | that the drop lands, leaving a freed prisoner standing on a tile nothing may walk on |
| **refuse the tile** | nothing — the prisoner stays in the hand, still a prisoner |

The third is what is built. It leaves the prisoner rules (ADR-0064) untouched.

## 5. The original's mechanism is NOT this, and is UNREAD (Q209)

Worth writing down at length, because the dig got a long way and the shape of
what it found is useful even though it did not finish.

**DK2 has no room-level walkability override.** It has an **obstacle** system
driven by the object table, and the object table says so out loud: object
**100 is named "Dungeon Heart Obstacle"**. What was established:

| Fact | Where |
|---|---|
| `objDef + 0xf4` is the runtime **flags** word | `FUN_004D12B0` reads it |
| `objDef + 0xf8` is a **size in 4096 fixed point**, used as `size >> 1` — a radius | same |
| that size's **file offset is `0x32C`** | the loader `FUN_0049DD60` stores `objDef+0xf8` from frame `EBP+0x3AC`; flags come from `EBP+0x3C4` against a known file offset of `0x344`, so the frame-to-record delta is `0x80` |
| the values at `0x32C` are diameters: Dungeon Heart **4096** (one tile), most beds **2048**, Work Bench **3276** | `kwd objecthex 32C 2 -w` |
| **15 objects carry `SolidObstacle`** (bit 25) and every one is room furniture | training dummy, anvil, work bench, book case, four tombstones, roulette wheel and table, bar table, barrel, steel bucket, guard post table, Dungeon Heart Obstacle |
| **58 objects carry `Obstacle`** (bit 14) | including every creature bed and every gold pile |
| `FirstPersonObstacle` (bit 24) counts only when a global is set | `DAT_006ECA78` |

**And that is why the dig stopped rather than finished.** `FUN_004D12B0` is not
tile pathing. It walks a tile rectangle, enumerates obstacles to a **callback**
as `{x, y, size, flags}`, gates on `Obstacle` — which 58 objects carry, including
things creatures demonstrably stand on — and consults `FirstPersonObstacle`
behind a mode flag. It is a **collision enumerator**, plausibly for first-person
movement and boulders.

**Two candidate rules were considered and both are refuted by the shipped data:**

- `Obstacle && size > 0` would block **every creature bed**, and a creature
  sleeps on its bed.
- `SolidObstacle` alone would **not** block a portal's centre: the **Portal
  Crystal (object 99) carries `Obstacle` without `SolidObstacle`**.

So the honest position is: **DK2 blocks these tiles with objects, the object
flags are mapped, and the predicate that turns an object into an impassable tile
has not been found.** Q209.

### 5.1 Which corrects what Q88 used to say

The old note read: *"Fixing it means teaching the path grid about rooms, which
is a bigger change than the fact justifies today."* Wrong on the mechanism.
When the real predicate is found, the path grid should learn about **objects**,
`StructureBlocks` should be deleted, and the two tiles should come out of the
object table like the anvil and the bookcase do.

**Which is also the point.** The fifteen `SolidObstacle` objects are the
workshop's anvil, the library's bookcases, the casino's tables and the
graveyard's tombstones. Every one of those rooms is coming. So the thing built
here deliberately covers two tiles and no more, rather than guessing a rule that
would silently decide the other thirteen.

## 6. What is built

`StructureBlocks`, consulted by `WorldPathGrid.IsWalkable`, plus
`PortalRules.IsHoleTile` and one branch in `HandSystem.Drop` (ADR-0084).

The mask costs one byte compare per tile plus a flood fill over portal and heart
tiles only. `CreatureMovementSystem` builds a grid per creature per tick, so it
computes the mask once for the tick and passes it in.

**Verified on real geometry**, not only on the fixture: the
`a_portal_fills_a_dungeon` scenario runs campaign level 1, which has a genuine
5x5 heart and a genuine 3x3 portal, and imps dig, claim the portal and four
Goblins arrive through it with no route failure and no creature stuck.
