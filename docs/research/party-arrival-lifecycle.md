# Party arrival lifecycle (Q712)

Source: retail DKII.EXE v1.70, Ghidra 12.1.4, 2026-11-01. Binary readings below
are STRONGLY INFERRED; capture observations are CONFIRMED. This corrects the
old interpretation of the spawn flag as selecting an arrival *from* a gate.

## Spawn selection

`00519F90` initializes its stack flag to 1 at `0051A01B`. Terrain 33 or 37
clears it at `0051A107`; other terrain leaves it set. After each occupied
party member is initialized, `0051A5A6-0051A5C6` tests that flag against zero,
enters 269 when nonzero and clears draw byte +F3 bit 0. Thus the two large
Hero Gates bypass 269. This is based on terrain at the original action-point
position, before the 2x2 shift and per-creature step-off. It is not a test of
the final creature position or room ownership. The Tile gate terrain 29 does
not share this exception.
The same nonzero flag requests leader effect 43 after the member loop; that
presentation request remains omitted. Event 3 already uses the existing
created callback in the remake.

`00519B20` is the common creature factory for party members and trigger
action 1. It performs the gate step-off but does not select 269. Action 1's
`00518AD0` arm creates at a tile centre, applies levels/flags and optional
collapse; it has no 269 step. Action 14 delegates to 00519F90. Ordinary and
action 28 portal arrivals go through 004DC9A0, which starts action 49 and
selects 193 for a keeper Portal or 278 for a Hero Portal, not 269.
The latter's handlers 00462730/004627E0/00462840 form 278→279→280, then attempt
a lair through 0046F390; they do not join 210's recovery chain. Their existing
unbuilt production path and position-helper semantics are outside this fix.

## State table, capabilities and exits

The state table has no per-state enter/exit functions: its three unused
pointer fields are zero here. All four states are group 3, category 7, whose
enter/update/leave category hooks are null. SetState 00476D30 stamps entry and
clears age/suspension on every accepted transition, including the same id.
Dispatcher 00476FC0 increments age +1C before calling a handler; a zero
handler result sets idle 1, with no second handler that turn.

