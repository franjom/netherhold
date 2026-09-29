# DK2's random numbers: the generator, its streams and its seed

Source(s):
- Retail v1.70 `DKII.EXE`, read in Ghidra 12.1.4, 2026-09-29: the generator's
  full disassembly, every call to it (479), every instruction naming world
  offset `0x15A0`, and the callers of the C runtime's `rand()`
- Flame's map (`D:\Flame`, `f6e2a3d`), as a lead: its names for the generator,
  the seed field, the checksum and the save and load routines are cited as
  Flame's where used
- OpenKeeper has nothing to offer: it draws from Java's unseeded `Random`

Original version: DK2 v1.70 (GOG)

Implemented as `Dk2Lcg` in `src/DK2.Core`, behind `SimRandom`, and not the
default (ADR-0275). Answers Q3.

## 1. The generator, `0x005558D0` (CONFIRMED)

It takes four arguments: the range `n`, a pointer to a 32-bit seed, a source
file name and a line number. Every caller passes its own `__FILE__` and
`__LINE__`, so each draw names its call site.

- If `n` is 0 it logs `Random called with mod of 0 [file:line]` to the shared
  debug log and returns 0. The seed is **not** advanced.
- Otherwise it replaces the seed with `seed * 0x0019660D + 0x3C6EF35F`, 32-bit
  wrapping. This is the quick LCG from *Numerical Recipes*.
- It returns the new seed rotated right 13 bits, taken unsigned modulo `n`.

Consequences:

- `random(2)` reads state bit 13, whose period is 2^14 draws. Coin flips do
  not alternate the way a low LCG bit would.
- The modulo bias is at most about 4×10^-6, at the largest range seen
  (`0x4000`), and cannot be observed.
- The furniture selector (`auto-placed-furniture.md` §2) is this same function
  called on a stack-local seed built from tile coordinates.

## 2. The streams (CONFIRMED by call-site argument)

Each call's seed pointer was read from the push before the source-file push.
The source file comes from that string.

| Seed | Sites | Source files |
|---|---|---|
| World record `+0x15A0` | 283 | The simulation: ComputerPlayer (30), ObjectState, Player, Creature, CasinoState, Workshop, RunAway, Room, the creature states, Hatchery, WorldTrigger and about 45 more |
| Static `0x006D6450` | 98 | Effects only: EffectGeneration, BridgeThing, Effect, EffectElement |
| Stack-local | 51 | RoomManager (the furniture hash), Camera, the hero-gate front end, Bridge, Hatchery, Library |
| Other statics and object fields | 47 | Rendering and UI: ProceduralMesh, RenderInfo, DefaultPlayerInterface, Bridge |

The world record is the static at `0x006F2A08`, so the world seed's address is
`0x006F3FA8`. One RenderInfo call passes that address directly.

- The effect stream `0x006D6450` is set to 1 at `0x0043AB86`.
- `0x00795854` is incremented before each of its draws, so it acts as a
  counter rather than a stream.

**The simulation has one stream.** A cosmetic effect never moves a creature's
draw.

The C runtime's `rand()` (`0x006359F0`, MSVC's `×0x343FD + 0x269EC3`) has 15
calls in 6 functions:

