---
systems: [creatures, rooms, tooling-process]
---
# Q710: fighter decisions after the Level 4 restore

## Verdict

Q710 remains OPEN. The completed inventory retains the two moving actors'
current body/path windows, but the restore contract does not carry them. This
supports preparing a guarded short continuation, not a fully matched route or
an explanation of the Guard-room cause. Equal seeds/counts remain insufficient
for call-order or behavioral parity.

Production code is unchanged by these studies. Throwaway tooling is archived
with external evidence; the targeted owned native replay below follows the
earlier managed controls.
[Remembered Guard memory](guard-job-memory.md) is a separate bounded build.

## Evidence and controls (2026-11-14)

The frozen packet, enriched turn-5000 inputs and eight native captures come
from `F:/DK2Evidence/studies/q714-restore--2026-11-14--001` and the
[post-Q714 comparison](resync-heart-terminal.md#level-4-recheck).
Prior full tables are retained in commit `2c297b90` and the studies below.
Evidence paths in this note are beneath `F:/DK2Evidence/studies/`.

The archived CSV's turn-5000 row precedes `WorldResync.Apply`; boundary sidecars
record its actual pair map and immediately restored fields. Ordinary carried
rows are not evidence of a matched route, bed or job history. Off/on observers
must reproduce all five ordinary telemetry files; assertions cover only their
recorded fields, not every private field or the complete world.

## The first observed difference is already in the restored inputs

At native seed-7 label 5000, Dark Elf 428 travels in state 6/resume 98 and owns
bed 2356; Troll 453 travels in 7/resume 80 and later owns bed 3916. The frozen
hash-516 generator-101 restore instead pairs them to modern 75/83, both idle
without a bed or route. All seven sampled fighters become 7/resume 80 by 5002.
This demonstrates unequal inputs, not different choices from matched inputs.
The [lair acquisition guard](functions/0046F390.md) reading is unchanged.

Evidence: `q710-room-decisions--2026-11-14--002`,
`q710-room-observer--2026-11-14--002` and
`q710-decision-summary--2026-11-14--002`. Raw resume/bed links and observer
pairings are checked against their parents; all five observer-off/on files agree.

## Late home-seeking includes different actors

In labels 5300..5450, native Goblins/Trolls have zero state-7/resume-80 exposure;
the frozen 40 default-Xoshiro runs average 39.125 actor-turns/run. In seed 7 /
generator 101, the late Troll exposure belongs to new unmatched modern 87/88,
not initial Troll 453. New Dark Elf 89 pairs to native 419 at differing birth
labels. The existing type/owner/100-turn-window matcher is an adapter: unmatched
births do not prove native absence, and retained actors do not prove identity.

Population evidence: `q710-population-census--2026-11-14--001`.
Keep later births separate from initial-pair decision controls.

## Examined hash-518 snapshot

Two seed-7 replays at source `7d6c318a`, generators 101/202, change fighter and
Guard-target exposures in different directions relative to frozen hash 516.
They diagnose that snapshot, not latest master, a replacement cohort or an
isolated Q807 intervention. No general improvement/regression follows.
Evidence: `q710-current-h518--2026-11-14--001`.

## Bed-ownership control at hash 519

The inventory retains untorn native 428 and reciprocal bed 2356, object type 20,
owner 3 at (27,59), in Lair 1111. Inline state/job/needs fields are present;
pointed-to paths and navigation caches are not ([capture scope](../../reverse-engineering/README.md#legacy-polling-captures-dk2livepy-record-dk2capturepy)).
At source `d0d3d065`, actual pair 428 to modern 76 is idle and bedless after
restore. A logical bed intervention changes its next path from home acquisition
to Guard. Recorded non-bed fields and RNG are asserted unchanged, but the bed
adds one object/id and two ordinary sound requests; raw native object identity
and the active native route are not restored.

The acquisition helper is bypassed by the outer `HasBed` test, STRONGLY INFERRED
from unchanged source and observed ownership; no flipped inner-helper guard was
observed. The native actor was already travelling, so this is not its chooser
event at the same label. Four runs complete; plain/off/on telemetry is identical.
Evidence: `q710-428-inventory--2026-11-14--003` and
`q710-428-bed-control--2026-11-14--002`.

## Inline job and needs controls at hash 519

Source `5dd771cf` restores seven supported needs fields and five current/dropped
job/chooser fields for the same pair. Sleep retry uses captured attempt plus
four; literal stamps do not establish clock alignment. Bed-only versus needs
changes later travel exposure; adding the supported jobs does not change that
window. State/resume, routes and unselected history remain modern.

Captured remembered Guard 6 and saved room 1116 are separate inputs. A first
state-89 scratch retry chooses Guard without the ordinary happy roll and retains
stamp 2584 rather than 5001, despite identical resulting state 97. This local
counterfactual does not establish a native chooser event or later scratch
fidelity. The subsequent bounded implementation is [guard-job-memory.md](guard-job-memory.md).
Evidence: `q710-428-inline-control--2026-11-14--001`; six completed runs and
byte-identical plain/off/on plus bed-only repeat controls.

## Post-build paired control at bb2a395b

Native seed 7/default generator 101 pairs 428 to modern 45. Both interventions
copy the same supported history and retain the linked bed at tile 2976 versus
native 2977. Clearing remembered fields is deliberately counterfactual: ordinary
production already has Guard 6/western room reference 28. Restoring captured
Guard/eastern reference 22 changes a later rolled Training decision to remembered
Guard, with recorded non-memory fields equal through label 5239.

At 5300..5450, restored-memory state-7/resume-97 exposure is 82 labels versus
native 97 and ordinary baseline 66. Routes, room mapping, other reservations,
birth populations and history remain unmatched; this is no production before/
after improvement or global outcome verdict. Four runs complete with five-file
plain/observed equality. Evidence: `q710-built-comparison--2026-11-14--002`;
`manifest-verified.json` is authoritative and its two assessment files qualify
retained invalid setup/self-entry claims.

## Fighter cohort at 7904875f

The hash-521 study compares ordinary, selected-history and history-plus-memory
arms for eight native states at generator 101: 24 arms, 26 completed runs with
byte-identical plain/off/on controls. The initial cohort has 58 living owner-3
type-6/7/10 actors, 21 remembered Guards and 37 None; 49 nonnull bed links are
reciprocally checked. Boundary assertions include unchanged RNG and only four
recorded memory-component differences between history arms.

| Mean stored-target actor-turns/run | Native | Ordinary | History | History-memory |
|---|---:|---:|---:|---:|
| All eligible actors | 273.75 | 242.375 | 250.625 | 195.25 |
| Initially paired cohort | 266 | 239.5 | 232.25 | 193.625 |

The metric counts positive-health, non-216/217 owner-3 Goblin/Troll rows naming
a living owner-1 Guard, type 25, in 5001..6000. Modern rows are live-world rows;
native rows are raw captures. It measures stored targets, not attacks, and has
different filters/windows from older carried-CSV metrics. Memory effects are
three lower, two higher, three unchanged: descriptive, not a population estimate
or regression/improvement verdict.

Bed presence differs for 22/58, bed tile/presence for 53/58 and state/resume for
50/58 (46 state ids, four walking resumes). None matches both bed and state,
even before requiring resume equality. Outside-cohort dying reservations,
unavailable routes and later births remain limits. The compatible local control
below is completed; this cohort still does not establish a matched-input cause.
Evidence: `q710-fighter-cohort--2026-11-14--002`,
`manifest-reviewed-input.json` (332 files), `analysis.json`,
`boundary-assessment.json`, `scope.json`.

## Compatible bed and point controls at d6d9b5f7

Native seed 8/default modern 101 selects compatible idle Troll 440 to modern 27,
with matching pose/health/state 1/resume 1, no active modern route and no forced
native state. Only supported literal history/jobs and remembered None are copied.
Sham recreates the old bed tile; native-bed mode recreates native bed 1753's
logical tile (27,61), rather than baseline (39,55). Both allocate id 1048, keep
138 objects and preserve RNG. Tile-centre placement is not raw object identity.
Across 1000 labels, all recorded non-bed-id fighter fields, scorer world metrics
and RNG match; changing the input bed tile has no observed effect in this case.

First state sequences match at 5001 (89), 5002 (7/resume 63). A read-only destination
probe finds native Workshop 1110's point (30,48), modern JobTile (30,49).
Only active state-7 [travel fields](travel-states.md) are decoded. A flagged
counterfactual runs after the normal picker, preserving its draw, validates
room/furniture/navigation/obstacle predicates, substitutes (30,48) once, then
uses the normal route without injecting a path. RNG after the step matches off.

| State-63 arrival label | Ordinary/off | Substituted point | Native |
|---|---:|---:|---:|
| Troll 440 | 5018 | 5025 | 5026 |

The seven-label shift leaves one label different. It establishes neither native
picker correctness nor timing/body/world parity; no one-tick steering correction
follows. Nine accepted runs complete and required plain/off/on controls match
all five files. Evidence: `q710-compatible-bed--2026-11-15--002`,
`manifest-reviewed-input.json` (223 files), main/destination/point analyses.
Trial 001's rejected raw-position assertion remains outside those nine runs.

## Room order and RNG inputs at 0b71f052

The new frozen hash-521 study continues native seed 8/Troll 440 with default
modern generator 101. Native Workshop 1110, type 10/owner 3, has the same exact
15-tile footprint and chain as modern. Head, tail, links and count are validated
at native labels 5000/5001/5002/5026. Modern slab ranks are 652..666 and next
slab order is 1680. Reassigning only relative ranks 1..15, without changing the
allocator or RNG, leaves the first picker and all 1000 sidecar labels unchanged.
This rules out chain order for this Workshop/control, not every room or restore.

Default Xoshiro 101 picks index 5, tile 2480=(30,49); native stores tile
2430=(30,48). The modern nine-tile eligibility mask is observed, not established
as native. Native seed 2790454060 at 5001 becomes 145474329 at 5002, a distance
of 23 LCG recurrence steps. No native call identity follows from that distance.
Under the modern mask and captured chain, ordinals 2/5/7/9/12/20 yield index 6
or 7 and the captured tile. These are conditional arithmetic matches, not an
identified native preseed or an exhaustive set of actual native candidates if
eligibility differs.

A further counterfactual switches algorithm in the same `SimRandom` object only
after turn-5000 bed/history/chain-read restoration: pre-5000 world history stays
Xoshiro, aliases/context remain intact, and the boundary seed becomes captured
1962619826. Scratch Core mutability/helpers and a Tools branch enable this; it
is no production API or correction. Flag off matches all five prior read-control
files. Recorded actor/scorer boundary fields match except RNG when switching on.

| Label-5001 endpoint from the same boundary seed | Seed | LCG steps |
|---|---:|---:|
| Native | 2790454060 | 26 |
| Same-world switched modern | 892191520 | 30 |

The switched first picker has preseed 966827007 (step 31), postseed 1671788882
(step 32), index 1 and tile 2481=(31,49), still not the captured point. Its preseed
occurs within the sampled native 5001..5002 seed interval; that does not identify
it as the native picker call. Steps are recurrence distances, not attributed
native call counts. Algorithm plus boundary seed is insufficient in this
partially restored world; arbitrary RNG burns would not establish equivalence.

All eight runs complete 6000 turns. Five-file equality checks cover main
plain/off/on, picker trace off/read and native-RNG flag off/read. The 882 baseline
source hashes match Git `0b71f052`; all modified-source after-hashes are bound.
Evidence: `q710-room-point-inputs--2026-11-15--001`,
`manifest-reviewed-input.json` (193 files, excluding itself), `analysis.json`,
`rng-switch/analysis.json`, `interpretation.json`, `retained-log-assessment.json`.

## Native room draw trace at 9ac4c16e

One owned native `l4live` packet replay uses the original map-gated seed-8
helper. The owner is warned before debugger pauses. Five world-return checkpoints
cover labels 4999..5003, alongside native RNG logging; no full-process dump is
made. Receipts report the owned game closed and the packet unchanged.

For actor 440's listed fields, room 1110, slab chain and world seed, each new
return checkpoint C matches retained polling label C+1: 4999..5003 map locally
to 5000..5004. Every same-label comparison fails and is preserved in
`checkpoint-comparison.json`; successful comparisons are in
`phase-projection-comparison.json`. The known world-call-before-clock-increment
order ([world tick](functions/00526590.md), [clock read](functions/00517490.md))
supports this sampled phase mapping. It is not a global polling reindex,
managed-tick identity or full actor/needs/body/world equivalence.

The new-return interval 5000..5001 corresponds to retained 5001..5002 and
contains 23 logged draws. Its sole `Room.cpp:1509` row is ordinal 2, preseed
2060768411, postseed 152469822. The complete appended log has 180 rows and
179 consecutive LCG links; 45 rows precede the first checkpoint and 135 are
attributed across four intervals, with counts 26/23/48/38.

Actor 440 is the only captured creature newly storing a state-7 destination
in both corresponding 448-creature windows: (30,48), resume 63. The native log
records site/preseed, not actor, range or predicate results. This association
therefore remains contextual. Failed walk attempts and per-call actor identities
remain unobserved.
Assuming room 1110 and range 15, the logged draw gives index 6/start (31,47).
Applying the prior observed modern eligibility mask then selects (30,48),
consistent with the native point, not proof of native eligibility or a fix.
The former six arithmetic matches are narrowed to this observed site ordinal;
no output-matching seed or arbitrary RNG burn is selected.

Evidence: `q710-native-room-draw-seed8--2026-11-15--006`,
`interpretation.json`, `draw-attribution.json`,
`phase-projection-comparison.json`, `native-transition-context.json` and
`log-sequence-validation.json`. `manifest-final-review.json` verifies 36 files,
excluding itself.

## Preceding draw attribution at c0a88b63

This research branch reuses frozen managed source `0b71f052`, hash 521, and the
prior same-world native-RNG control; it is not a latest-master production run.
The 882 baseline source hashes match that Git snapshot. Six scratch files differ
from base, only three of them additional observers. Off/read runs both complete
6000 turns; all seven ordinary telemetry/picker/sidecar files match each other
and the prior native-RNG-on control byte-for-byte. Neutrality is limited to those
outputs, not all private fields. No native game is launched.

The matrix records 49 modern `NextInt` calls over world ticks 5000/5001,
compared with the previously local-phase-bound native return intervals:

| Interval | Modern calls | Native logged draws |
|---|---:|---:|
| 5000 | 30 | 26 |
| 5001 | 19 | 23 |
| Combined | 49 | 49 |

Both end at seed 145474329. Every pre/post seed pair matches by combined global
ordinal from the same LCG start, not by caller identity or behavioral order.
The first recorded managed draw has update context modern 75/native pair 420,
starting state 1: the source-backed visible path is `CreatureSleepSystem` through
`LairSeeker` to `ArrivalTile`, range 4, not an exhaustive runtime call graph.
Native's first site is `GoodState.cpp:253`. The
[primary reread](functions/00461F00.md) establishes fixed range 9 there,
STRONGLY INFERRED; the log does not directly record its actor or range.

| Frozen boundary actor | Native state/resume | Native bed | Modern state/resume | Modern bed/route |
|---|---|---:|---|---|
| 420 | 6 / 98 | 3029 | 1 / 0 | none / none |
| 422 | 6 / 98 | 3191 | 1 / 0 | none / none |

Their poses and health agree, but the selected control intentionally restores
only stationary actor 440; these moving actors remain unmatched. This does not
report a bed intervention for 420/422. Troll 440's room draw is global ordinal
32 in managed execution versus 28 natively, with preseeds 966827007 versus
2060768411. Four LCG positions do not identify four extra/unnecessary calls,
a picker defect or an RNG-burn correction. Per-call native actor/range and
predicate attribution remain conditional.

Evidence: `q710-preceding-rng--2026-11-15--001`,
`manifest-reviewed-input.json` (62 entries, excluding itself), `analysis.json`
(49-row matrix and boundaries), `binary-read-facts.json`,
`replays-verified.json` and the source-provenance receipt.

## Route/body inventory at 65a0290c

This inventory adds no managed replay or native launch. It checks raw 1343-byte
actor records, including the embedded body at +0x124, for 420/422 across ten
frames. Five local phase pairs are new native returns 4999..5003 versus retained
polling 5000..5004. All 20 actor snapshots agree in the projected state/control,
entity/body and heading fields; this does not establish full-world equivalence.

| Boundary actor | Total / window points | Point index | Tail distance |
|---|---|---:|---:|
| 420 | 3 / 3 | 1 | 0 |
| 422 | 1 / 1 | 0 | 0 |

The last active point equals the target. These complete current paths fit in
the saved windows. Five raw segment slots exist, but only count-minus-one active
segments are interpreted. Actor 422 reaches state 98 at new return 5001 and has
a new state-6 route by 5002; a captured window cannot be reused indefinitely.

The exported seed08 creature rows contain state 6/job resume 98 but no body/route,
heading or bed binding fields. The checked `ResyncCreature` likewise has no body/path/
bed binding fields. `WorldResync`'s non-worker branch is text-identical to frozen
`0b71f052`: pose/health copy, then retention of the modern state when both
states belong to `FreeStates`. For these actors it retains modern idle; it does
not actively reset native state 6 to 1 or restore the saved moving resume/body.
These are source bindings checked at `65a0290c`, not a new runtime comparison.

The navigation pointee at world+0x9F2 lies outside every saved region/record span
in the ten checked frames. This scoped absence neither inventories the entire
archive nor proves map data absent. Complete current windows do not supply a
future route query's navigation context or all neighbour/action/animation inputs.

The MovementEnd extraction width is bound to the [state-6 record](functions/00470C80.md).
`width-correction.json` qualifies rejected inventory001; the corrected002 payloads
remain byte-identical for the sampled values. No long-lived route claim follows.
Evidence: `q710-route-body-inputs--2026-11-15--002`, `route-body-inputs.json`,
`coverage.json`, `width-correction.json`, and `manifest-final-review.json`
(six entries, excluding itself). Four current-source bindings are verified.

## Next trace and remaining limits

Prepare a source-bound guarded short moving-body continuation; no such control
is yet implemented or proved matched. Guard arrival/stop, refill, stuck requery
and new routes, and stop before unprovided context is required. Keep neighbours,
action/animation, mesh/cache, world history and broader-cohort differences explicit.
Bind the current managed baseline before implementation. No new native recording
is currently prescribed, and no AI/picker/production correction follows.
