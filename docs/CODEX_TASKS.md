# Briefs for a second agent

The owner has OpenAI Codex available alongside Claude. This file holds the tasks
worth giving it, phrased so they can be pasted directly.

## The division that makes sense

**Codex: verification, breadth, second opinions — and now footage.** Read-only
auditing, data sweeps over the 1236 shipped files, independent re-derivation of
something already derived, and **turning a recording of the running game into a
question list**. The last of those was added on 2026-09-05 after it worked
better than anything else has: see Brief 4, which is the highest-value entry in
this file.

**Ranked by what they have actually produced:** Brief 4 (reversed shipped
behaviour three weeks old), Brief 1 (the characteristic-defect audit), Brief 3
(corroboration), Brief 2 (still open).

**Not Codex: prose in `docs/research/`, the `reverse-engineering/` quarantine,
the determinism hash, and commits.** The first degrades an evidence discipline
that took months to build; the second is a licence boundary rather than a style
one; the third has a bump-and-re-pin protocol that makes the gate meaningless if
got wrong; the fourth has conventions no prompt reliably conveys.

## Setup

Codex reads `AGENTS.md`, which is why that file was committed on 2026-09-05 —
until then 430 of its 494 lines lived only in one working tree, so any fresh
clone would have shown a second agent 64 lines with none of the rank-of-evidence
rule, the ADR-0046 quarantine, or the twelve goals.

The gate is the same for work from any source, and must be green before anything
lands:

```
dotnet build DK2.slnx -c Release                          # zero warnings
dotnet test DK2.slnx -c Release                           # all green
dotnet run --project src/DK2.Harness -- run scenarios     # 21 scenarios
```

Anything that **writes** should get its own git worktree. Read-only auditing can
run against the main tree any time.

**Reference checkouts, both outside this repository** (2026-09-06):
`D:\OpenKeeper` (GPLv3 — facts only, provenance in `THIRD-PARTY-NOTICES.md`) and
`D:\Flame` (**unlicensed — facts only and never one of its identifiers**, see
`docs/research/external-resources.md`).

---

## Brief 1 — the unverified-claim audit (read-only, run this first)

The highest-value use, because it targets this project's characteristic defect:
a claim everything is consistent with that nobody ever checked. Three real
instances are given as calibration, because the pattern matters more than the
instruction.

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

## Brief 2 — Q132, the phantom variable — **CLOSED 2026-09-05, do not pick up**

**Answered:** variable 19 is ticks per game second and the **level header**
supplies it, which is why it appears in no variables file. All 66 shipped levels
carry 4. Built on 2026-09-06 (ADR-0066) — it was the hatchery clock.

The brief is kept because the shape of it was right: everything eliminated was
written down first, which is what made the answer findable when the level loader
turned up a second writer. What follows is the brief as it stood.

The question: **balance variable 19's global is read from 223 instructions in
`DKII.EXE` — more than any other — as a multiplier 28 times and a divisor 22
times. Exactly one instruction writes it. It lives in uninitialised memory, so
the image carries no default. And no shipped file supplies id 19**, checked
across all 1236 files in the install with a scanner validated against ids 3, 18
and 74. Twenty-two sites divide by it and the game does not crash.

Give Codex the *question and the eliminations*, not the write-up, and let it
work out where else a value could come from.

---

## Brief 3 — independent re-derivation (cross-check)

Point it at a specific decompiled function and ask it to derive a formula
**without** reading the project's write-up of that formula. Agreement is real
corroboration from an independent reading; disagreement is a finding either way.

Good candidates, both derived on 2026-09-04:

- the hatchery's laying interval and its exponent (`docs/research/hatchery.md` §4
  is the answer to withhold);
- the balance-variable dispatch table and the id-to-global mapping.

**This is the one brief that must touch `reverse-engineering/`**, so it is also
the one that needs the quarantine rule stated explicitly and its output checked
before anything is written down: facts in our own words may cross, decompiler
output may not.

---

## Brief 4 — turn a recording into a question list (the highest-value brief here)

