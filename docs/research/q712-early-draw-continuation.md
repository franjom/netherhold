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
Capture-derived inputs use completed1400; the derived next-poll claim timer and
binary-derived route options are identified below. No later pose or completion
tick is fed into the probe. `command.json` and `env.json` in each run name every input.
Runs through 1420 retain raw outputs after their first mismatch; those suffixes
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

In the 432 control, Hatchery pillars were virtual presentation furniture. The point selector
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

## Imp455 gold-return control: through1420

The earlier third-draw mismatch at1413 is a restore omission. Native Imp455 is
already carrying1091 gold in state6/resume28 at completed1400, with previous
state26, original entry1392, heading741 and an active captured route. The generic
worker restore instead leaves it in modern10/resume1; it mines again and later
calls the gold-store chooser. The earlier body input explicitly skipped455.

STRONGLY INFERRED from the re-read `0x00464400`: the gold-store chooser commits
its selected point through the worker walk wrapper with resume28 and options0xE8.
The control restores that captured trip and the existing body route; it makes no
new route request, draws no number during restoration, and supplies no future
arrival or work deadline. The route target in16.16 units is1234240,1714848.

`off-v2` matches all five prior production432 outputs byte-for-byte (draws,
actors, seeds, jobs, events). `on-v2` adds only the declared455 input and its
captured body. Ordered sites and pre-draw seeds agree for212draws through1416.
Imp455's state/resume/XY agree in all21 completed1400-1420 samples.
This validates the bounded input, not a general worker restore adapter.

The next mismatch at1417 is modern Chicken88, hatched at1416 from Egg61.
That Egg came from modern pre-restore history. The native1400 inventory contains
only Eggs1421 and1423; the old adapter added1421 and retained the unrelated61.
The `inventory` control uniquely matches the two captured Eggs by owner, pose,
process, age and velocity, then removes only unmatched modern61. It does not
copy future object existence or restore every Egg field or object class.
With both controls, all271 ordered sites and contiguous pre-draw seeds agree
through 1420. The full-room Hatchery's1419 coop-selection roll is the existing
`0x004DE620` call at004DE925, `Hatchery.cpp:357`; no missing consumer was added.

Both diagnostic builds compile with zero warnings/errors. Production remains
unchanged at hash/save 432; no production tests or remake expectations changed.
The previous full-suite and Unity results above apply to the same simulation.
Final source, complete runtime, input files, paired runs and verifier:
`F:/DK2Evidence/{studies,builds}/q712-imp455-gold-return--2026-11-06--001`.
Use `off-v2`, `on-v2` and `inventory`; initial `off`/`on` are superseded exploratory
runs whose initial executable was not separately frozen. Raw outputs are retained.
The extended study manifest verifies 469 entries and the build manifest 52 entries
by SHA-256; the archived verifiers independently pass.

## Next boundary:1421, Troll449's idle action

A further run, `through1450`, uses the frozen archive executable and unchanged
inventory inputs. The whole-turn match still ends at1420. At1421 the first two
draws agree: Imp445's existing gold-store selection (`Room.cpp:1563`,
`ImpState.cpp:764`). The third differs: modern Troll449 enters the wander search
(`StateUtils.cpp:325`) where native next updates a loose Chicken.

CONFIRMED from the capture: Troll449 starts action44 at1413; at 1421 it remains
in state87 with action44, phase1521, rate169, flags0 and count2. Modern's idle
choice at1413 sets `ActionUntilTick=1420` using `IdleActionGuessTicks=8`, then
its state87 handler starts wandering at 1421. STRONGLY INFERRED from the re-read
`0x004808E0`: native tests `0x00496D00` before any wander draw; the nonzero44
is busy. The earlier restored1400 action ends correctly in the bounded control,
but this newly chosen action uses the production duration guess.

ADR-0701 now builds the [counted non-worker idle clock](idle-action-clock.md),
which extends the controlled prefix through1424 with its declared inputs.
That note owns the current1425 boundary; no future-measured deadline is fitted. Raw1450 outputs after
the1421 boundary are retained and excluded from parity claims. Complete mixed
object restoration, full-state, playback and Heart-defence parity remain open;
Q712 and the remaining Q719 restore scope are not closed.
