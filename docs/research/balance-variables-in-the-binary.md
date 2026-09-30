# Where the balance variables go: the id to global map

**Source:** `DKII.EXE`, read on 2026-09-04. Facts only, in our own words —
ADR-0046 governs what may cross out of `reverse-engineering/`.

This document is a **research multiplier rather than a finding**. It answers a
question that could not be asked before: *what code reads variable N?* Until now
a balance variable was a number in a table with a community name attached, and
the only way to learn what used it was to guess from the name. There are 214 of
them and several of those names have already turned out to be wrong or
misleading.

## The dispatch

`GlobalVariables.kwd` and the per-level `*Variables.kld` files are lists of
16-byte records: id, value, and two more fields. When the engine applies one it
calls a small setter that does exactly this:

    index = id - 3
    if (index > 239) return        // not cached
    jump to table[index]           // a 240-entry table of tiny stubs

and each stub is three instructions — store the value into **one fixed global**,
return 1. So a variable the engine cares about has a **dedicated global
address**, and every place that reads that address is a place that uses that
variable.

**181 of the 240 slots in that setter have a global.** The other 59 jump to a
shared do-nothing stub.

> **CORRECTION, Q69, 2026-09-07: this is only one of two variable-install
> paths.** A second dispatcher takes a complete VARIABLES record and explicitly
> handles seventeen ids which are no-ops in the first table: 4–16, 136 and
> 174–176. Every one has a fixed global after all. Except for
> `DungeonHeartObjectHealth` (174), that dispatcher divides the shipped value by
> variable 19 before storing it. Thus the tile-work values are cached per tick,
> not read as an indexed set: for example `AttackTileHealth` −140 becomes −35
> at `0x006F2558`. The two mana-vault rates likewise become 1 and 5 at
> `0x006F294F` and `0x006F2953`, but neither global has a reader. Across both
> paths **198 ids have storage; 42 do not**. The original 181/59 count remains
> correct only for the first dispatch table.

## How the map is believed

**The anchor is the Hatchery.** Variable 18 is
`ChickenGenerationTimePerHatchery`, and this map puts its global at
`0x006F258C`. Two of the three functions that read that address are inside the
`Hatchery.cpp` translation unit — one of them is the routine that computes how
often a hatchery lays (see `hatchery.md`). A map that pairs the variable named
after hatcheries with code in the hatchery's own source file is not a map that
got its indexing wrong.

Two further checks fall out of the table itself:

- **the globals ascend in id order**, so an off-by-one in the index would have
  broken that ordering rather than shifted it;

> **CORRECTION, 2026-09-06.** This bullet used to end *"four bytes apart, so ids
> with no handler still occupy their slot — id 18 at `0x006F258C`, 19 at
> `0x006F2590`, 20 at `0x006F2594`"*. **The four-byte stride is wrong**, and it
> was generalised from three consecutive `u32` variables. The stubs store
> **`AL`, `AX` or `EAX`** depending on the variable's width and the globals are
> **packed**, so the stride is 1, 2 or 4 bytes — measured across all 181 cached
> slots it is 1 byte 42 times, 2 bytes 54 times and 4 bytes 72 times, with four
> larger jumps where a handful sit outside the main block. **Ids with no handler
> occupy no slot at all**; they share a stub that stores nothing.
>
> **Anything computed from the old rule is wrong past the first size change.**
> `base + (id - 18) * 4` puts `PrisonModifyCreatureHealthPerSecond` at
> `0x006F2620`; it is actually at `0x006F2621`, and a reference search on the
> computed address comes back **empty** — which reads exactly like "nothing uses
> this variable" and is not true. The full corrected map is §4 below, so nobody
> has to compute one again.
>
> Only two other documents cite an address from this map (Q1 and Q132) and both
> cite variable 19's `0x006F2590`, which the corrected map confirms.
- **177 of the 181 mapped ids are ones this project had already named** from the
  shipped table, and **175 have a shipped value**. A wrong base would have
  produced a scatter of unnamed ids instead.

**Reader counts are a hint, not a measurement.** They count instructions that
reference the exact address. Ghidra attributes an indexed access to the base it
starts from, so a routine walking the globals block as an array would pile
references onto whichever id sits at the base. Where a count is load-bearing
below, it has been checked instruction by instruction.

## Variable 19: read everywhere, shipped by the level header

**This is the one result here that matters on its own.** It was recorded as a
negative on 2026-09-04 and **answered on 2026-09-05** — the resolution is below.
The original table is kept with its two wrong rows struck, because the way it
went wrong is worth keeping.

| | |
|---|---|
| Global | `0x006F2590` — the slot immediately after `ChickenGenerationTimePerHatchery` |
| Read by | **223 instructions**, across the whole binary. 225 of the 226 raw occurrences of that address in `.text` are **direct absolute operands**, so there is no array-indexing inflation: it really is read from 223 places |
| Used as | a **multiplier 28 times** and a **divisor 22 times** (`IMUL`, `DIV`, `IDIV`); the rest load it into a register first |
| Written by | ~~exactly one instruction in the whole of `.text` — its own dispatch stub~~ **Two.** Its dispatch stub, and one store in the **level loader** (2026-09-05). The raw `.text` scan behind this table listed both stores; the row counted one |
| Lives in | the **uninitialised** part of `.data`, so the image carries no default: it is zero until something writes it |
| Supplied by | ~~nothing~~ **The LEVEL record of every level file**, not a VARIABLES record — which is why the id-19 file scan below found nothing, and was right about what it looked for |

That last row was checked by scanning **all 1236 files** in the install for a
variables record with id 19, with a scanner first validated against ids we know
are there — it finds id 3 with its global value and its three level overrides,
id 18 at 16, and id 74 at **340 in 64 files**, which is exactly the sweep Q1
already recorded. It finds no id 19 anywhere. The registry key the installer
writes carries only `BuildTypeCode`, `language` and the version.

**Twenty-two sites divide by it.** A zero there faults, and the game does not.
So either those paths never execute, or something outside every file we have
supplies the value. **Q132**, and it must not be guessed.

### Resolved, 2026-09-05: it is ticks per game second, and it is 4

The second store is in the routine that installs a loaded level: it copies a
**16-bit field of the LEVEL record** straight into the global. The field is the
one two bytes after the level's trigger id, and the layout around it pins the
identification without needing a name: trigger id, this field, a 520-byte block,
then the 0x5000-byte message block, then the level flags — the LEVEL chunk's own
order, byte for byte (`kwd-container.md`; OpenKeeper reads the same field as
`ticksPerSec`). **All 66 shipped level files carry 4 in it**, checked directly on
2026-09-05 with each file's name cross-checked against the record it opens with.

Three uses already read agree on the unit: the free-imp deadline is
`now + seconds × var19` (below), the job re-roll gate is `5 × var19`
(`lair-acquisition.md` §8), and the lair heal/anger routine runs when
`tick % var19 == 0` — once a game second (`creature-anger.md`). So **a game
second is four ticks**, and every "Seconds" in the balance table converts by
four. What this does *not* say is how a tick relates to wall-clock time; the
counter those gates read has not been followed to its incrementer. Q1, Q132.

