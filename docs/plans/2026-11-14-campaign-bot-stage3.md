# Campaign player stage 3: a capable campaign bot implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Follow AGENTS.md's reviewer/documenter passes before commits.

**Status:** execution plan, 2026-11-14, awaiting owner review. Nothing here is built.
**Goal:** grow the Level 1 campaign player into a bot that needs no AI model and wins every
campaign level the simulation can complete, level by level as each becomes playable, from
player-visible observation and normal commands only.
**Architecture:** the bot stays one controller (`CampaignPlayer`, `DK2.AI`) driven by two kinds
of data: a `PlayerStrategy` its routines read, and a per-level **playbook** that holds what a
veteran player knows about the level and switches the strategy by phase. A **self-play tuner**
in `DK2.Harness` searches a playbook's numbers over seeds on the headless simulation and
proposes better values. The bot's knowledge is the playbooks and the shared routines, all
reviewable files; no model is trained or consulted.
**Tech Stack:** existing netstandard2.1 assemblies (`DK2.Simulation.Players`, `DK2.AI.Players`,
`DK2.Presentation`), `DK2.Harness` (.NET 10) with its YAML scenario loader, xUnit 2/VSTest.
**Spec:** [design](2026-11-07-agentic-campaign-playtesting-design.md), stage 3 row and Owner
direction; stages 1-2 as built in [their plan](2026-11-07-campaign-player-stages1-2.md).

## Owner decisions (2026-11-14)

- The bot needs no AI model, during play or to replay. AI agents come later, as supervisor
  (stage 4) and skirmish playtester (stage 8).
- It should play the whole campaign competently, each level as the simulation makes it playable.
- **Veteran knowledge:** a playbook may hold everything a player who has beaten the level knows:
  layout, gold, portals, hero gates and what the level's script does and when. It is written from
  the level files and the research. During play the bot still sees only the player observation.
