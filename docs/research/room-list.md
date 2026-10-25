# The player's room list: where DK2 keeps what the player built

| | |
|---|---|
| Source(s) | `DKII.EXE` v1.70 — the per-turn gold resync `0x004B9F62`, the room dumper `0x004E70B6`, the room conditions at `0x004C7EB3` / `0x004C7F8E` |
| Observed behaviour | Sampled live, run 10 (level 1, 2026-09-10) |
| Confidence | **CONFIRMED** — the walk was read out of a live process and each room's own `PlayerId` agrees with it |
| Implementation | `RoomLog` replays captures; `RoomLists` retains insertion order for the combat room cache (ADR-0541, hash/save305) |
| Tests | `RoomLogTests`, `RoomListOrderTests` |
| Open questions | none; Q289 and Q291 answered below |

## The walk

The player's per-turn frame recomputes gold by walking this list, and does it
in the open at `0x004B9F62`:

```
idx  = u16 [player + 0x36]                 ; head
room = u32 [0x006EE540 + idx*4]            ; the global object table
while room:
    ... read room fields ...
    idx  = u16 [room + 0x08]
    room = u32 [0x006EE540 + idx*4]
```

A **null pointer** ends it, not a zero index. `room+0x06` and `room+0x08` are
the pair DK2's dumper prints as `PlayerRoomListNode (%d, %d)`, so `+0x06` is
the other direction and `+0x08` is the one the walk follows.

## What the recording reads

`room-object.md` is the canonical field map. `RoomLog` reads the room tag,
type, owner, centre, slab-list ends and size from that layout.

A room does know its tiles indirectly. `FirstSlab` is the head coordinate,
`LastSlab` the tail, and each 16-byte tile record carries the next coordinate
at `+0x04/+0x05`; zero ends the chain. Furniture and removal code walk it. The
live recorder deliberately does not: it uses only the room object, so the two
ends still cannot reconstruct an arbitrary L-shaped room.

## Why that is enough, and where it is not

The three player room conditions read `TypeId`, `Size` and `TotalCapacity`
only (`room-conditions.md`), never a tile. So for the measurement `capture-diff`
actually runs, a recording of type, owner and slab count is complete.

It is **not** enough to place the room. `RoomLog` therefore replays a row only
when the bounds enclose exactly `Size` slabs, and skips the rest rather than
filling the bounding box: an L-shaped room has the bounds of the rectangle that
would contain it, and putting tiles where DK2 had none makes every later
divergence unattributable.

### Q289, answered: they are list ends, and they must be ordered

Run 10's settled lair spans `FirstSlab (8,30)` to `LastSlab (10,26)` —
**descending in y**. They are the ends of a list, not a minimum and a maximum,
so a reader that assumes ordering computes an empty box and drops the room.
Ordered, they span exactly the room: 3×5 for 15 slabs.

Every settled room agrees. Level 1 starts with a Hero Gate 2×2 (`Size` 4), a
Portal 3×3 (9) and a Dungeon Heart 5×5 (25), and each one's ordered bounds
enclose exactly its slab count — which also confirms `Size` is the slab count.

### But not while the room is being built

**Imps build a room slab by slab.** For six consecutive samples the lair's
`Size` ran *ahead* of the box its two ends span: 3 slabs inside a 2-slab box,
then 9 inside 6, then 12 inside 9. The fields are updated together (binary,
2026-10-04, Q291). The box is what misleads, because the two ends are only the
list's first and last tiles. `Room_AddTile` (`0x004EC650`) appends a slab in
one consecutive run (`0x004EC791`–`0x004EC7A0`): it writes the old tail's next
link, sets `LastSlab` to the new tile and increments the word `Size`.
`Room_Absorb` (`0x004ECB70`) moves `LastSlab` and adds `Size` in the same call.
CONFIRMED. While a placement is being finished, the newest slab is the tail and
the first slab is the head. Those two tiles can be close together however many
slabs lie on the chain between them, so the box they span says nothing about
`Size`. In the settled rooms above, the two ends happen to sit at opposite
corners. Nothing read here guarantees that for every room: a later merge or
sale reorders the chain. STRONGLY INFERRED: run 10's rows were not re-derived
tile by tile. A replay
therefore still waits for a room to settle and measures what to place against
what it has already placed. A 15-slab lair then arrives as 1 slab and later
14, rather than as one slab and six refusals. Counting the tiles by walking
the chain (tile `+0x04/+0x05`) would remove the wait.

