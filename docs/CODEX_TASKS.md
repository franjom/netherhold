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
dotnet run --project src/DK2.Harness -- run scenarios     # 16 scenarios
```

Anything that **writes** should get its own git worktree. Read-only auditing can
run against the main tree any time.

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

## Brief 2 — Q132, the phantom variable (open research)

Genuinely unsolved, well bounded, and everything already eliminated is written
down in `docs/research/balance-variables-in-the-binary.md`. Fresh eyes are worth
something here precisely because the obvious avenues are closed.

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

- **Q150 — what makes a creature go and get a lair.** Drop a fresh, happy,
  bedless creature into a dungeon with a free lair tile. Time how long before it
  goes for the lair and watch what it does meanwhile; repeat with a creature
  whose anger is already part-way up. **This blocks the lair rework**, which is
  documented and deliberately unbuilt (`docs/research/lair-acquisition.md`).
- ~~**Q147 — is imp job priority global, or a tie-break within one position.**~~
  **Done 2026-09-05** — the owner played it (*"walls over gold, in both cases"*)
  and the decompiler finished it: two tiers, ADR-0058. Kept here as the model of
  what a two-minute recording can settle.
- **Q152 — does regeneration continue in combat, in the Hand of Evil, under
  possession.** Three ten-second looks. The combat case is the one that changes a
  game: an imp regenerates 67 a second.
- **Q149 — is the ~1.4s before a free imp appears simulation or animation.**
  Low stakes; nothing in the simulation depends on it.
- **Q151 — which of two lair rooms a creature prefers.** Two lairs, equidistant,
  differing in what is standing in them. The binary has a preference test whose
  meaning is unread.

### And what this brief must never become

Codex analysing footage produces **research input**, not research prose.
`docs/research/` stays off limits (see "The division that makes sense" above) —
the write-up is where the confidence ladder is enforced and where the evidence
rank is recorded, and that discipline is the thing this project has that most
reimplementations do not.
