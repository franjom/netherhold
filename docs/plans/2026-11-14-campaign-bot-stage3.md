# Campaign player stage 3: a capable campaign bot implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Follow AGENTS.md's reviewer/documenter passes before commits.

**Status:** execution plan, 2026-11-14, revised the same day after two adversarial reviews;
approved by the owner on 2026-11-14. Tasks 0a-3b are built; Task 4 is under way, one row per branch (built: each own creature's level, petals and mood; every offered spell and `CastSpell`).
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

From the [ladder](../research/campaign-bot-ladder.md): the controller before Task 2 won Levels 1-3
(Levels 2-3 rerun after ADR-0800) and nothing after. On every losing level it built at most one
3x3 of each room in its fixed list and then waited for 70-98% of its decisions. So:

- The first gap is **development**: keep building, growing and using what the level offers.
  It shows on every level, so it is measured across the ladder, not on one level.
- The **driver level** is chosen in Task 0b by triage, not assumed. Levels whose losses touch an
  open outcome question are excluded while it stays open: Level 4 (Q710: ours overruns in 37 of 60
  seeds even under the owner's replayed input) and Level 5 (Q712, Q772: Heart defence).
  Triage (2026-11-14, [ladder](../research/campaign-bot-ladder.md) section 5) chose **Level 6a**.

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

- [x] **Ladder scenarios.** Commit Levels 4-20 as `scenarios/campaign-ladder/<level>.yaml`
      (outside `scenarios/campaign-player`, which `watch` reads recursively), with
      `duration_ticks` at the per-level cap.
- [x] **Triage of every losing level up to 10** (4, 5, 6a, 6b, 7, 8, 9, 10). For each, in the
      ladder doc: the win condition from its triggers; which player the PLAYERS records give the
      human (all runs used 3); whether the loss was the tick cap (rerun "no result" levels with
      `--player-max-ticks 57600 --player-wall-seconds 1800 --timeout-seconds 2400`); open
      questions touching its outcome; the gap that lost it; and the observation or action
      additions (Task 4) it would need. Check whether `PreviousRewards` change any level's start
      in DK2 (`campaign-win-sequence.md` section 5; ours reads them only in the presentation) and
      file a question if they might.
- [x] **Choose the driver**: the earliest losing level whose loss is the controller's and touches
      no open outcome question. If none qualifies, take the losing level with the fewest open
      outcome questions, list them on its row, and say its acceptance waits on them.
- [ ] Reviewer, documenter, commit.

## Task 1: a strategy the routines read

**Produces:** `PlayerStrategy` (immutable record, `src/DK2.AI/Players/PlayerStrategy.cs`): `Rooms`
(build order), `RoomSide`, `Dig` (`Off`, `GoldFirst`, `Frontier`, `Toward` with `DigTarget`),
`MaxOutstandingTags`, `Fight` (`Engage`; `HoldHome`, which answers only an enemy within
`HomeGuardTiles` of home; `Off`), `UseSpecials`, `AdvanceTicks` (1-40); defaults equal today's
`CampaignPlayerOptions` (`CampaignPlayerOptions.Strategy`). `Validate()` returns every problem.
`CampaignPlayer` gains `Strategy` and `Apply(PlayerStrategy next)`, called between decisions: a
refused strategy leaves the current one; a pending room no longer listed is given up; turning
digging off takes the standing marks back as untags spread over decisions (other changes leave
marks to be dug). With digging off, the frontier is no pending work, so an idle player is not stalled.

- [x] Tests first (`PlayerStrategyTests`): defaults equal today's options; validation names each
      problem; `Toward` off the map is refused; an empty room list is allowed.
- [x] Refactor the routines to read the strategy (`csharp-refactoring`), behaviour-preserving.
- [x] `Apply` tests: `HoldHome` lifts no fighter toward an enemy; `Dig = Off` tags nothing; a
      removed room's pending square is given up; untags spread across decisions.
- [x] One-off check on one commit: every Task 0a baseline reproduces byte for byte (journal and
      decisions) before and after the refactor. **Done 2026-11-14:** all nine identical at base `3f5c89db`
      (hash 517); `baselines.tsv` was re-recorded there, since master had moved from hash 516.
- [x] Reviewer, documenter, commit.

## Task 2: development, a thin vertical slice

The gap every losing level shares. Built before the playbook format so the format is shaped by
what a working slice and the triage needed, not guessed.

- [x] Tests first (synthetic observations): with no room missing, the bot extends an existing
      room or builds another of a type the panel offers when its creature count outgrows it
      (lair and hatchery per creature, treasury when gold has nowhere to go); it builds offered
      rooms not in its list in a default order (Training Room before Workshop, for one); it uses
      a Training Room it owns (drops creatures there) and a Workshop if the level gives doors or
      traps; it does not wait while an offered, affordable build remains. **Done 2026-11-14:**
      16 tests in `CampaignPlayerTests.Development.cs`; `DK2.AI.Tests` 540 pass (524 before).
- [x] **Bounded digging.** Frontier digging stays within the outstanding-tag bound and a distance
      from owned territory, and never marks a tile that would open onto ground another player
      owns or where an enemy was seen; a test shows a closed corridor stays closed. Digging
      breaches release heroes (Level 2's party, `level2-party-release.md`). **Done 2026-11-14,**
      as `PlayerStrategy.Breach` (`BreachMode`): `Never` and `WhenDeveloped` mark no tile beside
      another side's open ground or within `EnemyClearanceTiles` (3) of any enemy sighting;
      `WhenDeveloped` (the default) breaches only once no safe tile is left, marked or not;
      `Always` is the pre-Task-2 frontier. The distance bound is the frontier's own: only a tile beside own floor is marked. A first default of `Never` could not win Level 1 (it
      stopped digging at tick 3253), hence `WhenDeveloped`. Tests: a closed corridor stays closed
      while safe ground remains; with none left the default opens it; ground near a sighting stays
      unmarked after the enemy leaves.
- [x] Implement in `PlayerActionRoutines`; the strategy gains the ordering and sizing values.
      **Done 2026-11-14.** `PlayerStrategy` gains `ExtraRooms` (Training Room, Library, Workshop,
      Prison, Temple, Graveyard, Torture Chamber, Casino, Combat Pit: each built once, rebuilt if
      lost, after every listed room stands and none has outgrown itself), `LairTilesPerCreature`
      1, `CreaturesPerHatcheryTile` 2, `TreasuryFullPercent` 80, `UseRooms`, `TilesPerWorkingCreature`
      3, `UseRoomEveryTicks` 200, `UseRoomMinGold` 1000, `Breach`, `EnemyClearanceTiles` 3, `EnemyNearTiles` 8 (playbook `strategy.enemy-near-tiles`, 0-255).
      `CampaignPlayerOptions` gains `GoldPerTreasuryTile` and `HeartGold`, definition data the
      harness passes (`MaxGoldPerTreasuryTile`; sixteen heart tiles of `MaxGoldPerDungeonHeartTile`,
      Q52, ADR-0027); unknown, no Treasury grows for want of room. Growth and extras wait while a
      missing listed room is offered but unaffordable; a pending build completes when its type's
      tile count rises; a Work step carries an idle own non-worker to a room `UseRooms` names. It stops while a threat
      stands within `EnemyNearTiles` of home (Chebyshev), and never picks a room tile with one that near, whatever its
      distance from home. A creature already lifted goes on to its room, the Pit or a scout tile; it turns back only
      for a threat near home or near its destination (Q843).
      `IsThreat` treats an enemy on the player's own Prison or Torture Chamber as a captive.
      **`UseRooms` defaults to empty:** with the Training Room on by default Level 3 accept seeds
      12 and 13 were lost (8/10); on those two seeds training off won both, extras off won both
      and `Breach = Always` still lost seed 13, so the training lifts were the cause. A level's
      strategy names the rooms it uses.
- [x] Measure per ladder level, before and after: rooms and room tiles owned, creatures housed
      and trained, win or loss. The share of wait decisions is reported but is not a goal.
      **Done 2026-11-14,** seeds 1-3 diagnosis only, no rate claimed; the table is in the
      [ladder](../research/campaign-bot-ladder.md) section 7. Room tiles rise 2-3x on most levels
      and the wait share stays 55-100%. Level 3 seed 3 is a loss; Level 6b is worse (one timeout,
      two defeats against three timeouts); Level 5 now survives to the cap but waits 97%; Level
      4 wins 2 of 3.
- [x] Levels 1-3 rerun on accept seeds 11-20 (behaviour changed on purpose): the ladder rows
      are updated, and a level that drops below 9 of 10 is reported, not hidden. New baselines go
      into `baselines.tsv` with the commit. **Done 2026-11-14:** 10/10 on each of Levels 1-3 from
      a receipted build, all acceptance claimed ([ladder](../research/campaign-bot-ladder.md)
      section 7); `baselines.tsv` re-recorded at hash 519 (commit 207f3a58).
- [x] Reviewer, documenter, commit. Two reviewer passes and a documenter pass, 2026-11-14; code
      in 207f3a58.

## Task 3a: keeper knowledge (owner direction, 2026-11-14)

The bot built every room as one fixed square and could not tell a working room from a useless one
(Task 2 ended Level 3 games with a 5-tile Training Room and a 7-tile Workshop; furniture stands
only on interior tiles, `AutoFurnitureRules`). The owner: the bot must be fed basic information
and common-sense strategy, for instance that a Library's shelves set how many Warlocks it draws.
Three layers, kept apart so each says where it comes from:

1. **Room facts, generated** (`knowledge/rooms.yaml`, by `keeper-knowledge` in DK2.Harness; never
   hand-written; the folder is outside `scenarios/` because every yaml there is a scenario). For
   every buildable room type and each square side 3-7, the simulation's own rules are run on a
   synthetic room painted on Level 1's map, ringed by wall (terrain 9) and again by open ground
   (terrain 3): floor slots, capacity, attraction (`AttractionBudget.Contribution`), gold held
   (`GoldStore`), furniture, cost (`RoomDef`), and per creature type the room tiles, gold and mana
   its portal asks and whether a keeper's portal offers it at all (`PortalRules.CanComeFromPortal`).
   The file's header names the research docs its readings rest on; their confidence is the
   confidence of the facts. Definition data, like `WorkerTypes`: loaded before tick 0, read-only
   during play. A test compares the committed file with a fresh generation.
2. **Rules, written** (`knowledge/rules.yaml`): common-sense strategy, one rule per entry with an
   id, the plain sentence, its source, a status (`proposed`, `tested`, `held`, `refuted`) and a
   machine form: `attraction-target`, `tile-target`, `research-shelves`, `built-in` (a strategy
   value already carries it) or `manual` (no form yet). `applies` is `always` or `playbook` (only
   when a level's playbook asks; Task 3b). A rule's status changes only with evidence (a tuner run
   or a cited source), never by assertion. Community claims enter only after a check against
   the binary or a run; none is a rule yet (R1-R5 come from the owner and `rooms.yaml`); DK1, DK
   Online and later games are excluded. `KeeperKnowledgeFile.Load` refuses a file with a wrong
   `hash_version`, an inexact room name, bad sizes, a negative value, a duplicate rule or a
   target without facts.
3. **Playbooks** (Task 3b): per level, what the level offers and the targets the rules imply.

The player (`CampaignPlayer`) sizes a room at the larger of its smallest useful side (a Temple
draws nothing below 5x5) and the side the attraction, floor-slot or tile targets need. With no free
square that big it digs one out (`PlayerStrategy.Sites`, default `RoomSites.Oversized`: only a room
larger than `RoomSide`; `All`, `None`), one site at a time, and waits for gold rather than settling
smaller while a free target square exists; a site not dug within 2400 ticks is given up and the
room settles for less. A room that caps creatures stays pending while its tiles rise, and a short
one is finished (three tries). R2 sets the Library's slot target from the research the level starts
with: floor slots = researchable items / 2 + 1 (`ResearchableItems`; Levels 2 and 7: 2 items, a 4x4
Library; Levels 4, 6a, 6b, 10: 1; the others 0, a 3x3).

- [x] Tests first: `KeeperKnowledgeTests` (installed data: a Library's shelves and a Temple's hand
      follow their square, the committed facts equal a fresh generation, each level's research
      sets the shelves, the repository's files load, bad files are refused with every problem
      named) and `CampaignPlayerTests.Knowledge` (sizing, sites, repair, waiting for gold, targets).
      **Done 2026-11-14:** `DK2.AI.Tests` 551 pass, `DK2.Behavior.Tests` 643 pass and 5 skipped;
      the test that a site dug in time but short of gold is not settled fails without its fix.
- [x] `keeper-knowledge` command, `rooms.yaml` generated and committed, `rules.yaml` started: R1
      smallest useful size, R2 research shelves (R2b, the Warlock form, is manual), R3 Treasury
      growth (built in, tested in Task 2), R4 25 Hatchery tiles for Bile Demons (playbook), R5
      size by the creatures wanted (manual). `--player-knowledge <dir|none>` (default the
      repository's `knowledge/`) is passed through `watch`; the verdict's `knowledge` section holds
      both files' SHA-256, the acting rules, the research count and the slot targets;
      `tools/build_receipt.py` hashes `knowledge/` too.
- [x] Community research (2026-11-14, background): DK2-only claims from public guides. None became
      a rule: the claims that Goblins need a 5-tile Lair and Bile Demons a 9-tile Lair are checked
      against the binary under Q816.
- [x] Measure as Task 2 did (seeds 1-3, Levels 1-10, Task 2 player against Task 3a); Levels 1-3
      re-accepted on seeds 11-20. **Done 2026-11-14,** hash 519 unchanged: Levels 1-3 won 10/10
      each, all claimed; on the diagnosis seeds one Level 2 seed is better, two Level 6a and one
      Level 10 seed lose at other ticks, the rest are identical, and Levels 5-10 are still not won
      ([ladder](../research/campaign-bot-ladder.md) section 8). Open: Q813 (Specials in the
      Library), Q814 (one site at a time), Q815 (the site scan's cost), Q816.
- [x] Reviewer (no blocker; it confirmed the knowledge stays within the observation boundary),
      documenter. Commit: 5118067a.

## Task 3b: the playbook format

**Produces:** `knowledge/playbooks/<level>.yaml` (not `scenarios/campaign-player/playbooks/`: every
yaml under `scenarios/` is a scenario, the reason `knowledge/` left it in Task 3a), loaded by
`PlaybookFile.Load(path, hashVersion)` (DK2.Harness; strict, hyphenated keys, an unknown key is
refused) to an immutable `Playbook` (DK2.AI):

```yaml
level: campaign:level2
available:          # generated by `playbook-available`, never hand-written
  read-at-tick: 3
  start: ["room:Lair", "room:Hatchery", ...]   # the bottom panel's Visible and Selectable choices
  creatures: ["creature:Goblin", ...]          # the level's portal pool, not the availability table
  may-change: [ { item: "room:Treasury", to: available, by: "trigger N MAKE" }, ... ]
plan:               # what the player does with it
  rooms: [Lair, Hatchery, ...]    # in building order
  extra-rooms: [...]
  rules: [R4]                     # knowledge rules with `applies: playbook`
tunable:            # integer values from the fixed `Playbook.TunablePaths`, each with a range
  - { path: strategy.room-side, value: 4, min: 3, max: 6, provenance: tuned, proven-on: "<commit>", hash-version: 521 }
notes:              # veteran knowledge; each cites a source
  - { kind: gold, text: "...", at: [x, y], provenance: researched, source: "..." }
events:             # what the script does, from the level's triggers
  - { id: first-wave, when: "trigger N, about tick T", expect: "...", source: "..." }
caps: { max-ticks: 57600, max-actions: 8192, wall-seconds: 2400, output-mib: 256 }
```

- **`playbook-available <scenario.yaml> [--player <id>] [--out <playbook>]`** (an authoring tool, not
  run-time) runs the scenario with a probe controller to its first playable tick. Start items are the
  bottom panel's choices that are Visible and Selectable (Room, Spell, Door, Trap), as
  `PlayerObservationBuilder.CaptureOffers` reads them. Creatures are the level's pool
  (`PortalRules.PoolLimit` above 0, level-merged database) that `CanComeFromPortal`, not the creature
  availability table, which the portal does not read (ADR-0316). `may-change` lists the player's trigger
  `MAKE` actions (type 6, `0x00518AD0`) and the research-list entries still researchable; creature
  `MAKE`s are omitted because the portal ignores them. `--out` replaces the available section of an
  existing playbook (refused for another level's, other sections, comments and line endings kept) or
  starts one.
- **Review.** `Playbook.Review(hashVersion)` names every problem at once. A planned room the level
  neither offers at the start nor may make available is a problem; one that only may become available
  is a warning. A tunable path outside the registry, a value outside its range, a researched value
  with no source and a tuned one with no `proven-on` commit and hash version are problems; a tuned
  value proven at another hash version is a stale warning. The command-line interval
  (`--player-interval-ticks`) is not a tunable: a limit stays on the command line.
- **`--playbook <file>`** (`HarnessCommandLine.PlayerFlags`, passed through `watch` as an absolute
  path) applies the plan and tunables to the starting strategy (`CampaignPlayerOptions.Playbook`). A
  run is refused (exit 2) for a playbook that fails review, one for another level, a `max-ticks` cap
  beyond the scenario's `duration_ticks`, and rules with `--player-knowledge none`.
  `KeeperKnowledgeFile.Load` switches on only the `applies: playbook` rules the playbook asks for and
  refuses a request for any other rule or a refuted one.
- **Caps** stand in only for limits the command line did not give
  (`CampaignPlayerSpec.GivenLimits`). Under `watch`, the playbook's wall cap becomes each run's
  `--player-wall-seconds` and must stay below `--timeout-seconds`.
- **Provenance.** `researched` carries a source; `tuned` carries the commit and hash version it was proven
  on.
- **Verdict and manifest.** `player-run.json` gains a `playbook` section (path, SHA-256, level, rules,
  tunables, warnings); `knowledge` reflects the playbook's rules. `PlayerRunManifest.Schema` is 3 and
  adds `PlaybookSha256`, information only; `Verify` reads 1-3 (2 was the seed). `PlayerRunOptions.Observed`
  is a hook for authoring tools; no player path reaches it.

- [x] Tests first: `PlaybookTests` (DK2.AI, 6) and `PlaybookFileTests` (DK2.Behavior, 12, install-gated
      where they need the game): Level 2 offers Lair, Hatchery and Training Room and no Treasury, a
      playbook planning a Treasury there is refused, creatures come from the pool, a replaced
      available section keeps the rest, caps and rules behave as above, the committed available
      section equals a fresh generation. **Done 2026-11-15:** `DK2.AI.Tests` 557 pass,
      `DK2.Behavior.Tests` 655 pass and 5 skipped.
- [x] `Playbook`, `PlaybookFile`, `playbook-available`, `--playbook` and caps. First review: the
      creatures were read from the availability table (a blocker; Level 3's script MAKEs Warlock, Goblin
      and Firefly unavailable, and the portal does not read that table, ADR-0316; whether one arrives
      depends on the rooms the keeper has and when they are built, owner 2026-11-15), rules were ignored without
      knowledge, `--out` could overwrite another level's playbook and ate its comments, and a playbook
      tunable overrode `--player-interval-ticks`; all four fixed.
- [x] `knowledge/playbooks/level1.yaml` (available section only) plays as without a playbook:
      Level 1 won at tick 2334 both ways, the manifest is schema 3 with the SHA-256, and the replay
      matched 2445 hashes. Hash 521, no simulation change.
- [x] Isolation review (no player path reaches `Observed`; the playbook is loaded before tick 0),
      reviewer, documenter. Open: Q817 (Level 6a creatures), Q818 (available items unchecked).
      Spot results of `playbook-available`: Level 1 Lair, Hatchery, Goblin; Level 2 adds Library and
      Training Room (the owner confirms the Library at the start, 2026-11-15), Warlock and Goblin,
      research Create Imp and Thunderbolt; Level 3 adds Treasury, Workshop, Wooden Door, Sentry Trap,
      Troll and Firefly; Level 6a has no pool creatures (Q817).

## Task 4: observation and action additions

Only what the panels show a player. Each addition: tests first, an admission test in
`PlayerActionAdapter`, an isolation review, and an observation schema bump with the journal and
replay tests updated.

| Addition | Why | Source of truth |
|---|---|---|
| Door and Trap offers in `PlayerOffers` | the panel shows them | `BottomPanelChoiceKind.Door/Trap`. **Built 2026-11-15** |
| Every offered spell, not only Create Imp | spells the level gives | the Spell panel. **Built 2026-11-15** |
| `PlaceDoor`, `PlaceTrap` actions | defend a route | the panel's build intents. **Built 2026-11-15** |
| `CastSpell(spell, target)` | offered spells | the existing spell intent path. **Built 2026-11-15** |
| Each own creature's level (1-10), health in petals and mood (happy, unhappy, angry) | the status flower shows them (`status-flowers.md`: H-00..H-05, SL-01..SL-10, SM-*) | the creature's flower. **Built 2026-11-15** |
| `CastSpell(Heal, creature)` | heal a creature in the Pit | the spell panel and the existing spell intent path. **Built 2026-11-15**, as a creature spell of `CastSpell`; the Pit routine that casts it is built below |

**The Combat Pit routine (owner direction, 2026-11-14; built 2026-11-15).** `CampaignPlayer.Pit` is off unless
`PlayerStrategy.PitFighters` is above 0 (default 0; playbook tunable `strategy.pit-fighters`, 0-16). It needs an
owned Combat Pit, an empty hand, no enemy within `strategy.enemy-near-tiles` of home or of the Pit's tile nearest home, and
`UseRoomEveryTicks` since its last act; the first that can act:

1. A creature collapsed on the Pit (one petal) gets the nearest liftable own Imp dropped on it, so the Imp carries it
   to its bed (`CarryUnconscious`). This runs before the interval check: a collapsed creature dies within
   `CreatureDyingStateDurationSeconds` = 60 s (240 ticks), so the rescue does not wait for `UseRoomEveryTicks`. Only
   with an own Lair, once per 400 ticks per body tile, rescues at least 40 ticks apart, and not for a body on an own
   worker's tile (it is being carried). A beaten fighter sinks into the floor and rises on a random rim tile (state 217,
   `combat-pit.md` section 4). A body on the rim is fetched at once, the Imp dropped on the body's own tile
   (`PlayerActionRoutines.RescueDropTile`: the nearest own floor tile within `ImpTaskSystem.DropScanTiles` that is not
   the Pit's interior, where the hand refuses a worker). A body still on the floor is left to sink and rise, timed per
   floor tile, and fetched there only after `RiseTicks` = 40 ticks, an estimate: state 217 waits ten turns, then sinks
   and rises 0x200 a turn, and the collapse before it is unread. Owner, 2026-11-15: "if a creature gets defeated in the
   combat pit, its body then appears on the combat pit rim, bot should grab an imp and drop it onto that bleeding
   creature so it gets dragged to its bed to recover". The Imp sent is the liftable one nearest the body (Q827,
   Q844-Q846).
2. A fighter on the floor (interior) at two petals (the last quarter, owner-confirmed 2026-11-15; a living creature
   shows at least two): `CastSpell(Heal)` when Heal is offered, affordable and recharged, else lifted to the nearest
   Hatchery tile (Q828, Q831).
3. A fighter on the floor at `CombatPitMaxExperienceLevel` (`CampaignPlayerOptions.CombatPitMaxLevel`, variable 192,
   ships 8; `combat-pit.md`): to its Lair when unhappy or angry (owner, 2026-11-15: "mood matters too, unhappy goes to
   lair"), else to a Hatchery at four petals or fewer, else to its Lair.
4. A lone fighter on the floor (its opponent gone, or none to come) is a victor: it is lifted to its Lair before it
   grows unhappy (owner, 2026-11-15; whether it does, and how fast, is Q836). "A pair fought" is remembered across the
   routine's interval, so a fight wholly inside one interval still leaves its victor lifted.
5. Below `PitFighters` fighters on the floor, and only when two can pair: the lowest-level idle own non-worker on plain
   floor or watching from the rim (job 26), at five petals or more, Happy and below the cap, is lifted onto an
   interior Pit tile (`InteriorRoomTile`, `IsInterior`); a drop there starts the fight (`combat-pit.md` section 9).
   The observation's Happy includes content, which the owner confirmed is what was meant (Q829). Only interior tiles
   count as fighting; a rim spectator is a recruit, and the owner confirmed fighters never stand on the rim (Q830).
   `PitFighters` of 1 is refused by `PlayerStrategy.Validate` and `Playbook.Review`: one fighter has no one to pair
   with.

A refused Pit drop goes to its own set, not the Work routine's. The harness passes `CombatPitMaxLevel`. Training Room
work stops at `TrainingRoomMaxExperienceLevel`, where the Pit takes over. Known gaps: a refused Heal is retried each
interval (cast outcomes are not read), and `Pit` allocates per decision when on. `DK2.AI.Tests` 597 and
`DK2.Behavior.Tests` 672 (5 skipped) pass, the latter with an end-to-end test (a beaten Goblin rises on the rim, the
bot drops one Imp there, the Goblin ends asleep in its bed); Level 1 seed 1 is unchanged (Completed at tick 2334, final
hash fd4199d3977b784f). Limits: that test covers the rim path only (the floor fallback is covered by unit tests), and a
floor-tile entry can survive up to 12 ticks when the routine is skipped. **Not measured:** only Levels 15a and later
offer a Combat Pit. A Level 15a campaign run built a 3x3 Pit, but the routine never acted. Q843 found two causes. Hero Guards stand in
the heroes' own Prison, 9 to 25 tiles from the Heart and in sight all game, and froze Work, the Pit and scouting; the
`EnemyNearTiles` gate fixes that. The bot's own eight Black Knights are prisoners in that Prison, listed as its
creatures but not liftable, so no fighter is free: freeing them is a level objective, not built, and the Pit stays
unused there. Level 15a seeds 1-3 are unchanged (Defeated at ticks 4861, 7554 and 9792). The fight routine is
unchanged: under `Engage` it still lifts a free fighter toward any enemy in sight (Q847).

**Built: creature level, petals and mood.** `PlayerCreatureSummary` gains `Level`, `Petals`, `Mood`
(`PlayerCreatureMood`: Happy, Unhappy, Angry) and `FlowerKnown`, filled by
`PlayerObservationBuilder.CaptureCreatures` as the flower shows them when hovered: petals are six at full
health, one fewer per quarter lost (`StatusFlowerRules.Ring`, `0x00596EB0`); mood is the band `0x00596410` reads,
Content shown as Happy. A creature whose flower never shows (held, joining, an own Horny;
`StatusFlowerRules.NeverShows`, `0x0048E830`) has level 0, 0 petals and no mood: unknown.
Open: Q819 (a held creature's values), Q820 (the ring's timing under hover).

**Built: every offered spell and `CastSpell`.** `PlayerOffers.Spells` lists a `PlayerSpellOffer` (spell id,
name, `Cost`, `Affordable`, `OnCreature`, `Ready`) for each Spell-panel choice that is visible and selectable,
the set a human can select (`BottomPanelView.Select`). `Cost` is the spell's `ManaCost` (Create Imp:
`ImpSupplyRules.CreateImpCost`), the value the simulation tests and charges; `Affordable` is the panel's cost
within mana; `Ready` is the slot's recharge pie done (`BottomPanelProgress.RechargePercent`, `0x004BE430`,
the cooldown `CanCast` tests); `OnCreature` is `KeeperSpellRules.TargetsAThing` of the target selector.
Possession is not offered: the player has no action to end one, and a human possession ends only by the owner's key press, the creature dying or mana running out (Q821, owner-confirmed). The Reaper's spells are never on the
panel. `PlayerAction.CastSpell(Spell, Target, Tile)` takes exactly one target, and `PlayerActionAdapter.Cast`
refuses Create Imp (its own action), an unoffered spell, the wrong target kind, an out-of-bounds tile, an
unknown handle and a non-creature handle. A creature spell becomes `CastSpellOnCreatureCommand` (packet 55) on
a visible creature, a place spell `CastSpellAtPlaceCommand` (packet 54) at the tile centre, as the click sends
them (`PlacementCursor.Spell`); the simulation decides whether the cast lands. The cursor shows No Go for Thunderbolt
or Heal at a dying creature because it asks the simulation's `CanCast`, so the bot's cast is refused the same way (Q823,
owner-confirmed). Both commands were already in
the journal and replay paths. `CoverageLimits` says Possession, the Reaper's spells and
object targets are not offered, that a creature spell may name a creature the hover pick passes over (Q823),
and that Sight of Evil is offered but reveals nothing to the observing view: a human sees the ground it reveals, so
the offer is a mana sink until a later addition applies it to the observation (Q822, owner-confirmed).
This row made `PlayerObservation.SchemaVersion` 3, information only (journal header `player.observation.schema`, read by
nothing; older journals replay). `DK2.AI.Tests` 557, `DK2.Simulation.Tests` 5472 and `DK2.Behavior.Tests` 667
(5 skipped) pass; `DK2.Presentation.Tests` passed 1785 of 1786 in the full run, the one failure
`ClassicObjectLightTests.WarmDynamicPublicationAndGatherAllocateNothing` (an allocation measurement in code
this change does not touch, [TRAPS](../TRAPS.md)), which passed alone three times. Level 1 seed 1 is unchanged
(Completed at tick 2334, final hash af7204fe4c0f8594), no simulation change. Reviewer: no blocker. The hidden-state
receipt test (`PlayerSessionTests.ReceiptsDoNotDistinguishHiddenStates`) has no `CastSpell` probe because its
synthetic database offers no castable spell; the adapter reads only the capture, so the property holds by design.
Answered by the owner on 2026-11-15: Q821-Q823.

**Built: doors and traps.** `PlayerOffers.Workshop` lists a `PlayerWorkshopOffer` (`Kind` Door or Trap, `Id`,
`Name`, `Cost`, `Affordable`, `NeedsWorkshop`), doors then traps by id, for each Workshop-panel slot that is
visible and selectable. A trap with definition flag `0x2000` is never on the panel; a slot whose availability
is unknown (2 or 4) is not offered. `Cost` is the item's `GoldCost`, paid at once when the blueprint is laid
(`BlueprintPlacementRules`, `0x004C33B0` door and `0x004C3860` trap; owner-confirmed,
[blueprint-placement.md](../research/blueprint-placement.md)), so `Affordable` (the panel's cost within gold) is
meaningful. `NeedsWorkshop` marks a slot greyed because the keeper owns no Workshop tile: the click places
nothing (`Hand_Click`, `0x0040849F` and `0x00408586`). `PlayerAction.PlaceDoor(Door, Tile)` and
`PlaceTrap(Trap, Tile)` are offered when an offer is not greyed; `PlayerActionAdapter` refuses an unoffered or
greyed item and an out-of-bounds tile, otherwise it sends `PlaceDoorCommand` or `PlaceTrapCommand` at the tile, as
`DungeonRunner.Hud`'s click does (both were already in `HumanCommandPolicy`, the codec and `PeerCommands`).
The click's own door and trap arms test the tile locally (vtable `+0x244`, `+0x24C`) before the packet and have
no gold gate; the adapter skips that test. The simulation refuses the same placements (`blueprint_refused`:
`NotTheKeepersTile`, `Solid`, `ARoom`, `Occupied`, `NoWalls`, or `NotEnoughGold`), so the outcome is the same,
but the bot can spend an action on a placement a human click never sends. Stock: the slot draws no digits and no
crate count ([panel-indicators.md](../research/panel-indicators.md)), only the head-order manufacture wedge, so
the offer carries none; `CoverageLimits` says that and the untested tile. `PlayerObservation.SchemaVersion` is 4,
information only. `DK2.AI.Tests` 557, `DK2.Simulation.Tests` 5479, `DK2.Presentation.Tests` 1812 and
`DK2.Behavior.Tests` 669 (5 skipped) passed before the review fixes; `PlayerWorkshopTests` (3) pass after them.
Level 1 seed 1 is unchanged (Completed at tick 2334, final hash af7204fe4c0f8594), no simulation change.
Reviewer: no blocker, no major. Open: Q825 (a greyed slot's click), Q826 (placed-but-unbuilt doors on the slot).

**The Imp scouting routine (owner direction, 2026-11-14; first aimed at Level 6a; built 2026-11-15).**
Unknown ground (`PlayerTile.IsKnown` false) hides gold, gems, water to bridge and enemies. `CampaignPlayer.Scout`
is off unless `PlayerStrategy.Scouts` is 1 (default 0; playbook tunable `strategy.scouts`, 0-1); rule R6 in
`knowledge/rules.yaml`. It runs when no Work or Pit routine acted, with an empty hand, no enemy within `strategy.enemy-near-tiles` of home, at
least 3 Imps, one of them unhurt to send, and `UseRoomEveryTicks` since the last trip.
- Target (`ScoutTarget`): known, dry, roomless floor beside unknown ground that no enemy seen in the last 1200 ticks
  stands within 8 tiles of, and no ground held by another keeper or the heroes lies within 8 tiles of. Owner 0
  (dirt path is not ownable, so the view shows 0) or Neutral qualifies. The binary's gate lets a
  worker down there (`0x00450910`, `HandRules.MapAllowsCreature`); water, lava, rock and enemy ground are refused.
- One own Imp at a time is lifted and dropped there. After 8 ticks it is lifted to the next target: the fog reveals
  its sight on the first tick (`FogOfWarSystem`, `fog-reveal-sources.md`), and the owner says an Imp left on dirt path
  stands 1-2 s, then walks back to own ground (Q832). The scout is followed within 3 tiles of where it was last seen.
- Danger: when it is hit (petals below 6) or an enemy comes within 8 tiles, it is lifted home on the routine's next
  decision (the owner says a hero cannot hit it first if the keeper is paying attention, Q833). A held worker is
  always dropped near home, never toward an enemy.
- What it reveals feeds the existing plans: gold and gem tiles to dig, a Wooden Bridge over water that joins two
  pieces of ground, enemies to avoid (`_sightings`).
- Stale carry flags are cleared when the hand is empty. Known risks, bot only (Q838): a refused scout drop leaves
  the routine's tile pointing where the Imp never went, and its refusals go into the Pit's refused set; an own Imp
  within 3 tiles may be adopted as the scout; the threat memory is never pruned.

`DK2.AI.Tests` 579 and `DK2.Behavior.Tests` 670 (5 skipped) pass; Level 1 seed 1 is unchanged (Completed at tick
2334, final hash af7204fe4c0f8594). **Level 6a, seeds 1-3, before the last fixes:** scouting off Defeated at ticks
3955, 3934 and 2982; scouting on Defeated at ticks 3951, 4094 and 3837, so no gain is shown yet. Removing the
drop-toward-enemy rule or the allied-ground rule fails its test. Open: Q834, Q835, Q838.

**The Imp creation routine, R8 (owner direction, 2026-11-15; built 2026-11-15).** `CampaignPlayer.CreateImp` is off
unless `PlayerStrategy.CreateImps` is 1 (default 0; playbook tunable `strategy.create-imps`, 0-1); rule R8 in
`knowledge/rules.yaml`. Before it, only the computer keeper (`KeeperAI.CreateImps`) cast Create Imp. It needs
observation schema 5, presentation only (the simulation and its hash are unchanged): `PlayerObservation.ManaIncome` and
`ManaUpkeep` are the mana orb's two figures, from the same `ManaRules` calls as `KeeperResourceView`, and
`PlayerSpellOffer.Upgraded` is the slot's upgraded icon ([panel-indicators.md](../research/panel-indicators.md)).

| Tunable | Path | Default | Range |
|---|---|---|---|
| `CreateImps` | `strategy.create-imps` | 0 (off) | 0-1 |
| `MaxImps` | `strategy.max-imps` | 16 | 1-40; the owner's band is 15-20 |
| `MinImps` | `strategy.min-imps` | 6 | 0-40; above `MaxImps` the cap wins |
| `ManaReserve` | `strategy.mana-reserve` | 15000 | 0-1000000 |
| `ImpPriceSeconds` | `strategy.imp-price-seconds` | 60 | 1-3600 |

One cast per 8 ticks, on own floor nearest the dig mark nearest home (a tile where a cast was refused, learned from
the receipt, is skipped). It casts only if all of these hold:
1. Own Imps are fewer than `min(target, MaxImps)`, where `target = max(MinImps, ceil(marks / 3))`.
2. The price leaves `ManaReserve` intact, so Heal, Thunderbolt and Call to Arms can still be cast.
3. The price is at most `ImpPriceSeconds` of net income (orb income minus upkeep). A heuristic, Q842; it ignores the
   new Imp's own upkeep (7/s).
4. No enemy in sight within 10 tiles (Chebyshev) of the birth tile, a rival keeper's creatures included (Q840, owner:
   defense is priority).
5. Own Imps, held ones included, are not below `CampaignPlayerOptions.FreeImps`: the game makes a free Imp every few
   seconds only while the tally is below `MinimumImpThreshold` (`0x004BB479`-`0x004BB486`), so the bot waits for it
   (Q841, owner: "wait"). The harness reads the variable off the level (4 as shipped); 0, unknown, never waits.

The upgrade wait: while Create Imp is not upgraded and an own non-worker stood in an own Library within 1200 ticks,
the target is `MinImps` unless the need is at least twice the Imps there are. An upgraded cast makes a level 4 Imp
(shot 60, `0x004AFE50`; Q837). The Library upgrades a spell only after everything researchable is done
(`0x004C3000`), so the wait lasts at most 4800 ticks (20 game minutes), Q839. The owner: the time depends on how many
items are still to research, since the upgrade follows the research order. Not built: estimate the wait from that
count in place of the fixed limit; the 20-minute limit stays.

Why no cast near an enemy: the first Level 6a measurement found the routine buying about 50 Imps in the last 900 ticks of a
lost keeper. Heroes killed each new Imp beside the Heart, the price fell back, and the bot bought again.

`DK2.AI.Tests` 588, `DK2.Simulation.Tests` 5575 and `DK2.Behavior.Tests` 671 (5 skipped) pass; Level 1 seed 1 is
unchanged (Completed at tick 2334). Breaking the reserve, price, upgrade-wait, free-Imp-wait or cap rule
each fails a test. **Level 6a, seeds 1-3:** routine off Defeated at ticks 3955, 3934 and 2982; routine on (after the
fight rule) Defeated at 3930, 3954 and 5274, with 4, 1 and 2 casts. The first "on" run, before the fight rule, was
Defeated at 4144, 4302 and 3953 with 63, 55 and 53 casts. No gain is shown; the bot loses Level 6a either way.
Reviewer: no blocker. Open, bot only: Q842; the observation builder computes the orb figures on every observation,
even with the routine off (minor cost); the Behavior test compares `ManaRules` with `ManaRules` through the HUD, so it
proves the wiring, not the figures; the fight rule counts any enemy, including a rival keeper's creatures parked in view
(owner-confirmed, Q840), and does not try another tile.

Possession (`PossessionCommands`) is not added here; a level that needs it is recorded as a
controller limitation until a later task.

Every row above is built; the routines that use them are separate: the Combat Pit, Imp scouting and Imp
creation (R8) routines are built; defending a route with doors and traps is not. Manufacturing doors and traps is a Task 2
routine.

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

**Level 6a** (chosen 2026-11-14): kill the Lord of the Land at (40,43), 22 tiles from the Heart, with
the starting roster only (no portal creatures); the Heart falls at ticks 4542-6523 today.

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
