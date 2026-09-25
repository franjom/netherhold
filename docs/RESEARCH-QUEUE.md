# Research queue: questions answerable without the running game

**Purpose.** Open questions that need only the **binary**, the **shipped data
files**, **Flame** or **OpenKeeper** — no owner observation, no stopwatch, no
play session. They can be worked in parallel with feature work, because none of
them touches `src/`. The other half of the split is
`docs/research/observation-checklist.md`.

Every row here must still be open in `docs/research/open-questions.md`, which is
the one home for a question's status. This page carries only the **starting
point** for the work. To re-derive what is still open, sort the question ids:

```sh
grep -oE '^\| \*{0,2}Q[0-9]+' docs/research/open-questions.md \
  | grep -oE '[0-9]+' | sort -n | tail -1
```

**Closed since this page was last pruned**, and not to be re-opened here: Q1
(a tick is 250 ms, adopted — ADR-0089), Q69, Q81, Q110, Q123 (all read from the
binary), Q86 (the portal tally's reset is an ownership change), Q209 (furniture
is continuous collision, not a nav-tile write), Q214 and Q22 (there is no
fill-in mechanism in DK2 — Q219), and **M1** (all 18 record-type ids parse and
round-trip, ADR-0091). Tranche B below is therefore field-level work inside
records that already parse, not a stalled milestone.

**The standing lesson from the last sitting**, because it cost a documented
claim: record a shipped variable as **"no reader found"**, never as **"no
reader"**. `IncreasedWorkRateDurationFromSlappingSeconds` was written up as read
by nothing and turned out to govern a second slap effect in a mechanism nobody
had looked at.

---

## The four rules, before anything else

**1. Decompiler output never enters the repository.** ADR-0046 and
`reverse-engineering/README.md`. Facts cross — a number, a formula, a
comparison, an order of operations, a description of control flow, **in our own
words**, in `docs/research/`, with provenance. Output does not cross: not into
`src/`, not into `docs/`, not into a commit message, not into a test fixture.
Check `git status --short reverse-engineering/` is empty before every commit.

**2. Flame is UNLICENSED.** Facts only. **Never write an identifier from Flame
into the repository** — not a function name, not a struct name, not a field name.
A Flame name may be *cited in a document* under ADR-0080 and may never appear in
code.

> **`dk2tools sgmap` now puts those identifiers one command away**, which is what
> makes this rule easy to break by reflex. The tool reads the map outside the
> repository and prints to the terminal; it writes nothing. Use it the way it was
> used first — **derive the offset yourself, then ask the map what it calls it**,
> so the map corroborates a reading instead of supplying one. A name that arrives
> that way is Flame's and stays in the terminal or, cited as Flame's, in a
> document.

**3. OpenKeeper is reusable with attribution** (ADR-0003's second amendment,
`THIRD-PARTY-NOTICES.md`) — **and its implementation is its guess, never
evidence.** The owner's standing instruction: *"reference it and compare with
our own findings, not copy it."* Q86 is the cautionary case: a slope taken from
OpenKeeper shipped, was tested, and was wrong.

**4. Numbers collide across worktrees.** ADR numbers and question numbers are
read from committed files, so two worktrees will pick the same next number.
**Check every worktree before taking one**, and prefer appending an answer to an
existing row over opening a new question.

## How the binary work is done here

Ghidra 12.1.4, headless, since 2026-09-25. Claude's project is
`D:\Ghidra\ghidra projects claude\DK2_RE` and Codex's is
`D:\Ghidra\ghidra projects\DK2_RE`; never share one between concurrent runs.

```
JAVA_HOME="C:/Program Files/Java/jdk-26.0.2.1" \
  "D:/Ghidra/ghidra_12.1.4_PUBLIC/support/pyghidraRun.bat" -H \
  "D:/Ghidra/ghidra projects claude" DK2_RE -process DKII.EXE -noanalysis \
  -scriptPath <dir> -postScript <Script.py> <args>
```

`-H` passes everything after it to `analyzeHeadless`. Add `-readOnly` for a run
that must not save. The launcher's venv is
`%APPDATA%\ghidra\ghidra_12.1.4_PUBLIC\venv`.

