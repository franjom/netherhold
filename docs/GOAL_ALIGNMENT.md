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

## Goal by goal, 2026-10-23

| # | Goal | What exists | What does not | Pointers |
|---|---|---|---|---|
| 1 | Modern DK2 engine | Digging, territory and all tile work as one data-driven rule; rooms as owned terrain, those `dk2tools coverage` lists with behaviour — treasury, heart, portal, lair, hatchery, prison and its jail break, training room, library accrual, temple prayer and sacrifice, graveyard and the Vampire, casino, torture chamber and the Mistress, workshop points and crates carried to blueprints placed in play, guard room, combat pit and its spectators, and the Casino's, Library's, Temple's and Workshop's own creature movement (ADR-0396, 0397, 0403, 0404); gold from seam to vault, wages taken from the whole room underfoot; mana income; creature health, hunger, sleep, anger, levels, leaving, death and jobs; **combat** — melee on both clocks, attack spells as shots, status effects, fear, withdrawal and return, the friendly arm; **hero parties** raised by action 14, running all five standing jobs, re-led after a knock-out, stealing gold and tunnelling; a level won and lost, and what defeat does to the loser's dungeon; alliances; the Hand of Evil with DK2's drop landing; fog of war; a save format of our own; **every creature holds a DK2 state id**, its activity is derived from it, and the category 0 states and the slap's state moves are built (ADR-0400–0402); all thirteen keeper spells — Create Imp, Thunderbolt, Heal, Call to Arms, Inferno, Chicken (ADR-0283, 0287, 0279, 0302), Create Gold, Tremor, Turncoat, Summon Horny, OneShotReaper and Possession's simulation side with its followers, commands and arms (ADR-0350, 0445, 0446, 0468–0470), Sight of Evil's cast with an empty effect (ADR-0352) — and the research tree with every spell upgrade (ADR-0247, 0350); a computer keeper of our own that digs and builds, answers an attack, runs its battle step, retreats the wounded, plans an attack, sends creatures to train and casts Create Gold and Sight of Evil where DK2's does (ADR-0278, 0283, 0284, 0298, 0317, 0352); doors fought and hero gates collapsed, every shipped trap firing, and a room with one health that heroes knock down tile by tile (ADR-0318, 0320, 0321, 0331, 0336, 0342). Since 2026-10-15: the fight approach is DK2's state 8 (ADR-0483); heroes skip the keeper needs (ADR-0485); the navigation mesh's locate and whole-map builder write DK2's slots (ADR-0486); the Imp's work, mining and route timings were corrected against native Level 4 traces (ADR-0502–0519); the Portal Gem (action 20, ADR-0495); DK2's six hero slots (action 42, ADR-0523); creature names and blood groups (ADR-0521); room rim flames (ADR-0524); and the Hand's drop gate from `0x0040D8F0` (ADR-0526) | **Sight of Evil's region (presentation, Q194/Q534); Horny's idle controller; Possession's camera and input in Unity; the rest of M12 (`docs/KEEPER_AI.md`)**. Of M7, creatures fight a revealed trap (ADR-0340) and what is left is the owner's observations (`docs/experiments/doors-and-traps.md`: Q513, Q520, Q524, Q362); shot heights moved to M9 (ADR-0341) and disarming is deferred as unreachable in a fresh v1.70 game (ADR-0329, Q455). `SetState` dispatches the category enter and leave hooks in DK2's order from one table, `CategoryHooks` (ADR-0308); `CategoryHooks.Slots` says for each of DK2's 48 hook slots whether it is built, partial, a stub or not built. Of the **281 states read from the binary**, `dk2tools coverage states` counts those with a handler here, those shipped data never enters, and lists the rest with what blocks them; the activity is only a label derived from the state. Effect generators' runtime (Q255), mentor speech and notifications | plan §8; ADR-0162–0170, 0197, 0203–0231; `docs/HANDOFF.md` §"Current state"; `dk2tools coverage` |
| 2 | Campaign compatibility | All 18 KWD/KLD record types parse and every record image round-trips byte for byte; every campaign map builds a `WorldState`; per-level VARIABLES layer over the global table; every placed THINGS record reaches the world; action points, the trigger flag bank and the timer bank are world state; `TriggerExecutor` walks the world, party, player, action-point and creature roots once per game turn (ADR-0152) and fires DK2's creature and player event filters (ADR-0204). Timing differences against the recordings remain (`navigation-captures.md`, Q444). **These action types are carried out**: 1, 6, 7, 8, 10 and 11 (win and lose), 14 (raises the party with its creatures, ADR-0197), 15 (a hero's standing job, ADR-0236), 18 (alliances, ADR-0377), 20 (the Portal Gem, ADR-0495), 22 (a room tile sold or built, ADR-0363), 23 (the time limit, ADR-0235), 24 and creature action 30 (ADR-0535), 28, 32 (a hero gate's collapse, ADR-0320), 37 (portals closed, ADR-0314), 42's creature arm (ADR-0523), 44, 45 and 47. Scripted camera paths with the letterbox and subtitles play in Unity (ADR-0496, 0498) | **Recorded packets of Levels 1–4 replay headlessly over seeds 1–20, last at pinned hash309 (`490b7d82`, 2026-10-26: wins 18/8/16/1 of 20, Level 4 Imp deaths median 14.5; write-up D:\DK2Evidence\runs\packet-replay-hash309\README.md)**; see [the hash305 rates, hash303/P2 comparisons and validity limits](experiments/2026-10-25-packet-replay-hash305/README.md). This set does not measure later master changes. Restored native Level 2/3 captures allow each seed's entity map to be rebuilt. The owner authorized the same completeness/stability criteria as Level 4, with placeholder counts disclosed. Level 4's per-seed comparison at hash 286 sits inside the native spread on every count except Imp deaths in two of five seeds (`research/imp-fidelity-integration.md`). Open differences: `docs/experiments/2026-10-06-packet-replay-seeds/` (Q567) and `docs/experiments/2026-10-12-level4-packet-replay/`. No level past 4 has been replayed or won, and nobody has played a level interactively from start to finish; many script actions are partial. `dk2tools kwd unreached 600`, run 2026-09-26, puts the 9,161 shipped records at **3,412 ran**, 3,881 reached and closed by a false condition, **110 declined**, 585 under creature roots that stayed closed, 666 under party subtrees nothing armed and 507 orphans — re-run it rather than trusting these. Action 17 (reveal an action point) is carried out through `DiscoveryRules.Rectangle`; actions 26, 31 and 38 emit view requests (ADR-0496), with action 26's glide and the timed moves condition 72 waits for unbuilt. Scripted messages and timed flowers are built (ADR-0535; see `research/message-tabs.md` §10); speech audio remains absent. Action 42's player-context arm remains partial (ADR-0523); actions 25, 27, 34 and 50 and creature actions 21, 29, 33, 35, 36 and 43 are not performed. **Campaign progression** is read (`campaign-progress-registry.md`) and not built; original saves load terrain only (Q182) | ADR-0091, ADR-0101, ADR-0102, ADR-0125, ADR-0152, ADR-0162; Q222–Q228 |
| 3 | Modern rendering | **The separation half is done**: three render targets over one view (text, Unity boxes, headless); zero Unity references in `src/`, asserted from the built assemblies; the view carries a **viewing keeper**, so fog is expressible at the boundary; input gestures translate in the Unity-free presentation assembly. ADR-0242 (2026-09-26) sets the standard a modern asset is held to — preserve the design, raise the fidelity — and measured that DK2's 2,015 KMF files carry no skeleton | **The modern renderer does not exist** (2026-10-23): no modern materials, shadows, effects, UI or high-resolution work; the Unity scene draws the classic look (row 11, gap 5). The asset pipeline of ADR-0242 is at proof-of-concept: the Imp's reference and approved concepts (ADR-0398, ADR-0399, `docs/ASSET_PIPELINE.md` §10), no rig or Unity import. This row read "nothing outstanding" until 2026-09-26, having scored only the separation half of the goal | ADR-0002, ADR-0035, ADR-0076, ADR-0104, ADR-0242, ADR-0391, ADR-0392; `docs/ART_DIRECTION.md`, `docs/ASSET_PIPELINE.md` |
| 4 | Replaceable assets | Every key the shipped database implies resolves to a placeholder with no install, proved over the whole database; `DK2.Convert` writes glTF/GLB, PNG and WAV and round-trips them through `FileAssetProvider`; no filename literal outside `DK2.Assets` | **No renderer loads converted art** (2026-10-23): Unity ships no runtime glTF importer, and `DungeonScene.Instantiate` draws a placeholder for a file-backed asset and warns; the classic look reads the install directly instead (ADR-0408). The catalogue produces mesh, texture, animation and effect keys only: `AssetKind.Sound`, `Music` and `Video` exist and **no catalogue entry produces one** | ADR-0034; `AssetCatalogueTests` |
| 5 | Behavioural fidelity | Confidence tags mandatory in docs and code, with `[Dk2Behavior]` on the rule type; a research note per mechanic on the Appendix B template; refusals on the record; the reading that a zero value with a live reader is *disabled*, not absent. Of the goal's own emphasis list: **pathfinding is the original's** — the navigation mesh and the steering tick drive every creature, and DK2's own route log replays 21,288 of 21,298 requests on Level 7 and all but three of 86,140 on a played Level 10; **combat is built from the binary** (ADR-0163–0170) rather than a rule only tests call; the **state ids are DK2's**; a tick is 250 ms, read and observed live at 3.94/s. A verification sweep re-reads committed ADRs against the binary (`verification-2026-09-26.md`: four wrong claims in ADR-0227–0234) | `capture-diff` scores trigger events. The Level 1 packet and creature trace now compare one played fight through the win tick (Q444); other combat, creature-state and economy rules still lack comparable played traces. The hook bodies `CategoryHooks.Slots` marks not built or stub are behaviour the state ids promise and do not yet run | ADR-0085, ADR-0089, ADR-0102, ADR-0155, ADR-0157; `docs/RESEARCH_STATUS.md`, `docs/VERIFICATION_SWEEP_PLAN.md` |
| 6 | Oracle testing | The scenario format, the headless runner, per-tick hash series, an event log, and a mandatory "what this run could not do" report. A live-memory reader against a running DK2 works. `dk2tools capture-diff` aligns a capture on stored timer stamps and reports by event set; the recorded runs are under `docs/experiments/`, and Level 20 idle produced a window with no difference at all. Two idle captures of DK2's own keepers on Levels 17 and 11A are compared in `computer-keeper-observations.md`. Scenarios that assert carry an `expect:` block (`grep -L '^expect:' -r scenarios --include=*.yaml` lists the ones that do not); those say in the file why they cannot (ADR-0150). Install-gated tests **skip** rather than passing vacuously. Two more oracles run beside it: DK2's **route log**, switched on by a memory write (row 5), and DK2's **world log**, a call that dumps every creature's state on demand (`debug-dumpers.md`, `tools/worldlog.py`). DK2's own `-PSAVE`/`-PLOAD` records a played session and replays it unattended. An observed entity map lets our headless Level 1 replay consume the recording's actions and win (Q444); Level 4 now has one entity map per seed and two native recordings of the same inputs, so DK2's own seed spread is measured rather than assumed (2026-10-21) | `capture-diff` and the scenario rig compare **idle** captures only; the packet replays of Levels 1 to 4 (row 2) are a separate workflow; the same-map keeper comparisons are a third. DKII.EXE's own functions also run under Unicorn as a test oracle (ADR-0481, `tests/DK2.Oracle.Tests`), only for functions that read their arguments, the image and captured state | plan §6, ADR-0100, ADR-0117, ADR-0119, ADR-0125; `docs/experiments/` |
| 7 | Level editor | Container writers, record-image Split/Join, typed overlays for all 17 shipped chunk types and synthetic GLOBALS paths; all 408 files round-trip exactly. DK2.Data loads, edits and saves level sets, safely inserts/removes Things and Triggers, and repaints/resizes maps. `dk2tools kwd rewrite` shares the corpus audit | Editor UI and validation of a level's playability are unbuilt. Standalone GLOBALS and unsafe trigger rewiring remain refused | ADR-0346; `src/DK2.Data/README.md` |
| 8 | Modding framework | The rule *"a balance constant hardcoded in simulation code is a bug"* means every number is read from `GameDatabase`, and every simulation test builds one by hand with no DK2 install — so the hand-authored-database path is proven | **No external content format** (2026-09-26). `GameDatabase` is DK2-shaped, so a mod author would express content in DK2's record shape | plan §7.3 |
| 9 | Deterministic, fast | A pinned state hash, the RNG's position hashed, snapshots verified by continuation rather than byte equality, no floating point in gameplay maths, one explicit system order, fully headless. Profiling markers, kept navigation and sweep state and a worker-thread tick are built (ADR-0444, 0453–0458, 0461, 0466); a parallelism design with determinism gates is proposed (`docs/plans/2026-10-17-parallelism-design.md`) | **Simulation speed is timed but not budgeted** (gap 4): `DK2.Harness` `RunTimings` times every run (total, per system, keeper AI, hashing) and nothing compares it with real time. GitHub CI runs the suite on Linux and Windows (gap 9, closed), so the pinned hash holds across both; the 971 install-gated tests skip there, so campaign-level runs are compared on one machine only | ADR-0001, ADR-0006, ADR-0013; `docs/architecture/simulation-loop.md` |
| 10 | AI/bot interface | `Tick(in InputFrame)` advances time; humans, scenarios and `DK2.AI.KeeperAI` submit `PlayerCommand`s. `KeeperView` captures one tick without mutable world references; `KeeperViewSnapshot` serializes it for an external process. Host pacing and the next-tick command boundary are ADR-0244 | Combat messages, threat assessment, the independent response queue, casualty-priority commands and represented pickup gates are built (ADRs 0245, 0261–0263). Worker pickup/idle flags and ownership order are maintained and the mana-pressure lifecycle is live (ADRs 0266, 0273–0274, 0277). The attack response, battle step, retreat, Heal, Chicken, Inferno and attack planner are built (ADR-0278, 0283, 0284, 0287, 0298, 0302); what remains is in `docs/KEEPER_AI.md` section 12, and external-agent transport/host is unbuilt. The scenario harness takes in-process test controllers that command one player (ADR-0552, Stage A of `docs/plans/2026-10-27-autonomous-playtesting.md`); their commands are journaled and replayable (Stage B, `replay`), and `run --fuzz` plays a keeper with a legal-action fuzzer (Stage C); Unity can journal its raw input and the frames it gives each tick (ADR-0553 Phase 1, off by default; moving the input mapping into `DK2.Presentation` is planned) | `docs/KEEPER_AI.md` section 12; ADR-0244; ADR-0552; ADR-0553 |
| 11 | Presentation modes | Headless, text and Unity boxes: three targets over one view, each with its own redraw strategy, which is what proves the boundary assumes neither; the Unity adapter has camera controls and builds `InputFrame`s through the presentation translator | Of the goal's three layers only headless exists in full (2026-10-23). The **classic look** draws terrain, walls, rooms, liquids, objects, effects and animated creatures from the install (steps 0–4); step 5's camera and lighting are on master and owner-accepted in appearance but not formally closed (matched-original comparisons pending, ADR-0522). **Step 6, the HUD**, is being built on `classic-look/step6` and merged to master as it goes: the top bar's live gold, mana and Heart ring, the minimap and its overlays, the creature panel's four modes, tooltips, the bottom panel's flash, message tabs and Review Objective (ADR-0525), status flowers, and the Hand's cursor, hover cage, placement cage and drop gate (`docs/plans/2026-10-17-classic-look-step6.md`; Q611 holds the remaining panel rules). Front-end scope: see `docs/plans/2026-10-23-front-end.md` (F1-F4 built; campaign session/results integration remains). **Not built**: a level-end screen, gameplay audio, and the modern renderer (row 3) | ADR-0035, ADR-0036, ADR-0104 |
| 12 | Preservation | Downstream of 1–3. The goal preserves *playable behaviour*, which is where the work has gone: every mechanic built carries its research note and confidence tag | Nothing to assess independently yet | |

## Gaps

Numbered for citation; the numbers do not move.

| # | Gap | Status |
|---|---|---|
| 1 | **Q1 (tick rate) makes goal 5 depend on goal 6.** Goal 5 names game timing explicitly and the rate could not be answered from the data files | **Closed** — but not by the oracle probe as predicted: it was a *reading* question, settled from the binary and then corroborated live (ADR-0089, ADR-0102). A stopwatch could never have separated *5 ticks a second at work rate 0.8* from *4 at 1.0* |
| 2 | **Breadth has not started, and goal coverage will look flat until it does** | **Overtaken** (2026-09-26). Plan revision 2 put M6, M7 and M12 ahead of breadth, and breadth began anyway beside M7: the Casino, the Torture Chamber, the Workshop, the Guard Room and the Combat Pit are M8 rooms, built before traps and hero gates were (ADR-0320, ADR-0321). Each was read from the binary first, so the fidelity rig's purpose held; what slipped is the order. Hold further breadth to the plan's Run column |
| 3 | **The asset layer must land in the same milestone as the first Unity view, not after it** — cheap while nothing depends on an asset, expensive the moment a renderer exists | **Closed 2026-09-04**, on the right side of its deadline (ADR-0034, cited from ADR-0034's own context) |
| 4 | **Simulation speed is timed but not budgeted.** Goal 9 wants "significantly faster than real time"; `RunTimings` measures every headless run and nothing compares it with real time | **Open**. Pick a budget and assert it before the shape of the loop hardens — more so now that combat, heroes and the navigation mesh run every tick |
| 5 | **The Unity scene draws boxes**, not DK2's walls | **Open, steps 0–4 merged, step 5 on master and not formally closed, step 6 (the HUD) in progress** (2026-10-23). ADR-0408 chose the route, reading the install directly under URP, and `docs/plans/2026-10-09-classic-look.md` holds the steps: terrain, walls, rooms, liquids, objects and creature animation are drawn; step 5's camera and lighting are on master (camera core owner-accepted, ADR-0478; light buffers ADR-0484, ADR-0487; Unity fill by default, ADR-0522) and the owner has accepted the appearance, with matched-original comparisons pending; step 6's HUD is built on `classic-look/step6` and merged to master as it goes (`docs/plans/2026-10-17-classic-look-step6.md`). It is goal 3's blocker: no modern renderer can be judged against a classic look that does not exist |
| 6 | **`ArchitectureTests` did not enforce what ADR-0002 claimed**: an assembly Unity loads could silently be net10.0 | **Closed** — the assertion now reads `TargetFrameworkAttribute` off the built assemblies |
| 7 | **The omniscient view.** Fog was simulation state and no renderer could honour it, because the presentation boundary had no notion of who was looking | **Closed** — `WorldView` takes a viewing keeper and substitutes terrain 27 for anything that keeper has not discovered |
| 8 | **The silent skip.** A test that returned early on a missing install was indistinguishable from a test that passed, so a green suite overstated what was checked by hundreds of tests | **Closed** — ADR-0100: `[InstallFact]`/`[InstallTheory]` set `Skip`, and `InstallGateTests` fails the build if a `return`-on-null reappears |
| 9 | **Nothing runs the suite but a person.** `ci.yml` declares Linux and Windows, and the repository had no remote, so the cross-machine determinism goal 9 claims and M14's lockstep depends on had been observed on one machine | **Closed** (2026-10-29). The repository is on GitHub, and since 2026-10-24 `ci.yml` builds and tests on ubuntu-latest and windows-latest for every push to master and every pull request, with a docs and KB gate beside them; the pin passes on both. Left for M14: CI has no game install, so the 971 install-gated tests skip there, and campaign-level determinism (the packet replays) is observed on one machine |
| 10 | **Our game has no debug tooling of its own.** DK2 ships dumpers, a world log, a route log, per-subsystem checksums and an out-of-sync handler; we use them as oracles, but a crash or wrong behaviour in our Unity build cannot be replayed | **Open, partly built** (planned 2026-10-18). The harness half of item 1 is built (ADR-0552 Stage B: `input.journal`, `replay`, `watch`), with coverage and a save/restore continuation check (Stage C); Unity writes the same journal when asked (ADR-0553 Phase 1, not yet run in the editor; the harness cannot replay it yet). `docs/plans/2026-10-18-crash-and-debug-tooling.md`: input journal plus rolling snapshot as the crash dump, per-subsystem hashes, world dump, debug invariants, and analysis tools (a replay core with CLI and an HTML report, plus a replay mode in Unity with a timeline and an inspector). Deadline: per-subsystem hashes before M14 |

## Watch list — not problems yet

- **`GameDatabase` is DK2-shaped.** Its records mirror KWD layout field for
  field, including string-table ids. Correct for fidelity; it does mean goal 8's
  mod author would express content in DK2's record shape. Revisit at M6.
- **`WorldState` grows one mechanic at a time, on purpose.** Good discipline, but
  goal 10 will eventually want a stable observation schema, and that schema is
  still moving. Do not freeze a bot-facing API early — but design it for a
  consumer outside the process (goal 10's row), not as a C# convenience for
  `KeeperAI`.

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
about *this project* had stopped being true with nothing re-reading it. Each was
corrected that day; the list was cut from `docs/HANDOFF.md` with the rest of its
history, and git holds it.

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
