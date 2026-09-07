# Research queue: questions answerable without the running game

**Purpose.** A standing list of open questions that need only the **binary**, the
**shipped data files**, **Flame** or **OpenKeeper** — no owner observation, no
stopwatch, no play session. These can be worked independently and in parallel
with feature work, because none of them touches `src/`.

Maintained as questions move. Last reviewed **2026-09-07 (late)** against 213
catalogued questions.

**The queue's head is clear.** Everything agreed for this pass is done: the cut
list re-check (all fourteen hold), **Q210**, **Q212**, **Q213**, **Q84** and
**Q85**. The next sitting is **not** a queue item — it is *Gap 1, settle the time
base*, then the Library's research accrual. See the end of `HANDOFF.md`.

**Two queue items are now blocked on each other and that is the real work:**
**Q1** reads a game second as 4 ticks and **Q47** needs five work applications a
second, which cannot be scheduled inside four. One of the two readings is wrong,
both are ours, and `SimulationTiming.TicksPerSecond = 20` cannot move until it
is settled.

---

## The four rules, before anything else

**1. Decompiler output never enters the repository.** ADR-0046 and
`reverse-engineering/README.md`. Facts cross — a number, a formula, a comparison,
an order of operations, a description of control flow, **in our own words**, in
`docs/research/`, with provenance. Output does not cross: not into `src/`, not
into `docs/`, not into a commit message, not into a test fixture. Check
`git status --short reverse-engineering/` is empty before every commit.

**2. Flame is UNLICENSED.** Facts only. **Never write an identifier from Flame
into the repository** — not a function name, not a struct name, not a field name.
A Flame name may be *cited in a document* under ADR-0080 and may never appear in
code. Use it to confirm or to aim; describe what you learn in our words.

**3. OpenKeeper is reusable with attribution** (ADR-0003's second amendment,
`THIRD-PARTY-NOTICES.md`) — **and its implementation is its guess, never
evidence.** The owner's standing instruction: *"reference it and compare with our
own findings, not copy it."* Q86 is the cautionary case: a slope taken from
OpenKeeper shipped, was tested, and was wrong.

**4. Numbers collide across worktrees.** ADR numbers and question numbers are
read from committed files, so two worktrees will pick the same next number.
**Check every worktree before taking one**, and prefer appending answers to
existing rows over opening new questions.

## How the binary work is done here

Ghidra 11.0.1, headless, against the project at `D:\ghidra projects\DK2_RE`:

```
JAVA_HOME="C:/Program Files/Java/jdk-26.0.2.1" \
  "D:/ghidra_11.0.1_PUBLIC/support/analyzeHeadless.bat" \
  "D:/ghidra projects" DK2_RE -process DKII.EXE -noanalysis \
  -scriptPath <dir> -postScript <Script.py> <args>
```

Helper scripts are copied into
`reverse-engineering/scripts-from-master/` in the codex worktree:

| Script | What it does |
|---|---|
| `ScanAll.py <out> <addrHex>...` | **use this one.** Raw scan of **every initialised block** for the little-endian bytes of an address, naming the block on each hit; sees indexed reads that Ghidra's reference analysis misses |
| ~~`ScanAddr.py`~~ | **superseded 2026-09-07.** Same scan, but hardcoded to `0x401000`–`0x652c00`, which is `.text` **only** — it never saw `cseg`, the image's *second executable block* (`0x653000`–`0x66bfff`, 100 KB of code). Every reader count in this project taken before that date is a claim about `.text` |
| `MaskScan.py <out> <hexConst>...` | every instruction whose text mentions a constant, **with the containing function name**. Pass the digits **without** `0x` — the script prepends it |
| `CallScan.py <out> <addrHex>...` | every `E8`/`E9` rel32 whose target is the address, plus absolute occurrences — finds callers in unanalysed regions and vtable installs |
| `ForceAt.py <out> <addrHex>...` | force a function at exactly this address and decompile |
| `ForceFunc.py <out> <addrHex>...` | walk back to the MSVC prologue, force a function, decompile |
| `Disasm.py <start> <end> <out>` | plain disassembly of a range — **use this when the decompiler garbles**, which it does around register-tracked table fills |
| `PreCall.py <out> <targetHex>` | the 12 instructions before every reference to a target, for reading pushed arguments |
| `CatTable.py <out> [start end]` | reconstructs a run-time-filled table by walking a straight-line `MOV` run and tracking registers |