| State | Handler / flags | Operation and next state |
|---|---|---|
| 269 | 004626A0 / 0x44 | Holds except at age exactly 8. Sets current Z to 0x2000, copies XYZ to previous pose +F6/+FA/+FE, sets +F3 bit 0, requests 210. It returns 1 even if that request fails; there is no >= timeout or automatic retry after 8. |
| 210 | 00481520 / 0x42 | Requests sound 0xC7 (event 199, sound manager slot +0x20, [sound-effects.md section 11](sound-effects.md#11-arrival-rest-and-reactions)), ends a running action, starts action 64 with count 1; success and successful SetState 263 return 1, otherwise 0. No fixed duration or route clear. |
| 263 | 004815A0 / 0x42 | Holds while an action is running. After completion, ShouldDelayGetUp (+3E6 bit 27) consumes one handler call and clears. Otherwise starts action 53/count 1 and requests 264; either failure returns 0. |
| 264 | 00458680 / 0x42 | Returns IsActionRunning 00496D00. On completion, dispatcher returns it to 1. No special hero job, target or route restoration. |

Flags 0x40 prevent the normal fight lookup via 0045A6D0; all four have it.
269 additionally has flag 0x04, which clears cached attackability at 0045A8B0.
210/263/264 lack 0x04 and may be targeted subject to the other ordinary guards.
Forced ProcessFight bypasses MayLookForFight; these flags do not establish
immunity from every externally forced state/action. The clear flag 0x01 suppresses
ordinary steering at 0048A248; physical motion 004B2030 still runs. State 269
is not a special rule about being hit or swinging.

## Action and height clocks

00496D00 tests current action id != 0 and != 21, not an elapsed timer. The
counted starter 004969D0 delegates to 00496700 and writes count + 1; count 1 clears
looping. Missing art still starts an action. All action records permit ending
in 00496D20. Ordinary immediate playback starts advancing after the handler
that starts it (0048BE50); the next earlier poll sees completion. The existing
AnimationTimingRules knockdown/get-up holds reproduce this ordinary path.
This does not establish arbitrary action replacement/queued animation parity.

The shared physical pass applies gravity above the signed floor when creature
slot +0x88 (0048ED40) reports non-flying. 004B2740 and the position setter clamp
the floor and clear downward velocity. Shipped gravity 240 gives -61440 in
the persistent vertical vector and -3840 position units on the first free step.
Our zero-floor height representation makes native Z 0x2000 one tile above
the usual native floor 0x1000. General collision/flight adapters remain
those documented in imp-create-position.md and steering-behaviours.md.

## Native control

New add-only evidence:
`F:/DK2Evidence/studies/q712-gate-arrival-audit--2026-11-01--001/`.
`arrival-native.py`, `hero439.json`, `spawn-tiles.json`, capture SHA in inputs.json.
Seed 2 hero 439, party 0, is born at (17, 42) on terrain 8, with 269 age 0 and no
action. Snapshot headers label the completed native turn plus 1:

| Captured ticks | State / action |
|---|---|
| 1514-1521 | 269 / none |
| 1522 | 210 / none; previous Z 8192, post-physical Z 4352 |
| 1523-1527 | 263 / 64; Z reaches floor 4096 at 1523 |
| 1528-1534 | 264 / 53 |
| 1535 | idle 1 / none |
| 1536 | 199 / 16, targeting 455 |

## Mismatches listed before implementation

| Address | Hash 362 comparison / intended scope |
|---|---|
| 0051A01B / 0051A107 / 0051A5A6 | Notes reverse the terrain exception. HeroPartyRules omits 269 entirely. Implement the non-gate party branch only; direct action 1 and portals retain their separate paths. |
| 004626A0 / 00476FC0 | No 269 age 8 handler, height/previous-pose change or draw enable in ours. Implement exact equality and one handler per turn, with saved age and vertical state. |
| 00481520 | Existing Tremor recovery starts the correct ordinary clip hold but fails to end an existing swing/training action. End it before starting 64. The presentation-manager slot +0x20 request remains omitted. |
| 004815A0 / 00458680 | Existing ordinary action-duration surrogate, optional one-call delay and idle completion agree. Reuse these; do not introduce a fixed stun duration. General active-action replacement is outside the surrogate. |
| 00488930 / 004B2030 | Add the non-flying vertical arrival subset before the existing recovery steering hold; retain the zero-floor convention. General physical flags, horizontal impulses/collision and dynamic flight remain existing adapter boundaries. |
| 0048755D-0048757A | Constructor seeds FightProcessCounter from native tag modulo 10. Ours starts 0 and has different ids; native 439 retains 9 through recovery. No phase constant is inserted. This separate identity/initialization boundary is now built; see [Q718](fight-scan-initial-phase.md). |

## Standalone implementation and validation

ADR-0622 builds the non-gate party branch and 269 handler, with the existing
210/263/264 ordinary action clocks. Arrival draw/age and vertical state survive
snapshots; standalone hash/save 365 and pin `0xBB097DC526B13102` cover the change. It also ends
the prior action at 210. No selector, reporting or fear constants are changed.

Frozen-build verification: twelve new binary-backed cases pass; eight fail against old source and four are
controls. The old-source proof uses a separate pre-change source archive, with the new
test file added; no production file was reverted in the working repository.
Simulation 4261, Assets 66 and Presentation 1404 pass. Full-solution testing
retains the same Level 18 failure observed on base master `1b41d64d`; Data 502
passes and the
previous Q520 custom-level failures do not recur in this install. Scratch
baseline attempts missing scenario/Unity input roots are excluded; restoring
the committed inputs leaves the known failure and passes HumanCommandPolicy.

Matched results below use provisional hash/save 363. Standalone version 365
differs from the frozen build only in its hash/save version stamps and three
portal coverage reason strings. All 29 focused arrival/determinism/coverage
tests pass at 365; its full solution build has zero warnings/errors. Frozen
evidence paths and measured table values retain 363.
No outcome constant or RNG adjustment is made.

## Integration verification (hash/save 366)

Merging master `b1eb6fd5` into branch `1b61059e` combines ADR-0620/0621/0622
at hash/save 366, pin `0x0BEC17EF48AD31CC`. The combined gameplay differs
from standalone 365; the frozen provisional-363 studies below do not measure it.

The completed full suite passes 4,270 Simulation and 1,413
Presentation cases. Behavior passes 510, skips 5 and retains the one known
Level 18 failure; the other projects pass. Q712/Q713/Q714 remain open; the later
[phase correction and 366/367 measurements](fight-scan-initial-phase.md#matched-outcomes-366-through-370)
build Q718 and test the later integrated versions.

## Matched results against 362

All 75 Level 5 and 40 Level 4 resyncs exit 0, stored pickups, original inputs and
five generators per native state. Corrected frozen build and final studies use
suffix 002; partial suffix 001 is explicitly superseded and excluded. Evidence:
`F:/DK2Evidence/builds/q712-party-arrival-h363--g1093bc85-wip--2026-11-01--002/`
and `F:/DK2Evidence/studies/heart-defence-q712-h363-l5--2026-11-01--002/`,
with the corresponding `l4` study. Commands, scripts, source hashes and scorer
SHA-256 provenance are retained; README files define each measurement.
The build's `source-metadata-amendment.json` records three later portal coverage
reason-string changes; `production-version-amendment.json` records the 365
version rebump and verifies identical gameplay.

On legacy header alignment, hero 439 follows the captured 269/210/263/264/idle
transition labels in all five seed 2 generators through 1535. These historical
controls do not establish [executed-turn parity](packet-playback-clock.md). Post-reveal height, previous height and
draw state agree in the zero-floor representation. A regression caught and
fixed previous-Z retention after landing before the final runs. The first
later combat decision still differs: native scans at 1536, whereas the gen 101
remake enters 90 at 1536 and acquires 455 at 1538 after movement. The later [phase correction](fight-scan-initial-phase.md) addresses the
native tag-derived initial counter; this standalone measurement used our 0.

### Hero presence and targets

Means per run, ticks 1401-3400, native seeds 2/3/7, three native runs vs 15 remake
runs per version. Strict dispatch radius about room tile (17, 26), not the
player Heart-position tile (16, 25). Full per-run target/state counts, visit
lengths and endings are in exposure-summary.csv, visits.csv and exposure.json.

| Hero-ticks inside | DK2 | 362 | 363 (provisional) |
|---|---:|---:|---:|
| All non-collapsed | 4327.00 | 2720.93 | 3028.60 |
| Targeting Heart pillar | 3298.67 | 1607.00 | 1893.13 |
| Targeting Imp | 790.00 | 790.60 | 843.87 |

### Reporting entries and exits

Same 1401-3400 cohort; counts are visible state transitions, not every
within-turn state call. Source: state-transitions.json with input CSV hashes.

| Transition, mean/run | DK2 | 362 | 363 (provisional) |
|---|---:|---:|---:|
| Entries into 254 | 15.00 | 12.87 | 9.53 |
| Exit to 199 | 3.67 | 0.60 | 0.80 |
| Exit to 203 | 5.00 | 2.60 | 1.07 |
| Exit to 1 | 3.00 | 4.67 | 3.47 |
| Exit to 6 | 2.00 | 4.40 | 3.53 |
| Exit to 8 | 1.00 | 0.53 | 0.67 |

Fight-plus-flee exits fall from 3.20 to 1.87 per remake run against native 8.67,
despite increased total hero exposure. The corrected lifecycle does not close
Q712; aggregate entry/acquisition parity worsens on this measure. No constants,
weights or scan rules were tuned toward the results.

### Death scores

Original score.py definitions and cohorts are unchanged. All ten native-state
resyncs ran at 1400; full-window death scoring excludes 5/8 because their native
captures stop near 1463. The later Level 5 window uses states 1/4/6/9/10; Level 4
uses all eight states. These are the existing death proxies, not new native
death instrumentation.

| Window / group, mean/run | DK2 | 362 | 363 (provisional) |
|---|---:|---:|---:|
| L5 1401-3400 / keeper | 13.38 | 9.90 | 9.75 |
| L5 1401-3400 / keeper Imp | 11.12 | 7.35 | 7.38 |
| L5 1401-3400 / hero | 3.75 | 5.10 | 4.95 |
| L5 3401-5000 / keeper | 8.60 | 9.16 | 8.60 |
| L5 3401-5000 / keeper Imp | 5.80 | 7.20 | 6.48 |
| L5 3401-5000 / hero | 5.00 | 4.44 | 4.32 |
| L4 5001-6000 / keeper Imp | 6.50 | 11.93 | 11.93 |

Level 4 is unchanged exactly: 11.925 Imp deaths/run; all 80 creature/event
outputs match 362 by SHA-256, and no 269 was observed. The same eight generator 404
histories were terminal before restore 5000; Q714 retains those histories.
The 32-pair intersection without detected pre-restore terminal state remains
10.03125 for both versions. That outcome-selected subset is descriptive only;
the old regression is still confounded, and this change supplies no new cause
for it. Native Heart controls are inherited unchanged in the sensitivity JSON.

Q712 remains open around matched target acquisition/dispatch inputs
(0045CB00, 0045E9F0, 004D9690); Q713 retains the known adapters, Q714 the restore
omission. The later [phase correction](fight-scan-initial-phase.md) builds Q718;
remaining allocator/clock restore inputs are Q719. No new play-only question is needed.
