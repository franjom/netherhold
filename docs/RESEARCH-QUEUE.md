# Research queue: questions answerable without the running game

**Purpose.** Open questions that need only the **binary**, the **shipped data
files**, **Flame** or **OpenKeeper** — no owner observation, no stopwatch, no
play session. They can be worked in parallel with feature work, because none of
them touches `src/`. The other half of the split is
`docs/research/observation-checklist.md`.

Every row here must still be open in `docs/research/open-questions.md`, which is
the one home for a question's status. This page carries only the **starting
point** for the work; last checked against that file on 2026-10-08. To find the
newest question id before taking a number:

```sh
grep -oE '^\| \*{0,2}Q[0-9]+' docs/research/open-questions.md \
  | grep -oE '[0-9]+' | sort -n | tail -1
```

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

Ghidra 12.1.4 at `D:\Ghidra\ghidra_12.1.4_PUBLIC`, headless, since 2026-09-25.
There is one project, `D:\Ghidra\DK2_RE`; read-only runs clone it per run and
delete the clone at exit, so concurrent runs do not collide. Run scripts through
the runner, and query interactively through the pyghidra-mcp server:

```
python tools/ghidra/ghidra.py --agent <you> <Script.py> <out> <args> [:: <next step> ...]
```

`tools/ghidra/mcp-serve.py --agent <you>` is the server the client spawns. Runs
are `-readOnly` unless `--write` is given. The runner calls
`pyghidraRun.bat -H` underneath; the launcher's venv is
`%APPDATA%\ghidra\ghidra_12.1.4_PUBLIC\venv`. The `ghidra-queries` skill
(`.agents/skills/ghidra-queries/SKILL.md`) holds the script catalogue.

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
- **Put every loop inside a function (`main()`).** PyGhidra runs a script
  with a `PyGhidraScript` mapping as its globals, so every module-level name
  lookup is slow. The same 300k-iteration loop takes 5.3 s at module level and
  0.46 s inside a function. `InsGrep` took 51 s at module level and 2 s inside
  `main()`. This, not CPython versus Jython, was most of the first-port
  slowdown.
- **Never index a Java array element by element.** Every access crosses into
  Java. Call `bytes(buf)` right after `getBytes`: it copies 2.4 MB of `.text`
  in about 1 ms. Then use `bytes.find`, `re.finditer` or slice assignment
  rather than a per-byte loop.
- **Walk the listing through `insdb`, not through Java.** Even inside a
  function, a walk calling `toString()` on all ~743k instructions takes
  7–20 s. `ListingDump.java` writes the whole listing (address, containing
  function, mnemonic, text, operand 0, scalar values) to
  `reverse-engineering/.cache/` in about 5 s, natively. `insdb.load(currentProgram,
  runScript)` rebuilds it when the program changes and streams it back in
  about 1 s. The key is the project, program, save time, modification number
  and instruction and function counts.

All 39 local helper scripts were ported on 2026-09-25, and Codex's four of its
own the same day. Each was run under both versions with the same arguments,
and every output matched except those that print decompiler output or string
labels. Timings from a warm cache: the 15 listing walks take 1–3 s each
(Jython under 11.0.1: 1–8 s; first 12.1.4 port: 10–50 s). The byte scans take
1–2 s (first port: up to 158 s). All 38 run in one headless call in about
75 s, including the cache build and Ghidra's start-up. Those that differ show
only the decompiler's presentation, listed below. 12.1.4 also sometimes merges
adjacent stack locals into one array (`auStack_58[3]`) and adds a "type
propagation not settling" warning on `FUN_005099a0`.

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

Tracked helper scripts sit in `tools/ghidra/scripts/`. A script below that is
missing there (`CatTable.py`, for one) is local to a clone in
`reverse-engineering/`, where only `README.md` is tracked.

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

Empty. **Method** for a new one: take the variable's cached global out of
`balance-variables-in-the-binary.md`, run `ScanAll.py`, then `MaskScan.py` to name
the containing routine, then decompile it.

## Tranche B — field-level questions inside parsed records

