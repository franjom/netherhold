# The runtime map: reaching a tile while the game is running

| | |
|---|---|
| Source(s) | `DKII.EXE` v1.70 — the tile lookup `FUN_004508D0`, the applier `FUN_00452F60`, the caller at `0x0049713D` |
| Observed behaviour | Not yet sampled from the running game |
| Confidence | **STRONGLY INFERRED** — read directly from the address arithmetic, unsampled |
| Implementation | Read by `dk2watch.py --tiles`; nothing in `src/` uses it |
| Tests | — |
| Open questions | Q292 — what one claim application is worth, which this is built to answer |

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

**Unverified until it runs.** The first capture also tests the two offsets: a
width outside the map sizes DK2 ships, or health that never moves while a tile
is plainly being dug, means the arithmetic above is wrong rather than the game.
