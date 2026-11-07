---
systems: [combat, navigation, simulation-core]
---
# Combat reach queries use funnel corners

## Boundary and cause

CONFIRMED in the Level 5 seed 1 capture: Imp 442 enters state 203 at 1575,
then state 6 resuming 202 at 1576. That turn begins with PlayerRooms.cpp:644,
FightWithDrawState.cpp:60/61 and PlayerRooms.cpp:603. The existing
[withdrawal](functions/004611D0.md) and room pickers
([typed](functions/004C6460.md), [whole list](functions/004C63D0.md)) account
for those draws. They are not a new random source.

Modern 446 loses Wizard 436's target at 1574 and leaves Imp 442 working.
The observational trace reaches the strategy's hearing guard: sight is blocked,
the route exists, but the measured length is 18283 and the range test fails.
With the plain-query points it measures 11721 and passes. This is the modern
controlled query result; native temporary query points were not captured.

## Binary rule and implementation

STRONGLY INFERRED from [0045AFF0](functions/0045AFF0.md), the cache/mode
branches of [004D0320](functions/004D0320.md), and whole reads of
[004CD920](functions/004CD920.md) and [004CEF40](functions/004CEF40.md):
combat requests mode -2. Both directional
searches are funnelled, their Euclidean lengths choose the corridor with forward
ties, and the kept corridor is funnelled into corner points. It does not run
the gate walk that movement uses for its midpoint aims. Combat then sums
octile legs and accepts strictly when `(length >> 12) * length < rangeSquared`.

The strategy [0045B500](functions/0045B500.md) uses that hearing test;
[00470E30](functions/00470E30.md) unlinks/reselects an ordinary approach target
and ends the approach when its tag changes. Fixing the query geometry retains
the target in this encounter and permits the existing fear/withdrawal rules.

Hash/save 447 adds `RoutePointKind` and explicit overloads in `MeshRouter` and
`WorldSteering`. `CombatTargetRules.CanReach` selects funnel corners; existing
movement signatures retain gate midpoints. There are no new saved fields.
The regression distinguishes a nine-tile hearing refusal from a ten-tile
acceptance around a wall, including target acquisition. See ADR-0715.

## Controlled comparison and limits

The plain-query control matches 3006 ordered sites/preseeds through 1587,
versus 2774 through 1575 at 446. Its 24 inputs are unchanged. It retains the
five earlier bootstrap clauses and a sixth that uses the old query points
before the 1400 restore. Production has no tick gate. The observation-only
trace emits verdicts from existing calls; it adds no route query or random draw.

Wizard 436's recorded state/XY samples match all 75 from its appearance through
1587. Six earlier tracked actors match 188/188 samples; Warlock 438 matches
177/188, first differing at 1577. Imp 442's small earlier position difference
persists even though its back-off and withdrawal states now agree. Draw
agreement is not full-state, unconditional replay or Heart-outcome parity.

The native query cache, global query-option writes and other reachability
adapters remain partial. This change selects the proper point representation
for combat; it does not establish complete native routing equivalence.

Next: 1588 draw 1, native ResearchState.cpp:215 versus modern ImpUtils.cpp:484,
preseed 2725511882, modern Imp 454 drawing 4. Warlock 438's earlier divergence
is a candidate antecedent; trace the actual native caller before attributing it.

## Verification and evidence

The exact final regression bytes fail one acceptance case and pass the refusal
control on old behavior, then pass both with the fix and matching version 447
metadata. The 57 nearby route, approach and registered-attacker tests pass.
The full Release suite passes 8569 with 30 skipped and zero failures across
ten projects.
Pin: `0x5ECC1567B9E7928A`.

Study: `F:/DK2Evidence/studies/q712-withdraw1576--2026-11-07--001`.
`controlled447/` and its draw receipt are final; `native-proof.json`,
`source-input-proof.json`, `verification-summary.json`, `counterfactual/`
and `full447/` bind the results to their source. The earlier `plain446/`
trial is exploratory; `trace446-v2/` owns the observational verdicts.
`trace-observer-proof.json` records equality of all 8654 turn/seed/bound tuples
against base 446; JIT inlining changes stack text. Console route probes use the
pre-increment world tick while `events.tsv` uses the context tick, so the 1574
hearing event corresponds to the console route stamp 1573.
`review-receipt.json` records the independent approval.
Frozen source/payload and SHA-256 manifest:
`F:/DK2Evidence/builds/q712-withdraw1576--2026-11-07--001`.