**Added 2026-09-05, because it worked.** The owner recorded DK2 playing, Codex
analysed the footage, and the resulting document **reversed behaviour this
project had shipped for three weeks**. No test could have caught it: every test
agreed with the wrong rule. The running game is the only oracle for that class of
defect, and this is how its evidence gets in.

### What made the output good, and should be kept

The document that worked had four things in it, and each earned its place:

1. **CONFIRMED observations** — what was seen, with timings, separated from any
   interpretation. *"Waited ~30 seconds at exactly 4 Imps; the replacement
   appeared ~1.3–1.5 seconds after the sacrifice; subsequent ones ~5 seconds
   apart."*
2. **An explicit CONTRADICTION against our code**, naming the method and quoting
   the line. *"`ImpSupplySystem.TopUp` sets `FreeImpCountdownTicks = 0` … that
   cannot reproduce the observed first replacement."* This is the part with the
   highest value per word, and the part a model that only reads the repo cannot
   produce.
3. **A confidence ladder over the inferences**, using this project's own rungs:
   CONFIRMED / STRONGLY INFERRED / UNKNOWN. Keeping the unknowns *labelled*
   rather than smoothed over is what made them safe to act on.
4. **Numbered questions addressed to the decompiler**, ordered, with a closing
   sentence naming which one mattered most. That ordering was correct and saved
   an hour.

### The rule for reading it back

**Its observations are oracle. Its inferences are reasoning about our repo.**
Treat the two differently: act on a CONFIRMED observation the way you would act
on the owner speaking, and check an inference before building on it. In the
2026-09-05 document one inference was wrong in a harmless direction —
`TimeBetweenReattemptingSleepSeconds` was offered as a possible retry interval,
and it turns out to be read by **zero instructions in the whole executable**.

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

### What to record next, if there is another session

These are the open observations, and the first is the one that blocks real work:

- ~~**Q150 — what makes a creature go and get a lair.**~~ **Done 2026-09-05**
  without a second recording: the owner corrected the first one from memory
  (four creature types *always* bedded down first) and the decompiler did the
  rest. Worth keeping as the other model — a remembered observation that
  contradicts a recording is a finding, not noise. What remains is a prediction
  to break: a big empty lair, and a Troll that beds down before it trains.
- ~~**Q147 — is imp job priority global, or a tie-break within one position.**~~
  **Done 2026-09-05** — the owner played it (*"walls over gold, in both cases"*)
  and the decompiler finished it: two tiers, ADR-0058. Kept here as the model of
  what a two-minute recording can settle.
- **Q152 — does regeneration continue in combat, in the Hand of Evil, under
  possession.** Three ten-second looks. The combat case is the one that changes a
  game: an imp regenerates 67 a second.
- **Q149 — is the ~1.4s before a free imp appears simulation or animation.**
  Low stakes; nothing in the simulation depends on it.
- ~~**Q151 — which of two lair rooms a creature prefers.**~~ **Done 2026-09-05**
  from the binary: alignment — a creature avoids a lair holding a bed of the
  other side. Only observable with converted heroes, late in the campaign.

### And what this brief must never become

Codex analysing footage produces **research input**, not research prose.
`docs/research/` stays off limits (see "The division that makes sense" above) —
the write-up is where the confidence ladder is enforced and where the evidence
rank is recorded, and that discipline is the thing this project has that most
reimplementations do not.

---

## Brief 5 — the `.str` string tables (write; own worktree)

**Status: open, offered 2026-09-06.** Self-contained, verifiable against data
this project already parses, and touching no file the simulation work touches.

### What it is

DK2 keeps its entire text — creature names, room names, tooltips, level
briefings, objectives, speech subtitles — in **92 `.str` files** under
`Data/Text/Default/`, 353 KB in total, and **this project reads none of it.**

The consequence is concrete rather than cosmetic. Four record types already
parse **nine `*StringId` fields** — `CreatureDef.NameStringId`,
`TooltipStringId`, `IntroductionStringId`, and the `Name`/`Tooltip` pairs on
`ObjectDef`, `RoomDef` and `TerrainDef` — and **every one of them currently
points at nothing.** The names this project uses come from a debug field in the
records, not from the game's own text.

### What is already established, so you do not repeat it

Measured on 2026-09-06; treat as a starting point to verify, not as gospel.