**How the wrong row happened, kept for the method.** The raw instruction scan of
`.text` that produced the reader count listed both stores — the stub's
`MOV [0x006F2590],EAX` and the loader's `MOV dword ptr [0x006F2590],ECX` — and
the write-up said one. How the second was dropped is not recorded. A "one
writer" claim is a claim about the whole binary: grep the raw scan for stores
before writing it, and follow every store to its source.

### One use of it, read end to end (2026-09-05)

The keeper's per-frame update sets the deadline for the next free imp as
`now + TimeBeforeFreeImpGeneratedSeconds × <id 19>`. Eight instructions, nothing
else going on in them, a balance variable that is **named in seconds** going in
and a value comparable against the engine's own clock coming out.

That does not give the value, and it must not be guessed. It does say plainly
what the multiplier is **for**, which is the half of Q132 that was reasoning from
shape. See `imps-and-mana.md`.

**Why it is worth this much attention:** multiply-and-divide is what a **unit
conversion** looks like — a designer-facing quantity turned into an
engine-facing one and back. Every duration in the balance table is in seconds,
the engine counts ticks, and something has to bridge the two 223 times. That
makes id 19 the strongest candidate yet for the constant **Q1** has been asking
about since this project's first day.

It also reframes Q1's other datum. `GameTicks` (id 74) is **340 in every level
file** and its global is read by **two** instructions. Two against 223. Whatever
340 is for, it is not the thing the engine multiplies by.

## A variable with a global, a shipped value, and no readers at all

**`TimeBetweenReattemptingSleepSeconds` (id 22, value 4) is read by zero
instructions.** Its dispatch stub stores it into `0x006F259C` and nothing in the
executable ever loads that address back.

Worth stating on its own because the name invites exactly the wrong conclusion.
Anyone building lair or sleep behaviour would reach for it as the retry interval,
and it governs **nothing**. It is the cheapest kind of claim to check: the reader
count is in the table.

> **CORRECTION, 2026-09-07.** This paragraph used to call it *"the third balance
> variable to fail that way, after `MaxFreeRangeChickensPerPlayer` and
> `MaxGoldPileOutsideTreasury`"*. **Those two are not zero-reader variables** —
> they have **3** and **1** readers and both are live. They failed a *different*
> way: the name invited a conclusion the code does not support, and
> `MaxGoldPileOutsideTreasury` in particular picks **which spot** an imp puts
> gold in rather than capping a pile (`imp-tasks.md`). *"The name is wrong"* and
> *"nothing reads it"* are separate findings and want separate sentences; only
> variable 22 belongs in this one.

**The general lesson the map now supports:** before building on a variable's
name, look at how many instructions read it. Zero means the name is a claim
nobody is checking.

## The other mapped ids with no shipped value

Recorded so that "no value" is not later mistaken for "not in the file":

| id | our name | global | readers |
|---|---|---|---|
| 29 | — | `0x006F25C1` | 0 |
| 74 | `GameTicks` | `0x006F28B6` | 2 |
| 186 | — | `0x006F296A` | 0 |
| 241 | `LevelRating` | `0x006F29FA` | 1 |
| 242 | `AverageTime` | `0x006F29FE` | 1 |

`LevelRating` and `AverageTime` being read once each, from level files only, is
consistent with this project's earlier reading of them as front-end metadata.

## The most-read variables

The top of the list, which is a rough map of where the engine's attention goes:

| id | variable | value | readers |
|---|---|---|---|
| **19** | **— (see above)** | **—** | **223** |
| 102 | `ModifyCreatureAngerWhilePrayingPerSecond` | -275 | 30 |
| 24 | `MaxGoldPerTreasuryTile` | 3000 | 19 |
| 178 | `MaxGoldPerDungeonHeartTile` | 1000 | 15 |
| 94 | `MinimumImpThreshold` | 4 | 10 |
| 179 | `ImpExperienceGainPerSecond` | 20 | 9 |
| 126 | `TrainingRoomMaxExperienceLevel` | 4 | 8 |
| 192 | `CombatPitMaxExperienceLevel` | 8 | 8 |

Two are worth a second look. **`ModifyCreatureAngerWhilePrayingPerSecond` at 30
readers is out of proportion** to a temple modifier, and is the most likely
place for the array-indexing caveat above to be biting; treat it as unverified.
And **`ImpExperienceGainPerSecond` reading 9 times** is quiet corroboration of
Q124: the owner said imps gain experience passively from work, this project made
the global authoritative over the per-creature record on that basis, and a
variable read from nine places is not a vestige.

## The binary this was read from

Two executables ship, and it is worth saying which is which, because the
Ghidra-first rule makes it matter:

- **`DKII.EXE`** — what this project analyses;
- **`DKII-DX.exe`** — the Direct3D build.

They are **the same size to the byte** (2,987,568) and differ in **1126 bytes of
2.99 MB**, the first at offset 0x130 in the PE header. Both contain the
hatchery's exponent constant exactly once. So the two are one game with two
renderer paths, and a fact read out of `DKII.EXE`'s game logic is a fact about
the DX build too. The registry confirms the install is **v1.70**
(`Version Number Major` 1, `Minor` 7), which is the target.

## Regenerating the map

The full 181-row map lives in `reverse-engineering/`, which is gitignored by
design. It is produced by reading the 240-entry table, following each stub, and
decoding its store instruction; the entry for id *n* is at table index *n* - 3.

---

## 4. The map

Read on **2026-09-06** by walking the dispatch table rather than by computing
addresses. The method, so it can be repeated:

1. The dispatcher's computed jump is `JMP [ECX*4 + 0x0051D740]`, so the table is
   **240 entries at `0x0051D740`**, indexed by `id - 3`.
2. Each entry points at a stub. A cached variable's stub is
   `MOV [global], AL|AX|EAX ; MOV EAX,1 ; RET 4` — the register names the width.
3. The 59 uncached ids point at a shared stub that stores nothing.

**181 cached by this setter, 59 not**, which reproduces its own table counts.
Q69 subsequently found the second, record-based dispatcher described above. It
caches seventeen members of this setter's apparent no-op set, making the
combined count **198 cached and 42 genuinely without storage**. Consequently,
"shared no-op in this table" alone is not proof that a variable was cut: check
the record dispatcher too.

**A cached global with no reader is a different and stronger fact**, and it is
worth checking before building on a variable. Four examples found the same day,
all of them prison-adjacent:

| Variable | Global | Readers |
|---|---|---|
| `PrisonModifyCreatureHealthPerSecond` (55) | `0x006F2621` | **2** — the drain applier and the general health routine, which recognises the drain *by its amount* |
| `OvercrowdingJailBreakPercentagePerExtraCreature` (151) | `0x006F2920` | **1** — room insertion rolls once when the new occupancy exceeds a prison's safe capacity (Q168) |
| `MotionlessInPrisonSeconds` (39) | `0x006F25E9` | **none** — written by its stub and read nowhere |
| `CreatureCriticalHealthPercentageOfMax` (173) | `0x006F294A` | **none** |

The last two are parsed, cached and dead in v1.70. That matters when a rule here
is built on one: `CreatureCriticalHealthPercentageOfMax` is the 20% this project
uses for a captured creature's arrival health (Q167), and the engine never
consults it.

### The tile-work rates, mapped to the handlers that read them (Q219, 2026-09-07)

