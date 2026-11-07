---
systems: [creatures, navigation, rooms, simulation-core]
---
# Stopped routes through Library handovers

## Boundary and cause

CONFIRMED in the Level 5 seed 1 capture: Warlock 438 enters Library return
state 62 at 1576 with one unfinished self-point. At 1577 it turns from heading
1050 to 1391; it resumes 53 at 1578, walks in 6 resuming 54 from 1579, reaches
54 at 1586 and starts reading in 56 at 1588. ResearchState.cpp:215 is that
furnished-slot random 7 draw, not a new random source.

Modern 447 discarded the stopped point. It missed the stationary turn at
1577, made its first actual shelf-walk step at 1580 rather than 1579, and drew
for reading at 1589. Retaining the point alone also exposes both Library
handover adapters: an unfinished zero-length self-route has whole progress
while HasRoute remains true.

## Binary rule and implementation

STRONGLY INFERRED from fresh whole reads of the
[stop wrapper](functions/0048ECE0.md), [body stop](functions/004D1070.md),
[state 6](functions/00470C80.md) and [state 7](functions/00471460.md): stopping
requests a route to the creature's own position and then zeroes velocity.
Arrival uses progress exactly 0x10000, not PathFinished or a nonempty route.
State 7 additionally validates the saved room id/type before stopping/resuming;
that existing adapter gap is outside this change.

The [return handler](functions/00476320.md) still waits for age greater than
one before resuming 53. The [shelf handler](functions/00475D20.md) still faces
the furniture before its random 7/action 29 branch; its failed-action-start
guard remains partial. Neither handler's timing needs adjustment.

Hash/save 448 extends the retained self-route from neutral non-Imps to resolved
Research non-Imps, including travel states resuming that category. ResearchStep
and CreatureJobSystem use whole progress for internal and entry Library walks.
Other job walks keep their existing adapter. This caller-family selection is
implementation coverage, not an ownership/category predicate found in DK2.
The self-point survives into state 62 even though that state's own category is
Refused. See ADR-0716 and the earlier [neutral stop](stop-self-route.md).

## Controlled comparison and limits

The final paired control matches 3159 ordered sites/preseeds through
1594, versus 3006 through 1587 at 447, with the same 24 restore inputs. It keeps
the six inherited bootstrap clauses and adds two that preserve pre-1400 Library
walk handovers. Production has no tick gates.

In this control, at 1579 native 438 was at X 26147 while modern was at X 26148.
The native stationary step had body velocity (-2,0); modern had (1,0) in 16.16
units. The [later precision correction](stopped-waypoint-precision.md) owns
that discrepancy. Do not infer full pose or full-state parity
from restored draw timing. Warlock 438 states match 195/195 samples but state/XY
match 177/195; five earlier tracked actors retain 195/195 state/XY, while 439 has
191/195, first differing at 1591. Hero 436 first differs in state at 1593
(80/82 state/XY matches); Imp 442 matches 194/195 states and 150/195 state/XY.
Other owned stop/travel adapters, unconditional replay and Heart outcomes
remain open.

This control stopped at 1595 draw 1, native ObjectState.cpp:2851 versus modern
Room.cpp:1563, preseed 2586590841; modern Hero 436/state 90 drew 25. The
[next control](combat-stop-route.md) traced the early room draw to the earlier
combat stop/facing sequence, with the cast duration unchanged.

## Verification and evidence

The seven exact regression cases fail three cases and pass four controls on
old behavior, then all pass with the correction and version 448 metadata.
The focused Library/movement/travel/pin selection passes 70 cases. The complete
Release suite passes 8576 with 30 skipped and zero failures across ten projects.
Pin: `0xF2FEC6511B348EED`.

Study: `F:/DK2Evidence/studies/q712-library-stop1588--2026-11-07--001`.
`controlled448/` and its draw receipt are final; `library-scope3/` is exploratory.
`source-input-proof.json`, `native-proof.json`, `verification-summary.json`,
`counterfactual/` and `full448/` bind the final result to its source.
`review-receipt.json` records the independent approval. Frozen 823-file source,
payload and SHA-256 manifest:
`F:/DK2Evidence/builds/q712-library-stop1588--2026-11-07--001`.
