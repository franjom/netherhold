# Handoff

Current state and the next useful action live here. Git holds the history. DK2
facts and their evidence live in docs/research/, decisions in docs/DECISIONS.md,
and unknowns in docs/research/open-questions.md (AGENTS.md: one fact, one home).

## Current state

The milestone table and acceptance criteria are in DK2_REIMPLEMENTATION_PLAN.md
§8. Regenerate its numeric status with dk2tools status after a Release test run;
do not maintain counts here. For exact build coverage use dk2tools coverage
--all and dk2tools coverage states.

- M0–M4 are closed. M5 has an original-game idle-trigger comparison on two
  levels (docs/experiments/). M6 remains partial: the original packet replays
  of Levels 1–4 run over seeds 1–20 and win 20/6/14/2 on master (2c0473bf, ADR-0499).
  The Level 1–3 seed spread against DK2's own seeds is in
  experiments/2026-10-06-packet-replay-seeds/ (open difference Q567); Level 4's
  is in experiments/2026-10-12-level4-packet-replay/. Level 4's largest gap is
  Imp losses: the keeper loses a median 5 Imps a seed (272 over 20) where DK2
  loses none, down from 16 through ADR-0488–0491 and ADR-0499.
- The fight approach is DK2's state 8, a walk resuming 199 (ADR-0483), and no
  combat experience is paid on that walk. A forced target no longer reaches a
  corpse (Q598). Room ticks run between the creature pass and the players, as
  DK2's do, with the hated-company rule (ADR-0479); the store sweep runs on
  DK2's turn (Q596). The stun counts from its first call (ADR-0480). Only a
  keeper's creatures run the needs, need-anger and mood choosers; heroes skip
  them (ADR-0485, Q599). The L1–4 wins above predate this hash 254 change
  and ADR-0486's hash 255; neither has been re-measured on the rig.
- The navigation mesh's locate is DK2's cell-cache walk and the whole-map
  builder writes DK2's slots (ADR-0486, hash 255): on the owner's Level 10
  mesh capture 130 of 140 intervals reproduce slot for slot and the first dump
  matches in 2,826 of 2,828 triangles (tools/meshreplay). The next capture
  should carry its route log (--navlog).
- Creature trigger action 20 gives the Portal Gem: Level 1's Lord carries it
  from creation and drops it at death, and its carrier gives off the Gem dust
  trail at mesh point 10001 in Unity (ADR-0495, hash 263; L1 20/20 unchanged).
  As of 2026-10-17 the owner has not judged the trail in Unity; ours is paced at 20 Hz and
  may be sparser than DK2's (Q606). The star icon above him is Q607. A live
  check rig: reverse-engineering/dk2runs/level1-lord-sparkle/run.ps1 -Live,
  with dk2reveal.py exploring the map in memory.
- M7 remains partial: combat, hero parties, door contact and the door fight
  (ADR-0318), hero gates (ADR-0320), every shipped trap (ADR-0321, ADR-0331) with
  the Boulder's rolling object (ADR-0333), the Jack In The Box's Jack (ADR-0337) and
  the reveal (ADR-0338), the Guard Post (ADR-0330), trap upkeep and damage (ADR-0326)
  and creatures fighting a revealed trap (scan D, ADR-0340) run; disarming is deferred under
  ADR-0329's scoped inference (Q455); shot heights go to M9 and the Boulder's slap to object slapping
  (ADR-0341). Left: Q513 and the owner observations pending in
  experiments/doors-and-traps.md.
- Keeper spells (ADR-0350): Create Gold, Tremor, Turncoat, Summon Horny,
  OneShotReaper and Possession's simulation side are built, and Sight of Evil
  casts with an empty effect (ADR-0352), its region presentation (Q194). Research
  grants every upgrade through ResearchTreeRules. Possession's followers
  (ADR-0445), sneak, hypnosis and sniper commands (ADR-0446), object, pillar,
  shot and body arms (ADR-0468), dig and claim (ADR-0469) and the gem face
  (ADR-0470) are built. Left unbuilt: Horny's idle AI, the third cast path
  (action 56 only, Q537) and the held-creature regeneration in Q152.
