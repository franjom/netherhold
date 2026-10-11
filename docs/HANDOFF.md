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
  levels (docs/experiments/). M6 remains partial: original packet replays run,
  with the remaining timing differences in Q444 and navigation-captures.md. The
  seed spread of the Level 1–3 replays against DK2's own seeds (2026-10-06) is in
  experiments/2026-10-06-packet-replay-seeds/; the open difference is Q567.
- M7 remains partial: combat, hero parties, door contact and the door fight
  (ADR-0318), hero gates (ADR-0320), every shipped trap (ADR-0321, ADR-0331) with
  the Boulder's rolling object (ADR-0333), the Jack In The Box's Jack (ADR-0337) and
  the reveal (ADR-0338), the Guard Post (ADR-0330), trap upkeep and damage (ADR-0326)
  and creatures fighting a revealed trap (scan D, ADR-0340) run; disarming is deferred under
  ADR-0329's scoped inference (Q455); shot heights go to M9 and the Boulder's slap to object slapping
  (ADR-0341). Left: Q513 and the owner observations pending in
  experiments/doors-and-traps.md.
- Keeper spells (ADR-0350, accepted 2026-10-04): Create Gold, Tremor, Turncoat,
  Summon Horny, OneShotReaper and Possession's simulation side are built, and Sight
  of Evil casts with an empty effect (ADR-0352), its region presentation (Q194). Research
  grants every upgrade through ResearchTreeRules (KeeperSpellUpgradeTests); the
  upgraded effects are the bonus data and Possession's 70 % upkeep. Left unbuilt:
  Horny's idle AI, the possessed action arms in Q535/Q536, the third cast path
  (action 56 only, Q537) and the held-creature regeneration in Q152.
  Evidence: research/keeper-spell-*.md.
- M12 remains partial. DK2.AI.KeeperAI is an input source that emits
  PlayerCommands through a detached, serialisable observation (schema 19).
  The proactive task machine (tasks 0–7) digs, expands, builds rooms, manages
  gold and imps, and crosses water or lava with bridges. Every message type its
  walk reads is produced where ours has the event and consumed (ADR-0353): room
  messages 41–44 and 32–35 make it build, expand or re-choose a room (ADR-0327,
  ADR-0335), 17/18 make it pay, feed, move or dispose of a creature, and 12, 34
  and 47 start a threat response; the rescue, job-room and Graveyard producers post theirs (ADR-0412–0414). Reactive combat
  messages, threat response, wounded retreat, the battle step, Call to Arms,
  Thunderbolt, Heal, Chicken and Inferno run through simulation commands, and the
  gold step's Create Gold and step 14's Sight of Evil are cast where DK2's are (ADR-0352).
  The attack planner selects a target, tunnels, stages and calls its army;
  step 9 sends creatures to Training or a Combat Pit. See KEEPER_AI.md §12.8
  and the canonical computer-keeper research notes for exact rules.
- Unaided modern scenarios show Harkan moving creatures onto its Training
  Room's floor on Level 17, Nemesis preparing an attack on Level 18, and
  Drako bridging Level 11A's lava and calling its
  army toward Morgana, and Harkan on Level 17 growing its Treasury after its
  imps find no store (Q518). Two idle captures of DK2's own keepers on the same
  maps (Level 17, 2026-10-05; Level 11A, 2026-10-06) are compared in
  computer-keeper-observations.md, whose last section is the M12 acceptance
  table: every verb agrees in kind; the open timing differences are Q509's
  re-lift delay, Q555 and Q563.
- Goal 7's writer/editor API is complete and verified (ADR-0346,
  `src/DK2.Data/README.md`); the editor UI and playability validation are
  future work.
- The creature state machine dispatches category hooks. Combat enter and
  leave are partially built (ADR-0319, combat-category-hooks.md);
  their unmodelled words and the update hook are listed there. The Casino,
  Torture and Manufacture leave hooks and the stored maker count are built
  (ADR-0322, leave-hook-bodies.md); the Sleeping leave is believed presentation only.
  Consult CategoryHooks.Slots for the current status of every hook.

## Classic look, steps 3–4 (worktree D:/DungeonKeeperRemake-classic)