**Scripts are CPython 3 (PyGhidra), not Jython.** 12.1 ships Jython only as an
uninstalled extension. `analyzeHeadless.bat` with a `.py` script aborts with
`JythonStubException`, even with a `# @runtime Jython` header. Porting a
Jython script:

- `jarray.zeros(n, 'b')` becomes `from jpype import JArray, JByte` then
  `JArray(JByte)(n)`.
- `/` on two ints is float division in CPython. Use `//` for addresses and
  counts.
- Two Jython traps gave false negatives in the past, and neither applies to
  PyGhidra. A Python `bytearray` passed to `Memory.getBytes` silently stayed
  zeroed, so a byte scan reported 0 hits. Any non-ASCII character in a
  script, even in a comment, was a `SyntaxError`.

**12.1.4 was checked against 11.0.1 on 2026-09-25**, on copies of the project
opened read-only. The function count (10,519), references to `0x006DA8B0`,
`0x006DA8C6` and `0x00699688`, and a byte read at `0x0050EA70` were
identical. Four decompiles (the world log and the creature, room and state
dumps) say the same thing but read differently:

- String labels lose a leading underscore (`s__Seed__x_…` becomes
  `s_Seed__x_…`), and long labels are cut at a different point.
- `undefined` becomes `undefined1`.
- A byte or short read through an `int*` is written `(char)param_1[0x1c8c]`
  where 11.0.1 wrote `*(undefined *)(param_1 + 0x1c8c)`. Same byte, and the
  index is still ×4.

So a label copied out of an old 11.0.1 export may not match 12.1.4 output.

Helper scripts sit in `reverse-engineering/` alongside the exports. **Only
`README.md` there is tracked** (`git ls-files reverse-engineering`), so the
scripts below are local to a clone and are lost with it.

| Script | What it does |
|---|---|
| `ScanAll.py <out> <addrHex>...` | **use this one.** Raw scan of **every initialised block** for the little-endian bytes of an address, naming the block on each hit; sees indexed reads that Ghidra's reference analysis misses |
| ~~`ScanAddr.py`~~ | **superseded.** Same scan, hardcoded to `0x401000`–`0x652c00`, which is `.text` **only** — it never saw `cseg`, the image's second executable block (`0x653000`–`0x66bfff`, 100 KB of code) |
| `MaskScan.py <out> <hexConst>...` | every instruction whose text mentions a constant, with the containing function name. Pass the digits **without** `0x` |
| `CallScanAll.py <out> <addrHex>...` | every `E8`/`E9` rel32 whose target is the address, plus absolute occurrences — finds callers in unanalysed regions and vtable installs |
| `ForceAt.py` / `ForceFunc.py <out> <addrHex>...` | force a function at exactly this address, or walk back to the MSVC prologue first, and decompile |
| `Disasm.py <start> <end> <out>` | plain disassembly of a range — **use this when the decompiler garbles**, which it does around register-tracked table fills |
| `PreCall.py <out> <targetHex>` | the 12 instructions before every reference to a target, for reading pushed arguments |
| `CatTable.py <out> [start end]` | reconstructs a run-time-filled table by walking a straight-line `MOV` run and tracking registers |
| `AtAndCallers.py <out> <addrHex>...` | decompiles the function containing each address **and lists its callers**. The workhorse for a cadence question: how often something happens is a property of who calls it |
| `Up.py <out> <addrHex> <depth>` | walks the caller tree upward breadth-first. Stops dead on a vtable method (0 callers), which is itself the finding — scan the address next |
| `Intersect.py <out> <globalHex> <calleeHex>` | functions that reference a global **and** call a given function. The sharpest instrument here when you can name two things the answer must touch |
| `CallSlot.py <out> <slotHex> <globalHex>` | indirect calls through a vtable slot offset. An empty result is weak evidence — the dispatcher may reach its list through a member rather than a global |
| `ReadsAt.py <out> <sourceOffsetHex>` | every reference to a record offset. Matches instruction text, so it hits **stack** offsets too — check the base register before counting a hit |
| `Imports.py <out> <needle>...` | imported symbols matching a needle, with every reference site. `timeGetTime`, `QueryPerformanceCounter` and `GetTickCount` in one run locate the platform clock |
| `dk2tools kwd fieldsweep <creatures\|objects>` | the data half of a record sweep in one command; a new record type is about ten lines |