- All 92 files share the magic **`BFST`**.
- ~~At `+4`, a u32 that behaves like a **base string id**~~ — **WRONG,
  and corrected by the branch this brief produced (2026-09-06).** `+4` is a
  **file id**: the 92 files carry 1000, 1001, 1002 … 1091, one each, in a fixed
  order. Read as bases they would overlap — `LEVEL1_BRIEFING` would own
  1000–1005 and `LEVEL2_BRIEFING` 1001–1006 — which is the check this
  brief did not make. **String ids are table-local and zero-based.** `+8` is the
  count and that part was right; the 92 counts sum to **7,024**, not the 96,186
  this line claimed.
- At `+12`, a table of `count` u32 offsets, monotonically non-decreasing —
  and **relative to byte 12**, not absolute. `Text.str`'s first stored value is
  12,344, which resolves to byte 12,356, exactly where its own 3,086-entry
  offset table ends.
- **The payload is encoded, not plain text.** No ASCII run of six printable
  characters exists anywhere in `Text.str`, and no UTF-16LE run either. 176
  distinct byte values, with `0x00`, `0x0C` and `0xB0` dominant — the shape of a
  bit-packed or dictionary scheme rather than a character encoding. Decoding it
  is the actual work.
- **One wrinkle worth knowing before you start**: in `Text.str` the offset table
  ends at byte 12,356 but the smallest offset in it is 12,344 — twelve bytes
  earlier. So either the count is not quite what it looks like, or the first few
  entries are sentinels. Do not paper over it.

### Definition of done

1. A reader in `src/DK2.Data` (or `src/DK2.Convert` if it fits the converters
   better — your call, say why) that opens a `.str` and resolves an id to a
   string.
2. **The verification that matters, because it uses data already in the repo:**
   the Goblin's `NameStringId` must resolve to the Goblin's name, and the same
   for a room, an object and a terrain. If those four come out right on the
   shipped files, the format is read correctly; if they do not, nothing else in
   the brief counts.
3. A real-data test over **all 92 files** that decodes **every** entry without
   throwing, and reports the totals — the same shape as
   `SdtArchiveTests.EveryShippedBankHasTheConfirmedLayoutAndKnownPayloadTypes`,
   which is the model to copy.
4. A `dk2tools str list <file>` / `str get <file> <id>` verb.
5. A section in the relevant README with **counts you actually measured**, and
   every UNKNOWN named. If some entries will not decode, say how many and which,
   rather than rounding it to "works".

### A hypothesis that turned out to be right — CONFIRMED 2026-09-06

DK2's creature **state table** — 281 records, `docs/research/creature-state-table.md`
— has a `u16` at record `+0x14` that is constant across states belonging to one
mechanic. **It is a `Text.str` string id**, and it resolves to the activity label
the game prints on the flower above a creature's head. All 281 resolve, none out
of range, giving 70 distinct labels: `idle`, `reinforcing walls`, `being held
captive`, `torturing a captive`, and so on.

Nothing is left here for Codex — it is done, and it landed via `Dk2StringTable`
from Brief 5. Noted because the brief posed it as a long shot and it paid.

It may equally be a tooltip id for something else entirely. Test it, report
either way, and do not build anything on it.

**ANSWERED, and it is a qualified no** (2026-09-06). Every one of the 239 values
resolves, but they resolve to only **63 distinct labels** — shared status
text describing state *families* rather than naming states one by one. So the
prize is not there, nothing was built on it, and the negative is worth having:
it closes a line of enquiry that looked like it would name the whole state
machine for free.

### Boundaries

- **Own git worktree**; do not commit to `master`.
- **No prose in `docs/research/`** and nothing in `reverse-engineering/` — the
  usual two.
- **No decoded game text committed anywhere.** The format facts and the reader
  are ours; the strings are EA's. Tests must read them from the install at run
  time, exactly as every other real-data test does, and skip when `DK2_DIR` is
  absent.
- OpenKeeper (`D:\OpenKeeper`) has a `.str` reader. **Consult it for format
  facts, with provenance in `THIRD-PARTY-NOTICES.md`, and write your own.**
  Flame (`D:\Flame`) is unlicensed: consult, never copy, never adopt an
  identifier.
