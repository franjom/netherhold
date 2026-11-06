---
systems: [creatures, rooms, imps-tile-work, simulation-core]
---
# Q712: early draw continuation after turn 1403

## Scope and evidence

Level 5 seed1, completed-turn1400 restore, continuing the
[Egg collision control](neutral-ambient-effect.md#built-at431-adr-0699).
This is a bounded diagnostic restore, not a general adapter or a Heart-outcome test.
Native capture and draw log are unchanged from the earlier study. New inputs,
runs, diagnostic source and verification live outside the clone:
`F:/DK2Evidence/studies/q712-draw1404--2026-11-06--001`.
The frozen hash/save 432 diagnostic build is archived under
`F:/DK2Evidence/builds/q712-hatchery-pillars-h432--2026-11-06--001`.
All new restore fields come from completed1400; no later pose or completion tick
is fed into the probe. `command.json` and `env.json` in each run name every input.
Runs through1420 retain raw outputs after their first mismatch; those suffixes
are excluded from parity claims.

## Restore boundaries before the rule defect

Turn1404 already agrees without another restore change: 56 ordered sites and
pre-draw seeds match across1401-1404. The first missing native draw at1405 is
`ImpUtils.cpp:484`, the unconditional random4 in `0x00468D10`, from Imp454.
The generic restore had reissued its claim as state6/resume15; native completed1400
has state16/resume1, original state entry1396, action4. `0x00465CD0` holds while
the action runs; `0x004974C0` applies claim work on each running-action poll,
then stops the animation when the tile is no longer claimable.

The `claim454-control` restores that captured state, original entry and heading,
retains the existing claim target18,10, and schedules its already-running handler
for the next poll (`TimerTicks=1`, not a future-measured completion deadline).
The ordinary fallback work cadence remains in use; this control does not validate
a general claim animation clock. Imp454's state/resume/pose agrees through1405;
79 ordered draws agree through1406.

At1407 native launches Egg1423 before the Chicken draws. The captured global
object chain has1423 at its head. The diagnostic `claim-and-head-control` moves
the matching existing Egg to the front of the modern newest-first walk, without
changing fields, ids or tile links. Its completed1400 pose, velocity, process24
and age9 are checked before the reorder. This restores91 ordered draws through1407.
It is only a head-order control, not a complete mixed-object-list restore.

`0x004A21C0` launches at age16 with random256 (`ObjectState.cpp:2638`);
`0x004DEA50` anchors it at Z0x13E8, decimal5096. The older research prose's5000
was a conversion error; production already uses0x13E8.

## Hatchery pillar obstacle at1408

CONFIRMED by the capture: Chicken1305 runs its process6 selector during turn1403 on tile10,25
(completed1402 state6; completed1403 state9).
Native chooses destination43339,104153 in that same tile, reaches reaction8
at1407 and draws its reaction timer at1408. Modern instead chose43339,108249
in tile10,26 and was still walking when native drew `ObjectState.cpp:2708`.

The native adjacent Hatchery tiles hold pillar78 objects1356 at10,24 and1355
at10,26, and Coop35 object1357 at11,25; tile9,25 is outside the room.
STRONGLY INFERRED from `0x004A1F30`: the owned-room selector tries four cardinal
neighbours, rejects `0x00453600` obstacles, and falls back to its current slab
if none passes. `0x004EF1F0` places Hatchery pillars at qualifying3x3 room corners,
unless a Pillar-flagged object already occupies the slab.

Our Hatchery pillars are virtual presentation furniture. The point selector
checked only stored objects, so the absent obstacle let the bird pick the south
corner. In `pillars-control`, supplying the four actual captured pillars78
reproduces the native destination and compared pose/process/age samples at
1403,1406,1407,1408. It extends the ordered sites and seeds to157 draws through 1412.

## Built at432 (ADR-0700)

Owned Chicken process6 now tests virtual Hatchery pillar78 obstacles using the
existing `RoomPillarCorners` geometry. The definition must have Obstacle;
an active physical Pillar-flagged object suppresses virtual placement, matching
the existing laying/view convention. Held or carried objects do not suppress it.
This adds no physical objects and consumes no additional random numbers.
Changed destinations shift later reaction draws. Two remake-only scenario pins
therefore change: the3000-tick Hatchery lays58 eggs (was55), and both Goblins
still eat at720 for375 health but choose birds7/9 (was5/11). These are not native
measurements. Other room pillars, native movement-gate bits and loose process10 are outside
this bounded change.

Ten regressions cover all four corners, the Obstacle flag, active/held/carried
replacement pillars, a narrow room without3x3 support and a noncorner edge.
The pre-fix eight-case run detects six affected cases and passes two controls;
the final ten cases and nine determinism checks pass. The full Release solution has
8437 passed and 30 skipped across ten suites. Unity compiles with zero errors and
two existing obsolete-API warnings. The regression pin is in
[hash-versions.md](hash-versions.md), not native evidence.

`production432` uses the same captured claim454 and object-head controls but
omits physical pillar injection. The archived `verify_prefix.py` checks all157
ordered sites and contiguous pre-draw seeds against the native log for1401-1412,
and also checks the older physical-pillar control. No future RNG reseeding is used.
The study's final manifest verifies 387 entries by SHA-256; the build's complete
manifest verifies 53 entries, including its earlier manifest. The build contains the frozen runtime
and `source.zip`; the study contains `production-source-sha256.json`.

## Next boundary:1413

The third draw differs: native draws the Chicken reaction (`ObjectState.cpp:2782`),
while modern Imp455 searches for a gold store with bounds25 then8.
Its restored state/resume is already wrong at1400: modern10/1 against native6/28;
this is the existing partial worker/mining restore boundary (Q719).
The first two draws at 1413 agree in site and seed (worker445 animation install,
then Troll449's idle-body choice); the matched whole-turn claim stops at1412.
Later mismatches are not attributed before this boundary is resolved.
Full-state, complete playback and Heart-defence parity remain unestablished.
