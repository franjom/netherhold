# Campaign player stage 3: a capable campaign bot implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Follow AGENTS.md's reviewer/documenter passes before commits.

**Status:** execution plan, 2026-11-14, revised the same day after two adversarial reviews;
approved by the owner on 2026-11-14. Task 0a is in progress.
**Goal:** grow the Level 1 campaign player into a bot that needs no AI model and wins every
campaign level the simulation can complete, level by level as each becomes playable, from
player-visible observation and normal commands only.
**Architecture:** one controller (`CampaignPlayer`, `DK2.AI`) driven by a `PlayerStrategy` its
routines read and a per-level **playbook** (veteran knowledge and phases). A **tuner** in
`DK2.Harness` searches a playbook's integer values over declared seed sets. Knowledge lives in
reviewable files; no model is trained or consulted.
**Tech Stack:** netstandard2.1 `DK2.Simulation.Players`, `DK2.AI.Players`, `DK2.Presentation`;
`DK2.Harness` (.NET 10); xUnit 2/VSTest.
**Spec:** [design](2026-11-07-agentic-campaign-playtesting-design.md), stage 3 row and Owner
direction. **Evidence:** [campaign bot ladder](../research/campaign-bot-ladder.md).

## Owner decisions (2026-11-14)

- The bot needs no AI model, during play or to replay. Agents come later: supervisor (stage 4),
  skirmish playtester (stage 8).
- It should play the whole campaign competently, each level as the simulation makes it playable.
- **Veteran knowledge:** a playbook may hold what a player who has beaten the level knows: layout,
  gold, portals, hero gates, and what the level's script does and when. During play the bot
  still sees only the player observation.