**Two traps that have each cost this project time twice:**

- `Memory.getBytes` with a Python `bytearray` **fails silently** and leaves the
  buffer zeroed, which reads as "no hits" rather than as an error. Use
  `jarray.zeros(n, 'b')`.
- `MaskScan` matches constants as **substrings**: searching `0x2000000` also
  matches `0x20000000`. Filter the output, or you will read one bit's answer as
  another's.
- **`MaskScan` prepends `0x` to its argument.** Pass `68a`, not `0x68a` — the
  latter searches for `0x0x68a`, matches nothing, and **reports zero hits rather
  than an error**. This cost real time on 2026-09-07: a zero-hit run was taken
  as evidence that the room-manager region was unanalysed and had to be scanned
  raw, and the conclusion *"reached only through a vtable"* was built on top of
  it. Re-run correctly, the same query returned 114 hits including twelve
  callers nothing had recorded. **A scanner that reports zero rather than
  failing is the most expensive kind of bug there is** — check any zero-hit run
  against a constant you know is present before believing it.

- **A scan bounded to `.text` misses 100 KB of code.** The image has **two
  executable blocks** — `.text` and **`cseg` at `0x00653000`–`0x0066BFFF`**.
  `ScanAddr.py` covered only the first, so every reader count taken before
  2026-09-07 is a claim about `.text` alone. Use `ScanAll.py`, which walks all
  thirteen initialised blocks and names the block on each hit. (Re-run over the
  cut list, `cseg` contributed **zero** hits — the conclusions held. That is the
  outcome, not the reason to skip the check.)
- **Always put a positive control in the same run.** Cheapest one here is
  variable 19's global `0x006F2590`: it must return **226** hits. A run where
  the control comes back low or empty is a broken harness, and you find that out
  in the same output rather than three conclusions later.

### The cut list is re-checked, and it holds (2026-09-07)

The item that stood at the head of this queue — *re-check the fourteen variables
declared cut, because every scan-based negative is suspect* — **is done**. All
fourteen hold; the sweep was widened to all **57** zero-reader globals rather
than the fourteen; and it returned three findings of its own, including `cseg`
above and a zero-shipped percentage that is **not** inert. See
`balance-variables-in-the-binary.md`, last section, and ADR-0085 amendment 1.
Leftover: **Q212**.

### Two facts about the executable worth knowing up front

- **Tables filled at run time cannot be read from the file.** The 281-record
  creature state table (`0x006DA8B0`, stride 25) and the 32-record state-category
  table (`0x00699688`, stride `0x40`) both live in uninitialised `.data` and are
  filled by long straight-line `MOV` runs that belong to no function. Recovering
  one means walking the disassembly tracking register values — `CatTable.py`.
- **Indexed reads carry the base constant.** A stride of 25 compiles to
  `[EAX + EAX*4 + base]` with `EAX` pre-multiplied by 5. So an address scan for
  a table slot really does find every read of that slot, which is what makes
  "written once and never read" a provable statement (Q184).

---

## Tranche A — the cheap reader scans

**Method identical for all six**, and each is an hour or two: take the variable's
cached global out of `docs/research/balance-variables-in-the-binary.md`, run
`ScanAddr.py`, then `MaskScan.py` to name the containing routine, then decompile
it. Expect a high close rate.

| Q | Question | Starting point |
|---|---|---|
| **Q69** | What reads `AttackTileHealth` (−140), and what reads the two mana-vault claim rates? | Q68 removed this project's only user for `AttackTileHealth`, so it may be dead. `ClaimNeutralManaVaultHealth` (4) and `ClaimEnemyManaVaultHealth` (20) have **no global listed** — find out why |
| **Q81** | What is `IncreasedWorkRatePersentageOfPlayersTorturedCreatureTypes` = 25? | Its three siblings all land in `creature[0x3B3]`, the work-rate bonus (Q6). Does this one? |
| **Q110** | Does anything shorten the dying window? | `CreatureDyingStateDurationSeconds` = 60 at `0x006F25B0` |
| **Q160** | What is `TimeInHandBeforeCreaturesBecomeAngrySeconds` = 1000 for? | `0x006F2977`. 1000 is absurd as seconds, so it is either ticks, dead, or not a duration |
| **Q168** | How often does an overcrowded prison roll its jail break? | `OvercrowdingJailBreakPercentagePerExtraCreature` at `0x006F2920` has **one** reader already counted |
| **Q123** | Does anger cool while merely *in* the lair, or only while asleep in your own bed? | **Nearly free now.** The routine is known and has exactly **three callers**, each a state's per-tick handler. The 281-state table with names is in `creature-state-table.md` — find the three callers, look up their names, done. If all three are sleeping states our rule is exact; if one is awake-in-lair, we undercool |

