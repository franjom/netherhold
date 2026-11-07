---
systems: [rooms, simulation-core]
---
# Room-manager order control

## Native1507 boundary

CONFIRMED from the Level5 seed1 capture: Portal1125 has stamp1371 and tally3
at1400 and1506; at1507 it stamps1507 and reaches tally4. Native creates a
Warlock that turn too. The Portal is not one turn early. The native draw log
runs the Heart's gold effects before Entrance.cpp:684; our441 control did the
Portal choice first. See the [preceding control](hatchery-pillar-steering.md).

The completed1400 manager chain has14 rooms. Heart1124 precedes Portal1125;
its global links differ from the owning player's chain, where Portal1125
precedes Lair1116. The recorded map has125 room slabs. Modern's shared
`RoomListPosition` gives the Portal12 and Heart4, so the descending room-tick
sort visits the Portal first. The player's list stamp is not a substitute
for the manager's independent order. This is the existing Q733(d) limit,
now exposed by this bounded draw comparison.

STRONGLY INFERRED from fresh whole reads of
[the room walk](functions/004EC450.md) and [allocation](functions/004EC490.md):
manager+0x0E is the head, room+0x21 the forward link, and new allocation inserts
at the head. [The transfer](functions/004E5650.md) reads/writes the separate
owner-list links. [Portal tick](functions/004DBBE0.md) confirms the due-roll
and unconditional stamp paths; this finding changes no Portal rule.
The two newly exposed constructor draw sites are recorded in
[personal name](functions/00491900.md) and [blood group](functions/004919C0.md).
They are existing implemented draws, not new rules.

## Paired diagnostic control

`Q712_ROOM_ORDER_INPUT` is a23rd input, extracted solely from completed1400.
It adds the manager chain and full room footprints to the22 unchanged inputs.
Both runs read and validate the identical dictionary. Matching requires tick,
map dimensions, room count, type, owner and exact footprints, with unique rooms.
The reader assigns ordinal ranks only to a diagnostic room-tick wrapper.
Owner-list fields and the allocation counter remain unchanged; the wrapper
runs the existing room steps and only changes their room visitation order.
It admits later newly constructed rooms only with a post-restore positive rank.
No future room list, route, deadline or draw is injected.

| Control | Complete-turn ordered sites/preseeds | Next difference |
|---|---|---|
| Read/validate23inputs only | 1600 through1506 | 1507 draw4: Treasure132 versus Entrance684 |
| Apply1400 manager order only | 1620 through1507 | 1508 draw1: StateUtils325 versus Library1068 |

The new boundary has preseed1068726096. Modern438/state1 searches Library
capacity, bound16; its first remaining state/XY difference is still1492.
Five comparison actors (439,441,447,449,455) each match108/108 state/XY samples
from1400 through1507;438 matches92/108. Native and modern new Warlock437
both enter state193 at67584,83968 at1507. No complete-state parity follows.

## Scope and evidence

Production remains hash/save441 and its pin0x12CF3D1067315450. No simulation,
snapshot, scenario or test changes and no new gameplay rule are delivered.
A general restore adapter and independent construction-only manager ordinal
remain open; the wrapper is an experiment, not their implementation.
The [Library arrival-chain control](library-arrival-chain.md) traces that
historical movement boundary to the earlier destination selection; it retains
the separate component and general restore limits.

Evidence is add-only under
`F:/DK2Evidence/studies/q712-room-order--2026-11-07--001`.
Input: `captured1400-room-order.json`; independent native chains/clocks:
`native-room-order-boundary.json`. Primary runs are `manager-read-only441`
and `manager-applied441`; mapped verification and component receipts sit
beside them. The build freeze is
`F:/DK2Evidence/builds/q712-room-order--2026-11-07--001`, with `source.zip`,
`payload/`, `manifest.json` and `frozen441-source.zip`. The read-only source and
payload are retained at study `manager-read-only-control/`.
`source-input-proof.json` verifies373 unchanged simulation sources,12 identical
non-Tools DLLs, identical23-key input dictionaries and the one Boolean
reader difference. The2 inherited observation-only runtime differences from
main retain their prior scope. Scripts are in `final-scripts/`.
`newborn437-components.json` checks only its birth type, owner, state and XY;
identity indices and full state are not compared. Both diagnostic builds and
replays exit0; builds have zero warnings/errors. Delivery verification and
local integration are recorded in `verification/summary.json` and the external
`integration-receipt.json`.
No .NET suite rerun is claimed for unchanged production code; the prior
[441 verification](hatchery-pillar-steering.md#regression-evidence) retains its scope.
This is bounded draw continuation, not full-state, playback or Heart-outcome parity.