**Traps that have each cost this project time twice:**

- `Memory.getBytes` with a Python `bytearray` **fails silently** and leaves the
  buffer zeroed, which reads as "no hits" rather than as an error. Use
  `jarray.zeros(n, 'b')`.
- `MaskScan` matches constants as **substrings**: searching `0x2000000` also
  matches `0x20000000`. Filter the output, or you will read one bit's answer as
  another's.
- **`MaskScan` prepends `0x` to its argument.** Pass `68a`, not `0x68a` — the
  latter searches for `0x0x68a` and **reports zero hits rather than an error**.
  A zero-hit run was once taken as evidence that a region was unanalysed, and
  the conclusion *"reached only through a vtable"* was built on top of it; the
  same query run correctly returned 114 hits including twelve unrecorded
  callers. **A scanner that reports zero rather than failing is the most
  expensive kind of bug there is** — check any zero-hit run against a constant
  you know is present before believing it.
- **A scan bounded to `.text` misses 100 KB of code.** The image has two
  executable blocks, `.text` and **`cseg` at `0x00653000`–`0x0066BFFF`**. Every
  reader count taken before 2026-09-07 is a claim about `.text` alone.
- **Always put a positive control in the same run.** Cheapest here is variable
  19's global `0x006F2590`: it must return **226** hits. A run whose control
  comes back low or empty is a broken harness, and you find that out in the same
  output rather than three conclusions later.
- **Watch for the second kind of inert** (ADR-0085): a variable with a live
  reader and a shipped value that makes it a no-op is **disabled**, not cut.
  A zero is a reason to look harder, not to stop.

### Two facts about the executable worth knowing up front

- **Tables filled at run time cannot be read from the file.** The 281-record
  creature state table (`0x006DA8B0`, stride 25) and the 32-record
  state-category table (`0x00699688`, stride `0x40`) both live in uninitialised
  `.data` and are filled by long straight-line `MOV` runs belonging to no
  function. Recovering one means walking the disassembly tracking register
  values — `CatTable.py`.
- **Indexed reads carry the base constant.** A stride of 25 compiles to
  `[EAX + EAX*4 + base]` with `EAX` pre-multiplied by 5, so an address scan for
  a table slot really does find every read of it — which is what makes "written
  once and never read" a provable statement (Q184).

---

## Tranche A — the cheap reader scans

**Method:** take the variable's cached global out of
`balance-variables-in-the-binary.md`, run `ScanAll.py`, then `MaskScan.py` to
name the containing routine, then decompile it. An hour or two each.

| Q | Question | Starting point |
|---|---|---|
| **Q160** | What is `TimeInHandBeforeCreaturesBecomeAngrySeconds` = 1000 for? | `0x006F2977`. It is **not** the hand's grace period, which the original hardcodes as `PickupTick + 20 × variable 19`. 1000 seconds is over sixteen minutes, so it is ticks, dead, or not a duration |
| **Q168** | How often does an overcrowded prison roll its jail break? | `OvercrowdingJailBreakPercentagePerExtraCreature` at `0x006F2920` has **one** reader already counted. Ours rolls once a game second, which makes two over capacity a certainty on the first roll. Also has an observation route |

## Tranche B — field-level questions inside parsed records

Pure file work with OpenKeeper's field lists and Flame's symbol map as
cross-checks; touches no simulation code. The tools are already there:
`dk2tools kwd recscan`, `creaturescan`, `creaturehex`, `objecthex`, `fieldsweep`.