File work with OpenKeeper's field lists and Flame's symbol map as cross-checks;
touches no simulation code. The tools are `dk2tools kwd recscan`, `creaturescan`,
`creaturehex`, `objecthex`, `fieldsweep`.

| Q | Question |
|---|---|
| **Q15** | What are the `THINGS(190)` chunk's 39 items in `GlobalVariables.kwd` for? The payloads parse (ADR-0091); their purpose is not recorded |
| **Q84** | What is variable id **77**? Nine levels, mostly `Secret` ones. Id 66 is strongly inferred as the level's player count; the unparsed PLAYERS record would close it. Low priority |
| **Q183** | What else is in the save's fixed block between the header and the grid? The player's gold is at `0x2dd4`; the rest is unread |
| **Q220** | Does a standalone `GLOBALS(0)` chunk exist outside the shipped corpus? None in 408 editor files |
| **Q221** | Is the 16-byte DeadBody THINGS payload right? No shipped record carries tag 202; needs a custom map |

### The record sweeps, and which are done

| record | swept | result |
|---|---|---|
| balance variables (240 ids) | **yes** | 12 zero-shipped: 2 cut, 10 disabled; ADR-0085, amended (id 112 is live at zero) |
| **CREATURES** (46 records) | **yes** | 2 zero everywhere, 4 constant, 3 fields **cut** with no consumer; `record-field-sweep.md` |
| **OBJECTS** (145 records) | **yes** | `ManaValue` zero in all 145 and cut (Q213) |
| ROOMS, TERRAIN, DOORS, TRAPS, SHOTS, SPELLS, EFFECTS | no | the same two questions apply to each: which fields never vary, and does the engine read them |

**Taking a field from a file offset to a runtime offset** — what the binary half
of a sweep needs:

1. the creature loader is `FUN_00492320`, and **source offset = file offset +
   `0x3F0`** (confirmed on two fields at opposite ends of the record); the
   objects loader is `FUN_0049DD60`, with file offset + `0x80`;
2. `ReadsAt.py <sourceOffsetHex>` finds the loader's read;
3. the **store** on the next line or two gives the runtime offset;
4. `ReadsAt.py` on *that* offset lists every reference — and three of them are
   never consumers: the initialiser `FUN_00491DC0`, the loader itself, and the
   definition-copy routine `FUN_0051F3B0`.

Each record type has its own source delta. For a new type, find it the way these
were found: scan for stores to a runtime offset you can identify at both ends,
then read the source offset copied into it.

## Tranche C — the trigger and action-point frontier

`TriggerExecutor` evaluates conditions and runs actions (ADR-0152, ADR-0235,
ADR-0236; shipped type counts: `dk2tools kwd trigger-types`). What is left is the
meaning of selectors and fields the binary reads without naming. All are binary
or shipped-file work.

| Q | Question |
|---|---|
| **Q225** | Do DK2-authored files corroborate the trigger types absent from the shipped corpus? Needs custom editor maps |
| **Q226** | What do the remaining transcribed trigger selectors mean? Accesses are known, names are not |
| **Q235** | What do action-point flag bits 2 and 3 mean? Bits 0, 4 and 5 have a direct reader; 2 and 3 have none found |
| **Q237** | Which invokers other than action 17 walk an action-point rectangle? |
| **Q238** | How is `NextWaypointId` traversed when missing or cyclic? Strongly inferred only; no post-load writer found |
| **Q186 / Q300** | What is the state-category record's `+0x3c` for? Next step in `state-categories.md` §5 (the caller at `0x004BFBD0`) |

## Tranche D — needs Flame specifically

Runtime structures, so the file data cannot answer them and the decompiler alone
is slow going. **Facts only — never an identifier into the repo.**

| Q | Question |
|---|---|
| **Q4** | What generates the 225-entry line-of-sight table at `map+0x124` (slot ranges, sort key, byte `+0`)? The tile record itself is answered (`runtime-map.md`) |

---

## What is *not* on this page, and why

Anything that needs the game running or the owner watching lives in
`docs/research/observation-checklist.md`. **Combat (Q8)** is built for melee
(ADR-0163 onward); it is no longer a research errand.