## Persistent player-list order (hash305)

**STRONGLY INFERRED, binary; captured boundary confirmed.** Re-reading
`004E3800` and `004E5650` confirms that construction and ownership transfer
insert at the owner's list head. Growth keeps the first N/E/S/W neighbour's
record; absorption drops the other record. Removal frees the old record and
replays its surviving slab chain, constructing new pieces. Row-major initial
loading uses the same first-neighbour rule. Sources and slab lifecycle are in
[room-slab-chain.md](room-slab-chain.md).

`RoomListPosition` is a retained ordinal on room tiles; its next counter is
world state. Growth/absorption keep the survivor's ordinal, reconstructed pieces
receive new ordinals, and a captured room receives a new head position.
`RoomCache.Of` reads that order; `RoomIndex.Build` retains its spatial ordering
for other consumers. Defeat snapshots the losing keeper's list, transferring its
eligible rooms in that order. Both fields are hashed and saved in format305.

The [captured withdrawal](decision-parity-audit.md#level4-captured-withdrawal-and-room-order-hash304)
supplies the baseline. Evidence:
`D:/DK2Evidence/room-list-order-implementation-2026-10-25/`.
A saved-native list-transition extraction through header3474 restores the final
surviving constructions and captures through production placement/slab and
room-transfer routines. It reproduces all15 incoming room slots in order,
without replacing the combat cache. Actual withdrawal matches the live return's
ending RNG3162508482, state6, destination(18,48), count14, flags13 and five
buffered route points. It does not compare all14 route points or advance the
complete native world; transient earlier incarnations of reused slot1104 are
excluded from restoration.

Eight focused regressions cover construction, growth, absorption, splitting,
capture plus its follow-up merge, initial merging, defeat and save/hash state.
The original three ordering regressions failed before implementation.
The native transfer itself preserves separate adjacent room records; conversion's
caller immediately merges eligible neighbours (`004C5BE0`/`004EC900`).
Our connected-component room model still cannot retain distinct adjacent rooms
when a transfer has no such merge, including defeat. Other room-list consumers
are not migrated by this change. Campaign-win effects require a new measurement.

Independent review passed110 focused checks and confirmed the production-history
comparison. Final per-project validation totals6556 passed,0 failed,7 capture
skips, including the enabled24-test native-byte oracle result. The evidence root's
`verify_implementation.py` verifies24 sealed artifacts and the original capture hash.
Run the archived `fixture-dlls/Analysis.dll <boundary.json> <new-output.json>
history <native-list-history.json>` to repeat this bounded handler check.

## Rooms are not imp tasks

No writer into the player task pool references the room handler table at
`0x6A1E9F`, across all 27 call sites of the pool's adder. That agrees from the
binary's side with what `imp-task-priority.md` §3 already said from ours — no
imp task kind for room construction — and is why `RoomBuildSystem` building
instantly is a deviation with nothing to model against, rather than a gap.

## Reading it live

`reverse-engineering/dk2watch.py --rooms <out.csv>` walks players 1–7 each
sample and writes one row per change:

```
wall,tick,player,event,room,type,x,y,size,x0,y0,x1,y1,owner
```

`event` is `build`, `grow`, `shrink` or `gone`, and one tag id changing size is
one room rather than two. A room **grows** for two different reasons: the
player places more of the same type beside it, and — the one run 10 showed —
its own imps finish the slabs of a single placement, one at a time.

`owner` is the room's own `PlayerId`, carried so the walk checks itself: the
walk's player index comes from a stride and the field does not. That caught the
first version reading **one player record too far** — the player base is
`playerArray + (id−1)×4389`, because the pool formula's `playerId×4389 − 0x20`
resolves to `player+0x1105`. Run 9 put the human's own dungeon heart under
player 2; run 10, corrected, agrees on every row. `RoomLog` prefers the field
over the index and counts any disagreement.

The tick column is one late, as every sampled column is (ADR-0119).