Every rate global in the `0x006F2554`–`0x006F2588` block scanned across all
thirteen initialised blocks, and each reader placed in the task-handler table at
`0x006E4198` (29 bytes a record, slot `+0x00` is the work handler):

| task record | handler | rate it reads |
|---|---|---|
| 1 | `FUN_00496E00` | `DigRockHealth` (8), `DigOwnWallHealth` (9), `DigEnemyWallHealth` (10), `RepairRoomHealth` (16) |
| 2 | `LAB_004971D0` | `MineGoldHealth` (7) |
| 4 | `FUN_004974C0` | `ClaimTileHealth` (4) |
| 5, 6 | `FUN_00497670`, `FUN_004977F0` | `AttackTileHealth` (5) |
| 7 | `LAB_00497960` | `ReinforceWallHealth` (12) |
| **8** | `LAB_00497AC0` | **`RepairWallHealth` (13)** |
| 10 | `LAB_00497BF0` | `ConvertRoomHealth` (14), `Unknown_17` (17) |
| 11 | `LAB_00497DF0` | `AttackRoomHealth` (15) |
| 12 | `LAB_00498120` | `RepairRoomHealth` (16) |

**Two of the block's rates have no reader at all: `RepairTileHealth` (6) and
`FillInHealth` (11).** Both are cut.

> **A derived offset formula is not a mapping.** This was nearly got wrong from
> the other direction: from three known pairs (ids 4, 5 and 12) the block looks
> like `0x006F2554 + (id − 4) × 4`, which fits all three and is **wrong** — ids 9
> and 10 sit above id 16, at `0x006F2584` and `0x006F2588`. The table below was
> derived from the setter stubs and is the authority; the arithmetic was a
> coincidence over a short run. Positive control for any re-scan: id 18's
> `0x006F258C` must return **three** readers.