- Four are in the texture JPEG decoder (Flame's `tqia_*`, `__tqi_*`), reached
  from its surface copy and file-body reader.
- Two are unidentified and reference no strings: `0x0042F040`, called from
  `0x0042C390`, and `0x0059F940`, which is recursive and called from
  `0x005A0030`.
- `rand()` keeps its state in the C runtime's per-thread data, not the world
  record. A simulation use would desync lockstep multiplayer, so these two
  are presumably presentation. That is a HYPOTHESIS until they are read.

## 3. The seed (CONFIRMED)

- **Level start sets it to 1.** `0x00513870` stores 1 at `+0x15A0`. It is
  called from `0x00511A80`, and Flame names it the level-load action handler.
  `0x005099A0` also stores 1; Flame calls it `create_objects`.
- These are the only two stores that name `+0x15A0` directly. Every other
  instruction with that offset passes the seed's address to the generator, or
  to the save and load routines.
- **A save stores it, and a load restores it.** Four bytes are written at
  `0x0050AC2F` (Flame's `saveAllToTbDiscFile`) and read back at `0x0050B04B`
  (Flame's `_loadSaveFile2`).
- **The multiplayer checksum reads it.** This is `0x0050F3D0`, Flame's
  `calcChecksum`, which also reads the draw-log tick range in §4.

So every level starts from the same random state. Two runs differ only by
their inputs, which is why `-PSAVE`/`-PLOAD` playback reproduces a session.

## 4. The dead draw log (CONFIRMED present, never enabled)

When the global `0x0075B2F4` is non-zero and the seed passed is the world
seed, the generator logs `R[file:line] seed` for each draw, with the file name
stripped to its base name. It logs only while the world's tick lies within
`[0x0075B300, 0x0075B304]`; both 0 means every tick. The line goes to the
shared debug log object `0x00756EE8`.

- All three globals are uninitialised `.bss`, and no instruction writes them,
  so the log is off in the shipped game.
- They are read only by the generator, the checksum and an unnamed block at
  `0x0050A4CA`.
- Setting `0x0075B2F4` to 1 prints every simulation draw with its call site to
  `MESSAGE.LOG` beside the executable (CONFIRMED 2026-09-29, §6). No Cheat Engine
  is needed: `reverse-engineering/dk2drawlog.py` writes the flag from outside.

This is the cheapest oracle for "which mechanics roll, how often, in what
order". Reading per-tick seeds from a full-state capture gives the count only:
`Dk2Lcg.StepsBetween(seedAtTick, seedAtNextTick, max)`.

## 5. What this does not settle

- Whether our draws can line up with DK2's. That needs the same call sites,
  ranges and order at all 283 world-seed sites. §6 measures what one level
  actually draws; most of it is cosmetic.
- Where each of the 283 sites sits in a mechanic. The per-file counts above
  are the index for that work.

## 6. What Level 2 draws, from the log (2026-09-29)

The log was switched on for one playback of the owner's `l2win928` packet,
recorded at the same time (`dk2runs/level2-drawlog-20260929/`, 3534 ticks).

- **The line is `R[file:line] seed`, and the seed is the one before the draw.**
  The first line reads 1, the level-start seed, and each of the 49,586 lines is
  one generator step after the one before. So the capture's seed at the end of a
  tick is the first logged value of the next, and every draw has its tick.
- **About 14 draws a tick.** Our replay of Level 1 draws under one a tick
  against DK2's six to seven.
- **87 call sites fire.** 74 map to a generator call by their pushed file and
  line (`PreCall` on `0x005558D0`: line, file, seed pointer, range, in push
  order). 13 are in branches that window drops.

The busiest, with what they are (read in Ghidra 12.1.4):

| draws | site | address, range | what |
|---|---|---|---|
| 15,361 | Treasure.cpp:162 | `0x004F3AC3`, 100 | per gold pile in a Treasury, on a tick the room's own roll passed: a glint effect below 10 |
| 3,534 | Treasure.cpp:132 | `0x004F3A20`, 100 | once a tick per Treasury: below 30, walk its tiles for the roll above |
| 3,534 + 3,534 | Player.cpp:2045, 2046 | `0x004BB156`, `0x004BB181`, map width − 2, height − 2 | once a tick: a random tile for an ambient effect; two more draws only when that tile is of the right kind |
| 5,749 | ObjectState.cpp:2782 | `0x004A2650`, 2 | object state `0x004A24A0` |
| 1,706 | ObjectState.cpp:2708 | `0x004A24C9`, 16 | the same state's timer, set to the draw + 8 |
| 2,982 | EffectGenerator.cpp:173 | `0x004B1BA4`, 4 | an effect generator picking one of four effects; 179/180/188 place it |
| 1,620 ×3 | ObjectState.cpp:2523, 2558, 2559 | `0x004A2149` and two unmapped | always together |
| 342 | RenderInfo.cpp:201 | `0x004B38C6`, 2 | a render routine that passes the world seed's address directly |
| 730 | ImpUtils.cpp:484 | unmapped | imp work |
| 676 | GoodState.cpp:253 | `0x00462079`, 9 | heroes |
| 554 + 554 | PatrolState.cpp:148, 165 | `0x0047220E`, 4; `0x0047228D`, 9 | patrolling |
| 476 | Training.cpp:188 | `0x004F30B3`, 100 | the Training Room |

**Most draws are cosmetic, and they still move the simulation's dice.**
The treasury glints, the ambient tile, the effect generators and a render
routine all draw from the world seed, so a Treasury holding more piles, or an
effect spawned one tick apart, shifts every later gameplay roll. Lining up with
DK2 means drawing for those too, in DK2's order within the tick, even where
nothing we model changes.
