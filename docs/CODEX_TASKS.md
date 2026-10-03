# Briefs for a second agent

The owner has OpenAI Codex available alongside Claude. This file holds the tasks
worth giving it, phrased so they can be pasted directly.

## The division that makes sense

**Codex has the same reach as Claude here.** `~/.codex/config.toml` carries
`pyghidra-mcp` (DKII.EXE, on its own clone of the canonical Ghidra project) and
`dk2kb` (the function records); it reads `AGENTS.md` and `docs/TRAPS.md`; and
since 2026-09-25 it has landed simulation slices with their ADRs, hash bumps and
commits: the fog reveal, the keeper's threat evaluation, Thunderbolt, Call to
Arms, the battle step and the pickup gates. So the split is by **independence**,
not by kind of work.

- **Give Codex** a slice one worktree can finish without editing files another
  session is in: a format task outside the simulation, a self-contained mechanic,
  a read-only audit, or footage.
- **One agent at a time, whoever it is:** `CategoryHooks`, `ImpTaskSystem`,
  `GameSimulation`'s hash, the replay rigs' scratchpad drivers, and anything that
  waits on an owner capture. Numbers can be coordinated across worktrees; a merge
  conflict in those files is the real cost.
- **What Codex lacks:** Claude's private memory (anything it needs must be in the
  repository, which is why `docs/TRAPS.md` exists) and Claude's `reviewer`
  subagent. A Codex brief has its own verifier read `.claude/agents/reviewer.md`
  and apply that method; Brief 10 below is the template for a simulation slice.

**Ranked by what they have actually produced:** Brief 4 (reversed shipped
behaviour three weeks old), the M12 keeper slices (2026-09-28/29), Brief 1 (the
characteristic-defect audit), Brief 8 (fog of war, via its own stop rule),
Brief 6 and Brief 5 (two shipped tables nothing read), Brief 3 (corroboration).

## Setup

Codex reads `AGENTS.md`: the rank of evidence, the ADR-0046 quarantine, the
recording budgets and the twelve goals are all there. It reads `docs/TRAPS.md`
before long work and adds a line there when something costs it time.

The gate is the same for work from any source, and must be green before anything
lands (`docs/HANDOFF.md` §"Run and verify" is the canonical list):

```
dotnet build DK2.slnx -c Release                            # zero warnings
dotnet test  DK2.slnx -c Release --no-build                 # green; expect skips
dotnet build unity/DK2.Unity.Compile/DK2.Unity.Compile.csproj -c Release
dotnet run --project src/DK2.Tools -c Release --no-build -- verification-ledger --check
dotnet run --project src/DK2.Tools -c Release --no-build -- kb check
dotnet run --project src/DK2.Tools -c Release --no-build -- coverage --check
dotnet run --project src/DK2.Tools -c Release --no-build -- docs --check
dotnet run --project src/DK2.Tools -c Release --no-build -- status --check
```

