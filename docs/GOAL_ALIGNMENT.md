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

---

## Review — 2026-09-04, the asset layer and the presentation boundary

Not a full re-run. **Gap 3 is closed**, which is the whole point of this entry:
it had a deadline attached, and the deadline has been met on the correct side of
it.

### Gap 3 is closed, and it was closed the cheap way

The gap said: *"AGENTS.md states that all asset access must go through an
abstraction layer. Goal 4 is cheap today, because nothing depends on an asset,
and expensive the moment a renderer exists. It must land in the same milestone as
the first Unity view, not after."*

`DK2.Assets` now exists in full (ADR-0034), and it landed **before** any renderer
did — so it cost what the gap predicted it would cost while it was still cheap:
nothing had to be retrofitted, because nothing depended on an asset yet. That is
the third time this project has paid a small cost early rather than a large one
late, after Unity independence (ADR-0002) and fixed-point maths (ADR-0001), and
it is worth noting that all three were recorded as risks *before* they were paid.

**Goal 4 (replaceable assets) moves from "not started, not yet owed" to
"built, and continuously tested".** The property AGENTS.md actually asks for —
that the engine must not fundamentally depend on proprietary assets — is now a
test rather than an intention: `AssetCatalogue` enumerates every key the shipped
game database implies and `AssetCatalogueTests` resolves every one of them with
no DK2 install and no art pack. The game runs as boxes, and *that it does* is
checked on every commit.

**Goal 3 (sim/render separation) moves from "enforced, unused" to "enforced and
exercised".** `DK2.Presentation` translates world state into asset-keyed
descriptions once (ADR-0035), and a test asserts that building and rendering a
whole view leaves the state hash untouched — the determinism gate pointed at the
presentation layer. The honest caveat is in the ADR: `WorldState.Tile` hands out
a `ref`, so observation is a convention plus a test rather than a compiler
guarantee.

**Goal 11 (presentation modes) moves from "headless works; it is the only one" to
two modes.** The harness's `TextRenderer` is a real second renderer — every
scenario now writes a picture of the finished world — and it is the only one that
will ever **run in CI**, which is what makes it evidence about the Unity view
rather than a toy. If the view types were secretly Unity-shaped, it would not
compile.

### What has NOT moved, and must not be read as having moved

**Goal 1 still has no Unity view, and this entry does not claim one.** Unity is
not installed on the development machine, so Unity code written here could not be
compiled or run, and this project does not commit code it has not built. What is
finished is everything that makes the Unity view thin — the boundary, the asset
layer, and a second renderer proving both work. The remaining piece is an
adapter: subscribe to a `WorldView`, turn `ResolvedAsset` into a `GameObject`,
convert `Fp32` to `float`. It is genuinely small, and it is genuinely not done.

**M3's definition of done still is not met**, for that reason and no other.

### The new gap, and it is small

**5. `AssetCatalogue` asks for one mesh per solid terrain, and DK2 does not
work that way.** Wall pieces are chosen by the size and shape of the room behind
them, and damage has its own textures (`wall-rendering.md`, CONFIRMED). The key
type already carries a variant segment, so this is a missing *list* rather than a
missing design — and the list is missing because nobody has established what the
pieces are (Q71). Recorded rather than guessed. It costs nothing until a renderer
wants to look like DK2, and then it costs whatever Q71 costs.

### One process note, which is the actual lesson of the day

The Simon Schreibt article that produced `wall-rendering.md` had been sitting in
`external-resources.md` for a day, in a row whose own text said *"useful for the
Unity view"*. The Unity view became the task and nobody re-read the row; the
owner asked about it directly. **A resource table's "when" column is a
commitment, and a table cannot tell you when its moment has come.** The editor
manual's row says **M6** and is queued up for the identical failure. See
`external-resources.md` §*Worked example 4*.

---

## Review — 2026-09-04 (evening), the Unity view renders

The previous entry closed gap 3 and said, carefully, that goal 1 still had no
Unity view. It does now, and the entry that claimed otherwise was written four
hours earlier — which is the pace worth noting, not the achievement.

### The table, where it moved

