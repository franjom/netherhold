# Goal alignment review

Where the project actually stands against the twelve long-term goals in
`AGENTS.md`.

Those goals are **end-state objectives**, and AGENTS.md is explicit that they
must not dictate premature implementation. This document exists for the other
failure mode: drifting away from them without noticing, or believing a goal is
served when only its easy half is built. It records honest status, not progress.

**Re-run this at every milestone.** The method is deliberately cheap:

1. For each goal, list what a reader could actually do today — not what the
   architecture permits.
2. Separate *"the structure is in place"* from *"the thing exists"*. These are
   not the same and conflating them is how a plan starts lying to itself.
3. Name the gaps that have a deadline attached, and say what the deadline is.
4. Append to the review log at the bottom rather than overwriting.

---

## Review — 2026-08-29 (commit `e5260d8`, milestone M3 partial)

### Verdict

The project has built the **spine** the twelve goals need — determinism,
data-driven rules, headless simulation/presentation separation, and an evidence
discipline that refuses to invent — and roughly **one vertebra of actual game**.

Concretely: a determinism-gated headless simulation core; a reader for five of
DK2's eighteen record types; and one vertical slice of gameplay (terrain work,
plus a worker that does it). 198 tests, ~2,500 lines of research documentation.
No Unity project exists; `DK2.AI` and `DK2.Assets` are empty shells.

Against the plan's own milestones: M2 done, M1 and M3 partial, M4–M11 untouched.

### Goal by goal

| # | Goal | Status | Where it stands |
|---|---|---|---|
| 1 | Modern DK2 engine | **~2 of 20 systems** | Digging and territory rules exist; saving/loading exists (our format). Rooms, creatures, needs, economy, combat, spells, traps, doors, heroes, enemy dungeons, scripting, objectives, campaign: none. No Unity. |
| 2 | Campaign compatibility | **Data-in solid; run-a-level absent** | Every shipped map, terrain, room and creature definition parses; every campaign level builds a `WorldState`. Objects, level configuration, scripts, objectives and progression need the other 13 record types — scheduled M6. |
| 3 | Sim/render separation | **Enforced, unused** | The *separation* is done and compiler-enforced: netstandard2.1 targets, `ArchitectureTests` fails the build on a Unity reference, positions are fixed-point tile units that only a renderer converts. Zero rendering exists. |
| 4 | Replaceable assets | **Not started, not yet owed** | `DK2.Assets` is empty. WAD extraction works as tooling. Nothing in the simulation depends on any asset, so there is nothing to retrofit — see the deadline in gap 3. |
| 5 | Behavioural fidelity | **Method built; coverage thin** | Confidence tags in code (`Dk2Behavior`) and docs, three research notes, 30 tracked questions, and refusals on the record (Q20 not "fixed", Q24 not invented). Of the goal's own emphasis list, only tile resource calculations and terrain state transitions are covered — and game timing is unmeasured. |
| 6 | Oracle testing | **Half the rig, the easy half** | Scenario format, headless runner, per-tick hash series, event log, and a mandatory "what this run could not do" report. The original-side probe does not exist. 5 scenarios against a target of 500+. |
| 7 | Level editor | **Nothing** | Readers exist; no writers. Not blocked by anything. |
| 8 | Modding framework | **Better placed than it looks** | The rule *"a balance constant hardcoded in simulation code is a bug"* means the simulation reads everything from `GameDatabase` — and tests already build one by hand with no DK2 install, so the hand-authored-database path is proven. Missing: an external content format. |
| 9 | Deterministic, fast | **Strongest area; one half untested** | Pinned cross-machine hash, RNG position hashed, snapshots verified by continuation rather than byte equality, no floating point in gameplay maths, one explicit system order, fully headless. Simulation *speed* has never been measured. |
| 10 | AI/bot interface | **Shape already right** | `Tick(InputFrame)` is the only way time moves and commands are the only input — the observe→act→advance loop the goal draws. Missing: more than one command type, and an observation API shaped for an agent rather than raw `WorldState`. |
| 11 | Presentation modes | **Headless works; it is the only one** | Same enforcement as goal 3. |
| 12 | Preservation | **Downstream of 1–3** | Nothing to assess independently yet. |