- The gate at the top of this file must be green.


---

## Brief 6 — Doors.kwd and Traps.kwd, the two shipped tables nothing reads

**Added 2026-09-06.** Needs its own worktree; it writes code.

### The task

`GameDatabaseLoader` parses six of the twelve `Data/editor/*.kwd` files. Six are
untouched, and two of them are worth having now:

| File | Records | Stride |
|---|---|---|
| `Doors.kwd` | **8** | 616 |
| `Traps.kwd` | **16** | 579 |

Parse both into typed definitions in `DK2.Data.Model`, load them in
`GameDatabaseLoader` alongside the rest, and test them. **Follow the house
pattern exactly** — `src/DK2.Data/Kwd/KwdObjects.cs` and `KwdKeeperSpells.cs`
are the two closest models, and `ObjectDef` shows how a definition is shaped.

### What is already established, so you do not repeat it

Measured on 2026-09-06 with this project's own `kwd chunks` tool. **Treat as a
starting point to verify, not as gospel** — the last brief's "already
established" section contained a wrong reading and the branch that followed it
was right to check.

- Both files carry a single chunk whose name matches the file, with a fixed
  stride and a whole number of records. Content offset is `fileLength -
  records * stride`, which for `Doors.kwd` is 56.
- **Each record opens with a 32-byte NUL-padded name string**, as every other
  KWD record does. `Doors.kwd`'s first three are `Wooden Door`, `Steel Door`,
  `Braced Door`.
- Bytes 32..56 of those three records are all zero, so the interesting fields
  start later. Do not assume the layout matches `ObjectDef`'s.

### How to find the fields without guessing

The same method that pinned the creature and terrain tables, and it is the whole
skill here:

1. **The shipped values are the oracle.** A door has a health, a gold cost, and
   a manufacture cost; a trap has those plus a shot type, a charge count and a
   trigger radius. Dump each candidate offset as u8/u16/u32 across all 8 or 16
   records and look for a **column that reads like a stat**: monotonic with the
   name order, or plausibly scaled, or matching something the manual states.
2. **Cross-check against `original-reference/Dungeon_Keeper_2_Manual.pdf`**,
   which prices some of this outright.
3. **`D:\OpenKeeper` has a reader for both** — facts only, with attribution in
   `THIRD-PARTY-NOTICES.md` if you adopt a field name (ADR-0003). Its layout is
   *its* guess and has been wrong before; use it to generate candidates and
   confirm them against the shipped bytes yourself.
4. **`D:\Flame` is UNLICENSED.** Consult it for structure if you like; never
   paste one of its identifiers into this repository.

### What to deliver

- `src/DK2.Data/Kwd/KwdDoors.cs`, `KwdTraps.cs`, and the two definition types.
- Loading wired into `GameDatabaseLoader`, reachable from `GameDatabase`.
- Tests in `DK2.Data.Tests` for the format (counts, names, bounds, a corrupt
  file rejected) and in `DK2.Behavior.Tests` for the shipped values you are
  confident of. **A field you could not identify is left out and named in the
  branch's own README, not guessed at.**
- A short `src/DK2.Data/README.md` section, same shape as the STR one: what is
  read, what is verified, and an explicit **UNKNOWN** list.

### What not to do

- **No behavioural rules.** This is data, not mechanics. Nothing decides how a
  trap fires or what a door costs to open; that is a later milestone and needs
  the binary.
- Nothing in `docs/research/`, nothing in `reverse-engineering/`, no touching
  the determinism hash, no commit to `master`.
- **Report a field you could not pin rather than filling it in.** The last
  brief's most useful output was a negative result.

### The gate

The three commands in Setup, green, before anything lands.

---

## Brief 7 — fog of war: how the original decides what a keeper has seen

**Added 2026-09-06. Read-only research, no code.** This is a Brief 3 shaped task
— independent derivation from the binary — so it is the second brief that must
touch `reverse-engineering/`, and the quarantine rule below is not optional.

### Why this one is worth giving away

