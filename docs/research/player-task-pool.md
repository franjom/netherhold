# The player task pool: where DK2 keeps what the player asked for

| | |
|---|---|
| Source(s) | `DKII.EXE` v1.70 — the area condition 62 counter `0x004B0960`, the per-coordinate lookup `0x00508290`, the total at `0x00508300`, and `FUN_00512D20` |
| Observed behaviour | Not yet sampled from the running game |
| Confidence | **STRONGLY INFERRED** — the layout is read directly, but nothing has been read out of a live process yet |
| Implementation | `TileState.IsTaggedBy` / `TileTagSystem` model the tagged set; nothing reads DK2's pool |
| Tests | `TriggerAreaConditions` type 62 (`Tagged`) |
| Open questions | Q286 — what kinds 3–15 are |

## Why this matters more than a tile flag

**A tagged tile is not a bit on the tile.** DK2 keeps a per-player pool of task
entries, one per marked tile, and area condition 62 answers by asking that pool
about each coordinate in its rectangle. So the set of tiles a player has marked
is a *list in memory*, and diffing it between two turns yields the player's
own dig and claim commands at turn resolution — the semantic level a replay
needs, with no camera or hit-testing to reconstruct.

That makes it the recording side of the input channel M5 needs (ADR-0127).

## Finding the pool

The counter behind condition 62 computes the address in the open:

```
playerId * 4389, then  pool = *(u32*)(world[+0x84] + playerId*4389 - 0x20)
```

`world+0x84` is the player array and **4389** (`0x1125`) is its stride, both
already established. The `-0x20` is the same field the writer names directly as
**`player+0x1105`** — `0x1125 - 0x20` — so the two sides agree and the array
base sits one record early.

## The layout

The pool holds **fifteen kind lists**, walked as `kind = 1 … 15`:

| | |
|---|---|
| `pool + 0x4678 + kind*2` | `u16`, zero when that kind's list is empty |
| `pool + 0x4656 + kind*2` | `u16`, the head entry index for that kind |
| `pool + index*36` | an entry, **36 bytes** |
| entry `+0x00`, `+0x02` | `x:u16`, `y:u16` |
| entry `+0x1F` | `u16`, the next index; **0 ends the list** |

Entry index 0 therefore never holds a live entry. The next-index field is
*unaligned* inside the entry, which is unusual enough to be worth restating:
`0x00508290` reads it as `MOV DX, word ptr [EDX + 0x1F]`.

**Kinds 1 and 2 are the tagged tiles**, split only by whether the terrain holds
gold; condition 62 counts both, which is why one tag bit on our side is the
same quantity. What kinds 3–15 hold is unread (Q286) — the pool is a general
task pool and tagging is only its first two lists.

A second accessor at `0x00508300` sums `+0x469A` (a byte) with the words at
`+0x4650`, `+0x4652` and `+0x4654`, so those four are counts of something the
pool tracks apart from the per-kind heads. `FUN_00512D20` compares `+0x4650`
against **20** and takes an overflow path above it.

## Reading it live

`reverse-engineering/dk2watch.py --tags <out.csv>` walks kinds 1 and 2 for
players 1–7 each sample and writes one row per change:

```
wall,tick,player,event,kind,x,y
```

`event` is `tag` or `untag`. That is a command log, not a state dump, so it
lines up with `GameSimulation.Tick(in InputFrame)` on our side.

**Unverified until it runs.** The walk guards against a null pool and caps its
iteration, but no value has been read out of DK2 yet; the first capture is also
the first test of this layout. Expect the tick column to be one late, as every
sampled column is (ADR-0119).