| id | name | shipped | global |
|---|---|---|---|
| 3 | `EntranceGenerationSpeedSeconds` | 25 | `0x006F2550` (u32) |
| 4 | `ClaimTileHealth` | 825 | `0x006F2554` (u32; load-time `/ 4`) |
| 5 | `AttackTileHealth` | -140 | `0x006F2558` (u32; load-time `/ 4`) — two live hostile-floor work readers (Q69) |
| 6 | `RepairTileHealth` | 125 | `0x006F255C` (u32; load-time `/ 4`) — **no reader; CUT** (Q219, 2026-09-07). The setter store is its only occurrence in any initialised block. So **repairing a floor tile is not a mechanism DK2 has**; the live repair rate is id 13 |
| 7 | `MineGoldHealth` | -300 | `0x006F2560` (u32; load-time `/ 4`) |
| 8 | `DigRockHealth` | -5000 | `0x006F2564` (u32; load-time `/ 4`) |
| 9 | `DigOwnWallHealth` | -130 | `0x006F2584` (u32; load-time `/ 4`) |
| 10 | `DigEnemyWallHealth` | -8 | `0x006F2588` (u32; load-time `/ 4`) |
| 11 | `FillInHealth` | 5 | `0x006F2568` (u32; load-time `/ 4`) — **no reader; CUT** (Q219, 2026-09-07), and the *name* is OpenKeeper's: **"fill in" appears nowhere in DKII.EXE** (one bare `fill` in 2.9 MB) **nor in any of 507 shipped data files**. There is no fill-in task, handler or UI |
| 12 | `ReinforceWallHealth` | 50 | `0x006F256C` (u32; load-time `/ 4`) |
| 13 | `RepairWallHealth` | 22 | `0x006F2570` (u32; load-time `/ 4`) — **LIVE**, read at `0x00497B37` by task record **8**, whose gate `FUN_004540D0` is *"the tile is mine AND its health is below its terrain's maximum"* (Q219) |
| 14 | `ConvertRoomHealth` | -4000 | `0x006F2574` (u32; load-time `/ 4`) |
| 15 | `AttackRoomHealth` | -1500 | `0x006F2578` (u32; load-time `/ 4`) |
| 16 | `RepairRoomHealth` | 20000 | `0x006F2580` (u32; load-time `/ 4`) |
| 17 | `Unknown_17` | 3 | `0x006F257C` (u32) — **found 2026-09-07 (Q219) and it HAS a reader**, at `0x00497C89`, in the same task-record-10 handler that reads `ConvertRoomHealth`. So it is a tile-work rate used by the room-conversion path, and the dash here was "not looked for" rather than "absent" |
| 18 | `ChickenGenerationTimePerHatchery` | 16 | `0x006F258C` (u32) |
| 19 | `?` | ? | `0x006F2590` (u32) |
| 20 | `GoldMinedFromGems` | 25 | `0x006F2594` (u32) |
| 21 | `TimeBetweenReattemptingHungerSeconds` | 4 | `0x006F2598` (u32) |
| 22 | `TimeBetweenReattemptingSleepSeconds` | 4 | `0x006F259C` (u32) |
| 23 | `DeadBodyDiesAfterSeconds` | 60 | `0x006F25A0` (u32) |
| 24 | `MaxGoldPerTreasuryTile` | 3000 | `0x006F25A4` (u32) |
| 25 | `DecomposeValueNeededForVampire` | 1000 | `0x006F25AC` (u32) |
| 26 | `CreatureDyingStateDurationSeconds` | 60 | `0x006F25B0` (u32); one live reader in the common creature-health routine — seeds an exact `60 * 4` dying countdown (Q110) |
| 27 | `SpecialIncreaseGoldAmount` | 10000 | `0x006F25B4` (u32) |
| 28 | `SpecialIncreaseManaAmount` | 50000 | `0x006F25B8` (u32) |
| 29 | `?` | ? | `0x006F25C1` (u32) |
| 30 | `ImpIdleDelayBeforeReevaluationSeconds` | 5 | `0x006F25C5` (u32) |
| 31 | `DelayBeforePayReevaluationSeconds` | 4 | `0x006F25C9` (u32) |
| 32 | `MaxGoldPileOutsideTreasury` | 2000 | `0x006F25CD` (u32) |
| 33 | `PayDayFrequencySeconds` | 600 | `0x006F25D1` (u32) |
| 34 | `MotionlessWhileResearchingSeconds` | 70 | `0x006F25D5` (u32) |
| 35 | `MotionlessWhileManufacturingSeconds` | 5 | `0x006F25D9` (u32) |
| 36 | `StateCounterDelayBeforeReattemptEating` | 5 | `0x006F25DD` (u32) |
| 37 | `StateCounterWhileGuarding` | 10 | `0x006F25E1` (u32) |
| 38 | `MotionlessWhilePrayingSeconds` | 30 | `0x006F25E5` (u32) |
| 39 | `MotionlessInPrisonSeconds` | 8 | `0x006F25E9` (u32) |
| 40 | `MotionlessWhileTrainingSeconds` | 5 | `0x006F25ED` (u32) — **DEAD**, setter only (Q203, ADR-0082) |
| 41 | `MotionlessInCasinoSeconds` | 13 | `0x006F25F1` (u32) |
| 42 | `ExcessManaDecreaseRatePerSecond` | 5 | `0x006F25F5` (u32) |
| 43 | `?` | ? | — |
| 44 | `?` | ? | — |
| 45 | `DefaultHorizontalTerrainWibble` | 24576 | `0x006F25F9` (u32) |
| 46 | `DefaultVerticalTerrainWibble` | 12288 | `0x006F25FD` (u32) |
| 47 | `RebelMaxFollowers` | 2 | `0x006F2601` (u32) |
| 48 | `RebelLeavingPercentage` | 70 | `0x006F2605` (u32) |
| 49 | `RebelBecomeGoodPercentage` | 15 | `0x006F2609` (u32) |
| 50 | `RebelDefectPercentage` | 15 | `0x006F260D` (u32) |
| 51 | `TimeBeforeFirstPayDaySeconds` | 600 | `0x006F2611` (u32) |
| 52 | `DefaultTorchLightRed` | 300 | `0x006F2615` (u32) |
| 53 | `DefaultTorchLightGreen` | 200 | `0x006F2619` (u32) |
| 54 | `DefaultTorchLightBlue` | 100 | `0x006F261D` (u32) |
| 55 | `PrisonModifyCreatureHealthPerSecond` | -3 | `0x006F2621` (u16) |
| 56 | `?` | ? | — |
| 57 | `?` | ? | — |
| 58 | `?` | ? | — |
| 59 | `?` | ? | — |
| 60 | `?` | ? | — |
| 61 | `Unknown_61` | 305493641 | — |
| 62 | `Unknown_62` | -377003726 | — |
| 63 | `Unknown_63` | 305498471 | — |
| 64 | `?` | ? | — |
| 65 | `?` | ? | — |
| 66 | `?` | ? | — |
| 67 | `DefaultCellingHeightTiles` | 0 | `0x006F28A7` (u32) |
| 68 | `PayDayCutOffTimeSeconds` | 120 | `0x006F28A3` (u32) |
| 69 | `?` | ? | — |
| 70 | `CasinoModifyCreatureGoldPerSecond` | -50 | `0x006F28AF` (u32) |
| 71 | `CasinoBigWinKeeperLossPercentage` | 0 | `0x006F28B3` (u8) |
| 72 | `?` | ? | — |
| 73 | `CreaturesSupportedByFirstPortal` | 15 | `0x006F28B4` (u8) |
| 74 | `?` | ? | `0x006F28B6` (u32) |
| 75 | `ModifyHealthOfCreatureInLairPerSecond` | 100 | `0x006F28BA` (u16) |
| 76 | `ModifyAngerOfCreatureInLairPerSecond` | -550 | `0x006F28BC` (u16) |
| 77 | `?` | ? | — |
| 78 | `ModifyAngerInCompanyOfHatedCreaturesPerSecond` | 150 | `0x006F28BE` (u16) |
| 79 | `ForceAppliedToSlappedCreature` | 1024 | `0x006F28C0` (u16) |
| 80 | `?` | ? | — |
| 81 | `?` | ? | — |
| 82 | `CannotSleepModifyCreatureHealthPerSecond` | 0 | `0x006F28C2` (u16) |
| 83 | `CannotLeaveModifyCreatureHealthPerSecond` | 0 | `0x006F28C4` (u16) |
| 84 | `PlayerRescanIntervalSeconds` | -1 | `0x006F28C6` (u32) |
| 85 | `PlayerRescanRange` | -1 | `0x006F28CA` (u8) |
| 86 | `MaxFreeRangeChickensPerPlayer` | 12 | `0x006F28CB` (u8) |
| 87 | `WoodBridgeLifeOnLavaSeconds` | 30 | `0x006F28CC` (u16) |
| 88 | `CreatureSleepsWhenBelowPercentHealth` | 50 | `0x006F28CE` (u8) |
| 89 | `?` | ? | — |
| 90 | `?` | ? | — |
| 91 | `?` | ? | — |
| 92 | `?` | ? | — |
| 93 | `DoorPickedTimeSeconds` | 5 | `0x006F28D3` (u32) |
| 94 | `MinimumImpThreshold` | 4 | `0x006F28D7` (u8) |
| 95 | `TimeBeforeFreeImpGeneratedSeconds` | 5 | `0x006F28D8` (u32) |
| 96 | `CreatureStunnedTimeSeconds` | 2 | `0x006F28DC` (u16) |
| 97 | `CreatureStunnedEffectDelaySeconds` | 1 | `0x006F28DE` (u16) |
| 98 | `FirstPersonStatIncreasePercentage` | 12 | `0x006F28E0` (u16) |
| 99 | `?` | ? | — |
| 100 | `ManaCostInFirstPersonPerSecond` | 5 | `0x006F28E2` (u32) |
| 101 | `HealthDecreaseWhenOnFirePerSecond` | -150 | `0x006F28E6` (u16) |
| 102 | `ModifyCreatureAngerWhilePrayingPerSecond` | -275 | `0x006F28E8` (u16) |
| 103 | `?` | ? | — |
| 104 | `RoomSellValuePercentageOfCost` | 50 | `0x006F28EA` (u8) |
| 105 | `DoorSellValuePercentageOfCost` | 50 | `0x006F28EB` (u8) |
| 106 | `TrapSellValuePercentageOfCost` | 50 | `0x006F28EC` (u8) |
| 107 | `?` | ? | — |
| 108 | `?` | ? | — |
| 109 | `?` | ? | — |
| 110 | `InsufficientManaImpEvaluationPeriodSeconds` | 10 | `0x006F28ED` (u8) |
| 111 | `DeathWhenPossessingManaReduction` | 100 | `0x006F28EE` (u16) |
| 112 | `PercentageOfManaGainedBySacrificing` | 0 | `0x006F28F0` (u16) |
| 113 | `PossessionFriendlyStatIncreasePercentage` | 6 | `0x006F28F2` (u16) |
| 114 | `PossessionFriendlyHealthIncreasePercentage` | 6 | `0x006F28F4` (u16) |
| 115 | `ModifyCreatureHealthWhilePrayingPerSecond` | 33 | `0x006F28F6` (u16) |
| 116 | `HypnotismManaDrainPerSecond` | 0 | `0x006F28F8` (u16) |
| 117 | `?` | ? | — |
| 118 | `?` | ? | — |
| 119 | `?` | ? | — |
| 120 | `?` | ? | — |
| 121 | `?` | ? | — |
| 122 | `?` | ? | — |
| 123 | `?` | ? | — |
| 124 | `?` | ? | — |
| 125 | `LowManaWarningThreshold` | 100 | `0x006F28FA` (u16) |
| 126 | `TrainingRoomMaxExperienceLevel` | 4 | `0x006F28FC` (u8) |
| 127 | `StunnedDamageIncreasePercentage` | 100 | `0x006F28FD` (u16) |
| 128 | `MaxNumberOfThingsInHand` | 64 | `0x006F28FF` (u8) |
| 129 | `RubberBandAreaLimit` | 8 | `0x006F2900` (u8) |
| 130 | `GravityConstant` | 240 | `0x006F2901` (u8) |
| 131 | `DungeonHeartHealthRegenerationPerSecond` | 25 | `0x006F2902` (u16) |
| 132 | `EPLossSecondWhileNotTraining` | 0 | `0x006F2904` (u16) — **has a reader and ships as zero**: a per-second hook drains experience from any creature whose state category is not 13 (training). Disabled, not cut (Q203, 2026-09-07) |
| 133 | `ModifyAngerWhileSolitaryInPitPerSecond` | 275 | `0x006F2906` (u16) |
| 134 | `ModifyAngerOfCombatPitVictor` | -16400 | `0x006F2908` (u16) |
| 135 | `ModifyAngerOfPitSpectatorPerSecond` | -275 | `0x006F290A` (u16) |
| 136 | `ConvertNeutralRoomHealth` | -20000 | `0x006F257C` (u32; load-time `/ 4`) |
| 137 | `?` | ? | — |
| 138 | `ManufacturePointLossWhileNoWorkersPerSecond` | 1 | `0x006F290C` (u16) |
| 139 | `CasinoBigWinnerIncreasedHappinessPersentage` | 100 | `0x006F290E` (u16) |
| 140 | `DecreaseEntrancePersentageOfLastSackedCreatureType` | 33 | `0x006F2910` (u16) |
| 141 | `IncreaseEntrancePersentageOfNewCreatureTypes` | 33 | `0x006F2912` (u16) |
| 142 | `IncreasedWorkRatePersentageOfPlayersTorturedCreatureTypes` | 25 | `0x006F2914` (u16) — live only in research crediting; matching a researcher's type against the keeper's torture list yields a 29% net uplift because the arithmetic adds variable 19 to the nominal percentage (Q81) |
| 143 | `IncreasedWorkRatePersentageWhenCasinoBigWinOccurs` | 35 | `0x006F2916` (u16) |
| 144 | `ImpPopCountdownTimeSeconds` | 10 | `0x006F2918` (u8) |
| 145 | `NumberOfRandomWallDeteriorationsPerSecond` | 0 | `0x006F2919` (u8) |
| 146 | `WallDeteriorationDamage` | 0 | `0x006F291A` (u8) |
| 147 | `WallDamageFromShot` | -10 | `0x006F291B` (u8) |
| 148 | `PercentageChanceOfKillingCreatureInPit` | 10 | `0x006F291C` (u8) |
| 149 | `ModifyPlayerColdWhileTrainingPerSecond` | -5 | `0x006F291D` (u16) — **live, and "Cold" is gold**: five a game second per creature in a training room, and a keeper who cannot pay loses the job (Q205, ADR-0083) |
| 150 | `SkeletonArmyDurationSeconds` | 30 | `0x006F291F` (u8) |
| 151 | `OvercrowdingJailBreakPercentagePerExtraCreature` | 50 | `0x006F2920` (u8) — one roll per creature inserted above a prison's safe capacity, chance `extra × 50%` (Q168) |
| 152 | `MaxTimeInGuardRoomBeforeWanderingToGuardPostSeconds` | 60 | `0x006F2921` (u8) |
| 153 | `?` | ? | — |
| 154 | `AmountOfGoldStolenInFirstPerson` | 100 | `0x006F2922` (u16) |
| 155 | `BoulderDeteriorationDamagePercentagePerSecond` | 10240 | `0x006F2926` (u32) |
| 156 | `DrunkenDurationSeconds` | 15 | `0x006F2924` (u16) |
| 157 | `BoulderInitialHealth` | 4000 | `0x006F292A` (u16) |
| 158 | `BoulderSpeedTilesPerSecond` | 4915 | `0x006F292C` (u32) |
| 159 | `BoulderToCreatureDamagePercentage` | 50 | `0x006F2930` (u8) |
| 160 | `BoulderSpeedToHealthRatio` | 50 | `0x006F2931` (u8) |
| 161 | `BoulderFearAmount` | 701 | `0x006F2932` (u16) |
| 162 | `BoulderFearRange` | 4 | `0x006F2934` (u16) |
| 163 | `DrunkChancePercentagePerDrink` | 5 | `0x006F2936` (u16) |
| 164 | `IncreasedWorkRatePecentageFromSlapping` | 50 | `0x006F2938` (u16) |
| 165 | `IncreasedWorkRateDurationFromSlappingSeconds` | 60 | `0x006F293A` (u16) |
| 166 | `DungeonHeartMaxManaIncrease` | 32000 | `0x006F293C` (u16) |
| 167 | `DungeonHeartManaGenerationIncreasePerSecond` | 30 | `0x006F293E` (u16) |
| 168 | `TremorDamagePerTerrainTilePerSecond` | 436 | `0x006F2940` (u16) |
| 169 | `ExternalGuardingDurationSeconds` | 15 | `0x006F2942` (u32) |
| 170 | `MinimumJobDurationSeconds` | 15 | `0x006F2946` (u32) |
| 171 | `?` | ? | — |
| 172 | `?` | ? | — |
| 173 | `CreatureCriticalHealthPercentageOfMax` | 20 | `0x006F294A` (u8) |
| 174 | `DungeonHeartObjectHealth` | 100000 | `0x006F294B` (u32; stored raw) |
| 175 | `ClaimNeutralManaVaultHealth` | 4 | `0x006F294F` (u32; load-time `/ 4`) — **no reader; CUT** (Q69) |
| 176 | `ClaimEnemyManaVaultHealth` | 20 | `0x006F2953` (u32; load-time `/ 4`) — **no reader; CUT** (Q69) |
| 177 | `CreaturesSupportedPerAdditionalPortal` | 5 | `0x006F28B5` (u8) |
| 178 | `MaxGoldPerDungeonHeartTile` | 1000 | `0x006F25A8` (u32) |
| 179 | `ImpExperienceGainPerSecond` | 20 | `0x006F295E` (u16) |
| 180 | `DungeonHeartClaimScanRadiusTiles` | 15 | `0x006F2960` (u8) |
| 181 | `StunTimePercentageIncreaseWhenHit` | 25 | `0x006F2961` (u16) |
| 182 | `CallToArmsFightDistanceTiles` | 5 | `0x006F2963` (u8) |
| 183 | `DungeonHeartReportingDistanceTiles` | 4 | `0x006F2964` (u8) |
| 184 | `DwarfDiggingMultiplier` | 4 | `0x006F2965` (u8) |
| 185 | `TriggerTrapTriggerSpeedTilesPerSecond` | 4096 | `0x006F2966` (u32) |
| 186 | `?` | ? | `0x006F296A` (u32) |
| 187 | `FirstPersonAccuracyAngleDegrees` | 45 | `0x006F296E` (u32) |
| 188 | `SpecialReceiveImpsAmount` | 10 | `0x006F2972` (u8) |
| 189 | `DungeonHeartCreatureGatheringDistanceTiles` | 7 | `0x006F2973` (u8) |
| 190 | `GuardRoomReportingDistanceTiles` | 7 | `0x006F2974` (u8) |
| 191 | `GuardPostReportingDistanceTiles` | 7 | `0x006F2975` (u8) |
| 192 | `CombatPitMaxExperienceLevel` | 8 | `0x006F2976` (u8) |
| 193 | `TimeInHandBeforeCreaturesBecomeAngrySeconds` | 1000 | `0x006F2977` (u32) — **CUT**: the loader store is its only `.text` occurrence; no reader (Q160, 2026-09-07) |
| 194 | `SlapWorkFasterDurationSeconds` | 25 | `0x006F297B` (u16) |
| 195 | `SlapSpeedUpDurationSeconds` | 25 | `0x006F297D` (u16) |
| 196 | `TortureSpeedUpDurationSeconds` | 40 | `0x006F297F` (u16) |
| 197 | `CasinoWinSpeedUpDurationSeconds` | 60 | `0x006F2981` (u16) |
| 198 | `CriticalHealthPercentageForHeroLair` | 20 | `0x006F2983` (u8) |
| 199 | `GamblingModifyCreatureMoneySmilesPerSecond` | 1 | `0x006F2984` (u32) |
| 200 | `GamblingModifyCreatureMoneyMoneyPerSecond` | -8 | `0x006F2988` (u32) |
| 201 | `GamblingModifyCreatureAngerSmilesPerSecond` | -2000 | `0x006F298C` (u32) |
| 202 | `GamblingModifyCreatureAngerMoneyPerSecond` | 100 | `0x006F2990` (u32) |
| 203 | `GamblingDelayBeforeBecomingFearlessSeconds` | 120 | `0x006F2994` (u32) |
| 204 | `GamblingFearlessDurationSeconds` | 120 | `0x006F2998` (u32) |
| 205 | `GamblingCreatureJackpotChanceSmilesPerSecond` | 5000 | `0x006F299C` (u32) |
| 206 | `GamblingCreatureJackpotChanceMoneyPerSecond` | 20000 | `0x006F29A0` (u32) |
| 207 | `GamblingJackpotPayTimeLimit` | 240 | `0x006F29A4` (u32) |
| 208 | `GamblingJackpotPayTimeLimitMissedAngerPercentageModifier` | 100 | `0x006F29A8` (u32) |
| 209 | `GamblingJackpotBasicAmount` | 1000 | `0x006F29AC` (u32) |
| 210 | `FirstPersonRechargeModifyPercentage` | 100 | `0x006F29B0` (u8) |
| 211 | `BackOffMinimumDurationSeconds` | 0 | `0x006F29B1` (u8) |
| 212 | `BackOffMaximumDurationSeconds` | 30 | `0x006F29B2` (u8) |
| 213 | `GoodCreaturesChaseEnemyDurationSeconds` | 3 | `0x006F29B3` (u8) |
| 214 | `EvilCreaturesChaseEnemyDurationSeconds` | 7 | `0x006F29B4` (u8) |
| 215 | `MusicLevelTwoThreatThreshold` | 100 | `0x006F29B5` (u32) |
| 216 | `MusicLevelThreeThreatThreshold` | 500 | `0x006F29B9` (u32) |
| 217 | `MusicLevelFourThreatThreshold` | 1500 | `0x006F29BD` (u32) |
| 218 | `TimeBeforeDungeonHeartConstructionBegins` | 0 | `0x006F29C1` (u16) |
| 219 | `DefaultTorchLightIntensity` | 180 | `0x006F29C3` (u8) |
| 220 | `DefaultTorchLightRadiusTiles` | 6144 | `0x006F29C4` (u32) |
| 221 | `DefaultTorchLightHeightTiles` | 6144 | `0x006F29C8` (u32) |
| 222 | `MaximumManaThreshold` | 200000 | `0x006F29CC` (u32) |
| 223 | `MaximumGroupNumberOfFollowers` | 8 | `0x006F29D0` (u8) |
| 224 | `?` | ? | — |
| 225 | `CombatPitMeleeDamageModifierPercentage` | 100 | `0x006F29D2` (u8) |
| 226 | `UpgradedPossessionCostPercentage` | 70 | `0x006F29D3` (u8) |
| 227 | `UpgradedCallToArmsCostPercentage` | 70 | `0x006F29D4` (u8) |
| 228 | `MpdScoreHeroKilled` | 50 | `0x006F29D5` (u32) |
| 229 | `MpdScoreLandOwned` | 5 | `0x006F29D9` (u32) |
| 230 | `MpdScoreGoldSlabsMined` | 1 | `0x006F29DD` (u32) |
| 231 | `MpdScoreItemManufactured` | 10 | `0x006F29E1` (u32) |
| 232 | `MpdScoreCreatureEntered` | 20 | `0x006F29E5` (u32) |
| 233 | `Unknown_233` | 40 | `0x006F29E9` (u32) |
| 234 | `TortureChanceOfDyingWhenConverted` | 0 | `0x006F29ED` (u8) |
| 235 | `MaximumManaGainPerSecond` | 500 | `0x006F29EE` (u16) |
| 236 | `ClaimScanLightRed` | 200 | `0x006F29F0` (u16) |
| 237 | `ClaimScanLightGreen` | 80 | `0x006F29F2` (u16) |
| 238 | `ClaimScanLightBlue` | 35 | `0x006F29F4` (u16) |
| 239 | `PitPercentageDamageTakenOfNormalCombat` | 20 | — |
| 240 | `BoulderSlapDamage` | 400 | `0x006F29F8` (u16) |
| 241 | `?` | ? | `0x006F29FA` (u32) |
| 242 | `?` | ? | `0x006F29FE` (u32) |