- M12 remains partial. DK2.AI.KeeperAI is an input source that emits
  PlayerCommands through a detached, serialisable observation (schema 19).
  The task machine digs, expands, builds rooms, manages gold and imps, and
  bridges water and lava; every message type its walk reads is produced and
  consumed (ADR-0353, ADR-0412–0418); the threat response, retreat, battle step,
  Call to Arms, Thunderbolt, Heal, Chicken, Inferno, Create Gold and Sight of
  Evil run through simulation commands; the attack planner tunnels, stages and
  calls its army; the nine lobby personalities load (ADR-0410). Left: the
  research move, prisoner/special decisions and the trap/door step (parked,
  below). See KEEPER_AI.md §12.7–12.8. Same-map comparisons against DK2's own
  keepers (Level 17, Level 11A) are in computer-keeper-observations.md, whose
  last section is the acceptance table; open timing differences are Q563 and
  Q574.
- Goal 7's writer/editor API is complete and verified (ADR-0346,
  `src/DK2.Data/README.md`); the editor UI and playability validation are
  future work.
- The creature state machine dispatches category hooks; CategoryHooks.Slots
  holds every hook's status (ADR-0308, ADR-0319, ADR-0322).
- Performance: profiling markers (ADR-0444, profiling), the worker reachability
  flood (ADR-0453), kept navigation and room-sweep state (ADR-0454, ADR-0466),
  DK2's per-tile navigation-mesh edit (ADR-0461), presenter and terrain
  batching (ADR-0456, ADR-0457) and a worker-thread tick (ADR-0458) are built.
  The owner has still to check shaders in a standalone build, terrain seams
  while a tag wave drains, and the worker-thread tick in Unity.
- A test oracle runs DKII.EXE's own pure functions and tables under Unicorn and
  holds our C# to them, also on states captured from DK2 (ADR-0481,
  tests/DK2.Oracle.Tests, tools/oracle/). The binary census of 2026-10-14
  (research/binary-census.md) ranks 941 unread simulation-core functions; every
  lead read so far was presentation, set-up or loader plumbing except the
  hated-company step (Q595, built).

## Classic look

Steps 0–4 of docs/plans/2026-10-09-classic-look.md are merged: DK2's rooms,
walls, liquids, objects, authored animation, room life, construction waves,
Prison rats, Portal entrances, corpse art, terrain debris and the Hand
(ADR-0431–0438, 0442, 0444 bank heights, 0447, 0449–0452, 0460, 0462–0465,
0467). The Heart's health pulse (ADR-0463), mana sparkles (ADR-0474, an owner
adjustment; cause open in Q594), opening sequence (ADR-0475), Portal entrance
effect (ADR-0476) and Dirt Path foot dust (ADR-0477) are built. Visual acceptance
is tracked in research/classic-lighting-verification.md. Step 5's
base camera, lighting/fog and follow-up fidelity corrections are on master.
Milestones and remaining gates: plans/2026-10-14-classic-look-step5.md.
Research: research/classic-lighting.md; current visual acceptance, standalone
material verification and performance limits: research/classic-lighting-verification.md.
The owner accepted the current appearance, including restored Heart arches,
wall caps, frame-independent torches and standalone rack transparency.
Matched-original startup-smoke and Levels7/8 comparisons remain pending.