Plan: docs/plans/2026-10-09-classic-look.md. Step 3 is merged to master at 6005ff9f.
Step 4 is underway on classic-look/step4. The owner
compares DK2 and Unity on campaign Level8 ("Snapdragon") screenshot by screenshot.
Open D:/DungeonKeeperRemake-classic/unity/DK2.Unity. Assemblies are synced and the adapter
compiles. Never commit editor state (ProjectSettings/*, Scenes/Dungeon.unity, URP globals).
Commits: b58eaa66 transparency; 4033eb24 gate/Heart pieces; bc806cc2 room simulation;
84a450da Prison door/object turning; 72cfe356 fallen-keeper imp regression.
Follow-up commits: 363aba56 victim attachment/device steering; e3c67c87 doors/traps.
4316bc97 enables Forward+ torch lighting; 2dc90d18 supplies campaign party scripts.
cd6fbd45 corrects obstacle query flags and grounded water speed.
9630abf4 corrects translucent portal triangle order; fb5e1980 draws locked-door chains.
3c1c6604 restores the imp's nearby-room alarm retreat.

Presentation includes object tilts/turning, Torture wheels and attached victim poses,
the corrected Prison door mesh facing, KMF transparency, Hero Gate floors and 3x1 wall
faces, ruined Heart floors, and placed doors/traps with settled door poses (ADR-0421),
locked-door chains (ADR-0426) and depth-sorted translucent portal pillars (ADR-0427).
Torch meshes now receive their wall-mount offset separately from the light;
owner visual check pending (2026-10-11), see wall-torches.md §4.
Evidence: object-drawing.md, furniture-placement.md, kmf-materials.md,
structure-drawing.md and room-floors-and-liquids.md. Remaining owner checks: screenshot alignment, tag-drag tools and
corners (tag-highlight.md §4, HYPOTHESIS), blue overlay and pillar height.

Simulation: placed captives/victims, attackability, walled room interiors, Prison delivery,
Dante's free-cast placement and Q581 wall-machine positions are built under ADR-0419.
ADR-0420 adds device attachment. ADR-0423 corrects ordinary obstacle query order and
retained flags; ADR-0424 applies grounded water slowdown. ADR-0425 restores an outmatched
imp's nearby-room alarm retreat and Call to Arms suppression. ADR-0428 corrects hatchery
laying and retains individual egg/chicken coordinates. ADR-0429 corrects newest-first
combat order; Level7 Imp deaths remain unresolved (Q582). ADR-0430 restores waiting
heroes' sub-tile movement, including Level8's hatchery Guard. Hash/save version is 221.
All 60 Level 1–3 replays validate; bounded-run wins are 17/5/16 versus 20/5/16 on 220.
These are modern regression measurements, not
proof of original timing parity. Campaign startup now spawns the northern Lord's
six-member party through existing scripts (ADR-0422). The merged master Release suite
passes all 4678 tests with no skips; Unity assemblies and adapter checks passed for step 3.

Replay drivers recovered from C:/Users/franj/AppData/Local/Temp/claude/d--DungeonKeeperRemake/
d869276a-99ca-491a-9cf5-0342488c1277/scratchpad. Measured baseline/current artifacts:
D:/DungeonKeeperRemake/build/classic-step3-replays/{baseline,final,wheel-followup,collision-water-followup,imp-alarm-followup,chicken-placement-followup,giant-imp-followup,level8-wait-followup}.
These are local artifacts. Follow-up: docs/plans/2026-10-10-torture-wheel-followup.md.
Step 4 adds fractional KMF sampling, per-instance Unity meshes and
previous/current position interpolation (ADR-0431). Existing idle, ordinary walk,
run, Imp dig/claim/reinforce, sleep, die and torture mappings can play, with
ordinary quantized phases and separate Start/End meshes. Hatchery eating retains
action 26's first-cycle wait and restarts playback from its entry stamp (ADR-0432).
Ordinary travel rates follow body velocity and the authored movement scale;
prison eating, other action restarts and exceptional rate overrides remain pending.
Chicken Walk/Eat/Scared playback and position/heading interpolation are built;
combat routes play walk/run and melee swings play once (ADR-0436).
See kmf-animation-playback.md and plans/2026-10-10-classic-look-step4.md.
The owner animation comparison gate and Unity crowd upload profiling are outstanding.
ADR-0433 restores followers' sub-tile walks around stationary leaders; grounded
liquid height uses the presentation approximation in room-floors-and-liquids.md.
Hash/save version is 225. Owner Level8 portal comparison pending (2026-10-11).
Imps face the task tile before digging, mining, reinforcing or repairing walls
(ADR-0435); owner reinforcement comparison pending.
Prisoner, room-job and idle-return travel play movement clips; see kmf-animation-playback.md §5.
Grounded bridge placement uses the deck height; see room-floors-and-liquids.md.
The playback/bridge follow-up passes 167 presentation tests and the Unity compile.
Torch meshes now share the wall lattice; see wall-torches.md §4.
The torch follow-up passes 172 presentation tests and the Unity compile; owner view pending (2026-10-11).
Last full-suite verification: 4803 tests pass without skips; Unity assemblies synced,
adapter compile and independent review pass.
Level 1–3 replay results are recorded in ADR-0432–0435; artifacts are under
D:/DungeonKeeperRemake/build/classic-step3-replays/: eating-action-followup,
portal-follow-followup, chicken-movement-final and imp-facing-final.
Creature status flowers are still part of the unbuilt HUD work. Creature separation
already runs with DK2's soft repulsion; coincident centres and arrival can still overlap.
Wheel sliding preserves arrival/escape exceptions; it is not rigid mesh collision.
The owner verified the blue portal pillars after reloading (2026-10-10).
Remaining owner checks: restart Play mode, compare chicken motion, Giant melee swings
and locked-door chains, inspect Level8's hatchery Guard visibility and portal movement
(hero-party-walk.md §1), and compare torch illumination
at the same camera positions. The per-object light limit is confirmed; its responsibility
for the reported camera-dependent darkness remains strongly inferred (unity/README.md).
Imp casualty/timing parity remains open (Q582, combat-acquisition.md §1);
non-creature threat flight remains unbuilt (combat-fear.md §7).
Mistress crossing the visible Heart needs an arrival/escape comparison (Q585).
Owned Hatchery chickens select points, turn, walk and react (ADR-0434);
free-range and Prison loops remain unbuilt (Q584). Chicken footstep audio
remains unbuilt; see hatchery-roaming.md.
The owner verified a Level8 Salamander walking on lava (2026-10-10);
see imp-idle-and-wandering.md §4 for the crossing regressions.
The reported graininess at both camera distances has no confirmed resolution;
recheck the Unity Game view scale before changing rendering (TRAPS.md).

## Next work

Owner's first priority (2026-10-10): resolve the excess Level7 Imp deaths (Q582),
researched in parallel with the authorized Step 4 work. Step 3 is merged;
this combat/escape discrepancy remains open, not an accepted fidelity deviation.
The follow-up trace ruled out velocity reset alone as the reproduced death's cure;
the original encounter capture is still needed (combat-acquisition.md §1).

1. M12: the Q542–Q544 producers are built (ADR-0412–0414), and their leftovers (ADR-0415–0418:
   the Casino party, the Library finder, the bedless rescue, the lying corpse, Q578); not
   `0x004BA420`'s room messages other than the sale's. The research move, prisoner/special
   decisions, trap/door decisions and personality loader also remain
   (KEEPER_AI.md §12.7). Do not infer an UNKNOWN rule from a name.
2. M12: the same-map comparison is done (two captures, the acceptance table at
   the end of computer-keeper-observations.md). The owner decides whether the
   reported differences (Q509's re-lift delay, Q555's Prison timing, Q563's
   defence and call timing, Q574's slap-driven spread on Level 11A) close M12 or are narrowed first. Compare with every
   computer keeper running and on the record's gold, as DK2 runs the level.
3. M6 and M7: use the plan's run order and the open-question rows for the
   remaining replay, combat, hero, trap and door work. Avoid reproducing their
   detailed backlogs here.
4. M6: the owner records Level 4 live (planned 2026-10-07), elevated:
   `reverse-engineering\dk2runs\level2-seeds\run.ps1 -Live -Level level4
   -Packet l4live -Map 50x64 -LastTick 30000 -OutName level4-live1`. The
   2026-10-06 attempt was deleted: the recorder stopped at tick 3069 when the
   tick read 3068 next. If that recurs without a save load, find what steps
   DK2's tick back before trusting the recorder's reload check.
5. Asset PoC (ASSET_PIPELINE.md §10): the original Imp's Blender reference is
   built (`tools/blender/imp-reference.ps1`, Blender 5.2.2). The procedural
   modern blockouts are paused at v015 (unapproved). The appearance target is
   the approved AI concepts (ADR-0398, ADR-0399, `art/manifests/creature/imp.yaml`).
   The first image-to-3D candidate (Tripo web, free plan, `candidates/t1-tripo-web/`)
   was accepted by the owner as the shape on 2026-10-07; a paid regeneration comes first
   (`docs/plans/2026-10-07-imp-image-to-3d-trial.md`).
   No rig or Unity work yet. Still open: the geometry-index and exact-frame
   export for all 35 Imp files.

After faithful M12, keep it as selectable Classic AI; explore a separate
Enhanced keeper using the same observation/command interface (KEEPER_AI.md
§12.7). Enhanced behavior is outside M12's fidelity target.

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

## Run and verify

    dotnet build DK2.slnx -c Release
    dotnet test DK2.slnx -c Release --no-build
    dotnet build unity/DK2.Unity.Compile/DK2.Unity.Compile.csproj -c Release
    dotnet run --project src/DK2.Tools -c Release --no-build -- verification-ledger --check
    dotnet run --project src/DK2.Tools -c Release --no-build -- kb check
    dotnet run --project src/DK2.Tools -c Release --no-build -- coverage --check
    dotnet run --project src/DK2.Tools -c Release --no-build -- docs --check
    dotnet run --project src/DK2.Tools -c Release --no-build -- status --check

Run dk2tools status after the tests when the plan's generated count changes.
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
  game files and Ghidra output out of commits. If Git reports dubious
  ownership in this environment, use a per-command safe.directory setting.
- Documents and source have budgets enforced by dk2tools docs; AGENTS.md
  has the limits. Replace stale claims rather than adding dated amendments.
  PowerShell is available here; pwsh is not. If a local shell rewrites a
  UTF-8 document, preserve its existing BOM and line endings.
