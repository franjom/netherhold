# The player task pool: where DK2 keeps what the player asked for

| | |
|---|---|
| Source(s) | `DKII.EXE` v1.70 — the area condition 62 counter `0x004B0960`, the per-coordinate lookup `0x00508290`, the total at `0x00508300`, and `FUN_00512D20` |
| Observed behaviour | Not yet sampled from the running game |
| Confidence | **STRONGLY INFERRED** — the layout is read directly, but nothing has been read out of a live process yet |
| Implementation | `TileState.IsTaggedBy` / `TileTagSystem` model the tagged set; `InputLog` replays a captured log |
| Tests | `TriggerAreaConditions` type 62 (`Tagged`), `InputLogTests` |
| Open questions | Q286 — what the kinds name; Q287 — whether entry coordinates are tile coordinates; Q288 — what kind 11 is |

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
| `pool + index*36` | an entry, **36 bytes**; there are **500** |

An entry, from DK2's own dumper at `0x005083C5` (`debug-dumpers.md`), whose
printed offsets are four low because its loop pairs them against a cursor at
`record + 4`:

| | |
|---|---|
| `+0x00`, `+0x02` | `x:u16`, `y:u16` — `Position` |
| `+0x04` | `Type:u8` |
| `+0x05` | `Worker[12]`, twelve `u16` |
| `+0x1D` | `Data:u16` |
| `+0x1F` | `Next:u16`; **0 ends the list** |
| `+0x21` | `Id:u16` |
| `+0x23` | `Allocated:u8` |

Entry index 0 therefore never holds a live entry. `Next` is *unaligned*, which
is unusual enough to be worth restating: `0x00508290` reads it as `MOV DX, word
ptr [EDX + 0x1F]`. The corrected offset is what makes the dumper and that
walker agree — before the correction they named different fields.

**Marking a tile files it under one of three kinds, not two.** The routine at
`0x00512D20` has three arms and pushes **11**, **2** and **1**; the first is
taken when a tile flag byte `+0x0B` bit 0 is set, the other two when the
terrain's `+0x17B` bit 3 is. Area condition 62 counts 1 and 2, which is where
the earlier "split by gold" reading came from — it is the split between those
two, not the whole story. What kind 11 is remains unread (Q288).

The **adder** is `0x00507BB0`, taking `(kind, x, y)`, and the **remover** is
`0x00507EF0`; between them they own the head and live-count arrays. Their 27
call sites push kinds 1, 2, 3, 6, 7, 8, 9, 10, 11, 12, 13, 14 and **16** —
which the coordinate lookup, walking only 1…15, never checks. Q286 is now
about which activity each names rather than whether they exist.

> A caution on method: a window of instructions before a `CALL` mis-reads the
> kind wherever several branches `JMP` to one shared call site, as these three
> tag arms do. Read the branches, not the call.

A second accessor at `0x00508300` sums `+0x469A` (a byte) with the words at
`+0x4650`, `+0x4652` and `+0x4654` — `NumberOfFreeTasks`, `NextUniqueId` and
`FreeListHead` by the dumper's names. `FUN_00512D20` compares `+0x4650` against
**20** and marks through the pool only when it is above that.

## Reading it live

`reverse-engineering/dk2watch.py --tags <out.csv>` walks **all fifteen** kinds
for players 1–7 each sample and writes one row per change. It records every
kind because that is how the unread ones get read; `InputLog` then replays only
the tag kinds, and counts the rest:

```
wall,tick,player,event,kind,x,y
```

`event` is `tag` or `untag`. That is a command log, not a state dump, so it
lines up with `GameSimulation.Tick(in InputFrame)` on our side.

**Unverified until it runs.** The walk guards against a null pool and caps its
iteration, but no value has been read out of DK2 yet; the first capture is also
the first test of this layout. Expect the tick column to be one late, as every
sampled column is (ADR-0119).

## Replaying it

`InputLog` reads the log back as `InputFrame`s and `dk2tools replay <level>
<tags.csv> <out.csv>` drives a level with them, writing the probe's CSV shape
so `capture-diff` compares a played capture the way it already compares an idle
one (ADR-0128). Three readings are arguments rather than inferences, because
the file does not carry them:

| `--lag` | how many turns late the stamp is; one, as every sampled column is |
|---|---|
| `--origin` | which DK2 turn our turn 1 is, for a capture that attached late |
| cancellation | a tag and an untag of one tile in one sample are a list migration |

The last is the one that would corrupt a replay silently. A tile crossing
between kinds 1 and 2 is untagged and tagged in the same sample, and replaying
that pair unmarks a tile the player still wants dug. It cancels safely because
a *real* untag and retag inside one sample nets to no change in the pool and
emits no rows at all.

A replay registers `SimulationSystems.Default()`, unlike `trigger-run`, which
registers none — without `TileTagSystem` there is nothing to honour the input.
On level1 and Level20 that costs nothing: with an empty log the trigger output
is identical to the triggers-only run across all 600 turns, so both matched
baselines survive.

**The `applied`/`rejected` line is the first-capture check.** If these
coordinates are not tile coordinates, every tag lands off the map or on floor
and the count says so, instead of a divergence report blaming a rule (Q287).