Preserve the accepted brighter preview with Native Initial Ambient off;
+/− controls native ambient. Discovery schema/cadence limits are in
research/fog-reveal-sources.md; animated silhouettes remain under ADR-0482.
Scripted camera paths are built separately (ADR-0496; research/camera-paths.md).
Room/neutral-Portal border flames are researched but unbuilt
(research/room-border-effects.md); claim-mist bleaching remains open (Q609).
Creature asset keys are cached per definition/action; transparent terrain uses
retained stable-sort buffers and reuses meshes across changes in draw order.
Measured gains, regression checks and remaining costs: verification document.
The debug FPS/frame-time display is built; usage and rotation profiling protocol:
[Unity performance guidance](../unity/README.md#measuring-performance).
Creature/cinematic timing now shares a late draw clock (ADR-0501); native tile
neighbour queries and torture-approach flags are corrected (ADR-0500).
Active tick/presentation stalls remain measurable: research/level8-intro-motion.md.
Proposed parallelism milestones and determinism gates:
[parallelism design](plans/2026-10-17-parallelism-design.md).
Next interface scope selected by the owner (2026-10-17):
[Step6 HUD and controls preparation](plans/2026-10-17-classic-look-step6.md),
following ART_DIRECTION.md §13 and proposed ADR-0497. Step5 is not formally closed.
The Step6 shader/font feasibility pilot passed in isolated URP targets at 1024×768
and 1440p (tools/hud-probe/README.md); a live minimap tile layer is connected
on master with the native frame and its idle corner art. Creature,
trap and door markers, Heart/Guard Post reporting-distance outlines, possession
and Alarm rings, Guard Room rectangles, textured camera footprint, magnifier zoom
and primary click navigation are built. Remaining overlays, other corner controls
and owner comparison remain. HUD hiding/restoration is researched but unbuilt
(Q611; research/hud-visibility.md).
Native minimap raster
and overlay reading: research/minimap-rendering.md; Q197 and input research remain.

The Step6 merge passes 629 presentation tests, adapter compilation and the HUD
probe. Four Behavior tests also fail on pre-merge master bda4952e: the Level1
variable override, Goblin eating scenario, Level17 Harkan Call to Arms and
Level11A Drako bridge/attack scenarios. No Step6 behavior regression was found.

Use serial checks; isolated probes must not save or restart the owner's editor.
For master, open unity/DK2.Unity; check the retained classic worktree's branch
before using D:/DungeonKeeperRemake-classic/unity/DK2.Unity. Restart Play after
unity/sync-assemblies.ps1. Never commit local editor state (ProjectSettings/*,
Scenes/Dungeon.unity, URP globals). Room selection is by name in the
DungeonRunner Inspector (unity/README.md).

Owner checks still pending in Unity: claiming, eating and sleeping; the Imp
startup pose (ADR-0462); portal smoke, wall reinforcement and portal buildup
effects (ADR-0464, 0465, 0467); the Heart sparkles and opening sequence; rock
dust prominence (Q593); live Heart platform guards and Arch Pillar targets
(Q585, Q586); Level8's guard placement (Q589). Unbuilt: non-creature threat
flight (combat-fear.md §7), free-range and Prison chicken loops (Q584),
creature status flowers (the HUD). Reported graininess: inspect the Game view
scale before changing rendering (TRAPS.md). Crowd profiling limits are in
tools/classic-unity-probe/README.md.

## Next work

1. M6: Level 4's Imp losses (a median 5 a seed against DK2's none). They die on
   alarm runs to the (24,37) Guard Room after a gate party survives; the seeds
   that lose most claim the portal late (663–1437 against DK2's 787) because an
   Imp keeps reinforcing walls while a claim waits (seed 5's Imp 13, 398–1060;
   ours spends a median ~300 Imp-ticks in state 22 before tick 1100, DK2 102).
   Then the state 8
   residual (ours spends 52 of 61 ticks in state 8 where DK2 spends 27 of 58;
   state 8's routeless movement `0x00470C80` and `0x0048ECE0` are unread).
   Then Q567 and Q590's remainder. The oracle's captured-state mode is the tool
   for a function-level difference: next it needs an owner recording of the
   Level 4 playback with --dump-ticks (reverse-engineering/README.md).
2. Owner's standing priority (2026-10-10): the excess Level7 Imp deaths (Q582),
   not an accepted deviation; see combat-acquisition.md §1.
3. M12: the owner decides whether Q563 and Q574 close M12 or are narrowed
   first; the trap/door step resumes when digging matches DK2 better (Level 17's
   Prison still stalls in some seeds). Compare with every computer keeper running and
   on the record's gold. Do not infer an UNKNOWN rule from a name.
4. M7: Q513 and the owner observations in experiments/doors-and-traps.md.
5. Possession: the Unity first-person camera and input
   (plans/2026-10-12-possession-arms-build-plan.md); owner play questions are
   in possession-arms-and-inputs.md §4–5.
6. Asset PoC (ASSET_PIPELINE.md §10): the original Imp's Blender reference is
   built; the appearance target is the approved AI concepts (ADR-0398,
   ADR-0399, `art/manifests/creature/imp.yaml`). The Tripo candidate was
   accepted as the shape on 2026-10-07; a paid regeneration comes first
   (`docs/plans/2026-10-07-imp-image-to-3d-trial.md`). No rig or Unity work yet.
   Still open: the geometry-index and exact-frame export for all 35 Imp files.

After faithful M12, keep it as selectable Classic AI; explore a separate
Enhanced keeper using the same observation/command interface (KEEPER_AI.md
§12.7). Enhanced behavior is outside M12's fidelity target.

## Work in other worktrees

Run git worktree list first; another session may own these.

- build/imp-store-timing, fixes/imp-store-timing: unmerged store, work-order
  and mining completion candidates; campaign regressions block gameplay merge.
  Verdict and next experiment: research/imp-candidate-acceptance.md.
- build/imp-acceptance, fixes/imp-verified-integration: integrating the verified
  Imp corrections for master; campaign wins are diagnostic, not a fidelity veto.
  Creation evidence: research/imp-create-position.md. Direct chooser
  controls and modern fixtures: research/imp-priority-checks.md. The preceding
  work comparison and tile-input control remain research/imp-hatchery-work.md.
  Captured strike/claim boundary: research/imp-strike-choice-boundary.md.
  Action-stop mismatch and rejected hash-276 candidate: research/imp-dig-stop-clock.md.
  Q612 remains open; no new chooser fix is established. Gameplay is isolated on
  fixes/imp-dig-stop-clock; master remains hash 274.
  Packet fractional-position loss is traced and independently reviewed:
  research/imp-create-position.md. Comparison clocks and regenerated measurements:
  research/imp-observation-clocks.md. The opening-time tile control confirms a
  local early-claim contribution, while reinforcement moves in opposite
  directions across the two seeds:
  research/imp-hatchery-work.md#opening-time-input-control. Completed original
  chooser controls now isolate reservation-sensitive selection at that boundary:
  research/imp-hatchery-work.md#reservation-and-position-controls. The earlier
  work/release sequence now has 36 full native update pairs and a bounded modern
  continuation: research/imp-dig-sequence.md. Matching all sampled tile results
  retains the action-completion mismatch. A complete arrival-to-choice replay
  and accepted correction remain open; no new priority defect is established.
  ADR-0509 is merged; its evidence remains research/steering-velocity-axes.md.
  The rejected movement/arrival candidates and random-input research remain
  on fixes/imp-movement-arrival and research/imp-rest-choice. Do not merge those
  gameplay candidates. Heart visual acceptance remains Q585.

- build/classic-step6, classic-look/step6: unmerged presentation work with local
  changes; preserve it.
- Merged checkouts and two obsolete detached probes were removed on 2026-10-18.
  Three named `housekeeping 2026-10-18` stashes preserve the classic Unity edits,
  parked trap/door work and work-only probe. Recovery hashes and the verified
  unique-file archive are recorded in
  reverse-engineering/housekeeping-20261018/manifest.json (local, gitignored).

## Where to work

- src/DK2.Simulation owns game rules and mutable state; it is independent of
  DK2.AI and Unity. src/DK2.AI observes and emits commands. src/DK2.Harness
  runs headless scenarios; tests/DK2.AI.Tests and
  tests/DK2.Behavior.Tests cover keeper decisions and stock maps.
- docs/ARCHITECTURE.md gives the project boundaries and system order.
  docs/DECISIONS.md records design choices. docs/research/functions/ is the
  canonical knowledge base for each DKII.EXE function.
- The reference install is E:\GogGames\Dungeon Keeper 2. DK2_DIR overrides
  discovery; an invalid override means no install. Tests marked InstallFact
  or InstallTheory skip without game data; DK2_REQUIRE_INSTALL=1 makes a
  missing install fail (ADR-0100).
- The replay rig is reverse-engineering/dk2runs/level4-live1/rig/runset.py
  (local, not in the repo): Levels 1–4 over seeds; count deaths from
  `creature_died` events. Measure master and the branch from the same base.

## Run and verify

    dotnet build DK2.slnx -c Release
    dotnet test DK2.slnx -c Release --no-build
    dotnet build unity/DK2.Unity.Compile/DK2.Unity.Compile.csproj -c Release
    dotnet run --project src/DK2.Tools -c Release --no-build -- verification-ledger --check
    dotnet run --project src/DK2.Tools -c Release --no-build -- kb check
    dotnet run --project src/DK2.Tools -c Release --no-build -- coverage --check
    dotnet run --project src/DK2.Tools -c Release --no-build -- docs --check
    dotnet run --project src/DK2.Tools -c Release --no-build -- status --check

DK2.Oracle.Tests runs DKII.EXE's own functions only when DK2_UNICORN_DLL
names a unicorn.dll outside the repo, through Python 3 (py -3, or
DK2_PYTHON); without the DLL the function tests skip
(reverse-engineering/README.md, ADR-0481). Its table tests need only the
install.

Run dk2tools status after the tests when the plan's generated count changes,
and dk2tools verification-ledger after adding or removing a rule attribute.
Run dk2tools kb index after changing function records; never edit INDEX.md by
hand. Check the changed commit with git diff --check. A simulation state
change requires matching GameSimulation.HashVersion and
WorldSnapshot.FormatVersion, a new determinism pin, and a hash-versions.md
row. An observation-format change requires a schema bump.

The Unity compile project is outside DK2.slnx. Run
unity/sync-assemblies.ps1 after a change under src/ before an editor smoke
test. Scenario format is in scenarios/README.md; harness output goes under
build/scenario-runs/ and is ignored.

## Research and workspace notes

- Follow AGENTS.md's source order: DKII.EXE through Ghidra first for a
  behavior question, then shipped data and original-game experiments.
  The ghidra-queries skill and docs/RESEARCH-QUEUE.md describe Ghidra
  12.1.4/PyGhidra. The runner clones the read-only D:\Ghidra\DK2_RE
  project per run. Keep decompiler output under reverse-engineering/ or
  scratch, never in tracked files.
- Read a function's record before querying it. Put each new finding or
  correction in that record with a dated History line. Write behavioral
  evidence once in its research note; link from other documents. Distinguish
  CONFIRMED, STRONGLY INFERRED, HYPOTHESIS and UNKNOWN.
- An original capture's turn is one tick behind the game's turn
  (ADR-0119); timer stamps need no correction. The live-memory and save
  tools are indexed in reverse-engineering/README.md and
  navigation-captures.md.
- Check git status and git worktree list before editing: other sessions may
  be working concurrently. Stage named paths, not git add -A; keep original
  game files and Ghidra output out of commits. Never switch branches in the
  shared main checkout (TRAPS.md). If Git reports dubious ownership in this
  environment, use a per-command safe.directory setting.
- Documents and source have budgets enforced by dk2tools docs; AGENTS.md
  has the limits. Replace stale claims rather than adding dated amendments.
  PowerShell is available here; pwsh is not. If a local shell rewrites a
  UTF-8 document, preserve its existing BOM and line endings.