- "Training" means building that knowledge and tuning it by self-play. Footage notes come later
  under `tools/footage/README.md` (sources by number only, never an author's name).

## What the evidence says

From the [ladder](../research/campaign-bot-ladder.md): the unchanged controller wins Levels 1-3
(Levels 2-3 rerun after ADR-0800) and nothing after. On every losing level it builds at most one
3x3 of each room in its fixed list and then waits for 70-98% of its decisions. So:

- The first gap is **development**: keep building, growing and using what the level offers.
  It shows on every level, so it is measured across the ladder, not on one level.
- The **driver level** is chosen in Task 0b by triage, not assumed. Levels whose losses touch an
  open outcome question are excluded while it stays open: Level 4 (Q710: ours overruns in 37 of 60
  seeds even under the owner's replayed input) and Level 5 (Q712, Q772: Heart defence).
  On present evidence Level 6b (2 own deaths, no result, one room built) and Level 9 (no deaths,
  no result) look like losses of the controller's own; triage confirms or replaces them.

## Global constraints

- **Observation boundary.** During play the bot reads only `PlayerObservation`. A playbook is
  prior knowledge loaded before tick 0 and never reads simulation state at run time. Every playbook
  and every new observation field gets an isolation review (reviewer pass): nothing in it may come
  from state a player cannot see or know from the level files.
- **Normal commands only**, through `PlayerActionAdapter`.
- **Determinism.** The same build, scenario, seed and playbook give the same journal. Replay reads
  the journal only; it never needs the controller or the playbook. A run stopped by wall time or
  output budget is **invalid**, never scored as a loss.
- **No DK2 behaviour changes to help the bot win.** A blocking mechanic is filed as a question.
- **Honest results.** Win rates are reported with their seed counts and a 95% Wilson interval;
  three seeds is a diagnosis, not a rate. Every loss is reported with its reason.
- Evidence of acceptance runs goes add-only under `F:/DK2Evidence/studies/` with a receipt
  (`tools/build_receipt.py`). Reviewer, then documenter, before each commit; worktrees only.

## .NET skills and optimization gate

As in the [stages 1-2 plan](2026-11-07-campaign-player-stages1-2.md#net-skills-and-optimization-gate):
`csharp-refactoring` for Task 1, `run-tests` for test commands, `analyzing-dotnet-performance` on
routines and the tuner, and a measurement before any optimization claim. The tuner has a
throughput gate (Task 7).

## Seeds

Declared once, used everywhere: **tune** seeds 1-5, **select** seeds 6-10, **accept** seeds
11-20. Diagnosis runs may use any seeds and claim nothing. A level is **Won** on the ladder when a
receipted build wins at least 9 of the 10 accept seeds and every journal replays. That means
"usually wins", not a measured rate: 9/10 has a 95% Wilson lower bound near 60%, passes a true
90% bot about 74% of the time and a true 70% bot about 15%. The select-seed check is a filter
(a two-win margin out of 5 still passes by chance about 17% of the time); the accept seeds are
the gate.

## Task 0a: baselines and acceptance of Levels 1-3

- [x] **Baselines.** On current master, run Levels 1, 2 and 3 on seeds 1-3 and record per run:
      result tick, final hash, and SHA-256 of `hashes.tsv` (the state-hash series),
      `player-decisions.tsv` and `player-actions.tsv`, in `scenarios/campaign-player/baselines.tsv`
      with the commit and HashVersion. The journal itself carries a start time, so it is not
      hashed; a rerun reproduced all three files exactly. This is a
      record, not a standing test: simulation changes re-roll seeds, and the existing Level 1
      tests assert relations on purpose. Task 1's byte-for-byte check is a one-off comparison on
      one commit, before and after the refactor. **Done 2026-11-14** (commit 20a6a48c, hash 516; recorded twice, identical).
- [x] **A `--seed` override** on `run`, `watch` and `replay` (and later `tune`): the manifest
      records the effective seed, and `replay` refuses a journal whose header seed differs from
      the one it would run. Tests: an overridden seed replays; a mismatch is refused.
- [x] **Acceptance of Levels 1, 2 and 3** on accept seeds 11-20 from a receipted build, evidence
      under `F:/DK2Evidence/studies/`, ladder rows with counts and intervals. **Done 2026-11-14:**
      30 of 30 won and claimed ([ladder](../research/campaign-bot-ladder.md) section 2).
- [ ] Reviewer, documenter, commit.

## Task 0b: ladder scenarios, triage and the driver

- [ ] **Ladder scenarios.** Commit Levels 4-20 as `scenarios/campaign-ladder/<level>.yaml`
      (outside `scenarios/campaign-player`, which `watch` reads recursively), with
      `duration_ticks` at the per-level cap.
- [ ] **Triage of every losing level up to 10** (4, 5, 6a, 6b, 7, 8, 9, 10). For each, in the
      ladder doc: the win condition from its triggers; which player the PLAYERS records give the
      human (all runs used 3); whether the loss was the tick cap (rerun "no result" levels with
      `--player-max-ticks 57600 --player-wall-seconds 1800 --timeout-seconds 2400`); open
      questions touching its outcome; the gap that lost it; and the observation or action
      additions (Task 4) it would need. Check whether `PreviousRewards` change any level's start
      in DK2 (`campaign-win-sequence.md` section 5; ours reads them only in the presentation) and
      file a question if they might.
- [ ] **Choose the driver**: the earliest losing level whose loss is the controller's and touches
      no open outcome question. If none qualifies, take the losing level with the fewest open
      outcome questions, list them on its row, and say its acceptance waits on them.
- [ ] Reviewer, documenter, commit.

## Task 1: a strategy the routines read

**Produces:** `PlayerStrategy` (immutable record): `Rooms` (build order), `RoomSide`, `Dig`
(`Off`, `GoldFirst`, `Frontier`, `Toward(TilePos)`), `MaxOutstandingTags`, `Fight` (`Engage`,
`HoldHome`, `Off`), `UseSpecials`, `AdvanceTicks`; defaults equal today's
`CampaignPlayerOptions`. `Validate()` returns every problem. `CampaignPlayer` gains `Strategy` and
`Apply(PlayerStrategy next)`; giving up marks and squares that no longer fit goes out as ordinary
untags, spread over decisions within `MaxActionsPerDecision`.

- [ ] Tests first (`PlayerStrategyTests`): defaults equal today's options; validation names each
      problem; `Toward` off the map is refused; an empty room list is allowed.
- [ ] Refactor the routines to read the strategy (`csharp-refactoring`), behaviour-preserving.
- [ ] `Apply` tests: `HoldHome` lifts no fighter toward an enemy; `Dig = Off` tags nothing; a
      removed room's pending square is given up; untags spread across decisions.
- [ ] One-off check on one commit: every Task 0a baseline reproduces byte for byte (journal and
      decisions) before and after the refactor.
- [ ] Reviewer, documenter, commit.

## Task 2: development, a thin vertical slice

The gap every losing level shares. Built before the playbook format so the format is shaped by
what a working slice and the triage needed, not guessed.

- [ ] Tests first (synthetic observations): with no room missing, the bot extends an existing
      room or builds another of a type the panel offers when its creature count outgrows it
      (lair and hatchery per creature, treasury when gold has nowhere to go); it builds offered
      rooms not in its list in a default order (Training Room before Workshop, for one); it uses
      a Training Room it owns (drops creatures there) and a Workshop if the level gives doors or
      traps; it does not wait while an offered, affordable build remains.
- [ ] **Bounded digging.** Frontier digging stays within the outstanding-tag bound and a distance
      from owned territory, and never marks a tile that would open onto ground another player
      owns or where an enemy was seen; a test shows a closed corridor stays closed. Digging
      breaches release heroes (Level 2's party, `level2-party-release.md`).
- [ ] Implement in `PlayerActionRoutines`; the strategy gains the ordering and sizing values.
- [ ] Measure per ladder level, before and after: rooms and room tiles owned, creatures housed
      and trained, win or loss. The share of wait decisions is reported but is not a goal.
- [ ] Levels 1-3 rerun on accept seeds 11-20 (behaviour changed on purpose): the ladder rows
      are updated, and a level that drops below 9 of 10 is reported, not hidden. New baselines go
      into `baselines.tsv` with the commit.
- [ ] Reviewer, documenter, commit.

## Task 3: the playbook format

**Produces:** `scenarios/campaign-player/playbooks/<level>.yaml`, loaded to an immutable
`Playbook` (DK2.AI; the YAML loader in DK2.Harness):

```yaml
level: campaign:level6b
available:          # generated by `playbook-available`, never hand-written
  start: { rooms: [...], spells: [...], doors: [...], traps: [...] }   # what the panel offers
  creatures: [...]  # from the level's creature availability: veteran knowledge, not seen in play
  may-change: [ { item: ..., to: available, by: "trigger N MAKE" }, { item: ..., to: available, by: research },
                { item: ..., to: unavailable, by: "trigger N MAKE" } ]
notes:              # veteran knowledge; each cites a doc, a trigger id or a map dump
  - { kind: gold, at: [x, y], source: "...", provenance: researched }
events:             # what the script does, from the level's triggers
  - { id: first-wave, when: "trigger N, about tick T", expect: "...", source: "..." }
phases: [...]       # Task 5
tunable:            # integer values only, each with a range
  - { path: strategy.room-side, min: 3, max: 6, provenance: tuned, proven-on: "<commit>" }
caps: { max-ticks: 57600, max-actions: 8192, wall-seconds: 2400 }   # per level
```

- **`playbook-available <level>`** (DK2.Harness, an authoring tool, not run-time): builds the
  level's start with the simulation's own start-up code, reads what the panel offers at the first
  playable tick (`BottomPanelView`, as `PlayerObservationBuilder.CaptureOffers` does, including
  Door and Trap choices), and lists every `MAKE` action in the triggers and every research path
  (`ResearchTreeRules`) as `may-change`, including MAKE actions that take something away. The
  loader **warns** when a plan uses an item that only `may-change` to available and **refuses**
  only items that are neither available at start nor may become available.
- **Caps** override the CLI defaults unless a flag is given explicitly, which wins. The scenario's
  `duration_ticks` must be at least `max-ticks`, and `wall-seconds` must stay below `watch`'s
  `--timeout-seconds`; the loader refuses otherwise.
- Values carry **provenance**: `researched` (with a source) or `tuned` (with the commit it was
  proven on). A tuned value is flagged stale when the HashVersion recorded with its `proven-on` commit
  differs from the current one.
- The run manifest records the playbook's SHA-256 as information. `PlayerRunManifest.Schema`
  goes to 2; `Verify` accepts 1 and 2 so stored evidence stays readable.

- [ ] Tests first: `PlaybookTests` (DK2.AI, unit): order, provenance, integer ranges, every
      problem reported. Install-gated tests: `playbook-available` on Level 2 lists Lair, Hatchery
      and Training Room and not the Treasury; a playbook naming the Treasury on Level 2 is refused.
- [ ] Implement `Playbook`, the loader, `playbook-available`, `--playbook` (added to
      `HarnessCommandLine.PlayerFlags` and passed through `watch`), and per-level caps.
- [ ] `playbooks/level1.yaml` reproduces Level 1's Task 2 baseline.
- [ ] Isolation review of the format and of `level1.yaml`. Reviewer, documenter, commit.

## Task 4: observation and action additions

Only what the panels show a player. Each addition: tests first, an admission test in
`PlayerActionAdapter`, an isolation review, and an observation schema bump with the journal and
replay tests updated.

| Addition | Why | Source of truth |
|---|---|---|
| Door and Trap offers in `PlayerOffers` | the panel shows them; `CaptureOffers` keeps only Room and Spell today | `BottomPanelChoiceKind.Door/Trap` |
| Every offered spell, not only Create Imp | spells the level gives | the Spell panel |
| `PlaceDoor`, `PlaceTrap` actions | defend a route | the panel's build intents |
| `CastSpell(spell, target)` | offered spells | the existing spell intent path |

Possession (`PossessionCommands`) is not added here; a level that needs it is recorded as a
controller limitation until a later task.

Only the rows the triage and the driver need are built; the others wait. Doors and traps are
Workshop stock: if the panel shows a stock count, the offer carries it; manufacturing is a
Task 2 routine.

- [ ] One branch per row; reviewer, documenter, commit each.

## Task 5: phases and goals

- **Phases are latched.** The playbook's phases form a sequence; the bot moves from phase *i* to
  *i+1* when *i*'s `until` is first met and never moves back. Each change is journalled in the
  decision reason.
- **Conditions** (observation only): tick, owned room tiles by type, creature count, gold,
  `enemy-visible`, a notice kind seen (only where the level's playbook shows it sends one), a tile
  seen or owned.
- **Goals:** `dig-toward(tile)`, `hold-at(tile)` (drop fighters there and keep them there),
  `avoid(region)` (no dig marks there), `claim(tile)` (dig to it; success is the tile owned, since
  imps claim by walking over it), `hold-home`.
- **Precedence:** the active phase sets the strategy; a later stage 4 supervisor change holds
  until the next phase change, which wins. The active phase's name and index are part of the
  stage 4 checkpoint summary, so a supervisor sees what will override it.

- [ ] Tests first (`CampaignPlayerPhaseTests`): latching (a condition that later fails does not
      return the bot to an earlier phase); each goal; each condition; the precedence rule.
- [ ] Implement through `Apply`. Isolation review. Reviewer, documenter, commit.

## Task 6: the driver level

The level chosen in Task 0b (record it here when chosen).

- [ ] Diagnose it with Tasks 2-5 in place; write its playbook (every note and event cited).
- [ ] Iterate on the tune seeds only; a change that helps is checked on the select seeds.
- [ ] Accept on seeds 11-20 from a receipted build: at least 9 of 10, journals replayed, evidence
      under `F:/DK2Evidence/studies/`. Otherwise record the reason and stop at a reproducible
      report.
- [ ] Rerun the ladder; record the change per level. Reviewer, documenter, commit.

## Task 7: the tuner

**Produces:** `DK2.Harness tune <scenario> --playbook <file> --candidates N --jobs J --out <dir>`,
using the declared tune and select seeds.

- **Deterministic.** Batch-synchronous: a seeded coarse grid over the integer tunables, every
  candidate run on every tune seed, the whole batch finished before the next batch of local steps
  is generated. The budget is a candidate count, not minutes. The candidate list and order are
  written before the first run.
- **Runs in process** only after a test shows that the same seed gives identical hashes run
  sequentially and run concurrently in one process (no mutable statics in the simulation or the
  controller); otherwise one process per run. One loaded database and one manifest source hash
  are shared; seeds come from the Task 0a `--seed` override. A light evidence mode keeps only the
  per-run summary; the full folder is written for the incumbent and the proposal.
- **Early stop:** a candidate's run ends at 1.5 times the slowest win the incumbent has on this
  level (any seed), or at the level cap if it has none, and counts as not won.
- **Invalid runs** (wall-time stop, output budget, technical failure) are rerun once with twice
  the wall limit, then reported. If more than a tenth of a batch is invalid, the batch is void and
  the report says so, so slow candidates are not quietly dropped.
- **Score:** wins on the tune seeds; ties broken by fewer own creatures lost. Ticks are reported,
  not scored.
- **Acceptance of a proposal:** at least two more wins than the incumbent on the select seeds,
  then the Task 6 acceptance on seeds 11-20. Tuned values get `provenance: tuned` and the commit.
- **Cost gate:** measure the mean cost of one run on the driver level (tune and select seeds,
  with early stop) first. The owner sets a CPU-hour budget per search (4 unless stated); the
  candidate count is the budget divided by the cost of one candidate on 5 tune seeds plus the
  finalists on 5 select seeds. Below 10 candidates, cut the tunables instead.

- [ ] Tests first: candidate generation is deterministic and in range; the batch order does not
      depend on completion order; invalid runs are excluded; the score ranks as above.
- [ ] Implement; measure throughput; tune only levels the bot sometimes loses. Reviewer,
      documenter, commit.

## Task 8: the ladder and the per-level loop

- [ ] The ladder lives in [campaign-bot-ladder.md](../research/campaign-bot-ladder.md): per
      level, status (Won with count and interval; Controller limitation; Blocked: question id;
      Not yet tried), its playbook, level-specific routines, and the open questions that touch its
      outcome.
- [ ] Per-level procedure there: triage, playbook, iterate on tune seeds, tune if it sometimes
      loses, accept on 11-20, ladder row.
- [ ] A long-category regression (not in the default test run) reruns the accept seeds of every
      Won level and flags a level that wins 7 or fewer of 10 (a true 90% bot does so about 7% of the time).
      A seed-level change is not a failure.
- [ ] HANDOFF and harness README. Reviewer, documenter, commit.

After Task 8 each further level repeats Task 6 (and 7 when it sometimes loses), one level per
branch, in campaign order, returning to excluded levels when their questions close.

## Known limits

- Variant levels (6a/6b, 11A-C, 15a/b) are separate ladder rows. Secret levels are out of scope.
- Multi-keeper levels depend on the Classic keeper AI's own fidelity; their rows list the open
  keeper questions.
- The Horned Reaper finale applies only where a Portal Gem exists (Q656).

## Later, not in this plan

Strategy notes from footage (kept only if the tuner shows a gain on select seeds); possession;
agent supervision (stage 4); skirmish (stage 8); rendered play and the real UI (stages 5-6);
any trained model.

## Out of scope

Changing DK2 behaviour to help the bot, hidden-state reads at run time, per-frame model control,
an Enhanced (computer-opponent) keeper, and native-game play.