Fog is the rare mechanic with almost no dependencies: it needs no combat, no
objectives, no room behaviour, no job selection. What it lacks is *facts*. There
was no research note and no open question about it in this repo until today, and
the balance table — normally where this project's numbers come from — **does not
contain a sight radius at all**. So the only sources left are the ones this brief
names, which is exactly the case where a second agent reading three codebases in
parallel beats one agent reading them in series.

### What is already established, so you do not repeat it

Measured 2026-09-06. **Treat as a starting point to verify, not as gospel** —
Brief 5's "already established" section contained a wrong reading, and the branch
that checked it was right to.

- **The substitution is CONFIRMED at the UI level.** Terrain id 27 in the shipped
  `Terrain.kwd` is `Fog Of War`, flags `0x0000000B` = `Solid, Impenetrable,
  Taggable`, health 1/1. Its `NameStringId` (record `+0x1A2`) is **2472** and its
  `TooltipStringId` (`+0x1A0`) is **2473**, which resolve in `Text.str` to
  **"Fog of War"** and **"Fog of War: this area has not yet been explored."** — and
  an owner screenshot of the running game shows exactly that caption with the
  cursor over blackness. So an unseen tile really is presented as terrain 27.
  **What is NOT established is where in the binary that substitution happens.**
- **`RevealThroughFogOfWar` is terrain flag bit 24**, set on exactly six types:
  Impenetrable Rock, Water, Lava, Gold, Gems, Edge of Map. Nothing in this repo
  reads it and no read of it in the binary has been found.
- **Fog appears to be a game *setting* with modes.** `Text.str` 348-353, inside a
  run of skirmish-setup labels, reads `Fog of War` / `Creatures Reveal` /
  `All Owned Reveal` / `Stay Revealed` / `Re-shroud` / `Re-fog`. Read as a group
  label plus five options, that is two reveal sources and three persistence
  behaviours. **Inferred from strings only** — nothing has watched the engine
  branch on them, and where the setting is stored is unknown.
- **No sight/vision radius exists in the 214-variable balance table.** The only
  radii are `DungeonHeartClaimScanRadiusTiles` (15, a u8 tile count) and
  `DefaultTorchLightRadiusTiles` (6144). The second is **1.5 tiles**, not 6:
  `Fp32.One` is 4096, and `DefaultTorchLightHeightTiles` carries the same 6144.
  That near-miss was checked and rejected; do not resurrect it.
- Owner observations, CONFIRMED 2026-09-06, **in the campaign as played**: once
  revealed a region **stays** revealed; an unrevealed tile presents as a black-ish
  solid tile that **can be tagged**; and a tag placed on fog is **lost on reveal**
  if the revealed tile is impenetrable rock or turns out to be a room. The owner
  estimates the reveal radius at **about 6 tiles** and explicitly asks for
  independent confirmation — treat it as a hypothesis to test, not a given.

### The claim to attack

**"Fog stays revealed"** is confirmed by the owner and is also the name of
option 351 of five. So the campaign's behaviour is probably one mode of several,
and building it as *the* rule would be this project's characteristic defect
again — internally consistent, contradicted by nothing, checked by nobody.

**Establish whether the modes are real engine behaviour or dead strings**, and if
they are real, what selects one. If instead the engine has exactly one fog rule
and those strings are vestigial, that is the more valuable finding and it
simplifies the build considerably.

### The questions, ordered by what they unblock

1. **Where is per-keeper discovered-ness stored, and how wide is it?** One bit
   per tile per player, a byte, a per-tile owner mask? Is it in the same
   structure as the tile, or a separate map?
2. **Is it simulation state or client state?** Concretely: does a **computer
   keeper** consult fog when it decides anything? A computer player that plays
   with fog is the single fact that decides whether this project's `WorldState`
   grows a field and takes a determinism-hash bump, or whether fog lives beside
   the renderer and costs nothing. **This is the question that matters most.**
3. **What reveals, and how far?** Which entities push the boundary back — any
   creature, the keeper's own only, a dropped creature, the camera, a possessed
   creature? Is the radius a constant in the image, a per-creature field, or a
   flood over connected floor? If it is an immediate in the code rather than a
   variable, that explains why the balance table has nothing. **The owner's
   estimate is ~6 tiles: confirm or refute it with a number from the image.**
