# On-demand autonomous playtesting

**Status:** accepted as ADR-0552 (2026-10-27). Stage A built; B and C not started.
**Mandate:** `AGENTS.md` Goal 10 (AI/Bot Interface). Related milestones: M5, M6, M9, M10, M12–M14.

## 1. Objective

An explicitly launched, resource-bounded way to let local controllers play the headless
simulation: exercise legal actions, find crashes, hangs and invalid states, and leave
reproducible evidence. It extends `DK2.Harness.ScenarioRunner`; it is not a second framework.
It needs no Unity, network or LLM.

## 2. Architecture

1. A controller hook: `ScenarioController` (player id, decision interval, a function from
   `KeeperView` to `PlayerCommand`s), passed to `ScenarioRunner.Run`. Built in Stage A.
2. Harness extensions: command journal, replay, watchdog, incremental logs (Stage B).
3. A legal-action fuzzer with its own deterministic randomness (Stage C).

No new assembly. Controllers are input sources exactly as the computer keeper is
(KEEPER_AI.md §12.1): their commands ride the next tick's `InputFrame`, their own state stays
outside `StateHash()`, and a run is reproduced by its commands alone. **No `HashVersion` bump
is expected from any stage**; a change that moves the pinned hash is a defect.

**Classic Keeper boundary.** `KeeperAI` may run unchanged beside a controller (the harness
refuses a controller on the player of a computer keeper it created; a `computer_keepers` entry
that failed to load leaves its player free). Controllers do not reference the
`KeeperAI` class and do not extend `KeeperView`. `KeeperAI` is an **unverified progression
baseline**: it can drive player 1 only via `keeper_personalities`, and nothing shows it pursues
campaign objectives.

## 3. Observations

`KeeperView` applies no fog of war (every tile, creature, trap and room). Acceptable for
testing; not a boundary for competitive or LLM players, which need a fog-filtered view later.
The test monitor (Stage B/C) reads `WorldState` directly for progression, objectives and
invariants; controller observations and monitor diagnostics stay separate.

## 4. Legal command allowlist (Stage C)

The fuzzer issues only commands a human can issue in the current context. Sources:
`PlayerInputTranslator` and `PlacementCursor` (`DK2.Presentation`), testable from `src/`;
and the Unity-only paths in `DungeonRunner.Hud.cs` and `DungeonRunner.Finale.cs` (doors,
traps, spell casts, `RevealMap`, `ContinueAfterResult`), which `src/` cannot reference. That
half is a hand-kept list; those files get a comment pointing at it.

Excluded: the readiness level-up, `BuildRoomCommand` Direct mode, scenario setup, cheats.
`RevealMap` and `ContinueAfterResult` are excluded by default (play after a verdict explores
little). The allowlist is explicit, versioned and tested. Invalid-input robustness is a
separate future mode.

## 5. Controller randomness

Each fuzzer owns a `SimRandom` seeded from the scenario seed and its player id, following
`ScenarioInputBuilder`'s `new SimRandom(scenario.Seed ^ id)`. It never draws from the
simulation's stream. Replay depends only on recorded commands.

## 6. Execution limits

Explicit launch only. Defaults: one worker, at most 20 matches, 30 minutes wall clock, a
configurable tick budget, deterministic seeds, cancellation. Each match runs in a child
process under a parent watchdog, because a hang inside synchronous `GameSimulation.Tick()`
cannot be cancelled cooperatively. Measure the per-process database load from `DK2_DIR`
before optimising it. Parallel workers wait for a review of mutable static state.
`ScenarioController.IntervalTicks` bounds observation cost: a `KeeperView` captures
navigation bytes, the room index and spell-placement checks.

## 7. Command journal (Stage B)

`keeper-commands.tsv` and Stage A's `controller-commands.tsv` hold type and hash only and
cannot be replayed. Stage B builds the **input journal of
`2026-10-18-crash-and-debug-tooling.md` item 1**, one format for Unity play and harness runs:

