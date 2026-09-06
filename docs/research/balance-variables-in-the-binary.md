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

**181 of the 240 slots have a global.** The 59 that do not jump to a shared
do-nothing stub, and the interesting thing is *which* they are: **ids 4 to 17
are the whole tile-work health block** — `ClaimTileHealth`, `DigRockHealth`,
`ReinforceWallHealth` and the rest. Those are not cached individually because
the work code reads them as a set, indexed by job. That is a coherent story
rather than a gap, and it is the first check on this map.

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
and it governs **nothing**. This is the third balance variable to fail that way,
after `MaxFreeRangeChickensPerPlayer` and `MaxGoldPileOutsideTreasury`, and it is
the cheapest of the three to check: the reader count is in the table.

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

**181 cached, 59 not**, which reproduces the counts this document already
carried from the other direction.

**"Not cached" does not mean "unused".** Ids 4–17 are the tile-work health block
and the work code reads them as a set; that is the pattern to expect wherever a
group of variables is consumed together. It *does* mean there is no global to
search, so the question "what reads variable N" has no cheap answer for those 59.

**A cached global with no reader is a different and stronger fact**, and it is
worth checking before building on a variable. Four examples found the same day,
all of them prison-adjacent:

| Variable | Global | Readers |
|---|---|---|
| `PrisonModifyCreatureHealthPerSecond` (55) | `0x006F2621` | **2** — the drain applier and the general health routine, which recognises the drain *by its amount* |
| `OvercrowdingJailBreakPercentagePerExtraCreature` (151) | `0x006F2920` | **1** |
| `MotionlessInPrisonSeconds` (39) | `0x006F25E9` | **none** — written by its stub and read nowhere |
| `CreatureCriticalHealthPercentageOfMax` (173) | `0x006F294A` | **none** |

The last two are parsed, cached and dead in v1.70. That matters when a rule here
is built on one: `CreatureCriticalHealthPercentageOfMax` is the 20% this project
uses for a captured creature's arrival health (Q167), and the engine never
consults it.

| id | name | shipped | global |
|---|---|---|---|
| 3 | `EntranceGenerationSpeedSeconds` | 25 | `0x006F2550` (u32) |
| 4 | `ClaimTileHealth` | 825 | — |
| 5 | `AttackTileHealth` | -140 | — |
| 6 | `RepairTileHealth` | 125 | — |
| 7 | `MineGoldHealth` | -300 | — |
| 8 | `DigRockHealth` | -5000 | — |
| 9 | `DigOwnWallHealth` | -130 | — |
| 10 | `DigEnemyWallHealth` | -8 | — |
| 11 | `FillInHealth` | 5 | — |
| 12 | `ReinforceWallHealth` | 50 | — |
| 13 | `RepairWallHealth` | 22 | — |
| 14 | `ConvertRoomHealth` | -4000 | — |
| 15 | `AttackRoomHealth` | -1500 | — |
| 16 | `RepairRoomHealth` | 20000 | — |
| 17 | `Unknown_17` | 3 | — |
| 18 | `ChickenGenerationTimePerHatchery` | 16 | `0x006F258C` (u32) |
| 19 | `?` | ? | `0x006F2590` (u32) |
| 20 | `GoldMinedFromGems` | 25 | `0x006F2594` (u32) |
| 21 | `TimeBetweenReattemptingHungerSeconds` | 4 | `0x006F2598` (u32) |
| 22 | `TimeBetweenReattemptingSleepSeconds` | 4 | `0x006F259C` (u32) |
| 23 | `DeadBodyDiesAfterSeconds` | 60 | `0x006F25A0` (u32) |
| 24 | `MaxGoldPerTreasuryTile` | 3000 | `0x006F25A4` (u32) |
| 25 | `DecomposeValueNeededForVampire` | 1000 | `0x006F25AC` (u32) |
| 26 | `CreatureDyingStateDurationSeconds` | 60 | `0x006F25B0` (u32) |
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
| 40 | `MotionlessWhileTrainingSeconds` | 5 | `0x006F25ED` (u32) |
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
| 132 | `EPLossSecondWhileNotTraining` | 0 | `0x006F2904` (u16) |
| 133 | `ModifyAngerWhileSolitaryInPitPerSecond` | 275 | `0x006F2906` (u16) |
| 134 | `ModifyAngerOfCombatPitVictor` | -16400 | `0x006F2908` (u16) |
| 135 | `ModifyAngerOfPitSpectatorPerSecond` | -275 | `0x006F290A` (u16) |
| 136 | `ConvertNeutralRoomHealth` | -20000 | — |
| 137 | `?` | ? | — |
| 138 | `ManufacturePointLossWhileNoWorkersPerSecond` | 1 | `0x006F290C` (u16) |
| 139 | `CasinoBigWinnerIncreasedHappinessPersentage` | 100 | `0x006F290E` (u16) |
| 140 | `DecreaseEntrancePersentageOfLastSackedCreatureType` | 33 | `0x006F2910` (u16) |
| 141 | `IncreaseEntrancePersentageOfNewCreatureTypes` | 33 | `0x006F2912` (u16) |
| 142 | `IncreasedWorkRatePersentageOfPlayersTorturedCreatureTypes` | 25 | `0x006F2914` (u16) |
| 143 | `IncreasedWorkRatePersentageWhenCasinoBigWinOccurs` | 35 | `0x006F2916` (u16) |
| 144 | `ImpPopCountdownTimeSeconds` | 10 | `0x006F2918` (u8) |
| 145 | `NumberOfRandomWallDeteriorationsPerSecond` | 0 | `0x006F2919` (u8) |
| 146 | `WallDeteriorationDamage` | 0 | `0x006F291A` (u8) |
| 147 | `WallDamageFromShot` | -10 | `0x006F291B` (u8) |
| 148 | `PercentageChanceOfKillingCreatureInPit` | 10 | `0x006F291C` (u8) |
| 149 | `ModifyPlayerColdWhileTrainingPerSecond` | -5 | `0x006F291D` (u16) |
| 150 | `SkeletonArmyDurationSeconds` | 30 | `0x006F291F` (u8) |
| 151 | `OvercrowdingJailBreakPercentagePerExtraCreature` | 50 | `0x006F2920` (u8) |
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
| 174 | `DungeonHeartObjectHealth` | 100000 | — |
| 175 | `ClaimNeutralManaVaultHealth` | 4 | — |
| 176 | `ClaimEnemyManaVaultHealth` | 20 | — |
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
| 193 | `TimeInHandBeforeCreaturesBecomeAngrySeconds` | 1000 | `0x006F2977` (u32) |
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