| Q | Question |
|---|---|
| **Q70** | What are the last **72 bytes** of a CREATURES record? `KwdCreatures` reads nothing past `0x1081`, and every offset it does read was confirmed against OpenKeeper's cumulative field order, so nothing built today is at risk |
| **Q14** | What are the **88 extra bytes** in the embedded 5537-byte CREATURES variant? |
| **Q15** | What is the `THINGS(190)` chunk inside `GlobalVariables.kwd` (39 items) for? |
| **Q85** | What do the two trailing fields on every VARIABLES record mean? In a level file almost every entry carries `unk1 = 13, unk2 = 1` while `GameTicks`, `LevelRating` and `AverageTime` carry `0, 0` — that is a grouping. Correlate across all 214 records and all 66 level files |
| **Q84** | What are variable ids **66** and **77**? Both appear only in per-level files; level 1 has 66 = 3 (`unk1` = 2) and 77 = 29 (`unk1` = 3, `unk2` = 125). Low priority |
| **Q183** | What is in the fixed block between a map's header (ends `0x134`) and its grid (`0x8861`)? Partly cracked — the player's gold is at `0x2dd4`. Whether the rest is identical across all 66 maps is itself informative |
| **Q120** | Which objects belong to which room? `Rooms.kwd` carries an `ObjectIds` list per room, parsed and read by nothing; `dk2tools kwd objects` prints a partial map. **Q169** is the follow-on when two rooms list the same object |
| **Q129** | Is the Dungeon Heart's health **34464** or a truncated **100000**? 34464 is exactly 100000 − 65536, the signature of a five-digit number typed into a 16-bit field — the same arithmetic already seen once (Q55) |
| **Q221** | Is the 16-byte DeadBody THINGS payload correct in retail data? |
| **Q220** | Does a standalone `GLOBALS(0)` chunk exist, and if so what is its payload? |

### The record sweeps, and which are done

| record | scalar fields | swept | result |
|---|---|---|---|
| balance variables (240 ids) | — | **yes** | 12 zero-shipped: 2 cut, 10 disabled; ADR-0085, amended (id 112 is live at zero) |
| **CREATURES** (46 records) | 56 | **yes** | 2 zero everywhere, 4 constant, 3 fields **cut** with no consumer; `record-field-sweep.md` |
| **OBJECTS** (145 records) | 21 | **data yes, binary no** | `ManaValue` zero in all 145; consumer unknown, and it needs the objects loader |
| ROOMS, TERRAIN, DOORS, TRAPS, SHOTS, SPELLS, EFFECTS | — | no | the same two questions apply to each: which fields never vary, and does the engine read them |

**Taking a field from a file offset to a runtime offset** — what the binary half
of a sweep needs, and what used to be guesswork:

1. the creature loader is `FUN_00492320`, and **source offset = file offset +
   `0x3F0`** (confirmed on two fields at opposite ends of the record);
2. `ReadsAt.py <sourceOffsetHex>` finds the loader's read;
3. the **store** on the next line or two gives the runtime offset;
4. `ReadsAt.py` on *that* offset lists every reference — and three of them are
   never consumers: the initialiser `FUN_00491DC0`, the loader itself, and the
   definition-copy routine `FUN_0051F3B0`.

Each record type has its own source delta. The objects one is not `0x3F0` and is
unknown; find it the way the creature one was found — scan for stores to a
runtime offset you can identify at both ends, then read the source offset copied
into it.

## Tranche C — the meaty single questions

**Q186 — the rest of the state-category record.** All 32 records are decoded
except `+0x34`, an allowed-category **bitmask** whose use is unread, and
`+0x3c`, a small integer. `CatTable.py` reconstructs the table; the question is
what consults those two. **Transition legality lives here**, and a
reimplementation that lets a creature move between states the original forbids
will drift with nothing to catch it.

**Q202 — anger buckets 0 and 5.** Six counters, four certain because exactly one
record field feeds each. Buckets 0 and 5 are catch-alls and **their names in our
code are ours**. All 60 call sites are tabulated in `creature-anger.md`; the
work is finding what distinguishes the two groups. Also has an observation route.

**Q204 — two unidentified flags in the job path.** (a) `player+0x76 & 4` in the
dispatcher restricts a creature to jobs 10 (Leave) and 16 (Rebel) and nothing
else — a switch that turns a keeper's whole workforce mutinous, and it smells
like a keeper whose heart has fallen. (b) `def+0x686 & 0x4000` diverts a chooser
to state `0xEE`, is per-creature-type, and can be checked against all 46
records. Both self-contained.