4. **Are `Creatures Reveal`, `All Owned Reveal`, `Stay Revealed`, `Re-shroud` and
   `Re-fog` real modes, and what selects one?** Level file, skirmish setup, both?
   Does the campaign fix it? This decides whether fog is a rule or read
   configuration, which matters for Goal 2 and Goal 8 as much as for fidelity.
5. **Where does `RevealThroughFogOfWar` get tested?** Six terrain types set it;
   find the read. That read is the clearest single point of entry to the whole
   mechanic.
6. **Is a tag on an undiscovered tile stored, and where?** The owner has the
   outcome — lost on reveal for non-taggable terrain — but not the mechanism.
   Does the original store a pending mark against the real tile, or against the
   substituted one?
7. **Sight of Evil and the Reveal Map special.** Both are known to exist and
   neither is built. Do they write the same state a creature writes, or a
   separate temporary layer? A temporary layer would contradict "stays revealed"
   being the whole model.

### Where to look

- **Ghidra / `DKII.EXE`** is the primary source (AGENTS.md, ADR-0046). The
  string table is the way in: this project's `docs/research/executable-strings.md`
  method has found translation units by name before, and a `Fog`- or
  `Reveal`-named unit is the obvious first grep.
- **`D:\OpenKeeper`** (GPLv3): has a renderer and will have made *some* decision
  about fog. Facts only, provenance in `THIRD-PARTY-NOTICES.md`. Its reading is
  its guess and has been wrong before.
- **`D:\Flame`** (**unlicensed**): best for **structure** — if discovered-ness is
  a field on a per-player or per-tile struct, this is where the layout shows.
  Consult, never copy, and **never paste one of its identifiers into this
  repository**.
- The shipped install at `E:\GogGames\Dungeon Keeper 2` settles anything about
  file contents directly.

### Output

A markdown report **to stdout**, in the four-part shape Brief 4 established:
what is established and how, contradictions with anything this repo currently
assumes, a confidence ladder using this project's own rungs, and numbered
follow-up questions. End with one sentence naming which finding matters most.

An **UNKNOWN left labelled is a good outcome.** The radius in particular may
simply not be findable in one pass; say so rather than offering a plausible
number, because a guessed constant here would go straight into a determinism
hash.

### Boundaries

- **Read-only. Do not edit any file, do not commit, do not open a pull request.**
- **Nothing in `docs/research/`** — the write-up is where the confidence ladder
  and the evidence rank are enforced, and that stays with the primary agent.
- **The quarantine (ADR-0046): facts in your own words may leave
  `reverse-engineering/`; decompiler output may not**, anywhere, at all. Not in
  the report, not quoted, not paraphrased line by line.
- Do not invent DK2 behaviour. If the binary does not show it, it is UNKNOWN.

---

## Brief 8 — build fog of war (write; own worktree and branch)

**Added 2026-09-06.** The implementation brief. Brief 7 is its read-only sibling
and **this brief absorbs it** — do not run both.

**Branch `codex/fog-of-war`, in its own git worktree.** The house convention is
`codex/<topic>`; `codex/doors-traps` is the precedent.

### The one thing that is not decided, and the stop rule

**Where fog state lives is an open question, and it decides the whole shape.**

- If fog is **presentation state**, it lives beside `WorldView`, no simulation
  assembly changes, and the determinism hash is untouched.
- If fog is **simulation state**, it goes in `WorldState`, enters `HashInto` and
  `WorldSnapshot`, and needs a `GameSimulation.HashVersion` bump with the pinned
  literal in `DeterminismTests` re-pinned in the same commit.

**The determinism hash is explicitly not Codex's** (see "The division that makes
sense" at the top of this file): the bump-and-re-pin protocol makes the gate
meaningless if it is got wrong.

So: **Phase 0 below settles it. Build presentation-side unless Phase 0 shows
otherwise. If Phase 0 shows fog is simulation state, STOP and report — do not
bump the hash, do not touch `WorldState`.** A correct stop there is a successful
outcome of this brief, not a failure.

---

### Rank of evidence — in this order, and the order is the owner's