---

## Twelve variables ship as zero, and ten of them are live mechanics

Read 2026-09-07 (Q203, ADR-0085). **The audit that found this was a correction to
the audit this document had been running.**

### The blind spot

Every "dead variable" finding here was reached the same way: take the cached
global, raw-scan `.text` for readers, find only the loader stub, retire the
variable. Twelve have gone that way and all twelve are genuinely cut.

**But that scan cannot see the opposite shape** — a variable with a real reader
whose *shipped value* makes the mechanism a no-op. It was found by accident:
`EPLossSecondWhileNotTraining` turned up while reading training (Q205), has a
live per-second hook, and ships as **0**.

So the audit was run the other way round: **list every balance variable whose
shipped value is zero, then scan those globals for readers.** Twelve are zero.

### Two are dead outright

| id | Variable | Global | Readers |
|---|---|---|---|
| 67 | `DefaultCellingHeightTiles` | `0x006F28A7` | **none** — setter only |
| 71 | `CasinoBigWinKeeperLossPercentage` | `0x006F28B3` | **none** — setter only |

### Ten are switched off, not cut

| id | Variable | Global | Read by |
|---|---|---|---|
| 82 | `CannotSleepModifyCreatureHealthPerSecond` | `0x006F28C2` | **`FUN_00477C20` — the needs routine** |
| 83 | `CannotLeaveModifyCreatureHealthPerSecond` | `0x006F28C4` | `FUN_00488930` |
| 112 | `PercentageOfManaGainedBySacrificing` | `0x006F28F0` | `0x0047BB4E`, in the state-category hook block |
| 116 | `HypnotismManaDrainPerSecond` | `0x006F28F8` | `FUN_004C10E0`, `FUN_004C17A0` |
| 132 | `EPLossSecondWhileNotTraining` | `0x006F2904` | `FUN_00485880`, per second unless the state's category is 13 |
| 145 | `NumberOfRandomWallDeteriorationsPerSecond` | `0x006F2919` | **`FUN_004BB6D0`** |
| 146 | `WallDeteriorationDamage` | `0x006F291A` | **`FUN_004BB6D0` — the same routine** |
| 211 | `BackOffMinimumDurationSeconds` | `0x006F29B1` | `FUN_0045F030`, `FUN_00470E30` |
| 218 | `TimeBeforeDungeonHeartConstructionBegins` | `0x006F29C1` | `0x004D9A77` |
| 234 | `TortureChanceOfDyingWhenConverted` | `0x006F29ED` | `0x0047B6FF`, inside **category 15's update hook** — torture |

