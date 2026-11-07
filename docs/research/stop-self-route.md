---
systems: [creatures, navigation, simulation-core]
---
# Neutral stops retain a self-route

## Native 1451–1452 boundary

CONFIRMED in the Level 5 seed 1 capture: neutral Troll 447 arrives in state 1 at 1451,
at 178130,195908 with heading 1617. Its body retains one point at its own position,
flags 0, total distance 0 and path-finished clear. At 1452 it enters neutral state 5;
the heading becomes 1958, the axes become zero and path-finished is set. Position
does not change. Subsequent stationary ticks keep that heading.

The preceding 442 control instead clears the route at 1451, retaining heading 1617.
Both versions choose the same destination at 1508. Native turns that frame and
moves at 1509; modern spends another turn rotating. Native reaches neutral idle
in time for State.cpp:1025 at 1532; the preceding control misses that draw before
Imp 455's gold deposit. This is not a demonstrated deposit-rule error.

STRONGLY INFERRED from fresh primary reads:
[the stop wrapper](functions/0048ECE0.md) and
[body helper](functions/004D1070.md) request a route to the creature's own
position, then zero velocity. [Follow Path](functions/004D3C40.md) can mark
the final point finished and still turn in that same call.
[Integration](functions/004D4760.md) derives axes from the resulting velocity.
The [integration wrapper](functions/004D5A40.md) skips integration when the
body's maximum speed is zero. The [outer creature update](functions/00488930.md) tests the current state's
movement bit after its handler, excluding type 1 Imps; their integration belongs
to their travel handler. [State 6](functions/00470C80.md) tests progress 1.0,
stops and resumes, rather than waiting for the retained point to disappear.

## Bounded implementation

ADR-0711 retains the zero-length one-point stop route for neutral non-Imps,
applies their current-state movement flag and zero-speed hold, and hands their
idle-return walk over at whole progress. The neutral state 1 frame skips steering; state 5 can then
perform the one stationary turn. No future heading, route, completion deadline
or draw is supplied. The type 1 Imp stop adapter is unchanged.

Ownership is an implementation boundary, not a native predicate: the native
helpers are generic. Other owners retain the empty-route adapter and their
existing movement gates. This does not complete the general stop or mover path.
Hash/save 443 adds no fields; its remake regression pin is 0x5F32B8E99F7A1CD5.

## Controlled comparison and bootstrap limit

The study inherits all 24 input values from the
[Library neighbour control](library-neighbour-kick.md).
A generic post-restore self-route/movement-gate experiment loses draw agreement
at 1406. A neutral-only version without the progress handover loses it at 1453.
Those controls do not validate either incomplete combination.

The final 443 controlled run, enabled at the existing 1400 restore boundary,
matches 2189 ordered sites/preseeds through 1541. Native and candidate 447/449 each
match 142/142 state/XY and heading samples 1400–1541. Actors 439, 441 and 455 match
142/142 state/XY; 438 remains 120/142, first differing in XY at 1520. This is not
full-body, full-state, playback or Heart-outcome parity.

The unconditional 443 probe changes pre-restore history and stops in the
diagnostic room-manager wrapper at 1425: an unknown room lacks its required
post-restore creation rank. That failure is retained. The wrapper guard is not
relaxed. Controlled runs gate the three new branches until 1400 to preserve the
old bootstrap; production applies its neutral correction from startup. The
three bootstrap gates and two inherited observation-only source differences
must be distinguished from production. The matched prefix is post-restore
mechanism evidence, not an unconditional 443 replay result.

The newly exposed Hero 436 HeartWalk draw at 1536 is an existing implemented site:
fresh reads of [job 19](functions/00462490.md) and
[room point selection](functions/004E4D90.md) map it to Room.cpp:1563. The source
comment's old floor-test polarity is corrected without changing its picker.
This control stopped at 1542 draw 1, native RenderInfo.cpp:201 versus modern
ObjectState.cpp:2851, preseed 210208807; modern Chicken 130 drew 4. The
[next control](imp-mine-arrival.md) traced the delayed existing animation draw
to the Imp job-walk handover.

## Evidence

Add-only study: `F:/DK2Evidence/studies/q712-stop-self-route--2026-11-07--001`.
The exploratory runs are `candidate442/`, `neutral442/`,
`neutral-progress442/` and `production443/`. Final `controlled443-v2/` has mapped,
native-facing and component receipts; `unconditional443-v2/` and
`unconditional-exclusion-v2.json` preserve the rejected startup comparison.
The first 1536 unmapped-site receipt is superseded by the mapped receipt,
not a gameplay fix.

Frozen builds are under
`F:/DK2Evidence/builds/q712-stop-self-route--2026-11-07--002`:
`source.zip`/`payload/` contain the controlled bootstrap;
`unconditional-source.zip`/`unconditional-payload/` retain production logic.
`source-input-proof-v2.json` checks six declared Simulation changes from frozen 442,
three bootstrap clauses and the two inherited observation-only differences.
The six unconditional overlays equal current production bytes. No cross-build
DLL identity is claimed. The build manifest verifies all retained files.

## Verification at 443

The final seven-case regression run has five failures and two passing controls
against the old three behavior files with 443 metadata, then seven passes with
the correction. The zero-speed mutation separately fails one case and passes
one control, then both pass with the guard. Exact test/source hashes are in
`verification-v2/counterfactual-v2.json`; the guard-specific proof is under
`verification-v2/speed-guard-proof/`.
The complete ten-suite run passes 8541 cases, skips 30 and fails none.
`verification-v2/summary.json` owns the results; logs are retained in
`verification-v2/red-v2/`, `verification-v2/green-v2/` and
`verification-v2/full443-v2/`, with scripts under `final-scripts-v2/`.
Startup unit/full-suite coverage and the controlled native comparison retain
their distinct scopes. Earlier build generation --001 and v1 study receipts
remain historical.
