---
systems: [creatures, simulation-core]
---
# Counted non-worker idle actions

## Binary rule and captured defect

The [Q712 turn1421 boundary](q712-early-draw-continuation.md#next-boundary1421-troll449s-idle-action)
identifies Troll449's newly chosen action44 outlasting our eight-tick estimate.
The captured action starts1413 and remains active1421, before the native wander
handler's first draw. This is separate from the declared initial-state controls.

STRONGLY INFERRED from fresh primary reads: `0x004776C0` tests the resolved art's
availability (`0x004966C0`) before starting43/44 through `0x004969D0` with count1.
`0x004950C0` installs null handlers, mode3, and art slots20/31 for these actions.
The wrapper stores count2 and clears Loop. For other callers its count increment
wraps to a byte, and the Loop gate interprets the supplied byte as signed.
`0x004808E0` returns while an action
runs, before any wander draw. `0x00488930` polls actions before the state handler
and `0x0048BE50` advances their animation afterwards.

`0x00496AA0` uses distinct count0/1/other branches at cycle flags. With count1,
only Complete writes255; Wrapped alone retains1. A larger count decrements;
reaching1 clears Loop, otherwise it clears both cycle flags. A zero advance
rate or cycle flag with count0 completes the animation half. Missing resolved
art falls through to wandering; an existing resource with no timing ends on
its next poll rather than inventing a duration.

## Implementation boundary (ADR-0701)

The typed IdleActionId marker identifies43/44 independently of the resolved
art slot. The clock preserves the requested slot as its cache identity while
resolving the resource through `0x00494860`/`0x00494880`: missing20 falls back
to1 and missing31 to20. Changing requests reinstalls even a shared resource.
The marker is hashed and saved with the existing phase/rate/flags/count.
The creature sweep polls after timed effects and the worker prelude, before
combat; it advances after the creature's trigger root, once per
turn even when a test host runs creature systems separately. The start itself
is not polled again that turn. Shared action interruption clears the marker.
Freeze cancels the old action before changing state; stun replacement cancels
after the state change. The counted runner's cycle branches also apply to its existing callers.
Worker state12 retains its existing host; other action timers and exceptional
animation overrides are outside this bounded implementation.

Hash/save433 rejects older snapshots. The determinism pin is a remake
regression, not native evidence; see [hash-versions.md](hash-versions.md).

## Controlled native continuation

The final-source controlled run matches **332 ordered draw sites and preseeds
over1401–1424**. It retains the declared1400 inputs from
[the preceding continuation](q712-early-draw-continuation.md), plus the captured
expired speed timer for Imp444: both native speed counters are0 at1400.
The candidate prehistory retained60 ticks at150%; restoring the captured body
cap alone was undone by that timer on the next turn. This diagnostic restores
only SpeedEffectTicks to0, leaving the unused150% value; no speed rule changes.

Troll449's newly chosen44 matches all seven clock fields at all26 samples
1413–1438: action identity, requested art, phase, rate, flags, count and handler
completion. Both clear the action at1439. This component check after1424 is
separate from global random-stream parity; no later render-phase match is claimed.
No future action deadline, pose or random seed is injected.

At1425, modern Warlock438 makes a Lair-arrival lookup at draw1; its Room label
aliases the native first draw, so that label alone does not identify the native actor.
The first different ordered site is draw2: modern Imp444 Room.cpp:1563 against
native ImpState.cpp:764. At completed1425, captured438 is in state89 at its previous
position while modern438 is in state7 and has moved. The modern Lair lookup is the
next investigation candidate; the source of the extra draw needs a binary check.
This advances the bounded probe, not full-state or Heart-outcome equivalence.

Evidence is add-only under
`F:/DK2Evidence/{studies,builds}/q712-idle-action-clock-h433--2026-11-06--001`.
The study's `final-control` holds the run, captured inputs and verification
receipts. Earlier `raw-production`, `raw-reviewed` and `speed-restored` runs
bypassed the new hooks in their captured-order host and are excluded. The
corrected host mirrors production poll/trigger/advance ordering.

## Verification

The 24 new cases cover availability, interruption, snapshot/hash state, clock
branches, fallback cache identity, signed counts and poll/trigger ordering.
The initial old-source counterfactual fails nine cases and passes one; valid
focused counterfactuals also detect the later phase, cancellation, fallback and
count-gate defects. The invalid empty-trigger fixture is excluded.

The final Release solution build has zero warnings/errors. All ten suites pass:
8461 passed, 30 skipped, zero failed. Unity compiles with zero errors and two
existing obsolete-API warnings. The Goblin meal scenario passes; its incidental
bird ids are 15/7 at 720, with the meal timing retained.