**Watch for the second kind of inert** (ADR-0085): a variable with a live reader
and a shipped value that makes it a no-op is **disabled**, not cut, and the two
belong in different columns. A zero is a reason to look harder, not to stop.

## Tranche B — the M1 record sweep (data, not binary)

**This is the stalled milestone.** M1's definition of done is *"all 18 record
types round-trip"*; **nine** parse today. This tranche is pure file work with
OpenKeeper's field lists and Flame's symbol map as cross-checks, and it touches
no simulation code at all.

| Q | Question |
|---|---|
| **Q70** | What are the last **72 bytes** of a CREATURES record? The shipped record is 5449 bytes and the fullest published field list does not account for the tail |
| **Q14** | What are the **88 extra bytes** in the embedded 5537-byte CREATURES variant? |
| **Q15** | What is the `THINGS(190)` chunk inside `GlobalVariables.kwd` (39 items) for? |
| **Q85** | What do the two trailing fields on every VARIABLES record mean? `Unk1` and `Unk2` are parsed and read by nothing — **correlate them across all 214 records and all 66 level files** |
| **Q84** | What are variable ids **66** and **77**? Both appear only in per-level files; level 1 has 66 = 3 (`unk1` = 2) and 77 = 29 (`unk1` = 3, `unk2` = 125) |
| **Q183** | What is in the fixed block between a map's header (ends `0x134`) and its grid (`0x8861`)? Same in all 66 maps or not — that alone is informative |
| **Q120** | Which objects belong to which room? `dk2tools kwd objects` already prints a partial "object ids per room"; **Q169** is the follow-on when two rooms list one object |
| **Q129** | Is the Dungeon Heart's health **34464** or a truncated **100000**? |

**The tool for this is `dk2tools`**, and it already has the offset-hunting
commands: `kwd recscan`, `kwd creaturescan`, `kwd creaturehex` and — added
2026-09-07 — **`kwd objecthex <offsetHex> <count> [-w]`**. Adding the equivalent
for another record type is the right move rather than a one-off script.

## Tranche C — the meaty single questions

**Q209 — what predicate turns an object into an impassable tile?** The highest
value item on this page, and it is properly set up. DK2 blocks a portal's centre
and a heart's platform with **objects**, and the table says so: object **100 is
named "Dungeon Heart Obstacle"**. Already established: `objDef+0xF4` is the
runtime flags word, `objDef+0xF8` a size in 4096 fixed point used as `size >> 1`
(file offset `0x32C`), **15** objects carry `SolidObstacle` and all fifteen are
room furniture, **58** carry `Obstacle`. **Both obvious rules are refuted by the
shipped data** — `Obstacle && size > 0` would block every creature bed, and
`SolidObstacle` alone would not block a portal because the Portal Crystal lacks
it. `FUN_004D12B0` reads all of it but is a collision *enumerator* behind a
first-person flag, not tile pathing. **So find DK2's tile-walkability predicate
itself, and come at it from the pathfinder rather than the object side.** It gates
the workshop's anvil, the library's bookcases, the casino's tables and the
graveyard's tombstones — every one of those rooms is coming.

**Q186 — the rest of the state-category record.** All 32 records are decoded
except two fields: `+0x34`, an allowed-category **bitmask** whose use is unread,
and `+0x3c`, a small integer. `CatTable.py` reconstructs the table; the question
is what consults those two. Transition legality lives here, and a
reimplementation that lets a creature move between states the original forbids
will drift with nothing to catch it.

