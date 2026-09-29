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
- Setting `0x0075B2F4` to 1 in Cheat Engine should print every simulation draw
  with its call site. Where the lines land is UNKNOWN. MESSAGE.LOG, the world
  log's file, is the likely target, but not checked.

This is the cheapest oracle for "which mechanics roll, how often, in what
order". Reading per-tick seeds from a full-state capture gives the count only:
`Dk2Lcg.StepsBetween(seedAtTick, seedAtNextTick, max)`.

## 5. What this does not settle

- Whether our draws can line up with DK2's. That needs the same call sites,
  ranges and order at all 283 world-seed sites. It is not a goal (ADR-0275).
- Where each of the 283 sites sits in a mechanic. The per-file counts above
  are the index for that work.
