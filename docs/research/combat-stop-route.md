---
systems: [combat, creatures, navigation, simulation-core]
---
# Stopped routes through combat facing

## Boundary and cause

CONFIRMED in the Level 5 seed 1 capture: Wizard 436 ends its approach at 1575
in state 199 with one finished self-point and heading 0, from 1941. It faces
1707 at 1576 and 1612 at 1577, starting action 16 then. That action remains
through 1593 and clears at 1594. State 90 follows at 1595, with its room-walk
draw on 1596.

Modern 448 discards the point and stays at heading 1941 on 1575. It faces 1612
and starts its cast at 1576, releases at 1593, enters 90 at 1594 and draws
Room.cpp:1563 at 1595 before the native Chicken ObjectState.cpp:2851 draw.
Both casts hold for 17 completed samples: the start is early, not the duration.
The small earlier Imp 442 position difference does not require fitting a timer.

The observation-only control adds heading/start/deadline fields to body JSON.
All 8211 turn/seed/bound tuples, actor rows, seeds and events remain identical
to frozen 448. Read the context stamps separately: the old cast event is 1576,
its SwingStartTick is 1575; native/modern completed frame labels align as above.

## Binary rule and implementation

STRONGLY INFERRED from fresh primary reads: the
[stop wrapper](functions/0048ECE0.md) and [body stop](functions/004D1070.md)
request the creature's own position, then zero velocity. The
[approach in-reach exit](functions/00470E30.md) stops and enters its saved resume.
[Follow Path](functions/004D3C40.md) still turns on a newly finished self-point;
an already finished point brakes without re-aiming.

The inspected [fight branches](functions/0045F030.md) turn toward the target
and hold while the remaining angle is nonzero (0045FB23-0045FB47), without
adding a stop. The [starter](functions/0045A550.md) preserves an existing Combat
state through registration/range setup; [action begin](functions/00496700.md)
sets action/animation state without adding a movement stop. The
[busy predicate](functions/00496D00.md) reads the action id, not a timer.
The [turn helper](functions/004B2AE0.md) and counted duration stay unchanged.
Existing whole-function hypothesis confidence in the starter/turn records
is preserved; the cited branch facts were independently read here.

Hash/save 449 retains self-routes for non-Imp resolved states 199/200, including
walks resuming them. Non-Imp facing, target reselection and action 16/17 starts
preserve the existing route. The old no-route contact regression now explicitly
loads an empty route; approach-exit assertions use whole progress.
See ADR-0717 and [Library stopped routes](library-stop-route.md).

The family selection is implementation coverage, not a native owner/state guard.
Victory and type 1 Imp stops retain their existing adapters. The victory movement and
body-facing adapters need their own paired treatment before widening this scope.
Other combat limits, including the independent physical +0x46F eligibility gate,
remain partial. No full-state, unconditional replay or Heart parity follows.

## Controlled result and verification

The final control matches 3180 ordered sites/preseeds through 1596,
versus 3159 through 1594 at 448. It retains the same 24 restore inputs and the eight
inherited bootstrap clauses, adding five clauses for the old pre-1400 combat
caller adapters. Production has no tick gates. The frozen source proves all
13 clauses and six production overlays; the broad-Combat prototype is not delivered.

Wizard 436 matches 84/84 states, 83/84 state/XY and 81/84 heading samples.
Its next position difference is at 1596. All 23 busy-predicate samples from
1574 through 1596 match; earlier 438/439/442 component differences persist.
The full component receipt owns the remaining counts.

Next: 1597 draw 1, native ImpUtils.cpp:484 versus modern State.cpp:1700,
preseed 3845468488, modern 437/state 89 drawing 100. Trace the native caller and
preceding state inputs before attributing a missing rule.

Nine regression cases cover the stationary turn/two facing ticks, preserved
support route, both attack starts and four partial-scope controls. The exact final
counterfactual fails five cases and passes four controls on old behavior, then
passes all nine cases with the correction and version 449 metadata.
The nearby combat/hold/stop/determinism/snapshot selection passes 493 cases.
The full Release suite passes 8585 with 30 skipped and zero failures across
ten projects.
Pin: `0x3F5FFE08DBE3D082`.

Study: `F:/DK2Evidence/studies/q712-combat-stop1593--2026-11-07--001`.
`trace448/` and `trace-observer-proof.json` own baseline telemetry validation;
`controlled449-v2/` and its draw receipt are final; preceding control variants
are exploratory. `source-input-proof.json`, `native-proof.json`,
`verification-summary.json`, `counterfactual/` and `full449/` bind the result.
`review-receipt.json` records the independent approval.
Frozen 823-file source, payload and SHA-256 manifest:
`F:/DK2Evidence/builds/q712-combat-stop1593--2026-11-07--001`.
