---
systems: [creatures, rooms, simulation-core]
---
# Library capacity and repeated shelf draws

## Binary reading

STRONGLY INFERRED from fresh whole primary reads:
[004E2500](functions/004E2500.md) checks a reachable Library for a shelf,
then asks [004E53B0](functions/004E53B0.md) for capacity. That helper's
Library arm calls [004E1AC0](functions/004E1AC0.md) again before the numeric
used-plus-requested comparison, even if the room will be full. Both shelf
calls draw on the world stream. The first result is not reused.

Production435 RoomCapacityRules.HasRoom omits that Library arm. This is a missing call
inside an already represented draw site, not a newly discovered RNG caller.
The research handler [004759D0](functions/004759D0.md) still uses the ordinary
room-route helper [00471A70](functions/00471A70.md); replacing its eventual
room-point draw with a shelf pick would reproduce the wrong call chain.

## Diagnostic control, production435 unchanged

A scratch build inserts the missing capacity shelf predicate in the two
Library-finder loops. It is not a production fix: other capacity callers,
including claims, are untouched. The frozen435 baseline and this diagnostic
use identical declared1400 inputs; no extra restore field or future fit is used.

| Run | Complete-turn ordered sites/preseeds | First different ordered site |
|---|---|---|
| Production435 |347 draws,1401–1425 |1426 draw3: Library.cpp:1068 / Room.cpp:1509 |
| Finder-only capacity check |488 draws,1401–1435 |1436 draw1: RoomManager.cpp:2177 / Treasure.cpp:335 |

CONFIRMED by the controlled replay: the extra capacity call fixes the third
draw at1426, then the genuine room-point call follows it. The whole-turn
match extends ten turns without changing production rules or inputs.
This is diagnostic evidence, not shipped behavior or Heart-outcome parity.

The corrected mapper labels RoomWalkRules.ArrivalTile as Room.cpp:1509
(source line0x5E5), not1563. Earlier archived verifiers retain the wrong
label; the historical332/347-draw complete-turn prefixes are unaffected, but433's first1425 site mismatch
is draw1 rather than the previously reported draw2. Other summaries link here.

At1436 the native site is the wall Training Target world draw already
recorded in [004EDFC0](functions/004EDFC0.md): type29, bound4096, line0x881.
The capture replaces target1412 with1429 in room1118 at the same position,
73472,124928. The remake instead next creates store gold, whose heading draw
is Treasure.cpp:335, verified in [004F3C60](functions/004F3C60.md).
No later parity is claimed, and no target-furniture rule is changed here.

Evidence is add-only under
`F:/DK2Evidence/{studies,builds}/q712-library-capacity-draw--2026-11-06--001`.
The runs are `baseline435` and `finder-capacity-check`. The diagnostic build
changes only RoomNeedRules in the archived435 source; its hash/save remains435.

## Built at436 (ADR-0704)

Execution hooks carry the same world RNG as their simulation context.
Library finders, entry claims, claim fallback and the general room finder
use the capacity overload that searches a shelf before numeric fullness.
The claim fallback completes every matching room's capacity check even after
finding a usable room; see [004C6AE0](functions/004C6AE0.md).
The public numeric query stays pure. Detached setup hooks check shelf
availability without a stream. Existing constructors remain available.
The Guard Room extra and existing geometry adapters remain outside this change.

The frozen435 baseline and final436 control use identical declared1400
inputs. Baseline retains347 ordered sites/preseeds through1425; final436
matches488 through1435. Its first differing site is1436 draw1,
RoomManager.cpp:2177 versus Treasure.cpp:335. No extra restore input or
future draw/seed is inserted. This extends the controlled draw prefix;
full-state, executed-turn and Heart-outcome parity remain unestablished.

Evidence is add-only under
`F:/DK2Evidence/{studies,builds}/q712-library-capacity-h436--2026-11-06--001`.
The final paired run directories are `baseline435` and `shipped436`; the latter
uses frozen435's diagnostic adapter with the shipped436 simulation changes.
The next boundary from this 436 control is addressed by the bounded
[neighbor-work rebuild at 437](training-target-rebuild.md). Its other Target
rebuild entries and animation phase remain outside that fix.

## Verification at436

The final focused run passes 90 cases. Counterfactuals fail 7 cases with one
positive control, and the fallback counterfactual fails its ordering case.
The final full solution run passes 8479 cases with 30 skipped and no failures
across 10 suites. The Release solution build has zero warnings/errors; Unity
compiles with zero errors and the two existing obsolete-API warnings.
Logs and TRX files are archived in the external study's `verification/` directory.
The final436 control and reviewer independently verify the 488-draw prefix.
The comment-refresh `shipped436` run verifies the same prefix and next boundary;
all eight production files match its executed control source byte for byte.
Its receipt is `shipped436-verification.json`; `manifest-final.json` records
SHA-256 checksums for the add-only archive.
