# Handoff

Current work and entry points for the next session. Decisions, evidence and open
questions live in their canonical documents; this file links to them.

## Current state

- Milestones and acceptance criteria: [reimplementation plan §8](../DK2_REIMPLEMENTATION_PLAN.md).
  Generate counts with `dk2tools status`; use `dk2tools coverage` for implementation
  coverage. Neither counts nor historical test totals are maintained here.
- Current simulation/save composition: [hash versions](research/hash-versions.md).
  Behavioral parity with DK2 remains incomplete; bounded replay controls do not
  establish full-state, campaign-outcome or interactive-play acceptance.
- Classic rendering, HUD and front-end integration are on master. Owner play,
  visual and hearing acceptance remain open in the
  [completion acceptance record](plans/2026-10-29-hud-and-front-end-completion.md#acceptance-record)
  and [HUD comparison checklist](research/hud-comparison-checklist.md).
  Lighting acceptance and remaining comparison/performance limits live in
  [classic lighting verification](research/classic-lighting-verification.md).
- Presented-world P1 and its follow-ups are merged. Measurements and remaining
  performance targets: [presented-world result](plans/2026-11-06-presented-world.md#result-measured-2026-11-06)
  and [performance policy](PERFORMANCE.md).

## Active continuation

The latest Q712 work is the bounded Level 5 draw-continuation investigation.
The last controlled result (hash 490 with ADR-0765, the replay inputs including the
five hero guards restored at turn 1400: every native draw of the log matches, turns 1401-1900) is in
[Q712](research/open-questions.md), with its causes in
[the Heart's pillars and centre-tile objects as obstacles, and the lava centre's refused route](research/structure-blocks.md#53-the-hearts-obstacle-is-forwarded-by-the-room-not-found-on-its-tile-q355) (ADR-0750, ADR-0751, ADR-0755),
[the mover block for every non-Imp](research/steering-behaviours.md#75-the-creatures-tick-0x004d5a40-and-its-callers) (ADR-0751),
[the generic stop self-point](research/stop-self-route.md#every-non-imp-stops-the-same-way-q712-adr-0751) and
[a hero's arrival at an action point](research/stop-self-route.md#a-hero-arriving-at-an-action-point-keeps-its-self-route-q712-adr-0750) (ADR-0750),
[a follower's turn toward its leader](research/hero-party-walk.md) (ADR-0751),
[an Imp's alarm walk ends on its last step](research/combat-handler-handovers.md) (ADR-0752),
[an attacked floor's claim clip](research/imp-claim-action-clock.md#an-attacked-floors-claim-starts-its-own-clip-adr-0746-hashsave-472) (ADR-0746),
[a fallen creature's stale body](research/steering-body-retention.md#state-217-leaves-its-body-where-it-fell-adr-0745-hashsave-471) (ADR-0745),
[possessed pick heights](research/possessed-pick-heights.md),
[step-back speed mode](research/step-back-speed-mode.md),
[a speed a handler sets, and the low-health half speed](research/speed-set-turn.md) (ADR-0737, ADR-0756),
[the body's blocker bits a fight sets](research/navigation-and-steering.md#32-three-of-the-eight-do-nothing-and-separation-is-read-to-its-store) (ADR-0756),
[the hand drop's fall and its landing in the creature's own update](research/hand-drop-landing.md) (ADR-0730, ADR-0753),
[the shot finder's tile window and first-match order](research/shots.md#4a-collision-0x004abd60) (ADR-0754),
[the pick-up's turn](research/hand-pickup-turn.md) (ADR-0731),
[the fall heading and the fall's step](research/death-fall-heading.md) (ADR-0732, ADR-0741),
[the shot's probe and the hit's heading](research/shot-probe-and-hit-heading.md) (ADR-0741),
[the drop's heading](research/drop-heading.md) (ADR-0733, ADR-0736) and
[the fight's step 5](research/combat-fight-state.md) (ADR-0734, ADR-0736),
[the stored attackable bit](research/combat-acquisition.md#4-isattackable-is-a-stored-bit-refreshed-at-four-sites-0x0045a8b0) and
[the collapse's action end](research/functions/0048B770.md) (ADR-0757),
[the head count's collapsed creature](research/combat-fear.md) (ADR-0758),
[own-land regeneration every turn](research/creature-needs.md) (ADR-0759),
[the fighting arms' candidate walk order](research/combat-target-selection.md) (ADR-0761),
[the melee hold's end, the lifted creature's bit, own-land healing and the collapse booking](research/open-questions.md)
(ADR-0762 to ADR-0765, Q768 to Q771); earlier prefixes are
[possessed melee draw](research/possessed-melee-draw.md) and
[store-gold steering](research/store-gold-steering.md). The capture's draw log ends at 1900;
the remaining state differences are in Q712. Build only behavior supported
by the binary or a capture; do not tune rules to outcome averages.

The controlled prefix does not establish full-state or Heart-outcome parity.
Restore and adapter gaps remain separate: Q713, Q714, Q719, Q723, Q736 and Q738
in [open questions](research/open-questions.md). The broader M6 work is in
[Heart defence parity](research/heart-defence-parity.md) and
[resync replay](research/resync-replay.md); Level 4's remaining Guard-room
exposure is Q710 in [Level 4 outcomes](research/level4-outcomes.md).

## Other pending work

These are entry points, not a new priority order.

- Owner's standing priority: excess Level 7 Imp deaths, Q582. See
  [combat acquisition](research/combat-acquisition.md) for the matched encounter
  checks still needed.
- M12 keeper acceptance and remaining decisions: [Keeper AI §12.7–12.8](KEEPER_AI.md),
  [native keeper comparisons](research/computer-keeper-observations.md), Q563.
  Enhanced AI remains outside Classic fidelity scope.
- M7 trap/door comparisons: Q732 and the owner observations linked from
  [traps](research/traps.md). Keep original-game acceptance separate from built rules.
- Possession camera and input: [build plan](plans/2026-10-12-possession-arms-build-plan.md)
  and [owner play questions](research/possession-arms-and-inputs.md).
- World-sound coverage and pending hearing checks: [world-sounds plan](plans/2026-10-31-world-sounds.md).
  Check the current code and Git history before relying on its branch-status prose.
- Autonomous playtesting: [Stages A–C](plans/2026-10-27-autonomous-playtesting.md)
  and [Stage D](plans/2026-10-27-stage-d-unity-input.md). Input replay/extraction
  and rendered-HUD automation remain follow-up work; use the completion acceptance
  record above for the integrated walkthrough and its journals.
  Approved design: [agentic campaign playtesting](plans/2026-11-07-agentic-campaign-playtesting-design.md).
  The [stages 1-2 plan](plans/2026-11-07-campaign-player-stages1-2.md) runs inline in a
  worktree on `feature/campaign-player` (not merged). Task 1, the detached player contract
  (`DK2.Simulation.Players`, `DK2.AI.Players`), task 2, the visibility-safe observation
  (`PlayerObservationBuilder` in `DK2.Presentation`), and task 3, bounded action admission
  (`PlayerSession` in `DK2.Harness`, `PlayerActionAdapter` in `DK2.Presentation`), task 4,
  the tick-loop integration (`RunOptions.PlayerRun`, `PlayerRunCoordinator`, replay of
  `player.run.schema=1` journals, `PlayerRunManifest`), and task 5, the observation-only
  `CampaignPlayer` strategy (`DK2.AI.Players`), and task 6, `run --campaign-player` with its
  limits, evidence folder and verdicts (`PlayerRunCommand`, `PlayerRunReport`; see
  [`DK2.Harness`](../src/DK2.Harness/README.md)), are built. Task 7, the Level 1 acceptance
  (`scenarios/campaign-player/`, `CampaignPlayerLevel1Tests`; run with `run --campaign-player 3`),
  is built on top of the Portal Gem fix (ADR-0725, hash 456, merged into this branch from fix/reaper-gem-pickup): from the unmodified
  start the player won seeds 1, 2 and 3 (ticks 2360, 2244, 2216), each with the finale reaching the
  gem pickup (ticks 2490, 2346, 2316), and every journal replayed all hashes without the controller.
  The study is `F:/DK2Evidence/studies/campaign-player-l01-acceptance--2026-11-09--002`; the owner's
  commit and integration are pending. This is Level 1 only, headless, not a whole-game or rendered
  acceptance. Build the receipt with `tools/build_receipt.py`; a run given `--player-receipt <dir>`
  can claim acceptance under the conditions in the [harness README](../src/DK2.Harness/README.md),
  and prints `not claimed: <reasons>` otherwise.
- Modern Imp asset proof of concept: [asset pipeline §10](ASSET_PIPELINE.md)
  and [image-to-3D trial](plans/2026-10-07-imp-image-to-3d-trial.md).
  Read [art direction](ART_DIRECTION.md) before asset work.

## Workspace and evidence

- Check `git status` and `git worktree list` before editing. Another session may
  own a checkout; do not switch branches in the shared main checkout. Retained
  worktrees and their branch names must be discovered, not copied from this file.
- The main checkout has an owner-edited `unity/DK2.Unity/Assets/DK2/Scenes/Dungeon.unity`.
  Preserve it. Use serial Unity checks; isolated probes must not save or restart
  the owner's editor. Editor workflow: [Unity README](../unity/README.md).
- Irreplaceable native captures, extracts and replay rigs belong in
  `F:/DK2Evidence/`. Follow AGENTS.md's add-only, no-links and cleanup-approval
  rules. Do not rely on legacy `reverse-engineering/dk2runs/` paths;
  the 2026-10-18 cleanup lost captures and rigs. Follow each research note's
  external evidence path and verify availability before planning a replay.
- New native recordings use the [complete recorder](../tools/oracle/native-capture.md).
  Research setup and tools: [reverse-engineering README](../reverse-engineering/README.md)
  and [research queue](RESEARCH-QUEUE.md). Read function records before binary queries.
- The reference install is `E:/GogGames/Dungeon Keeper 2`; `DK2_DIR` overrides
  discovery. Install-dependent checks skip without data unless
  `DK2_REQUIRE_INSTALL=1`. See [build/test guidance](../README.md#build-and-test).

## Where to work

- Project boundaries and simulation order: [architecture](ARCHITECTURE.md).
  Rules and mutable state belong in `src/DK2.Simulation`; `src/DK2.AI` observes
  and emits commands; `src/DK2.Harness` runs headless scenarios. Unity integrates
  and presents them.
- Tool and scenario entry points: [tool commands](../src/DK2.Tools/README.md)
  and [scenarios](../scenarios/README.md).
- Follow AGENTS.md for verification, reviewer/documenter passes and documentation
  gates. Read [traps](TRAPS.md) before long work. Evidence belongs in research
  notes, decisions in ADRs, function findings in their KB records, and remaining
  unknowns in [open questions](research/open-questions.md).

## Run and verify

From the repository root, the verification gate list is:

```sh
dotnet build DK2.slnx -c Release
dotnet test DK2.slnx -c Release --no-build
dotnet build unity/DK2.Unity.Compile/DK2.Unity.Compile.csproj -c Release
dotnet run --project src/DK2.Tools -c Release --no-build -- verification-ledger --check
dotnet run --project src/DK2.Tools -c Release --no-build -- kb check
dotnet run --project src/DK2.Tools -c Release --no-build -- coverage --check
dotnet run --project src/DK2.Tools -c Release --no-build -- docs --check
dotnet run --project src/DK2.Tools -c Release --no-build -- status --check
```

Build/test prerequisites and install-dependent skips: [root README](../README.md#build-and-test).
The Unity compile project is outside the solution; run `unity/sync-assemblies.ps1`
after source changes before editor checks. See [Unity workflow](../unity/README.md).
Check the changed diff with `git diff --check`.

A simulation state change requires matching `GameSimulation.HashVersion` and
`WorldSnapshot.FormatVersion`, a new determinism pin, and a
[hash-versions row](research/hash-versions.md); an observation-format change
requires a schema bump.