### What early decisions are already paying for

Four choices made before they were strictly necessary, each of which goal 5, 8,
9 or 10 would otherwise have made expensive to retrofit:

- **Compiler-enforced Unity independence** (ADR-0002) — goals 3, 9, 11.
- **Integer/fixed-point gameplay maths** (ADR-0001) — goals 5, 9.
- **Command-driven input frames** (`PlayerCommand` / `InputFrame`) — goals 9, 10.
- **Every number read from `GameDatabase`, never hardcoded** — goals 5, 8.

None was free at the time. All four are load-bearing for the end state, and this
is the section to check when a shortcut is being considered.

### The gaps that matter

**1. Q1 (tick rate) makes goal 5 depend on goal 6.** Goal 5 names "game timing"
explicitly, and the tick rate is unmeasured. It cannot be answered from the data
files — checked exhaustively (see `imp-tasks.md`, the id-74 negative result), so
it needs the oracle probe. That makes M5 a *dependency* of goal 5 rather than a
parallel track, which is worth knowing before scheduling breadth work.

**2. Breadth has not started, and goal coverage will look flat until it does.**
The goals are overwhelmingly about breadth; the project has one gameplay slice.
This is the plan's deliberate ordering — build the fidelity rig before
accumulating plausible-but-wrong behaviour that is expensive to unwind. Expect
the table above to move slowly and then quickly. Do not read a flat table as a
stalled project, and do not "fix" it by adding breadth ahead of M5.

**3. The asset layer has a deadline it does not know about.** AGENTS.md states
that *all* asset access must go through an abstraction layer. Goal 4 is cheap
today, because nothing depends on an asset, and expensive the moment a renderer
exists. **It must land in the same milestone as the first Unity view, not
after.**

**4. Simulation speed is an unverified claim.** Goal 9 wants "significantly
faster than real time". Nobody has measured it. That is an afternoon's work and
is worth doing before the shape of the loop hardens.

### Watch list — not problems yet

- **`GameDatabase` is DK2-shaped.** Its records mirror KWD layout field for
  field, including string-table ids. Correct for fidelity; it does mean goal 8's
  mod author would express content in DK2's record shape. Revisit at M6, not
  before.
- **`WorldState` grows one mechanic at a time, on purpose.** Good discipline,
  but goal 10 will eventually want a stable observation schema, and that schema
  is still moving. Do not freeze a bot-facing API early.

---

## Review log

| Date | Commit | Milestone | One-line summary |
|---|---|---|---|
| 2026-08-29 | `e5260d8` | M3 partial | Spine built (determinism, data-in, headless, evidence discipline); one gameplay slice. Goal 5 found to depend on goal 6 via Q1. Asset-layer deadline recorded. |
| 2026-08-29 | (rooms) | M3 partial | Rooms, the treasury and the gold model. Goal 5 gained its clearest win so far: the data contradicted an assumption the code already held, and the code changed. Goals 1, 8 and 10 move slightly; nothing else does. |
| 2026-08-29 | (mana) | M3 partial | Mana income from owned land. Goal 5's harder case: the data was nearly enough for a computed mana ceiling and the ceiling was left flat with the gap recorded (Q37) rather than guessed. ADR-0017 decouples per-second totals from the unmeasured tick rate. |
| 2026-08-29 | (mana, cont.) | M3 partial | Owner answered Q37 and the one-heart rule from the original in one message. Ceiling confirmed flat; the heart's 30/s became implementable. Process gap found: "the files cannot say" had been treated as "nobody can say" - asking the owner is now step 0 of the research method. |
| 2026-08-30 | (gold carrier) | M3 partial | Q31 built: gold moves seam - imp's hands - treasury. Goal 1 (behaviour first) gains its most visible slice yet; goal 5 holds, with the one invented decision isolated as Q50 and put on the observation checklist rather than buried. A number in the CREATURES record turned out to corroborate Q47 by accident. |
| 2026-08-30 | (Q47) | M3 partial | One measured number - 273 gold a swing - settled the tile-work granularity for the whole game, and the answer (five a second) was NOT the plausible one that had been recorded (twice a second). Goal 5 vindicated in its hardest form: the wrong reading was self-consistent, fitted every range, and would have been built if the discipline had been weaker. Also Q32 confirmed from five room prices, and Q52 recorded as a defect we cannot yet fix. |

