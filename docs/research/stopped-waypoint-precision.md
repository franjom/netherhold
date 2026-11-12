---
systems: [combat, creatures, navigation, rooms, simulation-core]
---
# Stopped waypoint precision

## Boundary and cause

The [450 control](combat-handler-handovers.md) stops at 1608 draw 1, native
State.cpp:1025 versus modern ResearchState.cpp:215. The idle draw is present:
neutral 449 takes it after Warlock 439's extra shelf draw. This is an early
researcher, not a missing idle roll.

CONFIRMED in the Level 5 seed 1 capture: Warlocks 438 and 439 still match
positions at 1576. At 1577, 438 moves from body position (428192,1972704)
to (428190,1972704), velocity (-2,0). Modern instead moves to (428193,1972704),
velocity (1,0). Native's stopped waypoint is (428032,1972480), while the
modern adapter used the full position. Both have stuck-escape mode before that move.

The waypoint difference changes the existing escape nudge's axis/sign.
It leaves native 438 one entity-position unit left of modern. At 1591 this
changes the crowded 439's movement, despite its equal starting pose, heading
and route. Native 439 resumes shelf state 54 at 1608; modern did so at 1606
and draws its next read at 1608 before native is ready. The precision-only
control removes that earlier discrepancy without changing research timing.

## Binary rule and implementation

STRONGLY INFERRED from whole primary rereads of the
[body stop](functions/004D1070.md),
[creature route wrapper](functions/004D0F60.md) and
[body route request](functions/004D5100.md): stopping routes to the creature's
own position, then sets body position and zero velocity. The route query
receives position and destination shifted right eight; its points are loaded
back shifted left eight. The requested target remains full 16.16 precision.
The shifts are explicit at 004D5138-004D5147 and 004D530F-004D531C.
The intervening [entity-unit wrapper](functions/004D59E0.md) first shifts the
two 12.12 position arguments left four, forwarding the flags to that request.

The [endpoint builder](functions/004D0150.md) and
[path copy](functions/004D0240.md) retain the destination on that route path.
The regular WorldSteering route path already converts through 16.8; the direct
stop adapter bypassed that conversion. StopMoving now rounds only its retained
waypoint down to the 1/256-tile grid. Body position and PathTargetPos stay exact.
Every non-Imp stop retains the route since ADR-0751 ([stop-self-route.md](stop-self-route.md)); Imp stops keep their adapter.

The focused [Avoid Walls tail](functions/004D35C0.md) reads the current window
point for its escape nudge. The [separation callback](functions/004D2B80.md)
uses retained body positions and the existing wrapped scaling; the focused
[integrator](functions/004D4760.md) clamps and weights the existing forces.
No nudge, separation or integrator arithmetic changes.
The [shelf handler](functions/00475D20.md) still waits for facing before its
existing read draw. The [idle inner](functions/004776C0.md) still takes its
nonzero-stamp random(100). Their broader partial coverage is unchanged.

Hash/save 451 changes behavior without new fields (ADR-0719).

## Verification and remaining scope

The final controlled hash-451 replay matches 3573 ordered sites/preseeds through
1615, versus 3426 through 1607 at 450. This control stopped at 1616 draw 1,
native ObjectState.cpp:2851 versus modern State.cpp:1700, preseed 3667998223,
modern bound 100 from Warlock 437 in state 89. The
[next correction](lair-home-handover.md) resolves the compressed home handover;
the waypoint precision rule is unchanged.

Five regressions cover neutral, Library and fight retained waypoints with
exact body/target positions, the captured escape-nudge direction, and an
owned-idle empty-route control. The ordinary stop/query adapters remain
partial. Controlled draw agreement does not establish full-state,
unconditional replay or Heart-outcome parity.

The revision-bound counterfactual keeps 451 metadata: old behavior fails four
cases and passes the empty-route control; the correction passes all five.
The nearby stop/route/determinism/snapshot selection passes 140 cases. The full
Release suite passes 8596, skips 30 and has zero failures across ten projects.
In the final control, creatures 438, 439, 441, 447, 449 and 455 each match
216/216 native state/XY samples from 1400 through 1615. Other components and
actors remain separate in the final native receipt.
Pin: `0x7772E79E5A838228`.

Study: `F:/DK2Evidence/studies/q712-stopped-point1608--2026-11-07--001`.
`quantized450` retains the preceding exploratory precision-only trial and its draw receipt.
The existing 26 input values and sixteen bootstrap clauses are retained;
the quantized waypoint adds no bootstrap clause.
Final delivery receipts are `controlled451/`, `source-input-proof.json`,
`native-proof.json` and `verification-summary.json`. Frozen source, executable
payload and SHA-256 manifest:
`F:/DK2Evidence/builds/q712-stopped-point1608--2026-11-07--001`.