**Three of these are whole mechanics with the switch at zero:**

- **Walls decay over time.** 145 and 146 are read by *one* routine,
  `FUN_004BB6D0` — a count of deteriorations a second and a damage per
  deterioration. A complete feature, shipped inert. Nothing in DK2 as played
  erodes a wall.
- **A creature that cannot sleep takes damage**, and the reader is the **needs
  routine** this project has already read twice (Q185, ADR-0074). So the
  punishment for a bedless creature was meant to be health as well as anger, and
  only the anger survives. Direct bearing on Q106.
- **Torture can kill on conversion**, rolled in category 15's own per-tick hook,
  at a chance of zero.

### What this changes here, and it is mostly a protection

> **AMENDED, 2026-09-07 (ADR-0085 amendment 1).** *"A rate of zero faithfully
> reproduced is the absence of the effect"* is true of nine of the ten and
> **false for id 112**: its reader computes `x += x * (pct + 100) / 100`, so the
> shipped zero means **double**, not nothing. The audit asked whether anything
> read each zero; it did not ask what the reader *does* with it. See the last
> section — the other nine were re-read and none has that shape.

**Nothing to build** — for nine of the ten. A rate of zero faithfully reproduced
*is* the absence of the effect, which is what this project already has, wherever
the reader uses the value as a plain rate. The value of the finding is
the other way round: **it names ten mechanics a reimplementation might
"helpfully" add, every one of which would be a divergence from DK2.** Wall
deterioration is the obvious trap — it is the sort of thing that looks like an
oversight.