- "Training" means building this knowledge (playbooks, shared routines) and tuning it by
  self-play. Strategy notes from human footage come later, under the footage rules in
  `tools/footage/README.md` (sources by number only; never an author's name).
- Level 2 is the first new level.

## Global constraints

- **Observation boundary.** During play the bot reads only `PlayerObservation`. A playbook is
  prior knowledge loaded before tick 0; it never reads simulation state at run time.
- **Normal commands only**, through `PlayerActionAdapter`; no scenario grants, no scripted help.
- **Determinism.** The same build, scenario, seed and playbook give the same journal. The run
  manifest records the playbook's SHA-256. Every journal replays without the controller.
- **No DK2 behaviour changes to help the bot win.** A mechanic that blocks a win is filed as its
  own question/task with evidence; the level is reported as blocked, not forced.
- **Every win is disclosed with its seeds; every loss with its reason.** A controller limitation
  is a valid result, not a failure to hide.
- Evidence of acceptance runs goes add-only under `F:/DK2Evidence/studies/` with a receipt
  (`tools/build_receipt.py`), as for Level 1.
- Reviewer, then documenter, before each commit; docs and KB gates; worktrees only.

## .NET skills and optimization gate

The [stages 1-2 gate](2026-11-07-campaign-player-stages1-2.md#net-skills-and-optimization-gate)
applies unchanged: `csharp-refactoring` for Task 1, `run-tests` for every test command,
`analyzing-dotnet-performance` on the routines and the tuner, and a measurement before any
optimization claim. The tuner's throughput (runs per minute per core) is measured and recorded.

## Concepts

| Term | What it is | Where |
|---|---|---|
| Strategy | The values the routines read: rooms and order, room size, dig mode, fight mode, specials, cadence | `DK2.AI.Players.PlayerStrategy` |
| Playbook | One level's veteran knowledge: map notes, ordered phases (when → strategy changes and goals), known events | `scenarios/campaign-player/playbooks/levelN.yaml`, loaded to `DK2.AI.Players.Playbook` |
| Goal | A target a phase sets: dig toward a tile, claim a portal, hold home, attack a gate | part of a phase |
| Ladder | Per level: Won (seeds), Controller limitation, Blocked by a mechanic (question id), Not yet tried | `scenarios/campaign-player/README.md` |

## Entry points and file ownership

| Task | Files |
|---|---|
| 1 | `src/DK2.AI/Players/PlayerStrategy.cs` (new), `CampaignPlayer.cs`, `CampaignPlayerOptions.cs`, `PlayerActionRoutines.cs`; `tests/DK2.AI.Tests/Players/PlayerStrategyTests.cs` (new) |
| 2 | `src/DK2.AI/Players/Playbook.cs` (new), `src/DK2.Harness/` playbook loader and manifest; `tests/DK2.AI.Tests/Players/PlaybookTests.cs`, harness loader tests |
| 3 | `CampaignPlayer.cs` phase runner and goals; `tests/DK2.AI.Tests/Players/CampaignPlayerPhaseTests.cs` |
| 4 | Level 2 diagnosis; new routines or action kinds in `PlayerActionRoutines.cs`, `PlayerAction.cs`, `PlayerActionAdapter` |
| 5 | `scenarios/campaign-player/level2*.yaml`, `playbooks/level2.yaml`; `tests/DK2.Behavior.Tests/CampaignPlayerLevel2Tests.cs` |
| 6 | `src/DK2.Harness/` `tune` command; tests beside the existing player-run tests in `tests/DK2.Behavior.Tests/` |
| 7 | `scenarios/campaign-player/README.md` ladder, `playbooks/level1.yaml`, HANDOFF, harness README |

## Task 1: a strategy the routines read

Moved here from the stage 4 plan (its Task 1), unchanged in substance: the playbook needs it first.

**Produces:** `PlayerStrategy` (immutable record): `Rooms` (build order), `RoomSide`, `Dig`
(`Off`, `GoldFirst`, `Frontier`, `Toward(TilePos)`), `MaxOutstandingTags`, `Fight` (`Engage`,
`HoldHome`, `Off`), `UseSpecials`, `AdvanceTicks`. Defaults equal today's `CampaignPlayerOptions`.
`Validate()` returns every problem rather than throwing on the first. `CampaignPlayer` gains
`Strategy` and `Apply(PlayerStrategy next)`; applying drops pending squares and dig marks that no
longer fit, through ordinary untag actions.

- [ ] Write `PlayerStrategyTests`: defaults equal today's options; validation names each problem;
      `Toward` outside the map is refused; an empty room list is allowed. Run; expect failures.
- [ ] Write a test that the default strategy makes identical decisions to today's on the
      existing observation fixtures (`CampaignPlayerTests` stay green unchanged).
- [ ] Refactor the routines to read the strategy (`csharp-refactoring`); behaviour-preserving.
- [ ] Add `Apply` tests: `HoldHome` lifts no fighter toward an enemy; `Dig = Off` tags nothing;
      a removed room's pending square is given up; `Toward` orders the frontier by distance.
- [ ] Run `CampaignPlayerLevel1Tests` with `DK2_REQUIRE_INSTALL=1`: seed 1 still wins at the tick
      and final hash the test currently records.
- [ ] Reviewer, documenter, commit.

## Task 2: the playbook format

**Produces:** a YAML playbook per level and its immutable form:

```yaml
level: campaign:level2
available:                # generated from the level file, never hand-written (see below)
  rooms: { Lair: Available, Hatchery: Available, TrainingRoom: Available, Treasury: Unavailable }
  spells: { ... }
  creatures: { Goblin: ... }
  traps: {}
  doors: {}
  unlocks: [ { room: ..., by: "trigger N" } ]  # MAKE actions in the level's script
  rewards-next: [ ... ]                      # what winning unlocks, from NextRewards
notes:                    # veteran knowledge; each item cites a doc, a trigger id or a map dump
  - { kind: gold, at: [x, y], source: "..." }
  - { kind: portal, at: [x, y], source: "..." }
  - { kind: hero-gate, at: [x, y], source: "..." }
events:                   # what the script does, from the level's triggers
  - { id: first-wave, when: "trigger 65531 / about tick N", expect: "...", source: "..." }
phases:                   # first whose 'until' is not met is active
  - name: open
    strategy: { rooms: [Treasury, Lair, Hatchery], dig: GoldFirst }
    goals: [ { dig-toward: [x, y] } ]
    until: { rooms: 3, creatures: 4 }
  - name: defend
    strategy: { fight: HoldHome }
    until: { event-seen: first-wave }
tunable: [ room-side, max-outstanding-tags, phases.open.until.creatures ]
```

**What the level allows is generated, not written.** `run playbook-available <level>` reads the
level with the simulation's own start-up code and writes what the panel offers the player at
the first playable tick (rooms, creatures, spells, traps, doors), plus every `MAKE` action in
the level's script and its trigger as `unlocks`. It does not re-derive the rules from the raw
`Availability` entries (`level2Variables.kld` lists only Treasury and Training Room, yet Lair
and Hatchery are offered), so it cannot drift from what the game does. The loader refuses a
playbook whose `available` block differs from the level file, and refuses any strategy, phase
or goal that names a room, spell, creature, trap or door the level does not offer or will not
unlock. The bot still checks the panel's offers in play; the playbook only stops it planning
around something the level never gives (Level 2 offers no Treasury, for one).

`until` conditions read only the observation (tick, owned room tiles by type, creature count,
gold, a notice kind seen, a tile seen or owned). `tunable` names the values the tuner may change
and their ranges.

- [ ] Write `PlaybookTests`: a valid file loads; every problem is reported (unknown room, a
      room or spell the level does not offer, an `available` block that differs from the level
      file, tile off the map, an `until` on hidden state, a tunable that names nothing, a note without a
      source); phases keep their order.
- [ ] Implement `Playbook` (DK2.AI, no YAML dependency), the loader and `playbook-available`
      (DK2.Harness). Test `playbook-available` against Level 1 and Level 2's shipped files
      (install-gated): Level 2 offers Lair, Hatchery and Training Room and not the Treasury.
- [ ] `run --campaign-player` takes `--playbook <file>`; the manifest records its SHA-256 and the
      report names it. Replay refuses a journal whose playbook hash differs.
- [ ] Write `playbooks/level1.yaml` expressing today's Level 1 behaviour; Level 1 seed 1 still
      wins with it, at the recorded tick and hash.
- [ ] Reviewer, documenter, commit.

## Task 3: phases and goals in the controller

- [ ] Tests first (`CampaignPlayerPhaseTests`, synthetic observations): the first phase whose
      `until` is unmet is active; reaching its condition applies the next phase's strategy on the
      next decision; a goal `dig-toward` orders dig marks toward the tile; `hold-home` keeps
      fighters home; `claim-portal` digs to and claims the portal's tiles; the phase change and
      reason appear in the decision's reason text (journalled).
- [ ] Implement in `CampaignPlayer` through `Apply`; no new observation fields unless a test
      shows one is needed, and then only player-visible ones (reviewed for isolation).
- [ ] Level 1 still wins with its playbook.
- [ ] Reviewer, documenter, commit.

## Task 4: what Level 2 needs

**Diagnosis run (2026-11-14, master at hash 513, no receipt, not an acceptance claim).** The
unchanged Level 1 controller, `watch scenarios/campaign-player/level2 --campaign-player 3`, won
all three seeds: seed 1 at tick 3963, seed 2 at 3708, seed 3 at 5421 (the owner's native win:
3263). Each journal replayed every hash without the controller. It built only a Lair and a
Hatchery: Level 2's own availability table (`level2Variables.kld`, `kwd vars -a`) sets the Treasury
UNAVAILABLE (1) and the Training Room AVAILABLE (3), so the default list's Treasury was never
offered and the Training Room, which is not in that list, was never built, which is why the playbook gets a generated `available` block (Task 2); in each seed the hero party came into the dungeon and
its Lord of the Land died there. **Caveat: that walk is Q525, open.** In DK2's capture the
party stays near its gate until about 3170; in ours it reaches the keeper's rooms from about
2307 in half the seeds. The wins may lean on that difference, so they are not accepted until
Q525 is settled. Level 2 needs no new routine to be won today; Task 5 reduces
to a receipted acceptance, and the first level the default strategy cannot win becomes the real
driver for Tasks 1-4. Scenarios: `scenarios/campaign-player/level2/`.

**Ladder diagnosis, Levels 3-20 (2026-11-14, same controller, seeds 1-3, no receipt).** The
player's default cap of 14,400 ticks applied, so "no result" means none in that hour.

| Result | Levels |
|---|---|
| Won, all seeds | 3 (ticks 3539, 4671, 5588; the Lord of the Land dies in the keeper's area; journals replay) |
| Defeated, all seeds | 5, 10, 13, 14, 15a, 20 |
| Defeated or no result | 4, 6a, 6b, 7 |
| No result | 8, 9, 11A, 11B, 11C, 12, 15b, 16, 18, 19 |
| Controller gave up (stall) | 17, seeds 2-3 |

Runs took 45-320 s each. Level 4 is the first level the default strategy cannot win, so it,
not Level 2, drives Tasks 1-4. Scenarios: `scenarios/campaign-player/level3/`.

- [ ] Write `scenarios/campaign-player/level2.yaml` like `level1.yaml` (unmodified start, fog,
      viewing player 3, placed things). Run unattended with the Level 1 strategy, seeds 1-3.
      Disclose the outcome and the stall or loss reason.
- [ ] Read Level 2's objective, events and layout from the level files, the existing research
      (for example `bottom-panel-controls.md`: Create Imp and Thunderbolt are offered) and the run.
      List each gap as one of: a missing routine, a missing action kind (a spell cast, for one),
      or a missing mechanic in the simulation.
- [ ] A missing mechanic: file it as an open question with its evidence; it is not built here.
- [ ] A missing routine or action kind: tests first, then the routine; a new action kind goes
      through `PlayerActionAdapter` with an admission test and an isolation review, like the
      stage 1 actions. Prefer routines that serve any level over Level 2-only code.
- [ ] Reviewer, documenter, commit per routine.

## Task 5: Level 2 playbook and acceptance

- [ ] Write `playbooks/level2.yaml` from Task 4's reading; every note and event cites its source.
- [ ] Iterate: run seeds 1-3, read the reports, change the playbook or a routine. Keep a short
      log of what each change fixed in the plan's as-built section.
- [ ] Acceptance as for Level 1: seeds 1-3 won unattended from a receipted build, every journal
      replayed without the controller, evidence add-only under `F:/DK2Evidence/studies/`.
      `CampaignPlayerLevel2Tests` (install-gated) pins seed 1's win tick and hash.
- [ ] If Level 2 cannot be won, record why (limitation or blocked by a question id) and stop at
      a reproducible report; do not change DK2 behaviour.
- [ ] Reviewer, documenter, commit.

## Task 6: the self-play tuner

**Produces:** `run tune --playbook <file> --seeds 1-N --budget-minutes M --jobs J --out <dir>`.
It varies the playbook's `tunable` values (seeded, deterministic candidate order: a coarse grid,
then local steps around the best), runs every candidate on every seed through the existing run
path, and scores each: wins first, then fewer ticks to win, fewer creatures lost, more gold left.
It writes `tune-report.md` (every candidate and score) and `proposal.yaml`; it never overwrites
the playbook.

- [ ] Tests first: candidate generation is deterministic and stays in range; scoring orders the
      cases above; the budget stops the search and the report says so; a candidate whose run
      fails technically is reported, not scored as a loss.
- [ ] Implement on the existing runner and `--jobs`; no second simulation loop.
- [ ] Measure runs per minute per core on Level 1 and record it; note the cost of a 3-seed,
      50-candidate search.
- [ ] Tune Level 1 and Level 2 once; accept a proposal only if it wins on seeds it was not
      tuned on (hold out at least two), and record before/after in the ladder.
- [ ] Reviewer, documenter, commit.

## Task 7: the ladder and the per-level loop

- [ ] Add the ladder to `scenarios/campaign-player/README.md`: one row per campaign level with
      status (Won: seeds and date; Controller limitation; Blocked: question id; Not yet tried),
      its playbook, and the level-specific routines it needs, if any.
- [ ] Write the per-level procedure there: scenario file → default run → reading (Task 4) →
      playbook → iterate → tune with hold-out seeds → acceptance → ladder row.
- [ ] Add a regression test that runs seed 1 of every Won level (install-gated, long category),
      so a later simulation change that breaks a win is seen.
- [ ] Update HANDOFF and the harness README entry points.
- [ ] Reviewer, documenter, commit.

After Task 7, each further level is a repetition of Tasks 4-5 (and 6 when worth it), one level
per branch, in campaign order, skipping levels the simulation cannot yet complete and returning
to them when it can.

## Later, not in this plan

- **Strategy notes from footage.** Under `tools/footage/README.md`: notes cite a source number
  and timestamp, never an author. A note becomes a playbook or strategy option only after the
  tuner shows it wins more on hold-out seeds.
- Agent supervision (stage 4), skirmish (stage 8), rendered play and the real UI (stages 5-6).
- Reinforcement learning or any trained model.

## Out of scope

Changing DK2 behaviour to help the bot, hidden-state reads at run time, per-frame model control,
an Enhanced (computer-opponent) keeper, and native-game play.
