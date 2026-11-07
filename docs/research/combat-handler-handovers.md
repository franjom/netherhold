---
systems: [combat, creatures, navigation, simulation-core]
---
# Combat handler handovers

## Three consecutive leads

The preceding [room-order control](room-selection-order-inputs.md) ends at 1597
draw 1. These corrections concern existing handler timing and movement state.

1. Imp 442 reaches its withdrawal destination at 1594, but the old combat
   handler already ran before movement. Testing arrival after the Imp's movement
   enters 202 that turn, matching native. The draw boundary remains 1597 because
   the subsequent idle handover still waits.
2. Its earlier work countdown is 2. The old idle/selection adapter decrements
   that countdown and adds a turn in 1. Removing that gate yields native 202 at
   1594, 1 at 1595, 10 at 1596, then a reinforcement walk and ring draw at 1597.
   The diagnostic prefix advances to 3327 draws through 1604. At 1605, modern
   withdraws again before the native Treasure.cpp:335 draw.
3. Wizard 436's failed reacquisition at 1594 adds a modern stop. That replaces
   its finished route with an unfinished self-point and turns it from 1612 to
   1953. Native keeps heading 1612 through 1595. On its new route native turns
   to 1953 without moving at 1596, then turns to 99 and moves at 1597. Modern
   was already facing 1953, so moved at 1596 and re-engaged Imp 442 a turn early
   at 1604. Preserving the finished point removes that early withdrawal draw.

CONFIRMED components above come from the Level 5 seed 1 native capture and
paired diagnostic replay. The controls preserve the same 26 restore input
values. They do not establish full-state or Heart-outcome parity.

## Binary evidence and delivered scope

STRONGLY INFERRED from a whole reread of [state 6](functions/00470C80.md): it
integrates an Imp before testing progress exactly 1.0, then stops and resumes
the saved state in the same call. The movement system now completes a type 1
Imp's state-6 withdrawal walk to 202 after its movement step. Other work walks
keep their existing handlers; other combat movement and empty-route adapters
remain partial. State 202's own handler still waits until the next turn.
The general state-6 hold during a kind-4 recoil action remains unmirrored.

The [idle handler](functions/004773D0.md), reread whole with its worker-arm
instructions, requests 10 and returns. The focused
[selection-entry instructions](functions/00463DF0.md) use the existing owner
and per-player search throttle, without a work countdown. UpdateIdle no longer
waits on TimerTicks; work applications retain their countdowns. The idle and
selection handlers remain separate turns. The
[ring-scan entry](functions/00468D10.md) still draws random(4) unconditionally;
the missing 1597 draw was delayed execution, not an absent random-draw rule.

The inspected no-target/refusal branches of
[spFighting](functions/0045F030.md) return zero without a movement stop. The
[Combat leave hook](functions/0047A980.md),
[fight release](functions/0045ADC0.md),
[forced-target clear](functions/0048F3F0.md) and
[unlink](functions/0045EF50.md) do not request one. CombatSystem's non-Imp
handlers in states 199/200 now enter idle directly on failure, preserving the
route and facing. External Disengage callers and type 1 Imps retain their
existing adapters. This caller-family coverage is not a native type guard.

The third lead was checked against the
[hero standing-job handler](functions/00477850.md) and
[KillPlayer job](functions/00462490.md): successful routing returns without a
second state-handler dispatch. The focused
[creature-update tail](functions/00488930.md) tests the current state's mover
bit after dispatch. No blanket delay on a hero's first route step was added.

Hash/save 450 changes behavior without new saved fields (ADR-0718).

## Verification and remaining boundary

The three-step diagnostic comparison matches 3426 ordered sites/preseeds
through 1607, versus 3180 through 1596 before these corrections. Its next
boundary is 1608 draw 1, native State.cpp:1025 versus modern
ResearchState.cpp:215, preseed 1284650174, modern bound 7 from creature 439 in
state 54. That is the next investigation; no further lead is claimed here.

The read-only timer observer preserves draw turn/seed/bound tuples and
byte-identical actors, seeds and events from the preceding room-order control.
The effective phase-only, countdown and failed-handler controls vary one
correction at a time. The first worker-handler placement was ineffective
because workers skip that handler while withdrawing; it is exploratory.

Six new cases cover the integrated arrival/idle/selection sequence, incomplete
and other-resume walk controls, both stale-countdown entry states, and facing
on failed reacquisition. The existing idle-flag case no longer asserts a wait
that the binary does not implement. Counterfactual evidence retains version
450 metadata while replacing the three behavior files with their prior bytes.
Five cases fail and eleven controls pass on the old code; all sixteen pass
with the corrections. The nearby selection passes 494 cases. The full Release
suite passes 8591, skips 30 and has zero failures across ten projects.

The final hash-450 control retains all 26 input values and thirteen inherited
bootstrap clauses, adding three clauses for these old pre-1400 adapters.
Production has no tick gates. Source proof strips all sixteen clauses and
compares against production, retaining only two inherited read-only simulation
observers and the Tools telemetry. Wizard 436 matches 95/95 state/XY and 92/95
heading samples; Warlock 437 matches 101/101 in both. Imp 455 matches all
208 state/XY/heading samples. Earlier Imp 442 position/heading and 438/439
component differences remain in the native receipt.
Pin: `0x5BB21DAEC8FC7759`.

Study: `F:/DK2Evidence/studies/q712-imp-withdraw1594--2026-11-07--001`.
`observer449`, `phase-v2-449`, `idle449` and `disengage449` own the staged
diagnostic results and draw-verification receipts. `counterfactual` owns
revision-bound red/green sources, logs and the restoration receipt.
`controlled450-v2` and its draw receipt are final. `source-input-proof-v2.json`,
`native-proof-v2.json`, `verification-summary-v2.json` and `full450` bind the result.
Frozen 823-file source, payload and SHA-256 manifest:
`F:/DK2Evidence/builds/q712-handler-handovers--2026-11-07--002`.