Two smaller consequences:

- **A zero is a reason to look harder, not a reason to stop.** Two of the twelve
  really are cut and the other ten are not, and nothing about the value
  distinguishes them.
- **"Dead" needs two words.** A variable with no reader was **cut**; a variable
  with a reader and a no-op value was **disabled**. The mechanism is intact in
  the second case and a mod that sets the rate would work, so the two facts
  belong in different columns.

---

## A third kind of false negative: an adjacent global read as a word

**2026-09-07, found while confirming the portal capacity rule (Q86).** This
document's dead-variable list is built by scanning for a variable's cached global
and finding only the loader's setter. That test has now failed in **three**
distinct ways, and the newest is the most dangerous because it is invisible.

| kind | what it looks like | how it was found |
|---|---|---|
| **cut** | one hit, the setter, and no reader exists | the original scan — still valid |
| **disabled** | live readers, shipped value makes the mechanism a no-op | ADR-0085, by listing every variable shipped as zero |
| **read as a neighbour** | one hit, the setter, **and a real reader that never names the address** | below |

**`CreaturesSupportedPerAdditionalPortal`** (`0x006F28B5`, value 5) has exactly
one reference in the whole executable: the loader's setter. By this document's
own rule it is cut, and it would have been the fifteenth entry on the list. It is
**live, and it is the rule the 1999 manual states in prose.**

Its byte sits immediately after `CreaturesSupportedByFirstPortal`'s
(`0x006F28B4`, value 15). The capacity routine loads a **word** at `0x006F28B4`
and takes the two halves — the low byte as the first portal's allowance, the high
byte with `>> 8 & 0xff` as the per-additional rate. **The second variable's
address is never assembled into any instruction**, so no scan for it can ever
find a reader, however wide.

**What to do about it.** Before recording a variable as cut, check whether its
global is **adjacent to another variable's** — this table lists the addresses, so
it is one look. If it is, scan the *neighbour* and read that routine, because a
compiler will fold two adjacent bytes into one load whenever it can. Two-byte and
four-byte neighbours are equally affected, and the whole balance table is packed,
so the shape is available almost everywhere.

> **RE-CHECKED, 2026-09-07 — see the last section of this document.** All
> fourteen hold. Seven are dword-wide and **cannot** be reached by a folded load
> at all, and the remaining shape was swept across all 57 zero-reader globals
> rather than the fourteen: it fires exactly once in the whole table, on the case
> below that found it. The re-check also turned up a **second executable block**
> (`cseg`) that no reader count here had ever covered, and a zero-shipped
> percentage that is **not** inert.


---

## The cut list re-checked against every false negative (2026-09-07)

**Why this was run.** The reader-count test behind every "cut" finding in this
document had failed in four distinct ways by the end of 2026-09-07, the last of
them a scanner that reported zero instead of erroring. A negative reached with a
broken instrument is not a finding, so the whole list was re-derived rather than
re-read. **The list survives in full**, and the re-check produced three results
that are worth more than the confirmation.

### Result 1: there is a second executable block, and no scan had ever seen it

`ScanAddr.py` — the workhorse behind every reader count here — hardcodes
`0x00401000`–`0x00652C00`. That is exactly `.text`. The image has **thirteen
initialised blocks and two of them are executable**:

| block | range | flags |
|---|---|---|
| `.text` | `00401000`–`00652bff` | r-x |
| **`cseg`** | **`00653000`–`0066bfff`** | **r-x** |
| `.rdata` | `0066c000`–`0068d5ff` | r-- |
| `.data` | `0068e000`–`006ccbff` | rw- (initialised) |
| `.data` | `006ccc00`–`007a6dcf` | rw- (**un**initialised) |
| `grpoly_d`, `uva_data`, `idct_dat`, `tqia_dat`, `dseg`, `lbmpeg_d` | `007a7000`–`007b23ff` | rw- |
| `.rsrc`, `tdb`, headers | — | r-- |

**`cseg` is 102,400 bytes of code that no reader count in this document has ever
covered.** Every "no reader in `.text`" sentence was therefore true of `.text`
and silent about 100 KB of executable image — the same shape as the vtable
error, which was a claim about `.text` while the answer sat in `.data`.

`ScanAll.py` replaces `ScanAddr.py` and walks **every initialised block**,
reporting the block name with each hit and flagging a hit that lands in an
executable block with no instruction disassembled at it.

**What the wider scan found: nothing, and that is the point.** Re-run over every
cut-list global, **`cseg` contributes zero hits to any query**, and each global
still shows exactly one occurrence in the whole image — its own loader store.
The neighbouring block names (`idct_dat`, `lbmpeg_d`, `uva_data`) say `cseg` is
the video decoder, which is why guessing would have got the right answer. The
guess is still not the evidence.

### Result 2: the folded-neighbour trap can only bite a byte or a word

`CreaturesSupportedPerAdditionalPortal` was found live despite having only a
setter because a **wider load at its neighbour's address** covers its byte. Read
directly, the capacity routine is:

1. `0x004DBE57`–`0x004DBE66`: the room counter `0x004DCE30` is called twice,
   once for room type 3 (Portal) and once for room type 25 (Hero Portal).
2. `0x004DBE6B`: the two counts are summed, `n = portals + hero portals`.
3. `0x004DBE6F`: one **dword** is loaded from `0x006F28B4`.
4. `0x004DBE77`: `n - 1` is formed.
5. `0x004DBE7A`: the second byte of that dword — the byte at `0x006F28B5`,
   `perAdditional` — is taken out of it (the `MOV DL,CH` named below).
