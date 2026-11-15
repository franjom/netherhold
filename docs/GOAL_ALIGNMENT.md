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
5. Keep a row to what decides the goal. A mechanic's detail has a home already
   (an ADR, a research note, a question row, `dk2tools coverage`); link to it.

Counts are not kept here. `dk2tools status` prints them; `docs/HANDOFF.md`
§"Current state" says how. A figure quoted below carries the date it was measured.

---

## Goal by goal, 2026-11-14

| # | Goal | What exists | What does not | Pointers |
|---|---|---|---|---|
| 1 | Modern DK2 engine | The dungeon loop runs headless from shipped data: tile work, rooms with their behaviour, gold and mana, creature needs, jobs, anger and levels, combat with spells, fear and withdrawal, hero parties, doors, traps and hero gates, the Hand, fog, research, all thirteen keeper spells, effect generators (ADR-0648), runtime speech (ADR-0583), winning and losing, alliances and a save format of our own. Every creature holds a DK2 state id, and `SetState` runs DK2's category hooks from one table (ADR-0308, ADR-0400–0402). A computer keeper digs, builds, defends, attacks, moves prisoners, staffs research and uses its specials (ADR-0726, 0735, 0743). Since 2026-10-29 most simulation changes are turn-level corrections found by replaying native recordings (row 5) | Sight of Evil casts with no effect; its region is unbuilt (ADR-0352, Q194). Horny's idle controllers (`research/special-role-idle.md`). Possession's first-person view is built but never seen running in an editor; not built: the death view, the entry and exit colour changes, the two special-ability keys, the possession HUD and the third-person view (`research/possession-client.md`). The rest of M12 (`docs/KEEPER_AI.md` §12.7–12.8, Q563). Trap disarming, deferred as unreachable in a fresh v1.70 game (ADR-0329, Q455). The creature states and hook slots with no handler: `dk2tools coverage states` and `CategoryHooks.Slots` list them. Original-game acceptance of traps and doors (`research/traps.md`). DK2's own defects are reproduced, by decision, until the game is complete (ADR-0783) | plan §8; `docs/HANDOFF.md`; `dk2tools coverage` |
| 2 | Campaign compatibility | All 18 KWD/KLD record types parse and every record image round-trips byte for byte; every campaign map builds a `WorldState`; per-level variables layer over the global table. `TriggerExecutor` walks the world, party, player, action-point and creature roots once a game turn and fires DK2's event filters (ADR-0152, ADR-0204). The action types carried out are `TriggerExecutor.Performs` and the creature arms in its `RunAction`. Scripted camera paths, messages, timed flowers and the Mentor's countdown play (ADR-0496, 0535, 0767). Campaign progression, the map, briefing and result loop and versioned save and load are built. The campaign bot wins Level 1 from the unmodified start on seeds 1–3 with a receipted build, and Levels 2 and 3 on seeds 1–3 with its default strategy (row 10) | Nobody has played a level interactively from start to finish. The bot loses Level 4 and has won nothing later ([stage 3 plan](plans/2026-11-14-campaign-bot-stage3.md), Q710). The owner's recorded games of Levels 1–4 were last replayed over seeds 1–20 at hash 309 (2026-10-26, wins 18/8/16/1 of 20) and have not been measured since (gap 11). Level 5 seed 1's controlled replay matches DK2's random draws through turn 2155 and differs at 2156 (Q772, Q811): a prefix of draws, not an outcome. Open outcome questions: Q710 (Level 4), Q582 and Q586 (Level 7), Q567 (the Lord on Levels 2 and 3). Not performed: player actions 25, 27, 34 and 42's player arm; creature actions 21, 29, 36 and 35's death fall; world types 4, 5, 39, 40, 41 and 48, which return DK2's success with no effect. `dk2tools kwd unreached 600`, run 2026-11-14, puts the 9,161 shipped records at 3,411 ran, 3,884 closed by a false condition, 96 declined, 585 under creature roots that stayed closed, 678 under party subtrees nothing armed and 507 orphans. Original saves load terrain only (Q182) | ADR-0091, ADR-0102, ADR-0152; `research/trigger-walkers.md`, `research/trigger-action-arms.md`, `research/level4-outcomes.md`, `research/resync-replay.md` |
| 3 | Modern rendering | The separation half: three render targets over one view (text, Unity, headless); no Unity reference in `src/`, asserted from the built assemblies; the view carries a viewing keeper, so fog is expressible at the boundary. ADR-0242 sets the standard a modern asset is held to: preserve the design, raise the fidelity | The modern renderer does not exist: no modern materials, shadows, effects, UI or high-resolution art. The Unity scene draws the classic look (row 11). The asset pipeline is at proof of concept: the Imp's reference and approved concepts, no rig and no Unity import (`docs/ASSET_PIPELINE.md` §10) | ADR-0002, ADR-0242, ADR-0398, ADR-0399; `docs/ART_DIRECTION.md` |
| 4 | Replaceable assets | Every key the shipped database implies resolves to a placeholder with no install; `DK2.Convert` writes glTF/GLB, PNG and WAV and round-trips them through `FileAssetProvider`; no filename literal outside `DK2.Assets` | No renderer loads converted art: Unity has no runtime glTF importer, and `DungeonScene` draws a placeholder for a file-backed asset. The classic look, its sounds, speech and music read the install directly (ADR-0408). The catalogue produces mesh, texture, animation and effect keys; `AssetKind.Sound`, `Music` and `Video` exist and no catalogue entry produces one | ADR-0034; `AssetCatalogueTests` |
| 5 | Behavioural fidelity | One confidence scale across notes, function records and code (ADR-0642); a record per DKII.EXE function, held current by `dk2tools kb check`; a ledger of verified rules (`verification-ledger --check`). Pathfinding is the original's: DK2's own route log replays 21,288 of 21,298 requests on Level 7 and all but three of 86,140 on a played Level 10. Combat, the state ids and the 250 ms turn are read from the binary. Native recordings now drive corrections a turn at a time: Level 5 seed 1 matches all 8,466 of DK2's draws for turns 1401–1900 and continues to 2155 (Q712, Q772) | Draw parity exists for one seed of one level, over a prefix. Full-state parity is established nowhere: on that same run five creatures sit at small position offsets and some state words differ in representation (Q773, Q774), and the restore and adapter gaps are separate (Q713, Q719, Q723, Q736, Q738); [Q714 restores bounded Heart/terminal state](research/resync-heart-terminal.md). Most combat, creature-state and economy rules still have no matched played trace. Rules are not tuned to outcome averages (`docs/HANDOFF.md`), so an outcome gap stays open until its cause is read | ADR-0085, ADR-0089, ADR-0642, ADR-0660; `docs/RESEARCH_STATUS.md`, `docs/VERIFICATION_SWEEP_PLAN.md`, `research/open-questions.md` |
| 6 | Oracle testing | The scenario format, the headless runner, per-tick hash series, an event log and a mandatory "what this run could not do" report. Against the running original: a live-memory reader, `capture-diff`, DK2's route log and world log, `-PSAVE`/`-PLOAD` packet recordings replayed headlessly with a per-seed entity map, replay resynced from DK2's own states (`replay --resync`), a native draw log compared draw for draw, and the complete recorder for new captures (`tools/oracle/native-capture.md`). DKII.EXE's own functions run under Unicorn as a test oracle (ADR-0481). Evidence is kept add-only outside the clone (`AGENTS.md`) | These are separate workflows, each run by hand at one hash; nothing reruns a comparison when the simulation changes (gap 11). `capture-diff` and the scenario rig compare idle captures only. Plan M9 asks for 500+ behavioural regression scenarios; `dk2tools status` counted 53 on 2026-11-14. The Unicorn oracle covers only functions that read their arguments, the image and captured state. CI has no install, so none of this runs there | plan §6; ADR-0100, ADR-0117, ADR-0119, ADR-0125, ADR-0481; `F:\DK2Evidence\studies` |
| 7 | Level editor | The data layer: container writers, record-image Split/Join, typed overlays for all 17 shipped chunk types; all 408 files round-trip exactly. `DK2.Data` loads, edits and saves level sets, safely inserts and removes Things and Triggers, and repaints and resizes maps. `tools/levelgen` builds test levels the original loads | No editor UI, and no validation that a level is playable. Standalone GLOBALS and unsafe trigger rewiring remain refused | ADR-0346; `src/DK2.Data/README.md`; `tools/levelgen/README.md` |
| 8 | Modding framework | Every balance number is read from `GameDatabase`, and every simulation test builds one by hand with no DK2 install, so the hand-authored-database path is proven | No external content format. `GameDatabase` is DK2-shaped, so a mod author would express content in DK2's record shape (watch list) | plan §7.3 |
| 9 | Deterministic, fast | A pinned state hash that passes on Linux and Windows CI, the RNG's position hashed, snapshots verified by continuation, no floating point in gameplay maths, one explicit system order, fully headless. Journals replay to the same hashes without their controller. The tick runs on a worker thread while the frame reads a presented copy of the world (ADR-0694). Speed is budgeted: campaign Levels 1–4 headless at 50x real time or better, no tick over 100 ms after the first two (ADR-0575) | The speed budget is an opt-in test, not in CI or the default run (gap 4). The install-gated tests skip in CI, so campaign-level determinism is observed on one machine (gap 9). Parallel simulation is a draft design (`plans/2026-10-17-parallelism-design.md`) | ADR-0001, ADR-0006, ADR-0013, ADR-0575, ADR-0694; `docs/architecture/simulation-loop.md`, `docs/PERFORMANCE.md` |
| 10 | AI/bot interface | `Tick(in InputFrame)` advances time; humans, scenarios, the computer keeper and bots submit `PlayerCommand`s. A detached player contract (`DK2.Simulation.Players`, `DK2.AI.Players`) gives a bot a visibility-safe `PlayerObservation` and bounded action admission; `run --campaign-player` plays, journals and reports a verdict, and a receipt ties a win to its build. `run --fuzz` plays a keeper with a legal-action fuzzer. `KeeperViewSnapshot` serializes one tick for another process. Command decoding is bounded against hostile input, and a peer's command in another player's name is refused | The bot is one in-process controller with one default strategy; playbooks and the self-play tuner are planned and unbuilt ([stage 3 plan](plans/2026-11-14-campaign-bot-stage3.md)). No transport or host for an agent outside the process. Unity input replay and rendered-HUD automation are follow-up work. No network code: lockstep is a decision and a plan (ADR-0724, `plans/2026-11-09-seamless-lockstep.md`). The computer keeper's remaining work is row 1's | ADR-0244, ADR-0552, ADR-0553, ADR-0724; [design](plans/2026-11-07-agentic-campaign-playtesting-design.md); `src/DK2.Harness/README.md` |
| 11 | Presentation modes | Headless, text and Unity over one view. The classic look is on master: terrain, walls, rooms, liquids, objects, effects, animated creatures, camera and lighting, the HUD, the front end, cutscenes, speech, music and world sounds, drawn and played from the install. Standalone players build with IL2CPP and record their own frames (ADR-0690, ADR-0691) | Of the goal's three layers only headless is complete. The classic layer is built and not accepted: owner play, visual and hearing checks are open (gap 5). The every-frame 60 FPS target is unmet (`docs/PERFORMANCE.md`). The modern layer does not exist (row 3) | ADR-0035, ADR-0104, ADR-0408, ADR-0522; [completion and acceptance](plans/2026-10-29-hud-and-front-end-completion.md); [presented-world result](plans/2026-11-06-presented-world.md) |
| 12 | Preservation | Downstream of 1–3. The repository is public, builds and tests without an install or Unity, and ships no original content. Every mechanic built carries its research note and confidence tag, so what is preserved is the behaviour and the evidence for it | Nothing to assess independently until a campaign is playable (rows 1 and 2) | `README.md`; ADR-0041 |