### Note on the rooms/gold review

Not a full re-run — the table above stands. One thing is worth recording because
it is the pattern goal 5 exists to produce, and it had not happened before:

**A schema assumption was refuted by the data and the code changed to match.**
`PlayerState.Gold` was written in M2 on the natural assumption that gold is a
player-level resource. Extracting the balance table for the treasury showed DK2
has no such variable — it has four limits and every one is per place — so gold
moved onto tiles and the field was removed (ADR-0015). Three behaviours the
original has then fell out for free instead of needing rules.

That is the goal-5 method working on something structural rather than on a
constant, which is the case the confidence tags were always going to be least
help with. Worth remembering the shape of it: the tell was not a wrong number,
it was **a missing variable**. When the data has no variable for a thing the
design assumes exists, the design is probably wrong.

Two smaller movements: goal 8 (modding) is better served than before, because
the room rules read placement from flags rather than from room identity — a
mod-authored room needs no code. Goal 10 (bot interface) gained its second
command type, which was the missing half of "commands are the only input".

Gap 3 (the asset-layer deadline) is unchanged and is now the next thing that
could go wrong, because the Unity view is the next item on the handoff list.

### Note on the mana review

Not a full re-run either — the table above still stands, with two small
movements and one thing worth recording.

**Goal 5 (behavioural fidelity) gained its second structural win, and it is the
opposite shape from the first.** With gold, the tell was a *missing* variable and
the code had to change. With mana the data was complete and unambiguous, and the
win was refusing to stop there: `MaxManaGain`, `DungeonHeartMaxManaIncrease` and
`DungeonHeartManaGenerationIncreasePerSecond` are three plausible-looking
variables that would have been easy to wire up into a keeper's mana ceiling. Each
would have been a guess. They are Q37 instead, and the note says out loud that
our flat ceiling is plausibly six times too generous for a one-heart keeper.

That is the harder half of the discipline. Refusing to invent a rule when the
data is silent is easy; refusing to invent one when the data is *nearly* enough
is where a project actually drifts.

**Goal 8 (modding) again.** Mana income reads `TerrainDef.ManaGain` and nothing
else. A mod-authored terrain that generates mana needs no code, exactly as a
mod-authored room needs none.

**Goal 9 (deterministic, fast).** ADR-0017 is a small decision with a
disproportionate payoff for goal 5's dependency on goal 6: paying per-second
rates once a game second means mana totals are independent of `TicksPerSecond`,
so answering Q1 will not silently rewrite the economy. Gap 1 is unchanged, but
one of the things that would have been damaged by a late Q1 answer no longer
would be.

Gap 3 (the asset-layer deadline) is still unchanged and still the next thing that
could go wrong, because the Unity view is still the next item on the handoff
list.

### Addendum — the mana ceiling, one message later

The note above was written before the owner said, in one sentence, that a
keeper's maximum mana is 200000 and that a keeper has exactly one dungeon heart
whose loss is defeat. Both had been written up here as things the data could not
settle. Both were settled immediately, and both changed the code.

The praise in that note was therefore half-earned, and the honest version is
worth having on the record: **refusing to guess was right, but treating "the
files cannot say" as "nobody can say" was a process failure.** Goal 5 names the
original as the behavioural oracle; the owner has played it. Asking is the
cheapest research step available and it was not in the method at all until now —
it is step 0 in `HANDOFF.md` §"Research method that has been working".

Two of the four questions this session raised were answerable that way. The test
for the rest is simple: **would a player see this happen?** If yes, ask before
writing it up as UNKNOWN. If no — tick rates, per-tile arithmetic, which treasury
tile a deposit lands in — the question is genuinely for measurement or
disassembly and belongs on the list.

