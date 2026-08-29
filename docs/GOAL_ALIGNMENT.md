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
plus a worker that does it). 163 tests, ~2,200 lines of research documentation.
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