**Goal 3 (sim/render separation): "enforced and exercised" → *demonstrated*.**
The separation now has a second consumer that is a real engine. The strongest
evidence is negative and worth stating: `DK2.Unity` is the only assembly in the
repository that references `UnityEngine`, and the simulation references none of
it, so deleting the entire Unity folder changes nothing that runs.

**Goal 4 (replaceable assets): unchanged, and that is the point.** The renderer
was built without touching the asset layer, because the layer landed first. Every
one of the 1178 assets a running level asks for resolves to a placeholder with no
DK2 art installed — measured in Unity, not asserted.

**Goal 11 (presentation modes): two, and they are different in kind.** Headless,
a text renderer that runs in CI, and now Unity. The text view redraws everything;
the Unity view diffs. Neither strategy is baked into the boundary, which is the
property having two renderers was supposed to establish.

**Goal 1 (modern DK2 engine): the view exists; the game is still ~2 of 20
systems.** Do not let a screenshot inflate this row. What renders is digging,
claiming, a treasury and imps — the same two systems as before, now visible.

**Goal 9 (deterministic, fast): one quiet confirmation.** Switching seven
assemblies from net10.0 to netstandard2.1 — a change of runtime, framework and
in two places the actual API called — **did not move the pinned determinism
hash**. That is the strongest evidence yet that the integer/fixed-point rule
(ADR-0001) does what it was chosen to do. It was luck that the change was small,
but it was design that the change was safe.

### The gap that closed, and the one that opened underneath it

**Gap 3 is closed** (previous entry) and **gap 5 stands**: one mesh per solid
terrain is a known simplification, waiting on Q71.

**New gap 6, and it is the uncomfortable one: a constraint expressed only in
build configuration is not enforced.** ADR-0002 claimed netstandard2.1 was
"enforced by the compiler from day one". It was never true — the condition
selecting it could not fire, and every assembly built as net10.0 from M1 to
2026-09-04. Nothing noticed for four months because nothing used the constraint
for its purpose.

This is a *class* of gap, not one bug, and the review question it adds is:
**which other claims in this document are checked by something that runs?** A
first pass says the strong ones are (the Unity-reference rule, the determinism
hash, the placeholder's totality, "presenting never changes the world" — all
tests), and the weak ones are the ones phrased as habits: "no magic numbers in
simulation code" is review-enforced, and "no filename literals outside
`DK2.Assets`" is checked only in the direction of `ResolvedAsset` being immutable.
Neither is urgent. Both are now written down as unchecked rather than assumed.

### One thing that is not a gap and should not be treated as one

**Appearance cannot be tested and it was still worth doing.** The headless smoke
test proves 1140 objects exist; the camera bug it could not catch — the level
framed by width on a level deeper than it is wide — took one screenshot to find.
That is the boundary of a test suite, not a hole in it. The correct response is
to keep the screenshot loop short, not to try to automate a judgement about
whether a dungeon looks like a dungeon.

---

## Review — 2026-09-04 (third entry), the manual and three rules

A research sitting rather than a building one, so most of the table does not
move. Two things happened that this document should record.

### The new gap from the previous review answered its own question, badly

The last entry added a gap of a *class* rather than a bug — *a constraint
expressed only in build configuration is not enforced* — and asked: **which other
claims in this document are checked by something that runs?**

**One of them was not, and it was already false.** "Creatures move only on
dug-out tiles" was implemented in a single line of `WorldPathGrid` and checked
nowhere. `PathfindingTests` proved the *search* would not cross a wall on a
hand-drawn grid, which is a property of the search. Nothing proved the
*simulation* never put a creature inside one — and `CreatureMovementSystem` was
walking stored routes without re-checking them, so it could.

Worse, and this is the part worth carrying: **adding the invariant broke two
existing tests that had been passing while walking imps through rock**. They
hand-fed routes across the fixture's rock to measure movement arithmetic. The
arithmetic was right, so they were green — for four months.

