---
systems: [rooms, creatures, simulation-core]
---
# Immediate Library neighbour kick

## Native1518 event

CONFIRMED in the Level5 seed1 capture: at1517 Library1115's head holder is
Warlock438. At1518 wall(7,32) changes terrain2 to9,438 becomes state62 with
age0, and the Library head becomes439. At1519 it remains62/age1; at1520 it
returns to53/age0 and becomes the head again. Its XY remains18332,129047
through1520. State62 is the Library return handler, not a blocked-travel state.

Imp446's mining state, purse and departure agree across the preceding control:
273 gold at1516,546 at1517,818 at1518,1091 at1519, then1364 and state6 at1520.
The first missing draw is instead Library.cpp:1068 before the Imp's two store
draws. Modern438 takes its neighbour kick at1519, returns at1521 and therefore
misses the1520 Library claim's shelf search.

STRONGLY INFERRED from fresh whole primary reads:
[neighbour notification](functions/004E44E0.md) kicks the holder inside the
terrain-change call; [the kick wrapper](functions/0048AD30.md) calls SetState
directly. [State62](functions/00476320.md) holds through age1 and returns to53
after that on its own Library. Fresh whole reads of
[mining state26](functions/00465870.md) and
[store selection](functions/00464400.md) retain their action/hands/store order.
No new mining or gold-store rule follows. The Library shelf call is the existing
[capacity rule](library-capacity-draws.md).

## Bounded production correction

ADR-0710 applies a Library neighbour kick immediately when SlabChain.Changed
has a live SimulationContext. It uses the existing state transition and category
hooks, releasing the held place and stamping the event's tick. Holders are tried
latest-claim first, skipping an in-hand holder per the existing
[SetState guard](functions/00476D30.md), and stopping after the first accepted
transition. The detached
setup path and other room types retain their deferred marks; this does not
complete the native generic notification or all terrain-change callers.
No additional stop or future state, destination, deadline or draw is injected.

Hash/save442 changes timing without adding fields. Pin0x7CCD482E9EC67757 is a
remake determinism regression value, not native evidence. The public signatures
and the existing internal three-argument KickNeighbours entry remain intact.

## Controlled continuation and limits

The [preceding Library chain control](library-arrival-chain.md)'s24 inputs are
unchanged. The only simulation-source changes in the diagnostic build are
SlabChain, the hash version and the save version; RoomEvictRules has only a
comment correction. It retains the preceding
two observation-only differences from main and extends Tools body telemetry.

| Control | Complete-turn ordered sites/preseeds | Next difference |
|---|---|---|
| Prior441, deferred Library kick | 1851 through1519 | 1520 draw1: Library1068 versus Room1563 |
| Live442, immediate Library kick | 2019 through1531 | 1532 draw1: State1025 versus Treasure335 |

This control stopped at 1532 draw 1 with preseed 2466167685, before modern
455/state29's bound 2048 deposit draw. The [next control](stop-self-route.md)
traced the omission to the neutral stop/facing/arrival path without changing
deposit rules.
The matched draw prefix does not establish complete state, playback or Heart
outcomes. Actors439,441,449 and455 each match132/132 state/XY samples1400–1531;
438 matches120/132, first XY difference1520 despite matching state53. Neutral447
matches109/132, retaining its first XY difference1509. These motion limits
remain separate from the corrected kick timing.

## Evidence

Add-only study: `F:/DK2Evidence/studies/q712-library-kick1520--2026-11-07--001`.
`native-boundary.json` records the wall/head/state-age/purse timeline.
`verified442/` and its mapped verification/component receipts record the final
guarded replay. Earlier `live442/` and `final442/` probes precede the local
in-hand guard. Frozen source/payload live at
`F:/DK2Evidence/builds/q712-library-kick1520--2026-11-07--001`.
The study's `source-input-proof.json`, `verification/summary.json` and final
manifest record source/input scope, checks and delivered hashes.

## Verification at 442

The regression changes terrain after the holder's own turn and checks the
immediate state 62 transition, released place, event tick and age 1/age 2 return
sequence. A theory checks that only the latest accepted holder is kicked,
including continuation past an in-hand head. The exact final three cases fail
against the preceding SlabChain body with 442 metadata and pass with the fix;
`verification/counterfactual-v2.json` records the test/source SHA-256 values.
The final ten-suite run passes 8534 cases with 30 skipped and zero failures.
The study retains `verification/red-final-v2/`, `verification/green-final-v2/`
and `verification/full442-v2/`; `verification/summary.json` owns the results.

`source-input-proof.json` checks that only four of 373 Simulation sources
change, including one comment-only file; all source outside the five declared
changes (four Simulation files and the Tools observer) matches the baseline.
Among the eleven DLLs other than Tools and Simulation, four match byte for byte
and seven differ.
No broader binary identity or explanation for those differences is claimed.