**1. Ghidra on `DKII.EXE` is the primary source and the first place to look**,
not the last (`reverse-engineering/README.md`, ADR-0046 as amended 2026-09-04).
It is the actual DK2; everything below is somebody's reading of it. An answer you
would file as UNKNOWN owes a Ghidra pass first. The Ghidra project lives at
`D:\ghidra projects\DK2_RE`; exports land in `reverse-engineering/`, which is
gitignored except for its README.

**The quarantine is absolute, and it is a licence boundary rather than a style
one.** Facts — a number, a formula, a comparison, an order of operations, a
description of control flow, **in your own words** — may cross. Decompiler output
may not: not into `src/`, not into `docs/`, not into a commit message, not into a
test fixture, not reformatted, not renamed, not "cleaned up".

**2. Flame (`D:\Flame`) is the reference for structure only.** Where fog state is
stored, how wide the field is, which struct holds it, what the vtable membership
looks like — that is what it is measurably good at. **It is UNLICENSED: no reuse
rights at all.** Facts only, and **never one of its identifiers pasted into this
repository**, in any form.

**3. OpenKeeper (`D:\OpenKeeper`) is the last reference.** GPLv3, so facts with
provenance recorded in `THIRD-PARTY-NOTICES.md` if you adopt a field name. Its
reading is its own guess and has been wrong before in this project's experience —
use it to generate candidates, then confirm them against the binary or the
shipped data yourself. **Do not reach for it before the two above.**

The running game outranks a misread disassembly for anything a player can watch.
The owner's confirmed observations are in `docs/research/fog-of-war.md` §3, and
`[Dk2Behavior(Confidence.Confirmed)]` stays reserved for them.

---

### What is already established

**Read `docs/research/fog-of-war.md` first** — it is the note for this mechanic
and it carries the evidence, the confidence per item and the open questions. The
short form:

- **The substitution is confirmed at the UI level.** Terrain id 27 is
  `Fog Of War`, flags `Solid, Impenetrable, Taggable`, health 1/1, and its name
  and tooltip string ids (2472/2473) resolve to the caption the running game
  shows over an unexplored tile. An unseen tile is *presented as terrain 27*.
- **`RevealThroughFogOfWar` is terrain flag bit 24**, set on Impenetrable Rock,
  Water, Lava, Gold, Gems and Edge of Map. Parsed, with **no `Is…` accessor and
  no reader anywhere**. Those six show through fog.
- **Fog looks like a setting with modes**, `Text.str` 348-353: `Creatures
  Reveal`, `All Owned Reveal`, `Stay Revealed`, `Re-shroud`, `Re-fog`. Inferred
  from strings; the campaign's confirmed behaviour is `Stay Revealed`.
- **No sight radius exists in the 214-variable balance table.** The owner
  estimates ~6 tiles and asks for independent confirmation.
  `DefaultTorchLightRadiusTiles` 6144 is **1.5 tiles** (`Fp32.One` is 4096) —
  that near-miss was checked and rejected, do not resurrect it.

---

### Phase 0 — settle these before writing code

Answer from Ghidra first, Flame second, OpenKeeper last. **Report the answers in
the branch README; they are the justification for everything you then build.**

1. **Does anything in the simulation read fog?** Above all: does a **computer
   keeper** consult it when deciding anything? This is the stop-rule question
   (Q190).

   **Try the cheap route before Ghidra on this one.** The saved-game session
   found that a save's 10-byte runtime tile has three lanes — **1, 3 and 9** —
   that move across most of the map between *any* two saves, which
   `docs/research/sav-format.md` records as "consistent with visibility or fog
   state, and still UNKNOWN". **If fog state is written into a save, it is game
   state and not client state**, because a save persists the simulation. Two
   saves taken either side of walking one creature into unexplored ground, with
   nothing else changed, would settle the stop rule in minutes — and that method
   is already built and validated by a control (an imp created, zero tiles
   changed in the other seven lanes). A negative there is not proof of the
   opposite; go to Ghidra either way, but go knowing which answer to expect.
2. **What is the reveal radius, and what does the revealing?** Confirm or refute
   the owner's ~6 tiles with a number from the image. Which entities push the
   boundary back — any creature, the keeper's own only, a dropped one, the
   camera, a possessed creature?
