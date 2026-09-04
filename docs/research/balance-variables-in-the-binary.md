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

- **the globals are contiguous in id order**, four bytes apart, so ids with no
  handler still occupy their slot — id 18 at `0x006F258C`, 19 at `0x006F2590`,
  20 at `0x006F2594`, and so on. An off-by-one in the index would have broken
  that ordering rather than shifted it;
- **177 of the 181 mapped ids are ones this project had already named** from the
  shipped table, and **175 have a shipped value**. A wrong base would have
  produced a scatter of unnamed ids instead.

**Reader counts are a hint, not a measurement.** They count instructions that
reference the exact address. Ghidra attributes an indexed access to the base it
starts from, so a routine walking the globals block as an array would pile
references onto whichever id sits at the base. Where a count is load-bearing
below, it has been checked instruction by instruction.

## Variable 19: read everywhere, shipped by nothing

**This is the one result here that matters on its own**, and it is a negative.

| | |
|---|---|
| Global | `0x006F2590` — the slot immediately after `ChickenGenerationTimePerHatchery` |
| Read by | **223 instructions**, across the whole binary. 225 of the 226 raw occurrences of that address in `.text` are **direct absolute operands**, so there is no array-indexing inflation: it really is read from 223 places |
| Used as | a **multiplier 28 times** and a **divisor 22 times** (`IMUL`, `DIV`, `IDIV`); the rest load it into a register first |
| Written by | **exactly one instruction in the whole of `.text`** — its own dispatch stub |
| Lives in | the **uninitialised** part of `.data`, so the image carries no default: it is zero until something writes it |
| Supplied by | **nothing.** Not `GlobalVariables.kwd`, not any of the 66 `*Variables.kld`, not any file in the install |

That last row was checked by scanning **all 1236 files** in the install for a
variables record with id 19, with a scanner first validated against ids we know
are there — it finds id 3 with its global value and its three level overrides,
id 18 at 16, and id 74 at **340 in 64 files**, which is exactly the sweep Q1
already recorded. It finds no id 19 anywhere. The registry key the installer
writes carries only `BuildTypeCode`, `language` and the version.

**Twenty-two sites divide by it.** A zero there faults, and the game does not.
So either those paths never execute, or something outside every file we have
supplies the value. **Q132**, and it must not be guessed.

**Why it is worth this much attention:** multiply-and-divide is what a **unit
conversion** looks like — a designer-facing quantity turned into an
engine-facing one and back. Every duration in the balance table is in seconds,
the engine counts ticks, and something has to bridge the two 223 times. That
makes id 19 the strongest candidate yet for the constant **Q1** has been asking
about since this project's first day.

It also reframes Q1's other datum. `GameTicks` (id 74) is **340 in every level
file** and its global is read by **two** instructions. Two against 223. Whatever
340 is for, it is not the thing the engine multiplies by.

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
