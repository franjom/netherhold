# The runtime map: reaching a tile while the game is running

| | |
|---|---|
| Source(s) | `DKII.EXE` v1.70 — the tile lookup `FUN_004508D0`, the applier `FUN_00452F60`, the caller at `0x0049713D` |
| Observed behaviour | Sampled, run 15 (level 1, 2026-09-10) — 235 rows over six tiles |
| Confidence | **CONFIRMED** — the terrains came back 2/3/6/8/9 and every health sat inside its tabled maximum |
| Implementation | Read by `dk2watch.py --tiles`; nothing in `src/` uses it |
| Tests | — |
| Open questions | Q292 and Q297 answered (ADR-0133/0134) |

## Finding a tile

`FUN_004508D0` computes a tile's address in the open, and its callers say where
the map is:

```
map  = world + 0x9DA                       ; 0x0049713D: load world, LEA 0x9DA
tile = [map + 0x14] + (y * [map + 0x0C] + x) * 16
```

So the map object carries its **width at `+0x0C`** and its **tile array at
`+0x14`**, and a tile record is **16 bytes** — the same size `fog-of-war.md`
established from the save path, now with a way to reach one live.

## Inside a tile

| | |
|---|---|
| `+0x06` | `u16` **health** — read, written and clamped by the applier |
| `+0x08` | `u16` `id:12 | flags:4`. **Bit 14 gates movement** and bit 13 is Q309. The low 12 bits are the **owner**, the player object's entity-table slot: `0x00455990` resolves them, and only `Tile_SetOwner` `0x004559D0` was found writing them. `Tile_SetTerrain` `0x0044FC40` passes the neutral player `[0x006F2548]` whenever the new terrain lacks `Ownable` (`+0x17B` bit 2, `0x0044FDBF`), and the map loader `0x0051EC60` sets every tile through it, so unclaimed ground names the neutral player (Q514; STRONGLY INFERRED, no direct-store scan). The room id is at `+0x02` (`0x004559B0`) |
| `+0x0A` | `u8` terrain id, indexing the terrain table at `0x006DA4A8` |
| `+0x0B` | `u8` flags: bit 6 is the fog gate (`fog-of-war.md`), bit 0 is read by the tag adder |

The applier `FUN_00452F60` is the whole of the health rule: a negative delta
subtracts and floors at zero, a positive one adds and **clamps at `0x7D00`**,
which is the 32000 ceiling `TileWork.MaxTileHealth` already carries.

## Why this was worth finding

Twice it was set aside as optional. It is not: the health field turns two
open questions from disassembly problems into measurements.

**Q292 — what is one claim application worth?** A claim was timed at twelve
ticks against our four, and the shipped `ClaimTileHealth` of 825 fits that
either as twelve swings of 55 or three of 220 a game second. Those differ in
mechanism, not in arithmetic, so no amount of curve-fitting separates them.
**Sampling the tile's health does**: the step size is one and the spacing is
the other, read straight off.

The same applies to digging, mining and reinforcing, each of which has a
tabled rate this can check against the tile it acts on.

## Reading it live

`dk2watch.py --tiles <out.csv>` reads the health, terrain and flags of every
tile the task pool currently has work on — a small set, so it costs one read
per working tile rather than a sweep — and writes a row when any of them
changes:

```
wall,tick,x,y,terrain,health,flags
```

**Verified.** Run 15 dug, claimed and reinforced three tiles and the trace reads
straight: Rock 165 climbing by 9, Dirt Path 1 climbing by 164, Gold 660 falling
by 60, each converting at its tabled maximum. That settled four work rates at
once (ADR-0133) and closed Q62, Q292 and Q296 in one capture.

## Q297 — the task survives, and every swing selects its rate again

**STRONGLY INFERRED from DKII.EXE v1.70; CONFIRMED outcome in run 15.** There is
no overflow carry and no chained destroy inside `FUN_00452F60`. The worker
handler selects a rate before every call: `0x00496E75` loads the own-wall rate,
`0x00496FE3` the rock rate and `0x00497090` the enemy-wall rate, then the shared
call at `0x0049713D` applies it to the tile as it exists at that moment.

The swing that empties Reinforced Wall therefore follows destroyed type 2 and
creates Rock at 165. The dig task and tag remain valid, so a later worker
resolves the same tile as unowned Rock and uses the rock rate. If the breaking
swing is last in the tick, the sampler sees Rock until the next tick, as at
(14,32); if another application follows before the sample, it sees the direct
wall-to-floor change at (10,28). Ours keeps its measured two-swing rock
correction (Q20), so two queued applications remove 100 then 65. The intermediate
Rock is real in both cases; only its lifetime differs.