**Q202 — anger buckets 0 and 5.** Six counters, four certain because exactly one
record field feeds each. Buckets 0 and 5 are catch-alls and **their names in our
code are ours**. All 60 call sites are tabulated in `creature-anger.md`; the work
is finding what distinguishes the two groups.

**Q204 — two unidentified flags in the job path.** (a) `player+0x76 & 4` in the
dispatcher restricts a creature to jobs 10 (Leave) and 16 (Rebel) and nothing
else — a switch that turns a keeper's whole workforce mutinous. (b)
`def+0x686 & 0x4000` diverts a chooser to state `0xEE`. Both self-contained.

**Q188 — when does a prisoner's step roll halve?** State 116 returns to the
decision state on `random(100) < 20`, but down one of two paths: when
`FUN_00494A00(creature, 9)` is non-zero it instead requires 10..19, halving the
rate. Read that predicate.

## Tranche D — needs Flame specifically

These are runtime structures, so the file data cannot answer them and the
decompiler alone is slow going. **Facts only — never an identifier into the
repo.**

| Q | Question |
|---|---|
| **Q4** | `CMapEntry` (11 bytes) — full per-tile runtime state or a cache? Field meanings |
| **Q5** | `Follow_Path` (36 bytes) contents, and DK2's pathfinding algorithm. This one eventually decides whether our A* can match the original's routes at all |
| **Q1** | **Simulation ticks per second.** The single most foundational open question here. Flame ships a `limit_tps` patch, which implies it knows the number. **Determine it; do not change `SimulationTiming.TicksPerSecond`** — that constant moves the pinned determinism hash and wants coordinating |

---

## What is *not* on this page, and why

Anything that needs the game running or the owner watching: Q1's confirmation,
Q46 claim duration, Q22 fill-in, Q102 hunger clock start, Q103 the sleep cycle,
Q39 mana granularity, Q75 the starting stockpile, Q89 attraction below
recommended size, Q143, Q138 — and the **portal recovery** experiment, which is
the cheapest item on `observation-checklist.md` and unblocks a formula that is
otherwise fully read (Q86).

**Combat (Q8) is deliberately absent.** It is the largest unread mechanic in the
game and an M7 milestone, not a research errand.

### The record sweeps, and which of them are done

| record | scalar fields | swept | result |
|---|---|---|---|
| balance variables (240 ids) | — | **yes**, 2026-09-06/07 | 12 zero-shipped: 2 cut, 10 disabled; ADR-0085, amended (id 112 is live at zero) |
| **CREATURES** (46 records) | 56 | **yes**, 2026-09-07 | 2 zero everywhere, 4 constant, 3 fields **CUT** with no consumer; `record-field-sweep.md` |
| **OBJECTS** (145 records) | 21 | **data yes, binary no** | `ManaValue` zero in all 145; consumer unknown, needs the objects loader (**Q213**) |
| ROOMS | — | no | — |
| TERRAIN | — | no | — |
| DOORS, TRAPS, SHOTS, SPELLS, EFFECTS | — | no | — |

`dk2tools kwd fieldsweep <creatures|objects>` does the data half in one command
and takes a new record type in about ten lines. **The remaining rows are cheap**
and the same two questions apply to each: which fields never vary, and does the
engine read them.

**How to take a field from a file offset to a runtime offset**, which is what
the binary half needs and what used to be guesswork:

1. the creature loader is `FUN_00492320`, and **source offset = file offset +
   `0x3F0`** (confirmed on two fields at opposite ends of the record);
2. `ReadsAt.py <out> <sourceOffsetHex>` finds the loader's read;
3. the **store** on the next line or two gives the runtime offset;
4. `ReadsAt.py` on *that* offset lists every reference — and three of them are
   never consumers: the initialiser `FUN_00491DC0`, the loader itself, and the
   definition-copy routine `FUN_0051F3B0`.

Each record type has its own source delta. The objects one is not `0x3F0` and is
unknown; find it the way the creature one was found — scan for stores to a
runtime offset you can identify at both ends, then read the source offset copied
into it.

**And a trap in step 4:** `ReadsAt.py` matches instruction text, so it hits
**stack** offsets too. `+0x758` returned a `[ESP + 0x758]` local from an
unrelated routine. Check the base register before counting a hit — this one
inflates a count, where every other scanning trap here deflates it.