3. **Where does `RevealThroughFogOfWar` get tested?** That read is the clearest
   single entry point to the whole mechanic.
4. **Are the five modes real engine behaviour, and what selects one?** If they
   are vestigial strings, say so — it simplifies the build considerably.
5. **Is a tag on an undiscovered tile stored, and where?** The owner has the
   outcome (the mark is lost on reveal if the tile is impenetrable rock, or turns
   out to be a room); the mechanism is unread.

**An UNKNOWN left labelled is a good outcome.** A guessed constant is not: if the
radius will not come out of the image, implement it as a named, documented
default in exactly one place and say plainly that it is ours, the way
`IPathfinder` admits its placeholder.

---

### What to build, assuming Phase 0 says presentation

The change fog forces is that **the presentation boundary has no viewer**.
`WorldView.TileAt(world, x, y)` takes no player, `ScenePresenter.Present(world)`
takes no player, and `TileView.Tagged` is `IsTaggedByAnyone`. Every renderer is
omniscient by construction, and nothing in the architecture says so.

1. **Give the presentation boundary a viewing player.** One keeper's view is what
   a renderer draws. `TileView.Tagged` becomes *the viewer's own* mark — a defect
   fog exposes rather than a feature it adds.
2. **Fog by substitution, because that is what the original does.** When the
   viewer has not discovered a tile, `WorldView` yields terrain 27's assets in
   place of the real terrain's, keeping the tile solid and taggable. The real
   terrain does not reach the renderer at all — which is also the test that no
   renderer can leak it.
3. **Honour `RevealThroughFogOfWar`**, via a new `IsRevealedThroughFog` accessor
   on `TerrainDef` beside the other six. Rules ask flags, never ids (ADR-0016).
4. **Discovery accumulates in view state**, one bit per tile per keeper,
   monotonic under `Stay Revealed`. If Phase 0 establishes the modes are real,
   make persistence and reveal-source **read configuration with `Stay Revealed`
   as the default**, not a hardcoded rule — that serves Goal 2 and Goal 8.
5. **Do not build Sight of Evil or Reveal Map.** They are M7 and M6. Leave the
   seam obvious and say in the README what they will need.

Every rule carries `[Dk2Behavior(confidence, "docs/research/fog-of-war.md")]`,
with the confidence the evidence actually supports — `Confirmed` only for what
the owner watched.

---

### Definition of done

1. A viewer-aware presentation path, with **both** renderers working:
   `TextRenderer` (the one that runs in CI) and Unity's `DungeonScene`.
2. **The determinism hash is unchanged and `DeterminismTests` is green,
   untouched.** This is the centrepiece: an unchanged pinned hash is the proof
   that fog stayed out of the simulation. If you changed it, you were supposed to
   have stopped.
3. Tests that a keeper's view hides what they have not discovered, that the six
   `RevealThroughFogOfWar` types show through, that discovery is monotonic, and
   that **two keepers viewing the same world see different things** — the last is
   the one that would have been impossible to write before this brief.
4. A scenario under `scenarios/` drawing a picture of a partly explored world.
5. A README section on the branch: what was read from the binary, in your own
   words; what is confirmed against what is inferred; **the radius and where it
   came from**; and an explicit UNKNOWN list. Facts, never decompiler output.

### Boundaries

- **Own git worktree, branch `codex/fog-of-war`. Do not commit to `master`.**
- **Do not bump `GameSimulation.HashVersion` or re-pin the literal in
  `DeterminismTests`.** If the work needs it, stop and report instead.
- **Nothing in `docs/research/`** — that write-up stays with the primary agent,
  because it is where the confidence ladder and the evidence rank are enforced.
  Your findings go in the branch README and in the code's own XML docs.
- **The quarantine (ADR-0046)**, exactly as stated above.
- **No Flame identifier, ever. OpenKeeper attribution in
  `THIRD-PARTY-NOTICES.md` if you adopt a name.**
- Do not invent DK2 behaviour. If no source shows it, it is UNKNOWN and the code
  says so.
- **The gate at the top of this file, green, zero warnings, before anything
  lands.**
