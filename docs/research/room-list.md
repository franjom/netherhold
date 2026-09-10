# The player's room list: where DK2 keeps what the player built

| | |
|---|---|
| Source(s) | `DKII.EXE` v1.70 — the per-turn gold resync `0x004B9F62`, the room dumper `0x004E70B6`, the room conditions at `0x004C7EB3` / `0x004C7F8E` |
| Observed behaviour | Not yet sampled from the running game |
| Confidence | **STRONGLY INFERRED** — the walk is read directly, but nothing has been read out of a live process yet |
| Implementation | `RoomLog` replays a captured log; `RoomInstance` is our model |
| Tests | `RoomLogTests` |
| Open questions | Q289 — whether `FirstSlab`/`LastSlab` bound the room |

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

## What a room knows

From DK2's own dumper (`room-object.md`), the fields a recording needs:

| | |
|---|---|
| `+0x04` | `Room TagId` — the room's identity, stable across a session |
| `+0x18` | `TypeId` |
| `+0x1D` | `PlayerId` |
| `+0x27` | `CentrePos (x, y)`, two words |
| `+0x2D`, `+0x2F` | `FirstSlab (x, y)`, `LastSlab (x, y)`, byte pairs |
| `+0xA7` | `Size` — the slab count (STRONGLY INFERRED, `room-conditions.md`) |

**A room does not know its tiles.** There is no slab list on the object, and
the dumper — which prints 122 fields — names none.

## Why that is enough, and where it is not

The three player room conditions read `TypeId`, `Size` and `TotalCapacity`
only (`room-conditions.md`), never a tile. So for the measurement `capture-diff`
actually runs, a recording of type, owner and slab count is complete.

It is **not** enough to place the room. `RoomLog` therefore replays a placement
only when the bounds enclose exactly `Size` slabs, and counts the rest rather
than filling the bounding box: an L-shaped room has the bounds of the rectangle
that would contain it, and putting tiles where DK2 had none makes every later
divergence unattributable.

Whether `FirstSlab`/`LastSlab` are a bounding box at all is Q289. The first
capture answers it: if the area equals `Size` for rooms placed as one drag,
they bound; if not, they are the ends of some list.

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
wall,tick,player,event,room,type,x,y,size,x0,y0,x1,y1
```

`event` is `build`, `grow`, `shrink` or `gone`. A room object appears when the
player places it and **grows** when they place next to it, so one tag id
changing size is one room and not two.

**Unverified until it runs**, and the tick column will be one late as every
sampled column is (ADR-0119).
