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
  levels (docs/experiments/). M6 remains partial: the latest completed packet
  measurement is hash309 at `490b7d82` (2026-10-26; write-up
  D:\DK2Evidence\runs\packet-replay-hash309\README.md): all 80 runs valid, wins
  18/8/16/1 of 20 against hash305's 18/9/17/1; Level 1 seeds 3 and 7 now win and
  6 and 11 lose; Level 4 Imp deaths median 14.5 against 7, with Imp flees, alarms
  and withdrawals all up and no-destination endings not. Isolating sweeps at
  hashes 306, 307 and 308 (D:\DK2Evidence\runs\packet-replay-hash30{6,7,8}\README.md):
  307, 308 and 309 are byte-identical, so ADR-0544 and ADR-0545 change nothing in
  these replays; the death median already reads 15 at hash306 (hero-party order),
  and a permutation test puts the 7 -> 15 shift at p ~ 0.15, inside seed noise.
  ADR-0543 (hash307) raised withdrawals (median 85 -> 112.5), untested for
  significance. The previous measurement is
  [hash305](experiments/2026-10-25-packet-replay-hash305/README.md), with
  hash303/P2 comparisons and mapping limits. The historical [hash303 failure analysis](experiments/2026-10-25-packet-replay-hash303/analysis.md)
  separates observed blockers from open causes; the current report checks the
  persistent Level 1 Hatchery refusal and records the changed outcomes.
  The [decision parity audit](research/decision-parity-audit.md) verifies bounded
  chooser/RNG and combat checks, but identifies replay-drop limits (the NonFighter alarm staging has no observable gap, STRONGLY INFERRED);
  the [Imp selection study](research/imp-selection-evidence.md) adds three live
  matched choices and 118 binary ordering controls. Full parity remains unestablished.
  The [navigation controls](research/navigation-partial-rebuild.md#6-level-1-same-input-navigation-controls-2026-10-25)
  separate fresh-build reconstruction from edit history. The linked actual-history
  follow-up matches55 native rebuilds and observes one modern write-order reversal;
  its effect on losses remains open. The [captured Level4 withdrawal](research/decision-parity-audit.md#level4-captured-withdrawal-and-room-order-hash304)
  isolates a room-order mismatch with identical RNG; supplying native order matches
  the captured destination and route window. This is now built for the combat
  room cache in [hash/save305](research/room-list.md#persistent-player-list-order-hash305)
  (ADR-0541), including construction/capture/split order and snapshot state.
  The completed measurement is linked above; broader campaign-loss causation
  and combat/scheduling parity remain open.
- The Imp Secret Door job path is built (ADR-0536, hash/save 304), including
  per-keeper entry order and native reservation retention after interruptions.
  Scope, focused verification and pending original-game acceptance:
  [Secret Door work](research/imp-task-objects-and-walks.md#2-kind-11-is-the-secret-door-0x0046cb80).
  Hash304 has no separate full sweep; the hash305 measurement includes both changes.
- The fight approach is DK2's state 8, a walk resuming 199 (ADR-0483), and no
  combat experience is paid on that walk. A forced target no longer reaches a
  corpse (Q598). Room ticks run between the creature pass and the players, as
  DK2's do, with the hated-company rule (ADR-0479); the store sweep runs on
  DK2's turn (Q596). The stun counts from its first call (ADR-0480). Only a
  keeper's creatures run the needs, need-anger and mood choosers; heroes skip
  them (ADR-0485, Q599). Store gold is built (ADR-0534, hash 299): every
  Treasury and unruined Heart draws the
  store sparkle each turn and the Hand lifts a store pile whole. Those draws
  move every replay and win rig. The Level 1–4 packet sweep is linked above;
  the level2/level4 live rigs, L11A and L17 have not been rerun since hash 298.
  Hash/save 303 withdraws portal crystal collision (ADR-0527), retaining
  hash 302's store gold/UI state (research/hash-versions.md).
  Initial chest headings remain Q644; Level2's reported
  water shapes under fog await a native visual comparison (Q643).
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
  The Step6 branch (classic-look/step6, presentation only, no hash change) also
  draws DK2's additive environment-map pass over every 0x40 material and animates
  the loose gem as state 50 does (float, spin, effect 297 each second; that a
  dropped gem stays in state 50 is LIKELY): kmf-materials.md §8, objects.md §8.
  Q622 (Lord corpse: no missing body found, the armour shine was the gap),
  Q623 and Q637 (camera frame and v sign unconfirmed) await the owner.
- Hero-party ordering is corrected (ADR-0542, hash/save 306); opening Dwarf
  reproduction, cross-level scope and remaining comparison limits:
  [hero-party walk](research/hero-party-walk.md#7-opening-party-order-2026-10-25-adr-0542).
- A withdrawal whose room pick fails (no rooms, or a drawn bridge) ends with no
  further draw (ADR-0544, hash/save 308), and step 5 spends the away stage's draws
  for a staged live creature or trap, each caller staging what DK2 does (ADR-0545,
  hash/save 309). Measured together with 306–307 at 490b7d82 (below).
- Level alliances (VARIABLES id 66) are applied at load and saved (ADR-0546); the game mode
  and the verdict recursion are built (ADR-0547, hash/save 310; Q359, Q576). No packet or
  win rig has been run since hash 309. Q652: Level 11A's Drako no longer calls at Morgana's
  front, a known regression.
- One creature's whole update runs before the next one's, newest first (ADR-0554, hash/save 315):
  `CreatureUpdateSystem` runs the fight, needs, anger, state handlers, mover and an Imp's handler
  per creature; the payday clock runs before the creatures. Non-Imp walks take their first step
  on the tick they start in 34% (Level 2) and 26% (Level 4) of cases, native 40% and 28%. Walks
  from state 173 still start late. Differences left and measurements:
  [creature-update-order.md](research/creature-update-order.md).
- The combat states' handlers run in the creature's dispatcher turn, after the needs routine, so a
  creature the scan engages runs its fight state in that update (ADR-0555, hash/save 316):
  `CombatSystem.States`, then the trap reveal. Level 4's Imps flee straight from a walk, as native
  Imps do; Level 2's Imp walk starts are 852 (native 871). Still open (Q665): Level 4's Imps flee
  far more often than native (236 entries into 201-203 against 41) and start few walks from state
  87 (41 against 809). `dk2tools replay --resync` continues a replay from a DK2 capture's tick
  (ADR-0556, ADR-0558, [resync-replay.md](research/resync-replay.md)), copying DK2's doors, traps,
  crates, manufacture list and Imp crate trips; Level 4 seed 1 resynced at 2340 starts jobs to 3400
  as native does (mine 110/112, claim 66/57, reinforce 83/90, native/ours) and strikes the east
  seam (29,34) at 2482 against 2476. A crate is paid for at the cost in the manufacture ring's
  slot 0, not the head's (ADR-0557, hash/save 317, Q618): Level 4's east seam group now finishes
  at 2810-3144 in seeds 1-5 (was 3098-3516; native 2815 and 2988). A stunned creature's fear
  piles up and is handed over on its first standing tick (ADR-0559, hash/save 318); Level 4 seed 1
  loses 45 Imps with and without it (32 at hash 316, one seed, not attributed to ADR-0557).
  The 30-tick party offset in the death window is drift (the Portal claim at 2059 against 2030),
  not a trigger rule. The resync copies the trigger banks and every creature and pairs them
  itself (ADR-0560): seed 1 at 3520 has parties 5 and 7 at 3829 and 4189 (native 3830, 4190), flag
  11 at 4330 (native 4378) and the hero door falling at 5314 (native about 5313), but 4 Imp
  deaths against 0, three to party 5's Guard, which survives the fight at (15-17,31-34) where
  DK2's party 5 is down by 3960. In that run the Guard survived because ours' Thief stood nearer
  and drew the Elves (one fight, consistent with nearest-first). Resynced at 3790 (one seed),
  ours reproduces the window apart from one Imp (Imp 26 walks to party 5's gate) and a post-Guard
  switch about 40 ticks late: parties on time, party 5 beaten, door at 5310 (native about 5313)
  ([resync-replay.md](research/resync-replay.md), "The Guard fight"). The full replay's 45 deaths
  look like drift, not tested over the same window. Seed 1 (one seed): Imp levels are alike (native 34 level-ups to 3520, ours 35). Ours has 15
  Imps against 18 because three Create Imp casts are refused, one by a 79-mana margin. The first
  difference is the first dig (strike 155 against 138), which closes by about 350. The claim and
  mana deficit opens at about 550, where ours digs a fresh mark at (24,46) ahead of older ones.
  That order follows from the north seam (31,54) falling at 324 against 305 with other Imps at its
  face, each step the faithful rule (STRONGLY INFERRED, one seed;
  [creature-update-order.md](research/creature-update-order.md), "Where the drift starts"). Next:
  why other Imps stood at the face when (31,54) fell (the first-dig lag and RNG, HYPOTHESIS), Imp
  26's walk, the late post-Guard switch, mining over reinforcing in 3000-4000, deaths across seeds. The resync does not copy
  a trigger record's active bit and repeat byte or the hero job triple, so resync before the
  creature triggers that matter fire. Some trigger timer stamps differ from DK2's by one tick,
  not investigated. The win and packet rigs have not been rerun at hash 318.
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
effect (ADR-0476) and Dirt Path foot dust (ADR-0477) are built. The complete
researched arrival roster, including mercenaries, is in
[creature-arrival-effects.md](research/creature-arrival-effects.md).
Visual acceptance is tracked in research/classic-lighting-verification.md. Step 5's
base camera, lighting/fog and follow-up fidelity corrections are on master.
Milestones and remaining gates: plans/2026-10-14-classic-look-step5.md.
Research: research/classic-lighting.md; current visual acceptance, standalone
material verification and performance limits: research/classic-lighting-verification.md.
The owner accepted the current appearance, including restored Heart arches,
wall caps, frame-independent torches and standalone rack transparency.
Matched-original startup-smoke and Levels7/8 comparisons remain pending.

The saved Dungeon scene uses the owner's Native Initial Ambient setting (on);
the brighter preview remains available with it off. +/− controls native ambient.
Discovery schema/cadence limits are in
research/fog-reveal-sources.md; animated silhouettes remain under ADR-0482.
Scripted camera paths are built separately (ADR-0496; research/camera-paths.md).
Step6 ordinary camera controls now follow native packets (ADR-0531); matched
recording measurements, crop correction and verification limits: research/classic-camera.md §6–8.
Room border flames are built (ADR-0524, research/room-border-effects.md); claim-mist
bleaching is mitigated, its cause open (Q609).
Wet/bloody footprints are built; native rules and pending visual acceptance: research/creature-footprints.md (Q610, 2026-10-20).
Creature asset keys are cached per definition/action; transparent terrain uses
retained stable-sort buffers and reuses meshes across changes in draw order.
Measured gains, regression checks and remaining costs: verification document.
Arrival rendering shares vertex uploads, culls offscreen triangles and retains
index capacity. The performance target remains unmet; buffer-churn measurements
and residual first-use/worker costs: [arrival performance](research/portal-arrival-performance.md).
Acceptance and remaining coverage: [performance policy](PERFORMANCE.md).
The debug FPS/frame-time display is built; usage and rotation profiling protocol:
[Unity performance guidance](../unity/README.md#measuring-performance).
Creature/cinematic timing now shares a late draw clock (ADR-0501); native tile
neighbour queries and torture-approach flags are corrected (ADR-0500).
Portal arrival transparency and animated beams are corrected. Red mist timing,
failed previews and the withdrawn keeper-colour report (Q619 closed):
[portal buildup](research/portals-interval.md#portal-buildup-built-adr-0467).
Mist now uses the native default additive blend and joins the terrain depth stream:
[rendering correction](research/room-construction-effects.md#sprite-blend-and-shared-terrain-ordering-2026-10-23).
Room/Portal capture uses the native full-slab profile (ADR-0529), replacing the
thin edge ring: [capture comparison](research/room-construction-effects.md#62-portal-capture-comparison-shot02310240).
Creature promotions now request the original sparkle effect, including imp work
levels and scripted grants: [promotion sparkles](research/creature-levels.md#promotion-sparkles-2026-10-23).
Portal crystal collision is withdrawn (ADR-0527, Q642 closed). The original
meshes remain; movement and its observed near-hole limits are recorded in
[the rollback note](research/portal-crystal-collision.md).
Level1's scripted red tutorial tile guidance is built (ADR-0528):
[native slots and pulse](research/tag-highlight.md#scripted-tutorial-red-flash).
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
and primary click navigation are built. Remaining overlays
and owner comparison remain. The Step6 branch now implements camera/letterbox
HUD hiding, restoration and input blocking, plus the native heading-dependent
north marker, camera-ribbon disc containment and zoomed Heart direction trail.
The branch also has original Hand/dig/pickup sprites, a terrain-deformed hover cage per tool mode (blue over dig tiles), a placement cage with tool icon and price (room, sell, door, trap, keeper spell, Query), the held-item icons beside the hand with the disposal bin over the Heart or Portal centre, the red hover tint and the gold `_HIGHLIGHT` texture (store sparkle and lifting store gold not built: research/gold-hover-and-sparkle.md §6), delayed localized names and a top status strip with door, trap, dead-body (unreachable through the Hand pick, Q634) and Casino/Prison fixture status, and DK2's two-stage pick (triangle hit, centre, 3×3 sweep, projected-sphere circle, object ownership arm; objects-only PAGE UP key and terrain occlusion not built; Q636 for rival doors and traps), the special creature name +0x895 (Lord of the Land's level name and the secret-door "Enemy Wall" arm not built). Status flowers have DK2's size, Leaving/S-Gem and Imprison icons, the removal countdown and the torture lift (Q635). Evidence and limits:
research/hand-cursor-rendering.md, research/placement-cursor.md, research/world-hover-text.md and research/status-flowers.md;
live gold/mana, separate rates and the Heart health ring now occupy the original
resource region, with status text to its right. The Step6 branch integration
adds gold withdrawal, payday/alarm/flash state, original personal names and blood
groups, G/H/M and corner controls, campaign Options pause, full-map terrain and
navigation, creature query/follow, bottom choices (slot recharge, manufacture and research sectors: research/panel-indicators.md), message tabs with the framed message panel, its scrollbar and the six indicator boxes (research/message-tabs.md; hash/save 297, ADR-0532), Shift-click tab pinning with two side-by-side panels (research/tab-alerts.md §3), Reaper state, room cycling
and all four creature overview modes. Canonical scope and remaining gates:
plans/2026-10-17-classic-look-step6.md. Tutorial messages, the first Goblin
introduction and script-forced flowers are built (ADR-0535, hash/save 302;
research/message-tabs.md §10). Matched visual acceptance is pending (2026-10-24).
Hash/save 289 adds native identity and
last-payday state; ADR-0521 documents initial constructor RNG ordering limits.
Campaign result suppression and scripted GUI restoration are built; Q611's
remaining continuation boundary and original-game comparison are in
research/hud-visibility.md (Q648/Q649).
Native minimap raster
and overlay reading: research/minimap-rendering.md; Q197 and input research remain.

Current verification (2026-10-25, ADR-0542): the combined solution and enabled
native CPU-oracle checks pass 6,562 cases with no failures; seven capture-dependent
checks skip. This includes 3,795 Simulation and 1,084 Presentation cases;
the Unity adapter compiles with zero warnings/errors. Logs:
`D:/DungeonKeeperRemake/build/dwarf-trace/` (combined-build, combined-tests, combined-adapter).
Prison chicken passes with master's central (5,4) drop. The branch's
earlier failure is master's edge-route symptom, whose native cause is still
unresolved (research/imp-fidelity-integration.md). Our side, from runs on
2026-10-19: at af4768e7 both prisoners at (4,4)/(5,4) get the two-point route
(7.5,2.5) -> (7.5,4.5) towards the rim tile (7,4), outside ADR-0139's fenced
interior, and PrisonSystem emits `prisoner_freed ... at=(7,2) reason=cell_gone`
on ticks 295 and 299. At 1f3805e0 creature 1 stood at (6,4), within reach, and
ate on 281 without walking, so the edge walk never ran. What DK2 does with food
on the rim is unknown. The HUD probe was not rerun
after the merge.

Use serial checks; isolated probes must not save or restart the owner's editor.
For master, open unity/DK2.Unity; check the retained classic worktree's branch
before using D:/DungeonKeeperRemake-classic/unity/DK2.Unity. Restart Play after
unity/sync-assemblies.ps1. Never commit local editor state (ProjectSettings/*,
Scenes/Dungeon.unity, URP globals). Room selection is available through the HUD;
the DungeonRunner Inspector remains available for debugging (unity/README.md).

Owner checks still pending in Unity: claiming, eating and sleeping; the Imp
startup pose (ADR-0462); portal smoke, wall reinforcement and portal buildup
effects (ADR-0464, 0465, 0467); the Heart sparkles and opening sequence; rock
dust prominence (Q593); live Heart platform guards and Arch Pillar targets
(Q585, Q586); Level8's guard placement (Q589). Unbuilt: non-creature threat
flight (combat-fear.md §7), free-range and Prison chicken loops (Q584),
the remaining flower gaps (research/status-flowers.md §4). Reported graininess: inspect the Game view
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
6. M10 front end: DK2's menus, campaign map and progression are read from the
   exe (`research/front-end-menus.md`, `research/campaign-map-and-progression.md`,
   `research/front-end-assets.md`); F1 storage/image plumbing (ADR-0538), F2 front-end scenery/camera sessions, F3 menu shells (ADR-0539), F4 campaign map/briefing and the F7 movie decoder/playback proof of concept are built. F5 stages A (briefing Go launches the level, verdict writes the profile once, Space leaves, statistics page 26 and map return) and B (level-end counters ADR-0548 hash 311, statistics rows laid out as DK2's (`0x0053CAF0`, `0x0053D270`) with DK2's scroll bar (`FrontEndScrollBar`; no mouse wheel, DK2's class reads none) and the objective images of `0x0053A580` on page 26 and the briefing (`FrontEndBriefingImages`; `research/level-statistics.md` §5), keeper rank, Total Evil Rating, hi-score name entry and `HiScores.dat`) are built, as are C (transfer creature arrival, specials 129/140, hash 311; the hand's targeting mode for 127/140 produces `TargetId`; the spells panel lists the Libraries' specials and routes 127/140 into it; the packet replay reads action 88's target word) D (post-win cutscenes, title-card and speech placeholders) and the finale (ADR-0549, hash 312: Reaper, win screen with its reward tablet (`RewardTablet`), Esc continue/reveal, celebration; `research/campaign-win-sequence.md`); all await a Unity play test. Today's Top Keepers (Extras, page 18) is built. Scope, remaining play checks and import/decoder limits are in `plans/2026-10-23-front-end.md`.
7. Asset PoC (ASSET_PIPELINE.md §10): the original Imp's Blender reference is
   built; the appearance target is the approved AI concepts (ADR-0398,
   ADR-0399, `art/manifests/creature/imp.yaml`). The Tripo candidate was
   accepted as the shape on 2026-10-07; a paid regeneration comes first
   (`docs/plans/2026-10-07-imp-image-to-3d-trial.md`). No rig or Unity work yet.
   Still open: the geometry-index and exact-frame export for all 35 Imp files.

8. Autonomous playtesting (ADR-0552, `plans/2026-10-27-autonomous-playtesting.md`): Stage A,
   the test-controller hook in `DK2.Harness`, Stage B's harness half (input journal,
   `replay`, `watch`) and Stage C (legal-action fuzzer, coverage, progress and the snapshot
   check; `src/DK2.Harness/README.md`) are built. Stage D (ADR-0553,
   `plans/2026-10-27-stage-d-unity-input.md`) Phase 1 is built: Unity writes `frames.journal`
   (`FrameInputJournal`) and `input.journal` when `DK2_INPUT_JOURNAL` names a folder
   (`unity/README.md`); it has not been run in the editor. Next: the owner plays three to five
   journaled baseline sessions (to `D:\DK2Evidence\runs\unity-input-<date>\`), and Phase 1b
   lets the harness replay a Unity journal; Phases 2-5 (input mapping into Presentation, the
   Input System ADR, migration, rendered-HUD automation) are not started. Failure bundles hold no
   snapshots, no DK2-behaviour invariant exists (each needs a cited source), and the fuzzer
   plays no specials or objects and tags nothing under fog.

After faithful M12, keep it as selectable Classic AI; explore a separate
Enhanced keeper using the same observation/command interface (KEEPER_AI.md
§12.7). Enhanced behavior is outside M12's fidelity target.

## Work in other worktrees

Run git worktree list first; another session may own these.

- build/imp-store-timing, fixes/imp-store-timing: archived mining-clock candidate,
  superseded by ADR-0514's stored clock (research/animation-clock.md).
- build/imp-acceptance retains ignored native captures and experimental probes.
  ADR-0512 integrates eight supported Imp corrections at hash/save 280; ADR-0513
  zeroes velocity on route requests and stops (281); ADR-0514 stores the dig and
  mining animation clock (282). Do not delete its ignored captures.
  Q612 remains open; no new chooser preference or reinforcement timeout is
  established. Direct chooser controls: research/imp-priority-checks.md.
  Work/claim controls: research/imp-hatchery-work.md. Complete native work pairs:
  research/imp-dig-sequence.md. Next: matched arrival-to-choice inputs, and
  moving the other actions' closed-form holds onto the stored clock. Heart visual acceptance remains Q585.

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