6. `0x004DBE7C`: `(n - 1) * perAdditional`.
7. `0x004DBE7F`: the low byte of the same dword is masked out: `byFirstPortal`.
8. `0x004DBE85`: capacity = `(n - 1) * perAdditional + byFirstPortal`.

> **Correction to the section above.** It says the routine *"loads a **word** at
> `0x006F28B4` and takes the two halves — the low byte as the first portal's
> allowance, the high byte with `>> 8 & 0xff`"*. The load is a **dword** and the
> extraction is `MOV DL,CH`. The arithmetic and the conclusion are unchanged.

**The structural consequence.** A folded load can only reach a neighbour if the
whole neighbour fits inside it, and the widest integer load 32-bit code uses is
four bytes. So a **dword** variable can never be read through another
variable's load: you would need an eight-byte load. **Every dword-wide entry on
the cut list is immune to this trap by construction**, which is seven of the
fourteen at a stroke, before any scanning.

### The whole table swept for the shape, not just the cut list

**57 of the 181 globals in this map have zero readers** — far more than the
fourteen ever written up. All 57 were tested mechanically: for each, is there
another variable whose *observed* load width covers this variable's full extent?

- **13 have a covering neighbour load.** (The candidate list is larger if you
  assume load widths; it collapses to 13 once the actual instructions are read,
  because most neighbours are loaded a byte or a word at a time and stop short.)
- **Reading all 13 leaves exactly one.** Twelve follow their wide load with
  `AND …,0xff` or `AND …,0xffff`, which throws the neighbour's bytes away —
  the wide load is the compiler's choice of encoding and carries no meaning. One
  extracts the upper bytes: `MOV DL,CH`, `CreaturesSupportedPerAdditionalPortal`,
  the case already known.

| id | variable | neighbour's load | verdict |
|---|---|---|---|
| **177** | `CreaturesSupportedPerAdditionalPortal` | dword @ `28b4`, then **`MOV DL,CH`** | **LIVE** (already known) |
| 113 | `PossessionFriendlyStatIncreasePercentage` | dword @ `28f0`, then `AND ECX,0xffff` | cut |
| 139 | `CasinoBigWinnerIncreasedHappinessPersentage` | dword @ `290c`, then `AND EAX,0xffff` | cut |
| 154 | `AmountOfGoldStolenInFirstPerson` | dword @ `2920`, then `AND ECX,0xff` | cut |
| 163 | `DrunkChancePercentagePerDrink` | dword @ `2934`, then `AND …,0xffff` | cut |
| 184 | `DwarfDiggingMultiplier` | dword @ `2964` ×3, each then `AND …,0xff` | cut |
| 225 | `CombatPitMeleeDamageModifierPercentage` | dword @ `29d0` ×2, each then `AND …,0xff` | cut |
| 105, 106 | door/trap sell percentages | neighbours load **byte only** | cut |
| 133 | `ModifyAngerWhileSolitaryInPitPerSecond` | neighbour loads **word only** | cut |
| 144 | `ImpPopCountdownTimeSeconds` | neighbour loads **word only** | cut |
| 148 | `PercentageChanceOfKillingCreatureInPit` | neighbours load **byte only** | cut |
| 166 | `DungeonHeartMaxManaIncrease` | neighbour loads **word only** | cut |

**So the trap fired once in the entire balance table, and it had already been
caught.** That is the useful form of this result: the mechanism is real, it is
now swept exhaustively rather than spot-checked, and the exposure is closed.

**The test is the extraction, not the load width.** A dword load at a byte
variable's address means nothing on its own; what matters is whether any
instruction afterwards touches bits above that variable's own width. Both live
in one disassembly window, so this is a cheap check and not a project.

### Result 3: a percentage of zero is not always a no-op

The zero-value audit (ADR-0085) split twelve zero-shipped variables into two
cut and ten *"switched off, not cut"*, and concluded there was **nothing to
build** because a zero rate faithfully reproduced is the absence of the effect.
That conclusion is wrong for one of the ten. The single reader of id 112,
inherited as `PercentageOfManaGainedBySacrificing`, computes
`x += x * (pct + 100) / 100`. Its shipped zero therefore doubles `x`.

The other nine were checked for the same shape and **none of them has it** — no
`ADD reg,0x64` anywhere in their readers' windows — so ADR-0085's conclusion
stands for the rest.

**This is a fifth kind of false negative, and it is about values rather than
scans:** *a percentage folded into `100 + pct` is fully active at zero.* The
audit that produced the ten looked for a zero and asked whether anything read
it; it did not ask **what the reader does with it**. Any "the shipped value
makes this inert" claim needs the reader's arithmetic, not just its existence.

The surrounding routine is now identified as the Temple prayer hook. It applies
prayer healing, derives mana from the creature's prayer-mana field and level,
and credits it to the player. Creature flag `+0x3EA` bit 4 gates id 112's bonus;
the manual independently identifies converted enemies as producing twice the
mana while praying. No sacrifice routine reads id 112. The inherited identifier
is wrong; the canonical mechanic analysis is `sacrifice.md`. **Q212 answered.**

### What the four checks actually returned

| false negative | how it was tested | result |
|---|---|---|
| **range** | re-scanned every cut-list global across all 13 initialised blocks, `cseg` included | **holds** — 1 hit each, all setters; `cseg` contributes nothing |
| **adjacency** | swept all 57 zero-reader globals for a covering neighbour load, then read every candidate | **holds** — the one live case was already known; dwords immune by construction |
| **the query never ran** | positive control in the same run: variable 19's global | **passes** — 226 hits, matching this document's own figure exactly |
| **disabled, not cut** | ADR-0085's audit, plus a re-read of all ten readers' arithmetic | **holds for nine; fails for id 112**, which doubles at zero |

**Two smaller corrections fell out.**

- The section above lists `TimeBetweenReattemptingSleepSeconds` as *"the third
  balance variable to fail that way, after `MaxFreeRangeChickensPerPlayer` and
  `MaxGoldPileOutsideTreasury`"*. Those two are **not** zero-reader variables —
  they have **3** and **1** readers respectively and both are live. They failed
  a different way: the *name* invited a conclusion the code does not support
  (`MaxGoldPileOutsideTreasury` picks *which spot* an imp uses rather than
  capping a pile). **"The name is wrong" and "nothing reads it" are different
  findings** and belong in different sentences; only variable 22 belongs in that
  one.
- `CreatureCriticalHealthPercentageOfMax` (id 173) is **confirmed cut against
  the whole image**, not just `.text`. `CreatureHealthRules.IsCriticallyHurt`
  already says so in its own words and can now say it without the `.text`
  caveat.

**Two incidental corroborations, recorded because they were free.** The capacity
routine above counts room type **3 and room type `0x19`** and adds them, which
is exactly the Hero-Portal capacity rule adopted the same day (ADR-0086
amendment 2) — read off the instructions rather than inferred from the handler
table. And `BoulderFearRange` is loaded and immediately `SHL …,0xc` before use,
which is this project's 4096-per-tile fixed point appearing in a routine chosen
for an unrelated reason.