So the gap generalises, and it should be read as the standing question at every
review: **not "is this claim true" but "what would fail if it stopped being
true?"** Three defects now share this shape — the netstandard2.1 target
(ADR-0002's correction), the text renderer's overlapping glyphs, and this one. All
three were *stated* correctly somewhere and *checked* nowhere.

Two more claims got checks this sitting as a direct consequence: "a dug-out tile
never closes" and "two terrains can never be dug through" were both true and both
unasserted; they are now pinned over a 4000-tick run and against the shipped
terrain table.

### Goal 1 (behavioural fidelity) — a small honest move

Still roughly 2 of 20 systems. What changed is not coverage but *confidence*:

| | |
|---|---|
| newly CONFIRMED behaviours | 3 (walls block movement, no fill-in, two indestructible terrains) — all from the owner, none of them asked for |
| behaviours upgraded from STRONGLY INFERRED | Q45 (room minimum sizes) now has a third independent source agreeing on 16 rooms |
| behaviours independently corroborated | 12, by the 1999 manual — the gold overflow chain, the 50% sell rate, both experience caps, `CreatedCreatureId`, `MinimumImpThreshold`, payday, bridge burning, and more |
| new questions | Q73–Q81, two of which push on existing decisions (ADR-0015's no-player-gold, and the flat mana ceiling) |

**Goal 5 (research before implementation) had its cheapest day yet**, and it is
worth naming why. The 1999 manual says what every room is *for*; the balance table
has a per-second rate for nearly all of it. Neither alone is a rule; together they
are. An afternoon of pairing them replaced what would have been several sittings
of owner observation — and the pairing only worked because the balance table had
already been fully extracted and named. **Research compounds: the dump made the
manual usable.**

### A discipline that held under temptation, and should be noted

The manual names creature **health, hunger, mood and experience** repeatedly, with
thresholds. `CreatureState` still carries none of them, because nothing reads
them. That was the right call and it was not automatic — having a source describe
four fields in detail is precisely when the "only what a built mechanic reads"
rule is hardest to keep. It is recorded here so the next sitting does not quietly
relax it.

Likewise: **nothing was deleted for disagreeing with the owner.** DK2 has no
keeper-triggerable fill-in, but `FillInHealth`, `TerrainFlags.FillInable` and
Claimed Path's route back to Rock are all in the shipped file. The data model
mirrors the data; behaviour is where fidelity is judged. Removing the transition
would have made our terrain graph disagree with the file it was read from, which
is a worse failure than carrying a rule nothing invokes.

### Addendum, same day — the standing question needs a second half

The review above asked *"what would fail if this claim stopped being true?"* and
found one claim that was already false. Hours later a third instance turned up
with a different shape, and it needs the question widened.

`docs/research/kwd-variables.md` has said **"Per-level `*Variables.kld` files
override it (same record format)"** since 2026-08-29. Correct, researched,
written down — and **read by no code for six days**. `GameDatabaseLoader` loaded
the global table and nothing else, so every campaign run used the wrong numbers
wherever a level disagreed, and 26 variable ids disagree on at least one level.
One of them was live: `MinimumImpThreshold` is 0 on four secret levels against a
global 4 (ADR-0040).

Three failures in a week, one pattern — **something stated in a place that does
not run**:

| | stated where | enforced where | how long |
|---|---|---|---|
| netstandard2.1 target | `Directory.Build.props`, behind a condition evaluated too early | nowhere | 4 months |
| creatures do not walk through walls | one line of `WorldPathGrid` | nowhere | 4 months |
| levels override the balance table | a research doc | nowhere | 6 days |

So the review question gains a clause. Not only *what would fail if this claim
stopped being true*, but **does anything read this fact at all?** The first
catches a rule that has rotted; the second catches a fact that was never wired
up. The research docs are full of the second kind by design — they are where
findings live before they are built — so the check is not "everything must be
implemented", it is: **when a doc states something the code should be doing, say
which code does it, or say plainly that none does.**

Goal 5 (research before implementation) is not damaged by this — the research was
right and it was done first, which is the whole discipline working. What failed
is the handover from research to code, and that is a different seam.

### Second addendum, same day — a fourth instance, and it was a check that lied

The pattern above gained a fourth member within hours, and it is the worst of the
four because the check was *present and green*.

The MIT file header was enforced by Roslyn's IDE0073 with `file_header_template`
in `.editorconfig`. When the project moved to GPLv3 (ADR-0041) the notice text
changed — and an `.editorconfig` value **ends at the first `;`**, which the GPL's
prose notice has in *"WITHOUT ANY WARRANTY; without even the implied warranty
of"*. The template truncated silently. Roslyn enforced the truncated form,
`dotnet format` wrote it into seven files, and **the build was green**, because
every file matched the template exactly.

The MIT text contains no semicolon. That is the only reason the mechanism ever
looked correct: it was never right, only untested against text that exercised it.

| | stated where | enforced where | how long | how it surfaced |
|---|---|---|---|---|
| netstandard2.1 target | props file, condition evaluated too early | nowhere | 4 months | first attempt to load a DLL into Unity |
| creatures do not walk through walls | one line of `WorldPathGrid` | nowhere | 4 months | the owner said it out loud |
| levels override the balance table | a research doc | nowhere | 6 days | checking what the next task needed |
| **the licence notice** | `.editorconfig` | **IDE0073 — and it passed** | ~1 hour | changing the text it checked |

So the standing review question needs its third clause. Not only *what would fail
if this claim stopped being true* and *does anything read this fact at all*, but
also: **could this check pass while the thing it checks is wrong?** A check whose
input is silently transformed before comparison — truncated, normalised,
lower-cased, trimmed — is checking something other than what it appears to.

The replacement is instructive on two counts beyond the licence. It is a **test
rather than an analyser**, because a build-time analyser is structurally blind to
`unity/DK2.Unity/Assets` — no project in `DK2.slnx` compiles those files, so the
files most easily forgotten are exactly the ones it cannot see. And it **asserts
its own sample size** (more than 100 files found) before checking any content,
because a sweep that finds nothing passes every content assertion. Both are
cheap; both would have caught a different failure from the one they were written
for.

---

## Addendum, 2026-09-04 (third): a justification can rot on its own

The portal sitting turned up a fifth instance of the pattern, and it is a
different **kind** — which is why it gets its own clause rather than a row in the
table above.

The owner byte on a tile was never cleared when the tile became terrain nobody
can own. The reason was written down, in two files, and it was a good reason:

> *"which owner id means 'nobody' is UNKNOWN (Q17), and every rule that cares
> asks `TerrainDef.IsOwnable` first, so a stale byte on unownable terrain is
> inert."*

**Both halves were false by the time it mattered, and each failed differently.**

**The first half expired.** Q17 — *which player id is neutral?* — was answered on
2026-09-04 by ADR-0043, while chasing something else entirely: 0 is unowned,
confirmed three ways across all 66 shipped levels. The moment that landed, "there
is no id meaning nobody, so we cannot write one" stopped being true. **Nothing
connected the answer to the decision that rested on it.** The answer did not know
what depended on it, and the decision did not know it had been answered.

**The second half was never true at all.** `TileWorkKind.Claim` does not ask
`IsOwnable` first — it tests `OwnerPlayerId != playerId` and nothing else. So the
stale byte was not inert: it made a tile permanently unclaimable by the one
keeper whose id was stuck on it. That is the ordinary "a claim nothing checks may
already be false" shape.

**What it cost:** a keeper who dug through their own reinforced wall could never
grow through the hole. On level 1 that is the *only* route out of the dungeon
heart, so nothing beyond it was ever reachable — no portal, no lair, no treasury.
Four months. It surfaced because a scenario dug toward level 1's portal and then
sat still for 130 game-seconds, which is the loudest a defect has ever been in
this project and still needed somebody to look.

### The third clause

The standing review question was *what would fail if this claim stopped being
true?* and *does anything read this fact at all?* and *could this check pass
while the thing it checks is wrong?* Add:

> **Is this justification still true — and would anybody notice if it stopped
> being?**

A reason that rests on an open question is a dependency, and it is the only kind
of dependency in this project that nothing tracks. Two cheap habits follow, and
both are being adopted rather than merely noted:

1. **When a question is answered, grep for its number.** Q17 appeared in
   `TileWorkRules.Convert`, `RoomRules.Unmake` and the question list. Answering
   it should have been followed by reading the two places that cited it. That is
   a thirty-second check and it would have saved four months.
2. **A justification that names an open question should say what it will do when
   it closes.** "UNKNOWN, so we keep the byte" is a decision with no exit; "we
   keep the byte until Q17 says which id means nobody, and then we clear it" is
   the same decision with a trigger.