## Gaps

Numbered for citation; the numbers do not move.

| # | Gap | Status |
|---|---|---|
| 1 | **Q1 (tick rate) makes goal 5 depend on goal 6.** Goal 5 names game timing explicitly and the rate could not be answered from the data files | **Closed** — but not by the oracle probe as predicted: it was a *reading* question, settled from the binary and then corroborated live (ADR-0089, ADR-0102). A stopwatch could never have separated *5 ticks a second at work rate 0.8* from *4 at 1.0* |
| 2 | **Breadth has not started, and goal coverage will look flat until it does** | **Overtaken** (2026-09-26). Plan revision 2 put M6, M7 and M12 ahead of breadth, and breadth began anyway beside M7. Each room was read from the binary first, so the fidelity rig's purpose held; what slipped is the order. Hold further breadth to the plan's Run column |
| 3 | **The asset layer must land in the same milestone as the first Unity view, not after it** — cheap while nothing depends on an asset, expensive the moment a renderer exists | **Closed 2026-09-04**, on the right side of its deadline (ADR-0034, cited from ADR-0034's own context) |
| 4 | **Simulation speed was timed but not budgeted.** Goal 9 wants "significantly faster than real time"; `RunTimings` measured every headless run and nothing compared it with real time | **Closed 2026-10-29** (ADR-0575). `SpeedBudgetTests` assert 50x real time on campaign Levels 1-4 and 100 ms per tick after the first two, opt-in (install, Release build, `DK2_SPEED_BUDGET=1`), run alone on an idle machine: `DK2_SPEED_BUDGET=1 dotnet test tests/DK2.Behavior.Tests -c Release --filter FullyQualifiedName~SpeedBudgetTests`. Nothing runs them automatically; run them after a change that could cost time |
| 5 | **Classic presentation acceptance remains open** | **Open acceptance** (2026-11-14). Everything the classic look needs is merged: the scene, camera and lighting, the HUD and front end (2026-10-30), world sounds and the presented world. What is open is the owner's play, visual and hearing acceptance and the matched-original comparisons (ADR-0522); [the completion plan](plans/2026-10-29-hud-and-front-end-completion.md#acceptance-record) and the [HUD checklist](research/hud-comparison-checklist.md) hold the record. The classic scene's comparison gates remain the reference for judging goal 3 |
| 6 | **`ArchitectureTests` did not enforce what ADR-0002 claimed**: an assembly Unity loads could silently be net10.0 | **Closed** — the assertion now reads `TargetFrameworkAttribute` off the built assemblies |
| 7 | **The omniscient view.** Fog was simulation state and no renderer could honour it, because the presentation boundary had no notion of who was looking | **Closed** — `WorldView` takes a viewing keeper and substitutes terrain 27 for anything that keeper has not discovered |
| 8 | **The silent skip.** A test that returned early on a missing install was indistinguishable from a test that passed, so a green suite overstated what was checked by hundreds of tests | **Closed** — ADR-0100: `[InstallFact]`/`[InstallTheory]` set `Skip`, and `InstallGateTests` fails the build if a `return`-on-null reappears |
| 9 | **Nothing runs the suite but a person.** `ci.yml` declares Linux and Windows, and the repository had no remote, so the cross-machine determinism goal 9 claims and M14's lockstep depends on had been observed on one machine | **Closed** (2026-10-29). Since 2026-10-24 `ci.yml` builds and tests on ubuntu-latest and windows-latest for every push to master and every pull request, with a docs and KB gate beside them; the pin passes on both. Left for M14: CI has no game install, so the install-gated tests skip there (1,140 on 2026-11-14) and campaign-level determinism is observed on one machine |
| 10 | **Our game has no debug tooling of its own.** DK2 ships dumpers, a world log, a route log, per-subsystem checksums and an out-of-sync handler; we use them as oracles, but a crash or wrong behaviour in our Unity build cannot be replayed | **Open, partly built.** Built: the harness's input journal, `replay` and `watch` (ADR-0552), the offline journal report and snapshot comparison (`src/DK2.Tools/README.md`); Unity writes the same journal when asked (ADR-0553 Phase 1), and the harness cannot replay it yet. Not built: the rolling snapshot, per-subsystem hashes, the world dump, debug invariants and the Unity timeline. Plan and item status: `docs/plans/2026-10-18-crash-and-debug-tooling.md`. Deadline: per-subsystem hashes before M14 |
| 11 | **A replay result is true at the hash it ran at, and nothing reruns it.** The packet replays, resync replays and draw-log comparisons behind rows 2 and 5 are each run by hand, while the hash moves several times a day | **Open** (2026-11-14). The last 20-seed replay of Levels 1–4 ran at hash 309; master is at 516. The campaign bot's journals are the exception: they replay every hash without the controller. Deadline: before M6's "level is winnable" or any M10 claim rests on a replay, rerun it at the hash being claimed |

## Watch list — not problems yet

- **`GameDatabase` is DK2-shaped.** Its records mirror KWD layout field for
  field, including string-table ids. Correct for fidelity; it does mean goal 8's
  mod author would express content in DK2's record shape. Decide before any
  external content format is designed.
- **The bot-facing schema now exists and is still moving.** `PlayerObservation`
  is versioned, and a format change requires a schema bump (`docs/HANDOFF.md`
  §"Run and verify"). Its only consumer is in-process. Before an agent outside
  the process depends on it (stage 4 of the playtesting design), check that the
  schema, not the C# types, is the contract.
- **Acceptance waits on one person.** Rows 1, 2 and 11 each end in owner checks
  against the original. Built-and-unaccepted work is accumulating faster than it
  is accepted; the campaign bot and its receipts are the first acceptance that
  needs no sitting.

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