What the caution bought is still real, and is the reason this is an addendum
rather than a retraction: the tidier computed-ceiling reading was **wrong**, and
it would have looked better justified than the flat ceiling that turned out to be
right. Having not implemented it meant the answer, when it arrived, cost nothing.

---

### Note on the gold carrier — and a correction to the addendum above

The addendum ends by listing questions that are "genuinely for measurement or
disassembly", and one of the examples it gives is **which treasury tile a deposit
lands in**. That example was wrong, twice over, and it is worth correcting in
place rather than quietly.

It was wrong first because the owner *did* answer it, the same day, from having
played the game: the nearest tile, spilling into another (Q35). A player does see
that happen. It was wrong second, and more interestingly, because implementing
Q31 answered it without anyone deciding anything. The carrier walks to the
nearest tile with room, puts down what fits, and looks again if it is still
carrying something. "Nearest, then spillover" is what that *does*; there is no
spillover rule anywhere in the code.

That is the pattern worth naming, because it is the cheapest kind of fidelity
there is: **a question about which of several outcomes the game picks sometimes
dissolves when the mechanism underneath it is built properly.** The wrong move
would have been to implement Q35 as a rule — a sort, a preference, a tie-break —
on top of a `Deposit` that spread gold across tiles by index. The right move was
to give the imp hands and a walk, and let the answer fall out.

**Goal 5 in its harder form still held.** One decision here is genuinely ours:
where an imp stops mining and turns round. It is not derivable from the data, the
owner has not been asked, and the two candidate rules differ. Rather than pick
quietly, it is Q50, it is tagged HYPOTHESIS in the code that implements it, and
it is on the observation checklist as a one-minute experiment. The shipped
numbers even say which answer to expect — the Imp's `maxGoldHeld` of 1400 is
almost exactly two swings' worth under Q47's half-delta reading — which is
corroboration for Q47 arriving from a record that has nothing to do with tile
work. That is what the evidence discipline is for: the coincidence was only
visible because both numbers had been written down honestly.

---

### Note on Q47 — the clearest case goal 5 has produced

Goal 5 says the original is the behavioural oracle and that we distinguish what
is known from what is guessed. Most entries in this log are about *refusing* to
guess. This one is about what that refusal was worth, and it is the sharpest
example so far because the guess was good.

The recorded reading of Q47 was: an application lands **twice a second** and
applies **half** the tabled delta. It was not lazy. It was self-consistent (half
the delta twice a second is the delta per second), it fitted every observed range
the owner had given, and it explained why everything ran too fast. It was tagged
HYPOTHESIS and left unimplemented for one reason: **ranges are not counts.**

Then one count arrived — 273 gold a swing — and the answer was **five a second, a
fifth of the delta.** The recorded reading was not slightly off; it was wrong by a
factor of 2.5, and the refutation is one line long: **`ClaimTileHealth` is 825,
which is odd, so half of it is not an integer.** That was sitting in the data the
whole time and nobody looked, because the reading fitted well enough not to be
questioned.

Two things worth taking from it.

**The cost of having been careful was zero, and the cost of not would have been
two rewrites.** Every application count in the game changed with this commit —
claiming 1→4, reinforcing 10→50, an enemy wall 113→563. Had the twice-a-second
reading been implemented, all of those would have moved twice, and the second move
would have arrived with a set of tests already written against the wrong numbers.

**A measurement is worth more than an observation, and it is a different ask.**
Fifteen questions came back from "would a player see this happen?" and every one
was a yes/no or a range. Q47 needed a *number*, and asking for a number got one.
Worth putting on the checklist explicitly: when a question is quantitative, ask
for the quantity, and say which one — "how much gold per swing" got an answer that
"how fast does mining feel" could not have.

**And the corroborations mattered more than the measurement.** 273 on its own is
one data point from one person watching one seam. What makes this CONFIRMED rather
than a fit is that two things agreed which were not consulted: eight of the ten
tabled deltas divide by five exactly, and claiming independently lands on the four
swings Q46 had measured a day earlier by watching an imp jump. Neither was
available to be fitted to, which is the whole test.
