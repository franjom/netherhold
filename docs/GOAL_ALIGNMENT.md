# Goal alignment review

Where the project stands against the twelve long-term goals in `AGENTS.md`.

Those goals are **end-state objectives**, and `AGENTS.md` is explicit that they
must not dictate premature implementation. This document exists for the other
failure mode: drifting away from them without noticing, or believing a goal is
served when only its easy half is built.

**Re-run this at every milestone.** The method:

1. For each goal, list what a reader could actually do today — not what the
   architecture permits.
2. Separate *"the structure is in place"* from *"the thing exists"*. These are
   not the same, and conflating them is how a plan starts lying to itself.
3. Name the gaps that have a deadline attached, and say what the deadline is.
4. **Rewrite the table.** Do not append a dated one beside it; eight
   prose-only reviews each said "the table above stands" while four of its rows
   had moved.

Counts are not kept here. `dk2tools status` prints them; `docs/HANDOFF.md`
§"Current state" says how.

---

## Goal by goal, 2026-09-18

| # | Goal | What exists | What does not | Pointers |
|---|---|---|---|---|
| 1 | Modern DK2 engine | Digging, territory and all tile work as one data-driven rule; rooms as owned terrain, ten of them with behaviour (treasury, heart, portal, lair, hatchery, prison, training room, library accrual, temple prayer and sacrifice, graveyard and the Vampire); gold from seam to vault, wages paid as a journey; mana income; creature health, hunger, sleep, anger, levels, death and jobs; the Hand of Evil; fog of war; a save format of our own; a computer keeper that digs and builds | **Combat (Q8), spells beyond Create Imp, traps in the simulation, heroes, most trigger actions, objectives, campaign progression.** A door opens, locks and writes its navigation byte (ADR-0158); nothing else of one is built. The creature state machine has **21 activities against the 281 states read from the binary** | plan §8, `docs/HANDOFF.md` §"What exists" / §"What does not exist" |
| 2 | Campaign compatibility | All 18 KWD/KLD record types parse and every record image round-trips byte for byte; every campaign map builds a `WorldState`; per-level VARIABLES layer over the global table; every placed THINGS record reaches the world — doors, traps, hero parties and effect generators as verbatim behaviour-free state; action points are ordered world state; the trigger flag and timer banks are world state; `TriggerExecutor` walks the world, party, player, action-point **and creature** roots once per game turn (ADR-0152) | **Most action effects are still absent**: `TriggerExecutor.Performs` carries out five types (1, 7, 8, 14's arming half, 22) and answers for every other as DK2's dispatcher does. Do not transcribe a coverage figure here — `dk2tools kwd unreached <turns>` measures it, and run idle for 600 turns it leaves **864** records behind party subtrees nothing arms (action 14 creates no creatures, Q254), **585** under creature roots that never opened and **124** behind conditions this executor declines. No objectives and no win condition, so **no level is winnable**; the state doors, traps, parties and generators carry drives no behaviour; original saves are terrain-grid only (Q182) | ADR-0091, ADR-0101, ADR-0102, ADR-0105, ADR-0125; Q222–Q228, Q235–Q238 |
| 3 | Sim/render separation | Three render targets over one view (text, Unity boxes, headless); zero Unity references in `src/`, asserted from the built assemblies; the view carries a **viewing keeper**, so fog in the simulation is expressible at the boundary and two keepers see different things; input gestures translate in the Unity-free presentation assembly | Nothing outstanding | ADR-0002, ADR-0035, ADR-0076, ADR-0104; `docs/ARCHITECTURE.md` §Presentation |
| 4 | Replaceable assets | Every key the shipped database implies resolves to a placeholder with no install, proved over the whole database; `DK2.Convert` writes glTF/GLB and PNG and round-trips them through `FileAssetProvider`; no filename literal outside `DK2.Assets` | **No renderer loads converted art** — Unity ships no runtime glTF importer and `DungeonScene` has no branch for it. The catalogue produces mesh, texture, animation and effect keys only: **no sound, music or video key is ever produced** | ADR-0034; `AssetCatalogueTests` |
| 5 | Behavioural fidelity | Confidence tags mandatory in docs and code, with `[Dk2Behavior]` on the rule type; a research note per mechanic on the Appendix B template; refusals on the record (Q20 not "fixed", Q24 not invented); shipped balance fields found dead — and the sharper reading that a zero value with a live reader is *disabled*, not absent | Of the goal's own emphasis list, **combat is read and wired to nothing** — the damage formula is a rule only tests call (ADR-0153) — while **pathfinding is now the original's**: every creature routes over the navigation mesh and moves by the steering tick (ADR-0155, ADR-0157 phase 3), and `GridPathfinder` survives only behind `TryFindNearest`. The state machine is 21 of 281. The time base is no longer the gap it was: a tick is 250 ms, read from the binary and observed live at 3.94/s | ADR-0085, ADR-0089, ADR-0102; `docs/RESEARCH_STATUS.md` |
| 6 | Oracle testing | The scenario format, the headless runner, per-tick hash series, an event log, and a mandatory "what this run could not do" report. A live-memory reader against a running DK2 works. `dk2tools capture-diff` is the comparer: it aligns a capture on stored timer stamps and reports by event set, an optional `dk2run6.csv` gates a regression test, and three runs are recorded under `docs/experiments/`. Level 20 idle produced a window with no difference at all. **25 of the 29 scenarios assert**; the four that do not say in the file why they cannot (ADR-0150). Install-gated tests **skip** rather than passing vacuously. A second oracle runs beside the first: DK2's own route log, switched on by a memory write, replays every request the original made against our router — 21,288 of 21,298 on Level 7, and on a Level 10 played by a keeper every request but three of 86,140 (`navigation-captures.md`) | `capture-diff` has compared **idle** captures only, and an idle run can exercise at most **29% of a level's script** (ADR-0125). The route log has since been captured from a played level, but the scenario rig itself has seen nothing but the quietest case | plan §6, ADR-0100, ADR-0117, ADR-0119, ADR-0125; `docs/experiments/` |
| 7 | Level editor | Readers for all 18 record types, and a `dk2tools` surface that dumps most of them | **No writers of any kind.** Not blocked by anything | ADR-0091 |
| 8 | Modding framework | The rule *"a balance constant hardcoded in simulation code is a bug"* means every number is read from `GameDatabase`, and every simulation test builds one by hand with no DK2 install — so the hand-authored-database path is proven | **No external content format.** `GameDatabase` is DK2-shaped, so a mod author would express content in DK2's record shape | plan §7.3 |
| 9 | Deterministic, fast | A pinned cross-machine state hash, the RNG's position hashed, snapshots verified by continuation rather than byte equality, no floating point in gameplay maths, one explicit system order, fully headless | **Simulation speed has never been measured** (gap 4). The cross-machine claim rests on local runs: `ci.yml` declares a Linux + Windows matrix but no CI is running | ADR-0001, ADR-0006, ADR-0013; `docs/architecture/simulation-loop.md` |
| 10 | AI/bot interface | `Tick(in InputFrame)` is the only way time moves and `PlayerCommand`s are the only input — the observe → act → advance loop the goal draws. Eight command types | **Observation is still raw `WorldState`**; there is no agent-shaped schema, and it should not be frozen while `WorldState` is still growing | `docs/KEEPER_AI.md` §12 |
| 11 | Presentation modes | Headless, text and Unity boxes: three targets over one view, each with its own redraw strategy, which is what proves the boundary assumes neither; the Unity adapter has camera controls and builds `InputFrame`s through the presentation translator | Boxes rather than DK2's walls (gap 5, Q71) | ADR-0035, ADR-0036, ADR-0104 |
| 12 | Preservation | Downstream of 1–3 | Nothing to assess independently yet | |

## Gaps

Numbered for citation; the numbers do not move.

| # | Gap | Status |
|---|---|---|
| 1 | **Q1 (tick rate) makes goal 5 depend on goal 6.** Goal 5 names game timing explicitly and the rate could not be answered from the data files | **Closed** — but not by the oracle probe as predicted: it was a *reading* question, settled from the binary and then corroborated live (ADR-0089, ADR-0102). A stopwatch could never have separated *5 ticks a second at work rate 0.8* from *4 at 1.0* |
| 2 | **Breadth has not started, and goal coverage will look flat until it does** | Open by design. This is the plan's deliberate ordering: build the fidelity rig before accumulating plausible-but-wrong behaviour that is expensive to unwind. Do not read a flat table as a stalled project, and do not "fix" it by adding breadth ahead of M5 |
| 3 | **The asset layer must land in the same milestone as the first Unity view, not after it** — cheap while nothing depends on an asset, expensive the moment a renderer exists | **Closed 2026-09-04**, on the right side of its deadline (ADR-0034, cited from ADR-0034's own context) |
| 4 | **Simulation speed is an unverified claim.** Goal 9 wants "significantly faster than real time" and nobody has timed a headless run | **Open.** An afternoon's work, worth doing before the shape of the loop hardens |
| 5 | **One mesh per solid terrain** in the Unity view — boxes rather than DK2's walls | **Open**, waiting on Q71 |
| 6 | **`ArchitectureTests` did not enforce what ADR-0002 claimed**: an assembly Unity loads could silently be net10.0 | **Closed** — the assertion now reads `TargetFrameworkAttribute` off the built assemblies |
| 7 | **The omniscient view.** Fog was simulation state and no renderer could honour it, because the presentation boundary had no notion of who was looking | **Closed** — `WorldView` takes a viewing keeper and substitutes terrain 27 for anything that keeper has not discovered |
| 8 | **The silent skip.** A test that returned early on a missing install was indistinguishable from a test that passed, so a green suite overstated what was checked by hundreds of tests | **Closed** — ADR-0100: `[InstallFact]`/`[InstallTheory]` set `Skip`, and `InstallGateTests` fails the build if a `return`-on-null reappears |

## Watch list — not problems yet

- **`GameDatabase` is DK2-shaped.** Its records mirror KWD layout field for
  field, including string-table ids. Correct for fidelity; it does mean goal 8's
  mod author would express content in DK2's record shape. Revisit at M6.
- **`WorldState` grows one mechanic at a time, on purpose.** Good discipline, but
  goal 10 will eventually want a stable observation schema, and that schema is
  still moving. Do not freeze a bot-facing API early.

---

## The standing review question

This project's characteristic defect is **a true statement about itself that
nothing re-reads**. Seven clauses have been added to the review question, each
from a defect with a different shape, and each has a mechanical check that costs
minutes. Other documents cite these by number; the numbering does not move.

| # | Ask | The check that follows |
|---|---|---|
| 1 | **What would fail if this claim stopped being true?** | If the answer is "nothing", the claim is decoration and may already be false. |
| 2 | **Does anything read this fact at all?** | When a doc states something the code should be doing, name the code that does it, or say plainly that none does. Research docs are full of unwired facts *by design* — the check is not "everything must be implemented". |
| 2a | **Could this check pass while the thing it checks is wrong?** | A present, green check is the worst case, because it buys silence. Never given a number of its own; it sits in the chain between 2 and 3. |
| 3 | **Is this justification still true — and would anybody notice if it stopped being?** | When a question is answered, `grep -rn "Qnnn" src/ docs/ scenarios/` and read every hit. A justification naming an open question should say what it will do when that question closes: not "UNKNOWN, so we keep the byte" but "we keep the byte until Q17 says which id means nobody, and then we clear it". |
| 4 | **When a comment says a difference is unobservable, does it name the observation that would settle it?** | Grep for hedges — "indistinguishable", "differs only in", "not observed", "makes no difference" — and check each names a test that exists. A hedge that describes its own refutation is a task, not a justification. "Indistinguishable **on the shipped data**" is a limit of the evidence and is honest; "indistinguishable on level 1" is a statement about one level out of 66. Whether an imp's half-second linger is a cooldown or an animation is the third kind and needs no re-audit: it really is invisible from outside, so it is a decompiler question (Q49, Q149). |
| 5 | **Does a statement about the original rest on a reading that names its own gap?** | "Three of these four tests refer to things we do not have" is a task, not a finding. Grep the research docs for gap-naming phrases — "unidentified", "not read", "none of which", "unread" — sitting in the same paragraph as a conclusion about DK2. Naming the gap **instead of** a conclusion is the shape to keep; naming it beside one is not. |
| 6 | **When a question is answered, grep for everything that cites it as a blocker.** | A "blocked on Qnnn" note is a claim with an expiry date and nothing re-reads it. Run `grep -rn "Qnnn" src/ docs/ scenarios/` on **every** question that moves to ANSWERED, the same day, and read each hit for whether it still says something true. |
| 7 | **"Nothing depends on this" is a claim about code nobody has read yet.** | Write it in the checkable form: *nothing in `<the routine you actually read>` depends on this*. A dismissal with no scope is indistinguishable from a dismissal that is wrong. And when the same fact is stated in two places, make one of them a test — then only one of them can be wrong for long (`CreatureNeedsOnRealDataTests` counts the food flag for exactly this reason). |

The clauses were turned on the project's own description of itself once, on
2026-09-06, and found eleven places across six documents where a true statement
about *this project* had stopped being true with nothing re-reading it. Each is
now a one-line entry in `docs/HANDOFF.md` §"Corrections carried forward", which
is where a correction with a reusable shape belongs.

Four defects that produced clauses 1–3, kept because the shape recurs:

| stated where | enforced where | how long |
|---|---|---|
| netstandard2.1 target, in `Directory.Build.props` behind a condition evaluated too early | nowhere | 4 months |
| "creatures do not walk through walls", in one line of `WorldPathGrid` | nowhere | 4 months |
| "levels override the balance table", in a research doc | nowhere | 6 days |
| the MIT file header, enforced by Roslyn's `IDE0073` with a `file_header_template` | a check that passed while the header was wrong | until ADR-0041 |

The fifth was the costliest and is the clearest case for clause 3. A tile's owner
byte was never cleared when the tile became terrain nobody can own, and the
written reason had two halves. The first **expired**: it rested on Q17, *which
player id means nobody*, which ADR-0043 answered while chasing something else —
and nothing connected the answer to the decision resting on it. The second was
**never true**: `TileWorkKind.Claim` did not ask `IsOwnable` first, it tested
`OwnerPlayerId != playerId`, so the stale byte made a tile permanently
unclaimable by the one keeper whose id was stuck on it. A keeper who dug through
their own reinforced wall could never grow through the hole, and on level 1 that
is the *only* route out of the dungeon heart. Four months, surfaced by a scenario
that dug toward the portal and then sat still for 130 game-seconds.

Three runs of these checks are worth recording.

- **Clause 6, on Q132.** The paragraph describing the run was first written from
  memory as "four hits, of which three were stale". The actual grep returned
  **38 hits across seven files**, of which **nine were live and stale** —
  guessing at the output of the command you are recommending is the same error
  one level up.
- **Clause 4.** Seventeen hits, of which one was a real task:
  `PortalRules.PoolLimit`'s "indistinguishable" was about level 1 alone, so Q87
  went onto the observation checklist after four months.
- **Clause 5.** Two hits, both honest, which is the useful outcome: `prison.md`
  §2 says the pick-up handler's *ownership* test is unread and declines to
  conclude anything from it, and `imp-task-priority.md` says kind 11's raise site
  is unread and draws no behaviour from it.

## What early decisions are already paying for

Four choices made before they were strictly necessary, each of which goal 5, 8, 9
or 10 would otherwise have made expensive to retrofit. This is the section to
check when a shortcut is being considered.

- **Compiler-enforced Unity independence** (ADR-0002) — goals 3, 9, 11.
- **Integer/fixed-point gameplay maths** (ADR-0001) — goals 5, 9.
- **Command-driven input frames** (`PlayerCommand` / `InputFrame`) — goals 9, 10.
- **Every number read from `GameDatabase`, never hardcoded** — goals 5, 8.

## Two results goal 5 exists to produce

Kept as calibration for what the discipline is for.

**A schema assumption refuted by the data, and the code changed.**
`PlayerState.Gold` was written on the natural assumption that gold is a
player-level resource. Extracting the balance table showed DK2 has no such
variable — four limits, every one per place — so gold moved onto the tiles and
the counter was deleted (ADR-0015). Three behaviours stopped being special cases
as a result.

**A self-consistent wrong reading, caught by one measured number.** One
observation — 273 gold a swing — settled tile-work granularity for the whole
game, and the answer was not the plausible one that had been recorded twice. The
wrong reading fitted every range and would have been built if the discipline had
been weaker. It has since been superseded in turn: five applications a second was
never DK2's cadence, and the fifth is the *size* of one application
(`tabled × 0.8 ÷ 4`) — work lands once a tick, four a second (ADR-0089).
