---
systems: [rooms, simulation-core]
---
# Library arrival slab-chain control

## Destination selected at1426

CONFIRMED from the Level5 seed1 capture and paired diagnostic replays:438's
first movement difference at1492 originates in its destination choice at1426.
Native chooses tile(4,31), while the preceding441 control chooses(7,28).
Their first five buffered route points are identical, hiding the different
endpoints until the buffer refills. Applying the captured Library chain makes
the1426 endpoint match without supplying a future destination or route.

At completed1400 and1426, native Library1115 has16 slabs in this order:

```
(7,31) (7,30) (6,31) (7,29) (6,30) (5,31) (7,28) (6,29)
(5,30) (4,31) (6,28) (5,29) (4,30) (5,28) (4,29) (4,28)
```

The restored modern chain puts(4,31) first: that tile has SlabOrder0, ahead
of the other tiles' positive ranks. WorldResync does not restore slab-chain
metadata. The earlier cause of that zero rank is not established here.
The same1426 draw starts at index7. Native skips the interior floor slots
(6,29) and(5,30), reaching(4,31); modern starts on the unoccupied corner(7,28).
The draw has preseed3209473002, bound16 and source Room.cpp:1509.

STRONGLY INFERRED from fresh whole primary reads of
[the arrival picker](functions/004E4C10.md) and
[floor-slot predicate](functions/00454110.md): the implemented chain traversal
and Library interior rejection agree with these paths. No new picker rule
is required. The wall predicate's decompiler still drops reachable blocks;
this is not a fresh complete disassembly of that predicate. General slab
ordering is documented in [room slab chains](room-slab-chain.md).

## Paired diagnostic control

`Q712_LIBRARY_CHAIN_INPUT` supplies only the actual1400 Library slab chain,
as a24th input after the [manager-order control](room-manager-order.md).
Both runs read the same24-key dictionary; all23 prior values are unchanged.
The Tools-only reader validates tick1400, unique slabs, Library type, owner
and exact footprint. Applied mode assigns ascending SlabOrder ranks1..16
inside that Library only, leaving the higher allocation counter unchanged.
Its sole read-only/applied source difference is a Boolean.

| Control | Complete-turn ordered sites/preseeds | Next difference |
|---|---|---|
| Read/validate24inputs only | 1620 through1507 | 1508 draw1: StateUtils325 versus Library1068 |
| Apply1400 Library chain | 1851 through1519 | 1520 draw1: Library1068 versus Room1563 |

This control stopped at 1520 draw 1, native Library1068 versus modern
Room1563, with preseed 3542246557. Modern 446/state26 made the bound 25
room-routing draw through ImpTaskSystem.TryRouteToSomewhereItFits. The
[next control traced the Library kick timing](library-neighbour-kick.md),
without changing mining or deposit rules.

438 state/XY matches119/120 samples from1400 through1519: its remaining
difference is state6 versus native62 at1518, with equal XY. Actors439,441,449
and455 each match120/120; neutral447 matches109/120, first differing in XY
at1509. Draw alignment does not imply equal state or complete creature motion.

## Evidence and limits

Production remains hash/save441, pin0x12CF3D1067315450. No production source,
snapshot, scenario or tests change. A general restore adapter remains open.
The373 diagnostic simulation sources equal the preceding frozen441 sources;
all12 non-Tools DLLs are identical across the paired builds. The2 inherited
observation-only differences from main retain their preceding scope.

Evidence: `F:/DK2Evidence/studies/q712-arrival-point1426--2026-11-07--001`.
Primary runs: `chain-read-only441-v2/` and `chain-applied441/`, with mapped
verification receipts beside them. `captured1400-library-chain.json` is the
input; `native-target-chain-comparison.json` checks the native chain/endpoints
and buffered points. The applied component receipt checks the six actors.
`source-input-proof.json` and `control-files-verified.json` check source,
payload and input scope. `verification/summary.json` records the final checks.
The original control manifest mistakenly includes
a hash of its own empty file; the separate receipt verifies every retained
file and supersedes that self-entry. `chain-read-only441/` is a failed setup
run, excluded from behavior evidence; `observed441/` is an earlier23-input probe.

Build freeze: `F:/DK2Evidence/builds/q712-arrival-point1426--2026-11-07--001`,
with `source.zip`, `payload/` and `manifest.json`. The study retains read-only
source/payload under `chain-read-only-control/`, and scripts under
`final-scripts/`. No new .NET suite run is claimed for unchanged production;
the [441 regression evidence](hatchery-pillar-steering.md#regression-evidence)
retains its scope. This establishes bounded draw continuation, not full-state,
playback or Heart-outcome parity.
