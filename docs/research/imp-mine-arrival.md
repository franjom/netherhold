---
systems: [imps-tile-work, navigation, simulation-core]
---
# Imp mining arrival before job validation

## Rule and missing implementation

STRONGLY INFERRED: state 6 integrates the Imp, then tests whole route progress.
At exactly 1.0 it stops, resumes the saved state and resets the resume to 1.
It does not check the target's terrain or working stance. Primary reread:
[state 6](functions/00470C80.md), including disassembly 00470DD8–00470E07.
[State 25](functions/00465790.md) checks the mining task and adjacency on its
own following update; a missing task sends it to 10.

This rule was already researched. `ImpTaskSystem.UpdateMoving` nevertheless
required `IsInWorkingPosition` before the arrival handover. An exhausted seam
becomes walkable floor, so the stance test changes from beside it to on it.
The walker waited until `HasRoute` cleared, then fell back to state 1.
Hash/save 444 removes that stance prerequisite from state 6's handover;
entry and work-state validation retain their existing behavior (ADR-0712).

## Native and replay comparison

CONFIRMED for Level 5 seed 1: native Imp 444 enters 25 at 1536 beside the former
seam at 15,14, then 10 at 1537. It chooses another seam at 1538, arrives 25 at 1540,
enters 26 at 1541 and starts action 2/art 5 at 1542. The old replay arrives a turn
late and installs art 5 at 1543. Native 1542 draw 1 is RenderInfo.cpp:201.
The [installer](functions/004B37D0.md) draws its existing coin for a creature
resource with flags & 0xFC00; this is a delayed existing draw, not a new RNG rule.

With the correction, the controlled replay matches 2337 ordered native draw
sites and preseeds through complete turn 1550, versus 2189 through 1541 before.
Imp 444 matches 151/151 state/XY samples over 1400–1550. Heading agrees over
1536–1543; slot 5 phase, flags and rate agree at 1542–1543.
Native standing/walking clips remain outside the action-clock comparison.
Next: 1551 draw 1, native ResearchState.cpp:215 versus modern ObjectState.cpp:2851,
preseed 1435647451, modern Chicken 115 bound 4. Identify the native research caller.

## Scope and evidence

The final `controlled444-v2/` run uses exactly the preceding 24 input keys and
values. It preserves the three bootstrap clauses described in
[stop self-route](stop-self-route.md#controlled-comparison-and-bootstrap-limit)
and runs the prior arrival implementation before 1400, enabling the correction
at that existing boundary. These four diagnostic clauses preserve the earlier
bootstrap; production applies the correction from startup. There is no later
state, position, deadline or draw injection.

`controlled444/` retains the rejected new-arrival-from-startup trial, which still
has the prior three bootstrap gates. The unchanged room-order adapter guard
rejects an unknown room at 1425; its last completed frame is 1424.
`startup-trial-exclusion.json` excludes it from prefix claims. Neither trial
establishes unconditional production replay, full-state parity or the Heart
outcome.

Study: `F:/DK2Evidence/studies/q712-mine-arrival--2026-11-07--001`.
`draw-verification-v2.json` and `native-proof-v2.json` retain the final draw and
component checks; scripts are in `scripts-v2/`.
Native source: `q712-native-drawlog-level5-seed1--2026-11-03--001` in the same
studies folder; immutable capture, draw log and per-turn index are retained.

Final build: `F:/DK2Evidence/builds/q712-mine-arrival--2026-11-07--002`, with
`source.zip`, `payload/` and `manifest.json`.
`source-input-proof-v2.json` compares all 373 Simulation C# sources after
UTF-8-sig decoding and newline normalization; removing the four declared
bootstrap clauses leaves only the two inherited observation-only differences.
The Work overlay also matches production bytes exactly after removing its
declared bootstrap clause. The production source changes from frozen 443 are
limited to three files among 821 source files. V1 build/control artifacts
remain historical.

## Verification at 444

`red/` retains two failing completed-walk regressions and one passing incomplete
control; all three pass with the correction. `green/` passes 35 nearby arrival,
state, walk and determinism cases. The hash 444 fixture pin is
`0xF12CD4A1DE7E18AD`.

The final ten-project solution run passes 8544 cases with 30 skipped and zero
failures. `verification-summary.json` records the results and their provenance:
full raw stdout is retained only in the session transcript, rather than an
archived full-suite log. Startup test coverage and the controlled native
comparison retain their separate scopes.