Run `status` (to regenerate the plan's line) after the tests, never after a bare
build. `--no-build` can run stale dependency DLLs after a one-project build, so
build the solution first.

Expect **skips** in the test run: every `[InstallFact]`/`[InstallTheory]` test
skips without a DK2 install (ADR-0100). A skip count of zero means the runner
found game data it should not have.

Anything that **writes** gets its own git worktree on a `codex/<topic>` branch,
with the hooks installed (`git config core.hooksPath .githooks`). Read-only
auditing can run against the main tree any time. Before taking an ADR number, a
question number or a `HashVersion`, check master, `git worktree list` and each
worktree's `DECISIONS.md`, `open-questions.md` and `hash-versions.md`: two
worktrees pick the same next number, which is why `docs/DECISIONS.md` is not in
numeric order. `kb_upsert` writes the main tree, not your worktree; in a worktree,
edit the record by hand, then `kb check` and `kb index` there.

**Reference checkouts, both outside this repository:** `D:\OpenKeeper` (GPLv3 —
facts only, provenance in `THIRD-PARTY-NOTICES.md`) and `D:\Flame`
(**unlicensed — facts only, and never one of its identifiers in `src/`, `tests/`
or `unity/`**). See `docs/research/external-resources.md`.

## Boundaries that apply to every brief

- **The quarantine is absolute, and it is a licence boundary rather than a style
  one** (ADR-0046). Facts — a number, a formula, a comparison, an order of
  operations, a description of control flow, **in your own words** — may cross.
  Decompiler output may not: not into `src/`, not into `docs/`, not into a commit
  message, not into a test fixture, not reformatted, not renamed, not "cleaned
  up".
- **Rank of evidence, in this order** (AGENTS.md): Ghidra on `DKII.EXE` first —
  it is the actual DK2 and everything else is somebody's reading of it; the
  shipped data files; the running game, which outranks a misread disassembly for
  anything a player can watch; then Flame for **structure only**; then
  OpenKeeper, whose reading is its own guess and has been wrong here before.
- **Research prose follows `AGENTS.md`**: a DK2 fact goes in one section of one
  `docs/research/` note with its confidence tag and evidence, and every DKII.EXE
  function read gets its record in `docs/research/functions/`. Footage analysis
  (Brief 4) is the exception: it produces input, not prose.
- **A moved hash follows the protocol**: `HashVersion` and `FormatVersion`
  bumped, a `hash-versions.md` row and the `DeterminismTests` literal, all in the
  commit that moved it, with the scenario or seed that moved named and a
  prediction written before measuring. A read-only brief that finds it needs one
  stops and reports; a correct stop is a successful outcome.
- **Commits**: stage named paths only (never `git add -A`; another session may
  be editing), trailer `Assisted-by: <model>` (e.g. `Assisted-by: GPT-6`), never
  `Co-Authored-By`, never `--no-verify`. Do not merge into master: the owner
  merges.
- **Verify before reporting.** A brief that writes ends with an independent
  verifier on the whole diff (`.claude/agents/reviewer.md`'s method, its own
  Ghidra copy); fix what it confirms, give evidence for what you reject, and
  re-run the gate.
- **Ask the owner** about observable DK2 behaviour before recording it as
  UNKNOWN; they have played it.
- **Do not invent DK2 behaviour.** If no source shows it, it is UNKNOWN and the
  code says so. **Report a field you could not pin rather than filling it in** —
  the most useful output any of these briefs has produced was a negative result.
- **No decoded game text or converted asset committed anywhere.** The format
  facts and the readers are ours; the content is EA's. Tests read it from the
  install at run time and skip when `DK2_DIR` is absent.

---

## Brief status

| Brief | Task | Deliverable in the tree | Status |
|---|---|---|---|
| 1 | The unverified-claim audit (read-only) | a ranked findings report to stdout; no artefact | **Standing** — re-runnable, and the highest-value read-only use |
| 2 | Q132, the phantom balance variable | answered from the binary and the level files; built as the hatchery clock (ADR-0066) | **Closed 2026-09-05** |
| 3 | Independent re-derivation (cross-check) | corroborated the hatchery laying exponent and the balance-variable dispatch table | **Standing** |
| 4 | Turn a recording into a question list | the 2026-09-05 document, which reversed the free-imp rule (Q56) | **Standing** — the highest-value brief here |
| 5 | The `.str` string tables | `src/DK2.Data/Text/Dk2StringTable.cs`, `Dk2StringTableTests`, `dk2tools str list` / `str get`, `src/DK2.Data/README.md` §STR | **Done 2026-09-06** |
| 6 | `Doors.kwd` and `Traps.kwd` | `KwdDoors.cs`, `KwdTraps.cs`, `DoorDef`, `TrapDef`, loaded by `GameDatabaseLoader`; `DoorsAndTrapsDefinitionsOnRealDataTests`, `TrapsOnRealDataTests`, `src/DK2.Data/README.md` §"Door and trap definitions" | **Done** |
| 7 | Fog of war — read-only research | absorbed by Brief 8; `docs/research/fog-of-war.md` | **Done** |
| 8 | Build fog of war | its stop rule fired: fog is **simulation** state (Q190 answered, ADR-0076). `FogOfWarSystem`, a discovery bit per keeper on `TileState`, hashed and snapshotted, the viewer-aware presentation boundary, `scenarios/fog/`, `FogOfWarOnRealDataTests` | **Done** |
| 9 | KWD/KLD writers from the typed model (goal 7) | — | **Open** (2026-10-03): outside the simulation, nothing blocks it |
| 10 | The remaining keeper spells (M8) | — | **Open** (2026-10-03): six of thirteen spells are built |

Merged Codex branches: `codex/doors-traps`, `codex/fog-of-war`, `codex/fog-reveal`
(Q429, Q430, Q434), `codex/m7-doors-traps`, `codex/m1-record-types` (all 18 KWD
record types, ADR-0091), `codex/trigger-semantics` (trigger parameter reads
verified against DKII.EXE, ADR-0092) and `codex/action-points` (action points as
ordered world state, ADR-0103). Landed outside a brief: the M12 keeper slices of
2026-09-28/29 (threat evaluation, message retention, Thunderbolt, Call to Arms,
the battle step, pickup gates, casualty priority) and the 2026-09-30 tooling
(pyghidra-mcp per agent, one record per function).

**Other independent work worth a brief** (2026-10-03, not yet written up): the
simulation speed budget (`GOAL_ALIGNMENT.md` gap 4: `RunTimings` measures, nothing
asserts); loading converted glTF art in the Unity view and catalogue entries for
sound and music (goal 4); specials beyond Increase Level (`specials.md`); building
Vampire reincarnation (Q111 is read, Q329 open, needs Q165's Graveyard); one unbuilt
room-state category at a time (Casino, Research, Temple, Training, Manufacture:
never two in `CategoryHooks` at once); M12 message producers on Brief 10's pattern.

---

## Closed briefs — kept for the method, not the task

### Brief 2 — Q132, the phantom variable — CLOSED, do not pick up

Balance variable 19 is **ticks per game second**, and the **level header**
supplies it, which is why it appears in no variables file; all 66 shipped levels
carry 4. The shape of the brief was right, and it is why the answer was findable:
everything eliminated had been written down first — 223 reading instructions,
exactly one apparent writer, uninitialised memory with no image default, and no
shipped file supplying id 19 across all 1,236 files with a scanner validated
against ids 3, 18 and 74. Give a brief like this **the question and the
eliminations**, never the write-up.

### Brief 5 — the `.str` string tables — DONE

92 files under `Data/Text/Default/`, 353 KB, all sharing the magic **`BFST`**.
`+4` is a **file id** — the 92 files carry 1000 … 1091, one each, in a fixed
order — and **string ids are table-local and zero-based**; reading `+4` as a
string *base* was the brief's own wrong "already established" line, and the
branch was right to check it. `+8` is the count, and the 92 counts sum to
**7,024**. At `+12`, a table of `count` u32 offsets, monotonically
non-decreasing and **relative to byte 12**. The payload is encoded rather than
plain text: no ASCII or UTF-16LE run of six printable characters exists anywhere
in `Text.str`, and 176 distinct byte values with `0x00`, `0x0C` and `0xB0`
dominant is the shape of a bit-packed or dictionary scheme. `dk2tools str get`
resolves a `NameStringId` from a creature, room, object or terrain record to the
game's own name, which is the verification that mattered.

A long shot in that brief paid off in the negative direction, which is worth
keeping: the `u16` at record `+0x14` of DK2's 281-record creature **state table**
*is* a `Text.str` id and all 281 resolve — but to only **63 distinct labels**,
shared status text describing state *families* rather than naming states one by
one (`docs/research/creature-state-table.md`). It closes a line of enquiry that
looked like it would name the whole state machine for free.

### Brief 6 — Doors.kwd and Traps.kwd — DONE

`Doors.kwd`: 8 records, stride 616. `Traps.kwd`: 16 records, stride 579. Both
carry a single chunk whose name matches the file; content offset is
`fileLength − records × stride`, which for `Doors.kwd` is 56. Each record opens
with a 32-byte NUL-padded name (`Wooden Door`, `Steel Door`, `Braced Door`), and
bytes 32..56 are zero.

The method is the reusable part: **the shipped values are the oracle.** Dump each
candidate offset as u8/u16/u32 across all 8 or 16 records and look for a column
that reads like a stat — monotonic with the name order, plausibly scaled, or
matching something the manual prices outright. Generate candidates from
OpenKeeper if you like; confirm them against the shipped bytes yourself. **No
behavioural rules** came out of it: this was data, and how a trap fires needs the
binary.

### Brief 7 — fog of war research — DONE, absorbed by Brief 8

`docs/research/fog-of-war.md` is the note.

### Brief 8 — build fog of war — DONE, by the other branch of its own stop rule

The brief said: build presentation-side unless Phase 0 shows fog is simulation
state, and if it does, **stop and report rather than bumping the hash**. Phase 0
showed exactly that. Q190 was answered from a three-save series on a four-keeper
skirmish map: DK2 persists discovery in the save, so it is game state, and
diffing all 80 bit positions of the save's tile record puts it at **lane 1 bit
6**, clearing in a band 13 tiles tall around a one-tile tunnel — the six-tile
`PerceptionRange` arriving independently. **DK2 stores one bit for one
viewpoint**; the per-keeper mask here is ours (`fog-of-war.md` §§10–11).

What the brief got right and is worth reusing:

- **The stop rule.** It fired, and the work landed correctly on the other side of
  it (ADR-0076) instead of being built in the wrong place and moved.
- **The cheap route before Ghidra.** "If fog state is written into a save, it is
  game state, because a save persists the simulation" — two saves either side of
  walking one creature into unexplored ground, with a control that changed
  nothing else. Minutes, against an afternoon of disassembly.
- **Fog by substitution**, because that is what the original does: an
  undiscovered tile is *presented as* terrain 27 (`Fog Of War`, flags
  `Solid, Impenetrable, Taggable`, health 1/1), the real terrain never reaching
  the renderer — which is also the test that no renderer can leak it. The six
  terrain types flagged `RevealThroughFogOfWar` (terrain flag bit 24:
  Impenetrable Rock, Water, Lava, Gold, Gems, Edge of Map) show through.
- **The gap the brief exposed was not fog.** `WorldView` had no viewing player
  and `TileView.Tagged` meant "tagged by anyone", so every renderer was
  omniscient by construction. Fog made that visible; the fix was a defect
  repair, not a feature.

Still open from it: the five fog modes in `Text.str` 348–353 (`Creatures Reveal`,
`All Owned Reveal`, `Stay Revealed`, `Re-shroud`, `Re-fog`) are inferred from
strings, with `Stay Revealed` the campaign's confirmed behaviour and no known
selector; and whether a tag on an undiscovered tile is stored, and where. Sight
of Evil and Reveal Map are M7 and M6. No sight radius exists in the balance
table — `DefaultTorchLightRadiusTiles` 6144 is **1.5 tiles** (`Fp32.One` is
4096), a near-miss that was checked and rejected; do not resurrect it.

---

## Brief 1 — the unverified-claim audit (read-only, run this first)

The highest-value read-only use, because it targets this project's characteristic
defect: a claim everything is consistent with that nobody ever checked. Three
real instances are given as calibration, because the pattern matters more than
the instruction.

Expect some false positives on claims that *are* checked but not near the text
making them — the drift guards live in `DK2.Behavior.Tests` with no project
reference back to the fixtures they guard, deliberately.

````text
You are auditing a C# project that reimplements Dungeon Keeper 2 (1999, v1.70)
as a behaviourally faithful clean-room reimplementation. The original game is
the oracle: the goal is not working code, it is code that behaves like DK2.

This project has a characteristic defect, and your job is to find the next
instance of it. The shape is: A CLAIM ABOUT DK2 THAT EVERYTHING IS CONSISTENT
WITH AND NOBODY EVER CHECKED.

Three real instances, for calibration:

1. "A corpse is an OBJECT" — written in an architecture decision record, a
   research document, and several code comments, as the NAMED CAUSE of three
   missing features. Survived about five working sessions. It is false: none of
   the game's 145 object records is a corpse. The disproof was already in data
   the project had parsed — both relevant timers are *creature* variables and
   the decompose value is a *creature* field.

2. "DK2's creature state machine is a class hierarchy with about 25 leaves" —
   inferred from 25 source filenames ending in State.cpp. False: the binary's
   RTTI carries 163 class names and exactly one of them is a state.

3. "MaxFreeRangeChickensPerPlayer (12) caps a hatchery's flock" — plausible,
   named exactly right, and the only limit anyone had found. False: a 5x5
   hatchery holds 25 chickens. The real limit was a RoomCapacity field of 1 in
   a record the project had already parsed and never read.

What those three share: each was internally consistent, none was ever
contradicted by anything else in the project, and in every case THE DISPROOF WAS
ALREADY SITTING IN DATA THE PROJECT HAD. Consistency is not evidence.

SCOPE
- Read: docs/**/*.md and src/**/*.cs.
- Read tests/ only to determine whether a claim is asserted somewhere.
- The shipped game data is readable at "E:\GogGames\Dungeon Keeper 2" — use it
  to settle claims about file contents directly where you can.
- DO NOT read, open, quote or reference anything under reverse-engineering/.
  That folder is a licence quarantine and its contents may not leave it.

WHAT COUNTS AS A CLAIM
Any statement about how DK2 behaves, or about what the shipped data contains.
That includes [Dk2Behavior(Confidence.…)] attributes, prose in documents, and
code comments that justify why a rule is written the way it is.

WHAT COUNTS AS CHECKED — a claim is NOT a finding if any of these holds:
  (a) a test asserts it;
  (b) a test reads it back out of the shipped game files;
  (c) a document quotes the project owner observing it in the running game;
  (d) a document cites a specific, named read of the game binary.
A confidence label is NOT a check. The label is itself a claim.

Prefer claims about BEHAVIOUR over claims about file layout — file layout
already has guard tests, behaviour largely does not.

FOR EACH FINDING, REPORT
  - the claim, quoted, with file:line
  - what else depends on it (this determines its rank)
  - what evidence, if any, is actually offered for it
  - WHAT WOULD BE OBSERVABLY DIFFERENT IN THE GAME IF IT WERE FALSE.
    If you cannot answer this, discard the finding — it is not load-bearing.
  - the single cheapest way to settle it: a grep over the shipped data, a test
    to write, or one specific thing to watch in the running game.

OUTPUT
A markdown report to stdout. At most 15 findings, ranked by how much rests on
them, most load-bearing first. Fewer is better than padded: a short list of real
ones is the deliverable; a long list of stylistic quibbles is worthless.

DO NOT edit any file. DO NOT commit. DO NOT open a pull request.

OUT OF SCOPE: code style, naming, performance, test coverage as a metric, and
anything that is about C# rather than about Dungeon Keeper 2.
````

---

## Brief 9 — KWD/KLD writers from the typed model (goal 7)

The level editor's first half. Readers exist for all 18 record types, and
`KwdRecordRoundTrip` proves every shipped record image byte for byte, but
nothing turns a typed record back into bytes (`GOAL_ALIGNMENT.md` row 7). It
touches no simulation state, so no hash, and no DK2 behaviour is decided.

````text
Build writers that turn this project's typed KWD/KLD records back into DK2's
on-disk bytes. Read AGENTS.md, docs/TRAPS.md, src/DK2.Data/README.md, ADR-0091
and src/DK2.Data/Kwd/ (KwdReader, KwdChunk, KwdRecordRoundTrip and each typed
reader) first.

WORKTREE: create your own, `codex/kwd-writers` from master; never edit the main
tree. Install the hooks (git config core.hooksPath .githooks).

THE PROMISE: for every record of every shipped KWD/KLD file (the global
databases and all 66 levels), parse -> typed -> write gives exactly the bytes
KwdRecordRoundTrip extracts, and a whole file rewritten from typed records is
byte-identical to the shipped one. Nothing is "close enough".

RULES
- A byte the typed model does not carry must not be invented on write. Where a
  typed record drops bytes, carry them (a raw tail or a raw field) rather than
  writing a default; say which records needed it. If a field's meaning is
  unknown, it stays raw. Never zero a byte because no reader names it.
- One writer per record type, beside its reader, sharing its offsets: the
  reader and the writer must not be two copies of one layout (one table both
  use, or a test that fails if they disagree).
- Tests: install-gated ([InstallFact]/[InstallTheory]) for the shipped corpus;
  plus hand-built records that write and read back with no install. Never commit
  shipped bytes, decoded text or a converted file.
- A dk2tools command that rewrites one level to a scratch directory and reports
  the first differing byte per file, so a regression names its record.
- An ADR for the writer API (where it lives, what it promises), with the next
  free number across master and every worktree.

GATE: the full list in docs/CODEX_TASKS.md §Setup, all green, before each commit.
Commit named paths only, trailer `Assisted-by: <your model>`, never
Co-Authored-By, never --no-verify. Do not merge.

VERIFY: spawn an independent verifier on the whole diff. It applies
.claude/agents/reviewer.md's method to the format claims (offsets, strides,
chunk headers against the readers and the shipped bytes), reruns the gate and
the corpus round trip itself, and reports findings with severity and
file:line. Fix what it confirms, give evidence for what you reject, re-run.

REPORT: records and files proven byte-identical, records that needed raw bytes
and why, test totals with skips, the verifier's findings and what you did.
````

---

## Brief 10 — the remaining keeper spells (M8)

`KeeperSpells.kwd` ships thirteen spells. Built: Create Imp (its own command),
Thunderbolt, Heal and Chicken at a creature, Call to Arms and Inferno at a place
(`KeeperSpellRules.ExecutesOnCreature`, `ExecutesAtPlace`). Unbuilt: Create Gold,
Tremor, Turncoat, Summon Horny, OneShotReaper, Sight of Evil (deliberately absent,
Q194, `fog-of-war.md`) and Possession (out of scope: it needs input and
presentation). Only Create Imp has its research upgrade built (ADR-0247).

````text
Build the unbuilt keeper spells, one per commit and ADR, in this order:
Create Gold, Tremor, Turncoat, Summon Horny, OneShotReaper; then Sight of
Evil only if the read shows it changes simulation state (fog is simulation
state, ADR-0076). Possession is out of scope.

Read AGENTS.md, docs/TRAPS.md, docs/research/keeper-spell-casting.md,
keeper-spell-thunderbolt.md, keeper-spell-inferno-chicken.md, shots.md,
fog-of-war.md, ADR-0247, ADR-0260, ADR-0279, ADR-0287 and ADR-0302 first, and
KeeperSpellRules, KeeperSpellSystem and SpellRules in src/DK2.Simulation.

WORKTREE: your own, `codex/keeper-spells` from master. Hooks installed.

FOR EACH SPELL
1. Read it in DKII.EXE before building: the cast validation (0x004BB8A0 and
   the target-selector accessor, 0x005177B0), its shot and the shot's process
   handler, every effect it applies, its duration or timer (Turncoat's), its
   cost and cooldown, and its research upgrade levels. Prologue and epilogue
   first; count each callee's arguments; read each call's MOV ECX. Name
   fields from DK2's debug dumper where one exists (debug-dumpers.md). A
   function record for every function read.
2. Write the prediction into the ADR before building: which scenarios cast it
   (probably none) and so whether any hash series moves.
3. Build through the existing cast boundary; every number from GameDatabase.
   Do not invent a rule a read did not show: leave it unbuilt with a question
   row, and ask the owner what the spell visibly does.
4. Tests: cast accepted and refused per validator branch, the effect and its
   timing, the upgrade levels, a snapshot round trip for any new state. Put
   the doubt in the test name where a test rests on inference.
5. A moved hash: HashVersion + FormatVersion, a hash-versions.md row and the
   DeterminismTests literal in the same commit, naming what moved. Measure
   master's baseline first, including the Level 1 and Level 2 replay rigs if
   you can run them; say so if you cannot.

NUMBERS: take the next free ADR, question and HashVersion across master and
every worktree (git worktree list; each one's DECISIONS.md, open-questions.md,
hash-versions.md), before each commit.

GATE: docs/CODEX_TASKS.md §Setup, all green, before each commit. Named paths
only, `Assisted-by: <your model>`, never Co-Authored-By, never --no-verify. Do
not merge.

VERIFY: an independent verifier on the whole diff with its own pyghidra-mcp
copy, applying .claude/agents/reviewer.md: each address re-read, each field
mapping, each confidence level against its evidence, each "nothing reads X"
scan for cseg coverage and a positive control, and the C# against its records.
Fix confirmed findings, give evidence for rejected ones, re-run the gate, and
re-verify once if a record or mapping changed.

REPORT: per spell, read / built / left with reasons; hash and replays before
and after; the verifier's findings and what you did; owner questions asked.
````

---

## Brief 3 — independent re-derivation (cross-check)

Point it at a specific decompiled function and ask it to derive a formula
**without** reading this project's write-up of that formula. Agreement is real
corroboration from an independent reading; disagreement is a finding either way.
Two candidates that worked: the hatchery's laying interval and its exponent
(`docs/research/hatchery.md` §4 is the answer to withhold), and the
balance-variable dispatch table with its id-to-global mapping.

**This is the brief that must touch `reverse-engineering/`**, so it is also the
one whose output is checked before anything is written down: facts in our own
words may cross, decompiler output may not.

---

## Brief 4 — turn a recording into a question list (the highest-value brief here)

The owner records DK2 playing, Codex analyses the footage, and the resulting
document is read back into the project. The first run **reversed behaviour this
project had shipped for three weeks** (Q56) — no test could have caught it,
because every test agreed with the wrong rule. The running game is the only
oracle for that class of defect.

### What made the output good, and should be kept

1. **CONFIRMED observations** — what was seen, with timings, separated from any
   interpretation. *"Waited ~30 seconds at exactly 4 Imps; the replacement
   appeared ~1.3–1.5 seconds after the sacrifice; subsequent ones ~5 seconds
   apart."*
2. **An explicit CONTRADICTION against our code**, naming the method and quoting
   the line. *"`ImpSupplySystem.TopUp` sets `FreeImpCountdownTicks = 0` … that
   cannot reproduce the observed first replacement."* The highest value per word,
   and the part a model that only reads the repo cannot produce.
3. **A confidence ladder over the inferences**, on this project's own rungs.
   Keeping the unknowns *labelled* rather than smoothed over is what made them
   safe to act on.
4. **Numbered questions addressed to the decompiler**, ordered, with a closing
   sentence naming which one mattered most.

### The rule for reading it back

**Its observations are oracle. Its inferences are reasoning about our repo.** Act
on a CONFIRMED observation the way you would act on the owner speaking; check an
inference before building on it. In the 2026-09-05 document one inference was
wrong in a harmless direction — `TimeBetweenReattemptingSleepSeconds` was offered
as a possible retry interval, and it is read by **zero instructions in the whole
executable**.

A remembered observation that contradicts a recording is also a finding rather
than noise: Q150 was settled without a second recording because the owner
corrected the first one from memory (four creature types *always* bed down
first), and the decompiler did the rest.

### The prompt

````text
You are analysing recordings of Dungeon Keeper 2 (1999, v1.70) for a project
that reimplements it in C# as a behaviourally faithful clean-room
reimplementation. The original game is the oracle: the goal is not working code,
it is code that behaves like DK2.

You will be given one or more screen recordings, and read access to the repo.

Produce a document with these four parts, in this order:

1. CONFIRMED OBSERVATIONS. Only what is visible. Give timings as measured, with
   the uncertainty ("approximately 1.3-1.5 seconds", not "1.4 seconds"). Say
   what was on screen, not what it means. Include negative observations - what
   did NOT happen is evidence.

2. CONTRADICTIONS WITH THE IMPLEMENTATION. For each observation, find the code
   that claims to implement it and say whether it can produce what was seen.
   Name the file and method and quote the line. If it cannot, say so plainly.
   THIS IS THE MOST VALUABLE SECTION - a contradiction here is worth more than
   ten observations, because it is the only thing that catches a defect the
   test suite agrees with.

3. WHAT IS ESTABLISHED, on this project's own confidence ladder:
   CONFIRMED (seen), STRONGLY INFERRED (the only reading that fits),
   HYPOTHESIS (fits, others also fit), UNKNOWN (say so; do not smooth it over).
   An UNKNOWN left labelled is useful. An UNKNOWN dressed as an inference is a
   future defect.

4. QUESTIONS FOR THE DECOMPILER, numbered, ordered by what they unblock, and
   phrased so someone with Ghidra open can answer them one at a time. End with
   one sentence naming which question matters most and why.

DO NOT edit any file. DO NOT commit. DO NOT open a pull request.
Do not invent DK2 behaviour: if the footage does not show it, it is UNKNOWN.
````

### What to record next

- **Q152 — does regeneration continue in combat, in the Hand of Evil, under
  possession?** Three ten-second looks. The combat case is the one that changes a
  game: an imp regenerates 67 a second.
- **A prediction to break**, from Q150: a big empty lair, and a Troll that beds
  down before it trains.

`docs/research/observation-checklist.md` is the standing list of everything else
the owner can settle by playing.

### And what this brief must never become

Codex analysing footage produces **research input**, not research prose.
`docs/research/` stays off limits: the write-up is where the confidence ladder is
enforced and where the evidence rank is recorded.
