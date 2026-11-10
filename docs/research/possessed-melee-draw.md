---
systems: [combat, creatures, simulation-core]
---
# Possessed melee animation draw at 1647

## Attribution

The [hash-455 control](store-gold-steering.md) stopped at 1647, where its
first mismatch names Warlock 437 in the modern stack; that identifies the
modern consumer, not the native draw's owner. The hash-457 control below
continues it.

CONFIRMED in the Level 5 seed-1 capture: possessed Imp 440 changes from
no action/standing slot 39 at 1646 to action 17/slot 5 at 1647. The action
start stamp is 1647; its phase/rate are 1365/1365, then phase 2730 at 1648.
The packet has one action-26 melee request over 1645 through 1648, on playback turn
1647. The modern event also reports possessed_swing at 1647. Its stored
SwingStartTick is 1646; this alone does not establish earlier playback.

Warlock 437 arrives in state 53 and changes walk slot 1 to standing 39 at
1647. The shipped standing descriptor at file +0x13E3 has flags 1, type 5
for both Warlock and Imp. Therefore that install cannot supply the coin.
The separate descriptor mapping is recorded in [the loader](functions/00492320.md).
Imp melee slot 5 has file flags 0x28011, including 0x8000 but not 0x400.

STRONGLY INFERRED from fresh whole reads of
[the possessed dispatcher](functions/00514830.md),
[the animation chooser](functions/0048BE50.md) and
[the installer](functions/004B37D0.md): the melee request starts action 17
through the existing action starter. Its slot-5 install qualifies for the
world-stream random(2) at RenderInfo.cpp:201 even without the alternate bit.
Imp 440's start is the eligible source for the missing coin. The native draw
log has no per-call creature tag, so this attribution is not a tagged capture.

## Implementation boundary

At hash/save457 (ADR-0726), PossessionRules.Melee reaches the shared slot-5
starter through PossessedMeleeRules. The mode-3 poll applies the existing blow
before combat, retains independent sticky animation completion and advances
the clip once at the end of the creature update. A wrap is observed by the
following poll; a frozen animation does not advance.

PossessedMeleeState stores active/completion/ownership in the hash, save and
copy. Current-action queries retain the action after impact. Cancellation
clears its pending swing and current target before handing the shared clip
to a replacement, preserving same-resource phase reuse. Ordinary replacement
swings release the old shared ownership and retain their existing computed hold.
An untimed possessed claim, conversion, dig or mine start releases it too.
The general resource-identity and animation-chooser adapters remain partial.

The native action target and retained target word are 1463; that record is
absent from the captured frame. The modern event targets creature20, tagged436.
Target selection is unchanged by this implementation; the native target kind
remains UNKNOWN from this capture. Draw agreement cannot establish a matched
attack, full state or Heart outcome. Command start-stamp conventions also
retain their separate scope; no packet scheduling delay was introduced.

## Controlled result

The `controlled457` replay (same 27 inputs; the earlier diagnostic clauses
plus one pre-1400 guard on the new starter, 27 in total) matches 4783 ordered
sites/preseeds through 1685, versus 4106 through 1646. Imp 455 matches 286/286
native state/XY samples. Next is 1686 draw 1: native ObjectState.cpp:2782
versus modern PlayerRooms.cpp:644, bound 1, FearRules.Withdraw for Imp 440 in
state 203. It was taken on master's merge at hash 457 and repeats the
branch's `controlled456-v4` run draw for draw (8617 rows).

Evidence: `F:/DK2Evidence/studies/q712-possessed-melee1647--2026-11-08--001`;
the controls and receipts are in `q712-possessed-melee-clock--2026-11-08--001`
and `--2026-11-10--001` (`verification-summary457.json`).
`observations.json` contains the native components, packet excerpt and modern
events; the copied packet is SHA-256 checked and its source remains in place.
The two raw shipped-data windows and source hashes bind the code/data reading.
All restore, full-state and Heart limits remain unchanged.
