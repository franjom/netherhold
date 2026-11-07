---
systems: [creatures, navigation, rooms, simulation-core]
---
# Lair home handover

## Boundary and cause

The [451 control](stopped-waypoint-precision.md) reaches 1616 draw 1 with
Warlock 437 already in state 89. Native 437 is still making its home in state 80.
The clone compressed travel arrival, bed creation and idle dispatch into one update.

CONFIRMED in the Level 5 seed 1 capture: 437 reaches (43008,129024) in state 7
on 1614, heading 1111. Its next states/headings are 80/1452 on 1615, 80/0 on
1616, 1/0 on 1617 and 89/0 on 1618. The diagnostic reproduces this sequence.

## Binary and implementation

STRONGLY INFERRED from whole-body rereads:
[travel state 7](functions/00471460.md) stops on whole progress and returns
after setting its resume state; it does not run the new handler.
[State 80](functions/0046F6B0.md) stops at age one, turns toward the centre of
its current tile and waits while the remaining angle is nonzero. Only then does
it validate a candidate, create the bed and clear Lair anger. A healthy creature
returns failure to the dispatcher; a wounded one sets 81.
[The turning helper](functions/004B2AE0.md) and existing movement turn are
separate steps, explaining 437's two turns on 1616.

[Bed registration](functions/004E03F0.md) and
[creation](functions/0048AD50.md) add no creature stop after creating the bed.
The [dispatcher](functions/00476FC0.md) runs one handler, so a completed home
handler's idle state waits until the next turn.

The clone now retains the existing quantized stopped self-route for non-Imp
resolved state 80, tests whole progress on home walks, separates arrival from
the home handler, and waits for facing. The current-state movement-bit gate
covers state 80 and updates that began there (including separate sweeps),
suppressing the mover when a healthy home handler exits to idle. A transient per-turn marker prevents a
second idle dispatch, including hosts that sweep the systems separately.
No new persistent fields are added. Hash/save 452, ADR-0720.

Existing saved-room validation, wounded 81-to-sleep compression and failed-home
retry/stop adapters remain partial. This change does not establish all Lair
handler timing or full-state parity.

## Verification

The final controlled hash-452 replay matches 3587 ordered sites/preseeds through 1616,
versus 3573 through 1615 at 451. Next is 1617 draw 1: native
Creature.cpp:2790 (bed heading), modern ObjectState.cpp:2851 (chicken roam),
preseed 1515096229, modern bound 4. That ordering is the next lead, not a rule
implemented here.

The unchanged 26 controlled inputs and sixteen inherited bootstrap clauses are
retained. Three diagnostic clauses preserve pre-1400 behavior for this correction;
production contains none of those diagnostic gates.

The exact six-case counterfactual fails five cases and passes one control on old
behavior, then passes all six with the correction. The nearby selection passes
244 cases. The full Release suite passes 8602, skips 30 and has zero failures
across ten projects (`full452-v2/output.log`, exit zero).

Warlock 437 matches 110/110 state/XY/heading samples. The six prior actors
438, 439, 441, 447, 449 and 455 each match 217/217 in those components.
Other actors and earlier Imp 442 pose differences retain the limits in
`native-proof.json`; these counts do not establish full-state parity.
Pin: `0x420CEFB2342AC832`.

Study: `F:/DK2Evidence/studies/q712-lair-home1616--2026-11-07--001`.
`trial451/` retains the exploratory replay. `controlled452/` and
`controlled452-draw-verification.json` are the final control;
`counterfactual-v2/result.json` owns the revision-bound red/green result.
`source-input-proof.json`, `native-proof.json` and `verification-summary.json`
bind the final source, native comparison and verification. Frozen source, payload
and SHA-256 manifest:
`F:/DK2Evidence/builds/q712-lair-home1616--2026-11-07--001`.
Controlled agreement does not establish unconditional replay or Heart-outcome parity.