- Header: journal version, commit SHA, **whether `src/` was dirty** (`git status --porcelain
  src`; a dirty run is not called reproducible), `HashVersion`, map, **the scenario file's
  hash (or the file itself)**, controllers and their configuration, seeds, limits.
- Body: the **complete merged `InputFrame`** per tick that has commands (scripted, computer
  keeper and controller alike), in frame order, plus periodic `StateHash` checkpoints.
- Unknown command types or versions fail loudly.
- Round-trip test for every command record, comparing both `HashInto` and a reflection
  comparison of every property: records holding `IReadOnlyList` compare by reference, and
  a hash check alone misses a field `HashInto` forgot.

The YAML `commands` vocabulary covers about ten commands, so the journal, not YAML, is the
reproduction artifact.

## 8. Replay (Stage B)

A harness replay verb: build the scenario's world and setup, keep injecting its `work`
entries (they are not commands and never appear in a journal), **skip its YAML `commands`**,
run no controllers or computer keepers, feed the journal's frames, compare hashes, report the
first divergent tick. Incompatible versions are refused. A clean replay proves reproduction
at the recorded revision only.

## 9. Runtime coverage (Stage C)

`GameSimulation.TrackTriggerReach` for trigger coverage, plus cheap per-run histograms of
creature `Dk2StateId` visited and event kinds emitted. `CreatureStateCoverage` and
`CategoryHooks` are implementation-status tables, not runtime coverage; use them afterwards
to list built states no run visited. Instrumentation only observes: no hash or RNG effect.

## 10. Failure monitoring

- Technical: exceptions, crashes, watchdog timeouts, replay mismatches, and save/load
  continuation failures from an optional snapshot → restore → compare cycle (`WorldSnapshot`,
  as `SnapshotTests` does) in Stage C.
- Command: refusals through the existing event sink, following the room builder's `built`
  event that names its reason. No separate result channel.
- Agent: no commands issued, versus commands issued without progress.
- Progression (territory, gold, creatures, rooms, objectives, status): candidates, not bugs.
- Fidelity: a difference from DK2 needs oracle verification and an explicit decision.
- Every invariant cites an evidence source and confidence. The crash plan's item 5 examples
  need sources before they qualify.

## 11. Evidence

Routine output goes to the harness `--out` directory. Only failure bundles or irreplaceable
evidence are copied to `D:\DK2Evidence` (add-only, AGENTS.md). Before tick zero, write and
flush the journal header. During the run, append commands, hashes, coverage, relevant events
and the last completed tick with bounded buffering. A failure bundle (crash plan item 2)
holds configuration, journal, hash trace, the exception or watchdog report, snapshots when
available, the first failing tick, and a minimal reproduction where feasible.

## 12. Statistics

Compare outcomes across seeds with paired comparisons or permutation tests, and record the
sample size and uncertainty; a 20-seed median can double by chance.

## 13. Stages

- **A — Controller hook (built).** `ScenarioController`, `ScenarioRunner.Run(scenario,
  controllers)`, `ScenarioResult.ControllerCommandLog`, `controller-commands.tsv`.
  Acceptance (`ScenarioControllerTests`): a controller's command is the same input as the
  scripted command one tick later (identical hash series), runs reproduce, the interval is kept,
  and a command for another player, an absent player, a player taken by another controller, a
  zero interval and a null decision are refused. Untested here (computer keepers need a
  campaign map): refusal on a keeper's player, and controller commands following the keepers'
  in the frame.
- **B — Reproducibility.** Journal and round-trip tests, replay verb, child-process watchdog,
  incremental logs, failure bundles. Acceptance: a recorded run replays without its
  controller; an interrupted or failing run leaves enough evidence.
- **C — Exploration.** Fuzzer, allowlist, runtime coverage, progression and sourced invariant
  monitoring, the snapshot cycle, bounded multi-seed campaigns. Acceptance: bounded runs
  exercise several systems and every failure reproduces.

**Deferred:** goal-directed AI, Enhanced Keeper, fog-filtered observations, LLM supervisor
code (an LLM can read reports by hand), parallel or distributed or continuous execution.