**Q188 — when does a prisoner's step roll halve?** State 116 returns to the
decision state on `random(100) < 20`, but down one of two paths: when
`FUN_00494A00(creature, 9)` is non-zero it instead requires 10..19, halving the
rate. `FUN_00494a00` reads bit 0 of `creature + 0xFE`, an entry in a table of
27-byte records indexed by a small id. Read that predicate.

**Q217 — what sets the keeper's casino flag at `player+0x20D`?** The research
adder's first bonus is gated on a 16-bit field beside the research accumulator
being non-zero, and the percentage comes from
`IncreasedWorkRatePersentageWhenCasinoBigWinOccurs`. The only evidence is a
variable's name. Scan for stores to `[reg + 0x20D]`.

**Q218 — what passes DK2's research adder a negative amount?** `FUN_004c2dc0`
has a full negative branch — clamp-subtract from the accumulator, floored at
zero, skipping every bonus — and its only caller is the category-4 hook, which
can only pass a non-negative value. So either a second caller exists or the
branch is dead. `CallScanAll.py` on `0x004c2dc0`, and remember that a `.text`-only
scan is how ADR-0086's "reached only through a vtable" went wrong.

## Tranche D — the trigger and action-point frontier

Records parse and their state banks exist (ADR-0092, ADR-0102, ADR-0103);
**nothing evaluates a condition or runs an action**, and these are what stands
between the corpus and execution. All are binary or shipped-file work.

| Q | Question |
|---|---|
| **Q225** | Do DK2-authored files corroborate the trigger types absent from the shipped corpus? Four unshipped action types have no body read |
| **Q226** | What do the remaining transcribed trigger selectors mean? |
| **Q227** | What are the trigger fields DKII.EXE reads without revealing a role? |
| **Q229** | Flag id 128 is **not a flag** — both dispatchers compare the id against `0x80`. What is it? |
| **Q230** | What does DK2 do when a trigger names a flag id above 128? The write path masks the id |
| **Q232** | Timer id 16 is **not a timer** — the condition path compares the id against `0x10`. What is it? |
| **Q235** | What do action-point flag bits 0, 2, 3, 4 and 5 mean? The corpus authors `0x7D`; bits 1 and 7 rest on the disassembly alone |
| **Q236** | Who consumes or mutates the passage counter and the wait timer? |
| **Q237** | When is an action-point rectangle tested, and what is its actual per-tick cost? |
| **Q238** | How is `NextWaypointId` traversed, terminated, and handled when missing or cyclic? |
| **Q223** | When a ROOM record and the MAP grid disagree about who owns a structure, which does DK2 believe? |
| **Q224** | What state does a door or a trap need, and is `DoorThing.Flags` the locked bit? |

## Tranche E — needs Flame specifically

Runtime structures, so the file data cannot answer them and the decompiler alone
is slow going. **Facts only — never an identifier into the repo.**

| Q | Question |
|---|---|
| **Q4** | `CMapEntry` (11 bytes) — full per-tile runtime state or a cache? Field meanings |
| **Q5** | **ANSWERED architecturally** (ADR-0155, `research/navigation-and-steering.md`): a triangulated navigation mesh plus a steering system, so our A* cannot match the original's routes in general. `Follow_Path` held no path data. Now worked as Q341–Q355, and rebuilt as ADR-0157: all three phases landed, so the mesh router and the steering tick move every creature |

---

## What is *not* on this page, and why

Anything that needs the game running or the owner watching lives in
`docs/research/observation-checklist.md`: Q35 the treasury fill order, Q49 the
pause after claiming, Q57 running out of mana, Q62 the reinforce application
count, Q75 the starting stockpile, Q89 and Q61 attraction below recommended
size, Q97 why arrivals slow, Q102 the hunger clock's start, Q103 the sleep
cycle, Q138/Q140 the floor lump, Q143, Q145, Q177, Q199 and Q216.

**Combat (Q8) is deliberately absent.** It is the largest unread mechanic in the
game and an M7 milestone, not a research errand.
