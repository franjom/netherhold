---
systems: [rooms, simulation-core]
---
# Chicken birth draws and the Egg animation clock

## Primary rule

STRONGLY INFERRED from fresh reads of [Object initialization](functions/0049B1C0.md),
[Egg launch](functions/004A21C0.md) and [Egg hatch](functions/004A1CF0.md).
The type9 initializer draws2048 on the world stream at0049B2CB,
Object.cpp:63, and stores4096+roll in both scale words+48/+4C.
The hatch caller makes the Chicken before choosing its heading. Outside its
owner's Hatchery, it draws2048 at004A1E09, ObjectState.cpp:2458.
The existing second owned-Hatchery check and destination rejection remain
outside this change's bounded heading implementation.

When a launched Egg slows to horizontal speed sum at most4096, state24
changes to32 without reinstalling art0. Its existing art clock survives.
State32 polls the previous animation flags, installs art5 after the floor
clip wraps, and creates the Chicken on the following update.

## Captured boundary and hypothesis control

CONFIRMED in the Level5 seed1 capture: Egg1423 keeps its art0 through1436's
state24-to32 transition, wraps at1441, installs art5 at1442 and disappears
when Chicken1431 appears at1443 at the same x68781/y106357. The new bird's
scale words are4489, matching size roll393 from preseed2308691482.
The modern438 run retains the same Egg's horizontal pose and velocity but
resets its art stamp at the state change, hatching one turn late. Its hatch
path also lacks the size and off-Hatchery heading draws.

An isolated hypothesis probe preserves the last art stamp, polls that
clock and adds the two birth draws. With exactly the same21 declared1400
inputs as438, all687 ordered sites/preseeds match through1450. No later
capture supplies an input. This is a diagnostic result, not production or
full-state/Heart-outcome parity.

Evidence: `F:/DK2Evidence/studies/q712-chicken-birth--2026-11-07--001`.
The frozen438-telemetry control reproduces590 draws through1442; the
hatch-hypothesis run is independently mapped in its verification receipt.

## Built at 439 (ADR-0707)

Live type9 creation samples and retains `ChickenScale` through the bound world
creation RNG service. The value is typed, saved, hashed and copied; service
bindings are omitted from state copies and rebound by GameSimulation. Egg
state24-to32 preserves the existing art stamp, and hatching polls elapsed art
time rather than state age. Off-owned-Hatchery heading follows the birth draw.
Map/startup/load and definition-replacement initialization remain partial, as
do broader hatch destinations, the second room check and deferred removal.
Presentation application of the retained scale is outside this change.

The final `delivered439-verification.json` verifies 716 complete ordered
sites/preseeds through 1452. Next is 1453 draw1: native State.cpp:1025 versus
modern Chicken ObjectState.cpp:2851, preseed3429617960 and bound4. The
[neutral heading control](neutral-idle-heading.md) addresses this historical
boundary without changing production rules. Imp455
matches all 53 samples within that prefix. Across the entire 501-sample
window, only 125 match; this is not later component or full-state parity.

State-only439, birth-draws-only439 with the old clock, and clock-only439 without
the birth draws each retain 590 draws through 1442 and the 1443 boundary. All
controls assert identical dictionaries of 21 declared input keys and values.
The adapter only suspends live creation draws during the 1400 restore and
rebinds afterward; production runs the rule from startup. No later capture
supplies a deadline, draw site or other input.

## Verification at 439

The original counterfactual fails four cases with two passing positive
controls. Nine new cases cover the change; the final focused run passes 55.
The final ten-suite run passes 8505 cases with 30 skipped and zero failures.
The Release solution build has zero warnings/errors; Unity compiles with zero
errors and the two existing obsolete-API warnings. Initial failing full-run
logs are retained separately from the successful final run.

The final study archive layout is `delivered-change.zip`,
`verification/final-tests/*.trx`, `verification/final-full-tests.log`,
`verification/chicken-birth-final-focused.trx`, `verification/summary.json`,
`manifest-final.json` and `integration-receipt.json`. The matching build
archive under `F:/DK2Evidence/builds/q712-chicken-birth--2026-11-07--001`
contains `source.zip`, payload and control variants. Archive verification and
local integration are recorded in the external `integration-receipt.json`.
